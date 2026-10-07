/* mcp-tool
{
  "description": "Read-only, ONE view: annotation pairs that overlap (dim texts/lines, tag heads, text notes, spots); returns ids for highlight_elements.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "minOverlapMm": {
        "type": "number",
        "description": "paper mm both ways an overlap must exceed to count, default 0.3"
      },
      "outPath": {
        "type": "string"
      },
      "includeRoomTags": {
        "type": "boolean",
        "description": "default false: room tag bounding boxes span the family's whole label frame, far…"
      },
      "debugIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      }
    },
    "required": [
      "viewId"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: in ONE view, find annotations that overlap each other: dimension texts, tag heads (door/window/room tags,
//    leaders ignored), text notes and spot dimensions overlapping one another, or crossed by another dimension's line.
//    Dimension texts are estimated boxes (text size x character count) anchored on the text position. Returns the
//    overlapping pairs (kinds, ids, values/texts, sheet-scale position) and the list of element ids involved, ready
//    for highlight_elements.
// Parameters:
//   includeRoomTags: default false: room tag bounding boxes span the family's whole label frame, far wider than the
//    text
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class AnnotationOverlaps
{
    const double MM = 304.8;
    class R
    {
        public double X0, X1, Y0, Y1; public int Id; public string Kind, Text; public bool IsLine;
        public R(double a, double b, double c, double d) { X0 = Math.Min(a, b); X1 = Math.Max(a, b); Y0 = Math.Min(c, d); Y1 = Math.Max(c, d); }
        public double Ox(R o) { return Math.Min(X1, o.X1) - Math.Max(X0, o.X0); }
        public double Oy(R o) { return Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0); }
    }
    static XYZ O, Rg, Up;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg); }
    static double VY(XYZ p) { return (p - O).DotProduct(Up); }
    static R Proj(BoundingBoxXYZ bb)
    {
        if (bb == null) return null;
        var pts = new List<XYZ>();
        foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                    pts.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
        return new R(pts.Min(VX), pts.Max(VX), pts.Min(VY), pts.Max(VY));
    }
    static R TextBox(bool vertical, double x, double y, string txt, double h)
    {
        double w = Math.Max(1, (txt ?? "").Length) * 0.6 * h + 0.3 * h;
        return vertical ? new R(x - 1.4 * h, x - 0.1 * h, y - w / 2, y + w / 2) : new R(x - w / 2, x + w / 2, y + 0.1 * h, y + 1.4 * h);
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = (View)doc.GetElement(new ElementId(args.Value<int>("viewId")));
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection;
        double P1 = v.Scale / MM, minOv = (args.Value<double?>("minOverlapMm") ?? 0.3) * P1;
        var items = new List<R>();

        // dimensions (linear): line band + one text box per segment
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension) continue;
            var ln = d.Curve as Line; if (ln == null) continue;
            bool vert = Math.Abs(ln.Direction.DotProduct(Up)) > 0.99, hor = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.99;
            if (!vert && !hor) continue;
            double h = 2 * P1; try { h = d.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * v.Scale; } catch { }
            double line = vert ? VX(ln.Origin) : VY(ln.Origin);
            var bnd = new List<double>();
            var segs = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(s => Tuple.Create(s.Origin, s.Value ?? 0, s.TextPosition, s.ValueString)).ToList()
                                              : new List<Tuple<XYZ, double, XYZ, string>> { Tuple.Create(d.Origin, d.Value ?? 0, d.TextPosition, d.ValueString) };
            foreach (var s in segs)
            {
                double m = vert ? VY(s.Item1) : VX(s.Item1);
                bnd.Add(m - s.Item2 / 2); bnd.Add(m + s.Item2 / 2);
                var tb = TextBox(vert, VX(s.Item3), VY(s.Item3), s.Item4, h); tb.Id = d.Id.IntegerValue; tb.Kind = "dim text"; tb.Text = s.Item4; items.Add(tb);
            }
            var lr = vert ? new R(line - 0.15 * P1, line + 0.15 * P1, bnd.Min(), bnd.Max()) : new R(bnd.Min(), bnd.Max(), line - 0.15 * P1, line + 0.15 * P1);
            lr.Id = d.Id.IntegerValue; lr.Kind = "dim line"; lr.IsLine = true; lr.Text = string.Join("|", segs.Select(s => s.Item4)); items.Add(lr);
        }

        // tag heads: measure with leaders switched off (rolled back)
        // room tag boxes cover the whole label frame of the family (much wider than the text): off unless asked
        bool roomTags = args.Value<bool?>("includeRoomTags") ?? false;
        var tags = new FilteredElementCollector(doc, v.Id).WherePasses(new LogicalOrFilter(new ElementClassFilter(typeof(IndependentTag)), new ElementClassFilter(typeof(SpatialElementTag))))
            .Where(e => roomTags || e is IndependentTag).ToList();
        using (var t = new Transaction(doc, "measure tag heads"))
        {
            t.Start();
            // switching a leader off can move the head (room tags jump to the room point): shift the measured box back
            Func<Element, XYZ> head = e => { try { return e is IndependentTag it ? it.TagHeadPosition : ((SpatialElementTag)e).TagHeadPosition; } catch { return null; } };
            var before = tags.ToDictionary(e => e.Id.IntegerValue, head);
            foreach (var e in tags)
            {
                try { if (e is IndependentTag it && it.HasLeader) it.HasLeader = false; } catch { }
                try { if (e is SpatialElementTag st && st.HasLeader) st.HasLeader = false; } catch { }
            }
            doc.Regenerate();
            foreach (var e in tags)
            {
                var r = Proj(e.get_BoundingBox(v)); if (r == null) continue;
                var h0 = before[e.Id.IntegerValue]; var h1 = head(e);
                if (h0 != null && h1 != null) { double dx = VX(h0) - VX(h1), dy = VY(h0) - VY(h1); r = new R(r.X0 + dx, r.X1 + dx, r.Y0 + dy, r.Y1 + dy); }
                r.Id = e.Id.IntegerValue; r.Kind = e is IndependentTag ? "tag" : "room tag";
                try { r.Text = e is IndependentTag it ? it.TagText : ((SpatialElementTag)e).get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString(); } catch { }
                items.Add(r);
            }
            t.RollBack();
        }
        foreach (var e in new FilteredElementCollector(doc, v.Id).WherePasses(new LogicalOrFilter(new ElementClassFilter(typeof(TextNote)), new ElementClassFilter(typeof(SpotDimension)))))
        {
            var r = Proj(e.get_BoundingBox(v)); if (r == null) continue;
            r.Id = e.Id.IntegerValue; r.Kind = e is TextNote ? "text note" : "spot";
            try { r.Text = e is TextNote tn ? tn.Text.Trim() : ((SpotDimension)e).ValueString; } catch { }
            items.Add(r);
        }

        // pairs
        var pairs = new List<object>(); var ids = new HashSet<int>();
        for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                var a = items[i]; var b = items[j];
                if (a.Id == b.Id || (a.IsLine && b.IsLine)) continue;
                double ox = a.Ox(b), oy = a.Oy(b);
                if (ox <= 0 || oy <= 0) continue;
                if (!a.IsLine && !b.IsLine && (ox < minOv || oy < minOv)) continue;
                // a dim line crossing only the very end of a text box is not counted
                if (a.IsLine || b.IsLine)
                {
                    var ln = a.IsLine ? a : b; var tx = a.IsLine ? b : a;
                    bool lv = ln.Y1 - ln.Y0 > ln.X1 - ln.X0;
                    double depth = lv ? Math.Min(tx.X1 - ln.X0, ln.X1 - tx.X0) : Math.Min(tx.Y1 - ln.Y0, ln.Y1 - tx.Y0);
                    double along = lv ? Math.Min(ln.Y1, tx.Y1) - Math.Max(ln.Y0, tx.Y0) : Math.Min(ln.X1, tx.X1) - Math.Max(ln.X0, tx.X0);
                    if (depth < minOv || along < minOv) continue;
                }
                ids.Add(a.Id); ids.Add(b.Id);
                double cx = (Math.Max(a.X0, b.X0) + Math.Min(a.X1, b.X1)) / 2, cy = (Math.Max(a.Y0, b.Y0) + Math.Min(a.Y1, b.Y1)) / 2;
                pairs.Add(new { A = a.Kind + " " + a.Id + " '" + a.Text + "'", B = b.Kind + " " + b.Id + " '" + b.Text + "'", AtMm = Math.Round(cx * MM) + "," + Math.Round(cy * MM) });
            }
        var dbg = (args["debugIds"] as JArray)?.Select(t => (int)t).ToList();
        if (dbg != null)
            return items.Where(r => dbg.Contains(r.Id)).Select(r => new { r.Id, r.Kind, r.Text, X = Math.Round(r.X0 * MM) + ".." + Math.Round(r.X1 * MM), Y = Math.Round(r.Y0 * MM) + ".." + Math.Round(r.Y1 * MM),
                Head = (doc.GetElement(new ElementId(r.Id)) as SpatialElementTag) is SpatialElementTag st ? Math.Round(VX(st.TagHeadPosition) * MM) + "," + Math.Round(VY(st.TagHeadPosition) * MM) : null }).ToList();
        var res = new { View = v.Name, Items = items.Count, Pairs = pairs.Count, Ids = ids.OrderBy(x => x).ToList(), List = pairs };
        var outPath = args.Value<string>("outPath");
        if (outPath != null) File.WriteAllText(outPath, JsonConvert.SerializeObject(res, Formatting.Indented));
        return new { res.View, res.Items, res.Pairs, Elements = res.Ids.Count, res.Ids, Sample = pairs.Take(25).ToList() };
    }
}
