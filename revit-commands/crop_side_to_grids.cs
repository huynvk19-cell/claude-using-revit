/* mcp-tool
{
  "description": "Plan: move one crop side just past the outermost grid end on that side (+marginMm). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "items": {
        "type": "array",
        "items": {
          "type": "object"
        },
        "description": "[{viewId, side: 'right'|'left'|'top'|'bottom'}]"
      },
      "marginMm": {
        "type": "number",
        "description": "paper mm beyond the bubble edge (default 6)"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply",
          "undo"
        ]
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "mode",
      "logPath"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Plan views: move one side of the rectangular crop region (right/left/top/bottom in view axes) to just beyond the
//    outermost host grid end (bubble included) on that side, plus marginMm paper. View setting only, no model element
//    changes. mode preview | apply | undo (restores crop boxes from logPath).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class CropSideToGrids
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], log = (string)args["logPath"];
        if (mode == "undo")
        {
            var items = JArray.Parse(File.ReadAllText(log)); int n = 0;
            using (var t = new Transaction(doc, "Crop restore"))
            {
                t.Start();
                foreach (var it in items)
                {
                    var v = (View)doc.GetElement(new ElementId((int)it["ViewId"]));
                    var cb = v.CropBox;
                    cb.Min = new XYZ((double)it["MinX"], (double)it["MinY"], cb.Min.Z); cb.Max = new XYZ((double)it["MaxX"], (double)it["MaxY"], cb.Max.Z);
                    v.CropBox = cb; n++;
                }
                t.Commit();
            }
            return new { Restored = n };
        }
        double margin = args.Value<double?>("marginMm") ?? 6;
        var rows = new List<object>(); var logRows = new List<object>();
        using (var t = new Transaction(doc, "Crop to grids"))
        {
            t.Start();
            foreach (JObject it in (JArray)args["items"])
            {
                var v = (View)doc.GetElement(new ElementId((int)it["viewId"])); string side = (string)it["side"];
                var cb = v.CropBox; var inv = cb.Transform.Inverse;
                double bubble = 9.0 / MM * v.Scale, m = margin / MM * v.Scale; // bubble diameter ~9 mm paper
                double ext = double.NaN;
                foreach (Grid g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)))
                {
                    Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
                    if (c == null) continue;
                    foreach (var end in new[] { DatumEnds.End0, DatumEnds.End1 })
                    {
                        var p = inv.OfPoint(c.GetEndPoint(end == DatumEnds.End0 ? 0 : 1));
                        bool bub = false; try { bub = g.IsBubbleVisibleInView(end, v); } catch { }
                        double add = bub ? bubble : 0;
                        double val = side == "right" ? p.X + add : side == "left" ? p.X - add : side == "top" ? p.Y + add : p.Y - add;
                        if (double.IsNaN(ext)) ext = val;
                        else ext = (side == "right" || side == "top") ? Math.Max(ext, val) : Math.Min(ext, val);
                    }
                }
                if (double.IsNaN(ext)) { rows.Add(new { View = v.Name, Error = "no grids" }); continue; }
                double target = (side == "right" || side == "top") ? ext + m : ext - m;
                double old = side == "right" ? cb.Max.X : side == "left" ? cb.Min.X : side == "top" ? cb.Max.Y : cb.Min.Y;
                bool shrink = (side == "right" || side == "top") ? target < old : target > old;
                logRows.Add(new { ViewId = v.Id.IntegerValue, MinX = cb.Min.X, MinY = cb.Min.Y, MaxX = cb.Max.X, MaxY = cb.Max.Y });
                if (shrink)
                {
                    var mn = cb.Min; var mx = cb.Max;
                    if (side == "right") mx = new XYZ(target, mx.Y, mx.Z); else if (side == "left") mn = new XYZ(target, mn.Y, mn.Z);
                    else if (side == "top") mx = new XYZ(mx.X, target, mx.Z); else mn = new XYZ(mn.X, target, mn.Z);
                    cb.Min = mn; cb.Max = mx; v.CropBox = cb;
                }
                rows.Add(new { View = v.Name, Side = side, Scale = v.Scale, ShrinkPaperMm = Math.Round((shrink ? Math.Abs(old - target) : 0) * MM / v.Scale, 1), Changed = shrink });
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(log, JsonConvert.SerializeObject(logRows, Formatting.Indented)); } else t.RollBack();
        }
        return new { mode, Rows = rows };
    }
}
