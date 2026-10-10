/* mcp-tool
{
  "description": "Rename a print set (View/Sheet Set). preview | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "setName": { "type": "string" },
      "newName": { "type": "string" }
    },
    "required": ["mode", "setName", "newName"]
  },
  "timeoutSeconds": 60
}
*/
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class PrintSetRename
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string setName = (string)args["setName"], newName = (string)args["newName"];
        var sets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().ToList();
        var set = sets.FirstOrDefault(x => x.Name == setName);
        if (set == null) return new { Error = "print set not found: " + setName, Sets = sets.Select(x => x.Name).ToList() };
        if (sets.Any(x => x.Name == newName)) return new { Error = "a print set is already named " + newName };
        if ((string)args["mode"] != "apply") return new { Set = set.Name, SetId = set.Id.IntegerValue, Sheets = set.Views.Size, NewName = newName, AllSets = sets.Select(x => x.Name).ToList() };
        using (var t = new Transaction(doc, "Rename print set"))
        {
            t.Start();
            var pm = doc.PrintManager;
            pm.PrintRange = PrintRange.Select; // ViewSheetSetting is only available with Select
            var vss = pm.ViewSheetSetting;
            vss.CurrentViewSheetSet = set;
            vss.Rename(newName);
            t.Commit();
        }
        var after = doc.GetElement(set.Id) as ViewSheetSet;
        return new { SetId = set.Id.IntegerValue, Name = after?.Name, Sheets = after?.Views.Size };
    }
}
