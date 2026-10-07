/* mcp-tool
{
  "description": "Recreate a dim from logged stable references on the line of lineDimId (optional reference swap). preview | apply.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "refs": {
        "type": "array",
        "items": {
          "type": "string"
        }
      },
      "lineDimId": {
        "type": "number"
      },
      "deleteLineDim": {
        "type": "boolean"
      },
      "typeName": {
        "type": "string"
      },
      "swap": {
        "type": "array",
        "items": {
          "type": "object"
        }
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply"
        ]
      }
    },
    "required": [
      "viewId",
      "lineDimId",
      "mode"
    ]
  },
  "timeoutSeconds": 60,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Recreate a dimension from stable reference strings (as logged by dims_edit / rollup_dims_add 'Removed'), on the line
//    of an existing dim (lineDimId) and with a type name; optionally delete lineDimId afterwards. Optional swap:
//    [{from: stable string, to: stable string}] replaces references before creating. mode preview | apply. Reports
//    values.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class DimRestore
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        var ld = (Dimension)doc.GetElement(new ElementId((int)args["lineDimId"]));
        var ln = (Line)ld.Curve;
        var type = args["typeName"] != null ? new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().First(x => x.Name == (string)args["typeName"]) : ld.DimensionType;
        var swap = ((args["swap"] as JArray) ?? new JArray()).ToDictionary(s => (string)s["from"], s => (string)s["to"]);
        var ra = new ReferenceArray(); var bad = new List<string>();
        var refList = args["refsFile"] != null ? JArray.Parse(System.IO.File.ReadAllText((string)args["refsFile"])) : (JArray)args["refs"];
        foreach (var s0 in refList.Select(x => (string)x))
        {
            var s = swap.ContainsKey(s0) ? swap[s0] : s0;
            try { var rr = Reference.ParseFromStableRepresentation(doc, s); var ge = doc.GetElement(rr.ElementId) as Grid; ra.Append(ge != null ? new Reference(ge) : rr); } catch (Exception e) { bad.Add(s + ": " + e.Message); }
        }
        if (args["nominalDoorId"] != null)
        {   // drop every reference to this door and use its named LEFT / RIGHT references instead
            var door = (FamilyInstance)doc.GetElement(new ElementId((int)args["nominalDoorId"]));
            var keep = new ReferenceArray();
            foreach (Reference r in ra) if (r.ElementId != door.Id) keep.Append(r);
            foreach (var nm in new[] { "LEFT", "RIGHT" })
                foreach (FamilyInstanceReferenceType rt in Enum.GetValues(typeof(FamilyInstanceReferenceType)))
                {
                    var hit = door.GetReferences(rt).FirstOrDefault(r => { try { return string.Equals(door.GetReferenceName(r), nm, StringComparison.OrdinalIgnoreCase); } catch { return false; } });
                    if (hit != null) { keep.Append(hit); break; }
                }
            ra = keep;
        }
        using (var t = new Transaction(doc, "Restore dim"))
        {
            t.Start();
            double off = (args.Value<double?>("createOffsetMm") ?? 0) / MM;
            var shift = XYZ.BasisZ * off;
            var d = doc.Create.NewDimension(v, Line.CreateBound(ln.Origin + shift - ln.Direction * 1000, ln.Origin + shift + ln.Direction * 1000), ra, type);
            if (off != 0) ElementTransformUtils.MoveElement(doc, d.Id, shift.Negate());
            var vals = d.NumberOfSegments == 0 ? new List<double> { Math.Round((d.Value ?? 0) * MM) } : d.Segments.Cast<DimensionSegment>().Select(g => Math.Round((g.Value ?? 0) * MM)).ToList();
            int newId = d.Id.IntegerValue;
            if (args.Value<bool?>("deleteLineDim") ?? false) doc.Delete(ld.Id);
            if ((string)args["mode"] == "apply") t.Commit(); else t.RollBack();
            return new { New = newId, RefsIn = ra.Size, Segments = vals.Count, Values = string.Join("/", vals), Bad = bad };
        }
    }
}
