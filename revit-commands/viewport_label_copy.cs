/* mcp-tool
{
  "description": "Copy the title position (LabelOffset) and title line length of one viewport to others, e.g. after a view moved to another sheet; optionally set Title on Sheet of views. preview | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "items": { "type": "array", "items": { "type": "object" }, "description": "{vpId, fromVpId}" },
      "titles": { "type": "array", "items": { "type": "object" }, "description": "{viewId, titleOnSheet}" },
      "mode": { "type": "string", "enum": ["preview", "apply"] }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details ----
// items: the title of vpId gets the LabelOffset (title position relative to the viewport box) and LabelLineLength of
// fromVpId. titles: Title on Sheet (VIEW_DESCRIPTION) of viewId set to titleOnSheet ("" clears it).
// preview rolls the transaction back and reports old -> new. Nothing else changes.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewportLabelCopy
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        const double MM = 304.8;
        string mode = args.Value<string>("mode") ?? "preview";
        var items = (args["items"] as JArray ?? new JArray()).Cast<JObject>().ToList();
        var titles = (args["titles"] as JArray ?? new JArray()).Cast<JObject>().ToList();
        var rows = new List<object>(); var problems = new List<string>();
        Func<double, double> r = v => Math.Round(v * MM, 1);
        using (var t = new Transaction(doc, "Viewport titles copied"))
        {
            t.Start();
            foreach (var it in items)
            {
                var vp = doc.GetElement(new ElementId((int)it["vpId"])) as Viewport;
                var src = doc.GetElement(new ElementId((int)it["fromVpId"])) as Viewport;
                if (vp == null || src == null) { problems.Add("viewport not found " + it); continue; }
                XYZ o0 = vp.LabelOffset; double l0 = vp.LabelLineLength;
                vp.LabelOffset = src.LabelOffset;
                vp.LabelLineLength = src.LabelLineLength;
                rows.Add(new { Vp = vp.Id.IntegerValue, From = src.Id.IntegerValue,
                    OffsetOld = new[] { r(o0.X), r(o0.Y) }, OffsetNew = new[] { r(src.LabelOffset.X), r(src.LabelOffset.Y) },
                    LineOld = r(l0), LineNew = r(src.LabelLineLength) });
            }
            foreach (var it in titles)
            {
                var v = doc.GetElement(new ElementId((int)it["viewId"])) as View;
                var p = v?.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                if (p == null || p.IsReadOnly) { problems.Add("title on sheet not settable " + it["viewId"]); continue; }
                string old = p.AsString(); string nw = (string)it["titleOnSheet"] ?? "";
                if (old != nw) { p.Set(nw); rows.Add(new { View = v.Name, TitleOld = old, TitleNew = nw }); }
            }
            if (mode == "apply") t.Commit(); else t.RollBack();
        }
        return new { Mode = mode, Rows = rows, Problems = problems };
    }
}
