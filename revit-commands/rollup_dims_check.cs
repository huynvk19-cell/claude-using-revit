/* mcp-tool
{
  "description": "Read-only: in one elevation/section view, list the roll-up doors (family name contains familyContains, default 'ROLL UP') with mark, type, nominal Width/Height (type params), whether a tag points at them in this view, and every linear dimension that references them: id, type, direction, values, and for each reference to the door its reference name (e.g. TOP / Left / Right) or 'face' when it is a geometry face (frame/coil box). Use to fix roll-up door dims to the nominal size.",
  "inputSchema": { "type": "object", "properties": { "viewId": { "type": "number" }, "familyContains": { "type": "string" } }, "required": ["viewId"] },
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

public static class RollupDimsCheck
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        string fam = ((string)args["familyContains"] ?? "ROLL UP").ToUpperInvariant();
        var doors = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().OfType<FamilyInstance>()
            .Where(f => (f.Symbol.FamilyName ?? "").ToUpperInvariant().Contains(fam)).ToList();
        var tagged = new HashSet<int>(new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().SelectMany(t => t.GetTaggedLocalElementIds()).Select(i => i.IntegerValue));
        var dims = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => d.OwnerViewId == v.Id && d.Curve is Line).ToList();
        var res = new List<object>();
        foreach (var f in doors)
        {
            Func<BuiltInParameter, string, double> P = (bip, name) => { var p = f.Symbol.get_Parameter(bip) ?? f.Symbol.LookupParameter(name); return p == null ? 0 : Math.Round(p.AsDouble() * MM); };
            var ds = dims.Where(d => d.References.Cast<Reference>().Any(r => r.ElementId == f.Id)).Select(d =>
            {
                var ln = (Line)d.Curve; bool vert = Math.Abs(ln.Direction.Z) > 0.9;
                var vals = d.NumberOfSegments == 0 ? new List<double> { Math.Round((d.Value ?? 0) * MM) } : d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();
                var refs = d.References.Cast<Reference>().Select(r =>
                {
                    if (r.ElementId != f.Id) { var e = doc.GetElement(r.ElementId); return (e?.Category?.Name ?? "?") + (e is Level ? ":" + e.Name : ""); }
                    string n = null; try { n = f.GetReferenceName(r); } catch { }
                    return "DOOR:" + (string.IsNullOrEmpty(n) ? (r.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_SURFACE ? "face" : r.ElementReferenceType.ToString()) : n);
                }).ToList();
                return new { Id = d.Id.IntegerValue, Type = d.DimensionType.Name, Dir = vert ? "V" : "H", Pos = Math.Round((ln.Origin - v.Origin).DotProduct(vert ? v.RightDirection : v.UpDirection) * MM), Values = vals, Refs = refs, Segments = d.NumberOfSegments };
            }).ToList();
            if (!tagged.Contains(f.Id.IntegerValue) && ds.Count == 0) continue;
            var bb = f.get_BoundingBox(null);
            res.Add(new
            {
                Id = f.Id.IntegerValue, Mark = f.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), Family = f.Symbol.FamilyName, Type = f.Symbol.Name,
                W = P(BuiltInParameter.DOOR_WIDTH, "Width"), H = P(BuiltInParameter.DOOR_HEIGHT, "Height"), Tagged = tagged.Contains(f.Id.IntegerValue),
                Level = (doc.GetElement(f.LevelId) as Level)?.Name, X = Math.Round(((bb.Min + bb.Max) / 2 - v.Origin).DotProduct(v.RightDirection) * MM), Dims = ds
            });
        }
        return new { View = v.Name, Count = res.Count, Doors = res };
    }
}
