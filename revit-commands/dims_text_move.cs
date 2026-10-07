/* mcp-tool
{
  "description": "ONE view: move the text of dimension segments (short segments whose texts sit on each other, e.g. 80 | 30 | 80): items [{dimId, segmentIndex (omit for a single-segment dim), alongMm, acrossMm}] = model mm offsets from the current text position, along the dimension line and across it (+ = view Right / Up side). mode preview (rolled back) | apply (logPath: old positions) | undo (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" } },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 60
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class DimsTextMove
{
    const double MM = 304.8;
    class Old { public int DimId; public int Seg; public double X, Y, Z; }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var olds = JsonConvert.DeserializeObject<List<Old>>(File.ReadAllText(logPath)); int n = 0;
            using (var t = new Transaction(doc, "Undo dim text move"))
            {
                t.Start();
                foreach (var o in olds)
                {
                    var d = doc.GetElement(new ElementId(o.DimId)) as Dimension; if (d == null) continue;
                    var p = new XYZ(o.X, o.Y, o.Z);
                    if (o.Seg < 0) d.TextPosition = p; else d.Segments.get_Item(o.Seg).TextPosition = p;
                    n++;
                }
                t.Commit();
            }
            return new { Mode = "undo", Restored = n };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var log = new List<Old>(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Dim text move"))
        {
            t.Start();
            foreach (var it in (JArray)args["items"] ?? new JArray())
            {
                var d = doc.GetElement(new ElementId((int)it["dimId"])) as Dimension;
                if (d == null || !(d.Curve is Line l)) { errors.Add(it["dimId"] + ": not a linear dimension"); continue; }
                int seg = it["segmentIndex"] != null ? (int)it["segmentIndex"] : -1;
                var along = l.Direction; if (along.DotProduct(v.RightDirection) < -1e-6 || (Math.Abs(along.DotProduct(v.RightDirection)) < 1e-6 && along.DotProduct(v.UpDirection) < 0)) along = along.Negate();
                var across = v.ViewDirection.CrossProduct(along).Normalize(); // left of 'along' in the view
                if (across.DotProduct(v.RightDirection) + across.DotProduct(v.UpDirection) < 0) across = across.Negate();
                try
                {
                    XYZ cur = seg < 0 ? d.TextPosition : d.Segments.get_Item(seg).TextPosition;
                    log.Add(new Old { DimId = d.Id.IntegerValue, Seg = seg, X = cur.X, Y = cur.Y, Z = cur.Z });
                    var np = cur + along * ((double?)it["alongMm"] ?? 0) / MM + across * ((double?)it["acrossMm"] ?? 0) / MM;
                    if (seg < 0) d.TextPosition = np; else d.Segments.get_Item(seg).TextPosition = np;
                    done.Add(new { DimId = d.Id.IntegerValue, Segment = seg, MovedMm = new[] { (double?)it["alongMm"] ?? 0, (double?)it["acrossMm"] ?? 0 } });
                }
                catch (Exception e) { errors.Add(d.Id.IntegerValue + "/" + seg + ": " + e.Message); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(log)); }
            else t.RollBack();
        }
        return new { Mode = mode, Done = done, Errors = errors };
    }
}
