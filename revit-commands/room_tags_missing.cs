/* mcp-tool
{
  "description": "Read-only, plans / ceiling plans: rooms with no room tag + tag body centre per RT1-RT8 (smallest room first, every room, short / aligned leaders).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "roomIds": {
        "type": "array",
        "items": {
          "type": "number"
        },
        "description": "only these rooms (default: every untagged room of the view)"
      },
      "typeName": {
        "type": "string",
        "description": "room tag type the user chose; empty = type of the nearest tagged rooms"
      },
      "tagSizeMm": {
        "type": "array",
        "items": {
          "type": "number"
        },
        "description": "tag width, height on paper mm (profile); default: box of a placed tag"
      },
      "clearMm": {
        "type": "number",
        "description": "paper mm kept clear around the tag, default 1"
      },
      "maxShiftMm": {
        "type": "number",
        "description": "paper mm the head may move off the diagonal point inside, default 4"
      },
      "maxLeaderMm": {
        "type": "number",
        "description": "paper mm beyond the room for the first search, widened x2, x4 (RT6), default 30"
      },
      "bubbleMm": {
        "type": "number",
        "description": "grid bubble diameter on paper mm kept clear, default 10 (0 = ignore bubbles)"
      },
      "outPath": {
        "type": "string",
        "description": "write Items + Problems to this JSON file (one view per call); result = counts only"
      }
    },
    "required": [
      "viewIds"
    ]
  },
  "timeoutSeconds": 180,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only. For each plan / ceiling plan view: the rooms seen in the view (rooms of the view's level inside the crop,
//    plus rooms the view shows from other levels) that have no room tag in it, and for each one a head position that
//    follows drafting-annotation.md §2 rules RT1-RT4:
//    - anchor = intersection of the two diagonals of the room's rectangle in the view frame (its centre); when that
//      point is not in the room (L-shaped rooms), the nearest point inside the room;
//    - inside: a tag box (tagSizeMm x view scale, + clearMm) centred on the anchor, or moved at most maxShiftMm, that
//      lies fully in the room and crosses no wall, no floor edge (floor plans) / ceiling edge (ceiling plans), no room
//      boundary, no dim line and overlaps no tag / text / spot / other placed head -> no leader;
//    - otherwise outside: the head straight above / right / below / left of the anchor (shortest leader first), box
//      clear of the same obstacles and inside the crop, leader end = anchor (orthogonal, no elbow); the leader (box
//      edge -> anchor) crosses no tag / text / spot box, no placed head and no other room tag leader.
//    Obstacles also: grid bubbles shown in the view (a bubbleMm square at each visible bubble end) and the room tags
//    already in the view: tag without leader = its own box; tag with leader = the leader (head -> elbow -> end) plus
//    a tagSizeMm body at the far end of its box from the leader end (room tag boxes include the leader).
//    The positions are the BODY centre of the tag: place them with a command that centres the tag body on the point
//    (a room tag head is not its body centre); annot_place puts the head there.
//    RT5-RT8 (2026-10-21): rooms are taken smallest area first; outside candidates are scored by leader length
//    (+6 mm for a one-elbow leader, path HV / VH) minus 8 mm when the tag lines up (same vertical centre or same left
//    edge) with a room tag within 80 mm; within each search ring (1x, 2x, 4x maxLeaderMm) the best RS1-RS4 score wins; none clear -> the leader may cross other leaders, then text,
//    lets the box touch model lines (note "CHECK"), and last puts the tag at the room centre ("FORCED"): every room
//    gets an item. Items with an elbow carry path HV / VH for roomtag_place_ortho.
//    Tag type = typeName, else the most common type among the 3 nearest tagged rooms of the view (NeighbourType).
//    Tag size: tagSizeMm, else the box of a placed tag of that type (wider than the text: room tag boxes span the
//    family's whole label frame, so positions come out conservative).
//    Items are ready for annot_place {mode:'preview'|'apply', viewId, items} (kind 'roomtag', leader, endRight/endUp).
//    Walls / floors / ceilings of linked models are not seen; the room boundary still stops the box at linked walls.
//    Coordinates: view frame mm (Right / Up from View.Origin), as annot_place / view_elem_boxes.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomTagsMissing
{
    const double MM = 304.8;

    class Seg { public double R1, U1, R2, U2; }
    class Box
    {
        public double R0, U0, R1, U1;
        public Box(double r0, double u0, double r1, double u1) { R0 = Math.Min(r0, r1); U0 = Math.Min(u0, u1); R1 = Math.Max(r0, r1); U1 = Math.Max(u0, u1); }
        public bool Hits(Box b) { return R0 < b.R1 && b.R0 < R1 && U0 < b.U1 && b.U0 < U1; }
        public Box Grow(double d) { return new Box(R0 - d, U0 - d, R1 + d, U1 + d); }
    }

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

    // RS1: two leader segments lying on each other (same line, overlapping by more than tol)
    static bool Overlap(Seg a, Seg b, double tol)
    {
        bool ah = Math.Abs(a.U1 - a.U2) < 1, bh = Math.Abs(b.U1 - b.U2) < 1, av = Math.Abs(a.R1 - a.R2) < 1, bv = Math.Abs(b.R1 - b.R2) < 1;
        if (ah && bh && Math.Abs(a.U1 - b.U1) < tol) return Math.Min(Math.Max(a.R1, a.R2), Math.Max(b.R1, b.R2)) - Math.Max(Math.Min(a.R1, a.R2), Math.Min(b.R1, b.R2)) > tol;
        if (av && bv && Math.Abs(a.R1 - b.R1) < tol) return Math.Min(Math.Max(a.U1, a.U2), Math.Max(b.U1, b.U2)) - Math.Max(Math.Min(a.U1, a.U2), Math.Min(b.U1, b.U2)) > tol;
        return false;
    }

    // do two segments cross (touching counts)?
    static bool SegX(Seg a, Seg b)
    {
        Func<double, double, double, double, double, double, double> cr = (ax, ay, bx, by, cx, cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        double d1 = cr(b.R1, b.U1, b.R2, b.U2, a.R1, a.U1), d2 = cr(b.R1, b.U1, b.R2, b.U2, a.R2, a.U2);
        double d3 = cr(a.R1, a.U1, a.R2, a.U2, b.R1, b.U1), d4 = cr(a.R1, a.U1, a.R2, a.U2, b.R2, b.U2);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        Func<Seg, double, double, bool> on = (s, x, y) => Math.Min(s.R1, s.R2) - 1 <= x && x <= Math.Max(s.R1, s.R2) + 1 && Math.Min(s.U1, s.U2) - 1 <= y && y <= Math.Max(s.U1, s.U2) + 1;
        return (Math.Abs(d1) < 1e-6 && on(b, a.R1, a.U1)) || (Math.Abs(d2) < 1e-6 && on(b, a.R2, a.U2))
            || (Math.Abs(d3) < 1e-6 && on(a, b.R1, b.U1)) || (Math.Abs(d4) < 1e-6 && on(a, b.R2, b.U2));
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var viewIds = ((JArray)args["viewIds"]).Select(t => (int)t).ToList();
        HashSet<int> only = args["roomIds"] is JArray ra && ra.Count > 0 ? new HashSet<int>(ra.Select(t => (int)t)) : null;
        string typeArg = ((string)args["typeName"] ?? "").Trim();
        double clearMm = (double?)args["clearMm"] ?? 1, maxShiftMm = (double?)args["maxShiftMm"] ?? 4, maxLeaderMm = (double?)args["maxLeaderMm"] ?? 30;
        double bubbleMm = (double?)args["bubbleMm"] ?? 10;
        double[] sizeArg = args["tagSizeMm"] is JArray sa && sa.Count == 2 ? new[] { (double)sa[0], (double)sa[1] } : null;

        var allTags = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().OfType<RoomTag>().ToList();
        Func<Element, string> typeOf = e => doc.GetElement(e.GetTypeId())?.Name ?? "";
        var sizeCache = new Dictionary<string, double[]>();
        var sizeSrc = new Dictionary<string, string>();
        // paper size of a tag type: argument, else the box of a placed tag of that type in its own view
        Func<string, double[]> sizeOf = tn =>
        {
            if (sizeArg != null) { sizeSrc[tn] = "tagSizeMm"; return sizeArg; }
            if (sizeCache.ContainsKey(tn)) return sizeCache[tn];
            double[] res = null;
            foreach (var t in allTags.Where(t => typeOf(t) == tn))
            {
                var ov = doc.GetElement(t.OwnerViewId) as View;
                var bb = ov == null ? null : t.get_BoundingBox(ov);
                if (bb == null || ov.Scale <= 0) continue;
                double w = Math.Abs((bb.Max - bb.Min).DotProduct(ov.RightDirection)) * MM / ov.Scale;
                double h = Math.Abs((bb.Max - bb.Min).DotProduct(ov.UpDirection)) * MM / ov.Scale;
                if (w > 0.5 && h > 0.5) { res = new[] { Math.Round(w, 1), Math.Round(h, 1) }; break; }
            }
            sizeCache[tn] = res; sizeSrc[tn] = res == null ? "none" : "placed tag box (wider than the text)";
            return res;
        };

        var outViews = new List<object>();
        foreach (var vid in viewIds)
        {
            var v = doc.GetElement(new ElementId(vid)) as ViewPlan;
            if (v == null) { outViews.Add(new { ViewId = vid, Error = "not a plan / ceiling plan view" }); continue; }
            XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
            double sc = v.Scale;
            Func<XYZ, double[]> F = p => new[] { (p - O).DotProduct(R) * MM, (p - O).DotProduct(U) * MM };
            Func<double, double, double, XYZ> P = (r, u, z) => { var p = O + R * (r / MM) + U * (u / MM); return new XYZ(p.X, p.Y, z); };

            // crop rectangle in the view frame
            Box crop = null;
            if (v.CropBoxActive)
            {
                var cb = v.CropBox; var tr = cb.Transform;
                var cs = new[] { cb.Min, cb.Max, new XYZ(cb.Min.X, cb.Max.Y, cb.Min.Z), new XYZ(cb.Max.X, cb.Min.Y, cb.Min.Z) }.Select(p => F(tr.OfPoint(p))).ToList();
                crop = new Box(cs.Min(c => c[0]), cs.Min(c => c[1]), cs.Max(c => c[0]), cs.Max(c => c[1]));
            }

            // room tags of the view
            var tags = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().OfType<RoomTag>().ToList();
            var tagged = new Dictionary<int, RoomTag>();
            foreach (var t in tags) { try { var rm = t.Room; if (rm != null) tagged[rm.Id.IntegerValue] = t; } catch { } }

            // rooms seen in the view
            var cand = new Dictionary<int, Room>();
            try { foreach (var r in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>()) cand[r.Id.IntegerValue] = r; } catch { }
            if (v.GenLevel != null)
                foreach (var r in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.LevelId == v.GenLevel.Id))
                    cand[r.Id.IntegerValue] = r;
            int notPlaced = 0;
            var rooms = new List<Room>();
            foreach (var r in cand.Values)
            {
                if (r.Area <= 0 || !(r.Location is LocationPoint lp)) { notPlaced++; continue; }
                var lpF = F(lp.Point);
                if (crop != null && (lpF[0] < crop.R0 || lpF[0] > crop.R1 || lpF[1] < crop.U0 || lpF[1] > crop.U1)) continue;
                rooms.Add(r);
            }

            // obstacles: wall outlines, floor (floor plan) or ceiling (ceiling plan) edges, dim lines, annotation boxes
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
                            if (Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) < 1) continue; // vertical edge seen end-on
                            segs.Add(new Seg { R1 = a[0], U1 = a[1], R2 = b[0], U2 = b[1] });
                        }
                    }
                }
            };
            var cats = new List<BuiltInCategory> { BuiltInCategory.OST_Walls };
            cats.Add(v.ViewType == ViewType.CeilingPlan ? BuiltInCategory.OST_Ceilings : BuiltInCategory.OST_Floors);
            var gopt = new Options { View = v };
            foreach (var bic in cats)
                foreach (var e in new FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType())
                    try { addGeo(e.get_Geometry(gopt)); } catch { }

            var annBoxes = new List<Box>();
            var leads = new List<Seg>(); // room tag leaders: existing + placed in this run
            var tagBoxes = new List<Box>(); // existing room tag bodies (RT8 alignment)
            // grid bubbles shown in the view
            if (bubbleMm > 0)
                foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
                {
                    try
                    {
                        var cs = g.GetCurvesInView(DatumExtentType.ViewSpecific, v);
                        if (cs == null || cs.Count == 0 || !(cs[0] is Line gl)) continue;
                        double rB = bubbleMm * sc / 2;
                        var a = F(gl.GetEndPoint(0)); var b = F(gl.GetEndPoint(1));
                        double len = Math.Sqrt(Math.Pow(b[0] - a[0], 2) + Math.Pow(b[1] - a[1], 2)); if (len < 1) continue;
                        double ur = (b[0] - a[0]) / len, uu = (b[1] - a[1]) / len;
                        if (g.IsBubbleVisibleInView(DatumEnds.End0, v)) annBoxes.Add(new Box(a[0] - ur * rB - rB, a[1] - uu * rB - rB, a[0] - ur * rB + rB, a[1] - uu * rB + rB));
                        if (g.IsBubbleVisibleInView(DatumEnds.End1, v)) annBoxes.Add(new Box(b[0] + ur * rB - rB, b[1] + uu * rB - rB, b[0] + ur * rB + rB, b[1] + uu * rB + rB));
                    }
                    catch { }
                }
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
            {
                if (e.OwnerViewId != v.Id || e.Category == null) continue;
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
                    if (e is RoomTag rt)
                    {
                        var tb = rt.get_BoundingBox(v);
                        var sz = sizeOf(typeOf(rt)) ?? sizeArg;
                        if (tb == null)
                        {
                            if (sz == null) continue;
                            var hp = F(rt.TagHeadPosition);
                            annBoxes.Add(new Box(hp[0] - sz[0] * sc / 2, hp[1] - sz[1] * sc / 2, hp[0] + sz[0] * sc / 2, hp[1] + sz[1] * sc / 2));
                            continue;
                        }
                        var tbx = new Box(F(tb.Min)[0], F(tb.Min)[1], F(tb.Max)[0], F(tb.Max)[1]);
                        if (!rt.HasLeader || sz == null) { annBoxes.Add(tbx); tagBoxes.Add(tbx); continue; }
                        // leader: head -> elbow -> end; body at the far end of the box from the leader end
                        var le = F(rt.LeaderEnd); var hd = F(rt.TagHeadPosition);
                        double[] el = null; try { if (rt.HasElbow) el = F(rt.LeaderElbow); } catch { }
                        var lpts = new List<double[]> { hd }; if (el != null) lpts.Add(el); lpts.Add(le);
                        for (int i = 1; i < lpts.Count; i++) { var ls = new Seg { R1 = lpts[i - 1][0], U1 = lpts[i - 1][1], R2 = lpts[i][0], U2 = lpts[i][1] }; segs.Add(ls); leads.Add(ls); }
                        double bw = Math.Min(sz[0] * sc, tbx.R1 - tbx.R0), bh = Math.Min(sz[1] * sc, tbx.U1 - tbx.U0);
                        double dl = le[0] - tbx.R0, dr = tbx.R1 - le[0], dd = le[1] - tbx.U0, du = tbx.U1 - le[1];
                        double m = new[] { dl, dr, dd, du }.Min();
                        if (m == dl) annBoxes.Add(new Box(tbx.R1 - bw, tbx.U0, tbx.R1, tbx.U1));        // end at left -> body right
                        else if (m == dr) annBoxes.Add(new Box(tbx.R0, tbx.U0, tbx.R0 + bw, tbx.U1));   // end at right -> body left
                        else if (m == dd) annBoxes.Add(new Box(tbx.R0, tbx.U1 - bh, tbx.R1, tbx.U1));   // end below -> body top
                        else annBoxes.Add(new Box(tbx.R0, tbx.U0, tbx.R1, tbx.U0 + bh));                 // end above -> body bottom
                        tagBoxes.Add(annBoxes[annBoxes.Count - 1]);
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

            // per room: anchor, rectangle, boundary
            var bopt = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
            var info = new Dictionary<int, object[]>(); // id -> {Box rect, double[] anchor, List<Seg> boundary, double z}
            foreach (var r in rooms)
            {
                var bsegs = new List<Seg>();
                try
                {
                    foreach (var loop in r.GetBoundarySegments(bopt))
                        foreach (var bs in loop)
                        {
                            var pts = bs.GetCurve().Tessellate();
                            for (int i = 1; i < pts.Count; i++) { var a = F(pts[i - 1]); var b = F(pts[i]); bsegs.Add(new Seg { R1 = a[0], U1 = a[1], R2 = b[0], U2 = b[1] }); }
                        }
                }
                catch { }
                if (bsegs.Count == 0) continue;
                var rect = new Box(bsegs.Min(s => Math.Min(s.R1, s.R2)), bsegs.Min(s => Math.Min(s.U1, s.U2)), bsegs.Max(s => Math.Max(s.R1, s.R2)), bsegs.Max(s => Math.Max(s.U1, s.U2)));
                double z = (r.Level != null ? r.Level.ProjectElevation : 0) + r.BaseOffset + Math.Min(1000 / MM, Math.Max(r.UnboundedHeight, 0.1) / 2);
                info[r.Id.IntegerValue] = new object[] { rect, new[] { (rect.R0 + rect.R1) / 2, (rect.U0 + rect.U1) / 2 }, bsegs, z };
            }
            Func<Room, double, double, bool> inRoom = (r, rr, uu) => { try { return r.IsPointInRoom(P(rr, uu, (double)info[r.Id.IntegerValue][3])); } catch { return false; } };

            // RT5: smallest room first
            var todo = rooms.Where(r => !tagged.ContainsKey(r.Id.IntegerValue) && (only == null || only.Contains(r.Id.IntegerValue)) && info.ContainsKey(r.Id.IntegerValue))
                .OrderBy(r => r.Area).ToList();
            var placed = new List<Box>();
            var items = new List<object>();
            var problems = new List<string>();
            var pending = new List<object[]>(); // room, type, size, anchor, local segs, local ann boxes, rect, label
            int nIn = 0, nOut = 0, nForced = 0;
            var scores = new List<double>();
            double maxLevel = maxLeaderMm * 4; // RT6: the search widens up to 4 x maxLeaderMm before relaxing

            foreach (var r in todo)
            {
                string label = r.Number + " " + r.Name;
                var inf = info[r.Id.IntegerValue];
                var rect = (Box)inf[0]; var A = (double[])inf[1];
                // neighbour type: most common type among the 3 nearest tagged rooms
                string nType = tagged.Values.Select(t => { var hp = F(t.TagHeadPosition); return new { T = typeOf(t), D = Math.Pow(hp[0] - A[0], 2) + Math.Pow(hp[1] - A[1], 2) }; })
                    .OrderBy(x => x.D).Take(3).GroupBy(x => x.T).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                string tn = typeArg.Length > 0 ? typeArg : nType;
                if (string.IsNullOrEmpty(tn)) { problems.Add(label + ": no type (pass typeName)"); continue; }
                var sz = sizeOf(tn);
                if (sz == null) { problems.Add(label + ": tag size unknown for '" + tn + "' (pass tagSizeMm)"); continue; }
                // anchor: diagonal intersection, else the nearest point inside the room
                if (!inRoom(r, A[0], A[1]))
                {
                    double step = Math.Max(Math.Min(rect.R1 - rect.R0, rect.U1 - rect.U0) / 20, 50);
                    double[] best = null; double bd = double.MaxValue;
                    for (double rr = rect.R0 + step / 2; rr < rect.R1; rr += step)
                        for (double uu = rect.U0 + step / 2; uu < rect.U1; uu += step)
                        {
                            double dd = Math.Pow(rr - A[0], 2) + Math.Pow(uu - A[1], 2);
                            if (dd < bd && inRoom(r, rr, uu)) { bd = dd; best = new[] { rr, uu }; }
                        }
                    if (best == null) { problems.Add(label + ": no point inside the room found"); continue; }
                    A = best;
                }
                double reach = Math.Max(rect.R1 - rect.R0, rect.U1 - rect.U0) + (maxLevel + sz[0] + sz[1]) * sc;
                var zone = new Box(A[0] - reach, A[1] - reach, A[0] + reach, A[1] + reach);
                var lSegs = segs.Where(s => SegHits(s, zone)).Concat((List<Seg>)inf[2]).ToList();
                var lAnn = annBoxes.Where(b => b.Hits(zone)).ToList();
                pending.Add(new object[] { r, tn, sz, A, lSegs, lAnn, rect, label });
            }

            var placedLeads = new List<Seg>();
            Func<Box, bool> inCrop = bx => crop == null || (bx.R0 >= crop.R0 && bx.R1 <= crop.R1 && bx.U0 >= crop.U0 && bx.U1 <= crop.U1);
            Func<Box, List<Box>, bool> annFree = (bx, la) => !la.Any(b => b.Hits(bx)) && !placed.Any(b => b.Hits(bx)) && !placedLeads.Any(s => SegHits(s, bx));
            Func<Box, List<Seg>, bool> segFree = (bx, ls) => !ls.Any(s => SegHits(s, bx));
            // leader: no tag / text box, no placed head, no other leader on its way
            Func<Seg, List<Box>, bool> leadClear = (L, la) => !la.Any(b => SegHits(L, b)) && !placed.Any(b => SegHits(L, b))
                && !leads.Any(s => SegX(L, s)) && !placedLeads.Any(s => SegX(L, s));
            Func<Seg, List<Box>, bool> leadSoft = (L, la) => !la.Any(b => SegHits(L, b)) && !placed.Any(b => SegHits(L, b));
            double tolO = 0.3 * sc;
            // RS4: point of a tag body on another room
            Func<Room, double, double, bool> onOther = (r0, pr, pu) => rooms.Any(ro => ro.Id != r0.Id && info.ContainsKey(ro.Id.IntegerValue)
                && ((Box)info[ro.Id.IntegerValue][0]).R0 <= pr && pr <= ((Box)info[ro.Id.IntegerValue][0]).R1
                && ((Box)info[ro.Id.IntegerValue][0]).U0 <= pu && pu <= ((Box)info[ro.Id.IntegerValue][0]).U1 && inRoom(ro, pr, pu));
            double tol = 0.5 * sc, near = 80 * sc, alignBonus = 8, elbowPen = 6;
            // RT8: same row (vertical centre) or same column (left edge) as a nearby room tag
            Func<Box, bool> aligned = bx => placed.Concat(tagBoxes).Any(p =>
                Math.Abs((p.R0 + p.R1) / 2 - (bx.R0 + bx.R1) / 2) < near && Math.Abs((p.U0 + p.U1) / 2 - (bx.U0 + bx.U1) / 2) < near
                && (Math.Abs((p.U0 + p.U1) / 2 - (bx.U0 + bx.U1) / 2) < tol || Math.Abs(p.R0 - bx.R0) < tol));

            foreach (var pd in pending)
            {
                var r = (Room)pd[0]; var sz = (double[])pd[2]; var A = (double[])pd[3]; var rect = (Box)pd[6];
                var ls = (List<Seg>)pd[4]; var la = (List<Box>)pd[5]; string tn = (string)pd[1], label = (string)pd[7];
                double longSide = Math.Max(rect.R1 - rect.R0, rect.U1 - rect.U0);
                double w = sz[0] * sc, h = sz[1] * sc, c = clearMm * sc, s = Math.Max(h / 2, 1), lim = maxShiftMm * sc;
                // values a tag centre may snap to so it lines up with a nearby tag (RT8)
                var nb = placed.Concat(tagBoxes).Where(p => Math.Abs((p.R0 + p.R1) / 2 - A[0]) < near + w && Math.Abs((p.U0 + p.U1) / 2 - A[1]) < near + h).ToList();
                var snapU = nb.Select(p => (p.U0 + p.U1) / 2).ToList();
                var snapR = nb.Select(p => p.R0 + w / 2 + c).ToList();

                // inside (RT4): box in the room, clear of everything; nearest to the anchor, aligned preferred
                var inC = new List<double[]>();
                // anywhere inside the room, nearest to the anchor first (a big room keeps its tag inside even when
                // smaller rooms took the spots round its centre)
                double lr = Math.Max(lim, (rect.R1 - rect.R0) / 2), lu = Math.Max(lim, (rect.U1 - rect.U0) / 2);
                for (double dx = -lr; dx <= lr + 1e-6; dx += s) for (double dy = -lu; dy <= lu + 1e-6; dy += s) inC.Add(new[] { A[0] + dx, A[1] + dy });
                foreach (var u in snapU) if (Math.Abs(u - A[1]) <= lu) for (double dx = -lr; dx <= lr + 1e-6; dx += s) inC.Add(new[] { A[0] + dx, u });
                foreach (var rr in snapR) if (Math.Abs(rr - A[0]) <= lr) for (double dy = -lu; dy <= lu + 1e-6; dy += s) inC.Add(new[] { rr, A[1] + dy });
                bool done = false;
                foreach (var q in inC.Select(p => new { P = p, B = new Box(p[0] - w / 2, p[1] - h / 2, p[0] + w / 2, p[1] + h / 2).Grow(c) })
                    .Select(x => new { x.P, x.B, S = Math.Sqrt(Math.Pow(x.P[0] - A[0], 2) + Math.Pow(x.P[1] - A[1], 2)) / sc - (aligned(x.B) ? alignBonus : 0) })
                    .OrderBy(x => x.S))
                {
                    var bx = q.B; double cr = q.P[0], cu = q.P[1];
                    if (!inCrop(bx) || !annFree(bx, la) || !segFree(bx, ls)) continue;
                    var pts = new[] { new[] { bx.R0, bx.U0 }, new[] { bx.R1, bx.U0 }, new[] { bx.R0, bx.U1 }, new[] { bx.R1, bx.U1 }, new[] { cr, bx.U0 }, new[] { cr, bx.U1 }, new[] { bx.R0, cu }, new[] { bx.R1, cu }, new[] { cr, cu } };
                    if (!pts.All(p => inRoom(r, p[0], p[1]))) continue;
                    placed.Add(bx); nIn++; done = true;
                    items.Add(new { kind = "roomtag", elementId = r.Id.IntegerValue, typeName = tn, right = Math.Round(cr), up = Math.Round(cu), leader = false, score = 10, note = label + " · inside" });
                    scores.Add(10);
                    break;
                }
                if (done) continue;

                // outside with a leader (RT4, RT7): straight or one right angle; shortest first, aligned preferred;
                // the search widens (RT6), then relaxes: leader may cross, then the box may touch model lines
                object best = null; Box bestBox = null; List<Seg> bestLead = null; string how = null; double bestScore = 0;
                for (int stage = 0; stage < 6 && best == null; stage++)
                {
                    double L = stage == 0 ? maxLeaderMm : stage == 1 ? maxLeaderMm * 2 : maxLeaderMm * 4; // near first: a far spot taken early blocks later rooms
                    bool crossLeads = stage >= 3, relaxLead = stage >= 4, relaxSeg = stage >= 5;
                    double hr = (rect.R1 - rect.R0) / 2 + L * sc + w, hu = (rect.U1 - rect.U0) / 2 + L * sc + h;
                    var rs = new List<double> { A[0] }; var us = new List<double> { A[1] };
                    for (double d = s; d <= hr; d += s) { rs.Add(A[0] + d); rs.Add(A[0] - d); }
                    for (double d = s; d <= hu; d += s) { us.Add(A[1] + d); us.Add(A[1] - d); }
                    rs.AddRange(snapR.Where(v => Math.Abs(v - A[0]) <= hr)); us.AddRange(snapU.Where(v => Math.Abs(v - A[1]) <= hu));
                    var cands = new List<object[]>(); // score, cr, cu, path, segs
                    foreach (var cr in rs)
                        foreach (var cu in us)
                        {
                            var bx = new Box(cr - w / 2, cu - h / 2, cr + w / 2, cu + h / 2).Grow(c);
                            bool xin = A[0] >= bx.R0 && A[0] <= bx.R1, yin = A[1] >= bx.U0 && A[1] <= bx.U1;
                            if (xin && yin) continue;
                            double bonus = aligned(bx) ? alignBonus : 0;
                            var opts = new List<object[]>();
                            if (Math.Abs(cr - A[0]) < 1 && !yin) opts.Add(new object[] { "", new List<Seg> { new Seg { R1 = cr, U1 = A[1] > cu ? bx.U1 : bx.U0, R2 = A[0], U2 = A[1] } } });
                            else if (Math.Abs(cu - A[1]) < 1 && !xin) opts.Add(new object[] { "", new List<Seg> { new Seg { R1 = A[0] > cr ? bx.R1 : bx.R0, U1 = cu, R2 = A[0], U2 = A[1] } } });
                            else
                            {
                                if (!xin) opts.Add(new object[] { "HV", new List<Seg> { new Seg { R1 = A[0] > cr ? bx.R1 : bx.R0, U1 = cu, R2 = A[0], U2 = cu }, new Seg { R1 = A[0], U1 = cu, R2 = A[0], U2 = A[1] } } });
                                if (!yin) opts.Add(new object[] { "VH", new List<Seg> { new Seg { R1 = cr, U1 = A[1] > cu ? bx.U1 : bx.U0, R2 = cr, U2 = A[1] }, new Seg { R1 = cr, U1 = A[1], R2 = A[0], U2 = A[1] } } });
                            }
                            foreach (var o in opts)
                            {
                                var sg = (List<Seg>)o[1];
                                double len = sg.Sum(x => Math.Sqrt(Math.Pow(x.R2 - x.R1, 2) + Math.Pow(x.U2 - x.U1, 2))) / sc;
                                // RS2, RS3: -5 for an elbow, -9 / -5 when longer than 3x / 2x the room long side
                                double lenM = len * sc;
                                int pen = ((string)o[0] == "" ? 0 : 5) + (lenM > 3 * longSide ? 9 : lenM > 2 * longSide ? 5 : 0);
                                cands.Add(new object[] { Math.Max(0, 10 - pen), cr, cu, o[0], sg, bx, len, len - bonus });
                            }
                        }
                    // best score first (RS1-RS4), then shortest / aligned; RS4 (on another room) is checked lazily
                    double bestS = -1, bestKey = 0;
                    foreach (var cd in cands.OrderByDescending(x => (int)x[0]).ThenBy(x => (double)x[7]))
                    {
                        double S0 = (int)cd[0], key = (double)cd[7];
                        if (bestS >= 0 && (S0 < bestS || (S0 == bestS && key >= bestKey))) break;
                        var bx = (Box)cd[5]; double cr = (double)cd[1], cu = (double)cd[2];
                        if (!inCrop(bx) || !annFree(bx, la)) continue;
                        if (!relaxSeg && !segFree(bx, ls)) continue;
                        var sg = (List<Seg>)cd[4];
                        if (sg.Any(x => leads.Any(y => Overlap(x, y, tolO)) || placedLeads.Any(y => Overlap(x, y, tolO)))) continue; // RS1: never on another leader
                        if (!relaxLead && !sg.All(x => crossLeads ? leadSoft(x, la) : leadClear(x, la))) continue;
                        if (inRoom(r, cr, cu)) continue; // the head sits outside its room
                        double S = Math.Max(0, S0 - (onOther(r, cr, cu) ? 5 : 0));
                        if (S < bestS || (S == bestS && key >= bestKey)) continue;
                        string path = (string)cd[3];
                        bestS = S; bestKey = key; bestBox = bx; bestLead = sg; bestScore = S;
                        how = (path == "" ? "straight" : "elbow " + path) + ", leader " + Math.Round((double)cd[6], 1) + " mm, score " + S
                            + (relaxSeg ? " · CHECK: touches model lines" : relaxLead ? " · CHECK: leader crosses text" : crossLeads ? " · leader crosses a leader" : "");
                        best = new { kind = "roomtag", elementId = r.Id.IntegerValue, typeName = tn, right = Math.Round(cr), up = Math.Round(cu), leader = true, endRight = Math.Round(A[0]), endUp = Math.Round(A[1]), path = path, score = S, note = label + " · outside " + how };
                        if (S == S0) break; // nothing later can beat it
                    }
                }
                if (best == null)
                {
                    // RT6 last resort: inside at the anchor, flagged
                    nForced++;
                    var bx = new Box(A[0] - w / 2, A[1] - h / 2, A[0] + w / 2, A[1] + h / 2).Grow(c);
                    placed.Add(bx);
                    items.Add(new { kind = "roomtag", elementId = r.Id.IntegerValue, typeName = tn, right = Math.Round(A[0]), up = Math.Round(A[1]), leader = false, score = 10, note = label + " · inside FORCED · CHECK" });
                    scores.Add(10);
                    problems.Add(label + ": no clear place, put at the room centre (check)");
                    continue;
                }
                if (how.Contains("CHECK")) problems.Add(label + ": " + how);
                placed.Add(bestBox); placedLeads.AddRange(bestLead); nOut++; items.Add(best); scores.Add(bestScore);
            }

            string outPath = (string)args["outPath"];
            if (!string.IsNullOrEmpty(outPath))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath));
                System.IO.File.WriteAllText(outPath, JObject.FromObject(new { ViewId = vid, View = v.Name, Problems = problems, Items = items }).ToString());
            }
            bool toFile = !string.IsNullOrEmpty(outPath);
            outViews.Add(new
            {
                ViewId = vid, View = v.Name, Rooms = rooms.Count, Tagged = rooms.Count(r => tagged.ContainsKey(r.Id.IntegerValue)),
                Untagged = todo.Count, Inside = nIn, Outside = nOut, Forced = nForced, ScoreAvg = scores.Count == 0 ? (double?)null : Math.Round(scores.Average(), 2), NotPlacedOrEnclosed = notPlaced,
                TagSize = sizeSrc.Select(kv => kv.Key + ": " + (sizeCache.ContainsKey(kv.Key) && sizeCache[kv.Key] != null ? string.Join(" x ", sizeCache[kv.Key]) : sizeArg != null ? string.Join(" x ", sizeArg) : "?") + " mm (" + kv.Value + ")").ToList(),
                Problems = toFile ? null : problems, Items = toFile ? null : items, OutPath = toFile ? outPath : null
            });
        }
        return new { Views = outViews };
    }
}
