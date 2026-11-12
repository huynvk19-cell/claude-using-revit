/* mcp-tool
{
  "description": "Assign view templates to views by name: list the model's templates, preview what each view has against what it should have, apply, undo. Never creates or edits a template. list | preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "items": { "type": "array", "items": { "type": "object" }, "description": "{template, viewIds, scale?}: one item per template name" },
      "mode": { "type": "string", "enum": ["list", "preview", "apply", "undo"] },
      "replace": { "type": "boolean", "description": "also change views that have another template (default false)" },
      "logPath": { "type": "string", "description": "old templates; required for apply and undo" },
      "verbose": { "type": "boolean", "description": "one row per view (default: counts + problems only)" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 180
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Rules VT1-VT10 of drafting-view-template.md. The command only sets View.ViewTemplateId; it never creates,
// duplicates or edits a template (VT3).
// mode list     read-only. Templates [{Name, Type (ViewType of the template), Scale (1:n when the template fixes the
//               scale, else 0), Used (views under it)}]. items given = only the templates they name.
// mode preview  does the whole job and rolls back.     mode apply  commits and writes logPath.
// mode undo     gives back the old template of every view of the log that still has the template the log set.
// item          template  exact view template name. Not in the model = one problem for the item, no view touched.
//               viewIds   the views that must be under it. A dependent view is handled through its parent view
//                         (the parent and its dependents share one template, VT5); the parent is counted once.
//               scale     1:n expected on the sheet register. A view whose scale is not n after the template is set
//                         (the template fixes another scale) is put back and reported (VT8).
// A view that already has the template counts as Ok. A view with no template gets it (Set). A view under another
// template is reported with Why "other template" and left alone unless replace:true (Replaced) (VT7).
// Result: Views, Ok, Set, Replaced, Problems [{Id, Name, Has, Want, Why}]; Why = other template | not valid for this
// view (legends take none; a plan template on a section...) | scale 1:x, register 1:n | template not found | not a
// view | is a view template | the Revit message. verbose adds Rows [{Id, Name, Has, Want, Now}].
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ViewTemplatesApply
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", log = (string)args["logPath"];
        if (mode != "list" && mode != "preview" && mode != "apply" && mode != "undo") return new { Error = "mode: list | preview | apply | undo" };
        if ((mode == "apply" || mode == "undo") && string.IsNullOrEmpty(log)) return new { Error = "logPath is required for " + mode };
        if (mode == "undo") return Undo(doc, log);

        var all = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var templates = new Dictionary<string, View>(StringComparer.Ordinal);
        foreach (var v in all) if (v.IsTemplate) templates[v.Name] = v;
        var items = args["items"] as JArray;

        if (mode == "list")
        {
            var wanted = items == null ? null : new HashSet<string>(items.Select(x => ((string)x["template"] ?? "").Trim()), StringComparer.Ordinal);
            var used = all.Where(v => !v.IsTemplate && v.ViewTemplateId != ElementId.InvalidElementId)
                .GroupBy(v => v.ViewTemplateId.IntegerValue).ToDictionary(g => g.Key, g => g.Count());
            var rows = templates.Values.Where(t => wanted == null || wanted.Contains(t.Name)).OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => new { t.Name, Type = t.ViewType.ToString(), Scale = FixedScale(t), Used = used.ContainsKey(t.Id.IntegerValue) ? used[t.Id.IntegerValue] : 0 }).ToList();
            var notFound = wanted == null ? new List<string>() : wanted.Where(n => n.Length > 0 && !templates.ContainsKey(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            return new { Mode = mode, Templates = rows, NotFound = notFound };
        }

        if (items == null || items.Count == 0) return new { Error = "no items" };
        bool replace = args.Value<bool?>("replace") ?? false, verbose = args.Value<bool?>("verbose") ?? false;
        int ok = 0, set = 0, replaced = 0;
        var problems = new List<object>();
        var detail = new List<object>();
        var logRows = new List<object>();
        var seen = new HashSet<int>();

        using (var t = new Transaction(doc, "View templates"))
        {
            t.Start();
            foreach (var it in items.OfType<JObject>())
            {
                string name = ((string)it["template"] ?? "").Trim();
                int? scale = it.Value<int?>("scale");
                var ids = it["viewIds"] as JArray;
                View tpl;
                if (!templates.TryGetValue(name, out tpl))
                {
                    problems.Add(new { Id = 0, Name = (string)null, Has = (string)null, Want = name, Why = "template not found" });
                    continue;
                }
                if (ids == null) continue;
                foreach (int raw in ids.Select(x => (int)x))
                {
                    var v = doc.GetElement(new ElementId(raw)) as View;
                    if (v == null) { problems.Add(new { Id = raw, Name = (string)null, Has = (string)null, Want = name, Why = "not a view" }); continue; }
                    if (v.IsTemplate) { problems.Add(new { Id = raw, v.Name, Has = (string)null, Want = name, Why = "is a view template" }); continue; }
                    var primary = v.GetPrimaryViewId();
                    if (primary != ElementId.InvalidElementId) v = (View)doc.GetElement(primary);
                    int id = v.Id.IntegerValue;
                    if (!seen.Add(id)) continue;

                    var old = doc.GetElement(v.ViewTemplateId) as View;
                    string has = old == null ? null : old.Name, why = null, now = has;
                    if (old != null && old.Id == tpl.Id)
                    {
                        if (scale != null && scale > 0 && v.Scale != scale.Value) why = "scale 1:" + v.Scale + ", register 1:" + scale.Value;
                        else ok++;
                    }
                    else if (!v.IsValidViewTemplate(tpl.Id)) why = "not valid for this view";
                    else if (old != null && !replace) why = "other template";
                    else
                    {
                        using (var st = new SubTransaction(doc))
                        {
                            st.Start();
                            try
                            {
                                v.ViewTemplateId = tpl.Id;
                                doc.Regenerate();
                                if (scale != null && scale > 0 && v.Scale != scale.Value) why = "scale 1:" + v.Scale + ", register 1:" + scale.Value;
                            }
                            catch (Exception ex) { why = ex.Message; }
                            if (why == null)
                            {
                                st.Commit();
                                now = tpl.Name;
                                if (old == null) set++; else replaced++;
                                logRows.Add(new { ViewId = id, Old = old == null ? -1 : old.Id.IntegerValue, New = tpl.Id.IntegerValue });
                            }
                            else st.RollBack();
                        }
                    }
                    if (why != null) problems.Add(new { Id = id, v.Name, Has = has, Want = name, Why = why });
                    if (verbose) detail.Add(new { Id = id, v.Name, Has = has, Want = name, Now = now });
                }
            }
            if (mode == "apply")
            {
                t.Commit();
                File.WriteAllText(log, JsonConvert.SerializeObject(new { Rows = logRows }, Formatting.Indented));
            }
            else t.RollBack();
        }
        return new { Mode = mode, Views = seen.Count, Ok = ok, Set = set, Replaced = replaced, Problems = problems, Rows = verbose ? detail : null, Log = mode == "apply" ? log : null };
    }

    /// <summary>1:n when the template controls the view scale, else 0.</summary>
    static int FixedScale(View tpl)
    {
        try
        {
            var free = tpl.GetNonControlledTemplateParameterIds();
            var scaleId = new ElementId(BuiltInParameter.VIEW_SCALE);
            if (free.Any(x => x == scaleId)) return 0;
            return tpl.Scale;
        }
        catch { return 0; }
    }

    static object Undo(Document doc, string log)
    {
        if (!File.Exists(log)) return new { Error = "log not found: " + log };
        var rows = JObject.Parse(File.ReadAllText(log))["Rows"] as JArray ?? new JArray();
        int restored = 0;
        var skipped = new List<object>();
        using (var t = new Transaction(doc, "View templates undo"))
        {
            t.Start();
            foreach (var r in rows)
            {
                int id = (int)r["ViewId"];
                var v = doc.GetElement(new ElementId(id)) as View;
                if (v == null) { skipped.Add(new { Id = id, Why = "view not found" }); continue; }
                if (v.ViewTemplateId.IntegerValue != (int)r["New"]) { skipped.Add(new { Id = id, Why = "template changed since" }); continue; }
                int oldId = (int)r["Old"];
                try
                {
                    v.ViewTemplateId = oldId < 0 || doc.GetElement(new ElementId(oldId)) == null ? ElementId.InvalidElementId : new ElementId(oldId);
                    restored++;
                }
                catch (Exception ex) { skipped.Add(new { Id = id, Why = ex.Message }); }
            }
            t.Commit();
        }
        return new { Mode = "undo", Restored = restored, Skipped = skipped };
    }
}
