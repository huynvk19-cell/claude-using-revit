/* mcp-tool
{
  "description": "Check door/window tags in views of the active document. mode=audit (default, no changes): per view, list doors/windows without a tag, broken tags (orphaned, empty/'?' text, host hidden, duplicate) and tags whose head is crossed by the visible linework (frame, leaf, swing) of the element it tags. Untagged elements are only reported in views that contain at least one door/window tag. mode=highlight: colour those issues in their views (overlap tag = red, broken tag = magenta, untagged element = orange) after saving each element's original view overrides to backupPath. mode=restore: put the saved overrides back from backupPath. Never syncs.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["audit", "highlight", "restore"], "description": "Default audit." },
      "viewIds": { "type": "array", "items": { "type": "number" }, "description": "Only these views. Default: all non-template views of viewTypes (placed on sheets if onlyOnSheets)." },
      "viewTypes": { "type": "array", "items": { "type": "string" }, "description": "ViewType names. Default [\"FloorPlan\"]." },
      "onlyOnSheets": { "type": "boolean", "description": "Default true." },
      "sheetNumberPrefix": { "type": "string", "description": "Only views placed on sheets whose number starts with this text (implies onlyOnSheets)." },
      "overlapMin": { "type": "number", "description": "Min length (paper mm) of the element's visible linework inside the tag head to count as overlap. Default 1.0." },
      "untaggedOnlyInTaggedViews": { "type": "boolean", "description": "Report untagged elements only in views that already have door/window tags. Default true." },
      "backupPath": { "type": "string", "description": "JSON file for original overrides (required for highlight/restore)." },
      "maxItems": { "type": "number", "description": "Max issue rows returned per view. Default 200." }
    }
  },
  "timeoutSeconds": 900,
  "readOnly": false
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class DoorWindowTagCheck
{
    class Issue { public string Kind; public long TagId; public long ElementId; public string Category; public string Text; public double Overlap; }
    class ViewRow { public int ViewId; public string View; public string Sheet; public int Untagged; public int UntaggedOtherLevel; public int Overlap; public int Broken; public List<object> Issues; }

    static bool untaggedOnlyInTaggedViews = true;
    static readonly int Doors = (int)BuiltInCategory.OST_Doors;
    static readonly int Windows = (int)BuiltInCategory.OST_Windows;
    static readonly HashSet<int> TagCats = new HashSet<int> {
        (int)BuiltInCategory.OST_DoorTags, (int)BuiltInCategory.OST_WindowTags, (int)BuiltInCategory.OST_MultiCategoryTags };

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument?.Document;
        if (doc == null) return new { Error = "No active document" };
        string mode = (args.Value<string>("mode") ?? "audit").ToLowerInvariant();
        string backupPath = args.Value<string>("backupPath");

        if (mode == "restore") return Restore(doc, backupPath);
        if (mode == "highlight" && string.IsNullOrWhiteSpace(backupPath))
            return new { Error = "backupPath is required for highlight" };

        var views = PickViews(doc, args);
        var sheetOf = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
            .GroupBy(vp => vp.ViewId.IntegerValue)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(vp => (doc.GetElement(vp.SheetId) as ViewSheet)?.SheetNumber)));
        double overlapMin = args.Value<double?>("overlapMin") ?? 1.0;
        untaggedOnlyInTaggedViews = args.Value<bool?>("untaggedOnlyInTaggedViews") ?? true;
        int maxItems = args.Value<int?>("maxItems") ?? 200;

        var perView = new List<ViewRow>();
        var issuesByView = new Dictionary<View, List<Issue>>();
        foreach (var v in views)
        {
            var issues = AuditView(doc, v, overlapMin);
            issuesByView[v] = issues;
            perView.Add(new ViewRow
            {
                ViewId = v.Id.IntegerValue, View = v.Name,
                Sheet = sheetOf.TryGetValue(v.Id.IntegerValue, out var s) ? s : null,
                Untagged = issues.Count(i => i.Kind == "untagged"),
                UntaggedOtherLevel = issues.Count(i => i.Kind == "untagged_other_level"),
                Overlap = issues.Count(i => i.Kind == "overlap"),
                Broken = issues.Count(i => IsBroken(i.Kind)),
                Issues = issues.Take(maxItems).Select(i => (object)new { i.Kind, i.Category, i.TagId, i.ElementId, i.Text, OverlapPaperMm = Math.Round(i.Overlap, 1) }).ToList()
            });
        }

        object highlight = null;
        if (mode == "highlight") highlight = Highlight(doc, issuesByView, backupPath);

        return new
        {
            Document = doc.Title, Mode = mode, ViewsChecked = views.Count,
            Totals = new
            {
                Untagged = issuesByView.Values.Sum(l => l.Count(i => i.Kind == "untagged")),
                UntaggedOtherLevel = issuesByView.Values.Sum(l => l.Count(i => i.Kind == "untagged_other_level")),
                Overlap = issuesByView.Values.Sum(l => l.Count(i => i.Kind == "overlap")),
                Broken = issuesByView.Values.Sum(l => l.Count(i => IsBroken(i.Kind)))
            },
            Highlight = highlight,
            Views = perView.Where(p => p.Untagged + p.UntaggedOtherLevel + p.Overlap + p.Broken > 0).ToList(),
            CleanViews = perView.Where(p => p.Untagged + p.UntaggedOtherLevel + p.Overlap + p.Broken == 0).Select(p => p.View).ToList()
        };
    }

    static List<View> PickViews(Document doc, JObject args)
    {
        var ids = args["viewIds"]?.Values<int>().ToList();
        if (ids != null && ids.Count > 0)
            return ids.Select(i => doc.GetElement(new ElementId(i)) as View).Where(v => v != null).ToList();

        var types = new HashSet<string>(args["viewTypes"]?.Values<string>() ?? new[] { "FloorPlan" });
        string prefix = args.Value<string>("sheetNumberPrefix");
        bool onlyOnSheets = (args.Value<bool?>("onlyOnSheets") ?? true) || !string.IsNullOrEmpty(prefix);
        var placed = new HashSet<int>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
            .Where(vp => string.IsNullOrEmpty(prefix)
                || ((doc.GetElement(vp.SheetId) as ViewSheet)?.SheetNumber ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(vp => vp.ViewId.IntegerValue));
        return new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && types.Contains(v.ViewType.ToString()))
            .Where(v => !onlyOnSheets || placed.Contains(v.Id.IntegerValue))
            .OrderBy(v => v.Name).ToList();
    }

    static List<Issue> AuditView(Document doc, View v, double overlapMin)
    {
        var issues = new List<Issue>();
        var elems = new FilteredElementCollector(doc, v.Id)
            .WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows }))
            .WhereElementIsNotElementType()
            .Where(e => !(e is FamilyInstance fi) || fi.SuperComponent == null)
            .ToDictionary(e => e.Id.IntegerValue);

        var tags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
            .Where(t => t.Category != null && TagCats.Contains(t.Category.Id.IntegerValue)).ToList();

        // Tag head boxes without leaders: drop leaders in a probe transaction, read boxes, roll back.
        var headBox = new Dictionary<int, BoundingBoxXYZ>();
        var leaderTags = tags.Where(t => { try { return t.HasLeader; } catch { return false; } }).ToList();
        if (leaderTags.Count > 0 && !doc.IsModifiable)
        {
            using (var probe = new Transaction(doc, "probe tag heads"))
            {
                probe.Start();
                foreach (var t in leaderTags) { try { t.HasLeader = false; } catch { } }
                doc.Regenerate();
                foreach (var t in leaderTags) headBox[t.Id.IntegerValue] = t.get_BoundingBox(v);
                probe.RollBack();
            }
        }

        var taggedCount = new Dictionary<int, int>();
        foreach (var t in tags)
        {
            var hostIds = new List<ElementId>();
            try { hostIds = t.GetTaggedLocalElementIds().Where(id => id != ElementId.InvalidElementId).ToList(); } catch { }
            bool linked = false;
            try { linked = t.GetTaggedElementIds().Any(l => l.LinkInstanceId != ElementId.InvalidElementId); } catch { }
            if (linked) continue;

            var hosts = hostIds.Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            // A multi-category tag only counts when it tags a door or window.
            if (t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags
                && !hosts.Any(h => IsDoorOrWindow(h))) continue;

            string text = null;
            try { text = t.TagText; } catch { }
            var host = hosts.FirstOrDefault();
            string cat = host?.Category?.Name ?? t.Category.Name;

            bool orphan = false;
            try { orphan = t.IsOrphaned; } catch { }
            if (orphan || host == null)
            {
                issues.Add(new Issue { Kind = "orphaned", TagId = t.Id.IntegerValue, Category = cat, Text = text });
                continue;
            }
            foreach (var h in hosts)
                taggedCount[h.Id.IntegerValue] = (taggedCount.TryGetValue(h.Id.IntegerValue, out var n) ? n : 0) + 1;

            if (string.IsNullOrWhiteSpace(text) || text.Contains("?"))
                issues.Add(new Issue { Kind = "empty_text", TagId = t.Id.IntegerValue, ElementId = host.Id.IntegerValue, Category = cat, Text = text });
            if (!elems.ContainsKey(host.Id.IntegerValue))
                issues.Add(new Issue { Kind = "host_not_visible", TagId = t.Id.IntegerValue, ElementId = host.Id.IntegerValue, Category = cat, Text = text });
            else if (taggedCount[host.Id.IntegerValue] > 1)
                issues.Add(new Issue { Kind = "duplicate", TagId = t.Id.IntegerValue, ElementId = host.Id.IntegerValue, Category = cat, Text = text });

            var tb = headBox.TryGetValue(t.Id.IntegerValue, out var hb) ? hb : t.get_BoundingBox(v);
            double paperMm = LineworkInside(v, host, tb);
            if (paperMm >= overlapMin)
                issues.Add(new Issue { Kind = "overlap", TagId = t.Id.IntegerValue, ElementId = host.Id.IntegerValue, Category = cat, Text = text, Overlap = paperMm });
        }

        // Untagged only matters in views that are used for door/window tagging.
        // Elements hosted on another level (e.g. tall doors seen from the level above) are reported apart.
        var viewLevel = v.GenLevel?.Id ?? ElementId.InvalidElementId;
        if (taggedCount.Count > 0 || !untaggedOnlyInTaggedViews)
            foreach (var e in elems.Values)
                if (!taggedCount.ContainsKey(e.Id.IntegerValue))
                {
                    bool other = viewLevel != ElementId.InvalidElementId && e.LevelId != ElementId.InvalidElementId && e.LevelId != viewLevel;
                    issues.Add(new Issue { Kind = other ? "untagged_other_level" : "untagged", ElementId = e.Id.IntegerValue,
                        Category = e.Category?.Name, Text = Mark(e) + (other ? " @ " + doc.GetElement(e.LevelId)?.Name : "") });
                }
        return issues;
    }

    // Length (paper mm) of the element's visible linework in this view that runs inside the tag head box.
    static double LineworkInside(View v, Element host, BoundingBoxXYZ tagBox)
    {
        if (tagBox == null) return 0;
        var r = Rect2D(v, tagBox);
        var segs = new List<XYZ[]>();
        Collect(host.get_Geometry(new Options { View = v }), Transform.Identity, segs);
        double len = 0;
        XYZ rd = v.RightDirection, ud = v.UpDirection;
        foreach (var s in segs)
            len += ClipLength(s[0].DotProduct(rd), s[0].DotProduct(ud), s[1].DotProduct(rd), s[1].DotProduct(ud), r);
        return len * 304.8 / Math.Max(1, v.Scale);
    }

    static void Collect(GeometryElement g, Transform tr, List<XYZ[]> segs)
    {
        if (g == null) return;
        foreach (var o in g)
        {
            if (o is GeometryInstance gi) Collect(gi.GetSymbolGeometry(), tr.Multiply(gi.Transform), segs);
            else if (o is Solid s && s.Volume > 1e-9) foreach (Edge e in s.Edges) AddPolyline(e.Tessellate(), tr, segs);
            else if (o is Curve c) AddPolyline(c.Tessellate(), tr, segs);
            else if (o is PolyLine pl) AddPolyline(pl.GetCoordinates(), tr, segs);
        }
    }

    static void AddPolyline(IList<XYZ> pts, Transform tr, List<XYZ[]> segs)
    {
        for (int i = 1; i < pts.Count; i++) segs.Add(new[] { tr.OfPoint(pts[i - 1]), tr.OfPoint(pts[i]) });
    }

    // Liang–Barsky: length of segment (x0,y0)-(x1,y1) inside rect r = {xmin, ymin, xmax, ymax}.
    static double ClipLength(double x0, double y0, double x1, double y1, double[] r)
    {
        double dx = x1 - x0, dy = y1 - y0, t0 = 0, t1 = 1;
        double[] p = { -dx, dx, -dy, dy }, q = { x0 - r[0], r[2] - x0, y0 - r[1], r[3] - y0 };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-12) { if (q[i] < 0) return 0; continue; }
            double t = q[i] / p[i];
            if (p[i] < 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
            if (t0 > t1) return 0;
        }
        return (t1 - t0) * Math.Sqrt(dx * dx + dy * dy);
    }

    static bool IsBroken(string k) => k == "orphaned" || k == "empty_text" || k == "host_not_visible" || k == "duplicate";

    static bool IsDoorOrWindow(Element e)
    {
        int c = e?.Category?.Id.IntegerValue ?? 0;
        return c == Doors || c == Windows;
    }

    static string Mark(Element e) => e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();

    static double[] Rect2D(View v, BoundingBoxXYZ bb)
    {
        var t = bb.Transform ?? Transform.Identity;
        XYZ r = v.RightDirection, u = v.UpDirection;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var x in new[] { bb.Min.X, bb.Max.X })
            foreach (var y in new[] { bb.Min.Y, bb.Max.Y })
                foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                {
                    var p = t.OfPoint(new XYZ(x, y, z));
                    double px = p.DotProduct(r), py = p.DotProduct(u);
                    x0 = Math.Min(x0, px); x1 = Math.Max(x1, px); y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
                }
        return new[] { x0, y0, x1, y1 };
    }

    // ---------- highlight / restore ----------

    static object Highlight(Document doc, Dictionary<View, List<Issue>> issuesByView, string backupPath)
    {
        var backup = File.Exists(backupPath) ? JObject.Parse(File.ReadAllText(backupPath)) : new JObject();
        if (backup["Document"] != null && (string)backup["Document"] != doc.Title)
            return new { Error = $"backupPath belongs to document '{backup["Document"]}'. Restore it first or use another path." };
        backup["Document"] = doc.Title;
        var entries = backup["Entries"] as JObject ?? new JObject();
        backup["Entries"] = entries;

        var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
        var red = new Color(255, 0, 0); var magenta = new Color(255, 0, 255); var orange = new Color(255, 128, 0); var yellow = new Color(255, 220, 0);

        int applied = 0;
        var skipped = new List<object>();
        using (var tx = new Transaction(doc, "Highlight door/window tag issues"))
        {
            tx.Start();
            foreach (var kv in issuesByView)
            {
                var v = kv.Key;
                if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, v.Id) == CheckoutStatus.OwnedByOtherUser)
                {
                    skipped.Add(new { ViewId = v.Id.IntegerValue, v.Name, Reason = "view owned by another user" });
                    continue;
                }
                if (v.ViewTemplateId != ElementId.InvalidElementId) { /* element overrides are not controlled by templates */ }

                foreach (var i in kv.Value)
                {
                    bool onElement = i.Kind.StartsWith("untagged");
                    int target = onElement ? (int)i.ElementId : (int)i.TagId;
                    if (target <= 0) continue;
                    var id = new ElementId(target);
                    string key = v.Id.IntegerValue + ":" + target;
                    if (entries[key] == null) entries[key] = Save(v.GetElementOverrides(id));

                    var o = new OverrideGraphicSettings();
                    var c = i.Kind == "untagged" ? orange : i.Kind == "untagged_other_level" ? yellow : i.Kind == "overlap" ? red : magenta;
                    o.SetProjectionLineColor(c); o.SetProjectionLineWeight(8);
                    o.SetCutLineColor(c); o.SetCutLineWeight(8);
                    if (onElement && solid != null)
                    {
                        o.SetSurfaceForegroundPatternId(solid.Id); o.SetSurfaceForegroundPatternColor(c);
                        o.SetCutForegroundPatternId(solid.Id); o.SetCutForegroundPatternColor(c);
                    }
                    v.SetElementOverrides(id, o);
                    applied++;
                }
            }
            File.WriteAllText(backupPath, backup.ToString(Formatting.Indented)); // before commit: never lose originals
            tx.Commit();
        }
        return new { Applied = applied, BackupPath = backupPath, BackupEntries = entries.Count, SkippedViews = skipped,
            Legend = new { Overlap = "red tag", Broken = "magenta tag", Untagged = "orange door/window", UntaggedOtherLevel = "yellow door/window (hosted on another level)" } };
    }

    static object Restore(Document doc, string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            return new { Error = "backupPath not found: " + backupPath };
        var backup = JObject.Parse(File.ReadAllText(backupPath));
        if ((string)backup["Document"] != doc.Title)
            return new { Error = $"Backup is for '{backup["Document"]}', active document is '{doc.Title}'." };

        int restored = 0; var missing = new List<string>();
        var pending = new JObject();                 // entries we could not restore now (view owned by someone else)
        var blockedViews = new HashSet<string>();
        using (var tx = new Transaction(doc, "Restore door/window tag highlight"))
        {
            tx.Start();
            foreach (var p in ((JObject)backup["Entries"]).Properties())
            {
                var parts = p.Name.Split(':');
                var v = doc.GetElement(new ElementId(int.Parse(parts[0]))) as View;
                var id = new ElementId(int.Parse(parts[1]));
                if (v == null || doc.GetElement(id) == null) { missing.Add(p.Name); continue; }
                if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, v.Id) == CheckoutStatus.OwnedByOtherUser)
                {
                    pending[p.Name] = p.Value; blockedViews.Add(v.Name + " (owner: " + WorksharingUtils.GetWorksharingTooltipInfo(doc, v.Id).Owner + ")");
                    continue;
                }
                v.SetElementOverrides(id, Load((JObject)p.Value));
                restored++;
            }
            tx.Commit();
        }
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (pending.Count == 0)
            File.Move(backupPath, backupPath + ".restored-" + stamp);
        else
        {
            File.Copy(backupPath, backupPath + ".before-partial-restore-" + stamp);
            backup["Entries"] = pending;
            File.WriteAllText(backupPath, backup.ToString(Formatting.Indented));
        }
        return new { Restored = restored, MissingElements = missing, StillPending = pending.Count, BlockedViews = blockedViews };
    }

    static JArray C(Color c) => c != null && c.IsValid ? new JArray(c.Red, c.Green, c.Blue) : null;
    static Color C(JToken t) => t is JArray a ? new Color((byte)(int)a[0], (byte)(int)a[1], (byte)(int)a[2]) : null;

    static JObject Save(OverrideGraphicSettings o) => new JObject
    {
        ["pLinePat"] = o.ProjectionLinePatternId.IntegerValue, ["pLineCol"] = C(o.ProjectionLineColor), ["pLineW"] = o.ProjectionLineWeight,
        ["cLinePat"] = o.CutLinePatternId.IntegerValue, ["cLineCol"] = C(o.CutLineColor), ["cLineW"] = o.CutLineWeight,
        ["sFgPat"] = o.SurfaceForegroundPatternId.IntegerValue, ["sFgCol"] = C(o.SurfaceForegroundPatternColor), ["sFgVis"] = o.IsSurfaceForegroundPatternVisible,
        ["sBgPat"] = o.SurfaceBackgroundPatternId.IntegerValue, ["sBgCol"] = C(o.SurfaceBackgroundPatternColor), ["sBgVis"] = o.IsSurfaceBackgroundPatternVisible,
        ["cFgPat"] = o.CutForegroundPatternId.IntegerValue, ["cFgCol"] = C(o.CutForegroundPatternColor), ["cFgVis"] = o.IsCutForegroundPatternVisible,
        ["cBgPat"] = o.CutBackgroundPatternId.IntegerValue, ["cBgCol"] = C(o.CutBackgroundPatternColor), ["cBgVis"] = o.IsCutBackgroundPatternVisible,
        ["transp"] = o.Transparency, ["halftone"] = o.Halftone, ["detail"] = o.DetailLevel.ToString()
    };

    static OverrideGraphicSettings Load(JObject j)
    {
        var o = new OverrideGraphicSettings();
        int Pat(string k) => (int?)j[k] ?? -1;
        if (Pat("pLinePat") > 0) o.SetProjectionLinePatternId(new ElementId(Pat("pLinePat")));
        if (C(j["pLineCol"]) is Color a) o.SetProjectionLineColor(a);
        if ((int)j["pLineW"] > 0) o.SetProjectionLineWeight((int)j["pLineW"]);
        if (Pat("cLinePat") > 0) o.SetCutLinePatternId(new ElementId(Pat("cLinePat")));
        if (C(j["cLineCol"]) is Color b) o.SetCutLineColor(b);
        if ((int)j["cLineW"] > 0) o.SetCutLineWeight((int)j["cLineW"]);
        if (Pat("sFgPat") > 0) o.SetSurfaceForegroundPatternId(new ElementId(Pat("sFgPat")));
        if (C(j["sFgCol"]) is Color c1) o.SetSurfaceForegroundPatternColor(c1);
        o.SetSurfaceForegroundPatternVisible((bool)j["sFgVis"]);
        if (Pat("sBgPat") > 0) o.SetSurfaceBackgroundPatternId(new ElementId(Pat("sBgPat")));
        if (C(j["sBgCol"]) is Color c2) o.SetSurfaceBackgroundPatternColor(c2);
        o.SetSurfaceBackgroundPatternVisible((bool)j["sBgVis"]);
        if (Pat("cFgPat") > 0) o.SetCutForegroundPatternId(new ElementId(Pat("cFgPat")));
        if (C(j["cFgCol"]) is Color c3) o.SetCutForegroundPatternColor(c3);
        o.SetCutForegroundPatternVisible((bool)j["cFgVis"]);
        if (Pat("cBgPat") > 0) o.SetCutBackgroundPatternId(new ElementId(Pat("cBgPat")));
        if (C(j["cBgCol"]) is Color c4) o.SetCutBackgroundPatternColor(c4);
        o.SetCutBackgroundPatternVisible((bool)j["cBgVis"]);
        o.SetSurfaceTransparency((int)j["transp"]);
        o.SetHalftone((bool)j["halftone"]);
        if (Enum.TryParse((string)j["detail"], out ViewDetailLevel d)) o.SetDetailLevel(d);
        return o;
    }
}
