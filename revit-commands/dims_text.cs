/* mcp-tool
{
  "description": "Set the Prefix / Suffix (and optionally Above / Below text) of dimension segments in ONE view, keeping the measured value live (never Replace with text). Each item names a dim and the segment: segmentIndex (0-based), or valueMm (the segment whose value matches within 1 mm), or neither for a single-segment dim. Use for the stair core rules: suffix ' CLEAR' on clear widths, prefix '280mm x 14T = ' on run lengths. mode preview (reports before -> after, changes nothing) | apply (logPath) | undo (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "dimId": { "type": "number" },
            "segmentIndex": { "type": "number" },
            "valueMm": { "type": "number" },
            "prefix": { "type": "string" },
            "suffix": { "type": "string" },
            "above": { "type": "string" },
            "below": { "type": "string" }
          },
          "required": ["dimId"]
        }
      },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 60,
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

public static class DimsText
{
    const double MM = 304.8;
    class Txt { public string Prefix, Suffix, Above, Below; }
    class Rec { public int DimId; public int Seg; public double Value; public Txt Before, After; }

    static Txt Get(Dimension d, int seg)
    {
        if (seg < 0) return new Txt { Prefix = d.Prefix, Suffix = d.Suffix, Above = d.Above, Below = d.Below };
        var s = d.Segments.get_Item(seg);
        return new Txt { Prefix = s.Prefix, Suffix = s.Suffix, Above = s.Above, Below = s.Below };
    }
    static void Set(Dimension d, int seg, Txt t)
    {
        if (seg < 0) { d.Prefix = t.Prefix; d.Suffix = t.Suffix; d.Above = t.Above; d.Below = t.Below; return; }
        var s = d.Segments.get_Item(seg);
        s.Prefix = t.Prefix; s.Suffix = t.Suffix; s.Above = t.Above; s.Below = t.Below;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], logPath = (string)args["logPath"];
        if (mode != "preview" && string.IsNullOrEmpty(logPath)) return new { Error = "logPath is required for apply / undo" };
        if (mode == "undo")
        {
            var recs = JsonConvert.DeserializeObject<List<Rec>>(File.ReadAllText(logPath)); var done = new List<string>();
            using (var t = new Transaction(doc, "Dim text undo"))
            {
                t.Start();
                foreach (var r in recs)
                {
                    var d = doc.GetElement(new ElementId(r.DimId)) as Dimension; if (d == null) { done.Add(r.DimId + ": gone"); continue; }
                    Set(d, r.Seg, r.Before); done.Add(r.DimId + "/" + r.Seg + " restored");
                }
                t.Commit();
            }
            return new { Undone = done };
        }
        var v = (View)doc.GetElement(new ElementId(args.Value<int>("viewId")));
        var out1 = new List<Rec>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Dim text"))
        {
            t.Start();
            foreach (JObject it in (JArray)args["items"])
            {
                var d = doc.GetElement(new ElementId((int)it["dimId"])) as Dimension;
                if (d == null) { errors.Add(it["dimId"] + ": not a dimension"); continue; }
                int seg = -1;
                if (d.NumberOfSegments > 1)
                {
                    if (it["segmentIndex"] != null) seg = (int)it["segmentIndex"];
                    else if (it["valueMm"] != null)
                    {
                        double want = (double)it["valueMm"]; var hits = new List<int>();
                        for (int i = 0; i < d.NumberOfSegments; i++) if (Math.Abs((d.Segments.get_Item(i).Value ?? 0) * MM - want) <= 1) hits.Add(i);
                        if (hits.Count != 1) { errors.Add(d.Id.IntegerValue + ": " + hits.Count + " segments measure " + want + " mm, give segmentIndex"); continue; }
                        seg = hits[0];
                    }
                    else { errors.Add(d.Id.IntegerValue + ": multi-segment dim, give segmentIndex or valueMm"); continue; }
                    if (seg < 0 || seg >= d.NumberOfSegments) { errors.Add(d.Id.IntegerValue + ": segmentIndex out of range"); continue; }
                }
                var before = Get(d, seg);
                var after = new Txt
                {
                    Prefix = it["prefix"] != null ? (string)it["prefix"] : before.Prefix, Suffix = it["suffix"] != null ? (string)it["suffix"] : before.Suffix,
                    Above = it["above"] != null ? (string)it["above"] : before.Above, Below = it["below"] != null ? (string)it["below"] : before.Below
                };
                try { Set(d, seg, after); } catch (Exception e) { errors.Add(d.Id.IntegerValue + ": " + e.Message); continue; }
                double val = (seg < 0 ? d.Value : d.Segments.get_Item(seg).Value) ?? 0;
                out1.Add(new Rec { DimId = d.Id.IntegerValue, Seg = seg, Value = Math.Round(val * MM), Before = before, After = after });
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(out1, Formatting.Indented)); }
            else t.RollBack();
        }
        return new
        {
            View = v.Name, Mode = mode, Errors = errors,
            Changed = out1.Select(r => new { r.DimId, Segment = r.Seg, r.Value, Before = (r.Before.Prefix ?? "") + r.Value + (r.Before.Suffix ?? ""), After = (r.After.Prefix ?? "") + r.Value + (r.After.Suffix ?? "") })
        };
    }
}
