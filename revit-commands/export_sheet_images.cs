/* mcp-tool
{
  "description": "Export sheets (by number) and/or views (viewIds) as PNG images; returns the file paths.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sheetNumbers": {
        "type": "array",
        "items": {
          "type": "string"
        }
      },
      "folder": {
        "type": "string",
        "description": "Output folder (created if missing)."
      },
      "pixelWidth": {
        "type": "number",
        "description": "Image width in px. Default 6000."
      },
      "viewIds": {
        "type": "array",
        "items": {
          "type": "number"
        },
        "description": "also/instead export these views (view-<id>.png); the view comes to the front"
      }
    },
    "required": [
      "sheetNumbers",
      "folder"
    ]
  },
  "timeoutSeconds": 900,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Export sheets (by sheet number) of the active document as PNG images (current state, no printing). Returns the file
//    paths.
// Parameters:
//   viewIds: also (or instead: sheetNumbers []) export these views; files named view-<id>.png. Use for checking:
//    the exported view comes to the front, a sheet export leaves the sheet in front. The view that was active
//    before goes back to the front afterwards.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ExportSheetImages
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string folder = args.Value<string>("folder");
        Directory.CreateDirectory(folder);
        int px = args.Value<int?>("pixelWidth") ?? 6000;
        var wanted = args["sheetNumbers"].Values<string>().ToList();
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<View>()
            .Where(s => wanted.Contains(((ViewSheet)s).SheetNumber)).ToList();
        // viewIds: export views instead of sheets (Revit brings the exported view to the front, so the working view stays in sight)
        if (args["viewIds"] is JArray vids)
            foreach (var vid in vids) { var vv = doc.GetElement(new ElementId((int)vid)) as View; if (vv != null) sheets.Add(vv); }

        var uidoc = app.ActiveUIDocument;
        var working = uidoc.ActiveView; // ExportImage activates the sheet in the UI: go back to this view afterwards
        var files = new List<object>();
        foreach (var s in sheets)
        {
            string key = s is ViewSheet vs ? vs.SheetNumber : "view-" + s.Id.IntegerValue;
            string prefix = Path.Combine(folder, key);
            var before = new HashSet<string>(Directory.GetFiles(folder));
            var opt = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = prefix,
                FitDirection = FitDirectionType.Horizontal,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = px,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150
            };
            opt.SetViewsAndSheets(new List<ElementId> { s.Id });
            doc.ExportImage(opt);
            var created = Directory.GetFiles(folder).Where(f => !before.Contains(f)).ToList();
            // Normalise the name Revit generates to <SheetNumber>.png
            string target = prefix + ".png";
            var src = created.FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            if (src != null && src != target) { if (File.Exists(target)) File.Delete(target); File.Move(src, target); }
            files.Add(new { Sheet = key, File = File.Exists(target) ? target : src });
        }
        string back = null;
        // ActiveView still reports the working view while the sheet window is in front, so RequestViewChange alone does nothing:
        // close the exported sheets' windows (not the working view), then bring the working view forward
        try
        {
            if (working != null)
            {
                var sheetIds = new HashSet<int>(sheets.Select(s => s.Id.IntegerValue));
                foreach (var uv in uidoc.GetOpenUIViews().ToList())
                    if (uv.ViewId != working.Id && sheetIds.Contains(uv.ViewId.IntegerValue)) { try { uv.Close(); } catch { } }
                uidoc.RequestViewChange(working); back = working.Name;
            }
        }
        catch (Exception e) { back = "could not return: " + e.Message; }
        return new { Exported = files, BackTo = back, Missing = wanted.Except(sheets.OfType<ViewSheet>().Select(s => s.SheetNumber)).ToList() };
    }
}
