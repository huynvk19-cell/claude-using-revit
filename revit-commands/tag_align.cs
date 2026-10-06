/* mcp-tool
{
  "description": "Elevation/section view: move door/window tags (by tag text) so their head sits at the same height as a reference tag (by tag text), directly above their own element (x = element centre), with a leader to the element; clears any graphic override on the moved tag. mode preview (report only) | apply (writes logPath with old head positions and leader state).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "tagTexts": { "type": "array", "items": { "type": "string" } },
      "refTagText": { "type": "string" },
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "logPath": { "type": "string" }
    },
    "required": ["viewId", "tagTexts", "refTagText", "mode", "logPath"]
  },
  "timeoutSeconds": 120,
  "readOnly": false
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

public static class TagAlign
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var tags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
        var refTag = tags.FirstOrDefault(t => t.TagText == (string)args["refTagText"]);
        if (refTag == null) return new { Error = "reference tag not found" };
        var want = ((JArray)args["tagTexts"]).Select(x => (string)x).ToList();
        var rows = new List<object>(); var log = new List<object>();
        using (var t = new Transaction(doc, "Align tags"))
        {
            t.Start();
            foreach (var tg in tags.Where(x => want.Contains(x.TagText)))
            {
                var el = doc.GetElement(tg.GetTaggedLocalElementIds().FirstOrDefault() ?? ElementId.InvalidElementId);
                var bb = el?.get_BoundingBox(null);
                if (bb == null) { rows.Add(new { Tag = tg.Id.IntegerValue, Error = "tagged element not found" }); continue; }
                var old = tg.TagHeadPosition; bool oldLeader = tg.HasLeader;
                var c = (bb.Min + bb.Max) / 2;
                // x: element centre projected on the view plane; z: reference tag head height
                double cx = (c - v.Origin).DotProduct(v.RightDirection);
                double ox = (old - v.Origin).DotProduct(v.RightDirection);
                var head = old + v.RightDirection * (cx - ox) + XYZ.BasisZ * (refTag.TagHeadPosition.Z - old.Z);
                log.Add(new { Tag = tg.Id.IntegerValue, X = old.X, Y = old.Y, Z = old.Z, Leader = oldLeader });
                tg.HasLeader = true;
                tg.LeaderEndCondition = LeaderEndCondition.Free;
                tg.TagHeadPosition = head;
                try { tg.SetLeaderEnd(tg.GetTaggedReferences().First(), new XYZ(head.X, head.Y, bb.Max.Z - 300 / MM)); } catch { }
                v.SetElementOverrides(tg.Id, new OverrideGraphicSettings());
                rows.Add(new { Tag = tg.Id.IntegerValue, Text = tg.TagText, MovedUpMm = Math.Round((head.Z - old.Z) * MM), MovedSideMm = Math.Round((cx - ox) * MM) });
            }
            if ((string)args["mode"] == "apply") { t.Commit(); File.WriteAllText((string)args["logPath"], JsonConvert.SerializeObject(log, Formatting.Indented)); } else t.RollBack();
        }
        return rows;
    }
}
