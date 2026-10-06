/* mcp-tool
{
  "description": "Edit linear dimensions of ONE view: delete ids; merge groups (each group = dim ids whose references are combined into one new dimension on the line of the first dim, then the group is deleted); retype ids to a dimension type. Everything removed is logged (id, type, view, values, line, references as stable strings) to logPath so it can be checked or rebuilt. mode preview (rolled back) | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "deleteIds": { "type": "array", "items": { "type": "number" } },
      "merge": { "type": "array", "items": { "type": "array", "items": { "type": "number" } } },
      "mergeType": { "type": "string", "description": "type for merged dims (default: type of the first dim)" },
      "retypeIds": { "type": "array", "items": { "type": "number" } },
      "retypeTo": { "type": "string" },
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "logPath": { "type": "string" }
    },
    "required": ["viewId", "mode", "logPath"]
  },
  "timeoutSeconds": 180,
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

public static class DimsEdit
{
    const double MM = 304.8;
    static object Info(Document doc, Dimension d)
    {
        var vals = d.NumberOfSegments == 0 ? new List<double> { Math.Round((d.Value ?? 0) * MM) } : d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();
        var ln = d.Curve as Line;
        return new
        {
            Id = d.Id.IntegerValue, Type = d.DimensionType.Name, Values = vals,
            Origin = ln == null ? null : new[] { ln.Origin.X, ln.Origin.Y, ln.Origin.Z }, Dir = ln == null ? null : new[] { ln.Direction.X, ln.Direction.Y, ln.Direction.Z },
            Refs = d.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc)).ToList()
        };
    }
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        string mode = (string)args["mode"];
        var types = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().ToList();
        var removed = new List<object>(); var created = new List<object>(); var retyped = new List<int>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Edit dims"))
        {
            t.Start();
            foreach (JArray grp in (args["merge"] as JArray) ?? new JArray())
            {
                var dims = grp.Select(x => doc.GetElement(new ElementId((int)x)) as Dimension).ToList();
                if (dims.Any(d => d == null)) { errors.Add("merge: missing dim in " + grp.ToString(Formatting.None)); continue; }
                var ra = new ReferenceArray(); var seen = new HashSet<string>(); var firstEls = new HashSet<int>();
                foreach (Reference r in dims[0].References) { firstEls.Add(r.ElementId.IntegerValue); if (seen.Add(r.ConvertToStableRepresentation(doc))) ra.Append(r); }
                // later dims only add references to elements the first dim does not reference (e.g. the Level)
                foreach (var d in dims.Skip(1)) foreach (Reference r in d.References)
                    if (!firstEls.Contains(r.ElementId.IntegerValue) && seen.Add(r.ConvertToStableRepresentation(doc))) ra.Append(r);
                var ln = (Line)dims[0].Curve;
                var ty = args["mergeType"] != null ? types.FirstOrDefault(x => x.Name == (string)args["mergeType"]) : dims[0].DimensionType;
                foreach (var d in dims) removed.Add(Info(doc, d));
                var line = Line.CreateBound(ln.Origin - ln.Direction * 1000, ln.Origin + ln.Direction * 1000);
                try
                {
                    var nd = doc.Create.NewDimension(v, line, ra, ty);
                    foreach (var d in dims) doc.Delete(d.Id);
                    created.Add(Info(doc, nd));
                }
                catch (Exception e) { errors.Add("merge failed " + grp.ToString(Formatting.None) + ": " + e.Message); }
            }
            foreach (var id in (args["deleteIds"] as JArray)?.Select(x => (int)x) ?? Enumerable.Empty<int>())
            {
                var d = doc.GetElement(new ElementId(id)) as Dimension;
                if (d == null) { errors.Add("not found " + id); continue; }
                removed.Add(Info(doc, d)); doc.Delete(d.Id);
            }
            var retypeList = (args["retypeIds"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>();
            if (args["retypeFromType"] != null)
                retypeList.AddRange(new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>()
                    .Where(d => d.OwnerViewId == v.Id && d.DimensionType.Name == (string)args["retypeFromType"]).Select(d => d.Id.IntegerValue));
            if (retypeList.Count > 0)
            {
                var ty = types.FirstOrDefault(x => x.Name == (string)args["retypeTo"]);
                if (ty == null) errors.Add("type not found " + args["retypeTo"]);
                else foreach (var id in retypeList)
                {
                    var d = doc.GetElement(new ElementId(id)) as Dimension;
                    if (d == null) { errors.Add("retype: not found " + id); continue; }
                    d.ChangeTypeId(ty.Id); retyped.Add(id);
                }
            }
            if (mode == "apply")
            {
                t.Commit();
                File.WriteAllText((string)args["logPath"], JsonConvert.SerializeObject(new { View = v.Name, Removed = removed, Created = created, Retyped = retyped, RetypedTo = args["retypeTo"] }, Formatting.Indented));
            }
            else t.RollBack();
        }
        return new { mode, View = v.Name, Removed = removed.Count, Created = created.Select(c => new { ((dynamic)c).Id, ((dynamic)c).Values }), Retyped = retyped.Count, Errors = errors };
    }
}
