/* mcp-tool
{
  "description": "Read-only: for every tag (IndependentTag) and spot elevation in ONE view (or the given ids): type, tagged element, head position, leader on/off, end condition, leader end and elbow per reference (view frame mm: Right / Up from the view origin), and the leader segment directions (H = horizontal, V = vertical, D = diagonal). Use to learn how the user draws leaders.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "integer" },
      "ids": { "type": "array", "items": { "type": "integer" } }
    },
    "required": ["viewId"]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class TagLeadersInfo
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = doc.GetElement(new ElementId((int)args["viewId"])) as View;
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        Func<XYZ, JArray> P = p => p == null ? null : new JArray(Math.Round((p - O).DotProduct(R) * MM), Math.Round((p - O).DotProduct(U) * MM));
        Func<XYZ, XYZ, string> Dir = (a, b) =>
        {
            if (a == null || b == null) return "?";
            double dx = Math.Abs((b - a).DotProduct(R)), dy = Math.Abs((b - a).DotProduct(U));
            if (dx < 1e-3 && dy < 1e-3) return "0";
            if (dy < dx * 0.02) return "H"; if (dx < dy * 0.02) return "V"; return "D";
        };
        IEnumerable<IndependentTag> tags;
        if (args["ids"] != null) tags = ((JArray)args["ids"]).Select(i => doc.GetElement(new ElementId((int)i)) as IndependentTag).Where(t => t != null);
        else tags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>();
        var rows = new JArray();
        foreach (var t in tags)
        {
            var row = new JObject
            {
                ["id"] = t.Id.IntegerValue, ["cat"] = t.Category?.Name, ["type"] = doc.GetElement(t.GetTypeId())?.Name,
                ["head"] = P(t.TagHeadPosition), ["leader"] = t.HasLeader, ["end"] = t.LeaderEndCondition.ToString()
            };
            var refs = new JArray();
            if (t.HasLeader)
                foreach (var r in t.GetTaggedReferences())
                {
                    XYZ end = null, elb = null; bool hasElb = false;
                    try { end = t.GetLeaderEnd(r); } catch { }
                    try { hasElb = t.HasLeaderElbow(r); if (hasElb) elb = t.GetLeaderElbow(r); } catch { }
                    string shape = hasElb ? Dir(t.TagHeadPosition, elb) + "+" + Dir(elb, end) : Dir(t.TagHeadPosition, end);
                    refs.Add(new JObject { ["on"] = r.ElementId.IntegerValue, ["end"] = P(end), ["elbow"] = P(elb), ["shape(head>elbow>end)"] = shape });
                }
            row["refs"] = refs;
            rows.Add(row);
        }
        return rows;
    }
}
