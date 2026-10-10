/* mcp-tool
{
  "description": "Create (or replace, if replace=true) a print set (View/Sheet Set) containing the sheets listed by a sheet-list schedule. scheduleId or scheduleName. mode preview lists the sheets; apply saves the set under setName.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "scheduleId": { "type": "integer" },
      "scheduleName": { "type": "string" },
      "setName": { "type": "string" },
      "replace": { "type": "boolean" }
    },
    "required": ["mode", "setName"]
  },
  "timeoutSeconds": 120
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class PrintSetFromSchedule
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        ViewSchedule vs = null;
        if (args["scheduleId"] != null) vs = doc.GetElement(new ElementId((int)args["scheduleId"])) as ViewSchedule;
        if (vs == null && args["scheduleName"] != null)
            vs = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().FirstOrDefault(s => s.Name == (string)args["scheduleName"]);
        if (vs == null) return new { Error = "schedule not found" };
        var sheets = new FilteredElementCollector(doc, vs.Id).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().OrderBy(s => s.SheetNumber).ToList();
        string setName = (string)args["setName"];
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().FirstOrDefault(x => x.Name == setName);
        var list = sheets.Select(s => s.SheetNumber + " - " + s.Name).ToList();
        if ((string)args["mode"] != "apply")
            return new { Schedule = vs.Name, Sheets = sheets.Count, SetExists = existing != null, List = list };
        if (existing != null && args["replace"]?.Value<bool>() != true) return new { Error = "print set already exists: " + setName };

        var pm = doc.PrintManager;
        pm.PrintRange = PrintRange.Select;
        var vss = pm.ViewSheetSetting;
        var set = new ViewSet();
        foreach (var s in sheets) set.Insert(s);
        using (var t = new Transaction(doc, "Create print set " + setName))
        {
            t.Start();
            if (existing != null) { vss.CurrentViewSheetSet = existing; vss.CurrentViewSheetSet.Views = set; vss.Save(); }
            else { vss.CurrentViewSheetSet.Views = set; vss.SaveAs(setName); }
            t.Commit();
        }
        var saved = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().FirstOrDefault(x => x.Name == setName);
        return new { Schedule = vs.Name, Set = saved?.Name, SetId = saved?.Id.IntegerValue, SheetsInSet = saved?.Views.Size, List = list };
    }
}
