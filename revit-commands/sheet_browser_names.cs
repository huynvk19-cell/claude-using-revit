/* mcp-tool
{
  "description": "Read-only: sheets and their views named exactly as the Project Browser shows them.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sheetNumberPrefix": {
        "type": "string"
      },
      "valuePattern": {
        "type": "string"
      }
    }
  },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: list sheets as the Project Browser names them - the sheet parameter values used for grouping (any text
//    parameter whose value matches valuePattern, default '^[A-Z]\d{2}\. '), 'NUMBER - NAME', and each placed view as
//    '<ViewType>: <name>'. Use to refer to sheets/views exactly as the project does.
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class SheetBrowserNames
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string prefix = args.Value<string>("sheetNumberPrefix") ?? "";
        var rx = new Regex(args.Value<string>("valuePattern") ?? @"^[A-Z]\d{2}\. ");
        string Label(View v)
        {
            switch (v.ViewType)
            {
                case ViewType.FloorPlan: return "Floor Plan";
                case ViewType.CeilingPlan: return "Reflected Ceiling Plan";
                case ViewType.DraftingView: return "Drafting View";
                case ViewType.ThreeD: return "3D View";
                default: return v.ViewType.ToString();
            }
        }
        return new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => s.SheetNumber.StartsWith(prefix))
            .OrderBy(s => s.SheetNumber)
            .Select(s => new
            {
                Browser = s.SheetNumber + " - " + s.Name,
                Groups = s.Parameters.Cast<Parameter>()
                    .Where(p => p.StorageType == StorageType.String && p.AsString() != null && rx.IsMatch(p.AsString()))
                    .Select(p => p.Definition.Name + " = " + p.AsString()).ToList(),
                Views = s.GetAllPlacedViews().Select(id => doc.GetElement(id) as View).Where(v => v != null)
                    .Select(v => Label(v) + ": " + v.Name).OrderBy(x => x).ToList()
            }).ToList();
    }
}
