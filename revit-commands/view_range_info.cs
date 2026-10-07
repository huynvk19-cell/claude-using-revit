/* mcp-tool
{
  "description": "Read-only: plan view range as absolute elevations (mm) and the Z range of given elements.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "integer"
      },
      "ids": {
        "type": "array",
        "items": {
          "type": "integer"
        }
      }
    },
    "required": [
      "viewId"
    ]
  },
  "timeoutSeconds": 30,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: plan view range (top, cut, bottom, view depth) as absolute elevations in mm, plus the levels used, and
//    for given element ids their Z range (bounding box, mm).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewRangeInfo
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = doc.GetElement(new ElementId((int)args["viewId"])) as ViewPlan;
        var vr = v.GetViewRange();
        var o = new JObject { ["view"] = v.Name, ["level"] = v.GenLevel?.Name + " " + Math.Round(v.GenLevel.ProjectElevation * MM) };
        foreach (PlanViewPlane p in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane })
        {
            var lid = vr.GetLevelId(p); var l = doc.GetElement(lid) as Level;
            o[p.ToString()] = l == null ? "unlimited / n.a. (" + lid.IntegerValue + ")" : l.Name + " + " + Math.Round(vr.GetOffset(p) * MM) + " = " + Math.Round((l.ProjectElevation + vr.GetOffset(p)) * MM);
        }
        var els = new JArray();
        if (args["ids"] != null)
            foreach (var j in (JArray)args["ids"])
            {
                var e = doc.GetElement(new ElementId((int)j)); var bb = e?.get_BoundingBox(null);
                els.Add(j + " " + e?.Category?.Name + (bb == null ? "" : " Z " + Math.Round(bb.Min.Z * MM) + " .. " + Math.Round(bb.Max.Z * MM)));
            }
        o["elements"] = els;
        return o;
    }
}
