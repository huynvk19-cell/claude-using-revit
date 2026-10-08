/* mcp-tool
{
  "description": "ONE view: copy view-specific annotations (generic annotations, tags, text) by dx/dz mm in the view frame. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" }, "description": "[{ids:[..], dx, dz}]" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["viewId", "mode"]
  },
  "timeoutSeconds": 60
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Copies annotation elements owned by ONE view (e.g. the F../C.. finish marks of one landing to another landing of a
//    stair section) by a translation along the view's Right (dx) and Up (dz), mm. Parameters (codes) are copied as
//    they are: check the copied text. Tags copy only if their host is still under the new position. mode preview
//    (rolled back) | apply (logPath: new ids) | undo (logPath: deletes them).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class AnnotCopyInView
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); var gone = new List<int>();
            using (var t = new Transaction(doc, "Undo annot copy"))
            {
                t.Start();
                foreach (var id in ids) if (doc.GetElement(new ElementId(id)) != null) { doc.Delete(new ElementId(id)); gone.Add(id); }
                t.Commit();
            }
            return new { Mode = "undo", Deleted = gone };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var created = new List<int>(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Annot copy in view"))
        {
            t.Start();
            foreach (var it in (JArray)args["items"] ?? new JArray())
            {
                var ids = ((JArray)it["ids"]).Select(x => new ElementId((int)x)).Where(id => doc.GetElement(id) != null && doc.GetElement(id).OwnerViewId == v.Id).ToList();
                if (ids.Count == 0) { errors.Add("no element of this view in " + it["ids"]); continue; }
                var tr = v.RightDirection * (((double?)it["dx"] ?? 0) / MM) + v.UpDirection * (((double?)it["dz"] ?? 0) / MM);
                try
                {
                    var news = ElementTransformUtils.CopyElements(doc, ids, tr);
                    foreach (var n in news)
                    {
                        created.Add(n.IntegerValue); var e = doc.GetElement(n);
                        var txt = e.Parameters.Cast<Parameter>().Where(p => p.StorageType == StorageType.String && !string.IsNullOrEmpty(p.AsString()) && p.AsString().Length <= 8).Select(p => p.Definition.Name + "=" + p.AsString()).Take(3).ToList();
                        done.Add(new { Id = n.IntegerValue, Category = e.Category?.Name, Texts = txt });
                    }
                }
                catch (Exception e) { errors.Add(string.Join(",", ids.Select(i => i.IntegerValue)) + ": " + e.Message); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, Done = done, Errors = errors };
    }
}
