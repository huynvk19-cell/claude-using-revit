/* mcp-tool
{
  "description": "Delete elements by id (one transaction). Reports what Revit deleted with them.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "elementId": { "type": "number", "description": "One element id" },
      "ids": { "type": "array", "items": { "type": "number" }, "description": "Several element ids" },
      "dryRun": { "type": "boolean", "description": "Only report what would be deleted" }
    }
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Deletes elementId and/or ids in one transaction "AI Culi: delete". Missing ids are listed, not an error.
// Revit also deletes what depends on an element (tags of a deleted door, dims of a deleted grid...): those come back
// as AlsoDeleted (count + first ids). dryRun: the same transaction is rolled back, so the report shows what WOULD go.
// Element types, views and sheets are deleted too if their id is given: check the id first.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DeleteElement
{
    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        var want = new List<int>();
        if (args["elementId"] != null) want.Add((int)args["elementId"]);
        if (args["ids"] is JArray arr) want.AddRange(arr.Select(t => (int)t));
        want = want.Distinct().ToList();
        if (want.Count == 0) return new { error = "pass elementId or ids" };
        bool dry = args.Value<bool?>("dryRun") ?? false;

        var ids = new List<ElementId>();
        var missing = new List<int>();
        foreach (int i in want)
        {
            var id = new ElementId(i);
            if (doc.GetElement(id) != null) ids.Add(id); else missing.Add(i);
        }
        if (ids.Count == 0) return new { Deleted = 0, Missing = missing };

        ICollection<ElementId> gone;
        using (var t = new Transaction(doc, "AI Culi: delete"))
        {
            t.Start();
            gone = doc.Delete(ids);
            if (dry) t.RollBack(); else t.Commit();
        }
        var asked = new HashSet<int>(ids.Select(x => x.IntegerValue));
        var also = gone.Select(x => x.IntegerValue).Where(x => !asked.Contains(x)).ToList();
        return new
        {
            DryRun = dry,
            Deleted = ids.Count,
            AlsoDeleted = also.Count,
            AlsoDeletedIds = also.Take(30).ToList(),
            Missing = missing,
        };
    }
}
