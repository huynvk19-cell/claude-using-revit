/* mcp-tool
{
  "description": "Read-only: for plan views, list the filled regions owned by each view (id, type, outline in mm, bbox) and the columns (Structural Columns + Columns, host and every loaded link) that cross the view's cut plane inside its crop: link, id, type, section outline at the cut plane (mm, host coords). Pairs each region with the column section it overlaps most. outPath writes the full JSON and returns a summary.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "integer" } },
      "outPath": { "type": "string" },
      "verbose": { "type": "boolean" }
    },
    "required": ["viewIds"]
  },
  "timeoutSeconds": 600
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ColumnRegionsSurvey
{
    const double MM = 304.8;
    static double R(double v) { return Math.Round(v * MM, 0); }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var outv = new JArray();
        var summary = new JArray();
        var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
            .Where(l => l.GetLinkDocument() != null).ToList();
        foreach (var jid in (JArray)args["viewIds"])
        {
            var v = doc.GetElement(new ElementId((int)jid)) as ViewPlan;
            if (v == null) { summary.Add(jid + ": not a plan view"); continue; }
            var lvl = v.GenLevel;
            var vr = v.GetViewRange();
            double cutZ = doc.GetElement(vr.GetLevelId(PlanViewPlane.CutPlane)) is Level cl ? cl.ProjectElevation + vr.GetOffset(PlanViewPlane.CutPlane) : lvl.ProjectElevation + 4;
            // crop in model XY (axis aligned, plan views)
            var cb = v.CropBox; var tr = cb.Transform;
            var cpts = new[] { tr.OfPoint(cb.Min), tr.OfPoint(cb.Max), tr.OfPoint(new XYZ(cb.Min.X, cb.Max.Y, 0)), tr.OfPoint(new XYZ(cb.Max.X, cb.Min.Y, 0)) };
            double x0 = cpts.Min(p => p.X), x1 = cpts.Max(p => p.X), y0 = cpts.Min(p => p.Y), y1 = cpts.Max(p => p.Y);

            var regs = new JArray();
            var regList = new FilteredElementCollector(doc, v.Id).OfClass(typeof(FilledRegion)).Cast<FilledRegion>().ToList();
            foreach (var fr in regList)
            {
                var loops = new JArray(); var allP = new List<XYZ>();
                foreach (var cl2 in fr.GetBoundaries())
                {
                    var lp = new JArray();
                    foreach (var c in cl2) { var p = c.GetEndPoint(0); allP.Add(p); lp.Add(new JArray(R(p.X), R(p.Y))); }
                    loops.Add(lp);
                }
                regs.Add(new JObject
                {
                    ["id"] = fr.Id.IntegerValue,
                    ["type"] = doc.GetElement(fr.GetTypeId())?.Name,
                    ["bbox"] = new JArray(R(allP.Min(p => p.X)), R(allP.Min(p => p.Y)), R(allP.Max(p => p.X)), R(allP.Max(p => p.Y))),
                    ["loops"] = loops
                });
            }

            var cols = new JArray();
            var sources = new List<Tuple<string, Document, Transform>> { Tuple.Create("HOST", doc, Transform.Identity) };
            foreach (var l in links) sources.Add(Tuple.Create(l.GetLinkDocument().Title, l.GetLinkDocument(), l.GetTotalTransform()));
            foreach (var s in sources)
            {
                var inv = s.Item3.Inverse;
                var cats = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns });
                foreach (var e in new FilteredElementCollector(s.Item2).WhereElementIsNotElementType().WherePasses(cats))
                {
                    var bb = e.get_BoundingBox(null); if (bb == null) continue;
                    // transform bbox corners to host
                    var hp = new List<XYZ>();
                    foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z }) hp.Add(s.Item3.OfPoint(new XYZ(x, y, z)));
                    if (hp.Max(p => p.X) < x0 || hp.Min(p => p.X) > x1 || hp.Max(p => p.Y) < y0 || hp.Min(p => p.Y) > y1) continue;
                    bool crosses = !(hp.Max(p => p.Z) < cutZ || hp.Min(p => p.Z) > cutZ);
                    // section at cut plane
                    var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ.Negate(), new XYZ(0, 0, cutZ));
                    var planeL = Plane.CreateByNormalAndOrigin(inv.OfVector(XYZ.BasisZ.Negate()), inv.OfPoint(new XYZ(0, 0, cutZ)));
                    var loops = new JArray(); double area = 0;
                    foreach (var so in Solids(e))
                    {
                        Solid cut = null;
                        try { cut = BooleanOperationsUtils.CutWithHalfSpace(so, planeL); } catch { }
                        if (cut == null || cut.Volume < 1e-9) continue;
                        foreach (Face f in cut.Faces)
                        {
                            var pf = f as PlanarFace; if (pf == null) continue;
                            var n = s.Item3.OfVector(pf.FaceNormal);
                            if (Math.Abs(n.Z) < 0.999) continue;
                            var o = s.Item3.OfPoint(pf.Origin); if (Math.Abs(o.Z - cutZ) > 1e-4) continue;
                            area += pf.Area;
                            foreach (CurveLoop cl3 in pf.GetEdgesAsCurveLoops())
                            {
                                var lp = new JArray();
                                foreach (var c in cl3) { var p = s.Item3.OfPoint(c.GetEndPoint(0)); lp.Add(new JArray(R(p.X), R(p.Y))); }
                                loops.Add(lp);
                            }
                        }
                    }
                    cols.Add(new JObject
                    {
                        ["src"] = s.Item1,
                        ["id"] = e.Id.IntegerValue,
                        ["cat"] = e.Category?.Name,
                        ["type"] = (s.Item2.GetElement(e.GetTypeId()) as ElementType)?.FamilyName + " : " + s.Item2.GetElement(e.GetTypeId())?.Name,
                        ["bbox"] = new JArray(R(hp.Min(p => p.X)), R(hp.Min(p => p.Y)), R(hp.Max(p => p.X)), R(hp.Max(p => p.Y))),
                        ["areaM2"] = Math.Round(area * 0.09290304, 3), ["crossesCut"] = crosses, ["z"] = new JArray(R(hp.Min(p => p.Z)), R(hp.Max(p => p.Z))),
                        ["loops"] = loops
                    });
                }
            }
            outv.Add(new JObject
            {
                ["viewId"] = v.Id.IntegerValue, ["view"] = v.Name, ["cutZ"] = R(cutZ),
                ["crop"] = new JArray(R(x0), R(y0), R(x1), R(y1)),
                ["regions"] = regs, ["columns"] = cols
            });
            summary.Add(v.Id.IntegerValue + " | " + v.Name + " | regions " + regs.Count + " | columns " + cols.Count);
        }
        if (args["outPath"] != null) System.IO.File.WriteAllText((string)args["outPath"], outv.ToString());
        if (args["verbose"] != null && (bool)args["verbose"]) return outv;
        return summary;
    }

    static IEnumerable<Solid> Solids(Element e)
    {
        var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
        var ge = e.get_Geometry(opt); if (ge == null) yield break;
        foreach (var s in Walk(ge)) yield return s;
    }
    static IEnumerable<Solid> Walk(GeometryElement ge)
    {
        foreach (var g in ge)
        {
            if (g is Solid s && s.Volume > 1e-9) yield return s;
            else if (g is GeometryInstance gi) foreach (var x in Walk(gi.GetInstanceGeometry())) yield return x;
        }
    }
}
