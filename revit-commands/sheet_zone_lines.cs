/* mcp-tool
{
  "description": "Split each sheet's drawing area into one zone per view with thin lines (guillotine cuts in the gaps between viewports, like cutting a cake). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sheetNumbers": { "type": "array", "items": { "type": "string" }, "description": "sheets to zone" },
      "areaMm": { "type": "object", "description": "drawing area {minX, minY, maxX, maxY} in paper mm" },
      "lineStyle": { "type": "string", "description": "line style name (default Thin Lines)" },
      "replace": { "type": "boolean", "description": "delete zone lines of the log before drawing (default true)" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string", "description": "created line ids per sheet; required for apply and undo" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Rule LO13 of drafting-sheet-layout.md. Each item of a sheet (viewport, schedule) has two extents in paper mm:
//    soft = the viewport box (annotation crop included) united with its title;
//    hard = the view's crop region + 2 mm, united with the title + 3 mm (schedules: soft = hard = their box).
// The drawing area is cut recursively: at each step one line runs from edge to edge of the region being cut, in the
// widest clear gap between the soft extents (middle of the gap); when the soft extents leave no gap, in the widest
// gap between the hard extents (middle of that gap, reported in Soft: the line may pass a grid bubble or a dim, check
// the image). Then each half is cut again until each region holds one item. No gap even between hard extents = the
// items stay in one zone (Tight): move the views apart (drafting-sheet-place card 03) and run again.
// Lines are detail lines on the sheet in lineStyle. apply writes logPath {sheetNumber: [line ids]}; with replace (default)
// the lines of an earlier log at the same path are deleted first, so the command can be run again after a layout change.
// undo deletes the lines of the log. Sheets with one item get no line.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class SheetZoneLines
{
    const double MM = 304.8;

    class Ext { public double X0, Y0, X1, Y1; }
    class Item { public Ext Soft, Hard; public string Name; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", log = (string)args["logPath"];
        if (mode != "preview" && mode != "apply" && mode != "undo") return new { Error = "mode: preview | apply | undo" };
        if (mode != "preview" && string.IsNullOrEmpty(log)) return new { Error = "logPath is required for " + mode };

        var old = new Dictionary<string, List<int>>();
        if (!string.IsNullOrEmpty(log) && File.Exists(log))
            old = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(File.ReadAllText(log)) ?? old;
        if (mode == "undo")
        {
            int del;
            using (var t = new Transaction(doc, "Zone lines undo")) { t.Start(); del = Delete(doc, old.Values.SelectMany(x => x)); t.Commit(); }
            File.Delete(log);
            return new { mode, Deleted = del };
        }

        var area = args["areaMm"] as JObject;
        if (area == null) return new { Error = "areaMm {minX, minY, maxX, maxY} is required" };
        var region = new Ext { X0 = (double)area["minX"], Y0 = (double)area["minY"], X1 = (double)area["maxX"], Y1 = (double)area["maxY"] };
        string styleName = (string)args["lineStyle"] ?? "Thin Lines";
        string bare = styleName.Trim('<', '>');
        var lineCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        var sub = lineCat.SubCategories.Cast<Category>().FirstOrDefault(c => c.Name == styleName || c.Name.Trim('<', '>') == bare);
        if (sub == null) return new { Error = "line style not found: " + styleName };
        var style = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
        bool replace = args.Value<bool?>("replace") ?? true;

        var numbers = (args["sheetNumbers"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => numbers.Contains(s.SheetNumber)).ToList();
        var rows = new List<object>();
        var logOut = new Dictionary<string, List<int>>(old);

        using (var t = new Transaction(doc, "Zone lines"))
        {
            t.Start();
            if (replace) foreach (var s in sheets) if (old.ContainsKey(s.SheetNumber)) { Delete(doc, old[s.SheetNumber]); logOut.Remove(s.SheetNumber); }
            foreach (var s in sheets)
            {
                var items = Items(doc, s);
                var cuts = new List<Ext>(); var tight = new List<string>(); var soft = new List<string>();
                Cut(region, items, cuts, tight, soft);
                var ids = new List<int>();
                foreach (var c in cuts)
                {
                    var dc = doc.Create.NewDetailCurve(s, Line.CreateBound(new XYZ(c.X0 / MM, c.Y0 / MM, 0), new XYZ(c.X1 / MM, c.Y1 / MM, 0)));
                    dc.LineStyle = style;
                    ids.Add(dc.Id.IntegerValue);
                }
                if (ids.Count > 0) logOut[s.SheetNumber] = ids;
                rows.Add(new { Sheet = s.SheetNumber, Items = items.Count, Lines = cuts.Count, Soft = soft, Tight = tight });
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(log, JsonConvert.SerializeObject(logOut, Formatting.Indented)); }
            else t.RollBack();
        }
        return new { mode, Sheets = rows };
    }

    static int Delete(Document doc, IEnumerable<int> ids)
    {
        int n = 0;
        foreach (var i in ids) { var e = doc.GetElement(new ElementId(i)); if (e != null) { doc.Delete(e.Id); n++; } }
        return n;
    }

    static Ext Union(Ext a, Ext b) => b == null ? a : new Ext { X0 = Math.Min(a.X0, b.X0), Y0 = Math.Min(a.Y0, b.Y0), X1 = Math.Max(a.X1, b.X1), Y1 = Math.Max(a.Y1, b.Y1) };
    static Ext Grow(Ext a, double d) => new Ext { X0 = a.X0 - d, Y0 = a.Y0 - d, X1 = a.X1 + d, Y1 = a.Y1 + d };

    static List<Item> Items(Document doc, ViewSheet s)
    {
        var list = new List<Item>();
        foreach (var id in s.GetAllViewports())
        {
            var vp = (Viewport)doc.GetElement(id);
            var view = (View)doc.GetElement(vp.ViewId);
            var o = vp.GetBoxOutline();
            var box = new Ext { X0 = o.MinimumPoint.X * MM, Y0 = o.MinimumPoint.Y * MM, X1 = o.MaximumPoint.X * MM, Y1 = o.MaximumPoint.Y * MM };
            Ext label = null;
            try
            {
                var l = vp.GetLabelOutline();
                if (l != null && !l.IsEmpty) label = new Ext { X0 = l.MinimumPoint.X * MM, Y0 = l.MinimumPoint.Y * MM, X1 = l.MaximumPoint.X * MM, Y1 = l.MaximumPoint.Y * MM };
            }
            catch { }
            Ext crop = box;
            if (view.CropBoxActive && view.Scale > 0)
            {
                // the box is the annotation crop around the crop region, centred on it
                var c = vp.GetBoxCenter(); var cb = view.CropBox;
                double hw = (cb.Max.X - cb.Min.X) * MM / view.Scale / 2, hh = (cb.Max.Y - cb.Min.Y) * MM / view.Scale / 2;
                crop = new Ext { X0 = c.X * MM - hw, Y0 = c.Y * MM - hh, X1 = c.X * MM + hw, Y1 = c.Y * MM + hh };
            }
            list.Add(new Item { Name = view.Name, Soft = Union(box, label), Hard = Union(Grow(crop, 2), label == null ? null : Grow(label, 3)) });
        }
        foreach (var si in new FilteredElementCollector(doc, s.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
        {
            if (si.IsTitleblockRevisionSchedule) continue;
            var bb = si.get_BoundingBox(s); if (bb == null) continue;
            var e = new Ext { X0 = bb.Min.X * MM, Y0 = bb.Min.Y * MM, X1 = bb.Max.X * MM, Y1 = bb.Max.Y * MM };
            list.Add(new Item { Name = doc.GetElement(si.ScheduleId).Name, Soft = e, Hard = e });
        }
        return list;
    }

    /// <summary>Widest gap over all splits of the items along x (vertical line) or y, between the chosen extents.</summary>
    static bool Best(List<Item> items, bool hard, out double at, out bool vertical, out double gap)
    {
        at = 0; vertical = true; gap = 0;
        foreach (bool v in new[] { true, false })
        {
            var sorted = items.OrderBy(b => v ? b.Soft.X0 + b.Soft.X1 : b.Soft.Y0 + b.Soft.Y1).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                Func<Item, Ext> E = b => hard ? b.Hard : b.Soft;
                double reach = sorted.Take(i).Max(b => v ? E(b).X1 : E(b).Y1), start = sorted.Skip(i).Min(b => v ? E(b).X0 : E(b).Y0);
                if (start - reach > gap) { gap = start - reach; at = (start + reach) / 2; vertical = v; }
            }
        }
        return gap > 0;
    }

    static void Cut(Ext r, List<Item> items, List<Ext> cuts, List<string> tight, List<string> soft)
    {
        if (items.Count <= 1) return;
        double at, gap; bool vertical;
        if (!Best(items, false, out at, out vertical, out gap))
        {
            if (!Best(items, true, out at, out vertical, out gap)) { tight.Add(string.Join(" + ", items.Select(b => b.Name))); return; }
            soft.Add(string.Join(" | ", items.Select(b => b.Name)));
        }
        // an item goes to the side its hard extent lies on
        Func<Item, bool> low = b => vertical ? b.Hard.X1 <= at : b.Hard.Y1 <= at;
        if (vertical)
        {
            cuts.Add(new Ext { X0 = at, Y0 = r.Y0, X1 = at, Y1 = r.Y1 });
            Cut(new Ext { X0 = r.X0, Y0 = r.Y0, X1 = at, Y1 = r.Y1 }, items.Where(low).ToList(), cuts, tight, soft);
            Cut(new Ext { X0 = at, Y0 = r.Y0, X1 = r.X1, Y1 = r.Y1 }, items.Where(b => !low(b)).ToList(), cuts, tight, soft);
        }
        else
        {
            cuts.Add(new Ext { X0 = r.X0, Y0 = at, X1 = r.X1, Y1 = at });
            Cut(new Ext { X0 = r.X0, Y0 = r.Y0, X1 = r.X1, Y1 = at }, items.Where(low).ToList(), cuts, tight, soft);
            Cut(new Ext { X0 = r.X0, Y0 = at, X1 = r.X1, Y1 = r.Y1 }, items.Where(b => !low(b)).ToList(), cuts, tight, soft);
        }
    }
}
