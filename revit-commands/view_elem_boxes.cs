/* mcp-tool
{
  "description": "Read-only: for element ids in ONE view (or allAnnotations:true = every annotation owned by the view), the view bounding box in model mm (X/Y) AND in the view frame (Right/Up from View.Origin, as stair_plan_audit), plus for dimensions the line origin/direction, values and type.",
  "inputSchema": { "type": "object", "properties": { "viewId": { "type": "number" }, "ids": { "type": "array", "items": { "type": "number" } }, "allAnnotations": { "type": "boolean" } }, "required": ["viewId"] },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewElemBoxes
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        System.Func<XYZ, double> vx = p => System.Math.Round((p - O).DotProduct(R) * MM), vy = p => System.Math.Round((p - O).DotProduct(U) * MM);
        var ids = new List<int>();
        if (args["ids"] != null) ids.AddRange(args["ids"].Select(x => (int)x));
        if (args.Value<bool?>("allAnnotations") == true)
            ids.AddRange(new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType()
                .Where(e => e.OwnerViewId == v.Id && e.Category != null && e.Category.CategoryType == CategoryType.Annotation).Select(e => e.Id.IntegerValue));
        var rows = new List<object>();
        foreach (var id in ids.Distinct())
        {
            var e = doc.GetElement(new ElementId(id)); if (e == null) { rows.Add(new { Id = id, Missing = true }); continue; }
            var bb = e.get_BoundingBox(v);
            object line = null;
            var d = e as Dimension;
            if (d != null && d.Curve is Line l)
            {
                var vals = d.NumberOfSegments > 0 ? string.Join("|", d.Segments.Cast<DimensionSegment>().Select(s => s.ValueString)) : d.ValueString;
                line = new { Origin = new[] { System.Math.Round(l.Origin.X * MM), System.Math.Round(l.Origin.Y * MM) }, Dir = new[] { System.Math.Round(l.Direction.X, 3), System.Math.Round(l.Direction.Y, 3) }, Values = vals, Type = doc.GetElement(d.GetTypeId())?.Name };
            }
            var t = doc.GetElement(e.GetTypeId()) as ElementType;
            rows.Add(new
            {
                Id = id, Category = e.Category?.Name, Type = t == null ? null : t.FamilyName + " : " + t.Name,
                Box = bb == null ? null : new[] { System.Math.Round(bb.Min.X * MM), System.Math.Round(bb.Min.Y * MM), System.Math.Round(bb.Max.X * MM), System.Math.Round(bb.Max.Y * MM) },
                ViewBox = bb == null ? null : new[] { System.Math.Min(vx(bb.Min), vx(bb.Max)), System.Math.Min(vy(bb.Min), vy(bb.Max)), System.Math.Max(vx(bb.Min), vx(bb.Max)), System.Math.Max(vy(bb.Min), vy(bb.Max)) },
                Dim = line
            });
        }
        return rows;
    }
}
