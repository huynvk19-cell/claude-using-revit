/* mcp-tool
{
  "description": "Grid bubbles G5: find overlapping bubbles per row and elbow one sideways to the roomier side. plan | preview | apply | remove.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "mode": { "type": "string", "enum": ["plan", "preview", "apply", "remove"] },
      "items": { "type": "array", "items": { "type": "object" }, "description": "{grid, end:'low'|'high', toward:'auto'|'plus'|'minus', offsetMm}; omit = plan" },
      "only": { "type": "array", "items": { "type": "string" }, "description": "plan / auto items: only these grids" },
      "bubbleMm": { "type": "number", "description": "bubble diameter on paper (default: measured from the grids)" },
      "gapMm": { "type": "number", "description": "paper mm kept between bubbles, default 2" },
      "logPath": { "type": "string" }
    },
    "required": ["viewId", "mode"]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Rule G5 (user 2026-10-17): the bubbles on one side of a grid group stay in ONE row (same 2D end); only where two
//    bubbles would overlap, ONE of them gets an elbow (Revit "Add Elbow") that moves it sideways, toward the side with
//    more room. No stagger along the grid.
// plan (read-only): per group (grids running up = 'vertical', running right = 'horizontal') and per end (low / high
//    along the view's up or right), the visible bubbles sorted across the group; pairs closer than bubble + gap are
//    'Overlaps'. For each pair the grid whose outer side (away from the other grid) has more room to its next
//    neighbour gets the elbow; offset = bubble + gap - spacing (coincident grids: bubble + gap). 'OffRow' lists ends
//    that are not on the row (most common end value) - put them back with align_grid_ends before the elbow.
//    'Tight' = the elbowed bubble would then touch its next neighbour too.
// preview / apply: items given, or (no items) the plan's proposals. Item: grid (exact name, invisible characters
//    are ignored when matching), end 'low'|'high', toward 'auto'|'plus'|'minus' (plus = view right for grids running
//    up, view up for grids running right), offsetMm (paper mm sideways; default from the plan). Legacy: sideMm or
//    shiftMm = signed offset. The leader is built from Revit's default leader; several elbow shapes are tried until
//    IsLeaderValid accepts one ('Shape' in the result); the bubble offset is read back after setting it.
//    preview rolls back. apply writes logPath.
// remove: items {grid, end} -> tries to take the elbow off (SetLeader null); when the API refuses, use the grid's
//    Remove Elbow grip or Ctrl+Z in Revit.
// Bubble diameter: from the grid's box along its own direction (box length - line length) / bubbles shown, needs a
//    view whose right/up run along project X/Y; else pass bubbleMm (a '6.5mm Bubble' type measured 11 mm).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class GridBubbleElbow
{
    const double Ft = 304.8;

    class GI { public Grid G; public string Name, Group; public XYZ P0, P1, Dir, Axis, Side; public bool RunsUp; }
    class Bub { public GI Gi; public string End; public DatumEnds De; public double Perp, Along; public bool HasLeader; }

    static string Clean(string s) { return new string((s ?? "").Where(ch => !char.IsControl(ch) && char.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim(); }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var view = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        string mode = (string)args["mode"] ?? "plan";
        double paper = view.Scale / Ft; // paper mm -> model feet
        XYZ right = view.RightDirection, up = view.UpDirection, o = view.Origin;
        double gap = args["gapMm"] != null ? (double)args["gapMm"] : 2;
        var only = (args["only"] as JArray)?.Select(x => Clean((string)x)).ToList();

        // ---- grids in view
        var gis = new List<GI>(); var skipped = new List<string>();
        foreach (var g in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            Curve c = null;
            try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, view).FirstOrDefault(); } catch { }
            if (c == null) try { c = g.GetCurvesInView(DatumExtentType.Model, view).FirstOrDefault(); } catch { }
            if (!(c is Line)) { skipped.Add(g.Name + " (not straight)"); continue; }
            XYZ p0 = c.GetEndPoint(0), p1 = c.GetEndPoint(1), d = (p1 - p0).Normalize();
            double du = Math.Abs(d.DotProduct(up)), dr = Math.Abs(d.DotProduct(right));
            if (du < 0.999 && dr < 0.999) { skipped.Add(g.Name + " (skewed)"); continue; }
            bool runsUp = du > dr;
            gis.Add(new GI { G = g, Name = g.Name, Group = runsUp ? "vertical" : "horizontal", P0 = p0, P1 = p1, Dir = d, RunsUp = runsUp, Axis = runsUp ? up : right, Side = runsUp ? right : up });
        }

        // ---- bubble diameter (paper mm)
        double bubble = 0;
        if (args["bubbleMm"] != null) bubble = (double)args["bubbleMm"];
        else
        {
            bool aligned = Math.Abs(Math.Abs(right.X) - 1) < 1e-6 || Math.Abs(Math.Abs(right.Y) - 1) < 1e-6;
            var meas = new List<double>();
            if (aligned)
                foreach (var gi in gis)
                {
                    int nb = (gi.G.IsBubbleVisibleInView(DatumEnds.End0, view) ? 1 : 0) + (gi.G.IsBubbleVisibleInView(DatumEnds.End1, view) ? 1 : 0);
                    var bb = gi.G.get_BoundingBox(view);
                    if (nb == 0 || bb == null) continue;
                    XYZ ext = bb.Max - bb.Min;
                    double alongExt = Math.Abs(ext.X * gi.Axis.X) + Math.Abs(ext.Y * gi.Axis.Y);
                    double len = (gi.P1 - gi.P0).GetLength();
                    double dmm = (alongExt - len) / nb / paper;
                    if (dmm > 2 && dmm < 40) meas.Add(dmm);
                }
            bubble = meas.Count > 0 ? Math.Round(meas.OrderBy(x => x).ElementAt(meas.Count / 2), 1) : 11;
        }
        double need = bubble + gap;

        // ---- bubbles per group/end
        Func<XYZ, XYZ, double> C = (p, ax) => (p - o).DotProduct(ax) / paper;
        var bubs = new List<Bub>();
        foreach (var gi in gis)
            foreach (var de in new[] { DatumEnds.End0, DatumEnds.End1 })
            {
                if (!gi.G.IsBubbleVisibleInView(de, view)) continue;
                XYZ p = de == DatumEnds.End0 ? gi.P0 : gi.P1, q = de == DatumEnds.End0 ? gi.P1 : gi.P0;
                string end = p.DotProduct(gi.Axis) < q.DotProduct(gi.Axis) ? "low" : "high";
                Leader ld = null; try { ld = gi.G.GetLeader(de, view); } catch { }
                XYZ at = ld != null ? ld.End : p;
                bubs.Add(new Bub { Gi = gi, End = end, De = de, Perp = C(at, gi.Side), Along = C(p, gi.Axis), HasLeader = ld != null });
            }

        // ---- plan
        var overlaps = new List<object>(); var offRow = new List<object>(); var proposals = new List<JObject>(); var tight = new List<string>();
        var taken = new HashSet<string>();
        foreach (var row in bubs.GroupBy(b => b.Gi.Group + "|" + b.End))
        {
            var list = row.OrderBy(b => b.Perp).ToList();
            double rowAlong = list.GroupBy(b => Math.Round(b.Along)).OrderByDescending(gp => gp.Count()).First().Key;
            foreach (var b in list) if (Math.Abs(b.Along - rowAlong) > 0.5) offRow.Add(new { Grid = b.Gi.Name, Row = row.Key, OffMm = Math.Round(b.Along - rowAlong, 1) });
            for (int i = 0; i + 1 < list.Count; i++)
            {
                Bub a = list[i], b = list[i + 1];
                double sp = b.Perp - a.Perp;
                if (sp >= need - 0.05 || Math.Abs(a.Along - b.Along) >= need) continue;
                if (only != null && !only.Contains(Clean(a.Gi.Name)) && !only.Contains(Clean(b.Gi.Name))) continue;
                double roomA = i > 0 ? a.Perp - list[i - 1].Perp : 1e6;            // a moves minus
                double roomB = i + 2 < list.Count ? list[i + 2].Perp - b.Perp : 1e6; // b moves plus
                if (a.HasLeader || b.HasLeader) { overlaps.Add(new { Row = row.Key, A = a.Gi.Name, B = b.Gi.Name, SpacingMm = Math.Round(sp, 1), Note = "already elbowed" }); continue; }
                bool moveB = roomB >= roomA;
                var m = moveB ? b : a; double room = moveB ? roomB : roomA;
                if (only != null && !only.Contains(Clean(m.Gi.Name))) { m = moveB ? a : b; moveB = !moveB; room = moveB ? roomB : roomA; }
                double off = Math.Round(need - sp, 1);
                string key = m.Gi.Name + "|" + m.End;
                overlaps.Add(new { Row = row.Key, A = a.Gi.Name, B = b.Gi.Name, SpacingMm = Math.Round(sp, 1), Elbow = m.Gi.Name, Toward = moveB ? "plus" : "minus", OffsetMm = off, RoomMm = room > 1e5 ? (double?)null : Math.Round(room, 1) });
                if (room < 1e5 && room - off < need) tight.Add(m.Gi.Name + " " + m.End);
                if (taken.Add(key)) proposals.Add(new JObject { ["grid"] = m.Gi.Name, ["end"] = m.End, ["toward"] = moveB ? "plus" : "minus", ["offsetMm"] = off });
            }
        }
        if (mode == "plan")
            return new { Mode = mode, View = view.Name, BubbleMm = bubble, NeedMm = need, Overlaps = overlaps, Proposed = proposals, OffRow = offRow, Tight = tight, Skipped = skipped };

        // ---- preview / apply / remove
        var items = (args["items"] as JArray)?.Cast<JObject>().ToList() ?? proposals;
        var report = new List<object>(); var log = new JArray();
        using (var t = new Transaction(doc, mode == "remove" ? "Grid bubble elbows off" : "Grid bubble elbows"))
        {
            t.Start();
            foreach (var it in items)
            {
                string name = Clean((string)it["grid"]), endW = (string)it["end"] ?? "low";
                var gi = gis.FirstOrDefault(x => Clean(x.Name) == name);
                if (gi == null) { report.Add(new { Grid = name, Error = "not a straight grid in view" }); continue; }
                var g = gi.G;
                bool end0Low = gi.P0.DotProduct(gi.Axis) < gi.P1.DotProduct(gi.Axis);
                DatumEnds de = (endW == "high") ^ end0Low ? DatumEnds.End0 : DatumEnds.End1;
                XYZ endPt = de == DatumEnds.End0 ? gi.P0 : gi.P1;
                XYZ outward = endW == "high" ? gi.Axis : gi.Axis.Negate();
                bool pinned = g.Pinned; if (pinned) g.Pinned = false;
                string err = null, shape = null; double? got = null;
                try
                {
                    if (mode == "remove")
                    {
                        try { g.SetLeader(de, view, null); shape = "removed"; } catch (Exception ex) { err = "API cannot remove: use Remove Elbow grip / Ctrl+Z (" + ex.Message + ")"; }
                    }
                    else
                    {
                        // signed sideways offset s (model feet, along gi.Side)
                        double off = it["offsetMm"] != null ? (double)it["offsetMm"] : need;
                        double s;
                        if (it["sideMm"] != null) s = (double)it["sideMm"] * paper;
                        else if (it["shiftMm"] != null) s = (double)it["shiftMm"] * paper;
                        else
                        {
                            string tw = (string)it["toward"] ?? "auto";
                            if (tw == "auto")
                            {
                                var pr = proposals.FirstOrDefault(p => Clean((string)p["grid"]) == name && (string)p["end"] == endW);
                                tw = pr != null ? (string)pr["toward"] : "plus";
                                if (pr != null && it["offsetMm"] == null) off = (double)pr["offsetMm"];
                            }
                            s = (tw == "minus" ? -1 : 1) * off * paper;
                        }
                        bool had = g.GetLeader(de, view) != null;
                        log.Add(new JObject { ["id"] = g.Id.IntegerValue, ["grid"] = g.Name, ["end"] = endW, ["hadLeader"] = had });
                        if (!had) g.AddLeader(de, view);
                        var ld = g.GetLeader(de, view);
                        XYZ A = ld.Anchor, E0 = ld.Elbow, N0 = ld.End, S = gi.Side;
                        double eIn = Math.Max(Math.Abs((E0 - endPt).DotProduct(outward)), 2 * paper);   // default elbow distance inside the end
                        double aIn = Math.Abs((A - endPt).DotProduct(outward));
                        double dfl = (N0 - endPt).DotProduct(S);
                        XYZ endT = endPt + S * s;
                        var cands = new List<Tuple<string, XYZ, XYZ>>   // name, anchor (null = keep), elbow
                        {
                            Tuple.Create("default-depth", (XYZ)null, endPt - outward * eIn + S * s),
                            Tuple.Create("mid", (XYZ)null, endPt - outward * (Math.Max(aIn, eIn) / 2) + S * s),
                            Tuple.Create("anchor-45", endPt - outward * (eIn + Math.Abs(s)), endPt - outward * eIn + S * s),
                            Tuple.Create("anchor-perp", (XYZ)null, A + S * s),
                        };
                        if (Math.Abs(dfl) > 1e-9 && Math.Sign(dfl) == Math.Sign(s))
                            cands.Add(Tuple.Create("scaled-default", (XYZ)null, A + (E0 - A) * (s / dfl)));
                        foreach (var cd in cands)
                        {
                            var l2 = g.GetLeader(de, view);
                            try
                            {
                                if (cd.Item2 != null) l2.Anchor = cd.Item2;
                                l2.Elbow = cd.Item3; l2.End = endT;
                                if (!g.IsLeaderValid(de, view, l2)) continue;
                                g.SetLeader(de, view, l2); shape = cd.Item1; break;
                            }
                            catch { }
                        }
                        if (shape == null) err = "no valid leader shape";
                        var chk = g.GetLeader(de, view);
                        if (chk != null) got = Math.Round((chk.End - endPt).DotProduct(S) / paper, 1);
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                if (pinned) g.Pinned = true;
                report.Add(new { Grid = g.Name, End = endW, Shape = shape, BubbleSideMm = got, Error = err });
            }
            if (mode == "apply" || mode == "remove") t.Commit(); else t.RollBack();
        }
        if (mode == "apply" && args["logPath"] != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName((string)args["logPath"]));
            File.WriteAllText((string)args["logPath"], new JObject { ["viewId"] = view.Id.IntegerValue, ["items"] = log }.ToString());
        }
        return new { Mode = mode, View = view.Name, BubbleMm = bubble, Items = report, OffRow = offRow.Count > 0 ? offRow : null };
    }
}
