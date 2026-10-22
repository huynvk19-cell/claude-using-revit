/* mcp-tool
{
  "description": "Read-only, floor plans: rooms with no floor spot elevation in the view + a clear point on the room's floor (rules FL1-FL3); annot_place spot items.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "roomIds": { "type": "array", "items": { "type": "number" }, "description": "only these rooms (default: every room seen in the view)" },
      "typeName": { "type": "string", "description": "spot elevation type the user chose; empty = most used in the view" },
      "minAreaM2": { "type": "number", "description": "rooms inside another room smaller than this get no spot, default 5" },
      "stairRoomText": { "type": "string", "description": "room name part of stair enclosures (left to stair_door_spots), default STAIR" },
      "spotSizeMm": { "type": "array", "items": { "type": "number" }, "description": "symbol + text width, height on paper mm, default 14 x 5.2" },
      "clearMm": { "type": "number", "description": "paper mm kept clear around the spot, default 1" },
      "maxShiftMm": { "type": "number", "description": "paper mm the spot may move off the diagonal point, default 6" }
    },
    "required": ["viewIds"]
  },
  "timeoutSeconds": 240,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only. For each floor plan view: the rooms seen in the view (rooms of the view's level inside the crop, plus rooms
//    the view shows) and whether each one already has a spot elevation of the view inside it (spot origin or leader end
//    in the room; a dependent view counts the annotations owned by its primary view). Rules FL1-FL3 (drafting-plan-dims.md / drafting-plan-floor-finish.md "Cao độ sàn"):
//    - FL1: every room gets one floor spot elevation, except a room enclosed by another room (it sits in an inner
//      boundary loop of that room) whose area is below minAreaM2 (Nested, no spot);
//    - FL2: stair enclosures (room name contains stairRoomText) need a spot inside and outside each door: they are only
//      listed (StairRooms), stair_door_spots places them;
//    - FL3: the spot type is the user's (typeName), else the most used spot elevation type of the view; none -> Problem.
//    Point: the intersection of the room rectangle's diagonals (view frame), else the nearest point inside the room,
//    moved at most maxShiftMm so the symbol + text box (spotSizeMm x scale, + clearMm; text to the right of the point)
//    lies in the room and crosses no wall, no floor edge, no dim line and no tag / text / spot; else the nearest clear
//    point anywhere in the room. The floor under the point: a host Floor (or landing) hit by a ray down at the room's
//    level; none (linked floor only) -> Problem. Items are ready for annot_place {mode, viewId, items} (kind 'spot').
//    - FL1b: a room whose host floors (seen in the view, centre inside the room) lie at SEVERAL levels (floor pieces at
//      different heights, ramps): one spot per flat level (on its largest piece, near its centre) + one at each end of
//      every sloped piece (ramp: low end and high end, 1 mm in from the end edge so the value reads the end height,
//      sliding only along that edge) unless a flat piece at the same
//      height touches that end or an existing spot of the view sits there. The room's single-spot rule is replaced by
//      these targets (Levels = their count); floor-piece edges do not block the box in this case (landings are narrow).
//    Coordinates: view frame mm (Right / Up from View.Origin), as annot_place / view_elem_boxes.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomSpotsMissing
{
    const double MM = 304.8;

    class Seg { public double R1, U1, R2, U2; }
    class Box
    {
        public double R0, U0, R1, U1;
        public Box(double r0, double u0, double r1, double u1) { R0 = Math.Min(r0, r1); U0 = Math.Min(u0, u1); R1 = Math.Max(r0, r1); U1 = Math.Max(u0, u1); }
        public bool Hits(Box b) { return R0 < b.R1 && b.R0 < R1 && U0 < b.U1 && b.U0 < U1; }
        public bool Holds(double r, double u) { return r >= R0 && r <= R1 && u >= U0 && u <= U1; }
        public Box Grow(double d) { return new Box(R0 - d, U0 - d, R1 + d, U1 + d); }
        public double Area { get { return (R1 - R0) * (U1 - U0); } }
    }

    // top face of a host floor in the view frame; DR / DU = down-slope direction (0 when flat)
    class Piece { public int Id; public bool Flat; public Box B; public double Zmin, Zmax, DR, DU; public List<double[]> Pts; }

    // Liang-Barsky: does the segment touch the box (crossing or lying inside)?
    static bool SegHits(Seg s, Box b)
    {
        double t0 = 0, t1 = 1, dx = s.R2 - s.R1, dy = s.U2 - s.U1;
        double[] p = { -dx, dx, -dy, dy };
        double[] q = { s.R1 - b.R0, b.R1 - s.R1, s.U1 - b.U0, b.U1 - s.U1 };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-9) { if (q[i] < 0) return false; continue; }
            double t = q[i] / p[i];
            if (p[i] < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
        }
        return true;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var viewIds = ((JArray)args["viewIds"]).Select(t => (int)t).ToList();
        HashSet<int> only = args["roomIds"] is JArray ra && ra.Count > 0 ? new HashSet<int>(ra.Select(t => (int)t)) : null;
        string typeArg = ((string)args["typeName"] ?? "").Trim();
        string stairText = ((string)args["stairRoomText"] ?? "STAIR").Trim().ToUpperInvariant();
        double minArea = (double?)args["minAreaM2"] ?? 5, clearMm = (double?)args["clearMm"] ?? 1, maxShiftMm = (double?)args["maxShiftMm"] ?? 6;
        double[] size = args["spotSizeMm"] is JArray sa && sa.Count == 2 ? new[] { (double)sa[0], (double)sa[1] } : new[] { 14.0, 5.2 };

        var v3 = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && !x.IsPerspective && !x.IsSectionBoxActive);
        ReferenceIntersector ri = null;
        if (v3 != null)
            ri = new ReferenceIntersector(new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_StairsLandings }), FindReferenceTarget.Face, v3) { FindReferencesInRevitLinks = false };

        var outViews = new List<object>();
        foreach (var vid in viewIds)
        {
            var v = doc.GetElement(new ElementId(vid)) as ViewPlan;
            if (v == null || v.ViewType != ViewType.FloorPlan) { outViews.Add(new { ViewId = vid, Error = "not a floor plan view" }); continue; }
            XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
            double sc = v.Scale;
            Func<XYZ, double[]> F = p => new[] { (p - O).DotProduct(R) * MM, (p - O).DotProduct(U) * MM };
            Func<double, double, double, XYZ> P = (r, u, z) => { var p = O + R * (r / MM) + U * (u / MM); return new XYZ(p.X, p.Y, z); };
            var primaryId = v.GetPrimaryViewId(); // dependent view: its annotations are owned by the primary view

            Box crop = null;
            if (v.CropBoxActive)
            {
                var cb = v.CropBox; var tr = cb.Transform;
                var cs = new[] { cb.Min, cb.Max, new XYZ(cb.Min.X, cb.Max.Y, cb.Min.Z), new XYZ(cb.Max.X, cb.Min.Y, cb.Min.Z) }.Select(p => F(tr.OfPoint(p))).ToList();
                crop = new Box(cs.Min(c => c[0]), cs.Min(c => c[1]), cs.Max(c => c[0]), cs.Max(c => c[1]));
            }

            // rooms seen in the view
            var cand = new Dictionary<int, Room>();
            try { foreach (var r in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>()) cand[r.Id.IntegerValue] = r; } catch { }
            if (v.GenLevel != null)
                foreach (var r in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.LevelId == v.GenLevel.Id))
                    cand[r.Id.IntegerValue] = r;
            var rooms = new List<Room>();
            foreach (var r in cand.Values)
            {
                if (r.Area <= 0 || !(r.Location is LocationPoint lp)) continue;
                var lpF = F(lp.Point);
                if (crop != null && !crop.Holds(lpF[0], lpF[1])) continue;
                rooms.Add(r);
            }

            // spots of the view and the spot type in use
            var spots = new FilteredElementCollector(doc, v.Id).OfClass(typeof(SpotDimension)).Cast<SpotDimension>()
                .Where(s => (s.OwnerViewId == v.Id || s.OwnerViewId == primaryId) && s.SpotDimensionType != null && s.SpotDimensionType.StyleType == DimensionStyleType.SpotElevation).ToList();
            var spotPts = new List<XYZ>();
            foreach (var s in spots)
            {
                try { spotPts.Add(s.Origin); } catch { }
                try { if (s.HasLeader) spotPts.Add(s.LeaderEndPosition); } catch { }
            }
            string viewType = spots.GroupBy(s => s.SpotDimensionType.Name).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            string typeName = typeArg.Length > 0 ? typeArg : viewType;

            // obstacles: wall outlines, floor edges, dim lines, annotation boxes
            var segs = new List<Seg>();
            Action<GeometryElement> addGeo = null;
            addGeo = ge =>
            {
                if (ge == null) return;
                foreach (var g in ge)
                {
                    if (g is GeometryInstance gi) { addGeo(gi.GetInstanceGeometry()); continue; }
                    if (!(g is Solid so) || so.Edges.Size == 0) continue;
                    foreach (Edge ed in so.Edges)
                    {
                        var pts = ed.Tessellate();
                        for (int i = 1; i < pts.Count; i++)
                        {
                            var a = F(pts[i - 1]); var b = F(pts[i]);
                            if (Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) < 1) continue;
                            segs.Add(new Seg { R1 = a[0], U1 = a[1], R2 = b[0], U2 = b[1] });
                        }
                    }
                }
            };
            var gopt = new Options { View = v };
            foreach (var e in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
                try { addGeo(e.get_Geometry(gopt)); } catch { }
            int floorSeg0 = segs.Count;
            foreach (var e in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType())
                try { addGeo(e.get_Geometry(gopt)); } catch { }
            int floorSeg1 = segs.Count;

            // FL1b: top faces of the host floors seen in the view (flat or sloped pieces)
            var pieces = new List<Piece>();
            var fopt = new Options { DetailLevel = ViewDetailLevel.Fine };
            foreach (var e in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType())
            {
                var st = new Stack<GeometryElement>();
                try { st.Push(e.get_Geometry(fopt)); } catch { continue; }
                while (st.Count > 0)
                {
                    var ge = st.Pop(); if (ge == null) continue;
                    foreach (var g in ge)
                    {
                        if (g is GeometryInstance gi) { st.Push(gi.GetInstanceGeometry()); continue; }
                        if (!(g is Solid so)) continue;
                        foreach (Face f in so.Faces)
                        {
                            var pf = f as PlanarFace;
                            if (pf == null || pf.FaceNormal.Z < 0.2 || f.Area * 0.09290304 < 0.5) continue;
                            var pts = new List<XYZ>();
                            foreach (EdgeArray ea in f.EdgeLoops) foreach (Edge ed in ea) pts.AddRange(ed.Tessellate());
                            if (pts.Count == 0) continue;
                            var fs = pts.Select(F).ToList();
                            var dn = new XYZ(pf.FaceNormal.X, pf.FaceNormal.Y, 0);
                            bool flat = pf.FaceNormal.Z > 0.99995; // < ~1 % slope
                            pieces.Add(new Piece
                            {
                                Id = e.Id.IntegerValue, Flat = flat,
                                B = new Box(fs.Min(x => x[0]), fs.Min(x => x[1]), fs.Max(x => x[0]), fs.Max(x => x[1])),
                                Zmin = pts.Min(p => p.Z) * MM, Zmax = pts.Max(p => p.Z) * MM,
                                DR = flat ? 0 : dn.Normalize().DotProduct(R), DU = flat ? 0 : dn.Normalize().DotProduct(U), Pts = fs
                            });
                        }
                    }
                }
            }
            var annBoxes = new List<Box>();
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
            {
                if ((e.OwnerViewId != v.Id && e.OwnerViewId != primaryId) || e.Category == null) continue;
                try
                {
                    if (e is Dimension d && !(e is SpotDimension))
                    {
                        if (!(d.Curve is Line dl)) continue;
                        var dir = dl.Direction;
                        var ends = new List<XYZ>();
                        if (d.Segments.Size > 0) foreach (DimensionSegment ds in d.Segments) { double hv = (ds.Value ?? 0) / 2; ends.Add(ds.Origin - dir * hv); ends.Add(ds.Origin + dir * hv); }
                        else { double hv = (d.Value ?? 0) / 2; ends.Add(d.Origin - dir * hv); ends.Add(d.Origin + dir * hv); }
                        var fs = ends.Select(F).OrderBy(f => f[0] * dir.DotProduct(R) + f[1] * dir.DotProduct(U)).ToList();
                        segs.Add(new Seg { R1 = fs.First()[0], U1 = fs.First()[1], R2 = fs.Last()[0], U2 = fs.Last()[1] });
                        continue;
                    }
                    bool ann = e is IndependentTag || e is TextNote || e is SpatialElementTag || e is SpotDimension
                        || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericAnnotation;
                    if (!ann) continue;
                    var bb = e.get_BoundingBox(v);
                    if (bb == null) continue;
                    var a = F(bb.Min); var b = F(bb.Max);
                    annBoxes.Add(new Box(a[0], a[1], b[0], b[1]));
                }
                catch { }
            }

            // per room: rectangle, boundary, inner-loop boxes, test height
            var bopt = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
            var rect = new Dictionary<int, Box>(); var bnd = new Dictionary<int, List<Seg>>(); var holes = new Dictionary<int, List<Box>>(); var zTest = new Dictionary<int, double>(); var zFloor = new Dictionary<int, double>();
            foreach (var r in rooms)
            {
                var all = new List<Seg>(); var loops = new List<Box>();
                try
                {
                    foreach (var loop in r.GetBoundarySegments(bopt))
                    {
                        var ls = new List<Seg>();
                        foreach (var bs in loop)
                        {
                            var pts = bs.GetCurve().Tessellate();
                            for (int i = 1; i < pts.Count; i++) { var a = F(pts[i - 1]); var b = F(pts[i]); ls.Add(new Seg { R1 = a[0], U1 = a[1], R2 = b[0], U2 = b[1] }); }
                        }
                        if (ls.Count == 0) continue;
                        all.AddRange(ls);
                        loops.Add(new Box(ls.Min(s => Math.Min(s.R1, s.R2)), ls.Min(s => Math.Min(s.U1, s.U2)), ls.Max(s => Math.Max(s.R1, s.R2)), ls.Max(s => Math.Max(s.U1, s.U2))));
                    }
                }
                catch { }
                if (all.Count == 0) continue;
                var outer = loops.OrderByDescending(b => b.Area).First();
                rect[r.Id.IntegerValue] = outer; bnd[r.Id.IntegerValue] = all;
                holes[r.Id.IntegerValue] = loops.Where(b => b != outer).ToList();
                double z0 = (r.Level != null ? r.Level.ProjectElevation : 0) + r.BaseOffset;
                zFloor[r.Id.IntegerValue] = z0;
                zTest[r.Id.IntegerValue] = z0 + Math.Min(1000 / MM, Math.Max(r.UnboundedHeight, 0.1) / 2);
            }
            Func<Room, double, double, bool> inRoom = (r, rr, uu) => { try { return r.IsPointInRoom(P(rr, uu, zTest[r.Id.IntegerValue])); } catch { return false; } };

            var nested = new List<string>(); var stairs = new List<string>(); var problems = new List<string>(); var items = new List<object>(); var levelsOut = new List<string>();
            var placed = new List<Box>();
            int has = 0;
            double w = size[0] * sc, h = size[1] * sc, c = clearMm * sc;
            // box of the symbol + text for a spot at (r, u): symbol around the point, text to the right
            Func<double, double, Box> boxAt = (rr, uu) => new Box(rr - 1.8 * sc, uu - 1.8 * sc, rr - 1.8 * sc + w, uu - 1.8 * sc + h).Grow(c);

            foreach (var r in rooms)
            {
                int id = r.Id.IntegerValue;
                if (!rect.ContainsKey(id)) continue;
                if (only != null && !only.Contains(id)) continue;
                string label = r.Number + " " + r.Name;
                var box = rect[id];
                double areaM2 = r.Area * 0.09290304;
                // FL1b: host floor pieces of this room (centre of the part inside the crop lies in the room)
                var rp = pieces.Where(p =>
                {
                    var q = crop == null ? p.B : new Box(Math.Max(p.B.R0, crop.R0), Math.Max(p.B.U0, crop.U0), Math.Min(p.B.R1, crop.R1), Math.Min(p.B.U1, crop.U1));
                    if (crop != null && (q.R0 >= q.R1 || q.U0 >= q.U1)) return false;
                    return inRoom(r, (q.R0 + q.R1) / 2, (q.U0 + q.U1) / 2);
                }).ToList();
                bool multi = rp.Any(p => !p.Flat) || rp.Where(p => p.Flat).Select(p => Math.Round(p.Zmax / 5)).Distinct().Count() > 1;
                if (!multi && spotPts.Any(p => inRoom(r, F(p)[0], F(p)[1]))) { has++; continue; }
                // FL1: enclosed by another room (inside one of its inner loops) and small -> no spot
                var host = rooms.FirstOrDefault(o => o.Id != r.Id && holes.ContainsKey(o.Id.IntegerValue)
                    && holes[o.Id.IntegerValue].Any(hb => hb.Grow(1).Holds(box.R0, box.U0) && hb.Grow(1).Holds(box.R1, box.U1)));
                if (host != null && areaM2 < minArea) { nested.Add(label + " (" + Math.Round(areaM2, 1) + " m2 in " + host.Number + ")"); continue; }
                if (stairText.Length > 0 && (r.Name ?? "").ToUpperInvariant().Contains(stairText)) { stairs.Add(label); continue; }
                if (string.IsNullOrEmpty(typeName)) { problems.Add(label + ": no spot type (pass typeName)"); continue; }

                if (multi)
                {
                    // targets: one per flat level (largest piece), both ends of every sloped piece
                    var tg = new List<Tuple<Piece, double, double, double, string>>();
                    foreach (var g in rp.Where(p => p.Flat).GroupBy(p => Math.Round(p.Zmax / 5)))
                    {
                        var big = g.OrderByDescending(p => p.B.Area).First();
                        // anchor: the middle of the part of the piece inside the room and the crop
                        var sp = new List<double[]>();
                        for (int i = 0; i <= 16; i++) for (int k = 0; k <= 16; k++)
                        {
                            double rr = big.B.R0 + (big.B.R1 - big.B.R0) * i / 16.0, uu = big.B.U0 + (big.B.U1 - big.B.U0) * k / 16.0;
                            if ((crop == null || crop.Holds(rr, uu)) && inRoom(r, rr, uu)) sp.Add(new[] { rr, uu });
                        }
                        if (sp.Count == 0) continue;
                        double mr = sp.Average(q => q[0]), mu = sp.Average(q => q[1]);
                        var an = sp.OrderBy(q => Math.Pow(q[0] - mr, 2) + Math.Pow(q[1] - mu, 2)).First();
                        tg.Add(Tuple.Create(big, an[0], an[1], big.Zmax, "flat"));
                    }
                    double inset = Math.Max(1.5 * sc, 200);
                    Action<Piece, double, double, double, string> addEnd = (p, er, eu, ez, what) =>
                    {
                        if (rp.Any(o => o.Flat && Math.Abs(o.Zmax - ez) < 5 && o.B.Grow(inset + 100).Holds(er, eu))) return;
                        if (tg.Any(t => Math.Abs(t.Item4 - ez) < 5 && Math.Sqrt(Math.Pow(t.Item2 - er, 2) + Math.Pow(t.Item3 - eu, 2)) < 3000)) return;
                        if (crop != null && !crop.Holds(er, eu)) return; // that end is drawn in another view
                        tg.Add(Tuple.Create(p, er, eu, ez, what));
                    };
                    foreach (var p in rp.Where(p => !p.Flat))
                    {
                        // end edges: face boundary points furthest along / against the down-slope direction, mid-point, inset
                        var pr = p.Pts.Select(q => new { q, s = q[0] * p.DR + q[1] * p.DU }).ToList();
                        double sMax = pr.Max(x => x.s), sMin = pr.Min(x => x.s);
                        var lo = pr.Where(x => x.s > sMax - 5).Select(x => x.q).ToList();
                        var hi = pr.Where(x => x.s < sMin + 5).Select(x => x.q).ToList();
                        double di = Math.Min(1, (sMax - sMin) / 2); // 1 mm in: the spot reads the end height (10 mm on a 5 % ramp already shows 0.5 mm off)
                        // the middle of the part of the end edge (moved in by di) that lies in the room and the crop
                        Func<List<double[]>, double, double[]> mid = (ed, sg) =>
                        {
                            var a = ed.OrderBy(q => q[0] * -p.DU + q[1] * p.DR).First(); var b = ed.OrderBy(q => q[0] * -p.DU + q[1] * p.DR).Last();
                            var ok2 = new List<double[]>();
                            for (int k = 0; k <= 40; k++)
                            {
                                double rr = a[0] + (b[0] - a[0]) * k / 40.0 + sg * p.DR * di, uu = a[1] + (b[1] - a[1]) * k / 40.0 + sg * p.DU * di;
                                if ((crop == null || crop.Holds(rr, uu)) && inRoom(r, rr, uu)) ok2.Add(new[] { rr, uu });
                            }
                            return ok2.Count == 0 ? null : ok2[ok2.Count / 2];
                        };
                        var mLo = mid(lo, -1); var mHi = mid(hi, 1);
                        if (mLo != null) addEnd(p, mLo[0], mLo[1], p.Zmin, "ramp low end");
                        if (mHi != null) addEnd(p, mHi[0], mHi[1], p.Zmax, "ramp high end");
                    }
                    var mSegs = segs.Where((s, i) => i < floorSeg0 || i >= floorSeg1).ToList();
                    foreach (var t in tg)
                    {
                        var pc = t.Item1; double ar = t.Item2, au = t.Item3;
                        if (spotPts.Select(F).Any(q => pc.B.Grow(100).Holds(q[0], q[1]) && (t.Item5 == "flat" || Math.Sqrt(Math.Pow(q[0] - ar, 2) + Math.Pow(q[1] - au, 2)) < 4000))) { has++; continue; }
                        var inner = pc.B.Grow(t.Item5 == "flat" ? -50 : -0.5);
                        var zoneM = pc.B.Grow((w + h) * 2);
                        var lS = mSegs.Where(s => SegHits(s, zoneM)).Concat(bnd[id]).ToList();
                        var lA = annBoxes.Where(b => b.Hits(zoneM)).ToList();
                        Func<double, double, bool> okM = (rr, uu) =>
                        {
                            if (!inner.Holds(rr, uu)) return false;
                            var bx = boxAt(rr, uu);
                            if (crop != null && (bx.R0 < crop.R0 || bx.R1 > crop.R1 || bx.U0 < crop.U0 || bx.U1 > crop.U1)) return false;
                            var pts = new[] { new[] { bx.R0, bx.U0 }, new[] { bx.R1, bx.U0 }, new[] { bx.R0, bx.U1 }, new[] { bx.R1, bx.U1 }, new[] { rr, uu } };
                            if (!pts.All(q => inRoom(r, q[0], q[1]))) return false;
                            return !lS.Any(s => SegHits(s, bx)) && !lA.Any(b => b.Hits(bx)) && !placed.Any(b => b.Hits(bx));
                        };
                        double sm = Math.Max(h / 4, 1), lm = maxShiftMm * sc;
                        double[] pk = null;
                        var cl = new List<double[]>();
                        if (t.Item5 == "flat") { for (double dx = -lm; dx <= lm + 1e-6; dx += sm) for (double dy = -lm; dy <= lm + 1e-6; dy += sm) cl.Add(new[] { ar + dx, au + dy }); }
                        else for (double dt = -lm * 3; dt <= lm * 3 + 1e-6; dt += sm) cl.Add(new[] { ar - pc.DU * dt, au + pc.DR * dt }); // ramp end: along the end edge only
                        foreach (var q in cl.OrderBy(q => Math.Pow(q[0] - ar, 2) + Math.Pow(q[1] - au, 2))) if (okM(q[0], q[1])) { pk = q; break; }
                        if (pk == null) { problems.Add(label + ": " + t.Item5 + " " + Math.Round(t.Item4) + " on floor " + pc.Id + ": no clear place"); continue; }
                        placed.Add(boxAt(pk[0], pk[1]));
                        items.Add(new { kind = "spot", elementId = pc.Id, typeName, right = Math.Round(pk[0]), up = Math.Round(pk[1]), note = label + " · " + t.Item5 + " · ~" + Math.Round(t.Item4) + " mm" });
                    }
                    levelsOut.Add(label + ": " + tg.Count + " levels/ends");
                    continue;
                }

                // anchor: diagonal intersection, else the nearest point inside the room
                var A = new[] { (box.R0 + box.R1) / 2, (box.U0 + box.U1) / 2 };
                double step = Math.Max(Math.Min(box.R1 - box.R0, box.U1 - box.U0) / 20, 50);
                if (!inRoom(r, A[0], A[1]))
                {
                    double[] best = null; double bd = double.MaxValue;
                    for (double rr = box.R0 + step / 2; rr < box.R1; rr += step)
                        for (double uu = box.U0 + step / 2; uu < box.U1; uu += step)
                        {
                            double dd = Math.Pow(rr - A[0], 2) + Math.Pow(uu - A[1], 2);
                            if (dd < bd && inRoom(r, rr, uu)) { bd = dd; best = new[] { rr, uu }; }
                        }
                    if (best == null) { problems.Add(label + ": no point inside the room found"); continue; }
                    A = best;
                }
                var zone = box.Grow((w + h) * 2);
                var lSegs = segs.Where(s => SegHits(s, zone)).Concat(bnd[id]).ToList();
                var lAnn = annBoxes.Where(b => b.Hits(zone)).ToList();
                Func<double, double, bool> ok = (rr, uu) =>
                {
                    var bx = boxAt(rr, uu);
                    if (crop != null && (bx.R0 < crop.R0 || bx.R1 > crop.R1 || bx.U0 < crop.U0 || bx.U1 > crop.U1)) return false;
                    var pts = new[] { new[] { bx.R0, bx.U0 }, new[] { bx.R1, bx.U0 }, new[] { bx.R0, bx.U1 }, new[] { bx.R1, bx.U1 }, new[] { rr, uu } };
                    if (!pts.All(p => inRoom(r, p[0], p[1]))) return false;
                    return !lSegs.Any(s => SegHits(s, bx)) && !lAnn.Any(b => b.Hits(bx)) && !placed.Any(b => b.Hits(bx));
                };
                // candidates: around the anchor first (maxShiftMm), then anywhere in the room, nearest first
                double s1 = Math.Max(h / 2, 1), lim = maxShiftMm * sc;
                var cands = new List<double[]>();
                for (double dx = -lim; dx <= lim + 1e-6; dx += s1) for (double dy = -lim; dy <= lim + 1e-6; dy += s1) cands.Add(new[] { A[0] + dx - w / 2 + 1.8 * sc, A[1] + dy });
                var far = new List<double[]>();
                for (double rr = box.R0 + step / 2; rr < box.R1; rr += step) for (double uu = box.U0 + step / 2; uu < box.U1; uu += step) far.Add(new[] { rr, uu });
                double[] pick = null; bool near = true;
                foreach (var p in cands.OrderBy(p => Math.Pow(p[0] + w / 2 - 1.8 * sc - A[0], 2) + Math.Pow(p[1] - A[1], 2))) if (ok(p[0], p[1])) { pick = p; break; }
                if (pick == null) { near = false; foreach (var p in far.OrderBy(p => Math.Pow(p[0] - A[0], 2) + Math.Pow(p[1] - A[1], 2))) if (ok(p[0], p[1])) { pick = p; break; } }
                if (pick == null) { problems.Add(label + ": no clear place in the room"); continue; }

                // floor under the point at the room's level
                int floorId = 0; double elev = 0;
                if (ri != null)
                {
                    double z0 = zFloor[id];
                    var q = P(pick[0], pick[1], z0 + 2000 / MM);
                    var hit = ri.Find(q, -XYZ.BasisZ).Select(x => x.GetReference()).Where(x => x != null)
                        .Select(x => new { Ref = x, Z = x.GlobalPoint.Z }).Where(x => x.Z > z0 - 400 / MM && x.Z < z0 + 600 / MM)
                        .OrderBy(x => Math.Abs(x.Z - z0)).FirstOrDefault();
                    if (hit != null) { floorId = hit.Ref.ElementId.IntegerValue; elev = hit.Z * MM; }
                }
                if (floorId == 0) { problems.Add(label + ": no host floor under the room (linked floor?)"); continue; }
                placed.Add(boxAt(pick[0], pick[1]));
                items.Add(new { kind = "spot", elementId = floorId, typeName, right = Math.Round(pick[0]), up = Math.Round(pick[1]), note = label + " · " + (near ? "centre" : "moved in room") + " · " + Math.Round(elev) + " mm" });
            }

            outViews.Add(new
            {
                ViewId = vid, View = v.Name, Rooms = rooms.Count, HasSpot = has, Missing = items.Count + problems.Count,
                SpotType = typeName ?? "?", Levels = levelsOut, Nested = nested, StairRooms = stairs, Problems = problems, Items = items
            });
        }
        return new { Views = outViews };
    }
}
