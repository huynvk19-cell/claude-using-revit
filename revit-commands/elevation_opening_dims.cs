/* mcp-tool
{
  "description": "Elevation/section, ONE view: dims for visible doors/windows (vertical level->bottom->top, horizontal per row). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply",
          "undo"
        ]
      },
      "dimTypeName": {
        "type": "string"
      },
      "baseDimType": {
        "type": "string"
      },
      "color": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "gridNearDist": {
        "type": "number",
        "description": "mm, default 3000"
      },
      "verticalMode": {
        "type": "string",
        "enum": [
          "perType",
          "all"
        ]
      },
      "verticalEachDoor": {
        "type": "boolean",
        "description": "Every door gets its own vertical dim even in perType mode. Default false."
      },
      "nominalFamilies": {
        "type": "array",
        "items": {
          "type": "string"
        },
        "description": "Family name parts (e.g"
      },
      "verticalEachMinHeight": {
        "type": "number",
        "description": "mm: openings at least this tall get their own vertical dim even in perType mode (e.g"
      },
      "moveExisting": {
        "type": "boolean",
        "description": "Move existing vertical dims that sit on their own opening. Default true."
      },
      "strictVisibility": {
        "type": "boolean",
        "description": "multi-ray first-hit test toward the viewer"
      },
      "verticalOnly": {
        "type": "boolean"
      },
      "horizontalOnly": {
        "type": "boolean"
      },
      "excludeIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "onlyIds": {
        "type": "array",
        "items": {
          "type": "number"
        },
        "description": "only dimension these openings (others are still obstacles? no"
      },
      "hostLevel": {
        "type": "boolean",
        "description": "Vertical chains start at each opening's HOST level"
      },
      "replaceStale": {
        "type": "boolean"
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "viewId"
    ]
  },
  "timeoutSeconds": 900,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Add opening dimensions to an elevation/section view, reusing what is already there. Dims go to the OUTER FRAME of
//    each door/window as seen in the view (front-most geometry faces). Vertical: Level -> bottom -> top (completes
//    partial existing dims; otherwise one per distinct type+height per row unless verticalMode='all'). Horizontal: one
//    chain per row placed next to that row (widths, gaps, nearby grids); an opening counts as dimensioned only when a
//    dim next to its row already has its frame edges (position based, so no duplicates). Dims and their texts are
//    placed clear of openings, tags and other dims; overlapping texts are moved. Existing vertical dims drawn across
//    their own opening are moved beside it. New dims use a dedicated coloured dimension type. Visibility: ray cast in
//    the default 3D view. mode=preview (default) | apply | undo (reverts everything recorded in logPath).
// Parameters:
//   nominalFamilies: Family name parts (e.g. 'ROLL UP') dimensioned to their nominal size: family Left/Right and the
//    reference named TOP (or Top) instead of the outer frame.
//   verticalEachMinHeight: mm: openings at least this tall get their own vertical dim even in perType mode (e.g. 3000
//    for tall windows). Default 0 = off.
//   strictVisibility: multi-ray first-hit test toward the viewer (walls, glazing, other openings, framing, links
//    block); cut openings are visible
//   onlyIds: only dimension these openings (others are still obstacles? no: they are skipped entirely)
//   hostLevel: Vertical chains start at each opening's HOST level (instance Level) instead of the nearest level below:
//    host level -> bottom -> top for windows and doors (bottom dropped when it is within 150 mm of the level). A
//    vertical dim counts as existing only when it includes the host level. Host level not shown in the view: bottom ->
//    top, reported. Own check-type vertical dims of an opening that start at another level are deleted (replaceStale,
//    default true). Default false.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ElevationOpeningDims
{
    const double MM = 304.8;

    class Rect
    {
        public double X0, X1, Z0, Z1; public string What;
        public Rect(double a, double b, double c, double d, string w) { X0 = Math.Min(a, b); X1 = Math.Max(a, b); Z0 = Math.Min(c, d); Z1 = Math.Max(c, d); What = w; }
        public bool Hits(Rect o) { return X0 < o.X1 && o.X0 < X1 && Z0 < o.Z1 && o.Z0 < Z1; }
        public Rect Move(double dx, double dz) { return new Rect(X0 + dx, X1 + dx, Z0 + dz, Z1 + dz, What); }
    }
    class Op
    {
        public FamilyInstance Fi; public string TypeMark, Mark; public double X0, X1, Z0, Z1, Depth, LvZ;
        public Reference L, R, B, T; public bool Frame; public Level Lv; public int Row; public bool NoHost;
        public string Name { get { return TypeMark + "#" + Mark; } }
    }
    class Ex
    {
        public Dimension D; public bool V; public double Line, Lo, Hi; public List<double> Bnd = new List<double>();
        public HashSet<int> Els = new HashSet<int>(); public bool Own; public List<Rect> Texts = new List<Rect>();
    }
    class Plan { public string Kind, Why; public double Pos; public List<Reference> Refs = new List<Reference>(); public List<double> At = new List<double>(); public List<string> Desc = new List<string>(); }

    static XYZ O, Rg, Vd;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg); }

    // text box of a dimension text anchored at (x,z) on the line; h = model text height
    static Rect TextBox(bool vertical, double x, double z, string txt, double h)
    {
        double w = Math.Max(1, (txt ?? "").Length) * 0.5 * h + 0.45 * h; // generous: texts must not touch
        return vertical ? new Rect(x - 1.65 * h, x - 0.2 * h, z - w / 2, z + w / 2, "text")
                        : new Rect(x - w / 2, x + w / 2, z + 0.2 * h, z + 1.65 * h, "text");
    }

    // outer frame of an opening from its front-most planar faces (as drawn in the view)
    static void Frame(FamilyInstance fi, View v, Op op)
    {
        var faces = new List<Tuple<bool, double, double, Reference, double>>();
        var opt = new Options { ComputeReferences = true, View = v };
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
                        var n = t.OfVector(pf.FaceNormal);
                        var bb = pf.GetBoundingBox();
                        var c = t.OfPoint(pf.Evaluate((bb.Min + bb.Max) / 2));
                        double depth = (c - O).DotProduct(Vd);
                        if (Math.Abs(n.DotProduct(Rg)) > 0.99) faces.Add(Tuple.Create(true, VX(c), depth, pf.Reference, pf.Area));
                        else if (Math.Abs(n.Z) > 0.99) faces.Add(Tuple.Create(false, c.Z, depth, pf.Reference, pf.Area));
                    }
                else if (obj is GeometryInstance gi) walk(gi.GetSymbolGeometry(), t.Multiply(gi.Transform));
            }
        };
        walk(fi.get_Geometry(opt), Transform.Identity);
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
        op.L = l.Item4; op.R = r.Item4; op.B = b.Item4; op.T = t2.Item4;
        op.X0 = l.Item2; op.X1 = r.Item2; op.Z0 = b.Item2; op.Z1 = t2.Item2; op.Frame = true;
    }

    static Reference FirstRef(FamilyInstance fi, FamilyInstanceReferenceType t) { var l = fi.GetReferences(t); return l.Count > 0 ? l[0] : null; }

    static Ex ParseDim(Dimension d, double hDefault)
    {
        var ln = d.Curve as Line; if (ln == null) return null;
        bool v = Math.Abs(ln.Direction.Z) > 0.99, hz = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.99;
        if (!v && !hz) return null;
        var e = new Ex { D = d, V = v, Line = v ? VX(ln.Origin) : ln.Origin.Z };
        double h = hDefault;
        try { var ts = d.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE); if (ts != null) h = ts.AsDouble() * d.View.Scale; } catch { }
        Func<XYZ, double> c = p => v ? p.Z : VX(p);
        if (d.NumberOfSegments > 1)
            foreach (DimensionSegment s in d.Segments)
            {
                double m = c(s.Origin), val = s.Value ?? 0;
                e.Bnd.Add(m - val / 2); e.Bnd.Add(m + val / 2);
                var tp = s.TextPosition;
                e.Texts.Add(TextBox(v, VX(tp), tp.Z, s.ValueString, h));
            }
        else
        {
            double m = c(d.Origin), val = d.Value ?? 0;
            e.Bnd.Add(m - val / 2); e.Bnd.Add(m + val / 2);
            var tp = d.TextPosition;
            e.Texts.Add(TextBox(v, VX(tp), tp.Z, d.ValueString, h));
        }
        e.Bnd = e.Bnd.OrderBy(x => x).Aggregate(new List<double>(), (acc, x) => { if (acc.Count == 0 || x - acc.Last() > 1 / MM) acc.Add(x); return acc; });
        e.Lo = e.Bnd.First(); e.Hi = e.Bnd.Last();
        foreach (Reference rf in d.References) e.Els.Add(rf.ElementId.IntegerValue);
        return e;
    }

    static Rect ProjectBox(BoundingBoxXYZ bb, string what)
    {
        if (bb == null) return null;
        var pts = new List<XYZ>();
        foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                    pts.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
        return new Rect(pts.Min(VX), pts.Max(VX), pts.Min(p => p.Z), pts.Max(p => p.Z), what);
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId(args.Value<int>("viewId")));
        string mode = args.Value<string>("mode") ?? "preview";
        string logPath = args.Value<string>("logPath");
        O = v.Origin; Rg = v.RightDirection; Vd = v.ViewDirection;

        if (mode == "undo")
        {
            var tok = JToken.Parse(File.ReadAllText(logPath));
            var toDelete = (tok is JArray ? (JArray)tok : (JArray)tok["created"] ?? new JArray()).Select(t => new ElementId((int)t)).Where(id => doc.GetElement(id) != null).ToList();
            var moved = tok is JObject && tok["moved"] != null ? (JArray)tok["moved"] : new JArray();
            int back = 0;
            using (var tx = new Transaction(doc, "Undo opening dims"))
            {
                tx.Start();
                doc.Delete(toDelete);
                foreach (JObject m in moved.Reverse())
                {
                    var id = new ElementId((int)m["id"]); if (doc.GetElement(id) == null) continue;
                    ElementTransformUtils.MoveElement(doc, id, new XYZ(-(double)m["dx"], -(double)m["dy"], -(double)m["dz"])); back++;
                }
                tx.Commit();
            }
            return new { Deleted = toDelete.Count, MovedBack = back };
        }

        if (string.IsNullOrEmpty(args.Value<string>("dimTypeName"))) return new { Error = "dimTypeName is required (the project check dimension type, see drafting-profile)" };
        string typeName = args.Value<string>("dimTypeName");
        string baseName = args.Value<string>("baseDimType"); // only needed when dimTypeName does not exist yet
        var col = (args["color"] as JArray)?.Select(t => (byte)(int)t).ToArray() ?? new byte[] { 0, 0, 255 };
        double gridNear = (args.Value<double?>("gridNearDist") ?? 3000) / MM;
        bool vAll = (args.Value<string>("verticalMode") ?? "perType") == "all";
        double eachMinH = (args.Value<double?>("verticalEachMinHeight") ?? 0) / MM;
        bool eachDoor = args.Value<bool?>("verticalEachDoor") ?? false;
        var nominalFams = ((args["nominalFamilies"] as JArray)?.Select(t => ((string)t).ToUpperInvariant()) ?? new string[0]).ToList();
        var nominalOps = new List<Op>(); var nominalNotes = new List<string>();
        bool moveExisting = args.Value<bool?>("moveExisting") ?? true;
        var exclude = new HashSet<int>((args["excludeIds"] as JArray)?.Select(t => (int)t) ?? new int[0]);
        var onlyIds = (args["onlyIds"] as JArray)?.Select(t => (int)t).ToList();
        double tol = 40 / MM;
        bool hostLv = args.Value<bool?>("hostLevel") ?? false;
        bool replaceStale = args.Value<bool?>("replaceStale") ?? true;
        var hostNotes = new List<string>();

        var dtExisting = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(t => t.Name == typeName);
        if (dtExisting == null && string.IsNullOrEmpty(baseName)) return new { Error = "dimension type '" + typeName + "' does not exist: pass baseDimType to create it as a coloured copy" };
        var dtBase = dtExisting ?? new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().First(t => t.Name == baseName && t.StyleType == DimensionStyleType.Linear);
        double h = dtBase.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * v.Scale;

        // ---------- openings (visible, facing the view) ----------
        var v3 = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && !x.IsSectionBoxActive && x.Name == "{3D}")
              ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && !x.IsSectionBoxActive);
        ReferenceIntersector ri3 = v3 == null ? null : new ReferenceIntersector(new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs }), FindReferenceTarget.Element, v3);
        // strict visibility: rays from points on the opening toward the viewer; anything in between (walls, glazing, other openings,
        // columns, framing, links...) blocks. Openings cut by the section plane always count as visible.
        bool strict = args.Value<bool?>("strictVisibility") ?? false;
        bool vOnly = args.Value<bool?>("verticalOnly") ?? false;
        ReferenceIntersector riS = null;
        if (strict && v3 != null)
        {
            riS = new ReferenceIntersector(new ElementMulticategoryFilter(new List<BuiltInCategory> {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_CurtainWallPanels, BuiltInCategory.OST_CurtainWallMullions, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_MechanicalEquipment }), FindReferenceTarget.Element, v3);
            riS.FindReferencesInRevitLinks = true;
        }
        var strictNotes = new List<string>();
        Func<FamilyInstance, BoundingBoxXYZ, bool> strictVisible = (fi, bbx) =>
        {
            var corners = new[] { bbx.Min, bbx.Max, new XYZ(bbx.Min.X, bbx.Max.Y, bbx.Min.Z), new XYZ(bbx.Max.X, bbx.Min.Y, bbx.Min.Z) };
            var dps = corners.Select(p => (p - O).DotProduct(Vd)).ToList();
            if (dps.Min() < 0 && dps.Max() > 0) return true; // cut by the view plane
            var cc = (bbx.Min + bbx.Max) / 2;
            var us = corners.Select(p => (p - O).DotProduct(Rg)).ToList();
            double w = us.Max() - us.Min(), hh = bbx.Max.Z - bbx.Min.Z;
            int free = 0, n = 0;
            foreach (var (fu, fz) in new[] { (0.0, 0.0), (-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3) })
            {
                var p = cc + Rg * (fu * w) + XYZ.BasisZ * (fz * hh);
                double dist = -(p - O).DotProduct(Vd); if (dist <= 0) { free++; n++; continue; }
                bool blocked = false;
                foreach (var hit in riS.Find(p, Vd).OrderBy(x => x.Proximity))
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
                n++; if (!blocked) free++;
            }
            return free >= 2;
        };
        var levels = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
        var ops = new List<Op>();
        foreach (var bic in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
            foreach (FamilyInstance fi in new FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                if (exclude.Contains(fi.Id.IntegerValue) || (onlyIds != null && !onlyIds.Contains(fi.Id.IntegerValue))) continue;
                if (Math.Abs(fi.FacingOrientation.DotProduct(Vd)) < 0.9) continue;
                var bb = fi.get_BoundingBox(null); if (bb == null) continue;
                var c = (bb.Min + bb.Max) / 2; double depth = (c - O).DotProduct(Vd);
                if (strict && riS != null) { if (!strictVisible(fi, bb)) { strictNotes.Add("hidden " + fi.Id.IntegerValue + " " + fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()); continue; } }
                else if (ri3 != null && depth < 0)
                {
                    bool hidden = false; var host = fi.Host?.Id;
                    foreach (var hit in ri3.Find(c, Vd).OrderBy(x => x.Proximity))
                    {
                        if (hit.Proximity >= -depth - 0.05) break;
                        var e = doc.GetElement(hit.GetReference().ElementId);
                        if (e == null || (host != null && e.Id == host)) continue;
                        // curtain walls block too (cladding systems are often modelled as curtain walls)
                        hidden = true; break;
                    }
                    if (hidden) continue;
                }
                var op = new Op { Fi = fi, Depth = depth, TypeMark = fi.Symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString(), Mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() };
                bool nominal = nominalFams.Any(s => (fi.Symbol.FamilyName ?? "").ToUpperInvariant().Contains(s));
                if (!nominal) Frame(fi, v, op);
                if (!op.Frame)
                {
                    var pr = ProjectBox(fi.get_BoundingBox(v) ?? bb, "op");
                    op.X0 = pr.X0; op.X1 = pr.X1; op.Z0 = pr.Z0; op.Z1 = pr.Z1;
                    op.L = FirstRef(fi, FamilyInstanceReferenceType.Left); op.R = FirstRef(fi, FamilyInstanceReferenceType.Right);
                    op.B = FirstRef(fi, FamilyInstanceReferenceType.Bottom); op.T = FirstRef(fi, FamilyInstanceReferenceType.Top);
                    if (nominal)
                    {
                        // nominal size: a reference named TOP (head of the opening) wins over the family's Top
                        foreach (FamilyInstanceReferenceType rt in new[] { FamilyInstanceReferenceType.StrongReference, FamilyInstanceReferenceType.WeakReference, FamilyInstanceReferenceType.Top })
                            foreach (var rf in fi.GetReferences(rt))
                                if (string.Equals(fi.GetReferenceName(rf), "TOP", StringComparison.OrdinalIgnoreCase)) { op.T = rf; goto foundTop; }
                        foundTop:
                        nominalOps.Add(op);
                    }
                }
                op.Lv = levels.Where(l => l.Elevation <= op.Z0 + 20 / MM).LastOrDefault() ?? levels.FirstOrDefault();
                if (hostLv)
                {
                    var hid = fi.LevelId;
                    if (hid == null || hid == ElementId.InvalidElementId)
                        foreach (var bp in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM })
                        { var pr = fi.get_Parameter(bp); if (pr != null && pr.StorageType == StorageType.ElementId && pr.AsElementId() != ElementId.InvalidElementId) { hid = pr.AsElementId(); break; } }
                    var hl = levels.FirstOrDefault(l => l.Id == hid);
                    var hlDoc = hid == null ? null : doc.GetElement(hid) as Level;
                    if (hl != null)
                    {
                        if (op.Lv == null || op.Lv.Id != hl.Id) hostNotes.Add(op.Name + " id" + fi.Id.IntegerValue + ": host level " + hl.Name + " (nearest below was " + (op.Lv?.Name ?? "-") + ")");
                        op.Lv = hl;
                    }
                    else { op.NoHost = true; hostNotes.Add(op.Name + " id" + fi.Id.IntegerValue + ": host level " + (hlDoc?.Name ?? "?") + " not shown in the view -> bottom -> top"); }
                }
                op.LvZ = op.Lv == null ? op.Z0 : op.Lv.Elevation;
                ops.Add(op);
            }

        // nominal openings: measure where their references really are (temporary dims, rolled back)
        if (nominalOps.Count > 0)
            using (var tq = new Transaction(doc, "probe nominal refs"))
            {
                tq.Start();
                foreach (var op in nominalOps)
                {
                    try
                    {
                        double zm = (op.Z0 + op.Z1) / 2;
                        var ra = new ReferenceArray(); ra.Append(op.L); ra.Append(op.R);
                        var a = new XYZ(O.X, O.Y, zm);
                        var d = doc.Create.NewDimension(v, Line.CreateBound(a, a + Rg * 10), ra);
                        double w = d.Value ?? 0, cx = VX(d.Origin);
                        op.X0 = cx - w / 2; op.X1 = cx + w / 2;
                        if (op.Lv != null && op.T != null)
                        {
                            var rb = new ReferenceArray(); rb.Append(op.Lv.GetPlaneReference()); rb.Append(op.T);
                            var b = O + Rg * cx;
                            var d2 = doc.Create.NewDimension(v, Line.CreateBound(new XYZ(b.X, b.Y, 0), new XYZ(b.X, b.Y, 10)), rb);
                            op.Z1 = op.LvZ + (d2.Value ?? 0);
                            if (op.B == null) op.Z0 = op.LvZ;
                        }
                    }
                    catch (Exception exn) { nominalNotes.Add(op.Name + ": " + exn.Message); }
                }
                tq.RollBack();
            }

        // rows
        ops = ops.OrderBy(p => p.Z0).ThenBy(p => p.X0).ToList();
        var rows = new List<List<Op>>();
        foreach (var op in ops)
        {
            var row = rows.LastOrDefault();
            if (row == null || op.Z0 - row[0].Z0 > 1500 / MM) { row = new List<Op>(); rows.Add(row); }
            row.Add(op); op.Row = rows.Count - 1;
        }

        // ---------- existing annotation ----------
        // every dim owned by the view, including ones outside the crop region (the view collector skips those)
        // dependent views: annotation belongs to the primary (parent) view
        var ownerIds = new HashSet<ElementId> { v.Id }; if (v.GetPrimaryViewId() != ElementId.InvalidElementId) ownerIds.Add(v.GetPrimaryViewId());
        var ex = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => ownerIds.Contains(d.OwnerViewId))
            .Where(d => d.GetType() == typeof(Dimension)).Select(d => ParseDim(d, h)).Where(e => e != null).ToList();
        foreach (var e in ex) e.Own = e.D.DimensionType.Name == typeName;
        // host-level mode: own check-type vertical dims of an opening that start at another level are stale
        var stale = new List<Ex>();
        if (hostLv && replaceStale)
            foreach (var op in ops.Where(q => !q.NoHost && q.Lv != null))
            {
                int id = op.Fi.Id.IntegerValue;
                // only dims that reference this opening (level / grid dims nearby are never touched)
                foreach (var e in ex.Where(q => q.Own && q.V && !stale.Contains(q) && q.Els.Contains(id)
                                             && q.Hi > op.Z1 - tol && q.Lo < op.Z0 - tol && q.Lo > op.Z0 - 30000 / MM))
                {
                    bool hasHost = e.Bnd.Any(b => Math.Abs(b - op.LvZ) < tol);
                    bool otherLv = levels.Any(l => l.Id != op.Lv.Id && Math.Abs(l.Elevation - e.Lo) < tol);
                    if (!hasHost && otherLv) { stale.Add(e); hostNotes.Add("stale own V dim " + e.D.Id.IntegerValue + " of " + op.Name + " starts at another level"); }
                }
            }
        ex = ex.Except(stale).ToList();
        var tagRects = new List<Rect>();
        foreach (var el in new FilteredElementCollector(doc, v.Id).WherePasses(new LogicalOrFilter(new List<ElementFilter> {
                     new ElementClassFilter(typeof(IndependentTag)), new ElementClassFilter(typeof(SpatialElementTag)), new ElementClassFilter(typeof(TextNote)), new ElementClassFilter(typeof(SpotDimension)) })))
        {
            var pr = ProjectBox(el.get_BoundingBox(v), el.GetType().Name); if (pr != null) tagRects.Add(pr);
        }
        // slabs / ground cut by the section plane are drawn as solid poche: keep dims off them
        foreach (var el in new FilteredElementCollector(doc, v.Id).WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> {
                     BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Topography })).WhereElementIsNotElementType())
        {
            var bb = el.get_BoundingBox(null); if (bb == null) continue;
            var ds = new[] { bb.Min, bb.Max }.Select(p => (p - O).DotProduct(Vd)).ToList();
            if (ds.Min() > 0 || ds.Max() < 0) continue; // not cut by the view plane
            var pr = ProjectBox(bb, "cut " + el.Category.Name); if (pr != null && pr.Z1 - pr.Z0 < 6000 / MM) tagRects.Add(pr);
        }
        // same for slabs/beams/foundations of visible Revit links (structure is usually linked), and filled regions
        var cutCats = new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Roofs };
        foreach (RevitLinkInstance li in new FilteredElementCollector(doc, v.Id).OfClass(typeof(RevitLinkInstance)))
        {
            var ld = li.GetLinkDocument(); if (ld == null) continue;
            var tf = li.GetTotalTransform();
            foreach (var el in new FilteredElementCollector(ld).WherePasses(new ElementMulticategoryFilter(cutCats)).WhereElementIsNotElementType())
            {
                var bb = el.get_BoundingBox(null); if (bb == null) continue;
                var pts = new List<XYZ>();
                foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                            pts.Add(tf.OfPoint(new XYZ(x, y, z)));
                var ds = pts.Select(p => (p - O).DotProduct(Vd)).ToList();
                if (ds.Min() > 0 || ds.Max() < 0) continue;
                var rc = new Rect(pts.Min(VX), pts.Max(VX), pts.Min(p => p.Z), pts.Max(p => p.Z), "cut link " + el.Category?.Name);
                if (rc.Z1 - rc.Z0 < 6000 / MM) tagRects.Add(rc);
            }
        }
        foreach (var el in new FilteredElementCollector(doc, v.Id).OfClass(typeof(FilledRegion)))
        {
            var pr = ProjectBox(el.get_BoundingBox(v), "filled region"); if (pr != null) tagRects.Add(pr);
        }
        var openRects = ops.Select(p => new Rect(p.X0, p.X1, p.Z0, p.Z1, "opening " + p.Name)).ToList();
        bool isSection = tagRects.Any(r => r.What.StartsWith("cut "));
        Func<Ex, Rect> lineRect = e => e.V ? new Rect(e.Line - 20 / MM, e.Line + 20 / MM, e.Lo, e.Hi, "dimline") : new Rect(e.Lo, e.Hi, e.Line - 20 / MM, e.Line + 20 / MM, "dimline");

        var moveLog = new JArray();
        var report = new List<string>(nominalNotes);
        if (strict) report.Add("strict visibility: " + strictNotes.Count + " hidden openings skipped");

        // ---------- move existing vertical dims drawn across their own opening ----------
        var movePlans = new List<Tuple<Ex, double>>();
        if (moveExisting)
            foreach (var e in ex.Where(q => q.V && !q.Own))
            {
                // the dim line runs over an opening or ends on it (e.g. a sill dim drawn inside the frame)
                var hit = ops.FirstOrDefault(p => e.Line > p.X0 + 30 / MM && e.Line < p.X1 - 30 / MM && e.Lo < p.Z1 + 10 / MM && e.Hi > p.Z0 - 10 / MM);
                if (hit == null) continue;
                double best = double.NaN;
                foreach (var x in new[] { hit.X0 - 150 / MM, hit.X0 - 450 / MM, hit.X1 + 1.85 * h, hit.X1 + 1.85 * h + 300 / MM }.OrderBy(q => Math.Abs(q - e.Line)))
                {
                    var lr = new Rect(x - 20 / MM, x + 20 / MM, e.Lo, e.Hi, "");
                    var txt = e.Texts.Select(t => t.Move(x - e.Line, 0)).ToList();
                    bool bad = openRects.Any(o => o.Hits(lr)) || txt.Any(t => openRects.Any(o => o.Hits(t)) || tagRects.Any(o => o.Hits(t)));
                    if (!bad) { best = x; break; }
                }
                if (double.IsNaN(best)) { report.Add("existing V dim " + e.D.Id.IntegerValue + " overlaps " + hit.Name + " - no free spot to move it"); continue; }
                movePlans.Add(Tuple.Create(e, best - e.Line));
            }
        var movedLine = movePlans.ToDictionary(m => m.Item1.D.Id.IntegerValue, m => m.Item1.Line + m.Item2);

        // ---------- coverage (by position, next to the opening / row) ----------
        var mismatch = new HashSet<string>();
        Func<Op, Tuple<bool, bool, bool, Ex>> vCover = op =>
        {
            int id = op.Fi.Id.IntegerValue;
            bool atLv = Math.Abs(op.Z0 - op.LvZ) < 150 / MM;
            // dims about this opening: they reference it, or stand right next to it
            var near = ex.Where(e => e.V && (e.Els.Contains(id) || (e.Line > op.X0 - 1000 / MM && e.Line < op.X1 + 1000 / MM))
                                     && e.Lo < op.Z1 + tol && e.Hi > op.Z0 - 300 / MM).ToList();
            Func<Ex, double, bool> has = (e, z) => e.Bnd.Any(b => Math.Abs(b - z) < tol);
            Func<Ex, bool> top = e => has(e, op.Z1) || (e.Els.Contains(id) && e.Bnd.Any(b => Math.Abs(b - op.Z1) < 300 / MM));
            bool z1 = near.Any(top);
            bool z0 = !atLv && near.Any(e => has(e, op.Z0));
            // complete = one dim shows the whole height (bottom and top), or level->top for openings standing on the level
            bool full = hostLv && !op.NoHost
                ? near.Any(e => top(e) && has(e, op.LvZ) && (atLv || has(e, op.Z0))) || (z1 && near.Any(e => has(e, op.Z0) && has(e, op.LvZ)))
                : near.Any(e => top(e) && (atLv || has(e, op.Z0))) || (z1 && near.Any(e => has(e, op.Z0) && has(e, op.LvZ)));
            foreach (var e in near.Where(e => e.Els.Contains(id) && !has(e, op.Z1) && e.Bnd.Any(b => Math.Abs(b - op.Z1) < 300 / MM)))
                if (!mismatch.Contains(e.D.Id.IntegerValue + "|" + id)) { mismatch.Add(e.D.Id.IntegerValue + "|" + id); report.Add("existing dim " + e.D.Id.IntegerValue + " measures " + op.Name + " top at " + Math.Round((e.Bnd.OrderBy(b => Math.Abs(b - op.Z1)).First() - op.LvZ) * MM) + " (frame top " + Math.Round((op.Z1 - op.LvZ) * MM) + ")"); }
            var anchor = near.Where(e => e.Els.Contains(id) && (has(e, op.Z0) || has(e, op.Z1))).OrderBy(e => Math.Abs(e.Line - op.X0)).FirstOrDefault()
                      ?? near.Where(e => has(e, op.Z0) || has(e, op.Z1)).OrderBy(e => Math.Abs(e.Line - op.X0)).FirstOrDefault();
            return Tuple.Create(z0, z1, full, anchor);
        };
        Func<Op, bool> hCover = op =>
        {
            var rw = rows[op.Row]; double lo = rw.Min(p => p.Z0) - (isSection && op.Row == 0 ? 8500 : 2500) / MM, hi = rw.Max(p => p.Z1) + 2500 / MM;
            return ex.Any(e => !e.V && e.Line > lo && e.Line < hi && e.Bnd.Any(b => Math.Abs(b - op.X0) < tol) && e.Bnd.Any(b => Math.Abs(b - op.X1) < tol));
        };

        // annotation planned in this run (grows while planning)
        var planned = new List<Rect>();
        foreach (var mp in movePlans)
        {
            planned.AddRange(mp.Item1.Texts.Select(t => t.Move(mp.Item2, 0)));
            planned.Add(new Rect(mp.Item1.Line + mp.Item2 - 20 / MM, mp.Item1.Line + mp.Item2 + 20 / MM, mp.Item1.Lo, mp.Item1.Hi, "dimline"));
        }
        var movedIds = new HashSet<int>(movePlans.Select(m => m.Item1.D.Id.IntegerValue));
        Func<Rect, bool> clear = rc => !openRects.Any(o => o.Hits(rc)) && !tagRects.Any(o => o.Hits(rc)) && !planned.Any(o => o.Hits(rc))
                                        && !ex.Where(e => !movedIds.Contains(e.D.Id.IntegerValue)).Any(e => e.Texts.Any(t => t.Hits(rc)) || lineRect(e).Hits(rc));

        var plans = new List<Plan>();
        // ---------- vertical ----------
        bool hOnly = args.Value<bool?>("horizontalOnly") ?? false;
        for (int ri = 0; ri < (hOnly ? 0 : rows.Count); ri++)
        {
            var done = new HashSet<string>();
            Func<Op, string> key = p => p.Fi.Symbol.Id.IntegerValue + "|" + Math.Round((p.Z0 - p.LvZ) * MM / 10) + "|" + Math.Round((p.Z1 - p.Z0) * MM / 10);
            foreach (var op in rows[ri]) { if (vCover(op).Item3) done.Add(key(op)); }
            foreach (var op in rows[ri].OrderBy(p => p.X0))
            {
                if (op.T == null || op.Lv == null) continue;
                var c = vCover(op); bool atLv = Math.Abs(op.Z0 - op.LvZ) < 150 / MM;
                if (c.Item3) continue;
                bool partial = c.Item1 || c.Item2;
                var p = new Plan { Kind = "V" };
                if (partial && c.Item4 != null)
                {
                    p.Pos = movedLine.ContainsKey(c.Item4.D.Id.IntegerValue) ? movedLine[c.Item4.D.Id.IntegerValue] : c.Item4.Line;
                    if (c.Item1 && !c.Item2) { p.Refs.Add(op.B); p.At.Add(op.Z0); p.Desc.Add("Bottom"); p.Refs.Add(op.T); p.At.Add(op.Z1); p.Desc.Add("Top"); }
                    else if (!atLv && op.B != null && !op.NoHost && (hostLv || op.Fi.Category.Id.IntegerValue != (int)BuiltInCategory.OST_Doors)) { p.Refs.Add(op.Lv.GetPlaneReference()); p.At.Add(op.LvZ); p.Desc.Add(op.Lv.Name); p.Refs.Add(op.B); p.At.Add(op.Z0); p.Desc.Add("Bottom"); }
                    else continue;
                    p.Why = "complete existing " + op.Name + " id" + op.Fi.Id.IntegerValue;
                }
                else
                {
                    bool each = (eachMinH > 0 && op.Z1 - op.Z0 >= eachMinH) || (eachDoor && op.Fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors);
                    if (!vAll && !each && done.Contains(key(op))) continue;
                    bool isDoorOp = op.Fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors;
                    if (hostLv && op.NoHost) { if (op.B == null) { report.Add("no bottom reference for " + op.Name); continue; } p.Refs.Add(op.B); p.At.Add(op.Z0); p.Desc.Add("Bottom"); }
                    else if (hostLv)
                    {
                        p.Refs.Add(op.Lv.GetPlaneReference()); p.At.Add(op.LvZ); p.Desc.Add(op.Lv.Name);
                        if (!atLv && op.B != null) { p.Refs.Add(op.B); p.At.Add(op.Z0); p.Desc.Add("Bottom"); }
                    }
                    else if (isDoorOp && !atLv && op.B != null) { p.Refs.Add(op.B); p.At.Add(op.Z0); p.Desc.Add("Bottom"); }
                    else
                    {
                        p.Refs.Add(op.Lv.GetPlaneReference()); p.At.Add(op.LvZ); p.Desc.Add(op.Lv.Name);
                        if (!atLv && op.B != null && !isDoorOp) { p.Refs.Add(op.B); p.At.Add(op.Z0); p.Desc.Add("Bottom"); }
                    }
                    p.Refs.Add(op.T); p.At.Add(op.Z1); p.Desc.Add("Top");
                    double best = double.NaN; int bestBad = int.MaxValue;
                    var cands = new List<double>();
                    for (int k = 0; k < 4; k++) cands.Add(op.X0 - (150 + 300 * k) / MM);
                    for (int k = 0; k < 4; k++) cands.Add(op.X1 + 1.85 * h + 300 * k / MM);
                    foreach (var x in cands)
                    {
                        var lr = new Rect(x - 20 / MM, x + 20 / MM, p.At.Min(), p.At.Max(), "");
                        if (openRects.Any(o => o.Hits(lr))) continue;
                        int bad = clear(lr) ? 0 : 1;
                        for (int i = 0; i + 1 < p.At.Count; i++)
                            if (!clear(TextBox(true, x, (p.At[i] + p.At[i + 1]) / 2, Math.Round((p.At[i + 1] - p.At[i]) * MM).ToString(), h))) bad++;
                        if (bad < bestBad) { bestBad = bad; best = x; if (bad == 0) break; }
                    }
                    if (double.IsNaN(best)) { report.Add("no free spot for V dim of " + op.Name); continue; }
                    p.Pos = best;
                    p.Why = "new " + op.Name + " id" + op.Fi.Id.IntegerValue;
                    done.Add(key(op));
                }
                plans.Add(p);
                planned.Add(new Rect(p.Pos - 20 / MM, p.Pos + 20 / MM, p.At.Min(), p.At.Max(), "dimline"));
                for (int i = 0; i + 1 < p.At.Count; i++) planned.Add(TextBox(true, p.Pos, (p.At[i] + p.At[i + 1]) / 2, Math.Round((p.At[i + 1] - p.At[i]) * MM).ToString(), h));
            }
        }

        // ---------- horizontal ----------
        var grids = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>()
            .Where(g => g.Curve is Line && Math.Abs(((Line)g.Curve).Direction.DotProduct(Rg)) < 0.01)
            .Select(g => new { G = g, X = VX(((Line)g.Curve).Origin) }).OrderBy(g => g.X).ToList();
        for (int ri = 0; ri < (vOnly ? 0 : rows.Count); ri++)
        {
            var kept = new List<Op>();
            foreach (var p in rows[ri].Where(q => q.L != null && q.R != null).OrderByDescending(q => q.Depth))
                if (!kept.Any(k => Math.Min(k.X1, p.X1) - Math.Max(k.X0, p.X0) > 100 / MM)) kept.Add(p);
            var row = kept.OrderBy(p => p.X0).ToList();
            var clusters = new List<List<Op>>(); List<Op> cur = null;
            foreach (var op in row)
            {
                if (hCover(op)) { cur = null; continue; }
                if (cur == null) { cur = new List<Op>(); clusters.Add(cur); }
                cur.Add(op);
            }
            double rowBottom = rows[ri].Min(p => p.Z0), rowTop = rows[ri].Max(p => p.Z1);
            double prevTop = ri > 0 ? rows[ri - 1].Max(p => p.Z1) : double.NegativeInfinity;
            double nextBottom = ri + 1 < rows.Count ? rows[ri + 1].Min(p => p.Z0) : double.PositiveInfinity;
            foreach (var cl in clusters)
            {
                var items = new List<Tuple<double, Reference, string>>();
                foreach (var op in cl) { items.Add(Tuple.Create(op.X0, op.L, "L " + op.Name)); items.Add(Tuple.Create(op.X1, op.R, "R " + op.Name)); }
                double lo0 = cl.Min(p => p.X0), hi0 = cl.Max(p => p.X1);
                // grid -> edge -> edge -> grid: the nearest grid on each side of the cluster plus the grids between its openings
                var gFree = grids.Where(g => !rows[ri].Any(p => g.X > p.X0 + 1 / MM && g.X < p.X1 - 1 / MM)).ToList();
                var gLeft = gFree.Where(g => g.X <= lo0 + 1 / MM && g.X >= lo0 - gridNear).OrderByDescending(g => g.X).FirstOrDefault();
                var gRight = gFree.Where(g => g.X >= hi0 - 1 / MM && g.X <= hi0 + gridNear).OrderBy(g => g.X).FirstOrDefault();
                foreach (var g in gFree.Where(g => g.X > lo0 + 1 / MM && g.X < hi0 - 1 / MM).Concat(new[] { gLeft, gRight }).Where(g => g != null).Distinct())
                    items.Add(Tuple.Create(g.X, new Reference(g.G), "Grid " + g.G.Name));
                if (gLeft == null || gRight == null) report.Add("row " + ri + ": no grid within " + Math.Round(gridNear * MM) + " mm on the " + (gLeft == null ? "left" : "right") + " of " + cl.First().Name + ".." + cl.Last().Name);
                items = items.OrderBy(t => t.Item1).ToList();
                var dd = new List<Tuple<double, Reference, string>>();
                foreach (var it in items)
                {
                    var last = dd.LastOrDefault();
                    if (last != null && Math.Abs(it.Item1 - last.Item1) < 2 / MM) { if (last.Item3.StartsWith("Grid") && !it.Item3.StartsWith("Grid")) dd[dd.Count - 1] = it; continue; }
                    dd.Add(it);
                }
                if (dd.Count < 2) continue;
                double xa = dd.First().Item1, xb = dd.Last().Item1;
                // candidate lines: below the row first (ground row: below the level), then above
                // sections (slabs cut): lowest row goes below the ground/foundation poche, upper rows above their openings
                var below = new List<double>(); var above = new List<double>();
                double belowStart = rowBottom - 1.75 * h, belowEnd = Math.Max(prevTop + 0.3 * h, rowBottom - (isSection && ri == 0 ? 8000 : 3500) / MM);
                for (double z = belowStart; z >= belowEnd; z -= 100 / MM) below.Add(z);
                double aboveStart = rowTop + 0.3 * h, aboveEnd = Math.Min(nextBottom - 1.75 * h, rowTop + 3500 / MM);
                for (double z = aboveStart; z <= aboveEnd; z += 100 / MM) above.Add(z);
                var cands = isSection && ri > 0 ? above.ToList() : below.Concat(above).ToList();
                double best = double.NaN; int bestBad = int.MaxValue;
                foreach (var z in cands)
                {
                    var lr = new Rect(xa, xb, z - 20 / MM, z + 20 / MM, "");
                    bool tagHit = tagRects.Any(o => o.Hits(lr));
                    if (openRects.Any(o => o.Hits(lr)) || (tagHit && !isSection)) continue;
                    // keep parallel dim lines apart
                    var band300 = new Rect(xa, xb, z - 300 / MM, z + 300 / MM, "");
                    if (planned.Any(o => o.What == "dimline" && o.Z1 - o.Z0 < 60 / MM && o.Hits(band300)) || ex.Any(e => !e.V && lineRect(e).Hits(band300))) continue;
                    int bad = (clear(lr) ? 0 : 1) + (tagHit ? 2 : 0);
                    for (int i = 0; i + 1 < dd.Count; i++)
                    {
                        double len = dd[i + 1].Item1 - dd[i].Item1;
                        var tb = TextBox(false, (dd[i].Item1 + dd[i + 1].Item1) / 2, z, Math.Round(len * MM).ToString(), h);
                        if (tb.X1 - tb.X0 > len) continue; // small segments get their text moved after creation
                        if (!clear(tb)) bad++;
                    }
                    if (bad < bestBad) { bestBad = bad; best = z; if (bad == 0) break; }
                }
                if (double.IsNaN(best)) { report.Add("no free line for H chain row " + ri); continue; }
                var p = new Plan { Kind = "H", Pos = best, Why = "row " + ri + " (" + cl.Count + " openings)" };
                foreach (var it in dd) { p.Refs.Add(it.Item2); p.At.Add(it.Item1); p.Desc.Add(it.Item3); }
                plans.Add(p);
                planned.Add(new Rect(xa, xb, best - 20 / MM, best + 20 / MM, "dimline"));
                for (int i = 0; i + 1 < dd.Count; i++) planned.Add(TextBox(false, (dd[i].Item1 + dd[i + 1].Item1) / 2, best, Math.Round((dd[i + 1].Item1 - dd[i].Item1) * MM).ToString(), h));
            }
        }

        // duplicates among existing dims (same segment twice next to each other)
        var dups = new List<string>();
        for (int i = 0; i < ex.Count; i++)
            for (int j = i + 1; j < ex.Count; j++)
            {
                var a = ex[i]; var b = ex[j];
                if (a.V != b.V || Math.Abs(a.Line - b.Line) > 3000 / MM) continue;
                for (int k = 0; k + 1 < a.Bnd.Count; k++)
                    if (b.Bnd.Any(q => Math.Abs(q - a.Bnd[k]) < 10 / MM) && b.Bnd.Any(q => Math.Abs(q - a.Bnd[k + 1]) < 10 / MM)
                        && !b.Bnd.Any(q => q > a.Bnd[k] + 10 / MM && q < a.Bnd[k + 1] - 10 / MM))
                    { dups.Add(a.D.Id.IntegerValue + (a.Own ? "(check)" : "") + " & " + b.D.Id.IntegerValue + (b.Own ? "(check)" : "") + " both show " + Math.Round((a.Bnd[k + 1] - a.Bnd[k]) * MM)); break; }
            }

        var info = new
        {
            View = v.Name, Visibility = v3 == null ? "all facing" : "ray:" + v3.Name, Openings = ops.Count,
            Obstacles = string.Join(", ", tagRects.GroupBy(r => r.What).Select(g => g.Key + " " + g.Count())),
            NotFrame = ops.Where(p => !p.Frame).Select(p => p.Name + " id" + p.Fi.Id.IntegerValue).ToList(),
            Rows = rows.Select((rw, i) => new { Row = i, Z = Math.Round(rw.Min(p => p.Z0) * MM) + ".." + Math.Round(rw.Max(p => p.Z1) * MM), N = rw.Count, Hdone = rw.Count(hCover) }).ToList(),
            MoveExisting = movePlans.Select(m => m.Item1.D.Id.IntegerValue + " by " + Math.Round(m.Item2 * MM) + " mm").ToList(),
            Planned = plans.Select(p => new { p.Kind, Pos = Math.Round(p.Pos * MM), p.Why, Values = string.Join(" | ", p.At.Zip(p.At.Skip(1), (a, b) => Math.Round((b - a) * MM))), Refs = string.Join(" | ", p.Desc) }).ToList(),
            FrameSizes = ops.GroupBy(p => p.Fi.Symbol.Name).Select(g => g.Key + " -> " + string.Join(", ", g.Select(p => Math.Round((p.X1 - p.X0) * MM) + "x" + Math.Round((p.Z1 - p.Z0) * MM)).Distinct())).ToList(),
            TypeMarkSizes = ops.GroupBy(p => p.TypeMark).Where(g => g.Select(p => p.Fi.Symbol.Name).Distinct().Count() > 1).Select(g => g.Key + ": " + string.Join(" / ", g.Select(p => p.Fi.Symbol.Name).Distinct())).ToList(),
            ExistingDuplicates = dups,
            HostLevel = hostNotes,
            StaleOwnDims = stale.Select(e => e.D.Id.IntegerValue).ToList(),
            Notes = report
        };
        if (mode != "apply") return info;

        // ---------- apply ----------
        var createdIds = new List<int>(); var created = new List<object>(); var errors = new List<string>(); var textMoves = new List<string>();
        var deletedStale = new JArray();
        using (var tx = new Transaction(doc, "Opening dims " + v.Name))
        {
            tx.Start();
            foreach (var e in stale)
            {
                deletedStale.Add(new JObject { ["id"] = e.D.Id.IntegerValue, ["values"] = string.Join("|", e.Bnd.Zip(e.Bnd.Skip(1), (a, b) => Math.Round((b - a) * MM))),
                    ["refs"] = new JArray(e.D.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc))) });
                doc.Delete(e.D.Id);
            }
            var dt = dtExisting;
            if (dt == null) { dt = (DimensionType)dtBase.Duplicate(typeName); dt.get_Parameter(BuiltInParameter.LINE_COLOR).Set(col[0] + (col[1] << 8) + (col[2] << 16)); }
            foreach (var mp in movePlans)
            {
                var vec = Rg * mp.Item2;
                ElementTransformUtils.MoveElement(doc, mp.Item1.D.Id, vec);
                moveLog.Add(new JObject { ["id"] = mp.Item1.D.Id.IntegerValue, ["dx"] = vec.X, ["dy"] = vec.Y, ["dz"] = vec.Z });
                mp.Item1.Line += mp.Item2; mp.Item1.Texts = mp.Item1.Texts.Select(t => t.Move(mp.Item2, 0)).ToList();
            }
            var allTexts = ex.SelectMany(e => e.Texts).ToList();
            foreach (var p in plans)
            {
                try
                {
                    var ra = new ReferenceArray(); foreach (var rf in p.Refs) ra.Append(rf);
                    Line line;
                    if (p.Kind == "V") { var b = O + Rg * p.Pos; line = Line.CreateBound(new XYZ(b.X, b.Y, 0), new XYZ(b.X, b.Y, 10)); }
                    else { var a = new XYZ(O.X, O.Y, p.Pos); line = Line.CreateBound(a, a + Rg * 10); }
                    var d = doc.Create.NewDimension(v, line, ra, dt);
                    doc.Regenerate();
                    createdIds.Add(d.Id.IntegerValue);
                    // move texts that collide with openings, tags or other texts
                    var segs = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(s => Tuple.Create((object)s, s.TextPosition, s.ValueString, s.IsTextPositionAdjustable())).ToList()
                                                      : new List<Tuple<object, XYZ, string, bool>> { Tuple.Create((object)d, d.TextPosition, d.ValueString, d.IsTextPositionAdjustable()) };
                    bool vert = p.Kind == "V";
                    Func<Rect, bool> ok = rc => !openRects.Any(o => o.Hits(rc)) && !tagRects.Any(o => o.Hits(rc)) && !allTexts.Any(o => o.Hits(rc));
                    foreach (var s in segs)
                    {
                        var tb = TextBox(vert, VX(s.Item2), s.Item2.Z, s.Item3, h);
                        if (ok(tb) || !s.Item4) { allTexts.Add(tb); continue; }
                        double w = tb.X1 - tb.X0, ht = tb.Z1 - tb.Z0;
                        var offs = vert
                            ? new[] { Tuple.Create(-1.3 * h, 0.0), Tuple.Create(-2.6 * h, 0.0), Tuple.Create(1.85 * h, 0.0), Tuple.Create(0.0, ht + 0.1 * h), Tuple.Create(0.0, -(ht + 0.1 * h)), Tuple.Create(-1.3 * h, ht), Tuple.Create(-1.3 * h, -ht) }
                            : new[] { Tuple.Create(0.0, 1.3 * h), Tuple.Create(0.0, 2.6 * h), Tuple.Create(0.0, -1.85 * h), Tuple.Create(w + 0.1 * h, 0.0), Tuple.Create(-(w + 0.1 * h), 0.0), Tuple.Create(w * 0.6, 1.3 * h), Tuple.Create(-w * 0.6, 1.3 * h) };
                        bool moved = false;
                        foreach (var of in offs)
                        {
                            var nb = tb.Move(of.Item1, of.Item2);
                            if (!ok(nb)) continue;
                            var np = s.Item2 + Rg * of.Item1 + XYZ.BasisZ * of.Item2;
                            if (s.Item1 is DimensionSegment ds) ds.TextPosition = np; else ((Dimension)s.Item1).TextPosition = np;
                            allTexts.Add(nb); moved = true; textMoves.Add(d.Id.IntegerValue + " '" + s.Item3 + "'"); break;
                        }
                        if (!moved) { allTexts.Add(tb); report.Add("text '" + s.Item3 + "' of dim " + d.Id.IntegerValue + " still overlaps"); }
                    }
                    var vals = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList() : new List<double> { Math.Round((d.Value ?? 0) * MM) };
                    created.Add(new { Id = d.Id.IntegerValue, p.Kind, p.Why, Values = string.Join(" | ", vals) });
                }
                catch (Exception exn) { errors.Add(p.Kind + " " + p.Why + ": " + exn.Message); }
            }
            tx.Commit();
        }
        if (logPath != null)
        {
            JObject lg = null;
            if (File.Exists(logPath)) { var tk = JToken.Parse(File.ReadAllText(logPath)); lg = tk as JObject; }
            if (lg == null) lg = new JObject { ["created"] = new JArray(), ["moved"] = new JArray() };
            foreach (var i in createdIds) ((JArray)lg["created"]).Add(i);
            foreach (var m in moveLog) ((JArray)lg["moved"]).Add(m);
            if (deletedStale.Count > 0) { if (lg["deletedStale"] == null) lg["deletedStale"] = new JArray(); foreach (var s in deletedStale) ((JArray)lg["deletedStale"]).Add(s); }
            File.WriteAllText(logPath, lg.ToString(Formatting.None));
        }
        return new { info.View, info.Openings, info.NotFrame, info.MoveExisting, DeletedStale = deletedStale.Count, info.HostLevel, Created = created, TextMoved = textMoves.Count, Errors = errors, info.FrameSizes, info.TypeMarkSizes, info.ExistingDuplicates, Notes = report, LogPath = logPath };
    }
}
