/* mcp-tool
{
  "description": "Plans: fill tagged rooms via a room Color Scheme on the view template; rooms with no tag in a view stay unfilled (element override). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "templateId": { "type": "number", "description": "view template that gets the room Color Scheme" },
      "sourceSchemeId": { "type": "number", "description": "Rooms Color Scheme to copy when schemeName does not exist" },
      "schemeName": { "type": "string", "description": "scheme to create / update (neutral name)" },
      "defaultRgb": { "type": "array", "items": { "type": "number" }, "description": "colour of every value, default [200,230,240]" },
      "valueRgb": { "type": "object", "description": "parameter value -> [r,g,b], e.g. {\"CIRCULATION\":[247,242,187]}" },
      "viewIds": { "type": "array", "items": { "type": "number" }, "description": "views where untagged rooms stay unfilled" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "verbose": { "type": "boolean", "description": "list room names per view" },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details ----
// Scheme: copy of sourceSchemeId (keeps its parameter, e.g. Department) named schemeName, or the existing one of that name.
// Every value of that parameter among the model's rooms gets an entry: colour = valueRgb[value] or defaultRgb, solid fill,
// visible. The scheme is set on the template with View.SetColorFillSchemeId(OST_Rooms) (ceiling plans cannot take one:
// View.CanApplyColorFillScheme = false).
// Untagged = room seen in the view (Area > 0) with no RoomTag in that view (TaggedLocalRoomId). It gets
// Override Graphics in View > By Element > Surface Transparency 100: no fill, the room (and its reference X) stays.
// Never Hide in View: the room disappears with its X. A room that has a tag now and still carries an override with
// transparency 100 gets the override cleared, so re-running apply after tagging refreshes the fill.
// Undo (logPath): clears the overrides it set, restores the template's previous scheme, deletes the scheme if it made it.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class RoomColorApply
{
    static Color Rgb(JToken t) { var a = (JArray)t; return new Color((byte)(int)a[0], (byte)(int)a[1], (byte)(int)a[2]); }

    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview";
        string logPath = (string)args["logPath"];
        bool verbose = args["verbose"] != null && (bool)args["verbose"];
        var roomCat = new ElementId(BuiltInCategory.OST_Rooms);

        if (mode == "undo")
        {
            var ulog = JObject.Parse(File.ReadAllText(logPath));
            using (var t = new Transaction(doc, "Room fill undo"))
            {
                t.Start();
                foreach (JObject v in (JArray)ulog["views"])
                {
                    var view = doc.GetElement(new ElementId((int)v["id"])) as View;
                    if (view == null) continue;
                    foreach (var x in (JArray)v["unfilled"])
                    {
                        var id = new ElementId((int)x);
                        if (doc.GetElement(id) != null) view.SetElementOverrides(id, new OverrideGraphicSettings());
                    }
                }
                var tpl = doc.GetElement(new ElementId((int)ulog["templateId"])) as View;
                if (tpl != null) tpl.SetColorFillSchemeId(roomCat, new ElementId((int)ulog["prevSchemeId"]));
                if ((bool)ulog["schemeCreated"]) doc.Delete(new ElementId((int)ulog["schemeId"]));
                t.Commit();
            }
            return new { undone = true };
        }

        var tplView = args["templateId"] != null ? doc.GetElement(new ElementId((int)args["templateId"])) as View : null;
        string schemeName = (string)args["schemeName"];
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ColorFillScheme)).Cast<ColorFillScheme>()
            .FirstOrDefault(s => s.CategoryId == roomCat && s.Name == schemeName);
        var source = args["sourceSchemeId"] != null ? doc.GetElement(new ElementId((int)args["sourceSchemeId"])) as ColorFillScheme : null;
        var paramScheme = existing ?? source;
        if (paramScheme == null) return new { error = "no scheme named " + schemeName + " and no sourceSchemeId" };
        if (tplView != null && !tplView.CanApplyColorFillScheme(roomCat, paramScheme.Id)) return new { error = "template cannot take a room colour scheme: " + tplView.Name };
        var pid = paramScheme.ParameterDefinition;
        Func<Element, string> valueOf = r =>
        {
            Parameter p = pid.IntegerValue < 0 ? r.get_Parameter((BuiltInParameter)pid.IntegerValue)
                : r.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Id == pid);
            return p == null ? null : (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString());
        };
        var values = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
            .Select(valueOf).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();
        var defaultRgb = args["defaultRgb"] != null ? Rgb(args["defaultRgb"]) : new Color(200, 230, 240);
        var valueRgb = (JObject)args["valueRgb"] ?? new JObject();
        Func<string, Color> colorOf = s => valueRgb[s] != null ? Rgb(valueRgb[s]) : defaultRgb;

        var plan = new List<JObject>();
        foreach (var jt in (JArray)args["viewIds"] ?? new JArray())
        {
            var v = doc.GetElement(new ElementId((int)jt)) as View;
            if (v == null) continue;
            var rooms = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().Cast<SpatialElement>().Where(r => r.Area > 0).ToList();
            var tagged = new HashSet<int>(new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType().OfType<RoomTag>()
                .Where(t => t.TaggedLocalRoomId != ElementId.InvalidElementId).Select(t => t.TaggedLocalRoomId.IntegerValue));
            var untagged = rooms.Where(r => !tagged.Contains(r.Id.IntegerValue)).ToList();
            var refill = rooms.Where(r => tagged.Contains(r.Id.IntegerValue) && v.GetElementOverrides(r.Id).Transparency == 100).ToList();
            plan.Add(new JObject
            {
                ["id"] = v.Id.IntegerValue, ["name"] = v.Name, ["rooms"] = rooms.Count, ["tagged"] = rooms.Count - untagged.Count,
                ["unfilled"] = new JArray(untagged.Select(r => r.Id.IntegerValue)),
                ["refilled"] = new JArray(refill.Select(r => r.Id.IntegerValue)),
                ["unfilledNames"] = new JArray(untagged.Select(r => r.Number + " " + r.Name))
            });
        }
        int prevScheme = tplView != null ? tplView.GetColorFillSchemeId(roomCat).IntegerValue : -1;
        var views = plan.Select(p => verbose
            ? (object)new { view = (string)p["name"], rooms = (int)p["rooms"], tagged = (int)p["tagged"], unfilled = p["unfilledNames"] }
            : new { view = (string)p["name"], rooms = (int)p["rooms"], tagged = (int)p["tagged"], unfilled = ((JArray)p["unfilled"]).Count, refilled = ((JArray)p["refilled"]).Count }).ToList();
        var summary = new { template = tplView?.Name, scheme = schemeName, schemeExists = existing != null, values = values.Count, views };
        if (mode != "apply") return summary;

        var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting);
        bool created = false;
        ColorFillScheme scheme = existing;
        using (var t = new Transaction(doc, "Room fill"))
        {
            t.Start();
            if (scheme == null) { scheme = (ColorFillScheme)doc.GetElement(source.Duplicate(schemeName)); created = true; }
            var entries = scheme.GetEntries();
            var have = new HashSet<string>(entries.Select(e => e.GetStringValue()));
            foreach (var e in entries)
            {
                e.Color = colorOf(e.GetStringValue());
                if (solid != null) e.FillPatternId = solid.Id;
                e.IsVisible = true;
                if (scheme.CanUpdateEntry(e)) scheme.UpdateEntry(e);
            }
            foreach (var d in values.Where(d => !have.Contains(d)))
            {
                var e = new ColorFillSchemeEntry(StorageType.String);
                e.SetStringValue(d);
                e.Color = colorOf(d);
                if (solid != null) e.FillPatternId = solid.Id;
                e.IsVisible = true;
                scheme.AddEntry(e);
            }
            if (tplView != null) tplView.SetColorFillSchemeId(roomCat, scheme.Id);
            var ogs = new OverrideGraphicSettings(); ogs.SetSurfaceTransparency(100);
            foreach (var p in plan)
            {
                var v = (View)doc.GetElement(new ElementId((int)p["id"]));
                foreach (var x in (JArray)p["unfilled"]) v.SetElementOverrides(new ElementId((int)x), ogs);
                foreach (var x in (JArray)p["refilled"]) v.SetElementOverrides(new ElementId((int)x), new OverrideGraphicSettings());
            }
            t.Commit();
        }
        if (!string.IsNullOrEmpty(logPath))
        {
            var log = new JObject
            {
                ["time"] = DateTime.Now.ToString("s"), ["templateId"] = tplView != null ? tplView.Id.IntegerValue : -1,
                ["prevSchemeId"] = prevScheme, ["schemeId"] = scheme.Id.IntegerValue, ["schemeCreated"] = created, ["views"] = new JArray(plan)
            };
            File.WriteAllText(logPath, log.ToString(Formatting.Indented));
        }
        return new { applied = true, schemeId = scheme.Id.IntegerValue, created, logPath, summary };
    }
}
