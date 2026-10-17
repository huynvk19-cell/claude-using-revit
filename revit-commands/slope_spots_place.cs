/* mcp-tool
{
  "description": "Spot slopes: types (direction, units) | ONE plan view: place on sloped floor / roof / ramp faces (copy of a plan spot slope, re-hosted). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" }, "description": "[{elementId, right, up, typeName}] point on the sloped face" },
      "sourceId": { "type": "number", "description": "plan spot slope to copy (default: one in this view, else any plan)" },
      "mode": { "type": "string", "enum": ["types", "preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// The Revit 2023 API has no call to create a spot slope (only NewSpotElevation / NewSpotCoordinate). This command
//    copies an existing spot slope (sourceId, or one owned by the view, or any in the project) to the given point and
//    lets Revit re-host it on the face under that point, then sets the type (typeName, a Spot Slopes type that already
//    exists). items: {elementId (floor / roof / ramp floor / landing whose sloped top face is under the point), right,
//    up (view frame mm, as floor_levels_probe / view_elem_boxes ViewBox), typeName}. A copy that does not land on
//    elementId (no sloped face under the point, or Revit kept the old host) is deleted and reported. Result per item:
//    Id, On (host now referenced), SlopePct of the face. mode preview (rolled back) |
//    apply (logPath: new ids) | undo (logPath: deletes them). The source must be on a plan view (a copy cannot leave
//    the view plane, so spot slopes of sections do not work): none yet → place one by hand on any plan (Annotate >
//    Spot Slope) as the seed.
// mode types (read-only, no viewId): every Spot Slopes type with Direction (Up / Down), Representation, Units (the
//    slope unit and accuracy of the type, or "project" when it uses the project units, with those) and how many
//    instances use it, plus the number of plan spot slopes that can serve as a source.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class SlopeSpotsPlace
{
    const double MM = 304.8;

    // sloped (or flat) top face of e straight under the plan point; returns the face and its z there
    static PlanarFace FaceAt(Element e, View v, XYZ probe, out double zAt)
    {
        PlanarFace best = null; zAt = double.NegativeInfinity;
        foreach (var opt in new[] { new Options { ComputeReferences = true, View = v }, new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine } })
        {
            var st = new Stack<GeometryElement>(); st.Push(e.get_Geometry(opt));
            while (st.Count > 0)
            {
                var ge = st.Pop(); if (ge == null) continue;
                foreach (var g in ge)
                {
                    if (g is GeometryInstance gi) { st.Push(gi.GetInstanceGeometry()); continue; }
                    if (!(g is Solid s)) continue;
                    foreach (Face f in s.Faces)
                    {
                        var pf = f as PlanarFace; if (pf == null || pf.Reference == null || pf.FaceNormal.Z < 0.2) continue;
                        var n = pf.FaceNormal; var o = pf.Origin;
                        double z = o.Z - (n.X * (probe.X - o.X) + n.Y * (probe.Y - o.Y)) / n.Z;
                        var pr = pf.Project(new XYZ(probe.X, probe.Y, z));
                        if (pr != null && pr.Distance < 1e-4 && z > zAt) { best = pf; zAt = z; }
                    }
                }
            }
            if (best != null) return best;
        }
        return null;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); var gone = new List<int>();
            using (var t = new Transaction(doc, "Undo slope spots"))
            {
                t.Start();
                foreach (var id in ids) if (doc.GetElement(new ElementId(id)) != null) { doc.Delete(new ElementId(id)); gone.Add(id); }
                t.Commit();
            }
            return new { Mode = "undo", Deleted = gone };
        }
        if (mode == "types")
        {
            var all = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_SpotSlopes).WhereElementIsNotElementType().Cast<SpotDimension>().ToList();
            Func<FormatOptions, string> fmt = fo => fo.GetUnitTypeId().TypeId.Split(':').Last().Split('-').First() + " / " + fo.Accuracy;
            var proj = doc.GetUnits().GetFormatOptions(SpecTypeId.Slope);
            var list = new FilteredElementCollector(doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().Where(x => x.StyleType == DimensionStyleType.SpotSlope).Select(x =>
            {
                FormatOptions fo = null; try { fo = x.GetUnitsFormatOptions(); } catch { }
                bool useProj = fo == null || fo.UseDefault;
                var dir = x.get_Parameter(BuiltInParameter.SPOT_SLOPE_SLOPE_DIRECTION); var rep = x.get_Parameter(BuiltInParameter.SPOT_SLOPE_SLOPE_REPRESENTATION);
                return new { Id = x.Id.IntegerValue, Name = x.Name, Direction = dir != null ? dir.AsValueString() : null, Representation = rep != null ? rep.AsValueString() : null,
                    Units = useProj ? "project: " + fmt(proj) : fmt(fo), Used = all.Count(s => s.GetTypeId() == x.Id) };
            }).ToList();
            int plan = all.Count(s => { var ov = doc.GetElement(s.OwnerViewId) as View; return ov is ViewPlan; });
            return new { Types = list, PlanSources = plan, OtherViews = all.Count - plan };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        Func<double, double, double, XYZ> P = (r, u, z) => new XYZ(O.X, O.Y, z) + R * (r / MM) + U * (u / MM);

        var slopes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_SpotSlopes).WhereElementIsNotElementType().Cast<SpotDimension>().ToList();
        // the copy must stay in the plane of the view: source = a spot slope of a plan view (same view first)
        Func<SpotDimension, bool> inPlan = x => { var ov = doc.GetElement(x.OwnerViewId) as View; return ov != null && ov.ViewDirection.CrossProduct(v.ViewDirection).GetLength() < 1e-6; };
        SpotDimension src = args["sourceId"] != null ? doc.GetElement(new ElementId((int)args["sourceId"])) as SpotDimension
            : slopes.FirstOrDefault(x => x.OwnerViewId == v.Id) ?? slopes.FirstOrDefault(inPlan);
        if (src == null || !inPlan(src)) return new { Error = "no spot slope on a plan view to copy (" + slopes.Count + " on sections / elevations): place one by hand on any plan (Annotate > Spot Slope), then run again" };
        var srcView = (View)doc.GetElement(src.OwnerViewId);
        var types = new FilteredElementCollector(doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().Where(x => x.StyleType == DimensionStyleType.SpotSlope).ToList();

        var created = new List<int>(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Slope spots"))
        {
            t.Start();
            int i = 0;
            foreach (var it in (JArray)args["items"] ?? new JArray())
            {
                i++;
                var e = doc.GetElement(new ElementId((int)it["elementId"]));
                if (e == null) { errors.Add(i + ": element " + it["elementId"] + " not found"); continue; }
                string typeName = (string)it["typeName"];
                var st = types.FirstOrDefault(x => x.Name == typeName);
                if (st == null) { errors.Add(i + ": no spot slope type '" + typeName + "' (have: " + string.Join(", ", types.Select(x => x.Name)) + ")"); continue; }
                double r = (double)it["right"], u = (double)it["up"];
                var probe = P(r, u, 0);
                double z; var face = FaceAt(e, v, probe, out z);
                if (face == null) { errors.Add(i + ": no top face of " + e.Id.IntegerValue + " under the point"); continue; }
                var n = face.FaceNormal; double pct = Math.Round(Math.Sqrt(Math.Max(0, 1 - n.Z * n.Z)) / n.Z * 100, 2);
                var target = new XYZ(probe.X, probe.Y, z);
                var sub = new SubTransaction(doc); sub.Start();
                try
                {
                    var o = src.Origin; var d = new XYZ(target.X - o.X, target.Y - o.Y, 0); var move = Transform.CreateTranslation(d);
                    ICollection<ElementId> copied = src.OwnerViewId == v.Id
                        ? ElementTransformUtils.CopyElement(doc, src.Id, d)
                        : ElementTransformUtils.CopyElements(srcView, new List<ElementId> { src.Id }, v, move, new CopyPasteOptions());
                    doc.Regenerate();
                    var sd = copied.Select(id => doc.GetElement(id)).OfType<SpotDimension>().FirstOrDefault();
                    if (sd == null) { errors.Add(i + ": copy failed (Revit deleted it: nothing to host under the point?)"); sub.RollBack(); continue; }
                    var hosts = new List<int>();
                    try { foreach (Reference rf in sd.References) hosts.Add(rf.ElementId.IntegerValue); } catch { }
                    if (!hosts.Contains(e.Id.IntegerValue)) { errors.Add(i + ": copy stayed on " + string.Join("/", hosts) + ", not on " + e.Id.IntegerValue); sub.RollBack(); continue; }
                    if (sd.GetTypeId() != st.Id) sd.ChangeTypeId(st.Id);
                    doc.Regenerate();
                    sub.Commit();
                    created.Add(sd.Id.IntegerValue);
                    done.Add(new { Item = i, Id = sd.Id.IntegerValue, On = e.Id.IntegerValue, SlopePct = pct, Type = typeName });
                }
                catch (Exception ex) { errors.Add(i + ": " + ex.Message); if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack(); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, Source = src.Id.IntegerValue, SourceView = srcView.Name, Done = done, Errors = errors };
    }
}
