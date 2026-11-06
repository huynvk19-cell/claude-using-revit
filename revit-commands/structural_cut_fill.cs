/* mcp-tool
{
  "description": "Fill cut structural framing, floors, foundations on ONE section view: one region per type, touching profiles merge, underground filtered. preview | apply.",
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
      "groundLevel": { "type": "string", "description": "Host level name of the ground floor" },
      "pileCapPatterns": { "type": "array", "items": { "type": "string" }, "description": "Regex on family : type of pile caps and piles, never filled" },
      "slabBandMm": { "type": "number", "description": "Depth of the kept slab bands, default 1000" },
      "logPath": { "type": "string" }
    },
    "required": ["mode", "viewId", "sources", "concreteType", "steelType", "suffix", "lineStyle", "groundLevel", "pileCapPatterns"]
  },
  "timeoutSeconds": 600
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Standard: drafting-section-general.md SF1-SF8. Scans Structural Framing, Floors and Structural Foundations of
//    `sources` (visible loaded links, or HOST) and takes the section of each element at the view's cut plane.
//    Region type by Structural Material: CONCRETE -> concreteType, METAL/STEEL -> steelType. Concrete and steel never
//    merge. A foundation without material counts as concrete (noMaterialFoundations); any other element without a
//    known material is left out and listed in `unknown`.
//    Underground (top of the section at or below groundLevel): slabs (Floor class) within slabBandMm under the ground
//    level or above the lowest slab are kept; every other underground profile is kept only if it touches a kept slab.
//    Foundations matching pileCapPatterns are never filled. Each region type gets ONE region per view, all its loops
//    in it (splitRegions > 0 = Revit refused the loops together, so that type got one region per profile).
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
    class Item { public Solid S; public string Kind; public double X0, X1, Y0, Y1, ZTop; public bool IsSlab; public JArray Members = new JArray(); }

    static XYZ org, dir;

    public static object Run(UIApplication app, JObject args)
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
        if (offC == null || offS == null) return new JObject { ["error"] = "filled region type not found" };

        var lineCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories.Cast<Category>().FirstOrDefault(c => c.Name == (string)args["lineStyle"]);
        if (lineCat == null) return new JObject { ["error"] = "line style not found: " + args["lineStyle"] };
        var lineId = lineCat.GetGraphicsStyle(GraphicsStyleType.Projection).Id;

        var ground = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => l.Name == (string)args["groundLevel"]);
        if (ground == null) return new JObject { ["error"] = "level not found: " + args["groundLevel"] };
        double groundZ = ground.ProjectElevation, band = (args["slabBandMm"] != null ? (double)args["slabBandMm"] : 1000.0) / MM;
        var capRx = ((JArray)args["pileCapPatterns"]).Select(p => new Regex((string)p, RegexOptions.IgnoreCase)).ToList();
        int nCap = 0;
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
        int nFr = 0, nFl = 0, nFo = 0;
        var cats = new List<BuiltInCategory> { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation };
        foreach (var s in sources)
        {
            var T = s.Item3; var Ti = T.Inverse; var d = s.Item2;
            var planeL = Plane.CreateByNormalAndOrigin(Ti.OfVector(dir).Normalize(), Ti.OfPoint(org));
            foreach (var e in new FilteredElementCollector(d).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(cats)))
            {
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
                bool conc = Has(mat, "CONCRETE");

                var caps = CutProfiles(e, opt, planeL, T, inv, cb, fails, name);
                if (caps.Count == 0) continue;
                if (isFound && capRx.Any(r => r.IsMatch(name))) { nCap++; continue; }
                string kind = steel ? "steel" : conc ? "concrete" : isFound ? "concrete" : null;
                if (kind == null) { unknown.Add(e.Id.IntegerValue + " " + name + " material: " + mat); continue; }
                if (!steel && !conc) noMat.Add(e.Id.IntegerValue + " " + name);
                if (isFound) nFo++; else if (e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Floors) nFl++; else nFr++;
                foreach (var c in caps)
                {
                    c.Kind = kind; c.IsSlab = e is Floor;
                    c.Members.Add(new JObject { ["src"] = s.Item1, ["id"] = e.Id.IntegerValue, ["cat"] = e.Category.Name, ["type"] = name, ["mat"] = mat });
                    items.Add(c);
                }
            }
        }
        var hiddenTypes = FilterUnderground(items, groundZ, band);
        int nHidden = hiddenTypes.Properties().Sum(g => (int)g.Value);
        int profiles = items.Count;
        int merges = Merge(items, out var notMerged);

        var tx = new Transaction(doc, "Update structural cut regions"); tx.Start();
        var markC = MarkType(types, offC, suffix); var markS = MarkType(types, offS, suffix);
        var inView = new FilteredElementCollector(doc, v.Id).OfClass(typeof(FilledRegion)).Cast<FilledRegion>().ToList();
        int official = inView.Count(r => r.GetTypeId() == offC.Id || r.GetTypeId() == offS.Id);
        var oldIds = new JArray(); var kept = 0;
        foreach (var r in inView.Where(r => r.GetTypeId() == markC.Id || r.GetTypeId() == markS.Id))
        {
            if (doc.IsWorkshared && WorksharingUtils.GetWorksharingTooltipInfo(doc, r.Id).Creator != app.Application.Username) { kept++; continue; }
            oldIds.Add(r.Id.IntegerValue); doc.Delete(r.Id);
        }
        var log = new JArray(); int nC = 0, nS = 0, split = 0;
        foreach (var kind in new[] { "concrete", "steel" })
        {
            var group = items.Where(i => i.Kind == kind).ToList();
            if (group.Count == 0) continue;
            var tid = kind == "concrete" ? markC.Id : markS.Id;
            var made = new List<ElementId>();
            try
            {
                var fr = FilledRegion.Create(doc, tid, v.Id, group.SelectMany(i => Caps(i.S)).SelectMany(pf => pf.GetEdgesAsCurveLoops()).ToList());
                fr.SetLineStyleId(lineId); made.Add(fr.Id);
            }
            catch (Exception ex)
            {
                split++; fails.Add(kind + " as one region: " + ex.Message);
                foreach (var it in group)
                    foreach (var pf in Caps(it.S))
                    {
                        try { var fr = FilledRegion.Create(doc, tid, v.Id, pf.GetEdgesAsCurveLoops().ToList()); fr.SetLineStyleId(lineId); made.Add(fr.Id); }
                        catch (Exception ex2) { fails.Add("single " + (string)it.Members[0]["id"] + ": " + ex2.Message); }
                    }
            }
            if (kind == "concrete") nC += made.Count; else nS += made.Count;
            log.Add(new JObject { ["kind"] = kind, ["regions"] = new JArray(made.Select(i => i.IntegerValue)), ["members"] = new JArray(group.SelectMany(i => i.Members)) });
        }
        if (apply) tx.Commit(); else tx.RollBack();

        var res = new JObject
        {
            ["mode"] = apply ? "apply" : "preview", ["view"] = v.Name, ["scale"] = v.Scale,
            ["framing"] = nFr, ["floors"] = nFl, ["foundations"] = nFo, ["profiles"] = profiles, ["merges"] = merges,
            ["concreteRegions"] = nC, ["steelRegions"] = nS, ["replaced"] = oldIds.Count, ["keptOthers"] = kept, ["officialInView"] = official,
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
                    res.Add(it);
                }
                catch (Exception ex) { fails.Add(e.Id.IntegerValue + " " + name + " prism: " + ex.Message); }
            }
        }
        return res;
    }

    // Underground rule SF7: keep slabs near the ground level and at the bottom, and what touches those slabs.
    static JObject FilterUnderground(List<Item> items, double groundZ, double band)
    {
        double tol = 1.0 / MM;
        var under = items.Where(i => i.ZTop <= groundZ + tol).ToList();
        var slabs = under.Where(i => i.IsSlab).ToList();
        double low = slabs.Count > 0 ? slabs.Min(i => i.ZTop) : groundZ;
        var seeds = slabs.Where(i => i.ZTop >= groundZ - band || i.ZTop <= low + band).ToList();
        var hidden = new JObject();
        foreach (var it in under.Where(i => !seeds.Contains(i)).ToList())
        {
            bool touches = false;
            foreach (var sl in seeds)
            {
                if (it.X0 > sl.X1 + tol || sl.X0 > it.X1 + tol || it.Y0 > sl.Y1 + tol || sl.Y0 > it.Y1 + tol) continue;
                Solid u = null;
                try { u = BooleanOperationsUtils.ExecuteBooleanOperation(it.S, sl.S, BooleanOperationsType.Union); } catch { }
                if (u != null && Caps(u).Count < Caps(it.S).Count + Caps(sl.S).Count) { touches = true; break; }
            }
            if (touches) continue;
            items.Remove(it);
            var key = (string)it.Members[0]["type"];
            hidden[key] = (hidden[key] != null ? (int)hidden[key] : 0) + 1;
        }
        return hidden;
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
