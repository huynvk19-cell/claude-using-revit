/* mcp-tool
{
  "description": "Read-only: every linear dim of ONE view in the view frame: lines, witness positions, values, texts, references.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "outPath": {
        "type": "string"
      }
    },
    "required": [
      "viewId"
    ]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: every linear dimension of ONE view in the view frame (mm Right / Up from View.Origin, as
//    stair_plan_audit): id, type, measured direction, line position, witness positions, segment values with prefix /
//    suffix / below, references (element + category). outPath writes the JSON. Use to learn how the user adjusted
//    dims (snapshot before / after).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ViewDimsSnapshot
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        Func<XYZ, double> vx = p => Math.Round((p - O).DotProduct(R) * MM, 1), vy = p => Math.Round((p - O).DotProduct(U) * MM, 1);
        var rows = new List<object>();
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension || d.DimensionShape != DimensionShape.Linear || !(d.Curve is Line l)) continue;
            bool right = Math.Abs(l.Direction.DotProduct(R)) > 0.99;
            Func<XYZ, double> along = right ? vx : vy, across = right ? vy : vx;
            var segs = new List<object>(); var wit = new List<double>();
            if (d.NumberOfSegments > 0)
            {
                var ss = d.Segments.Cast<DimensionSegment>().ToList();
                wit.Add(along(ss[0].Origin - l.Direction * ((ss[0].Value ?? 0) / 2)));
                foreach (var s in ss) { wit.Add(along(s.Origin + l.Direction * ((s.Value ?? 0) / 2))); segs.Add(new { Value = Math.Round((s.Value ?? 0) * MM), s.Prefix, s.Suffix, s.Below }); }
            }
            else
            {
                wit.Add(along(d.Origin - l.Direction * ((d.Value ?? 0) / 2))); wit.Add(along(d.Origin + l.Direction * ((d.Value ?? 0) / 2)));
                segs.Add(new { Value = Math.Round((d.Value ?? 0) * MM), d.Prefix, d.Suffix, d.Below });
            }
            var refs = new List<string>();
            foreach (Reference r in d.References) { var e = doc.GetElement(r.ElementId); refs.Add((e?.Category?.Name ?? "?") + " " + r.ElementId.IntegerValue); }
            rows.Add(new { Id = d.Id.IntegerValue, Type = doc.GetElement(d.GetTypeId())?.Name, Measures = right ? "right" : "up", LineAt = across(l.Origin), Witness = wit.OrderBy(x => x).ToList(), Segments = segs, Refs = refs });
        }
        string outPath = (string)args["outPath"];
        if (!string.IsNullOrEmpty(outPath)) System.IO.File.WriteAllText(outPath, JsonConvert.SerializeObject(rows, Formatting.Indented));
        return new { View = v.Name, Count = rows.Count, Dims = rows };
    }
}
