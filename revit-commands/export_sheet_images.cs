/* mcp-tool
{
  "description": "Export sheets (by number) as PNG images; returns the file paths.",
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
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => wanted.Contains(s.SheetNumber)).ToList();

        var files = new List<object>();
        foreach (var s in sheets)
        {
            string prefix = Path.Combine(folder, s.SheetNumber);
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
            files.Add(new { Sheet = s.SheetNumber, File = File.Exists(target) ? target : src });
        }
        return new { Exported = files, Missing = wanted.Except(sheets.Select(s => s.SheetNumber)).ToList() };
    }
}
