/* mcp-tool
{
  "description": "Grid dims in the band OUTSIDE the crop and INSIDE the grid bubbles. Per view, visible straight host grids are grouped by direction (parallel = one group, any angle; coincident grids count once). Each group needs exactly ONE grid-to-grid chain and ONE overall dim (a 2-grid group: one dim) on ONE side, placed between the crop boundary and the bubbles, and the annotation crop must reach every grid end that carries a bubble and hold every grid dim. mode audit (read-only, many views: sheetPrefix / sheetNumbers / viewIds): status per group. mode preview | apply (ONE view: viewId): move kept dims into the band, create missing ones (dimType), delete extra ones only with deleteExtra, extend 2D grid ends only with extendGrids when the band is too narrow, enlarge the annotation crop to the bubble ends and the dims (never shrinks it). Dims of dependent views live in the parent: with hideInSiblings, new/moved dims are hidden in the other views of the family that are placed on sheets. mode undo: reverts an apply from logPath.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["audit", "preview", "apply", "undo"] },
      "viewId": { "type": "number", "description": "preview / apply: the one view to fix" },
      "viewIds": { "type": "array", "items": { "type": "number" }, "description": "audit" },
      "sheetPrefix": { "type": "string", "description": "audit: views placed on sheets whose number starts with this" },
      "sheetNumbers": { "type": "array", "items": { "type": "string" }, "description": "audit" },
      "excludeNameContains": { "type": "array", "items": { "type": "string" }, "description": "audit: skip views whose name contains any of these (profile: views without grid dims)" },
      "onlyProblems": { "type": "boolean", "description": "audit: list only views with something to fix" },
      "dimType": { "type": "string", "description": "linear dimension type name for new dims (profile check type); required when a dim must be created" },
      "preferSides": { "type": "array", "items": { "type": "string" }, "description": "side order when a group has no dims yet: 'bottom','top','left','right' (profile, e.g. ['bottom','left'])" },
      "overallMm": { "type": "number", "description": "paper mm from the grid end (bubble) to the overall dim line, default 4" },
      "stepMm": { "type": "number", "description": "paper mm between overall and chain, default 7" },
      "cropGapMm": { "type": "number", "description": "paper mm kept clear between the crop boundary and the dim (text included), default 1.5" },
      "annoMarginMm": { "type": "number", "description": "paper mm the annotation crop keeps beyond the bubble ends of the grids and the grid dims, default 2" },
      "move": { "type": "boolean", "description": "move kept dims that are outside the band into it (default true)" },
      "deleteExtra": { "type": "boolean", "description": "delete duplicate / partial / link-grid dims of a group (default false: only listed)" },
      "extendGrids": { "type": "boolean", "description": "extend the 2D grid ends on the chosen side when the band between crop and bubble is too narrow (default false: only reported)" },
      "fitAnnoCrop": { "type": "boolean", "description": "enlarge the annotation crop to bubbles + dims (default true)" },
      "activateAnnoCrop": { "type": "boolean", "description": "turn the annotation crop on when it is off (default false: off = nothing is clipped)" },
      "hideInSiblings": { "type": "boolean", "description": "dependent families: hide new/moved dims in the other sheet-placed views where they would show (default true)" },
      "compact": { "type": "boolean", "description": "preview / apply: short result (actions + status after)" },
      "extraIds": { "type": "array", "items": { "type": "number" }, "description": "preview / apply: dims to treat as extra (with deleteExtra: deleted, or hidden here when another sheet view uses them)" },
      "forceSides": { "type": "object", "description": "preview / apply: side per group kind, e.g. { horizontal: 'right', vertical: 'bottom' } (user decision, e.g. to keep clear of another viewport)" },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 300
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class GridDimsBand
{
    const double MM = 304.8;

    class Opt
    {
        public double OffO = 4, Step = 7, Gap = 1.5, AnnoMargin = 2;
        public bool Move = true, DeleteExtra, ExtendGrids, FitAnno = true, ActivateAnno, HideSiblings = true;
        public List<string> Prefer = new List<string>();
        public Dictionary<string, string> Force = new Dictionary<string, string>(); // group kind (vertical / horizontal / grids) -> side label
        public HashSet<int> ExtraIds = new HashSet<int>(); // dims the user wants treated as extra (e.g. a parent dim shared by dependents with different crops)
        public DimensionType Type;
    }
    class GI { public Grid G; public double A, W0, W1; public bool B0, B1; public int E0, E1; } // W0 < W1 along t; E0/E1 = curve end index at W0/W1
    class Pos { public List<GI> Gs = new List<GI>(); public double A; public GI Ref => Gs.OrderByDescending(g => (g.B0 ? 1 : 0) + (g.B1 ? 1 : 0)).First(); }
    class Side
    {
        public int S; public string Label; public double Ecrop, Wbub, Lo, Hi, Avail, Need; public bool Feasible, TextToCrop; public int Bubbles;
        public List<string> Short = new List<string>();
    }
    class DR { public Dimension D; public string Kind; public double W; public int S; public double Txt; public bool InBand; public string Where; public bool Clipped; public bool Check; }
    class Grp
    {
        public double tR, tU, aR, aU; public List<Pos> P = new List<Pos>(); public Dictionary<int, int> Idx = new Dictionary<int, int>();
        public string Label; public double Mid; public Side[] Sides; public List<DR> Dims = new List<DR>();
        public Side Chosen; public DR KeepC, KeepO; public List<DR> Extra = new List<DR>();
        public double? NewC, NewO, MoveC, MoveO, ExtendTo; public List<string> Issues = new List<string>(); public string Decision;
        public List<DR> Outside = new List<DR>(); public DR AdoptC, AdoptO; // dims outside the annotation crop; ones taken over instead of creating a duplicate
        public bool Multi => P.Count > 2;
    }
    class VA
    {
        public View V; public double Sc; public XYZ O, R, U; public List<double[]> Crop; public double[] CropRect, AnnoRect; public bool AnnoOn, CropOn;
        public List<Grp> Groups = new List<Grp>(); public List<string> Singles = new List<string>(), Skipped = new List<string>();
        public List<object> BubbleEndsOutside = new List<object>(); public double[] AnnoNeed;
        public double Rr(XYZ p) => (p - O).DotProduct(R) * MM; public double Uu(XYZ p) => (p - O).DotProduct(U) * MM;
        public XYZ P2(double r, double u) => O + R * (r / MM) + U * (u / MM);
    }

    static Opt ReadOpt(Document doc, JObject a)
    {
        var o = new Opt
        {
            OffO = a.Value<double?>("overallMm") ?? 4, Step = a.Value<double?>("stepMm") ?? 7, Gap = a.Value<double?>("cropGapMm") ?? 1.5,
            AnnoMargin = a.Value<double?>("annoMarginMm") ?? 2,
            Move = a.Value<bool?>("move") ?? true, DeleteExtra = a.Value<bool?>("deleteExtra") ?? false, ExtendGrids = a.Value<bool?>("extendGrids") ?? false,
            FitAnno = a.Value<bool?>("fitAnnoCrop") ?? true, ActivateAnno = a.Value<bool?>("activateAnnoCrop") ?? false, HideSiblings = a.Value<bool?>("hideInSiblings") ?? true,
            Prefer = (a["preferSides"] as JArray)?.Select(x => ((string)x).ToLower()).ToList() ?? new List<string>()
        };
        if (a["forceSides"] is JObject fs) foreach (var p in fs.Properties()) o.Force[p.Name.ToLower()] = ((string)p.Value).ToLower();
        if (a["extraIds"] is JArray xi) foreach (var x in xi) o.ExtraIds.Add((int)x);
        if (a["dimType"] != null)
            o.Type = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)a["dimType"] && x.StyleType == DimensionStyleType.Linear);
        return o;
    }

    // paper mm above/below the dim line taken by its text (text size + gap to line)
    static double TextMm(DimensionType t)
    {
        if (t == null) return 3;
        double s = t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 2.5 / MM, g = t.get_Parameter(BuiltInParameter.TEXT_DIST_TO_LINE)?.AsDouble() ?? 1 / MM;
        return (s + g) * MM + 0.3;
    }

    static VA Analyze(Document doc, View v, Opt opt)
    {
        var va = new VA { V = v, Sc = v.Scale, O = v.Origin, R = v.RightDirection, U = v.UpDirection, CropOn = v.CropBoxActive };
        double sc = va.Sc;
        var m = v.GetCropRegionShapeManager();
        // crop polygon (view mm) and its bounding rect
        va.Crop = new List<double[]>();
        if (va.CropOn)
        {
            IList<CurveLoop> loops = null; try { loops = m.GetCropShape(); } catch { }
            if (loops != null && loops.Count > 0) foreach (Curve c in loops[0]) foreach (var p in c.Tessellate()) va.Crop.Add(new[] { va.Rr(p), va.Uu(p) });
            if (va.Crop.Count < 3)
            {
                var cb = v.CropBox; var t = cb.Transform; va.Crop.Clear();
                foreach (var p in new[] { new XYZ(cb.Min.X, cb.Min.Y, cb.Min.Z), new XYZ(cb.Max.X, cb.Min.Y, cb.Min.Z), new XYZ(cb.Max.X, cb.Max.Y, cb.Min.Z), new XYZ(cb.Min.X, cb.Max.Y, cb.Min.Z) })
                { var q = t.OfPoint(p); va.Crop.Add(new[] { va.Rr(q), va.Uu(q) }); }
            }
            va.CropRect = new[] { va.Crop.Min(p => p[0]), va.Crop.Max(p => p[0]), va.Crop.Min(p => p[1]), va.Crop.Max(p => p[1]) };
            va.AnnoOn = v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger() == 1;
            try
            {   // offsets are paper feet
                double L = m.LeftAnnotationCropOffset * MM * sc, Rt = m.RightAnnotationCropOffset * MM * sc, T = m.TopAnnotationCropOffset * MM * sc, B = m.BottomAnnotationCropOffset * MM * sc;
                va.AnnoRect = new[] { va.CropRect[0] - L, va.CropRect[1] + Rt, va.CropRect[2] - B, va.CropRect[3] + T };
            }
            catch { }
        }
        // grids -> groups of parallel lines
        var raw = new List<Tuple<Grid, double[], double[], bool, bool>>(); // grid, end0, end1 (view mm), bubble0, bubble1
        foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
            if (!(c is Line ln)) { va.Skipped.Add(g.Name + " (not straight)"); continue; }
            bool b0 = false, b1 = false; try { b0 = g.IsBubbleVisibleInView(DatumEnds.End0, v); b1 = g.IsBubbleVisibleInView(DatumEnds.End1, v); } catch { }
            raw.Add(Tuple.Create(g, new[] { va.Rr(ln.GetEndPoint(0)), va.Uu(ln.GetEndPoint(0)) }, new[] { va.Rr(ln.GetEndPoint(1)), va.Uu(ln.GetEndPoint(1)) }, b0, b1));
        }
        foreach (var x in raw)
        {
            double dr = x.Item3[0] - x.Item2[0], du = x.Item3[1] - x.Item2[1], len = Math.Sqrt(dr * dr + du * du);
            if (len < 1) continue; dr /= len; du /= len;
            var grp = va.Groups.FirstOrDefault(gg => Math.Abs(gg.tR * du - gg.tU * dr) < 0.001);
            if (grp == null)
            {
                if (du < -1e-6 || (Math.Abs(du) <= 1e-6 && dr < 0)) { dr = -dr; du = -du; }
                double aR = -du, aU = dr; if (aR < -1e-6 || (Math.Abs(aR) <= 1e-6 && aU < 0)) { aR = -aR; aU = -aU; }
                grp = new Grp { tR = dr, tU = du, aR = aR, aU = aU }; va.Groups.Add(grp);
            }
            double w0 = x.Item2[0] * grp.tR + x.Item2[1] * grp.tU, w1 = x.Item3[0] * grp.tR + x.Item3[1] * grp.tU;
            var gi = new GI { G = x.Item1, A = x.Item2[0] * grp.aR + x.Item2[1] * grp.aU };
            if (w0 <= w1) { gi.W0 = w0; gi.W1 = w1; gi.B0 = x.Item4; gi.B1 = x.Item5; gi.E0 = 0; gi.E1 = 1; }
            else { gi.W0 = w1; gi.W1 = w0; gi.B0 = x.Item5; gi.B1 = x.Item4; gi.E0 = 1; gi.E1 = 0; }
            var pos = grp.P.FirstOrDefault(p => Math.Abs(p.A - gi.A) < 5);
            if (pos == null) { pos = new Pos { A = gi.A }; grp.P.Add(pos); }
            pos.Gs.Add(gi);
        }
        foreach (var grp in va.Groups.ToList())
        {
            grp.P = grp.P.OrderBy(p => p.A).ToList();
            var names = grp.P.Select(p => string.Join("=", p.Gs.Select(g => g.G.Name))).ToList();
            double ang = Math.Round(Math.Atan2(grp.tU, grp.tR) * 180 / Math.PI, 2);
            grp.Label = (Math.Abs(grp.tU) > 0.999 ? "vertical grids" : Math.Abs(grp.tR) > 0.999 ? "horizontal grids" : "grids at " + ang + " deg") + " " + string.Join(",", names);
            if (grp.P.Count < 2) { va.Singles.Add(grp.Label); va.Groups.Remove(grp); continue; }
            for (int i = 0; i < grp.P.Count; i++) foreach (var g in grp.P[i].Gs) grp.Idx[g.G.Id.IntegerValue] = i;
            grp.Mid = grp.P.SelectMany(p => p.Gs).Average(g => (g.W0 + g.W1) / 2);
        }
        // annotation crop must hold every bubble
        double tol = 0.5 * va.Sc; Func<double[], double[], bool> InRect = (rc, pt) => pt[0] >= rc[0] - tol && pt[0] <= rc[1] + tol && pt[1] >= rc[2] - tol && pt[1] <= rc[3] + tol;
        var need = new List<double[]>();
        foreach (var grp in va.Groups)
            foreach (var gi in grp.P.SelectMany(p => p.Gs)) AddBubbles(va, grp.tR, grp.tU, gi, opt, need);
        foreach (var x in raw.Where(y => !va.Groups.Any(gg => gg.Idx.ContainsKey(y.Item1.Id.IntegerValue))))
        {   // single grids: same bubble rule
            double dr = x.Item3[0] - x.Item2[0], du = x.Item3[1] - x.Item2[1], len = Math.Sqrt(dr * dr + du * du); if (len < 1) continue;
            AddBubblePts(va, x.Item2, -dr / len, -du / len, x.Item4, opt, need, x.Item1.Name, x.Item1.Id.IntegerValue);
            AddBubblePts(va, x.Item3, dr / len, du / len, x.Item5, opt, need, x.Item1.Name, x.Item1.Id.IntegerValue);
        }
        va.AnnoNeed = need.Count == 0 ? null : new[] { need.Min(p => p[0]), need.Max(p => p[0]), need.Min(p => p[1]), need.Max(p => p[1]) };
        if (va.CropOn && va.AnnoOn && va.AnnoRect != null)
            foreach (var p in need.Where(p => p.Length > 2 && !InRect(va.AnnoRect, p)).GroupBy(p => (int)p[2]))
            { var gname = doc.GetElement(new ElementId(p.Key))?.Name; if (!va.BubbleEndsOutside.Contains(gname)) va.BubbleEndsOutside.Add(gname); }

        // sides
        foreach (var grp in va.Groups)
        {
            double sc0 = sc;
            double aMin = grp.P.First().A, aMax = grp.P.Last().A;
            // text normal of a dim running along a (reading left-to-right / bottom-to-top)
            double nR = -grp.aU, nU = grp.aR, nt = nR * grp.tR + nU * grp.tU;
            double txtNew = TextMm(opt.Type);
            grp.Sides = new Side[2];
            for (int k = 0; k < 2; k++)
            {
                int s = k == 0 ? -1 : 1;
                var sd = new Side { S = s };
                sd.Label = Math.Abs(grp.tU) >= Math.Abs(grp.tR) ? (s * Math.Sign(grp.tU) > 0 ? "top" : "bottom") : (s * Math.Sign(grp.tR) > 0 ? "right" : "left");
                sd.TextToCrop = nt * s < 0;
                sd.Ecrop = va.CropOn ? CropReach(va.Crop, grp, s, aMin, aMax) : double.NegativeInfinity;
                sd.Wbub = grp.P.Min(p => p.Gs.Max(g => s > 0 ? g.W1 : -g.W0));
                sd.Bubbles = grp.P.Count(p => p.Gs.Any(g => s > 0 ? g.B1 : g.B0));
                sd.Need = opt.Gap + (sd.TextToCrop ? txtNew : 0) + (grp.Multi ? opt.Step : 0) + opt.OffO;
                sd.Avail = double.IsNegativeInfinity(sd.Ecrop) ? 999 : (sd.Wbub - sd.Ecrop) / sc0;
                sd.Feasible = sd.Avail >= sd.Need - 0.05;
                sd.Lo = sd.Ecrop + (opt.Gap + (sd.TextToCrop ? txtNew : 0)) * sc0;
                sd.Hi = sd.Wbub - Math.Max(1.5, sd.TextToCrop ? 0 : txtNew + 0.3) * sc0;
                if (va.CropOn)
                    foreach (var p in grp.P) { double reach = p.Gs.Max(g => s > 0 ? g.W1 : -g.W0); if (reach < sd.Ecrop + sd.Need * sc0) sd.Short.Add(string.Join("=", p.Gs.Select(g => g.G.Name)) + (reach <= sd.Ecrop ? " (end inside crop)" : "")); }
                grp.Sides[k] = sd;
            }
        }
        // existing linear dims that reference grids
        var dimTypeName = opt.Type?.Name;
        var inView = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
        var seen = new HashSet<int>(inView.Select(x => x.Id.IntegerValue));
        // the view collector leaves out dims beyond the annotation crop: add the owner view's (parent's) dims not hidden here, as "outside"
        var prim0 = v.GetPrimaryViewId(); var owner0 = prim0 == ElementId.InvalidElementId ? v.Id : prim0;
        var ownerDims = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(x => x.OwnerViewId == owner0 && !seen.Contains(x.Id.IntegerValue) && !x.IsHidden(v)).ToList();
        foreach (var d in inView.Concat(ownerDims))
        {
            if (d is SpotDimension || !(d.Curve is Line ln)) continue;
            double dR = ln.Direction.DotProduct(va.R), dU = ln.Direction.DotProduct(va.U);
            var grp = va.Groups.FirstOrDefault(gg => Math.Abs(dR * gg.aR + dU * gg.aU) > 0.9998);
            if (grp == null) continue;
            var idx = new HashSet<int>(); bool link = false, other = false; int beyond = 0;
            foreach (Reference r in d.References)
            {
                var e = doc.GetElement(r.ElementId);
                if (e is Grid gg && r.LinkedElementId == ElementId.InvalidElementId)
                {
                    if (grp.Idx.TryGetValue(gg.Id.IntegerValue, out int ix)) idx.Add(ix);
                    else if (gg.Curve is Line gl && Math.Abs(gl.Direction.DotProduct(va.R) * grp.aR + gl.Direction.DotProduct(va.U) * grp.aU) < 0.001) beyond++; // parallel grid not shown in this view (parent dim of a dependent)
                    else { other = true; break; }
                }
                else if (e is RevitLinkInstance li && r.LinkedElementId != ElementId.InvalidElementId && li.GetLinkDocument()?.GetElement(r.LinkedElementId) is Grid) link = true;
                else { other = true; break; }
            }
            if (other || (idx.Count + beyond < 2 && !link) || (idx.Count == 0 && !link)) continue;
            // drawn but entirely outside the annotation crop: not on this drawing (kept aside: a shared parent dim may be adopted)
            bool outside = !seen.Contains(d.Id.IntegerValue);
            if (va.CropOn && va.AnnoOn && va.AnnoRect != null)
            {
                var bb0 = d.get_BoundingBox(v);
                if (bb0 != null)
                {
                    double r0 = Math.Min(va.Rr(bb0.Min), va.Rr(bb0.Max)), r1 = Math.Max(va.Rr(bb0.Min), va.Rr(bb0.Max)), u0 = Math.Min(va.Uu(bb0.Min), va.Uu(bb0.Max)), u1 = Math.Max(va.Uu(bb0.Min), va.Uu(bb0.Max));
                    outside = outside || r1 < va.AnnoRect[0] || r0 > va.AnnoRect[1] || u1 < va.AnnoRect[2] || u0 > va.AnnoRect[3];
                }
            }
            var dr = new DR { D = d, Check = dimTypeName != null && d.DimensionType?.Name == dimTypeName };
            int n = grp.P.Count;
            if (link) dr.Kind = "link-grid";
            else if (idx.Count == n) dr.Kind = n == 2 && beyond == 0 ? "single" : "chain";
            else if (beyond > 0) dr.Kind = "wide";
            else if (idx.Count == 2 && idx.Contains(0) && idx.Contains(n - 1) && d.NumberOfSegments == 0) dr.Kind = "overall";
            else dr.Kind = "partial";
            var org = ln.Origin; double w = va.Rr(org) * grp.tR + va.Uu(org) * grp.tU;
            dr.W = w; dr.S = w >= grp.Mid ? 1 : -1;
            var sd = grp.Sides[dr.S > 0 ? 1 : 0];
            dr.Txt = TextMm(d.DimensionType);
            double sw = dr.S * w;
            double lo = sd.Ecrop + (opt.Gap + (sd.TextToCrop ? dr.Txt : 0)) * sc - 0.3 * sc, hi = sd.Wbub - Math.Max(1.5, sd.TextToCrop ? 0 : dr.Txt + 0.3) * sc + 0.3 * sc;
            dr.InBand = sw >= lo && sw <= hi;
            dr.Where = sw < lo ? (va.CropOn && sw < sd.Ecrop ? "inside crop" : "touches crop") : sw > sd.Wbub ? "beyond bubble" : sw > hi ? "on bubble" : "in band";
            if (outside) { grp.Outside.Add(dr); continue; }
            if (va.CropOn && va.AnnoOn && va.AnnoRect != null)
            {
                var bb = d.get_BoundingBox(v);
                if (bb != null)
                {
                    double r0 = Math.Min(va.Rr(bb.Min), va.Rr(bb.Max)), r1 = Math.Max(va.Rr(bb.Min), va.Rr(bb.Max)), u0 = Math.Min(va.Uu(bb.Min), va.Uu(bb.Max)), u1 = Math.Max(va.Uu(bb.Min), va.Uu(bb.Max));
                    if (Math.Abs(grp.aR) > 0.7) { r0 = Math.Max(r0, va.CropRect[0]); r1 = Math.Min(r1, va.CropRect[1]); } // a parent dim running on into the next dependent
                    if (Math.Abs(grp.aU) > 0.7) { u0 = Math.Max(u0, va.CropRect[2]); u1 = Math.Min(u1, va.CropRect[3]); }
                    dr.Clipped = r0 < va.AnnoRect[0] - 1 || r1 > va.AnnoRect[1] + 1 || u0 < va.AnnoRect[2] - 1 || u1 > va.AnnoRect[3] + 1;
                }
            }
            grp.Dims.Add(dr);
        }
        foreach (var grp in va.Groups) Plan(va, grp, opt);
        return va;
    }

    static void AddBubbles(VA va, double tR, double tU, GI gi, Opt opt, List<double[]> need)
    {
        Curve c = null; try { c = gi.G.GetCurvesInView(DatumExtentType.ViewSpecific, va.V).FirstOrDefault(); } catch { }
        if (!(c is Line ln)) return;
        var p0 = new[] { va.Rr(ln.GetEndPoint(0)), va.Uu(ln.GetEndPoint(0)) }; var p1 = new[] { va.Rr(ln.GetEndPoint(1)), va.Uu(ln.GetEndPoint(1)) };
        double dr = p1[0] - p0[0], du = p1[1] - p0[1], len = Math.Sqrt(dr * dr + du * du); if (len < 1) return;
        bool b0 = false, b1 = false; try { b0 = gi.G.IsBubbleVisibleInView(DatumEnds.End0, va.V); b1 = gi.G.IsBubbleVisibleInView(DatumEnds.End1, va.V); } catch { }
        AddBubblePts(va, p0, -dr / len, -du / len, b0, opt, need, gi.G.Name, gi.G.Id.IntegerValue);
        AddBubblePts(va, p1, dr / len, du / len, b1, opt, need, gi.G.Name, gi.G.Id.IntegerValue);
    }
    // points the annotation crop must hold at one grid end: the end, and the bubble circle beyond it
    static void AddBubblePts(VA va, double[] end, double oR, double oU, bool bubble, Opt opt, List<double[]> need, string name, int id = 0)
    {
        // grids are not clipped by the annotation crop, but the grid dims are: the annotation crop reaches the grid ends that carry a bubble,
        // so every dim lying between crop and bubble is held. Ends without bubble do not count.
        if (bubble) need.Add(new[] { end[0], end[1], id });
    }

    // furthest reach (s * w) of the crop polygon over the dim span aMin..aMax
    static double CropReach(List<double[]> poly, Grp g, int s, double aMin, double aMax)
    {
        double best = double.NegativeInfinity;
        Func<double[], double> PA = p => p[0] * g.aR + p[1] * g.aU, PW = p => s * (p[0] * g.tR + p[1] * g.tU);
        foreach (var p in poly) { double a = PA(p); if (a >= aMin - 1 && a <= aMax + 1) best = Math.Max(best, PW(p)); }
        for (int k = 0; k <= 60; k++)
        {
            double ak = aMin + (aMax - aMin) * k / 60.0;
            for (int i = 0; i < poly.Count; i++)
            {
                var P = poly[i]; var Q = poly[(i + 1) % poly.Count];
                double pa = PA(P), qa = PA(Q);
                if ((pa - ak) * (qa - ak) > 0) continue;
                if (Math.Abs(qa - pa) < 1e-9) { best = Math.Max(best, Math.Max(PW(P), PW(Q))); continue; }
                double u = (ak - pa) / (qa - pa); best = Math.Max(best, PW(P) + u * (PW(Q) - PW(P)));
            }
        }
        return best;
    }

    static void Plan(VA va, Grp g, Opt opt)
    {
        double sc = va.Sc;
        var full = g.Dims.Where(d => (d.Kind == "chain" || d.Kind == "single") && !opt.ExtraIds.Contains(d.D.Id.IntegerValue)).ToList();
        var ovr = g.Dims.Where(d => d.Kind == "overall" && !opt.ExtraIds.Contains(d.D.Id.IntegerValue)).ToList();
        // side: one that already holds a kept dim in band, else preference, else bubbles, else room
        Func<Side, int> PrefIdx = sd => { int i = opt.Prefer.IndexOf(sd.Label); return i < 0 ? 99 : i; };
        var withDims = g.Sides.Where(sd => full.Concat(ovr).Any(d => d.S == sd.S && d.InBand)).ToList();
        var order = g.Sides.OrderByDescending(sd => withDims.Contains(sd) && sd.Feasible).ThenByDescending(sd => (sd.Feasible || opt.ExtendGrids) && sd.Bubbles > 0).ThenByDescending(sd => sd.Feasible)
            .ThenBy(PrefIdx).ThenByDescending(sd => sd.Bubbles).ThenByDescending(sd => sd.Avail).ToList();
        g.Chosen = order.First();
        if (opt.Force.TryGetValue(g.Label.Split(' ')[0], out var fside) && g.Sides.Any(sd => sd.Label == fside)) g.Chosen = g.Sides.First(sd => sd.Label == fside);
        var S = g.Chosen;
        g.Decision = withDims.Contains(S) && S.Feasible ? "side with existing dims" : S.Feasible ? (PrefIdx(S) < 99 ? "preferred side" : S.Bubbles > 0 ? "bubble side" : "side with room") : "no side has room";
        if (opt.Force.ContainsKey(g.Label.Split(' ')[0])) g.Decision = "side forced";
        if (S.Feasible && S.Bubbles == 0) g.Decision += "; NO bubbles on this side (bubble side too narrow: extendGrids or manual)";
        // keep one chain + one overall, nearest to the band on the chosen side, official type first
        Func<DR, int> Rank = d => d.S == S.S ? (d.InBand ? 0 : 1) : 2;
        g.KeepC = full.OrderBy(Rank).ThenBy(d => d.Check ? 1 : 0).ThenBy(d => Math.Abs(d.S * d.W - S.Wbub)).FirstOrDefault();
        g.KeepO = g.Multi ? ovr.OrderBy(Rank).ThenBy(d => d.Check ? 1 : 0).ThenBy(d => Math.Abs(d.S * d.W - S.Wbub)).FirstOrDefault() : null;
        g.Extra = g.Dims.Where(d => d != g.KeepC && d != g.KeepO).ToList();
        Func<DR, bool> Stays = d => d != null && d.S == S.S && d.InBand && S.Feasible;
        // status (before any extension)
        if (g.KeepC == null) g.Issues.Add(g.Multi ? "missing chain" : "missing dim");
        else if (!Stays(g.KeepC)) g.Issues.Add((g.Multi ? "chain " : "dim ") + g.KeepC.D.Id.IntegerValue + " " + (g.KeepC.S != S.S ? "on the other side" : g.KeepC.Where));
        if (g.Multi && g.KeepO == null) g.Issues.Add("missing overall");
        else if (g.Multi && !Stays(g.KeepO)) g.Issues.Add("overall " + g.KeepO.D.Id.IntegerValue + " " + (g.KeepO.S != S.S ? "on the other side" : g.KeepO.Where));
        if (g.Extra.Count > 0) g.Issues.Add("extra " + string.Join(",", g.Extra.Select(d => d.D.Id.IntegerValue + "(" + d.Kind + ")")));
        foreach (var d in new[] { g.KeepC, g.KeepO }.Where(d => d != null && d.Clipped && Stays(d))) g.Issues.Add(d.D.Id.IntegerValue + " clipped by annotation crop");
        if (!S.Feasible)
        {
            g.Issues.Add("band " + S.Label + " too narrow: " + F(S.Avail) + " of " + F(S.Need) + " mm" + (S.Short.Count > 0 ? "; short grids " + string.Join(",", S.Short.Take(8)) + (S.Short.Count > 8 ? ",+" + (S.Short.Count - 8) : "") : ""));
            if (!opt.ExtendGrids) return;
            g.ExtendTo = S.Ecrop + (S.Need + 0.5) * sc; // new reach of the short grid ends (s * w)
            g.Decision = (S.Bubbles > 0 ? "bubble side" : "side") + ", grid ends extended";
            double add = g.ExtendTo.Value - S.Wbub; S.Wbub += add; S.Hi += add; S.Avail = (S.Wbub - S.Ecrop) / sc;
        }
        double tO = S.Wbub - opt.OffO * sc, tC = tO - (g.Multi ? opt.Step * sc : 0);
        var taken = new List<double>();
        if (Stays(g.KeepC)) taken.Add(S.S * g.KeepC.W);
        if (Stays(g.KeepO)) taken.Add(S.S * g.KeepO.W);
        Func<double, double> Free = target =>
        {   // nearest position to target inside the band, clear of the other grid dims of the group
            double minGap = 0.8 * opt.Step * sc;
            for (int k = 0; k < 400; k++)
            {
                foreach (var cand in new[] { target - k * 0.5 * sc, target + k * 0.5 * sc })
                    if (cand >= S.Lo - 0.01 && cand <= S.Hi + 0.01 && taken.All(x => Math.Abs(x - cand) >= minGap)) { taken.Add(cand); return cand; }
            }
            taken.Add(target); return target;
        };
        // a shared parent dim already sitting at the target just outside the annotation crop: take it over (the crop grows to it) instead of a duplicate
        Func<string[], double, DR> Adopt = (kinds, target) => g.Outside.FirstOrDefault(d => kinds.Contains(d.Kind) && d.S == S.S && Math.Abs(S.S * d.W - target) < 1.5 * sc);
        if (g.KeepC == null && (g.AdoptC = Adopt(new[] { "chain", "single" }, tC)) != null) taken.Add(S.S * g.AdoptC.W);
        if (g.Multi && g.KeepO == null && (g.AdoptO = Adopt(new[] { "overall" }, tO)) != null) taken.Add(S.S * g.AdoptO.W);
        if (g.KeepC == null) { if (g.AdoptC == null) g.NewC = Free(tC); } else if (!Stays(g.KeepC) && opt.Move) g.MoveC = Free(tC);
        if (g.Multi) { if (g.KeepO == null) { if (g.AdoptO == null) g.NewO = Free(tO); } else if (!Stays(g.KeepO) && opt.Move) g.MoveO = Free(tO); }
    }

    static string F(double x) => double.IsInfinity(x) ? "n/a" : x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    static object Summ(VA va, bool full)
    {
        var groups = va.Groups.Select(g => new
        {
            Group = g.Label, Side = g.Chosen.Label, Why = g.Decision,
            BandMm = F(g.Chosen.Avail) + " / need " + F(g.Chosen.Need),
            Chain = g.KeepC == null ? null : g.KeepC.D.Id.IntegerValue + " " + g.KeepC.Kind + " " + g.KeepC.D.DimensionType?.Name + " (" + g.KeepC.Where + ")",
            Overall = g.KeepO == null ? null : g.KeepO.D.Id.IntegerValue + " " + g.KeepO.D.DimensionType?.Name + " (" + g.KeepO.Where + ")",
            Status = g.Issues.Count == 0 ? "OK" : "FIX",
            Issues = g.Issues,
            Plan = full ? new
            {
                CreateChain = g.NewC.HasValue, CreateOverall = g.NewO.HasValue, MoveChain = g.MoveC.HasValue, MoveOverall = g.MoveO.HasValue,
                ExtendGridsToMm = g.ExtendTo.HasValue ? (object)Math.Round(g.ExtendTo.Value) : null,
                OtherSide = g.Sides.Where(sd => sd != g.Chosen).Select(sd => sd.Label + " " + F(sd.Avail) + "/" + F(sd.Need) + "mm").FirstOrDefault()
            } : null
        }).ToList();
        object anno;
        if (!va.CropOn) anno = "crop off";
        else if (!va.AnnoOn) anno = "annotation crop off (nothing clipped)";
        else anno = new { OffsetsPaperMm = AnnoOff(va), BubbleEndsOutside = va.BubbleEndsOutside.Count == 0 ? null : va.BubbleEndsOutside, Need = AnnoGrow(va) };
        bool ok = groups.All(x => x.Status == "OK") && (!va.CropOn || !va.AnnoOn || va.BubbleEndsOutside.Count == 0);
        return new
        {
            View = va.V.Name, ViewId = va.V.Id.IntegerValue, Type = va.V.ViewType.ToString(), Scale = (int)va.Sc,
            Parent = va.V.GetPrimaryViewId() == ElementId.InvalidElementId ? null : va.V.GetPrimaryViewId().IntegerValue.ToString(),
            Status = ok ? "OK" : "FIX", AnnoCrop = anno, Groups = groups,
            SingleGrids = va.Singles.Count > 0 ? va.Singles : null, Skipped = va.Skipped.Count > 0 ? va.Skipped : null
        };
    }
    static object AnnoOff(VA va)
    {
        var m = va.V.GetCropRegionShapeManager();
        try { return new { L = Math.Round(m.LeftAnnotationCropOffset * MM, 1), R = Math.Round(m.RightAnnotationCropOffset * MM, 1), T = Math.Round(m.TopAnnotationCropOffset * MM, 1), B = Math.Round(m.BottomAnnotationCropOffset * MM, 1) }; } catch { return null; }
    }
    // paper mm each annotation crop side must grow (0 = fine); extra = additional rect (view mm) to hold, e.g. dims
    static Dictionary<string, double> AnnoGrow(VA va, double[] extra = null, double margin = 0)
    {
        var res = new Dictionary<string, double>();
        if (va.AnnoRect == null || va.AnnoNeed == null) return res;
        var n = (double[])va.AnnoNeed.Clone();
        if (extra != null) { n[0] = Math.Min(n[0], extra[0]); n[1] = Math.Max(n[1], extra[1]); n[2] = Math.Min(n[2], extra[2]); n[3] = Math.Max(n[3], extra[3]); }
        double sc = va.Sc, mg = margin * sc;
        double L = va.AnnoRect[0] - (n[0] - mg), R = (n[1] + mg) - va.AnnoRect[1], B = va.AnnoRect[2] - (n[2] - mg), T = (n[3] + mg) - va.AnnoRect[3];
        double th = extra == null ? 0.5 * sc : 0.05 * sc; // audit: ignore < 0.5 mm paper
        if (L > th) res["L"] = Math.Round(L / sc, 1); if (R > th) res["R"] = Math.Round(R / sc, 1);
        if (T > th) res["T"] = Math.Round(T / sc, 1); if (B > th) res["B"] = Math.Round(B / sc, 1);
        return res;
    }

    static List<View> SheetViews(Document doc, JObject args, out Dictionary<int, string> sheetOf)
    {
        sheetOf = new Dictionary<int, string>(); var res = new List<View>();
        var excl = (args["excludeNameContains"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        if (args["viewIds"] is JArray vids) { foreach (var t in vids) res.Add((View)doc.GetElement(new ElementId((int)t))); }
        else
        {
            string prefix = (string)args["sheetPrefix"]; var nums = (args["sheetNumbers"] as JArray)?.Select(t => (string)t).ToList();
            foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => nums != null ? nums.Contains(s.SheetNumber) : prefix != null && s.SheetNumber.StartsWith(prefix)).OrderBy(s => s.SheetNumber))
                foreach (var id in s.GetAllPlacedViews())
                {
                    var v = (View)doc.GetElement(id);
                    if (v.ViewType == ViewType.Legend || v.ViewType == ViewType.Schedule || v.ViewType == ViewType.DraftingView || v.ViewType == ViewType.ThreeD) continue;
                    sheetOf[v.Id.IntegerValue] = s.SheetNumber; res.Add(v);
                }
        }
        return res.Where(v => v != null && !excl.Any(e => v.Name.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], logPath = (string)args["logPath"];
        if (mode == "undo") return Undo(doc, logPath);
        var opt = ReadOpt(doc, args);
        if (args["dimType"] != null && opt.Type == null) return new { Error = "dimension type not found: " + args["dimType"] };
        if (mode == "audit")
        {
            var views = SheetViews(doc, args, out var sheetOf);
            bool only = args.Value<bool?>("onlyProblems") ?? false;
            var outL = new List<object>(); int ok = 0, fix = 0, none = 0; var hidden = new List<string>();
            foreach (var v in views)
            {
                if (DimsHidden(v)) { hidden.Add(v.Name); continue; } // the template hides dimensions: grid dims would never show
                VA va; try { va = Analyze(doc, v, opt); } catch (Exception e) { outL.Add(new { View = v.Name, ViewId = v.Id.IntegerValue, Error = e.Message }); continue; }
                if (va.Groups.Count == 0) { none++; continue; }
                var j = JObject.FromObject(Summ(va, false));
                if ((string)j["Status"] == "OK") { ok++; if (only) continue; } else fix++;
                if (sheetOf.TryGetValue(v.Id.IntegerValue, out var sn)) j.AddFirst(new JProperty("Sheet", sn));
                outL.Add(j);
            }
            return new { Views = views.Count, Ok = ok, Fix = fix, NoGridGroups = none, DimensionsHidden = hidden.Count > 0 ? hidden : null, Result = outL };
        }
        if (mode != "preview" && mode != "apply") return new { Error = "mode must be audit, preview, apply or undo" };
        if (args["viewId"] == null) return new { Error = "viewId required" };
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "logPath required for apply" };
        var view = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        if (DimsHidden(view)) return new { Error = "Dimensions are hidden in this view (view template / V/G): grid dims would not show. Skipped." };
        var A = Analyze(doc, view, opt);
        if (opt.Type == null && A.Groups.Any(g => g.NewC.HasValue || g.NewO.HasValue)) return new { Error = "dimType required: dims must be created", Before = Summ(A, true) };
        var log = new JObject { ["viewId"] = view.Id.IntegerValue, ["created"] = new JArray(), ["moved"] = new JArray(), ["deleted"] = new JArray(), ["grids"] = new JArray(), ["hidden"] = new JArray() };
        var errors = new List<string>(); var done = new List<string>();
        var touched = new List<Dimension>();
        var sibs = Siblings(doc, view);
        var shownBefore = new Dictionary<int, HashSet<int>>(); // dim id -> sibling views that show it now
        foreach (var d in A.Groups.SelectMany(g => g.Dims).Select(x => x.D))
            shownBefore[d.Id.IntegerValue] = new HashSet<int>(sibs.Where(u => ShowsIn(doc, u, d)).Select(u => u.Id.IntegerValue));
        Func<Dimension, string> SharedWith = d => shownBefore.TryGetValue(d.Id.IntegerValue, out var hs) && hs.Count > 0 ? string.Join(" | ", sibs.Where(u => hs.Contains(u.Id.IntegerValue)).Select(u => u.Name)) : null;
        using (var t = new Transaction(doc, "Grid dims in band"))
        {
            t.Start();
            foreach (var g in A.Groups)
            {
                var S = g.Chosen; int s = S.S;
                Func<double, double, XYZ> P = (a, sw) => { double w = s * sw; return A.P2(g.aR * a + g.tR * w, g.aU * a + g.tU * w); };
                XYZ tVec = A.R * g.tR + A.U * g.tU, aVec = A.R * g.aR + A.U * g.aU;
                try
                {
                    if (g.ExtendTo.HasValue)
                        foreach (var gi in g.P.SelectMany(p => p.Gs))
                        {
                            double reach = s > 0 ? gi.W1 : -gi.W0; if (reach >= g.ExtendTo.Value - 1) continue;
                            var grid = gi.G; int ei = s > 0 ? gi.E1 : gi.E0; var de = ei == 0 ? DatumEnds.End0 : DatumEnds.End1;
                            var c = (Line)grid.GetCurvesInView(DatumExtentType.ViewSpecific, view).First();
                            ((JArray)log["grids"]).Add(new JObject { ["id"] = grid.Id.IntegerValue, ["x0"] = c.GetEndPoint(0).X, ["y0"] = c.GetEndPoint(0).Y, ["z0"] = c.GetEndPoint(0).Z, ["x1"] = c.GetEndPoint(1).X, ["y1"] = c.GetEndPoint(1).Y, ["z1"] = c.GetEndPoint(1).Z, ["e"] = (int)de, ["ext"] = grid.GetDatumExtentTypeInView(de, view).ToString() });
                            bool pin = grid.Pinned; if (pin) grid.Pinned = false;
                            if (grid.GetDatumExtentTypeInView(de, view) != DatumExtentType.ViewSpecific) grid.SetDatumExtentType(de, view, DatumExtentType.ViewSpecific);
                            c = (Line)grid.GetCurvesInView(DatumExtentType.ViewSpecific, view).First();
                            XYZ e0 = c.GetEndPoint(0), e1 = c.GetEndPoint(1), move = tVec * (s * (g.ExtendTo.Value - reach) / MM);
                            grid.SetCurveInView(DatumExtentType.ViewSpecific, view, ei == 0 ? Line.CreateBound(e0 + move, e1) : Line.CreateBound(e0, e1 + move));
                            if (pin) grid.Pinned = true;
                            done.Add("grid " + grid.Name + " " + S.Label + " end +" + F((g.ExtendTo.Value - reach) / A.Sc) + "mm");
                        }
                    foreach (var mv in new[] { Tuple.Create(g.KeepC, g.MoveC), Tuple.Create(g.KeepO, g.MoveO) })
                    {
                        if (mv.Item1 == null || !mv.Item2.HasValue) continue;
                        double delta = s * mv.Item2.Value - mv.Item1.W; var dv = tVec * (delta / MM);
                        ElementTransformUtils.MoveElement(doc, mv.Item1.D.Id, dv);
                        ((JArray)log["moved"]).Add(new JObject { ["id"] = mv.Item1.D.Id.IntegerValue, ["x"] = dv.X, ["y"] = dv.Y, ["z"] = dv.Z });
                        var sh = SharedWith(mv.Item1.D);
                        touched.Add(mv.Item1.D); done.Add("moved " + mv.Item1.D.Id.IntegerValue + " " + F(delta / A.Sc) + "mm to " + S.Label + (sh != null ? " (also shown in " + sh + ": re-check that view)" : ""));
                    }
                    foreach (var ad in new[] { g.AdoptC, g.AdoptO }.Where(x => x != null)) done.Add("adopted " + ad.D.Id.IntegerValue + " (" + ad.Kind + ", shared, shown once the annotation crop grows) " + S.Label);
                    double a0 = g.P.First().A, a1 = g.P.Last().A;
                    if (g.NewC.HasValue)
                    {
                        var ra = new ReferenceArray(); foreach (var p in g.P) ra.Append(new Reference(p.Ref.G));
                        var d = doc.Create.NewDimension(view, Line.CreateBound(P(a0, g.NewC.Value), P(a1, g.NewC.Value)), ra, opt.Type);
                        ((JArray)log["created"]).Add(d.Id.IntegerValue); touched.Add(d); done.Add("created " + (g.Multi ? "chain " : "dim ") + d.Id.IntegerValue + " " + S.Label);
                    }
                    if (g.NewO.HasValue)
                    {
                        var ra = new ReferenceArray(); ra.Append(new Reference(g.P.First().Ref.G)); ra.Append(new Reference(g.P.Last().Ref.G));
                        var d = doc.Create.NewDimension(view, Line.CreateBound(P(a0, g.NewO.Value), P(a1, g.NewO.Value)), ra, opt.Type);
                        ((JArray)log["created"]).Add(d.Id.IntegerValue); touched.Add(d); done.Add("created overall " + d.Id.IntegerValue + " " + S.Label);
                        // overall text sitting on a grid line: nudge it along the dim just clear of the line
                        double TW = ((Math.Round(Math.Abs(a1 - a0)).ToString().Length * 1.3) + 1) * A.Sc, oc = (a0 + a1) / 2;
                        var gp = g.P.Select(p => p.A).ToList();
                        var hit = gp.Where(x => Math.Abs(x - oc) < TW / 2 + 0.5 * A.Sc).OrderBy(x => Math.Abs(x - oc)).ToList();
                        if (hit.Count > 0)
                        {
                            double g0 = hit[0], dd = TW / 2 + 0.8 * A.Sc;
                            double right = gp.Where(x => x > g0 + 1).DefaultIfEmpty(double.MaxValue).Min() - g0, left = g0 - gp.Where(x => x < g0 - 1).DefaultIfEmpty(double.MinValue).Max();
                            double nc = right >= left ? g0 + dd : g0 - dd;
                            doc.Regenerate(); try { d.TextPosition = d.TextPosition + aVec * ((nc - oc) / MM); } catch { }
                        }
                    }
                    if (opt.DeleteExtra)
                        foreach (var x in g.Extra)
                        {
                            var sh = SharedWith(x.D);
                            if (sh != null)
                            {   // another drawing uses it: hide it here only
                                view.HideElements(new List<ElementId> { x.D.Id });
                                ((JArray)log["hidden"]).Add(new JObject { ["view"] = view.Id.IntegerValue, ["id"] = x.D.Id.IntegerValue });
                                done.Add("hidden " + x.D.Id.IntegerValue + " (" + x.Kind + ") here, kept for " + sh); continue;
                            }
                            var refs = new JArray();
                            foreach (Reference r in x.D.References)
                            {
                                var e = doc.GetElement(r.ElementId);
                                if (e is Grid && r.LinkedElementId == ElementId.InvalidElementId) refs.Add(new JObject { ["grid"] = e.Id.IntegerValue });
                                else refs.Add(new JObject { ["stable"] = r.ConvertToStableRepresentation(doc) });
                            }
                            var ln = (Line)x.D.Curve; var org = ln.Origin; var dir = ln.Direction;
                            ((JArray)log["deleted"]).Add(new JObject { ["id"] = x.D.Id.IntegerValue, ["view"] = x.D.OwnerViewId.IntegerValue, ["type"] = x.D.GetTypeId().IntegerValue, ["refs"] = refs, ["ox"] = org.X, ["oy"] = org.Y, ["oz"] = org.Z, ["dx"] = dir.X, ["dy"] = dir.Y, ["dz"] = dir.Z });
                            done.Add("deleted " + x.D.Id.IntegerValue + " (" + x.Kind + ")");
                            doc.Delete(x.D.Id);
                        }
                }
                catch (Exception e) { errors.Add(g.Label + ": " + e.Message); }
            }
            doc.Regenerate();
            // dependent families: new / moved dims belong to the parent and show in every family view whose crop holds them.
            // Hide them where they newly show; a moved dim that already showed in a sibling is that sibling's dim too: keep it, report it.
            if (opt.HideSiblings && touched.Count > 0)
                foreach (var u in sibs)
                {
                    var hide = touched.Where(d => d.IsValidObject && ShowsIn(doc, u, d) && !(shownBefore.TryGetValue(d.Id.IntegerValue, out var was) && was.Contains(u.Id.IntegerValue))).Select(d => d.Id).ToList();
                    if (hide.Count == 0) continue;
                    try { u.HideElements(hide); foreach (var id in hide) ((JArray)log["hidden"]).Add(new JObject { ["view"] = u.Id.IntegerValue, ["id"] = id.IntegerValue }); done.Add("hidden " + string.Join(",", hide.Select(x => x.IntegerValue)) + " in " + u.Name); }
                    catch (Exception e) { errors.Add("hide in " + u.Name + ": " + e.Message); }
                }
            // annotation crop: hold every bubble and the grid dims (never shrink)
            object annoDone = null;
            if (opt.FitAnno && A.CropOn)
            {
                var m = view.GetCropRegionShapeManager();
                bool on = A.AnnoOn;
                if (!on && opt.ActivateAnno && m.CanHaveAnnotationCrop)
                {
                    log["annoActive"] = 0; view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE).Set(1); on = true;
                    if (A.AnnoRect == null) A.AnnoRect = (double[])A.CropRect.Clone();
                }
                if (on)
                {
                    var B = AnalyzeAfter(doc, view, opt, A);
                    var grow = AnnoGrow(B, DimRect(B), opt.AnnoMargin);
                    log["anno"] = new JObject { ["L"] = m.LeftAnnotationCropOffset, ["R"] = m.RightAnnotationCropOffset, ["T"] = m.TopAnnotationCropOffset, ["B"] = m.BottomAnnotationCropOffset };
                    try
                    {
                        if (grow.ContainsKey("L")) m.LeftAnnotationCropOffset += grow["L"] / MM;
                        if (grow.ContainsKey("R")) m.RightAnnotationCropOffset += grow["R"] / MM;
                        if (grow.ContainsKey("T")) m.TopAnnotationCropOffset += grow["T"] / MM;
                        if (grow.ContainsKey("B")) m.BottomAnnotationCropOffset += grow["B"] / MM;
                        annoDone = grow.Count == 0 ? (object)"already holds bubbles and dims" : new { GrownPaperMm = grow, Now = AnnoOff(B) };
                    }
                    catch (Exception e) { errors.Add("annotation crop: " + e.Message); }
                }
                else annoDone = "annotation crop off (nothing clipped)";
            }
            if (mode == "apply" && errors.Count == 0) t.Commit(); else t.RollBack();
            if (mode == "apply" && errors.Count == 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.WriteAllText(logPath, log.ToString(Formatting.Indented));
            }
            if (args.Value<bool?>("compact") ?? false)
            {   // one line per action kind; grid end extensions collapsed per side
                var gridLines = done.Where(x => x.StartsWith("grid ")).Select(x => x.Split(' ')).GroupBy(p => p[2] + " " + p[4]).Select(gp => "extended " + gp.Count() + " grid ends " + gp.Key);
                var after = mode == "apply" && errors.Count == 0 ? Analyze(doc, view, opt) : null;
                return new
                {
                    View = view.Name, Committed = mode == "apply" && errors.Count == 0, Errors = errors,
                    Done = gridLines.Concat(done.Where(x => !x.StartsWith("grid "))).ToList(), AnnoCrop = annoDone,
                    After = after == null ? null : after.Groups.Select(g => g.Label.Split(' ')[0] + " " + g.Chosen.Label + ": " + (g.Issues.Count == 0 ? "OK" : string.Join("; ", g.Issues))).ToList(),
                    ExtendedSides = A.Groups.Where(g => g.ExtendTo.HasValue).Select(g => g.Chosen.Label).ToList()
                };
            }
            return new
            {
                mode, Committed = mode == "apply" && errors.Count == 0, Errors = errors, Done = done, AnnoCrop = annoDone,
                Before = Summ(A, true), After = mode == "apply" && errors.Count == 0 ? Summ(Analyze(doc, view, opt), false) : null
            };
        }
    }

    static bool DimsHidden(View v) { try { return v.GetCategoryHidden(new ElementId(BuiltInCategory.OST_Dimensions)); } catch { return false; } }

    // the other views of the dependent family (parent + dependents) that are placed on sheets
    static List<View> Siblings(Document doc, View view)
    {
        var prim = view.GetPrimaryViewId(); var parent = prim == ElementId.InvalidElementId ? view : (View)doc.GetElement(prim);
        var fam = new List<View> { parent }; fam.AddRange(parent.GetDependentViewIds().Select(id => (View)doc.GetElement(id)));
        if (fam.Count == 1) return new List<View>();
        var onSheet = new HashSet<int>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(vp => vp.ViewId.IntegerValue));
        return fam.Where(u => u.Id != view.Id && onSheet.Contains(u.Id.IntegerValue)).ToList();
    }
    // a dim shows in view u: not hidden there and inside its annotation crop (annotation crop off: shows anywhere)
    static bool ShowsIn(Document doc, View u, Dimension d)
    {
        if (!new FilteredElementCollector(doc, u.Id).OfClass(typeof(Dimension)).ToElementIds().Contains(d.Id)) return false;
        if (!u.CropBoxActive || u.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger() != 1) return true;
        var ann = u.GetCropRegionShapeManager().GetAnnotationCropShape(); var bb = d.get_BoundingBox(u);
        if (ann == null || bb == null) return true;
        var ua = new VA { V = u, Sc = u.Scale, O = u.Origin, R = u.RightDirection, U = u.UpDirection };
        var pts = ann.SelectMany(c => c.Tessellate()).Select(p => new[] { ua.Rr(p), ua.Uu(p) }).ToList();
        double r0 = Math.Min(ua.Rr(bb.Min), ua.Rr(bb.Max)), r1 = Math.Max(ua.Rr(bb.Min), ua.Rr(bb.Max)), u0 = Math.Min(ua.Uu(bb.Min), ua.Uu(bb.Max)), u1 = Math.Max(ua.Uu(bb.Min), ua.Uu(bb.Max));
        return !(r1 < pts.Min(p => p[0]) || r0 > pts.Max(p => p[0]) || u1 < pts.Min(p => p[1]) || u0 > pts.Max(p => p[1]));
    }

    // re-read the view inside the transaction (grids may have moved) but keep the annotation rect we may have switched on
    static VA AnalyzeAfter(Document doc, View v, Opt opt, VA before)
    {
        var b = Analyze(doc, v, opt);
        if (b.AnnoRect == null) b.AnnoRect = before.AnnoRect;
        return b;
    }
    // view-mm rect of the kept/new grid dims of every group
    static double[] DimRect(VA va)
    {
        var pts = new List<double[]>();
        foreach (var g in va.Groups)
            foreach (var d in g.Dims.Where(d => d.Kind != "link-grid").Concat(new[] { g.AdoptC, g.AdoptO }.Where(x => x != null)))
            {
                var bb = d.D.get_BoundingBox(va.V);
                double r0, r1, u0, u1;
                if (bb != null) { r0 = Math.Min(va.Rr(bb.Min), va.Rr(bb.Max)); r1 = Math.Max(va.Rr(bb.Min), va.Rr(bb.Max)); u0 = Math.Min(va.Uu(bb.Min), va.Uu(bb.Max)); u1 = Math.Max(va.Uu(bb.Min), va.Uu(bb.Max)); }
                else
                {   // beyond the annotation crop Revit gives no box: rebuild it from the dim line and the text height
                    var ln = d.D.Curve as Line; if (ln == null) continue;
                    var segs = d.D.NumberOfSegments > 0 ? d.D.Segments.Cast<DimensionSegment>().Select(s => Tuple.Create(s.Origin, s.Value ?? 0)).ToList() : new List<Tuple<XYZ, double>> { Tuple.Create(d.D.Origin, d.D.Value ?? 0) };
                    var ends = segs.SelectMany(s => new[] { s.Item1 - ln.Direction * (s.Item2 / 2), s.Item1 + ln.Direction * (s.Item2 / 2) }).ToList();
                    double pad = (d.Txt + 1) * va.Sc;
                    r0 = ends.Min(va.Rr) - pad; r1 = ends.Max(va.Rr) + pad; u0 = ends.Min(va.Uu) - pad; u1 = ends.Max(va.Uu) + pad;
                }
                if (va.CropRect != null)
                {   // along the dim (parent dims may run on into the next dependent) only the part over this crop counts
                    if (Math.Abs(g.aR) > 0.7) { r0 = Math.Max(r0, va.CropRect[0]); r1 = Math.Min(r1, va.CropRect[1]); }
                    if (Math.Abs(g.aU) > 0.7) { u0 = Math.Max(u0, va.CropRect[2]); u1 = Math.Min(u1, va.CropRect[3]); }
                }
                pts.Add(new[] { r0, u0 }); pts.Add(new[] { r1, u1 });
            }
        if (pts.Count == 0) return null;
        return new[] { pts.Min(p => p[0]), pts.Max(p => p[0]), pts.Min(p => p[1]), pts.Max(p => p[1]) };
    }

    static object Undo(Document doc, string logPath)
    {
        var L = JObject.Parse(File.ReadAllText(logPath));
        var view = (View)doc.GetElement(new ElementId((int)L["viewId"]));
        int n = 0; var errors = new List<string>();
        using (var t = new Transaction(doc, "Undo grid dims in band"))
        {
            t.Start();
            foreach (JObject h in (JArray)L["hidden"]) { try { var u = (View)doc.GetElement(new ElementId((int)h["view"])); var id = new ElementId((int)h["id"]); if (doc.GetElement(id) != null) u.UnhideElements(new List<ElementId> { id }); n++; } catch (Exception e) { errors.Add(e.Message); } }
            foreach (var id in (JArray)L["created"]) { var e = doc.GetElement(new ElementId((int)id)); if (e != null) { doc.Delete(e.Id); n++; } }
            foreach (JObject mv in (JArray)L["moved"]) { var id = new ElementId((int)mv["id"]); if (doc.GetElement(id) != null) { ElementTransformUtils.MoveElement(doc, id, -new XYZ((double)mv["x"], (double)mv["y"], (double)mv["z"])); n++; } }
            foreach (JObject dl in (JArray)L["deleted"])
            {
                try
                {
                    var ra = new ReferenceArray();
                    foreach (JObject r in (JArray)dl["refs"])
                        ra.Append(r["grid"] != null ? new Reference(doc.GetElement(new ElementId((int)r["grid"]))) : Reference.ParseFromStableRepresentation(doc, (string)r["stable"]));
                    var owner = (View)doc.GetElement(new ElementId((int)dl["view"]));
                    var org = new XYZ((double)dl["ox"], (double)dl["oy"], (double)dl["oz"]); var dir = new XYZ((double)dl["dx"], (double)dl["dy"], (double)dl["dz"]);
                    var d = doc.Create.NewDimension(owner, Line.CreateBound(org - dir * 10, org + dir * 10), ra, (DimensionType)doc.GetElement(new ElementId((int)dl["type"])));
                    n++;
                }
                catch (Exception e) { errors.Add("recreate " + dl["id"] + ": " + e.Message); }
            }
            foreach (JObject g in ((JArray)L["grids"]).Reverse())
            {
                try
                {
                    var gr = (Grid)doc.GetElement(new ElementId((int)g["id"])); bool pin = gr.Pinned; if (pin) gr.Pinned = false;
                    gr.SetCurveInView(DatumExtentType.ViewSpecific, view, Line.CreateBound(new XYZ((double)g["x0"], (double)g["y0"], (double)g["z0"]), new XYZ((double)g["x1"], (double)g["y1"], (double)g["z1"])));
                    if ((string)g["ext"] == "Model") gr.SetDatumExtentType((DatumEnds)(int)g["e"], view, DatumExtentType.Model);
                    if (pin) gr.Pinned = true; n++;
                }
                catch (Exception e) { errors.Add("grid " + g["id"] + ": " + e.Message); }
            }
            if (L["anno"] is JObject an)
            {
                var m = view.GetCropRegionShapeManager();
                m.LeftAnnotationCropOffset = (double)an["L"]; m.RightAnnotationCropOffset = (double)an["R"]; m.TopAnnotationCropOffset = (double)an["T"]; m.BottomAnnotationCropOffset = (double)an["B"]; n++;
            }
            if (L["annoActive"] != null) view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE).Set((int)L["annoActive"]);
            t.Commit();
        }
        return new { Undone = n, Errors = errors };
    }
}
