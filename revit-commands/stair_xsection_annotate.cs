/* mcp-tool
{
  "description": "ONE stair section across the flights: XA1 band heights, XA3 widths to handrails, XA4 clear | thickness chain, XA5 wall to wall, XA6 handrail heights. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" },
      "parts": { "type": "array", "items": { "type": "string" }, "description": "XA1 XA3 XA4 XA5 XA6 (default all; one card = one part)" },
      "dimTypeName": { "type": "string" },
      "excludeStairIds": { "type": "array", "items": { "type": "number" }, "description": "stairs seen but not part of this core" },
      "xa1Side": { "type": "string", "enum": ["left", "right"], "description": "default right (XA2 levels go on the other side)" },
      "xa1LineMm": { "type": "number", "description": "view x of the XA1 chain (default outer wall face + 800)" },
      "xa4X": { "type": "number", "description": "view x of the XA4 column (default middle of the half with 'away' flights)" },
      "xa5LineMm": { "type": "number", "description": "elevation mm of the XA5 chain (default lowest slab - 1000)" },
      "xa6WallMm": { "type": "number", "description": "XA6 handrail height on a wall, default 900" },
      "xa6OpenMm": { "type": "number", "description": "XA6 rail height at an opening, default 1200" },
      "railingIds": { "type": "array", "items": { "type": "number" }, "description": "XA3 / XA6: only these railings" },
      "items": { "type": "array", "items": { "type": "object" }, "description": "overrides: {part, key, x, z, skip} (key as reported in Done)" }
    },
    "required": ["viewId", "mode"]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Stair SECTION cut ACROSS the flights (drafting-stair-section-cross.md), ONE view, host stairs only. x = mm along the
//  view's Right from the view origin; z = elevation mm (as the flights' From / To EL). References on the Stairs element,
//  Floors, walls, Handrails (run / landing 3D faces are hidden in the view: dims on them are not drawn), never a link.
//  XA1  one vertical chain: the lowest flight foot -> each band boundary (a band = one elevation range of flights, a
//       scissor pair counts once) -> top band -> roof slab top (no formula on that segment); each band segment prefix
//       'riser x R = ' + below '(EQUAL RISERS)'; + the overall foot -> roof one line further out. Outside the wall on
//       xa1Side. Skipped when every band already has its segment with the right prefix.
//  XA3  per stair block (stairs grouped by overlapping elevation): one horizontal chain in the free space above its
//       lowest cut slab (top + 1800): per lane between two wall faces, wall -> the lane-facing edge of the handrail on
//       each side (when there is one, within 300 mm of the wall) -> wall. Skipped when a width chain with handrail
//       references already sits in that block.
//  XA4  one vertical chain at the column (xa4X, default the middle of the half with 'away' flights): top of the lowest
//       slab -> soffit / top of each slab cut through the column -> roof soffit / top (clear | thickness alternating).
//  XA5  below the section: every face of the walls cut at the lowest storey (+ host grids between them) + the overall
//       outer -> outer one line further down; on top one overall between the outer faces of the top storey's walls.
//       Each skipped when a horizontal dim already sits below the lowest slab / above the top.
//  XA6  (mandatory) per cut landing / floor and each handrail cut 500..1500 above it: slab top -> top of the handrail,
//       references from the handrail's symbol geometry ('<uid>:1:INSTANCE:<sym>:<n>', as LA5), the drawn value
//       nearest xa6WallMm (handrail within 250 mm of a wall face) or xa6OpenMm (else), within 150 of it. Existing rail
//       dims on that slab near that rail are kept.
//  mode preview (rolled back; values, keys, positions) | apply (logPath: created ids) | undo (logPath: deletes them).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairXsectionAnnotate
{
    const double MM = 304.8;
    static XYZ O, Rg, Vd; static View V; static double ElOff;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double Dp(XYZ p) { return (p - O).DotProduct(Vd) * MM; }
    static double ZE(XYZ p) { return (p.Z - ElOff) * MM; }
    static XYZ PV(double x, double z) { return new XYZ(O.X, O.Y, ElOff + z / MM) + Rg * (x / MM); }
    static string Num(double x) { return Math.Round(x, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }

    class Fl { public StairsRun R; public Stairs S; public string Label, Facing, Half; public double El0, El1, X0, X1, RiserH; public int Risers; }
    class F { public PlanarFace P; public double X0, X1, Z0, Z1, D0, D1; public XYZ N; public int Id; public string Kind; }
    class Slab { public F Top, Soffit; public double X0, X1; }

    static List<F> Faces(Element e, string kind)
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
                        res.Add(new F { P = pf, N = pf.FaceNormal, Id = e.Id.IntegerValue, Kind = kind, X0 = pts.Min(VX), X1 = pts.Max(VX), Z0 = pts.Min(ZE), Z1 = pts.Max(ZE), D0 = pts.Min(Dp), D1 = pts.Max(Dp) });
                    }
                }
            }
            if (res.Count > 0) break;
        }
        return res;
    }
    static bool Cut(F f) { return f.D0 <= 1 && f.D1 >= -1; }   // crosses the section plane

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); var gone = new List<int>();
            using (var t = new Transaction(doc, "Undo stair cross-section annotate"))
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
        var parts = args["parts"] != null ? ((JArray)args["parts"]).Select(x => ((string)x).ToUpper()).ToList() : new List<string> { "XA1", "XA3", "XA4", "XA5", "XA6" };
        var errors = new List<string>(); var done = new List<object>(); var created = new List<int>();
        var items = (JArray)args["items"] ?? new JArray();
        Func<string, string, JToken> Ov = (part, key) => items.FirstOrDefault(i => ((string)i["part"] ?? "").ToUpper() == part && (string)i["key"] == key);
        Func<string, string, bool> Skip = (part, key) => { var o = Ov(part, key); return o != null && (bool?)o["skip"] == true; };

        // ---- flights (as stair_xsection_info)
        var fl = new List<Fl>(); var stairs = new List<Stairs>(); bool elSet = false;
        var excl = new HashSet<int>(((JArray)args["excludeStairIds"] ?? new JArray()).Select(x => (int)x));
        foreach (var s in new FilteredElementCollector(doc, V.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>().Where(s => !excl.Contains(s.Id.IntegerValue)))
        {
            var pl = s.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            var bl = pl != null && pl.StorageType == StorageType.ElementId ? doc.GetElement(pl.AsElementId()) as Level : null; if (bl == null) continue;
            if (!elSet) { ElOff = bl.ProjectElevation - bl.Elevation; elSet = true; }
            double sb = bl.Elevation + (s.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET)?.AsDouble() ?? 0);
            stairs.Add(s);
            foreach (var rid in s.GetStairsRuns())
            {
                var r = doc.GetElement(rid) as StairsRun; if (r == null) continue;
                var fp = new List<XYZ>(); try { foreach (Curve c in r.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count == 0 || fp.Max(Dp) > 0 && fp.Min(Dp) > 0) continue;   // in front of the cut
                var f = new Fl { R = r, S = s, El0 = (sb + r.BaseElevation) * MM, El1 = (sb + r.TopElevation) * MM, X0 = fp.Min(VX), X1 = fp.Max(VX), Risers = r.ActualRisersNumber, RiserH = s.ActualRiserHeight * MM };
                try
                {
                    var pc = r.GetStairsPath().Cast<Curve>().ToList(); var dir = pc.Last().GetEndPoint(1) - pc.First().GetEndPoint(0); dir = new XYZ(dir.X, dir.Y, 0);
                    double along = dir.GetLength() < 1e-6 ? 0 : dir.Normalize().DotProduct(Vd);
                    f.Facing = Math.Abs(along) < 0.7 ? "sideways" : along < 0 ? "away" : "toward";
                }
                catch { f.Facing = "?"; }
                fl.Add(f);
            }
        }
        fl = fl.OrderBy(f => f.El0).ThenBy(f => f.X0).ToList(); for (int i = 0; i < fl.Count; i++) fl[i].Label = "F" + (i + 1);
        if (fl.Count == 0) return new { View = V.Name, Error = "no host stair flight seen in this section" };
        if (fl.All(f => f.Facing == "sideways")) return new { View = V.Name, Error = "every flight runs across the view: a section along the flights -> stair_section_annotate" };
        double cx0 = fl.Min(f => f.X0), cx1 = fl.Max(f => f.X1), mid = (cx0 + cx1) / 2, zBot = fl.Min(f => f.El0), zTop = fl.Max(f => f.El1);
        foreach (var f in fl) f.Half = (f.X0 + f.X1) / 2 < mid ? "left" : "right";
        var bands = fl.GroupBy(f => Math.Round(f.El0) + "|" + Math.Round(f.El1)).Select(g => g.ToList()).OrderBy(g => g[0].El0).ToList();

        // ---- faces: stairs + floors (horizontal, cut), walls (vertical, cut), handrails
        var hFaces = new List<F>();
        foreach (var s in stairs) hFaces.AddRange(Faces(s, "stairs").Where(q => Math.Abs(q.N.Z) > 0.999 && Cut(q)));
        foreach (var e in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Floor)))
            hFaces.AddRange(Faces(e, "floor").Where(q => Math.Abs(q.N.Z) > 0.999 && Cut(q) && q.X1 > cx0 - 50 && q.X0 < cx1 + 50 && q.Z0 > zBot - 1000 && q.Z0 < zTop + 8000));
        var wFaces = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Wall)).Cast<Wall>().SelectMany(w => Faces(w, "wall"))
            .Where(q => Math.Abs(q.N.DotProduct(Rg)) > 0.999 && Cut(q) && q.X1 > cx0 - 2500 && q.X0 < cx1 + 2500).ToList();
        Func<double, List<F>> wallsAt = z => wFaces.Where(q => q.Z0 <= z && q.Z1 >= z).OrderBy(q => q.X0).ToList();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        // slab top reference at an elevation, preferring one covering x
        Func<double, double, Tuple<Reference, string>> RefTop = (z, x) =>
        {
            var c = hFaces.Where(q => q.N.Z > 0 && Math.Abs(q.Z0 - z) < 5).OrderBy(q => q.X0 <= x && q.X1 >= x ? 0 : 1).ThenBy(q => q.Kind == "stairs" ? 0 : 1).FirstOrDefault();
            if (c != null) return Tuple.Create(c.P.Reference, c.Kind + " " + c.Id + " top");
            var lv = levels.FirstOrDefault(l => Math.Abs(l.Elevation * MM - z) < 3);
            return lv != null ? Tuple.Create(lv.GetPlaneReference(), "level " + lv.Name) : null;
        };
        // slabs cut through a column x: top face + lowest soffit within 800 below it; inner faces of a slab dropped
        Func<double, List<Slab>> slabsAt = x =>
        {
            var at = hFaces.Where(q => q.X0 <= x + 1 && q.X1 >= x - 1).ToList();
            var tops = at.Where(q => q.N.Z > 0).OrderByDescending(q => q.Z0).ToList(); var res = new List<Slab>();
            foreach (var t in tops)
            {
                if (res.Any(sl => t.Z0 < sl.Top.Z0 + 1 && t.Z0 > (sl.Soffit != null ? sl.Soffit.Z0 : sl.Top.Z0) - 1)) continue;   // inside a slab already listed
                var so = at.Where(q => q.N.Z < 0 && q.Z0 < t.Z0 - 30 && q.Z0 > t.Z0 - 800).OrderBy(q => q.Z0).FirstOrDefault();
                res.Add(new Slab { Top = t, Soffit = so, X0 = t.X0, X1 = t.X1 });
            }
            return res.OrderBy(sl => sl.Top.Z0).ToList();
        };
        var dimType = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["dimTypeName"] && x.StyleType == DimensionStyleType.Linear);
        if (dimType == null) return new { Error = "dimTypeName '" + args["dimTypeName"] + "' not found (linear)" };
        double gap = 7 * V.Scale;
        var dims = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => !(d is SpotDimension) && d.Curve is Line).ToList();
        Func<Dimension, bool> isVert = d => Math.Abs(((Line)d.Curve).Direction.Z) > 0.999;
        Func<Dimension, IEnumerable<Element>> refEls = d => d.References.Cast<Reference>().Select(r => doc.GetElement(r.ElementId)).Where(e => e != null);
        Func<Element, bool> isRail = e => e.Category != null && new[] { (int)BuiltInCategory.OST_RailingTopRail, (int)BuiltInCategory.OST_RailingHandRail, (int)BuiltInCategory.OST_StairsRailing }.Contains(e.Category.Id.IntegerValue);
        Func<Dimension, List<double>> segVals = d => d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(g => (g.Value ?? 0) * MM).ToList() : new List<double> { (d.Value ?? 0) * MM };
        Func<Dimension, string> valText = d => string.Join(" | ", segVals(d).Select(v => Math.Round(v).ToString()));
        Func<List<Tuple<double, Reference>>, bool, double, Dimension> make = (refs, vertical, line) =>
        {
            var ra = new ReferenceArray(); foreach (var q in refs) ra.Append(q.Item2);
            var ln = vertical ? Line.CreateBound(PV(line, refs.First().Item1), PV(line, refs.Last().Item1)) : Line.CreateBound(PV(refs.First().Item1, line), PV(refs.Last().Item1, line));
            var d = doc.Create.NewDimension(V, ln, ra, dimType); created.Add(d.Id.IntegerValue); return d;
        };

        using (var t = new Transaction(doc, "Stair cross-section annotate"))
        {
            t.Start();
            var wMid = wallsAt((zBot + zTop) / 2);
            string side = ((string)args["xa1Side"] ?? "right").ToLower();

            // ---- XA1 band heights
            if (parts.Contains("XA1"))
                try
                {
                    Func<string, string> norm = x => (x ?? "").Replace(" ", "").ToLowerInvariant();
                    bool all = bands.All(b =>
                    {
                        double val = b[0].El1 - b[0].El0; string pre = Num(b[0].RiserH) + "mm x " + b[0].Risers + "R = ";
                        return dims.Where(isVert).Any(d => d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Any(g => Math.Abs((g.Value ?? 0) * MM - val) < 1 && norm(g.Prefix) == norm(pre)) : Math.Abs((d.Value ?? 0) * MM - val) < 1 && norm(d.Prefix) == norm(pre));
                    });
                    if (all) done.Add(new { Part = "XA1", Skipped = "every band already has its segment" });
                    else
                    {
                        var zs = bands.Select(b => Math.Round(b[0].El0)).Concat(bands.Select(b => Math.Round(b[0].El1))).Distinct().OrderBy(z => z).ToList();
                        var roof = slabsAt(mid).Where(sl => sl.Top.Z0 > zTop + 100).Select(sl => (double?)sl.Top.Z0).FirstOrDefault();
                        if (roof != null) zs.Add(Math.Round(roof.Value));
                        var refs = new List<Tuple<double, Reference>>(); var miss = new List<string>();
                        foreach (var z in zs) { var r = RefTop(z, mid); if (r == null) miss.Add(Num(z)); else refs.Add(Tuple.Create(z, r.Item1)); }
                        if (miss.Count > 0) errors.Add("XA1: no reference at " + string.Join(", ", miss));
                        var wo = side == "right" ? wMid.Where(q => q.X0 > cx1 - 5).OrderByDescending(q => q.X0).FirstOrDefault() : wMid.Where(q => q.X1 < cx0 + 5).OrderBy(q => q.X0).FirstOrDefault();
                        double lineX = args["xa1LineMm"] != null ? (double)args["xa1LineMm"] : wo != null ? wo.X0 + (side == "right" ? 800 : -800) : (side == "right" ? cx1 + 1500 : cx0 - 1500);
                        var d = make(refs, true, lineX);
                        var sg = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().ToList() : null;
                        for (int i = 0; i + 1 < refs.Count; i++)
                        {
                            var b = bands.FirstOrDefault(q => Math.Abs(q[0].El0 - refs[i].Item1) < 3 && Math.Abs(q[0].El1 - refs[i + 1].Item1) < 3); if (b == null) continue;   // roof segment: no formula
                            string pre = Num(b[0].RiserH) + "mm x " + b[0].Risers + "R = ";
                            if (sg != null && i < sg.Count) { sg[i].Prefix = pre; sg[i].Below = "(EQUAL RISERS)"; } else if (sg == null) { d.Prefix = pre; d.Below = "(EQUAL RISERS)"; }
                        }
                        var ov = make(new List<Tuple<double, Reference>> { refs.First(), refs.Last() }, true, lineX + (side == "right" ? gap : -gap));
                        done.Add(new { Part = "XA1", Id = d.Id.IntegerValue, OverallId = ov.Id.IntegerValue, X = Math.Round(lineX), Values = valText(d), Overall = valText(ov) });
                    }
                }
                catch (Exception e) { errors.Add("XA1: " + e.Message); }

            // ---- XA3 widths to handrails, per stair block
            var rails = new List<F>();
            if (parts.Contains("XA3") || parts.Contains("XA6"))
            {
                var only = args["railingIds"] != null ? new HashSet<int>(((JArray)args["railingIds"]).Select(x => (int)x)) : null;
                foreach (var rl in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Railing)).Cast<Railing>().Where(r => only == null || only.Contains(r.Id.IntegerValue)))
                {
                    var els = new List<Element>();
                    try { var tr = doc.GetElement(rl.TopRail); if (tr != null) els.Add(tr); } catch { }
                    try { foreach (var h in rl.GetHandRails()) { var he = doc.GetElement(h); if (he != null) els.Add(he); } } catch { }
                    foreach (var e in els) rails.AddRange(Faces(e, e.Category != null && e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RailingTopRail ? "top rail" : "handrail").Where(q => Math.Abs(q.N.DotProduct(Rg)) > 0.999 && Cut(q)));
                }
            }
            if (parts.Contains("XA3"))
            {
                var blocks = new List<Tuple<string, double, double>>();
                foreach (var g in fl.GroupBy(f => f.S.Id.IntegerValue).Select(g => Tuple.Create("stairs " + g.Key, g.Min(f => f.El0), g.Max(f => f.El1))).OrderBy(b => b.Item2))
                {
                    var m = blocks.FirstOrDefault(q => g.Item2 < q.Item3 - 1 && g.Item3 > q.Item2 + 1);
                    if (m != null) { blocks.Remove(m); blocks.Add(Tuple.Create(m.Item1 + "+" + g.Item1.Substring(7), Math.Min(m.Item2, g.Item2), Math.Max(m.Item3, g.Item3))); } else blocks.Add(g);
                }
                foreach (var b in blocks.OrderBy(q => q.Item2))
                    try
                    {
                        string key = b.Item1; if (Skip("XA3", key)) continue;
                        bool has = dims.Where(dm => !isVert(dm)).Any(dm => { double zd = ZE(((Line)dm.Curve).Origin); return zd > b.Item2 - 100 && zd < b.Item3 + 3000 && refEls(dm).Any(isRail); });
                        if (has) { done.Add(new { Part = "XA3", Key = key, Skipped = "a width chain with handrail references is there" }); continue; }
                        var low = slabsAt(mid).Where(sl => sl.Top.Z0 >= b.Item2 - 5 && sl.Top.Z0 < b.Item3).OrderBy(sl => sl.Top.Z0).FirstOrDefault();
                        double baseZ = low != null ? low.Top.Z0 : b.Item2;
                        var o = Ov("XA3", key); double z3 = o != null && o["z"] != null ? (double)o["z"] : baseZ + 1800;
                        var wf = wallsAt(baseZ + 1000); var refs = new List<Tuple<double, Reference>>();
                        for (int i = 0; i + 1 < wf.Count; i++)
                        {
                            F a = wf[i], c = wf[i + 1];
                            if (!(a.N.DotProduct(Rg) > 0 && c.N.DotProduct(Rg) < 0 && c.X0 - a.X0 > 500)) continue;   // a lane between two wall faces
                            if (c.X0 < cx0 - 50 || a.X0 > cx1 + 50) continue;
                            var inLane = rails.Where(q => q.X0 > a.X0 && q.X1 < c.X0 && q.Z0 < baseZ + 1500 && q.Z1 > baseZ + 500).ToList();
                            var hl = inLane.Where(q => q.N.DotProduct(Rg) > 0 && q.X0 - a.X0 < 300).OrderBy(q => q.X0).FirstOrDefault();
                            var hr = inLane.Where(q => q.N.DotProduct(Rg) < 0 && c.X0 - q.X0 < 300).OrderByDescending(q => q.X0).FirstOrDefault();
                            refs.Add(Tuple.Create(a.X0, a.P.Reference));
                            if (hl != null) refs.Add(Tuple.Create(hl.X0, hl.P.Reference));
                            if (hr != null) refs.Add(Tuple.Create(hr.X0, hr.P.Reference));
                            refs.Add(Tuple.Create(c.X0, c.P.Reference));
                        }
                        refs = refs.OrderBy(q => q.Item1).GroupBy(q => Math.Round(q.Item1)).Select(q => q.First()).ToList();
                        if (refs.Count < 2) { errors.Add("XA3 " + key + ": no lane between wall faces found at +" + Math.Round(baseZ + 1000)); continue; }
                        var d = make(refs, false, z3);
                        done.Add(new { Part = "XA3", Key = key, Id = d.Id.IntegerValue, Z = Math.Round(z3), Values = valText(d) });
                    }
                    catch (Exception e) { errors.Add("XA3 " + b.Item1 + ": " + e.Message); }
            }

            // ---- XA4 clear | thickness chain at the column
            if (parts.Contains("XA4"))
                try
                {
                    var away = fl.Where(f => f.Facing == "away").ToList(); var hf = away.Count > 0 ? away.Where(f => f.Half == away[0].Half).ToList() : fl;
                    double col = args["xa4X"] != null ? (double)args["xa4X"] : (hf.Min(f => f.X0) + hf.Max(f => f.X1)) / 2;
                    var ss = slabsAt(col).Where(sl => sl.Top.Z0 >= zBot - 5).ToList();
                    var refs = new List<Tuple<double, Reference>>(); var miss = new List<string>();
                    for (int i = 0; i < ss.Count; i++)
                    {
                        if (i > 0) { if (ss[i].Soffit != null) refs.Add(Tuple.Create(ss[i].Soffit.Z0, ss[i].Soffit.P.Reference)); else miss.Add("soffit of +" + Math.Round(ss[i].Top.Z0)); }
                        refs.Add(Tuple.Create(ss[i].Top.Z0, ss[i].Top.P.Reference));
                    }
                    if (miss.Count > 0) errors.Add("XA4: " + string.Join(", ", miss));
                    bool has = dims.Where(isVert).Any(d => Math.Abs(VX(((Line)d.Curve).Origin) - col) < 600 && d.NumberOfSegments >= Math.Max(2, refs.Count - 2));
                    if (Skip("XA4", "column")) { }
                    else if (has) done.Add(new { Part = "XA4", Skipped = "a vertical chain is already near the column x " + Math.Round(col) });
                    else if (refs.Count < 2) errors.Add("XA4: fewer than two slabs cut at x " + Math.Round(col) + " (pass xa4X)");
                    else { var d = make(refs, true, col); done.Add(new { Part = "XA4", Key = "column", Id = d.Id.IntegerValue, X = Math.Round(col), Values = valText(d) }); }
                }
                catch (Exception e) { errors.Add("XA4: " + e.Message); }

            // ---- XA5 wall to wall: chain + overall below, one overall on top
            if (parts.Contains("XA5"))
                try
                {
                    var grids = new List<Tuple<double, Reference>>();
                    foreach (var g in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Grid)).Cast<Grid>())
                    {
                        Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, V).FirstOrDefault(); } catch { }
                        if (c is Line gl && Math.Abs(gl.Direction.Z) > 0.999) grids.Add(Tuple.Create(VX(gl.Origin), new Reference(g)));
                    }
                    Func<double, List<Tuple<double, Reference>>> facesAt = z =>
                    {
                        var wf = wallsAt(z).Where(q => q.X1 > cx0 - 2000 && q.X0 < cx1 + 2000).ToList();
                        // keep the walls that bound the core: from the nearest wall left of the flights to the nearest right of them
                        var lw = wf.Where(q => q.X1 <= cx0 + 5).OrderByDescending(q => q.X0).FirstOrDefault(); var rw = wf.Where(q => q.X0 >= cx1 - 5).OrderBy(q => q.X0).FirstOrDefault();
                        var ids = new HashSet<int>(wf.Where(q => (lw == null || q.X0 >= wf.Where(p => p.Id == lw.Id).Min(p => p.X0) - 1) && (rw == null || q.X0 <= wf.Where(p => p.Id == rw.Id).Max(p => p.X0) + 1)).Select(q => q.Id));
                        var res = wf.Where(q => ids.Contains(q.Id)).Select(q => Tuple.Create(q.X0, q.P.Reference)).ToList();
                        if (res.Count > 0) res.AddRange(grids.Where(q => q.Item1 > res.Min(p => p.Item1) && q.Item1 < res.Max(p => p.Item1)));
                        return res.OrderBy(q => q.Item1).GroupBy(q => Math.Round(q.Item1)).Select(q => q.First()).ToList();
                    };
                    var lowest = slabsAt(mid).FirstOrDefault(); double zLow = lowest != null ? lowest.Top.Z0 : zBot;
                    bool hasBot = dims.Where(d => !isVert(d)).Any(d => ZE(((Line)d.Curve).Origin) < zLow - 100);
                    bool hasTop = dims.Where(d => !isVert(d)).Any(d => ZE(((Line)d.Curve).Origin) > zTop + 300);
                    if (hasBot) done.Add(new { Part = "XA5", Key = "bottom", Skipped = "a horizontal dim already sits below the lowest slab" });
                    else if (!Skip("XA5", "bottom"))
                    {
                        var bot = facesAt(zLow + 1000);
                        if (bot.Count < 2) errors.Add("XA5 bottom: fewer than two wall faces at +" + Math.Round(zLow + 1000));
                        else
                        {
                            double zl = args["xa5LineMm"] != null ? (double)args["xa5LineMm"] : zLow - 1000;
                            var d = make(bot, false, zl); var ov = make(new List<Tuple<double, Reference>> { bot.First(), bot.Last() }, false, zl - gap);
                            done.Add(new { Part = "XA5", Key = "bottom", Id = d.Id.IntegerValue, OverallId = ov.Id.IntegerValue, Z = Math.Round(zl), Values = valText(d), Overall = valText(ov) });
                        }
                    }
                    if (hasTop) done.Add(new { Part = "XA5", Key = "top", Skipped = "a horizontal dim already sits above the top" });
                    else if (!Skip("XA5", "top"))
                    {
                        var top = facesAt(zTop - 1000).Where(q => !grids.Any(g => g.Item2 == q.Item2)).ToList();
                        if (top.Count >= 2)
                        {
                            var ov = make(new List<Tuple<double, Reference>> { top.First(), top.Last() }, false, zTop + 1000);
                            done.Add(new { Part = "XA5", Key = "top", Id = ov.Id.IntegerValue, Z = Math.Round(zTop + 1000), Overall = valText(ov) });
                        }
                    }
                    if (grids.Count == 0) errors.Add("XA5: no host grid seen: the chain stops at the wall faces");
                }
                catch (Exception e) { errors.Add("XA5: " + e.Message); }

            // ---- XA6 handrail heights on every cut slab
            if (parts.Contains("XA6"))
            {
                double hWall = (double?)args["xa6WallMm"] ?? 900, hOpen = (double?)args["xa6OpenMm"] ?? 1200;
                var railDims = dims.Where(isVert).Where(d => refEls(d).Any(isRail)).Select(d => Tuple.Create(VX(((Line)d.Curve).Origin), (d.Value ?? 0) * MM)).ToList();
                var slabTops = hFaces.Where(q => q.N.Z > 0 && q.X1 > cx0 - 50 && q.X0 < cx1 + 50).GroupBy(q => Math.Round(q.Z0 / 5)).Select(g => g.OrderByDescending(q => q.X1 - q.X0).First()).OrderBy(q => q.Z0).ToList();
                foreach (var st in slabTops)
                    foreach (var rg in rails.Where(q => q.X0 >= st.X0 - 50 && q.X1 <= st.X1 + 50 && q.Z1 > st.Z0 + 500 && q.Z0 < st.Z0 + 1500).GroupBy(q => q.Id))
                        try
                        {
                            double rx0 = rg.Min(q => q.X0), rx1 = rg.Max(q => q.X1), rcx = (rx0 + rx1) / 2;
                            string key = Num(st.Z0) + ":" + rg.Key; if (Skip("XA6", key)) continue;
                            var nearWall = wallsAt(st.Z0 + 900).Any(w => Math.Abs(w.X0 - rx0) < 250 || Math.Abs(w.X0 - rx1) < 250);
                            double target = nearWall ? hWall : hOpen;
                            if (railDims.Any(r => Math.Abs(r.Item1 - rcx) < 500 && Math.Abs(r.Item2 - target) < 150)) { done.Add(new { Part = "XA6", Key = key, Skipped = "has a rail dim" }); continue; }
                            var el = doc.GetElement(new ElementId(rg.Key)); var heads = new List<string>();
                            foreach (var g in el.get_Geometry(new Options { ComputeReferences = true, View = V }) ?? Enumerable.Empty<GeometryObject>())
                                if (g is GeometryInstance gi && gi.Symbol != null) heads.Add(el.UniqueId + ":1:INSTANCE:" + gi.Symbol.UniqueId + ":");
                            var o = Ov("XA6", key);
                            double x = o != null && o["x"] != null ? (double)o["x"] : nearWall ? (Math.Abs(rx0 - mid) < Math.Abs(rx1 - mid) ? rx0 - 150 : rx1 + 150) : rcx;
                            string best = null; double bestVal = double.NaN;
                            foreach (var head in heads.Distinct())
                                for (int n = 1; n <= 1500 && (double.IsNaN(bestVal) || Math.Abs(bestVal - target) > 0.5); n++)
                                {
                                    Reference rr; try { rr = Reference.ParseFromStableRepresentation(doc, head + n); } catch { continue; }
                                    using (var sub = new SubTransaction(doc))
                                    {
                                        sub.Start(); double val = double.NaN;
                                        try
                                        {
                                            var ra = new ReferenceArray(); ra.Append(st.P.Reference); ra.Append(rr);
                                            var d = doc.Create.NewDimension(V, Line.CreateBound(PV(x, st.Z0), PV(x, st.Z0 + target)), ra, dimType);
                                            doc.Regenerate(); if (d.get_BoundingBox(V) != null) val = (d.Value ?? 0) * MM;
                                        }
                                        catch { }
                                        sub.RollBack();
                                        if (!double.IsNaN(val) && Math.Abs(val - target) <= 150 && (double.IsNaN(bestVal) || Math.Abs(val - target) < Math.Abs(bestVal - target))) { bestVal = val; best = head + n; }
                                    }
                                }
                            if (best == null) { errors.Add("XA6 " + key + ": no drawn handrail line within 150 of " + target + " (hidden behind a bigger rail? or pass a hand dim and copy it with dims_copy_refs)"); continue; }
                            var raF = new ReferenceArray(); raF.Append(st.P.Reference); raF.Append(Reference.ParseFromStableRepresentation(doc, best));
                            var dF = doc.Create.NewDimension(V, Line.CreateBound(PV(x, st.Z0), PV(x, st.Z0 + bestVal)), raF, dimType);
                            created.Add(dF.Id.IntegerValue);
                            done.Add(new { Part = "XA6", Key = key, Id = dF.Id.IntegerValue, Value = Math.Round((dF.Value ?? 0) * MM), Target = target, X = Math.Round(x), Rail = rg.First().Kind });
                        }
                        catch (Exception e) { errors.Add("XA6 " + Num(st.Z0) + ":" + rg.Key + ": " + e.Message); }
            }

            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else t.RollBack();
        }
        return new
        {
            View = V.Name, Mode = mode,
            Flights = fl.Count, Bands = bands.Select(b => "+" + Math.Round(b[0].El0) + "..+" + Math.Round(b[0].El1) + " " + string.Join("/", b.Select(f => f.Label + " " + f.Half + " " + f.Facing))).ToList(),
            Done = done, Errors = errors
        };
    }
}
