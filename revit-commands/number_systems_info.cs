/* mcp-tool
{
  "description": "Read-only: stair tread/riser numbers in views: host run, type, every instance parameter value.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      }
    },
    "required": [
      "viewIds"
    ]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: list the stair tread/riser numbers (NumberSystem) in the given views with their host run, type and every
//    instance parameter value (start number, display rule, side, orientation...).
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class NumberSystemsInfo
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var rows = new List<object>();
        foreach (var vid in args["viewIds"].Select(x => (int)x))
        {
            foreach (var ns in new FilteredElementCollector(doc, new ElementId(vid)).OfClass(typeof(NumberSystem)).Cast<NumberSystem>())
            {
                var ps = new List<string>();
                foreach (Parameter p in ns.Parameters)
                {
                    string val = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString() ?? (p.StorageType == StorageType.Integer ? p.AsInteger().ToString() : p.StorageType == StorageType.ElementId ? p.AsElementId().IntegerValue.ToString() : "");
                    ps.Add(p.Definition.Name + (p.IsReadOnly ? " (ro)" : "") + " = " + val);
                }
                var t = doc.GetElement(ns.GetTypeId()) as ElementType;
                rows.Add(new { View = vid, Id = ns.Id.IntegerValue, Host = ns.NumberedElementId.HostElementId.IntegerValue, Type = t == null ? "-" : t.FamilyName + " : " + t.Name, Params = ps.OrderBy(s => s).ToList() });
            }
        }
        return rows;
    }
}
