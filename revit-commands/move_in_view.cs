/* mcp-tool
{
  "description": "Move elements of a view by dx (view right) and dz (view up), mm.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "ids": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "dx": {
        "type": "number"
      },
      "dz": {
        "type": "number"
      }
    },
    "required": [
      "viewId",
      "ids"
    ]
  },
  "timeoutSeconds": 60
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Move elements (e.g. dimensions, tags, text) of a view along that view's right direction (dx, mm) and up (dz, mm).
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class MoveInView
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId(args.Value<int>("viewId")));
        var mv = v.RightDirection * ((args.Value<double?>("dx") ?? 0) / MM) + v.UpDirection * ((args.Value<double?>("dz") ?? 0) / MM);
        var ids = ((JArray)args["ids"]).Select(t => new ElementId((int)t)).ToList();
        using (var t = new Transaction(doc, "Move in view")) { t.Start(); ElementTransformUtils.MoveElements(doc, ids, mv); t.Commit(); }
        return new { Moved = ids.Select(i => i.IntegerValue), Dx = args["dx"], Dz = args["dz"] };
    }
}
