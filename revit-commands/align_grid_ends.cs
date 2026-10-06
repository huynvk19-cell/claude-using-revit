/* mcp-tool
{
  "description": "Line up the 2D ends of straight host grids in a view. Grids are grouped by direction (along the view's up direction = 'vertical', along right = 'horizontal'); each side of a group goes to a target coordinate (default: the most common current value, or given in mm). Ends are switched to 2D (ViewSpecific) in this view only; pinned grids are unpinned and re-pinned. Modes: preview, apply, undo (from log).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "viewId": { "type": "number", "description": "default: active view" },
      "targets": { "type": "object", "description": "optional { vertical: { low, high }, horizontal: { low, high } } in mm along the view's up / right direction from the view origin" },
      "only": { "type": "array", "items": { "type": "string" }, "description": "optional grid names to change (others still count for the default target)" },
      "logPath": { "type": "string" }
    },
    "required": ["mode", "logPath"]
  },
  "timeoutSeconds": 180
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class AlignGridEnds
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var uidoc = app.ActiveUIDocument; var doc = uidoc.Document;
        string mode = (string)args["mode"], logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var log = JObject.Parse(File.ReadAllText(logPath));
            var v0 = (View)doc.GetElement(new ElementId((int)log["viewId"]));
            int n = 0;
            using (var t = new Transaction(doc, "Restore grid ends"))
            {
                t.Start();
                foreach (JObject g in (JArray)log["grids"])
                {
                    var gr = (Grid)doc.GetElement(new ElementId((int)g["id"]));
                    bool pin = gr.Pinned; if (pin) gr.Pinned = false;
                    var c = gr.GetCurvesInView(DatumExtentType.ViewSpecific, v0).First();
                    var p0 = new XYZ((double)g["x0"], (double)g["y0"], c.GetEndPoint(0).Z); var p1 = new XYZ((double)g["x1"], (double)g["y1"], c.GetEndPoint(1).Z);
                    gr.SetCurveInView(DatumExtentType.ViewSpecific, v0, Line.CreateBound(p0, p1));
                    if ((string)g["e0"] == "Model") gr.SetDatumExtentType(DatumEnds.End0, v0, DatumExtentType.Model);
                    if ((string)g["e1"] == "Model") gr.SetDatumExtentType(DatumEnds.End1, v0, DatumExtentType.Model);
                    if (pin) gr.Pinned = true; n++;
                }
                t.Commit();
            }
            return new { Restored = n };
        }

        var v = args["viewId"] != null ? (View)doc.GetElement(new ElementId((int)args["viewId"])) : uidoc.ActiveView;
        XYZ o = v.Origin, r = v.RightDirection, u = v.UpDirection;
        Func<XYZ, double> R = p => (p - o).DotProduct(r) * MM; Func<XYZ, double> U = p => (p - o).DotProduct(u) * MM;
        var only = (args["only"] as JArray)?.Select(x => (string)x).ToList();
        var rows = new List<Tuple<Grid, string, double, double, bool>>(); // grid, group, low, high, flipped(end0 is high)
        var skipped = new List<string>();
        foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
            if (!(c is Line ln)) { skipped.Add(g.Name + " (not straight)"); continue; }
            var d = ln.Direction;
            double dr = Math.Abs(d.DotProduct(r)), du = Math.Abs(d.DotProduct(u));
            XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
            if (du > 0.999) rows.Add(Tuple.Create(g, "vertical", Math.Min(U(a), U(b)), Math.Max(U(a), U(b)), U(a) > U(b)));
            else if (dr > 0.999) rows.Add(Tuple.Create(g, "horizontal", Math.Min(R(a), R(b)), Math.Max(R(a), R(b)), R(a) > R(b)));
            else skipped.Add(g.Name + " (skewed)");
        }
        Func<IEnumerable<double>, double> Mode = vals => vals.GroupBy(x => Math.Round(x)).OrderByDescending(gp => gp.Count()).ThenBy(gp => gp.Key).First().Key;
        var targets = args["targets"] as JObject;
        var plan = new Dictionary<string, double[]>();
        foreach (var grp in rows.GroupBy(x => x.Item2))
        {
            double lo = Mode(grp.Select(x => x.Item3)), hi = Mode(grp.Select(x => x.Item4));
            var tj = targets?[grp.Key] as JObject;
            if (tj?["low"] != null) lo = (double)tj["low"]; if (tj?["high"] != null) hi = (double)tj["high"];
            plan[grp.Key] = new[] { lo, hi };
        }
        var report = new List<object>(); var log2 = new JArray();
        using (var t = new Transaction(doc, "Align grid ends"))
        {
            t.Start();
            foreach (var x in rows.OrderBy(x => x.Item2).ThenBy(x => x.Item1.Name))
            {
                var g = x.Item1; var tg = plan[x.Item2];
                bool change = (only == null || only.Contains(g.Name)) && (Math.Abs(x.Item3 - tg[0]) > 0.5 || Math.Abs(x.Item4 - tg[1]) > 0.5);
                report.Add(new { Grid = g.Name, Group = x.Item2, Low = Math.Round(x.Item3), High = Math.Round(x.Item4), Target = Math.Round(tg[0]) + " .. " + Math.Round(tg[1]), Change = change,
                    Ext = g.GetDatumExtentTypeInView(DatumEnds.End0, v) + "/" + g.GetDatumExtentTypeInView(DatumEnds.End1, v) });
                if (!change || mode != "apply") continue;
                var c = (Line)g.GetCurvesInView(DatumExtentType.ViewSpecific, v).First();
                log2.Add(new JObject { ["id"] = g.Id.IntegerValue, ["x0"] = c.GetEndPoint(0).X, ["y0"] = c.GetEndPoint(0).Y, ["x1"] = c.GetEndPoint(1).X, ["y1"] = c.GetEndPoint(1).Y,
                    ["e0"] = g.GetDatumExtentTypeInView(DatumEnds.End0, v).ToString(), ["e1"] = g.GetDatumExtentTypeInView(DatumEnds.End1, v).ToString() });
                bool pin = g.Pinned; if (pin) g.Pinned = false;
                if (g.GetDatumExtentTypeInView(DatumEnds.End0, v) != DatumExtentType.ViewSpecific) g.SetDatumExtentType(DatumEnds.End0, v, DatumExtentType.ViewSpecific);
                if (g.GetDatumExtentTypeInView(DatumEnds.End1, v) != DatumExtentType.ViewSpecific) g.SetDatumExtentType(DatumEnds.End1, v, DatumExtentType.ViewSpecific);
                c = (Line)g.GetCurvesInView(DatumExtentType.ViewSpecific, v).First();
                XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1);
                XYZ axis = x.Item2 == "vertical" ? u : r;
                Func<XYZ, double, XYZ> At = (p, val) => { double cur = (p - o).DotProduct(axis) * MM; return p + axis * ((val - cur) / MM); };
                XYZ na = At(a, x.Item5 ? tg[1] : tg[0]), nb = At(b, x.Item5 ? tg[0] : tg[1]);
                g.SetCurveInView(DatumExtentType.ViewSpecific, v, Line.CreateBound(na, nb));
                if (pin) g.Pinned = true;
            }
            if (mode == "apply") t.Commit(); else t.RollBack();
        }
        if (mode == "apply") { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.WriteAllText(logPath, new JObject { ["viewId"] = v.Id.IntegerValue, ["grids"] = log2 }.ToString()); }
        if (args.Value<bool?>("compact") ?? false) return new { View = v.Name, Targets = plan.ToDictionary(k => k.Key, k => Math.Round(k.Value[0]) + " .. " + Math.Round(k.Value[1])), Changed = report.Count(r => (bool)((dynamic)r).Change), Skipped = skipped };
        return new { View = v.Name, ViewId = v.Id.IntegerValue, Targets = plan.ToDictionary(k => k.Key, k => Math.Round(k.Value[0]) + " .. " + Math.Round(k.Value[1])), Skipped = skipped, Grids = report };
    }
}
