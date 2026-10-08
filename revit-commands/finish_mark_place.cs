/* mcp-tool
{
  "description": "ONE view: place finish marks (Generic Annotation, e.g. F.. / W.. box) copied from an existing mark's type, code set, optional orthogonal leader. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "sourceId": { "type": "number" },
      "paramName": { "type": "string" },
      "items": { "type": "array", "items": { "type": "object" } },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ONE plan view: create Generic Annotation instances of the same type as sourceId (an existing finish mark), set the
//    text parameter paramName (the code, e.g. "W05") and, when endRight/endUp are given, add a leader ending there.
//    items: {code, right, up (insertion point, view frame mm as view_elem_boxes ViewBox), endRight, endUp (leader end,
//    optional), elbowFirst "V" (default: vertical from the head, then horizontal) | "H"}. Leaders stay orthogonal: when
//    the end lines up with the head the elbow sits half way, otherwise at the corner. A family without leader support
//    is placed without one (reported). mode preview (rolled back) | apply (logPath) | undo (logPath).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class FinishMarkPlace
{
    const double MM = 304.8;

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); var gone = new List<int>();
            using (var t = new Transaction(doc, "Undo finish marks"))
            {
                t.Start();
                foreach (var id in ids) if (doc.GetElement(new ElementId(id)) != null) { doc.Delete(new ElementId(id)); gone.Add(id); }
                t.Commit();
            }
            return new { Mode = "undo", Deleted = gone };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var src = doc.GetElement(new ElementId((int)args["sourceId"])) as FamilyInstance;
        if (src == null) return new { Error = "sourceId is not a family instance" };
        string pName = (string)args["paramName"];
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        Func<double, double, XYZ> P = (r, u) => new XYZ(O.X, O.Y, O.Z) + R * (r / MM) + U * (u / MM);

        var created = new List<int>(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Finish marks"))
        {
            t.Start();
            int i = 0;
            foreach (var it in (JArray)args["items"] ?? new JArray())
            {
                i++;
                try
                {
                    double r = (double)it["right"], u = (double)it["up"];
                    var inst = doc.Create.NewFamilyInstance(P(r, u), src.Symbol, v);
                    var prm = inst.LookupParameter(pName);
                    if (prm == null || prm.IsReadOnly) { errors.Add($"item {i}: parameter '{pName}' not found / read-only"); }
                    else prm.Set((string)it["code"]);
                    string leader = "none";
                    if (it["endRight"] != null && it["endUp"] != null)
                    {
                        var a = inst as AnnotationSymbol;
                        try
                        {
                            a.addLeader();
                            var ld = a.GetLeaders().Cast<Leader>().FirstOrDefault();
                            double er = (double)it["endRight"], eu = (double)it["endUp"];
                            bool hFirst = ((string)it["elbowFirst"] ?? "V").ToUpper() == "H";
                            XYZ end = P(er, eu), elbow;
                            if (Math.Abs(er - r) < 1 || Math.Abs(eu - u) < 1) elbow = P((r + er) / 2, (u + eu) / 2);
                            else elbow = hFirst ? P(er, u) : P(r, eu);
                            ld.End = end; ld.Elbow = elbow;
                            leader = "added";
                        }
                        catch (Exception ex) { leader = "not supported: " + ex.Message; }
                    }
                    created.Add(inst.Id.IntegerValue);
                    done.Add(new { Item = i, Id = inst.Id.IntegerValue, Code = (string)it["code"], Leader = leader });
                }
                catch (Exception ex) { errors.Add($"item {i}: {ex.Message}"); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, Done = done, Errors = errors };
    }
}
