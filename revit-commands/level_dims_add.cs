/* mcp-tool
{
  "description": "Section/elevation: level chain + overall next to the level heads on one side. Skips views with a level chain. preview | apply (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "side": {
        "type": "string",
        "enum": [
          "left",
          "right"
        ]
      },
      "chainMm": {
        "type": "number",
        "description": "paper mm from the level end to the chain (default 14)"
      },
      "overallMm": {
        "type": "number",
        "description": "paper mm from the level end to the overall (default 8)"
      },
      "names": {
        "type": "array",
        "items": {
          "type": "string"
        }
      },
      "typeName": {
        "type": "string"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply"
        ]
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "viewId",
      "side",
      "mode",
      "logPath"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Section/elevation view: add a vertical chain between all visible host levels (in names list, or all) and an overall
//    (lowest to highest) dimension, next to the level heads on one side (left/right). The dims sit chainMm / overallMm
//    paper mm inside the levels' 2D end on that side (toward the building). Skipped if the view already has a vertical
//    dim referencing 3+ levels. mode preview | apply (logPath).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class LevelDimsAdd
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        if (string.IsNullOrEmpty((string)args["typeName"])) return new { Error = "typeName is required (project dimension type, see the project drafting-profile)" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        bool left = (string)args["side"] == "left";
        var names = (args["names"] as JArray)?.Select(x => (string)x).ToList();
        var levels = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Level)).Cast<Level>()
            .Where(l => names == null || names.Contains(l.Name)).OrderBy(l => l.Elevation).ToList();
        var existing = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(d => d.OwnerViewId == v.Id && d.References.Cast<Reference>().Count(r => doc.GetElement(r.ElementId) is Level) >= 3).Select(d => d.Id.IntegerValue).ToList();
        if (existing.Count > 0 && (args.Value<bool?>("overallOnly") ?? false))
        {
            var ch = (Dimension)doc.GetElement(new ElementId(existing[0]));
            var lvRefs = ch.References.Cast<Reference>().Where(r => doc.GetElement(r.ElementId) is Level).ToList();
            var lvs = lvRefs.Select(r => (Level)doc.GetElement(r.ElementId)).OrderBy(l => l.Elevation).ToList();
            bool hasOverall = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Any(d => d.OwnerViewId == v.Id && d.NumberOfSegments == 0
                && d.References.Size == 2 && d.References.Cast<Reference>().All(r => r.ElementId == lvs.First().Id || r.ElementId == lvs.Last().Id));
            if (hasOverall) return new { View = v.Name, Skipped = "overall exists" };
            double cx = (((Line)ch.Curve).Origin - v.Origin).DotProduct(v.RightDirection);
            double ox = cx + (left ? -1 : 1) * 6 * v.Scale / MM;
            var ty = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["typeName"]);
            using (var t = new Transaction(doc, "Level overall dim"))
            {
                t.Start();
                var ra = new ReferenceArray(); ra.Append(lvs.First().GetPlaneReference()); ra.Append(lvs.Last().GetPlaneReference());
                var p = v.Origin + v.RightDirection * ox;
                var d = doc.Create.NewDimension(v, Line.CreateBound(new XYZ(p.X, p.Y, lvs.First().Elevation), new XYZ(p.X, p.Y, lvs.Last().Elevation)), ra, ty);
                var val = Math.Round((d.Value ?? 0) * MM);
                if ((string)args["mode"] == "apply") { t.Commit(); File.WriteAllText((string)args["logPath"], JsonConvert.SerializeObject(new { View = v.Name, Created = new[] { d.Id.IntegerValue } })); } else t.RollBack();
                return new { View = v.Name, Overall = d.Id.IntegerValue, Value = val, From = lvs.First().Name, To = lvs.Last().Name };
            }
        }
        if (existing.Count > 0) return new { View = v.Name, Skipped = "already has level chain", existing };
        if (levels.Count < 2) return new { View = v.Name, Skipped = "fewer than 2 levels" };
        // level 2D end on the chosen side (along view right)
        double end = left ? double.MaxValue : double.MinValue;
        foreach (var l in levels)
        {
            Curve c = null; try { c = l.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
            if (c == null) continue;
            foreach (var p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
            {
                double x = (p - v.Origin).DotProduct(v.RightDirection);
                end = left ? Math.Min(end, x) : Math.Max(end, x);
            }
        }
        double s = v.Scale / MM, sign = left ? 1 : -1;
        double xc = end + sign * (args.Value<double?>("chainMm") ?? 14) * s, xo = end + sign * (args.Value<double?>("overallMm") ?? 8) * s;
        var type = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == (string)args["typeName"]);
        var created = new List<int>(); var vals = new List<object>();
        using (var t = new Transaction(doc, "Level dims"))
        {
            t.Start();
            foreach (var (x, set) in new[] { (xc, levels), (xo, new List<Level> { levels.First(), levels.Last() }) })
            {
                var ra = new ReferenceArray(); foreach (var l in set) ra.Append(l.GetPlaneReference());
                var p = v.Origin + v.RightDirection * x;
                var d = doc.Create.NewDimension(v, Line.CreateBound(new XYZ(p.X, p.Y, levels.First().Elevation), new XYZ(p.X, p.Y, levels.Last().Elevation)), ra, type);
                created.Add(d.Id.IntegerValue);
                vals.Add(d.NumberOfSegments == 0 ? new List<double> { Math.Round((d.Value ?? 0) * MM) } : d.Segments.Cast<DimensionSegment>().Select(g => Math.Round((g.Value ?? 0) * MM)).ToList());
            }
            if ((string)args["mode"] == "apply") { t.Commit(); File.WriteAllText((string)args["logPath"], JsonConvert.SerializeObject(new { View = v.Name, Created = created }, Formatting.Indented)); }
            else t.RollBack();
        }
        return new { View = v.Name, Levels = string.Join(", ", levels.Select(l => l.Name)), LevelEndMm = Math.Round(end * MM), Created = created, Values = vals };
    }
}
