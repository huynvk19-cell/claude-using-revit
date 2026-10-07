/* mcp-tool
{
  "description": "Old logic (use grid_dims_band): add grid chain + overall per parallel group near the grid ends in ONE view. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply",
          "undo"
        ]
      },
      "viewId": {
        "type": "number"
      },
      "dimType": {
        "type": "string",
        "description": "linear dimension type name"
      },
      "overallMm": {
        "type": "number",
        "description": "sheet mm from the grid end to the overall dim line (default 4"
      },
      "stepMm": {
        "type": "number",
        "description": "sheet mm between overall and chain (default 7)"
      },
      "forceSides": {
        "type": "object",
        "description": "optional { x: ['low'|'high'...], y: [...] } sides to dimension"
      },
      "force": {
        "type": "boolean",
        "description": "with forceSides"
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "mode",
      "logPath"
    ]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Add missing grid dimensions in ONE view: per group of parallel grids, a grid-to-grid chain and an overall (first-to-
//    last) dimension near the grid ends, on both sides for plans (one side, the bubble side, for
//    sections/elevations/details). A side is skipped when it already has them or has a partial grid chain; if the
//    chain text is tight or a side's band is blocked by other content, only one side is dimensioned. New dims use the
//    given (check) dimension type. Modes: preview, apply, undo (log).
// Parameters:
//   overallMm: sheet mm from the grid end to the overall dim line (default 4: dims sit just inside the grid bubbles,
//    outside the building)
//   forceSides: optional { x: ['low'|'high'...], y: [...] } sides to dimension (x = grids spaced left-right)
//   force: with forceSides: add on those sides even if the best band still touches something
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class GridDimsAdd
{
    const double MM = 304.8;
    class G { public Grid Grid; public string Name; public double Pos; public double Lo, Hi; public bool BubLo, BubHi; }
    class Rc { public double a0, a1, b0, b1; public string Src; } // a = along axis, b = across (mm, view axes)
    class Dm { public Dimension D; public string Axis; public List<double> B = new List<double>(); public double Pos; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var log = JArray.Parse(File.ReadAllText(logPath)); int n = 0;
            var only = (args["ids"] as JArray)?.Select(x => (int)x).ToList();
            var keep = new JArray();
            using (var t = new Transaction(doc, "Remove added grid dimensions"))
            {
                t.Start();
                foreach (var id in log)
                {
                    if (only != null && !only.Contains((int)id)) { keep.Add(id); continue; }
                    var e = doc.GetElement(new ElementId((int)id)); if (e != null) { doc.Delete(e.Id); n++; }
                }
                t.Commit();
            }
            File.WriteAllText(logPath, keep.ToString());
            return new { Removed = n, LeftInLog = keep.Count };
        }
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ o = v.Origin, r = v.RightDirection, u = v.UpDirection;
        double sc = v.Scale;
        double offO = (args.Value<double?>("overallMm") ?? 4) * sc, step = (args.Value<double?>("stepMm") ?? 7) * sc;
        bool isPlan = v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan || v.ViewType == ViewType.AreaPlan || v.ViewType == ViewType.EngineeringPlan;
        DimensionType dt = null;
        if (args["dimType"] != null)
        {
            dt = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["dimType"] && x.StyleType == DimensionStyleType.Linear);
            if (dt == null) return new { Error = "dimension type not found: " + args["dimType"] };
        }
        Func<XYZ, double> R = p => (p - o).DotProduct(r) * MM; Func<XYZ, double> U = p => (p - o).DotProduct(u) * MM;

        // grids
        var gx = new List<G>(); var gy = new List<G>();
        foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
            if (!(c is Line ln)) continue;
            XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
            bool b0 = false, b1 = false; try { b0 = g.IsBubbleVisibleInView(DatumEnds.End0, v); b1 = g.IsBubbleVisibleInView(DatumEnds.End1, v); } catch { }
            if (Math.Abs(ln.Direction.DotProduct(u)) > 0.999)
            { bool f = U(a) > U(b); gx.Add(new G { Grid = g, Name = g.Name, Pos = R(a), Lo = Math.Min(U(a), U(b)), Hi = Math.Max(U(a), U(b)), BubLo = f ? b1 : b0, BubHi = f ? b0 : b1 }); }
            else if (Math.Abs(ln.Direction.DotProduct(r)) > 0.999)
            { bool f = R(a) > R(b); gy.Add(new G { Grid = g, Name = g.Name, Pos = U(a), Lo = Math.Min(R(a), R(b)), Hi = Math.Max(R(a), R(b)), BubLo = f ? b1 : b0, BubHi = f ? b0 : b1 }); }
        }
        // existing linear dims
        var dims = new List<Dm>();
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension || d.OwnerViewId != v.Id || !(d.Curve is Line ln)) continue;
            string axis = Math.Abs(ln.Direction.DotProduct(r)) > 0.999 ? "x" : Math.Abs(ln.Direction.DotProduct(u)) > 0.999 ? "y" : null;
            if (axis == null) continue;
            XYZ ax = axis == "x" ? r : u, pe = axis == "x" ? u : r;
            var dm = new Dm { D = d, Axis = axis };
            var segs = new List<Tuple<XYZ, double>>();
            if (d.NumberOfSegments > 0) { foreach (DimensionSegment s in d.Segments) if (s.Value.HasValue) segs.Add(Tuple.Create(s.Origin, s.Value.Value)); }
            else if (d.Value.HasValue) segs.Add(Tuple.Create(d.Origin, d.Value.Value));
            if (segs.Count == 0) continue;
            foreach (var s in segs) { double c = (s.Item1 - o).DotProduct(ax) * MM, h = s.Item2 * MM / 2; dm.B.Add(c - h); dm.B.Add(c + h); }
            dm.B = dm.B.OrderBy(x => x).Aggregate(new List<double>(), (l, x) => { if (l.Count == 0 || Math.Abs(l.Last() - x) > 1) l.Add(x); return l; });
            dm.Pos = (segs[0].Item1 - o).DotProduct(pe) * MM;
            dims.Add(dm);
        }
        // obstacles (view axes): annotations and model elements, except big/background categories
        var skipCats = new HashSet<int> { (int)BuiltInCategory.OST_Floors, (int)BuiltInCategory.OST_Ceilings, (int)BuiltInCategory.OST_Rooms, (int)BuiltInCategory.OST_Areas,
            (int)BuiltInCategory.OST_Cameras, (int)BuiltInCategory.OST_VolumeOfInterest, (int)BuiltInCategory.OST_SectionBox, (int)BuiltInCategory.OST_RoomSeparationLines,
            (int)BuiltInCategory.OST_Grids, (int)BuiltInCategory.OST_Levels, (int)BuiltInCategory.OST_CLines, (int)BuiltInCategory.OST_Topography, (int)BuiltInCategory.OST_RvtLinks,
            (int)BuiltInCategory.OST_MEPSpaces, (int)BuiltInCategory.OST_AreaSchemeLines, (int)BuiltInCategory.OST_SketchLines, (int)BuiltInCategory.OST_ColorFillLegends, (int)BuiltInCategory.OST_FilledRegion,
            (int)BuiltInCategory.OST_Matchline, (int)BuiltInCategory.OST_ReferenceViewer };
        var obs = new List<Tuple<double, double, double, double, string>>(); // R0,R1,U0,U1
        var obsEl = new Dictionary<int, Element>();
        double vw = 0, vh = 0;
        if (v.CropBoxActive) { var cb = v.CropBox; vw = (cb.Max.X - cb.Min.X) * MM; vh = (cb.Max.Y - cb.Min.Y) * MM; }
        foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
        {
            if (e.Category == null || skipCats.Contains(e.Category.Id.IntegerValue) || e is ImportInstance || e is RevitLinkInstance) continue;
            if (e is Dimension dd && !(e is SpotDimension) && dims.Any(x => x.D.Id == e.Id)) continue; // existing dims handled separately
            BoundingBoxXYZ bb = null; try { bb = e.get_BoundingBox(v); } catch { }
            if (bb == null) continue;
            var pts = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z) }.Select(p => bb.Transform != null ? bb.Transform.OfPoint(p) : p).ToList();
            double r0 = pts.Min(R), r1 = pts.Max(R), u0 = pts.Min(U), u1 = pts.Max(U);
            if (vw > 0 && (r1 - r0) > 0.8 * vw && (u1 - u0) > 0.8 * vh) continue; // huge background items
            bool solidBlock = e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Parking; // may carry model text: whole box counts
            if (!solidBlock && (e.Category.CategoryType == CategoryType.Model || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Viewers)) obsEl[obs.Count] = e;
            obs.Add(Tuple.Create(r0, r1, u0, u1, e.Category.Name + " " + e.Id.IntegerValue));
        }
        // linked models (plans): site blocks, parking, planting, equipment, walls, columns drawn from links (box only)
        if (isPlan && v.CropBoxActive && v.GenLevel != null)
        {
            var linkCats = new List<BuiltInCategory> { BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Parking, BuiltInCategory.OST_Planting, BuiltInCategory.OST_Entourage,
                BuiltInCategory.OST_Site, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_Furniture, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns };
            double z = v.GenLevel.Elevation;
            var cb = v.CropBox; var cpts = new[] { cb.Min, cb.Max, new XYZ(cb.Min.X, cb.Max.Y, 0), new XYZ(cb.Max.X, cb.Min.Y, 0) }.Select(p => cb.Transform.OfPoint(p)).ToList();
            foreach (var li in new FilteredElementCollector(doc, v.Id).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var ld = li.GetLinkDocument(); if (ld == null) continue;
                if (v.GetCategoryHidden(new ElementId(BuiltInCategory.OST_RvtLinks)) || li.IsHidden(v)) continue;
                var tr = li.GetTotalTransform(); var inv = tr.Inverse;
                var lp = cpts.Select(p => inv.OfPoint(new XYZ(p.X, p.Y, z))).ToList();
                var outline = new Outline(new XYZ(lp.Min(p => p.X), lp.Min(p => p.Y), lp.Min(p => p.Z) - 2000 / MM), new XYZ(lp.Max(p => p.X), lp.Max(p => p.Y), lp.Max(p => p.Z) + 4000 / MM));
                var col = new FilteredElementCollector(ld).WherePasses(new ElementMulticategoryFilter(linkCats)).WherePasses(new BoundingBoxIntersectsFilter(outline)).WhereElementIsNotElementType();
                foreach (var e in col)
                {
                    var bb = e.get_BoundingBox(null); if (bb == null) continue;
                    var pts = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z) }.Select(tr.OfPoint).ToList();
                    double r0 = pts.Min(R), r1 = pts.Max(R), u0 = pts.Min(U), u1 = pts.Max(U);
                    if (vw > 0 && (r1 - r0) > 0.8 * vw && (u1 - u0) > 0.8 * vh) continue;
                    if (e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Walls && (r1 - r0) > 0.3 * vw && (u1 - u0) > 0.3 * vh) continue; // long diagonal walls: box too coarse
                    obs.Add(Tuple.Create(r0, r1, u0, u1, "Link " + e.Category.Name + " " + e.Id.IntegerValue));
                }
            }
        }
        // underlay (plans): walls of the underlay level are drawn but not collected with the view
        var underlayIds = new HashSet<int>();
        ElementId ulev = ElementId.InvalidElementId;
        try { ulev = v.get_Parameter(BuiltInParameter.VIEW_UNDERLAY_BOTTOM_ID)?.AsElementId() ?? ElementId.InvalidElementId; } catch { }
        if (isPlan && ulev != ElementId.InvalidElementId)
        {
            foreach (var w in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Where(w => w.LevelId == ulev))
            {
                var bb = w.get_BoundingBox(null); if (bb == null) continue;
                var pts = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z) };
                obsEl[obs.Count] = w; underlayIds.Add(w.Id.IntegerValue);
                obs.Add(Tuple.Create(pts.Min(R), pts.Max(R), pts.Min(U), pts.Max(U), "Underlay wall " + w.Id.IntegerValue));
            }
        }
        // precise test for model elements: their geometry edges (projected to view axes) against the band rectangle
        var segCache = new Dictionary<int, List<double[]>>();
        var gopt = new Options { View = v, ComputeReferences = false };
        var gopt0 = new Options { ComputeReferences = false };
        Func<Element, List<double[]>> Segs = el =>
        {
            if (segCache.TryGetValue(el.Id.IntegerValue, out var cached)) return cached;
            var list = new List<double[]>();
            Action<IList<XYZ>> AddPl = ps => { for (int i = 1; i < ps.Count; i++) list.Add(new[] { R(ps[i - 1]), U(ps[i - 1]), R(ps[i]), U(ps[i]) }); };
            Action<GeometryElement> Walk = null;
            Walk = ge =>
            {
                if (ge == null) return;
                foreach (var go in ge)
                {
                    if (go is Solid so && so.Edges.Size > 0) { foreach (Edge ed in so.Edges) AddPl(ed.Tessellate()); }
                    else if (go is Curve cu) { try { AddPl(cu.Tessellate()); } catch { } }
                    else if (go is PolyLine pl) AddPl(pl.GetCoordinates());
                    else if (go is GeometryInstance gi) Walk(gi.GetInstanceGeometry());
                }
            };
            try { Walk(el.get_Geometry(underlayIds.Contains(el.Id.IntegerValue) ? gopt0 : gopt)); } catch { }
            segCache[el.Id.IntegerValue] = list; return list;
        };
        // filled areas in plans (floors, ceilings, roofs drawn with patterns): text must not sit on them
        var regions = new List<Tuple<double[], List<List<double[]>>, string>>();
        if (isPlan)
        {
            var fillCats = new HashSet<int> { (int)BuiltInCategory.OST_Floors, (int)BuiltInCategory.OST_Ceilings, (int)BuiltInCategory.OST_Roofs };
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().Where(e => e.Category != null && fillCats.Contains(e.Category.Id.IntegerValue)))
            {
                var polys = new List<List<double[]>>();
                Action<GeometryElement> Walk2 = null;
                Walk2 = ge =>
                {
                    if (ge == null) return;
                    foreach (var go in ge)
                    {
                        if (go is Solid so)
                        {
                            foreach (Face f in so.Faces)
                            {
                                var pf = f as PlanarFace;
                                if (pf == null || Math.Abs(pf.FaceNormal.DotProduct(v.ViewDirection)) < 0.9) continue;
                                foreach (EdgeArray loop in pf.EdgeLoops)
                                {
                                    var poly = new List<double[]>();
                                    foreach (Edge ed in loop) foreach (var p in ed.Tessellate()) poly.Add(new[] { R(p), U(p) });
                                    if (poly.Count > 2) polys.Add(poly);
                                }
                            }
                        }
                        else if (go is GeometryInstance gi) Walk2(gi.GetInstanceGeometry());
                    }
                };
                try { Walk2(e.get_Geometry(gopt)); } catch { }
                if (polys.Count == 0) continue;
                var all = polys.SelectMany(x => x).ToList();
                regions.Add(Tuple.Create(new[] { all.Min(p => p[0]), all.Max(p => p[0]), all.Min(p => p[1]), all.Max(p => p[1]) }, polys, e.Category.Name + " " + e.Id.IntegerValue));
            }
        }
        Func<List<double[]>, double, double, bool> Inside = (poly, x, y) =>
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if (((poly[i][1] > y) != (poly[j][1] > y)) && (x < (poly[j][0] - poly[i][0]) * (y - poly[i][1]) / (poly[j][1] - poly[i][1]) + poly[i][0])) c = !c;
            return c;
        };
        Func<double[], double, double, double, double, bool> SegHit = (sg, x0, x1, y0, y1) =>
        {   // Liang-Barsky clip of segment against rectangle
            double ax = sg[0], ay = sg[1], dx = sg[2] - sg[0], dy = sg[3] - sg[1], t0 = 0, t1 = 1;
            double[] p = { -dx, dx, -dy, dy }, q = { ax - x0, x1 - ax, ay - y0, y1 - ay };
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-9) { if (q[i] < 0) return false; }
                else { double tt = q[i] / p[i]; if (p[i] < 0) { if (tt > t1) return false; if (tt > t0) t0 = tt; } else { if (tt < t0) return false; if (tt < t1) t1 = tt; } }
            }
            return true;
        };
        // a dim line at pos from a0 to a1 with text boxes (centre, width): text must not touch anything (incl. grid lines, filled areas); the line must not cross annotations
        Func<string, List<double[]>, double, double, double, List<double>, List<string>> Band = (axis, texts, a0, a1, pos, gridPos) =>
        {
            var rects = new List<double[]>(); // along0, along1, across0, across1, textFlag
            foreach (var tx in texts)
                rects.Add(axis == "x" ? new[] { tx[0] - tx[1] / 2, tx[0] + tx[1] / 2, pos + 0.3 * sc, pos + 3.5 * sc, 1 } : new[] { tx[0] - tx[1] / 2, tx[0] + tx[1] / 2, pos - 3.5 * sc, pos - 0.3 * sc, 1 });
            rects.Add(new[] { a0, a1, pos - 0.4 * sc, pos + 0.4 * sc, 0 });
            var res = new List<string>();
            foreach (var rc in rects.Where(x => x[4] == 1))
                if (gridPos.Any(g => g > rc[0] + 0.3 * sc && g < rc[1] - 0.3 * sc)) { res.Add("grid line through text"); break; }
            foreach (var rc in rects.Where(x => x[4] == 1))
                foreach (var rg in regions)
                {
                    double x0 = axis == "x" ? rc[0] : rc[2], x1 = axis == "x" ? rc[1] : rc[3], y0 = axis == "x" ? rc[2] : rc[0], y1 = axis == "x" ? rc[3] : rc[1];
                    if (rg.Item1[1] < x0 || rg.Item1[0] > x1 || rg.Item1[3] < y0 || rg.Item1[2] > y1) continue;
                    var samples = new[] { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x0, y1 }, new[] { x1, y1 }, new[] { (x0 + x1) / 2, (y0 + y1) / 2 } };
                    if (rg.Item2.Any(pg => samples.Any(sp => Inside(pg, sp[0], sp[1])))) { if (!res.Contains(rg.Item3)) res.Add(rg.Item3); }
                }
            for (int i2 = 0; i2 < obs.Count; i2++)
            {
                var ob = obs[i2];
                double oa0 = axis == "x" ? ob.Item1 : ob.Item3, oa1 = axis == "x" ? ob.Item2 : ob.Item4, ob0 = axis == "x" ? ob.Item3 : ob.Item1, ob1 = axis == "x" ? ob.Item4 : ob.Item2;
                bool isModel = obsEl.TryGetValue(i2, out var el);
                foreach (var rc in rects)
                {
                    if (rc[4] == 0 && isModel) continue; // dim line may cross model lines
                    if (!(oa1 > rc[0] && oa0 < rc[1] && ob1 > rc[2] && ob0 < rc[3])) continue;
                    if (isModel)
                    {
                        double x0 = axis == "x" ? rc[0] : rc[2], x1 = axis == "x" ? rc[1] : rc[3], y0 = axis == "x" ? rc[2] : rc[0], y1 = axis == "x" ? rc[3] : rc[1];
                        var sgs = Segs(el);
                        if (sgs.Count > 0 && !sgs.Any(sg => SegHit(sg, x0, x1, y0, y1))) continue;
                    }
                    res.Add(ob.Item5); break;
                }
            }
            return res;
        };
        // existing dims as obstacles: dim line +- text band
        foreach (var dm in dims)
        {
            double t0 = dm.Pos - 1 * sc, t1 = dm.Pos + 4 * sc;
            if (dm.Axis == "x") obs.Add(Tuple.Create(dm.B.First(), dm.B.Last(), t0, t1, "Dimension " + dm.D.Id.IntegerValue));
            else obs.Add(Tuple.Create(dm.Pos - 4 * sc, dm.Pos + 1 * sc, dm.B.First(), dm.B.Last(), "Dimension " + dm.D.Id.IntegerValue));
        }

        var report = new List<object>(); var created = new List<int>();
        var forced = args["forceSides"] as JObject;
        using (var t = new Transaction(doc, "Add grid dimensions"))
        {
            t.Start();
            foreach (var grp in new[] { Tuple.Create("x", gx), Tuple.Create("y", gy) })
            {
                // coincident grids (same line, e.g. 47 on C): keep one per position, prefer the one with a visible bubble
                var merged = new List<string>();
                var gs = grp.Item2.OrderBy(x => x.Pos).GroupBy(x => Math.Round(x.Pos / 5)).Select(gp =>
                {
                    var keep = gp.OrderByDescending(x => (x.BubLo ? 1 : 0) + (x.BubHi ? 1 : 0)).First();
                    if (gp.Count() > 1) merged.Add(string.Join("=", gp.Select(x => x.Name)) + " -> " + keep.Name);
                    return keep;
                }).OrderBy(x => x.Pos).ToList();
                if (gs.Count < 2) continue;
                string axis = grp.Item1;
                double lo = gs.Max(x => x.Lo), hi = gs.Min(x => x.Hi), mid = (lo + hi) / 2;
                double a0 = gs.First().Pos, a1 = gs.Last().Pos;
                Func<double, int> Match = x => gs.FindIndex(p => Math.Abs(p.Pos - x) < 6);
                // chain text tightness: smallest spacing vs text width
                double minGap = Enumerable.Range(1, gs.Count - 1).Min(i => gs[i].Pos - gs[i - 1].Pos);
                string minTxt = Math.Round(minGap).ToString();
                bool tight = minGap / sc < (minTxt.Length * 1.3 + 1.5);
                Func<double, double> TW = len => (Math.Round(len).ToString().Length * 1.3 + 1) * sc;
                var gridPos = gs.Select(x => x.Pos).ToList();
                var chainTexts = Enumerable.Range(1, gs.Count - 1).Select(i => new[] { (gs[i].Pos + gs[i - 1].Pos) / 2, TW(gs[i].Pos - gs[i - 1].Pos) }).ToList();
                double oc = (a0 + a1) / 2, ow = TW(a1 - a0);
                var hit = gridPos.Where(g => Math.Abs(g - oc) < ow / 2 + 0.5 * sc).OrderBy(g => Math.Abs(g - oc)).ToList();
                if (hit.Count > 0)
                {   // overall text sits on a grid line: nudge it just clear of the line (user practice: text edge ~0.5-1 mm from the grid), to the side with more room
                    double g0 = hit[0], d = ow / 2 + 0.8 * sc;
                    double right = gridPos.Where(g => g > g0 + 1).DefaultIfEmpty(double.MaxValue).Min() - g0, left = g0 - gridPos.Where(g => g < g0 - 1).DefaultIfEmpty(double.MinValue).Max();
                    oc = right >= left ? g0 + d : g0 - d;
                }
                var overallTexts = new List<double[]> { new[] { oc, ow } };
                var sideInfo = new Dictionary<string, Dictionary<string, object>>();
                foreach (var side in new[] { "low", "high" })
                {
                    var mine = dims.Where(d => d.Axis == axis && (side == "low" ? d.Pos < mid : d.Pos >= mid)).ToList();
                    bool overall = false; var covered = new HashSet<int>(); var gridDimPos = new List<double>();
                    foreach (var d in mine)
                    {
                        var idx = d.B.Select(Match).ToList(); int hits = idx.Count(i => i >= 0);
                        if (hits < 2) continue;
                        if (d.B.Count == 2 && idx[0] == 0 && idx[1] == gs.Count - 1) { overall = true; gridDimPos.Add(d.Pos); continue; }
                        if (hits >= 0.8 * d.B.Count) { foreach (var i in idx.Where(i => i >= 0)) covered.Add(i); gridDimPos.Add(d.Pos); }
                    }
                    string chain = covered.Count == gs.Count ? "full" : covered.Count == 0 ? "none" : "partial";
                    if (gs.Count == 2 && chain == "full") overall = true;
                    if (gs.Count == 2 && overall) chain = "full";
                    bool bubble = side == "low" ? gs.Any(x => x.BubLo) : gs.Any(x => x.BubHi);
                    sideInfo[side] = new Dictionary<string, object> { ["chain"] = chain, ["overall"] = overall, ["pos"] = gridDimPos, ["bubble"] = bubble };
                }
                // which sides to fill
                var want = new List<string>();
                var fj = forced?[axis] as JArray;
                if (fj != null) want = fj.Select(x => (string)x).ToList();
                else if (isPlan) want = new List<string> { "low", "high" };
                else
                {   // sections/elevations/details: the bubble side (or the side that already has grid dims)
                    var withDims = new[] { "low", "high" }.Where(s => ((List<double>)sideInfo[s]["pos"]).Count > 0).ToList();
                    if (withDims.Count > 0) want = withDims.Take(1).ToList();
                    else want = new List<string> { (bool)sideInfo["high"]["bubble"] || !(bool)sideInfo["low"]["bubble"] ? "high" : "low" };
                }
                var plans = new List<Tuple<string, double?, double?, List<string>>>(); // side, overallPos, chainPos, conflicts
                foreach (var side in want)
                {
                    var si = sideInfo[side];
                    if ((string)si["chain"] == "partial") { plans.Add(Tuple.Create(side, (double?)null, (double?)null, new List<string> { "partial chain exists - not touched" })); continue; }
                    bool needC = (string)si["chain"] != "full", needO = !(bool)si["overall"] && gs.Count > 2;
                    if (gs.Count == 2) { needO = false; needC = (string)si["chain"] != "full"; }
                    if (!needC && !needO) { plans.Add(Tuple.Create(side, (double?)null, (double?)null, new List<string> { "complete" })); continue; }
                    double end = side == "low" ? lo : hi, s = side == "low" ? 1 : -1;
                    var existing = (List<double>)si["pos"];
                    // candidate positions: walk inward from the grid end in 1 mm (sheet) steps
                    double? bestO = null, bestC = null; List<string> bestHits = null;
                    var ks = Enumerable.Range(0, 45).Select(i => (2 + i) * sc).OrderBy(k => Math.Abs(k - offO)).ToList();
                    foreach (var k in ks)
                    {
                        double pO = end + s * k, pC = end + s * (k + (needO ? step : 0));
                        if ((side == "low" && pC > mid) || (side == "high" && pC < mid)) continue;
                        var hitsC = needC ? Band(axis, chainTexts, a0, a1, pC, gridPos) : new List<string>();
                        var hitsO = needO ? Band(axis, overallTexts, a0, a1, pO, gridPos) : new List<string>();
                        var hits = hitsC.Concat(hitsO).ToList();
                        if (bestHits == null || hits.Count < bestHits.Count) { bestHits = hits; bestO = needO ? pO : (double?)null; bestC = needC ? pC : (double?)null; }
                        if (hits.Count == 0) break;
                    }
                    plans.Add(Tuple.Create(side, bestO, bestC, bestHits));
                }
                // tight chain or blocked side -> one side only
                var doable = plans.Where(p => (p.Item2 != null || p.Item3 != null)).ToList();
                var clean = doable.Where(p => p.Item4.Count == 0).ToList();
                bool otherComplete = plans.Any(p => p.Item4.Count == 1 && p.Item4[0] == "complete");
                List<Tuple<string, double?, double?, List<string>>> chosen;
                string why;
                if (fj != null && (args.Value<bool?>("force") ?? false) && doable.Count > 0) { chosen = doable; why = "forced by user (least-overlap position)"; }
                else if (doable.Count == 0) { chosen = doable; why = "nothing to add"; }
                else if (clean.Count == doable.Count && !(tight && (doable.Count > 1 || otherComplete))) { chosen = doable; why = "clear"; }
                else if (otherComplete) { chosen = new List<Tuple<string, double?, double?, List<string>>>(); why = tight ? "tight chain: other side already complete" : "blocked: other side already complete"; }
                else if (clean.Count > 0) { chosen = new List<Tuple<string, double?, double?, List<string>>> { clean.First() }; why = tight ? "tight chain: one side" : "one side blocked: other side only"; }
                else if (args.Value<bool?>("force") ?? false) { chosen = new List<Tuple<string, double?, double?, List<string>>> { doable.OrderBy(p => p.Item4.Count).First() }; why = "forced by user: least-overlap side"; }
                else { chosen = new List<Tuple<string, double?, double?, List<string>>>(); why = "NOT ADDED: no clear band on any side (would overlap) - needs manual placement"; }

                foreach (var p in chosen)
                {
                    if (mode != "apply") continue;
                    XYZ axv = axis == "x" ? r : u, pev = axis == "x" ? u : r;
                    Func<double, double, XYZ> P = (a, b) => o + axv * (a / MM) + pev * (b / MM);
                    if (p.Item3 != null)
                    {
                        var ra = new ReferenceArray(); foreach (var g in gs) ra.Append(new Reference(g.Grid));
                        var d = dt != null ? doc.Create.NewDimension(v, Line.CreateBound(P(a0, p.Item3.Value), P(a1, p.Item3.Value)), ra, dt) : doc.Create.NewDimension(v, Line.CreateBound(P(a0, p.Item3.Value), P(a1, p.Item3.Value)), ra);
                        created.Add(d.Id.IntegerValue);
                    }
                    if (p.Item2 != null)
                    {
                        var ra = new ReferenceArray(); ra.Append(new Reference(gs.First().Grid)); ra.Append(new Reference(gs.Last().Grid));
                        var d = dt != null ? doc.Create.NewDimension(v, Line.CreateBound(P(a0, p.Item2.Value), P(a1, p.Item2.Value)), ra, dt) : doc.Create.NewDimension(v, Line.CreateBound(P(a0, p.Item2.Value), P(a1, p.Item2.Value)), ra);
                        created.Add(d.Id.IntegerValue);
                        if (Math.Abs(oc - (a0 + a1) / 2) > 1) { doc.Regenerate(); try { d.TextPosition = d.TextPosition + axv * ((oc - (a0 + a1) / 2) / MM); } catch { } }
                    }
                }
                report.Add(new
                {
                    Group = axis == "x" ? "grids spaced left-right" : "grids spaced bottom-top", Grids = string.Join(",", gs.Select(x => x.Name)),
                    Existing = sideInfo.ToDictionary(k => k.Key, k => k.Value["chain"] + (((bool)k.Value["overall"]) ? "+overall" : "")),
                    Merged = merged.Count > 0 ? merged : null, Tight = tight ? "min spacing " + Math.Round(minGap) : null, Decision = why,
                    Plans = plans.Select(p => new { Side = p.Item1, Overall = p.Item2.HasValue ? (object)Math.Round(p.Item2.Value) : null, Chain = p.Item3.HasValue ? (object)Math.Round(p.Item3.Value) : null, Hits = p.Item4.Take(4).ToList(), Chosen = chosen.Contains(p) }).ToList()
                });
            }
            if (mode == "apply") t.Commit(); else t.RollBack();
        }
        if (mode == "apply" && created.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            var log = File.Exists(logPath) ? JArray.Parse(File.ReadAllText(logPath)) : new JArray();
            foreach (var id in created) log.Add(id);
            File.WriteAllText(logPath, log.ToString());
        }
        return new { View = v.Name, ViewId = v.Id.IntegerValue, Type = v.ViewType.ToString(), Scale = v.Scale, Created = created, Groups = report };
    }

}
