/* mcp-tool
{
  "description": "Read-only: crop region of views in view coordinates (mm from the view origin along right / up), annotation crop offsets, scope box, scale.",
  "inputSchema": { "type": "object", "properties": { "viewIds": { "type": "array", "items": { "type": "number" } } }, "required": ["viewIds"] },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewCropInfo
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        return ((JArray)args["viewIds"]).Select(x => (View)doc.GetElement(new ElementId((int)x))).Select(v =>
        {
            var cb = v.CropBox; var t = cb.Transform;
            var pts = new[] { cb.Min, cb.Max }.Select(p => t.OfPoint(p)).ToList();
            Func<XYZ, double> R = p => Math.Round((p - v.Origin).DotProduct(v.RightDirection) * MM), U = p => Math.Round((p - v.Origin).DotProduct(v.UpDirection) * MM);
            var sb = doc.GetElement(v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId() ?? ElementId.InvalidElementId);
            var m = v.GetCropRegionShapeManager();
            object ann = null; try { ann = new { L = Math.Round(m.LeftAnnotationCropOffset * MM, 1), R = Math.Round(m.RightAnnotationCropOffset * MM, 1), T = Math.Round(m.TopAnnotationCropOffset * MM, 1), B = Math.Round(m.BottomAnnotationCropOffset * MM, 1) }; } catch { } // API offsets are paper feet
            return new { View = v.Name, Id = v.Id.IntegerValue, Scale = v.Scale, Right = pts.Select(R).Min() + " .. " + pts.Select(R).Max(), Up = pts.Select(U).Min() + " .. " + pts.Select(U).Max(), ScopeBox = sb?.Name, AnnoCropActive = v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger(), AnnoOffsetPaperMm = ann };
        }).ToList();
    }
}
