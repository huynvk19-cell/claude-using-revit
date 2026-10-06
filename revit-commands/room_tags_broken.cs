/* mcp-tool
{
  "description": "Find room tags showing '?' (no room, or the room's Name/Number is empty) in given views; mode preview lists them (view, tag, room id/number/name/level, tag position); apply colours them red with a view override (annotation only, the rooms are not changed) and logs the previous overrides to logPath; undo restores.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode", "logPath"]
  },
  "timeoutSeconds": 180,
  "readOnly": false
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class RoomTagsBroken
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document; string mode = (string)args["mode"], log = (string)args["logPath"];
        if (mode == "undo")
        {
            var items = JArray.Parse(File.ReadAllText(log)); int n = 0;
            using (var t = new Transaction(doc, "Room tag colours restore"))
            {
                t.Start();
                foreach (var it in items) { ((View)doc.GetElement(new ElementId((int)it["View"]))).SetElementOverrides(new ElementId((int)it["Tag"]), new OverrideGraphicSettings()); n++; }
                t.Commit();
            }
            return new { Restored = n };
        }
        var rows = new List<object>(); var logRows = new List<object>(); var okTypes = new Dictionary<string, int>();
        var red = new OverrideGraphicSettings().SetProjectionLineColor(new Color(255, 0, 0)).SetProjectionLineWeight(5);
        using (var t = new Transaction(doc, "Colour broken room tags"))
        {
            t.Start();
            foreach (var vid in ((JArray)args["viewIds"]).Select(x => (int)x))
            {
                var v = (View)doc.GetElement(new ElementId(vid));
                foreach (RoomTag tag in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().Cast<RoomTag>())
                {
                    Room room = null; try { room = tag.Room; } catch { }
                    string text = null; try { text = tag.TagText; } catch { }
                    string name = room?.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(), num = room?.Number;
                    okTypes[doc.GetElement(tag.GetTypeId())?.Name ?? "?"] = (okTypes.TryGetValue(doc.GetElement(tag.GetTypeId())?.Name ?? "?", out var c0) ? c0 : 0) + 1;
                    bool broken = room == null || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(num) || (text != null && text.Contains("?"));
                    if (!broken) continue;
                    var p = tag.TagHeadPosition;
                    rows.Add(new { View = v.Name, Tag = tag.Id.IntegerValue, TagType = doc.GetElement(tag.GetTypeId())?.Name, Text = text, Room = room?.Id.IntegerValue, Number = num, Name = name, Level = room?.Level?.Name, Area = room == null ? 0 : Math.Round(room.Area * 0.092903, 1), XY = Math.Round(p.X * MM) + "," + Math.Round(p.Y * MM) });
                    logRows.Add(new { View = vid, Tag = tag.Id.IntegerValue });
                    if (mode == "apply") v.SetElementOverrides(tag.Id, red);
                }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(log, JsonConvert.SerializeObject(logRows, Formatting.Indented)); } else t.RollBack();
        }
        return new { mode, Count = rows.Count, TagTypesInViews = okTypes, Rows = rows.Select(r => new { ((dynamic)r).Tag, ((dynamic)r).TagType, ((dynamic)r).Number }) };
    }
}
