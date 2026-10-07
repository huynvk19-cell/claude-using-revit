/* mcp-tool
{
  "description": "Read-only: for element ids and views, whether each element is in the view, its tag text, and its position (mm).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "ids": {
        "type": "array",
        "items": {
          "type": "number"
        }
      }
    },
    "required": [
      "viewIds",
      "ids"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: for given element ids and views, tell whether each element is collected in the view and whether a tag in
//    that view points at it (tag text), plus its horizontal position in the view (mm) for locating it on an exported
//    image.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class OpeningTagsInViews
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var ids = ((JArray)args["ids"]).Select(t => (int)t).ToList();
        var res = new List<object>();
        foreach (var vid in ((JArray)args["viewIds"]).Select(t => new ElementId((int)t)))
        {
            var v = (View)doc.GetElement(vid);
            var inView = new HashSet<int>(new FilteredElementCollector(doc, vid).WhereElementIsNotElementType().ToElementIds().Select(i => i.IntegerValue));
            var tags = new FilteredElementCollector(doc, vid).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
            foreach (var id in ids)
            {
                var e = doc.GetElement(new ElementId(id)); if (e == null) continue;
                var tg = tags.Where(t => t.GetTaggedLocalElementIds().Any(x => x.IntegerValue == id)).Select(t => t.TagText).ToList();
                var bb = e.get_BoundingBox(null);
                double x = bb == null ? 0 : (((bb.Min + bb.Max) / 2) - v.Origin).DotProduct(v.RightDirection) * MM;
                double depth = bb == null ? 0 : (((bb.Min + bb.Max) / 2) - v.Origin).DotProduct(v.ViewDirection) * MM;
                res.Add(new { View = v.Name, Id = id, Mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), InView = inView.Contains(id), Tags = string.Join(",", tg), X = Math.Round(x), Depth = Math.Round(depth) });
            }
        }
        return res;
    }
}
