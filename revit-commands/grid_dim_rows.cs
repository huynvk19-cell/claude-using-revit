/* mcp-tool
{
  "description": "Read-only: per view, dims whose references are all grids: direction, position, segments, grid names.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      }
    },
    "required": [
      "viewIds"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: for each view, the linear dimensions whose references are all host grids (grid chains / overall grid
//    dims): id, type, direction (H = runs along view right, V = along up), position across in mm from the view origin,
//    segment count, total, and the grid names referenced.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class GridDimRows
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document; var res = new List<object>();
        foreach (var vid in ((JArray)args["viewIds"]).Select(x => (int)x))
        {
            var v = (View)doc.GetElement(new ElementId(vid)); var rows = new List<object>();
            foreach (Dimension d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
            {
                if (d.OwnerViewId != v.Id || !(d.Curve is Line ln)) continue;
                var refs = new List<string>(); bool allGrid = true;
                foreach (Reference r in d.References)
                {
                    var e = doc.GetElement(r.ElementId) as Grid;
                    if (e == null || r.LinkedElementId != ElementId.InvalidElementId) { allGrid = false; break; }
                    refs.Add(e.Name);
                }
                if (!allGrid || refs.Count < 2) continue;
                var dir = ln.Direction; bool h = Math.Abs(dir.DotProduct(v.RightDirection)) > Math.Abs(dir.DotProduct(v.UpDirection));
                double across = (ln.Origin - v.Origin).DotProduct(h ? v.UpDirection : v.RightDirection) * MM;
                double total = d.NumberOfSegments == 0 ? (d.Value ?? 0) * MM : d.Segments.Cast<DimensionSegment>().Sum(s => s.Value ?? 0) * MM;
                rows.Add(new { Id = d.Id.IntegerValue, Type = d.DimensionType.Name, Dir = h ? "H" : "V", AcrossMm = Math.Round(across), Segs = d.NumberOfSegments, TotalMm = Math.Round(total), Grids = string.Join(",", refs) });
            }
            res.Add(new { View = v.Name, ViewId = vid, Rows = rows.OrderBy(r => ((dynamic)r).Dir).ThenBy(r => ((dynamic)r).AcrossMm) });
        }
        return res;
    }
}
