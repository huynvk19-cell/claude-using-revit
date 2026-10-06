/* mcp-tool
{
  "description": "Add an elbow (leader) to grid bubbles in one view so bubbles of close grids no longer overlap. For each item {grid, end: 'low'|'high' (along the view's up direction for grids running up, along right for grids running right), shiftMm (paper mm, + = toward the view's right/up), anchorMm, elbowMm (paper mm measured inward from the grid end; defaults 8 and 4)}. The bubble keeps its distance from the building and is moved sideways. Modes: preview (rolled back) | apply (writes logPath). Leaders cannot be removed through the API: undo with Ctrl+Z in Revit or the grid's Remove Elbow grip.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" } },
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "logPath": { "type": "string" }
    },
    "required": ["viewId", "items", "mode"]
  },
  "timeoutSeconds": 120
}
*/
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

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var view = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        string mode = (string)args["mode"] ?? "preview";
        double paper = view.Scale / Ft; // paper mm -> feet in model
        XYZ right = view.RightDirection, up = view.UpDirection;
        var grids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>().ToDictionary(g => g.Name);
        var report = new List<object>();

        using (var t = new Transaction(doc, "Grid bubble elbows"))
        {
            t.Start();
            foreach (JObject it in (JArray)args["items"])
            {
                string name = (string)it["grid"];
                if (!grids.ContainsKey(name)) { report.Add(new { Grid = name, Error = "not visible in view" }); continue; }
                var g = grids[name];
                bool wasPinned = g.Pinned; if (wasPinned) g.Pinned = false;
                var curves = g.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                if (curves.Count == 0) curves = g.GetCurvesInView(DatumExtentType.Model, view);
                var c = curves[0];
                XYZ p0 = c.GetEndPoint(0), p1 = c.GetEndPoint(1);
                XYZ dir = (p1 - p0).Normalize();
                bool runsUp = Math.Abs(dir.DotProduct(up)) > Math.Abs(dir.DotProduct(right));
                XYZ axis = runsUp ? up : right, side = runsUp ? right : up;
                bool wantHigh = ((string)it["end"] ?? "low") == "high";
                // pick the end whose coordinate along axis is low/high
                bool end0IsLow = p0.DotProduct(axis) < p1.DotProduct(axis);
                DatumEnds de = (wantHigh ^ end0IsLow) ? DatumEnds.End0 : DatumEnds.End1;
                XYZ endPt = de == DatumEnds.End0 ? p0 : p1;
                XYZ outward = wantHigh ? axis : axis.Negate();

                double shift = (it["shiftMm"] != null ? (double)it["shiftMm"] : 0) * paper;
                double a = (it["anchorMm"] != null ? (double)it["anchorMm"] : 8) * paper;
                double b = (it["elbowMm"] != null ? (double)it["elbowMm"] : 4) * paper;

                if (g.GetLeader(de, view) == null) g.AddLeader(de, view);
                var ld = g.GetLeader(de, view);
                Func<XYZ, double[]> rel = p => new[] { Math.Round((p - endPt).DotProduct(outward) / paper, 2), Math.Round((p - endPt).DotProduct(side) / paper, 2) };
                var dflt = new { Anchor = rel(ld.Anchor), Elbow = rel(ld.Elbow), End = rel(ld.End) };
                string err = null;
                if (it["sideMm"] != null)
                {   // absolute: bubble 'sideMm' paper mm from its grid line (+ = view right for grids running up, view up for grids running right), same distance along the grid
                    double s = (double)it["sideMm"] * paper;
                    XYZ A = ld.Anchor, N0 = ld.End;
                    XYZ sd = runsUp ? right : up;
                    double alongN = (N0 - A).DotProduct(dir);
                    XYZ L = A + dir * alongN;
                    ld.Elbow = A + sd * (s / 2);
                    ld.End = L + sd * s;
                    if (g.IsLeaderValid(de, view, ld)) g.SetLeader(de, view, ld); else err = "side leader not valid";
                    if (wasPinned) g.Pinned = true;
                    var chk = g.GetLeader(de, view);
                    report.Add(new { Grid = name, End = de.ToString(), BubbleSidePaperMm = Math.Round((chk.End - chk.Anchor).DotProduct(sd) / paper, 2), Error = err });
                    continue;
                }
                if (it["scaleDefault"] != null)
                {   // keep Revit's default elbow shape, scale its sideways offset (e.g. 0.3 -> bubble ~3.8 mm aside instead of 12.7 mm)
                    double k = (double)it["scaleDefault"];
                    XYZ A = ld.Anchor, E0 = ld.Elbow, N0 = ld.End;
                    ld.Elbow = A + (E0 - A) * k;
                    ld.End = endPt + (N0 - endPt) * k;
                    if (g.IsLeaderValid(de, view, ld)) g.SetLeader(de, view, ld); else err = "scaled leader not valid";
                    if (wasPinned) g.Pinned = true;
                    report.Add(new { Grid = name, End = de.ToString(), ShiftPaperMm = Math.Round((ld.End - endPt).GetLength() / paper, 2), Error = err, DefaultLeaderPaperMm_OutSide = dflt });
                    continue;
                }
                // Anchor is placed by Revit on the datum line; only the elbow and the end can be set.
                ld.Elbow = endPt - outward * b + side * shift;
                ld.End = endPt + side * shift;
                if (it["shiftMm"] == null || it["useDefault"] != null && (bool)it["useDefault"]) { /* keep Revit's default elbow (bubble shifted by its default offset) */ }
                else if (g.IsLeaderValid(de, view, ld)) g.SetLeader(de, view, ld); else err = "leader not valid";
                if (wasPinned) g.Pinned = true;
                report.Add(new { Grid = name, End = de.ToString(), ShiftPaperMm = shift / paper, Error = err, DefaultLeaderPaperMm_OutSide = dflt });
            }
            if (mode == "apply") t.Commit(); else t.RollBack();
        }
        if (mode == "apply" && args["logPath"] != null) File.WriteAllText((string)args["logPath"], JArray.FromObject(report).ToString());
        return new { Mode = mode, View = view.Name, Items = report };
    }
}
