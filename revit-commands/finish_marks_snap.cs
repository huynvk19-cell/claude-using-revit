/* mcp-tool
{
  "description": "ONE section: put the leader end of finish marks on the slab they name: F.. on the floor/landing top below, C.. on the soffit above. survey | preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "mode": { "type": "string", "enum": ["survey", "preview", "apply", "undo"] },
      "logPath": { "type": "string" },
      "paramName": { "type": "string", "description": "code parameter of the finish marks (drafting-profile.md)" },
      "ids": { "type": "array", "items": { "type": "number" }, "description": "only these marks" },
      "excludeStairIds": { "type": "array", "items": { "type": "number" } },
      "toleranceMm": { "type": "number", "description": "end already on the surface within this, default 5" }
    },
    "required": ["viewId", "mode", "paramName"]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Stair sections (drafting-stair-section-parallel.md LB4 / LB5): finish marks are Generic Annotations with a code
//    parameter. F.. (floor finish) → the leader end goes on the TOP face of the landing / floor right below the head;
//    C.. (ceiling / soffit) → on the BOTTOM face right above the head. Surfaces: horizontal faces of the host Stairs
//    (not excluded) and Floors crossing the section plane. The end keeps its x (clamped onto the slab); the leader is
//    made orthogonal (straight vertical when the end is under / over the head, else one elbow at the head height).
//    Marks with other codes (W..) are left alone. survey/preview change nothing (preview rolls back) | apply (logPath:
//    old ends) | undo (logPath).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class FinishMarksSnap
{
    const double MM = 304.8;
    class S { public double Z, X0, X1; public bool Top; public string Of; }
    class SL { public XYZ O, N; public double X0, X1, Y0, Y1; public string Of; }
    class Old { public int Id; public int Index; public double[] End; public double[] Elbow; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "survey", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var olds = JsonConvert.DeserializeObject<List<Old>>(File.ReadAllText(logPath)); var n = 0;
            using (var t = new Transaction(doc, "Undo finish marks snap"))
            {
                t.Start();
                foreach (var o in olds)
                {
                    var a = doc.GetElement(new ElementId(o.Id)) as AnnotationSymbol; if (a == null) continue;
                    var ld = a.GetLeaders().Cast<Leader>().ElementAtOrDefault(o.Index); if (ld == null) continue;
                    ld.End = new XYZ(o.End[0], o.End[1], o.End[2]); if (o.Elbow != null) ld.Elbow = new XYZ(o.Elbow[0], o.Elbow[1], o.Elbow[2]); n++;
                }
                t.Commit();
            }
            return new { Mode = "undo", Restored = n };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection, D = v.ViewDirection;
        Func<XYZ, double> VX = p => (p - O).DotProduct(R) * MM, VY = p => (p - O).DotProduct(U) * MM, DP = p => (p - O).DotProduct(D) * MM;
        Func<double, double, double, XYZ> P = (x, y, depth) => O + R * (x / MM) + U * (y / MM) + D * (depth / MM);
        string pName = (string)args["paramName"]; if (string.IsNullOrEmpty(pName)) return new { Error = "paramName (the code parameter of the finish marks, from drafting-profile.md) is required" };
        double tol = (double?)args["toleranceMm"] ?? 5;
        var excl = new HashSet<int>(((JArray)args["excludeStairIds"] ?? new JArray()).Select(x => (int)x));
        var only = args["ids"] != null ? new HashSet<int>(((JArray)args["ids"]).Select(x => (int)x)) : null;

        // horizontal surfaces crossing the section plane
        var surf = new List<S>(); var slopes = new List<SL>();
        var hosts = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().Where(e => e is Stairs && !excl.Contains(e.Id.IntegerValue)).ToList();
        hosts.AddRange(new FilteredElementCollector(doc, v.Id).OfClass(typeof(Floor)).ToElements());
        foreach (var e in hosts)
        {
            var st = new Stack<GeometryElement>(); st.Push(e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
            while (st.Count > 0)
            {
                var ge = st.Pop(); if (ge == null) continue;
                foreach (var g in ge)
                {
                    if (g is GeometryInstance gi) { st.Push(gi.GetInstanceGeometry()); continue; }
                    var so = g as Solid; if (so == null) continue;
                    foreach (Face f in so.Faces)
                    {
                        var pf = f as PlanarFace; if (pf == null) continue;
                        if (pf.FaceNormal.Z < -0.1 && pf.FaceNormal.Z > -0.99 && e is Stairs)   // sloped soffit of a flight
                        {
                            var sp = new List<XYZ>(); foreach (EdgeArray ea in pf.EdgeLoops) foreach (Edge ed in ea) sp.AddRange(ed.Tessellate());
                            if (sp.Count > 0 && Math.Abs(pf.FaceNormal.DotProduct(D)) < 0.01) slopes.Add(new SL { O = pf.Origin, N = pf.FaceNormal, X0 = sp.Min(VX), X1 = sp.Max(VX), Y0 = sp.Min(VY), Y1 = sp.Max(VY), Of = "flight soffit of Stairs " + e.Id.IntegerValue });
                            continue;
                        }
                        if (Math.Abs(pf.FaceNormal.Z) < 0.999) continue;
                        var pts = new List<XYZ>(); foreach (EdgeArray ea in pf.EdgeLoops) foreach (Edge ed in ea) pts.AddRange(ed.Tessellate());
                        if (pts.Count == 0 || pts.Min(DP) > 1 || pts.Max(DP) < -1) continue;
                        surf.Add(new S { Z = VY(pts[0]), X0 = pts.Min(VX), X1 = pts.Max(VX), Top = pf.FaceNormal.Z > 0, Of = e.Category.Name + " " + e.Id.IntegerValue });
                    }
                }
            }
        }

        var rows = new List<object>(); var log = new List<Old>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Finish marks snap"))
        {
            t.Start();
            foreach (var a in new FilteredElementCollector(doc, v.Id).OfClass(typeof(FamilyInstance)).OfType<AnnotationSymbol>())
            {
                if (only != null && !only.Contains(a.Id.IntegerValue)) continue;
                var code = a.LookupParameter(pName)?.AsString(); if (string.IsNullOrEmpty(code)) continue;
                char k = char.ToUpper(code[0]); if (k != 'F' && k != 'C') continue;
                var head = (a.Location as LocationPoint)?.Point; if (head == null) continue;
                double hx = VX(head), hy = VY(head);
                var lds = a.GetLeaders().Cast<Leader>().ToList();
                if (lds.Count == 0) { rows.Add(new { Id = a.Id.IntegerValue, Code = code, Head = Math.Round(hx) + "," + Math.Round(hy), Status = "no leader" }); continue; }
                for (int i = 0; i < lds.Count; i++)
                {
                    var ld = lds[i]; double ex = VX(ld.End), ey = VY(ld.End), dep = DP(ld.End);
                    // C.. leader drawn horizontally (end at the head height): it points at the sloped soffit of the flight
                    if (k == 'C' && Math.Abs(ey - hy) < 30 && Math.Abs(ex - hx) > 200)
                    {
                        double dir = Math.Sign(ex - hx), best = double.NaN; string of = null;
                        foreach (var sl in slopes)
                        {
                            if (hy < sl.Y0 - 1 || hy > sl.Y1 + 1) continue;
                            var p0 = P(0, hy, dep); double kx = sl.N.DotProduct(R) / MM; if (Math.Abs(kx) < 1e-12) continue;
                            double x = -sl.N.DotProduct(p0 - sl.O) / kx;
                            if (x < sl.X0 - 1 || x > sl.X1 + 1 || Math.Sign(x - hx) != dir) continue;
                            if (double.IsNaN(best) || Math.Abs(x - hx) < Math.Abs(best - hx)) { best = x; of = sl.Of; }
                        }
                        if (double.IsNaN(best)) { rows.Add(new { Id = a.Id.IntegerValue, Code = code, Leader = i, End = Math.Round(ex) + "," + Math.Round(ey), Status = "horizontal leader: no sloped soffit at the head height" }); continue; }
                        bool ok2 = Math.Abs(best - ex) <= tol;
                        rows.Add(new { Id = a.Id.IntegerValue, Code = code, Leader = i, Head = Math.Round(hx) + "," + Math.Round(hy), End = Math.Round(ex) + "," + Math.Round(ey), Target = of, NewEnd = Math.Round(best) + "," + Math.Round(hy), Status = ok2 ? "OK" : "off by " + Math.Round(Math.Abs(best - ex)) + " mm (x)" });
                        if (ok2 || mode == "survey") continue;
                        try
                        {
                            log.Add(new Old { Id = a.Id.IntegerValue, Index = i, End = new[] { ld.End.X, ld.End.Y, ld.End.Z }, Elbow = ld.Elbow == null ? null : new[] { ld.Elbow.X, ld.Elbow.Y, ld.Elbow.Z } });
                            ld.End = P(best, hy, dep); ld.Elbow = P((hx + best) / 2, hy, dep);
                        }
                        catch (Exception e2) { errors.Add(a.Id.IntegerValue + ": " + e2.Message); }
                        continue;
                    }
                    double lo = Math.Min(hx, ex) - 300, hi = Math.Max(hx, ex) + 300;
                    S s = k == 'F'
                        ? surf.Where(q => q.Top && q.Z <= hy + 50 && q.X1 >= lo && q.X0 <= hi).OrderByDescending(q => q.Z).ThenByDescending(q => Math.Min(q.X1, hi) - Math.Max(q.X0, lo)).FirstOrDefault()
                        : surf.Where(q => !q.Top && q.Z >= hy - 50 && q.X1 >= lo && q.X0 <= hi).OrderBy(q => q.Z).ThenByDescending(q => Math.Min(q.X1, hi) - Math.Max(q.X0, lo)).FirstOrDefault();
                    if (s == null) { rows.Add(new { Id = a.Id.IntegerValue, Code = code, Leader = i, End = Math.Round(ex) + "," + Math.Round(ey), Status = "no " + (k == 'F' ? "top" : "soffit") + " surface found" }); continue; }
                    double nx = Math.Max(s.X0 + 30, Math.Min(s.X1 - 30, ex)), ny = s.Z;
                    bool ok = Math.Abs(nx - ex) <= tol && Math.Abs(ny - ey) <= tol;
                    var row = new { Id = a.Id.IntegerValue, Code = code, Leader = i, Head = Math.Round(hx) + "," + Math.Round(hy), End = Math.Round(ex) + "," + Math.Round(ey), Target = s.Of + (s.Top ? " top " : " soffit ") + Math.Round(s.Z), NewEnd = Math.Round(nx) + "," + Math.Round(ny), Status = ok ? "OK" : "off by " + Math.Round(Math.Abs(ny - ey)) + " mm (z), " + Math.Round(Math.Abs(nx - ex)) + " mm (x)" };
                    rows.Add(row);
                    if (ok || mode == "survey") continue;
                    try
                    {
                        log.Add(new Old { Id = a.Id.IntegerValue, Index = i, End = new[] { ld.End.X, ld.End.Y, ld.End.Z }, Elbow = ld.Elbow == null ? null : new[] { ld.Elbow.X, ld.Elbow.Y, ld.Elbow.Z } });
                        var end = P(nx, ny, dep);
                        ld.End = end;
                        ld.Elbow = Math.Abs(nx - hx) < 1 ? P(nx, (hy + ny) / 2, dep) : P(nx, hy, dep);   // orthogonal: vertical, or along the head height then vertical
                    }
                    catch (Exception e) { errors.Add(a.Id.IntegerValue + ": " + e.Message); }
                }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(log)); }
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, Surfaces = surf.Count, Marks = rows, Errors = errors };
    }
}
