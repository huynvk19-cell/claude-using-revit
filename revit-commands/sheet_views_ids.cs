/* mcp-tool
{
  "description": "Read-only: for sheets whose number starts with a prefix, list each placed view (not legends/schedules unless includeAll) with view id, viewport id, viewport type, view type, scale, view template (id + name). Optional outPath writes the full list as JSON and returns only counts.",
  "inputSchema": { "type": "object", "properties": { "sheetPrefix": { "type": "string" }, "includeAll": { "type": "boolean" }, "outPath": { "type": "string" } }, "required": ["sheetPrefix"] },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class SheetViewsIds
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string prefix = (string)args["sheetPrefix"];
        bool all = args.Value<bool?>("includeAll") ?? false;
        var rows = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => s.SheetNumber.StartsWith(prefix)).OrderBy(s => s.SheetNumber)
            .SelectMany(s => s.GetAllViewports().Select(id => (Viewport)doc.GetElement(id)).Select(vp =>
            {
                var v = (View)doc.GetElement(vp.ViewId);
                var t = doc.GetElement(v.ViewTemplateId) as View;
                return new
                {
                    Sheet = s.SheetNumber, SheetId = s.Id.IntegerValue, ViewId = v.Id.IntegerValue, VpId = vp.Id.IntegerValue,
                    VpType = doc.GetElement(vp.GetTypeId())?.Name, ViewType = v.ViewType.ToString(), View = v.Name, Scale = v.Scale,
                    TemplateId = t?.Id.IntegerValue ?? -1, Template = t?.Name,
                    Detail = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString()
                };
            }))
            .Where(x => all || (x.ViewType != "Legend" && x.ViewType != "Schedule")).ToList();
        string outPath = (string)args["outPath"];
        if (!string.IsNullOrEmpty(outPath)) { System.IO.File.WriteAllText(outPath, JsonConvert.SerializeObject(rows, Formatting.Indented)); return new { Count = rows.Count, outPath }; }
        return rows;
    }
}
