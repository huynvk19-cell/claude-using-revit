/* mcp-tool
{
  "description": "Move elements by dx, dy, dz in mm (model coordinates, one transaction).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "elementId": { "type": "number", "description": "One element id" },
      "ids": { "type": "array", "items": { "type": "number" }, "description": "Several element ids" },
      "dx": { "type": "number", "description": "mm along model X (default 0)" },
      "dy": { "type": "number", "description": "mm along model Y (default 0)" },
      "dz": { "type": "number", "description": "mm along model Z (default 0)" }
    }
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ElementTransformUtils.MoveElements in one transaction "AI Culi: move". The vector is in MODEL coordinates (mm), not
// in the view frame: for a move "right / up on the sheet" of an annotation in a section, convert with the view's
// RightDirection / UpDirection first (or use the commands that take view-frame offsets, e.g. dims_text_move).
// Pinned elements are skipped and listed. Missing ids are listed.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class MoveElement
{
    const double MmToFt = 1.0 / 304.8;

    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        var want = new List<int>();
        if (args["elementId"] != null) want.Add((int)args["elementId"]);
        if (args["ids"] is JArray arr) want.AddRange(arr.Select(t => (int)t));
        want = want.Distinct().ToList();
        if (want.Count == 0) return new { error = "pass elementId or ids" };
        var v = new XYZ((args.Value<double?>("dx") ?? 0) * MmToFt,
                        (args.Value<double?>("dy") ?? 0) * MmToFt,
                        (args.Value<double?>("dz") ?? 0) * MmToFt);
        if (v.IsZeroLength()) return new { error = "dx, dy and dz are all 0" };

        var ids = new List<ElementId>();
        var missing = new List<int>();
        var pinned = new List<int>();
        foreach (int i in want)
        {
            Element e = doc.GetElement(new ElementId(i));
            if (e == null) missing.Add(i);
            else if (e.Pinned) pinned.Add(i);
            else ids.Add(e.Id);
        }
        if (ids.Count > 0)
        {
            using (var t = new Transaction(doc, "AI Culi: move"))
            {
                t.Start();
                ElementTransformUtils.MoveElements(doc, ids, v);
                t.Commit();
            }
        }
        return new { Moved = ids.Count, Pinned = pinned, Missing = missing };
    }
}
