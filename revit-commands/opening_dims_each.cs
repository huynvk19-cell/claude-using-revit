/* mcp-tool
{
  "description": "Elevation/section: door/window dims one opening at a time (Q1-Q5). audit (once per view, cachePath) | preview | apply (openingId).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "mode": {
        "type": "string",
        "enum": [
          "audit",
          "preview",
          "apply"
        ]
      },
      "openingId": {
        "type": "number"
      },
      "cachePath": {
        "type": "string",
        "description": "JSON written by audit (visible opening ids); required for preview/apply"
      },
      "dimTypeName": {
        "type": "string",
        "description": "required: the project check dimension type (drafting-profile)"
      },
      "nominalFamilies": {
        "type": "array",
        "items": {
          "type": "string"
        },
        "description": "family name parts sized by named refs LEFT/RIGHT/TOP, default ['ROLL UP']"
      },
      "gridNearDist": {
        "type": "number",
        "description": "mm, default 12000"
      },
      "only": {
        "type": "string",
        "enum": [
          "V",
          "H"
        ],
        "description": "add only this direction"
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "viewId",
      "mode"
    ]
  },
  "timeoutSeconds": 600,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Door/window dims ONE OPENING AT A TIME on an elevation/section view (rules Q1-Q5). mode 'audit' (read-only, once per
//    view): finds the openings really visible (multi-ray test), writes them to cachePath and lists, per opening,
//    whether it already has a VERTICAL dim that references it with host level -> sill -> head and a HORIZONTAL dim
//    that references both its edges. mode 'preview' | 'apply' with openingId: adds only what that opening is missing -
//    vertical host level -> bottom -> top (bottom dropped within 150 mm of the level) on the nearest clear line beside
//    it, horizontal nearest grid -> edge -> edge -> nearest grid on the nearest clear line just above the head or
//    below the sill inside its storey. Obstacles: visible openings (from the cache), dims and their texts, tags, text
//    notes, spots, cut slabs/beams. logPath collects created ids (undo: dims_edit delete).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class OpeningDimsEach
{
    const double MM = 304.8;
    class R
    {
        public double X0, X1, Y0, Y1; public string W;
        public R(double a, double b, double c, double d, string w = "") { X0 = Math.Min(a, b); X1 = Math.Max(a, b); Y0 = Math.Min(c, d); Y1 = Math.Max(c, d); W = w; }
        public bool Hits(R o) { return X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1; }
    }
    class Op
    {
        public FamilyInstance Fi; public double X0, X1, Z0, Z1, LvZ; public Reference L, Rr, B, T; public Level Lv; public bool NoHost, Frame;
        public string Name { get { return (Fi.Symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString() ?? "") + "#" + (Fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? ""); } }
    }
    class Ex { public Dimension D; public bool V; public double Line; public List<double> Bnd = new List<double>(); public HashSet<int> Els = new HashSet<int>(); public List<R> Texts = new List<R>(); }

    static XYZ O, Rg, Up, Vd; static View v; static Document doc; static double h, P1;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg); }
    static double VY(XYZ p) { return (p - O).DotProduct(Up); }
    static double LZ(Level l) { return VY(new XYZ(O.X, O.Y, l.ProjectElevation)); }
    static R Box(BoundingBoxXYZ bb, string w)
    {
        if (bb == null) return null;
        var pts = new List<XYZ>();
        foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                    pts.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
        return new R(pts.Min(VX), pts.Max(VX), pts.Min(VY), pts.Max(VY), w);
    }
    static R TextBox(bool vertical, double x, double y, string txt, double hh)
    {
        double w = Math.Max(1, (txt ?? "").Length) * 0.6 * hh + 0.3 * hh;
        return vertical ? new R(x - 1.4 * hh, x - 0.1 * hh, y - w / 2, y + w / 2, "text") : new R(x - w / 2, x + w / 2, y + 0.1 * hh, y + 1.4 * hh, "text");
    }
    static Reference FirstRef(FamilyInstance fi, FamilyInstanceReferenceType t) { var l = fi.GetReferences(t); return l.Count > 0 ? l[0] : null; }
    static Reference Named(FamilyInstance fi, string name)
    {
        foreach (FamilyInstanceReferenceType rt in Enum.GetValues(typeof(FamilyInstanceReferenceType)))
        {
            IList<Reference> refs; try { refs = fi.GetReferences(rt); } catch { continue; }
            foreach (var r in refs) { string n = null; try { n = fi.GetReferenceName(r); } catch { } if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return r; }
        }
        return null;
    }

    // outer frame from the front-most planar faces (as drawn in the view)
    static void Frame(FamilyInstance fi, Op op)
    {
        var faces = new List<Tuple<bool, double, double, Reference, double>>();
        Action<GeometryElement, Transform> walk = null;
        walk = (ge, t) =>
        {
            if (ge == null) return;
            foreach (var obj in ge)
            {
                if (obj is Solid s)
                    foreach (Face f in s.Faces)
                    {
                        var pf = f as PlanarFace; if (pf == null || pf.Reference == null) continue;
                        var n = t.OfVector(pf.FaceNormal); var bb = pf.GetBoundingBox();
                        var c = t.OfPoint(pf.Evaluate((bb.Min + bb.Max) / 2)); double depth = (c - O).DotProduct(Vd);
                        if (Math.Abs(n.DotProduct(Rg)) > 0.99) faces.Add(Tuple.Create(true, VX(c), depth, pf.Reference, pf.Area));
                        else if (Math.Abs(n.DotProduct(Up)) > 0.99) faces.Add(Tuple.Create(false, VY(c), depth, pf.Reference, pf.Area));
                    }
                else if (obj is GeometryInstance gi) walk(gi.GetSymbolGeometry(), t.Multiply(gi.Transform));
            }
        };
        walk(fi.get_Geometry(new Options { ComputeReferences = true, View = v }), Transform.Identity);
        if (faces.Count == 0) return;
        double front = faces.Max(f => f.Item3);
        var fr = faces.Where(f => f.Item3 >= front - 400 / MM).ToList();
        var vf = fr.Where(f => f.Item1).ToList(); var hf = fr.Where(f => !f.Item1).ToList();
        if (vf.Count < 2 || hf.Count < 2) return;
        var l = vf.OrderBy(f => f.Item2).ThenByDescending(f => f.Item5).First();
        var r = vf.OrderByDescending(f => f.Item2).ThenByDescending(f => f.Item5).First();
        var b = hf.OrderBy(f => f.Item2).ThenByDescending(f => f.Item5).First();
        var t2 = hf.OrderByDescending(f => f.Item2).ThenByDescending(f => f.Item5).First();
        if (r.Item2 - l.Item2 < 100 / MM || t2.Item2 - b.Item2 < 100 / MM) return;
        op.L = l.Item4; op.Rr = r.Item4; op.B = b.Item4; op.T = t2.Item4;
        op.X0 = l.Item2; op.X1 = r.Item2; op.Z0 = b.Item2; op.Z1 = t2.Item2; op.Frame = true;
    }

    static Op Build(FamilyInstance fi, List<Level> levels, List<string> nominal)
    {
        var op = new Op { Fi = fi };
        bool nom = nominal.Any(s => (fi.Symbol.FamilyName ?? "").ToUpperInvariant().Contains(s));
        if (!nom) Frame(fi, op);
        if (!op.Frame)
        {
            var pr = Box(fi.get_BoundingBox(v) ?? fi.get_BoundingBox(null), "");
            op.X0 = pr.X0; op.X1 = pr.X1; op.Z0 = pr.Y0; op.Z1 = pr.Y1;
            op.L = (nom ? Named(fi, "LEFT") : null) ?? FirstRef(fi, FamilyInstanceReferenceType.Left);
            op.Rr = (nom ? Named(fi, "RIGHT") : null) ?? FirstRef(fi, FamilyInstanceReferenceType.Right);
            op.B = FirstRef(fi, FamilyInstanceReferenceType.Bottom);
            op.T = (nom ? Named(fi, "TOP") : null) ?? FirstRef(fi, FamilyInstanceReferenceType.Top);
        }
        // host level (instance Level), shown in the view
        var hid = fi.LevelId;
        if (hid == null || hid == ElementId.InvalidElementId)
            foreach (var bp in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM })
            { var p = fi.get_Parameter(bp); if (p != null && p.StorageType == StorageType.ElementId && p.AsElementId() != ElementId.InvalidElementId) { hid = p.AsElementId(); break; } }
        op.Lv = levels.FirstOrDefault(l => l.Id == hid);
        if (op.Lv == null) { op.NoHost = true; op.Lv = levels.Where(l => LZ(l) <= op.Z0 + 20 / MM).LastOrDefault(); }
        op.LvZ = op.Lv != null ? LZ(op.Lv) : op.Z0;
        if (nom)
            using (var tq = new Transaction(doc, "probe"))
            {   // measure where the named references really are
                tq.Start();
                try
                {
                    if (op.L != null && op.Rr != null)
                    {
                        var ra = new ReferenceArray(); ra.Append(op.L); ra.Append(op.Rr);
                        var a = O + Up * ((op.Z0 + op.Z1) / 2);
                        var d = doc.Create.NewDimension(v, Line.CreateBound(a, a + Rg * 10), ra);
                        double w = d.Value ?? 0, cx = VX(d.Origin); op.X0 = cx - w / 2; op.X1 = cx + w / 2;
                    }
                    if (op.Lv != null && op.T != null)
                    {
                        var rb = new ReferenceArray(); rb.Append(op.Lv.GetPlaneReference()); rb.Append(op.T);
                        var b = O + Rg * ((op.X0 + op.X1) / 2);
                        var d2 = doc.Create.NewDimension(v, Line.CreateBound(b, b + Up * 10), rb);
                        op.Z1 = op.LvZ + (d2.Value ?? 0); if (op.B == null) op.Z0 = op.LvZ;
                    }
                }
                catch { }
                tq.RollBack();
            }
        return op;
    }

    static List<Ex> Dims(HashSet<ElementId> owners)
    {
        var list = new List<Ex>();
        foreach (var d in new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => owners.Contains(d.OwnerViewId) && d.GetType() == typeof(Dimension)))
        {
            var ln = d.Curve as Line; if (ln == null) continue;
            bool vert = Math.Abs(ln.Direction.DotProduct(Up)) > 0.99, hor = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.99;
            if (!vert && !hor) continue;
            double th = h; try { th = d.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * v.Scale; } catch { }
            var e = new Ex { D = d, V = vert, Line = vert ? VX(ln.Origin) : VY(ln.Origin) };
            var segs = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(s => Tuple.Create(s.Origin, s.Value ?? 0, s.TextPosition, s.ValueString)).ToList()
                                              : new List<Tuple<XYZ, double, XYZ, string>> { Tuple.Create(d.Origin, d.Value ?? 0, d.TextPosition, d.ValueString) };
            foreach (var s in segs)
            {
                double m = vert ? VY(s.Item1) : VX(s.Item1);
                e.Bnd.Add(m - s.Item2 / 2); e.Bnd.Add(m + s.Item2 / 2);
                e.Texts.Add(TextBox(vert, VX(s.Item3), VY(s.Item3), s.Item4, th));
            }
            foreach (Reference rf in d.References) e.Els.Add(rf.ElementId.IntegerValue);
            list.Add(e);
        }
        return list;
    }
    static bool Has(Ex e, double z, double tol) { return e.Bnd.Any(b => Math.Abs(b - z) < tol); }

    // openings side by side in one row (same sill and head, edges touching or almost): dimensioned as one group
    static List<int> Group(int id, Dictionary<int, double[]> rects)
    {
        var res = new List<int> { id }; if (!rects.ContainsKey(id)) return res;
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var kv in rects)
            {
                if (res.Contains(kv.Key)) continue;
                var o = kv.Value;
                if (res.Any(m =>
                {
                    var a = rects[m];
                    if (Math.Abs(a[2] - o[2]) > 60 / MM || Math.Abs(a[3] - o[3]) > 60 / MM) return false;
                    double gap = Math.Max(o[0] - a[1], a[0] - o[1]);
                    return gap > -100 / MM && gap < 150 / MM;
                })) { res.Add(kv.Key); grew = true; }
            }
        }
        return res.OrderBy(i => rects[i][0]).ToList();
    }

    // vertical status: refs this opening, shows host level (unless no host), bottom (unless at level) and top
    static string VStatus(Op op, List<Ex> ex, out Ex partial)
    {
        double tol = 40 / MM; int id = op.Fi.Id.IntegerValue; partial = null;
        bool atLv = !op.NoHost && Math.Abs(op.Z0 - op.LvZ) < 150 / MM;
        var mine = ex.Where(e => e.V && e.Els.Contains(id)).ToList();
        foreach (var g in mine.GroupBy(e => Math.Round(e.Line * MM / 50)))
        {
            // collinear dims on the same line count together (stacked openings: window below + louvre above on one line)
            double ln = g.First().Line;
            var bnd = ex.Where(e => e.V && Math.Abs(e.Line - ln) < 50 / MM).SelectMany(e => e.Bnd).ToList();
            Func<double, bool> has = z => bnd.Any(b => Math.Abs(b - z) < tol);
            // a dim on the opening itself may measure the leaf instead of the frame (e.g. 2600 vs 2650): 60 mm on top/bottom
            Func<double, bool> near = z => bnd.Any(b => Math.Abs(b - z) < 60 / MM);
            bool top = near(op.Z1), bot = atLv || near(op.Z0), lv = op.NoHost || has(op.LvZ);
            if (top && bot && lv) return "ok";
            if (partial == null && (top || has(op.Z0))) partial = g.First();
        }
        return mine.Count == 0 ? "missing" : "partial";
    }
    static string HStatus(Op op, List<Ex> ex)
    {
        double tol = 40 / MM; int id = op.Fi.Id.IntegerValue;
        if (ex.Any(e => !e.V && e.Els.Contains(id) && Has(e, op.X0, tol) && Has(e, op.X1, tol))) return "ok";
        // stacked openings with the same edges (e.g. louvre over window) share one horizontal chain
        Func<int, bool> isOpening = i => { var c = doc.GetElement(new ElementId(i))?.Category?.Id.IntegerValue; return c == (int)BuiltInCategory.OST_Doors || c == (int)BuiltInCategory.OST_Windows; };
        if (ex.Any(e => !e.V && Has(e, op.X0, tol) && Has(e, op.X1, tol) && e.Line > op.Z0 - 3500 / MM && e.Line < op.Z1 + 3500 / MM && e.Els.Any(i => i != id && isOpening(i)))) return "ok (shared with stacked opening)";
        return ex.Any(e => !e.V && e.Els.Contains(id)) ? "partial" : "missing";
    }

    public static object Run(UIApplication app, JObject args)
    {
        doc = app.ActiveUIDocument.Document;
        v = (View)doc.GetElement(new ElementId(args.Value<int>("viewId")));
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection; Vd = v.ViewDirection; P1 = v.Scale / MM;
        string mode = args.Value<string>("mode") ?? "audit";
        if (string.IsNullOrEmpty(args.Value<string>("dimTypeName"))) return new { Error = "dimTypeName is required (the project check dimension type, see drafting-profile)" };
        string typeName = args.Value<string>("dimTypeName");
        var dt = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(t => t.Name == typeName);
        if (dt == null) return new { Error = "dimension type not found: " + typeName };
        h = dt.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * v.Scale;
        var nominal = ((args["nominalFamilies"] as JArray)?.Select(t => ((string)t).ToUpperInvariant()) ?? new[] { "ROLL UP" }).ToList();
        double gridNear = (args.Value<double?>("gridNearDist") ?? 12000) / MM;
        var levels = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
        var owners = new HashSet<ElementId> { v.Id }; if (v.GetPrimaryViewId() != ElementId.InvalidElementId) owners.Add(v.GetPrimaryViewId());
        var ex = Dims(owners);
        string cachePath = args.Value<string>("cachePath");

        if (mode == "audit")
        {
            var v3 = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && !x.IsSectionBoxActive && x.Name == "{3D}")
                  ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && !x.IsSectionBoxActive);
            var ri = new ReferenceIntersector(new ElementMulticategoryFilter(new List<BuiltInCategory> {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_CurtainWallPanels, BuiltInCategory.OST_CurtainWallMullions, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_MechanicalEquipment }), FindReferenceTarget.Element, v3) { FindReferencesInRevitLinks = true };
            var rows = new List<object>(); var visible = new JArray(); int hidden = 0;
            var built = new Dictionary<int, Op>(); var vSt = new Dictionary<int, string>(); var hSt = new Dictionary<int, string>();
            foreach (var bic in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
                foreach (FamilyInstance fi in new FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
                {
                    if (Math.Abs(fi.FacingOrientation.DotProduct(Vd)) < 0.9) continue;
                    var bb = fi.get_BoundingBox(null); if (bb == null) continue;
                    // visible: cut by the view plane, or at least 2 of 5 rays toward the viewer reach it first
                    var corners = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z) };
                    var dps = corners.Select(p => (p - O).DotProduct(Vd)).ToList();
                    bool vis = dps.Min() < 0 && dps.Max() > 0;
                    if (!vis)
                    {
                        var cc = (bb.Min + bb.Max) / 2; var us = corners.Select(p => (p - O).DotProduct(Rg)).ToList();
                        double w = us.Max() - us.Min(), hh = bb.Max.Z - bb.Min.Z; int free = 0;
                        foreach (var (fu, fz) in new[] { (0.0, 0.0), (-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3) })
                        {
                            var p = cc + Rg * (fu * w) + XYZ.BasisZ * (fz * hh);
                            double dist = -(p - O).DotProduct(Vd); if (dist <= 0) { free++; continue; }
                            bool blocked = false;
                            foreach (var hit in ri.Find(p, Vd).OrderBy(x => x.Proximity))
                            {
                                if (hit.Proximity >= dist - 0.05) break;
                                var rf = hit.GetReference();
                                if (rf.LinkedElementId != ElementId.InvalidElementId) { blocked = true; break; }
                                var e = doc.GetElement(rf.ElementId);
                                if (e == null || e.Id == fi.Id || (fi.Host != null && e.Id == fi.Host.Id)) continue;
                                var sup = (e as FamilyInstance)?.SuperComponent; bool own = false;
                                while (sup != null) { if (sup.Id == fi.Id) { own = true; break; } sup = (sup as FamilyInstance)?.SuperComponent; }
                                if (own) continue;
                                blocked = true; break;
                            }
                            if (!blocked) free++;
                        }
                        vis = free >= 2;
                    }
                    if (!vis) { hidden++; continue; }
                    var op = Build(fi, levels, nominal);
                    visible.Add(fi.Id.IntegerValue);
                    Ex pt; built[fi.Id.IntegerValue] = op; vSt[fi.Id.IntegerValue] = VStatus(op, ex, out pt); hSt[fi.Id.IntegerValue] = HStatus(op, ex);
                }
            var rects = built.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.X0, kv.Value.X1, kv.Value.Z0, kv.Value.Z1 });
            foreach (var kv in built)
            {
                var op = kv.Value; int id = kv.Key; var ag = Group(id, rects);
                string vs = vSt[id], hs = hSt[id];
                // one vertical chain beside a group of identical side-by-side openings serves them all
                if (vs != "ok" && ag.Count > 1 && ag.Any(g => g != id && vSt[g] == "ok")) vs = "ok (group)";
                rows.Add(new { Id = id, op.Name, Level = op.Lv?.Name + (op.NoHost ? " (host not shown)" : ""), X = Math.Round(op.X0 * MM), Z = Math.Round(op.Z0 * MM), V = vs, H = hs,
                               Group = ag.Count > 1 ? string.Join("+", ag.Select(g => built[g].Name)) : null });
            }
            var jr = new JObject(); foreach (var kv in rects) jr[kv.Key.ToString()] = new JArray(kv.Value);
            if (cachePath != null) File.WriteAllText(cachePath, new JObject { ["viewId"] = v.Id.IntegerValue, ["visible"] = visible, ["rects"] = jr }.ToString());
            Func<object, bool> okV = r => ((string)((dynamic)r).V).StartsWith("ok"), okH = r => ((string)((dynamic)r).H).StartsWith("ok");
            return new { View = v.Name, Visible = visible.Count, Hidden = hidden, MissingV = rows.Count(r => !okV(r)), MissingH = rows.Count(r => !okH(r)),
                         Missing = rows.Where(r => !okV(r) || !okH(r)).OrderBy(r => ((dynamic)r).Z).ThenBy(r => ((dynamic)r).X).ToList() };
        }

        // ---------- one opening ----------
        var fiT = doc.GetElement(new ElementId(args.Value<int>("openingId"))) as FamilyInstance;
        if (fiT == null) return new { Error = "opening not found" };
        var visIds = cachePath != null && File.Exists(cachePath) ? ((JArray)JObject.Parse(File.ReadAllText(cachePath))["visible"]).Select(t => (int)t).ToList() : new List<int>();
        var me = Build(fiT, levels, nominal);
        Ex part; string vStat = VStatus(me, ex, out part), hStat = HStatus(me, ex);
        string only = args.Value<string>("only");
        // side-by-side group (from the audit cache): one H chain with every edge, one V chain beside the group
        var rectsC = new Dictionary<int, double[]>();
        if (cachePath != null && File.Exists(cachePath) && JObject.Parse(File.ReadAllText(cachePath))["rects"] is JObject jrc)
            foreach (var p in jrc.Properties()) rectsC[int.Parse(p.Name)] = p.Value.Select(t => (double)t).ToArray();
        var grp = new List<Op> { me };
        foreach (var gid in Group(fiT.Id.IntegerValue, rectsC).Where(i => i != fiT.Id.IntegerValue))
        { var gfi = doc.GetElement(new ElementId(gid)) as FamilyInstance; if (gfi != null) grp.Add(Build(gfi, levels, nominal)); }
        grp = grp.OrderBy(o => o.X0).ToList();
        double gx0 = grp.Min(o => o.X0), gx1 = grp.Max(o => o.X1);
        if (grp.Count > 1)
        {
            if (vStat != "ok") foreach (var o in grp.Where(o => o != me)) { Ex pp; if (VStatus(o, ex, out pp) == "ok") { vStat = "ok (group)"; break; } }
            hStat = grp.All(o => HStatus(o, ex).StartsWith("ok")) ? "ok (group)" : "group";
        }
        var grpIds = new HashSet<int>(grp.Select(o => o.Fi.Id.IntegerValue));

        // obstacles
        var obst = new List<R>();
        foreach (var id in visIds.Where(i => i != fiT.Id.IntegerValue))
        { var e = doc.GetElement(new ElementId(id)); var r = Box(e?.get_BoundingBox(v), "opening " + id); if (r != null) obst.Add(r); }
        var meRect = new R(gx0, gx1, me.Z0, me.Z1, "self");
        foreach (var e in ex)
        {
            obst.Add(e.V ? new R(e.Line - 0.2 * P1, e.Line + 0.2 * P1, e.Bnd.Min(), e.Bnd.Max(), "dim line " + e.D.Id.IntegerValue)
                         : new R(e.Bnd.Min(), e.Bnd.Max(), e.Line - 0.2 * P1, e.Line + 0.2 * P1, "dim line " + e.D.Id.IntegerValue));
            foreach (var t in e.Texts) { t.W = "dim text " + e.D.Id.IntegerValue; obst.Add(t); }
        }
        foreach (var el in new FilteredElementCollector(doc, v.Id).WherePasses(new LogicalOrFilter(new List<ElementFilter> {
                     new ElementClassFilter(typeof(IndependentTag)), new ElementClassFilter(typeof(TextNote)), new ElementClassFilter(typeof(SpotDimension)) })))
        { var r = Box(el.get_BoundingBox(v), el.GetType().Name + " " + el.Id.IntegerValue); if (r != null) obst.Add(r); }
        foreach (var el in new FilteredElementCollector(doc, v.Id).WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFoundation })).WhereElementIsNotElementType())
        {
            var bb = el.get_BoundingBox(null); if (bb == null) continue;
            var ds = new[] { bb.Min, bb.Max }.Select(p => (p - O).DotProduct(Vd)).ToList(); if (ds.Min() > 0 || ds.Max() < 0) continue;
            var r = Box(bb, "cut " + el.Category.Name); if (r != null && r.Y1 - r.Y0 < 6000 / MM) obst.Add(r);
        }
        foreach (RevitLinkInstance li in new FilteredElementCollector(doc, v.Id).OfClass(typeof(RevitLinkInstance)))
        {
            var ld = li.GetLinkDocument(); if (ld == null) continue; var tf = li.GetTotalTransform();
            foreach (var el in new FilteredElementCollector(ld).WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFoundation })).WhereElementIsNotElementType())
            {
                var bb = el.get_BoundingBox(null); if (bb == null) continue;
                var pts = new List<XYZ>(); foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z }) pts.Add(tf.OfPoint(new XYZ(x, y, z)));
                var ds = pts.Select(p => (p - O).DotProduct(Vd)).ToList(); if (ds.Min() > 0 || ds.Max() < 0) continue;
                var r = new R(pts.Min(VX), pts.Max(VX), pts.Min(VY), pts.Max(VY), "cut link " + el.Category?.Name);
                if (r.Y1 - r.Y0 < 6000 / MM && r.X1 > me.X0 - 30000 / MM && r.X0 < me.X1 + 30000 / MM) obst.Add(r);
            }
        }
        R crop = null;
        if (v.CropBoxActive) { var cb = v.CropBox; var c0 = cb.Transform.OfPoint(cb.Min); var c1 = cb.Transform.OfPoint(cb.Max); crop = new R(VX(c0), VX(c1), VY(c0), VY(c1)); }
        Func<R, int> bad = rc => obst.Count(o => o.Hits(rc)) + (rc.Hits(meRect) ? 5 : 0) + (crop != null && (rc.X0 < crop.X0 || rc.X1 > crop.X1 || rc.Y0 < crop.Y0 || rc.Y1 > crop.Y1) ? 10 : 0);

        var plans = new List<Tuple<string, double, List<Reference>, List<double>, List<string>>>(); var notes = new List<string>();
        // ---- vertical ----
        if (!vStat.StartsWith("ok") && only != "H" && me.T != null)
        {
            bool atLv = !me.NoHost && Math.Abs(me.Z0 - me.LvZ) < 150 / MM;
            var refs = new List<Reference>(); var at = new List<double>(); var desc = new List<string>();
            if (!me.NoHost && me.Lv != null) { refs.Add(me.Lv.GetPlaneReference()); at.Add(me.LvZ); desc.Add(me.Lv.Name); }
            if ((!atLv || me.NoHost) && me.B != null) { refs.Add(me.B); at.Add(me.Z0); desc.Add("Bottom"); }
            refs.Add(me.T); at.Add(me.Z1); desc.Add("Top");
            // stacked on an opening below (same edges, its head = our sill) whose vertical chain reaches our sill:
            // continue that chain on the same line with bottom -> top
            Ex below = null;
            if (me.B != null)
            {
                var belowIds = rectsC.Where(kv => kv.Key != fiT.Id.IntegerValue && Math.Abs(kv.Value[0] - me.X0) < 60 / MM && Math.Abs(kv.Value[1] - me.X1) < 60 / MM
                                                 && Math.Abs(kv.Value[3] - me.Z0) < 100 / MM).Select(kv => kv.Key).ToList();
                below = ex.FirstOrDefault(q => q.V && q.Els.Overlaps(belowIds) && Has(q, me.Z0, 40 / MM));
            }
            if (below != null)
            {
                plans.Add(Tuple.Create("V", below.Line, new List<Reference> { me.B, me.T }, new List<double> { me.Z0, me.Z1 }, new List<string> { "Bottom (on the chain of the opening below)", "Top" }));
                notes.Add("V continues chain " + below.D.Id.IntegerValue + " of the opening below");
            }
            else if (refs.Count < 2) notes.Add("no bottom/level reference for V");
            else
            {
                double best = double.NaN; int bb = int.MaxValue;
                var cands = new List<double>();
                for (int k = 0; k < 12; k++) { cands.Add(gx0 - (2.5 + 1.5 * k) * P1); cands.Add(gx1 + 1.5 * h + (2.5 + 1.5 * k) * P1); }
                foreach (var x in cands.OrderBy(x => Math.Min(Math.Abs(x - gx0), Math.Abs(x - gx1))))
                {
                    int b = bad(new R(x - 0.2 * P1, x + 0.2 * P1, at.Min(), at.Max()));
                    for (int i = 0; i + 1 < at.Count; i++)
                    {
                        double len = at[i + 1] - at[i]; string txt = Math.Round(len * MM).ToString();
                        var tb = TextBox(true, x, (at[i] + at[i + 1]) / 2, txt, h); if (tb.Y1 - tb.Y0 > len) continue;
                        b += bad(tb);
                    }
                    if (b < bb) { bb = b; best = x; if (b == 0) break; }
                }
                plans.Add(Tuple.Create("V", best, refs, at, desc)); if (bb > 0) notes.Add("V placed with " + bb + " clashes (closest best line)");
            }
        }
        // ---- horizontal ----
        if (!hStat.StartsWith("ok") && only != "V" && grp.All(o => o.L != null && o.Rr != null))
        {
            var grids = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>()
                .Where(g => g.Curve is Line && Math.Abs(((Line)g.Curve).Direction.DotProduct(Rg)) < 0.01).Select(g => Tuple.Create(g, VX(((Line)g.Curve).Origin))).ToList();
            var gl = grids.Where(g => g.Item2 <= gx0 + 2 / MM && g.Item2 >= gx0 - gridNear).OrderByDescending(g => g.Item2).FirstOrDefault();
            var gr = grids.Where(g => g.Item2 >= gx1 - 2 / MM && g.Item2 <= gx1 + gridNear).OrderBy(g => g.Item2).FirstOrDefault();
            var refs = new List<Reference>(); var at = new List<double>(); var desc = new List<string>();
            if (gl != null && gx0 - gl.Item2 > 2 / MM) { refs.Add(new Reference(gl.Item1)); at.Add(gl.Item2); desc.Add("Grid " + gl.Item1.Name); }
            foreach (var o in grp)
            {   // every edge of every opening of the group; a shared joint counts once
                if (!at.Any(a => Math.Abs(a - o.X0) < 30 / MM)) { refs.Add(o.L); at.Add(o.X0); desc.Add("L " + o.Name); }
                if (!at.Any(a => Math.Abs(a - o.X1) < 30 / MM)) { refs.Add(o.Rr); at.Add(o.X1); desc.Add("R " + o.Name); }
            }
            if (gr != null && gr.Item2 - gx1 > 2 / MM) { refs.Add(new Reference(gr.Item1)); at.Add(gr.Item2); desc.Add("Grid " + gr.Item1.Name); }
            if (gl == null || gr == null) notes.Add("no grid within " + Math.Round(gridNear * MM) + " mm on the " + (gl == null ? "left" : "right"));
            double up = me.Z1 + 3600 / MM; // cut slabs / beams are obstacles; level lines are only datums
            double floor = me.LvZ;
            var cands = new List<double>();
            for (int k = 0; k < 20; k++)
            {
                double za = me.Z1 + (2.5 + 1.5 * k) * P1; if (za < up - 1.5 * h && za < me.Z1 + 3500 / MM) cands.Add(za);
                double zb = me.Z0 - 1.5 * h - (2.5 + 1.5 * k) * P1; if (zb > floor + 0.5 * h && zb > me.Z0 - 3500 / MM) cands.Add(zb);
            }
            double best = double.NaN; int bb = int.MaxValue;
            foreach (var z in cands.OrderBy(z => Math.Min(Math.Abs(z - me.Z1), Math.Abs(z - me.Z0))))
            {
                int b = bad(new R(at.Min(), at.Max(), z - 0.2 * P1, z + 0.2 * P1));
                for (int i = 0; i + 1 < at.Count; i++)
                {
                    double len = at[i + 1] - at[i]; var tb = TextBox(false, (at[i] + at[i + 1]) / 2, z, Math.Round(len * MM).ToString(), h);
                    if (tb.X1 - tb.X0 > len) continue; b += bad(tb);
                }
                if (b < bb) { bb = b; best = z; if (b == 0) break; }
            }
            // replacing our own partial chain of this group: keep its line (the layout the user already saw)
            var oldOwn = grp.Count > 1 ? ex.FirstOrDefault(q => !q.V && q.Els.Overlaps(grpIds) && q.D.DimensionType.Name == typeName) : null;
            if (oldOwn != null) { best = oldOwn.Line; bb = 0; notes.Add("H on the line of replaced chain " + oldOwn.D.Id.IntegerValue); }
            if (double.IsNaN(best)) notes.Add("no line for H inside the storey");
            else
            {
                plans.Add(Tuple.Create("H", best, refs, at, desc));
                if (bb > 0)
                {
                    var lr = new R(at.Min(), at.Max(), best - 0.2 * P1, best + 0.2 * P1);
                    notes.Add("H placed with " + bb + " clashes: " + string.Join(", ", obst.Where(o => o.Hits(lr)).Select(o => o.W).Distinct().Take(8))
                        + (crop != null && (lr.X0 < crop.X0 || lr.X1 > crop.X1 || lr.Y0 < crop.Y0 || lr.Y1 > crop.Y1) ? ", outside crop" : ""));
                }
            }
        }

        var outPlans = plans.Select(p => new { Kind = p.Item1, PosMm = Math.Round(p.Item2 * MM), Values = string.Join(" | ", p.Item4.Zip(p.Item4.Skip(1), (a, b) => Math.Round((b - a) * MM))), Refs = string.Join(" | ", p.Item5) }).ToList();
        if (mode != "apply") return new { View = v.Name, Opening = me.Name, Id = fiT.Id.IntegerValue, Level = me.Lv?.Name, V = vStat, H = hStat, Group = grp.Count > 1 ? string.Join("+", grp.Select(o => o.Name)) : null, Planned = outPlans, Notes = notes };

        var created = new List<object>(); var errors = new List<string>(); var removed = new List<object>();
        using (var tx = new Transaction(doc, "Opening dims " + me.Name))
        {
            tx.Start();
            // a new full vertical chain replaces our own incomplete vertical pieces on this opening (check type only; black dims stay)
            if (plans.Any(p => p.Item1 == "V") && vStat == "partial")
                foreach (var e in ex.Where(q => q.V && q.Els.Contains(fiT.Id.IntegerValue) && q.D.DimensionType.Name == typeName).ToList())
                {
                    removed.Add(new { Id = e.D.Id.IntegerValue, Values = string.Join("|", e.Bnd.Zip(e.Bnd.Skip(1), (a, b) => Math.Round((b - a) * MM)).Where(x => x > 0)) });
                    doc.Delete(e.D.Id);
                }
            // an existing horizontal chain (any type) that already runs past the group but misses some of its edges
            // (e.g. 1000 | 8000 | 1000 over two 4000 windows) is completed in place: same line, same type, the missing edges added
            if (plans.Any(p => p.Item1 == "H") && (grp.Count > 1 || hStat == "partial"))
            {
                var host = ex.Where(q => !q.V && q.Els.Overlaps(grpIds) && q.Line > me.Z0 - 3500 / MM && q.Line < me.Z1 + 3500 / MM
                                         && q.Bnd.Min() <= gx0 + 40 / MM && q.Bnd.Max() >= gx1 - 40 / MM)
                             .OrderBy(q => q.D.DimensionType.Name == typeName ? 1 : 0).FirstOrDefault();
                int hostId = host?.D.Id.IntegerValue ?? 0;
                if (host != null)
                {
                    var ra = new ReferenceArray(); int added = 0;
                    foreach (Reference r in host.D.References) { var g = doc.GetElement(r.ElementId) as Grid; ra.Append(g != null ? new Reference(g) : r); }
                    var pos = new List<double>(host.Bnd);   // a joint shared by two openings is added once
                    foreach (var o in grp)
                    {
                        if (!pos.Any(b => Math.Abs(b - o.X0) < 40 / MM) && o.L != null) { ra.Append(o.L); pos.Add(o.X0); added++; }
                        if (!pos.Any(b => Math.Abs(b - o.X1) < 40 / MM) && o.Rr != null) { ra.Append(o.Rr); pos.Add(o.X1); added++; }
                    }
                    if (added > 0)
                    {
                        try
                        {
                            var nd = doc.Create.NewDimension(v, Line.CreateBound(O + Up * host.Line, O + Up * host.Line + Rg * 10), ra, host.D.DimensionType);
                            var vals = nd.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();
                            removed.Add(new { Id = host.D.Id.IntegerValue, Values = string.Join("|", host.Bnd.Zip(host.Bnd.Skip(1), (a, b) => Math.Round((b - a) * MM)).Where(x => x > 0)), Refs = host.D.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc)).ToList() });
                            doc.Delete(host.D.Id);
                            created.Add(new { Id = nd.Id.IntegerValue, Kind = "H (completed " + hostId + ", +" + added + " edges)", Values = string.Join(" | ", vals) });
                            plans.RemoveAll(p => p.Item1 == "H");
                        }
                        catch (Exception exn) { errors.Add("complete chain " + hostId + ": " + exn.Message); }
                    }
                }
            }
            // a group chain replaces our own horizontal chains that dimension only part of the group (e.g. outer edges only)
            if (plans.Any(p => p.Item1 == "H") && grp.Count > 1)
                foreach (var e in ex.Where(q => !q.V && q.Els.Overlaps(grpIds) && q.D.DimensionType.Name == typeName).ToList())
                {
                    removed.Add(new { Id = e.D.Id.IntegerValue, Values = string.Join("|", e.Bnd.Zip(e.Bnd.Skip(1), (a, b) => Math.Round((b - a) * MM)).Where(x => x > 0)) });
                    doc.Delete(e.D.Id);
                }
            foreach (var p in plans)
            {
                try
                {
                    var ra = new ReferenceArray(); foreach (var r in p.Item3) ra.Append(r);
                    Line line = p.Item1 == "V" ? Line.CreateBound(O + Rg * p.Item2, O + Rg * p.Item2 + Up * 10) : Line.CreateBound(O + Up * p.Item2, O + Up * p.Item2 + Rg * 10);
                    var d = doc.Create.NewDimension(v, line, ra, dt);
                    var vals = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList() : new List<double> { Math.Round((d.Value ?? 0) * MM) };
                    created.Add(new { Id = d.Id.IntegerValue, Kind = p.Item1, Values = string.Join(" | ", vals) });
                }
                catch (Exception exn) { errors.Add(p.Item1 + ": " + exn.Message); }
            }
            tx.Commit();
        }
        var logPath = args.Value<string>("logPath");
        if (logPath != null)
        {
            var lg = File.Exists(logPath) ? JArray.Parse(File.ReadAllText(logPath)) : new JArray();
            foreach (dynamic c in created) lg.Add(new JObject { ["opening"] = fiT.Id.IntegerValue, ["name"] = me.Name, ["id"] = (int)c.Id, ["kind"] = (string)c.Kind, ["values"] = (string)c.Values });
            File.WriteAllText(logPath, lg.ToString());
        }
        return new { View = v.Name, Opening = me.Name, Id = fiT.Id.IntegerValue, V = vStat, H = hStat, Created = created, ReplacedOwn = removed.Select(r => (int)((dynamic)r).Id).ToList(), Errors = errors, Notes = notes };
    }
}
