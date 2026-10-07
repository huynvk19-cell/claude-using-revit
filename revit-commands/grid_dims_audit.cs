/* mcp-tool
{
  "description": "Old logic (use grid_dims_band audit), read-only: which sides of each grid group have chain + overall dims.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "sheetPrefix": {
        "type": "string"
      },
      "sheetNumbers": {
        "type": "array",
        "items": {
          "type": "string"
        }
      },
      "viewIds": {
        "type": "array",
        "items": {
          "type": "number"
        }
      },
      "onlyMissing": {
        "type": "boolean"
      },
      "detail": {
        "type": "boolean",
        "description": "list the matching dimensions"
      }
    }
  },
  "timeoutSeconds": 300,
  "readOnly": true
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Read-only: for plan/section/elevation views placed on sheets, group the visible straight grids by direction and
//    check whether linear dimensions already give a grid-to-grid chain and an overall (first-to-last grid) dimension
//    on each side (low = bottom/left, high = top/right in view axes). Dimension ends are matched to grid positions
//    (works for host or linked grid references).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public static class GridDimsAudit
{
    const double MM = 304.8;
    public class GridGroup { public string Axis; public List<Tuple<string, double>> Grids = new List<Tuple<string, double>>(); public double Low, High; }

    // groups of grids in a view; Axis = "x" means grids spaced along view right (grid lines vertical in view)
    public static List<GridGroup> Groups(Document doc, View v)
    {
        XYZ o = v.Origin, r = v.RightDirection, u = v.UpDirection;
        var gx = new GridGroup { Axis = "x" }; var gy = new GridGroup { Axis = "y" };
        var lows = new Dictionary<string, List<double>> { ["x"] = new List<double>(), ["y"] = new List<double>() };
        var highs = new Dictionary<string, List<double>> { ["x"] = new List<double>(), ["y"] = new List<double>() };
        foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            Curve c = null; try { c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault(); } catch { }
            if (!(c is Line ln)) continue;
            double R0 = (ln.GetEndPoint(0) - o).DotProduct(r) * MM, R1 = (ln.GetEndPoint(1) - o).DotProduct(r) * MM;
            double U0 = (ln.GetEndPoint(0) - o).DotProduct(u) * MM, U1 = (ln.GetEndPoint(1) - o).DotProduct(u) * MM;
            if (Math.Abs(ln.Direction.DotProduct(u)) > 0.999) { gx.Grids.Add(Tuple.Create(g.Name, R0)); lows["x"].Add(Math.Min(U0, U1)); highs["x"].Add(Math.Max(U0, U1)); }
            else if (Math.Abs(ln.Direction.DotProduct(r)) > 0.999) { gy.Grids.Add(Tuple.Create(g.Name, U0)); lows["y"].Add(Math.Min(R0, R1)); highs["y"].Add(Math.Max(R0, R1)); }
        }
        var res = new List<GridGroup>();
        foreach (var gg in new[] { gx, gy })
        {
            if (gg.Grids.Count < 2) continue;
            gg.Grids = gg.Grids.OrderBy(t => t.Item2).ToList();
            gg.Low = lows[gg.Axis].Max(); gg.High = highs[gg.Axis].Min(); // common span of all grids
            res.Add(gg);
        }
        return res;
    }

    public class DimInfo { public Dimension D; public string Axis; public List<double> B = new List<double>(); public double Pos; public string Type; }

    // linear dims with their boundary coordinates along the axis and line position across it (mm)
    public static List<DimInfo> Dims(Document doc, View v)
    {
        XYZ o = v.Origin, r = v.RightDirection, u = v.UpDirection;
        var res = new List<DimInfo>();
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension || d.OwnerViewId != v.Id) continue;
            if (!(d.Curve is Line ln)) continue;
            string axis = Math.Abs(ln.Direction.DotProduct(r)) > 0.999 ? "x" : Math.Abs(ln.Direction.DotProduct(u)) > 0.999 ? "y" : null;
            if (axis == null) continue;
            XYZ ax = axis == "x" ? r : u, pe = axis == "x" ? u : r;
            var di = new DimInfo { D = d, Axis = axis, Type = d.DimensionType?.Name };
            var segs = new List<Tuple<XYZ, double>>();
            if (d.NumberOfSegments > 0) foreach (DimensionSegment s in d.Segments) { if (s.Value.HasValue) segs.Add(Tuple.Create(s.Origin, s.Value.Value)); }
            else if (d.Value.HasValue) segs.Add(Tuple.Create(d.Origin, d.Value.Value));
            if (segs.Count == 0) continue;
            foreach (var s in segs)
            {
                double c = (s.Item1 - o).DotProduct(ax) * MM, h = s.Item2 * MM / 2;
                di.B.Add(c - h); di.B.Add(c + h);
            }
            di.B = di.B.OrderBy(x => x).Aggregate(new List<double>(), (l, x) => { if (l.Count == 0 || Math.Abs(l.Last() - x) > 1) l.Add(x); return l; });
            di.Pos = (segs[0].Item1 - o).DotProduct(pe) * MM;
            res.Add(di);
        }
        return res;
    }

    public static object Check(Document doc, View v, GridGroup gg, List<DimInfo> dims, bool detail)
    {
        var pos = gg.Grids.Select(t => t.Item2).ToList();
        Func<double, int> Match = x => pos.FindIndex(p => Math.Abs(p - x) < 5);
        double mid = (gg.Low + gg.High) / 2;
        var sides = new Dictionary<string, object>();
        foreach (var side in new[] { "low", "high" })
        {
            var mine = dims.Where(d => d.Axis == gg.Axis && (side == "low" ? d.Pos < mid : d.Pos >= mid)).ToList();
            var covered = new HashSet<int>(); bool overall = false; var used = new List<string>();
            foreach (var d in mine)
            {
                var idx = d.B.Select(Match).ToList();
                int hits = idx.Count(i => i >= 0);
                if (hits < 2) continue;
                if (d.B.Count == 2 && idx[0] == 0 && idx[1] == pos.Count - 1) { overall = true; used.Add("overall " + d.D.Id.IntegerValue); if (pos.Count == 2) { covered.Add(0); covered.Add(1); } continue; }
                if (hits >= 0.8 * d.B.Count) { foreach (var i in idx.Where(i => i >= 0)) covered.Add(i); used.Add("chain " + d.D.Id.IntegerValue + " (" + hits + "/" + d.B.Count + ")"); }
            }
            // chain complete when every consecutive pair is spanned by grid-matched ends
            var missing = Enumerable.Range(0, pos.Count).Where(i => !covered.Contains(i)).Select(i => gg.Grids[i].Item1).ToList();
            sides[side] = new { Chain = missing.Count == 0 ? "full" : covered.Count == 0 ? "none" : "partial, missing " + string.Join(",", missing), Overall = overall || pos.Count == 2 && missing.Count == 0, Dims = detail ? used : null };
        }
        return new { Axis = gg.Axis == "x" ? "grids spaced left-right" : "grids spaced bottom-top", Grids = string.Join(",", gg.Grids.Select(t => t.Item1)), Span = Math.Round(gg.Low) + " .. " + Math.Round(gg.High), Sides = sides };
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        bool onlyMissing = args.Value<bool?>("onlyMissing") ?? false, detail = args.Value<bool?>("detail") ?? false;
        var views = new List<Tuple<string, View>>();
        if (args["viewIds"] is JArray vids) foreach (var t in vids) { var vv = (View)doc.GetElement(new ElementId((int)t)); views.Add(Tuple.Create("", vv)); }
        else
        {
            string prefix = (string)args["sheetPrefix"]; var nums = (args["sheetNumbers"] as JArray)?.Select(t => (string)t).ToList();
            foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => nums != null ? nums.Contains(s.SheetNumber) : s.SheetNumber.StartsWith(prefix)).OrderBy(s => s.SheetNumber))
                foreach (var id in s.GetAllPlacedViews()) { var vv = (View)doc.GetElement(id); if (vv.ViewType != ViewType.Legend && vv.ViewType != ViewType.Schedule && vv.ViewType != ViewType.DraftingView) views.Add(Tuple.Create(s.SheetNumber, vv)); }
        }
        var outL = new List<object>();
        foreach (var t in views)
        {
            var v = t.Item2; var groups = Groups(doc, v);
            if (groups.Count == 0) { if (!onlyMissing) outL.Add(new { Sheet = t.Item1, View = v.Name, ViewId = v.Id.IntegerValue, Note = "fewer than 2 parallel grids" }); continue; }
            var dims = Dims(doc, v);
            var chk = groups.Select(g => Check(doc, v, g, dims, detail)).ToList();
            bool ok = chk.All(c => { var j = JObject.FromObject(c); return j["Sides"].Children<JProperty>().Any(p => (string)p.Value["Chain"] == "full" && (bool)p.Value["Overall"]); });
            if (onlyMissing && ok) continue;
            outL.Add(new { Sheet = t.Item1, View = v.Name, ViewId = v.Id.IntegerValue, Type = v.ViewType.ToString(), Scale = v.Scale, AtLeastOneSideComplete = ok, Groups = chk });
        }
        return new { Views = views.Count, Listed = outL.Count, Result = outL };
    }
}
