/* mcp-tool
{
  "description": "Redraw column filled regions in plan views to match the real column sections. For each view: columns (Structural Columns + Columns, host + loaded links) cut by the view's cut plane inside the crop; each filled region loop (optionally only region types in regionTypes) is paired with the column section it overlaps most. Loops that already match (within tolMm) stay; changed ones take the column section; unmatched loops are kept; columns inside the crop with no loop are listed (addMissing true also adds them to the region of the same view). A changed region is recreated with the same type, view, element overrides and Comments, then the old one is deleted; regions referenced by dimensions are skipped unless force. mode preview / apply / undo (undo only deletes regions created, listed in logPath; old ones come back with Revit Undo). Writes logPath.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "viewIds": { "type": "array", "items": { "type": "integer" } },
      "regionTypes": { "type": "array", "items": { "type": "string" } },
      "tolMm": { "type": "number" },
      "addMissing": { "type": "boolean" },
      "dropUnmatched": { "type": "boolean" },
      "force": { "type": "boolean" },
      "logPath": { "type": "string" },
      "verbose": { "type": "boolean" }
    },
    "required": ["mode", "viewIds"]
  },
  "timeoutSeconds": 900
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ColumnRegionsSync
{
    const double MM = 304.8;
    static double R(double v) { return Math.Round(v * MM, 0); }

    class Sec { public string Src; public int Id; public string Type; public List<CurveLoop> Loops = new List<CurveLoop>(); public double[] Bb; public bool Used; }

    static double[] Bb(IEnumerable<XYZ> pts) { var l = pts.ToList(); return new[] { l.Min(p => p.X), l.Min(p => p.Y), l.Max(p => p.X), l.Max(p => p.Y) }; }
    static IEnumerable<XYZ> Pts(CurveLoop cl) { foreach (var c in cl) { yield return c.GetEndPoint(0); if (!(c is Line)) yield return c.Evaluate(0.5, true); } }
    static double Ov(double[] a, double[] b) { double w = Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]), h = Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]); return (w > 0 && h > 0) ? w * h : 0; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        bool apply = (string)args["mode"] == "apply";
        double tol = (args["tolMm"] != null ? (double)args["tolMm"] : 2) / MM;
        bool addMissing = args["addMissing"] != null && (bool)args["addMissing"];
        bool force = args["force"] != null && (bool)args["force"];
        bool dropUn = args["dropUnmatched"] != null && (bool)args["dropUnmatched"];
        var rtypes = args["regionTypes"] != null ? ((JArray)args["regionTypes"]).Select(x => (string)x).ToList() : null;
        var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Where(l => l.GetLinkDocument() != null).ToList();
        var sources = new List<Tuple<string, Document, Transform>> { Tuple.Create("HOST", doc, Transform.Identity) };
        foreach (var l in links) sources.Add(Tuple.Create(l.GetLinkDocument().Title, l.GetLinkDocument(), l.GetTotalTransform()));
        // dimension references to filled regions
        var dimRef = new Dictionary<int, int>();
        var log = new JArray(); var rows = new JArray();

        Transaction tx = null; if (apply) { tx = new Transaction(doc, "Update column regions"); tx.Start(); }
        try
        {
            foreach (var jid in (JArray)args["viewIds"])
            {
                var v = doc.GetElement(new ElementId((int)jid)) as ViewPlan;
                if (v == null) { rows.Add(new JObject { ["viewId"] = jid, ["error"] = "not a plan view" }); continue; }
                var vr = v.GetViewRange();
                var cutLvl = doc.GetElement(vr.GetLevelId(PlanViewPlane.CutPlane)) as Level ?? v.GenLevel;
                double cutZ = cutLvl.ProjectElevation + vr.GetOffset(PlanViewPlane.CutPlane);
                var cb = v.CropBox; var tr = cb.Transform;
                var crop = Bb(new[] { tr.OfPoint(cb.Min), tr.OfPoint(cb.Max), tr.OfPoint(new XYZ(cb.Min.X, cb.Max.Y, 0)), tr.OfPoint(new XYZ(cb.Max.X, cb.Min.Y, 0)) });

                // column sections
                var secs = new List<Sec>();
                var cats = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns });
                foreach (var s in sources)
                {
                    var inv = s.Item3.Inverse;
                    var planeL = Plane.CreateByNormalAndOrigin(inv.OfVector(XYZ.BasisZ.Negate()), inv.OfPoint(new XYZ(0, 0, cutZ)));
                    foreach (var e in new FilteredElementCollector(s.Item2).WhereElementIsNotElementType().WherePasses(cats))
                    {
                        var bb = e.get_BoundingBox(null); if (bb == null) continue;
                        var hp = new List<XYZ>();
                        foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z }) hp.Add(s.Item3.OfPoint(new XYZ(x, y, z)));
                        var hb = Bb(hp);
                        if (Ov(hb, crop) <= 0) continue;
                        if (hp.Max(p => p.Z) < cutZ || hp.Min(p => p.Z) > cutZ) continue;
                        var sec = new Sec { Src = s.Item1, Id = e.Id.IntegerValue, Type = (s.Item2.GetElement(e.GetTypeId()) as ElementType)?.FamilyName + " : " + s.Item2.GetElement(e.GetTypeId())?.Name };
                        foreach (var so in Solids(e))
                        {
                            Solid cut = null;
                            try { cut = BooleanOperationsUtils.CutWithHalfSpace(so, planeL); } catch { }
                            if (cut == null || cut.Volume < 1e-9) continue;
                            foreach (Face f in cut.Faces)
                            {
                                var pf = f as PlanarFace; if (pf == null) continue;
                                if (Math.Abs(s.Item3.OfVector(pf.FaceNormal).Z) < 0.999) continue;
                                if (Math.Abs(s.Item3.OfPoint(pf.Origin).Z - cutZ) > 1e-4) continue;
                                foreach (CurveLoop cl in pf.GetEdgesAsCurveLoops()) sec.Loops.Add(CurveLoop.CreateViaTransform(cl, s.Item3));
                            }
                        }
                        if (sec.Loops.Count == 0) continue;
                        sec.Bb = Bb(sec.Loops.SelectMany(Pts));
                        secs.Add(sec);
                    }
                }

                var vrow = new JObject { ["viewId"] = v.Id.IntegerValue, ["view"] = v.Name, ["columns"] = secs.Count };
                var regRows = new JArray();
                var regs = new FilteredElementCollector(doc, v.Id).OfClass(typeof(FilledRegion)).Cast<FilledRegion>()
                    .Where(r => rtypes == null || rtypes.Contains(doc.GetElement(r.GetTypeId())?.Name)).ToList();
                // dims in this view referencing the regions
                var dimsHere = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
                FilledRegion firstReg = null; List<CurveLoop> firstLoops = null;
                foreach (var fr in regs)
                {
                    var loops = fr.GetBoundaries().ToList();
                    if (loops.Count == 0) continue;
                    double z = loops[0].First().GetEndPoint(0).Z;
                    var newLoops = new List<CurveLoop>(); int nOk = 0, nUpd = 0, nKeep = 0, nDrop = 0; var det = new JArray();
                    foreach (var cl in loops)
                    {
                        var lb = Bb(Pts(cl));
                        Sec best = null; double bo = 0;
                        foreach (var s2 in secs) { double o = Ov(lb, s2.Bb); if (o > bo) { bo = o; best = s2; } }
                        if (best == null && dropUn) { nDrop++; det.Add("drop " + Box(lb)); continue; }
                        if (best == null) { nKeep++; newLoops.Add(cl); det.Add("keep " + Box(lb)); continue; }
                        best.Used = true;
                        bool same = Math.Abs(lb[0] - best.Bb[0]) < tol && Math.Abs(lb[1] - best.Bb[1]) < tol && Math.Abs(lb[2] - best.Bb[2]) < tol && Math.Abs(lb[3] - best.Bb[3]) < tol && best.Loops.Count == 1;
                        if (same) { nOk++; newLoops.Add(cl); continue; }
                        nUpd++;
                        det.Add("update " + Box(lb) + " -> " + Box(best.Bb) + " (" + best.Type + ", " + best.Src + " " + best.Id + ")");
                        foreach (var sl in best.Loops) newLoops.Add(Flat(sl, z));
                    }
                    int refs = dimsHere.Count(d => { try { foreach (Reference rf in d.References) if (rf.ElementId == fr.Id) return true; } catch { } return false; });
                    var rr = new JObject { ["region"] = fr.Id.IntegerValue, ["type"] = doc.GetElement(fr.GetTypeId())?.Name, ["loops"] = loops.Count, ["ok"] = nOk, ["update"] = nUpd, ["keep"] = nKeep, ["drop"] = nDrop, ["dimRefs"] = refs, ["detail"] = det };
                    if (firstReg == null && nOk + nUpd > 0) { firstReg = fr; firstLoops = loops; }
                    if (nUpd + nDrop > 0 && apply && newLoops.Count > 0)
                    {
                        if (refs > 0 && !force) rr["skipped"] = "dimension references";
                        else
                        {
                            int oldId = fr.Id.IntegerValue; bool wasFirst = firstReg == fr;
                            var nfr = Recreate(doc, v, fr, newLoops);
                            rr["newRegion"] = nfr.Id.IntegerValue;
                            log.Add(new JObject { ["view"] = v.Id.IntegerValue, ["old"] = oldId, ["new"] = nfr.Id.IntegerValue });
                            if (wasFirst) { firstReg = nfr; firstLoops = newLoops; }
                        }
                    }
                    regRows.Add(rr);
                }
                var missing = new JArray();
                var miss = secs.Where(s3 => !s3.Used).ToList();
                foreach (var m in miss)
                {
                    bool inside = m.Bb[0] >= crop[0] && m.Bb[2] <= crop[2] && m.Bb[1] >= crop[1] && m.Bb[3] <= crop[3];
                    missing.Add((inside ? "inside " : "partly ") + Box(m.Bb) + " (" + m.Type + ", " + m.Src + " " + m.Id + ")");
                }
                if (apply && addMissing && miss.Count > 0 && firstReg != null)
                {
                    var loops = new List<CurveLoop>(firstLoops); double z = loops[0].First().GetEndPoint(0).Z;
                    foreach (var m in miss) foreach (var sl in m.Loops) loops.Add(Flat(sl, z));
                    int oldId = firstReg.Id.IntegerValue;
                    var nfr = Recreate(doc, v, firstReg, loops);
                    log.Add(new JObject { ["view"] = v.Id.IntegerValue, ["old"] = oldId, ["new"] = nfr.Id.IntegerValue, ["added"] = miss.Count });
                    vrow["addedTo"] = nfr.Id.IntegerValue;
                }
                vrow["regions"] = regRows; vrow["missingColumns"] = missing;
                rows.Add(vrow);
            }
            if (tx != null) tx.Commit();
        }
        catch { if (tx != null && tx.HasStarted()) tx.RollBack(); throw; }
        if (args["logPath"] != null) System.IO.File.WriteAllText((string)args["logPath"], new JObject { ["mode"] = (string)args["mode"], ["changes"] = log, ["views"] = rows }.ToString());
        if (args["verbose"] != null && (bool)args["verbose"]) return rows;
        // compact summary
        var sum = new JArray();
        foreach (JObject r in rows)
        {
            if (r["error"] != null) { sum.Add(r["viewId"] + " " + r["error"]); continue; }
            int upd = r["regions"].Sum(x => (int)x["update"]), ok = r["regions"].Sum(x => (int)x["ok"]), keep = r["regions"].Sum(x => (int)x["keep"]);
            sum.Add(r["viewId"] + " | " + r["view"] + " | cols " + r["columns"] + " | loops ok " + ok + ", update " + upd + ", keep " + keep + " | missing " + ((JArray)r["missingColumns"]).Count
                + (r["regions"].Any(x => x["skipped"] != null) ? " | SKIPPED(dim refs)" : ""));
        }
        return new JObject { ["changes"] = log.Count, ["views"] = sum };
    }

    static string Box(double[] b) { return "[" + R(b[0]) + "," + R(b[1]) + " " + R(b[2] - b[0]) + "x" + R(b[3] - b[1]) + "]"; }

    static CurveLoop Flat(CurveLoop cl, double z)
    {
        double dz = z - cl.First().GetEndPoint(0).Z;
        return Math.Abs(dz) < 1e-9 ? cl : CurveLoop.CreateViaTransform(cl, Transform.CreateTranslation(new XYZ(0, 0, dz)));
    }

    static FilledRegion Recreate(Document doc, View v, FilledRegion old, List<CurveLoop> loops)
    {
        var nfr = FilledRegion.Create(doc, old.GetTypeId(), v.Id, loops);
        var ogs = v.GetElementOverrides(old.Id);
        v.SetElementOverrides(nfr.Id, ogs);
        foreach (var bip in new[] { BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS })
        {
            var a = old.get_Parameter(bip); var b = nfr.get_Parameter(bip);
            if (a != null && b != null && !b.IsReadOnly && a.HasValue) b.Set(a.AsString());
        }
        try { var ls = old.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM); var ns = nfr.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM); if (ls != null && ns != null && !ns.IsReadOnly) ns.Set(ls.AsInteger()); } catch { }
        if (old.IsHidden(v)) v.HideElements(new List<ElementId> { nfr.Id });
        doc.Delete(old.Id);
        return nfr;
    }

    static IEnumerable<Solid> Solids(Element e)
    {
        var ge = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }); if (ge == null) yield break;
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
