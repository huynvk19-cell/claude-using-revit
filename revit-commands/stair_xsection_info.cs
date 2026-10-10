/* mcp-tool
{
  "description": "Read-only, stair SECTION across the flights (XA-XC): flight bands, clear heights, widths, run tags, spots, riser numbers vs the standard.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number", "description": "the stair cross-section view" },
      "excludeStairIds": { "type": "array", "items": { "type": "number" }, "description": "stairs seen but not part of this core" },
      "toleranceMm": { "type": "number", "description": "value tolerance when matching dims, default 1" },
      "outPath": { "type": "string", "description": "write the full result as JSON here" }
    },
    "required": [ "viewId" ]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: stair SECTION cut ACROSS the flights (drafting-stair-section-cross.md): the flights are seen end-on.
//  - Flights F1.. going up, with Facing: 'away' (rises away from the viewer: risers and nosings seen -> riser numbers)
//    or 'toward' (rises toward the viewer: only the soffit is seen -> no numbers); half 'left' / 'right' of the core.
//  - Bands: the elevation steps of the core (one per flight elevation range, a scissor pair counts once) with the
//    rise text '169.4mm x 16R = ' + height, below '(EQUAL RISERS)' -> XA1 check (value live, prefix, below), plus
//    segments carrying an 'R =' prefix that match no band (merged flights).
//  - XA2 level chain present; XA3 width chains (a horizontal dim with handrail references) per stair block;
//    XA4 clear height + slab thickness per half (landing / floor top -> soffit of the next slab above that overlaps
//    the column), matched with the vertical segments of the view; XA5 overall widths; dim hygiene: Replace with
//    text, 0 mm segments, duplicate dims.
//  - XB1 one run tag per seen flight, tag text elevations = the flight's From / To EL (wrong LV type shows here);
//    XB3 spot elevations on every cut landing / floor top; XC riser numbers only on 'away' flights, Display Rule,
//    first number per flight vs the count by elevation over the whole core (a scissor band counted once).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairXsectionInfo
{
    const double MM = 304.8;
    static XYZ O, Rg, Up, Vd; static double ElOff;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double Dp(XYZ p) { return (p - O).DotProduct(Vd) * MM; }
    static double ZE(double zft) { return (zft - ElOff) * MM; }
    static string Num(double x) { return Math.Round(x, 1).ToString("0.#", CultureInfo.InvariantCulture); }

    class Fl
    {
        public StairsRun R; public Stairs S; public string Label, State, Facing, Half; public int Risers, Treads, FirstNo = -1;
        public double El0, El1, X0, X1, RiserH, Depth;
        public string RiseText { get { return Num(RiserH) + "mm x " + Risers + "R = "; } }
    }
    class Slab { public string Name; public double Top, Soffit, X0, X1; public bool Cut; }

    static List<PlanarFace> HFaces(Element e)   // horizontal planar faces of the element geometry
    {
        var res = new List<PlanarFace>(); var st = new Stack<GeometryElement>(); st.Push(e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
        while (st.Count > 0)
        {
            var ge = st.Pop(); if (ge == null) continue;
            foreach (var g in ge)
            {
                var gi = g as GeometryInstance; if (gi != null) { st.Push(gi.GetInstanceGeometry()); continue; }
                var so = g as Solid; if (so == null || so.Faces.IsEmpty) continue;
                foreach (Face f in so.Faces) { var pf = f as PlanarFace; if (pf != null && Math.Abs(pf.FaceNormal.Z) > 0.999) res.Add(pf); }
            }
        }
        return res;
    }
    static List<List<XYZ>> Loops(PlanarFace pf) { var r = new List<List<XYZ>>(); foreach (EdgeArray ea in pf.EdgeLoops) foreach (Edge ed in ea) r.Add(ed.Tessellate().ToList()); return r; }
    static double[] CutX(IEnumerable<List<XYZ>> loops)   // x range where the outline crosses the section plane (depth 0)
    {
        var xs = new List<double>();
        foreach (var lp in loops)
            for (int i = 0; i + 1 < lp.Count; i++)
            {
                double a = Dp(lp[i]), b = Dp(lp[i + 1]);
                if (!((a <= 0 && b >= 0) || (a >= 0 && b <= 0))) continue;
                if (Math.Abs(a - b) < 1e-6) { xs.Add(VX(lp[i])); xs.Add(VX(lp[i + 1])); }
                else { double t = a / (a - b); xs.Add(VX(lp[i]) + t * (VX(lp[i + 1]) - VX(lp[i]))); }
            }
        return xs.Count < 2 ? null : new[] { xs.Min(), xs.Max() };
    }
    class Seg { public int Id; public bool Vert; public double Val, Line; public string Pre, Below, Ovr; public List<string> Cats; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = doc.GetElement(new ElementId(args.Value<int>("viewId"))) as View;
        if (v == null || v.ViewType != ViewType.Section) return new { Error = "viewId must be a section view" };
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection; Vd = v.ViewDirection;
        double tol = args.Value<double?>("toleranceMm") ?? 1;
        var issues = new List<string>(); var notes = new List<string>();
        var excl = new HashSet<int>(((JArray)args["excludeStairIds"] ?? new JArray()).Select(x => (int)x));

        // ---- flights and landings
        var fl = new List<Fl>(); var slabs = new List<Slab>(); bool elSet = false; var startNo = new Dictionary<int, int>();
        foreach (var s in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>().Where(s => !excl.Contains(s.Id.IntegerValue)))
        {
            var pl = s.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            var bl = pl != null && pl.StorageType == StorageType.ElementId ? doc.GetElement(pl.AsElementId()) as Level : null;
            if (bl == null) { notes.Add("stairs " + s.Id.IntegerValue + ": base level not readable, skipped"); continue; }
            if (!elSet) { ElOff = bl.ProjectElevation - bl.Elevation; elSet = true; }
            double sb = bl.Elevation + (s.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET)?.AsDouble() ?? 0);
            var sn = s.LookupParameter("Tread/Riser Start Number"); if (sn != null && sn.StorageType == StorageType.Integer) startNo[s.Id.IntegerValue] = sn.AsInteger();
            foreach (var rid in s.GetStairsRuns())
            {
                var r = doc.GetElement(rid) as StairsRun; if (r == null) continue;
                var f = new Fl { R = r, S = s, El0 = (sb + r.BaseElevation) * MM, El1 = (sb + r.TopElevation) * MM, Risers = r.ActualRisersNumber, Treads = r.ActualTreadsNumber >= r.ActualRisersNumber ? r.ActualRisersNumber - 1 : r.ActualTreadsNumber, RiserH = s.ActualRiserHeight * MM, Depth = s.ActualTreadDepth * MM };
                var fp = new List<XYZ>(); try { foreach (Curve c in r.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count == 0) continue;
                f.X0 = fp.Min(VX); f.X1 = fp.Max(VX);
                double d0 = fp.Min(Dp), d1 = fp.Max(Dp);
                f.State = d0 <= 0 && d1 >= 0 ? "cut" : d1 < 0 ? "beyond" : "in front";
                try
                {
                    var pc = r.GetStairsPath().Cast<Curve>().ToList();
                    var dir = pc.Last().GetEndPoint(1) - pc.First().GetEndPoint(0); dir = new XYZ(dir.X, dir.Y, 0);
                    double along = dir.GetLength() < 1e-6 ? 0 : dir.Normalize().DotProduct(Vd);
                    f.Facing = Math.Abs(along) < 0.7 ? "sideways (parallel section?)" : along < 0 ? "away" : "toward";   // Vd points to the viewer
                }
                catch { f.Facing = "?"; }
                fl.Add(f);
            }
            foreach (var lid in s.GetStairsLandings())
            {
                var l = doc.GetElement(lid) as StairsLanding; if (l == null) continue;
                var loops = new List<List<XYZ>>(); try { foreach (Curve c in l.GetFootprintBoundary()) loops.Add(c.Tessellate().ToList()); } catch { }
                var fp = loops.SelectMany(q => q).ToList(); if (fp.Count == 0 || fp.Min(Dp) > 0) continue;
                double top = (sb + l.BaseElevation) * MM;
                // thickness from the geometry: top face -> lowest downward face (a 15 mm finish face sits right under the top; the bounding box spans the stairs)
                var hf = HFaces(l); var up = hf.Where(q => q.FaceNormal.Z > 0).OrderByDescending(q => q.Origin.Z).FirstOrDefault();
                var dn = up == null ? null : hf.Where(q => q.FaceNormal.Z < 0 && q.Origin.Z < up.Origin.Z - 0.03).OrderBy(q => q.Origin.Z).FirstOrDefault();
                double th = up != null && dn != null ? (up.Origin.Z - dn.Origin.Z) * MM : double.NaN;
                var cx = CutX(loops);   // the x range the section plane cuts (an L-shaped landing may be cut on one half only)
                slabs.Add(new Slab { Name = "landing " + l.Id.IntegerValue + " (+" + Math.Round(top) + ")", Top = top, Soffit = top - th, X0 = cx != null ? cx[0] : fp.Min(VX), X1 = cx != null ? cx[1] : fp.Max(VX), Cut = cx != null });
            }
        }
        fl = fl.Where(f => f.State != "in front").OrderBy(f => f.El0).ThenBy(f => f.X0).ToList();
        if (fl.Count == 0) return new { View = v.Name, Error = "no host stair flight seen in this section (link stairs are not read)" };
        for (int i = 0; i < fl.Count; i++) fl[i].Label = "F" + (i + 1);
        double cx0 = fl.Min(f => f.X0), cx1 = fl.Max(f => f.X1), mid = (cx0 + cx1) / 2;
        foreach (var f in fl) f.Half = (f.X0 + f.X1) / 2 < mid ? "left" : "right";
        if (fl.All(f => f.Facing.StartsWith("sideways"))) return new { View = v.Name, Error = "every flight runs across the view: this is a section parallel to the stair path -> stair_section_info" };
        if (fl.Any(f => f.Facing.StartsWith("sideways"))) notes.Add("some flights run across the view (parallel part): check them with stair_section_info");

        // floors cut by the section inside the core: top face crossing the section plane, soffit right under it
        foreach (var e in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Floor)))
        {
            var bb = e.get_BoundingBox(null); if (bb == null || (bb.Max.Z - bb.Min.Z) * MM > 1500) continue;
            if (ZE(bb.Max.Z) < fl.Min(f => f.El0) - 500 || ZE(bb.Max.Z) > fl.Max(f => f.El1) + 6000) continue;
            var hf = HFaces(e); double[] cx = null;
            var up = hf.Where(q => q.FaceNormal.Z > 0).OrderByDescending(q => q.Origin.Z).FirstOrDefault(q => { cx = CutX(Loops(q)); return cx != null && cx[1] > cx0 + 50 && cx[0] < cx1 - 50; });
            if (up == null) continue;
            var dn = hf.Where(q => q.FaceNormal.Z < 0 && q.Origin.Z < up.Origin.Z - 0.01).OrderByDescending(q => q.Origin.Z).FirstOrDefault();
            double top = ZE(up.Origin.Z);
            if (slabs.Any(q => q.Cut && Math.Abs(q.Top - top) < 50 && q.X0 <= mid && q.X1 >= mid)) continue;
            slabs.Add(new Slab { Name = "floor " + e.Id.IntegerValue + " (+" + Math.Round(top) + ")", Top = top, Soffit = dn != null ? ZE(dn.Origin.Z) : double.NaN, X0 = Math.Max(cx[0], cx0), X1 = Math.Min(cx[1], cx1), Cut = true });
        }

        // ---- bands (XA1): one per flight elevation range, a scissor pair counts once
        var bands = fl.GroupBy(f => Math.Round(f.El0) + "|" + Math.Round(f.El1)).Select(g => g.ToList()).OrderBy(g => g[0].El0).ToList();
        for (int i = 1; i < bands.Count; i++) if (bands[i][0].El0 < bands[i - 1][0].El1 - 1) notes.Add("bands overlap: " + string.Join("/", bands[i - 1].Select(f => f.Label)) + " and " + string.Join("/", bands[i].Select(f => f.Label)));
        foreach (var b in bands) if (b.Select(f => f.Risers + "x" + Num(f.RiserH)).Distinct().Count() > 1) issues.Add("LC: band +" + Math.Round(b[0].El0) + "..+" + Math.Round(b[0].El1) + ": the flights differ (" + string.Join(", ", b.Select(f => f.Label + " " + f.Risers + "R")) + "): check the model");
        foreach (var f in fl) if (Math.Abs(f.Risers * f.RiserH - (f.El1 - f.El0)) > Math.Max(1, f.Risers * 0.05)) issues.Add("LC: " + f.Label + " " + f.Risers + "R x " + Num(f.RiserH) + " <> " + Math.Round(f.El1 - f.El0) + ": check the model (do not edit it)");

        // ---- dims of the view
        var segs = new List<Seg>(); var dimsRaw = new List<Tuple<Dimension, bool, double, string>>();   // dim, vertical, line pos, signature
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension) continue;
            var ln = d.Curve as Line; if (ln == null) continue;
            bool vert = Math.Abs(ln.Direction.DotProduct(Up)) > 0.999, hor = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.999;
            if (!vert && !hor) continue;
            var cats = new List<string>(); foreach (Reference r in d.References) cats.Add(doc.GetElement(r.ElementId)?.Category?.Name ?? "?");
            double pos = vert ? VX(ln.Origin) : ZE(ln.Origin.Z);   // d.Origin throws on multi-segment dims
            var vals = new List<double>();
            if (d.NumberOfSegments > 1) foreach (DimensionSegment g in d.Segments) { vals.Add((g.Value ?? 0) * MM); segs.Add(new Seg { Id = d.Id.IntegerValue, Vert = vert, Val = (g.Value ?? 0) * MM, Line = pos, Pre = g.Prefix, Below = g.Below, Ovr = g.ValueOverride, Cats = cats }); }
            else { vals.Add((d.Value ?? 0) * MM); segs.Add(new Seg { Id = d.Id.IntegerValue, Vert = vert, Val = (d.Value ?? 0) * MM, Line = pos, Pre = d.Prefix, Below = d.Below, Ovr = d.ValueOverride, Cats = cats }); }
            dimsRaw.Add(Tuple.Create(d, vert, pos, (vert ? "V:" : "H:") + string.Join("|", vals.Select(x => Math.Round(x)))));
        }
        Func<string, string> norm = x => (x ?? "").Replace(" ", "").Replace("×", "x").ToLowerInvariant();
        var dimCheck = new List<object>();

        // XA1: one segment per band, live value + prefix + below
        foreach (var b in bands)
        {
            var f = b[0]; double val = f.El1 - f.El0; string pre = f.RiseText;
            var hit = segs.Where(x => x.Vert && Math.Abs(x.Val - val) <= tol).OrderByDescending(x => norm(x.Pre) == norm(pre) || norm(x.Ovr).StartsWith(norm(pre)) ? 1 : 0).FirstOrDefault();
            string st;
            if (hit == null) st = "missing";
            else
            {
                var bad = new List<string>();
                if (!string.IsNullOrEmpty(hit.Ovr)) bad.Add("Replace with text '" + hit.Ovr + "' (value not live): clear it, prefix '" + pre + "'");
                else if (norm(hit.Pre) != norm(pre)) bad.Add("prefix should be '" + pre + "' (now '" + (hit.Pre ?? "") + "')");
                if (!(hit.Below ?? "").ToUpper().Contains("EQUAL RISERS")) bad.Add("below text should be '(EQUAL RISERS)'");
                st = bad.Count == 0 ? "OK" : string.Join("; ", bad);
            }
            dimCheck.Add(new { Band = "+" + Math.Round(f.El0) + "..+" + Math.Round(f.El1), Flights = string.Join("/", b.Select(x => x.Label)), Rule = "XA1", Text = pre + Math.Round(val) + " / (EQUAL RISERS)", Status = st, DimId = hit?.Id });
            if (st != "OK") issues.Add("XA1: band +" + Math.Round(f.El0) + "..+" + Math.Round(f.El1) + ": " + st);
        }
        foreach (var x in segs.Where(x => x.Vert && Regex.IsMatch(norm(x.Pre) + norm(x.Ovr), @"x\d+r=")))
            if (!bands.Any(b => Math.Abs(b[0].El1 - b[0].El0 - x.Val) <= tol))
                issues.Add("XA1: dim " + x.Id + " segment " + Math.Round(x.Val) + " carries '" + (string.IsNullOrEmpty(x.Ovr) ? x.Pre : x.Ovr) + "' but matches no flight band (merged flights / missing witness): split it per band");

        // XA2 level chain
        var lvl = dimsRaw.Where(t => t.Item2 && t.Item1.References.Cast<Reference>().Any(r => doc.GetElement(r.ElementId) is Level)).Select(t => t.Item1.Id.IntegerValue).ToList();
        dimCheck.Add(new { Rule = "XA2", Text = "level chain (levels + plan-cut marks)", Status = lvl.Count > 0 ? "OK" : "missing", DimId = lvl.FirstOrDefault() });
        if (lvl.Count == 0) issues.Add("XA2: no level chain (level_dims_add)");

        // XA3 width chains (handrail references) per stair block
        var blocks = fl.GroupBy(f => f.S.Id.IntegerValue).Select(g => new { Ids = new List<int> { g.Key }, El0 = g.Min(f => f.El0), El1 = g.Max(f => f.El1) }).OrderBy(b => b.El0).ToList();
        var merged = new List<Tuple<string, double, double>>();
        foreach (var b in blocks)
        {
            var m = merged.FirstOrDefault(q => b.El0 < q.Item3 - 1 && b.El1 > q.Item2 + 1);
            if (m != null) { merged.Remove(m); merged.Add(Tuple.Create(m.Item1 + "+" + b.Ids[0], Math.Min(m.Item2, b.El0), Math.Max(m.Item3, b.El1))); }
            else merged.Add(Tuple.Create("stairs " + b.Ids[0], b.El0, b.El1));
        }
        foreach (var m in merged.OrderBy(q => q.Item2))
        {
            var w = dimsRaw.Where(t => !t.Item2 && t.Item3 > m.Item2 - 100 && t.Item3 < m.Item3 + 3000 && t.Item1.References.Cast<Reference>().Any(r => (doc.GetElement(r.ElementId)?.Category?.Name ?? "").Contains("Handrail") || (doc.GetElement(r.ElementId)?.Category?.Name ?? "").Contains("Rail"))).ToList();
            dimCheck.Add(new { Rule = "XA3", Text = "width chain wall-handrail-handrail-wall, " + m.Item1 + " (+" + Math.Round(m.Item2) + "..+" + Math.Round(m.Item3) + ")", Status = w.Count > 0 ? "OK " + w[0].Item4 : "missing", DimId = w.Select(t => (int?)t.Item1.Id.IntegerValue).FirstOrDefault() });
            if (w.Count == 0) issues.Add("XA3: " + m.Item1 + " (+" + Math.Round(m.Item2) + "..+" + Math.Round(m.Item3) + "): no width chain to the handrails");
        }

        // XA4 clear height + slab thickness, per half column
        var xa4 = new List<object>(); int bestFound = -1, bestTotal = 0; string bestHalf = null; List<string> bestMissing = null;
        foreach (var half in new[] { "left", "right" })
        {
            var hf = fl.Where(f => f.Half == half).ToList(); if (hf.Count == 0) continue;
            double col = (hf.Min(f => f.X0) + hf.Max(f => f.X1)) / 2;
            // cut slabs only (sample: a landing seen beyond does not stop the chain), one per elevation
            var ss = slabs.Where(q => q.Cut && !double.IsNaN(q.Soffit) && q.X0 <= col && q.X1 >= col).OrderBy(q => q.Top).GroupBy(q => Math.Round(q.Top / 10)).Select(g => g.First()).ToList();
            var exp = new List<Tuple<string, double>>();
            for (int i = 0; i < ss.Count; i++)
            {
                var a = ss.Skip(i + 1).FirstOrDefault(q => q.Soffit > ss[i].Top + 10); if (a == null) continue;
                exp.Add(Tuple.Create("clear " + ss[i].Name + " -> soffit " + a.Name, a.Soffit - ss[i].Top));
                exp.Add(Tuple.Create("thickness " + a.Name, a.Top - a.Soffit));
            }
            var used = new HashSet<Seg>(); var miss = new List<string>(); int found = 0;
            foreach (var e in exp)
            {
                var hit = segs.FirstOrDefault(x => x.Vert && !used.Contains(x) && Math.Abs(x.Val - e.Item2) <= Math.Max(tol, 2) && x.Cats.Any(c => c != "Levels"));
                if (hit != null) { used.Add(hit); found++; } else miss.Add(e.Item1 + " = " + Math.Round(e.Item2));
            }
            xa4.Add(new { Half = half, ColumnX = Math.Round(col), Expected = exp.Select(e => e.Item1 + " = " + Math.Round(e.Item2)).ToList(), Found = found, Missing = miss });
            if (found > bestFound) { bestFound = found; bestTotal = exp.Count; bestHalf = half; bestMissing = miss; }
        }
        if (bestHalf == null || bestFound < bestTotal)
            issues.Add("XA4: clear-height chain incomplete (best half '" + bestHalf + "' " + bestFound + "/" + bestTotal + "): " + string.Join("; ", (bestMissing ?? new List<string>()).Take(6)));

        // hygiene: Replace with text, 0 mm segments, duplicates
        foreach (var x in segs.Where(x => !string.IsNullOrEmpty(x.Ovr) && !Regex.IsMatch(norm(x.Ovr), @"x\d+r="))) issues.Add("dim " + x.Id + ": Replace with text '" + x.Ovr + "' (value not live)");
        foreach (var g in segs.Where(x => x.Val < 0.5).GroupBy(x => x.Id)) issues.Add("dim " + g.Key + ": " + g.Count() + " segment(s) of 0 mm (stray witness): rebuild it");
        foreach (var g in dimsRaw.GroupBy(t => t.Item4).Where(g => g.Count() > 1))
        {
            var l = g.OrderBy(t => t.Item3).ToList();
            for (int i = 1; i < l.Count; i++) if (Math.Abs(l[i].Item3 - l[i - 1].Item3) < 600) issues.Add("dims " + l[i - 1].Item1.Id.IntegerValue + " and " + l[i].Item1.Id.IntegerValue + ": same values " + g.Key.Substring(2) + " side by side (duplicate)");
        }

        // ---- XB1 run tags
        var tagged = new Dictionary<int, List<IndependentTag>>();
        foreach (var t in new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            try { foreach (var id in t.GetTaggedLocalElementIds()) { if (!tagged.ContainsKey(id.IntegerValue)) tagged[id.IntegerValue] = new List<IndependentTag>(); tagged[id.IntegerValue].Add(t); } } catch { }
        foreach (var f in fl)
        {
            var ts = tagged.ContainsKey(f.R.Id.IntegerValue) ? tagged[f.R.Id.IntegerValue] : new List<IndependentTag>();
            if (ts.Count != 1) issues.Add("XB1: " + f.Label + " (" + f.Half + ", " + f.Facing + ") has " + ts.Count + " run tags (one per seen flight)");
            foreach (var t in ts)
            {
                var els = Regex.Matches(t.TagText ?? "", @"EL\s*\+?(-?[\d.]+)").Cast<Match>().Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
                if (els.Count == 2 && (Math.Abs(els[0] - f.El0) > 1 || Math.Abs(els[1] - f.El1) > 1)) issues.Add("XB1: " + f.Label + " tag " + t.Id.IntegerValue + " reads '" + t.TagText + "' but the flight is +" + Math.Round(f.El0) + " -> +" + Math.Round(f.El1) + ": wrong tag type (tags_retype)");
                if (!(t.TagText ?? "").Contains(f.Risers + "R")) issues.Add("XB1: " + f.Label + " tag " + t.Id.IntegerValue + " reads '" + t.TagText + "': a section tag counts risers (" + f.Risers + "R)");
            }
        }

        // ---- XB3 spots on every cut landing / floor top
        var spotVals = new List<double>();
        foreach (var sd in new FilteredElementCollector(doc, v.Id).OfClass(typeof(SpotDimension)).Cast<SpotDimension>())
        {
            try { spotVals.Add(ZE(sd.Origin.Z)); } catch { }   // the point the spot measures (ValueString is empty on spots)
        }
        foreach (var t in slabs.Where(q => q.Cut).Select(q => Math.Round(q.Top)).Distinct().OrderBy(z => z))
        {
            bool ok = spotVals.Any(x => Math.Abs(x - t) <= 2);
            dimCheck.Add(new { Rule = "XB3", Text = "spot +" + t, Status = ok ? "OK" : "missing" });
            if (!ok) issues.Add("XB3: no spot elevation at +" + t);
        }

        // ---- XC riser numbers
        var ns = new Dictionary<int, NumberSystem>();
        foreach (var n in new FilteredElementCollector(doc, v.Id).OfClass(typeof(NumberSystem)).Cast<NumberSystem>())
            try { ns[n.NumberedElementId.HostElementId.IntegerValue] = n; } catch { }
        foreach (var g in fl.GroupBy(f => f.S.Id.IntegerValue))
            if (startNo.ContainsKey(g.Key)) { int k = startNo[g.Key]; foreach (var f in g.OrderBy(f => f.El0)) { f.FirstNo = k; k += f.Risers; } }
        int count = 0; var expFirst = new Dictionary<string, int>();
        foreach (var b in bands) { expFirst[b[0].Label] = count + 1; foreach (var f in b) expFirst[f.Label] = count + 1; count += b[0].Risers; }
        foreach (var f in fl)
        {
            bool has = ns.ContainsKey(f.R.Id.IntegerValue);
            if (f.Facing == "away" && !has) issues.Add("XC: " + f.Label + " (" + f.Half + ", risers seen) has no riser numbers");
            if (f.Facing == "toward" && has) issues.Add("XC: " + f.Label + " (" + f.Half + ", soffit seen) has riser numbers: only flights rising away from the viewer");
            if (has)
            {
                var dr = ns[f.R.Id.IntegerValue].LookupParameter("Display Rule")?.AsValueString();
                if (dr != null && dr != "Start and End") issues.Add("XC: " + f.Label + " numbers Display Rule '" + dr + "' (cross-section: 'Start and End')");
                if (f.FirstNo > 0 && f.FirstNo != expFirst[f.Label]) issues.Add("XC: " + f.Label + " first riser number " + f.FirstNo + ", by elevation over the core " + expFirst[f.Label] + ": compare with the plan numbers before changing");
            }
        }

        var res = new
        {
            View = v.Name, ViewId = v.Id.IntegerValue, Scale = v.Scale,
            Flights = fl.Select(f => new
            {
                f.Label, Id = f.R.Id.IntegerValue, Stairs = f.S.Id.IntegerValue, f.Half, f.Facing, f.State,
                FromEl = Math.Round(f.El0), ToEl = Math.Round(f.El1), TagText = "From EL +" + Math.Round(f.El0) + " To EL +" + Math.Round(f.El1) + " / " + Num(f.RiserH) + "mm x " + f.Risers + "R",
                RiseText = f.RiseText + Math.Round(f.El1 - f.El0), XMm = Math.Round(f.X0) + ".." + Math.Round(f.X1),
                RunTags = tagged.ContainsKey(f.R.Id.IntegerValue) ? tagged[f.R.Id.IntegerValue].Select(t => doc.GetElement(t.GetTypeId())?.Name + " '" + t.TagText + "'").ToList() : new List<string>(),
                RiserNumbers = ns.ContainsKey(f.R.Id.IntegerValue), FirstNumber = f.FirstNo > 0 ? (int?)f.FirstNo : null, FirstByElevation = expFirst[f.Label]
            }).ToList(),
            Slabs = slabs.OrderBy(q => q.Top).Select(q => new { q.Name, Top = Math.Round(q.Top), Soffit = Math.Round(q.Soffit), Thickness = Math.Round(q.Top - q.Soffit), X = Math.Round(q.X0) + ".." + Math.Round(q.X1), q.Cut }).ToList(),
            Dims = dimCheck, ClearHeights = xa4, Notes = notes, Issues = issues
        };
        var outPath = args.Value<string>("outPath");
        if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, JsonConvert.SerializeObject(res, Formatting.Indented));
        return res;
    }
}
