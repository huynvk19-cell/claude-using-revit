/* mcp-tool
{
  "description": "Put views, legends and schedules on sheets at paper mm from the sheet's lower-left corner: by box centre, or so that a grid crossing / grid + level lands on a point. Reports boxes and collisions. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "items": { "type": "array", "items": { "type": "object" }, "description": "{sheet, viewId, centerMm{x,y} | gridPoint{gridA, gridB|level, atMm}, move?}" },
      "itemsPath": { "type": "string", "description": "read items from this JSON file instead" },
      "viewportType": { "type": "string", "description": "viewport type name for every item (an item can name its own)" },
      "areaMm": { "type": "object", "description": "drawing area {minX, minY, maxX, maxY}; default the whole title block" },
      "me": { "type": "string", "description": "Revit user name: viewports last changed by others are not moved" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string", "description": "placed / moved viewports; required for apply and undo" },
      "outPath": { "type": "string", "description": "one row per item (id, box mm) as JSON" },
      "verbose": { "type": "boolean", "description": "return the rows too (default: ids + problems only)" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Rules VP1-VP10 of drafting-sheet-place.md. One transaction; each item in its own sub-transaction. preview does the
// whole job and rolls back, so its boxes and collisions are the real ones.
// Paper mm are measured from the lower-left corner of the sheet's title block (no title block: the sheet outline).
// item:
//    sheet         sheet number (text) or sheet id (number)
//    viewId        a view, a legend (may sit on many sheets) or a schedule
//    centerMm      {x, y}: centre of the viewport box (the view without its title); schedules: centre of the table
//    gridPoint     {gridA, gridB, atMm{x,y}}: the crossing of two straight grids lands on atMm. Plan series: the same
//                  atMm on every sheet keeps the plans in register (VP5). {gridA, level, atMm}: elevations and
//                  sections, the grid at that level's height (VP6). Not for legends, schedules, drafting views.
//    viewportType  type name, default the top-level viewportType; none = Revit's default type stays
//    move          true: the view is already on this sheet, move its viewport to the position. Without it a view
//                  already on the sheet is left alone and reported as Reused. With `me`, a viewport last changed by
//                  another user is not moved and comes back as Ask (VP9).
// A view on another sheet is never taken from it (VP1): Error names that sheet.
// Result: Ids[i] per item (apply: viewport / schedule instance id; preview: -1 = would be placed; 0 = failed),
// Problems {I, Error | Ask | Reused | Outside | Overlaps}. Outside = mm beyond the drawing area per side
// [left, bottom, right, top]; Overlaps = ids of the viewports / schedules on the sheet its box crosses.
// Rows (verbose, outPath, the log): I, Sheet, Id, View, Box [x0, y0, x1, y1] mm, MovedMm [dx, dy].
// apply writes logPath {Created, Moved, Retyped, Rows}; undo puts moved viewports and types back, deletes the created.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ViewportPlace
{
    const double MM = 304.8;

    class Placed
    {
        public int I, Id;
        public ViewSheet Sheet;
        public XYZ Origin;
        public double X0, Y0, X1, Y1;     // sheet feet
        public string View;
        public double Dx, Dy;             // moved, feet
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", log = (string)args["logPath"];
        if (mode != "preview" && mode != "apply" && mode != "undo") return new { Error = "mode: preview | apply | undo" };
        if (mode != "preview" && string.IsNullOrEmpty(log)) return new { Error = "logPath is required for " + mode };
        if (mode == "undo") return Undo(doc, log);

        var items = args["items"] as JArray;
        string itemsPath = (string)args["itemsPath"];
        if (items == null && !string.IsNullOrEmpty(itemsPath))
        {
            var tok = JToken.Parse(File.ReadAllText(itemsPath));
            items = tok as JArray ?? tok["items"] as JArray ?? tok["Items"] as JArray;
        }
        if (items == null || items.Count == 0) return new { Error = "no items" };
        bool verbose = args.Value<bool?>("verbose") ?? false;
        string defaultType = (string)args["viewportType"], me = (string)args["me"], outPath = (string)args["outPath"];
        var area = args["areaMm"] as JObject;

        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder).ToList();
        var types = new Dictionary<string, ElementType>(StringComparer.Ordinal);
        foreach (var ty in new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>().Where(x => x.FamilyName == "Viewport")) types[ty.Name] = ty;
        var grids = new Dictionary<string, Grid>(StringComparer.Ordinal);
        foreach (var g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()) grids[g.Name] = g;
        var levels = new Dictionary<string, Level>(StringComparer.Ordinal);
        foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()) levels[l.Name] = l;

        var ids = new int[items.Count];
        var problems = new List<object>();
        var done = new List<Placed>();
        var created = new List<int>();
        var moved = new List<object>();
        var retyped = new List<object>();
        int nPlaced = 0, nMoved = 0, nReused = 0, nFailed = 0;

        using (var t = new Transaction(doc, "Place views on sheets"))
        {
            t.Start();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i] as JObject;
                try
                {
                    if (it == null) throw new Exception("item is not an object");
                    var sheet = FindSheet(doc, sheets, it["sheet"]);
                    var view = it["viewId"] != null && it["viewId"].Type == JTokenType.Integer ? doc.GetElement(new ElementId((int)it["viewId"])) as View : null;
                    if (view == null || view is ViewSheet || view.IsTemplate) throw new Exception("viewId: not a view that can sit on a sheet");
                    string typeName = (string)it["viewportType"] ?? defaultType;
                    ElementType type = null;
                    if (!string.IsNullOrEmpty(typeName) && !types.TryGetValue(typeName, out type)) throw new Exception("viewport type not found: " + typeName);
                    bool move = it.Value<bool?>("move") ?? false;
                    var center = it["centerMm"] as JObject;
                    var gp = it["gridPoint"] as JObject;
                    XYZ origin = SheetOrigin(doc, sheet);

                    Placed p; string state;
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        try
                        {
                            p = view is ViewSchedule
                                ? PlaceSchedule(doc, sheet, (ViewSchedule)view, origin, center, gp, move, me, moved, out state)
                                : PlaceView(doc, sheet, view, origin, center, gp, move, me, type, grids, levels, moved, retyped, out state);
                            st.Commit();
                        }
                        catch { st.RollBack(); throw; }
                    }
                    p.I = i; p.Sheet = sheet; p.Origin = origin; p.View = view.Name;
                    done.Add(p);
                    if (state == "placed") { nPlaced++; created.Add(p.Id); ids[i] = mode == "apply" ? p.Id : -1; }
                    else if (state == "moved") { nMoved++; ids[i] = p.Id; }
                    else if (state == "reused") { nReused++; ids[i] = p.Id; problems.Add(Row(i, "Reused", "already on this sheet, not moved (move:true to move it)")); }
                    else { ids[i] = p.Id; problems.Add(Row(i, "Ask", state)); }
                }
                catch (Exception ex)
                {
                    nFailed++; ids[i] = 0;
                    problems.Add(Row(i, "Error", Short(ex.Message)));
                }
            }
            doc.Regenerate();
            Collisions(doc, done, area, problems);
            var rows = done.Select(p => (object)new
            {
                p.I, Sheet = p.Sheet.SheetNumber, Id = p.Id, p.View,
                Box = new[] { R((p.X0 - p.Origin.X) * MM), R((p.Y0 - p.Origin.Y) * MM), R((p.X1 - p.Origin.X) * MM), R((p.Y1 - p.Origin.Y) * MM) },
                MovedMm = new[] { R(p.Dx * MM), R(p.Dy * MM) }
            }).ToList();
            if (mode == "apply")
            {
                if (t.Commit() != TransactionStatus.Committed) return new { Error = "the transaction was not committed (a Revit warning stopped it)" };
                File.WriteAllText(log, JsonConvert.SerializeObject(new { Created = created, Moved = moved, Retyped = retyped, Rows = rows }, Formatting.Indented));
            }
            else t.RollBack();
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, JsonConvert.SerializeObject(rows, Formatting.Indented));

            var result = new Dictionary<string, object>
            {
                { "mode", mode }, { "Items", items.Count }, { "Placed", nPlaced }, { "Moved", nMoved }, { "Reused", nReused }, { "Failed", nFailed },
                { "Ids", ids }, { "Problems", problems }
            };
            if (mode == "apply") result["logPath"] = log;
            if (!string.IsNullOrEmpty(outPath)) result["outPath"] = outPath;
            if (verbose) result["Rows"] = rows;
            return result;
        }
    }

    // ---- views and legends ---------------------------------------------------------------

    static Placed PlaceView(Document doc, ViewSheet sheet, View view, XYZ origin, JObject center, JObject gp, bool move, string me,
        ElementType type, Dictionary<string, Grid> grids, Dictionary<string, Level> levels, List<object> moved, List<object> retyped, out string state)
    {
        Viewport vp = sheet.GetAllViewports().Select(id => doc.GetElement(id) as Viewport).FirstOrDefault(x => x != null && x.ViewId == view.Id);
        XYZ target = center != null ? origin + new XYZ(Num(center, "x") / MM, Num(center, "y") / MM, 0) : null;
        XYZ at = null;
        if (gp != null)
        {
            var atMm = gp["atMm"] as JObject;
            if (atMm == null) throw new Exception("gridPoint.atMm {x, y}");
            at = origin + new XYZ(Num(atMm, "x") / MM, Num(atMm, "y") / MM, 0);
        }
        bool existed = vp != null;
        XYZ before = existed ? vp.GetBoxCenter() : null;
        state = "placed";
        if (existed && !move) state = "reused";
        else if (existed && !string.IsNullOrEmpty(me) && doc.IsWorkshared)
        {
            string by = LastChangedBy(doc, vp.Id);
            if (!string.IsNullOrEmpty(by) && !string.Equals(by, me, StringComparison.OrdinalIgnoreCase)) state = "viewport last changed by " + by + ": not moved";
            else state = "moved";
        }
        else if (existed) state = "moved";

        if (state == "placed" || state == "moved")
        {
            if (target == null && at == null) throw new Exception("centerMm {x, y} or gridPoint {gridA, gridB | level, atMm}");
            if (!existed)
            {
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                {
                    var other = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().FirstOrDefault(x => x.ViewId == view.Id);
                    var otherSheet = other == null ? null : doc.GetElement(other.SheetId) as ViewSheet;
                    throw new Exception(otherSheet != null ? "view is already on sheet " + otherSheet.SheetNumber : "this view cannot be placed on a sheet");
                }
                vp = Viewport.Create(doc, sheet.Id, view.Id, target ?? at);
                if (vp == null) throw new Exception("Revit placed no viewport");
            }
            else if (target != null) vp.SetBoxCenter(target);
            if (type != null && vp.GetTypeId() != type.Id)
            {
                if (existed) retyped.Add(new { Id = vp.Id.IntegerValue, OldTypeId = vp.GetTypeId().IntegerValue });
                vp.ChangeTypeId(type.Id);
            }
            if (at != null)
            {
                doc.Regenerate();
                XYZ model = ModelPoint(view, gp, grids, levels);
                XYZ now = ToSheet(view, vp, model);
                XYZ c = vp.GetBoxCenter();
                vp.SetBoxCenter(new XYZ(c.X + at.X - now.X, c.Y + at.Y - now.Y, c.Z));
            }
            doc.Regenerate();
            if (existed)
            {
                XYZ after = vp.GetBoxCenter();
                moved.Add(new { Id = vp.Id.IntegerValue, Kind = "viewport", X = before.X, Y = before.Y });
                var pm = Box(vp);
                pm.Dx = after.X - before.X; pm.Dy = after.Y - before.Y;
                return pm;
            }
        }
        return Box(vp);
    }

    static Placed Box(Viewport vp)
    {
        var o = vp.GetBoxOutline();
        return new Placed { Id = vp.Id.IntegerValue, X0 = o.MinimumPoint.X, Y0 = o.MinimumPoint.Y, X1 = o.MaximumPoint.X, Y1 = o.MaximumPoint.Y };
    }

    /// <summary>The model point that must land on gridPoint.atMm: two grids crossing, or a grid at a level's height.</summary>
    static XYZ ModelPoint(View view, JObject gp, Dictionary<string, Grid> grids, Dictionary<string, Level> levels)
    {
        if (view.ViewType == ViewType.Legend || view.ViewType == ViewType.DraftingView) throw new Exception("gridPoint: this view has no model (use centerMm)");
        Line a = GridLine(grids, (string)gp["gridA"]);
        string nameB = (string)gp["gridB"], levelName = (string)gp["level"];
        if (!string.IsNullOrEmpty(nameB))
        {
            Line b = GridLine(grids, nameB);
            XYZ p = a.Origin, r = a.Direction, q = b.Origin, s = b.Direction;
            double cross = r.X * s.Y - r.Y * s.X;
            if (Math.Abs(cross) < 1e-9) throw new Exception("gridPoint: grids " + gp["gridA"] + " and " + nameB + " are parallel");
            double k = ((q.X - p.X) * s.Y - (q.Y - p.Y) * s.X) / cross;
            double z = view.GenLevel != null ? view.GenLevel.ProjectElevation : p.Z;
            return new XYZ(p.X + k * r.X, p.Y + k * r.Y, z);
        }
        if (!string.IsNullOrEmpty(levelName))
        {
            Level level;
            if (!levels.TryGetValue(levelName, out level)) throw new Exception("gridPoint: level not found: " + levelName);
            XYZ o = view.Origin;
            XYZ on = a.Origin + a.Direction * (o - a.Origin).DotProduct(a.Direction);   // the grid where it meets the view plane
            return new XYZ(on.X, on.Y, level.ProjectElevation);
        }
        throw new Exception("gridPoint: gridB or level");
    }

    static Line GridLine(Dictionary<string, Grid> grids, string name)
    {
        Grid g;
        if (string.IsNullOrEmpty(name) || !grids.TryGetValue(name, out g)) throw new Exception("gridPoint: grid not found: " + name);
        var line = g.Curve as Line;
        if (line == null) throw new Exception("gridPoint: grid " + name + " is not straight");
        return line;
    }

    static XYZ ToSheet(View view, Viewport vp, XYZ model)
    {
        var modelToProjection = view.GetModelToProjectionTransforms().First().GetModelToProjectionTransform();
        return vp.GetProjectionToSheetTransform().OfPoint(modelToProjection.OfPoint(model));
    }

    // ---- schedules -----------------------------------------------------------------------

    static Placed PlaceSchedule(Document doc, ViewSheet sheet, ViewSchedule schedule, XYZ origin, JObject center, JObject gp, bool move, string me,
        List<object> moved, out string state)
    {
        if (gp != null) throw new Exception("gridPoint: a schedule has no grids (use centerMm)");
        var inst = new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
            .FirstOrDefault(x => x.ScheduleId == schedule.Id && !x.IsTitleblockRevisionSchedule);
        bool existed = inst != null;
        state = !existed ? "placed" : move ? "moved" : "reused";
        if (existed && move && !string.IsNullOrEmpty(me) && doc.IsWorkshared)
        {
            string by = LastChangedBy(doc, inst.Id);
            if (!string.IsNullOrEmpty(by) && !string.Equals(by, me, StringComparison.OrdinalIgnoreCase)) state = "schedule last changed by " + by + ": not moved";
        }
        double dx = 0, dy = 0;
        if (state == "placed" || state == "moved")
        {
            if (center == null) throw new Exception("centerMm {x, y}");
            XYZ target = origin + new XYZ(Num(center, "x") / MM, Num(center, "y") / MM, 0);
            XYZ before = existed ? inst.Point : null;
            if (!existed) inst = ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, target);
            doc.Regenerate();
            var bb = inst.get_BoundingBox(sheet);
            if (bb != null)
            {
                XYZ mid = (bb.Min + bb.Max) / 2;
                inst.Point = new XYZ(inst.Point.X + target.X - mid.X, inst.Point.Y + target.Y - mid.Y, inst.Point.Z);
                doc.Regenerate();
            }
            if (existed)
            {
                moved.Add(new { Id = inst.Id.IntegerValue, Kind = "schedule", X = before.X, Y = before.Y });
                dx = inst.Point.X - before.X; dy = inst.Point.Y - before.Y;
            }
        }
        var box = inst.get_BoundingBox(sheet);
        if (box == null) throw new Exception("schedule has no box on the sheet");
        return new Placed { Id = inst.Id.IntegerValue, X0 = box.Min.X, Y0 = box.Min.Y, X1 = box.Max.X, Y1 = box.Max.Y, Dx = dx, Dy = dy };
    }

    // ---- sheet, collisions ---------------------------------------------------------------

    static ViewSheet FindSheet(Document doc, List<ViewSheet> sheets, JToken t)
    {
        if (t == null || t.Type == JTokenType.Null) throw new Exception("sheet missing");
        ViewSheet sheet = t.Type == JTokenType.Integer
            ? doc.GetElement(new ElementId((int)t)) as ViewSheet
            : sheets.FirstOrDefault(s => s.SheetNumber == ((string)t).Trim());
        if (sheet == null) throw new Exception("sheet not found: " + t);
        return sheet;
    }

    static BoundingBoxXYZ TitleBlockBox(Document doc, ViewSheet sheet)
    {
        BoundingBoxXYZ all = null;
        foreach (var tb in new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType())
        {
            var bb = tb.get_BoundingBox(sheet);
            if (bb == null) continue;
            if (all == null) { all = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max }; continue; }
            all.Min = new XYZ(Math.Min(all.Min.X, bb.Min.X), Math.Min(all.Min.Y, bb.Min.Y), 0);
            all.Max = new XYZ(Math.Max(all.Max.X, bb.Max.X), Math.Max(all.Max.Y, bb.Max.Y), 0);
        }
        if (all != null) return all;
        var o = sheet.Outline;
        return new BoundingBoxXYZ { Min = new XYZ(o.Min.U, o.Min.V, 0), Max = new XYZ(o.Max.U, o.Max.V, 0) };
    }

    static XYZ SheetOrigin(Document doc, ViewSheet sheet)
    {
        var bb = TitleBlockBox(doc, sheet);
        return new XYZ(bb.Min.X, bb.Min.Y, 0);
    }

    /// <summary>Per item: outside the drawing area, and the other viewports / schedules of the sheet its box crosses.</summary>
    static void Collisions(Document doc, List<Placed> done, JObject area, List<object> problems)
    {
        double tol = 0.5 / MM;
        foreach (var group in done.GroupBy(p => p.Sheet.Id.IntegerValue))
        {
            var sheet = group.First().Sheet;
            var tb = TitleBlockBox(doc, sheet);
            double ax0 = tb.Min.X, ay0 = tb.Min.Y, ax1 = tb.Max.X, ay1 = tb.Max.Y;
            if (area != null)
            {
                ax1 = ax0 + Num(area, "maxX") / MM; ay1 = ay0 + Num(area, "maxY") / MM;
                ax0 += Num(area, "minX") / MM; ay0 += Num(area, "minY") / MM;
            }
            var boxes = new List<Placed>();
            foreach (var id in sheet.GetAllViewports())
            {
                var vp = doc.GetElement(id) as Viewport;
                if (vp == null) continue;
                try { boxes.Add(Box(vp)); } catch { }
            }
            foreach (var si in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                if (si.IsTitleblockRevisionSchedule) continue;
                var bb = si.get_BoundingBox(sheet);
                if (bb != null) boxes.Add(new Placed { Id = si.Id.IntegerValue, X0 = bb.Min.X, Y0 = bb.Min.Y, X1 = bb.Max.X, Y1 = bb.Max.Y });
            }
            foreach (var p in group)
            {
                var now = boxes.FirstOrDefault(b => b.Id == p.Id);
                if (now != null) { p.X0 = now.X0; p.Y0 = now.Y0; p.X1 = now.X1; p.Y1 = now.Y1; }
                double left = ax0 - p.X0, bottom = ay0 - p.Y0, right = p.X1 - ax1, top = p.Y1 - ay1;
                if (left > tol || bottom > tol || right > tol || top > tol)
                    problems.Add(Row(p.I, "Outside", new[] { R(Math.Max(0, left) * MM), R(Math.Max(0, bottom) * MM), R(Math.Max(0, right) * MM), R(Math.Max(0, top) * MM) }));
                var hits = boxes.Where(b => b.Id != p.Id && b.X0 < p.X1 - tol && b.X1 > p.X0 + tol && b.Y0 < p.Y1 - tol && b.Y1 > p.Y0 + tol).Select(b => b.Id).ToList();
                if (hits.Count > 0) problems.Add(Row(p.I, "Overlaps", hits));
            }
        }
    }

    // ---- helpers -------------------------------------------------------------------------

    static string LastChangedBy(Document doc, ElementId id)
    {
        try { return WorksharingUtils.GetWorksharingTooltipInfo(doc, id).LastChangedBy; } catch { return null; }
    }

    static double Num(JObject o, string key)
    {
        var t = o[key];
        if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) throw new Exception(key + " missing (number, mm)");
        return (double)t;
    }

    static double R(double mm) { return Math.Round(mm, 1); }

    static Dictionary<string, object> Row(int i, string key, object value)
    {
        return new Dictionary<string, object> { { "I", i }, { key, value } };
    }

    static string Short(string message)
    {
        message = (message ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length > 160 ? message.Substring(0, 160) : message;
    }

    // ---- undo ----------------------------------------------------------------------------

    static object Undo(Document doc, string log)
    {
        if (!File.Exists(log)) return new { Error = "log not found: " + log };
        var j = JObject.Parse(File.ReadAllText(log));
        int restored = 0, retyped = 0, deleted = 0;
        using (var t = new Transaction(doc, "Place views on sheets: undo"))
        {
            t.Start();
            foreach (var m in j["Moved"] as JArray ?? new JArray())
            {
                var e = doc.GetElement(new ElementId((int)m["Id"]));
                var at = new XYZ((double)m["X"], (double)m["Y"], 0);
                var vp = e as Viewport;
                var si = e as ScheduleSheetInstance;
                if (vp != null) { vp.SetBoxCenter(new XYZ(at.X, at.Y, vp.GetBoxCenter().Z)); restored++; }
                else if (si != null) { si.Point = new XYZ(at.X, at.Y, si.Point.Z); restored++; }
            }
            foreach (var r in j["Retyped"] as JArray ?? new JArray())
            {
                var vp = doc.GetElement(new ElementId((int)r["Id"])) as Viewport;
                if (vp == null) continue;
                try { vp.ChangeTypeId(new ElementId((int)r["OldTypeId"])); retyped++; } catch { }
            }
            foreach (int id in (j["Created"] as JArray ?? new JArray()).Select(x => (int)x))
            {
                var e = doc.GetElement(new ElementId(id));
                if (!(e is Viewport) && !(e is ScheduleSheetInstance)) continue;
                try { doc.Delete(e.Id); deleted++; } catch { }
            }
            t.Commit();
        }
        return new { mode = "undo", Deleted = deleted, MovedBack = restored, Retyped = retyped };
    }
}
