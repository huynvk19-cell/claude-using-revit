/* mcp-tool
{
  "description": "Read-only: score every room tag of plan / ceiling plan views (rules TR1-TR8) and the card score per view; worst tags listed.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "tagSizeMm": { "type": "array", "items": { "type": "number" }, "description": "tag body width, height on paper mm (profile); default: measured box" },
      "maxScore": { "type": "number", "description": "score of a tag in its room with no leader, default 10" },
      "acceptAbove": { "type": "number", "description": "card accepted when its score is above this, default 7" },
      "zeroAtMm": { "type": "number", "description": "leader length on paper mm where TR1 reaches 0, default 50" },
      "alignMm": { "type": "number", "description": "TR2: other tag within this paper mm counts for alignment, default 80" },
      "worst": { "type": "number", "description": "how many lowest-score tags to list per view, default 10" },
      "outPath": { "type": "string", "description": "write every tag's score to this JSON file" }
    },
    "required": ["viewIds"]
  },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Rules TR1-TR8 (drafting-annotation.md §2, Huy 2026-10-22). Score of one room tag:
//    TR1 base = maxScore x (1 - L / zeroAtMm), never below 0. L = leader length on paper from the body edge to the dot,
//        plus the distance from the dot to its room when the dot is not in it; a tag with no leader: L = 0 when its
//        body centre is in its room, else the distance from the body centre to the room.
//    TR2 the body lines up with another room tag within alignMm (same body centre height = row; same left edge or
//        same centre line = column; 0.5 mm on paper)                                   -> +2
//    TR3 the body sits on a wall or a floor edge (ceiling plan: ceiling edge)          -> 0
//    TR4 the leader lies on another room tag leader                                     -> 0
//    TR5 the leader runs along a wall or a floor / ceiling edge                         -> 0
//    TR6 the leader runs through the end point (dot) of another room tag leader        -> 0
//    TR7 the leader runs right through other rooms (not its own, not the room under the body) -> -3 each
//    clamped 0..maxScore; a TR3-TR6 tag stays 0. View card score = average of its tags;
//    TR8 a room seen in the view with no room tag in it -> card score 0. Accepted when the card score > acceptAbove.
// Rooms seen = rooms the view shows + rooms of the view's level whose point is in the crop (area > 0); their
// finish boundary is the room polygon (even-odd, holes count). Hard lines = wall and floor (ceiling) solid edges
// in the view; walls / floors of linked models are not seen.
// Tag body: the tag box with its leader switched off (rolled back, the head shift undone); tagSizeMm, when given,
// replaces it, centred on the same point. Leader = body centre -> elbow -> end; only the part outside the body counts.
// Coordinates: view frame mm (Right / Up from View.Origin); tolerances are paper mm x view scale.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomTagsScore
{
    const double MM = 304.8;

    class Seg
    {
        public double R1, U1, R2, U2;
        public double Len { get { return Math.Sqrt((R2 - R1) * (R2 - R1) + (U2 - U1) * (U2 - U1)); } }
    }
    class Box
    {
        public double R0, U0, R1, U1;
        public Box(double r0, double u0, double r1, double u1) { R0 = Math.Min(r0, r1); U0 = Math.Min(u0, u1); R1 = Math.Max(r0, r1); U1 = Math.Max(u0, u1); }
        public bool Hits(Box b) { return R0 < b.R1 && b.R0 < R1 && U0 < b.U1 && b.U0 < U1; }
        public bool Has(double r, double u) { return r >= R0 && r <= R1 && u >= U0 && u <= U1; }
        public Box Grow(double d) { return new Box(R0 - d, U0 - d, R1 + d, U1 + d); }
        public double CR { get { return (R0 + R1) / 2; } }
        public double CU { get { return (U0 + U1) / 2; } }
    }
    class Rm { public int Id; public string Num; public double Area; public List<Seg> B = new List<Seg>(); public Box Rect; }
    class Tg { public int Id, RoomId; public string Num, Type; public Box Body; public List<Seg> Lead = new List<Seg>(); public double[] Dot; public double LeadLen; }

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

    static double PtSeg(double pr, double pu, Seg s)
    {
        double dx = s.R2 - s.R1, dy = s.U2 - s.U1, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((pr - s.R1) * dx + (pu - s.U1) * dy) / l2));
        return Math.Sqrt(Math.Pow(pr - (s.R1 + t * dx), 2) + Math.Pow(pu - (s.U1 + t * dy), 2));
    }

    // two segments on the same line, overlapping by more than tol (TR4, TR5)
    static bool Along(Seg a, Seg b, double tol)
    {
        if (a.Len < b.Len) { var x = a; a = b; b = x; }
        double la = a.Len; if (la < 1e-6 || b.Len < tol) return false;
        double ux = (a.R2 - a.R1) / la, uy = (a.U2 - a.U1) / la;
        if (Math.Abs((b.R1 - a.R1) * uy - (b.U1 - a.U1) * ux) > tol || Math.Abs((b.R2 - a.R1) * uy - (b.U2 - a.U1) * ux) > tol) return false;
        double t1 = (b.R1 - a.R1) * ux + (b.U1 - a.U1) * uy, t2 = (b.R2 - a.R1) * ux + (b.U2 - a.U1) * uy;
        return Math.Min(la, Math.Max(t1, t2)) - Math.Max(0, Math.Min(t1, t2)) > tol;
    }

    static bool In(Rm r, double x, double y)
    {
        if (!r.Rect.Has(x, y)) return false;
        bool c = false;
        foreach (var s in r.B)
            if ((s.U1 > y) != (s.U2 > y) && x < s.R1 + (y - s.U1) * (s.R2 - s.R1) / (s.U2 - s.U1)) c = !c;
        return c;
    }
    static double DistTo(Rm r, double x, double y) { return In(r, x, y) ? 0 : r.B.Min(s => PtSeg(x, y, s)); }

    // ---- shared with room_tags_missing (keep the two copies the same) ----
    static List<Rm> RoomsSeen(Document doc, ViewPlan v, Box crop, Func<XYZ, double[]> F)
    {
        var cand = new Dictionary<int, Room>();
        try { foreach (var r in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>()) cand[r.Id.IntegerValue] = r; } catch { }
        if (v.GenLevel != null)
            foreach (var r in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.LevelId == v.GenLevel.Id))
                cand[r.Id.IntegerValue] = r;
        var bopt = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
        var res = new List<Rm>();
        foreach (var r in cand.Values)
        {
            if (r.Area <= 0 || !(r.Location is LocationPoint lp)) continue;
            var p = F(lp.Point);
            if (crop != null && !crop.Has(p[0], p[1])) continue;
            var rm = new Rm { Id = r.Id.IntegerValue, Num = r.Number, Area = r.Area };
            try
            {
                foreach (var loop in r.GetBoundarySegments(bopt))
                    foreach (var bs in loop)
                    {
                        var pts = bs.GetCurve().Tessellate();
                        for (int i = 1; i < pts.Count; i++) { var a = F(pts[i - 1]); var b = F(pts[i]); rm.B.Add(new Seg { R1 = a[0], U1 = a[1], R2 = b[0], U2 = b[1] }); }
                    }
            }
            catch { }
            if (rm.B.Count == 0) continue;
            rm.Rect = new Box(rm.B.Min(s => Math.Min(s.R1, s.R2)), rm.B.Min(s => Math.Min(s.U1, s.U2)), rm.B.Max(s => Math.Max(s.R1, s.R2)), rm.B.Max(s => Math.Max(s.U1, s.U2)));
            res.Add(rm);
        }
        return res;
    }

    // walls + floors (floor plan) or ceilings (ceiling plan): solid edges seen in the view
    static List<Seg> HardSegs(Document doc, ViewPlan v, Func<XYZ, double[]> F)
    {
        var segs = new List<Seg>();
        Action<GeometryElement> add = null;
        add = ge =>
        {
            if (ge == null) return;
            foreach (var g in ge)
            {
                if (g is GeometryInstance gi) { add(gi.GetInstanceGeometry()); continue; }
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
        var gopt = new Options { View = v };
        foreach (var bic in new[] { BuiltInCategory.OST_Walls, v.ViewType == ViewType.CeilingPlan ? BuiltInCategory.OST_Ceilings : BuiltInCategory.OST_Floors })
            foreach (var e in new FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType())
                try { add(e.get_Geometry(gopt)); } catch { }
        return segs;
    }

    // room tags of the view: body box measured with the leader off (rolled back), visible leader, dot
    static List<Tg> MeasureTags(Document doc, ViewPlan v, Func<XYZ, double[]> F, double[] size)
    {
        double sc = v.Scale;
        var tags = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().OfType<RoomTag>().ToList();
        var res = new List<Tg>();
        var raw = new Dictionary<int, object[]>(); // head, end, elbow
        foreach (var t in tags)
        {
            double[] le = null, el = null;
            try { if (t.HasLeader) { le = F(t.LeaderEnd); if (t.HasElbow) el = F(t.LeaderElbow); } } catch { }
            raw[t.Id.IntegerValue] = new object[] { F(t.TagHeadPosition), le, el };
        }
        var boxes = new Dictionary<int, Box>();
        using (var tr = new Transaction(doc, "measure room tag bodies"))
        {
            tr.Start();
            foreach (var t in tags) try { if (t.HasLeader) t.HasLeader = false; } catch { }
            doc.Regenerate();
            foreach (var t in tags)
            {
                var bb = t.get_BoundingBox(v); if (bb == null) continue;
                // leader off: Revit draws the body at the tag point (Location = old leader end) but TagHeadPosition still reads
                // the old head, so measure the shift from the Location point
                var h0 = (double[])raw[t.Id.IntegerValue][0]; var lp = (t.Location as LocationPoint)?.Point; var h1 = raw[t.Id.IntegerValue][1] != null && lp != null ? F(lp) : F(t.TagHeadPosition);
                double dr = h0[0] - h1[0], du = h0[1] - h1[1];
                var a = F(bb.Min); var b = F(bb.Max);
                boxes[t.Id.IntegerValue] = new Box(a[0] + dr, a[1] + du, b[0] + dr, b[1] + du);
            }
            tr.RollBack();
        }
        foreach (var t in tags)
        {
            Room rm = null; try { rm = t.Room; } catch { }
            var o = raw[t.Id.IntegerValue]; var hd = (double[])o[0];
            Box body = boxes.ContainsKey(t.Id.IntegerValue) ? boxes[t.Id.IntegerValue] : null;
            if (size != null)
            {
                double cr = body != null ? body.CR : hd[0], cu = body != null ? body.CU : hd[1];
                body = new Box(cr - size[0] * sc / 2, cu - size[1] * sc / 2, cr + size[0] * sc / 2, cu + size[1] * sc / 2);
            }
            var g = new Tg { Id = t.Id.IntegerValue, RoomId = rm != null ? rm.Id.IntegerValue : -1, Num = rm != null ? rm.Number : "?", Type = doc.GetElement(t.GetTypeId())?.Name ?? "", Body = body };
            var le = (double[])o[1];
            if (le != null)
            {
                var c = body != null ? new[] { body.CR, body.CU } : hd;
                var pts = new List<double[]> { c };
                var el = (double[])o[2];
                if (el != null)
                {
                    double cross = (el[0] - c[0]) * (le[1] - c[1]) - (el[1] - c[1]) * (le[0] - c[0]);
                    double l02 = Math.Sqrt(Math.Pow(le[0] - c[0], 2) + Math.Pow(le[1] - c[1], 2));
                    if (l02 >= 1 && Math.Abs(cross) / l02 >= 0.5 * sc) pts.Add(el); // an elbow on the straight line is dropped
                }
                pts.Add(le);
                g.Dot = le;
                g.Lead = Visible(pts, body);
                g.LeadLen = g.Lead.Sum(s => s.Len);
            }
            res.Add(g);
        }
        return res;
    }

    // the part of the polyline outside the body box (the leader starts at the body centre)
    static List<Seg> Visible(List<double[]> pts, Box body)
    {
        var res = new List<Seg>();
        bool inside = body != null;
        for (int i = 1; i < pts.Count; i++)
        {
            double r1 = pts[i - 1][0], u1 = pts[i - 1][1], r2 = pts[i][0], u2 = pts[i][1];
            if (inside)
            {
                if (body.Has(r2, u2)) continue;
                double dr = r2 - r1, du = u2 - u1, t = 1;
                if (dr > 1e-9) t = Math.Min(t, (body.R1 - r1) / dr); else if (dr < -1e-9) t = Math.Min(t, (body.R0 - r1) / dr);
                if (du > 1e-9) t = Math.Min(t, (body.U1 - u1) / du); else if (du < -1e-9) t = Math.Min(t, (body.U0 - u1) / du);
                t = Math.Max(0, t); r1 += dr * t; u1 += du * t; inside = false;
            }
            if (Math.Abs(r2 - r1) + Math.Abs(u2 - u1) > 1e-6) res.Add(new Seg { R1 = r1, U1 = u1, R2 = r2, U2 = u2 });
        }
        return res;
    }

    class Prm { public double Max, ZeroAt, Tol, Near, Sc; }

    // TR1-TR7 for one tag against the other tags; reasons in why; returns the score
    static double Score(Tg t, Rm own, IEnumerable<Tg> others, List<Seg> hard, List<Rm> rooms, Prm p, List<string> why)
    {
        var oth = others.Where(o => o != t).ToList();
        if (t.Body != null && hard.Any(s => SegHits(s, t.Body.Grow(-0.1 * p.Sc)))) { why.Add("TR3 body on a wall / floor or ceiling edge"); return 0; }
        if (t.Lead.Any(a => oth.Any(o => o.Lead.Any(b => Along(a, b, p.Tol))))) { why.Add("TR4 leader on another leader"); return 0; }
        if (t.Lead.Any(a => hard.Any(b => Along(a, b, p.Tol)))) { why.Add("TR5 leader along a wall / floor or ceiling edge"); return 0; }
        var hit = oth.FirstOrDefault(o => o.Dot != null && t.Lead.Any(a => PtSeg(o.Dot[0], o.Dot[1], a) < p.Tol));
        if (hit != null) { why.Add("TR6 leader through the leader end of " + hit.Num); return 0; }
        double L = t.LeadLen;
        if (own != null)
        {
            if (t.Dot != null) L += DistTo(own, t.Dot[0], t.Dot[1]);
            else if (t.Body != null) L += DistTo(own, t.Body.CR, t.Body.CU);
        }
        double s = Math.Max(0, p.Max * (1 - L / p.ZeroAt));
        if (L > 0) why.Add("TR1 leader " + Math.Round(L / p.Sc, 1) + " mm -> " + Math.Round(s, 2));
        // the room under the body (where the leader starts) is not crossed, only rooms the leader runs right through (Huy 2026-10-22)
        var crossed = new HashSet<int>();
        if (t.Body != null) foreach (var r in rooms) if (r.Id != t.RoomId && In(r, t.Body.CR, t.Body.CU)) crossed.Add(r.Id);
        if (t.Lead.Count > 0) foreach (var r in rooms) if (r.Id != t.RoomId && In(r, t.Lead[0].R1, t.Lead[0].U1)) crossed.Add(r.Id);
        var start = new HashSet<int>(crossed);
        foreach (var lg in t.Lead)
        {
            int n = Math.Max(1, (int)Math.Ceiling(lg.Len / (0.5 * p.Sc)));
            var lb = new Box(lg.R1, lg.U1, lg.R2, lg.U2);
            var near = rooms.Where(r => r.Id != t.RoomId && r.Rect.R0 <= lb.R1 && lb.R0 <= r.Rect.R1 && r.Rect.U0 <= lb.U1 && lb.U0 <= r.Rect.U1).ToList();
            for (int k = 0; k <= n && near.Count > 0; k++)
            {
                double pr = lg.R1 + (lg.R2 - lg.R1) * k / n, pu = lg.U1 + (lg.U2 - lg.U1) * k / n;
                foreach (var r in near) if (!crossed.Contains(r.Id) && In(r, pr, pu)) crossed.Add(r.Id);
            }
        }
        crossed.ExceptWith(start);
        if (crossed.Count > 0) { s -= 3 * crossed.Count; why.Add("TR7 leader through " + crossed.Count + " room(s) " + string.Join(",", rooms.Where(r => crossed.Contains(r.Id)).Select(r => r.Num))); }
        if (t.Body != null && oth.Any(o => o.Body != null && Math.Abs(o.Body.CR - t.Body.CR) < p.Near && Math.Abs(o.Body.CU - t.Body.CU) < p.Near
            && (Math.Abs(o.Body.CU - t.Body.CU) < p.Tol || Math.Abs(o.Body.R0 - t.Body.R0) < p.Tol || Math.Abs(o.Body.CR - t.Body.CR) < p.Tol)))
        { s += 2; why.Add("TR2 aligned +2"); }
        return Math.Max(0, Math.Min(p.Max, s));
    }
    // ---- end shared ----

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        double[] size = args["tagSizeMm"] is JArray sa && sa.Count == 2 ? new[] { (double)sa[0], (double)sa[1] } : null;
        int worstN = (int?)args["worst"] ?? 10;
        double maxS = (double?)args["maxScore"] ?? 10, acceptAbove = (double?)args["acceptAbove"] ?? 7;
        double zeroAt = (double?)args["zeroAtMm"] ?? 50, alignMm = (double?)args["alignMm"] ?? 80;
        var all = new JArray();
        var res = new List<object>();
        foreach (var vid in ((JArray)args["viewIds"]).Select(x => (int)x))
        {
            var v = doc.GetElement(new ElementId(vid)) as ViewPlan;
            if (v == null) { res.Add(new { ViewId = vid, Error = "not a plan view" }); continue; }
            XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection; double sc = v.Scale;
            Func<XYZ, double[]> F = q => new[] { (q - O).DotProduct(R) * MM, (q - O).DotProduct(U) * MM };
            Box crop = null;
            if (v.CropBoxActive)
            {
                var cb = v.CropBox; var tf = cb.Transform;
                var cs = new[] { cb.Min, cb.Max, new XYZ(cb.Min.X, cb.Max.Y, cb.Min.Z), new XYZ(cb.Max.X, cb.Min.Y, cb.Min.Z) }.Select(q => F(tf.OfPoint(q))).ToList();
                crop = new Box(cs.Min(c => c[0]), cs.Min(c => c[1]), cs.Max(c => c[0]), cs.Max(c => c[1]));
            }
            var rooms = RoomsSeen(doc, v, crop, F);
            var hard = HardSegs(doc, v, F);
            var tags = MeasureTags(doc, v, F, size);
            var p = new Prm { Max = maxS, ZeroAt = zeroAt * sc, Tol = 0.5 * sc, Near = alignMm * sc, Sc = sc };
            var tagged = new HashSet<int>(tags.Select(t => t.RoomId));
            var untagged = rooms.Where(r => !tagged.Contains(r.Id)).ToList();

            var scored = new List<object[]>(); // tag, score, why
            foreach (var t in tags)
            {
                var own = rooms.FirstOrDefault(r => r.Id == t.RoomId);
                var zone = (t.Body ?? new Box(0, 0, 0, 0)).Grow(p.Tol * 4);
                foreach (var lg in t.Lead) zone = new Box(Math.Min(zone.R0, Math.Min(lg.R1, lg.R2) - p.Tol * 4), Math.Min(zone.U0, Math.Min(lg.U1, lg.U2) - p.Tol * 4), Math.Max(zone.R1, Math.Max(lg.R1, lg.R2) + p.Tol * 4), Math.Max(zone.U1, Math.Max(lg.U1, lg.U2) + p.Tol * 4));
                var lh = hard.Where(s => SegHits(s, zone)).ToList();
                var why = new List<string>();
                double s = Score(t, own, tags.Where(o => o.Id != t.Id), lh, rooms, p, why);
                scored.Add(new object[] { t, Math.Round(s, 2), string.Join("; ", why) });
                all.Add(new JObject { ["view"] = vid, ["tag"] = t.Id, ["room"] = t.Num, ["score"] = Math.Round(s, 2), ["why"] = string.Join("; ", why) });
            }
            double? avg = scored.Count == 0 ? (double?)null : Math.Round(scored.Average(x => (double)x[1]), 2);
            double? card = untagged.Count > 0 ? 0 : avg;
            Func<string, int> cnt = code => scored.Count(x => ((string)x[2]).Contains(code));
            res.Add(new
            {
                ViewId = vid, View = v.Name, Rooms = rooms.Count, Tags = scored.Count, Untagged = untagged.Count,
                UntaggedRooms = untagged.Take(20).Select(r => r.Num).ToList(),
                ScoreAvg = avg, CardScore = card, Accepted = card.HasValue && card.Value > acceptAbove,
                Count = new { Leader = cnt("TR1"), Aligned = cnt("TR2"), OnWall = cnt("TR3"), OnLeader = cnt("TR4"), AlongWall = cnt("TR5"), ThroughEnd = cnt("TR6"), ThroughRooms = cnt("TR7"), Full = scored.Count(x => (double)x[1] >= maxS) },
                Worst = scored.OrderBy(x => (double)x[1]).Take(worstN).Select(x => ((Tg)x[0]).Num + " " + x[1] + " (" + x[2] + ")").ToList()
            });
        }
        string op = (string)args["outPath"];
        if (!string.IsNullOrEmpty(op)) { Directory.CreateDirectory(Path.GetDirectoryName(op)); File.WriteAllText(op, all.ToString()); }
        return new { Views = res };
    }
}
