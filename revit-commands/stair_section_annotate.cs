/* mcp-tool
{
  "description": "ONE stair section along the flights: LA1 rise chain, LA3 going chains, LA4 clear heights, LB1 run tags, LB3 landing spots, LC riser numbers. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" },
      "parts": { "type": "array", "items": { "type": "string" }, "description": "LA1 LA3 LA4 LB1 LB3 LC (default all)" },
      "dimTypeName": { "type": "string" },
      "la1Side": { "type": "string", "enum": ["left", "right"], "description": "default right" },
      "la1LineMm": { "type": "number", "description": "view x of the LA1 dim line (default outer wall face + 800)" },
      "la4InsetMm": { "type": "number", "description": "LA4 columns: distance from the inner wall faces, default 300" },
      "la4LeftX": { "type": "number" }, "la4RightX": { "type": "number" },
      "runTagTypes": { "type": "array", "items": { "type": "object" }, "description": "[{fromMm, type}]: tag type by flight base elevation" },
      "tagFlights": { "type": "string", "enum": ["cut", "all"], "description": "default cut" },
      "spotTypeName": { "type": "string" },
      "numberSourceId": { "type": "number", "description": "a NumberSystem whose settings are copied" },
      "excludeStairIds": { "type": "array", "items": { "type": "number" }, "description": "stairs seen in the view but not part of this core" },
      "la5Auto": { "type": "boolean", "description": "LA5 without a sample dim: rail refs found from the top rails" },
      "la5LeftX": { "type": "number" }, "la5RightX": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" }, "description": "overrides: {part, flight|landing, x, z, skip} (view x mm, absolute z mm)" }
    },
    "required": ["viewId", "mode"]
  },
  "timeoutSeconds": 180
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Stair SECTION cut parallel to the stair path (drafting-stair-section-parallel.md), ONE view, host stairs only.
//  x = mm along the view's Right from the view origin (as stair_section_info XMm); z = mm (elevation as the flights'
//  From EL / To EL, i.e. level-based). Parts:
//  LA1  one vertical chain through every flight boundary (bottom of the lowest flight .. top of the highest), each
//       segment prefix 'R x riser height = ' + below '(EQUAL RISERS)'; refs: landing top face > level plane > floor top.
//       Placed outside the wall on la1Side at la1LineMm.
//  LA3  per cut flight: inner wall face | first riser | last riser (the top tread level with the landing is not
//       counted: T = R - 1 when the model has one tread per riser) | inner wall face, flight segment prefix
//       'depth x T = ' + below '(EQUAL TREADS)'; line at mid-height of the flight.
//  LA4  per landing (cut or seen beyond) and floor slab at the side column: top -> soffit of the next slab above on the
//       same side; vertical columns la4InsetMm inside the inner wall faces.
//  LB1  run tag under each cut flight (tagFlights 'all': also the flights beyond), head beside the soffit, one
//       horizontal leader to the soffit; tag type from runTagTypes by the flight base elevation.
//  LB3  spot elevation on each cut landing top without a spot yet, near the flight end.
//  LC   riser numbers on each cut flight without numbers; settings copied from numberSourceId.
//  Values (risers, heights, treads, depth) come from the model, never from the drawing. Dim text goes in
//  Prefix / Below, never Replace with text. mode preview (rolled back, reports values/tag texts/positions) | apply
//  (logPath: created ids) | undo (logPath: deletes them).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairSectionAnnotate
{
    const double MM = 304.8;
    static XYZ O, Rg, Vd; static View V; static double ElOff; // ElOff: internal Z (ft) of elevation 0 as the flights read it
    static double VX(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double Dp(XYZ p) { return (p - O).DotProduct(Vd) * MM; }
    static double ZE(XYZ p) { return (p.Z - ElOff) * MM; }                 // elevation mm
    static XYZ PV(double x, double z) { return new XYZ(O.X, O.Y, ElOff + z / MM) + Rg * (x / MM); }
    static string Num(double x) { return Math.Round(x, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }

    class Fl { public StairsRun R; public Stairs S; public string Label, State; public bool RisesRight; public double Z0, Z1, X0, X1; public int Risers, Treads; public double RiserH, Depth; }
    class Ld { public StairsLanding L; public int Sid; public double Z, X0, X1; public bool Cut; }
    class F { public PlanarFace P; public double X0, X1, Z0, Z1, D0, D1; public XYZ N; public int Id; }

    static List<F> Faces(Element e)
    {
        var res = new List<F>();
        foreach (var opt in new[] { new Options { ComputeReferences = true, View = V }, new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine } })
        {
            var st = new Stack<GeometryElement>(); st.Push(e.get_Geometry(opt));
            while (st.Count > 0)
            {
                var ge = st.Pop(); if (ge == null) continue;
                foreach (var g in ge)
                {
                    if (g is GeometryInstance gi) { st.Push(gi.GetInstanceGeometry()); continue; }
                    var s = g as Solid; if (s == null) continue;
                    foreach (Face f in s.Faces)
                    {
                        var pf = f as PlanarFace; if (pf == null || pf.Reference == null) continue;
                        var pts = new List<XYZ>(); foreach (EdgeArray ea in pf.EdgeLoops) foreach (Edge ed in ea) pts.AddRange(ed.Tessellate());
                        if (pts.Count == 0) continue;
                        res.Add(new F { P = pf, N = pf.FaceNormal, Id = e.Id.IntegerValue, X0 = pts.Min(VX), X1 = pts.Max(VX), Z0 = pts.Min(ZE), Z1 = pts.Max(ZE), D0 = pts.Min(Dp), D1 = pts.Max(Dp) });
                    }
                }
            }
            if (res.Count > 0) break;
        }
        return res;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); var gone = new List<int>();
            using (var t = new Transaction(doc, "Undo stair section annotate"))
            {
                t.Start();
                foreach (var id in ids) if (doc.GetElement(new ElementId(id)) != null) { doc.Delete(new ElementId(id)); gone.Add(id); }
                t.Commit();
            }
            return new { Mode = "undo", Deleted = gone };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        V = doc.GetElement(new ElementId(args.Value<int>("viewId"))) as View;
        if (V == null || V.ViewType != ViewType.Section) return new { Error = "viewId must be a section view" };
        O = V.Origin; Rg = V.RightDirection; Vd = V.ViewDirection;
        var parts = args["parts"] != null ? ((JArray)args["parts"]).Select(x => ((string)x).ToUpper()).ToList() : new List<string> { "LA1", "LA3", "LA4", "LB1", "LB3", "LC" };
        var errors = new List<string>(); var done = new List<object>(); var created = new List<int>();

        // ---- flights and landings (as stair_section_info)
        var fl = new List<Fl>(); var lds = new List<Ld>();
        var excl = new HashSet<int>(((JArray)args["excludeStairIds"] ?? new JArray()).Select(x => (int)x));
        foreach (var s in new FilteredElementCollector(doc, V.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>().Where(s => !excl.Contains(s.Id.IntegerValue)))
        {
            var pl = s.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            var bl = pl != null ? doc.GetElement(pl.AsElementId()) as Level : null; if (bl == null) continue;
            double off = s.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET)?.AsDouble() ?? 0;
            ElOff = bl.ProjectElevation - bl.Elevation;   // internal Z of elevation 0
            double sb = bl.Elevation + off;
            foreach (var rid in s.GetStairsRuns())
            {
                var r = doc.GetElement(rid) as StairsRun; if (r == null) continue;
                var fp = new List<XYZ>(); try { foreach (Curve c in r.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count == 0) continue;
                double d0 = fp.Min(Dp), d1 = fp.Max(Dp); if (d0 > 0) continue;
                var f = new Fl { R = r, S = s, Z0 = (sb + r.BaseElevation) * MM, Z1 = (sb + r.TopElevation) * MM, X0 = fp.Min(VX), X1 = fp.Max(VX), Risers = r.ActualRisersNumber, Treads = r.ActualTreadsNumber >= r.ActualRisersNumber ? r.ActualRisersNumber - 1 : r.ActualTreadsNumber, RiserH = s.ActualRiserHeight * MM, Depth = s.ActualTreadDepth * MM, State = d1 >= 0 ? "cut" : "beyond" };
                try { var pc = r.GetStairsPath().Cast<Curve>().ToList(); f.RisesRight = VX(pc.Last().GetEndPoint(1)) > VX(pc.First().GetEndPoint(0)); } catch { }
                fl.Add(f);
            }
            foreach (var lid in s.GetStairsLandings())
            {
                var l = doc.GetElement(lid) as StairsLanding; if (l == null) continue;
                var fp = new List<XYZ>(); try { foreach (Curve c in l.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count == 0) continue;
                lds.Add(new Ld { L = l, Sid = s.Id.IntegerValue, Z = (sb + l.BaseElevation) * MM, X0 = fp.Min(VX), X1 = fp.Max(VX), Cut = fp.Min(Dp) <= 0 && fp.Max(Dp) >= 0 });
            }
        }
        fl = fl.OrderBy(f => f.Z0).ToList(); for (int i = 0; i < fl.Count; i++) fl[i].Label = "F" + (i + 1);
        if (fl.Count == 0) return new { View = V.Name, Error = "no host stair flight seen in this section" };
        double sx0 = Math.Min(fl.Min(f => f.X0), lds.Count > 0 ? lds.Min(l => l.X0) : 1e9), sx1 = Math.Max(fl.Max(f => f.X1), lds.Count > 0 ? lds.Max(l => l.X1) : -1e9);
        double zBot = fl.Min(f => f.Z0), zTop = fl.Max(f => f.Z1), zMid = (zBot + zTop) / 2;

        // ---- walls: inner faces left / right of the stair, outer face on the LA1 side
        var wallFaces = new List<F>();
        foreach (var w in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Wall)).Cast<Wall>())
            wallFaces.AddRange(Faces(w).Where(f => Math.Abs(f.N.DotProduct(Rg)) > 0.999 && f.D0 <= 1 && f.D1 >= -1 && f.Z0 <= zMid && f.Z1 >= zMid));
        var wl = wallFaces.Where(f => f.N.DotProduct(Rg) > 0 && f.X1 <= sx0 + 5).OrderByDescending(f => f.X0).FirstOrDefault();
        var wr = wallFaces.Where(f => f.N.DotProduct(Rg) < 0 && f.X0 >= sx1 - 5).OrderBy(f => f.X0).FirstOrDefault();
        if (wl == null || wr == null) errors.Add("inner wall face not found on the " + (wl == null ? "left" : "right") + " (LA3 / LA4 need both)");
        string side = ((string)args["la1Side"] ?? "right").ToLower();
        F wOut = side == "right" && wr != null ? wallFaces.Where(f => f.Id == wr.Id && f.N.DotProduct(Rg) > 0).OrderByDescending(f => f.X0).FirstOrDefault()
                : side == "left" && wl != null ? wallFaces.Where(f => f.Id == wl.Id && f.N.DotProduct(Rg) < 0).OrderBy(f => f.X0).FirstOrDefault() : null;

        // ---- references at an elevation: landing top > level > floor top
        // run / landing 3D faces are often hidden in the view (dims on them are not drawn): take the faces of the
        // Stairs element itself and pick them by position (as the hand-made dims of the project do)
        var sFaces = fl.Select(f => f.S).Concat(new FilteredElementCollector(doc, V.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>())
            .GroupBy(s => s.Id.IntegerValue).ToDictionary(g => g.Key, g => Faces(g.First()));
        Func<double, double, double, double, double> ovl = (a0, a1, b0, b1) => Math.Min(a1, b1) - Math.Max(a0, b0);
        var landFaces = lds.ToDictionary(l => l.L.Id.IntegerValue, l => sFaces[l.Sid].Where(q =>
            (q.N.Z > 0.999 && Math.Abs(q.Z0 - l.Z) < 5 && ovl(q.X0, q.X1, l.X0, l.X1) > 50) ||
            (q.N.Z < -0.999 && q.Z0 < l.Z - 1 && q.Z0 > l.Z - 800 && ovl(q.X0, q.X1, l.X0, l.X1) > 50)).ToList());
        Func<Fl, List<F>> runFaces = f => sFaces[f.S.Id.IntegerValue].Where(q => ovl(q.Z0, q.Z1, f.Z0 - 5, f.Z1 + 5) > 1 && ovl(q.X0, q.X1, f.X0 - 5, f.X1 + 5) >= 0).ToList();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        var floorsTop = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Floor)).SelectMany(e => Faces(e)).Where(f => f.N.Z > 0.999 && f.D0 <= 1 && f.D1 >= -1).ToList();
        Func<double, double?, Tuple<Reference, string>> RefAt = (z, nearX) =>
        {
            var cand = lds.Where(l => Math.Abs(l.Z - z) < 3).OrderByDescending(l => l.Cut).ThenBy(l => nearX == null ? 0 : Math.Min(Math.Abs(l.X0 - nearX.Value), Math.Abs(l.X1 - nearX.Value)));
            foreach (var l in cand)
            {
                var top = landFaces[l.L.Id.IntegerValue].Where(f => f.N.Z > 0.999 && Math.Abs(f.Z0 - z) < 5).OrderByDescending(f => f.X1 - f.X0).FirstOrDefault();
                if (top != null) return Tuple.Create(top.P.Reference, "landing " + l.L.Id.IntegerValue + " top");
            }
            var lv = levels.FirstOrDefault(x => Math.Abs(x.Elevation * MM - z) < 3);
            if (lv != null) return Tuple.Create(lv.GetPlaneReference(), "level " + lv.Name);
            var fz = floorsTop.Where(f => Math.Abs(f.Z0 - z) < 5).FirstOrDefault();
            if (fz != null) return Tuple.Create(fz.P.Reference, "floor " + fz.Id + " top");
            return null;
        };

        var dimType = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["dimTypeName"] && x.StyleType == DimensionStyleType.Linear);
        if (dimType == null && parts.Any(p => p.StartsWith("LA"))) return new { Error = "dimTypeName '" + args["dimTypeName"] + "' not found (linear)" };
        var items = (JArray)args["items"] ?? new JArray();
        Func<string, string, JToken> Ov = (part, key) => items.FirstOrDefault(i => ((string)i["part"] ?? "").ToUpper() == part && ((string)i["flight"] == key || (string)i["landing"] == key));

        using (var t = new Transaction(doc, "Stair section annotate"))
        {
            t.Start();
            // ---- LA1
            if (parts.Contains("LA1"))
                try
                {
                    var zs = fl.Select(f => Math.Round(f.Z0)).Concat(fl.Select(f => Math.Round(f.Z1))).Distinct().OrderBy(z => z).ToList();
                    var ra = new ReferenceArray(); var used = new List<string>(); var miss = new List<double>();
                    foreach (var z in zs) { var r = RefAt(z, null); if (r == null) miss.Add(z); else { ra.Append(r.Item1); used.Add(Num(z) + ": " + r.Item2); } }
                    double lineX = args["la1LineMm"] != null ? (double)args["la1LineMm"] : wOut != null ? (side == "right" ? wOut.X0 + 800 : wOut.X0 - 800) : (side == "right" ? sx1 + 1500 : sx0 - 1500);
                    if (miss.Count > 0) errors.Add("LA1: no reference at " + string.Join(", ", miss.Select(Num)));
                    var d = doc.Create.NewDimension(V, Line.CreateBound(PV(lineX, zs.First()), PV(lineX, zs.Last())), ra, dimType);
                    created.Add(d.Id.IntegerValue);
                    var texts = new List<string>();
                    var segs = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().ToList() : null;
                    for (int i = 0; i + 1 < zs.Count && (segs == null ? i < 1 : i < segs.Count); i++)
                    {
                        var f = fl.Where(x => Math.Abs(x.Z0 - zs[i]) < 3 && Math.Abs(x.Z1 - zs[i + 1]) < 3).FirstOrDefault();
                        string pre = f != null ? Num(f.RiserH) + "mm x " + f.Risers + "R = " : null;
                        if (pre == null) { texts.Add(Num(zs[i + 1] - zs[i]) + " (no single flight)"); continue; }
                        if (segs != null) { segs[i].Prefix = pre; segs[i].Below = "(EQUAL RISERS)"; texts.Add(pre + Math.Round((segs[i].Value ?? 0) * MM)); }
                        else { d.Prefix = pre; d.Below = "(EQUAL RISERS)"; texts.Add(pre + Math.Round((d.Value ?? 0) * MM)); }
                    }
                    done.Add(new { Part = "LA1", Id = d.Id.IntegerValue, LineX = Math.Round(lineX), Refs = used, Texts = texts });
                }
                catch (Exception e) { errors.Add("LA1: " + e.Message); }

            // ---- LA3
            if (parts.Contains("LA3") && wl != null && wr != null)
                foreach (var f in fl.Where(x => x.State == "cut" && !((bool?)Ov("LA3", x.Label)?["skip"] ?? false)))
                    try
                    {
                        var rf = runFaces(f).Where(x => Math.Abs(x.N.DotProduct(Rg)) > 0.999).ToList();
                        Func<double, F> at = x => rf.Where(q => Math.Abs(q.X0 - x) < 3).OrderByDescending(q => q.Z1 - q.Z0).FirstOrDefault();
                        // first riser -> last riser: the top tread level with the landing belongs to the landing segment
                        double xBot = f.RisesRight ? f.X0 : f.X1, xTop = xBot + (f.RisesRight ? 1 : -1) * f.Treads * f.Depth;
                        F a = at(Math.Min(xBot, xTop)), b = at(Math.Max(xBot, xTop));
                        if (a == null || b == null) { errors.Add("LA3 " + f.Label + ": no riser face at " + (a == null ? Num(Math.Min(xBot, xTop)) : Num(Math.Max(xBot, xTop)))); continue; }
                        var ra = new ReferenceArray(); ra.Append(wl.P.Reference); ra.Append(a.P.Reference); ra.Append(b.P.Reference); ra.Append(wr.P.Reference);
                        var o = Ov("LA3", f.Label); double z = o != null && o["z"] != null ? (double)o["z"] : (f.Z0 + f.Z1) / 2;
                        var d = doc.Create.NewDimension(V, Line.CreateBound(PV(wl.X0, z), PV(wr.X0, z)), ra, dimType);
                        created.Add(d.Id.IntegerValue);
                        var segs = d.Segments.Cast<DimensionSegment>().ToList(); string pre = Num(f.Depth) + "mm x " + f.Treads + "T = ";
                        var mid = segs.OrderBy(sg => Math.Abs((sg.Value ?? 0) * MM - f.Treads * f.Depth)).First();
                        mid.Prefix = pre; mid.Below = "(EQUAL TREADS)";
                        done.Add(new { Part = "LA3", Flight = f.Label, Id = d.Id.IntegerValue, Z = Math.Round(z), Values = string.Join(" | ", segs.Select(sg => sg == mid ? pre + Math.Round((sg.Value ?? 0) * MM) : Math.Round((sg.Value ?? 0) * MM).ToString())) });
                    }
                    catch (Exception e) { errors.Add("LA3 " + f.Label + ": " + e.Message); }

            // ---- LA4: clear height, per side column: top of each slab (landing or floor at the column) -> soffit of
            //      the next slab above (landing on the same side, or a floor slab: floors between landings stop the dim)
            if (parts.Contains("LA4") && wl != null && wr != null)
            {
                double inset = args["la4InsetMm"] != null ? (double)args["la4InsetMm"] : 300, cx = (wl.X0 + wr.X0) / 2;
                var floorFaces = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Floor)).SelectMany(e => Faces(e)).Where(q => Math.Abs(q.N.Z) > 0.999 && q.D0 <= 1 && q.D1 >= -1).ToList();
                foreach (var left in new[] { true, false })
                {
                    double colX = left ? (args["la4LeftX"] != null ? (double)args["la4LeftX"] : wl.X0 + inset) : (args["la4RightX"] != null ? (double)args["la4RightX"] : wr.X0 - inset); string sideName = left ? "left" : "right";
                    // slabs: (top z, top ref, soffit z, soffit ref, name)
                    var slabs = new List<Tuple<double, Reference, double, Reference, string>>(); var xr = new Dictionary<string, double[]>();
                    foreach (var l in lds.Where(l => ((l.X0 + l.X1) / 2 < cx) == left))   // cut AND beyond landings (user 2026-10-08: a landing seen beyond also stops / starts a clear height)
                    {
                        var top = RefAt(l.Z, colX); var sof = landFaces[l.L.Id.IntegerValue].Where(q => q.N.Z < -0.999).OrderBy(q => q.Z0).FirstOrDefault();
                        if (top != null && sof != null) { slabs.Add(Tuple.Create(l.Z, top.Item1, sof.Z0, sof.P.Reference, "landing " + l.L.Id.IntegerValue)); xr["landing " + l.L.Id.IntegerValue] = new[] { l.X0, l.X1 }; }
                    }
                    foreach (var g in floorFaces.Where(q => q.X0 <= colX && q.X1 >= colX).GroupBy(q => q.Id))
                    {
                        var top = g.Where(q => q.N.Z > 0).OrderByDescending(q => q.Z0).FirstOrDefault(); var sof = g.Where(q => q.N.Z < 0).OrderBy(q => q.Z0).FirstOrDefault();
                        if (top == null || sof == null || top.Z0 < zBot - 400 || sof.Z0 > zTop + 6000) continue;
                        if (slabs.Any(s => Math.Abs(s.Item1 - top.Z0) < 50)) continue;
                        slabs.Add(Tuple.Create(top.Z0, top.P.Reference, sof.Z0, sof.P.Reference, "floor " + g.Key));
                    }
                    slabs = slabs.OrderBy(s => s.Item1).ToList();
                    if (!slabs.Any(s => Math.Abs(s.Item1 - zBot) < 50)) { var rb = RefAt(zBot, colX); if (rb != null) slabs.Insert(0, Tuple.Create(zBot, rb.Item1, double.NegativeInfinity, (Reference)null, "bottom " + rb.Item2)); }
                    for (int i = 0; i + 1 < slabs.Count; i++)
                        try
                        {
                            var b = slabs[i]; if (b.Item1 > zTop + 10) break;
                            Func<string, double[]> X = n => xr.ContainsKey(n) ? xr[n] : new[] { -1e9, 1e9 };   // floors, bottom: full width
                            var a = slabs.Skip(i + 1).FirstOrDefault(s => s.Item3 > b.Item1 + 10 && ovl(X(s.Item5)[0], X(s.Item5)[1], X(b.Item5)[0], X(b.Item5)[1]) > 0); if (a == null) continue;   // the slab above must overlap the base in plan along the view
                            var o4 = Ov("LA4", sideName + ":" + Num(b.Item1)); if (o4 != null && (bool?)o4["skip"] == true) continue;
                            double cX = o4 != null && o4["x"] != null ? (double)o4["x"] : colX;
                            var ra = new ReferenceArray(); ra.Append(b.Item2); ra.Append(a.Item4);
                            var d = doc.Create.NewDimension(V, Line.CreateBound(PV(cX, b.Item1), PV(cX, a.Item3)), ra, dimType);
                            created.Add(d.Id.IntegerValue);
                            done.Add(new { Part = "LA4", Side = sideName, Key = sideName + ":" + Num(b.Item1), From = b.Item5, To = "soffit of " + a.Item5, Id = d.Id.IntegerValue, Value = Math.Round((d.Value ?? 0) * MM), X = Math.Round(cX) });
                        }
                        catch (Exception e) { errors.Add("LA4 " + sideName + ": " + e.Message); }
                }
            }

            // ---- LA5 handrail / top rail height: landing top -> a horizontal line of the rail above the landing
            //      (references from the rail's symbol geometry, as the hand dims of the project: '<uid>:1:INSTANCE:<sym>:<n>')
            if (parts.Contains("LA5") && (bool?)args["la5Lines"] == true)   // older search for straight rail lines (rarely finds any)
            {
                var rails = new FilteredElementCollector(doc, V.Id).WhereElementIsNotElementType()
                    .Where(e => e.Category != null && (e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RailingHandRail || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RailingTopRail)).ToList();
                var only = args["la5RailIds"] != null ? new HashSet<int>(((JArray)args["la5RailIds"]).Select(x => (int)x)) : null;
                foreach (var rl in rails.Where(e => only == null || only.Contains(e.Id.IntegerValue)))
                {
                    var lines = new List<Tuple<Reference, double, double, double>>(); // ref, z elev, x0, x1
                    var st = new Stack<Tuple<GeometryElement, Transform>>(); var dbg = new Dictionary<string, int>();
                    st.Push(Tuple.Create(rl.get_Geometry(new Options { ComputeReferences = true, View = V }), Transform.Identity));
                    st.Push(Tuple.Create(rl.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine }), Transform.Identity));
                    while (st.Count > 0)
                    {
                        var cur = st.Pop(); if (cur.Item1 == null) continue;
                        foreach (var g in cur.Item1)
                        {
                            if (g is GeometryInstance gi) { st.Push(Tuple.Create(gi.GetSymbolGeometry(), cur.Item2.Multiply(gi.Transform))); continue; }
                            { var k = g.GetType().Name + (g is Curve cc ? (cc.Reference != null ? "+ref" : "-ref") : ""); dbg[k] = dbg.ContainsKey(k) ? dbg[k] + 1 : 1; if (g is Solid sd0) { int ne = 0, nl = 0; foreach (Edge ed0 in sd0.Edges) { ne++; if (ed0.AsCurve() is Line) nl++; } dbg["edges"] = (dbg.ContainsKey("edges") ? dbg["edges"] : 0) + ne; dbg["lineEdges"] = (dbg.ContainsKey("lineEdges") ? dbg["lineEdges"] : 0) + nl; } }
                            var cs = new List<Tuple<Curve, Reference>>();
                            if (g is Curve c0 && c0.Reference != null) cs.Add(Tuple.Create(c0, c0.Reference));
                            if (g is Solid so) foreach (Edge ed in so.Edges) if (ed.Reference != null) cs.Add(Tuple.Create(ed.AsCurve(), ed.Reference));
                            foreach (var cr in cs)
                            {
                                var ln = cr.Item1 as Line; if (ln == null) continue;
                                XYZ a = cur.Item2.OfPoint(ln.GetEndPoint(0)), b = cur.Item2.OfPoint(ln.GetEndPoint(1));
                                var dv = (b - a).Normalize(); if (Math.Abs(dv.Z) > 0.001) continue;   // horizontal (along the view, or across it: the rail at a landing edge)
                                lines.Add(Tuple.Create(cr.Item2, ZE(a), Math.Min(VX(a), VX(b)), Math.Max(VX(a), VX(b))));
                            }
                        }
                    }
                    if (mode == "preview") done.Add(new { Part = "LA5 lines", Rail = rl.Id.IntegerValue, Geo = string.Join(", ", dbg.Select(kv => kv.Key + " " + kv.Value)), Lines = lines.Select(q => "z " + Num(q.Item2) + " x " + Num(q.Item3) + ".." + Num(q.Item4)).Distinct().Take(12).ToList() });
                    foreach (var l in lds.Where(x => x.Cut))
                    {
                        var hit = lines.Where(q => q.Item2 > l.Z + 600 && q.Item2 < l.Z + 1400 && ovl(q.Item3, q.Item4, l.X0, l.X1) >= -5).OrderByDescending(q => q.Item2).FirstOrDefault();
                        if (hit == null) continue;
                        var o5 = Ov("LA5", Num(l.Z)); if (o5 != null && (bool?)o5["skip"] == true) { done.Add(new { Part = "LA5", Landing = Num(l.Z), Skipped = "items: skip" }); continue; }
                        try
                        {
                            var r0 = RefAt(l.Z, (hit.Item3 + hit.Item4) / 2); if (r0 == null) continue;
                            double x = Math.Max(hit.Item3, l.X0) + Math.Min(150, (Math.Min(hit.Item4, l.X1) - Math.Max(hit.Item3, l.X0)) / 2);
                            var o = Ov("LA5", Num(l.Z)); if (o != null && o["x"] != null) x = (double)o["x"];
                            var ra = new ReferenceArray(); ra.Append(r0.Item1); ra.Append(hit.Item1);
                            var d = doc.Create.NewDimension(V, Line.CreateBound(PV(x, l.Z), PV(x, hit.Item2)), ra, dimType);
                            created.Add(d.Id.IntegerValue);
                            done.Add(new { Part = "LA5", Rail = rl.Id.IntegerValue, Landing = Num(l.Z), Id = d.Id.IntegerValue, Value = Math.Round((d.Value ?? 0) * MM), X = Math.Round(x), RailLineX = Num(hit.Item3) + ".." + Num(hit.Item4) });
                        }
                        catch (Exception e) { errors.Add("LA5 rail " + rl.Id.IntegerValue + " landing " + Num(l.Z) + ": " + e.Message); }
                    }
                }
            }

            // ---- LA5 from a hand dim of the user (la5FromDimId): its rail reference '<rail>:1:INSTANCE:<symbol>:<n>' is reused
            //      with other <n> until a dim from the landing top reads the same value and is drawn (rails without
            //      straight lines in the API geometry, e.g. a round top rail running across the view at a landing edge)
            // ---- LA5 rail height at each landing (mandatory, drafting-stair-section-parallel.md LA5): landing top -> top of the
            //      rail running along the landing edge. Round rails have no straight line in the API geometry; the line the
            //      user picks is the symbol reference '<rail UniqueId>:1:INSTANCE:<symbol UniqueId>:<n>'. The head comes from a
            //      hand dim (la5FromDimId: value to match) or, without one, from the top rails of la5RailingIds / every railing in
            //      the view (la5Auto: the highest value within la5MinMm..la5MaxMm that is drawn). Landings: items {part:'LA5',
            //      landing, x} (x of the dim line), else every cut landing at la5LeftX / la5RightX.
            if (parts.Contains("LA5") && (args["la5FromDimId"] != null || (bool?)args["la5Auto"] == true))
            {
                var heads = new List<string>(); double want = double.NaN;
                double vMin = (double?)args["la5MinMm"] ?? 850, vMax = (double?)args["la5MaxMm"] ?? 1400; double? target = (double?)args["la5TargetMm"];   // nominal rail height (e.g. 1200): nearest wins
                if (args["la5FromDimId"] != null)
                {
                    var src = doc.GetElement(new ElementId((int)args["la5FromDimId"])) as Dimension;
                    foreach (Reference r in src.References) { var e = doc.GetElement(r.ElementId); if (e != null && e.Category != null && (e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RailingTopRail || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RailingHandRail)) { var rep = r.ConvertToStableRepresentation(doc); heads.Add(rep.Substring(0, rep.LastIndexOf(':') + 1)); } }
                    want = (src.Value ?? 0) * MM;
                }
                else
                {
                    var railIds = args["la5RailingIds"] != null ? ((JArray)args["la5RailingIds"]).Select(x => new ElementId((int)x)).ToList()
                        : new FilteredElementCollector(doc, V.Id).OfClass(typeof(Railing)).ToElementIds().ToList();
                    foreach (var rid in railIds)
                    {
                        var rl = doc.GetElement(rid) as Railing; if (rl == null || rl.TopRail == ElementId.InvalidElementId) continue;
                        var tr = doc.GetElement(rl.TopRail); if (tr == null) continue;
                        foreach (var g in tr.get_Geometry(new Options { ComputeReferences = true, View = V }) ?? Enumerable.Empty<GeometryObject>())
                            if (g is GeometryInstance gi && gi.Symbol != null) heads.Add(tr.UniqueId + ":1:INSTANCE:" + gi.Symbol.UniqueId + ":");
                    }
                    heads = heads.Distinct().ToList();
                }
                if (heads.Count == 0) errors.Add("LA5: no rail reference head (no sample dim / no top rail with a symbol instance)");
                int n0 = args["la5IndexFrom"] != null ? (int)args["la5IndexFrom"] : 1, n1 = args["la5IndexTo"] != null ? (int)args["la5IndexTo"] : 1500;
                double inset = 300, cx5 = wl != null && wr != null ? (wl.X0 + wr.X0) / 2 : 0;
                foreach (var l in lds.Where(x => x.Cut))
                {
                    var o5 = Ov("LA5", Num(l.Z)); if (o5 != null && (bool?)o5["skip"] == true) continue;
                    double x;
                    if (o5 != null && o5["x"] != null) x = (double)o5["x"];
                    else if ((l.X0 + l.X1) / 2 < cx5 && args["la5LeftX"] != null) x = (double)args["la5LeftX"];
                    else if ((l.X0 + l.X1) / 2 >= cx5 && args["la5RightX"] != null) x = (double)args["la5RightX"];
                    else if (args["la5FromDimId"] != null) continue;   // sample mode: only the landings listed
                    else x = (l.X0 + l.X1) / 2 < cx5 ? l.X1 - inset : l.X0 + inset;
                    var r0 = RefAt(l.Z, x); if (r0 == null) { errors.Add("LA5 " + Num(l.Z) + ": no landing reference"); continue; }
                    string bestRef = null; double bestVal = double.NaN;
                    foreach (var head in heads)
                        for (int n = n0; n <= n1; n++)
                        {
                            Reference rr; try { rr = Reference.ParseFromStableRepresentation(doc, head + n); } catch { continue; }
                            using (var sub = new SubTransaction(doc))
                            {
                                sub.Start(); double val = double.NaN;
                                try
                                {
                                    var ra = new ReferenceArray(); ra.Append(r0.Item1); ra.Append(rr);
                                    var d = doc.Create.NewDimension(V, Line.CreateBound(PV(x, l.Z), PV(x, l.Z + 1000)), ra, dimType);
                                    doc.Regenerate();
                                    if (d.get_BoundingBox(V) != null) val = (d.Value ?? 0) * MM;
                                }
                                catch { }
                                sub.RollBack();
                                if (double.IsNaN(val)) continue;
                                bool fits = !double.IsNaN(want) ? Math.Abs(val - want) < 2 : val >= vMin && val <= vMax;
                                bool better = double.IsNaN(bestVal) || (target != null ? Math.Abs(val - target.Value) < Math.Abs(bestVal - target.Value) : val > bestVal);
                                if (fits && double.IsNaN(want) && better) { bestVal = val; bestRef = head + n; }
                                if (fits && !double.IsNaN(want)) { bestVal = val; bestRef = head + n; break; }
                            }
                        }
                    if (bestRef == null) { errors.Add("LA5 " + Num(l.Z) + ": no rail line " + (!double.IsNaN(want) ? "reads " + Math.Round(want) : "between " + vMin + " and " + vMax) + " above this landing"); continue; }
                    var raF = new ReferenceArray(); raF.Append(r0.Item1); raF.Append(Reference.ParseFromStableRepresentation(doc, bestRef));
                    var dF = doc.Create.NewDimension(V, Line.CreateBound(PV(x, l.Z), PV(x, l.Z + bestVal)), raF, dimType);
                    created.Add(dF.Id.IntegerValue);
                    done.Add(new { Part = "LA5", Landing = Num(l.Z), Id = dF.Id.IntegerValue, Value = Math.Round((dF.Value ?? 0) * MM), X = Math.Round(x), Ref = bestRef.Substring(bestRef.LastIndexOf(':') + 1) });
                }
            }

            // ---- LB1 run tags
            if (parts.Contains("LB1"))
            {
                var map = ((JArray)args["runTagTypes"] ?? new JArray()).Select(j => new { From = (double)j["fromMm"], Type = (string)j["type"] }).OrderBy(j => j.From).ToList();
                var tagSyms = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StairsRunTags).ToList();
                var tagged = new HashSet<int>(new FilteredElementCollector(doc, V.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().SelectMany(x => x.GetTaggedLocalElementIds()).Select(x => x.IntegerValue));
                foreach (var f in fl.Where(x => ((string)args["tagFlights"] ?? "cut") == "all" || x.State == "cut"))
                    try
                    {
                        if (tagged.Contains(f.R.Id.IntegerValue)) continue;
                        var tn = map.LastOrDefault(m => m.From <= f.Z0 + 1)?.Type; var sym = tagSyms.FirstOrDefault(x => x.Name == tn);
                        if (sym == null) { errors.Add("LB1 " + f.Label + ": no run tag type '" + tn + "'"); continue; }
                        var o = Ov("LB1", f.Label);
                        double zh = o != null && o["z"] != null ? (double)o["z"] : f.Z0 + 0.3 * (f.Z1 - f.Z0);
                        // soffit: the run's sloped face facing down; x where it meets the height zh in the view plane
                        var sl = runFaces(f).Where(q => q.N.Z < -0.1 && q.N.Z > -0.99 && q.D0 <= 1 && q.D1 >= -1 && (q.N.DotProduct(Rg) > 0) == f.RisesRight && q.X0 >= f.X0 - 600 && q.X1 <= f.X1 + 600).OrderByDescending(q => (q.X1 - q.X0)).FirstOrDefault();
                        double xs;
                        if (sl != null)
                        {
                            var p0 = PV(0, zh); double k = sl.N.DotProduct(Rg / MM);
                            xs = Math.Abs(k) > 1e-9 ? -sl.N.DotProduct(p0 - sl.P.Origin) / k : (f.X0 + f.X1) / 2;
                        }
                        else xs = double.NaN;
                        if (double.IsNaN(xs) || xs < f.X0 - 600 || xs > f.X1 + 600)   // no soffit found: the nosing line
                            xs = f.RisesRight ? f.X0 + (zh - f.Z0) / (f.Z1 - f.Z0) * (f.X1 - f.X0) : f.X1 - (zh - f.Z0) / (f.Z1 - f.Z0) * (f.X1 - f.X0);
                        double dir = f.RisesRight ? 1 : -1;   // under the flight: right of the soffit when it rises to the right
                        double hx = o != null && o["x"] != null ? (double)o["x"] : xs + dir * 1100;
                        var tg = IndependentTag.Create(doc, sym.Id, V.Id, new Reference(f.R), true, TagOrientation.Horizontal, PV(hx, zh));
                        tg.LeaderEndCondition = LeaderEndCondition.Free;
                        tg.SetLeaderEnd(new Reference(f.R), PV(xs, zh));
                        tg.TagHeadPosition = PV(hx, zh);
                        created.Add(tg.Id.IntegerValue);
                        done.Add(new { Part = "LB1", Flight = f.Label, Id = tg.Id.IntegerValue, Type = tn, Text = tg.TagText, Head = Math.Round(hx) + "," + Math.Round(zh), LeaderEndX = Math.Round(xs) });
                    }
                    catch (Exception e) { errors.Add("LB1 " + f.Label + ": " + e.Message); }
            }

            // ---- LB3 spots on cut landings
            if (parts.Contains("LB3"))
            {
                var st = new FilteredElementCollector(doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().FirstOrDefault(x => x.Name == (string)args["spotTypeName"] && x.StyleType == DimensionStyleType.SpotElevation);
                var have = new FilteredElementCollector(doc, V.Id).OfClass(typeof(SpotDimension)).Cast<SpotDimension>().Select(sd => { try { var p = sd.Origin; return ZE(p); } catch { return double.NaN; } }).ToList();
                if (st == null) errors.Add("LB3: no spot elevation type '" + args["spotTypeName"] + "'");
                else
                    foreach (var l in lds.Where(x => x.Cut))
                        try
                        {
                            string key = Num(l.Z);
                            if (have.Any(h => Math.Abs(h - l.Z) < 5)) { done.Add(new { Part = "LB3", Landing = key, Skipped = "a spot already at this elevation" }); continue; }
                            // near the flight end of the landing
                            var near = fl.Where(f => Math.Abs(f.Z0 - l.Z) < 3 || Math.Abs(f.Z1 - l.Z) < 3).Select(f => Math.Abs(f.X0 - l.X1) < Math.Abs(f.X1 - l.X0) ? f.X0 : f.X1).ToList();
                            var o = Ov("LB3", key);
                            double x = o != null && o["x"] != null ? (double)o["x"] : near.Count > 0 ? (Math.Abs(near[0] - l.X1) < Math.Abs(near[0] - l.X0) ? l.X1 - 250 : l.X0 + 250) : (l.X0 + l.X1) / 2;
                            var top = landFaces[l.L.Id.IntegerValue].Where(q => q.N.Z > 0.999 && q.X0 <= x + 1 && q.X1 >= x - 1 && q.D0 <= 1 && q.D1 >= -1).OrderByDescending(q => q.Z0).FirstOrDefault()
                                ?? landFaces[l.L.Id.IntegerValue].Where(q => q.N.Z > 0.999).OrderByDescending(q => q.X1 - q.X0).FirstOrDefault();
                            if (top == null) { errors.Add("LB3 " + key + ": no top face"); continue; }
                            if (x < top.X0 || x > top.X1) x = Math.Max(top.X0 + 50, Math.Min(top.X1 - 50, x));
                            var pt = PV(x, top.Z0);
                            var sd2 = doc.Create.NewSpotElevation(V, top.P.Reference, pt, pt, pt, pt, false);
                            sd2.ChangeTypeId(st.Id);
                            created.Add(sd2.Id.IntegerValue);
                            done.Add(new { Part = "LB3", Landing = key, Id = sd2.Id.IntegerValue, X = Math.Round(x) });
                        }
                        catch (Exception e) { errors.Add("LB3 " + Num(l.Z) + ": " + e.Message); }
            }

            // ---- LC riser numbers on cut flights
            if (parts.Contains("LC"))
            {
                var src = args["numberSourceId"] != null ? doc.GetElement(new ElementId((int)args["numberSourceId"])) as NumberSystem : null;
                var numbered = new HashSet<int>();
                foreach (var ns in new FilteredElementCollector(doc, V.Id).OfClass(typeof(NumberSystem)).Cast<NumberSystem>()) try { numbered.Add(ns.NumberedElementId.HostElementId.IntegerValue); } catch { }
                var opt = StairsNumberSystemReferenceOption.Right;
                if (src != null) try { opt = (StairsNumberSystemReferenceOption)src.get_Parameter(BuiltInParameter.NUMBER_SYSTEM_REFERENCE).AsInteger(); } catch { }
                var copied = new[] { BuiltInParameter.NUMBER_SYSTEM_DISPLAY_RULE, BuiltInParameter.NUMBER_SYSTEM_TEXT_SIZE, BuiltInParameter.NUMBER_SYSTEM_JUSTIFY, BuiltInParameter.NUMBER_SYSTEM_JUSTIFY_OFFSET, BuiltInParameter.NUMBER_SYSTEM_ORIENTATION, BuiltInParameter.NUMBER_SYSTEM_TAG_TYPE, BuiltInParameter.NUMBER_SYSTEM_REFERENCE_OFFSET };
                foreach (var f in fl.Where(x => x.State == "cut" && !numbered.Contains(x.R.Id.IntegerValue)))
                    try
                    {
                        var ns = NumberSystem.Create(doc, V.Id, new LinkElementId(f.R.Id), f.R.GetNumberSystemReference(opt));
                        if (src != null)
                        {
                            if (ns.GetTypeId() != src.GetTypeId()) ns.ChangeTypeId(src.GetTypeId());
                            foreach (var b in copied)
                            {
                                var ps = src.get_Parameter(b); var pt = ns.get_Parameter(b); if (ps == null || pt == null || pt.IsReadOnly) continue;
                                if (ps.StorageType == StorageType.Integer) pt.Set(ps.AsInteger()); else if (ps.StorageType == StorageType.Double) pt.Set(ps.AsDouble());
                            }
                        }
                        created.Add(ns.Id.IntegerValue);
                        done.Add(new { Part = "LC", Flight = f.Label, Id = ns.Id.IntegerValue, Risers = f.Risers, Reference = opt.ToString() });
                    }
                    catch (Exception e) { errors.Add("LC " + f.Label + ": " + e.Message); }
            }

            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else t.RollBack();
        }
        return new
        {
            View = V.Name, Mode = mode,
            Walls = new { LeftInner = wl == null ? (double?)null : Math.Round(wl.X0), RightInner = wr == null ? (double?)null : Math.Round(wr.X0), Outer = wOut == null ? (double?)null : Math.Round(wOut.X0) },
            Flights = fl.Select(f => f.Label + " " + f.State + " " + Num(f.Z0) + "→" + Num(f.Z1) + " x " + Num(f.X0) + ".." + Num(f.X1) + (f.RisesRight ? " up-right" : " up-left")).ToList(),
            Landings = lds.Select(l => Num(l.Z) + (l.Cut ? " cut " : " beyond ") + Num(l.X0) + ".." + Num(l.X1)).ToList(),
            Done = done, Errors = errors
        };
    }
}
