/* mcp-tool
{
  "description": "Read-only: every linear dim of a type (or ids): view, sheet, values, line position, text offset, nearest grid. outPath for JSON.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "typeName": {
        "type": "string"
      },
      "ids": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "outPath": {
        "type": "string"
      }
    }
  },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: every linear dimension of a given type (or given ids): owner view, view type/scale, sheet, segment count,
//    values, line position, text offset along the line from its default centre (sheet mm, single-segment only),
//    nearest grid line to the text (sheet mm), sheet rectangle, view override colour. Optionally writes JSON to
//    outPath.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DimsByTypeInventory
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        IEnumerable<Dimension> dims;
        if (args["ids"] is JArray ids) dims = ids.Select(t => doc.GetElement(new ElementId((int)t)) as Dimension).Where(d => d != null);
        else dims = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => !(d is SpotDimension) && d.DimensionType?.Name == (string)args["typeName"]);
        var vpByView = new Dictionary<int, Viewport>();
        foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()) vpByView[vp.ViewId.IntegerValue] = vp;
        var res = new List<object>();
        foreach (var d in dims)
        {
            var v = doc.GetElement(d.OwnerViewId) as View; if (v == null) continue;
            double sc = v.Scale;
            var ln = d.Curve as Line;
            string sheet = null; double[] rect = null;
            if (vpByView.TryGetValue(v.Id.IntegerValue, out var vp))
            {
                sheet = ((ViewSheet)doc.GetElement(vp.SheetId)).SheetNumber;
                try
                {
                    var bb = d.get_BoundingBox(v);
                    var m2p = v.GetModelToProjectionTransforms().First().GetModelToProjectionTransform(); var p2s = vp.GetProjectionToSheetTransform();
                    var pts = new[] { bb.Min, bb.Max }.Select(p => p2s.OfPoint(m2p.OfPoint(p))).ToList();
                    rect = new[] { Math.Round(pts.Min(p => p.X) * MM, 1), Math.Round(pts.Min(p => p.Y) * MM, 1), Math.Round(pts.Max(p => p.X) * MM, 1), Math.Round(pts.Max(p => p.Y) * MM, 1) };
                }
                catch { }
            }
            double? textOff = null, nearestGrid = null; string nearestName = null;
            if (d.NumberOfSegments == 0 && ln != null && d.TextPosition != null && d.Origin != null)
            {
                textOff = Math.Round((d.TextPosition - d.Origin).DotProduct(ln.Direction) * MM / sc, 1);
                // nearest grid crossing to the text along the line
                foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
                {
                    Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
                    if (!(c is Line gl) || Math.Abs(gl.Direction.DotProduct(ln.Direction)) > 0.01) continue;
                    double dist = (gl.Origin - d.TextPosition).DotProduct(ln.Direction) * MM / sc;
                    if (nearestGrid == null || Math.Abs(dist) < Math.Abs(nearestGrid.Value)) { nearestGrid = Math.Round(dist, 1); nearestName = g.Name; }
                }
            }
            var ogs = v.GetElementOverrides(d.Id);
            res.Add(new
            {
                Id = d.Id.IntegerValue, View = v.Name, ViewId = v.Id.IntegerValue, ViewType = v.ViewType.ToString(), Scale = v.Scale, Sheet = sheet,
                Segs = d.NumberOfSegments, Values = d.NumberOfSegments > 0 ? string.Join("|", d.Segments.Cast<DimensionSegment>().Select(s => s.ValueString)) : d.ValueString,
                Refs = d.References.Size, TextOffMm = textOff, NearestGridToTextMm = nearestGrid, NearestGrid = nearestName,
                Red = ogs.ProjectionLineColor.IsValid ? ogs.ProjectionLineColor.Red + "," + ogs.ProjectionLineColor.Green + "," + ogs.ProjectionLineColor.Blue : null,
                SheetRect = rect
            });
        }
        if (args["outPath"] != null) System.IO.File.WriteAllText((string)args["outPath"], Newtonsoft.Json.JsonConvert.SerializeObject(res));
        return new { Count = res.Count, Written = (string)args["outPath"] };
    }
}
