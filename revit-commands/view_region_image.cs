/* mcp-tool
{
  "description": "Export PNGs of square regions of views around given points (zooms the view; no model change).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "items": {
        "type": "array",
        "items": {
          "type": "object"
        }
      },
      "folder": {
        "type": "string"
      },
      "pixels": {
        "type": "integer"
      }
    },
    "required": [
      "items",
      "folder"
    ]
  },
  "timeoutSeconds": 600,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Export PNG images of a region of views: items [{viewId, x, y, halfMm, name}] (model mm). Activates each view, zooms
//    to the square around (x,y) and exports the visible region at pixels (default 1600). Does not change the model;
//    the last view stays active.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class ViewRegionImage
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var uidoc = app.ActiveUIDocument; var doc = uidoc.Document;
        string folder = (string)args["folder"]; Directory.CreateDirectory(folder);
        int px = args["pixels"] != null ? (int)args["pixels"] : 1600;
        var res = new List<object>();
        foreach (var it in (JArray)args["items"])
        {
            var v = doc.GetElement(new ElementId((int)it["viewId"])) as View;
            if (v == null) { res.Add(new { Error = "view not found " + it["viewId"] }); continue; }
            uidoc.ActiveView = v;
            var uiv = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == v.Id);
            double x = (double)it["x"] / MM, y = (double)it["y"] / MM, h = (double)it["halfMm"] / MM;
            double z = (v as ViewPlan)?.GenLevel?.ProjectElevation ?? 0;
            uiv.ZoomAndCenterRectangle(new XYZ(x - h, y - h, z), new XYZ(x + h, y + h, z));
            uidoc.RefreshActiveView();
            string name = (string)it["name"] ?? v.Id.IntegerValue.ToString();
            var opt = new ImageExportOptions
            {
                ExportRange = ExportRange.VisibleRegionOfCurrentView, ZoomType = ZoomFitType.FitToPage, PixelSize = px,
                ImageResolution = ImageResolution.DPI_150, HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
                FilePath = Path.Combine(folder, name)
            };
            doc.ExportImage(opt);
            var file = Directory.GetFiles(folder, name + "*.png").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            res.Add(new { View = v.Name, File = file });
        }
        return res;
    }
}
