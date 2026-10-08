/* mcp-tool
{
  "description": "Change the type of tags (same category), keeping head and leader; reports the tag text before/after. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "items": { "type": "array", "items": { "type": "object" }, "description": "[{tagId, typeName}]" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 60
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// IndependentTag type change, e.g. stair run tags whose type reads the elevations from the wrong level
//    ('From EL +0 …' on an upper stair → the LV1 / LV2 … variant). typeName = a FamilySymbol of the tag's own
//    category. Head position and leader are restored after the change. mode preview (rolled back: Text before /
//    after) | apply (logPath: old type ids) | undo (logPath).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class TagsRetype
{
    class Old { public int Id; public int TypeId; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var done = new List<object>(); var errors = new List<string>(); var log = new List<Old>();
        using (var t = new Transaction(doc, mode == "undo" ? "Undo tags retype" : "Tags retype"))
        {
            t.Start();
            if (mode == "undo")
            {
                foreach (var o in JsonConvert.DeserializeObject<List<Old>>(File.ReadAllText(logPath)))
                {
                    var tg = doc.GetElement(new ElementId(o.Id)) as IndependentTag; if (tg == null) continue;
                    var head = tg.TagHeadPosition; tg.ChangeTypeId(new ElementId(o.TypeId)); tg.TagHeadPosition = head;
                    done.Add(new { Id = o.Id, Restored = o.TypeId });
                }
                t.Commit(); return new { Mode = "undo", Done = done };
            }
            foreach (var it in (JArray)args["items"] ?? new JArray())
            {
                var tg = doc.GetElement(new ElementId((int)it["tagId"])) as IndependentTag;
                if (tg == null) { errors.Add(it["tagId"] + ": not a tag"); continue; }
                string tn = (string)it["typeName"];
                var sym = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Name == tn && s.Category != null && s.Category.Id == tg.Category.Id);
                if (sym == null) { errors.Add(tg.Id.IntegerValue + ": no '" + tn + "' in " + tg.Category.Name); continue; }
                string before = tg.TagText; var head = tg.TagHeadPosition;
                log.Add(new Old { Id = tg.Id.IntegerValue, TypeId = tg.GetTypeId().IntegerValue });
                try
                {
                    tg.ChangeTypeId(sym.Id); doc.Regenerate(); tg.TagHeadPosition = head; doc.Regenerate();
                    done.Add(new { Id = tg.Id.IntegerValue, Type = tn, Before = before, After = tg.TagText });
                }
                catch (Exception e) { errors.Add(tg.Id.IntegerValue + ": " + e.Message); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(log)); }
            else t.RollBack();
        }
        return new { Mode = mode, Done = done, Errors = errors };
    }
}
