/* mcp-tool
{
  "description": "Room tags whose head lies outside their own room in plan views. mode preview lists them (view, tag id, room number/name, distance outside, leader); apply moves the selected tags (ids, or all listed) to the room's location point (leader off) and writes logPath with the old positions; undo restores from logPath.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "tagIds": { "type": "array", "items": { "type": "number" }, "description": "apply only to these tags" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode", "logPath"]
  },
  "timeoutSeconds": 300,
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

public static class RoomTagsOutside
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], log = (string)args["logPath"];
        if (mode == "undo")
        {
            var items = JArray.Parse(File.ReadAllText(log)); int n = 0;
            using (var t = new Transaction(doc, "Room tags restore"))
            {
                t.Start();
                foreach (var it in items)
                {
                    var tag = doc.GetElement(new ElementId((int)it["Tag"])) as RoomTag; if (tag == null) continue;
                    tag.HasLeader = (bool)it["Leader"];
                    var p = new XYZ((double)it["X"], (double)it["Y"], (double)it["Z"]);
                    tag.Location.Move(p - ((LocationPoint)tag.Location).Point); n++;
                }
                t.Commit();
            }
            return new { Restored = n };
        }
        var only = (args["tagIds"] as JArray)?.Select(x => (int)x).ToList();
        var rows = new List<object>(); var logRows = new List<object>();
        using (var t = new Transaction(doc, "Room tags into rooms"))
        {
            t.Start();
            foreach (var vid in ((JArray)args["viewIds"]).Select(x => (int)x))
            {
                var v = (View)doc.GetElement(new ElementId(vid));
                foreach (RoomTag tag in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType().Cast<RoomTag>())
                {
                    Room room = null; try { room = tag.Room; } catch { }
                    if (room == null || room.Location == null) continue; // linked-room tags / orphans skipped
                    var head = tag.TagHeadPosition;
                    double z = (room.Level?.Elevation ?? 0) + 1.0 / MM * 500;
                    var hp = new XYZ(head.X, head.Y, z);
                    if (room.IsPointInRoom(hp)) continue;
                    var rp = ((LocationPoint)room.Location).Point;
                    double dist = new XYZ(head.X - rp.X, head.Y - rp.Y, 0).GetLength() * MM;
                    bool sel = only == null || only.Contains(tag.Id.IntegerValue);
                    rows.Add(new { View = v.Name, Tag = tag.Id.IntegerValue, Room = room.Number + " " + room.Name, AreaM2 = Math.Round(room.Area * 0.092903, 1), Leader = tag.HasLeader, DistToRoomPointMm = Math.Round(dist), Selected = sel });
                    if (mode == "apply" && sel)
                    {
                        var lp = (LocationPoint)tag.Location;
                        logRows.Add(new { Tag = tag.Id.IntegerValue, Leader = tag.HasLeader, X = lp.Point.X, Y = lp.Point.Y, Z = lp.Point.Z });
                        tag.HasLeader = false;
                        tag.Location.Move(new XYZ(rp.X - lp.Point.X, rp.Y - lp.Point.Y, 0));
                    }
                }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(log, JsonConvert.SerializeObject(logRows, Formatting.Indented)); } else t.RollBack();
        }
        return new { mode, Count = rows.Count, Rows = rows };
    }
}
