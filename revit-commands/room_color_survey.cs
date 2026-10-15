/* mcp-tool
{
  "description": "Read-only: per plan view rooms / tagged rooms, room Color Scheme, whether the template controls it and can take one; room Color Schemes of the model.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "verbose": { "type": "boolean", "description": "list untagged rooms and scheme entries" }
    },
    "required": ["viewIds"]
  },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
// ---- Details ----
// Rooms: FilteredElementCollector(doc, view) OfCategory(OST_Rooms), Area > 0 (host rooms only). Tagged: a RoomTag in the
// view with TaggedLocalRoomId. Scheme: View.GetColorFillSchemeId(OST_Rooms) of the view; template control: the template's
// "Color Scheme" parameter is in GetTemplateParameterIds and not in GetNonControlledTemplateParameterIds.
// canApply: View.CanApplyColorFillScheme on the template (or the view) with the first room scheme; false on ceiling plans.
// Schemes: every ColorFillScheme of OST_Rooms with its parameter, entry count, creator / last changed by (worksharing).
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class RoomColorSurvey
{
    static string Who(Document doc, ElementId id)
    {
        try { var t = WorksharingUtils.GetWorksharingTooltipInfo(doc, id); return t.Creator + " / " + t.LastChangedBy; }
        catch { return ""; }
    }

    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        bool verbose = args["verbose"] != null && (bool)args["verbose"];
        var roomCat = new ElementId(BuiltInCategory.OST_Rooms);
        var roomSchemes = new FilteredElementCollector(doc).OfClass(typeof(ColorFillScheme)).Cast<ColorFillScheme>()
            .Where(s => s.CategoryId == roomCat).ToList();
        var views = new List<object>();
        foreach (var jt in (JArray)args["viewIds"])
        {
            var v = doc.GetElement(new ElementId((int)jt)) as View;
            if (v == null) { views.Add(new { id = (int)jt, error = "not a view" }); continue; }
            var rooms = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().Cast<SpatialElement>().Where(r => r.Area > 0).ToList();
            var tagged = new HashSet<int>(new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType().OfType<RoomTag>()
                .Where(t => t.TaggedLocalRoomId != ElementId.InvalidElementId).Select(t => t.TaggedLocalRoomId.IntegerValue));
            var tpl = v.ViewTemplateId != ElementId.InvalidElementId ? doc.GetElement(v.ViewTemplateId) as View : null;
            var owner = tpl ?? v;
            ElementId sid = ElementId.InvalidElementId;
            try { sid = owner.GetColorFillSchemeId(roomCat); } catch { }
            bool controlled = false;
            if (tpl != null)
            {
                var nonCtl = new HashSet<int>(tpl.GetNonControlledTemplateParameterIds().Select(i => i.IntegerValue));
                controlled = tpl.GetTemplateParameterIds().Any(pid => !nonCtl.Contains(pid.IntegerValue)
                    && tpl.Parameters.Cast<Parameter>().Any(p => p.Id == pid && p.Definition.Name == "Color Scheme"));
            }
            bool canApply = false;
            try { canApply = roomSchemes.Count > 0 && owner.CanApplyColorFillScheme(roomCat, roomSchemes[0].Id); } catch { }
            var untagged = rooms.Where(r => !tagged.Contains(r.Id.IntegerValue)).ToList();
            views.Add(new
            {
                id = v.Id.IntegerValue, name = v.Name, type = v.ViewType.ToString(), rooms = rooms.Count, untagged = untagged.Count,
                scheme = sid != ElementId.InvalidElementId ? doc.GetElement(sid)?.Name : null,
                template = tpl?.Name, templateControlsScheme = controlled, canApply,
                untaggedRooms = verbose ? untagged.Select(r => r.Id.IntegerValue + " " + r.Number + " " + r.Name).ToList() : null
            });
        }
        var schemes = roomSchemes.Select(s =>
        {
            string param = "";
            try { var p = s.ParameterDefinition; param = p.IntegerValue < 0 ? ((BuiltInParameter)p.IntegerValue).ToString() : (doc.GetElement(p)?.Name ?? p.IntegerValue.ToString()); } catch { }
            var ents = s.GetEntries();
            return new
            {
                id = s.Id.IntegerValue, name = s.Name, param, entries = ents.Count, who = Who(doc, s.Id),
                sample = verbose ? ents.Select(e => e.GetStringValue() + " " + e.Color.Red + "," + e.Color.Green + "," + e.Color.Blue + (e.IsVisible ? "" : " hidden")).ToList() : null
            };
        }).ToList();
        return new { views, schemes };
    }
}
