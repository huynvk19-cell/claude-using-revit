/* mcp-tool
{
  "description": "Read-only: in the views placed on sheets (sheet number prefix), list annotations (dimensions, spot dimensions, tags, text notes) that carry an Override Graphics in View with a line colour, e.g. review colouring left behind. Returns id, kind, view, colour.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sheetPrefix": { "type": "string" },
      "limit": { "type": "number", "description": "max items listed (default 200)" }
    },
    "required": ["sheetPrefix"]
  },
  "timeoutSeconds": 600,
  "readOnly": true
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class AnnotationOverrideScan
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string prefix = (string)args["sheetPrefix"]; int limit = args.Value<int?>("limit") ?? 200;
        var views = new Dictionary<int, View>();
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => s.SheetNumber.StartsWith(prefix)))
            foreach (var id in s.GetAllPlacedViews()) { var v = (View)doc.GetElement(id); if (v.ViewType != ViewType.Legend && v.ViewType != ViewType.Schedule) views[id.IntegerValue] = v; }
        var items = new List<object>(); int total = 0;
        foreach (var v in views.Values)
        {
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
            {
                var cat = e.Category; if (cat == null || cat.CategoryType != CategoryType.Annotation) continue;
                bool isTag = e is IndependentTag || e is SpatialElementTag || cat.Name.EndsWith("Tags");
                if (!(e is Dimension) && !isTag && !(e is TextNote)) continue;
                OverrideGraphicSettings o; try { o = v.GetElementOverrides(e.Id); } catch { continue; }
                var c = o.ProjectionLineColor;
                if (c == null || !c.IsValid) continue;
                total++;
                if (items.Count < limit) items.Add(new { Id = e.Id.IntegerValue, Kind = e is Dimension ? "dim" : isTag ? "tag" : "text", Category = cat.Name, View = v.Name, ViewId = v.Id.IntegerValue, Rgb = c.Red + "," + c.Green + "," + c.Blue });
            }
        }
        return new { Views = views.Count, Coloured = total, Items = items };
    }
}
