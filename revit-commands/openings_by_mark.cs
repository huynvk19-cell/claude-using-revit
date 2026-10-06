/* mcp-tool
{
  "description": "Read-only: doors/windows in ONE view whose Mark is in marks: id, category, type mark, mark, family/type, level, and the ids + values of the dimensions of that view (and its parent) that reference them.",
  "inputSchema": {
    "type": "object",
    "properties": { "viewId": { "type": "number" }, "marks": { "type": "array", "items": { "type": "string" } } },
    "required": ["viewId", "marks"]
  },
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

public static class OpeningsByMark
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var marks = new HashSet<string>(((JArray)args["marks"]).Select(t => (string)t));
        var owners = new HashSet<ElementId> { v.Id }; if (v.GetPrimaryViewId() != ElementId.InvalidElementId) owners.Add(v.GetPrimaryViewId());
        var dims = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => owners.Contains(d.OwnerViewId)).ToList();
        var res = new List<object>();
        foreach (var bic in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
            foreach (FamilyInstance fi in new FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                var mk = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                if (mk == null || !marks.Contains(mk)) continue;
                var refd = dims.Where(d => d.References.Cast<Reference>().Any(r => r.ElementId == fi.Id))
                               .Select(d => d.Id.IntegerValue + " " + d.DimensionType.Name + " [" + (d.NumberOfSegments > 1 ? string.Join("|", d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * 304.8))) : Math.Round((d.Value ?? 0) * 304.8).ToString()) + "]").ToList();
                res.Add(new { Id = fi.Id.IntegerValue, Cat = fi.Category.Name, TypeMark = fi.Symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString(), Mark = mk,
                              Type = fi.Symbol.FamilyName + " : " + fi.Symbol.Name, Level = (doc.GetElement(fi.LevelId) as Level)?.Name, FacingDot = Math.Round(fi.FacingOrientation.DotProduct(v.ViewDirection), 2), Dims = refd });
            }
        return res;
    }
}
