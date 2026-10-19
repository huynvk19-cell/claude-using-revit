/* mcp-tool
{
  "description": "Mark types for new work: snapshot a batch of views, give new elements '<type><suffix>' types, list, restore to official.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": { "type": "string", "enum": ["snapshot", "mark", "ensure", "list", "restore"] },
      "suffix": { "type": "string", "description": "profile mark suffix, e.g. ' - check' (all but snapshot)" },
      "viewIds": { "type": "array", "items": { "type": "number" }, "description": "snapshot: the batch (1-10 views); list/restore: only these views" },
      "statePath": { "type": "string", "description": "snapshot file: written by snapshot, read by mark" },
      "types": { "type": "array", "items": { "type": "string" }, "description": "ensure: official type names, 'Type' or 'Family : Type'" },
      "color": { "type": "array", "items": { "type": "number" }, "description": "[r,g,b] of new dim/text mark types, default [0,0,255]" },
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "deleteEmpty": { "type": "boolean", "description": "restore apply: delete mark types left with no instance" },
      "logPath": { "type": "string" },
      "verbose": { "type": "boolean" }
    },
    "required": ["action"]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Everything Claude draws gets a "mark type": a copy of its official type named <official name><suffix> (suffix from
//    the project profile, neutral, never "Claude"/"AI"). Back to official later = Select All Instances of the mark
//    type (In Entire Project) → pick the official type, or action restore.
//   snapshot: stores the ids of the elements owned by each batch view (OwnedByView: dims, tags, spots, text, filled
//    regions, detail items…) in statePath. A dependent view's primary view is stored too (dims may land there).
//    1-10 views; MixedKinds lists the ViewTypes when the batch mixes them (batches should be one kind).
//   mark: elements owned by those views that are not in the snapshot = new work. Each gets the mark type of its
//    type (created by duplicating when missing; dims and text notes coloured with `color`). Elements already in a
//    mark type are skipped, so mark can run again after fixes. Elements without a type (detail lines: line style)
//    are only counted (NoType). Tags keep their head position. preview (rolled back) | apply (logPath: id, from, to).
//   ensure: creates the mark types of `types` up front, for commands that take a type name (dimTypeName / typeName:
//    pass the mark type = the profile's "check" dim type). Ambiguous name → error asking for 'Family : Type'.
//   list: every mark type, its instance count, view count and whether its official type exists (verbose: per view).
//   restore: every instance of a mark type (only those owned by viewIds when given) → its official type (same
//    family / class, name without suffix). Only after the user approves. preview | apply (logPath); deleteEmpty also
//    deletes the mark types that no longer have instances.
//   Edits to existing elements (moved tags, 2D grid ends, crops) have no new element to mark: they stay in the logs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class MarkTypes
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string action = (string)args["action"], suffix = (string)args["suffix"], mode = (string)args["mode"] ?? "preview";
        string statePath = (string)args["statePath"], logPath = (string)args["logPath"];
        bool verbose = args.Value<bool?>("verbose") ?? false;
        var viewIds = (args["viewIds"] as JArray)?.Select(x => (int)x).Distinct().ToList() ?? new List<int>();
        var ca = (args["color"] as JArray)?.Select(x => (int)x).ToArray();
        var color = ca != null && ca.Length == 3 ? ca : new[] { 0, 0, 255 };

        if (action == "snapshot")
        {
            if (viewIds.Count == 0 || viewIds.Count > 10) return new { Error = "snapshot needs 1-10 viewIds (one batch)" };
            if (string.IsNullOrEmpty(statePath)) return new { Error = "snapshot needs statePath" };
            var state = new Dictionary<int, List<int>>(); var views = new List<string>(); var kinds = new HashSet<string>();
            foreach (var vid in viewIds)
            {
                var v = doc.GetElement(new ElementId(vid)) as View;
                if (v == null) return new { Error = vid + ": not a view" };
                kinds.Add(v.ViewType.ToString());
                var ids = new List<ElementId> { v.Id };
                var primary = v.GetPrimaryViewId();
                if (primary != ElementId.InvalidElementId) ids.Add(primary);
                foreach (var id in ids)
                {
                    if (state.ContainsKey(id.IntegerValue)) continue;
                    state[id.IntegerValue] = Owned(doc, id).Select(e => e.Id.IntegerValue).ToList();
                }
                views.Add(v.ViewType + " | " + v.Name + (primary != ElementId.InvalidElementId ? " (+primary)" : ""));
            }
            WriteFile(statePath, JsonConvert.SerializeObject(state));
            return new { Action = "snapshot", Views = views, MixedKinds = kinds.Count > 1 ? string.Join(", ", kinds) : null };
        }

        if (string.IsNullOrWhiteSpace(suffix)) return new { Error = "suffix is required (the profile's mark suffix)" };
        if (mode == "apply" && (action == "mark" || action == "restore") && string.IsNullOrEmpty(logPath))
            return new { Error = "apply needs logPath" };

        if (action == "mark")
        {
            if (string.IsNullOrEmpty(statePath) || !File.Exists(statePath)) return new { Error = "mark needs the statePath written by snapshot" };
            var state = JsonConvert.DeserializeObject<Dictionary<int, List<int>>>(File.ReadAllText(statePath));
            var fresh = new List<Element>();
            foreach (var kv in state)
            {
                var old = new HashSet<int>(kv.Value);
                fresh.AddRange(Owned(doc, new ElementId(kv.Key)).Where(e => !old.Contains(e.Id.IntegerValue)));
            }
            var marked = new Dictionary<string, int>(); var noType = new Dictionary<string, int>();
            var created = new List<string>(); var errors = new List<string>(); var log = new JArray(); int already = 0;
            var cache = new Dictionary<int, ElementType>();
            using (var t = new Transaction(doc, "Mark new annotation types"))
            {
                t.Start();
                foreach (var e in fresh)
                {
                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    if (et == null) { Bump(noType, e.Category?.Name ?? e.GetType().Name); continue; }
                    if (et.Name.EndsWith(suffix)) { already++; continue; }
                    try
                    {
                        ElementType mk;
                        if (!cache.TryGetValue(et.Id.IntegerValue, out mk))
                        {
                            mk = Find(doc, et, et.Name + suffix);
                            if (mk == null) { mk = et.Duplicate(et.Name + suffix); Colour(mk, color); created.Add(Label(mk)); }
                            cache[et.Id.IntegerValue] = mk;
                        }
                        SetType(doc, e, mk.Id);
                        Bump(marked, Label(mk));
                        log.Add(new JObject { ["id"] = e.Id.IntegerValue, ["from"] = et.Id.IntegerValue, ["to"] = mk.Id.IntegerValue });
                    }
                    catch (Exception ex) { errors.Add(e.Id.IntegerValue + " " + Label(et) + ": " + ex.Message); }
                }
                if (mode == "apply") { t.Commit(); WriteFile(logPath, log.ToString()); }
                else t.RollBack();
            }
            return new { Action = "mark", Mode = mode, New = fresh.Count, Marked = marked, AlreadyMarked = already, NoType = noType, NewTypes = created, Errors = errors.Take(20) };
        }

        if (action == "ensure")
        {
            var names = (args["types"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            if (names.Count == 0) return new { Error = "ensure needs types" };
            var all = AllTypes(doc);
            var res = new List<object>();
            using (var t = new Transaction(doc, "Create mark types"))
            {
                t.Start();
                foreach (var n in names)
                {
                    string fam = null, tn = n; int i = n.IndexOf(" : ");
                    if (i > 0) { fam = n.Substring(0, i); tn = n.Substring(i + 3); }
                    var c = all.Where(x => x.Name == tn && (fam == null || x.FamilyName == fam)).ToList();
                    if (c.Count > 1)
                    {
                        var lin = c.Where(x => x is DimensionType d && !(x is SpotDimensionType) && d.StyleType == DimensionStyleType.Linear).ToList();
                        if (lin.Count == 1) c = lin;
                    }
                    if (c.Count == 0) { res.Add(new { Type = n, Error = "not found" }); continue; }
                    if (c.Count > 1) { res.Add(new { Type = n, Error = "ambiguous: " + string.Join(" | ", c.Take(5).Select(Label)) + " → use 'Family : Type'" }); continue; }
                    var et = c[0];
                    if (et.Name.EndsWith(suffix)) { res.Add(new { Type = Label(et), Mark = et.Name, Id = et.Id.IntegerValue, Created = false }); continue; }
                    var mk = Find(doc, et, et.Name + suffix); bool made = false;
                    if (mk == null) { mk = et.Duplicate(et.Name + suffix); Colour(mk, color); made = true; }
                    res.Add(new { Type = Label(et), Mark = mk.Name, Id = mk.Id.IntegerValue, Created = made });
                }
                t.Commit();
            }
            return new { Action = "ensure", Types = res };
        }

        if (action != "list" && action != "restore") return new { Error = "unknown action: " + action };

        var marks = AllTypes(doc).Where(x => x.Name.EndsWith(suffix) && x.Name.Length > suffix.Length).ToList();
        var markIds = new HashSet<int>(marks.Select(m => m.Id.IntegerValue));
        var inst = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => markIds.Contains(e.GetTypeId().IntegerValue)).ToList();
        var onlyViews = viewIds.Count > 0 ? new HashSet<int>(viewIds) : null;
        var scoped = inst.Where(e => onlyViews == null || onlyViews.Contains(e.OwnerViewId.IntegerValue)).ToList();
        var byType = scoped.GroupBy(e => e.GetTypeId().IntegerValue).ToDictionary(g => g.Key, g => g.ToList());
        var official = marks.ToDictionary(m => m.Id.IntegerValue, m => Find(doc, m, m.Name.Substring(0, m.Name.Length - suffix.Length)));

        if (action == "list")
        {
            var rows = marks.Where(m => byType.ContainsKey(m.Id.IntegerValue) || onlyViews == null).Select(m =>
            {
                var l = byType.TryGetValue(m.Id.IntegerValue, out var x) ? x : new List<Element>();
                return new
                {
                    Type = Label(m), Count = l.Count, Views = l.Select(e => e.OwnerViewId.IntegerValue).Distinct().Count(),
                    Official = official[m.Id.IntegerValue] == null ? "(not found)" : "ok",
                    ByView = verbose ? l.GroupBy(e => (doc.GetElement(e.OwnerViewId) as View)?.Name ?? "?").Select(g => g.Key + ": " + g.Count()).ToList() : null
                };
            }).OrderByDescending(r => r.Count).ToList();
            return new { Action = "list", Suffix = suffix, Total = scoped.Count, Types = rows };
        }

        // restore
        var restored = new Dictionary<string, int>(); var noOfficial = new HashSet<string>(); var errs = new List<string>();
        var rlog = new JArray(); var deleted = new List<string>();
        using (var t = new Transaction(doc, "Restore official types"))
        {
            t.Start();
            foreach (var kv in byType)
            {
                var m = (ElementType)doc.GetElement(new ElementId(kv.Key));
                var off = official[kv.Key];
                if (off == null) { noOfficial.Add(Label(m) + " (" + kv.Value.Count + ")"); continue; }
                foreach (var e in kv.Value)
                {
                    try
                    {
                        SetType(doc, e, off.Id); Bump(restored, Label(off));
                        rlog.Add(new JObject { ["id"] = e.Id.IntegerValue, ["from"] = m.Id.IntegerValue, ["to"] = off.Id.IntegerValue });
                    }
                    catch (Exception ex) { errs.Add(e.Id.IntegerValue + " " + Label(m) + ": " + ex.Message); }
                }
            }
            if (args.Value<bool?>("deleteEmpty") ?? false)
            {
                var left = new HashSet<int>(new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Select(e => e.GetTypeId().IntegerValue).Where(markIds.Contains));
                foreach (var m in marks.Where(m => !left.Contains(m.Id.IntegerValue)))
                {
                    try { var lb = Label(m); doc.Delete(m.Id); deleted.Add(lb); }
                    catch (Exception ex) { errs.Add("delete " + Label(m) + ": " + ex.Message); }
                }
            }
            if (mode == "apply") { t.Commit(); WriteFile(logPath, rlog.ToString()); }
            else t.RollBack();
        }
        return new { Action = "restore", Mode = mode, Restored = restored, NoOfficial = noOfficial, DeletedTypes = deleted, Errors = errs.Take(20) };
    }

    static IList<Element> Owned(Document doc, ElementId viewId) =>
        new FilteredElementCollector(doc).WherePasses(new ElementOwnerViewFilter(viewId)).WhereElementIsNotElementType().ToElements();

    static List<ElementType> AllTypes(Document doc) =>
        new FilteredElementCollector(doc).WhereElementIsElementType().OfType<ElementType>().ToList();

    // the type named `name` in the same family (family symbols) or the same class + family name (system types)
    static ElementType Find(Document doc, ElementType et, string name)
    {
        if (et is FamilySymbol fs)
            return fs.Family.GetFamilySymbolIds().Select(id => doc.GetElement(id) as ElementType).FirstOrDefault(s => s != null && s.Name == name);
        return AllTypes(doc).FirstOrDefault(x => x.GetType() == et.GetType() && x.FamilyName == et.FamilyName && x.Name == name);
    }

    static void SetType(Document doc, Element e, ElementId typeId)
    {
        if (e is IndependentTag tg)
        {
            var head = tg.TagHeadPosition; tg.ChangeTypeId(typeId); doc.Regenerate(); tg.TagHeadPosition = head;
        }
        else e.ChangeTypeId(typeId);
    }

    static void Colour(ElementType t, int[] c)
    {
        if (!(t is DimensionType || t is TextNoteType)) return;
        var p = t.get_Parameter(BuiltInParameter.LINE_COLOR);
        if (p != null && !p.IsReadOnly) p.Set(c[0] + c[1] * 256 + c[2] * 65536);
    }

    static string Label(ElementType t) => t.FamilyName + " : " + t.Name;

    static void Bump(Dictionary<string, int> d, string k) { d.TryGetValue(k, out var n); d[k] = n + 1; }

    // creates the folder first: a missing review folder used to throw after the commit, losing the log
    static void WriteFile(string path, string text)
    {
        if (string.IsNullOrEmpty(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, text);
    }
}
