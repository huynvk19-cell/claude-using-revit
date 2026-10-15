/* mcp-tool
{
  "description": "Stair core PLAN, ONE view: X in dashed lines over the open gap between two flights (fits the 4 corners, skips walls/floors, refits an existing X). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" },
      "lineStyle": { "type": "string", "description": "line style of the X, default <Overhead>" },
      "excludeStairIds": { "type": "array", "items": { "type": "integer" } },
      "minGapMm": { "type": "number", "description": "narrower gaps are ignored, default 50" },
      "rect": { "type": "array", "items": { "type": "number" }, "description": "[r0,u0,r1,u1] view mm: use this gap instead of the one found" }
    },
    "required": ["viewId", "mode"]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Stair core PLAN, rule "Lỗ trống cạnh vế thang: ký hiệu X" (drafting-stair-plan.md §0). The gap between two flights
//    side by side (the well, often kept for the fire-fighting pipes) is open to the floors below: it gets an X of two
//    detail lines in a dashed style (<Overhead> by default), corner to corner, fitting the hole exactly.
// How the gap is found: runs of the core grouped in lanes (same travel axis as stair_plan_audit); between two
//    neighbouring lanes, across = the facing run edges, along = where both lanes still have runs; then tightened to the
//    inner faces of railings (centre railing frame) and to the edges of landings that reach into it. 'rect' overrides.
// No X (Skipped) when: the gap is narrower than minGapMm; a wall fills it; a floor of this level covers its centre
//    (e.g. the lowest floor, where the side of the flight is the slab).
// An existing X (diagonal detail lines in the gap) is kept and refitted to the corners when an end is off by more
//    than 5 mm; a missing diagonal is added. Tag heads lying on the X are listed in Warnings (move them, standard SB).
// Detail lines have no type: they cannot take a mark type; the created / moved ids are in the apply log.
// mode preview (rolled back) | apply (logPath) | undo (logPath: deletes the created lines, puts moved ends back).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairGapX
{
    const double MM = 304.8;
    static XYZ O, Rg, Up;
    static bool AlongX;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double VY(XYZ p) { return (p - O).DotProduct(Up) * MM; }
    static double Al(XYZ p) { return AlongX ? VX(p) : VY(p); }
    static double Cr(XYZ p) { return AlongX ? VY(p) : VX(p); }
    static double Ov(double a0, double a1, double b0, double b1) { return Math.Min(a1, b1) - Math.Max(a0, b0); }
    static double R1(double x) { return Math.Round(x, 1); }

    class RunI { public StairsRun R; public double A0, A1, C0, C1; public XYZ P0, P1; }
    class Moved { public int Id; public double[] Start, End; }
    class Log { public int ViewId; public List<int> Created = new List<int>(); public List<Moved> Moved = new List<Moved>(); }

    static List<XYZ> Pts(Element e)
    {
        var res = new List<XYZ>(); GeometryElement ge = null;
        try { ge = e.get_Geometry(new Options { ComputeReferences = false }); } catch { }
        var st = new Stack<GeometryElement>(); if (ge != null) st.Push(ge);
        while (st.Count > 0)
            foreach (var g in st.Pop())
            {
                if (g is Solid s && s.Volume > 1e-9) { foreach (Edge ed in s.Edges) res.AddRange(ed.Tessellate()); }
                else if (g is GeometryInstance gi) st.Push(gi.GetInstanceGeometry());
                else if (g is Curve c) res.AddRange(c.Tessellate());
            }
        return res;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode != "preview" && string.IsNullOrEmpty(logPath)) return new { Error = "logPath is required for apply / undo" };
        if (mode == "undo")
        {
            var lg = JsonConvert.DeserializeObject<Log>(File.ReadAllText(logPath)); var undone = new List<string>();
            var uv = doc.GetElement(new ElementId(lg.ViewId)) as View;
            using (var t = new Transaction(doc, "Stair gap X undo"))
            {
                t.Start();
                foreach (var id in lg.Created) { var e = doc.GetElement(new ElementId(id)); if (e != null) { doc.Delete(e.Id); undone.Add("deleted " + id); } }
                if (uv != null)
                {
                    O = uv.Origin; Rg = uv.RightDirection; Up = uv.UpDirection;
                    foreach (var m in lg.Moved)
                    {
                        var dc = doc.GetElement(new ElementId(m.Id)) as DetailCurve; if (dc == null || !(dc.GeometryCurve is Line ln)) continue;
                        double z = ln.GetEndPoint(0).Z;
                        dc.SetGeometryCurve(Line.CreateBound(ToXyz(m.Start[0], m.Start[1], z), ToXyz(m.End[0], m.End[1], z)), true);
                        undone.Add("restored " + m.Id);
                    }
                }
                t.Commit();
            }
            return new { Undone = undone };
        }

        var v = doc.GetElement(new ElementId(args.Value<int>("viewId"))) as ViewPlan;
        if (v == null) return new { Error = "viewId must be a plan view" };
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection;
        double minGap = args.Value<double?>("minGapMm") ?? 50, tol = 5;
        string styleName = (string)args["lineStyle"] ?? "<Overhead>";
        var notes = new List<string>(); var warnings = new List<string>(); var errors = new List<string>();

        GraphicsStyle style = null;
        foreach (Category c in doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories)
            if (c.Name == styleName) { style = c.GetGraphicsStyle(GraphicsStyleType.Projection); break; }
        if (style == null) return new { Error = "line style not found: " + styleName };

        // ---- level and cut plane (internal elevations)
        double lvZ = v.GenLevel != null ? v.GenLevel.ProjectElevation : 0, cut = double.NaN;
        try
        {
            var vr = v.GetViewRange();
            var cl = doc.GetElement(vr.GetLevelId(PlanViewPlane.CutPlane)) as Level;
            if (cl != null) cut = cl.ProjectElevation + vr.GetOffset(PlanViewPlane.CutPlane);
        }
        catch { }
        if (double.IsNaN(cut)) cut = lvZ + 1200 / MM;

        // ---- runs of the core that start below the cut (the drawn ones and the stacked ones below)
        var excl = args["excludeStairIds"] != null ? new HashSet<int>(((JArray)args["excludeStairIds"]).Select(x => (int)x)) : new HashSet<int>();
        var sts = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>()
            .Where(s => !excl.Contains(s.Id.IntegerValue)).ToList();
        if (sts.Count == 0) return new { View = v.Name, Error = "no host stairs (by component) in this view" };
        var runs = new List<RunI>(); var landFeet = new List<List<XYZ>>();
        foreach (var s in sts)
        {
            double sb = s.BaseElevation;
            var pl = s.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            var bl = pl != null && pl.StorageType == StorageType.ElementId ? doc.GetElement(pl.AsElementId()) as Level : null;
            if (bl != null) sb = bl.ProjectElevation + (s.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET)?.AsDouble() ?? 0);
            foreach (var rid in s.GetStairsRuns())
            {
                var r = doc.GetElement(rid) as StairsRun; if (r == null || sb + r.BaseElevation >= cut - 10 / MM) continue;
                var fp = new List<XYZ>(); try { foreach (Curve c in r.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count == 0) continue;
                var ri = new RunI { R = r, A0 = fp.Min(VX), A1 = fp.Max(VX), C0 = fp.Min(VY), C1 = fp.Max(VY) }; // view X / Y for now
                try { var pc = r.GetStairsPath().Cast<Curve>().ToList(); ri.P0 = pc.First().GetEndPoint(0); ri.P1 = pc.Last().GetEndPoint(1); } catch { }
                runs.Add(ri);
            }
            foreach (var lid in s.GetStairsLandings())
            {
                var l = doc.GetElement(lid) as StairsLanding; if (l == null || sb + l.BaseElevation > cut + 10 / MM) continue;
                var fp = new List<XYZ>(); try { foreach (Curve c in l.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count > 0) landFeet.Add(fp);
            }
        }
        if (runs.Count == 0) return new { View = v.Name, Error = "no stair run below the cut plane" };
        int nx = 0, ny = 0;
        foreach (var r in runs.Where(r => r.P0 != null)) { var d = r.P1 - r.P0; if (Math.Abs(d.DotProduct(Rg)) >= Math.Abs(d.DotProduct(Up))) nx++; else ny++; }
        AlongX = nx >= ny;
        if (!AlongX) foreach (var r in runs) { double a0 = r.A0, a1 = r.A1; r.A0 = r.C0; r.A1 = r.C1; r.C0 = a0; r.C1 = a1; }

        // ---- lanes: runs grouped by cross overlap
        var lanes = new List<List<RunI>>();
        foreach (var r in runs.OrderBy(r => r.C0))
        {
            var ln = lanes.FirstOrDefault(l => l.Any(x => Ov(x.C0, x.C1, r.C0, r.C1) > 0.5 * Math.Min(x.C1 - x.C0, r.C1 - r.C0)));
            if (ln == null) { ln = new List<RunI>(); lanes.Add(ln); }
            ln.Add(r);
        }
        lanes = lanes.OrderBy(l => l.Min(r => r.C0)).ToList();

        // ---- gaps: [a0, a1] along x [c0, c1] across
        var gaps = new List<double[]>(); var bounds = new List<string>();
        var rect = args["rect"] as JArray;
        if (rect != null && rect.Count == 4)
        {
            double r0 = (double)rect[0], u0 = (double)rect[1], r1 = (double)rect[2], u1 = (double)rect[3];
            gaps.Add(AlongX ? new[] { Math.Min(r0, r1), Math.Max(r0, r1), Math.Min(u0, u1), Math.Max(u0, u1) } : new[] { Math.Min(u0, u1), Math.Max(u0, u1), Math.Min(r0, r1), Math.Max(r0, r1) });
            bounds.Add("rect given");
        }
        else
        {
            if (lanes.Count < 2) notes.Add("one lane of flights only: no gap between flights");
            for (int i = 0; i + 1 < lanes.Count; i++)
            {
                double c0 = lanes[i].Max(r => r.C1), c1 = lanes[i + 1].Min(r => r.C0);
                double a0 = Math.Max(lanes[i].Min(r => r.A0), lanes[i + 1].Min(r => r.A0)), a1 = Math.Min(lanes[i].Max(r => r.A1), lanes[i + 1].Max(r => r.A1));
                if (c1 - c0 < minGap) { notes.Add("lanes " + (i + 1) + "-" + (i + 2) + ": gap " + Math.Round(c1 - c0) + " mm < minGapMm, no X"); continue; }
                if (a1 - a0 < minGap) { notes.Add("lanes " + (i + 1) + "-" + (i + 2) + ": the runs do not face each other, no X"); continue; }
                var by = new List<string> { "run edges" };
                // landings reaching into the gap close it along
                foreach (var fp in landFeet)
                {
                    double la0 = fp.Min(Al), la1 = fp.Max(Al), lc0 = fp.Min(Cr), lc1 = fp.Max(Cr);
                    if (Ov(lc0, lc1, c0, c1) < 0.8 * (c1 - c0) || Ov(la0, la1, a0, a1) <= 0) continue;
                    if ((la0 + la1) / 2 < (a0 + a1) / 2) { if (la1 > a0) { a0 = la1; by.Add("landing edge"); } }
                    else if (la0 < a1) { a1 = la0; by.Add("landing edge"); }
                }
                // railings inside the gap: their inner faces bound it (the centre railing frame)
                double bandC = Math.Min(250, 0.4 * (c1 - c0)), bandA = Math.Min(250, 0.4 * (a1 - a0));
                double n0 = c0, n1 = c1, m0 = a0, m1 = a1;
                foreach (var rl in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Railing)).Cast<Railing>())
                {
                    var els = new List<Element> { rl };
                    try { var tr = doc.GetElement(rl.TopRail); if (tr != null) els.Add(tr); } catch { }
                    try { foreach (var h in rl.GetHandRails()) { var he = doc.GetElement(h); if (he != null) els.Add(he); } } catch { }
                    foreach (var p in els.SelectMany(Pts))
                    {
                        double pa = Al(p), pc = Cr(p);
                        if (pa <= a0 || pa >= a1 || pc <= c0 || pc >= c1) continue;
                        double dc0 = pc - c0, dc1 = c1 - pc, da0 = pa - a0, da1 = a1 - pa, dm = Math.Min(Math.Min(dc0, dc1), Math.Min(da0, da1));
                        if (dm == dc0 && dc0 <= bandC) n0 = Math.Max(n0, pc);
                        else if (dm == dc1 && dc1 <= bandC) n1 = Math.Min(n1, pc);
                        else if (dm == da0 && da0 <= bandA) m0 = Math.Max(m0, pa);
                        else if (dm == da1 && da1 <= bandA) m1 = Math.Min(m1, pa);
                    }
                }
                if (n0 != c0 || n1 != c1 || m0 != a0 || m1 != a1) by.Add("railing faces");
                gaps.Add(new[] { m0, m1, n0, n1 }); bounds.Add(string.Join(", ", by.Distinct()));
            }
        }

        // ---- existing detail lines, walls, floors, tags of the view
        var lines = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Lines).WhereElementIsNotElementType()
            .OfType<DetailCurve>().Where(d => d.GeometryCurve is Line).ToList();
        var used = new HashSet<int>();
        var log = new Log { ViewId = v.Id.IntegerValue }; var result = new List<object>();

        using (var t = new Transaction(doc, "Stair gap X"))
        {
            t.Start();
            for (int g = 0; g < gaps.Count; g++)
            {
                double a0 = gaps[g][0], a1 = gaps[g][1], c0 = gaps[g][2], c1 = gaps[g][3];
                // corners in view frame mm (right, up)
                Func<double, double, double[]> V = (a, c) => AlongX ? new[] { R1(a), R1(c) } : new[] { R1(c), R1(a) };
                var k00 = V(a0, c0); var k11 = V(a1, c1); var k01 = V(a0, c1); var k10 = V(a1, c0);
                var mid = ToXyz((k00[0] + k11[0]) / 2, (k00[1] + k11[1]) / 2, lvZ);
                string skip = null;
                foreach (var w in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Wall)).Cast<Wall>())
                {
                    var bb = w.get_BoundingBox(null); if (bb == null || bb.Max.Z < lvZ || bb.Min.Z > cut) continue;
                    var bp = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z) };
                    double oa = Ov(bp.Min(Al), bp.Max(Al), a0, a1), oc = Ov(bp.Min(Cr), bp.Max(Cr), c0, c1);
                    if (oa > 0.5 * (a1 - a0) && oc > 0.5 * (c1 - c0)) { skip = "a wall fills the gap (" + w.Id.IntegerValue + ")"; break; }
                }
                if (skip == null)
                    foreach (var f in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Floor)).Cast<Floor>())
                    {
                        var bb = f.get_BoundingBox(null); if (bb == null || bb.Max.Z < lvZ - 600 / MM || bb.Max.Z > cut) continue;
                        if (mid.X < bb.Min.X || mid.X > bb.Max.X || mid.Y < bb.Min.Y || mid.Y > bb.Max.Y) continue;
                        IList<Reference> tops; try { tops = HostObjectUtils.GetTopFaces(f); } catch { continue; }
                        foreach (var rf in tops)
                        {
                            var face = f.GetGeometryObjectFromReference(rf) as Face; if (face == null) continue;
                            var ir = face.Project(new XYZ(mid.X, mid.Y, bb.Max.Z));
                            if (ir != null && ir.Distance < 1) { skip = "a floor of this level covers the gap (" + f.Id.IntegerValue + ")"; break; }
                        }
                        if (skip != null) break;
                    }
                var item = new Dictionary<string, object> { ["Gap"] = g + 1, ["CornersMm"] = new[] { k00, k11 }, ["SizeMm"] = new[] { Math.Round(a1 - a0), Math.Round(c1 - c0) }, ["BoundBy"] = bounds[g] };
                if (skip != null) { item["Skipped"] = skip; result.Add(item); continue; }

                // the two diagonals: k00-k11 and k01-k10
                var diag = new[] { new[] { k00, k11 }, new[] { k01, k10 } };
                var done = new List<string>();
                for (int d = 0; d < 2; d++)
                {
                    double[] p = diag[d][0], q = diag[d][1];
                    int sr = Math.Sign(q[0] - p[0]), su = Math.Sign(q[1] - p[1]);
                    // an existing diagonal of this gap going the same way
                    var ex = lines.Where(x => !used.Contains(x.Id.IntegerValue)).Select(x => new { L = x, E = Ends((Line)x.GeometryCurve) })
                        .Where(x => Inside(x.E[0], k00, k11, 300) && Inside(x.E[1], k00, k11, 300)
                                    && Math.Abs(x.E[1][0] - x.E[0][0]) > 0.3 * Math.Abs(q[0] - p[0]) && Math.Abs(x.E[1][1] - x.E[0][1]) > 0.3 * Math.Abs(q[1] - p[1])
                                    && Math.Sign(x.E[1][0] - x.E[0][0]) * Math.Sign(x.E[1][1] - x.E[0][1]) == sr * su)
                        .OrderBy(x => Math.Min(Dist(x.E[0], p) + Dist(x.E[1], q), Dist(x.E[0], q) + Dist(x.E[1], p))).FirstOrDefault();
                    if (ex != null)
                    {
                        used.Add(ex.L.Id.IntegerValue);
                        bool flip = Dist(ex.E[0], q) + Dist(ex.E[1], p) < Dist(ex.E[0], p) + Dist(ex.E[1], q);
                        double[] s0 = flip ? q : p, s1 = flip ? p : q;
                        double off = Math.Max(Dist(ex.E[0], s0), Dist(ex.E[1], s1));
                        if (off <= tol) { done.Add("line " + ex.L.Id.IntegerValue + " fits"); continue; }
                        double z = ((Line)ex.L.GeometryCurve).GetEndPoint(0).Z;
                        try
                        {
                            ex.L.SetGeometryCurve(Line.CreateBound(ToXyz(s0[0], s0[1], z), ToXyz(s1[0], s1[1], z)), true);
                            log.Moved.Add(new Moved { Id = ex.L.Id.IntegerValue, Start = ex.E[0], End = ex.E[1] });
                            done.Add("line " + ex.L.Id.IntegerValue + " refitted (" + Math.Round(off) + " mm off)");
                        }
                        catch (Exception e) { errors.Add("gap " + (g + 1) + " line " + ex.L.Id.IntegerValue + ": " + e.Message); }
                        continue;
                    }
                    try
                    {
                        var dc = doc.Create.NewDetailCurve(v, Line.CreateBound(ToXyz(p[0], p[1], lvZ), ToXyz(q[0], q[1], lvZ)));
                        dc.LineStyle = style; log.Created.Add(dc.Id.IntegerValue); done.Add("line " + dc.Id.IntegerValue + " created");
                    }
                    catch (Exception e) { errors.Add("gap " + (g + 1) + " diagonal " + (d + 1) + ": " + e.Message); }
                }
                item["Done"] = done; result.Add(item);
                foreach (var tg in new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                {
                    XYZ h; try { h = tg.TagHeadPosition; } catch { continue; }
                    double ha = Al(h), hc = Cr(h);
                    if (ha > a0 && ha < a1 && hc > c0 && hc < c1) warnings.Add("gap " + (g + 1) + ": tag " + tg.Id.IntegerValue + " has its head on the X: move it");
                }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(log, Formatting.Indented)); }
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, LineStyle = styleName, Gaps = result, Notes = notes, Warnings = warnings, Errors = errors };
    }

    static XYZ ToXyz(double r, double u, double z) { var p = O + Rg * (r / MM) + Up * (u / MM); return new XYZ(p.X, p.Y, z); }
    static double[][] Ends(Line ln) { return new[] { new[] { R1(VX(ln.GetEndPoint(0))), R1(VY(ln.GetEndPoint(0))) }, new[] { R1(VX(ln.GetEndPoint(1))), R1(VY(ln.GetEndPoint(1))) } }; }
    static double Dist(double[] a, double[] b) { return Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])); }
    static bool Inside(double[] p, double[] k0, double[] k1, double pad)
    {
        return p[0] >= Math.Min(k0[0], k1[0]) - pad && p[0] <= Math.Max(k0[0], k1[0]) + pad && p[1] >= Math.Min(k0[1], k1[1]) - pad && p[1] <= Math.Max(k0[1], k1[1]) + pad;
    }
}
