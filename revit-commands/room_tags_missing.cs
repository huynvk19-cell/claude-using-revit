/* mcp-tool
{
  "description": "Read-only, plans / ceiling plans: rooms of the view's level with no room tag + tag body centre per rules RT1-RT4 (inside, else outside + leader); annot_place items.",
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
        "description": "tag body width, height on paper mm (profile); default: a placed tag's body"
      },
      "clearMm": {
        "type": "number",
        "description": "paper mm kept clear around the tag, default 1"
      },
      "maxShiftMm": {
        "type": "number",
        "description": "paper mm the body may move off the diagonal point inside, default 4"
      },
      "maxLeaderMm": {
        "type": "number",
        "description": "paper mm the body may sit beyond the room, default 30"
      },
      "ignore": {
        "type": "array",
        "items": {
          "type": "string"
        },
        "description": "obstacle kinds to skip: wall, edge, boundary, dim, or a category name"
      },
      "explain": {
        "type": "boolean",
        "description": "per problem room: what blocked the tested positions"
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
// Read-only. For each plan / ceiling plan view: the rooms of the VIEW'S LEVEL whose location point is in the crop and
//    that have no room tag in the view, its primary view or any sibling dependent view (annotations of dependent
//    views belong to the primary, so a room tagged in the overlap strip of a sibling counts as tagged). For each one a
//    tag BODY centre per drafting-annotation.md §2 rules RT1-RT4:
//    - anchor = intersection of the two diagonals of the room's rectangle in the view frame (its centre); when that
//      point is not in the room (L-shaped rooms), the nearest point inside the room;
//    - inside: the body box (tagSizeMm x view scale, + clearMm) centred on the anchor, or moved at most maxShiftMm,
//      fully in the room, crossing no wall, no floor edge (floor plans) / ceiling edge (ceiling plans), no room
//      boundary, no dim line, overlapping no tag / text / spot / generic annotation seen in the view (whoever owns it)
//      and no other body placed in this run -> no leader;
//    - otherwise outside: the body straight above / right / below / left of the anchor (shortest leader first), box
//      clear of the same obstacles, inside the crop and not in another room's anchor; leader end = anchor, so the
//      leader (it starts at the body centre) is one vertical / horizontal segment on the paper.
//    Tag type = typeName, else the most common type among the 3 nearest tagged rooms of the view (NeighbourType).
//    Tag size: tagSizeMm, else the box of a placed tag of that type WITHOUT leader (its body).
//    Items are ready for annot_place {mode:'preview'|'apply', viewId, items} (kind 'roomtag': right / up = body centre,
//    leader, endRight / endUp). ignore: skip obstacle kinds (e.g. a category name that blocks every position).
//    explain: for each problem room, counts of what blocked the positions tried (kind or category name).
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

    class Seg { public double R1, U1, R2, U2; public string Kind; }
    class Box
    {
        public double R0, U0, R1, U1; public string Kind;
        public Box(double r0, double u0, double r1, double u1, string kind = null) { R0 = Math.Min(r0, r1); U0 = Math.Min(u0, u1); R1 = Math.Max(r0, r1); U1 = Math.Max(u0, u1); Kind = kind; }
        public bool Hits(Box b) { return R0 < b.R1 && b.R0 < R1 && U0 < b.U1 && b.U0 < U1; }
        public Box Grow(double d) { return new Box(R0 - d, U0 - d, R1 + d, U1 + d, Kind); }
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

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var viewIds = ((JArray)args["viewIds"]).Select(t => (int)t).ToList();
        HashSet<int> only = args["roomIds"] is JArray ra && ra.Count > 0 ? new HashSet<int>(ra.Select(t => (int)t)) : null;
        string typeArg = ((string)args["typeName"] ?? "").Trim();
        double clearMm = (double?)args["clearMm"] ?? 1, maxShiftMm = (double?)args["maxShiftMm"] ?? 4, maxLeaderMm = (double?)args["maxLeaderMm"] ?? 30;
        double[] sizeArg = args["tagSizeMm"] is JArray sa && sa.Count == 2 ? new[] { (double)sa[0], (double)sa[1] } : null;
        var ignore = new HashSet<string>(((args["ignore"] as JArray) ?? new JArray()).Select(t => ((string)t).ToLower()));
        bool explain = (bool?)args["explain"] ?? false;

        var allTags = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().OfType<RoomTag>().ToList();
        Func<Element, string> typeOf = e => doc.GetElement(e.GetTypeId())?.Name ?? "";
        var sizeCache = new Dictionary<string, double[]>();
        var sizeSrc = new Dictionary<string, string>();
        // paper size of a tag body: argument, else the box of a placed tag of that type without leader, in its own view
        Func<string, double[]> sizeOf = tn =>
        {
            if (sizeArg != null) { sizeSrc[tn] = "tagSizeMm"; return sizeArg; }
            if (sizeCache.ContainsKey(tn)) return sizeCache[tn];
            double[] res = null;
            foreach (var t in allTags.Where(t => !t.HasLeader && typeOf(t) == tn))
            {
                var ov = doc.GetElement(t.OwnerViewId) as View;
                var bb = ov == null ? null : t.get_BoundingBox(ov);
                if (bb == null || ov.Scale <= 0) continue;
                double w = Math.Abs((bb.Max - bb.Min).DotProduct(ov.RightDirection)) * MM / ov.Scale;
                double h = Math.Abs((bb.Max - bb.Min).DotProduct(ov.UpDirection)) * MM / ov.Scale;
                if (w > 0.5 && h > 0.5) { res = new[] { Math.Round(w, 1), Math.Round(h, 1) }; break; }
            }
            sizeCache[tn] = res; sizeSrc[tn] = res == null ? "none" : "body box of a placed tag";
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

            // room tags of the view, its primary view and the sibling dependents (all owned by the primary)
            var owners = new HashSet<int> { v.Id.IntegerValue };
            var primary = v.GetPrimaryViewId();
            if (primary != ElementId.InvalidElementId) owners.Add(primary.IntegerValue);
            var seen = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().OfType<RoomTag>().ToList();
            var tagged = new Dictionary<int, RoomTag>();
            foreach (var t in seen.Concat(allTags.Where(t => owners.Contains(t.OwnerViewId.IntegerValue))))
                try { var rm = t.Room; if (rm != null && !tagged.ContainsKey(rm.Id.IntegerValue)) tagged[rm.Id.IntegerValue] = t; } catch { }

            // rooms of the view's level only (rooms of other levels cannot be tagged here: "point outside spatial element")
            var lvl = v.GenLevel;
            IEnumerable<Room> pool = lvl != null
                ? new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.LevelId == lvl.Id)
                : new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>();
            int notPlaced = 0;
            var rooms = new List<Room>();
            foreach (var r in pool)
            {
                if (r.Area <= 0 || !(r.Location is LocationPoint lp)) { notPlaced++; continue; }
                var lpF = F(lp.Point);
                if (crop != null && (lpF[0] < crop.R0 || lpF[0] > crop.R1 || lpF[1] < crop.U0 || lpF[1] > crop.U1)) continue;
                rooms.Add(r);
            }

            // obstacles: wall outlines, floor (floor plan) or ceiling (ceiling plan) edges, dim lines, annotation boxes
            var segs = new List<Seg>();
            Action<GeometryElement, string> addGeo = null;
            addGeo = (ge, kind) =>
            {
                if (ge == null) return;
                foreach (var g in ge)
                {
                    if (g is GeometryInstance gi) { addGeo(gi.GetInstanceGeometry(), kind); continue; }
                    if (!(g is Solid so) || so.Edges.Size == 0) continue;
                    foreach (Edge ed in so.Edges)
                    {
                        var pts = ed.Tessellate();
                        for (int i = 1; i < pts.Count; i++)
                        {
                            var a = F(pts[i - 1]); var b = F(pts[i]);
                            if (Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) < 1) continue; // vertical edge seen end-on
                            segs.Add(new Seg { R1 = a[0], U1 = a[1], R2 = b[0], U2 = b[1], Kind = kind });
                        }
                    }
                }
            };
            var cats = new List<KeyValuePair<BuiltInCategory, string>>();
            if (!ignore.Contains("wall")) cats.Add(new KeyValuePair<BuiltInCategory, string>(BuiltInCategory.OST_Walls, "wall"));
            if (!ignore.Contains("edge")) cats.Add(new KeyValuePair<BuiltInCategory, string>(v.ViewType == ViewType.CeilingPlan ? BuiltInCategory.OST_Ceilings : BuiltInCategory.OST_Floors, v.ViewType == ViewType.CeilingPlan ? "ceiling edge" : "floor edge"));
            var gopt = new Options { View = v };
            foreach (var kv in cats)
                foreach (var e in new FilteredElementCollector(doc, v.Id).OfCategory(kv.Key).WhereElementIsNotElementType())
                    try { addGeo(e.get_Geometry(gopt), kv.Value); } catch { }

            // annotations seen in the view, whoever owns them (dependent views: the primary)
            var annBoxes = new List<Box>();
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
            {
                if (!e.ViewSpecific || e.Category == null) continue;
                string cat = e.Category.Name;
                if (ignore.Contains(cat.ToLower())) continue;
                try
                {
                    if (e is Dimension d && !(e is SpotDimension))
                    {
                        if (ignore.Contains("dim") || !(d.Curve is Line dl)) continue;
                        var dir = dl.Direction;
                        var ends = new List<XYZ>();
                        if (d.Segments.Size > 0) foreach (DimensionSegment ds in d.Segments) { double hv = (ds.Value ?? 0) / 2; ends.Add(ds.Origin - dir * hv); ends.Add(ds.Origin + dir * hv); }
                        else { double hv = (d.Value ?? 0) / 2; ends.Add(d.Origin - dir * hv); ends.Add(d.Origin + dir * hv); }
                        var fs = ends.Select(F).OrderBy(f => f[0] * dir.DotProduct(R) + f[1] * dir.DotProduct(U)).ToList();
                        segs.Add(new Seg { R1 = fs.First()[0], U1 = fs.First()[1], R2 = fs.Last()[0], U2 = fs.Last()[1], Kind = "dim" });
                        continue;
                    }
                    if (e is RoomTag rt && rt.HasLeader)
                    {
                        // a box with a leader spans the leader too: use the body size around the head
                        var sz = sizeOf(typeOf(rt)) ?? sizeArg;
                        if (sz == null) continue;
                        var hp = F(rt.TagHeadPosition);
                        annBoxes.Add(new Box(hp[0] - sz[0] * sc / 2, hp[1] - sz[1] * sc / 2, hp[0] + sz[0] * sc / 2, hp[1] + sz[1] * sc / 2, cat));
                        continue;
                    }
                    bool ann = e is IndependentTag || e is TextNote || e is SpatialElementTag || e is SpotDimension
                        || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericAnnotation;
                    if (!ann) continue;
                    var bb = e.get_BoundingBox(v);
                    if (bb == null) continue;
                    var a = F(bb.Min); var b = F(bb.Max);
                    annBoxes.Add(new Box(a[0], a[1], b[0], b[1], cat));
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
                            for (int i = 1; i < pts.Count; i++) { var a = F(pts[i - 1]); var b = F(pts[i]); bsegs.Add(new Seg { R1 = a[0], U1 = a[1], R2 = b[0], U2 = b[1], Kind = "boundary" }); }
                        }
                }
                catch { }
                if (bsegs.Count == 0) continue;
                var rect = new Box(bsegs.Min(s => Math.Min(s.R1, s.R2)), bsegs.Min(s => Math.Min(s.U1, s.U2)), bsegs.Max(s => Math.Max(s.R1, s.R2)), bsegs.Max(s => Math.Max(s.U1, s.U2)));
                double z = (r.Level != null ? r.Level.ProjectElevation : 0) + r.BaseOffset + Math.Min(1000 / MM, Math.Max(r.UnboundedHeight, 0.1) / 2);
                info[r.Id.IntegerValue] = new object[] { rect, new[] { (rect.R0 + rect.R1) / 2, (rect.U0 + rect.U1) / 2 }, ignore.Contains("boundary") ? new List<Seg>() : bsegs, z };
            }
            Func<Room, double, double, bool> inRoom = (r, rr, uu) => { try { return r.IsPointInRoom(P(rr, uu, (double)info[r.Id.IntegerValue][3])); } catch { return false; } };
            // the room of the view containing a point (outside bodies must not sit on another room's anchor)
            var anchorsTaken = new List<double[]>();

            int taggedHere = rooms.Count(r => tagged.ContainsKey(r.Id.IntegerValue));
            var todo = rooms.Where(r => !tagged.ContainsKey(r.Id.IntegerValue) && (only == null || only.Contains(r.Id.IntegerValue)) && info.ContainsKey(r.Id.IntegerValue)).ToList();
            var placed = new List<Box>();
            var items = new List<object>();
            var problems = new List<object>();
            var pending = new List<object[]>(); // room, type, size, anchor, local segs, local ann boxes, rect, label
            int nIn = 0, nOut = 0;

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
                anchorsTaken.Add(A);
                double reach = Math.Max(rect.R1 - rect.R0, rect.U1 - rect.U0) + (maxLeaderMm + sz[0] + sz[1]) * sc;
                var zone = new Box(A[0] - reach, A[1] - reach, A[0] + reach, A[1] + reach);
                var lSegs = segs.Where(s => SegHits(s, zone)).Concat((List<Seg>)inf[2]).ToList();
                var lAnn = annBoxes.Where(b => b.Hits(zone)).ToList();
                pending.Add(new object[] { r, tn, sz, A, lSegs, lAnn, rect, label });
            }

            // first blocker of a box (null = clear); counts kept for explain
            Func<Box, List<Seg>, List<Box>, string> blocker = (bx, ls, la) =>
            {
                var s = ls.FirstOrDefault(x => SegHits(x, bx)); if (s != null) return s.Kind;
                var b = la.FirstOrDefault(x => x.Hits(bx)); if (b != null) return b.Kind ?? "annotation";
                if (placed.Any(x => x.Hits(bx))) return "tag placed in this run";
                if (anchorsTaken.Any(a => a[0] > bx.R0 && a[0] < bx.R1 && a[1] > bx.U0 && a[1] < bx.U1)) return "another room's centre";
                return null;
            };
            var why = new Dictionary<int, Dictionary<string, int>>();
            Action<int, string> note = (id, k) => { if (!why.ContainsKey(id)) why[id] = new Dictionary<string, int>(); why[id][k] = why[id].ContainsKey(k) ? why[id][k] + 1 : 1; };

            // pass 1: inside (RT4), pass 2: outside with leader (RT4)
            var left = new List<object[]>();
            foreach (var pd in pending)
            {
                var r = (Room)pd[0]; var sz = (double[])pd[2]; var A = (double[])pd[3]; int rid = r.Id.IntegerValue;
                double w = sz[0] * sc, h = sz[1] * sc, c = clearMm * sc, s = Math.Max(h / 2, 1), lim = maxShiftMm * sc;
                var offs = new List<double[]>();
                for (double dx = -lim; dx <= lim + 1e-6; dx += s) for (double dy = -lim; dy <= lim + 1e-6; dy += s) offs.Add(new[] { dx, dy });
                bool ok = false;
                foreach (var o in offs.OrderBy(o => o[0] * o[0] + o[1] * o[1]))
                {
                    double cr = A[0] + o[0], cu = A[1] + o[1];
                    var bx = new Box(cr - w / 2, cu - h / 2, cr + w / 2, cu + h / 2).Grow(c);
                    if (crop != null && (bx.R0 < crop.R0 || bx.R1 > crop.R1 || bx.U0 < crop.U0 || bx.U1 > crop.U1)) { note(rid, "crop"); continue; }
                    var pts = new[] { new[] { bx.R0, bx.U0 }, new[] { bx.R1, bx.U0 }, new[] { bx.R0, bx.U1 }, new[] { bx.R1, bx.U1 }, new[] { cr, bx.U0 }, new[] { cr, bx.U1 }, new[] { bx.R0, cu }, new[] { bx.R1, cu }, new[] { cr, cu } };
                    if (!pts.All(p => inRoom(r, p[0], p[1]))) { note(rid, "inside: room too small"); continue; }
                    // the room's own centre is not an obstacle for itself
                    anchorsTaken.Remove(A);
                    var bk = blocker(bx, (List<Seg>)pd[4], (List<Box>)pd[5]);
                    anchorsTaken.Add(A);
                    if (bk != null) { note(rid, "inside: " + bk); continue; }
                    placed.Add(bx); nIn++; ok = true;
                    items.Add(new { kind = "roomtag", elementId = rid, typeName = (string)pd[1], right = Math.Round(cr), up = Math.Round(cu), leader = false, note = (string)pd[7] + " · inside" });
                    break;
                }
                if (!ok) left.Add(pd);
            }
            foreach (var pd in left)
            {
                var r = (Room)pd[0]; var sz = (double[])pd[2]; var A = (double[])pd[3]; var rect = (Box)pd[6]; int rid = r.Id.IntegerValue;
                double w = sz[0] * sc, h = sz[1] * sc, c = clearMm * sc, s = Math.Max(h / 2, 1);
                // direction: dr, du, distance from the anchor to the room rectangle edge, half box size along it
                var dirs = new[] {
                    new { N = "up", Dr = 0.0, Du = 1.0, Edge = rect.U1 - A[1], Half = h / 2 },
                    new { N = "right", Dr = 1.0, Du = 0.0, Edge = rect.R1 - A[0], Half = w / 2 },
                    new { N = "down", Dr = 0.0, Du = -1.0, Edge = A[1] - rect.U0, Half = h / 2 },
                    new { N = "left", Dr = -1.0, Du = 0.0, Edge = A[0] - rect.R0, Half = w / 2 } };
                object best = null; Box bestBox = null; double bestLen = double.MaxValue;
                anchorsTaken.Remove(A);
                foreach (var d in dirs)
                {
                    for (double dist = d.Half + c; dist - d.Half <= d.Edge + maxLeaderMm * sc; dist += s)
                    {
                        double cr = A[0] + d.Dr * dist, cu = A[1] + d.Du * dist;
                        var bx = new Box(cr - w / 2, cu - h / 2, cr + w / 2, cu + h / 2).Grow(c);
                        if (crop != null && (bx.R0 < crop.R0 || bx.R1 > crop.R1 || bx.U0 < crop.U0 || bx.U1 > crop.U1)) { note(rid, "outside " + d.N + ": crop"); break; }
                        if (inRoom(r, cr, cu)) continue; // body must be outside its room here
                        var bk = blocker(bx, (List<Seg>)pd[4], (List<Box>)pd[5]);
                        if (bk != null) { note(rid, "outside: " + bk); continue; }
                        double len = dist - d.Half;
                        if (len < bestLen)
                        {
                            bestLen = len; bestBox = bx;
                            best = new { kind = "roomtag", elementId = rid, typeName = (string)pd[1], right = Math.Round(cr), up = Math.Round(cu), leader = true, endRight = Math.Round(A[0]), endUp = Math.Round(A[1]), note = (string)pd[7] + " · outside " + d.N + ", leader " + Math.Round(len / sc, 1) + " mm" };
                        }
                        break;
                    }
                }
                anchorsTaken.Add(A);
                if (best == null)
                {
                    string msg = (string)pd[7] + ": no clear place inside or within " + maxLeaderMm + " mm outside";
                    if (explain && why.ContainsKey(rid)) msg += " (" + string.Join(", ", why[rid].OrderByDescending(kv => kv.Value).Take(4).Select(kv => kv.Key + " " + kv.Value)) + ")";
                    problems.Add(msg); continue;
                }
                placed.Add(bestBox); nOut++; items.Add(best);
            }

            outViews.Add(new
            {
                ViewId = vid, View = v.Name, Level = lvl?.Name, Rooms = rooms.Count, Tagged = taggedHere,
                Untagged = todo.Count, Inside = nIn, Outside = nOut, NotPlacedOrEnclosed = notPlaced,
                TagSize = sizeSrc.Select(kv => kv.Key + ": " + (sizeCache.ContainsKey(kv.Key) && sizeCache[kv.Key] != null ? string.Join(" x ", sizeCache[kv.Key]) : sizeArg != null ? string.Join(" x ", sizeArg) : "?") + " mm (" + kv.Value + ")").ToList(),
                Problems = problems, Items = items
            });
        }
        return new { Views = outViews };
    }
}
