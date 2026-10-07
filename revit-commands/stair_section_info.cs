/* mcp-tool
{
  "description": "Read-only: stair SECTION cut parallel to the stair path (drafting-stair-section-parallel.md). Lists the host stairs (by component) seen in ONE section view, every flight ordered going up (F1, F2…): cut by the section plane or seen beyond, rising to the left or right on the sheet, From EL / To EL (level-based elevations), risers x riser height = flight height with the rise text ('169.4mm x 16R = ' + height, below '(EQUAL RISERS)'), treads x tread depth = going with the going text ('280mm x 15T = ' + going, below '(EQUAL TREADS)'), landings with their elevation and side. Checks the existing dims of the view against those values (prefix / below text), tags and tread/riser numbers per flight, and the project's most-used stair run tag types. Returns an Issues list per rule.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number", "description": "the stair section view" },
      "toleranceMm": { "type": "number", "description": "value tolerance when matching existing dims, default 1" },
      "outPath": { "type": "string", "description": "write the full result as JSON here" }
    },
    "required": ["viewId"]
  },
  "timeoutSeconds": 120,
  "readOnly": true
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairSectionInfo
{
    const double MM = 304.8;
    static XYZ O, Rg, Up, Vd;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double Depth(XYZ p) { return (p - O).DotProduct(Vd) * MM; }
    static string Num(double x) { return Math.Round(x, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }

    class Fl
    {
        public StairsRun R; public Stairs S; public double El0, El1, X0, X1; public string Label, State, Rises;
        public int Risers, Treads; public double RiserH, Depth, Height, Going;
        public string RiseText { get { return Num(RiserH) + "mm x " + Risers + "R = "; } }
        public string GoText { get { return Num(Depth) + "mm x " + Treads + "T = "; } }
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        var v = doc.GetElement(new ElementId(args.Value<int>("viewId"))) as View;
        if (v == null || v.ViewType != ViewType.Section) return new { Error = "viewId must be a section view (plans: stair_plan_audit)" };
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection; Vd = v.ViewDirection;
        double tol = args.Value<double?>("toleranceMm") ?? 1;
        var issues = new List<string>(); var notes = new List<string>();

        var fl = new List<Fl>(); var lands = new List<object>();
        foreach (var s in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>())
        {
            // level-based elevation (as the tags read it): base level Elevation + base offset
            var pl = s.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            var bl = pl != null && pl.StorageType == StorageType.ElementId ? doc.GetElement(pl.AsElementId()) as Level : null;
            if (bl == null) { notes.Add("stairs " + s.Id.IntegerValue + ": base level not readable, skipped"); continue; }
            double sb = bl.Elevation + (s.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET)?.AsDouble() ?? 0);
            if (Math.Abs(Rg.DotProduct(XYZ.BasisZ)) > 0.01) notes.Add("view right direction is not horizontal");
            foreach (var rid in s.GetStairsRuns())
            {
                var r = doc.GetElement(rid) as StairsRun; if (r == null) continue;
                var f = new Fl { R = r, S = s, El0 = (sb + r.BaseElevation) * MM, El1 = (sb + r.TopElevation) * MM, Risers = r.ActualRisersNumber, Treads = r.ActualTreadsNumber, RiserH = s.ActualRiserHeight * MM, Depth = s.ActualTreadDepth * MM };
                f.Height = f.El1 - f.El0; f.Going = f.Treads * f.Depth;
                var fp = new List<XYZ>(); try { foreach (Curve c in r.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count == 0) continue;
                f.X0 = fp.Min(VX); f.X1 = fp.Max(VX);
                double d0 = fp.Min(Depth), d1 = fp.Max(Depth);
                f.State = d0 <= 0 && d1 >= 0 ? "cut" : d1 < 0 ? "beyond" : "in front of the section (not seen)";
                try { var pc = r.GetStairsPath().Cast<Curve>().ToList(); double dx = VX(pc.Last().GetEndPoint(1)) - VX(pc.First().GetEndPoint(0)); f.Rises = Math.Abs(dx) < 1 ? "toward / away from the viewer" : dx > 0 ? "rises to the right" : "rises to the left"; } catch { f.Rises = "?"; }
                fl.Add(f);
            }
            foreach (var lid in s.GetStairsLandings())
            {
                var l = doc.GetElement(lid) as StairsLanding; if (l == null) continue;
                var fp = new List<XYZ>(); try { foreach (Curve c in l.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (fp.Count == 0) continue;
                double d0 = fp.Min(Depth), d1 = fp.Max(Depth); double cx = (fp.Min(VX) + fp.Max(VX)) / 2;
                lands.Add(new { Id = l.Id.IntegerValue, Stairs = s.Id.IntegerValue, ElMm = Math.Round((sb + l.BaseElevation) * MM), State = d0 <= 0 && d1 >= 0 ? "cut" : d1 < 0 ? "beyond" : "in front", XMm = Math.Round(cx) });
            }
        }
        fl = fl.Where(f => f.State != "in front of the section (not seen)").OrderBy(f => f.El0).ToList();
        if (fl.Count == 0) return new { View = v.Name, Error = "no host stair flight seen in this section (link stairs are not read)" };
        for (int i = 0; i < fl.Count; i++) fl[i].Label = "F" + (i + 1);
        if (fl.Any(f => f.Rises == "toward / away from the viewer")) notes.Add("some flights run toward the viewer: this view is a cross-section, not a section parallel to the stair path");

        // check: risers x riser height = flight height, treads x depth = going
        foreach (var f in fl)
            if (Math.Abs(f.Risers * f.RiserH - f.Height) > Math.Max(1, f.Risers * 0.05))
                issues.Add("LC: " + f.Label + " " + f.Risers + "R x " + Num(f.RiserH) + " = " + Math.Round(f.Risers * f.RiserH) + " but the flight rises " + Math.Round(f.Height) + ": check the model (do not edit it)");

        // existing dims vs the flights
        var segs = new List<Tuple<int, bool, double, string, string>>(); // dim id, vertical, value, prefix, below
        foreach (var d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (d is SpotDimension) continue;
            var ln = d.Curve as Line; if (ln == null) continue;
            bool vert = Math.Abs(ln.Direction.DotProduct(Up)) > 0.999, hor = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.999;
            if (!vert && !hor) continue;
            if (d.NumberOfSegments > 1) foreach (DimensionSegment g in d.Segments) segs.Add(Tuple.Create(d.Id.IntegerValue, vert, (g.Value ?? 0) * MM, g.Prefix, g.Below));
            else segs.Add(Tuple.Create(d.Id.IntegerValue, vert, (d.Value ?? 0) * MM, d.Prefix, d.Below));
        }
        Func<string, string> norm = x => (x ?? "").Replace(" ", "").Replace("×", "x").ToLowerInvariant();
        var dimCheck = new List<object>();
        foreach (var f in fl)
        {
            foreach (var k in new[] { "rise", "going" })
            {
                bool vert = k == "rise"; double val = vert ? f.Height : f.Going; string pre = vert ? f.RiseText : f.GoText, below = vert ? "(EQUAL RISERS)" : "(EQUAL TREADS)";
                var hit = segs.Where(x => x.Item2 == vert && Math.Abs(x.Item3 - val) <= tol).OrderByDescending(x => norm(x.Item4) == norm(pre) ? 1 : 0).FirstOrDefault();
                string st;
                if (hit == null) st = "missing";
                else
                {
                    var bad = new List<string>();
                    if (norm(hit.Item4) != norm(pre)) bad.Add("prefix should be '" + pre + "'");
                    if (!(hit.Item5 ?? "").ToUpper().Contains(vert ? "EQUAL RISERS" : "EQUAL TREADS")) bad.Add("below text should be '" + below + "'");
                    st = bad.Count == 0 ? "OK" : string.Join("; ", bad);
                }
                if (k == "going" && f.State == "beyond" && st == "missing") st = "missing (flight seen beyond: dim only if the sample does)";
                dimCheck.Add(new { Flight = f.Label, Rule = vert ? "LA1" : "LA3", Text = pre + Math.Round(val) + " / " + below, Status = st, DimId = hit?.Item1 });
                if (st != "OK" && !st.StartsWith("missing (")) issues.Add((vert ? "LA1" : "LA3") + ": " + f.Label + " " + (vert ? "rise" : "going") + " dim: " + st);
            }
        }

        // tags and numbers per flight
        var tagged = new Dictionary<int, List<IndependentTag>>();
        foreach (var t in new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            try { foreach (var id in t.GetTaggedLocalElementIds()) { if (!tagged.ContainsKey(id.IntegerValue)) tagged[id.IntegerValue] = new List<IndependentTag>(); tagged[id.IntegerValue].Add(t); } } catch { }
        var numbered = new HashSet<int>();
        foreach (var ns in new FilteredElementCollector(doc, v.Id).OfClass(typeof(NumberSystem)).Cast<NumberSystem>())
            try { numbered.Add(ns.NumberedElementId.HostElementId.IntegerValue); } catch { }
        foreach (var f in fl)
        {
            int n = tagged.ContainsKey(f.R.Id.IntegerValue) ? tagged[f.R.Id.IntegerValue].Count : 0;
            if (n != 1) issues.Add("LB1: " + f.Label + " has " + n + " run tags");
            if (f.State == "cut" && !numbered.Contains(f.R.Id.IntegerValue)) issues.Add("LC: " + f.Label + " (cut) has no riser numbers");
        }
        var runTagTypes = new FilteredElementCollector(doc).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
            .Where(t => t.Category != null && t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StairsRunTags)
            .GroupBy(t => (doc.GetElement(t.OwnerViewId) as View)?.ViewType == ViewType.Section ? "in sections" : "elsewhere")
            .Select(g => new { Where = g.Key, Top = g.GroupBy(t => t.GetTypeId().IntegerValue).OrderByDescending(x => x.Count()).Take(3).Select(x => doc.GetElement(new ElementId(x.Key))?.Name + " (" + x.Count() + ")").ToList() }).ToList();

        var res = new
        {
            View = v.Name, ViewId = v.Id.IntegerValue, Scale = v.Scale,
            Flights = fl.Select(f => new
            {
                f.Label, Id = f.R.Id.IntegerValue, Stairs = f.S.Id.IntegerValue, f.State, f.Rises,
                FromEl = Math.Round(f.El0), ToEl = Math.Round(f.El1), TagText = "From EL +" + Math.Round(f.El0) + " To EL +" + Math.Round(f.El1) + " / " + Num(f.RiserH) + "mm x " + f.Risers + "R",
                f.Risers, RiserHeight = Num(f.RiserH), Height = Math.Round(f.Height), RiseText = f.RiseText + Math.Round(f.Height),
                f.Treads, TreadDepth = Num(f.Depth), Going = Math.Round(f.Going), GoingText = f.GoText + Math.Round(f.Going),
                XMm = Math.Round(f.X0) + ".." + Math.Round(f.X1),
                RunTags = tagged.ContainsKey(f.R.Id.IntegerValue) ? tagged[f.R.Id.IntegerValue].Select(t => t.TagText).ToList() : new List<string>(),
                RiserNumbers = numbered.Contains(f.R.Id.IntegerValue)
            }).ToList(),
            Landings = lands, Dims = dimCheck, RunTagTypes = runTagTypes, Notes = notes, Issues = issues
        };
        var outPath = args.Value<string>("outPath");
        if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, JsonConvert.SerializeObject(res, Formatting.Indented));
        return res;
    }
}
