/* mcp-tool
{
  "description": "Read-only, ONE view: who created / last changed its tags and dims (worksharing), grouped by user and type.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "ids": { "type": "array", "items": { "type": "number" }, "description": "only these elements (any view)" },
      "listIds": { "type": "boolean", "description": "list element ids per group (default false)" }
    }
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only. For the door/window tags and the dimensions of one view (or the given ids), reads
// WorksharingUtils.GetWorksharingTooltipInfo: Creator, LastChangedBy, Owner. Groups by
// "category | type | creator | lastChangedBy" so you can tell your own annotations from a teammate's
// before moving or deleting anything.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class AnnotOwners
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        bool listIds = args["listIds"] != null && (bool)args["listIds"];
        var els = new List<Element>();
        if (args["ids"] != null)
            els = ((JArray)args["ids"]).Select(t => doc.GetElement(new ElementId((int)t))).Where(e => e != null).ToList();
        else
        {
            var vid = new ElementId((int)args["viewId"]);
            var cats = new[] { BuiltInCategory.OST_DoorTags, BuiltInCategory.OST_WindowTags };
            els.AddRange(new FilteredElementCollector(doc, vid).OfClass(typeof(IndependentTag)).Cast<Element>()
                .Where(e => e.Category != null && cats.Any(c => (int)c == e.Category.Id.IntegerValue)));
            els.AddRange(new FilteredElementCollector(doc, vid).OfClass(typeof(Dimension)).Cast<Element>()
                .Where(e => e.OwnerViewId == vid));
        }
        var rows = new List<Tuple<string, int>>();
        foreach (var e in els)
        {
            string cr = "", lc = "", ow = "";
            if (doc.IsWorkshared)
            {
                try { var ti = WorksharingUtils.GetWorksharingTooltipInfo(doc, e.Id); cr = ti.Creator; lc = ti.LastChangedBy; ow = ti.Owner; } catch { }
            }
            var t = doc.GetElement(e.GetTypeId());
            string key = (e.Category != null ? e.Category.Name : e.GetType().Name) + " | " + (t != null ? t.Name : "") + " | created " + cr + " | last " + lc + (string.IsNullOrEmpty(ow) ? "" : " | owner " + ow);
            rows.Add(Tuple.Create(key, e.Id.IntegerValue));
        }
        return rows.GroupBy(r => r.Item1).OrderBy(g => g.Key)
            .Select(g => listIds ? (object)new { Group = g.Key, Count = g.Count(), Ids = g.Select(x => x.Item2).ToList() } : new { Group = g.Key, Count = g.Count() })
            .ToList();
    }
}
