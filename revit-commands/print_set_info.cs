/* mcp-tool
{
  "description": "Read-only: sheets in a print set (View/Sheet Set) by name, optionally compared with a sheet-list schedule.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "setName": { "type": "string" },
      "scheduleName": { "type": "string" }
    },
    "required": ["setName"]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class PrintSetInfo
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string setName = (string)args["setName"];
        var set = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().FirstOrDefault(x => x.Name == setName);
        if (set == null) return new { Error = "print set not found: " + setName };
        var inSet = new List<string>();
        foreach (View v in set.Views) inSet.Add(v is ViewSheet s ? s.SheetNumber : "view: " + v.Name);
        inSet.Sort();
        object cmp = null;
        if (args["scheduleName"] != null)
        {
            var vs = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().FirstOrDefault(s => s.Name == (string)args["scheduleName"]);
            if (vs != null)
            {
                var listed = new FilteredElementCollector(doc, vs.Id).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber).ToList();
                cmp = new { Listed = listed.Count, MissingInSet = listed.Except(inSet).OrderBy(x => x).ToList(), OnlyInSet = inSet.Except(listed).ToList() };
            }
        }
        return new { Set = set.Name, SetId = set.Id.IntegerValue, Count = inSet.Count, Compare = cmp };
    }
}
