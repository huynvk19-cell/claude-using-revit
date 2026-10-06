/* mcp-tool
{
  "description": "Roll-up doors on an elevation/section view, nominal size from the family's named references (TOP, LEFT, RIGHT; case-insensitive): items [{doorId, levelId, vX? (mm along view right: vertical Level->TOP dim on that line), hZ? (mm elevation: horizontal LEFT->RIGHT dim at that height)}]. fixChains [{dimId, doorId}]: rebuild a chain that hooks the door's frame faces so it uses LEFT/RIGHT instead (same line and type; old dim deleted). typeName (required) = dimension type for new dims. mode preview | apply (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" } },
      "fixChains": { "type": "array", "items": { "type": "object" } },
      "typeName": { "type": "string" },
      "mode": { "type": "string", "enum": ["preview", "apply"] },
      "logPath": { "type": "string" }
    },
    "required": ["viewId", "mode", "logPath"]
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

public static class RollupDimsAdd
{
    const double MM = 304.8;
    static Reference Named(FamilyInstance f, string name)
    {
        foreach (FamilyInstanceReferenceType rt in Enum.GetValues(typeof(FamilyInstanceReferenceType)))
        {
            IList<Reference> refs; try { refs = f.GetReferences(rt); } catch { continue; }
            foreach (var r in refs) { string n = null; try { n = f.GetReferenceName(r); } catch { } if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return r; }
        }
        return null;
    }
    static List<double> Vals(Dimension d) => d.NumberOfSegments == 0 ? new List<double> { Math.Round((d.Value ?? 0) * MM) } : d.Segments.Cast<DimensionSegment>().Select(s => Math.Round((s.Value ?? 0) * MM)).ToList();

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        if (string.IsNullOrEmpty((string)args["typeName"])) return new { Error = "typeName is required (project dimension type, see the project drafting-profile)" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var type = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["typeName"]);
        var res = new List<object>(); var created = new List<int>(); var removed = new List<object>();
        using (var t = new Transaction(doc, "Roll-up door dims"))
        {
            t.Start();
            foreach (JObject it in (JArray)args["items"] ?? new JArray())
            {
                var f = doc.GetElement(new ElementId((int)it["doorId"])) as FamilyInstance;
                var mark = f?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                Reference top = Named(f, "TOP"), left = Named(f, "LEFT"), right = Named(f, "RIGHT");
                var lv = doc.GetElement(new ElementId((int)it["levelId"])) as Level;
                if (it["vX"] != null)
                {
                    if (top == null || lv == null) res.Add(new { Mark = mark, Error = "no TOP reference or level" });
                    else
                    {
                        var ra = new ReferenceArray(); ra.Append(lv.GetPlaneReference()); ra.Append(top);
                        var p = v.Origin + v.RightDirection * ((double)it["vX"] / MM);
                        var d = doc.Create.NewDimension(v, Line.CreateBound(new XYZ(p.X, p.Y, lv.Elevation), new XYZ(p.X, p.Y, lv.Elevation + 1)), ra, type);
                        created.Add(d.Id.IntegerValue); res.Add(new { Mark = mark, Dim = d.Id.IntegerValue, Kind = "V", Values = Vals(d) });
                    }
                }
                if (it["hZ"] != null)
                {
                    if (left == null || right == null) res.Add(new { Mark = mark, Error = "no LEFT/RIGHT reference" });
                    else
                    {
                        var ra = new ReferenceArray(); ra.Append(left); ra.Append(right);
                        double z = (double)it["hZ"] / MM;
                        var bb = f.get_BoundingBox(null); var c = (bb.Min + bb.Max) / 2;
                        var p0 = new XYZ(c.X, c.Y, z) - v.RightDirection * 5; var p1 = new XYZ(c.X, c.Y, z) + v.RightDirection * 5;
                        var d = doc.Create.NewDimension(v, Line.CreateBound(p0, p1), ra, type);
                        created.Add(d.Id.IntegerValue); res.Add(new { Mark = mark, Dim = d.Id.IntegerValue, Kind = "H", Values = Vals(d) });
                    }
                }
            }
            foreach (JObject fx in (JArray)args["fixChains"] ?? new JArray())
            {
                var old = (Dimension)doc.GetElement(new ElementId((int)fx["dimId"]));
                var f = doc.GetElement(new ElementId((int)fx["doorId"])) as FamilyInstance;
                Reference left = Named(f, "LEFT"), right = Named(f, "RIGHT");
                if (old == null || left == null || right == null) { res.Add(new { Fix = (int)fx["dimId"], Error = "dim or LEFT/RIGHT missing" }); continue; }
                var ra = new ReferenceArray();
                foreach (Reference r in old.References) if (r.ElementId != f.Id) ra.Append(r);
                ra.Append(left); ra.Append(right);
                var ln = (Line)old.Curve; var before = Vals(old);
                removed.Add(new { Id = old.Id.IntegerValue, Type = old.DimensionType.Name, Values = before, Refs = old.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc)).ToList() });
                var nd = doc.Create.NewDimension(v, Line.CreateBound(ln.Origin - ln.Direction * 1000, ln.Origin + ln.Direction * 1000), ra, old.DimensionType);
                doc.Delete(old.Id);
                created.Add(nd.Id.IntegerValue);
                res.Add(new { Fix = (int)fx["dimId"], New = nd.Id.IntegerValue, Before = string.Join("/", before), After = string.Join("/", Vals(nd)) });
            }
            if ((string)args["mode"] == "apply") { t.Commit(); File.WriteAllText((string)args["logPath"], JsonConvert.SerializeObject(new { View = v.Name, Created = created, Removed = removed }, Formatting.Indented)); }
            else t.RollBack();
        }
        return res;
    }
}
