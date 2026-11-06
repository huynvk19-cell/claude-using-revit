/* mcp-tool
{
  "description": "Fill cut structural framing, floors, foundations, walls on ONE section view: one region per type, touching profiles merge; an earth region covers what stays hidden underground. preview | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "viewId": { "type": "integer" },
      "sources": { "type": "array", "items": { "type": "string" }, "description": "Structural model titles (link title, or HOST)" },
      "concreteType": { "type": "string", "description": "Official filled region type for concrete" },
      "steelType": { "type": "string", "description": "Official filled region type for steel" },
      "suffix": { "type": "string", "description": "Mark type suffix from the profile" },
      "lineStyle": { "type": "string", "description": "Line style of the region boundary" },
      "earthType": { "type": "string", "description": "Official filled region type of the earth" },
      "groundLevel": { "type": "string", "description": "Host level name of the ground floor" },
      "pileCapPatterns": { "type": "array", "items": { "type": "string" }, "description": "Regex on family : type of pile caps and piles, never filled" },
      "slabBandMm": { "type": "number", "description": "Depth of the kept slab bands, default 1000" },
      "touchGapMm": { "type": "number", "description": "Underground gap that still counts as touching a slab, default 50" },
      "logPath": { "type": "string" }
    },
    "required": ["mode", "viewId", "sources", "concreteType", "steelType", "suffix", "lineStyle", "earthType", "groundLevel", "pileCapPatterns"]
  },
  "timeoutSeconds": 600
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Standard: drafting-section-general.md SF1-SF8. Scans Structural Framing, Floors, Structural Foundations and Walls
//    with the Structural parameter on (concrete when the material or the type name says CONCRETE) of
//    `sources` (visible loaded links, or HOST) and takes the section of each element at the view's cut plane.
//    Region type by Structural Material: CONCRETE -> concreteType, METAL/STEEL -> steelType. Concrete and steel never
//    merge. A foundation without material counts as concrete (noMaterialFoundations); any other element without a
//    known material is left out and listed in `unknown`.
//    Only solids with volume on both sides of the cut plane count: an element merely touching the plane is not cut.
//    Underground (top of the section at or below groundLevel): slabs (Floor class) within slabBandMm under the ground
//    level or above the lowest slab are kept; every other underground profile is kept only if it connects to a kept slab, directly or through other kept profiles
//    (touching, or within touchGapMm: joint gap).
//    Foundations matching pileCapPatterns are never filled. Each region type gets ONE region per view, all its loops
//    in it. splitRegions > 0 = Revit refused them together: merged profiles it cannot draw stay as separate parts in
//    their own regions (listed in fails), everything else stays one region.
//    Earth: one masking region of <earthType><suffix>, drawn first so the structure stays on top. Its outline = the
//    outer loops of the earth regions already on the view (left untouched), holes dropped, plus every hidden profile
//    and pile cap, so those are covered. earthSources 0 = no earth region on the view: only the hidden profiles get it.
//    Regions use the mark types <type><suffix> (created when missing, an existing one keeps its hatch). Re-running
//    replaces the regions of those mark types on the view that this user created; every other region stays.
//    officialInView > 0 = regions of the official types already there: the new set may double them, check first.
//    preview runs everything, then rolls back. Full element list per region: logPath.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class StructuralCutFill
{
    const double MM = 304.8;
    class Item { public Solid S; public string Kind; public double X0, X1, Y0, Y1, ZTop; public bool IsSlab; public JArray Members = new JArray(); public List<Solid> Parts = new List<Solid>(); }

    static XYZ org, dir;

    static TransactionGroup tg;

    public static object Run(UIApplication app, JObject args)
    {
        tg = null;
        try { return Go(app, args); }
        catch { if (tg != null && tg.GetStatus() == TransactionStatus.Started) tg.RollBack(); throw; }
    }

    static object Go(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = doc.GetElement(new ElementId((int)args["viewId"])) as View;
        if (v == null) return new JObject { ["error"] = "view not found" };
        if (v.ViewType != ViewType.Section && v.ViewType != ViewType.Detail)
            return new JObject { ["error"] = "not a section view: " + v.ViewType };
        bool apply = (string)args["mode"] == "apply";
        var cb = v.CropBox; var inv = cb.Transform.Inverse; org = v.Origin; dir = v.ViewDirection;
        double oz = inv.OfPoint(org).Z;

        string suffix = (string)args["suffix"];
        if (string.IsNullOrEmpty(suffix)) return new JObject { ["error"] = "suffix is required" };
        var types = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var offC = types.FirstOrDefault(t => t.Name == (string)args["concreteType"]);
        var offS = types.FirstOrDefault(t => t.Name == (string)args["steelType"]);
        var offE = types.FirstOrDefault(t => t.Name == (string)args["earthType"]);
        if (offC == null || offS == null || offE == null) return new JObject { ["error"] = "filled region type not found" };

        var lineCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories.Cast<Category>().FirstOrDefault(c => c.Name == (string)args["lineStyle"]);
        if (lineCat == null) return new JObject { ["error"] = "line style not found: " + args["lineStyle"] };
        var lineId = lineCat.GetGraphicsStyle(GraphicsStyleType.Projection).Id;

        var ground = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => l.Name == (string)args["groundLevel"]);
        if (ground == null) return new JObject { ["error"] = "level not found: " + args["groundLevel"] };
        double groundZ = ground.ProjectElevation, band = (args["slabBandMm"] != null ? (double)args["slabBandMm"] : 1000.0) / MM;
        var capRx = ((JArray)args["pileCapPatterns"]).Select(p => new Regex((string)p, RegexOptions.IgnoreCase)).ToList();
        int nCap = 0;
        var covered = new List<Item>();
        var wanted = ((JArray)args["sources"]).Select(s => (string)s).ToList();
        var sources = new List<Tuple<string, Document, Transform>>();
        if (wanted.Contains("HOST")) sources.Add(Tuple.Create("HOST", doc, Transform.Identity));
        foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            var ld = l.GetLinkDocument(); if (ld == null || l.IsHidden(v) || !wanted.Contains(ld.Title)) continue;
            if (!sources.Any(s => s.Item1 == ld.Title)) sources.Add(Tuple.Create(ld.Title, ld, l.GetTotalTransform()));
        }
        var missing = wanted.Where(w => !sources.Any(s => s.Item1 == w)).ToList();

        var opt = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
        var fails = new JArray(); var unknown = new JArray(); var noMat = new JArray();
        var items = new List<Item>();
        int nFr = 0, nFl = 0, nFo = 0, nWa = 0;
        var cats = new List<BuiltInCategory> { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Walls };
        foreach (var s in sources)
        {
            var T = s.Item3; var Ti = T.Inverse; var d = s.Item2;
            var planeL = Plane.CreateByNormalAndOrigin(Ti.OfVector(dir).Normalize(), Ti.OfPoint(org));
            foreach (var e in new FilteredElementCollector(d).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(cats)))
            {
                bool isWall = e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Walls;
                if (isWall && (!(e is Wall) || e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() != 1)) continue;
                var bb = e.get_BoundingBox(null); if (bb == null) continue;
                var pts = new List<XYZ>();
                foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z }) pts.Add(inv.OfPoint(T.OfPoint(new XYZ(x, y, z))));
                if (pts.Min(p => p.Z) > oz || pts.Max(p => p.Z) < oz) continue;
                if (pts.Max(p => p.X) < cb.Min.X || pts.Min(p => p.X) > cb.Max.X || pts.Max(p => p.Y) < cb.Min.Y || pts.Min(p => p.Y) > cb.Max.Y) continue;

                var ty = d.GetElement(e.GetTypeId()) as ElementType;
                var name = (ty?.FamilyName ?? "") + " : " + (ty?.Name ?? "");
                var matP = e.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM) ?? ty?.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                string mat = matP != null && matP.AsElementId() != ElementId.InvalidElementId ? d.GetElement(matP.AsElementId())?.Name ?? "" : "";
                bool isFound = e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFoundation;
                bool steel = Has(mat, "METAL") || Has(mat, "STEEL");
                bool conc = Has(mat, "CONCRETE") || (isWall && Has(name, "CONCRETE"));

                var caps = CutProfiles(e, opt, planeL, T, inv, cb, fails, name);
                if (caps.Count == 0) continue;
                if (isFound && capRx.Any(r => r.IsMatch(name))) { nCap++; covered.AddRange(caps); continue; }
                string kind = steel ? "steel" : conc ? "concrete" : isFound ? "concrete" : null;
                if (kind == null) { unknown.Add(e.Id.IntegerValue + " " + name + " material: " + mat); continue; }
                if (!steel && !conc) noMat.Add(e.Id.IntegerValue + " " + name);
                if (isFound) nFo++; else if (isWall) nWa++; else if (e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Floors) nFl++; else nFr++;
                foreach (var c in caps)
                {
                    c.Kind = kind; c.IsSlab = e is Floor;
                    c.Members.Add(new JObject { ["src"] = s.Item1, ["id"] = e.Id.IntegerValue, ["cat"] = e.Category.Name, ["type"] = name, ["mat"] = mat });
                    items.Add(c);
                }
            }
        }
        var hiddenTypes = FilterUnderground(items, covered, groundZ, band, (args["touchGapMm"] != null ? (double)args["touchGapMm"] : 50.0) / MM);
        int nHidden = hiddenTypes.Properties().Sum(g => (int)g.Value);
        int profiles = items.Count;
        int merges = Merge(items, out var notMerged);

        tg = new TransactionGroup(doc, "Update structural cut regions"); tg.Start();
        FilledRegionType markC = null, markS = null, markE = null;
        int official = 0, kept = 0; var earthSrc = new List<FilledRegion>(); var oldIds = new JArray();
        bool ready = Step(doc, () =>
        {
            markC = MarkType(types, offC, suffix); markS = MarkType(types, offS, suffix); markE = MarkType(types, offE, suffix);
            markE.IsMasking = true;
            var inView = new FilteredElementCollector(doc, v.Id).OfClass(typeof(FilledRegion)).Cast<FilledRegion>().ToList();
            official = inView.Count(r => r.GetTypeId() == offC.Id || r.GetTypeId() == offS.Id);
            earthSrc = inView.Where(r => r.GetTypeId() == offE.Id).ToList();
            foreach (var r in inView.Where(r => r.GetTypeId() == markC.Id || r.GetTypeId() == markS.Id || r.GetTypeId() == markE.Id))
            {
                if (doc.IsWorkshared && WorksharingUtils.GetWorksharingTooltipInfo(doc, r.Id).Creator != app.Application.Username) { kept++; continue; }
                oldIds.Add(r.Id.IntegerValue); doc.Delete(r.Id);
            }
        }, fails, "prepare");
        if (!ready) { tg.RollBack(); return new JObject { ["error"] = "could not prepare the view", ["fails"] = fails }; }
        int earthSources = 0;
        var earthMade = DrawEarth(doc, v, earthSrc, covered, markE.Id, inv, fails, out earthSources);
        var log = new JArray(); int nC = 0, nS = 0, split = 0;
        foreach (var kind in new[] { "concrete", "steel" })
        {
            var group = items.Where(i => i.Kind == kind).ToList();
            if (group.Count == 0) continue;
            var tid = kind == "concrete" ? markC.Id : markS.Id;
            var made = new List<ElementId>();
            bool one = Step(doc, () =>
            {
                var fr = FilledRegion.Create(doc, tid, v.Id, group.SelectMany(i => Caps(i.S)).SelectMany(pf => pf.GetEdgesAsCurveLoops()).ToList());
                fr.SetLineStyleId(lineId); made.Add(fr.Id);
            }, fails, kind + " as one region");
            if (!one)
            {
                // Find the merged profiles Revit cannot draw; those stay as their separate parts, the rest stay one region.
                split++; made.Clear();
                var probes = new List<ElementId>(); var bad = new List<Item>();
                foreach (var it in group)
                {
                    var ids = string.Join(",", it.Members.Select(m => (string)m["id"]));
                    var made1 = new List<ElementId>();
                    bool ok1 = Step(doc, () => { foreach (var pf in Caps(it.S)) made1.Add(FilledRegion.Create(doc, tid, v.Id, pf.GetEdgesAsCurveLoops().ToList()).Id); }, fails, "merged " + ids);
                    if (ok1) probes.AddRange(made1); else bad.Add(it);
                }
                Step(doc, () => { foreach (var id in probes) doc.Delete(id); }, fails, "probes");
                var good = group.Where(i => !bad.Contains(i)).ToList();
                bool goodOne = good.Count > 0 && Step(doc, () =>
                {
                    var fr = FilledRegion.Create(doc, tid, v.Id, good.SelectMany(i => Caps(i.S)).SelectMany(pf => pf.GetEdgesAsCurveLoops()).ToList());
                    fr.SetLineStyleId(lineId); made.Add(fr.Id);
                }, fails, kind + " without the merged profiles it refused");
                foreach (var it in goodOne ? bad : group)
                    foreach (var part in it.Parts.Count > 1 && bad.Contains(it) ? it.Parts : new List<Solid> { it.S })
                        foreach (var pf in Caps(part))
                        {
                            ElementId nid = null;
                            if (Step(doc, () => { var fr = FilledRegion.Create(doc, tid, v.Id, pf.GetEdgesAsCurveLoops().ToList()); fr.SetLineStyleId(lineId); nid = fr.Id; }, fails, "part of " + (string)it.Members[0]["id"])) made.Add(nid);
                        }
            }
            if (kind == "concrete") nC += made.Count; else nS += made.Count;
            log.Add(new JObject { ["kind"] = kind, ["regions"] = new JArray(made.Select(i => i.IntegerValue)), ["members"] = new JArray(group.SelectMany(i => i.Members)) });
        }
        if (apply) tg.Assimilate(); else tg.RollBack();

        var res = new JObject
        {
            ["mode"] = apply ? "apply" : "preview", ["view"] = v.Name, ["scale"] = v.Scale,
            ["framing"] = nFr, ["floors"] = nFl, ["foundations"] = nFo, ["walls"] = nWa, ["profiles"] = profiles, ["merges"] = merges,
            ["concreteRegions"] = nC, ["steelRegions"] = nS, ["earthRegions"] = earthMade, ["earthSources"] = earthSources, ["replaced"] = oldIds.Count, ["keptOthers"] = kept, ["officialInView"] = official,
            ["notMergedPairs"] = notMerged.Count, ["splitRegions"] = split,
            ["pileCapsSkipped"] = nCap, ["undergroundHidden"] = nHidden, ["undergroundHiddenTypes"] = hiddenTypes,
            ["noMaterialFoundations"] = noMat, ["unknown"] = unknown, ["missingSources"] = new JArray(missing), ["fails"] = fails
        };
        var lp = (string)args["logPath"];
        if (lp != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lp));
            File.WriteAllText(lp, new JObject { ["summary"] = res.DeepClone(), ["replacedIds"] = oldIds, ["notMerged"] = new JArray(notMerged), ["groups"] = log }.ToString());
        }
        return res;
    }

    // One transaction per step; a region Revit cannot draw rolls back here instead of opening an error dialog.
    class RollBackOnError : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (var f in a.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
                else return FailureProcessingResult.ProceedWithRollBack;
            }
            return FailureProcessingResult.Continue;
        }
    }

    static bool Step(Document doc, Action act, JArray fails, string what)
    {
        using (var t = new Transaction(doc, "Update structural cut regions"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(new RollBackOnError()); o.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(o);
            t.Start();
            try { act(); }
            catch (Exception ex) { t.RollBack(); fails.Add(what + ": " + ex.Message); return false; }
            if (t.Commit() == TransactionStatus.Committed) return true;
            fails.Add(what + ": Revit cannot draw it");
            return false;
        }
    }

    static bool Has(string s, string w) { return s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0; }

    static FilledRegionType MarkType(List<FilledRegionType> types, FilledRegionType official, string suffix)
    {
        var name = official.Name + suffix;
        return types.FirstOrDefault(t => t.Name == name) ?? (FilledRegionType)official.Duplicate(name);
    }

    // One flat prism per section face of the element, standing on the cut plane.
    static List<Item> CutProfiles(Element e, Options opt, Plane planeL, Transform T, Transform inv, BoundingBoxXYZ cb, JArray fails, string name)
    {
        var res = new List<Item>();
        foreach (var so in Union(Walk(e.get_Geometry(opt)).ToList()))
        {
            Solid cut = null;
            try { cut = BooleanOperationsUtils.CutWithHalfSpace(so, planeL); } catch (Exception ex) { fails.Add(e.Id.IntegerValue + " slice: " + ex.Message); continue; }
            if (cut == null || cut.Volume < 1e-9) continue;
            // Really cut through: the solid must have volume on both sides of the plane, not only a face lying on it.
            Solid back = null;
            try { back = BooleanOperationsUtils.CutWithHalfSpace(so, Plane.CreateByNormalAndOrigin(planeL.Normal.Negate(), planeL.Origin)); } catch { }
            if (back == null || back.Volume < 1e-6 || cut.Volume < 1e-6) continue;
            foreach (Face f in cut.Faces)
            {
                var pf = f as PlanarFace; if (pf == null) continue;
                if (Math.Abs(T.OfVector(pf.FaceNormal).DotProduct(dir)) < 0.9999) continue;
                if (Math.Abs((T.OfPoint(pf.Origin) - org).DotProduct(dir)) > 1e-4) continue;
                var move = Transform.CreateTranslation(dir.Multiply((org - T.OfPoint(pf.Origin)).DotProduct(dir))).Multiply(T);
                var loops = new List<CurveLoop>(); double x0 = 1e12, x1 = -1e12, y0 = 1e12, y1 = -1e12, zt = -1e12;
                foreach (CurveLoop cl in pf.GetEdgesAsCurveLoops())
                {
                    var cs = new List<Curve>();
                    foreach (var c in cl)
                    {
                        if (c is Line || c is Arc) cs.Add(c.CreateTransformed(move));
                        else foreach (var ln in Polyline(c)) cs.Add(ln.CreateTransformed(move));
                    }
                    foreach (var c in cs) { zt = Math.Max(zt, c.GetEndPoint(0).Z); var p = inv.OfPoint(c.GetEndPoint(0)); x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
                    loops.Add(CurveLoop.Create(cs));
                }
                if (x1 < cb.Min.X || x0 > cb.Max.X || y1 < cb.Min.Y || y0 > cb.Max.Y) continue;
                try
                {
                    var it = new Item { S = GeometryCreationUtilities.CreateExtrusionGeometry(loops, dir, 100.0 / MM), X0 = x0, X1 = x1, Y0 = y0, Y1 = y1, ZTop = zt };
                    it.Parts.Add(it.S);
                    res.Add(it);
                }
                catch (Exception ex) { fails.Add(e.Id.IntegerValue + " " + name + " prism: " + ex.Message); }
            }
        }
        return res;
    }

    // Underground rule SF7: keep slabs near the ground level and at the bottom, and what touches those slabs.
    static JObject FilterUnderground(List<Item> items, List<Item> covered, double groundZ, double band, double gap)
    {
        double tol = 1.0 / MM;
        var under = items.Where(i => i.ZTop <= groundZ + tol).ToList();
        var slabs = under.Where(i => i.IsSlab).ToList();
        double low = slabs.Count > 0 ? slabs.Min(i => i.ZTop) : groundZ;
        var seeds = slabs.Where(i => i.ZTop >= groundZ - band || i.ZTop <= low + band).ToList();
        var rest = under.Where(i => !seeds.Contains(i)).ToList();
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var it in rest.ToList())
            {
                if (!seeds.Any(sl => Connected(it, sl, gap, tol))) continue;
                seeds.Add(it); rest.Remove(it); grew = true;
            }
        }
        var hidden = new JObject();
        foreach (var it in rest)
        {
            items.Remove(it); covered.Add(it);
            var key = (string)it.Members[0]["type"];
            hidden[key] = (hidden[key] != null ? (int)hidden[key] : 0) + 1;
        }
        return hidden;
    }

    // Earth first, so the structure drawn after it stays on top. Others' earth regions only lend their outline.
    static int DrawEarth(Document doc, View v, List<FilledRegion> sources, List<Item> covered, ElementId typeId, Transform inv, JArray fails, out int nSources)
    {
        nSources = sources.Count;
        var solids = new List<Solid>();
        ElementId style = null;
        foreach (var r in sources)
        {
            if (style == null)
            {
                var ce = r.GetDependentElements(new ElementClassFilter(typeof(CurveElement))).Select(i => doc.GetElement(i)).OfType<CurveElement>().FirstOrDefault();
                if (ce != null) style = ce.LineStyle.Id;
            }
            var loops = r.GetBoundaries().ToList();
            foreach (var cl in loops)
            {
                var box = Box(cl, inv);
                if (loops.Any(o => o != cl && Inside(box, Box(o, inv)))) continue;
                var p = cl.First().GetEndPoint(0);
                var flat = CurveLoop.CreateViaTransform(cl, Transform.CreateTranslation(dir.Multiply((org - p).DotProduct(dir))));
                try { solids.Add(GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { flat }, dir, 100.0 / MM)); }
                catch (Exception ex) { fails.Add("earth outline: " + ex.Message); }
            }
        }
        foreach (var it in covered) solids.Add(it.S);
        if (solids.Count == 0) return 0;
        var merged = new List<Solid> { solids[0] };
        foreach (var so in solids.Skip(1))
        {
            try { merged[0] = BooleanOperationsUtils.ExecuteBooleanOperation(merged[0], so, BooleanOperationsType.Union); }
            catch { merged.Add(so); }
        }
        int made = 0;
        if (Step(doc, () =>
        {
            var fr = FilledRegion.Create(doc, typeId, v.Id, merged.SelectMany(Caps).SelectMany(pf => pf.GetEdgesAsCurveLoops()).ToList());
            if (style != null) fr.SetLineStyleId(style);
        }, fails, "earth as one region")) return 1;
        foreach (var pf in merged.SelectMany(Caps))
        {
            if (Step(doc, () =>
            {
                var fr = FilledRegion.Create(doc, typeId, v.Id, pf.GetEdgesAsCurveLoops().ToList());
                if (style != null) fr.SetLineStyleId(style);
            }, fails, "earth part")) made++;
        }
        return made;
    }

    static double[] Box(CurveLoop cl, Transform inv)
    {
        var pts = cl.Select(c => inv.OfPoint(c.GetEndPoint(0))).ToList();
        return new[] { pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y) };
    }

    static bool Inside(double[] a, double[] b) { return a[0] >= b[0] && a[1] >= b[1] && a[2] <= b[2] && a[3] <= b[3]; }

    static bool Connected(Item a, Item b, double gap, double tol)
    {
        double gx = Math.Max(a.X0 - b.X1, b.X0 - a.X1), gy = Math.Max(a.Y0 - b.Y1, b.Y0 - a.Y1), g = Math.Max(gx, gy);
        if (g > gap) return false;
        if (g > tol) return true;
        Solid u = null;
        try { u = BooleanOperationsUtils.ExecuteBooleanOperation(a.S, b.S, BooleanOperationsType.Union); } catch { }
        return u != null && Caps(u).Count < Caps(a.S).Count + Caps(b.S).Count;
    }

    // Union pairs of one kind whose boxes touch; a union that keeps two faces on the cut plane did not really touch.
    static int Merge(List<Item> items, out List<string> notMerged)
    {
        double tol = 0.5 / MM; int merges = 0; bool again = true;
        var seen = new HashSet<string>(); notMerged = new List<string>();
        while (again)
        {
            again = false;
            for (int i = 0; i < items.Count && !again; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    var a = items[i]; var b = items[j];
                    if (a.Kind != b.Kind) continue;
                    if (a.X0 > b.X1 + tol || b.X0 > a.X1 + tol || a.Y0 > b.Y1 + tol || b.Y0 > a.Y1 + tol) continue;
                    var key = string.Join(",", a.Members.Select(m => (string)m["id"])) + "|" + string.Join(",", b.Members.Select(m => (string)m["id"]));
                    if (seen.Contains(key)) continue;
                    Solid u = null;
                    try { u = BooleanOperationsUtils.ExecuteBooleanOperation(a.S, b.S, BooleanOperationsType.Union); } catch { }
                    if (u == null || Caps(u).Count >= Caps(a.S).Count + Caps(b.S).Count) { seen.Add(key); notMerged.Add(key); continue; }
                    a.S = u; a.X0 = Math.Min(a.X0, b.X0); a.X1 = Math.Max(a.X1, b.X1); a.Y0 = Math.Min(a.Y0, b.Y0); a.Y1 = Math.Max(a.Y1, b.Y1);
                    foreach (var m in b.Members) a.Members.Add(m);
                    a.Parts.AddRange(b.Parts);
                    items.RemoveAt(j); merges++; again = true; break;
                }
        }
        return merges;
    }

    static List<PlanarFace> Caps(Solid s)
    {
        var res = new List<PlanarFace>();
        foreach (Face f in s.Faces)
        {
            var pf = f as PlanarFace; if (pf == null) continue;
            if (Math.Abs(pf.FaceNormal.DotProduct(dir)) < 0.9999) continue;
            if (Math.Abs((pf.Origin - org).DotProduct(dir)) > 1e-4) continue;
            res.Add(pf);
        }
        return res;
    }

    static List<Line> Polyline(Curve c)
    {
        var pts = c.Tessellate().ToList(); var keep = new List<XYZ> { pts[0] };
        for (int i = 1; i < pts.Count - 1; i++) if (pts[i].DistanceTo(keep.Last()) > 3.0 / MM && pts[i].DistanceTo(pts.Last()) > 3.0 / MM) keep.Add(pts[i]);
        keep.Add(pts.Last());
        var res = new List<Line>();
        for (int i = 0; i < keep.Count - 1; i++) res.Add(Line.CreateBound(keep[i], keep[i + 1]));
        return res;
    }

    static List<Solid> Union(List<Solid> ss)
    {
        if (ss.Count < 2) return ss;
        var res = new List<Solid>(); Solid cur = ss[0];
        for (int i = 1; i < ss.Count; i++)
        {
            try { cur = BooleanOperationsUtils.ExecuteBooleanOperation(cur, ss[i], BooleanOperationsType.Union); }
            catch { res.Add(ss[i]); }
        }
        res.Add(cur);
        return res;
    }

    static IEnumerable<Solid> Walk(GeometryElement ge)
    {
        if (ge == null) yield break;
        foreach (var g in ge)
        {
            if (g is Solid s && s.Volume > 1e-9) yield return s;
            else if (g is GeometryInstance gi) foreach (var x in Walk(gi.GetInstanceGeometry())) yield return x;
        }
    }
}
