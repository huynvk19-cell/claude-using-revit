/* mcp-tool
{
  "description": "Read-only: viewport titles on sheets (prefix): shown, centred, below the box, overlaps; viewport types in use.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sheetPrefix": {
        "type": "string"
      },
      "sheetNumbers": {
        "type": "array",
        "items": {
          "type": "string"
        }
      },
      "includeLegends": {
        "type": "boolean",
        "description": "default true"
      },
      "onlyIssues": {
        "type": "boolean",
        "description": "default false"
      }
    }
  },
  "timeoutSeconds": 180,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: for sheets whose number starts with a prefix, list every viewport with its type, whether the type shows
//    the title, box outline, label outline, label offset/line length (sheet mm), and checks: title shown, centred
//    under the box, below the box, overlapping other viewports or other titles. Also the viewport types in use.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewportTitleAudit
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string prefix = (string)args["sheetPrefix"];
        var nums = (args["sheetNumbers"] as JArray)?.Select(t => (string)t).ToList();
        bool legends = args.Value<bool?>("includeLegends") ?? true, onlyIssues = args.Value<bool?>("onlyIssues") ?? false;
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => (nums != null && nums.Contains(s.SheetNumber)) || (nums == null && prefix != null && s.SheetNumber.StartsWith(prefix)))
            .OrderBy(s => s.SheetNumber).ToList();
        Func<double, double> R = x => Math.Round(x * MM, 1);
        var IC = System.Globalization.CultureInfo.InvariantCulture; Func<double, string> RI = x => R(x).ToString(IC); Func<Outline, string> O = o => o == null ? null : RI(o.MinimumPoint.X) + "," + RI(o.MinimumPoint.Y) + " .. " + RI(o.MaximumPoint.X) + "," + RI(o.MaximumPoint.Y);
        Func<Outline, Outline, bool> Hit = (a, b) => a != null && b != null && a.MinimumPoint.X < b.MaximumPoint.X && a.MaximumPoint.X > b.MinimumPoint.X && a.MinimumPoint.Y < b.MaximumPoint.Y && a.MaximumPoint.Y > b.MinimumPoint.Y;
        var typeUse = new Dictionary<string, int>();
        var outSheets = new List<object>();
        foreach (var s in sheets)
        {
            var vps = s.GetAllViewports().Select(id => (Viewport)doc.GetElement(id)).ToList();
            var info = vps.Select(vp =>
            {
                var v = (View)doc.GetElement(vp.ViewId);
                var t = doc.GetElement(vp.GetTypeId()) as ElementType;
                var show = t?.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL);
                Outline box = null, lab = null;
                try { box = vp.GetBoxOutline(); } catch { }
                try { lab = vp.GetLabelOutline(); } catch { }
                return new { vp, v, t, Show = show?.AsInteger() ?? -1, ShowText = show?.AsValueString(), box, lab };
            }).ToList();
            var sheetEls = new List<Tuple<string, Outline>>();
            foreach (var e in new FilteredElementCollector(doc, s.Id).OwnedByView(s.Id))
            {
                if (e is Viewport || e.Category == null || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_TitleBlocks || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Viewports) continue;
                var bb = e.get_BoundingBox(s); if (bb == null) continue;
                sheetEls.Add(Tuple.Create(e.Category.Name + " " + e.Id.IntegerValue, new Outline(bb.Min, bb.Max)));
            }
            var rows = new List<object>();
            foreach (var x in info)
            {
                bool isLegend = x.v.ViewType == ViewType.Legend;
                if (isLegend && !legends) continue;
                string key = (x.t?.Name ?? "?") + (isLegend ? " [legend]" : "");
                typeUse[key] = typeUse.TryGetValue(key, out var n) ? n + 1 : 1;
                var issues = new List<string>();
                bool shown = x.Show != 0 && x.lab != null && x.lab.MaximumPoint.X - x.lab.MinimumPoint.X > 1e-6;
                if (!shown) issues.Add("no title");
                double dxc = 0, gap = 0;
                if (shown && x.box != null)
                {
                    double bc = (x.box.MinimumPoint.X + x.box.MaximumPoint.X) / 2, lc = (x.lab.MinimumPoint.X + x.lab.MaximumPoint.X) / 2;
                    dxc = (lc - bc) * MM;
                    gap = (x.box.MinimumPoint.Y - x.lab.MaximumPoint.Y) * MM;
                    if (Math.Abs(dxc) > 5) issues.Add("not centred " + Math.Round(dxc) + "mm");
                    if (gap < 0) issues.Add("not below (gap " + Math.Round(gap) + "mm)");
                    foreach (var y in info) if (y.vp.Id != x.vp.Id)
                    {
                        if (Hit(x.lab, y.box)) issues.Add("hits view " + y.v.Name);
                        if (y.Show != 0 && Hit(x.lab, y.lab)) issues.Add("hits title of " + y.v.Name);
                    }
                    foreach (var se in sheetEls) if (Hit(x.lab, se.Item2)) issues.Add("hits sheet " + se.Item1 + " [" + O(se.Item2) + "]");
                }
                if (onlyIssues && issues.Count == 0) continue;
                rows.Add(new
                {
                    VpId = x.vp.Id.IntegerValue, View = x.v.ViewType + ": " + x.v.Name, Type = x.t?.Name, ShowTitle = x.ShowText,
                    Box = O(x.box), Label = O(x.lab), LabelOffset = R(x.vp.LabelOffset.X) + "," + R(x.vp.LabelOffset.Y), LineLen = R(x.vp.LabelLineLength),
                    TitleOnSheet = x.v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.AsString(),
                    CentreDx = Math.Round(dxc), GapBelow = Math.Round(gap), Issues = issues
                });
            }
            if (rows.Count > 0 || !onlyIssues) outSheets.Add(new { Sheet = s.SheetNumber + " - " + s.Name, SheetId = s.Id.IntegerValue, Viewports = rows });
        }
        var types = new FilteredElementCollector(doc).WhereElementIsElementType().OfCategory(BuiltInCategory.OST_Viewports).Cast<ElementType>()
            .Select(t => t.Id.IntegerValue + " " + t.Name + " show=" + t.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL)?.AsValueString()).ToList();
        return new { Sheets = outSheets.Count, TypeUse = typeUse, ViewportTypes = types, Result = outSheets };
    }
}
