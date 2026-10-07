/* mcp-tool
{
  "description": "Read-only: dims of sheet views (sheet prefix) that do not show: no box in view or outside the crop / annotation crop.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sheetPrefix": {
        "type": "string"
      }
    },
    "required": [
      "sheetPrefix"
    ]
  },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: linear dimensions owned by views placed on sheets (sheet number prefix) that do not show in their view:
//    no bounding box in the view, or entirely outside the view's crop/annotation crop region. Lists id, view, sheet,
//    values, reason.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class HiddenDimsScan
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var res = new List<object>();
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => s.SheetNumber.StartsWith((string)args["sheetPrefix"])))
            foreach (var vpId in s.GetAllViewports())
            {
                var vp = (Viewport)doc.GetElement(vpId); var v = (View)doc.GetElement(vp.ViewId);
                if (v.ViewType == ViewType.Legend) continue;
                var visible = new HashSet<ElementId>(new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).ToElementIds());
                Outline box = null; try { box = vp.GetBoxOutline(); } catch { }
                Transform m2p = null, p2s = null;
                try { m2p = v.GetModelToProjectionTransforms().First().GetModelToProjectionTransform(); p2s = vp.GetProjectionToSheetTransform(); } catch { }
                foreach (var d in new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => d.OwnerViewId == v.Id && !(d is SpotDimension)))
                {
                    string reason = null;
                    if (!visible.Contains(d.Id)) reason = "hidden in view (category/filter/hide)";
                    else
                    {
                        var bb = d.get_BoundingBox(v);
                        if (bb == null) reason = "no extent in view";
                        else if (box != null && m2p != null)
                        {
                            var pts = new[] { bb.Min, bb.Max }.Select(p => p2s.OfPoint(m2p.OfPoint(p))).ToList();
                            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
                            if (x1 < box.MinimumPoint.X || x0 > box.MaximumPoint.X || y1 < box.MinimumPoint.Y || y0 > box.MaximumPoint.Y) reason = "outside the viewport (crop)";
                        }
                    }
                    if (reason == null) continue;
                    res.Add(new { Id = d.Id.IntegerValue, Sheet = s.SheetNumber, View = v.Name, Type = d.DimensionType?.Name, Values = d.NumberOfSegments > 0 ? d.NumberOfSegments + " segs" : d.ValueString, Reason = reason });
                }
            }
        return new { Count = res.Count, Items = res };
    }
}
