/* mcp-tool
{
  "description": "UI helper so the user can watch progress. action open: make viewId the active view and zoom to its crop region (call twice when the view was not active: the zoom needs the view window). action 'panes': hide or show the Properties palette and the Project Browser (panes: 'hide' | 'show'). No model change.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": { "type": "string", "enum": ["open", "panes", "toggle"] },
      "which": { "type": "string", "enum": ["properties", "browser"], "description": "toggle: View > User Interface > Properties / Project Browser (flips the checkbox)" },
      "viewId": { "type": "number" },
      "panes": { "type": "string", "enum": ["hide", "show"] }
    },
    "required": ["action"]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
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
        string action = (string)args["action"];
        if (action == "toggle")
        {
            // View > User Interface checkboxes (one posted command per call; it runs right after this call returns)
            var which = (string)args["which"] == "browser" ? PostableCommand.ProjectBrowser : PostableCommand.TogglePropertiesPalette;
            app.PostCommand(RevitCommandId.LookupPostableCommandId(which));
            return new { Posted = which.ToString() };
        }
        if (action == "panes")
        {
            bool show = (string)args["panes"] == "show";
            var res = new System.Collections.Generic.List<string>();
            foreach (var id in new[] { DockablePanes.BuiltInDockablePanes.PropertiesPalette, DockablePanes.BuiltInDockablePanes.ProjectBrowser })
            {
                try
                {
                    var p = app.GetDockablePane(id); bool before = p.IsShown();
                    if (show && !before) p.Show(); else if (!show && before) p.Hide();
                    res.Add(p.GetTitle() + ": was " + (before ? "shown" : "hidden") + ", now " + (p.IsShown() ? "shown" : "hidden"));
                }
                catch (Exception e) { res.Add("error: " + e.Message); }
            }
            return new { Panes = res };
        }
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
