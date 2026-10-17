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
      "gapMm": { "type": "number", "description": "paper mm between an elbowed bubble and its neighbour, default 2" },
      "touchMm": { "type": "number", "description": "pairs closer than bubble + touchMm overlap, default 0.5" },
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
//    along the view's up or right), the visible bubbles sorted across the group; pairs closer than bubble + touchMm (0.5) are
//    'Overlaps' (bubbles that only come close are left alone). For each pair the grid whose outer side (away from the other grid) has more room to its next
//    neighbour gets the elbow (but when one grid of the pair is already elbowed at its other end, that grid, if it
//    has room); offset = bubble + gap - spacing (coincident grids: bubble + gap). 'OffRow' lists ends
//    that are not on the row (most common end value) - put them back with align_grid_ends before the elbow.
//    'Tight' = the elbowed bubble would then touch its next neighbour too.
// preview / apply: items given, or (no items) the plan's proposals. Item: grid (exact name; without its invisible
//    characters only when that is unique), end 'low'|'high', toward 'auto'|'plus'|'minus' (plus = view right for grids running
//    up, view up for grids running right), offsetMm (paper mm sideways; default from the plan). Legacy: sideMm or
//    shiftMm = signed offset. The leader is built from Revit's default leader; several elbow shapes are tried until
//    IsLeaderValid accepts one (Revit's default shape first: line point at twice the knee depth) ('Shape' in the result); the bubble offset is read back after setting it.
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
    class Bub { public GI Gi; public string End; public DatumEnds De; public double Perp, Line, Along; public bool HasLeader; }

    static string Clean(string s) { return new string((s ?? "").Where(ch => !char.IsControl(ch) && char.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim(); }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var view = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        string mode = (string)args["mode"] ?? "plan";
        double paper = view.Scale / Ft; // paper mm -> model feet
        XYZ right = view.RightDirection, up = view.UpDirection, o = view.Origin;
        double gap = args["gapMm"] != null ? (double)args["gapMm"] : 2;
        double touch = args["touchMm"] != null ? (double)args["touchMm"] : 0.5;
        bool verbose = args.Value<bool?>("verbose") ?? false;
        var only = (args["only"] as JArray)?.Select(x => (string)x).ToList();

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
        double need = bubble + gap, hit = bubble + touch;
        // exact name first; the name without invisible characters only when unique ('C1' and '‭C1' can both exist)
        Func<string, GI> Find = n => gis.FirstOrDefault(x => x.Name == n) ?? (gis.Count(x => Clean(x.Name) == Clean(n)) == 1 ? gis.First(x => Clean(x.Name) == Clean(n)) : null);
        Func<GI, bool> InOnly = gi => only == null || only.Any(n => Find(n) == gi);

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
                XYZ at = ld != null ? ld.Anchor : p;   // a datum leader's Anchor is the bubble
                bubs.Add(new Bub { Gi = gi, End = end, De = de, Perp = C(at, gi.Side), Line = C(p, gi.Side), Along = C(p, gi.Axis), HasLeader = ld != null });
            }
        var leaders = bubs.Where(b => b.HasLeader).Select(b => b.Gi.Name + " " + b.End + " " + Math.Round(b.Perp - b.Line, 1) + "mm").ToList();

        // ---- plan
        var overlaps = new List<object>(); var offRow = new List<object>(); var proposals = new List<JObject>(); var tight = new List<string>();
        var taken = new HashSet<string>();
        foreach (var row in bubs.GroupBy(b => b.Gi.Group + "|" + b.End))
        {
            var list = row.OrderBy(b => b.Perp).ToList();
            double rowAlong = list.GroupBy(b => Math.Round(b.Along)).OrderByDescending(gp => gp.Count()).First().Key;
            foreach (var b in list) if (Math.Abs(b.Along - rowAlong) > 1) offRow.Add(new { Grid = b.Gi.Name, Row = row.Key, OffMm = Math.Round(b.Along - rowAlong, 1) });
            for (int i = 0; i + 1 < list.Count; i++)
            {
                Bub a = list[i], b = list[i + 1];
                double sp = b.Perp - a.Perp;
                if (sp >= hit || Math.Abs(a.Along - b.Along) >= hit) continue;
                if (!InOnly(a.Gi) && !InOnly(b.Gi)) continue;
                double roomA = i > 0 ? a.Perp - list[i - 1].Perp : 1e6;            // a moves minus
                double roomB = i + 2 < list.Count ? list[i + 2].Perp - b.Perp : 1e6; // b moves plus
                bool moveB = roomB >= roomA;
                var m = moveB ? b : a; double room = moveB ? roomB : roomA;
                // same pair already elbowed at the grids' other end -> elbow the same grid here too, when it has room
                Func<Bub, bool> OtherEndElbowed = x => bubs.Any(y => y.Gi == x.Gi && y.End != x.End && y.HasLeader);
                var pref = OtherEndElbowed(a) && !OtherEndElbowed(b) ? a : OtherEndElbowed(b) && !OtherEndElbowed(a) ? b : null;
                if (pref != null && pref != m && (pref == b ? roomB : roomA) - (need - sp) >= hit) { moveB = pref == b; m = pref; room = moveB ? roomB : roomA; }
                if (!InOnly(m.Gi)) { m = moveB ? a : b; moveB = !moveB; room = moveB ? roomB : roomA; }
                var other = moveB ? a : b;
                // bubble goes to the other bubble +/- (bubble + gap); offset measured from its own grid line
                double s = (moveB ? other.Perp + need : other.Perp - need) - m.Line;
                double off = Math.Round(Math.Abs(s), 1); string tw = s >= 0 ? "plus" : "minus";
                string key = m.Gi.Name + "|" + m.End;
                overlaps.Add(new { Row = row.Key, A = a.Gi.Name, B = b.Gi.Name, SpacingMm = Math.Round(sp, 1), Elbow = m.Gi.Name, Toward = tw, OffsetMm = off, RoomMm = room > 1e5 ? (double?)null : Math.Round(room, 1),
                    Note = other.HasLeader ? other.Gi.Name + " has an elbow that does not clear: remove it (Remove Elbow grip)" : m.HasLeader ? "re-set the elbow" : null });
                if (room < 1e5 && room - (need - sp) < hit) tight.Add(m.Gi.Name + " " + m.End);
                if (taken.Add(key)) proposals.Add(new JObject { ["grid"] = m.Gi.Name, ["end"] = m.End, ["toward"] = tw, ["offsetMm"] = off });
            }
        }
        if (mode == "plan")
            return new { Mode = mode, View = view.Name, BubbleMm = bubble, OverlapUnderMm = hit, ElbowToMm = need, Overlaps = overlaps, Proposed = proposals, OffRow = offRow, Tight = tight, Elbows = leaders, Skipped = skipped };

        // ---- preview / apply / remove
        var items = (args["items"] as JArray)?.Cast<JObject>().ToList() ?? proposals;
        var report = new List<object>(); var log = new JArray();
        using (var t = new Transaction(doc, mode == "remove" ? "Grid bubble elbows off" : "Grid bubble elbows"))
        {
            t.Start();
            foreach (var it in items)
            {
                string name = (string)it["grid"], endW = (string)it["end"] ?? "low";
                var gi = Find(name);
                if (gi == null) { report.Add(new { Grid = name, Error = "not a straight grid in view" }); continue; }
                var g = gi.G;
                bool end0Low = gi.P0.DotProduct(gi.Axis) < gi.P1.DotProduct(gi.Axis);
                DatumEnds de = (endW == "high") ^ end0Low ? DatumEnds.End0 : DatumEnds.End1;
                XYZ endPt = de == DatumEnds.End0 ? gi.P0 : gi.P1;
                XYZ outward = endW == "high" ? gi.Axis : gi.Axis.Negate();
                bool pinned = g.Pinned; if (pinned) g.Pinned = false;
                string err = null, shape = null, dbg = null; double? got = null;
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
                                var pr = proposals.FirstOrDefault(p => Find((string)p["grid"]) == gi &&(string)p["end"] == endW);
                                tw = pr != null ? (string)pr["toward"] : "plus";
                                if (pr != null && it["offsetMm"] == null) off = (double)pr["offsetMm"];
                            }
                            s = (tw == "minus" ? -1 : 1) * off * paper;
                        }
                        bool had = g.GetLeader(de, view) != null;
                        log.Add(new JObject { ["id"] = g.Id.IntegerValue, ["grid"] = g.Name, ["end"] = endW, ["hadLeader"] = had });
                        if (!had) { g.AddLeader(de, view); doc.Regenerate(); }   // read the new leader only after a regenerate
                        var ld = g.GetLeader(de, view);
                        // Revit's datum leader: Anchor = the bubble (read-only, follows the elbow sideways), Elbow = the
                        // knee, End = where the leader leaves the grid line. Default: End 2 x elbow depth inside the end.
                        XYZ A = ld.Anchor, E0 = ld.Elbow, N0 = ld.End, S = gi.Side;
                        double eIn = Math.Max(Math.Abs((E0 - endPt).DotProduct(outward)), 2 * paper);   // default elbow depth inside the end
                        double nIn = Math.Abs((N0 - endPt).DotProduct(outward));
                        XYZ vd = view.ViewDirection;
                        Func<XYZ, XYZ> Pl = q => q - vd * (q - endPt).DotProduct(vd);   // keep every point in the grid's plane
                        // Revit 2023: a leader read right after AddLeader (no regenerate) is offset by its default shape;
                        // after a regenerate the points are absolute. The read-back corrects what is left.
                        Func<double> Side = () => (g.GetLeader(de, view).Anchor - endPt).DotProduct(S);
                        double k = s;
                        Func<string, double, bool> TrySet = (nm, kk) =>
                        {
                            XYZ knee = Pl(endPt - outward * eIn + S * kk);
                            XYZ lineEnd = nm == "45" ? Pl(endPt - outward * (eIn + Math.Abs(kk))) : nm == "default-end" ? Pl(endPt - outward * Math.Max(nIn, eIn + paper)) : Pl(endPt - outward * (2 * eIn));
                            var l2 = g.GetLeader(de, view);
                            try
                            {
                                l2.Elbow = knee; l2.End = lineEnd;
                                if (!g.IsLeaderValid(de, view, l2)) { dbg += nm + ":invalid "; return false; }
                                g.SetLeader(de, view, l2); doc.Regenerate(); return true;
                            }
                            catch (Exception ex) { dbg += nm + ":" + ex.GetType().Name + " "; return false; }
                        };
                        Func<XYZ, string> Rel = q => Math.Round((q - endPt).DotProduct(outward) / paper, 1) + "/" + Math.Round((q - endPt).DotProduct(S) / paper, 1);
                        dbg = "default anchor/elbow/end (out/side mm): " + Rel(A) + " " + Rel(E0) + " " + Rel(N0) + "; ";
                        foreach (var nm in new[] { "double-depth", "default-end", "45" })   // Revit's own shape first (line point = 2 x knee depth, as Add Elbow draws it)
                        {
                            if (!TrySet(nm, k)) continue;
                            shape = nm;
                            for (int it2 = 0; it2 < 3 && Math.Abs(Side() - s) > 0.2 * paper; it2++) { k += s - Side(); if (!TrySet(nm, k)) break; }
                            break;
                        }
                        if (shape == null) err = "no valid leader shape";
                        else if (Math.Abs(Side() - s) > 0.5 * paper) err = "bubble not where asked";
                        var chk = g.GetLeader(de, view);
                        if (chk != null) { got = Math.Round((chk.Anchor - endPt).DotProduct(S) / paper, 1); if (verbose) dbg += "set: " + Rel(chk.Anchor) + " " + Rel(chk.Elbow) + " " + Rel(chk.End); }
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                if (pinned) g.Pinned = true;
                report.Add(new { Grid = g.Name, End = endW, Shape = shape, BubbleSideMm = got, Error = err, Debug = (shape == null || verbose) ? dbg : null });
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
