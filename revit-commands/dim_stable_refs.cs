/* mcp-tool
{
  "description": "Read-only: stable references of dims (element, position) and top rail / handrail ids of railings.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "dimIds": {
        "type": "array",
        "items": {
          "type": "integer"
        }
      },
      "railingIds": {
        "type": "array",
        "items": {
          "type": "integer"
        }
      }
    }
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: (1) for dimension ids, each reference's stable representation, element id / UniqueId / category and the
//    position of that reference along the dim; (2) for railing ids, their top rail / handrail element ids and
//    UniqueIds. Use to rebuild hand-picked railing references (which Revit draws) on other railings of the same type.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DimStableRefs
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var dims = new JArray();
        if (args["dimIds"] != null)
            foreach (var j in (JArray)args["dimIds"])
            {
                var d = doc.GetElement(new ElementId((int)j)) as Dimension; if (d == null) continue;
                var refs = new JArray();
                foreach (Reference r in d.References)
                {
                    var e = doc.GetElement(r.ElementId);
                    refs.Add(new JObject { ["stable"] = r.ConvertToStableRepresentation(doc), ["id"] = r.ElementId.IntegerValue, ["uid"] = e?.UniqueId, ["cat"] = e?.Category?.Name });
                }
                dims.Add(new JObject { ["dim"] = d.Id.IntegerValue, ["view"] = d.View?.Name, ["refs"] = refs });
            }
        var rails = new JArray();
        if (args["railingIds"] != null)
            foreach (var j in (JArray)args["railingIds"])
            {
                var rl = doc.GetElement(new ElementId((int)j)) as Railing; if (rl == null) continue;
                var tr = doc.GetElement(rl.TopRail);
                var hr = rl.GetHandRails().Select(h => doc.GetElement(h)).Where(x => x != null).Select(x => x.Id.IntegerValue + " " + x.UniqueId).ToList();
                var bb = tr?.get_BoundingBox(null);
                rails.Add(new JObject
                {
                    ["railing"] = rl.Id.IntegerValue, ["topRail"] = tr?.Id.IntegerValue, ["topRailUid"] = tr?.UniqueId,
                    ["topRailBox"] = bb == null ? null : new JArray(Math.Round(bb.Min.X * MM), Math.Round(bb.Min.Y * MM), Math.Round(bb.Max.X * MM), Math.Round(bb.Max.Y * MM), Math.Round(bb.Min.Z * MM), Math.Round(bb.Max.Z * MM)),
                    ["handRails"] = new JArray(hr)
                });
            }
        return new JObject { ["dims"] = dims, ["railings"] = rails };
    }
}
