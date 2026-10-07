/* mcp-tool
{
  "description": "Read-only, plan: elements running past a coordinate on one side (e.g. beyond the last grid), plus visible links.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "side": {
        "type": "string",
        "enum": [
          "right",
          "left",
          "up",
          "down"
        ]
      },
      "beyondMm": {
        "type": "number",
        "description": "coordinate along the side axis (mm from view origin)"
      }
    },
    "required": [
      "viewId",
      "side"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: host elements visible in a plan view whose extent goes beyond a coordinate on one side (view
//    right/left/up/down from the view origin, mm), e.g. lines running out past the last grid. Lists id, category,
//    class, line style / type, view-specific or model, extents in view mm. Also lists Revit link instances visible in
//    the view.
// Parameters:
//   beyondMm: coordinate along the side axis (mm from view origin); default = outermost host grid end
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ElementsBeyond
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"])); string side = (string)args["side"];
        XYZ o = v.Origin, r = v.RightDirection, u = v.UpDirection;
        Func<XYZ, double> along = p => (side == "right" || side == "left") ? (p - o).DotProduct(r) * MM : (p - o).DotProduct(u) * MM;
        bool pos = side == "right" || side == "up";
        double lim;
        if (args["beyondMm"] != null) lim = (double)args["beyondMm"];
        else
        {
            var vals = new List<double>();
            foreach (Grid g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)))
            {
                Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
                if (c != null) { vals.Add(along(c.GetEndPoint(0))); vals.Add(along(c.GetEndPoint(1))); }
            }
            lim = pos ? vals.Max() : vals.Min();
        }
        var rows = new List<object>();
        foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
        {
            if (e.Category == null || e is Grid) continue;
            BoundingBoxXYZ bb = null; try { bb = e.get_BoundingBox(v); } catch { }
            if (bb == null) continue;
            var pts = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, 0), new XYZ(bb.Max.X, bb.Min.Y, 0) }.Select(along).ToList();
            double ext = pos ? pts.Max() : pts.Min();
            if (pos ? ext <= lim + 1000 : ext >= lim - 1000) continue;
            string style = (e as CurveElement)?.LineStyle?.Name ?? doc.GetElement(e.GetTypeId())?.Name;
            rows.Add(new { Id = e.Id.IntegerValue, Category = e.Category.Name, Class = e.GetType().Name, Style = style, ViewSpecific = e.ViewSpecific, OwnerView = e.OwnerViewId.IntegerValue, ExtentMm = Math.Round(ext), FromMm = Math.Round(pos ? pts.Min() : pts.Max()) });
        }
        var links = new FilteredElementCollector(doc, v.Id).OfClass(typeof(RevitLinkInstance)).Select(l => l.Name).ToList();
        var imports = new FilteredElementCollector(doc, v.Id).OfClass(typeof(ImportInstance)).Select(l => l.Name + (l.ViewSpecific ? " (view)" : "")).ToList();
        return new { View = v.Name, LimitMm = Math.Round(lim), Count = rows.Count, Rows = rows.Take(80), Links = links, Imports = imports };
    }
}
