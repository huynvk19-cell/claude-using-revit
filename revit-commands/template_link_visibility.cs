/* mcp-tool
{
  "description": "Read-only: for view templates (by name list, or those used by views on sheets with sheetPrefix), which Revit link types are visible (link instance not hidden in the template) - the rows ticked in V/G > Revit Links.",
  "inputSchema": { "type": "object", "properties": { "sheetPrefix": { "type": "string" }, "names": { "type": "array", "items": { "type": "string" } } } },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class TemplateLinkVisibility
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var tids = new HashSet<ElementId>();
        var names = (args["names"] as JArray)?.Select(x => (string)x).ToList();
        string prefix = (string)args["sheetPrefix"];
        if (prefix != null)
            foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => s.SheetNumber.StartsWith(prefix)))
                foreach (var vid in s.GetAllPlacedViews()) { var v = doc.GetElement(vid) as View; if (v != null && v.ViewTemplateId != ElementId.InvalidElementId) tids.Add(v.ViewTemplateId); }
        if (names != null)
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(t => t.IsTemplate && names.Contains(t.Name))) tids.Add(t.Id);
        var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
        var linkCat = Category.GetCategory(doc, BuiltInCategory.OST_RvtLinks);
        var res = new List<object>();
        foreach (var tid in tids)
        {
            var t = (View)doc.GetElement(tid);
            bool catHidden = false; try { catHidden = t.GetCategoryHidden(linkCat.Id); } catch { }
            var vis = links.GroupBy(l => doc.GetElement(l.GetTypeId()).Name)
                .Where(g => g.Any(l => { try { return !l.IsHidden(t); } catch { return true; } }))
                .Select(g => g.Key).OrderBy(n => n).ToList();
            res.Add(new { Template = t.Name, Id = tid.IntegerValue, LinksCategoryHidden = catHidden, VisibleLinks = vis.Count, Visible = vis });
        }
        return res.OrderBy(r => ((dynamic)r).Template).ToList();
    }
}
