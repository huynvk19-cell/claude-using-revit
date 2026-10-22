/* mcp-tool
{
  "description": "Read-only, plans / ceiling plans: rooms with no room tag + tag body centre with the shortest leader and the best TR1-TR7 score; items for roomtag_place_ortho.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "roomIds": { "type": "array", "items": { "type": "number" }, "description": "only these rooms (default: every untagged room of the view)" },
      "typeName": { "type": "string", "description": "room tag type the user chose; empty = type of the nearest tagged rooms" },
      "tagSizeMm": { "type": "array", "items": { "type": "number" }, "description": "tag body width, height on paper mm (profile); default: measured box" },
      "maxScore": { "type": "number", "description": "score of a tag in its room with no leader, default 10" },
      "zeroAtMm": { "type": "number", "description": "leader length on paper mm where TR1 reaches 0 (search radius), default 50" },
      "alignMm": { "type": "number", "description": "TR2: other tag within this paper mm counts for alignment, default 80" },
      "clearMm": { "type": "number", "description": "paper mm kept clear round the body (dims, tags, text), default 1" },
      "insetMm": { "type": "number", "description": "paper mm the leader dot sits inside the room boundary, default 2" },
      "stepMm": { "type": "number", "description": "search grid step on paper mm, default 2" },
      "bubbleMm": { "type": "number", "description": "grid bubble diameter on paper mm kept clear, default 10 (0 = ignore)" },
      "outPath": { "type": "string", "description": "write Items + Problems to this JSON file (one view per call); result = counts only" }
    },
    "required": ["viewIds"]
  },
  "timeoutSeconds": 900,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only. Per plan / ceiling plan view: the rooms seen in the view with no room tag in it (room_tags_score TR8), and
// for each one the tag body centre that scores best by TR1-TR7 (drafting-annotation.md §2, Huy 2026-10-22; the same
// score code as room_tags_score):
//    pass 0: every room whose body fits inside it (no leader = full score) takes an inside spot, smallest room first;
//            aligned with a tag nearby first, then nearest to the room centre;
//    pass 1: the other rooms, smallest first, outside with a leader: body centres on a stepMm grid round the room
//            (plus the rows / columns of nearby tags), leader straight or one right angle (HV = across then up / down,
//            VH = up / down then across), dot insetMm inside the room where the leader first enters it = the shortest
//            leader. Candidates are tried best upper bound first (TR1 by length + TR2), the exact score decides.
//    Never chosen: a body on a wall / floor (ceiling) edge (TR3), a leader on another leader (TR4), along a wall or edge
//    (TR5), through another leader dot (TR6) or with another leader through its dot; a body over a dim, tag, text, spot,
//    grid bubble, leader or dot (clearMm); a leader through a tag / text box (relaxed last, note CHECK); outside the crop.
//    No spot within zeroAtMm -> searched to 2 x zeroAtMm (TR1 = 0), then relaxed, last the room centre (FORCED, CHECK):
//    every room gets an item (TR8). Each item carries its expected score; place, then run room_tags_score.
// Tag type = typeName, else the most common type among the 3 nearest tagged rooms (NeighbourType). Tag size: tagSizeMm,
// else the measured body of a tag of that type in the view.
// Items {kind:'roomtag', elementId, typeName, right, up, leader, endRight, endUp, path} (view frame mm, body centre)
// go to roomtag_place_ortho (itemsPath), which centres the tag BODY on the point (annot_place would not).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomTagsMissing
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

    // first crossing of the room boundary by an axis ray from (x0, y0) after which the point ins further on is in the room:
    // (the dot) is in the room at least 0.9 ins off its boundary: 0 when that point is so already, -1 when none within maxLen
    static double RayIn(Rm r, double x0, double y0, double dx, double dy, double maxLen, double ins)
    {
        Func<double, double, bool> deep = (x, y) => In(r, x, y) && r.B.All(s => PtSeg(x, y, s) >= 0.9 * ins);
        if (deep(x0 + dx * ins, y0 + dy * ins)) return 0;
        var ts = new List<double>();
        foreach (var s in r.B)
        {
            if (dx != 0)
            {
                if ((s.U1 - y0) * (s.U2 - y0) > 0 || Math.Abs(s.U2 - s.U1) < 1e-9) continue;
                double t = (s.R1 + (y0 - s.U1) * (s.R2 - s.R1) / (s.U2 - s.U1) - x0) * dx;
                if (t > 0 && t <= maxLen) ts.Add(t);
            }
            else
            {
                if ((s.R1 - x0) * (s.R2 - x0) > 0 || Math.Abs(s.R2 - s.R1) < 1e-9) continue;
                double t = (s.U1 + (x0 - s.R1) * (s.U2 - s.U1) / (s.R2 - s.R1) - y0) * dy;
                if (t > 0 && t <= maxLen) ts.Add(t);
            }
        }
        foreach (var t in ts.OrderBy(t => t)) if (deep(x0 + dx * (t + ins), y0 + dy * (t + ins))) return t;
        return -1;
    }

    class Cd { public double UB, L, CR, CU; public string Path; public List<double[]> Pts; }

    // pass 1 for one room: the outside spot with the best score, then the shortest leader; stage 0 = within zeroAtMm,
    // 1 = within 2 x zeroAtMm, 2 = the leader may cross a tag / text box
    static Tg Outside(Rm r, string tn, double w, double h, List<Seg> lh, List<Box> la, List<Tg> others, List<Rm> rooms, Prm p,
        double st, double ins, Func<Box, bool> free, out Cd bestC, out double bestS, out string bestWhy, out int bestStage)
    {
        double maxS = p.Max, tol = p.Tol;
        Tg best = null; bestC = null; bestS = -1; bestWhy = null; bestStage = 0;
        for (int stage = 0; stage < 3 && best == null; stage++)
        {
            double Rl = p.ZeroAt * (stage == 0 ? 1 : 2); bool relax = stage == 2;
            double r0 = r.Rect.R0 - Rl - w / 2, r1 = r.Rect.R1 + Rl + w / 2, u0 = r.Rect.U0 - Rl - h / 2, u1 = r.Rect.U1 + Rl + h / 2;
            double sb = st; while (((r1 - r0) / sb + 1) * ((u1 - u0) / sb + 1) > 6000) sb *= 1.25;
            var rs = new List<double>(); var us = new List<double>();
            for (double x = r.Rect.CR; x <= r1; x += sb) rs.Add(x);
            for (double x = r.Rect.CR - sb; x >= r0; x -= sb) rs.Add(x);
            for (double y = r.Rect.CU; y <= u1; y += sb) us.Add(y);
            for (double y = r.Rect.CU - sb; y >= u0; y -= sb) us.Add(y);
            foreach (var t in others.Where(t => t.Body != null && t.Body.CR > r0 - p.Near && t.Body.CR < r1 + p.Near && t.Body.CU > u0 - p.Near && t.Body.CU < u1 + p.Near))
            { rs.Add(t.Body.R0 + w / 2); rs.Add(t.Body.CR); us.Add(t.Body.CU); }
            var cols = Spread(r.Rect.R0 + ins, r.Rect.R1 - ins, 9); var rows = Spread(r.Rect.U0 + ins, r.Rect.U1 - ins, 9);
            var cands = new List<Cd>();
            Action<double, double, string, List<double[]>, double> add = (cr, cu, path, pts, L) =>
                cands.Add(new Cd { CR = cr, CU = cu, Path = path, Pts = pts, L = L, UB = Math.Min(maxS, Math.Max(0, maxS * (1 - L / p.ZeroAt)) + 2) });
            foreach (var cr in rs)
                foreach (var cu in us)
                {
                    if (In(r, cr, cu)) continue; // the body sits outside its room
                    var b = new Box(cr - w / 2, cu - h / 2, cr + w / 2, cu + h / 2);
                    double gr = Math.Max(0, Math.Max(r.Rect.R0 - b.R1, b.R0 - r.Rect.R1)), gu = Math.Max(0, Math.Max(r.Rect.U0 - b.U1, b.U0 - r.Rect.U1));
                    if (Math.Sqrt(gr * gr + gu * gu) > Rl) continue;
                    // straight, out of each side of the body
                    foreach (var dd in new[] { new[] { 1.0, 0 }, new[] { -1.0, 0 }, new[] { 0, 1.0 }, new[] { 0, -1.0 } })
                    {
                        double x0 = dd[0] > 0 ? b.R1 : dd[0] < 0 ? b.R0 : cr, y0 = dd[1] > 0 ? b.U1 : dd[1] < 0 ? b.U0 : cu;
                        double t = RayIn(r, x0, y0, dd[0], dd[1], Rl, ins); if (t < 0) continue;
                        double L = t + ins; if (L > Rl) continue;
                        add(cr, cu, "", new List<double[]> { new[] { x0, y0 }, new[] { x0 + dd[0] * L, y0 + dd[1] * L } }, L);
                    }
                    // HV: across from the left / right side to a column of the room, then up / down into it
                    foreach (var sd in new[] { 1.0, -1.0 })
                    {
                        double sx = sd > 0 ? b.R1 : b.R0;
                        foreach (var X in cols)
                        {
                            if ((X - sx) * sd <= ins || In(r, X, cu)) continue;
                            foreach (var vd in new[] { 1.0, -1.0 })
                            {
                                double t = RayIn(r, X, cu, 0, vd, Rl, ins); if (t <= 0) continue;
                                double L = Math.Abs(X - sx) + t + ins; if (L > Rl) continue;
                                add(cr, cu, "HV", new List<double[]> { new[] { sx, cu }, new[] { X, cu }, new[] { X, cu + vd * (t + ins) } }, L);
                            }
                        }
                    }
                    // VH: up / down from the top / bottom to a row of the room, then across into it
                    foreach (var sd in new[] { 1.0, -1.0 })
                    {
                        double sy = sd > 0 ? b.U1 : b.U0;
                        foreach (var Y in rows)
                        {
                            if ((Y - sy) * sd <= ins || In(r, cr, Y)) continue;
                            foreach (var hd in new[] { 1.0, -1.0 })
                            {
                                double t = RayIn(r, cr, Y, hd, 0, Rl, ins); if (t <= 0) continue;
                                double L = Math.Abs(Y - sy) + t + ins; if (L > Rl) continue;
                                add(cr, cu, "VH", new List<double[]> { new[] { cr, sy }, new[] { cr, Y }, new[] { cr + hd * (t + ins), Y } }, L);
                            }
                        }
                    }
                }
            int evals = 0;
            foreach (var cd in cands.OrderByDescending(x => x.UB).ThenBy(x => x.L))
            {
                if (best != null && (cd.UB < bestS - 1e-9 || (cd.UB < bestS + 1e-9 && cd.L >= bestC.L))) break;
                var b = new Box(cd.CR - w / 2, cd.CU - h / 2, cd.CR + w / 2, cd.CU + h / 2);
                if (!free(b)) continue;
                var segs = new List<Seg>();
                for (int i = 1; i < cd.Pts.Count; i++) segs.Add(new Seg { R1 = cd.Pts[i - 1][0], U1 = cd.Pts[i - 1][1], R2 = cd.Pts[i][0], U2 = cd.Pts[i][1] });
                var dot = cd.Pts[cd.Pts.Count - 1];
                if (others.Any(t => t.Lead.Any(q => PtSeg(dot[0], dot[1], q) < tol))) continue; // another leader through this dot
                if (!relax && segs.Any(s => la.Any(a => SegHits(s, a)) || others.Any(t => t.Body != null && SegHits(s, t.Body)))) continue;
                if (++evals > 4000) break;
                var tg = new Tg { Id = 0, RoomId = r.Id, Num = r.Num, Type = tn, Body = b, Lead = segs, Dot = dot, LeadLen = segs.Sum(s => s.Len) };
                var why = new List<string>();
                double S = Score(tg, r, others, lh, rooms, p, why);
                if (why.Any(x => x.StartsWith("TR3") || x.StartsWith("TR4") || x.StartsWith("TR5") || x.StartsWith("TR6"))) continue; // never a hard 0
                if (best == null || S > bestS + 1e-9 || (S > bestS - 1e-9 && cd.L < bestC.L)) { best = tg; bestC = cd; bestS = S; bestWhy = string.Join("; ", why); bestStage = stage; }
            }
        }
        return best;
    }
    static List<double> Spread(double a, double b, int n)
    {
        var res = new List<double>();
        if (b < a) return res;
        if (n <= 1 || b - a < 1e-6) { res.Add((a + b) / 2); return res; }
        for (int i = 0; i < n; i++) res.Add(a + (b - a) * i / (n - 1));
        return res;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var viewIds = ((JArray)args["viewIds"]).Select(t => (int)t).ToList();
        HashSet<int> only = args["roomIds"] is JArray ra && ra.Count > 0 ? new HashSet<int>(ra.Select(t => (int)t)) : null;
        string typeArg = ((string)args["typeName"] ?? "").Trim();
        double[] sizeArg = args["tagSizeMm"] is JArray sa && sa.Count == 2 ? new[] { (double)sa[0], (double)sa[1] } : null;
        double maxS = (double?)args["maxScore"] ?? 10, zeroAt = (double?)args["zeroAtMm"] ?? 50, alignMm = (double?)args["alignMm"] ?? 80;
        double clearMm = (double?)args["clearMm"] ?? 1, insetMm = (double?)args["insetMm"] ?? 2, stepMm = (double?)args["stepMm"] ?? 2;
        double bubbleMm = (double?)args["bubbleMm"] ?? 10;
        string outPath = (string)args["outPath"];
        var outViews = new List<object>();

        foreach (var vid in viewIds)
        {
            var v = doc.GetElement(new ElementId(vid)) as ViewPlan;
            if (v == null) { outViews.Add(new { ViewId = vid, Error = "not a plan / ceiling plan view" }); continue; }
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
            var tags = MeasureTags(doc, v, F, sizeArg);
            var p = new Prm { Max = maxS, ZeroAt = zeroAt * sc, Tol = 0.5 * sc, Near = alignMm * sc, Sc = sc };
            double clr = clearMm * sc, ins = insetMm * sc, st = stepMm * sc, tol = p.Tol;

            // obstacles for the body: dim lines, tag / text / spot / generic annotation boxes, grid bubbles
            var annSegs = new List<Seg>(); var ann = new List<Box>();
            if (bubbleMm > 0)
                foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
                {
                    try
                    {
                        var gc = g.GetCurvesInView(DatumExtentType.ViewSpecific, v);
                        if (gc == null || gc.Count == 0 || !(gc[0] is Line gl)) continue;
                        double rB = bubbleMm * sc / 2;
                        var a = F(gl.GetEndPoint(0)); var b = F(gl.GetEndPoint(1));
                        double len = Math.Sqrt(Math.Pow(b[0] - a[0], 2) + Math.Pow(b[1] - a[1], 2)); if (len < 1) continue;
                        double ur = (b[0] - a[0]) / len, uu = (b[1] - a[1]) / len;
                        if (g.IsBubbleVisibleInView(DatumEnds.End0, v)) ann.Add(new Box(a[0] - ur * rB - rB, a[1] - uu * rB - rB, a[0] - ur * rB + rB, a[1] - uu * rB + rB));
                        if (g.IsBubbleVisibleInView(DatumEnds.End1, v)) ann.Add(new Box(b[0] + ur * rB - rB, b[1] + uu * rB - rB, b[0] + ur * rB + rB, b[1] + uu * rB + rB));
                    }
                    catch { }
                }
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
            {
                if (e.OwnerViewId != v.Id || e.Category == null || e is RoomTag) continue;
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
                        annSegs.Add(new Seg { R1 = fs.First()[0], U1 = fs.First()[1], R2 = fs.Last()[0], U2 = fs.Last()[1] });
                        continue;
                    }
                    bool isAnn = e is IndependentTag || e is TextNote || e is SpatialElementTag || e is SpotDimension
                        || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericAnnotation;
                    if (!isAnn) continue;
                    var bb = e.get_BoundingBox(v);
                    if (bb == null) continue;
                    var a = F(bb.Min); var b = F(bb.Max);
                    ann.Add(new Box(a[0], a[1], b[0], b[1]));
                }
                catch { }
            }

            var tagged = new HashSet<int>(tags.Select(t => t.RoomId));
            var todo = rooms.Where(r => !tagged.Contains(r.Id) && (only == null || only.Contains(r.Id))).OrderBy(r => r.Area).ToList();
            var others = new List<Tg>(tags); int nid = -1;
            var items = new List<object>(); var problems = new List<string>(); var scores = new List<double>();
            int nIn = 0, nOut = 0, nForced = 0;
            Func<Box, bool> inCrop = b => crop == null || (b.R0 >= crop.R0 && b.R1 <= crop.R1 && b.U0 >= crop.U0 && b.U1 <= crop.U1);
            Func<Box, List<Box>, List<Seg>, List<Seg>, bool> bodyFree = (b, la, ls, lh) => inCrop(b)
                && !la.Any(a => a.Hits(b.Grow(clr))) && !ls.Any(s => SegHits(s, b.Grow(clr)))
                && !others.Any(o => (o.Body != null && o.Body.Hits(b.Grow(clr))) || o.Lead.Any(s => SegHits(s, b)) || (o.Dot != null && b.Has(o.Dot[0], o.Dot[1])))
                && !lh.Any(s => SegHits(s, b.Grow(clr))); // TR3, kept clearMm off walls and edges
            Func<Box, bool> aligned = b => others.Any(o => o.Body != null && Math.Abs(o.Body.CR - b.CR) < p.Near && Math.Abs(o.Body.CU - b.CU) < p.Near
                && (Math.Abs(o.Body.CU - b.CU) < tol || Math.Abs(o.Body.R0 - b.R0) < tol || Math.Abs(o.Body.CR - b.CR) < tol));

            // per room: type, size, anchor (rectangle centre, else the nearest point in the room), local obstacles
            var prep = new Dictionary<int, object[]>();
            foreach (var r in todo)
            {
                double[] A = { r.Rect.CR, r.Rect.CU };
                if (!In(r, A[0], A[1]))
                {
                    double[] best = null; double bd = double.MaxValue;
                    for (int i = 0; i < 30; i++) for (int j = 0; j < 30; j++)
                        {
                            double x = r.Rect.R0 + (r.Rect.R1 - r.Rect.R0) * (i + 0.5) / 30, y = r.Rect.U0 + (r.Rect.U1 - r.Rect.U0) * (j + 0.5) / 30;
                            double dd = Math.Pow(x - A[0], 2) + Math.Pow(y - A[1], 2);
                            if (dd < bd && In(r, x, y)) { bd = dd; best = new[] { x, y }; }
                        }
                    if (best == null) { problems.Add(r.Num + ": no point inside the room found"); continue; }
                    A = best;
                }
                string nType = tags.Where(t => t.Body != null && t.Type != "").OrderBy(t => Math.Pow(t.Body.CR - A[0], 2) + Math.Pow(t.Body.CU - A[1], 2)).Take(3)
                    .GroupBy(t => t.Type).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                string tn = typeArg.Length > 0 ? typeArg : nType;
                if (string.IsNullOrEmpty(tn)) { problems.Add(r.Num + ": no type (pass typeName)"); continue; }
                double w, h;
                if (sizeArg != null) { w = sizeArg[0] * sc; h = sizeArg[1] * sc; }
                else
                {
                    var mb = tags.FirstOrDefault(t => t.Type == tn && t.Body != null)?.Body;
                    if (mb == null) { problems.Add(r.Num + ": tag size unknown for '" + tn + "' (pass tagSizeMm)"); continue; }
                    w = mb.R1 - mb.R0; h = mb.U1 - mb.U0;
                }
                var zone = r.Rect.Grow(2 * p.ZeroAt + w + h);
                prep[r.Id] = new object[] { tn, w, h, A, hard.Where(s => SegHits(s, zone)).ToList(), ann.Where(b => b.Hits(zone)).ToList(), annSegs.Where(s => SegHits(s, zone)).ToList() };
            }

            var done = new HashSet<int>();
            // pass 0: inside, no leader (full score), smallest room first
            foreach (var r in todo.Where(r => prep.ContainsKey(r.Id)))
            {
                var o = prep[r.Id]; string tn = (string)o[0]; double w = (double)o[1], h = (double)o[2]; var A = (double[])o[3];
                var lh = (List<Seg>)o[4]; var la = (List<Box>)o[5]; var ls = (List<Seg>)o[6];
                if (r.Rect.R1 - r.Rect.R0 < w || r.Rect.U1 - r.Rect.U0 < h) continue;
                double sx = st, sy = st;
                while (((r.Rect.R1 - r.Rect.R0 - w) / sx + 1) * ((r.Rect.U1 - r.Rect.U0 - h) / sy + 1) > 40000) { sx *= 1.5; sy *= 1.5; }
                var xs = new List<double>(); var ys = new List<double>();
                for (double x = r.Rect.R0 + w / 2; x <= r.Rect.R1 - w / 2 + 1e-6; x += sx) xs.Add(x);
                for (double y = r.Rect.U0 + h / 2; y <= r.Rect.U1 - h / 2 + 1e-6; y += sy) ys.Add(y);
                foreach (var t in others.Where(t => t.Body != null && Math.Abs(t.Body.CR - A[0]) < p.Near + r.Rect.R1 - r.Rect.R0 && Math.Abs(t.Body.CU - A[1]) < p.Near + r.Rect.U1 - r.Rect.U0))
                { xs.Add(t.Body.R0 + w / 2); xs.Add(t.Body.CR); ys.Add(t.Body.CU); }
                var cands = xs.SelectMany(x => ys.Select(y => new[] { x, y, Math.Sqrt(Math.Pow(x - A[0], 2) + Math.Pow(y - A[1], 2)) })).OrderBy(q => q[2]).ToList();
                Box pick = null; double d0 = -1;
                foreach (var q in cands)
                {
                    if (d0 >= 0 && q[2] > d0 + 5 * sc) break; // an aligned spot up to 5 mm further wins
                    var b = new Box(q[0] - w / 2, q[1] - h / 2, q[0] + w / 2, q[1] + h / 2);
                    var pts = new[] { new[] { b.R0, b.U0 }, new[] { b.R1, b.U0 }, new[] { b.R0, b.U1 }, new[] { b.R1, b.U1 }, new[] { b.CR, b.U0 }, new[] { b.CR, b.U1 }, new[] { b.R0, b.CU }, new[] { b.R1, b.CU }, new[] { b.CR, b.CU } };
                    if (!pts.All(pt => In(r, pt[0], pt[1])) || r.B.Any(s => SegHits(s, b.Grow(clr))) || !bodyFree(b, la, ls, lh)) continue;
                    if (aligned(b)) { pick = b; break; }
                    if (d0 < 0) { pick = b; d0 = q[2]; }
                }
                if (pick == null) continue;
                others.Add(new Tg { Id = nid--, RoomId = r.Id, Num = r.Num, Type = tn, Body = pick });
                items.Add(new { kind = "roomtag", elementId = r.Id, typeName = tn, right = Math.Round(pick.CR), up = Math.Round(pick.CU), leader = false, score = maxS, note = r.Num + " · inside" });
                scores.Add(maxS); nIn++; done.Add(r.Id);
            }

            // pass 1: outside with the shortest leader that scores best, smallest room first
            foreach (var r in todo.Where(r => prep.ContainsKey(r.Id) && !done.Contains(r.Id)))
            {
                var o = prep[r.Id]; string tn = (string)o[0]; double w = (double)o[1], h = (double)o[2]; var A = (double[])o[3];
                var lh = (List<Seg>)o[4]; var la = (List<Box>)o[5]; var ls = (List<Seg>)o[6];
                Cd bestC; double bestS; string bestWhy; int bestStage;
                var best = Outside(r, tn, w, h, lh, la, others, rooms, p, st, ins, b => bodyFree(b, la, ls, lh), out bestC, out bestS, out bestWhy, out bestStage);
                if (best == null)
                {
                    // last resort: the room centre, no leader, flagged
                    var b = new Box(A[0] - w / 2, A[1] - h / 2, A[0] + w / 2, A[1] + h / 2);
                    others.Add(new Tg { Id = nid--, RoomId = r.Id, Num = r.Num, Type = tn, Body = b });
                    items.Add(new { kind = "roomtag", elementId = r.Id, typeName = tn, right = Math.Round(A[0]), up = Math.Round(A[1]), leader = false, score = (double?)null, note = r.Num + " · inside FORCED · CHECK" });
                    problems.Add(r.Num + ": no clear place, put at the room centre (check)");
                    nForced++; continue;
                }
                best.Id = nid--; others.Add(best);
                var end = bestC.Pts[bestC.Pts.Count - 1];
                string how = (bestC.Path == "" ? "straight" : "elbow " + bestC.Path) + ", leader " + Math.Round(bestC.L / sc, 1) + " mm, score " + Math.Round(bestS, 2)
                    + (bestStage == 2 ? " · CHECK: leader crosses a tag / text" : bestStage == 1 ? " · beyond zeroAtMm" : "") + (bestWhy.Length > 0 ? " (" + bestWhy + ")" : "");
                if (bestStage > 0) problems.Add(r.Num + ": " + how);
                items.Add(new { kind = "roomtag", elementId = r.Id, typeName = tn, right = Math.Round(bestC.CR), up = Math.Round(bestC.CU), leader = true, endRight = Math.Round(end[0]), endUp = Math.Round(end[1]), path = bestC.Path, score = Math.Round(bestS, 2), note = r.Num + " · outside " + how });
                scores.Add(bestS); nOut++;
            }

            if (!string.IsNullOrEmpty(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, JObject.FromObject(new { ViewId = vid, View = v.Name, Problems = problems, Items = items }).ToString());
            }
            bool toFile = !string.IsNullOrEmpty(outPath);
            outViews.Add(new
            {
                ViewId = vid, View = v.Name, Rooms = rooms.Count, Tagged = rooms.Count(r => tagged.Contains(r.Id)), Untagged = todo.Count,
                Inside = nIn, Outside = nOut, Forced = nForced, NotPlanned = todo.Count - nIn - nOut - nForced,
                ScoreAvgNew = scores.Count == 0 ? (double?)null : Math.Round(scores.Average(), 2),
                TagSize = sizeArg != null ? string.Join(" x ", sizeArg) + " mm (tagSizeMm)" : "measured body of a placed tag",
                Problems = toFile ? null : problems, Items = toFile ? null : items, OutPath = toFile ? outPath : null
            });
        }
        return new { Views = outViews };
    }
}
