/* mcp-tool
{
  "description": "Stair tread/riser numbers (NumberSystem) in ONE view: copy the display settings of a source number (Display Rule, Number Size, Justify, Justify Offset, Orientation, Tag Type, Offset from Reference) onto target numbers, and set each target's Reference (left | right | center | leftQuarter | rightQuarter). Use to match the numbers already used on the project's sheets. mode preview (rolled back) | apply (logPath: old values) | undo (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "sourceId": { "type": "number", "description": "a NumberSystem whose settings are copied (any view)" },
      "targets": { "type": "array", "items": { "type": "object", "properties": { "id": { "type": "number" }, "reference": { "type": "string", "enum": ["left", "right", "center", "leftQuarter", "rightQuarter"] } }, "required": ["id"] } },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 120
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairNumbersMatch
{
    static readonly BuiltInParameter[] Copied =
    {
        BuiltInParameter.NUMBER_SYSTEM_DISPLAY_RULE, BuiltInParameter.NUMBER_SYSTEM_TEXT_SIZE, BuiltInParameter.NUMBER_SYSTEM_JUSTIFY,
        BuiltInParameter.NUMBER_SYSTEM_JUSTIFY_OFFSET, BuiltInParameter.NUMBER_SYSTEM_ORIENTATION, BuiltInParameter.NUMBER_SYSTEM_TAG_TYPE,
        BuiltInParameter.NUMBER_SYSTEM_REFERENCE_OFFSET
    };

    class Old { public int Id; public Dictionary<string, string> Values = new Dictionary<string, string>(); }

    static string Get(Parameter p) => p.StorageType == StorageType.Integer ? p.AsInteger().ToString() : p.StorageType == StorageType.Double ? p.AsDouble().ToString("R") : null;
    static void Set(Parameter p, string v) { if (p.StorageType == StorageType.Integer) p.Set(int.Parse(v)); else if (p.StorageType == StorageType.Double) p.Set(double.Parse(v)); }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var olds = JsonConvert.DeserializeObject<List<Old>>(File.ReadAllText(logPath)); var undone = new List<int>();
            using (var t = new Transaction(doc, "Undo stair numbers match"))
            {
                t.Start();
                foreach (var o in olds)
                {
                    var ns = doc.GetElement(new ElementId(o.Id)); if (ns == null) continue;
                    foreach (var kv in o.Values) { var p = ns.get_Parameter((BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), kv.Key)); if (p != null && !p.IsReadOnly) Set(p, kv.Value); }
                    undone.Add(o.Id);
                }
                t.Commit();
            }
            return new { Mode = "undo", Restored = undone };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var src = args["sourceId"] != null ? doc.GetElement(new ElementId((int)args["sourceId"])) as NumberSystem : null;
        var targets = (JArray)args["targets"] ?? new JArray();
        var log = new List<Old>(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Stair numbers match"))
        {
            t.Start();
            foreach (var tj in targets)
            {
                var ns = doc.GetElement(new ElementId((int)tj["id"])) as NumberSystem;
                if (ns == null) { errors.Add(tj["id"] + ": not a stair tread/riser number"); continue; }
                var old = new Old { Id = ns.Id.IntegerValue };
                var keys = Copied.ToList(); keys.Add(BuiltInParameter.NUMBER_SYSTEM_REFERENCE);
                foreach (var bip in keys) { var p = ns.get_Parameter(bip); if (p != null) old.Values[bip.ToString()] = Get(p); }
                try
                {
                    if (src != null)
                        foreach (var bip in Copied)
                        {
                            var ps = src.get_Parameter(bip); var pt = ns.get_Parameter(bip);
                            if (ps != null && pt != null && !pt.IsReadOnly) Set(pt, Get(ps));
                        }
                    string r = (string)tj["reference"];
                    if (r != null)
                    {
                        var opt = (StairsNumberSystemReferenceOption)Enum.Parse(typeof(StairsNumberSystemReferenceOption), r, true);
                        var p = ns.get_Parameter(BuiltInParameter.NUMBER_SYSTEM_REFERENCE); p.Set((int)opt);
                    }
                    log.Add(old);
                }
                catch (Exception e) { errors.Add(ns.Id.IntegerValue + ": " + e.Message); continue; }
                doc.Regenerate();
                var now = new[] { BuiltInParameter.NUMBER_SYSTEM_DISPLAY_RULE, BuiltInParameter.NUMBER_SYSTEM_TEXT_SIZE, BuiltInParameter.NUMBER_SYSTEM_REFERENCE }
                    .Select(b => ns.get_Parameter(b)).Where(p => p != null).Select(p => p.Definition.Name + " = " + p.AsValueString()).ToList();
                done.Add(new { Id = ns.Id.IntegerValue, Run = ns.NumberedElementId.HostElementId.IntegerValue, Now = now });
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(log, Formatting.Indented)); }
            else t.RollBack();
        }
        return new { Mode = mode, Source = src?.Id.IntegerValue, Done = done, Errors = errors };
    }
}
