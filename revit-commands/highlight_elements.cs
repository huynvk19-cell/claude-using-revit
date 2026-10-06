/* mcp-tool
{
  "description": "Colour elements in given views with an Override Graphics in View (projection + cut lines, and a solid surface/cut fill for model elements), saving the previous overrides to a JSON log; mode 'undo' restores them from the log.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["apply", "undo"] },
      "items": { "type": "array", "items": { "type": "object" }, "description": "apply: [{ viewId, ids: [..], label }]" },
      "color": { "type": "array", "items": { "type": "number" }, "description": "[r,g,b], default [255,0,0]" },
      "lineWeight": { "type": "number", "description": "projection/cut line weight, default 6" },
      "fill": { "type": "boolean", "description": "solid fill for model elements, default true" },
      "logPath": { "type": "string" }
    },
    "required": ["mode", "logPath"]
  },
  "timeoutSeconds": 120
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class HighlightElements
{
    static JObject Save(OverrideGraphicSettings o)
    {
        Func<Color, JToken> C = c => c != null && c.IsValid ? new JArray(c.Red, c.Green, c.Blue) : null;
        return new JObject
        {
            ["plc"] = C(o.ProjectionLineColor), ["plw"] = o.ProjectionLineWeight,
            ["clc"] = C(o.CutLineColor), ["clw"] = o.CutLineWeight,
            ["sfp"] = o.SurfaceForegroundPatternId.IntegerValue, ["sfc"] = C(o.SurfaceForegroundPatternColor), ["sfv"] = o.IsSurfaceForegroundPatternVisible,
            ["cfp"] = o.CutForegroundPatternId.IntegerValue, ["cfc"] = C(o.CutForegroundPatternColor), ["cfv"] = o.IsCutForegroundPatternVisible,
            ["ht"] = o.Halftone, ["tr"] = o.Transparency
        };
    }
    static OverrideGraphicSettings Load(JObject j)
    {
        Func<JToken, Color> C = t => t == null || t.Type == JTokenType.Null ? Color.InvalidColorValue : new Color((byte)(int)t[0], (byte)(int)t[1], (byte)(int)t[2]);
        var o = new OverrideGraphicSettings();
        o.SetProjectionLineColor(C(j["plc"])); o.SetProjectionLineWeight((int)j["plw"]);
        o.SetCutLineColor(C(j["clc"])); o.SetCutLineWeight((int)j["clw"]);
        o.SetSurfaceForegroundPatternId(new ElementId((int)j["sfp"])); o.SetSurfaceForegroundPatternColor(C(j["sfc"])); o.SetSurfaceForegroundPatternVisible((bool)j["sfv"]);
        o.SetCutForegroundPatternId(new ElementId((int)j["cfp"])); o.SetCutForegroundPatternColor(C(j["cfc"])); o.SetCutForegroundPatternVisible((bool)j["cfv"]);
        o.SetHalftone((bool)j["ht"]); o.SetSurfaceTransparency((int)j["tr"]);
        return o;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], logPath = (string)args["logPath"];
        var res = new List<object>();
        if (mode == "undo")
        {
            var log = JArray.Parse(File.ReadAllText(logPath));
            using (var t = new Transaction(doc, "Remove highlight overrides"))
            {
                t.Start();
                foreach (JObject r in log)
                {
                    var v = (View)doc.GetElement(new ElementId((int)r["viewId"]));
                    var id = new ElementId((int)r["id"]);
                    if (v == null || doc.GetElement(id) == null) { res.Add(new { Id = id.IntegerValue, Skipped = "missing" }); continue; }
                    v.SetElementOverrides(id, Load((JObject)r["prev"]));
                    res.Add(new { Id = id.IntegerValue, View = v.Name, Restored = true });
                }
                t.Commit();
            }
            return res;
        }

        var col = (args["color"] as JArray)?.Select(x => (int)x).ToArray() ?? new[] { 255, 0, 0 };
        var color = new Color((byte)col[0], (byte)col[1], (byte)col[2]);
        int lw = args.Value<int?>("lineWeight") ?? 6;
        bool fill = args.Value<bool?>("fill") ?? true;
        var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting);
        var entries = File.Exists(logPath) ? JArray.Parse(File.ReadAllText(logPath)) : new JArray();
        using (var t = new Transaction(doc, "Highlight review items"))
        {
            t.Start();
            foreach (JObject it in (JArray)args["items"])
            {
                View v0 = it["viewId"] != null ? (View)doc.GetElement(new ElementId((int)it["viewId"])) : null;
                foreach (var tok in (JArray)it["ids"])
                {
                    var id = new ElementId((int)tok);
                    var e = doc.GetElement(id);
                    if (e == null) { res.Add(new { Id = id.IntegerValue, Error = "not found" }); continue; }
                    var v = v0 ?? (View)doc.GetElement(e.OwnerViewId);
                    var prev = v.GetElementOverrides(id);
                    if (!entries.Any(x => (int)x["id"] == id.IntegerValue && (int)x["viewId"] == v.Id.IntegerValue))
                        entries.Add(new JObject { ["viewId"] = v.Id.IntegerValue, ["id"] = id.IntegerValue, ["label"] = (string)it["label"], ["prev"] = Save(prev) });
                    var o = new OverrideGraphicSettings(prev);
                    o.SetProjectionLineColor(color); o.SetProjectionLineWeight(lw);
                    o.SetCutLineColor(color); o.SetCutLineWeight(lw);
                    o.SetHalftone(false);
                    bool model = e.Category != null && e.Category.CategoryType == CategoryType.Model;
                    if (fill && model && solid != null)
                    {
                        o.SetSurfaceForegroundPatternId(solid.Id); o.SetSurfaceForegroundPatternColor(color); o.SetSurfaceForegroundPatternVisible(true);
                        o.SetCutForegroundPatternId(solid.Id); o.SetCutForegroundPatternColor(color); o.SetCutForegroundPatternVisible(true);
                    }
                    v.SetElementOverrides(id, o);
                    res.Add(new { Id = id.IntegerValue, Cat = e.Category?.Name, View = v.Name, Label = (string)it["label"] });
                }
            }
            t.Commit();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(logPath));
        File.WriteAllText(logPath, entries.ToString());
        return res;
    }
}
