/* mcp-tool
{
  "description": "Read-only: for plan views whose name contains a text, list the finish marks drawn as Generic Annotations (family/type name contains FINISH): code (first string parameter shaped like F13 / W05), type, count per view.",
  "inputSchema": { "type": "object", "properties": { "nameContains": { "type": "string" } }, "required": ["nameContains"] },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class FinishMarksSurvey
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string key = ((string)args["nameContains"]).ToUpperInvariant();
        var rx = new System.Text.RegularExpressions.Regex(@"^[A-Z]{1,2}\d{1,3}[A-Z]?$");
        var rows = new List<object>();
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate && v.Name.ToUpperInvariant().Contains(key)).OrderBy(v => v.Name))
        {
            var codes = new List<string>();
            foreach (var ga in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_GenericAnnotation).WhereElementIsNotElementType())
            {
                var t = doc.GetElement(ga.GetTypeId()) as ElementType;
                if (!((t?.FamilyName ?? "") + " " + (t?.Name ?? "")).ToUpper().Contains("FINISH")) continue;
                string code = "?";
                foreach (Parameter p in ga.Parameters) { if (p.StorageType != StorageType.String) continue; var s = (p.AsString() ?? "").Trim(); if (rx.IsMatch(s)) { code = s; break; } }
                codes.Add(code + " (" + t?.Name + (ga is FamilyInstance fi && fi.HasModifiedGeometry() ? "" : "") + ")");
            }
            if (codes.Count > 0) rows.Add(new { View = v.Name, Marks = codes.GroupBy(c => c).Select(g => g.Key + " x" + g.Count()).ToList() });
        }
        return rows;
    }
}
