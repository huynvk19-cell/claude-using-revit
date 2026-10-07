/* mcp-tool
{
  "description": "Turn on and centre viewport titles under each view's drawing, below its lowest content; reports collisions. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply",
          "undo"
        ]
      },
      "sheetNumbers": {
        "type": "array",
        "items": {
          "type": "string"
        }
      },
      "titledType": {
        "type": "string",
        "description": "viewport type name to give untitled (non-legend) viewports, e.g"
      },
      "gapMm": {
        "type": "number",
        "description": "gap between content and title top, sheet mm (default 4)"
      },
      "skipViewports": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "align": {
        "type": "string",
        "enum": [
          "centre",
          "left"
        ],
        "description": "centre (default) under the crop centre, or left"
      },
      "onlyViewports": {
        "type": "array",
        "items": {
          "type": "number"
        },
        "description": "only place these viewports (others on the sheet are obstacles only)"
      },
      "logPath": {
        "type": "string"
      },
      "ignoreCrop": {
        "type": "boolean",
        "description": "treat a visible crop region as non-printing"
      },
      "debugVp": {
        "type": "number"
      }
    },
    "required": [
      "mode",
      "logPath"
    ]
  },
  "timeoutSeconds": 600
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Turn on and place viewport titles on sheets: optionally switch untitled viewports to a titled viewport type, then
//    centre each title under its view's drawing (crop region centre), just below the lowest visible content (crop
//    region, grid/level heads, dimensions, tags, text...) under the title span, and report collisions with other
//    viewports' content or titles. Legends are skipped. Modes: preview, apply, undo (from log).
// Parameters:
//   titledType: viewport type name to give untitled (non-legend) viewports, e.g. 'Detail Ref - Title - Scale'
//   align: centre (default) under the crop centre, or left: title starts at the leftmost content of the view (bottom-
//    left title)
//   ignoreCrop: treat a visible crop region as non-printing: use the model/annotation extents instead
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewportTitlesPlace
{
    const double MM = 304.8;
    static bool IgnoreCrop = false;
    class Rect { public double x0, y0, x1, y1; public string src; public Rect(double a, double b, double c, double d, string s = null) { x0 = Math.Min(a, c); x1 = Math.Max(a, c); y0 = Math.Min(b, d); y1 = Math.Max(b, d); src = s; } }
    static bool Hit(Rect a, Rect b, double tol = 0) => a.x0 < b.x1 - tol && a.x1 > b.x0 + tol && a.y0 < b.y1 - tol && a.y1 > b.y0 + tol;
    static string F(double v) => Math.Round(v * MM, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    static string RS(Rect r) => F(r.x0) + "," + F(r.y0) + " .. " + F(r.x1) + "," + F(r.y1);

    // content rectangles of a viewport, in sheet coordinates (feet)
    static List<Rect> Content(Document doc, Viewport vp, View v, out Rect crop)
    {
        var res = new List<Rect>(); crop = null;
        var box = vp.GetBoxOutline();
        var boxR = new Rect(box.MinimumPoint.X, box.MinimumPoint.Y, box.MaximumPoint.X, box.MaximumPoint.Y);
        Transform p2s = vp.GetProjectionToSheetTransform();
        var m2p = v.GetModelToProjectionTransforms();
        Func<XYZ, XYZ> S = p =>
        {
            foreach (var t in m2p)
            {
                var q = t.GetModelToProjectionTransform().OfPoint(p);
                return p2s.OfPoint(q);
            }
            return p;
        };
        Func<IEnumerable<XYZ>, string, Rect> RR = (pts, src) =>
        {
            var l = pts.Select(S).ToList();
            return new Rect(l.Min(q => q.X), l.Min(q => q.Y), l.Max(q => q.X), l.Max(q => q.Y), src);
        };
        Func<Rect, Rect> Clip = r => r == null ? null : (Hit(r, boxR) ? new Rect(Math.Max(r.x0, boxR.x0), Math.Max(r.y0, boxR.y0), Math.Min(r.x1, boxR.x1), Math.Min(r.y1, boxR.y1), r.src) : null);

        if (v.CropBoxActive)
        {
            var cb = v.CropBox; var tf = cb.Transform;
            var pts = new[] { new XYZ(cb.Min.X, cb.Min.Y, cb.Max.Z), new XYZ(cb.Max.X, cb.Min.Y, cb.Max.Z), new XYZ(cb.Min.X, cb.Max.Y, cb.Max.Z), new XYZ(cb.Max.X, cb.Max.Y, cb.Max.Z) }.Select(tf.OfPoint);
            crop = Clip(RR(pts, "crop"));
            if (crop != null && v.CropBoxVisible && !IgnoreCrop) res.Add(crop);
            if (crop != null && (!v.CropBoxVisible || IgnoreCrop))
            {   // model geometry extents inside the crop (links skipped)
                foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
                {
                    if (e.Category == null || e.Category.CategoryType != CategoryType.Model || e is RevitLinkInstance || e is ImportInstance) continue;
                    var cat = (BuiltInCategory)e.Category.Id.IntegerValue;
                    if (cat == BuiltInCategory.OST_Cameras || cat == BuiltInCategory.OST_VolumeOfInterest || cat == BuiltInCategory.OST_SectionBox || cat == BuiltInCategory.OST_RoomSeparationLines || cat == BuiltInCategory.OST_Rooms || cat == BuiltInCategory.OST_Areas) continue;
                    BoundingBoxXYZ mb = null; try { mb = e.get_BoundingBox(v); } catch { }
                    if (mb == null) continue;
                    var mr = RR(new[] { mb.Min, mb.Max, new XYZ(mb.Min.X, mb.Max.Y, mb.Min.Z), new XYZ(mb.Max.X, mb.Min.Y, mb.Max.Z), new XYZ(mb.Min.X, mb.Min.Y, mb.Max.Z), new XYZ(mb.Max.X, mb.Max.Y, mb.Min.Z) }, e.Category.Name + " " + e.Id.IntegerValue);
                    if (!Hit(mr, crop)) continue;
                    mr = new Rect(Math.Max(mr.x0, crop.x0), Math.Max(mr.y0, crop.y0), Math.Min(mr.x1, crop.x1), Math.Min(mr.y1, crop.y1), mr.src);
                    res.Add(mr);
                }
            }
        }
        else { crop = boxR; res.Add(boxR); }

        double sc = v.Scale;
        var ids = new HashSet<ElementId>(new FilteredElementCollector(doc, v.Id).OwnedByView(v.Id).ToElementIds());
        foreach (var id in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).ToElementIds()) ids.Add(id);
        foreach (var id in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Level)).ToElementIds()) ids.Add(id);
        foreach (var id in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Viewers).ToElementIds()) ids.Add(id);
        foreach (var id in new FilteredElementCollector(doc, v.Id).OfClass(typeof(ElevationMarker)).ToElementIds()) ids.Add(id);
        foreach (var id in ids)
        {
            var e = doc.GetElement(id);
            if (e == null || e.Category == null) continue;
            if (e is Grid || e is Level)
            {
                var dp = (DatumPlane)e;
                Curve c = null;
                try { c = dp.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
                if (c == null) continue;
                XYZ a = S(c.GetEndPoint(0)), b = S(c.GetEndPoint(1));
                var r = Clip(new Rect(a.X, a.Y, b.X, b.Y, e.Category.Name + " " + e.Name)); if (r != null) res.Add(r);
                var d = (b - a).Normalize();
                double rad = 4.5 / MM;
                foreach (var end in new[] { DatumEnds.End0, DatumEnds.End1 })
                {
                    bool on = false; try { on = dp.IsBubbleVisibleInView(end, v); } catch { }
                    if (!on) continue;
                    XYZ p = end == DatumEnds.End0 ? a : b; XYZ dir = end == DatumEnds.End0 ? -d : d;
                    if (e is Level)
                    {   // level head: symbol + text beside the end, about 30 x 12 mm
                        var q = p + dir * (30 / MM);
                        var rl = Clip(new Rect(p.X, p.Y - 7 / MM, q.X, q.Y + 9 / MM, "head " + e.Name)); if (rl != null) res.Add(rl);
                    }
                    else
                    {
                        var q = p + dir * (2 * rad);
                        bool vert = Math.Abs(dir.Y) >= Math.Abs(dir.X);
                        var rg = Clip(vert ? new Rect(p.X - rad, p.Y, p.X + rad, q.Y, "bubble " + e.Name) : new Rect(p.X, p.Y - rad, q.X, p.Y + rad, "bubble " + e.Name)); if (rg != null) res.Add(rg);
                    }
                }
                continue;
            }
            if (e.Category.CategoryType == CategoryType.Model && !(e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_DetailComponents)) continue;
            if (e is Viewport) continue;
            BoundingBoxXYZ bb = null; try { bb = e.get_BoundingBox(v); } catch { }
            if (bb == null) continue;
            var corners = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z), new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z), new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z), new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z) }
                .Select(p => bb.Transform != null ? bb.Transform.OfPoint(p) : p);
            var re = Clip(RR(corners, e.Category.Name + " " + e.Id.IntegerValue));
            if (re == null) continue;
            if (re.x1 - re.x0 > 0.9 * (boxR.x1 - boxR.x0) && re.y1 - re.y0 > 0.9 * (boxR.y1 - boxR.y0)) continue; // huge (scope-like) items
            res.Add(re);
        }
        return res;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var log = JArray.Parse(File.ReadAllText(logPath)); var outU = new List<object>();
            using (var t = new Transaction(doc, "Restore viewport titles"))
            {
                t.Start();
                foreach (JObject r in log.Reverse())
                {
                    var vp = doc.GetElement(new ElementId((int)r["vp"])) as Viewport; if (vp == null) continue;
                    if (r["oldType"] != null && vp.GetTypeId().IntegerValue != (int)r["oldType"]) vp.ChangeTypeId(new ElementId((int)r["oldType"]));
                    vp.LabelOffset = new XYZ((double)r["offX"], (double)r["offY"], 0);
                    outU.Add(vp.Id.IntegerValue);
                }
                t.Commit();
            }
            return new { Restored = outU.Count };
        }

        var nums = ((JArray)args["sheetNumbers"]).Select(x => (string)x).ToList();
        double gap = (args.Value<double?>("gapMm") ?? 4) / MM;
        IgnoreCrop = args.Value<bool?>("ignoreCrop") ?? false;
        var skip = (args["skipViewports"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>();
        int debugVp = args.Value<int?>("debugVp") ?? 0;
        ElementType titled = null;
        if (args["titledType"] != null)
        {
            titled = new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>().FirstOrDefault(x => x.FamilyName == "Viewport" && x.Name == (string)args["titledType"]);
            if (titled == null) return new { Error = "viewport type not found: " + args["titledType"] };
        }
        var entries = File.Exists(logPath) ? JArray.Parse(File.ReadAllText(logPath)) : new JArray();
        var report = new List<object>();
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => nums.Contains(s.SheetNumber)).OrderBy(s => s.SheetNumber).ToList();
        using (var tg = new TransactionGroup(doc, "Place viewport titles"))
        {
            tg.Start();
            foreach (var s in sheets)
            {
                using (var t = new Transaction(doc, "Place viewport titles " + s.SheetNumber))
                {
                    t.Start();
                    var only = (args["onlyViewports"] as JArray)?.Select(x => (int)x).ToList();
                    var vps = s.GetAllViewports().Select(id => (Viewport)doc.GetElement(id)).Where(vp => ((View)doc.GetElement(vp.ViewId)).ViewType != ViewType.Legend && !skip.Contains(vp.Id.IntegerValue) && (only == null || only.Contains(vp.Id.IntegerValue))).ToList();
                    var all = s.GetAllViewports().Select(id => (Viewport)doc.GetElement(id)).ToList();
                    // 1. titles on
                    foreach (var vp in vps)
                    {
                        var ty = doc.GetElement(vp.GetTypeId()) as ElementType;
                        int show = ty?.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL)?.AsInteger() ?? 1;
                        if (show == 0 && titled != null)
                        {
                            entries.Add(new JObject { ["vp"] = vp.Id.IntegerValue, ["oldType"] = vp.GetTypeId().IntegerValue, ["offX"] = vp.LabelOffset.X, ["offY"] = vp.LabelOffset.Y });
                            vp.ChangeTypeId(titled.Id);
                            report.Add(new { Sheet = s.SheetNumber, Vp = vp.Id.IntegerValue, View = doc.GetElement(vp.ViewId).Name, TypeChanged = ty.Name + " -> " + titled.Name });
                        }
                    }
                    doc.Regenerate();
                    // 2. content of every viewport (incl. legends as obstacles)
                    var content = new Dictionary<ElementId, List<Rect>>(); var crops = new Dictionary<ElementId, Rect>();
                    foreach (var vp in all)
                    {
                        var v = (View)doc.GetElement(vp.ViewId);
                        try { content[vp.Id] = Content(doc, vp, v, out var cr); crops[vp.Id] = cr; }
                        catch (Exception ex) { var b = vp.GetBoxOutline(); content[vp.Id] = new List<Rect> { new Rect(b.MinimumPoint.X, b.MinimumPoint.Y, b.MaximumPoint.X, b.MaximumPoint.Y, "box(" + ex.Message + ")") }; crops[vp.Id] = content[vp.Id][0]; }
                    }
                    var titleRects = new Dictionary<ElementId, Rect>();
                    foreach (var vp in all)
                    {
                        var ty = doc.GetElement(vp.GetTypeId()) as ElementType;
                        if ((ty?.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL)?.AsInteger() ?? 1) == 0) continue;
                        try { var lo = vp.GetLabelOutline(); if (lo.MaximumPoint.X > lo.MinimumPoint.X) titleRects[vp.Id] = new Rect(lo.MinimumPoint.X, lo.MinimumPoint.Y, lo.MaximumPoint.X, lo.MaximumPoint.Y); } catch { }
                    }
                    // 3. place
                    foreach (var vp in vps)
                    {
                        if (!titleRects.ContainsKey(vp.Id)) { report.Add(new { Sheet = s.SheetNumber, Vp = vp.Id.IntegerValue, View = doc.GetElement(vp.ViewId).Name, Note = "no title (type has Show Title = No)" }); continue; }
                        var lab = titleRects[vp.Id]; double w = lab.x1 - lab.x0, h = lab.y1 - lab.y0;
                        var crop = crops[vp.Id];
                        double cx = (crop.x0 + crop.x1) / 2;
                        var own = content[vp.Id];
                        // align "left": the title's left edge on the leftmost content of the view (grid heads, crop), shifts only to the right
                        bool alignLeft = ((string)args["align"] ?? "centre") == "left";
                        if (alignLeft) cx = own.Select(r => r.x0).DefaultIfEmpty(crop.x0).Min() + w / 2;
                        var tb = new FilteredElementCollector(doc, s.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
                        var tbb = tb?.get_BoundingBox(s);
                        Rect target = null, first = null; Rect lowest = null; List<string> hits = null, firstHits = null; double usedShift = 0, usedGap = gap;
                        // try centred first, then the smallest sideways shift, gap from gapMm down to 1.5 mm
                        var shifts = new List<double> { 0 }; for (int k = 5; k <= 120; k += 5) { shifts.Add(k / MM); if (!alignLeft) shifts.Add(-k / MM); }
                        var gaps = new List<double> { gap }; if (gap > 2.5 / MM) gaps.Add(2.5 / MM); gaps.Add(1.5 / MM);
                        foreach (var sh in shifts)
                        {
                            foreach (var gp in gaps)
                            {
                                double tx0 = cx + sh - w / 2, tx1 = cx + sh + w / 2;
                                var under = own.Where(r => r.x1 > tx0 && r.x0 < tx1).ToList();
                                double bottom = under.Select(r => r.y0).DefaultIfEmpty(crop.y0).Min();
                                double top = bottom - gp;
                                var tr = new Rect(tx0, top - h, tx1, top);
                                var hs = new List<string>();
                                foreach (var o in all) if (o.Id != vp.Id)
                                {
                                    foreach (var r in content[o.Id]) if (Hit(tr, r, 0.3 / MM)) { hs.Add(doc.GetElement(o.ViewId).Name + ": " + r.src); break; }
                                    if (titleRects.ContainsKey(o.Id) && Hit(tr, titleRects[o.Id], 0.3 / MM)) hs.Add("title of " + doc.GetElement(o.ViewId).Name);
                                }
                                if (tbb != null && (tr.y0 < tbb.Min.Y + 8 / MM || tr.x0 < tbb.Min.X + 8 / MM)) hs.Add("sheet border");
                                if (first == null) { first = tr; firstHits = hs; lowest = under.OrderBy(r => r.y0).FirstOrDefault(); }
                                if (hs.Count == 0) { target = tr; hits = hs; usedShift = sh; usedGap = gp; lowest = under.OrderBy(r => r.y0).FirstOrDefault(); break; }
                            }
                            if (target != null) break;
                        }
                        if (target == null) { target = first; hits = firstHits; }
                        double dx = target.x0 - lab.x0, dy = target.y0 - lab.y0;
                        string action = Math.Abs(dx) < 0.5 / MM && Math.Abs(dy) < 0.5 / MM ? "ok" : (hits.Count > 0 ? "conflict" : "move");
                        if (mode == "apply" && action == "move")
                        {
                            if (!entries.Any(x => (int)x["vp"] == vp.Id.IntegerValue))
                                entries.Add(new JObject { ["vp"] = vp.Id.IntegerValue, ["offX"] = vp.LabelOffset.X, ["offY"] = vp.LabelOffset.Y });
                            vp.LabelOffset = vp.LabelOffset + new XYZ(dx, dy, 0);
                            doc.Regenerate();
                            var lo = vp.GetLabelOutline();
                            titleRects[vp.Id] = new Rect(lo.MinimumPoint.X, lo.MinimumPoint.Y, lo.MaximumPoint.X, lo.MaximumPoint.Y);
                        }
                        report.Add(new
                        {
                            Sheet = s.SheetNumber, Vp = vp.Id.IntegerValue, View = doc.GetElement(vp.ViewId).Name, Action = action,
                            MoveMm = F(dx) + "," + F(dy), ShiftMm = F(usedShift), GapMm = F(usedGap), Crop = RS(crop), Lowest = lowest == null ? null : lowest.src + " y=" + F(lowest.y0), Title = RS(mode == "apply" ? titleRects[vp.Id] : target), Hits = hits,
                            Debug = vp.Id.IntegerValue == debugVp ? own.OrderBy(r => r.y0).Take(25).Select(r => r.src + " " + RS(r)).ToList() : null
                        });
                    }
                    if (mode == "apply") t.Commit(); else t.RollBack();
                }
            }
            if (mode == "apply") tg.Assimilate(); else tg.RollBack();
        }
        if (mode == "apply") { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.WriteAllText(logPath, entries.ToString()); }
        return report;
    }
}
