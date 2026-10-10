/* mcp-tool
{
  "description": "Read-only: runs and landings of given stairs with plan box (mm, model X/Y) and top elevation, plus existing stair paths.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "stairIds": { "type": "array", "items": { "type": "integer" } }
    },
    "required": ["stairIds"]
  },
  "timeoutSeconds": 60,
  "readOnly": true
}
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class StairPartsInfo
{
    const double MM = 304.8;

    static object Part(Document doc, ElementId id, string kind)
    {
        var e = doc.GetElement(id);
        var bb = e.get_BoundingBox(null);
        if (bb == null) return new { Kind = kind, Id = id.IntegerValue };
        return new
        {
            Kind = kind,
            Id = id.IntegerValue,
            Box = new[] { Math.Round(bb.Min.X * MM), Math.Round(bb.Min.Y * MM), Math.Round(bb.Max.X * MM), Math.Round(bb.Max.Y * MM) },
            Z = new[] { Math.Round(bb.Min.Z * MM), Math.Round(bb.Max.Z * MM) }
        };
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var paths = new FilteredElementCollector(doc).OfClass(typeof(StairsPath)).Cast<StairsPath>().ToList();
        var res = new List<object>();
        foreach (var t in (JArray)args["stairIds"])
        {
            var s = doc.GetElement(new ElementId((int)t)) as Stairs;
            if (s == null) { res.Add(new { Id = (int)t, Error = "not a Stairs element" }); continue; }
            var parts = new List<object>();
            foreach (var r in s.GetStairsRuns()) parts.Add(Part(doc, r, "run"));
            foreach (var l in s.GetStairsLandings()) parts.Add(Part(doc, l, "landing"));
            var ps = paths.Where(p => p.StairsId.HostElementId.IntegerValue == s.Id.IntegerValue)
                .Select(p => new { Id = p.Id.IntegerValue, View = doc.GetElement(p.OwnerViewId)?.Name }).ToList();
            res.Add(new
            {
                Id = s.Id.IntegerValue,
                BaseMm = Math.Round(s.BaseElevation * MM),
                TopMm = Math.Round(s.TopElevation * MM),
                Risers = s.ActualRisersNumber,
                Parts = parts,
                Paths = ps
            });
        }
        return res;
    }
}
