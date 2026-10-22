/* mcp-tool
{
  "description": "Plans: floor spot elevations inside and outside stair-enclosure doors (rule FL2). plan | apply | undo (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["plan", "apply", "undo"] },
      "viewIds": { "type": "array", "items": { "type": "integer" } },
      "rooms": { "type": "array", "items": { "type": "string" }, "description": "only these stair room numbers" },
      "typeName": { "type": "string", "description": "required for apply: the check spot elevation type (profile)" },
      "baseType": { "type": "string", "description": "spot type duplicated as typeName when it is missing, default Plan" },
      "stairRoomText": { "type": "string", "description": "room name part of stair enclosures, default STAIR" },
      "stairViewText": { "type": "string", "description": "view name part of stair core detail plans, default STAIRCASE" },
      "civilSymbolId": { "type": "integer", "description": "generic annotation for ground exits (profile); none = spot" },
      "civilText": { "type": "string", "description": "text of that symbol, default TO CIVIL ELEVATION" },
      "skipPaperMm": { "type": "number", "description": "an existing spot this close (paper mm) skips the side, default 9" },
      "growCrop": { "type": "boolean" },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 900
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Doors between a stair room (name contains stairRoomText) and another space (or outside) are assigned to views:
//    floor plans of the door's level, and the stair core detail plan (name contains stairViewText) whose cut plane is
//    the lowest one above the door. For each side, candidate points (distance from the door and sideways shift in
//    paper mm, scale-aware) are tried; a candidate is accepted when the surface under it (host Floors/Stairs/Roofs top
//    face, else a linked floor) is at the door's floor level, the spot's symbol+text box stays inside the right room
//    (stair room inside, the other room outside) and does not touch tags, text, dimensions, spots or doors in the view.
//    Sides that already have a spot within skipPaperMm, or a 'CIVIL' mark outside, are skipped. Ground-floor exits to
//    the outside get the civilSymbolId generic annotation (civilText) when given. apply creates the spots with typeName
//    (duplicated from baseType, red, when missing) and may grow the crop (growCrop); undo deletes what logPath lists.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class StairDoorSpots
{
    const double MM = 304.8;
    struct R { public double x0, y0, x1, y1; public bool Hit(R o) => x0 < o.x1 && o.x0 < x1 && y0 < o.y1 && o.y0 < y1; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"];
        string lpth = (string)args["logPath"];
        if (mode == "undo")
        {
            var lg = JArray.Parse(System.IO.File.ReadAllText(lpth));
            var ids = lg.Select(x => new ElementId((int)x["spot"])).Where(i => doc.GetElement(i) != null).ToList();
            using (var t = new Transaction(doc, "Remove stair door spots")) { t.Start(); doc.Delete(ids); t.Commit(); }
            return new { Deleted = ids.Count };
        }
        bool apply = mode == "apply";
        double skipPaper = args["skipPaperMm"] != null ? (double)args["skipPaperMm"] : 9;
        bool growCrop = args["growCrop"]?.Value<bool>() ?? false;
        string baseType = (string)args["baseType"] ?? "Plan";
        string typeName = (string)args["typeName"];
        if (apply && string.IsNullOrWhiteSpace(typeName)) return new { Error = "apply needs typeName (the check spot elevation type from the profile)" };
        string stairText = ((string)args["stairRoomText"] ?? "STAIR").ToUpper(), stairViewText = ((string)args["stairViewText"] ?? "STAIRCASE").ToUpper();
        var roomFilter = (args["rooms"] as JArray)?.Select(x => (string)x).ToList();
        int civilSymId = args["civilSymbolId"] != null ? (int)args["civilSymbolId"] : 0;
        string civilText = (string)args["civilText"] ?? "TO CIVIL ELEVATION";
        var civilSym = civilSymId > 0 ? doc.GetElement(new ElementId(civilSymId)) as FamilySymbol : null;
        var phase = doc.Phases.Cast<Phase>().LastOrDefault();

        var stairRooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).Cast<Room>()
            .Where(r => r.Area > 0 && (r.Name ?? "").ToUpper().Contains(stairText) && (roomFilter == null || roomFilter.Contains(r.Number))).ToList();
        var stairIds = new HashSet<ElementId>(stairRooms.Select(r => r.Id));
        var doors = new List<(FamilyInstance d, Room stair, Room other)>();
        foreach (var d in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
        {
            Room fr = null, to = null;
            try { fr = d.get_FromRoom(phase); to = d.get_ToRoom(phase); } catch { }
            if (fr != null && to != null && fr.Id == to.Id) continue;   // door inside one room, not an enclosure door
            if (fr != null && stairIds.Contains(fr.Id)) doors.Add((d, fr, to));
            else if (to != null && stairIds.Contains(to.Id)) doors.Add((d, to, fr));
        }

        var v3 = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective && !v.IsSectionBoxActive);
        var filter = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsLandings, BuiltInCategory.OST_Roofs });
        var ri = new ReferenceIntersector(filter, FindReferenceTarget.Face, v3) { FindReferencesInRevitLinks = false };
        var riLink = new ReferenceIntersector(v3) { FindReferencesInRevitLinks = true, TargetType = FindReferenceTarget.Face };

        var viewList = ((JArray)args["viewIds"]).Select(x => doc.GetElement(new ElementId((int)x)) as ViewPlan).Where(v => v != null).ToList();
        Func<ViewPlan, double> cutZ = v => { var vr = v.GetViewRange(); var lv = doc.GetElement(vr.GetLevelId(PlanViewPlane.CutPlane)) as Level; return (lv?.ProjectElevation ?? 0) + vr.GetOffset(PlanViewPlane.CutPlane); };
        Func<ViewPlan, XYZ, bool> inCropXY = (v, p) => { if (!v.CropBoxActive) return true; var cb = v.CropBox; var q = cb.Transform.Inverse.OfPoint(p); return q.X >= cb.Min.X && q.X <= cb.Max.X && q.Y >= cb.Min.Y && q.Y <= cb.Max.Y; };
        var allowed = new HashSet<string>();
        foreach (var (d, stair, other) in doors)
        {
            var lp0 = (d.Location as LocationPoint)?.Point; if (lp0 == null) continue;
            foreach (var v in viewList.Where(v => !v.Name.ToUpper().Contains(stairViewText) && v.GenLevel != null && v.GenLevel.Id == d.LevelId)) allowed.Add(v.Id.IntegerValue + "_" + d.Id.IntegerValue);
            var best = viewList.Where(v => v.Name.ToUpper().Contains(stairViewText) && inCropXY(v, lp0) && cutZ(v) - lp0.Z > 100 / MM).OrderBy(v => cutZ(v) - lp0.Z).FirstOrDefault();
            if (best != null) allowed.Add(best.Id.IntegerValue + "_" + d.Id.IntegerValue);
        }

        // surface under a point at the door's floor level
        Func<XYZ, double, bool, (Reference r, XYZ p, bool link)> surface = (pt, z0, allowLink) =>
        {
            var o = new XYZ(pt.X, pt.Y, z0 + 2000 / MM);
            var h = ri.Find(o, -XYZ.BasisZ).Select(x => x.GetReference()).Select(r => (r, p: r.GlobalPoint)).Where(x => x.p.Z > z0 - 400 / MM && x.p.Z < z0 + 100 / MM).OrderBy(x => Math.Abs(x.p.Z - z0)).FirstOrDefault();
            if (h.r != null) return (h.r, h.p, false);
            if (!allowLink) return (null, null, false);
            var hl = riLink.Find(o, -XYZ.BasisZ).Select(x => x.GetReference()).Where(r => r.LinkedElementId != ElementId.InvalidElementId).Select(r => (r, p: r.GlobalPoint)).Where(x => x.p.Z > z0 - 1500 / MM && x.p.Z < z0 + 700 / MM).OrderByDescending(x => x.p.Z).FirstOrDefault();
            return (hl.r, hl.p, hl.r != null);
        };

        SpotDimensionType checkType = null;
        var rows = new List<JObject>(); var log = new JArray();
        Transaction tx = null;
        if (apply) { tx = new Transaction(doc, "Stair door finish spot elevations"); tx.Start(); }
        try
        {
            if (apply)
            {
                checkType = new FilteredElementCollector(doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().FirstOrDefault(t => t.Name == typeName);
                if (checkType == null)
                {
                    var bt = new FilteredElementCollector(doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().First(t => t.Name == baseType && t.FamilyName == "Spot Elevations");
                    checkType = (SpotDimensionType)bt.Duplicate(typeName);
                    var cp = checkType.get_Parameter(BuiltInParameter.LINE_COLOR); if (cp != null && !cp.IsReadOnly) cp.Set(255);
                }
            }
            foreach (var view in viewList)
            {
                double k = view.Scale / MM;   // paper mm -> model ft
                // obstacles in the view (annotation boxes + doors)
                var obst = new List<R>();
                Action<Element> addBox = e => { var b = e.get_BoundingBox(view); if (b != null) obst.Add(new R { x0 = b.Min.X, y0 = b.Min.Y, x1 = b.Max.X, y1 = b.Max.Y }); };
                foreach (var e in new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag))) addBox(e);
                foreach (var e in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType()) addBox(e);
                foreach (var e in new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote))) addBox(e);
                foreach (var e in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_GenericAnnotation).WhereElementIsNotElementType()) addBox(e);
                foreach (var e in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType()) addBox(e);
                var soft = new List<R>();   // stair runs / railings: avoid if possible, small penalty
                foreach (var e in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_StairsRuns).WhereElementIsNotElementType()
                             .Concat(new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_StairsRailing).WhereElementIsNotElementType()))
                { var b = e.get_BoundingBox(view); if (b != null) soft.Add(new R { x0 = b.Min.X, y0 = b.Min.Y, x1 = b.Max.X, y1 = b.Max.Y }); }
                var insideDone = new List<(ElementId room, double z, XYZ p)>();
                // dimension lines as thin boxes along each segment line
                foreach (var dm in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Where(x => !(x is SpotDimension)))
                {
                    var b = dm.get_BoundingBox(view); if (b == null) continue;
                    double w = b.Max.X - b.Min.X, h = b.Max.Y - b.Min.Y;
                    if (w > h) { double yc = (b.Min.Y + b.Max.Y) / 2; obst.Add(new R { x0 = b.Min.X, x1 = b.Max.X, y0 = yc - 2.5 * k, y1 = yc + 2.5 * k }); }
                    else { double xc = (b.Min.X + b.Max.X) / 2; obst.Add(new R { x0 = xc - 2.5 * k, x1 = xc + 2.5 * k, y0 = b.Min.Y, y1 = b.Max.Y }); }
                }
                var existing = new FilteredElementCollector(doc, view.Id).OfClass(typeof(SpotDimension)).Cast<SpotDimension>().ToList();
                var exPts = new List<XYZ>();
                foreach (var s in existing)
                {
                    try { exPts.Add(s.Origin); } catch { }
                    try { if (s.HasLeader) exPts.Add(s.LeaderEndPosition); } catch { }
                    var b = s.get_BoundingBox(view); if (b != null) obst.Add(new R { x0 = b.Min.X, y0 = b.Min.Y, x1 = b.Max.X, y1 = b.Max.Y });
                }
                // "TO CIVIL ELEVATION" style markers (generic annotations / text) count as an existing level mark
                var civil = new List<XYZ>();
                foreach (var e in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_GenericAnnotation).WhereElementIsNotElementType()
                             .Concat(new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).ToElements()))
                {
                    bool isCivil = (e as TextNote)?.Text?.ToUpper().Contains("CIVIL") == true
                        || e.Parameters.Cast<Parameter>().Any(p => p.StorageType == StorageType.String && (p.AsString() ?? "").ToUpper().Contains("CIVIL"));
                    if (!isCivil) continue;
                    var lpE = (e.Location as LocationPoint)?.Point ?? (e as TextNote)?.Coord;
                    if (lpE != null) civil.Add(lpE);
                    else { var b = e.get_BoundingBox(view); if (b != null) civil.Add((b.Min + b.Max) / 2); }
                }
                var crop = view.CropBox; var ct = crop.Transform; var inv = ct.Inverse;
                double cMinX = crop.Min.X, cMinY = crop.Min.Y, cMaxX = crop.Max.X, cMaxY = crop.Max.Y; bool cropChanged = false;

                foreach (var (d, stair, other) in doors)
                {
                    if (!allowed.Contains(view.Id.IntegerValue + "_" + d.Id.IntegerValue)) continue;
                    var lp = (d.Location as LocationPoint)?.Point; if (lp == null) continue;
                    var n = new XYZ(d.FacingOrientation.X, d.FacingOrientation.Y, 0).Normalize();
                    var rc = stair.get_BoundingBox(null); var cen = (rc.Min + rc.Max) / 2;
                    if (n.DotProduct(new XYZ(cen.X - lp.X, cen.Y - lp.Y, 0)) < 0) n = n.Negate();
                    var tdir = new XYZ(-n.Y, n.X, 0);
                    double dw = (d.Symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? d.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? 1000 / MM);
                    foreach (var side in new[] { "inside", "outside" })
                    {
                        var dirN = side == "inside" ? n : n.Negate();
                        var row = new JObject { ["view"] = view.Name, ["viewId"] = view.Id.IntegerValue, ["door"] = d.Id.IntegerValue, ["mark"] = d.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), ["stair"] = stair.Number, ["other"] = other == null ? "(outside)" : other.Number + " " + other.Name, ["side"] = side };
                        // existing spot on this side near the door?
                        // an existing spot on this side, on the same floor surface (z within 30 mm), close to the door
                        double reach = Math.Max(Math.Max(skipPaper, 20) * k, 1800 / MM);
                        double zTestOut = lp.Z + 500 / MM;
                        if (exPts.Any(p => p.Z > lp.Z - 400 / MM && p.Z < lp.Z + 100 / MM &&Math.Sqrt(Math.Pow(p.X - lp.X, 2) + Math.Pow(p.Y - lp.Y, 2)) < reach && (p - lp).DotProduct(dirN) > 0.05))
                        { row["status"] = "existing spot – skipped"; rows.Add(row); continue; }
                        if (side == "inside" && insideDone.Any(x => x.room == stair.Id && Math.Abs(x.z - lp.Z) < 30 / MM && x.p.DistanceTo(new XYZ(lp.X, lp.Y, x.p.Z)) < 26 * k))
                        { row["status"] = "same landing already marked in this view – skipped"; rows.Add(row); continue; }
                        bool allowLink = side == "outside" && other == null;
                        if (allowLink && civil.Any(p => Math.Sqrt(Math.Pow(p.X - lp.X, 2) + Math.Pow(p.Y - lp.Y, 2)) < Math.Max(reach, 14 * k) && (new XYZ(p.X - lp.X, p.Y - lp.Y, 0)).DotProduct(dirN) > 0.05))
                        { row["status"] = "existing 'TO CIVIL ELEVATION' mark – skipped"; rows.Add(row); continue; }
                        if (allowLink && civilSym != null && Math.Abs(lp.Z) < 1000 / MM)   // ground-floor exits only (roof exits keep a spot)
                        {
                            // exterior side: place the 'TO CIVIL ELEVATION' symbol like the stair detail plans
                            XYZ cbest = null; double cscore = double.MaxValue;
                            foreach (var dp in new[] { 5.0, 7.0, 9.0, 12.0, 15.0 })
                                foreach (var sp in new[] { 0.0, 4.0, -4.0, 8.0, -8.0, 12.0, -12.0, 18.0, -18.0 })
                                {
                                    var p = lp + dirN * Math.Max(dp * k, dw * 0.35 + 200 / MM) + tdir * (sp * k);
                                    var box = new R { x0 = p.X - 2.0 * k, x1 = p.X + 26.0 * k, y0 = p.Y - 6.5 * k, y1 = p.Y + 3.0 * k };
                                    var corners = new[] { new XYZ(box.x0, box.y0, 0), new XYZ(box.x1, box.y0, 0), new XYZ(box.x0, box.y1, 0), new XYZ(box.x1, box.y1, 0), p };
                                    if (corners.Any(c => stair.IsPointInRoom(new XYZ(c.X, c.Y, zTestOut)))) continue;
                                    double sc = 10 * obst.Count(o => o.Hit(box)) + 3 * soft.Count(o => o.Hit(box)) + dp / 3.0 + Math.Abs(sp) / 4.0;
                                    if (sc < cscore) { cscore = sc; cbest = p; }
                                }
                            if (cbest == null) cbest = lp + dirN * Math.Max(7 * k, dw * 0.35 + 200 / MM);
                            row["mark"] = row["mark"]; row["surface"] = "TO CIVIL ELEVATION symbol"; row["overlaps"] = (int)(cscore / 10);
                            if (apply)
                            {
                                try
                                {
                                    if (!civilSym.IsActive) civilSym.Activate();
                                    var gi = doc.Create.NewFamilyInstance(new XYZ(cbest.X, cbest.Y, lp.Z), civilSym, view);
                                    gi.LookupParameter("Elevation Custom")?.Set(civilText);
                                    row["status"] = "created (civil mark)"; row["spotId"] = gi.Id.IntegerValue;
                                    log.Add(new JObject { ["spot"] = gi.Id.IntegerValue, ["viewId"] = view.Id.IntegerValue, ["door"] = d.Id.IntegerValue, ["side"] = side, ["kind"] = "civil mark" });
                                }
                                catch (Exception ex) { row["status"] = "failed (civil mark): " + ex.Message; }
                            }
                            else row["status"] = "planned (civil mark)";
                            obst.Add(new R { x0 = cbest.X - 2.0 * k, x1 = cbest.X + 26.0 * k, y0 = cbest.Y - 6.5 * k, y1 = cbest.Y + 3.0 * k });
                            rows.Add(row); continue;
                        }
                        Room want = side == "inside" ? stair : other;
                        double zTest = lp.Z + 500 / MM;
                        Func<XYZ, bool> inWanted = p =>
                        {
                            var q = new XYZ(p.X, p.Y, zTest);
                            if (want != null) return want.IsPointInRoom(q);
                            return !stair.IsPointInRoom(q);
                        };
                        (XYZ pt, Reference r, XYZ hp, bool link, int hits) best = (null, null, null, false, int.MaxValue);
                        double bestScore = double.MaxValue;
                        var dists = new[] { 4.0, 5.5, 7.0, 9.0, 11.0, 14.0 };
                        var shifts = new[] { 0.0, 3.0, -3.0, 6.0, -6.0, 9.0, -9.0, 12.0, -12.0 };
                        foreach (var dp in dists)
                        {
                            foreach (var sp in shifts)
                            {
                                var p = lp + dirN * (Math.Max(dp * k, dw * 0.35 + 200 / MM)) + tdir * (sp * k);
                                // symbol + text box on paper: symbol r=1.6, text to the right (value up to 6 chars)
                                var box = new R { x0 = p.X - 1.8 * k, x1 = p.X + 12.0 * k, y0 = p.Y - 1.8 * k, y1 = p.Y + 3.4 * k };
                                var corners = new[] { new XYZ(box.x0, box.y0, 0), new XYZ(box.x1, box.y0, 0), new XYZ(box.x0, box.y1, 0), new XYZ(box.x1, box.y1, 0), p };
                                if (!corners.All(inWanted)) continue;
                                int hard = obst.Count(o => o.Hit(box)), sft = soft.Count(o => o.Hit(box));
                                double score = 10 * hard + 3 * sft + dp / 3.0 + Math.Abs(sp) / 4.0;
                                if (score >= bestScore) continue;
                                var s = surface(p, lp.Z, allowLink);
                                if (s.r == null) continue;
                                bestScore = score; best = (p, s.r, s.p, s.link, hard);
                            }
                        }
                        if (best.r == null)
                        {
                            // fallback: straight out from the door, ignoring the room test
                            var p = lp + dirN * Math.Max(7 * k, dw * 0.35 + 200 / MM); var s = surface(p, lp.Z, allowLink);
                            if (s.r == null) { row["status"] = "no surface found"; rows.Add(row); continue; }
                            best = (p, s.r, s.p, s.link, -1);
                        }
                        var he = doc.GetElement(best.r);
                        row["surface"] = best.link && he is RevitLinkInstance rli ? "LINK " + doc.GetElement(rli.GetTypeId())?.Name + " : " + rli.GetLinkDocument()?.GetElement(best.r.LinkedElementId)?.Name : he?.Category?.Name + " '" + he?.Name + "'";
                        row["elevMm"] = Math.Round(best.hp.Z * MM, 1);
                        if (Math.Abs(best.hp.Z - lp.Z) > 30 / MM) row["elevMm"] = Math.Round(best.hp.Z * MM, 1) + " (door sill " + Math.Round(lp.Z * MM) + ")";
                        row["overlaps"] = best.hits;
                        var tc = inv.OfPoint(best.hp);
                        bool tIn = !view.CropBoxActive || (tc.X - 2 * k >= cMinX && tc.X + 13 * k <= cMaxX && tc.Y - 2 * k >= cMinY && tc.Y + 4 * k <= cMaxY);
                        row["inCrop"] = tIn;
                        if (!tIn && growCrop) { cMinX = Math.Min(cMinX, tc.X - 4 * k); cMaxX = Math.Max(cMaxX, tc.X + 15 * k); cMinY = Math.Min(cMinY, tc.Y - 4 * k); cMaxY = Math.Max(cMaxY, tc.Y + 6 * k); cropChanged = true; }
                        row["status"] = apply ? "pending" : "planned";
                        if (apply)
                        {
                            try
                            {
                                if (cropChanged) { view.CropBox = new BoundingBoxXYZ { Transform = ct, Min = new XYZ(cMinX, cMinY, crop.Min.Z), Max = new XYZ(cMaxX, cMaxY, crop.Max.Z) }; crop = view.CropBox; cropChanged = false; row["cropGrown"] = true; }
                                var spd = doc.Create.NewSpotElevation(view, best.r, best.hp, best.hp, best.hp, best.hp, false);
                                spd.ChangeTypeId(checkType.Id);
                                doc.Regenerate();
                                if (spd.get_BoundingBox(view) == null)
                                {
                                    doc.Delete(spd.Id);
                                    row["status"] = "surface not shown in this view – not placed"; rows.Add(row); continue;
                                }
                                row["status"] = "created"; row["spotId"] = spd.Id.IntegerValue;
                                if (side == "inside") insideDone.Add((stair.Id, lp.Z, new XYZ(lp.X, lp.Y, lp.Z)));
                                var bx = new R { x0 = best.pt.X - 1.8 * k, x1 = best.pt.X + 12.0 * k, y0 = best.pt.Y - 1.8 * k, y1 = best.pt.Y + 3.4 * k };
                                obst.Add(bx); exPts.Add(best.hp);
                                log.Add(new JObject { ["spot"] = spd.Id.IntegerValue, ["viewId"] = view.Id.IntegerValue, ["door"] = d.Id.IntegerValue, ["side"] = side, ["elevMm"] = Math.Round(best.hp.Z * MM, 1) });
                            }
                            catch (Exception ex) { row["status"] = "failed: " + ex.Message; }
                        }
                        else
                        {
                            obst.Add(new R { x0 = best.pt.X - 1.8 * k, x1 = best.pt.X + 12.0 * k, y0 = best.pt.Y - 1.8 * k, y1 = best.pt.Y + 3.4 * k });
                            exPts.Add(best.hp);
                            if (side == "inside") insideDone.Add((stair.Id, lp.Z, new XYZ(lp.X, lp.Y, lp.Z)));
                        }
                        rows.Add(row);
                    }
                }
            }
            if (tx != null) tx.Commit();
        }
        catch { if (tx != null && tx.HasStarted()) tx.RollBack(); throw; }
        if (apply && lpth != null) System.IO.File.WriteAllText(lpth, log.ToString());
        return new
        {
            Mode = mode, Created = log.Count,
            Rows = rows.Select(r => string.Format("{0} | door {1} ({2}) {3} -> {4} | {5} | elev {6} | overlaps {7} | crop {8}{9}",
                r["view"], r["mark"], r["stair"], r["side"], r["other"], r["status"], r["elevMm"], r["overlaps"], r["inCrop"], r["spotId"] != null ? " | id " + r["spotId"] : "")).ToList()
        };
    }
}
