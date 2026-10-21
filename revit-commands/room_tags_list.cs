/* mcp-tool
{
  "description": "Read-only: room tags in views: per view the tagged room numbers (sorted), and optionally each tag id, room id, type.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "detail": { "type": "boolean", "description": "also list tag id, room id, type per tag (default false)" }
    },
    "required": ["viewIds"]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomTagsList
{
    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        bool detail = args.Value<bool?>("detail") ?? false;
        return ((JArray)args["viewIds"]).Select(x =>
        {
            var v = doc.GetElement(new ElementId((int)x)) as View;
            if (v == null) return (object)new { ViewId = (int)x, Error = "not a view" };
            var tags = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType().OfType<RoomTag>().ToList();
            var rows = tags.Select(t =>
            {
                Room r = null; try { r = t.Room; } catch { }
                return new { Tag = t.Id.IntegerValue, Room = r?.Id.IntegerValue ?? -1, Number = r?.Number ?? "?", Type = doc.GetElement(t.GetTypeId())?.Name };
            }).ToList();
            return new
            {
                ViewId = v.Id.IntegerValue, View = v.Name, Count = rows.Count,
                Numbers = rows.Select(r => r.Number).Distinct().OrderBy(n => n).ToList(),
                Tags = detail ? rows : null
            };
        }).ToList();
    }
}
