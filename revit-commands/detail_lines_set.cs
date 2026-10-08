/* mcp-tool
{
  "description": "ONE view: set the end points of existing detail lines (view frame mm), e.g. to fit a void X to the opening. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" } },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 60
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ONE plan/section view: move the two ends of existing straight detail lines. items: {id, start:[right, up], end:[right, up]}
//    in view frame mm (as view_detail_curves / view_elem_boxes ViewBox). The line keeps its style and its plane (Z).
//    mode preview (rolled back) | apply (logPath: old ends, for undo) | undo (logPath: puts the old ends back).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class DetailLinesSet
{
    const double MM = 304.8;

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        JArray items;
        if (mode == "undo") items = JArray.Parse(File.ReadAllText(logPath));
        else items = (JArray)args["items"] ?? new JArray();
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };

        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        Func<JToken, double, XYZ> P = (t, z) =>
        {
            var p = O + R * ((double)t[0] / MM) + U * ((double)t[1] / MM);
            return new XYZ(p.X, p.Y, z);
        };
        Func<XYZ, double[]> F = p => new[] { Math.Round((p - O).DotProduct(R) * MM, 1), Math.Round((p - O).DotProduct(U) * MM, 1) };

        var done = new List<object>(); var errors = new List<string>(); var old = new JArray();
        using (var t = new Transaction(doc, "Detail lines set"))
        {
            t.Start();
            foreach (var it in items)
            {
                int id = (int)it["id"];
                var dc = doc.GetElement(new ElementId(id)) as DetailCurve;
                if (dc == null || !(dc.GeometryCurve is Line ln)) { errors.Add($"{id}: not a straight detail line"); continue; }
                double z = ln.GetEndPoint(0).Z;
                old.Add(new JObject { ["id"] = id, ["start"] = new JArray(F(ln.GetEndPoint(0))), ["end"] = new JArray(F(ln.GetEndPoint(1))) });
                try
                {
                    dc.SetGeometryCurve(Line.CreateBound(P(it["start"], z), P(it["end"], z)), true);
                    var nl = (Line)dc.GeometryCurve;
                    done.Add(new { Id = id, Before = new { Start = F(ln.GetEndPoint(0)), End = F(ln.GetEndPoint(1)) }, After = new { Start = F(nl.GetEndPoint(0)), End = F(nl.GetEndPoint(1)) } });
                }
                catch (Exception ex) { errors.Add($"{id}: {ex.Message}"); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, old.ToString()); }
            else if (mode == "undo") t.Commit();
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, Done = done, Errors = errors };
    }
}
