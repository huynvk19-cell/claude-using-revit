/* mcp-tool
{
  "description": "ONE view: move dims of one type sideways in small steps until they clear other annotation / openings. preview | apply (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply"
        ]
      },
      "typeName": {
        "type": "string"
      },
      "ids": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "cachePath": {
        "type": "string"
      },
      "stepMm": {
        "type": "number",
        "description": "paper mm, default 1.2"
      },
      "maxMm": {
        "type": "number",
        "description": "paper mm, default 7.2"
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "viewId",
      "mode"
    ]
  },
  "timeoutSeconds": 300,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ONE view: move dimensions of one type (default the check type) that overlap other annotation (dim texts/lines, tag
//    heads, text notes, spots) or cross a visible door/window, sideways (perpendicular to the dim line) by the
//    smallest step that clears them (steps of stepMm paper mm, up to maxMm). Only dims of that type move; others are
//    obstacles. ids limits the dims to try. cachePath = opening_dims_each audit cache (visible openings). mode preview
//    | apply (logPath records moves; undo with move_in_view using the negative offsets).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class DimsDeclutter
{
    const double MM = 304.8;
    class R
    {
        public double X0, X1, Y0, Y1; public int Id; public bool Line; public string W;
        public R(double a, double b, double c, double d) { X0 = Math.Min(a, b); X1 = Math.Max(a, b); Y0 = Math.Min(c, d); Y1 = Math.Max(c, d); }
        public R Mv(double dx, double dy) { return new R(X0 + dx, X1 + dx, Y0 + dy, Y1 + dy) { Id = Id, Line = Line, W = W }; }
        public double Ox(R o) { return Math.Min(X1, o.X1) - Math.Max(X0, o.X0); }
        public double Oy(R o) { return Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0); }
    }
    static XYZ O, Rg, Up;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg); }
    static double VY(XYZ p) { return (p - O).DotProduct(Up); }
    static R Box(BoundingBoxXYZ bb)
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
        double P1 = v.Scale / MM, step = (args.Value<double?>("stepMm") ?? 1.2) * P1, maxOff = (args.Value<double?>("maxMm") ?? 7.2) * P1, minOv = 0.3 * P1;
        string typeName = args.Value<string>("typeName");
        if (string.IsNullOrEmpty(typeName)) return new { Error = "typeName is required (the dimension type whose dims may move, see drafting-profile)" };
        var only = (args["ids"] as JArray)?.Select(t => (int)t).ToList();

        var items = new List<R>(); var dimInfo = new Dictionary<int, Tuple<bool, Dimension>>();
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension) continue;
            var ln = d.Curve as Line; if (ln == null) continue;
            bool vert = Math.Abs(ln.Direction.DotProduct(Up)) > 0.99, hor = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.99;
            if (!vert && !hor) continue;
            double h = 2 * P1; try { h = d.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * v.Scale; } catch { }
            double line = vert ? VX(ln.Origin) : VY(ln.Origin); var bnd = new List<double>();
            var segs = d.NumberOfSegments > 1 ? d.Segments.Cast<DimensionSegment>().Select(s => Tuple.Create(s.Origin, s.Value ?? 0, s.TextPosition, s.ValueString)).ToList()
                                              : new List<Tuple<XYZ, double, XYZ, string>> { Tuple.Create(d.Origin, d.Value ?? 0, d.TextPosition, d.ValueString) };
            foreach (var s in segs)
            {
                double m = vert ? VY(s.Item1) : VX(s.Item1); bnd.Add(m - s.Item2 / 2); bnd.Add(m + s.Item2 / 2);
                var tb = TextBox(vert, VX(s.Item3), VY(s.Item3), s.Item4, h); tb.Id = d.Id.IntegerValue; tb.W = "dim text '" + s.Item4 + "'"; items.Add(tb);
            }
            var lr = vert ? new R(line - 0.15 * P1, line + 0.15 * P1, bnd.Min(), bnd.Max()) : new R(bnd.Min(), bnd.Max(), line - 0.15 * P1, line + 0.15 * P1);
            lr.Id = d.Id.IntegerValue; lr.Line = true; lr.W = "dim line"; items.Add(lr);
            dimInfo[d.Id.IntegerValue] = Tuple.Create(vert, d);
        }
        var tags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
        using (var t = new Transaction(doc, "measure tag heads"))
        {
            t.Start();
            var before = tags.ToDictionary(e => e.Id.IntegerValue, e => e.TagHeadPosition);
            foreach (var e in tags) { try { if (e.HasLeader) e.HasLeader = false; } catch { } }
            doc.Regenerate();
            foreach (var e in tags)
            {
                var r = Box(e.get_BoundingBox(v)); if (r == null) continue;
                var h0 = before[e.Id.IntegerValue]; var h1 = e.TagHeadPosition;
                r = r.Mv(VX(h0) - VX(h1), VY(h0) - VY(h1)); r.Id = e.Id.IntegerValue; r.W = "tag '" + e.TagText + "'"; items.Add(r);
            }
            t.RollBack();
        }
        foreach (var e in new FilteredElementCollector(doc, v.Id).WherePasses(new LogicalOrFilter(new ElementClassFilter(typeof(TextNote)), new ElementClassFilter(typeof(SpotDimension)))))
        { var r = Box(e.get_BoundingBox(v)); if (r == null) continue; r.Id = e.Id.IntegerValue; r.W = e.GetType().Name; items.Add(r); }
        // visible openings: a dim line must not run over one
        var opIds = new List<int>();
        var cache = args.Value<string>("cachePath");
        if (cache != null && File.Exists(cache)) opIds = ((JArray)JObject.Parse(File.ReadAllText(cache))["visible"]).Select(x => (int)x).ToList();
        var opRects = opIds.Select(i => { var r = Box(doc.GetElement(new ElementId(i))?.get_BoundingBox(v)); if (r != null) { r.Id = i; r.W = "opening"; } return r; }).Where(r => r != null).ToList();

        // slabs / beams cut by the section plane (host and links): no dim text or horizontal dim line on the poche
        var cuts = new List<R>();
        Action<Element, Transform> addCut = (el, tf) =>
        {
            var bb = el.get_BoundingBox(null); if (bb == null) return;
            var pts = new List<XYZ>(); foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z }) pts.Add(tf.OfPoint(new XYZ(x, y, z)));
            var ds = pts.Select(p => (p - O).DotProduct(v.ViewDirection)).ToList(); if (ds.Min() > 0 || ds.Max() < 0) return;
            var r = new R(pts.Min(VX), pts.Max(VX), pts.Min(VY), pts.Max(VY)); if (r.Y1 - r.Y0 < 2500 / MM) cuts.Add(r);
        };
        var cutCats = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFoundation });
        foreach (var el in new FilteredElementCollector(doc, v.Id).WherePasses(cutCats).WhereElementIsNotElementType()) addCut(el, Transform.Identity);
        foreach (RevitLinkInstance li in new FilteredElementCollector(doc, v.Id).OfClass(typeof(RevitLinkInstance)))
        { var ld = li.GetLinkDocument(); if (ld == null) continue; var tf = li.GetTotalTransform(); foreach (var el in new FilteredElementCollector(ld).WherePasses(cutCats).WhereElementIsNotElementType()) addCut(el, tf); }

        Func<R, R, bool> clash = (a, b) =>
        {
            if (a.Id == b.Id || (a.Line && b.Line)) return false;
            double ox = a.Ox(b), oy = a.Oy(b); if (ox <= 0 || oy <= 0) return false;
            if (!a.Line && !b.Line) return ox >= minOv && oy >= minOv;
            var ln = a.Line ? a : b; var tx = a.Line ? b : a; bool lv = ln.Y1 - ln.Y0 > ln.X1 - ln.X0;
            double depth = lv ? Math.Min(tx.X1 - ln.X0, ln.X1 - tx.X0) : Math.Min(tx.Y1 - ln.Y0, ln.Y1 - tx.Y0);
            double along = lv ? Math.Min(ln.Y1, tx.Y1) - Math.Max(ln.Y0, tx.Y0) : Math.Min(ln.X1, tx.X1) - Math.Max(ln.X0, tx.X0);
            return depth >= minOv && along >= minOv;
        };
        Func<List<R>, int> score = mine =>
            mine.Sum(m => items.Count(o => o.Id != m.Id && clash(m, o)) + (m.Line ? opRects.Count(o => m.Ox(o) > minOv && m.Oy(o) > minOv) * 3 : 0)
                          + ((!m.Line || (m.X1 - m.X0 > m.Y1 - m.Y0)) ? cuts.Count(c => m.Ox(c) > minOv && m.Oy(c) > minOv) * 2 : 0));

        var cand = dimInfo.Where(kv => kv.Value.Item2.DimensionType.Name == typeName && (only == null || only.Contains(kv.Key))).Select(kv => kv.Key).ToList();
        // a dim that continues another dim on the same line (stacked openings: louvre chain on top of the window chain) stays on that line
        var lines = items.Where(r => r.Line).ToList();
        Func<int, bool> continues = id =>
        {
            var a = lines.FirstOrDefault(r => r.Id == id); if (a == null) return false;
            bool vert = a.Y1 - a.Y0 > a.X1 - a.X0; double tol = 20 / MM, touch = 60 / MM;
            return lines.Any(b => b.Id != id && (vert
                ? Math.Abs((a.X0 + a.X1) / 2 - (b.X0 + b.X1) / 2) < tol && (Math.Abs(a.Y0 - b.Y1) < touch || Math.Abs(a.Y1 - b.Y0) < touch)
                : Math.Abs((a.Y0 + a.Y1) / 2 - (b.Y0 + b.Y1) / 2) < tol && (Math.Abs(a.X0 - b.X1) < touch || Math.Abs(a.X1 - b.X0) < touch)));
        };
        var kept = cand.Where(continues).ToList();
        cand = cand.Except(kept).ToList();
        var moves = new List<object>(); var stuck = new List<object>(); var plan = new List<Tuple<int, double, double>>();
        foreach (var id in cand)
        {
            var mine = items.Where(r => r.Id == id).ToList();
            int s0 = score(mine); if (s0 == 0) continue;
            bool vert = dimInfo[id].Item1;
            double bestOff = 0; int best = s0;
            for (double k = step; k <= maxOff + 1e-9; k += step)
                foreach (var off in new[] { k, -k })
                {
                    var moved = mine.Select(r => vert ? r.Mv(off, 0) : r.Mv(0, off)).ToList();
                    int sc = score(moved);
                    if (sc < best) { best = sc; bestOff = off; }
                }
            if (bestOff != 0)
            {
                // commit in the working model so later dims see the new place
                items.RemoveAll(r => r.Id == id);
                items.AddRange(mine.Select(r => vert ? r.Mv(bestOff, 0) : r.Mv(0, bestOff)));
                plan.Add(Tuple.Create(id, vert ? bestOff : 0, vert ? 0 : bestOff));
                moves.Add(new { Id = id, Dir = vert ? "right" : "up", Mm = Math.Round(bestOff / P1, 1) + " paper mm", Before = s0, After = best });
            }
            else stuck.Add(new { Id = id, Clashes = s0, With = string.Join(", ", mine.SelectMany(m => items.Where(o => o.Id != m.Id && clash(m, o)).Select(o => o.W + " " + o.Id)).Distinct().Take(5)) });
        }
        if ((args.Value<string>("mode") ?? "preview") == "apply" && plan.Count > 0)
        {
            using (var tx = new Transaction(doc, "Tidy dims " + v.Name))
            {
                tx.Start();
                foreach (var p in plan) ElementTransformUtils.MoveElement(doc, new ElementId(p.Item1), Rg * p.Item2 + Up * p.Item3);
                tx.Commit();
            }
            var lp = args.Value<string>("logPath");
            if (lp != null) File.WriteAllText(lp, JsonConvert.SerializeObject(plan.Select(p => new { id = p.Item1, dxMm = Math.Round(p.Item2 * MM), dzMm = Math.Round(p.Item3 * MM) }), Formatting.Indented));
        }
        return new { View = v.Name, Checked = cand.Count, KeptOnLine = kept, Moved = moves, Stuck = stuck };
    }
}
