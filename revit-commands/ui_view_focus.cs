/* mcp-tool
{
  "description": "UI only: open a view zoomed to its crop.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": {
        "type": "string",
        "enum": [
          "open"
        ]
      },
      "viewId": {
        "type": "number"
      }
    },
    "required": [
      "action"
    ]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// UI helper so the user can watch progress. action open: make viewId the active view and zoom to its crop region (call
//    twice when the view was not active: the zoom needs the view window). No model change.
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class UiViewFocus
{
    public static object Run(UIApplication app, JObject args)
    {
        var uidoc = app.ActiveUIDocument;
        var v = uidoc.Document.GetElement(new ElementId((int)args["viewId"])) as View;
        if (v == null) return new { Error = "view not found" };
        bool switched = false;
        if (uidoc.ActiveView.Id != v.Id) { uidoc.ActiveView = v; switched = true; }
        var uiv = uidoc.GetOpenUIViews().FirstOrDefault(x => x.ViewId == v.Id);
        string how = null;
        if (uiv != null)
        {
            // zoom to the crop region (what the sheet shows); ZoomToFit as fallback
            try
            {
                if (v.CropBoxActive)
                {
                    var cb = v.CropBox; var t = cb.Transform;
                    uiv.ZoomAndCenterRectangle(t.OfPoint(cb.Min), t.OfPoint(cb.Max)); how = "crop region";
                }
                else { uiv.ZoomToFit(); how = "fit"; }
            }
            catch { uiv.ZoomToFit(); how = "fit"; }
            try { uidoc.RefreshActiveView(); } catch { }
        }
        return new { View = v.Name, Switched = switched, Zoomed = how ?? "view window not open yet: call again" };
    }
}
