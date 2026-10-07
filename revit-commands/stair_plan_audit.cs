/* mcp-tool
{
  "description": "Read-only, stair core PLAN (SA1-SD): runs V1/V2/V3, treads from the model + formula, clear widths, walls, landings, expected dims/tags/paths status.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number",
        "description": "the stair core plan view"
      },
      "searchMm": {
        "type": "number",
        "description": "how far beyond the stair footprint to look for the end walls"
      },
      "toleranceMm": {
        "type": "number",
        "description": "value / position tolerance when matching existing dims, default 2"
      },
      "excludeStairIds": {
        "type": "array",
        "items": {
          "type": "integer"
        },
        "description": "stairs to leave out (e.g. a neighbouring stair partly in the crop)"
      },
      "outPath": {
        "type": "string",
        "description": "write the full result as JSON here"
      }
    },
    "required": [
      "viewId"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: stair core PLAN view, rules SA1-SD (drafting-stair-plan.md). Finds the host stairs (by component) seen in
//    the view and classifies every run against the view cut plane, going up: V1 = below the cut and under a cut run
//    (half, beyond the cut line), V2 = fully seen, V3 = cut (half, before the cut line). Per run, from the MODEL:
//    risers, treads, tread depth, length = treads x depth checked against the footprint, the formula prefix ('280mm x
//    14T = '), the run's clear width between the inner handrail edges (railing geometry) or the finish wall face.
//    Finds the walls around the core (finish face = face nearest the stair, outer face of the wall stack) on the 4
//    sides, wall-to-wall clear both ways, landing clear widths and depths, doors/windows in the core walls, the
//    visible grids. Lists the expected dims per rule with the existing dim segment that matches (value + position) and
//    whether it carries the CLEAR suffix / formula prefix; tags on runs, railings, doors/windows, landings, core
//    walls; spot elevations on landings and outside the core doors; stair paths (type family, UP/DOWN text); tread
//    numbers per run; and the project's most-used tag / spot / path types. Returns an Issues list per rule.
// Parameters:
//   excludeStairIds: stairs to leave out (e.g. a neighbouring stair outside the core, partly in the crop)
//   searchMm: how far beyond the stair footprint to look for the end walls (perpendicular to travel), default 3000;
//    side walls: 1000
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairPlanAudit
{
    const double MM = 304.8;
    static XYZ O, Rg, Up;
    static bool AlongX; // travel direction runs along the view's right direction
    static double VX(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double VY(XYZ p) { return (p - O).DotProduct(Up) * MM; }
    static double Al(XYZ p) { return AlongX ? VX(p) : VY(p); }
    static double Cr(XYZ p) { return AlongX ? VY(p) : VX(p); }

    class RunI
    {
        public StairsRun R; public Stairs S; public double Z0, Z1; public List<XYZ> Foot = new List<XYZ>(); public XYZ P0, P1;
        public string State, Label; public double X0, X1, Y0, Y1, A0, A1, C0, C1;
        public int Treads, Risers, ModelTreads; public bool FlushTop; public double Depth, RiserH, Len, FootLen;
        public double LA0, LA1; // the counted treads along the travel axis: first riser -> last riser
        public double InLow, InHigh; public string InLowBy, InHighBy; public int Lane = -1;
        public string Formula { get { return Num(Depth) + "mm x " + Treads + "T ="; } } // Revit adds the space before the value
    }
    class FinMark { public int Id; public string Type, Code; public double A, C; }
    class LandI { public StairsLanding L; public Stairs S; public double Z; public double A0, A1, C0, C1; public string WallSide; public double? Depth, Clear; public double ClrA, ClrB; public double? RailOuter; public double WallF; public string ClearFrom, ClearTo; }
    class WallI { public Wall W; public bool ParAlong; public double N0, N1, T0, T1; }
    class SideI { public string Key, Label, Kind; public double? Finish, Outer; public List<WallI> Stack = new List<WallI>(); }
    class Seg { public int DimId; public bool Cross; public double Val, Pos, Line; public string Prefix, Suffix, Below; }
    class Exp
    {
        public string Rule, Item; public bool Cross; public double From, To; public bool NeedClear; public string Prefix, Below;
        public string Status; public int? DimId; public string Found;
        public double Mm { get { return Math.Abs(To - From); } }
    }

    static string Num(double x) { return Math.Round(x, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }
    static double R0(double x) { return Math.Round(x); }
    static Parameter Bip(Element e, string name)
    {
        BuiltInParameter b; if (!Enum.TryParse(name, out b)) return null;
        try { return e.get_Parameter(b); } catch { return null; }
    }
    static List<XYZ> Pts(Element e, bool curves)
    {
        var res = new List<XYZ>(); GeometryElement ge = null;
        try { ge = e.get_Geometry(new Options { ComputeReferences = false }); } catch { }
        if (ge != null) Walk(ge, res, curves);
        return res;
    }
    static void Walk(GeometryElement ge, List<XYZ> res, bool curves)
    {
        foreach (var g in ge)
        {
            if (g is Solid s && s.Volume > 1e-9) { foreach (Edge ed in s.Edges) res.AddRange(ed.Tessellate()); }
            else if (g is GeometryInstance gi) Walk(gi.GetInstanceGeometry(), res, curves);
            else if (curves && g is Curve c) res.AddRange(c.Tessellate());
        }
    }
    static List<XYZ> LoopPts(CurveLoop cl)
    {
        var res = new List<XYZ>(); if (cl == null) return res;
        foreach (Curve c in cl) res.AddRange(c.Tessellate());
        return res;
    }
    static double Ov(double a0, double a1, double b0, double b1) { return Math.Min(a1, b1) - Math.Max(a0, b0); }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = doc.GetElement(new ElementId(args.Value<int>("viewId"))) as ViewPlan;
        if (v == null) return new { Error = "viewId must be a plan view" };
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection;
        double reachEnd = args.Value<double?>("searchMm") ?? 3000, reachSide = 1000, tol = args.Value<double?>("toleranceMm") ?? 2;
        var issues = new List<string>(); var notes = new List<string>();

        // ---- cut plane and view depth (absolute, internal feet)
        double cut = double.NaN, bottom = double.NegativeInfinity, lvZ = v.GenLevel != null ? v.GenLevel.ProjectElevation : 0;
        try
        {
            var vr = v.GetViewRange();
            var cl = doc.GetElement(vr.GetLevelId(PlanViewPlane.CutPlane)) as Level;
            if (cl != null) cut = cl.ProjectElevation + vr.GetOffset(PlanViewPlane.CutPlane);
            var dl = doc.GetElement(vr.GetLevelId(PlanViewPlane.ViewDepthPlane)) as Level;
            if (dl != null) bottom = dl.ProjectElevation + vr.GetOffset(PlanViewPlane.ViewDepthPlane);
        }
        catch { }
        if (double.IsNaN(cut)) { cut = lvZ + 1200 / MM; notes.Add("cut plane not readable: assumed level + 1200"); }

        // ---- stairs and runs
        var stairs = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().ToElements();
        var legacy = stairs.Where(e => !(e is Stairs)).Select(e => e.Id.IntegerValue).ToList();
        if (legacy.Count > 0) issues.Add("Stairs not by component (not audited): " + string.Join(", ", legacy));
        var excl = args["excludeStairIds"] != null ? new HashSet<int>(((Newtonsoft.Json.Linq.JArray)args["excludeStairIds"]).Select(x => (int)x)) : new HashSet<int>();
        var sts = stairs.OfType<Stairs>().Where(s => !excl.Contains(s.Id.IntegerValue)).ToList();
        if (excl.Count > 0) notes.Add("stairs left out: " + string.Join(", ", excl));
        if (sts.Count == 0) return new { View = v.Name, Error = "no host stairs (by component) in this view - stairs in a link are not audited", Legacy = legacy };
        var runs = new List<RunI>(); var lands = new List<LandI>();
        var stBase = new Dictionary<int, double>();
        foreach (var s in sts)
        {
            double sb = s.BaseElevation; Level bl = null;
            var pl = Bip(s, "STAIRS_BASE_LEVEL_PARAM"); if (pl != null && pl.StorageType == StorageType.ElementId) bl = doc.GetElement(pl.AsElementId()) as Level;
            if (bl != null) { var po = Bip(s, "STAIRS_BASE_OFFSET"); sb = bl.ProjectElevation + (po != null ? po.AsDouble() : 0); }
            stBase[s.Id.IntegerValue] = sb;
            foreach (var rid in s.GetStairsRuns())
            {
                var r = doc.GetElement(rid) as StairsRun; if (r == null) continue;
                var ri = new RunI { R = r, S = s, Z0 = sb + r.BaseElevation, Z1 = sb + r.TopElevation, ModelTreads = r.ActualTreadsNumber, Risers = r.ActualRisersNumber, Depth = s.ActualTreadDepth * MM, RiserH = s.ActualRiserHeight * MM };
                // C (user rule 2026-10-07): the top tread of a run that sits at the landing / floor level belongs to the landing, it is not counted
                ri.FlushTop = ri.ModelTreads >= ri.Risers;
                ri.Treads = ri.FlushTop ? ri.Risers - 1 : ri.ModelTreads;
                try { ri.Foot = LoopPts(r.GetFootprintBoundary()); } catch { }
                try { var pc = r.GetStairsPath().Cast<Curve>().ToList(); ri.P0 = pc.First().GetEndPoint(0); ri.P1 = pc.Last().GetEndPoint(1); } catch { }
                if (ri.Foot.Count == 0) { var bb = r.get_BoundingBox(null); if (bb != null) ri.Foot = new List<XYZ> { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z) }; }
                if (ri.Foot.Count == 0) continue;
                ri.X0 = ri.Foot.Min(VX); ri.X1 = ri.Foot.Max(VX); ri.Y0 = ri.Foot.Min(VY); ri.Y1 = ri.Foot.Max(VY);
                double eps = 10 / MM;
                ri.State = ri.Z0 >= cut - eps ? "above" : ri.Z1 <= cut + eps ? (ri.Z1 < bottom ? "below view depth" : "below") : "cut";
                runs.Add(ri);
            }
            foreach (var lid in s.GetStairsLandings())
            {
                var l = doc.GetElement(lid) as StairsLanding; if (l == null) continue;
                var li = new LandI { L = l, S = s, Z = sb + l.BaseElevation };
                if (li.Z > cut + 10 / MM) continue; // below the view depth: kept if the stair is shown (filtered below)
                List<XYZ> fp = new List<XYZ>(); try { fp = LoopPts(l.GetFootprintBoundary()); } catch { }
                if (fp.Count == 0) continue;
                li.A0 = fp.Min(VX); li.A1 = fp.Max(VX); li.C0 = fp.Min(VY); li.C1 = fp.Max(VY); // view X/Y for now, swapped below
                lands.Add(li);
            }
        }
        // Revit draws every stair collected in the view with all its components, even below the view depth.
        // Seen = not covered by a higher run / landing: below runs under a cut run are V1 (beyond the cut),
        // below runs under nothing are V2, below runs / landings under a higher run or landing are hidden.
        foreach (var r in runs.Where(r => r.State == "below view depth")) r.State = "below";
        var cutRuns = runs.Where(r => r.State == "cut").ToList();
        var cover = new List<double[]>(); // footprints of things already seen above, highest first
        Func<double, double, double, double, bool> covered = (x0, x1, y0, y1) =>
        {
            double area = (x1 - x0) * (y1 - y0);
            return cover.Any(c => { double ox = Ov(x0, x1, c[0], c[1]), oy = Ov(y0, y1, c[2], c[3]); return ox > 0 && oy > 0 && ox * oy > 0.3 * area; });
        };
        var belowItems = runs.Where(r => r.State == "below").Select(r => new { Z = r.Z1, Run = r, Land = (LandI)null })
            .Concat(lands.Select(l => new { Z = l.Z, Run = (RunI)null, Land = l })).OrderByDescending(x => x.Z).ToList();
        var hiddenLands = new HashSet<LandI>();
        foreach (var it in belowItems)
        {
            if (it.Run != null)
            {
                var r = it.Run;
                if (covered(r.X0, r.X1, r.Y0, r.Y1)) { r.State = "hidden (under a higher run)"; continue; }
                double area = (r.X1 - r.X0) * (r.Y1 - r.Y0);
                bool under = cutRuns.Any(c => { double ox = Ov(r.X0, r.X1, c.X0, c.X1), oy = Ov(r.Y0, r.Y1, c.Y0, c.Y1); return ox > 0 && oy > 0 && ox * oy > 0.3 * area; });
                r.State = under ? "beyond cut" : "full";
                if (r.Z1 < bottom) notes.Add("run " + r.R.Id.IntegerValue + " is below the view depth but drawn with its stairs " + r.S.Id.IntegerValue);
                cover.Add(new[] { r.X0, r.X1, r.Y0, r.Y1 });
            }
            else
            {
                var l = it.Land;
                if (covered(l.A0, l.A1, l.C0, l.C1)) { hiddenLands.Add(l); continue; }
                cover.Add(new[] { l.A0, l.A1, l.C0, l.C1 });
            }
        }
        lands = lands.Where(l => !hiddenLands.Contains(l)).ToList();
        var vis = runs.Where(r => r.State == "beyond cut" || r.State == "full" || r.State == "cut").OrderBy(r => r.Z0).ToList();
        if (vis.Count == 0) return new { View = v.Name, Error = "no stair run is seen in this view (all above the cut or below the view depth)", Runs = runs.Select(r => new { r.R.Id.IntegerValue, r.State }) };
        foreach (var g in vis.GroupBy(r => r.State))
        {
            string b = g.Key == "beyond cut" ? "V1" : g.Key == "full" ? "V2" : "V3"; int k = 0;
            foreach (var r in g) r.Label = g.Count() == 1 ? b : b + (char)('a' + k++);
        }

        // ---- travel axis
        int nx = 0, ny = 0;
        foreach (var r in vis.Where(r => r.P0 != null))
        {
            var d = r.P1 - r.P0; double dx = Math.Abs(d.DotProduct(Rg)), dy = Math.Abs(d.DotProduct(Up));
            if (dx >= dy) nx++; else ny++;
            if (Math.Min(dx, dy) > 0.02 * Math.Max(dx, dy)) notes.Add("run " + r.R.Id.IntegerValue + " is not parallel to the view axes: dims of that run need manual placement");
        }
        AlongX = nx >= ny;
        if (nx > 0 && ny > 0) notes.Add("runs go both ways (L-shaped / winder): the audit uses the majority direction; check the other runs by hand");
        foreach (var r in runs)
        {
            r.A0 = AlongX ? r.X0 : r.Y0; r.A1 = AlongX ? r.X1 : r.Y1; r.C0 = AlongX ? r.Y0 : r.X0; r.C1 = AlongX ? r.Y1 : r.X1;
            r.FootLen = r.A1 - r.A0; r.Len = r.Treads * r.Depth;
            // counted part starts at the bottom end (first riser); a flush top tread is left to the landing
            bool upPlus = r.P0 == null || Al(r.P1) >= Al(r.P0);
            if (upPlus) { r.LA0 = r.A0; r.LA1 = r.A0 + r.Len; } else { r.LA1 = r.A1; r.LA0 = r.A1 - r.Len; }
        }
        foreach (var l in lands)
        {
            double x0 = l.A0, x1 = l.A1, y0 = l.C0, y1 = l.C1;
            l.A0 = AlongX ? x0 : y0; l.A1 = AlongX ? x1 : y1; l.C0 = AlongX ? y0 : x0; l.C1 = AlongX ? y1 : x1;
        }
        string lowC = AlongX ? "bottom" : "left", highC = AlongX ? "top" : "right", lowA = AlongX ? "left" : "bottom", highA = AlongX ? "right" : "top";
        Func<RunI, string> upDir = r =>
        {
            if (r.P0 == null) return "?";
            double d = Al(r.P1) - Al(r.P0);
            return d >= 0 ? (AlongX ? "→ right" : "↑ up") : (AlongX ? "← left" : "↓ down");
        };

        // core rectangle (along / cross) from the stairs that have a visible run
        var visStairs = new HashSet<int>(vis.Select(r => r.S.Id.IntegerValue));
        var coreParts = runs.Where(r => visStairs.Contains(r.S.Id.IntegerValue) && r.State != "below view depth").Select(r => new[] { r.A0, r.A1, r.C0, r.C1 })
            .Concat(lands.Select(l => new[] { l.A0, l.A1, l.C0, l.C1 })).ToList();
        double kA0 = coreParts.Min(p => p[0]), kA1 = coreParts.Max(p => p[1]), kC0 = coreParts.Min(p => p[2]), kC1 = coreParts.Max(p => p[3]);

        // ---- walls around the core
        var walls = new List<WallI>();
        foreach (var w in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Wall)).Cast<Wall>())
        {
            var ln = (w.Location as LocationCurve)?.Curve as Line; if (ln == null) continue;
            double dx = Math.Abs(ln.Direction.DotProduct(Rg)), dy = Math.Abs(ln.Direction.DotProduct(Up));
            if (dx < 0.999 && dy < 0.999) continue;
            var bb = w.get_BoundingBox(null); if (bb == null) continue;
            var bp = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z) };
            if (Ov(bp.Min(Al), bp.Max(Al), kA0 - reachEnd, kA1 + reachEnd) <= 0 || Ov(bp.Min(Cr), bp.Max(Cr), kC0 - reachEnd, kC1 + reachEnd) <= 0) continue;
            var pts = Pts(w, false); if (pts.Count == 0) continue;
            bool parAlong = AlongX ? dx >= 0.999 : dy >= 0.999;
            var wi = new WallI { W = w, ParAlong = parAlong };
            if (parAlong) { wi.N0 = pts.Min(Cr); wi.N1 = pts.Max(Cr); wi.T0 = pts.Min(Al); wi.T1 = pts.Max(Al); }
            else { wi.N0 = pts.Min(Al); wi.N1 = pts.Max(Al); wi.T0 = pts.Min(Cr); wi.T1 = pts.Max(Cr); }
            walls.Add(wi);
        }
        Func<string, string, bool, bool, SideI> side = (key, label, parAlong, low) =>
        {
            double n0 = parAlong ? kC0 : kA0, n1 = parAlong ? kC1 : kA1, t0 = parAlong ? kA0 : kC0, t1 = parAlong ? kA1 : kC1, reach = parAlong ? reachSide : reachEnd;
            var si = new SideI { Key = key, Label = label, Kind = parAlong ? "side wall (parallel to travel)" : "end wall (perpendicular to travel)" };
            var mine = walls.Where(w => w.ParAlong == parAlong && Ov(w.T0, w.T1, t0, t1) >= 0.3 * Math.Min(t1 - t0, w.T1 - w.T0)).ToList();
            var cand = mine.Where(w => low ? (w.N1 <= n0 + 30 && w.N1 >= n0 - reach) : (w.N0 >= n1 - 30 && w.N0 <= n1 + reach)).ToList();
            if (cand.Count == 0) return si;
            double fin = low ? cand.Max(w => w.N1) : cand.Min(w => w.N0);
            si.Stack = cand.Where(w => Math.Abs((low ? w.N1 : w.N0) - fin) < 5).ToList();
            double outer = low ? si.Stack.Min(w => w.N0) : si.Stack.Max(w => w.N1);
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var w in mine.Where(w => !si.Stack.Contains(w)).ToList())
                    if (low ? (Math.Abs(w.N1 - outer) < 5 && w.N0 < outer) : (Math.Abs(w.N0 - outer) < 5 && w.N1 > outer))
                    { si.Stack.Add(w); outer = low ? Math.Min(outer, w.N0) : Math.Max(outer, w.N1); grew = true; }
            }
            si.Finish = fin; si.Outer = outer;
            return si;
        };
        var sLowC = side("lowCross", lowC, true, true); var sHighC = side("highCross", highC, true, false);
        var sLowA = side("lowAlong", lowA, false, true); var sHighA = side("highAlong", highA, false, false);
        var sides = new[] { sLowC, sHighC, sLowA, sHighA };
        foreach (var s in sides.Where(s => s.Finish == null)) notes.Add("no host wall found on the " + s.Label + " (" + s.Kind + "): open side, or walls in a link");
        var coreWallIds = new HashSet<int>(sides.SelectMany(s => s.Stack).Select(w => w.W.Id.IntegerValue));

        // ---- railings: geometry points (along, cross)
        var railPts = new Dictionary<int, List<double[]>>();
        foreach (var rl in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Railing)).Cast<Railing>())
        {
            var els = new List<Element> { rl };
            try { var tr = doc.GetElement(rl.TopRail); if (tr != null) els.Add(tr); } catch { }
            try { foreach (var h in rl.GetHandRails()) { var he = doc.GetElement(h); if (he != null) els.Add(he); } } catch { }
            var pts = els.SelectMany(e => Pts(e, true)).Select(p => new[] { Al(p), Cr(p) }).ToList();
            if (pts.Count == 0) continue;
            if (Ov(pts.Min(p => p[0]), pts.Max(p => p[0]), kA0 - 300, kA1 + 300) <= 0 || Ov(pts.Min(p => p[1]), pts.Max(p => p[1]), kC0 - 300, kC1 + 300) <= 0) continue;
            railPts[rl.Id.IntegerValue] = pts;
        }
        var railIds = new HashSet<int>(railPts.Keys);
        // handrail ends as drawn: lines of the top rail / handrails across the travel (visible styles only) = what a hand dim snaps to
        var railEnds = new Dictionary<int, List<double[]>>(); // railing id -> {A, C0, C1}
        var alongDir = AlongX ? Rg : Up;
        foreach (var rid in railIds)
        {
            var rl = (Railing)doc.GetElement(new ElementId(rid)); var parts = new List<Element>();
            try { var tr = doc.GetElement(rl.TopRail); if (tr != null) parts.Add(tr); } catch { }
            try { foreach (var h in rl.GetHandRails()) { var he = doc.GetElement(h); if (he != null) parts.Add(he); } } catch { }
            var list = new List<double[]>();
            foreach (var e in parts)
            {
                GeometryElement ge = null; try { ge = e.get_Geometry(new Options { View = v }); } catch { }
                var stack = new Stack<GeometryElement>(); if (ge != null) stack.Push(ge);
                while (stack.Count > 0)
                    foreach (var g in stack.Pop())
                    {
                        if (g is GeometryInstance gi) { stack.Push(gi.GetInstanceGeometry()); continue; }
                        if (!(g is Line ln)) continue;
                        if (g.GraphicsStyleId != ElementId.InvalidElementId)
                        {
                            var gs = doc.GetElement(g.GraphicsStyleId) as GraphicsStyle; var gc = gs?.GraphicsStyleCategory;
                            if (gc != null && (v.GetCategoryHidden(gc.Id) || (gc.Parent != null && v.GetCategoryHidden(gc.Parent.Id)))) continue; // e.g. <Above> Top Rails
                        }
                        if (Math.Abs(ln.Direction.DotProduct(alongDir)) > 0.01) continue; // only lines across the travel
                        double c0 = Cr(ln.GetEndPoint(0)), c1 = Cr(ln.GetEndPoint(1));
                        list.Add(new[] { Al(ln.GetEndPoint(0)), Math.Min(c0, c1), Math.Max(c0, c1) });
                    }
            }
            if (list.Count > 0) railEnds[rid] = list;
        }

        // ---- lanes: visible runs grouped by cross overlap
        var lanes = new List<List<RunI>>();
        foreach (var r in vis.OrderBy(r => r.C0))
        {
            var ln = lanes.FirstOrDefault(l => l.Any(x => Ov(x.C0, x.C1, r.C0, r.C1) > 0.5 * Math.Min(x.C1 - x.C0, r.C1 - r.C0)));
            if (ln == null) { ln = new List<RunI>(); lanes.Add(ln); }
            ln.Add(r);
        }
        lanes = lanes.OrderBy(l => l.Min(r => r.C0)).ToList();
        for (int i = 0; i < lanes.Count; i++) foreach (var r in lanes[i]) r.Lane = i;

        // run clear width: inner handrail edges, else finish wall face, else run edge
        foreach (var r in vis)
        {
            double mid = (r.C0 + r.C1) / 2; r.InLow = r.C0; r.InHigh = r.C1; r.InLowBy = "run edge"; r.InHighBy = "run edge";
            foreach (var kv in railPts)
            {
                var near = kv.Value.Where(p => p[0] > r.A0 + 50 && p[0] < r.A1 - 50).ToList();
                var lo = near.Where(p => p[1] >= r.C0 - 400 && p[1] < mid).ToList();
                var hi = near.Where(p => p[1] > mid && p[1] <= r.C1 + 400).ToList();
                if (lo.Count > 0) { double e = lo.Max(p => p[1]); if (r.InLowBy == "run edge" || r.InLowBy.StartsWith("wall") || e > r.InLow) { r.InLow = e; r.InLowBy = "handrail of railing " + kv.Key; } }
                if (hi.Count > 0) { double e = hi.Min(p => p[1]); if (r.InHighBy == "run edge" || r.InHighBy.StartsWith("wall") || e < r.InHigh) { r.InHigh = e; r.InHighBy = "handrail of railing " + kv.Key; } }
            }
            // user rule 2026-10-07: railing edge, else the step (run) edge, else the finish wall face (only when the run reaches past the wall)
            if (r.InLowBy == "run edge" && sLowC.Finish != null && r.C0 < sLowC.Finish.Value) { r.InLow = sLowC.Finish.Value; r.InLowBy = "wall finish (" + lowC + ")"; }
            if (r.InHighBy == "run edge" && sHighC.Finish != null && r.C1 > sHighC.Finish.Value) { r.InHigh = sHighC.Finish.Value; r.InHighBy = "wall finish (" + highC + ")"; }
            if (r.InLowBy == "run edge" || r.InHighBy == "run edge") notes.Add("SA1: run " + r.Label + ": no railing on one side, clear width measured to the step edge");
        }

        // landings: depth (riser line -> end wall) and clear (inner handrail / wall -> wall / handrail)
        double coreMidA = (kA0 + kA1) / 2;
        foreach (var l in lands)
        {
            bool low = (l.A0 + l.A1) / 2 < coreMidA; var sw = low ? sLowA : sHighA; l.WallSide = sw.Label;
            if (sw.Finish == null) { l.ClearFrom = "no end wall found"; continue; }
            double f = sw.Finish.Value, edge = low ? l.A1 : l.A0; l.Depth = Math.Abs(edge - f);
            // clear (user rule 2026-10-07): each bound = railing edge, else the landing / step edge, else the finish wall face
            Func<double[], bool> inBand = p => p[1] > l.C0 + 300 && p[1] < l.C1 - 300 && p[0] > l.A0 - 50 && p[0] < l.A1 + 50;
            // run side = the centre railing (inside the well along the runs); wall side = the first railing crossing the well axis toward the wall
            double w0 = l.C0 + 300, w1 = l.C1 - 300;
            var centreIds = new List<int>();
            if (lanes.Count >= 2)
            {
                w0 = PickRun(lanes[0]).InHigh - 20; w1 = PickRun(lanes[1]).InLow + 20;
                double rA0 = vis.Min(x => Math.Min(x.A0, x.A1)) + 300, rA1 = vis.Max(x => Math.Max(x.A0, x.A1)) - 300;
                centreIds = railPts.Where(kv => kv.Value.Any(p => p[1] >= w0 && p[1] <= w1 && p[0] > rA0 && p[0] < rA1)).Select(kv => kv.Key).ToList();
            }
            else centreIds = railPts.Where(kv => kv.Value.Any(inBand) && kv.Value.Any(p => p[1] > w0 && p[1] < w1 && (low ? p[0] > l.A1 + 50 : p[0] < l.A0 - 50))).Select(kv => kv.Key).ToList();
            double wb0 = w0, wb1 = w1;
            Func<double[], bool> crossesWell = e => e[2] >= wb0 && e[1] <= wb1; // the handrail end line crosses the well axis
            // (1) handrail ends as drawn (what a hand dim snaps to), (2) else railing geometry points
            var cEnds = railEnds.Where(kv => centreIds.Contains(kv.Key)).SelectMany(kv => kv.Value)
                                .Where(e => crossesWell(e) && (low ? e[0] < edge - 5 && e[0] > f + 150 : e[0] > edge + 5 && e[0] < f - 150)).ToList();
            double b = edge; l.ClearTo = "landing edge (no railing on the run side)";
            if (cEnds.Count > 0) { b = low ? cEnds.Max(e => e[0]) : cEnds.Min(e => e[0]); l.ClearTo = "end of the centre handrail"; } // the first handrail end line past the last riser, as the user dims it
            else
            {
                var centre = railPts.Where(kv => centreIds.Contains(kv.Key)).SelectMany(kv => kv.Value)
                                    .Where(p => p[1] >= w0 && p[1] <= w1 && p[0] > l.A0 - 50 && p[0] < l.A1 + 50 && Math.Abs(p[0] - f) > 150).ToList();
                if (centre.Count > 0) { b = low ? centre.Min(p => p[0]) : centre.Max(p => p[0]); l.ClearTo = "centre railing (geometry)"; }
            }
            // wall side: the nearest handrail line crossing the well axis between b and the wall, else the landing edge, else the finish wall face
            var wEnds = railEnds.Where(kv => !centreIds.Contains(kv.Key)).SelectMany(kv => kv.Value)
                                .Where(e => crossesWell(e) && (low ? e[0] < b - 50 && e[0] >= f - 5 : e[0] > b + 50 && e[0] <= f + 5)).ToList();
            double landOuter = low ? l.A0 : l.A1, a;
            if (wEnds.Count > 0)
            {
                a = low ? wEnds.Max(e => e[0]) : wEnds.Min(e => e[0]); l.ClearFrom = "handrail / guard rail edge";
                // the other edge of that handrail (its width), so the chain can close on the finish wall (user, 2026-10-07: 49 | 80 | 1600, 1393 | 80 | 2618)
                double aa = a; var beyond = wEnds.Where(e => low ? e[0] < aa - 5 : e[0] > aa + 5).ToList();
                if (beyond.Count > 0) l.RailOuter = low ? beyond.Max(e => e[0]) : beyond.Min(e => e[0]);
            }
            else if (Math.Abs(landOuter - f) > 5 && (low ? landOuter > f : landOuter < f)) { a = landOuter; l.ClearFrom = "landing edge"; }
            else { a = f; l.ClearFrom = "wall finish (" + sw.Label + ")"; }
            l.Clear = Math.Abs(b - a); l.ClrA = a; l.ClrB = b; l.WallF = f;
            if (l.ClearTo.StartsWith("landing edge")) notes.Add("SA3: landing " + l.L.Id.IntegerValue + ": no railing on the run side, clear measured from the landing edge");
        }

        // ---- expected dims
        var exp = new List<Exp>();
        // SA1: across, chain + overall (placed outside an end wall)
        for (int i = 0; i < lanes.Count; i++)
        {
            var r = PickRun(lanes[i]);
            exp.Add(new Exp { Rule = "SA1", Item = "clear width of run " + string.Join("/", lanes[i].Select(x => x.Label)), Cross = true, From = r.InLow, To = r.InHigh, NeedClear = true });
            if (i + 1 < lanes.Count) exp.Add(new Exp { Rule = "SA1", Item = "railing / well between lanes " + (i + 1) + " and " + (i + 2), Cross = true, From = r.InHigh, To = PickRun(lanes[i + 1]).InLow });
        }
        if (sLowC.Finish != null) exp.Add(new Exp { Rule = "SA1", Item = "wall finish -> inner handrail (" + lowC + ")", Cross = true, From = sLowC.Finish.Value, To = PickRun(lanes[0]).InLow });
        if (sHighC.Finish != null) exp.Add(new Exp { Rule = "SA1", Item = "inner handrail -> wall finish (" + highC + ")", Cross = true, From = PickRun(lanes.Last()).InHigh, To = sHighC.Finish.Value });
        // overall clear across (user rule 2026-10-07): bounded like every clear width (handrail edge > step edge > finish wall), not wall to wall
        exp.Add(new Exp { Rule = "SA1", Item = "overall clear across (" + PickRun(lanes[0]).InLowBy + " -> " + PickRun(lanes.Last()).InHighBy + ")", Cross = true, From = PickRun(lanes[0]).InLow, To = PickRun(lanes.Last()).InHigh });
        exp.RemoveAll(e => e.Mm < 1);
        // SA2: along, per lane
        var laneOut = new List<object>();
        double coreMidC = (kC0 + kC1) / 2;
        for (int i = 0; i < lanes.Count; i++)
        {
            var r = PickRun(lanes[i]);
            var v1 = lanes[i].FirstOrDefault(x => x.State == "beyond cut"); var v3 = lanes[i].FirstOrDefault(x => x.State == "cut");
            bool same = v1 == null || v3 == null || (v1.Treads == v3.Treads && Math.Abs(v1.Depth - v3.Depth) < 0.5 && Math.Abs(v1.A0 - v3.A0) <= 5 && Math.Abs(v1.A1 - v3.A1) <= 5);
            if (!same) issues.Add("SA2: lane " + (i + 1) + ": V1 (" + v1.Treads + "T x " + Num(v1.Depth) + ") and V3 (" + v3.Treads + "T x " + Num(v3.Depth) + ") differ: dim follows V3, V1 goes to 'Cần xem'");
            string dimSide = (r.C0 + r.C1) / 2 < coreMidC ? lowC : highC;
            exp.Add(new Exp { Rule = "SA2", Item = "run length " + string.Join("/", lanes[i].Select(x => x.Label)) + " (outside the " + dimSide + " wall, first riser -> last riser)", Cross = false, From = r.LA0, To = r.LA1, Prefix = r.Formula, Below = "(EQUAL TREADS)" });
            if (sLowA.Finish != null) exp.Add(new Exp { Rule = "SA2", Item = "wall finish (" + lowA + ") -> run " + r.Label, Cross = false, From = sLowA.Finish.Value, To = r.LA0 });
            if (sHighA.Finish != null) exp.Add(new Exp { Rule = "SA2", Item = "run " + r.Label + " -> wall finish (" + highA + ")", Cross = false, From = r.LA1, To = sHighA.Finish.Value });
            laneOut.Add(new { Lane = i + 1, Runs = string.Join("/", lanes[i].Select(x => x.Label)), DimRun = r.Label, DimSide = dimSide, Formula = r.Formula + " " + R0(r.Len), V1V3Same = same });
        }
        if (sLowA.Finish != null && sHighA.Finish != null) exp.Add(new Exp { Rule = "SA2", Item = "wall to wall (end walls)", Cross = false, From = sLowA.Finish.Value, To = sHighA.Finish.Value });
        // SA3
        foreach (var l in lands.Where(l => l.Clear != null))
        {
            exp.Add(new Exp { Rule = "SA3", Item = "landing " + l.L.Id.IntegerValue + " clear (" + l.ClearFrom + " -> " + l.ClearTo + ")", Cross = false, From = l.ClrA, To = l.ClrB });
            // the same chain closes on the finish wall: handrail width, then the gap to the wall
            double toWallFrom = l.ClrA;
            if (l.RailOuter != null) { exp.Add(new Exp { Rule = "SA3", Item = "landing " + l.L.Id.IntegerValue + " handrail width (same chain)", Cross = false, From = l.ClrA, To = l.RailOuter.Value }); toWallFrom = l.RailOuter.Value; }
            if (Math.Abs(l.WallF - toWallFrom) > 1) exp.Add(new Exp { Rule = "SA3", Item = "landing " + l.L.Id.IntegerValue + " handrail -> wall finish (same chain)", Cross = false, From = toWallFrom, To = l.WallF });
        }
        foreach (var l in lands.Where(l => l.Depth != null && !exp.Any(e => e.Rule == "SA2" && Math.Abs(e.Mm - l.Depth.Value) < 1)))
        {
            double f = (l.WallSide == lowA ? sLowA : sHighA).Finish.Value, edge = l.WallSide == lowA ? l.A1 : l.A0;
            exp.Add(new Exp { Rule = "SA2", Item = "landing " + l.L.Id.IntegerValue + " depth to the wall finish", Cross = false, From = edge, To = f });
        }
        // SA3: the floor landing (end without a stair landing): railing end -> end wall finish
        var landSides = new HashSet<string>(lands.Select(l => l.WallSide));
        foreach (var sw in new[] { sLowA, sHighA }.Where(x => x.Finish != null && !landSides.Contains(x.Label)))
        {
            bool low = sw == sLowA; double f = sw.Finish.Value;
            var pts = railPts.Values.SelectMany(p => p).Where(p => p[1] > kC0 + 300 && p[1] < kC1 - 300 && (low ? p[0] > f + 150 : p[0] < f - 150)).ToList();
            if (pts.Count == 0) continue;
            double b = low ? pts.Min(p => p[0]) : pts.Max(p => p[0]);
            exp.Add(new Exp { Rule = "SA3", Item = "floor landing clear (railing end -> wall finish " + sw.Label + ")", Cross = false, From = f, To = b });
        }
        // SA4: wall thickness (finish -> outer face) on each side with walls
        foreach (var s in sides.Where(s => s.Finish != null && s.Outer != null && Math.Abs(s.Outer.Value - s.Finish.Value) > 1))
            exp.Add(new Exp { Rule = "SA4", Item = "wall " + s.Label + " (finish -> outer face)", Cross = s.Key.EndsWith("Cross"), From = s.Finish.Value, To = s.Outer.Value });
        exp.RemoveAll(e => e.Mm < 1);

        // ---- existing dims (segments)
        var segs = new List<Seg>();
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension) continue;
            var ln = d.Curve as Line; if (ln == null) continue;
            double da = Math.Abs(AlongX ? ln.Direction.DotProduct(Rg) : ln.Direction.DotProduct(Up)), dc = Math.Abs(AlongX ? ln.Direction.DotProduct(Up) : ln.Direction.DotProduct(Rg));
            if (da < 0.999 && dc < 0.999) continue;
            bool cross = dc >= 0.999;
            if (d.NumberOfSegments > 1)
                foreach (DimensionSegment g in d.Segments)
                    segs.Add(new Seg { DimId = d.Id.IntegerValue, Cross = cross, Val = (g.Value ?? 0) * MM, Pos = cross ? Cr(g.Origin) : Al(g.Origin), Line = cross ? Al(g.Origin) : Cr(g.Origin), Prefix = g.Prefix, Suffix = g.Suffix, Below = g.Below });
            else segs.Add(new Seg { DimId = d.Id.IntegerValue, Cross = cross, Val = (d.Value ?? 0) * MM, Pos = cross ? Cr(d.Origin) : Al(d.Origin), Line = cross ? Al(d.Origin) : Cr(d.Origin), Prefix = d.Prefix, Suffix = d.Suffix, Below = d.Below });
        }
        Func<string, string> norm = s => (s ?? "").Replace(" ", "").Replace("×", "x").ToLowerInvariant();
        foreach (var e in exp)
        {
            double mid = (e.From + e.To) / 2;
            var hit = segs.Where(s => s.Cross == e.Cross && Math.Abs(s.Val - e.Mm) <= tol && Math.Abs(s.Pos - mid) <= 50).ToList();
            if (hit.Count == 0 && !e.NeedClear && e.Prefix == null)
            {   // the same span split on one dim line, e.g. a grid inside the chain or handrail | well | handrail
                double lo = Math.Min(e.From, e.To), hi = Math.Max(e.From, e.To);
                var split = segs.Where(s => s.Cross == e.Cross && s.Pos - s.Val / 2 >= lo - tol && s.Pos + s.Val / 2 <= hi + tol).GroupBy(s => s.DimId)
                    .FirstOrDefault(g => g.Count() > 1 && Math.Abs(g.Sum(s => s.Val) - e.Mm) <= tol * g.Count() && Math.Abs(g.Min(s => s.Pos - s.Val / 2) - lo) <= tol + 1 && Math.Abs(g.Max(s => s.Pos + s.Val / 2) - hi) <= tol + 1);
                if (split != null) { e.DimId = split.Key; e.Found = string.Join(" | ", split.OrderBy(s => s.Pos).Select(s => R0(s.Val))); e.Status = "OK (split)"; continue; }
            }
            if (hit.Count == 0) { e.Status = "missing"; issues.Add(e.Rule + ": missing dim '" + e.Item + "' " + R0(e.Mm)); continue; }
            var best = hit.OrderByDescending(s => (e.NeedClear && (s.Suffix ?? "").ToUpper().Contains("CLEAR") ? 1 : 0) + (e.Prefix != null && norm(s.Prefix) == norm(e.Prefix) ? 1 : 0)).First();
            e.DimId = best.DimId; e.Found = (best.Prefix ?? "") + R0(best.Val) + (best.Suffix ?? "");
            var bad = new List<string>();
            if (e.NeedClear && !(best.Suffix ?? "").ToUpper().Contains("CLEAR")) bad.Add("no CLEAR suffix");
            if (!e.NeedClear && (best.Suffix ?? "").ToUpper().Contains("CLEAR")) bad.Add("CLEAR is only for the clear width of a run: remove it");
            if (e.Prefix != null && norm(best.Prefix) != norm(e.Prefix)) bad.Add("prefix should be '" + e.Prefix + "'");
            if (e.Below != null && !(best.Below ?? "").ToUpper().Contains("EQUAL TREADS")) bad.Add("below text should be '" + e.Below + "'");
            e.Status = bad.Count == 0 ? "OK" : string.Join("; ", bad);
            if (bad.Count > 0) issues.Add(e.Rule + ": dim " + best.DimId + " '" + e.Item + "': " + e.Status);
        }

        // ---- C: tread counts (model) vs footprint
        foreach (var r in vis.Where(r => Math.Abs(r.ModelTreads * r.Depth - r.FootLen) > 1))
            issues.Add("C: run " + r.Label + " " + r.ModelTreads + " model treads x " + Num(r.Depth) + " = " + R0(r.ModelTreads * r.Depth) + " but the footprint is " + R0(r.FootLen) + " long: check the run (do not edit the model)");
        foreach (var r in vis.Where(r => r.FlushTop))
            notes.Add("C: run " + r.Label + ": top tread at the landing / floor level, not counted -> " + r.Treads + "T (" + r.Risers + " risers); run length = " + R0(r.Len) + ", the last " + Num(r.Depth) + " goes to the landing");

        // ---- openings in the core walls
        var openings = new List<Tuple<FamilyInstance, string>>();
        foreach (var bic in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
            foreach (var fi in new FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                if (fi.Host == null || !coreWallIds.Contains(fi.Host.Id.IntegerValue)) continue;
                var s = sides.First(x => x.Stack.Any(w => w.W.Id == fi.Host.Id));
                openings.Add(Tuple.Create(fi, s.Label));
            }

        // ---- tags
        var tags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
        var byHost = new Dictionary<int, List<IndependentTag>>();
        foreach (var t in tags)
        {
            List<ElementId> ids = new List<ElementId>(); try { ids = t.GetTaggedLocalElementIds().ToList(); } catch { }
            foreach (var id in ids) { if (!byHost.ContainsKey(id.IntegerValue)) byHost[id.IntegerValue] = new List<IndependentTag>(); byHost[id.IntegerValue].Add(t); }
        }
        Func<IndependentTag, object> tagInfo = t => new { Id = t.Id.IntegerValue, Type = doc.GetElement(t.GetTypeId())?.Name, Category = t.Category?.Name, Text = SafeText(t), Leader = t.HasLeader, Material = t.IsMaterialTag };
        Func<IEnumerable<int>, List<IndependentTag>> tagsOn = ids => ids.Where(byHost.ContainsKey).SelectMany(i => byHost[i]).Distinct().ToList();
        var runTags = vis.Select(r => new { Run = r.Label, RunId = r.R.Id.IntegerValue, Tags = tagsOn(new[] { r.R.Id.IntegerValue }).Select(tagInfo).ToList() }).ToList();
        foreach (var rt in runTags.Where(x => x.Tags.Count != 1)) issues.Add("SB1: run " + rt.Run + " has " + rt.Tags.Count + " tags");
        var railTags = new List<object>();
        foreach (var rid in railIds)
        {
            var rl = (Railing)doc.GetElement(new ElementId(rid)); var ids = new List<int> { rid };
            try { ids.Add(rl.TopRail.IntegerValue); } catch { }
            try { ids.AddRange(rl.GetHandRails().Select(h => h.IntegerValue)); } catch { }
            var tg = tagsOn(ids);
            railTags.Add(new { Railing = rid, Type = doc.GetElement(rl.GetTypeId())?.Name, Tags = tg.Select(tagInfo).ToList() });
            if (tg.Count == 0) issues.Add("SB2: railing " + rid + " untagged");
            foreach (var t in tg.Where(t => !t.HasLeader)) issues.Add("SB2: railing tag " + t.Id.IntegerValue + " has no leader");
        }
        var openTags = openings.Select(o => new { Id = o.Item1.Id.IntegerValue, Category = o.Item1.Category.Name, Mark = o.Item1.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), Type = o.Item1.Symbol.Name, Wall = o.Item2, Tags = tagsOn(new[] { o.Item1.Id.IntegerValue }).Select(tagInfo).ToList() }).ToList();
        foreach (var o in openTags.Where(o => o.Tags.Count != 1)) issues.Add("SB4: " + o.Category + " " + o.Id + " (" + o.Mark + ") has " + o.Tags.Count + " tags");
        // finish marks drawn as generic annotations (a box with a code such as F13 / W05): not tags of an element, found by position + code
        var codeRx = new System.Text.RegularExpressions.Regex(@"^[A-Z]{1,2}\d{1,3}[A-Z]?$");
        var finishMarks = new List<FinMark>();
        foreach (var ga in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_GenericAnnotation).WhereElementIsNotElementType())
        {
            var gt = doc.GetElement(ga.GetTypeId()) as ElementType;
            string nm = ((gt?.FamilyName ?? "") + " " + (gt?.Name ?? "")).ToUpper();
            if (!nm.Contains("FINISH")) continue;
            string code = null;
            foreach (Parameter p in ga.Parameters) { if (p.StorageType != StorageType.String) continue; var s = (p.AsString() ?? "").Trim(); if (codeRx.IsMatch(s)) { code = s; break; } }
            var gbb = ga.get_BoundingBox(v); if (gbb == null) continue;
            var c = (gbb.Min + gbb.Max) / 2;
            finishMarks.Add(new FinMark { Id = ga.Id.IntegerValue, Type = gt?.FamilyName + " : " + gt?.Name, Code = code, A = Al(c), C = Cr(c) });
        }
        var landTags = lands.Select(l => new
        {
            Landing = l.L.Id.IntegerValue,
            Tags = tagsOn(new[] { l.L.Id.IntegerValue }).Select(tagInfo).ToList(),
            FinishMarks = finishMarks.Where(m => m.A >= l.A0 - 50 && m.A <= l.A1 + 50 && m.C >= l.C0 - 50 && m.C <= l.C1 + 50).Select(m => m.Id + " " + (m.Code ?? "?")).ToList()
        }).ToList();
        foreach (var lt in landTags.Where(x => x.Tags.Count == 0 && x.FinishMarks.Count == 0)) issues.Add("SB5: landing " + lt.Landing + " has no floor finish mark (tag or finish annotation F..)");
        var wallTags = sides.Where(s => s.Stack.Count > 0).Select(s => new { Side = s.Label, Walls = s.Stack.Select(w => w.W.Id.IntegerValue + " " + w.W.Name).ToList(), Tags = tagsOn(s.Stack.Select(w => w.W.Id.IntegerValue)).Select(tagInfo).ToList() }).ToList();
        var wallMarks = finishMarks.Where(m => m.Code != null && m.Code.StartsWith("W")).ToList();
        if (wallMarks.Count == 0 && wallTags.All(x => x.Tags.Count == 0))
            issues.Add("SB6: no wall finish mark (W..) in the core: one per wall finish kind, leader to the nearest wall face (sides: " + string.Join(", ", wallTags.Select(x => x.Side)) + "); code from the room's Wall Finish / finish legend, ask if unknown");

        // ---- spot elevations
        var spots = new List<object>(); var spotOnLanding = new HashSet<int>(); bool spotOutside = false;
        foreach (var sd in new FilteredElementCollector(doc, v.Id).OfClass(typeof(SpotDimension)).Cast<SpotDimension>())
        {
            if (sd.Category == null || sd.Category.Id.IntegerValue != (int)BuiltInCategory.OST_SpotElevations) continue;
            XYZ p = null; try { p = sd.Origin; } catch { }
            if (p == null) continue;
            double a = Al(p), c = Cr(p); string at = null;
            var l = lands.FirstOrDefault(x => a > x.A0 - 50 && a < x.A1 + 50 && c > x.C0 - 50 && c < x.C1 + 50);
            if (l != null) { at = "landing " + l.L.Id.IntegerValue; spotOnLanding.Add(l.L.Id.IntegerValue); }
            else if (openings.Any(o => o.Item1.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors && OutsideNear(o.Item1, a, c, sides))) { at = "outside a core door"; spotOutside = true; }
            spots.Add(new { Id = sd.Id.IntegerValue, Type = doc.GetElement(sd.GetTypeId())?.Name, Value = SafeVal(sd), At = at ?? "elsewhere" });
        }
        foreach (var l in lands.Where(l => !spotOnLanding.Contains(l.L.Id.IntegerValue))) issues.Add("SB3: landing " + l.L.Id.IntegerValue + " has no spot elevation");
        if (openings.Any(o => o.Item1.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors) && !spotOutside) issues.Add("SB3: no spot elevation on the floor outside the stair door");

        // ---- stair paths
        var paths = new List<object>(); var pathCount = new Dictionary<int, int>();
        foreach (var p in new FilteredElementCollector(doc, v.Id).OfClass(typeof(StairsPath)).Cast<StairsPath>())
        {
            int st = p.StairsId.HostElementId.IntegerValue;
            var ty = doc.GetElement(p.GetTypeId()) as ElementType;
            bool upT = p.ShowUpText, dnT = p.ShowDownText;
            pathCount[st] = (pathCount.ContainsKey(st) ? pathCount[st] : 0) + 1;
            paths.Add(new { Id = p.Id.IntegerValue, Stairs = st, Family = ty?.FamilyName, Type = ty?.Name, ShowUpText = upT, ShowDownText = dnT });
            if (ty != null && !(ty.FamilyName ?? "").ToLower().Contains("fixed")) issues.Add("SD: path " + p.Id.IntegerValue + " is '" + ty.FamilyName + "', use a Fixed Up Direction type");
            if (upT || dnT) issues.Add("SD: path " + p.Id.IntegerValue + " shows UP/DOWN text: turn it off");
        }
        foreach (var s in visStairs) { int n = pathCount.ContainsKey(s) ? pathCount[s] : 0; if (n != 1) issues.Add("SD: stairs " + s + " has " + n + " stair paths in this view"); }

        // ---- tread numbers per run
        var numbers = new Dictionary<string, int>();
        foreach (var ns in new FilteredElementCollector(doc, v.Id).OfClass(typeof(NumberSystem)).Cast<NumberSystem>())
        {
            int rid = -1; try { rid = ns.NumberedElementId.HostElementId.IntegerValue; } catch { }
            var r = vis.FirstOrDefault(x => x.R.Id.IntegerValue == rid);
            string k = r != null ? r.Label : "other (" + rid + ")"; numbers[k] = (numbers.ContainsKey(k) ? numbers[k] : 0) + 1;
        }
        foreach (var r in vis.Where(r => !numbers.ContainsKey(r.Label))) issues.Add("C: run " + r.Label + " has no tread numbers (skip if the profile says no tread numbers)");

        // ---- grids seen in the view
        var grids = new List<object>();
        foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
            var ln = c as Line; if (ln == null) continue;
            bool alongLine = AlongX ? Math.Abs(ln.Direction.DotProduct(Rg)) > 0.999 : Math.Abs(ln.Direction.DotProduct(Up)) > 0.999;
            bool crossLine = AlongX ? Math.Abs(ln.Direction.DotProduct(Up)) > 0.999 : Math.Abs(ln.Direction.DotProduct(Rg)) > 0.999;
            if (!alongLine && !crossLine) continue;
            double pos = alongLine ? Cr(ln.Origin) : Al(ln.Origin);
            grids.Add(new { Name = g.Name, Measures = alongLine ? "across (SA4 on the end-wall sides)" : "along (SA4 on the side-wall sides)", PosMm = R0(pos) });
        }
        if (grids.Count == 0) notes.Add("SA4: no host grid seen in this view: wall/door chains stop at the last wall face");

        // ---- project types (most used)
        var tagTypes = new FilteredElementCollector(doc).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
            .Where(t => t.Category != null).GroupBy(t => t.Category.Name)
            .Where(g => new[] { "Stair", "Railing", "Door", "Window", "Material", "Wall", "Floor", "Keynote", "Multi" }.Any(k => g.Key.Contains(k)))
            .Select(g => new { Category = g.Key, Top = g.GroupBy(t => t.GetTypeId().IntegerValue).OrderByDescending(x => x.Count()).Take(3).Select(x => doc.GetElement(new ElementId(x.Key))?.Name + " (" + x.Count() + ")").ToList() }).ToList<object>();
        var spotTypes = new FilteredElementCollector(doc).OfClass(typeof(SpotDimension)).Cast<SpotDimension>()
            .Where(s => s.Category != null && s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_SpotElevations)
            .GroupBy(s => s.GetTypeId().IntegerValue).OrderByDescending(x => x.Count()).Take(3).Select(x => doc.GetElement(new ElementId(x.Key))?.Name + " (" + x.Count() + ")").ToList();
        var pathTypes = new FilteredElementCollector(doc).OfClass(typeof(StairsPathType)).Cast<ElementType>().Select(t => t.FamilyName + " : " + t.Name + " [id " + t.Id.IntegerValue + "]").ToList();

        var res = new
        {
            View = v.Name, ViewId = v.Id.IntegerValue, Scale = v.Scale, CutAboveLevelMm = R0((cut - lvZ) * MM),
            Travel = AlongX ? "along the view's horizontal" : "along the view's vertical",
            Runs = vis.Select(r => new
            {
                r.Label, Id = r.R.Id.IntegerValue, Stairs = r.S.Id.IntegerValue, r.State, Up = upDir(r), BaseAboveLevelMm = R0((r.Z0 - lvZ) * MM), TopAboveLevelMm = R0((r.Z1 - lvZ) * MM),
                r.Risers, r.Treads, ModelTreads = r.ModelTreads, TopTreadAtLanding = r.FlushTop, TreadDepth = Num(r.Depth), RiserHeight = Num(r.RiserH), Length = R0(r.Len), FootprintLength = R0(r.FootLen), RunLengthMm = new[] { R0(Math.Min(r.LA0, r.LA1)), R0(Math.Max(r.LA0, r.LA1)) }, Text = r.Formula + " " + R0(r.Len),
                Lane = r.Lane + 1, ClearWidth = R0(r.InHigh - r.InLow), ClearFrom = r.InLowBy, ClearTo = r.InHighBy, Width = R0(r.C1 - r.C0)
            }).ToList(),
            NotSeen = runs.Where(r => !vis.Contains(r)).Select(r => new { Id = r.R.Id.IntegerValue, Stairs = r.S.Id.IntegerValue, r.State, r.Treads }).ToList(),
            Lanes = laneOut,
            Landings = lands.Select(l => new { Id = l.L.Id.IntegerValue, Stairs = l.S.Id.IntegerValue, ElevAboveLevelMm = R0((l.Z - lvZ) * MM), EndWall = l.WallSide, DepthToWall = l.Depth.HasValue ? R0(l.Depth.Value) : (double?)null, Clear = l.Clear.HasValue ? R0(l.Clear.Value) : (double?)null, l.ClearFrom, l.ClearTo }).ToList(),
            Walls = sides.Select(s => new { Side = s.Label, s.Kind, FinishFaceMm = s.Finish.HasValue ? R0(s.Finish.Value) : (double?)null, OuterFaceMm = s.Outer.HasValue ? R0(s.Outer.Value) : (double?)null, Walls = s.Stack.Select(w => w.W.Id.IntegerValue + " " + w.W.Name).ToList() }).ToList(),
            ClearAcross = sLowC.Finish != null && sHighC.Finish != null ? R0(sHighC.Finish.Value - sLowC.Finish.Value) : (double?)null,
            ClearAlong = sLowA.Finish != null && sHighA.Finish != null ? R0(sHighA.Finish.Value - sLowA.Finish.Value) : (double?)null,
            Expected = exp.Select(e => new { e.Rule, e.Item, Measures = e.Cross ? "across" : "along", Mm = R0(e.Mm), FromMm = R0(Math.Min(e.From, e.To)), ToMm = R0(Math.Max(e.From, e.To)), Text = (e.Prefix != null ? e.Prefix + " " : "") + R0(e.Mm) + (e.NeedClear ? " CLEAR" : "") + (e.Below != null ? " / below: " + e.Below : ""), e.Status, e.DimId, e.Found }).ToList(),
            Openings = openTags, Grids = grids,
            Tags = new { Runs = runTags, Railings = railTags, Landings = landTags, Walls = wallTags, FinishMarks = finishMarks.Select(m => new { m.Id, m.Type, m.Code, AtMm = new[] { R0(m.A), R0(m.C) } }).ToList() },
            Spots = spots, Paths = paths, TreadNumbers = numbers,
            ProjectTypes = new { Tags = tagTypes, Spots = spotTypes, StairPaths = pathTypes },
            Notes = notes, Issues = issues
        };
        var outPath = args.Value<string>("outPath");
        if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, JsonConvert.SerializeObject(res, Formatting.Indented));
        return res;
    }

    // the run that carries the lane's dims: V3 (this floor's run), else V2, else V1
    static RunI PickRun(List<RunI> lane)
    {
        return lane.FirstOrDefault(r => r.State == "cut") ?? lane.FirstOrDefault(r => r.State == "full") ?? lane.First();
    }
    static string SafeText(IndependentTag t) { try { return t.TagText; } catch { return null; } }
    static string SafeVal(SpotDimension s) { try { return s.ValueString; } catch { return null; } }
    // point (along, cross) lies outside the core walls and within 2 m of the door
    static bool OutsideNear(FamilyInstance door, double a, double c, SideI[] sides)
    {
        var bb = door.get_BoundingBox(null); if (bb == null) return false;
        var mid = (bb.Min + bb.Max) / 2; double da = Al(mid) - a, dc = Cr(mid) - c;
        if (Math.Sqrt(da * da + dc * dc) > 2000) return false;
        var s = sides.FirstOrDefault(x => x.Stack.Any(w => w.W.Id == door.Host.Id)); if (s == null || s.Outer == null || s.Finish == null) return false;
        double n = s.Key.EndsWith("Cross") ? c : a;
        return s.Outer.Value < s.Finish.Value ? n < s.Outer.Value : n > s.Outer.Value;
    }
}
