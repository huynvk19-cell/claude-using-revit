/* mcp-tool
{
  "description": "ONE plan view: linear dims on railing edges built like a hand pick (drawn by Revit), walls, refs. probe | preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "integer"
      },
      "typeName": {
        "type": "string"
      },
      "toleranceMm": {
        "type": "number"
      },
      "dims": {
        "type": "array",
        "items": {
          "type": "object"
        }
      },
      "mode": {
        "type": "string",
        "enum": [
          "probe",
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
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ONE plan view: linear dims whose railing references are built like a hand pick (what Revit draws): '<top rail
//    UniqueId>:1:INSTANCE:<symbol line stable ref>' from the rail's view geometry (Options.View = the view).
//    Positions: {mm, rail: topRailId} | {mm, wall: wallId} (wall side face at mm) | {mm, ref: '<stable
//    representation>'} | {mm, dimId, index} (reuse a reference of an existing dim). measure 'up' (positions along the
//    view Up, line at Right = lineMm) or 'right'. mode probe (lists the rail lines near each position) | preview
//    (rolled back, reports values and whether drawn) | apply (logPath) | undo (logPath).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DimsRailRefs
{
    const double MM = 304.8;
    class Cand { public double Pos; public double Len; public Reference Ref; public string Stable; }

    static Dictionary<string, bool> validCache = new Dictionary<string, bool>();
    // a candidate edge is kept only if a test dim to a grid gives the expected value and is drawn
    static bool Valid(Document doc, View v, Cand c, bool alongUp, XYZ O, XYZ U, XYZ R)
    {
        var axis = alongUp ? U : R; var run = alongUp ? R : U;
        var grid = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>()
            .FirstOrDefault(g => g.Curve is Line gl && Math.Abs(gl.Direction.DotProduct(run)) > 0.999);
        if (grid == null) return true;
        double gpos = (((Line)grid.Curve).Origin - O).DotProduct(axis);
        bool ok = false;
        using (var st = new SubTransaction(doc))
        {
            st.Start();
            try
            {
                var ra = new ReferenceArray(); ra.Append(new Reference(grid)); ra.Append(Reference.ParseFromStableRepresentation(doc, c.Stable));
                var cb = v.CropBox; var o = cb.Transform.OfPoint((cb.Min + cb.Max) / 2);
                var d = doc.Create.NewDimension(v, Line.CreateBound(o, o + axis * 10), ra);
                doc.Regenerate();
                double val = (d.Value ?? -1) * 304.8;
                ok = d.get_BoundingBox(v) != null && Math.Abs(val - Math.Abs(c.Pos - gpos) * 304.8) < 1;
            }
            catch { ok = false; }
            st.RollBack();
        }
        return ok;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"]; string lp = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JArray.Parse(System.IO.File.ReadAllText(lp)).Select(x => new ElementId((int)x)).Where(i => doc.GetElement(i) != null).ToList();
            using (var t = new Transaction(doc, "Undo rail dims")) { t.Start(); doc.Delete(ids); t.Commit(); }
            return "deleted " + ids.Count;
        }
        var v = doc.GetElement(new ElementId((int)args["viewId"])) as View;
        double tol = (args["toleranceMm"] != null ? (double)args["toleranceMm"] : 2) / MM;
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        var dt = args["typeName"] != null ? new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["typeName"]) : null;
        var rows = new JArray(); var created = new JArray();
        var railCache = new Dictionary<string, List<Cand>>();

        Func<int, bool, List<Cand>> RailLines = (id, alongUp) =>
        {
            string key = id + (alongUp ? "u" : "r");
            if (railCache.ContainsKey(key)) return railCache[key];
            var list = new List<Cand>();
            var e = doc.GetElement(new ElementId(id));
            var ge = e?.get_Geometry(new Options { ComputeReferences = true, View = v, IncludeNonVisibleObjects = true });
            if (ge != null)
                foreach (var g in ge)
                {
                    var gi = g as GeometryInstance; if (gi == null) continue;
                    var T = gi.Transform;
                    Action<Curve, Reference> add = (c, rf) =>
                    {
                        var ln = c as Line; if (ln == null || rf == null) return;
                        var d = T.OfVector(ln.Direction);
                        // positions along Up -> lines must run along Right, and vice versa
                        var axis = alongUp ? U : R; var run = alongUp ? R : U;
                        if (Math.Abs(d.DotProduct(run)) < 0.999) return;
                        var p = T.OfPoint(ln.GetEndPoint(0));
                        string st;
                        try { st = e.UniqueId + ":1:INSTANCE:" + rf.ConvertToStableRepresentation(doc); if (!st.EndsWith(":LINEAR") && !st.EndsWith(":SURFACE")) st += ":LINEAR"; } catch { return; }
                        list.Add(new Cand { Pos = (p - O).DotProduct(axis), Len = ln.Length, Stable = st });
                    };
                    foreach (var sg in gi.GetSymbolGeometry())
                    {
                        if (sg is Curve cv) add(cv, cv.Reference);
                        else if (sg is Solid so) foreach (Edge ed in so.Edges) add(ed.AsCurve(), ed.Reference);
                    }
                }
            railCache[key] = list;
            return list;
        };

        using (var t = new Transaction(doc, "Rail dims"))
        {
            t.Start();
            var made = new List<Tuple<string, Dimension>>();
            foreach (JObject dj in (JArray)args["dims"])
            {
                string name = (string)dj["name"] ?? "dim";
                bool alongUp = ((string)dj["measure"] ?? "up") == "up";
                var axis = alongUp ? U : R; var across = alongUp ? R : U;
                double lineMm = (double)dj["lineMm"];
                var ra = new ReferenceArray(); var errs = new List<string>(); var probe = new JArray();
                foreach (JObject pj in (JArray)dj["positions"])
                {
                    double mm = (double)pj["mm"]; double pos = mm / MM;
                    Reference rf = null;
                    if (pj["rail"] != null)
                    {
                        var cands = RailLines((int)pj["rail"], alongUp);
                        var near = cands.Where(c => Math.Abs(c.Pos - pos) < tol).OrderByDescending(c => c.Len).ToList();
                        if (mode == "probe")
                            probe.Add(mm + " rail " + pj["rail"] + ": " + string.Join(", ", cands.Where(c => Math.Abs(c.Pos - pos) < 100 / MM).OrderBy(c => Math.Abs(c.Pos - pos)).Take(6).Select(c => Math.Round(c.Pos * MM, 1) + " L" + Math.Round(c.Len * MM) + " [" + c.Stable.Substring(c.Stable.IndexOf(":1:INSTANCE:") + 12) + "]")));
                        foreach (var c in near)
                        {
                            if (Valid(doc, v, c, alongUp, O, U, R)) { rf = Reference.ParseFromStableRepresentation(doc, c.Stable); break; }
                        }
                    }
                    else if (pj["wall"] != null)
                    {
                        var w = doc.GetElement(new ElementId((int)pj["wall"])) as HostObject;
                        foreach (var side in new[] { ShellLayerType.Interior, ShellLayerType.Exterior })
                        {
                            foreach (var fr in HostObjectUtils.GetSideFaces(w, side))
                            {
                                var pf = w.GetGeometryObjectFromReference(fr) as PlanarFace;
                                if (pf == null) continue;
                                if (Math.Abs(Math.Abs(pf.FaceNormal.DotProduct(axis)) - 1) > 1e-3) continue;
                                if (Math.Abs((pf.Origin - O).DotProduct(axis) - pos) < tol) { rf = fr; break; }
                            }
                            if (rf != null) break;
                        }
                    }
                    else if (pj["ref"] != null) rf = Reference.ParseFromStableRepresentation(doc, (string)pj["ref"]);
                    else if (pj["dimId"] != null)
                    {
                        var d0 = doc.GetElement(new ElementId((int)pj["dimId"])) as Dimension;
                        rf = d0?.References.get_Item((int)pj["index"]);
                        if (rf != null) rf = Reference.ParseFromStableRepresentation(doc, rf.ConvertToStableRepresentation(doc));
                    }
                    if (rf == null) errs.Add("no reference at " + mm); else ra.Append(rf);
                }
                if (mode == "probe") { rows.Add(new JObject { ["dim"] = name, ["probe"] = probe, ["errors"] = new JArray(errs) }); continue; }
                if (errs.Count > 0) { rows.Add(name + ": " + string.Join("; ", errs)); continue; }
                var o = O + across * (lineMm / MM);
                var line = Line.CreateBound(o, o + axis * 10);
                Dimension nd = null;
                try { nd = dt != null ? doc.Create.NewDimension(v, line, ra, dt) : doc.Create.NewDimension(v, line, ra); }
                catch (Exception ex) { rows.Add(name + ": not created: " + ex.Message); continue; }
                made.Add(Tuple.Create(name, nd));
            }
            doc.Regenerate();
            foreach (var m in made)
            {
                var nd = m.Item2; var bb = nd.get_BoundingBox(v);
                string vals = nd.NumberOfSegments == 0 ? Math.Round((nd.Value ?? 0) * MM).ToString()
                    : string.Join("|", Enumerable.Range(0, nd.NumberOfSegments).Select(i => Math.Round((nd.Segments.get_Item(i).Value ?? 0) * MM).ToString()));
                double span = bb == null ? 0 : Math.Max((bb.Max - bb.Min).DotProduct(R) * 0 + Math.Abs((bb.Max - bb.Min).X), Math.Abs((bb.Max - bb.Min).Y)) * MM;
                rows.Add(new JObject { ["dim"] = m.Item1, ["id"] = nd.Id.IntegerValue, ["values"] = vals, ["boxSpanMm"] = Math.Round(span) });
                created.Add(nd.Id.IntegerValue);
            }
            if (mode == "apply") t.Commit(); else t.RollBack();
        }
        if (mode == "apply" && lp != null) System.IO.File.WriteAllText(lp, created.ToString());
        return rows;
    }
}
