/* mcp-tool
{
  "description": "Read-only: in ONE view, whether given built-in categories (and their subcategories) are hidden, and whether the view template controls V/G. Example cats: OST_StairsRailing, OST_RailingTopRail, OST_RailingHandRail, OST_StairsRuns.",
  "inputSchema": { "type": "object", "properties": { "viewId": { "type": "number" }, "cats": { "type": "array", "items": { "type": "string" } } }, "required": ["viewId", "cats"] },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewCatHidden
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var tpl = doc.GetElement(v.ViewTemplateId) as View;
        var rows = new List<string>();
        foreach (var name in args["cats"].Select(x => (string)x))
        {
            var bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), name);
            var cat = Category.GetCategory(doc, bic); if (cat == null) { rows.Add(name + ": no category"); continue; }
            rows.Add(cat.Name + ": hidden=" + v.GetCategoryHidden(cat.Id));
            foreach (Category sub in cat.SubCategories) rows.Add("  " + cat.Name + " > " + sub.Name + ": hidden=" + v.GetCategoryHidden(sub.Id));
        }
        return new { View = v.Name, Template = tpl?.Name, Rows = rows };
    }
}
