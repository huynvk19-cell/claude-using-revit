/* mcp-tool
{
  "description": "Read-only, ONE section/elevation: top of a railing's top rail (or handrail) at given view x; returns view-frame up for tag leader ends.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" }, "description": "[{railingId, x, upMin, upMax}] (view frame mm; up range picks one storey)" }
    },
    "required": ["viewId", "items"]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// For each railing: its top rail (Railing.TopRail), else its first handrail, else the railing itself. A vertical line
//    at view x, placed at the depth of the rail near x (average depth of the rail's edge points within ±60 mm of x),
//    is intersected with the rail solids; the highest point is returned as Up (view frame, as view_elem_boxes ViewBox
//    / annot_place 'up') and as elevation-free model Z (mm). Use it for railing tag leader ends (LB2) and LA5 checks.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RailTopAt
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection, D = v.ViewDirection;
        Func<XYZ, double> VX = p => (p - O).DotProduct(R) * MM, VY = p => (p - O).DotProduct(U) * MM, DP = p => (p - O).DotProduct(D) * MM;
        var rows = new List<object>();
        foreach (var it in (JArray)args["items"])
        {
            int rid = (int)it["railingId"]; double x = (double)it["x"]; double? zLo = (double?)it["upMin"], zHi = (double?)it["upMax"];
            var rail = doc.GetElement(new ElementId(rid)) as Railing;
            Element top = null;
            if (rail != null) { if (rail.TopRail != ElementId.InvalidElementId) top = doc.GetElement(rail.TopRail); if (top == null) { var hr = rail.GetHandRails(); if (hr != null && hr.Count > 0) top = doc.GetElement(hr.First()); } }
            if (top == null) top = doc.GetElement(new ElementId(rid));
            if (top == null) { rows.Add(new { RailingId = rid, Error = "not found" }); continue; }
            var solids = new List<Solid>(); var st = new Stack<GeometryElement>(); st.Push(top.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
            while (st.Count > 0) { var ge = st.Pop(); if (ge == null) continue; foreach (var g in ge) { if (g is GeometryInstance gi) st.Push(gi.GetInstanceGeometry()); else if (g is Solid s && s.Volume > 0) solids.Add(s); } }
            var near = solids.SelectMany(s => s.Edges.Cast<Edge>().SelectMany(e => e.Tessellate())).Where(p => Math.Abs(VX(p) - x) < 60 && (zLo == null || VY(p) >= zLo) && (zHi == null || VY(p) <= zHi)).ToList();
            if (near.Count == 0) { rows.Add(new { RailingId = rid, X = x, Error = "rail does not reach this x" }); continue; }
            double best = double.NegativeInfinity;
            foreach (var dep in new[] { near.Average(DP), near.Min(DP), near.Max(DP) })
            {
                double zmin = zLo ?? near.Min(VY) - 2000, zmax = zHi ?? near.Max(VY) + 2000;
                var a = O + R * (x / MM) + U * (zmin / MM) + D * (dep / MM); var b = O + R * (x / MM) + U * (zmax / MM) + D * (dep / MM);
                var ln = Line.CreateBound(a, b);
                foreach (var s in solids)
                {
                    try { var res = s.IntersectWithCurve(ln, new SolidCurveIntersectionOptions()); for (int i = 0; i < res.SegmentCount; i++) { var c = res.GetCurveSegment(i); best = Math.Max(best, Math.Max(VY(c.GetEndPoint(0)), VY(c.GetEndPoint(1)))); } } catch { }
                }
                if (!double.IsNegativeInfinity(best)) break;
            }
            rows.Add(new { RailingId = rid, Rail = top.Id.IntegerValue, X = x, TopUp = double.IsNegativeInfinity(best) ? (double?)null : Math.Round(best, 1), NearUp = Math.Round(near.Max(VY), 1) });
        }
        return new { View = v.Name, Rows = rows };
    }
}
