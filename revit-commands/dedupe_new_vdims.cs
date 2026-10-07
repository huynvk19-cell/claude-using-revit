/* mcp-tool
{
  "description": "ONE view: delete newly created vertical dims whose door/window already has another vertical dim. preview | apply (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "ids": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "fromLog": {
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
      "mode",
      "logPath"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// In one view, for newly created vertical dims (ids, or the 'Created' ids in a log file written by
//    elevation_opening_dims), delete those whose door/window already has another vertical dim (not in the new set)
//    referencing it. mode preview | apply (logPath gets the deleted ids with values).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class DedupeNewVdims
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var ids = (args["ids"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>();
        if (args["fromLog"] != null)
        {
            var j = JToken.Parse(File.ReadAllText((string)args["fromLog"]));
            var arr = j is JObject o ? (o["Created"] ?? o["created"]) : j;
            foreach (var t in (JArray)arr) ids.Add(t.Type == JTokenType.Object ? (int)t["Id"] : (int)t);
        }
        var newSet = new HashSet<int>(ids);
        Func<Element, bool> isOpening = e => e != null && e.Category != null && (e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows);
        var oldV = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(d => !newSet.Contains(d.Id.IntegerValue) && d.Curve is Line l && Math.Abs(l.Direction.Z) > 0.9).ToList();
        var covered = new HashSet<int>(oldV.SelectMany(d => d.References.Cast<Reference>()).Select(r => doc.GetElement(r.ElementId)).Where(isOpening).Select(e => e.Id.IntegerValue));
        var del = new List<object>(); var dbg = new List<string>();
        using (var t = new Transaction(doc, "Drop duplicate opening dims"))
        {
            t.Start();
            foreach (var id in ids)
            {
                var d = doc.GetElement(new ElementId(id)) as Dimension; if (d == null || !(d.Curve is Line l) || Math.Abs(l.Direction.Z) < 0.9) continue;
                var ops = d.References.Cast<Reference>().Select(r => doc.GetElement(r.ElementId)).Where(isOpening).ToList();
                var vals = d.NumberOfSegments == 0 ? new List<double> { Math.Round((d.Value ?? 0) * MM) } : d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();
                // position rule: an existing vertical dim with the same value(s) within the opening's width +-1500 mm
                bool posDup = false;
                foreach (var op in ops)
                {
                    var bb = op.get_BoundingBox(null); if (bb == null) continue;
                    var xs = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, 0), new XYZ(bb.Max.X, bb.Min.Y, 0) }.Select(p => (p - v.Origin).DotProduct(v.RightDirection)).ToList();
                    double x0 = xs.Min() - 1500 / MM, x1 = xs.Max() + 1500 / MM;
                    foreach (var od in oldV)
                    {
                        double ox = (((Line)od.Curve).Origin - v.Origin).DotProduct(v.RightDirection);
                        if (ox < x0 || ox > x1) continue;
                        var ov = od.NumberOfSegments == 0 ? new List<double> { Math.Round((od.Value ?? 0) * MM) } : od.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();
                        if (vals.All(x => ov.Any(y => Math.Abs(x - y) <= 60))) { posDup = true; break; }
                    }
                    if (posDup) break;
                    dbg.Add(id + " op " + op.Id.IntegerValue + " x " + Math.Round(x0*MM) + ".." + Math.Round(x1*MM) + " vals " + string.Join("/", vals));
                }
                if (ops.Count == 0) dbg.Add(id + " no opening refs: " + string.Join(",", d.References.Cast<Reference>().Select(r => doc.GetElement(r.ElementId)?.Category?.Name)));
                if (ops.Count > 0 && posDup)
                {
                    del.Add(new { Id = id, Mark = ops[0].get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), Values = vals });
                    doc.Delete(d.Id);
                }
            }
            if ((string)args["mode"] == "apply") { t.Commit(); File.WriteAllText((string)args["logPath"], JsonConvert.SerializeObject(del, Formatting.Indented)); } else t.RollBack();
        }
        return new { View = v.Name, Checked = ids.Count, Deleted = del.Count, Marks = string.Join(",", del.Select(x => ((dynamic)x).Mark)) };
    }
}
