/* mcp-tool
{
  "description": "Create (or reuse) a review copy of a linear dimension type: duplicate baseType as newName and set its Color (RGB). Returns the type id. Use for temporary check types that are swapped back to the official type after review.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "baseType": { "type": "string" },
      "newName": { "type": "string" },
      "color": { "type": "array", "items": { "type": "number" }, "description": "[r,g,b], default [0,0,255]" }
    },
    "required": ["baseType", "newName"]
  },
  "timeoutSeconds": 60
}
*/
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DimTypeCheckCopy
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string baseName = (string)args["baseType"], newName = (string)args["newName"];
        var all = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().ToList();
        var existing = all.FirstOrDefault(t => t.Name == newName);
        if (existing != null) return new { Created = false, Id = existing.Id.IntegerValue, Name = newName };
        var bt = all.FirstOrDefault(t => t.Name == baseName && t.StyleType == DimensionStyleType.Linear) ?? all.FirstOrDefault(t => t.Name == baseName);
        if (bt == null) return new { Error = "base type not found: " + baseName };
        var c = (args["color"] as JArray)?.Select(x => (int)x).ToArray() ?? new[] { 0, 0, 255 };
        using (var t = new Transaction(doc, "Create dimension check type"))
        {
            t.Start();
            var nt = (DimensionType)bt.Duplicate(newName);
            nt.get_Parameter(BuiltInParameter.LINE_COLOR).Set(c[0] + c[1] * 256 + c[2] * 65536);
            t.Commit();
            return new { Created = true, Id = nt.Id.IntegerValue, Name = newName, Base = bt.Id.IntegerValue };
        }
    }
}
