/* mcp-tool
{
  "description": "Read-only: score every room tag in plan views 0-10 (rules RS1-RS4) and the view average; worst tags listed.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "tagSizeMm": { "type": "array", "items": { "type": "number" }, "description": "tag body width, height on paper mm (profile), default 30, 13" },
      "worst": { "type": "number", "description": "how many lowest-score tags to list per view, default 10" },
      "outPath": { "type": "string", "description": "write every tag's score to this JSON file" }
    },
    "required": ["viewIds"]
  },
  "timeoutSeconds": 180,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Score of one room tag, start 10 (drafting-annotation.md §2, user 2026-10-21):
//    RS1 its leader lies on another room tag leader (same line, overlapping)  -> 0 at once
//    RS2 the leader has a right-angle bend (elbow off the straight line)       -> -5
//    RS3 the leader is longer than 3x / 2x the long side of its room           -> -9 / -5
//    RS4 the tag body sits on another room                                     -> -5
//    floor 0. View score = average of its tags.
// Geometry: room tag box in the view (it includes the leader); body = tagSizeMm at the far end of the box from the
// leader end (a tag without leader: its own box). Leader = body centre -> elbow -> end, length measured from the body
// edge. Room long side = the longer side of the room's box in the view frame.
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
    class Seg { public double R1, U1, R2, U2; }

    static bool Overlap(Seg a, Seg b, double tol)
    {
        bool ah = Math.Abs(a.U1 - a.U2) < 1, bh = Math.Abs(b.U1 - b.U2) < 1, av = Math.Abs(a.R1 - a.R2) < 1, bv = Math.Abs(b.R1 - b.R2) < 1;
        if (ah && bh && Math.Abs(a.U1 - b.U1) < tol) return Math.Min(Math.Max(a.R1, a.R2), Math.Max(b.R1, b.R2)) - Math.Max(Math.Min(a.R1, a.R2), Math.Min(b.R1, b.R2)) > tol;
        if (av && bv && Math.Abs(a.R1 - b.R1) < tol) return Math.Min(Math.Max(a.U1, a.U2), Math.Max(b.U1, b.U2)) - Math.Max(Math.Min(a.U1, a.U2), Math.Min(b.U1, b.U2)) > tol;
        return false;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        double[] sz = args["tagSizeMm"] is JArray sa && sa.Count == 2 ? new[] { (double)sa[0], (double)sa[1] } : new[] { 30.0, 13.0 };
        int worstN = (int?)args["worst"] ?? 10;
        var all = new JArray();
        var res = new List<object>();
        foreach (var vid in ((JArray)args["viewIds"]).Select(x => (int)x))
        {
            var v = doc.GetElement(new ElementId(vid)) as ViewPlan;
            if (v == null) { res.Add(new { ViewId = vid, Error = "not a plan view" }); continue; }
            XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection; double sc = v.Scale;
            Func<XYZ, double[]> F = p => new[] { (p - O).DotProduct(R) * MM, (p - O).DotProduct(U) * MM };
            Func<double, double, double, XYZ> P = (r, u, z) => { var p = O + R * (r / MM) + U * (u / MM); return new XYZ(p.X, p.Y, z); };
            double bw = sz[0] * sc, bh = sz[1] * sc, tolO = 0.3 * sc, tolS = 0.5 * sc;

            // rooms seen in the view: box in the view frame, test height
            var rooms = new Dictionary<int, object[]>();
            foreach (var r in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>())
            {
                if (r.Area <= 0 || !(r.Location is LocationPoint lp)) continue;
                var bb = r.get_BoundingBox(null); if (bb == null) continue;
                var cs = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, 0), new XYZ(bb.Max.X, bb.Min.Y, 0) }.Select(F).ToList();
                rooms[r.Id.IntegerValue] = new object[] { r, cs.Min(c => c[0]), cs.Min(c => c[1]), cs.Max(c => c[0]), cs.Max(c => c[1]), lp.Point.Z + 300 / MM };
            }
            Func<int, double, double, bool> inRoom = (id, pr, pu) =>
            {
                if (!rooms.ContainsKey(id)) return false; var o = rooms[id];
                if (pr < (double)o[1] || pr > (double)o[3] || pu < (double)o[2] || pu > (double)o[4]) return false;
                try { return ((Room)o[0]).IsPointInRoom(P(pr, pu, (double)o[5])); } catch { return false; }
            };

            var rows = new List<object[]>(); // tag, number, body centre, segs, long side, elbow, len
            foreach (var t in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().OfType<RoomTag>())
            {
                Room rm = null; try { rm = t.Room; } catch { }
                var tb = t.get_BoundingBox(v);
                if (rm == null || tb == null) continue;
                double r0 = F(tb.Min)[0], u0 = F(tb.Min)[1], r1 = F(tb.Max)[0], u1 = F(tb.Max)[1];
                double tr0 = Math.Min(r0, r1), tr1 = Math.Max(r0, r1), tu0 = Math.Min(u0, u1), tu1 = Math.Max(u0, u1);
                double longSide = rooms.ContainsKey(rm.Id.IntegerValue) ? Math.Max((double)rooms[rm.Id.IntegerValue][3] - (double)rooms[rm.Id.IntegerValue][1], (double)rooms[rm.Id.IntegerValue][4] - (double)rooms[rm.Id.IntegerValue][2]) : 0;
                var segs = new List<Seg>(); bool elbow = false; double len = 0;
                double cr = (tr0 + tr1) / 2, cu = (tu0 + tu1) / 2;
                if (t.HasLeader)
                {
                    var le = F(t.LeaderEnd);
                    double w = Math.Min(bw, tr1 - tr0), h = Math.Min(bh, tu1 - tu0);
                    double dl = le[0] - tr0, dr = tr1 - le[0], dd = le[1] - tu0, du = tu1 - le[1], m = new[] { dl, dr, dd, du }.Min();
                    if (m == dl) { cr = tr1 - w / 2; } else if (m == dr) { cr = tr0 + w / 2; } else if (m == dd) { cu = tu1 - h / 2; } else { cu = tu0 + h / 2; }
                    if (m == dl || m == dr) cu = (tu0 + tu1) / 2 + 0; // body spans the full box height on a side leader
                    if (m == dd || m == du) cr = (tr0 + tr1) / 2;
                    var pts = new List<double[]> { new[] { cr, cu } };
                    try { if (t.HasElbow) { var e = F(t.LeaderElbow); pts.Add(e); } } catch { }
                    pts.Add(le);
                    // drop an elbow lying on the straight line
                    if (pts.Count == 3)
                    {
                        double cross = (pts[1][0] - pts[0][0]) * (pts[2][1] - pts[0][1]) - (pts[1][1] - pts[0][1]) * (pts[2][0] - pts[0][0]);
                        double l02 = Math.Sqrt(Math.Pow(pts[2][0] - pts[0][0], 2) + Math.Pow(pts[2][1] - pts[0][1], 2));
                        if (l02 < 1 || Math.Abs(cross) / l02 < tolS) pts.RemoveAt(1); else elbow = true;
                    }
                    for (int i = 1; i < pts.Count; i++)
                    {
                        segs.Add(new Seg { R1 = pts[i - 1][0], U1 = pts[i - 1][1], R2 = pts[i][0], U2 = pts[i][1] });
                        len += Math.Sqrt(Math.Pow(pts[i][0] - pts[i - 1][0], 2) + Math.Pow(pts[i][1] - pts[i - 1][1], 2));
                    }
                    var s0 = segs[0]; bool horiz = Math.Abs(s0.U2 - s0.U1) < Math.Abs(s0.R2 - s0.R1);
                    len = Math.Max(0, len - (horiz ? w : h) / 2); // from the body edge
                }
                rows.Add(new object[] { t, rm, new[] { cr, cu }, segs, longSide, elbow, len });
            }

            var scored = new List<object[]>(); // number, score, reasons, tag id
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; var rm = (Room)row[1]; var c = (double[])row[2]; var segs = (List<Seg>)row[3];
                double longSide = (double)row[4], len = (double)row[6]; bool elbow = (bool)row[5];
                var why = new List<string>(); double s = 10;
                bool ov = segs.Any(a => rows.Where((x, j) => j != i).Any(x => ((List<Seg>)x[3]).Any(b => Overlap(a, b, tolO))));
                if (ov) { s = 0; why.Add("RS1 leader on another leader"); }
                else
                {
                    if (elbow) { s -= 5; why.Add("RS2 elbow"); }
                    if (longSide > 0 && len > 3 * longSide) { s -= 9; why.Add("RS3 leader > 3x room (" + Math.Round(len / sc) + " mm)"); }
                    else if (longSide > 0 && len > 2 * longSide) { s -= 5; why.Add("RS3 leader > 2x room (" + Math.Round(len / sc) + " mm)"); }
                    if (!inRoom(rm.Id.IntegerValue, c[0], c[1]) && rooms.Keys.Any(k => k != rm.Id.IntegerValue && inRoom(k, c[0], c[1]))) { s -= 5; why.Add("RS4 on another room"); }
                    s = Math.Max(0, s);
                }
                scored.Add(new object[] { rm.Number, s, string.Join("; ", why), ((RoomTag)row[0]).Id.IntegerValue });
            }
            foreach (var x in scored) all.Add(new JObject { ["view"] = vid, ["tag"] = (int)x[3], ["room"] = (string)x[0], ["score"] = (double)x[1], ["why"] = (string)x[2] });
            res.Add(new
            {
                ViewId = vid, View = v.Name, Tags = scored.Count,
                ScoreAvg = scored.Count == 0 ? (double?)null : Math.Round(scored.Average(x => (double)x[1]), 2),
                Count = new
                {
                    Zero = scored.Count(x => ((string)x[2]).Contains("RS1")), Elbow = scored.Count(x => ((string)x[2]).Contains("RS2")),
                    Long2x = scored.Count(x => ((string)x[2]).Contains("> 2x")), Long3x = scored.Count(x => ((string)x[2]).Contains("> 3x")),
                    OtherRoom = scored.Count(x => ((string)x[2]).Contains("RS4")), Full10 = scored.Count(x => (double)x[1] >= 10)
                },
                Worst = scored.OrderBy(x => (double)x[1]).Take(worstN).Select(x => x[0] + " " + x[1] + " (" + x[2] + ")").ToList()
            });
        }
        string op = (string)args["outPath"];
        if (!string.IsNullOrEmpty(op)) { Directory.CreateDirectory(Path.GetDirectoryName(op)); File.WriteAllText(op, all.ToString()); }
        return new { Views = res };
    }
}
