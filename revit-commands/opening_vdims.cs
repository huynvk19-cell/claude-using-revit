/* mcp-tool
{
  "description": "Elevation/section: vertical dim for given doors/windows from family Bottom/Top (optionally from a level) on a line at xMm. preview | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "items": {
        "type": "array",
        "items": {
          "type": "object"
        }
      },
      "typeName": {
        "type": "string"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply"
        ]
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "viewId",
      "items",
      "mode",
      "logPath"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Elevation/section view: add a vertical dimension for given doors/windows from the family's Bottom and Top references
//    (optionally starting at a Level: Level -> bottom -> top), on a vertical line at xMm (mm along the view's right
//    direction from the view origin). items [{openingId, xMm, levelId?}]. typeName = dimension type. mode preview
//    (rolled back, reports values) | apply (writes logPath with created ids).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class OpeningVdims
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var type = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["typeName"]);
        var res = new List<object>(); var ids = new List<int>();
        using (var t = new Transaction(doc, "Opening vertical dims"))
        {
            t.Start();
            foreach (JObject it in (JArray)args["items"])
            {
                var fi = doc.GetElement(new ElementId((int)it["openingId"])) as FamilyInstance;
                if (fi == null) { res.Add(new { it, Error = "not a family instance" }); continue; }
                var ra = new ReferenceArray();
                if (it["templateDimId"] != null)
                {   // copy the references of a template dim on another instance of the same family type: swap the instance's UniqueId
                    var td = (Dimension)doc.GetElement(new ElementId((int)it["templateDimId"]));
                    var tOp = doc.GetElement(new ElementId((int)it["templateOpeningId"]));
                    if (it["levelRefDimId"] != null)
                    {   // Level reference taken from a dim of this view (a level reference copied from another view is dropped)
                        var ld = (Dimension)doc.GetElement(new ElementId((int)it["levelRefDimId"]));
                        foreach (Reference r in ld.References) if (doc.GetElement(r.ElementId) is Level) { ra.Append(r); break; }
                    }
                    foreach (Reference r in td.References)
                    {
                        if (it["levelRefDimId"] != null && doc.GetElement(r.ElementId) is Level) continue;
                        string s = r.ConvertToStableRepresentation(doc);
                        if (r.ElementId == tOp.Id) s = s.Replace(tOp.UniqueId, fi.UniqueId);
                        ra.Append(Reference.ParseFromStableRepresentation(doc, s));
                    }
                }
                else
                {
                    var bot = fi.GetReferences(FamilyInstanceReferenceType.Bottom).FirstOrDefault();
                    var top = fi.GetReferences(FamilyInstanceReferenceType.Top).FirstOrDefault();
                    if (bot == null || top == null) { res.Add(new { Opening = fi.Id.IntegerValue, Error = "no Bottom/Top reference" }); continue; }
                    if (it["levelId"] != null) { var lv = doc.GetElement(new ElementId((int)it["levelId"])) as Level; if (lv != null) ra.Append(lv.GetPlaneReference()); }
                    ra.Append(bot); if (!(it.Value<bool?>("bottomOnly") ?? false)) ra.Append(top);
                }
                var bb = fi.get_BoundingBox(null);
                double zMid = (bb.Min.Z + bb.Max.Z) / 2;
                var p = v.Origin + v.RightDirection * ((double)it["xMm"] / MM);
                XYZ p0 = new XYZ(p.X, p.Y, zMid - 10), p1 = new XYZ(p.X, p.Y, zMid + 10);
                try
                {
                    var d = doc.Create.NewDimension(v, Line.CreateBound(p0, p1), ra, type);
                    var vals = d.NumberOfSegments == 0 ? new List<double> { Math.Round((d.Value ?? 0) * MM) } : d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();
                    ids.Add(d.Id.IntegerValue);
                    res.Add(new { Opening = fi.Id.IntegerValue, Mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), Dim = d.Id.IntegerValue, Values = vals });
                }
                catch (Exception e) { res.Add(new { Opening = fi.Id.IntegerValue, Error = e.Message }); }
            }
            if ((string)args["mode"] == "apply") { t.Commit(); File.WriteAllText((string)args["logPath"], JsonConvert.SerializeObject(new { View = v.Name, Created = ids }, Formatting.Indented)); }
            else t.RollBack();
        }
        return res;
    }
}
