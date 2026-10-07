/* mcp-tool
{
  "description": "ONE plan view: linear dims at view-frame positions, each on real geometry found there (walls, rails, stairs, grids). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "typeName": {
        "type": "string"
      },
      "dims": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "name": {
              "type": "string"
            },
            "measure": {
              "type": "string",
              "enum": [
                "right",
                "up"
              ]
            },
            "positions": {
              "type": "array",
              "description": "numbers (mm) or {mm, id, src: 'view' | '3d'} (src: take the reference from…"
            },
            "lineMm": {
              "type": "number"
            },
            "candidateIds": {
              "type": "array",
              "items": {
                "type": "number"
              },
              "description": "look only in these elements (plus their rails)"
            },
            "use3D": {
              "type": "boolean",
              "description": "also take the 3D faces of every element and prefer them (railings: dims on…"
            }
          },
          "required": [
            "measure",
            "positions",
            "lineMm"
          ]
        }
      },
      "toleranceMm": {
        "type": "number",
        "description": "default 2"
      },
      "use3D": {
        "type": "boolean",
        "description": "default for every dim"
      },
      "refDims": {
        "type": "array",
        "items": {
          "type": "number"
        },
        "description": "existing dims whose references are reused first (e.g. a hand dim on railings)"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply",
          "undo"
        ]
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "mode"
    ]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ONE plan view: create linear dimensions whose witness lines sit at given positions, each referencing REAL geometry
//    found at that position (planar faces / lines of walls, railings incl. top rail and handrails, stair runs and
//    landings, floors, doors/windows, columns; grids). Positions are in the view frame used by stair_plan_audit /
//    view_crop_info: mm along the view's Right / Up from the view origin. Each dim: measure 'right' (positions are
//    Right values, line at Up = lineMm) or 'up' (positions are Up values, line at Right = lineMm); a position may be
//    a number or {mm, id} to take the reference from that element only. A position with no reference found within
//    toleranceMm fails the dim (nothing guessed, no detail lines). typeName is required (the project's check dim
//    type). mode preview (rolled back, reports found references and values) | apply (logPath) | undo (logPath:
//    deletes the created dims).
// Parameters:
//   dims[].positions: numbers (mm) or {mm, id, src: 'view' | '3d'} (src: take the reference from the plan geometry or
//    from the 3D faces only; walls: 3d is stable, Stairs: view is what displays)
//   dims[].use3D: also take the 3D faces of every element and prefer them (railings: dims on their plan lines are not
//    drawn)
//   refDims: existing dims (same view, same direction) whose references are reused first at their witness positions:
//    use a dim drawn by hand when API references are not drawn (railings)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class DimsAtPositions
{
    const double MM = 304.8;
    static XYZ O, Rg, Up;
    static Document D; static View V;
    static bool HiddenStyle(GeometryObject g)
    {
        if (V == null || g.GraphicsStyleId == ElementId.InvalidElementId) return false;
        var gs = D.GetElement(g.GraphicsStyleId) as GraphicsStyle;
        var c = gs?.GraphicsStyleCategory;
        if (c == null) return false;
        if (V.GetCategoryHidden(c.Id)) return true;
        return c.Parent != null && V.GetCategoryHidden(c.Parent.Id);
    }
    static double VX(XYZ p) => (p - O).DotProduct(Rg) * MM;
    static double VY(XYZ p) => (p - O).DotProduct(Up) * MM;

    class Cand { public double Pos; public Reference Ref; public int Id; public string Kind; }

    static readonly BuiltInCategory[] Cats =
    {
        BuiltInCategory.OST_Walls, BuiltInCategory.OST_StairsRailing, BuiltInCategory.OST_RailingTopRail, BuiltInCategory.OST_RailingHandRail,
        BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsRuns, BuiltInCategory.OST_StairsLandings, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns, BuiltInCategory.OST_Grids
    };

    static void Walk(GeometryElement ge, Transform tr, XYZ axis, int id, string cat, List<Cand> outList)
    {
        if (ge == null) return;
        foreach (var g in ge)
        {
            if (HiddenStyle(g)) continue; // e.g. "<Above> Top Rails" hidden by the template: a dim on it is created but never drawn
            if (g is Solid s)
            {
                foreach (Face f in s.Faces)
                {
                    var pf = f as PlanarFace; if (pf == null || pf.Reference == null) continue;
                    var n = tr.OfVector(pf.FaceNormal);
                    if (Math.Abs(n.DotProduct(axis)) < 0.999) continue;
                    var p = tr.OfPoint(pf.Origin);
                    outList.Add(new Cand { Pos = axis.IsAlmostEqualTo(Rg) || axis.IsAlmostEqualTo(Rg.Negate()) ? VX(p) : VY(p), Ref = pf.Reference, Id = id, Kind = cat + " face" });
                }
            }
            else if (g is Line l && l.Reference != null)
            {
                var d = tr.OfVector(l.Direction);
                if (Math.Abs(d.DotProduct(axis)) > 0.001) continue; // must be perpendicular to the measured direction
                var p = tr.OfPoint(l.GetEndPoint(0));
                outList.Add(new Cand { Pos = axis.IsAlmostEqualTo(Rg) || axis.IsAlmostEqualTo(Rg.Negate()) ? VX(p) : VY(p), Ref = l.Reference, Id = id, Kind = cat + " line" });
            }
            else if (g is GeometryInstance gi)
            {
                // instance faces keep usable references in most families; fall back to the symbol geometry
                var before = outList.Count;
                Walk(gi.GetInstanceGeometry(), tr, axis, id, cat, outList);
                if (outList.Count == before) Walk(gi.GetSymbolGeometry(), tr.Multiply(gi.Transform), axis, id, cat, outList);
            }
        }
    }

    static List<Cand> Candidates(Document doc, View v, XYZ axis, IEnumerable<Element> els, bool use3D)
    {
        var res = new List<Cand>();
        var opt = new Options { ComputeReferences = true, View = v, IncludeNonVisibleObjects = false };
        foreach (var e in els)
        {
            string cat = e.Category?.Name ?? "?";
            if (e is Grid gr)
            {
                if (gr.Curve is Line gl && Math.Abs(gl.Direction.DotProduct(axis)) < 0.001)
                {
                    var p = gl.GetEndPoint(0);
                    res.Add(new Cand { Pos = axis.IsAlmostEqualTo(Rg) || axis.IsAlmostEqualTo(Rg.Negate()) ? VX(p) : VY(p), Ref = new Reference(gr), Id = e.Id.IntegerValue, Kind = "grid " + gr.Name });
                }
                continue;
            }
            int before = res.Count;
            try { Walk(e.get_Geometry(opt), Transform.Identity, axis, e.Id.IntegerValue, cat, res); } catch { }
            // stair runs / landings give plan curves without references: fall back to their 3D faces (risers, landing edges)
            if (res.Count == before || use3D)
                try { Walk(e.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine }), Transform.Identity, axis, e.Id.IntegerValue, cat + " (3D)", res); } catch { }
        }
        return res;
    }

    static IEnumerable<Element> WithRails(Document doc, Element e)
    {
        yield return e;
        if (e is Railing rl)
        {
            Element tr = null; try { tr = doc.GetElement(rl.TopRail); } catch { }
            if (tr != null) yield return tr;
            IList<ElementId> hs = null; try { hs = rl.GetHandRails(); } catch { }
            if (hs != null) foreach (var h in hs) { var he = doc.GetElement(h); if (he != null) yield return he; }
        }
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); var gone = new List<int>();
            using (var t = new Transaction(doc, "Undo dims at positions"))
            {
                t.Start();
                foreach (var id in ids) if (doc.GetElement(new ElementId(id)) != null) { doc.Delete(new ElementId(id)); gone.Add(id); }
                t.Commit();
            }
            return new { Mode = "undo", Deleted = gone };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection; D = doc; V = v;
        string typeName = (string)args["typeName"];
        if (string.IsNullOrEmpty(typeName)) return new { Error = "typeName is required (the project's check dim type)" };
        var dimType = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(t => t.Name == typeName && t.StyleType == DimensionStyleType.Linear);
        if (dimType == null) return new { Error = "no linear dimension type named '" + typeName + "'" };
        double tol = args.Value<double?>("toleranceMm") ?? 2;
        double z = v.GenLevel != null ? v.GenLevel.ProjectElevation : O.Z;

        var visible = new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType()
            .Where(e => e.Category != null && Cats.Contains((BuiltInCategory)e.Category.Id.IntegerValue)).ToList();
        var extra = new List<Element>();
        foreach (var rl in visible.OfType<Railing>()) extra.AddRange(WithRails(doc, rl).Skip(1));
        var all = visible.Concat(extra).GroupBy(e => e.Id.IntegerValue).Select(g => g.First()).ToList();

        var created = new List<int>(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Dims at positions"))
        {
            t.Start();
            foreach (var dj in (JArray)args["dims"] ?? new JArray())
            {
                string name = (string)dj["name"] ?? "dim";
                bool right = ((string)dj["measure"] ?? "right").ToLower() == "right";
                var axis = right ? Rg : Up;
                double lineMm = (double)dj["lineMm"];
                IEnumerable<Element> pool = all;
                if (dj["candidateIds"] is JArray cids)
                {
                    var want = new HashSet<int>(cids.Select(x => (int)x));
                    pool = all.Where(e => want.Contains(e.Id.IntegerValue)).SelectMany(e => WithRails(doc, e)).GroupBy(e => e.Id.IntegerValue).Select(g => g.First());
                }
                bool use3D = dj.Value<bool?>("use3D") ?? args.Value<bool?>("use3D") ?? false;
                var cands = Candidates(doc, v, axis, pool, use3D);
                // references of existing dims (e.g. drawn by hand in the UI) are known to display: offer them first
                foreach (var rid in ((JArray)dj["refDims"] ?? (JArray)args["refDims"] ?? new JArray()).Select(x => (int)x))
                {
                    var rd = doc.GetElement(new ElementId(rid)) as Dimension;
                    if (rd == null || !(rd.Curve is Line rl) || Math.Abs(rl.Direction.DotProduct(axis)) < 0.999) continue;
                    var refs = rd.References; var pts = new List<XYZ>();
                    if (rd.NumberOfSegments > 0)
                    {
                        var segs = rd.Segments.Cast<DimensionSegment>().ToList();
                        pts.Add(segs[0].Origin - rl.Direction * ((segs[0].Value ?? 0) / 2));
                        foreach (var sg in segs) pts.Add(sg.Origin + rl.Direction * ((sg.Value ?? 0) / 2));
                    }
                    else { pts.Add(rd.Origin - rl.Direction * ((rd.Value ?? 0) / 2)); pts.Add(rd.Origin + rl.Direction * ((rd.Value ?? 0) / 2)); }
                    for (int i = 0; i < Math.Min(refs.Size, pts.Count); i++)
                    {
                        var r = refs.get_Item(i);
                        cands.Add(new Cand { Pos = right ? VX(pts[i]) : VY(pts[i]), Ref = r, Id = r.ElementId.IntegerValue, Kind = "ref of dim " + rid });
                    }
                }
                var ra = new ReferenceArray(); var used = new List<string>(); bool ok = true; var posList = new List<double>();
                foreach (var pj in (JArray)dj["positions"])
                {
                    double mm; int? onlyId = null; string src = null;
                    if (pj.Type == JTokenType.Object) { mm = (double)pj["mm"]; onlyId = pj["id"] != null ? (int?)(int)pj["id"] : null; src = (string)pj["src"]; } else mm = (double)pj;
                    var hit = cands.Where(c => Math.Abs(c.Pos - mm) <= tol && (onlyId == null || c.Id == onlyId)
                            && (src == null || (src == "3d") == c.Kind.Contains("(3D)")))
                        .OrderBy(c => c.Kind.StartsWith("ref of dim") ? 0 : 1).ThenBy(c => use3D && !c.Kind.StartsWith("grid") && !c.Kind.Contains("(3D)") ? 1 : 0).ThenBy(c => Math.Abs(c.Pos - mm)).ThenBy(c => c.Kind.StartsWith("grid") ? 0 : c.Kind.EndsWith("face") ? 1 : 2).FirstOrDefault();
                    if (hit == null)
                    {
                        var near = cands.OrderBy(c => Math.Abs(c.Pos - mm)).Take(3).Select(c => c.Kind + " " + c.Id + " @" + Math.Round(c.Pos, 1)).ToList();
                        errors.Add(name + ": nothing to reference at " + mm + " (nearest: " + string.Join("; ", near) + ")"); ok = false; break;
                    }
                    ra.Append(hit.Ref); posList.Add(mm); used.Add(Math.Round(hit.Pos, 1) + " " + hit.Kind + " " + hit.Id);
                }
                if (!ok) continue;
                double a0 = posList.Min() - 500, a1 = posList.Max() + 500;
                Func<double, double, XYZ> P = (r, u) => O + Rg * (r / MM) + Up * (u / MM) + XYZ.BasisZ * (z - O.Z);
                var ln = right ? Line.CreateBound(P(a0, lineMm), P(a1, lineMm)) : Line.CreateBound(P(lineMm, a0), P(lineMm, a1));
                try
                {
                    var d = doc.Create.NewDimension(v, ln, ra, dimType);
                    created.Add(d.Id.IntegerValue);
                    string vals = d.NumberOfSegments > 0 ? string.Join("|", d.Segments.Cast<DimensionSegment>().Select(s => s.ValueString)) : d.ValueString;
                    done.Add(new { Name = name, Id = d.Id.IntegerValue, Values = vals, References = used });
                }
                catch (Exception e) { errors.Add(name + ": " + e.Message); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, Type = typeName, Done = done, Errors = errors };
    }
}
