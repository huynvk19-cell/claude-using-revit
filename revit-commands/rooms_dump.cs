/* mcp-tool
{
  "description": "Read-only: rooms (id, number, name, level, area, centre) of the model or of one plan view.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number", "description": "Only rooms of this plan view's level, inside its crop" },
      "nameContains": { "type": "array", "items": { "type": "string" }, "description": "Keep names holding any of these" },
      "levelName": { "type": "string", "description": "Only rooms of this level" },
      "maxCount": { "type": "number", "description": "Default 300" }
    }
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Placed rooms only (Area > 0; unplaced / not enclosed rooms are counted in Skipped). Each room: Id, Number, Name,
// Level, AreaM2, CenterMm {x, y} (model coordinates of the room's location point) and BoxMm {minX, minY, maxX, maxY}
// (its bounding box). viewId: rooms of that plan's GenLevel whose location point is inside the view's crop box (a view
// without an active crop: every room of the level). nameContains: case-insensitive, any part matches (e.g. ["TOILET",
// "WC"] for the toilet areas of the profile).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomsDump
{
    const double FtToMm = 304.8;

    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        int max = args.Value<int?>("maxCount") ?? 300;
        var parts = (args["nameContains"] as JArray ?? new JArray()).Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToList();
        string levelName = (string)args["levelName"];
        ViewPlan plan = null;
        if (args["viewId"] != null)
        {
            plan = doc.GetElement(new ElementId((int)args["viewId"])) as ViewPlan;
            if (plan == null) return new { error = "viewId is not a plan view" };
        }

        Transform toCrop = null;
        BoundingBoxXYZ crop = null;
        if (plan != null && plan.CropBoxActive)
        {
            crop = plan.CropBox;
            toCrop = crop.Transform.Inverse;
        }

        var rooms = new List<object>();
        int skipped = 0, total = 0;
        foreach (Room r in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>())
        {
            if (r.Area <= 0 || !(r.Location is LocationPoint lp)) { skipped++; continue; }
            if (plan != null && plan.GenLevel != null && r.LevelId != plan.GenLevel.Id) continue;
            if (levelName != null && !string.Equals(r.Level?.Name, levelName, StringComparison.OrdinalIgnoreCase)) continue;
            string name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Name;
            if (parts.Count > 0 && !parts.Any(p => name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            if (toCrop != null)
            {
                XYZ q = toCrop.OfPoint(lp.Point);
                if (q.X < crop.Min.X || q.X > crop.Max.X || q.Y < crop.Min.Y || q.Y > crop.Max.Y) continue;
            }
            total++;
            if (rooms.Count >= max) continue;
            BoundingBoxXYZ bb = r.get_BoundingBox(null);
            rooms.Add(new
            {
                Id = r.Id.IntegerValue,
                r.Number,
                Name = name,
                Level = r.Level?.Name,
                AreaM2 = Math.Round(r.Area * 0.09290304, 2),
                CenterMm = new { x = Math.Round(lp.Point.X * FtToMm), y = Math.Round(lp.Point.Y * FtToMm) },
                BoxMm = bb == null ? null : new
                {
                    minX = Math.Round(bb.Min.X * FtToMm), minY = Math.Round(bb.Min.Y * FtToMm),
                    maxX = Math.Round(bb.Max.X * FtToMm), maxY = Math.Round(bb.Max.Y * FtToMm),
                },
            });
        }
        return new { Total = total, Count = rooms.Count, Skipped = skipped, Rooms = rooms };
    }
}
