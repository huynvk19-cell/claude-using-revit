/* mcp-tool
{
  "description": "ONE view: move existing tags (head + free leader end + orthogonal elbow) to view-frame points. preview | apply | undo.",
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
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ONE plan view: re-place existing IndependentTags. items: {id, right, up (head), endRight, endUp (free leader end on the
//    tagged element, optional), elbowFirst "V" (default) | "H"}: leaders stay orthogonal, one elbow when the end is not
//    straight under / beside the head. View frame mm as view_elem_boxes ViewBox. mode preview (rolled back) | apply
//    (logPath: old head / end / elbow) | undo (logPath).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class TagReposition
{
    const double MM = 304.8;

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        Func<double, double, double, XYZ> P = (r, u, z) => { var p = O + R * (r / MM) + U * (u / MM); return new XYZ(p.X, p.Y, z); };
        Func<XYZ, double[]> F = p => new[] { Math.Round((p - O).DotProduct(R) * MM, 1), Math.Round((p - O).DotProduct(U) * MM, 1) };

        JArray items = mode == "undo" ? JArray.Parse(File.ReadAllText(logPath)) : (JArray)args["items"] ?? new JArray();
        var old = new JArray(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Tag reposition"))
        {
            t.Start();
            foreach (var it in items)
            {
                int id = (int)it["id"];
                var tg = doc.GetElement(new ElementId(id)) as IndependentTag;
                if (tg == null) { errors.Add(id + ": not a tag"); continue; }
                try
                {
                    var rf = tg.GetTaggedReferences().FirstOrDefault();
                    double z = tg.TagHeadPosition.Z;
                    var o = new JObject { ["id"] = id, ["right"] = F(tg.TagHeadPosition)[0], ["up"] = F(tg.TagHeadPosition)[1] };
                    if (tg.HasLeader && rf != null && tg.LeaderEndCondition == LeaderEndCondition.Free)
                    {
                        var en = F(tg.GetLeaderEnd(rf)); o["endRight"] = en[0]; o["endUp"] = en[1];
                        if (tg.HasLeaderElbow(rf)) { var el = F(tg.GetLeaderElbow(rf)); o["elbowRight"] = el[0]; o["elbowUp"] = el[1]; }
                    }
                    old.Add(o);

                    double r = (double)it["right"], u = (double)it["up"];
                    if (it["endRight"] != null && rf != null)
                    {
                        if (!tg.HasLeader) tg.HasLeader = true;
                        tg.LeaderEndCondition = LeaderEndCondition.Free;
                        tg.SetLeaderEnd(rf, P((double)it["endRight"], (double)it["endUp"], z));
                    }
                    tg.TagHeadPosition = P(r, u, z);
                    if (it["endRight"] != null && rf != null)
                    {
                        double er = (double)it["endRight"], eu = (double)it["endUp"];
                        XYZ elb = null;
                        if (it["elbowRight"] != null) elb = P((double)it["elbowRight"], (double)it["elbowUp"], z);
                        else if (Math.Abs(er - r) >= 1 && Math.Abs(eu - u) >= 1)
                            elb = ((string)it["elbowFirst"] ?? "V").ToUpper() == "H" ? P(er, u, z) : P(r, eu, z);
                        else elb = P((r + er) / 2, (u + eu) / 2, z);
                        tg.SetLeaderElbow(rf, elb);
                    }
                    done.Add(new { Id = id, Text = tg.TagText, Head = F(tg.TagHeadPosition) });
                }
                catch (Exception ex) { errors.Add(id + ": " + ex.Message); }
            }
            if (mode == "preview") t.RollBack();
            else { t.Commit(); if (mode == "apply") File.WriteAllText(logPath, old.ToString()); }
        }
        return new { View = v.Name, Mode = mode, Done = done, Errors = errors };
    }
}
