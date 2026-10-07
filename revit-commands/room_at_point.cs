/* mcp-tool
{
  "description": "Read-only: rooms at view-frame points of ONE plan view and their 'Finish' parameters; rooms by name.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "points": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "right": {
              "type": "number"
            },
            "up": {
              "type": "number"
            }
          }
        }
      },
      "paramContains": {
        "type": "string"
      },
      "roomNameContains": {
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
// Read-only: the room(s) at points of ONE plan view (view frame mm Right / Up, as stair_plan_audit; tested 1 m above
//    the view's level) and their parameters whose name contains a filter (default 'Finish'), e.g. Wall Finish / Floor
//    Finish codes for finish tags. Also lists rooms of the view's level whose name contains roomNameContains.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomAtPoint
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        string filt = ((string)args["paramContains"] ?? "Finish").ToLower();
        string nameF = ((string)args["roomNameContains"] ?? "").ToLower();
        double z = (v.GenLevel != null ? v.GenLevel.ProjectElevation : v.Origin.Z) + 1000 / MM;
        var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>().Where(r => r.Area > 0).ToList();
        Func<Room, object> info = r => new
        {
            Id = r.Id.IntegerValue, r.Name, r.Number, Level = r.Level?.Name,
            Params = r.Parameters.Cast<Parameter>().Where(p => p.Definition.Name.ToLower().Contains(filt))
                      .Select(p => p.Definition.Name + " = " + (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString())).OrderBy(s => s).ToList()
        };
        var atPts = new List<object>();
        foreach (var pj in (JArray)args["points"] ?? new JArray())
        {
            var p = new XYZ(v.Origin.X, v.Origin.Y, z) + v.RightDirection * ((double)pj["right"] / MM) + v.UpDirection * ((double)pj["up"] / MM);
            var hit = rooms.Where(r => { try { return r.IsPointInRoom(p); } catch { return false; } }).Select(info).ToList();
            atPts.Add(new { Right = (double)pj["right"], Up = (double)pj["up"], Rooms = hit });
        }
        var named = nameF.Length == 0 ? new List<object>() : rooms.Where(r => (r.Name ?? "").ToLower().Contains(nameF) && (v.GenLevel == null || r.LevelId == v.GenLevel.Id)).Select(info).ToList();
        return new { View = v.Name, AtPoints = atPts, ByName = named };
    }
}
