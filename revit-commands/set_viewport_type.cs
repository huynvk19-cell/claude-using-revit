/* mcp-tool
{
  "description": "Change the type of given viewports to a viewport type (by name). Reports old type, detail number and title position before/after. mode preview | apply | undo (restores old types from logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewportIds": { "type": "array", "items": { "type": "number" } },
      "typeName": { "type": "string" },
      "mode": { "type": "string" },
      "logPath": { "type": "string" }
    },
    "required": ["mode", "logPath"]
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class SetViewportType
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], log = (string)args["logPath"];
        if (mode == "undo")
        {
            var items = JArray.Parse(File.ReadAllText(log));
            using (var t = new Transaction(doc, "Viewport type restore"))
            {
                t.Start();
                foreach (var it in items) { var vp = doc.GetElement(new ElementId((int)it["VpId"])) as Viewport; if (vp != null) vp.ChangeTypeId(new ElementId((int)it["OldTypeId"])); }
                t.Commit();
            }
            return new { Restored = items.Count };
        }
        var type = new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>()
            .FirstOrDefault(x => x.FamilyName == "Viewport" && x.Name == (string)args["typeName"]);
        if (type == null) return new { Error = "type not found" };
        var rows = new List<object>(); var logRows = new List<object>();
        using (var t = new Transaction(doc, "Viewport type"))
        {
            t.Start();
            foreach (var id in ((JArray)args["viewportIds"]).Select(x => (int)x))
            {
                var vp = doc.GetElement(new ElementId(id)) as Viewport; if (vp == null) { rows.Add(new { VpId = id, Error = "not a viewport" }); continue; }
                var old = vp.GetTypeId(); var sheet = (ViewSheet)doc.GetElement(vp.SheetId); var view = (View)doc.GetElement(vp.ViewId);
                if (old != type.Id) vp.ChangeTypeId(type.Id);
                rows.Add(new { VpId = id, Sheet = sheet.SheetNumber, View = view.Name, Old = doc.GetElement(old).Name, Detail = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() });
                logRows.Add(new { VpId = id, OldTypeId = old.IntegerValue });
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(log, JsonConvert.SerializeObject(logRows, Formatting.Indented)); } else t.RollBack();
        }
        return new { mode, Type = type.Name, TypeId = type.Id.IntegerValue, Rows = rows };
    }
}
