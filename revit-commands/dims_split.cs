/* mcp-tool
{
  "description": "Split a chain dim at every segment longer than maxMm into separate dims on the same line. preview | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "dimId": {
        "type": "number"
      },
      "maxMm": {
        "type": "number"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply"
        ]
      }
    },
    "required": [
      "viewId",
      "dimId",
      "maxMm",
      "mode"
    ]
  },
  "timeoutSeconds": 60,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Split a chain dimension at every segment longer than maxMm: each run of references between long segments becomes its
//    own dim on the same line and type (runs with fewer than 2 references are dropped); the original dim is deleted.
//    mode preview | apply. Reports the new ids and values.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DimsSplit
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var d = (Dimension)doc.GetElement(new ElementId((int)args["dimId"]));
        double max = (double)args["maxMm"];
        var refs = d.References.Cast<Reference>().ToList();
        var segs = d.Segments.Cast<DimensionSegment>().Select(s => (s.Value ?? 0) * MM).ToList();
        var groups = new List<List<Reference>> { new List<Reference> { refs[0] } };
        for (int i = 0; i < segs.Count; i++)
        {
            if (segs[i] > max) groups.Add(new List<Reference>());
            groups.Last().Add(refs[i + 1]);
        }
        var ln = (Line)d.Curve; var type = d.DimensionType;
        var res = new List<object>();
        using (var t = new Transaction(doc, "Split dim"))
        {
            t.Start();
            foreach (var g in groups.Where(g => g.Count >= 2))
            {
                var ra = new ReferenceArray();
                foreach (var r in g) { var gr = doc.GetElement(r.ElementId) as Grid; ra.Append(gr != null ? new Reference(gr) : r); }
                var nd = doc.Create.NewDimension(v, Line.CreateBound(ln.Origin - ln.Direction * 1000, ln.Origin + ln.Direction * 1000), ra, type);
                var vals = nd.NumberOfSegments == 0 ? new List<double> { Math.Round((nd.Value ?? 0) * MM) } : nd.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();
                res.Add(new { Id = nd.Id.IntegerValue, Values = string.Join(" | ", vals) });
            }
            doc.Delete(d.Id);
            if ((string)args["mode"] == "apply") t.Commit(); else t.RollBack();
        }
        return new { View = v.Name, Removed = (int)args["dimId"], Created = res };
    }
}
