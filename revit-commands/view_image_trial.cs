/* mcp-tool
{
  "description": "Read-only trial (always rolled back): hide elements or drop the template, then export the view/sheet as PNG.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number",
        "description": "View whose visibility is changed."
      },
      "hideIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "exportId": {
        "type": "number",
        "description": "View or sheet to export (default viewId)."
      },
      "file": {
        "type": "string",
        "description": "Output PNG path without extension."
      },
      "pixelWidth": {
        "type": "number"
      }
    },
    "required": [
      "viewId",
      "file"
    ]
  },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only trial: inside a transaction that is always rolled back, optionally hide elements (e.g. a Revit link
//    instance) in a view or remove its view template, then export the view (or a sheet) as PNG. Shows what a
//    visibility change would look like without changing the model.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewImageTrial
{
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var view = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var hide = (args["hideIds"] as JArray)?.Select(t => new ElementId((int)t)).ToList() ?? new List<ElementId>();
        var exportId = args["exportId"] != null ? new ElementId((int)args["exportId"]) : view.Id;
        string file = (string)args["file"]; Directory.CreateDirectory(Path.GetDirectoryName(file));
        string note = "";
        using (var t = new Transaction(doc, "trial"))
        {
            t.Start();
            if (hide.Count > 0) { try { view.HideElements(hide); note = "hidden " + hide.Count; } catch (Exception e) { note = "hide failed: " + e.Message; } }
            doc.Regenerate();
            var before = new HashSet<string>(Directory.GetFiles(Path.GetDirectoryName(file)));
            var opt = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews, FilePath = file, FitDirection = FitDirectionType.Horizontal,
                ZoomType = ZoomFitType.FitToPage, PixelSize = args.Value<int?>("pixelWidth") ?? 6000,
                HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150
            };
            opt.SetViewsAndSheets(new List<ElementId> { exportId });
            string err = null;
            try { doc.ExportImage(opt); } catch (Exception e) { err = e.Message; }
            t.RollBack();
            var created = Directory.GetFiles(Path.GetDirectoryName(file)).Where(f => !before.Contains(f) && f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
            string target = file + ".png";
            if (created.Count > 0 && created[0] != target) { if (File.Exists(target)) File.Delete(target); File.Move(created[0], target); }
            return new { note, err, File = File.Exists(target) ? target : null, RolledBack = true };
        }
    }
}
