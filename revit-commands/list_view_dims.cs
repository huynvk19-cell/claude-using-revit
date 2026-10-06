/* mcp-tool
{
  "description": "Read-only: list linear dimensions in a view, optionally only those of a given dimension type or referencing a given element: id, type, direction, line position (mm, along the view or height), values.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "typeName": { "type": "string" },
      "elementId": { "type": "number" },
      "ids": { "type": "array", "items": { "type": "number" } }
    },
    "required": ["viewId"]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ListViewDims
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId(args.Value<int>("viewId")));
        string tn = args.Value<string>("typeName"); int? el = args.Value<int?>("elementId");
        var only = (args["ids"] as JArray)?.Select(t => (int)t).ToList();
        XYZ o = v.Origin, r = v.RightDirection;
        return new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(d => d.GetType() == typeof(Dimension) && d.Curve is Line)
            .Where(d => tn == null || d.DimensionType.Name == tn)
            .Where(d => only == null || only.Contains(d.Id.IntegerValue))
            .Where(d => el == null || d.References.Cast<Reference>().Any(rf => rf.ElementId.IntegerValue == el.Value))
            .Select(d =>
            {
                var ln = (Line)d.Curve; bool vert = Math.Abs(ln.Direction.Z) > 0.99;
                var vals = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList() : new System.Collections.Generic.List<double> { Math.Round((d.Value ?? 0) * MM) };
                return new { Id = d.Id.IntegerValue, Type = d.DimensionType.Name, Dir = vert ? "V" : "H", Line = Math.Round(vert ? (ln.Origin - o).DotProduct(r) * MM : ln.Origin.Z * MM), Values = string.Join("|", vals.Count > 12 ? vals.Take(12) : vals) + (vals.Count > 12 ? "|…(" + vals.Count + ")" : "") };
            }).ToList();
    }
}
