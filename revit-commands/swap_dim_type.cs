/* mcp-tool
{
  "description": "Change every dim of one type to another type (e.g. check type back to official). preview | apply (log).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply"
        ]
      },
      "fromType": {
        "type": "string"
      },
      "toTypeId": {
        "type": "number"
      },
      "toType": {
        "type": "string"
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "mode",
      "fromType"
    ]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Change every dimension of one type (fromType, by name) to another type (toTypeId or toType name), e.g. return check-
//    type dims to the official type. Mode preview lists counts per view; apply changes them and writes a log of ids.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class SwapDimType
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var from = (string)args["fromType"];
        DimensionType to = null;
        if (args["toTypeId"] != null) to = doc.GetElement(new ElementId((int)args["toTypeId"])) as DimensionType;
        else if (args["toType"] != null) to = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(t => t.Name == (string)args["toType"] && t.StyleType == DimensionStyleType.Linear);
        bool spots = args.Value<bool?>("spots") ?? false;
        var dims = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => (d is SpotDimension) == spots && d.DimensionType?.Name == from).ToList();
        var byView = dims.GroupBy(d => (doc.GetElement(d.OwnerViewId) as View)?.Name ?? "?").Select(g => g.Key + ": " + g.Count()).OrderBy(s => s).ToList();
        if ((string)args["mode"] == "apply" && to != null)
        {
            using (var t = new Transaction(doc, "Return dimensions to official type"))
            {
                t.Start();
                foreach (var d in dims) d.ChangeTypeId(to.Id);
                t.Commit();
            }
            if (args["logPath"] != null) File.WriteAllText((string)args["logPath"], new JObject { ["from"] = from, ["to"] = to.Name, ["ids"] = new JArray(dims.Select(d => d.Id.IntegerValue)) }.ToString());
        }
        return new { From = from, To = to == null ? "(not found)" : to.Id.IntegerValue + " " + to.Name + " (" + to.StyleType + ")", Count = dims.Count, ByView = byView };
    }
}
