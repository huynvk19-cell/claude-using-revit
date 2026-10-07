/* mcp-tool
{
  "description": "Copy linear dims to another view by reusing their references (e.g. hand dims on railings). preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "targetViewId": {
        "type": "integer"
      },
      "items": {
        "type": "array",
        "items": {
          "type": "object"
        }
      },
      "copyText": {
        "type": "boolean"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply",
          "undo"
        ]
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "mode"
    ]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Copy linear dimensions to another view by REUSING their references (stable representations), e.g. dims drawn by
//    hand on railings that the API cannot reference so that Revit draws them. Each item: sourceDimId, optional
//    offsetMm [dRight, dUp] model mm to shift the dim line, optional typeName (default: source type). Segment prefix
//    / suffix / above / below texts are copied unless copyText false. mode preview (rolled back, reports values and
//    whether each new dim is drawn in the target view) | apply (logPath: created ids) | undo (logPath).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DimsCopyRefs
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"]; string lp = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JArray.Parse(System.IO.File.ReadAllText(lp)).Select(x => new ElementId((int)x["new"])).Where(i => doc.GetElement(i) != null).ToList();
            using (var t = new Transaction(doc, "Undo copied dims")) { t.Start(); doc.Delete(ids); t.Commit(); }
            return "deleted " + ids.Count;
        }
        var view = doc.GetElement(new ElementId((int)args["targetViewId"])) as View;
        bool copyText = args["copyText"] == null || (bool)args["copyText"];
        var rows = new JArray(); var log = new JArray();
        using (var t = new Transaction(doc, "Copy dims by references"))
        {
            t.Start();
            var created = new List<Tuple<int, Dimension>>();
            foreach (JObject it in (JArray)args["items"])
            {
                var src = doc.GetElement(new ElementId((int)it["sourceDimId"])) as Dimension;
                if (src == null) { rows.Add(it["sourceDimId"] + ": not a dimension"); continue; }
                var ra = new ReferenceArray(); var bad = new List<string>();
                foreach (Reference r in src.References)
                {
                    try { ra.Append(Reference.ParseFromStableRepresentation(doc, r.ConvertToStableRepresentation(doc))); }
                    catch (Exception ex) { bad.Add(ex.Message); }
                }
                if (bad.Count > 0) { rows.Add(src.Id.IntegerValue + ": references not parsed: " + string.Join("; ", bad)); continue; }
                var ln = src.Curve as Line;
                XYZ o = ln.Origin, d = ln.Direction;
                if (it["offsetMm"] != null)
                {
                    var off = (JArray)it["offsetMm"];
                    o = o + view.RightDirection * ((double)off[0] / MM) + view.UpDirection * ((double)off[1] / MM);
                }
                var line = Line.CreateBound(o - d * 1000, o + d * 1000);
                DimensionType dt = src.DimensionType;
                if (it["typeName"] != null)
                    dt = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)it["typeName"]) ?? dt;
                Dimension nd = null;
                try { nd = doc.Create.NewDimension(view, line, ra, dt); }
                catch (Exception ex) { rows.Add(src.Id.IntegerValue + ": not created: " + ex.Message); continue; }
                if (copyText && nd != null)
                {
                    if (src.NumberOfSegments == 0) { nd.Prefix = src.Prefix; nd.Suffix = src.Suffix; nd.Above = src.Above; nd.Below = src.Below; }
                    else if (nd.NumberOfSegments == src.NumberOfSegments)
                        for (int i = 0; i < src.NumberOfSegments; i++)
                        {
                            var a = src.Segments.get_Item(i); var b = nd.Segments.get_Item(i);
                            b.Prefix = a.Prefix; b.Suffix = a.Suffix; b.Above = a.Above; b.Below = a.Below;
                        }
                }
                created.Add(Tuple.Create(src.Id.IntegerValue, nd));
            }
            doc.Regenerate();
            foreach (var c in created)
            {
                var nd = c.Item2;
                var bb = nd.get_BoundingBox(view);
                string vals = nd.NumberOfSegments == 0 ? Math.Round((nd.Value ?? 0) * MM).ToString()
                    : string.Join("|", Enumerable.Range(0, nd.NumberOfSegments).Select(i => Math.Round((nd.Segments.get_Item(i).Value ?? 0) * MM).ToString()));
                rows.Add(new JObject
                {
                    ["source"] = c.Item1, ["new"] = nd.Id.IntegerValue, ["values"] = vals,
                    ["box"] = bb == null ? null : new JArray(Math.Round(bb.Min.X * MM), Math.Round(bb.Min.Y * MM), Math.Round(bb.Max.X * MM), Math.Round(bb.Max.Y * MM))
                });
                log.Add(new JObject { ["source"] = c.Item1, ["new"] = nd.Id.IntegerValue });
            }
            if (mode == "apply") t.Commit(); else t.RollBack();
        }
        if (mode == "apply" && lp != null) System.IO.File.WriteAllText(lp, log.ToString());
        return rows;
    }
}
