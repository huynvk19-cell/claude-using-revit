/* mcp-tool
{
  "description": "Read-only: for plan views whose name contains a text, count the annotation elements by category and type (stair run tags, railing tags, spots, stair paths, tread numbers, dims, tags), plus the sheet each view sits on. Use to see how the project already details similar views.",
  "inputSchema": { "type": "object", "properties": { "nameContains": { "type": "string" }, "outPath": { "type": "string" } }, "required": ["nameContains"] },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairViewsSurvey
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string key = ((string)args["nameContains"]).ToUpperInvariant();
        var sheetOf = new Dictionary<int, string>();
        foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
        {
            var s = doc.GetElement(vp.SheetId) as ViewSheet;
            if (s != null) sheetOf[vp.ViewId.IntegerValue] = s.SheetNumber + " - " + s.Name;
        }
        var rows = new List<object>();
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(v => !v.IsTemplate && v.Name.ToUpperInvariant().Contains(key)).OrderBy(v => v.Name))
        {
            var counts = new Dictionary<string, int>();
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
            {
                var cat = e.Category;
                if (cat == null || cat.CategoryType != CategoryType.Annotation || e.OwnerViewId != v.Id && !(e is Dimension)) continue;
                var t = doc.GetElement(e.GetTypeId()) as ElementType;
                string k = cat.Name + " : " + (t == null ? "-" : t.FamilyName + " : " + t.Name);
                counts[k] = counts.TryGetValue(k, out var n) ? n + 1 : 1;
            }
            rows.Add(new
            {
                View = v.Name, ViewId = v.Id.IntegerValue, Scale = v.Scale,
                Sheet = sheetOf.TryGetValue(v.Id.IntegerValue, out var sh) ? sh : null,
                Total = counts.Values.Sum(),
                Counts = counts.OrderByDescending(p => p.Value).Select(p => p.Key + " (" + p.Value + ")").ToList()
            });
        }
        string outPath = (string)args["outPath"];
        if (!string.IsNullOrEmpty(outPath)) System.IO.File.WriteAllText(outPath, JsonConvert.SerializeObject(rows, Formatting.Indented));
        return new { Views = rows.Count, Rows = rows };
    }
}
