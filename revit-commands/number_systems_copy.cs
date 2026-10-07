/* mcp-tool
{
  "description": "Copy the display settings of one stair tread/riser number (NumberSystem) to others: Display Rule, Number Size, Justify, Justify Offset, Orientation, Offset from Reference, Tag Type (values copied as stored, no unit parsing). Reference (side) is kept per target unless copyReference. mode preview | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sourceId": { "type": "integer" },
      "targetIds": { "type": "array", "items": { "type": "integer" } },
      "copyReference": { "type": "boolean" },
      "mode": { "type": "string", "enum": ["preview", "apply"] }
    },
    "required": ["sourceId", "targetIds", "mode"]
  },
  "timeoutSeconds": 60
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class NumberSystemsCopy
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var src = doc.GetElement(new ElementId((int)args["sourceId"]));
        if (src == null) return "source not found";
        bool copyRef = args["copyReference"] != null && (bool)args["copyReference"];
        var names = new List<string> { "Display Rule", "Number Size", "Justify", "Justify Offset", "Orientation", "Offset from Reference", "Tag Type" };
        if (copyRef) names.Add("Reference");
        bool apply = (string)args["mode"] == "apply";
        var rows = new List<string>();
        using (var t = new Transaction(doc, "Copy tread number settings"))
        {
            t.Start();
            foreach (var j in (JArray)args["targetIds"])
            {
                var e = doc.GetElement(new ElementId((int)j));
                if (e == null) { rows.Add(j + ": not found"); continue; }
                foreach (var n in names)
                {
                    var a = src.LookupParameter(n); var b = e.LookupParameter(n);
                    if (a == null || b == null || b.IsReadOnly) { rows.Add(j + " " + n + ": skipped"); continue; }
                    string before = b.AsValueString();
                    switch (a.StorageType)
                    {
                        case StorageType.Double: b.Set(a.AsDouble()); break;
                        case StorageType.Integer: b.Set(a.AsInteger()); break;
                        case StorageType.String: b.Set(a.AsString()); break;
                        case StorageType.ElementId: b.Set(a.AsElementId()); break;
                    }
                    rows.Add(j + " " + n + ": " + before + " -> " + b.AsValueString());
                }
            }
            if (apply) t.Commit(); else t.RollBack();
        }
        return rows;
    }
}
