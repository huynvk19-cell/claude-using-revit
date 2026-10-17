/* mcp-tool
{
  "description": "Read-only, ONE plan view: host floors / ramps / roofs seen in the view with their top faces (z range, slope, view-frame box).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "ids": { "type": "array", "items": { "type": "number" }, "description": "only these elements" }
    },
    "required": ["viewId"]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details ----
// For each host Floor / Ramp / landing / Roof visible in the view: every upward-facing face of its solid (normal.Z > 0.2):
//    view-frame box (Right / Up mm from View.Origin), z min / max of the face (mm, project), slope % and the
//    down-slope direction in the view frame. Use to find floor pieces at different levels and ramp ends (FL1).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class FloorLevelsProbe
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = doc.GetElement(new ElementId((int)args["viewId"])) as View;
        if (v == null) return new { Error = "no view" };
        HashSet<int> only = args["ids"] is JArray a && a.Count > 0 ? new HashSet<int>(a.Select(t => (int)t)) : null;
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        Func<XYZ, double[]> F = p => new[] { (p - O).DotProduct(R) * MM, (p - O).DotProduct(U) * MM };
        var cats = new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ramps, BuiltInCategory.OST_StairsLandings, BuiltInCategory.OST_Roofs };
        var res = new List<object>();
        var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
        foreach (var e in new FilteredElementCollector(doc, v.Id).WherePasses(new ElementMulticategoryFilter(cats)).WhereElementIsNotElementType())
        {
            if (only != null && !only.Contains(e.Id.IntegerValue)) continue;
            var faces = new List<object>();
            Action<GeometryElement> walk = null;
            walk = ge =>
            {
                if (ge == null) return;
                foreach (var g in ge)
                {
                    if (g is GeometryInstance gi) { walk(gi.GetInstanceGeometry()); continue; }
                    if (!(g is Solid so) || so.Faces.Size == 0) continue;
                    foreach (Face f in so.Faces)
                    {
                        var bb = f.GetBoundingBox();
                        var n = f.ComputeNormal(new UV((bb.Min.U + bb.Max.U) / 2, (bb.Min.V + bb.Max.V) / 2));
                        if (n.Z <= 0.2) continue;
                        var pts = new List<XYZ>();
                        foreach (EdgeArray ea in f.EdgeLoops) foreach (Edge ed in ea) pts.AddRange(ed.Tessellate());
                        if (pts.Count == 0) continue;
                        var fs = pts.Select(F).ToList();
                        double slope = Math.Sqrt(Math.Max(0, 1 - n.Z * n.Z)) / n.Z * 100;
                        var down = new XYZ(n.X, n.Y, 0);
                        faces.Add(new
                        {
                            Box = new[] { Math.Round(fs.Min(x => x[0])), Math.Round(fs.Min(x => x[1])), Math.Round(fs.Max(x => x[0])), Math.Round(fs.Max(x => x[1])) },
                            Zmin = Math.Round(pts.Min(p => p.Z) * MM), Zmax = Math.Round(pts.Max(p => p.Z) * MM),
                            SlopePct = Math.Round(slope, 2),
                            DownRU = down.GetLength() < 1e-6 ? null : new[] { Math.Round(down.Normalize().DotProduct(R), 2), Math.Round(down.Normalize().DotProduct(U), 2) },
                            AreaM2 = Math.Round(f.Area * 0.09290304, 2)
                        });
                    }
                }
            };
            try { walk(e.get_Geometry(opt)); } catch { }
            res.Add(new { Id = e.Id.IntegerValue, Cat = e.Category?.Name, Type = doc.GetElement(e.GetTypeId())?.Name, Top = faces });
        }
        return new { View = v.Name, Elements = res };
    }
}
