/* mcp-tool
{
  "description": "ONE view of any kind, the whole job in one call: finds what is missing, solves positions, places, marks, checks; returns only counts and questions. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "profile": { "type": "object", "description": "values of the project profile (see the Details comment)" },
      "imagePath": { "type": "string", "description": "PNG of the result, also in preview" },
      "logPath": { "type": "string", "description": "created ids; required for apply and undo" },
      "outPath": { "type": "string", "description": "full plan as JSON (not returned)" }
    },
    "required": ["viewId", "mode"]
  },
  "timeoutSeconds": 600
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Replaces the many small calls of a view: everything runs inside Revit, one transaction group (one Undo step), and
//    Claude reads only the result. Two problems are solved in this order:
//    1. parameters: which components are missing, what each one says, which references it sits on (model + audit);
//    2. positions: bands outward from the stair on its free side, heads tried along the seen run; hard = no overlap
//       with an annotation head, a dim line or a dim text, leader not through a text; soft = off the grid lines, near
//       the middle of the run. Tag sizes are measured by placing the tag and rolling back.
//    Result: Placed (counts), Ask (decisions for the user), Left (audit issues still open, by rule), Overlaps,
//    Undrawn. Full plan in outPath. preview rolls the whole group back.
// Modules, chosen from the view kind and name (stairViewText, default STAIR):
//    stair plan: path, run and railing tags, run chain, wall thickness, SA1, SA3, landing spots, door tags, F and W marks, X.
//    stair section along the flights: LB1, LB2, LA3, LA4, LA5, LB3, LC, with or without walls.
//    stair section across the flights: XA1-XA6, XB1, XC.
//    elevation or building section: structural fill (not on names with ELEVATION), grid dims, level dims, opening dims, opening tags.
//    floor plan: grid dims, opening chains, opening tags, room tags, floor spots, wall tags, slope marks. Ceiling plan: grid dims, room tags.
// profile (or top-level args): dimType, suffix, me, runTagType, railTagType, pathType, spotType, spotCheckType, numberSourceId,
//    numberSourceIdCross, la5TargetMm, levelSide, preferSides, noGridDims[], nominalFamilies[], doorTagType, windowTagType,
//    roomTagType, roomTagSizeMm, minAreaM2, stairRoomText, stairViewText, civilSymbolId, wallTagType, wallTagSkipRooms[],
//    slopeType, floorCode, wallCode, finishMarkSourceId, finishParam, fill{sources, concreteType, earthType, lineStyle, ...}.
//    A value the job needs and the profile lacks goes to Ask; nothing is invented.
// Others' dims are never changed: a dim last changed by another user goes to Ask; by `me` it is fixed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClaudeUsingRevit.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ViewDevelop
{
    const double MM = 304.8;
    class Rc
    {
        public double L, R, B, T;
        public Rc(double l, double r, double b, double t) { L = Math.Min(l, r); R = Math.Max(l, r); B = Math.Min(b, t); T = Math.Max(b, t); }
        public bool Hit(Rc o, double m = 0) { return L - m < o.R && R + m > o.L && B - m < o.T && T + m > o.B; }
    }
    class RunD { public StairsRun R; public Stairs S; public string Label, State; public int T; public double Depth, AStart, ALast, A0, A1, C0, C1; public int Sign; }
    class Ln { public double A, C0, C1, Len; public string Stable; }

    static View V; static XYZ O, Rg, Up; static double Z0, S;
    static bool Sw;   // stair plan with the flights along the view's vertical: along = Up, across = Right
    static XYZ AxA { get { return Sw ? Up : Rg; } }
    static XYZ AxC { get { return Sw ? Rg : Up; } }
    static double A(XYZ p) { return (p - O).DotProduct(AxA) * MM; }
    static double C(XYZ p) { return (p - O).DotProduct(AxC) * MM; }
    static bool Plan;
    static XYZ P(double a, double c) { var q = O + AxA * (a / MM) + AxC * (c / MM); return Plan ? new XYZ(q.X, q.Y, Z0) : q; }

    static JObject Call(UIApplication app, string name, JObject args)
    {
        var r = DynamicCommands.Run(app, name, args);
        return r == null ? new JObject() : JObject.FromObject(r);
    }

    static string Owner(Document doc, ElementId id)
    {
        try { return WorksharingUtils.GetWorksharingTooltipInfo(doc, id).LastChangedBy; } catch { return ""; }
    }

    static List<Ln> StairLines(Document doc, Stairs s)
    {
        var res = new List<Ln>(); var st = new Stack<GeometryElement>();
        st.Push(s.get_Geometry(new Options { ComputeReferences = true, View = V }));
        while (st.Count > 0)
        {
            var ge = st.Pop(); if (ge == null) continue;
            foreach (var g in ge)
            {
                if (g is GeometryInstance gi) { st.Push(gi.GetInstanceGeometry()); continue; }
                var l = g as Line; if (l == null || l.Reference == null) continue;
                if (Math.Abs(l.Direction.DotProduct(AxC)) < 0.99) continue;
                string stable; try { stable = l.Reference.ConvertToStableRepresentation(doc); } catch { continue; }
                double c0 = C(l.GetEndPoint(0)), c1 = C(l.GetEndPoint(1));
                res.Add(new Ln { A = A(l.GetEndPoint(0)), C0 = Math.Min(c0, c1), C1 = Math.Max(c0, c1), Len = l.Length * MM, Stable = stable });
            }
        }
        return res;
    }

    // hard rectangles of the existing annotations; text rectangles are kept apart for the leader test
    static void Obstacles(Document doc, List<Rc> hard, List<Rc> text)
    {
        hard.Clear(); text.Clear();
        double cw = 1.15 * S, th = 2.8 * S;
        foreach (var e in new FilteredElementCollector(doc, V.Id).WhereElementIsNotElementType())
        {
            try
            {
                if (e is Dimension d)
                {
                    var ln = d.Curve as Line; var bb0 = d.get_BoundingBox(V); if (ln == null || bb0 == null) continue;
                    bool horizontal = Math.Abs(ln.Direction.DotProduct(AxA)) > 0.99;
                    double a0 = A(bb0.Min), a1 = A(bb0.Max), c0 = C(bb0.Min), c1 = C(bb0.Max);
                    double on = horizontal ? C(ln.Origin) : A(ln.Origin);
                    hard.Add(horizontal ? new Rc(a0, a1, on - 30, on + 30) : new Rc(on - 30, on + 30, c0, c1));
                    Action<XYZ, string> addText = (tp, s) =>
                    {
                        double w = Math.Max(3, s.Length) * cw, x = A(tp), y = C(tp);
                        var r = horizontal ? new Rc(x - w / 2, x + w / 2, y - th / 2, y + th / 2) : new Rc(x - th / 2, x + th / 2, y - w / 2, y + w / 2);
                        hard.Add(r); text.Add(r);
                    };
                    if (d.NumberOfSegments > 1) { foreach (DimensionSegment g in d.Segments) addText(g.TextPosition, (g.Prefix ?? "") + (g.ValueString ?? "") + (g.Suffix ?? "")); }
                    else addText(d.TextPosition, (d.Prefix ?? "") + (d.ValueString ?? "") + (d.Suffix ?? ""));
                    continue;
                }
                if (e is IndependentTag t)
                {
                    var bbt = t.get_BoundingBox(V); if (bbt == null) continue;
                    if (t.HasLeader)
                    {
                        var h = t.TagHeadPosition; string tx = t.TagText ?? ""; double w = Math.Max(4, tx.Length) * cw * 0.55, hh = 1.9 * S;
                        if (Sw) { double sw0 = w; w = hh; hh = sw0; }
                        hard.Add(new Rc(A(h) - w, A(h) + w, C(h) - hh, C(h) + hh));
                        hard.Add(new Rc(A(h) - 20, A(h) + 20, C(bbt.Min), C(bbt.Max)));
                    }
                    else hard.Add(new Rc(A(bbt.Min), A(bbt.Max), C(bbt.Min), C(bbt.Max)));
                    continue;
                }
                if (e is SpotDimension || e is TextNote || e is AnnotationSymbol)
                {
                    var bb = e.get_BoundingBox(V); if (bb != null) hard.Add(new Rc(A(bb.Min), A(bb.Max), C(bb.Min), C(bb.Max)));
                }
            }
            catch { }
        }
    }

    // the project has one run tag type per base level: take the type of the family whose text reads the flight's elevations
    static FamilySymbol RunTagFor(Document doc, FamilySymbol baseType, Element run, double fromEl, List<string> ask)
    {
        if (baseType == null) return null;
        var fam = baseType.Family.GetFamilySymbolIds().Select(i => doc.GetElement(i) as FamilySymbol).Where(s => s != null).ToList();
        string kind = baseType.Name.Contains("RISER") ? "RISER" : baseType.Name.Contains("TREAD") ? "TREAD" : "";
        var order = new[] { baseType }.Concat(fam.Where(s => s.Id != baseType.Id && s.Name.Contains(kind))).ToList();
        foreach (var s in order)
        {
            using (var st = new SubTransaction(doc))
            {
                st.Start(); string text = null;
                try
                {
                    if (!s.IsActive) s.Activate();
                    var g = IndependentTag.Create(doc, s.Id, V.Id, new Reference(run), false, TagOrientation.Horizontal, P(0, 0));
                    doc.Regenerate(); text = g.TagText;
                }
                catch { }
                st.RollBack();
                var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"From EL \+?(-?[\d.]+)");
                if (m.Success && Math.Abs(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - fromEl) < 2) return s;
            }
        }
        ask.Add("không có type tag vế nào ghi đúng From EL " + Math.Round(fromEl));
        return baseType;
    }

    static double[] Measure(Document doc, FamilySymbol type, Element e)
    {
        using (var st = new SubTransaction(doc))
        {
            st.Start();
            double[] res = null;
            try
            {
                var g = IndependentTag.Create(doc, type.Id, V.Id, new Reference(e), false, TagOrientation.Horizontal, P(0, 0));
                doc.Regenerate();
                var bb = g.get_BoundingBox(V);
                if (bb != null) res = new[] { Math.Abs(A(bb.Max) - A(bb.Min)), Math.Abs(C(bb.Max) - C(bb.Min)) };
            }
            catch { }
            st.RollBack();
            return res;
        }
    }

    public static object Run(UIApplication app, JObject args)
    {
        Module = "";
        var result = RunModule(app, args);
        Record(app, args, result);
        return result;
    }

    // The last result of each view, for the Workflow window of the add-in (lib/workflow_lib.py). Never fails the run.
    static string Module = "";
    static void Record(UIApplication app, JObject args, object result)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "claude-using-revit", "view-develop");
            var doc = app.ActiveUIDocument.Document;
            string name = (args["viewId"] ?? "0") + "-" + string.Concat(doc.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json";
            string mode = (string)args["mode"] ?? "preview";
            if (mode == "undo") { if (File.Exists(Path.Combine(dir, name))) File.Delete(Path.Combine(dir, name)); return; }
            var o = JObject.FromObject(result);
            o["viewId"] = args["viewId"]; o["doc"] = doc.Title; o["module"] = Module; o["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name), o.ToString());
        }
        catch { }
    }

    static object RunModule(UIApplication app, JObject args)
    {
        var sw = Stopwatch.StartNew();
        var doc = app.ActiveUIDocument.Document;
        if (args["profile"] is JObject prof) foreach (var kv in prof) if (args[kv.Key] == null) args[kv.Key] = kv.Value;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"], outPath = (string)args["outPath"];
        if (mode == "undo")
        {
            var ids0 = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); int gone = 0;
            using (var t = new Transaction(doc, "Undo develop view"))
            {
                t.Start();
                foreach (var id in ids0) if (doc.GetElement(new ElementId(id)) != null) { doc.Delete(new ElementId(id)); gone++; }
                t.Commit();
            }
            return new { Mode = "undo", Deleted = gone };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        if (doc.IsFamilyDocument) return new { Error = "Revit is on the family " + doc.Title + ": wait until the user is back on the project" };
        V = doc.GetElement(new ElementId((int)args["viewId"])) as View;
        if (V == null) return new { Error = "view not found in " + doc.Title };
        Sw = false;
        var kinds = new[] { ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.Section, ViewType.Elevation };
        if (!kinds.Contains(V.ViewType)) return new { View = V.Name, Error = "no module for " + V.ViewType };
        O = V.Origin; Rg = V.RightDirection; Up = V.UpDirection; Z0 = V.Origin.Z; S = V.Scale; Plan = V.ViewType == ViewType.FloorPlan;
        Plan = V.ViewType == ViewType.FloorPlan || V.ViewType == ViewType.CeilingPlan;
        string stairText = ((string)args["stairViewText"] ?? "STAIR").ToUpper();
        bool stairView = V.Name.ToUpper().Contains(stairText);
        Module = !stairView ? (V.ViewType == ViewType.CeilingPlan ? "ceiling" : Plan ? "plan" : "building") : V.ViewType == ViewType.CeilingPlan ? "ceiling" : "stair-plan";
        if (!stairView) return Plan ? PlanGeneralModule(app, doc, args, mode, logPath, outPath, sw) : BuildingModule(app, doc, args, mode, logPath, outPath, sw);
        if (!Plan)
        {
            // along or across the flights: the direction of the seen runs against the view
            var seenRuns = new FilteredElementCollector(doc, V.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>().SelectMany(st => st.GetStairsRuns()).Select(i => doc.GetElement(i) as StairsRun).Where(r => r != null).ToList();
            bool across = seenRuns.Count > 0 && seenRuns.All(r => { var pc = r.GetStairsPath().Cast<Curve>().ToList(); var d = (pc.Last().GetEndPoint(1) - pc.First().GetEndPoint(0)).Normalize(); return Math.Abs(d.DotProduct(Rg)) < 0.5; });
            Module = across ? "stair-cross" : "stair-along";
            return across ? CrossModule(app, doc, args, mode, logPath, outPath, sw) : SectionModule(app, doc, args, mode, logPath, outPath, sw);
        }
        if (V.ViewType == ViewType.CeilingPlan) return PlanGeneralModule(app, doc, args, mode, logPath, outPath, sw);
        string me = (string)args["me"] ?? "", suffix = (string)args["suffix"] ?? "";
        string dimName = ((string)args["dimType"] ?? "") + suffix;
        string tmp = Path.Combine(Path.GetTempPath(), "view_develop"); Directory.CreateDirectory(tmp);
        string aPath = Path.Combine(tmp, V.Id.IntegerValue + "-audit.json"), snap = Path.Combine(tmp, V.Id.IntegerValue + "-snap.json");

        var placed = new Dictionary<string, int> { ["path"] = 0, ["runTags"] = 0, ["railTags"] = 0, ["runChains"] = 0, ["wallDims"] = 0, ["marked"] = 0 };
        var ask = new List<string>(); var errors = new List<string>(); var undrawn = new List<string>(); var created = new List<int>(); var plan = new JArray();

        Call(app, "stair_plan_audit", new JObject { ["viewId"] = V.Id.IntegerValue, ["outPath"] = aPath });
        var au = JObject.Parse(File.ReadAllText(aPath));
        if (au["Runs"] == null || !au["Runs"].Any()) return new { View = V.Name, Error = "no stair run seen" };
        Sw = !((string)au["Travel"] ?? "").Contains("horizontal");
        string cPlus = Sw ? "right" : "top", cMinus = Sw ? "left" : "bottom";

        // ---- problem 1: parameters
        var runs = new List<RunD>();
        foreach (var r in au["Runs"])
        {
            var run = doc.GetElement(new ElementId((int)r["Id"])) as StairsRun; if (run == null) continue;
            var fp = new List<XYZ>(); foreach (Curve c in run.GetFootprintBoundary()) fp.AddRange(c.Tessellate());
            var pc = run.GetStairsPath().Cast<Curve>().ToList();
            int sign = A(pc.Last().GetEndPoint(1)) > A(pc.First().GetEndPoint(0)) ? 1 : -1;
            var s = run.GetStairs();
            var d = new RunD { R = run, S = s, Label = (string)r["Label"], State = (string)r["State"], T = (int)r["Treads"], Depth = s.ActualTreadDepth * MM, Sign = sign, A0 = fp.Min(q => A(q)), A1 = fp.Max(q => A(q)), C0 = fp.Min(q => C(q)), C1 = fp.Max(q => C(q)) };
            d.AStart = sign > 0 ? d.A0 : d.A1; d.ALast = d.AStart + sign * d.T * d.Depth;
            runs.Add(d);
        }
        Func<string, JToken> wall = side => au["Walls"].FirstOrDefault(w => (string)w["Side"] == side);
        Func<string, bool> open = side => wall(side) == null || wall(side)["FinishFaceMm"] == null || wall(side)["FinishFaceMm"].Type == JTokenType.Null;
        var lanes = au["Lanes"].ToList();
        Func<RunD, string> sideOf = rd =>
        {
            if (lanes.Count <= 1 && open(cPlus) != open(cMinus)) return open(cPlus) ? cPlus : cMinus;
            var lane = lanes.FirstOrDefault(l => ((string)l["Runs"] ?? "").Split('/').Contains(rd.Label)) ?? lanes.FirstOrDefault();
            return (string)lane?["DimSide"] ?? cMinus;
        };

        foreach (var e in au["Expected"] ?? new JArray())
        {
            if (((string)e["Status"] ?? "").StartsWith("no CLEAR") && e["DimId"] != null && e["DimId"].Type != JTokenType.Null)
            {
                var id = new ElementId((int)e["DimId"]); string who = Owner(doc, id);
                if (who != me) ask.Add("dim " + id.IntegerValue + " thiếu CLEAR, của " + who);
            }
        }
        var handLines = new FilteredElementCollector(doc, V.Id).OfClass(typeof(CurveElement)).Cast<CurveElement>().Where(l =>
        {
            try { var q = l.GeometryCurve.Evaluate(0.5, true); return runs.Any(r => A(q) > r.A0 - 400 && A(q) < r.A1 + 400 && C(q) > r.C0 && C(q) < r.C1); } catch { return false; }
        }).ToList();
        if (handLines.Count > 0) ask.Add(handLines.Count + " detail line trên vế thang, của " + string.Join(", ", handLines.Select(l => Owner(doc, l.Id)).Distinct()) + ": nghi là path vẽ tay, sẽ chồng lên stair path");

        var tg = new TransactionGroup(doc, "Develop view"); tg.Start();
        try
        {
            Call(app, "mark_types", new JObject { ["action"] = "snapshot", ["viewIds"] = new JArray(V.Id.IntegerValue), ["statePath"] = snap });
            if (!string.IsNullOrEmpty(suffix) && args["dimType"] != null) Call(app, "mark_types", new JObject { ["action"] = "ensure", ["suffix"] = suffix, ["types"] = new JArray((string)args["dimType"]) });

            // ---- stair paths
            if ((au["Issues"] ?? new JArray()).Any(i => ((string)i).StartsWith("SD: stairs")) && args["pathType"] != null)
            {
                var pr = Call(app, "stair_plan_annotate", new JObject { ["viewId"] = V.Id.IntegerValue, ["mode"] = "apply", ["parts"] = new JArray("path"), ["pathTypeName"] = args["pathType"], ["logPath"] = Path.Combine(tmp, V.Id.IntegerValue + "-path.json") });
                foreach (var dn in pr["Done"] ?? new JArray()) if (dn["Created"] != null) { created.Add((int)dn["Created"]); placed["path"]++; }
                foreach (var er in pr["Errors"] ?? new JArray()) errors.Add("path: " + er);
            }

            using (var t = new Transaction(doc, "Develop view annotations"))
            {
                t.Start();
                var dimType = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == dimName && x.StyleType == DimensionStyleType.Linear);
                Func<string, FamilySymbol> sym = n => n == null ? null : new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(x => x.Name == n);
                var runTag = sym((string)args["runTagType"]); var railTag = sym((string)args["railTagType"]);
                if (runTag != null && !runTag.IsActive) runTag.Activate();
                if (railTag != null && !railTag.IsActive) railTag.Activate();
                doc.Regenerate();

                var hard = new List<Rc>(); var text = new List<Rc>(); Obstacles(doc, hard, text);
                var grids = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Grid)).Cast<Grid>().Select(g => g.Curve as Line).Where(l => l != null && Math.Abs(l.Direction.DotProduct(AxC)) > 0.99).Select(l => A(l.Origin)).ToList();
                var linesOf = runs.Select(r => r.S).GroupBy(s => s.Id.IntegerValue).ToDictionary(g => g.Key, g => StairLines(doc, g.First()));

                foreach (var group in runs.GroupBy(sideOf))
                {
                    int sg = group.Key == cPlus ? 1 : -1;
                    var gr = group.ToList();
                    double la0 = gr.Min(r => r.A0), la1 = gr.Max(r => r.A1);
                    double edge = sg > 0 ? gr.Max(r => r.C1) : gr.Min(r => r.C0);
                    Func<double> extent = () =>
                    {
                        double x = edge;
                        foreach (var h in hard)
                        {
                            if (h.R < la0 || h.L > la1) continue;
                            double outer = sg > 0 ? h.T : h.B, inner = sg > 0 ? h.B : h.T;
                            if (sg * (inner - edge) < -60) continue;              // on the stair or beyond it
                            if (sg * (inner - edge) > 40 * S) continue;           // far away: not part of this band
                            if (sg * (outer - x) > 0) x = outer;
                        }
                        return x;
                    };

                    // tries heads along the run, nearest the wanted place first; hard rules first, soft as cost
                    Func<double, double, double, double, double, Func<double, bool>, double?> solve = (want, a0, a1, w, c, okEnd) =>
                    {
                        double? best = null; double bestCost = 1e12; double hh = 0;
                        for (double a = a0; a <= a1; a += 1.4 * S)
                        {
                            var head = new Rc(a - w / 2, a + w / 2, c - 2.0 * S, c + 2.0 * S);
                            if (hard.Any(h => h.Hit(head, 20))) continue;
                            var lead = new Rc(a - 15, a + 15, Math.Min(c, edge), Math.Max(c, edge));
                            if (text.Any(x => x.Hit(lead, 30))) continue;
                            if (okEnd != null && !okEnd(a)) continue;
                            double cost = Math.Abs(a - want);
                            if (grids.Any(g => g > head.L - 60 && g < head.R + 60)) cost += 4000;
                            if (grids.Any(g => Math.Abs(g - a) < 150)) cost += 2000;
                            if (cost < bestCost) { bestCost = cost; best = a; }
                        }
                        return best;
                    };

                    // ---- railing tags: one tag, the rails of a single lane share it
                    if (railTag != null)
                    {
                        double zLo = gr.Min(r => r.R.BaseElevation + r.S.BaseElevation), zHi = gr.Max(r => r.R.TopElevation + r.S.BaseElevation);
                        var rails = (au["Tags"]?["Railings"] ?? new JArray()).Where(x => x["Tags"] == null || !x["Tags"].Any())
                            .Select(x => doc.GetElement(new ElementId((int)x["Railing"]))).Where(x => x != null)
                            .Where(x => { var b = x.get_BoundingBox(null); return b != null && b.Max.Z >= zLo - 0.01 && b.Min.Z <= zHi + 4; }).ToList();
                        if (rails.Count > 0)
                        {
                            var size = Measure(doc, railTag, rails[0]) ?? new[] { 300.0, 175.0 };
                            double c = sg > 0 ? Math.Max(extent(), edge) + sg * (3 * S + size[1] / 2) : Math.Min(extent(), edge) + sg * (3 * S + size[1] / 2);
                            var r0 = gr.First();
                            double? a = solve((r0.A0 + r0.A1) / 2, r0.A0 + size[0], r0.A1 - size[0], size[0], c, null);
                            if (a == null) ask.Add("không tìm được chỗ cho tag lan can");
                            else
                            {
                                var tg1 = IndependentTag.Create(doc, railTag.Id, V.Id, new Reference(rails[0]), true, TagOrientation.Horizontal, P(a.Value, c));
                                tg1.TagHeadPosition = P(a.Value, c);
                                if (rails.Count > 1 && lanes.Count == 1) tg1.AddReferences(rails.Skip(1).Select(x => new Reference(x)).ToList());
                                created.Add(tg1.Id.IntegerValue); placed["railTags"]++;
                                hard.Add(new Rc(a.Value - size[0] / 2, a.Value + size[0] / 2, c - size[1] / 2, c + size[1] / 2));
                                hard.Add(new Rc(a.Value - 15, a.Value + 15, Math.Min(c, edge), Math.Max(c, edge)));
                                plan.Add(new JObject { ["kind"] = "railTag", ["id"] = tg1.Id.IntegerValue, ["a"] = a.Value, ["c"] = c });
                            }
                        }
                    }

                    // ---- run tags: one band, level with each other
                    var need = gr.Where(r => ((au["Tags"]?["Runs"] ?? new JArray()).FirstOrDefault(x => (int)x["RunId"] == r.R.Id.IntegerValue)?["Tags"] ?? new JArray()).Any() == false).ToList();
                    double outer1 = extent();
                    if (runTag != null && need.Count > 0)
                    {
                        var sizes = need.ToDictionary(r => r, r => Measure(doc, runTag, r.R) ?? new[] { 1700.0, 320.0 });
                        double hMax = sizes.Values.Max(x => x[1]);
                        double c = outer1 + sg * (3 * S + hMax / 2);
                        foreach (var r in need)
                        {
                            double endC = (sg > 0 ? r.C1 : r.C0) - sg * 130;
                            var lines = linesOf[r.S.Id.IntegerValue].Where(l => l.A >= r.A0 - 1 && l.A <= r.A1 + 1).ToList();
                            // the leader must end on a part of the run the view draws (a cut run has a break line)
                            Func<double, bool> seen = x => lines.Any(l => Math.Abs(l.A - x) < r.Depth && l.C0 <= endC && l.C1 >= endC);
                            double? a = solve((r.A0 + r.A1) / 2, r.A0 + 100, r.A1 - 100, sizes[r][0], c, seen);
                            if (a == null) { ask.Add("không tìm được chỗ cho tag vế " + r.Label); continue; }
                            var runType = RunTagFor(doc, runTag, r.R, r.R.BaseElevation * MM + r.S.BaseElevation * MM, ask);
                            var tg2 = IndependentTag.Create(doc, runType.Id, V.Id, new Reference(r.R), true, TagOrientation.Horizontal, P(a.Value, c));
                            tg2.LeaderEndCondition = LeaderEndCondition.Free;
                            tg2.SetLeaderEnd(new Reference(r.R), P(a.Value, endC));
                            tg2.TagHeadPosition = P(a.Value, c);
                            created.Add(tg2.Id.IntegerValue); placed["runTags"]++;
                            hard.Add(new Rc(a.Value - sizes[r][0] / 2, a.Value + sizes[r][0] / 2, c - sizes[r][1] / 2, c + sizes[r][1] / 2));
                            hard.Add(new Rc(a.Value - 15, a.Value + 15, Math.Min(c, endC), Math.Max(c, endC)));
                            plan.Add(new JObject { ["kind"] = "runTag", ["id"] = tg2.Id.IntegerValue, ["run"] = r.Label, ["a"] = a.Value, ["c"] = c });
                        }
                        outer1 = c + sg * hMax / 2;
                    }
                    else outer1 = extent();

                    // ---- run length chain with the formula, outside the tags
                    var hasFormula = new HashSet<long>();
                    foreach (var d0 in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
                    {
                        Action<string, double?> take = (pre, val) => { if ((pre ?? "").Contains("mm x") && val != null) hasFormula.Add((long)Math.Round(val.Value * MM)); };
                        if (d0.NumberOfSegments > 1) { foreach (DimensionSegment g in d0.Segments) take(g.Prefix, g.Value); } else take(d0.Prefix, d0.Value);
                    }
                    var chainRuns = gr.Where(r => r.State != "beyond cut" && !hasFormula.Contains((long)Math.Round(r.T * r.Depth))).ToList();
                    if (dimType != null && chainRuns.Count > 0)
                    {
                        var all = chainRuns.SelectMany(r => linesOf[r.S.Id.IntegerValue]).ToList();
                        var pts = new SortedDictionary<double, string>();
                        var missing = new List<string>();
                        foreach (var r in chainRuns)
                            foreach (var pos in new[] { r.AStart, r.ALast })
                            {
                                var l = all.Where(x => Math.Abs(x.A - pos) < 3 && x.Len > (r.C1 - r.C0) * 0.5).OrderBy(x => Math.Abs(x.A - pos)).FirstOrDefault();
                                if (l == null) missing.Add(r.Label); else pts[Math.Round(l.A, 1)] = l.Stable;
                            }
                        if (missing.Count > 0) ask.Add("vế " + string.Join(", ", missing.Distinct()) + ": cổ bậc nằm trên mặt cắt không vẽ, không dim chiều dài được ở view này");
                        double width = chainRuns.Max(r => r.C1 - r.C0);
                        var far = all.Where(x => x.Len > width * 0.8).OrderBy(x => x.A).ToList();
                        // a cut run: what lies beyond the cut is not drawn, so no landing segment to it
                        if (pts.Count >= 2 && far.Count > 0 && chainRuns.All(r => r.State == "full"))
                        {
                            if (far.First().A < pts.Keys.First() - 50) pts[Math.Round(far.First().A, 1)] = far.First().Stable;
                            if (far.Last().A > pts.Keys.Last() + 50) pts[Math.Round(far.Last().A, 1)] = far.Last().Stable;
                        }
                        if (pts.Count >= 2)
                        {
                            double line = outer1 + sg * 8 * S;
                            var ra = new ReferenceArray(); foreach (var kv in pts) ra.Append(Reference.ParseFromStableRepresentation(doc, kv.Value));
                            var dm = doc.Create.NewDimension(V, Line.CreateBound(P(pts.Keys.First(), line), P(pts.Keys.Last(), line)), ra, dimType);
                            doc.Regenerate();
                            var box = dm.get_BoundingBox(V);
                            if (box == null || Math.Abs(A(box.Max) - A(box.Min)) < 10) { undrawn.Add("chuỗi chiều dài vế " + string.Join(",", chainRuns.Select(r => r.Label))); doc.Delete(dm.Id); }
                            else
                            {
                                foreach (var r in chainRuns)
                                {
                                    string pre = Math.Round(r.Depth) + "mm x " + r.T + "T =";
                                    if (dm.NumberOfSegments > 1)
                                    {
                                        var seg = dm.Segments.Cast<DimensionSegment>().FirstOrDefault(g => Math.Abs((g.Value ?? 0) * MM - r.T * r.Depth) < 1 && string.IsNullOrEmpty(g.Prefix));
                                        if (seg != null) { seg.Prefix = pre; seg.Below = "(EQUAL TREADS)"; }
                                    }
                                    else if (Math.Abs((dm.Value ?? 0) * MM - r.T * r.Depth) < 1) { dm.Prefix = pre; dm.Below = "(EQUAL TREADS)"; }
                                }
                                created.Add(dm.Id.IntegerValue); placed["runChains"]++;
                                plan.Add(new JObject { ["kind"] = "runChain", ["id"] = dm.Id.IntegerValue, ["c"] = line });
                            }
                        }
                    }
                }
                t.Commit();
            }

            // ---- wall thickness, on the column of the existing across dims
            if (args["dimType"] != null)
                foreach (var side in new[] { cPlus, cMinus })
                {
                    var w = wall(side); if (open(side)) continue;
                    bool missing = (au["Expected"] ?? new JArray()).Any(e => (string)e["Rule"] == "SA4" && ((string)e["Item"] ?? "").StartsWith("wall " + side) && (string)e["Status"] == "missing");
                    if (!missing) continue;
                    double finish = (double)w["FinishFaceMm"], outerF = (double)w["OuterFaceMm"];
                    double lineA = runs.Max(r => r.A1) + 400;
                    foreach (var d0 in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
                    {
                        var ln = d0.Curve as Line; if (ln == null || Math.Abs(ln.Direction.DotProduct(AxC)) < 0.99) continue;
                        double on = A(ln.Origin); if (on > runs.Max(r => r.A1) && on < runs.Max(r => r.A1) + 1500) lineA = on;
                    }
                    var positions = new JArray(new JObject { ["mm"] = Math.Min(finish, outerF), ["src"] = "3d" }, new JObject { ["mm"] = Math.Max(finish, outerF), ["src"] = "3d" });
                    foreach (var g in au["Grids"] ?? new JArray())
                    {
                        if (!((string)g["Measures"] ?? "").StartsWith("across")) continue;
                        double pos = (double)g["PosMm"];
                        if (Math.Sign(outerF - finish) == Math.Sign(pos - outerF) && Math.Abs(pos - outerF) < 3000) positions.Add(pos);
                    }
                    var res = Call(app, "dims_at_positions", new JObject
                    {
                        ["viewId"] = V.Id.IntegerValue, ["mode"] = "apply", ["typeName"] = dimName, ["use3D"] = true,
                        ["logPath"] = Path.Combine(tmp, V.Id.IntegerValue + "-sa4.json"),
                        ["dims"] = new JArray(new JObject { ["name"] = "wall " + side, ["measure"] = Sw ? "right" : "up", ["lineMm"] = lineA, ["positions"] = positions })
                    });
                    foreach (var dn in res["Done"] ?? new JArray()) if (dn["Id"] != null) { created.Add((int)dn["Id"]); placed["wallDims"]++; }
                    foreach (var er in res["Errors"] ?? new JArray()) errors.Add("SA4: " + er);
                }

            StairPlanRest(app, doc, args, au, runs, created, placed, ask, errors, tmp, sw);

            if (!string.IsNullOrEmpty(suffix))
            {
                var mk = Call(app, "mark_types", new JObject { ["action"] = "mark", ["suffix"] = suffix, ["statePath"] = snap, ["mode"] = "apply", ["logPath"] = Path.Combine(tmp, V.Id.IntegerValue + "-mark.json") });
                placed["marked"] = (int?)mk["New"] ?? 0;
            }

            // ---- check: what is still open, what overlaps
            Call(app, "stair_plan_audit", new JObject { ["viewId"] = V.Id.IntegerValue, ["outPath"] = aPath });
            var au2 = JObject.Parse(File.ReadAllText(aPath));
            var left = (au2["Issues"] ?? new JArray()).Select(i => (string)i).ToList();
            var ov = Call(app, "annotation_overlaps", new JObject { ["viewId"] = V.Id.IntegerValue });
            var ovIds = (ov["Ids"] ?? new JArray()).Select(x => (int)x).ToList();
            int ovNew = ovIds.Count(created.Contains);

            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, new JObject { ["view"] = V.Name, ["plan"] = plan, ["left"] = new JArray(left), ["overlapIds"] = new JArray(ovIds) }.ToString());
            var imgCx = NewCx(app, doc, args, mode, logPath, outPath, sw); string img = Image(imgCx); errors.AddRange(imgCx.Errors);
            if (mode == "apply") { tg.Assimilate(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else tg.RollBack();

            return new
            {
                View = V.Name, Mode = mode, Placed = placed.Where(k => k.Value > 0).ToDictionary(k => k.Key, k => k.Value),
                Ask = ask, Undrawn = undrawn, Errors = errors,
                Left = left.GroupBy(x => x.Split(':')[0]).ToDictionary(g => g.Key, g => g.Count()),
                Overlaps = new { Total = (int?)ov["Pairs"] ?? 0, WithNew = ovNew }, Image = img,
                Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1)
            };
        }
        catch (Exception ex)
        {
            if (tg.HasStarted()) tg.RollBack();
            return new { View = V.Name, Mode = mode, Error = ex.GetType().Name + ": " + ex.Message, Line = (ex.StackTrace ?? "").Split('\n').FirstOrDefault(), Placed = placed };
        }
    }

    // ---- section along the flights
    class Fl { public StairsRun R; public Stairs S; public string Label; public double Z0, Z1, A0, A1, Foot, Top; public int Dir, T; public double Depth; public bool Tagged; }

    static object SectionModule(UIApplication app, Document doc, JObject args, string mode, string logPath, string outPath, Stopwatch sw)
    {
        string suffix = (string)args["suffix"] ?? "", me = (string)args["me"] ?? "", dimName = ((string)args["dimType"] ?? "") + suffix;
        string tmp = Path.Combine(Path.GetTempPath(), "view_develop"); Directory.CreateDirectory(tmp);
        int vid = V.Id.IntegerValue;
        string aPath = Path.Combine(tmp, vid + "-sec.json"), snap = Path.Combine(tmp, vid + "-snap.json");
        var placed = new Dictionary<string, int>(); var ask = new List<string>(); var errors = new List<string>(); var created = new List<int>();
        Action<string, int> add = (k, n) => { placed[k] = (placed.ContainsKey(k) ? placed[k] : 0) + n; };

        Call(app, "stair_section_info", new JObject { ["viewId"] = vid, ["outPath"] = aPath });
        var au = JObject.Parse(File.ReadAllText(aPath));
        var fls = new List<Fl>();
        foreach (var f in au["Flights"] ?? new JArray())
        {
            if ((string)f["State"] != "cut") continue;
            var run = doc.GetElement(new ElementId((int)f["Id"])) as StairsRun; if (run == null) continue;
            var s = run.GetStairs(); var fp = new List<XYZ>(); foreach (Curve c in run.GetFootprintBoundary()) fp.AddRange(c.Tessellate());
            var pc = run.GetStairsPath().Cast<Curve>().ToList();
            int dir = A(pc.Last().GetEndPoint(1)) > A(pc.First().GetEndPoint(0)) ? 1 : -1;
            var d = new Fl { R = run, S = s, Label = (string)f["Label"], Z0 = (double)f["FromEl"], Z1 = (double)f["ToEl"], A0 = fp.Min(q => A(q)), A1 = fp.Max(q => A(q)), Dir = dir, T = (int)f["Treads"], Depth = s.ActualTreadDepth * MM, Tagged = (f["RunTags"] ?? new JArray()).Any() };
            d.Foot = dir > 0 ? d.A0 : d.A1; d.Top = d.Foot + dir * d.T * d.Depth;
            fls.Add(d);
        }
        if (fls.Count == 0) return CrossModule(app, doc, args, mode, logPath, outPath, sw);

        var tg = new TransactionGroup(doc, "Develop view"); tg.Start();
        try
        {
            Call(app, "mark_types", new JObject { ["action"] = "snapshot", ["viewIds"] = new JArray(vid), ["statePath"] = snap });
            if (!string.IsNullOrEmpty(suffix) && args["dimType"] != null) Call(app, "mark_types", new JObject { ["action"] = "ensure", ["suffix"] = suffix, ["types"] = new JArray((string)args["dimType"]) });

            Func<JObject, string, JObject> annotate = (extra, log) =>
            {
                var a = new JObject { ["viewId"] = vid, ["mode"] = "apply", ["dimTypeName"] = dimName, ["logPath"] = Path.Combine(tmp, vid + "-" + log + ".json") };
                foreach (var kv in extra) a[kv.Key] = kv.Value;
                var r = Call(app, "stair_section_annotate", a);
                foreach (var dn in r["Done"] ?? new JArray()) if (dn["Id"] != null) { created.Add((int)dn["Id"]); add((string)dn["Part"], 1); }
                foreach (var er in r["Errors"] ?? new JArray()) if (!((string)er).StartsWith("inner wall")) errors.Add((string)er);
                return r;
            };

            // ---- dims first: they fix where the tags can go
            var first = new JObject { ["parts"] = new JArray("LA5", "LC", "LB3"), ["la5TargetMm"] = args["la5TargetMm"] ?? 1200 };
            if (args["numberSourceId"] != null) first["numberSourceId"] = args["numberSourceId"];
            if (args["spotType"] != null) first["spotTypeName"] = args["spotType"];
            annotate(first, "a");
            // only what is missing: LA3 by the audit; LA4 has no audit row, so an existing vertical dim inside the stair
            // that is not a rail height counts as done
            bool needLa3 = (au["Dims"] ?? new JArray()).Any(x => (string)x["Rule"] == "LA3" && !((string)x["Status"] ?? "").StartsWith("OK"));
            double sa0 = fls.Min(f => f.A0), sa1 = fls.Max(f => f.A1);
            bool needLa4 = !new FilteredElementCollector(doc, V.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Any(d0 =>
            {
                var ln = d0.Curve as Line; if (ln == null || Math.Abs(ln.Direction.DotProduct(Up)) < 0.99 || d0.NumberOfSegments > 1) return false;
                double on = A(ln.Origin), val = (d0.Value ?? 0) * MM; bool onStairs = false;
                foreach (Reference rf in d0.References) if (fls.Any(f => f.S.Id == rf.ElementId)) onStairs = true;
                return onStairs && on > sa0 - 3000 && on < sa1 + 3000 && val > 1400 && Math.Abs(val - (fls.Max(f => f.Z1) - fls.Min(f => f.Z0))) > 5;
            });
            var la = new JArray(); if (needLa3) la.Add("LA3"); if (needLa4) la.Add("LA4");
            bool noWalls = false;
            if (la.Count > 0)
            {
                var withWalls = annotate(new JObject { ["parts"] = la }, "b");
                noWalls = (withWalls["Errors"] ?? new JArray()).Any(x => ((string)x).StartsWith("inner wall"));
            }

            var hard = new List<Rc>(); var text = new List<Rc>();
            double zOff = C(new XYZ(O.X, O.Y, 0));      // view Up of elevation 0
            using (var t = new Transaction(doc, "Develop section"))
            {
                t.Start();
                var dimType = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(x => x.Name == dimName && x.StyleType == DimensionStyleType.Linear);
                if (noWalls && dimType != null)
                {
                    if (needLa3) foreach (var f in fls)
                    {
                        // LA3: the flight only (no wall to close on)
                        var faces = SecFaces(f.S);
                        Func<double, FaceD> at = x => faces.Where(q => Math.Abs(q.N.DotProduct(Rg)) > 0.999 && Math.Abs(q.A0 - x) < 3 && q.Z1 > f.Z0 - 5 && q.Z0 < f.Z1 + 5).OrderByDescending(q => q.Z1 - q.Z0).FirstOrDefault();
                        var a = at(f.Foot); var b = at(f.Top);
                        if (a == null || b == null) { errors.Add("LA3 " + f.Label + ": no riser face"); continue; }
                        double h = f.Z1 - f.Z0, slope = h / (f.T * f.Depth + f.Depth), lineZ = f.Z0 + Math.Max(0.31 * h, 12 * S);
                        var ra = new ReferenceArray(); ra.Append(a.P.Reference); ra.Append(b.P.Reference);
                        var d = doc.Create.NewDimension(V, Line.CreateBound(P(f.Foot, lineZ + zOff), P(f.Top, lineZ + zOff)), ra, dimType);
                        d.Prefix = Math.Round(f.Depth) + "mm x " + f.T + "T ="; d.Below = "(EQUAL TREADS)";
                        doc.Regenerate();
                        // the formula goes where the flight is above it: the line meets the soffit near the foot
                        double w = 20 * 1.15 * S, under = f.Foot + f.Dir * ((lineZ - f.Z0 + 4 * S) / slope + w / 2);
                        try { d.TextPosition = P(under, C(d.TextPosition)); } catch { }
                        created.Add(d.Id.IntegerValue); add("LA3", 1);
                    }
                    // LA4: floor to the soffit of each cut landing
                    var lv = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation * MM - fls.Min(f => f.Z0))).First();
                    if (needLa4) foreach (var l in au["Landings"] ?? new JArray())
                    {
                        if ((string)l["State"] != "cut") continue;
                        var ld = doc.GetElement(new ElementId((int)l["Id"])) as StairsLanding; if (ld == null) continue;
                        var s = ld.GetStairs(); var fp = new List<XYZ>(); foreach (Curve c in ld.GetFootprintBoundary()) fp.AddRange(c.Tessellate());
                        double a0 = fp.Min(q => A(q)), a1 = fp.Max(q => A(q)), z = (double)l["ElMm"], col = (a0 + a1) / 2;
                        var so = SecFaces(s).Where(q => q.N.Z < -0.999 && q.A0 <= col && q.A1 >= col && Math.Abs(q.Z0 - z) < 500).OrderBy(q => q.Z0).FirstOrDefault();
                        if (so == null) { errors.Add("LA4: no soffit at landing " + z); continue; }
                        var ra = new ReferenceArray(); ra.Append(lv.GetPlaneReference()); ra.Append(so.P.Reference);
                        var d = doc.Create.NewDimension(V, Line.CreateBound(P(col, lv.Elevation * MM + zOff), P(col, so.Z0 + zOff)), ra, dimType);
                        created.Add(d.Id.IntegerValue); add("LA4", 1);
                    }
                }
                t.Commit();
            }

            // ---- positions of the tags, now that the dims exist
            Obstacles(doc, hard, text);
            var fams = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            var runTag = fams.FirstOrDefault(x => x.Name == (string)args["runTagType"]);
            var railTag = fams.FirstOrDefault(x => x.Name == (string)args["railTagType"]);
            using (var t = new Transaction(doc, "Develop section tags"))
            {
                t.Start();
                foreach (var f in fls)
                {
                    double h = f.Z1 - f.Z0, slope = h / (f.T * f.Depth + f.Depth);
                    Func<double, double> soffit = x => Math.Min(f.Z0 + Math.Max(0, f.Dir * (x - f.Foot)) * slope, f.Z1 - 100) - 80;
                    if (!f.Tagged && runTag != null)
                    {
                        var tagType = RunTagFor(doc, runTag, f.R, f.Z0, ask);
                        var size = Measure(doc, tagType, f.R) ?? new[] { 1800.0, 360.0 };
                        double z = 0, best = double.NaN;
                        for (double zz = f.Z0 + 0.55 * h; zz <= f.Z0 + 0.8 * h && double.IsNaN(best); zz += 2 * S)
                            for (double a = f.Foot; Math.Abs(a - f.Foot) < f.T * f.Depth + 2500; a += f.Dir * 1.4 * S)
                            {
                                double footSide = a - f.Dir * size[0] / 2;
                                if (zz + size[1] / 2 + 60 > soffit(footSide)) continue;
                                var head = new Rc(a - size[0] / 2, a + size[0] / 2, zz + zOff - size[1] / 2, zz + zOff + size[1] / 2);
                                if (hard.Any(r => r.Hit(head, 30))) continue;
                                best = a; z = zz;
                                break;
                            }
                        if (double.IsNaN(best)) ask.Add("không tìm được chỗ cho tag vế " + f.Label);
                        else
                        {
                            var g = IndependentTag.Create(doc, tagType.Id, V.Id, new Reference(f.R), true, TagOrientation.Horizontal, P(best, z + zOff));
                            double endA = f.Foot + f.Dir * Math.Max(0, (z - f.Z0) / slope);
                            g.LeaderEndCondition = LeaderEndCondition.Free; g.SetLeaderEnd(new Reference(f.R), P(endA, z + zOff)); g.TagHeadPosition = P(best, z + zOff);
                            created.Add(g.Id.IntegerValue); add("LB1", 1);
                            hard.Add(new Rc(best - size[0] / 2, best + size[0] / 2, z + zOff - size[1] / 2, z + zOff + size[1] / 2));
                        }
                    }
                    if (railTag != null)
                    {
                        var rails = f.S.GetAssociatedRailings().Select(i => doc.GetElement(i)).Where(x => x != null).ToList();
                        if (rails.Count == 0) continue;
                        var vd = V.ViewDirection;
                        var rail = rails.OrderByDescending(x => { var b = x.get_BoundingBox(null); return b == null ? -1e9 : -((b.Min + b.Max) / 2 - O).DotProduct(vd); }).First();
                        double x0 = f.Foot + f.Dir * 0.2 * f.T * f.Depth;
                        double lo = Math.Min(f.Foot, f.Top) - 100, hi = Math.Max(f.Foot, f.Top) + 100;
                        bool has = new FilteredElementCollector(doc, V.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().Any(g => { try { return g.GetTaggedLocalElementIds().Contains(rail.Id) && A(g.TagHeadPosition) > lo && A(g.TagHeadPosition) < hi; } catch { return false; } });
                        if (has) continue;
                        double zr = f.Z0 + Math.Abs(x0 - f.Foot) * slope + 1300, zh = zr + 9 * S;
                        var gt = IndependentTag.Create(doc, railTag.Id, V.Id, new Reference(rail), true, TagOrientation.Horizontal, P(x0, zh + zOff));
                        gt.LeaderEndCondition = LeaderEndCondition.Free; gt.SetLeaderEnd(new Reference(rail), P(x0, zr + zOff)); gt.TagHeadPosition = P(x0, zh + zOff);
                        created.Add(gt.Id.IntegerValue); add("LB2", 1);
                    }
                }
                t.Commit();
            }

            if (!string.IsNullOrEmpty(suffix))
            {
                var mk = Call(app, "mark_types", new JObject { ["action"] = "mark", ["suffix"] = suffix, ["statePath"] = snap, ["mode"] = "apply", ["logPath"] = Path.Combine(tmp, vid + "-mark.json") });
                add("marked", (int?)mk["New"] ?? 0);
            }

            Call(app, "stair_section_info", new JObject { ["viewId"] = vid, ["outPath"] = aPath });
            var au2 = JObject.Parse(File.ReadAllText(aPath));
            var left = (au2["Issues"] ?? new JArray()).Select(i => (string)i).ToList();
            foreach (var row in au2["Dims"] ?? new JArray())
            {
                if (((string)row["Status"] ?? "").StartsWith("OK") || row["DimId"] == null || row["DimId"].Type == JTokenType.Null) continue;
                var id = new ElementId((int)row["DimId"]); string who = Owner(doc, id);
                if (who != me) { string q = "dim " + id.IntegerValue + " " + row["Rule"] + " thiếu công thức, của " + who; if (!ask.Contains(q)) ask.Add(q); }
            }
            var ov = Call(app, "annotation_overlaps", new JObject { ["viewId"] = vid });
            // a new vertical dim on something: slide it sideways, it keeps its references
            foreach (var nid in (ov["Ids"] ?? new JArray()).Select(x => (int)x).Where(created.Contains).ToList())
            {
                var dn = doc.GetElement(new ElementId(nid)) as Dimension; var ln = dn?.Curve as Line;
                if (ln == null || Math.Abs(ln.Direction.DotProduct(Up)) < 0.99) continue;
                foreach (var step in new[] { -650.0, 650.0, -1000.0, 1000.0 })
                {
                    using (var t = new Transaction(doc, "Develop section slide"))
                    {
                        t.Start(); ElementTransformUtils.MoveElement(doc, dn.Id, Rg * (step / MM)); t.Commit();
                    }
                    var ov2 = Call(app, "annotation_overlaps", new JObject { ["viewId"] = vid });
                    if (!(ov2["Ids"] ?? new JArray()).Select(x => (int)x).Contains(nid)) { ov = ov2; break; }
                    using (var t = new Transaction(doc, "Develop section slide back"))
                    {
                        t.Start(); ElementTransformUtils.MoveElement(doc, dn.Id, Rg * (-step / MM)); t.Commit();
                    }
                }
            }
            int ovNew = (ov["Ids"] ?? new JArray()).Select(x => (int)x).Count(created.Contains);
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, new JObject { ["view"] = V.Name, ["created"] = new JArray(created), ["left"] = new JArray(left) }.ToString());
            var imgCx = NewCx(app, doc, args, mode, logPath, outPath, sw); string img = Image(imgCx); errors.AddRange(imgCx.Errors);
            if (mode == "apply") { tg.Assimilate(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); } else tg.RollBack();
            return new { View = V.Name, Mode = mode, Placed = placed, Ask = ask, Errors = errors, Left = left.GroupBy(x => x.Split(':')[0]).ToDictionary(g => g.Key, g => g.Count()), Overlaps = new { Total = (int?)ov["Pairs"] ?? 0, WithNew = ovNew }, Image = img, Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1) };
        }
        catch (Exception ex)
        {
            if (tg.HasStarted()) tg.RollBack();
            return new { View = V.Name, Mode = mode, Error = ex.GetType().Name + ": " + ex.Message, Line = (ex.StackTrace ?? "").Split('\n').FirstOrDefault() };
        }
    }

    class FaceD { public PlanarFace P; public XYZ N; public double A0, A1, Z0, Z1; }
    static List<FaceD> SecFaces(Element e)
    {
        var res = new List<FaceD>();
        foreach (var opt in new[] { new Options { ComputeReferences = true, View = V }, new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine } })
        {
            var st = new Stack<GeometryElement>(); st.Push(e.get_Geometry(opt));
            while (st.Count > 0)
            {
                var ge = st.Pop(); if (ge == null) continue;
                foreach (var g in ge)
                {
                    if (g is GeometryInstance gi) { st.Push(gi.GetInstanceGeometry()); continue; }
                    var s = g as Solid; if (s == null) continue;
                    foreach (Face f in s.Faces)
                    {
                        var pf = f as PlanarFace; if (pf == null || pf.Reference == null) continue;
                        var pts = new List<XYZ>(); foreach (EdgeArray ea in pf.EdgeLoops) foreach (Edge ed in ea) pts.AddRange(ed.Tessellate());
                        if (pts.Count == 0) continue;
                        res.Add(new FaceD { P = pf, N = pf.FaceNormal, A0 = pts.Min(q => A(q)), A1 = pts.Max(q => A(q)), Z0 = pts.Min(q => q.Z * MM), Z1 = pts.Max(q => q.Z * MM) });
                    }
                }
            }
            if (res.Count > 0) break;
        }
        return res;
    }

    // ---- shared by the modules below
    class Cx
    {
        public UIApplication App; public Document Doc; public JObject A; public int Vid; public string Tmp, Suffix, Me, Dim, Mode, LogPath, OutPath; public Stopwatch Sw;
        public Dictionary<string, int> Placed = new Dictionary<string, int>(); public List<string> Ask = new List<string>(), Errors = new List<string>(); public List<int> Created = new List<int>();
        public Dictionary<string, int> Left = new Dictionary<string, int>(); public string Snap;
        public void Add(string k, int n) { if (n != 0) Placed[k] = (Placed.ContainsKey(k) ? Placed[k] : 0) + n; }
        public string Log(string s) { return Path.Combine(Tmp, Vid + "-" + s + ".json"); }
    }

    static Cx NewCx(UIApplication app, Document doc, JObject a, string mode, string logPath, string outPath, Stopwatch sw)
    {
        var x = new Cx { App = app, Doc = doc, A = a, Vid = V.Id.IntegerValue, Mode = mode, LogPath = logPath, OutPath = outPath, Sw = sw, Suffix = (string)a["suffix"] ?? "", Me = (string)a["me"] ?? "", Tmp = Path.Combine(Path.GetTempPath(), "view_develop") };
        Directory.CreateDirectory(x.Tmp); x.Dim = ((string)a["dimType"] ?? "") + x.Suffix; x.Snap = x.Log("snap"); return x;
    }

    // Revit is frozen for the user while a command runs: past the budget the remaining steps are skipped and reported
    static JObject Step(Cx x, string name, string cmd, JObject a)
    {
        double budget = (double?)x.A["maxSeconds"] ?? 120;
        if (x.Sw != null && x.Sw.Elapsed.TotalSeconds > budget && cmd != "mark_types" && cmd != "annotation_overlaps")
        {
            x.Errors.Add("hết giờ, chưa chạy: " + name);
            return new JObject();
        }
        try
        {
            var st0 = Stopwatch.StartNew();
            var r = Call(x.App, cmd, a);
            if (st0.Elapsed.TotalSeconds > 10) x.Errors.Add("chậm " + Math.Round(st0.Elapsed.TotalSeconds) + " giây: " + name);
            if (r["Error"] != null) x.Errors.Add(name + ": " + r["Error"]);
            if (r["error"] != null) x.Errors.Add(name + ": " + r["error"]);
            foreach (var k in new[] { "Errors", "errors" }) foreach (var e in r[k] as JArray ?? new JArray()) if (!((string)e).StartsWith("inner wall")) x.Errors.Add(name + ": " + e);
            return r;
        }
        catch (Exception e) { x.Errors.Add(name + ": " + e.Message); return new JObject(); }
    }

    static void Begin(Cx x)
    {
        Step(x, "snapshot", "mark_types", new JObject { ["action"] = "snapshot", ["viewIds"] = new JArray(x.Vid), ["statePath"] = x.Snap });
        if (x.Suffix.Length > 0 && x.A["dimType"] != null) Step(x, "type", "mark_types", new JObject { ["action"] = "ensure", ["suffix"] = x.Suffix, ["types"] = new JArray((string)x.A["dimType"]) });
    }

    // preview images are taken inside the group, so a rolled-back result can still be looked at
    static string Image(Cx x)
    {
        string path = (string)x.A["imagePath"]; if (string.IsNullOrEmpty(path)) return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string stem = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
            var o = new ImageExportOptions { FilePath = stem, ExportRange = ExportRange.SetOfViews, ZoomType = ZoomFitType.FitToPage, PixelSize = (int?)x.A["imageWidth"] ?? 2400, HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150, FitDirection = FitDirectionType.Horizontal };
            o.SetViewsAndSheets(new List<ElementId> { V.Id });
            x.Doc.ExportImage(o);
            var f = Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(stem) + "*.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (f != null && f != path) { if (File.Exists(path)) File.Delete(path); File.Move(f, path); }
            return path;
        }
        catch (Exception e) { x.Errors.Add("image: " + e.Message); return null; }
    }

    static object Finish(Cx x, TransactionGroup tg)
    {
        if (x.Suffix.Length > 0)
        {
            var mk = Step(x, "mark", "mark_types", new JObject { ["action"] = "mark", ["suffix"] = x.Suffix, ["statePath"] = x.Snap, ["mode"] = "apply", ["logPath"] = x.Log("mark") });
            x.Add("marked", (int?)mk["New"] ?? 0);
        }
        var ov = Step(x, "overlaps", "annotation_overlaps", new JObject { ["viewId"] = x.Vid });
        int ovNew = (ov["Ids"] as JArray ?? new JArray()).Select(i => (int)i).Count(x.Created.Contains);
        string img = Image(x);
        if (!string.IsNullOrEmpty(x.OutPath)) File.WriteAllText(x.OutPath, JObject.FromObject(new { View = V.Name, x.Created, x.Placed, x.Left, x.Ask, x.Errors, Overlaps = ov["Sample"] }).ToString());
        if (x.Mode == "apply") { tg.Assimilate(); File.WriteAllText(x.LogPath, JsonConvert.SerializeObject(x.Created)); } else tg.RollBack();
        return new { View = V.Name, Mode = x.Mode, Placed = x.Placed, Ask = x.Ask, Left = x.Left, Errors = x.Errors.Take(12).ToList(), Overlaps = new { Total = (int?)ov["Pairs"] ?? 0, WithNew = ovNew }, Image = img, Seconds = Math.Round(x.Sw.Elapsed.TotalSeconds, 1) };
    }

    static object Fail(Cx x, TransactionGroup tg, Exception ex)
    {
        if (tg.HasStarted()) tg.RollBack();
        return new { View = V.Name, Mode = x.Mode, Error = ex.GetType().Name + ": " + ex.Message, Where = (ex.StackTrace ?? "").Split('\n').FirstOrDefault(), x.Placed };
    }

    static void Grids(Cx x)
    {
        foreach (var n in x.A["noGridDims"] as JArray ?? new JArray()) if (V.Name.Contains((string)n)) return;
        var r = Step(x, "grid", "grid_dims_band", new JObject { ["mode"] = "apply", ["viewId"] = x.Vid, ["dimType"] = x.Dim, ["preferSides"] = x.A["preferSides"] ?? new JArray("bottom", "left"), ["compact"] = true, ["logPath"] = x.Log("grid") });
        var done = r["Done"]; int cnt = 0;
        if (done is JArray da) cnt = da.Count;
        else if (done is JObject && done["Created"] is JArray dc) cnt = dc.Count;
        else if (r["Created"] is JArray rc) cnt = rc.Count;
        x.Add("gridDims", cnt);
    }

    // ---- elevations and building sections
    static object BuildingModule(UIApplication app, Document doc, JObject a, string mode, string logPath, string outPath, Stopwatch sw)
    {
        var x = NewCx(app, doc, a, mode, logPath, outPath, sw);
        var tg = new TransactionGroup(doc, "Develop view"); tg.Start();
        try
        {
            // the fill first and before the snapshot: its sketch elements must not be marked (the command marks its own)
            var fill = a["fill"] as JObject;
            if (fill != null && !V.Name.ToUpper().Contains("ELEVATION"))
            {
                var fa = (JObject)fill.DeepClone(); fa["mode"] = "apply"; fa["viewId"] = x.Vid; fa["suffix"] = x.Suffix; fa["logPath"] = x.Log("fill");
                var fr = Step(x, "fill", "structural_cut_fill", fa); if (fr["error"] == null && fr["Error"] == null) x.Add("fill", 1);
                foreach (var k in new[] { "missingSources" }) if (fr[k] is JArray m && m.Count > 0) x.Ask.Add("fill: thiếu link " + string.Join(", ", m));
            }
            else if (fill == null && !V.Name.ToUpper().Contains("ELEVATION")) x.Ask.Add("chưa có profile fill kết cấu nên bỏ qua SF");
            Begin(x);
            Grids(x);
            var lv = Step(x, "level", "level_dims_add", new JObject { ["viewId"] = x.Vid, ["side"] = a["levelSide"] ?? "left", ["mode"] = "apply", ["typeName"] = x.Dim, ["logPath"] = x.Log("level") });
            foreach (var id in lv["Created"] as JArray ?? new JArray()) { x.Created.Add((int)id); x.Add("levelDims", 1); }

            var common = new JObject { ["viewId"] = x.Vid, ["dimTypeName"] = x.Dim, ["cachePath"] = x.Log("openings") };
            if (a["nominalFamilies"] != null) common["nominalFamilies"] = a["nominalFamilies"];
            Func<string, JObject> oa = m => { var o = (JObject)common.DeepClone(); o["mode"] = m; return o; };
            var au = Step(x, "opening audit", "opening_dims_each", oa("audit"));
            foreach (var row in au["Missing"] as JArray ?? new JArray())
            {
                var o = oa("apply"); o["openingId"] = row["Id"]; o["logPath"] = x.Log("opening-dims");
                var r = Step(x, "opening " + row["Id"], "opening_dims_each", o);
                foreach (var c in r["Created"] as JArray ?? new JArray()) if (c["Id"] != null) { x.Created.Add((int)c["Id"]); x.Add("openingDims", 1); }
            }
            Step(x, "opening tags", "elevation_opening_tags", new JObject { ["viewIds"] = new JArray(x.Vid), ["mode"] = "apply", ["addLeader"] = true, ["maxSeconds"] = 90, ["logPath"] = x.Log("opening-tags") });

            var au2 = Step(x, "opening recheck", "opening_dims_each", oa("audit"));
            if (((int?)au2["MissingV"] ?? 0) > 0) x.Left["Q dọc"] = (int)au2["MissingV"];
            if (((int?)au2["MissingH"] ?? 0) > 0) x.Left["Q ngang"] = (int)au2["MissingH"];
            return Finish(x, tg);
        }
        catch (Exception ex) { return Fail(x, tg, ex); }
    }

    // ---- ordinary floor plans and ceiling plans
    static object PlanGeneralModule(UIApplication app, Document doc, JObject a, string mode, string logPath, string outPath, Stopwatch sw)
    {
        var x = NewCx(app, doc, a, mode, logPath, outPath, sw);
        var tg = new TransactionGroup(doc, "Develop view"); tg.Start();
        try
        {
            Begin(x);
            bool ceiling = V.ViewType == ViewType.CeilingPlan;
            string upper = V.Name.ToUpper();
            // what the plan is for decides its parts: names of diagrams get nothing, floor finish plans only levels and slopes
            var skipNames = (a["planSkipNames"] as JArray ?? new JArray("FUNCTIONAL", "FLOW", "CIRCULATION", "KEY", "SITE", "DEMARCATION")).Select(s => ((string)s).ToUpper());
            if (skipNames.Any(upper.Contains)) { x.Ask.Add("view sơ đồ, không phải mặt bằng kích thước: bỏ qua. Đổi bằng planSkipNames"); return Finish(x, tg); }
            bool finish = upper.Contains("FINISH");
            var parts = (a["parts"] as JArray)?.Select(s => (string)s).ToList() ?? (ceiling ? new List<string> { "grid", "rooms" } : finish ? new List<string> { "spots", "slopes" } : new List<string> { "grid", "openings", "tags", "rooms", "spots", "walls", "slopes" });
            if (parts.Contains("grid")) Grids(x);
            if (parts.Contains("openings")) OpeningChains(x);
            if (parts.Contains("tags")) OpeningTags(x);
            if (parts.Contains("rooms")) RoomTags(x);
            if (parts.Contains("spots")) FloorSpots(x);
            if (parts.Contains("walls")) WallTags(x);
            if (parts.Contains("slopes")) SlopeMarks(x);
            return Finish(x, tg);
        }
        catch (Exception ex) { return Fail(x, tg, ex); }
    }

    static void RoomTags(Cx x)
    {
        if (x.A["roomTagType"] == null) { x.Ask.Add("chưa có type tag phòng trong profile nên bỏ qua TR"); return; }
        var ra = new JObject { ["viewIds"] = new JArray(x.Vid), ["typeName"] = x.A["roomTagType"], ["outPath"] = x.Log("tr") };
        if (x.A["roomTagSizeMm"] != null) ra["tagSizeMm"] = x.A["roomTagSizeMm"];
        var r = Step(x, "room tags", "room_tags_missing", ra);
        if (!File.Exists(x.Log("tr"))) return;
        var items = JObject.Parse(File.ReadAllText(x.Log("tr")));
        if (items["Items"] == null || !items["Items"].Any()) return;
        foreach (var p in items["Problems"] as JArray ?? new JArray()) x.Ask.Add("tag phòng: " + p.ToString().Substring(0, Math.Min(80, p.ToString().Length)));
        var pr = Step(x, "room tags place", "roomtag_place_ortho", new JObject { ["mode"] = "apply", ["viewId"] = x.Vid, ["itemsPath"] = x.Log("tr"), ["logPath"] = x.Log("tr-place"), ["brief"] = true });
        x.Add("roomTags", (int?)pr["placed"] ?? 0);
    }

    static void FloorSpots(Cx x)
    {
        if (x.A["spotType"] == null) { x.Ask.Add("chưa có type cao độ trong profile nên bỏ qua FL"); return; }
        var ra = new JObject { ["viewIds"] = new JArray(x.Vid), ["typeName"] = x.A["spotType"], ["minAreaM2"] = x.A["minAreaM2"] ?? 5 };
        if (x.A["stairRoomText"] != null) ra["stairRoomText"] = x.A["stairRoomText"];
        var r = Step(x, "floor spots", "room_spots_missing", ra);
        var row = (r["Views"] as JArray)?.FirstOrDefault(); if (row == null) return;
        foreach (var p in (row["Problems"] as JArray ?? new JArray()).Take(5)) x.Ask.Add("cao độ phòng: " + p.ToString().Substring(0, Math.Min(90, p.ToString().Length)));
        var items = row["Items"] as JArray;
        if (items != null && items.Count > 0)
        {
            var pr = Step(x, "floor spots place", "annot_place", new JObject { ["viewId"] = x.Vid, ["mode"] = "apply", ["items"] = items, ["logPath"] = x.Log("spots") });
            foreach (var d in pr["Done"] as JArray ?? new JArray()) if (d["Id"] != null) { x.Created.Add((int)d["Id"]); x.Add("floorSpots", 1); }
        }
        if (x.A["stairRoomText"] != null && x.A["spotCheckType"] != null)
        {
            var sa = new JObject { ["mode"] = "apply", ["viewIds"] = new JArray(x.Vid), ["typeName"] = x.A["spotCheckType"], ["baseType"] = x.A["spotType"], ["stairRoomText"] = x.A["stairRoomText"], ["logPath"] = x.Log("door-spots") };
            if (x.A["stairViewText"] != null) sa["stairViewText"] = x.A["stairViewText"];
            if (x.A["civilSymbolId"] != null) sa["civilSymbolId"] = x.A["civilSymbolId"];
            Step(x, "stair door spots", "stair_door_spots", sa);
        }
    }

    static void SlopeMarks(Cx x)
    {
        if (x.A["slopeType"] == null) return;
        var fl = Step(x, "slopes", "floor_levels_probe", new JObject { ["viewId"] = x.Vid });
        var doc = x.Doc;
        var done = new FilteredElementCollector(doc, V.Id).OfCategory(BuiltInCategory.OST_SpotSlopes).WhereElementIsNotElementType().Select(e => e.get_BoundingBox(V)).Where(b => b != null).ToList();
        var items = new JArray();
        foreach (var e in fl["Elements"] as JArray ?? new JArray())
            foreach (var f in e["Top"] as JArray ?? new JArray())
            {
                double sl = (double?)f["SlopePct"] ?? 0, area = (double?)f["AreaM2"] ?? 0; var box = f["Box"] as JArray;
                if (sl <= 0 || sl >= 50 || area < 1 || box == null || box.Count < 4) continue;
                double r = ((double)box[0] + (double)box[2]) / 2, u = ((double)box[1] + (double)box[3]) / 2;
                if (done.Any(b => A(b.Min) < r && A(b.Max) > r && C(b.Min) < u && C(b.Max) > u)) continue;
                items.Add(new JObject { ["elementId"] = e["Id"], ["right"] = r, ["up"] = u, ["typeName"] = x.A["slopeType"] });
            }
        if (items.Count == 0) return;
        var pr = Step(x, "slopes place", "slope_spots_place", new JObject { ["viewId"] = x.Vid, ["mode"] = "apply", ["items"] = items, ["logPath"] = x.Log("slopes") });
        foreach (var d in pr["Done"] as JArray ?? new JArray()) if (d["Id"] != null) { x.Created.Add((int)d["Id"]); x.Add("slopeMarks", 1); }
    }

    // opening tag: on the wall line, on the side without an annotation in the way
    static void OpeningTags(Cx x)
    {
        var r = Step(x, "tag audit", "door_window_tag_check", new JObject { ["mode"] = "audit", ["viewIds"] = new JArray(x.Vid), ["viewTypes"] = new JArray(V.ViewType.ToString()), ["untaggedOnlyInTaggedViews"] = false });
        var row = (r["Views"] as JArray ?? r["PerView"] as JArray ?? new JArray()).FirstOrDefault();
        var ids = (row?["Issues"] as JArray ?? new JArray()).Where(i => (string)i["Kind"] == "untagged").Select(i => (int)i["ElementId"]).ToList();
        if (ids.Count == 0) return;
        using (var t = new Transaction(x.Doc, "Develop opening tags"))
        {
            t.Start();
            foreach (var id in ids) PlaceOpeningTag(x, x.Doc.GetElement(new ElementId(id)));
            t.Commit();
        }
    }

    static void PlaceOpeningTag(Cx x, Element e)
    {
        var doc = x.Doc; var fi = e as FamilyInstance; if (fi == null) return;
        bool door = fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors;
        string tn = (string)x.A[door ? "doorTagType" : "windowTagType"]; if (tn == null) { x.Ask.Add("chưa có type tag cửa trong profile"); return; }
        var type = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.Name == tn);
        var lp = fi.Location as LocationPoint; if (type == null || lp == null) return;
        var bb = fi.get_BoundingBox(V); if (bb == null) return;
        // what the view does not draw gets no tag
        var crop = V.CropBoxActive ? V.CropBox : null;
        double a0 = A(bb.Min), a1 = A(bb.Max), c0 = C(bb.Min), c1 = C(bb.Max);
        var hard = new List<Rc>(); var text = new List<Rc>(); Obstacles(doc, hard, text);
        var size = Measure(doc, type, fi) ?? new[] { 374.0, 175.0 };
        double ca = (a0 + a1) / 2, cc = (c0 + c1) / 2;
        var n = fi.FacingOrientation; bool nRight = Math.Abs(n.DotProduct(Rg)) > 0.7;
        double gap = 1.5 * S, d = (nRight ? (a1 - a0) : (c1 - c0)) / 2 + gap;
        double w = nRight ? size[0] : size[1];
        var tries = new List<double[]>();
        foreach (var sg in new[] { 1, -1 })
            tries.Add(nRight ? new[] { ca + sg * (d + w / 2), cc } : new[] { ca, cc + sg * (d + w / 2) });
        tries.Add(new[] { ca, cc });
        foreach (var p in tries)
        {
            var head = new Rc(p[0] - size[0] / 2, p[0] + size[0] / 2, p[1] - size[1] / 2, p[1] + size[1] / 2);
            if (hard.Any(h => h.Hit(head, 20))) continue;
            var g = IndependentTag.Create(doc, type.Id, V.Id, new Reference(fi), false, TagOrientation.Horizontal, P(p[0], p[1]));
            g.TagHeadPosition = P(p[0], p[1]); x.Created.Add(g.Id.IntegerValue); x.Add(door ? "doorTags" : "windowTags", 1);
            return;
        }
        x.Ask.Add("không có chỗ cho tag cửa " + e.Id.IntegerValue);
    }

    // PD1-PD6: one chain per host wall line: grid -> crossing wall faces -> opening edges -> grid
    static void OpeningChains(Cx x)
    {
        var doc = x.Doc;
        var openings = new FilteredElementCollector(doc, V.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Category != null && (f.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors || f.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows) && f.Host is Wall && f.get_BoundingBox(V) != null).ToList();
        var dimmed = new HashSet<int>();
        foreach (var d in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
            foreach (Reference r in d.References) dimmed.Add(r.ElementId.IntegerValue);
        var need = openings.Where(f => !dimmed.Contains(f.Id.IntegerValue)).ToList();
        var groups = need.GroupBy(f =>
        {
            var w = (Wall)f.Host; var c = (w.Location as LocationCurve)?.Curve as Line; if (c == null) return "?";
            bool hor = Math.Abs(c.Direction.DotProduct(Rg)) > 0.99, ver = Math.Abs(c.Direction.DotProduct(Up)) > 0.99; if (!hor && !ver) return "?";
            return (hor ? "H" : "V") + Math.Round((hor ? C(c.Origin) : A(c.Origin)) / 50);
        }).Where(g => g.Key != "?").ToList();
        var gridEls = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Grid)).Cast<Grid>().Where(g => g.Curve is Line).ToList();
        var grids = gridEls.Select(g => (Line)g.Curve).ToList();
        var walls = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Wall)).Cast<Wall>().ToList();
        double range = (double?)x.A["openingGridRangeMm"] ?? 12000;
        foreach (var g in groups)
        {
            bool hor = g.Key[0] == 'H';
            Func<XYZ, double> al = p => hor ? A(p) : C(p); Func<XYZ, double> ac = p => hor ? C(p) : A(p);
            var w0 = (Wall)g.First().Host; var wc = ((LocationCurve)w0.Location).Curve as Line; double wall = ac(wc.Origin), half = w0.Width * MM / 2;
            // only the elements of this chain: searching the whole view took minutes on a 1:200 plan
            var cands = new HashSet<int>(g.Select(f => f.Id.IntegerValue)); foreach (var ge in gridEls) cands.Add(ge.Id.IntegerValue);
            var pos = new JArray(); var taken = new List<double>();
            Action<double, JToken> put = (mm, j) => { if (taken.Any(t => Math.Abs(t - mm) < 3)) return; taken.Add(mm); pos.Add(j); };
            double lo = 1e12, hi = -1e12;
            foreach (var f in g)
            {
                // the box holds the swing and the frame: the edges come from the location and the width
                var bb = f.get_BoundingBox(V); double e0 = Math.Min(al(bb.Min), al(bb.Max)), e1 = Math.Max(al(bb.Min), al(bb.Max));
                var wp = f.Symbol?.LookupParameter("Width") ?? f.LookupParameter("Width"); var lpt = f.Location as LocationPoint;
                if (wp != null && wp.HasValue && lpt != null) { double ctr = al(lpt.Point), hw0 = wp.AsDouble() * MM / 2; e0 = ctr - hw0; e1 = ctr + hw0; }
                lo = Math.Min(lo, e0); hi = Math.Max(hi, e1);
                put(e0, new JObject { ["mm"] = e0, ["id"] = f.Id.IntegerValue }); put(e1, new JObject { ["mm"] = e1, ["id"] = f.Id.IntegerValue });
            }
            var perp = grids.Where(l => Math.Abs(l.Direction.DotProduct(hor ? Up : Rg)) > 0.99).Select(l => hor ? A(l.Origin) : C(l.Origin)).ToList();
            var gl = perp.Where(p => p < lo && lo - p < range).OrderByDescending(p => p).FirstOrDefault();
            var gh = perp.Where(p => p > hi && p - hi < range).OrderBy(p => p).FirstOrDefault();
            double from = perp.Any(p => p < lo && lo - p < range) ? gl : lo, to = perp.Any(p => p > hi && p - hi < range) ? gh : hi;
            if (from < lo) put(from, from); if (to > hi) put(to, to);
            foreach (var cw in walls)
            {
                var l = (cw.Location as LocationCurve)?.Curve as Line; if (l == null || Math.Abs(l.Direction.DotProduct(hor ? Up : Rg)) < 0.99) continue;
                double p = al(l.Origin), c0 = Math.Min(ac(l.GetEndPoint(0)), ac(l.GetEndPoint(1))), c1 = Math.Max(ac(l.GetEndPoint(0)), ac(l.GetEndPoint(1)));
                if (p <= from || p >= to || c0 > wall + 50 || c1 < wall - 50) continue;
                double h = cw.Width * MM / 2; cands.Add(cw.Id.IntegerValue);
                put(p - h, new JObject { ["mm"] = p - h, ["id"] = cw.Id.IntegerValue, ["src"] = "3d" }); put(p + h, new JObject { ["mm"] = p + h, ["id"] = cw.Id.IntegerValue, ["src"] = "3d" });
            }
            // exterior: outside; interior: the side without a room's door swing is not known here, so the freer side
            var hard = new List<Rc>(); var text = new List<Rc>(); Obstacles(doc, hard, text);
            double off = half + 7 * S; double best = wall + off;
            foreach (var cand in new[] { wall + off, wall - off })
            {
                var band = hor ? new Rc(from, to, cand - 60, cand + 60) : new Rc(cand - 60, cand + 60, from, to);
                if (!hard.Any(h => h.Hit(band))) { best = cand; break; }
            }
            var sorted = new JArray(pos.OrderBy(j => j.Type == JTokenType.Object ? (double)j["mm"] : (double)j));
            var r = Step(x, "opening chain", "dims_at_positions", new JObject
            {
                ["viewId"] = x.Vid, ["mode"] = "apply", ["typeName"] = x.Dim, ["logPath"] = x.Log("pd"), ["use3D"] = true, ["toleranceMm"] = 12,
                ["dims"] = new JArray(new JObject { ["name"] = "wall " + g.Key, ["measure"] = hor ? "right" : "up", ["lineMm"] = best, ["positions"] = sorted, ["candidateIds"] = new JArray(cands) })
            });
            foreach (var d in r["Done"] as JArray ?? new JArray()) if (d["Id"] != null) { x.Created.Add((int)d["Id"]); x.Add("openingChains", 1); }
        }
    }

    // WT1-WT5: one tag per wall run, on a free stretch, room side, straight leader
    static void WallTags(Cx x)
    {
        string tn = (string)x.A["wallTagType"]; if (tn == null) return;
        var doc = x.Doc; var type = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.Name == tn);
        if (type == null) { x.Ask.Add("không có type tag tường " + tn); return; }
        var skip = (x.A["wallTagSkipRooms"] as JArray ?? new JArray("TOILET", "WC")).Select(s => ((string)s).ToUpper()).ToList();
        var tagged = new HashSet<int>();
        foreach (var t0 in new FilteredElementCollector(doc, V.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            try { foreach (var i in t0.GetTaggedLocalElementIds()) tagged.Add(i.IntegerValue); } catch { }
        var walls = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Wall)).Cast<Wall>().Where(w => (w.Location as LocationCurve)?.Curve is Line && w.get_BoundingBox(V) != null).ToList();
        double minLen = (double?)x.A["wallTagMinLenMm"] ?? 2000;
        using (var t = new Transaction(doc, "Develop wall tags"))
        {
            t.Start();
            var size = walls.Count > 0 ? (Measure(doc, type, walls[0]) ?? new[] { 800.0, 175.0 }) : new[] { 800.0, 175.0 };
            var hard = new List<Rc>(); var text = new List<Rc>(); Obstacles(doc, hard, text);
            var runs = walls.GroupBy(w => { var c = (Line)((LocationCurve)w.Location).Curve; bool hr = Math.Abs(c.Direction.DotProduct(Rg)) > 0.99; return w.GetTypeId().IntegerValue + "|" + (hr ? "H" : "V") + Math.Round((hr ? C(c.Origin) : A(c.Origin)) / 20); });
            foreach (var run in runs)
            {
                if (run.Any(q => tagged.Contains(q.Id.IntegerValue))) continue;   // one tag per wall run
                var w = run.OrderByDescending(q => q.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0).First();
                var l = (Line)((LocationCurve)w.Location).Curve; if (l.Length * MM < minLen) continue;
                bool hr = Math.Abs(l.Direction.DotProduct(Rg)) > 0.99, ver = Math.Abs(l.Direction.DotProduct(Up)) > 0.99; if (!hr && !ver) continue;
                var nrm = l.Direction.CrossProduct(XYZ.BasisZ).Normalize(); XYZ mid = l.Evaluate(0.5, true); double hw = w.Width / 2;
                Func<int, Room> room = sg => { var p = mid + nrm * (sg * (hw + 0.3)); return doc.GetRoomAtPoint(new XYZ(p.X, p.Y, p.Z + 1)); };
                var ra = room(1); var rb = room(-1);
                if (new[] { ra, rb }.Any(q => q != null && skip.Any(s => (q.Name ?? "").ToUpper().Contains(s)))) continue;
                int side = ra != null ? 1 : rb != null ? -1 : 1;
                bool placed = false;
                foreach (var f in new[] { 0.5, 0.35, 0.65, 0.2, 0.8 })
                {
                    var pt = l.Evaluate(f, true); var end = pt + nrm * (side * hw); var hd = pt + nrm * (side * (hw + (4 * S + (hr ? size[1] : size[0]) / 2) / MM));
                    double ha = A(hd), hc = C(hd);
                    var head = new Rc(ha - size[0] / 2, ha + size[0] / 2, hc - size[1] / 2, hc + size[1] / 2);
                    if (hard.Any(h => h.Hit(head, 20))) continue;
                    var g = IndependentTag.Create(doc, type.Id, V.Id, new Reference(w), true, TagOrientation.Horizontal, P(ha, hc));
                    g.LeaderEndCondition = LeaderEndCondition.Free; g.SetLeaderEnd(new Reference(w), P(A(end), C(end))); g.TagHeadPosition = P(ha, hc);
                    hard.Add(head); x.Created.Add(g.Id.IntegerValue); x.Add("wallTags", 1); placed = true; break;
                }
                if (!placed) x.Add("wallTagsNoRoom", 1);
            }
            t.Commit();
        }
        if (x.Placed.ContainsKey("wallTagsNoRoom")) x.Left["WT"] = x.Placed["wallTagsNoRoom"];
    }

    // ---- stair section across the flights
    static object CrossModule(UIApplication app, Document doc, JObject a, string mode, string logPath, string outPath, Stopwatch sw)
    {
        var x = NewCx(app, doc, a, mode, logPath, outPath, sw);
        var tg = new TransactionGroup(doc, "Develop view"); tg.Start();
        try
        {
            Begin(x);
            string info = x.Log("xsec");
            Step(x, "audit", "stair_xsection_info", new JObject { ["viewId"] = x.Vid, ["outPath"] = info });
            var au = JObject.Parse(File.ReadAllText(info));
            // a chain with (EQUAL RISERS) already there: XA1 must not build a second one
            bool hasRise = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Any(d0 =>
            {
                if ((d0.Below ?? "").Contains("EQUAL RISERS")) return true;
                if (d0.NumberOfSegments > 1) foreach (DimensionSegment g in d0.Segments) if ((g.Below ?? "").Contains("EQUAL RISERS")) return true;
                return false;
            });
            var xaParts = new JArray(new[] { "XA1", "XA3", "XA4", "XA5", "XA6" }.Where(p => p != "XA1" || !hasRise));
            var r = Step(x, "XA", "stair_xsection_annotate", new JObject { ["viewId"] = x.Vid, ["mode"] = "apply", ["dimTypeName"] = x.Dim, ["parts"] = xaParts, ["logPath"] = x.Log("xa") });
            foreach (var d in r["Done"] as JArray ?? new JArray()) if (d["Id"] != null) { x.Created.Add((int)d["Id"]); x.Add((string)d["Part"] ?? "XA", 1); }
            var lv = Step(x, "XA2", "level_dims_add", new JObject { ["viewId"] = x.Vid, ["side"] = a["levelSide"] ?? "left", ["mode"] = "apply", ["typeName"] = x.Dim, ["logPath"] = x.Log("level") });
            foreach (var id in lv["Created"] as JArray ?? new JArray()) { x.Created.Add((int)id); x.Add("levelDims", 1); }

            var fams = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            var runTag = fams.FirstOrDefault(s => s.Name == (string)a["runTagType"]);
            double zOff = C(new XYZ(O.X, O.Y, 0));
            var numbered = new List<int>();
            using (var t = new Transaction(doc, "Develop cross section"))
            {
                t.Start();
                var hard = new List<Rc>(); var text = new List<Rc>(); Obstacles(doc, hard, text);
                foreach (var f in au["Flights"] as JArray ?? new JArray())
                {
                    var run = doc.GetElement(new ElementId((int)f["Id"])) as StairsRun; if (run == null) continue;
                    var fp = new List<XYZ>(); foreach (Curve c in run.GetFootprintBoundary()) fp.AddRange(c.Tessellate());
                    double a0 = fp.Min(q => A(q)), a1 = fp.Max(q => A(q)), z = ((double)f["FromEl"] + (double)f["ToEl"]) / 2;
                    bool hasTag = (f["RunTags"] as JArray ?? new JArray()).Count > 0;
                    if (!hasTag && runTag != null)
                    {
                        bool left = (string)f["Half"] == "left";
                        var tagType = RunTagFor(doc, runTag, run, (double)f["FromEl"], x.Ask);
                        var size = Measure(doc, tagType, run) ?? new[] { 1800.0, 360.0 };
                        double tw = Math.Max(size[1], 300), th2 = Math.Max(size[0], 1000);    // the text stands up here
                        // outward from the flight: the first column clear of dim lines and texts, past the wall
                        double ha = double.NaN, dirX = left ? -1 : 1, startA = left ? a0 : a1;
                        for (double dx = 6 * S; dx < 120 * S; dx += 1.4 * S)
                        {
                            double xx = startA + dirX * (dx + tw / 2);
                            var head = new Rc(xx - tw / 2, xx + tw / 2, z + zOff - th2 / 2, z + zOff + th2 / 2);
                            if (hard.Any(h => h.Hit(head, 30))) continue;
                            ha = xx; hard.Add(head); break;
                        }
                        if (double.IsNaN(ha)) { x.Ask.Add("không có chỗ cho tag vế " + f["Label"]); continue; }
                        var g = IndependentTag.Create(doc, tagType.Id, V.Id, new Reference(run), true, TagOrientation.Vertical, P(ha, z + zOff));
                        g.LeaderEndCondition = LeaderEndCondition.Free; g.SetLeaderEnd(new Reference(run), P((a0 + a1) / 2, z + zOff)); g.TagHeadPosition = P(ha, z + zOff);
                        x.Created.Add(g.Id.IntegerValue); x.Add("XB1", 1);
                    }
                    if ((string)f["Facing"] == "away" && f["RiserNumbers"] != null && !(bool)f["RiserNumbers"])
                    {
                        try { var ns = NumberSystem.Create(doc, V.Id, new LinkElementId(run.Id), run.GetNumberSystemReference(StairsNumberSystemReferenceOption.Center)); numbered.Add(ns.Id.IntegerValue); x.Created.Add(ns.Id.IntegerValue); x.Add("XC", 1); }
                        catch (Exception e) { x.Errors.Add("XC: " + e.Message); }
                    }
                }
                t.Commit();
            }
            if (numbered.Count > 0 && a["numberSourceIdCross"] != null)
                Step(x, "XC settings", "stair_numbers_match", new JObject { ["viewId"] = x.Vid, ["mode"] = "apply", ["sourceId"] = a["numberSourceIdCross"], ["logPath"] = x.Log("xc"), ["targets"] = new JArray(numbered.Select(i => new JObject { ["id"] = i, ["reference"] = "rightQuarter" })) });
            else if (numbered.Count > 0) x.Ask.Add("chưa có số bậc mẫu cho mặt cắt ngang (numberSourceIdCross)");

            Step(x, "recheck", "stair_xsection_info", new JObject { ["viewId"] = x.Vid, ["outPath"] = info });
            foreach (var g in (JObject.Parse(File.ReadAllText(info))["Issues"] ?? new JArray()).Select(i => (string)i).GroupBy(s => s.Split(':')[0])) x.Left[g.Key] = g.Count();
            return Finish(x, tg);
        }
        catch (Exception ex) { return Fail(x, tg, ex); }
    }

    // ---- additions to the stair plan: everything the audit still asks for after the first pass
    static void StairPlanRest(UIApplication app, Document doc, JObject a, JObject au, List<RunD> runs, List<int> created, Dictionary<string, int> placed, List<string> ask, List<string> errors, string tmp, Stopwatch sw)
    {
        int vid = V.Id.IntegerValue;
        var x = new Cx { App = app, Doc = doc, A = a, Vid = vid, Tmp = tmp, Dim = ((string)a["dimType"] ?? "") + ((string)a["suffix"] ?? ""), Created = created, Ask = ask, Errors = errors, Placed = placed, Sw = sw };
        var expected = (au["Expected"] as JArray ?? new JArray()).Where(e => (string)e["Status"] == "missing").ToList();
        var stairs = runs.Select(r => r.S).GroupBy(s => s.Id.IntegerValue).Select(g => g.First()).ToList();
        var railIds = (au["Tags"]?["Railings"] as JArray ?? new JArray()).Select(r => (int)r["Railing"]).ToList();
        var rails = railIds.Select(i => doc.GetElement(new ElementId(i)) as Railing).Where(r => r != null).ToList();
        var hard = new List<Rc>(); var text = new List<Rc>();

        // SA1 / SA3: on the rails and the walls, through dims_rail_refs, one chain per row group
        Func<double, bool, JObject> refAt = (mm, across) =>
        {
            foreach (var w in au["Walls"] as JArray ?? new JArray())
            {
                double? ff = (double?)w["FinishFaceMm"]; if (ff == null || Math.Abs(ff.Value - mm) > 3) continue;
                var id = ((w["Walls"] as JArray)?.Select(s => ((string)s).Split(' ')[0]).FirstOrDefault()); if (id != null) return new JObject { ["mm"] = mm, ["wall"] = int.Parse(id) };
            }
            foreach (var r in rails)
            {
                var b = r.get_BoundingBox(V); if (b == null) continue;
                double lo = across ? C(b.Min) : A(b.Min), hi = across ? C(b.Max) : A(b.Max);
                if (mm > Math.Min(lo, hi) - 120 && mm < Math.Max(lo, hi) + 120 && r.TopRail != ElementId.InvalidElementId) return new JObject { ["mm"] = mm, ["rail"] = r.TopRail.IntegerValue };
            }
            return null;
        };
        foreach (var rule in new[] { "SA1", "SA3" })
        {
            var rows = expected.Where(e => (string)e["Rule"] == rule).ToList(); if (rows.Count == 0) continue;
            // the rail edge test regenerates the model per edge: on a core with many rails it can freeze Revit for minutes
            if (rails.Count > 4 && a["railDims"] == null) { ask.Add(rule + ": lõi có " + rails.Count + " lan can, phần này chạy rất lâu nên chưa làm; cho phép bằng railDims:true"); continue; }
            bool across = rule == "SA1";
            foreach (var g in rule == "SA3" ? rows.GroupBy(e => (string)e["Item"]) : rows.GroupBy(e => "all"))
            {
                var mms = g.SelectMany(e => new[] { (double)e["FromMm"], (double)e["ToMm"] }).Distinct().OrderBy(m => m).ToList();
                var pos = new JArray(); bool bad = false;
                foreach (var m in mms) { var j = refAt(m, across); if (j == null) bad = true; else pos.Add(j); }
                if (bad || pos.Count < 2) { ask.Add(rule + ": chưa bắt được tham chiếu ở " + g.Key); continue; }
                double line = across ? runs.First().AStart + runs.First().Sign * 300 : (runs.Min(r => r.C0) + runs.Max(r => r.C1)) / 2;
                var pr = Step(x, rule, "dims_rail_refs", new JObject { ["viewId"] = vid, ["mode"] = "apply", ["typeName"] = x.Dim, ["logPath"] = x.Log(rule.ToLower()), ["dims"] = new JArray(new JObject { ["name"] = rule, ["measure"] = (across != Sw) ? "up" : "right", ["lineMm"] = line, ["positions"] = pos }) });
                foreach (var d in pr as IEnumerable<JToken> ?? new JToken[0]) { }
                if (pr["0"] == null) x.Add(rule, 1);
            }
        }

        // SB3: a spot on each seen landing without one, near its middle
        var spotAt = (au["Spots"] as JArray ?? new JArray()).Select(s => (string)s["At"]).ToList();
        string spotType = (string)a["spotType"];
        var items = new JArray(); Obstacles(doc, hard, text);
        foreach (var l in au["Tags"]?["Landings"] as JArray ?? new JArray())
        {
            int id = (int)l["Landing"]; if (spotAt.Contains("landing " + id) || spotType == null) continue;
            var ld = doc.GetElement(new ElementId(id)) as StairsLanding; if (ld == null) continue;
            var fp = new List<XYZ>(); foreach (Curve c in ld.GetFootprintBoundary()) fp.AddRange(c.Tessellate());
            double ca = (fp.Min(q => A(q)) + fp.Max(q => A(q))) / 2, cc = (fp.Min(q => C(q)) + fp.Max(q => C(q))) / 2;
            for (double d = 0; d < 800; d += 100)
            {
                var pt = Pair(ca, cc + d); var box = new Rc(pt[0] - 300, pt[0] + 300, pt[1] - 200, pt[1] + 200);
                if (hard.Any(h => h.Hit(box))) continue;
                items.Add(new JObject { ["kind"] = "spot", ["elementId"] = ld.GetStairs().Id.IntegerValue, ["typeName"] = spotType, ["right"] = pt[0], ["up"] = pt[1] }); hard.Add(box); break;
            }
        }
        if (items.Count > 0)
        {
            var pr = Step(x, "SB3", "annot_place", new JObject { ["viewId"] = vid, ["mode"] = "apply", ["items"] = items, ["logPath"] = x.Log("sb3") });
            foreach (var d in pr["Done"] as JArray ?? new JArray()) if (d["Id"] != null) { created.Add((int)d["Id"]); x.Add("SB3", 1); }
        }

        // SB4: doors and windows the view draws, without a tag
        var noTag = (au["Tags"]?["Openings"] as JArray ?? au["Openings"] as JArray ?? new JArray()).Where(o => o["Tags"] == null || !o["Tags"].Any()).Select(o => (int)o["Id"]).ToList();
        if (noTag.Count > 0)
            using (var t = new Transaction(doc, "Develop stair opening tags"))
            {
                t.Start();
                foreach (var id in noTag) { var e = doc.GetElement(new ElementId(id)); if (e?.get_BoundingBox(V) != null) PlaceOpeningTag(x, e); }
                t.Commit();
            }

        // SB5, SB6: finish marks, from the profile or the room; never invented
        string floor = (string)a["floorCode"], wallCode = (string)a["wallCode"];
        var src = a["finishMarkSourceId"] != null ? (int)a["finishMarkSourceId"] : (int?)null;
        var fm = new JArray();
        foreach (var l in au["Tags"]?["Landings"] as JArray ?? new JArray())
        {
            if ((l["FinishMarks"] as JArray)?.Count > 0) continue;
            if (floor == null || src == null) { ask.Add("SB5: cần mã hoàn thiện sàn F và mark mẫu (floorCode, finishMarkSourceId)"); break; }
            var ld = doc.GetElement(new ElementId((int)l["Landing"])) as StairsLanding; if (ld == null) continue;
            var fp = new List<XYZ>(); foreach (Curve c in ld.GetFootprintBoundary()) fp.AddRange(c.Tessellate());
            var pt = Pair((fp.Min(q => A(q)) + fp.Max(q => A(q))) / 2 + 500, (fp.Min(q => C(q)) + fp.Max(q => C(q))) / 2 - 300);
            fm.Add(new JObject { ["code"] = floor, ["right"] = pt[0], ["up"] = pt[1] });
        }
        foreach (var w in au["Tags"]?["Walls"] as JArray ?? new JArray())
        {
            if ((w["Tags"] as JArray)?.Count > 0 || (w["FinishMarks"] as JArray)?.Count > 0) continue;
            if (wallCode == null || src == null) { if (!ask.Any(s => s.StartsWith("SB6"))) ask.Add("SB6: cần mã hoàn thiện tường W và mark mẫu (wallCode, finishMarkSourceId)"); break; }
            var row = (au["Walls"] as JArray ?? new JArray()).FirstOrDefault(q => (string)q["Side"] == (string)w["Side"]); double? ff = (double?)row?["FinishFaceMm"]; if (ff == null) continue;
            string sd = (string)w["Side"]; bool across = sd == "top" || sd == "bottom"; double mid = across ? (runs.Min(r => r.A0) + runs.Max(r => r.A1)) / 2 : (runs.Min(r => r.C0) + runs.Max(r => r.C1)) / 2;
            double inward = (sd == "top" || sd == "right") ? -300 : 300;
            fm.Add(across ? new JObject { ["code"] = wallCode, ["right"] = mid, ["up"] = ff.Value + inward, ["endRight"] = mid, ["endUp"] = ff.Value } : new JObject { ["code"] = wallCode, ["right"] = ff.Value + inward, ["up"] = mid, ["endRight"] = ff.Value, ["endUp"] = mid });
        }
        if (fm.Count > 0 && src != null)
        {
            var fa = new JObject { ["viewId"] = vid, ["mode"] = "apply", ["sourceId"] = src.Value, ["items"] = fm, ["logPath"] = x.Log("finish") };
            if (a["finishParam"] != null) fa["paramName"] = a["finishParam"];
            var pr = Step(x, "SB5/6", "finish_mark_place", fa);
            foreach (var d in pr["Done"] as JArray ?? new JArray()) if (d["Id"] != null) { created.Add((int)d["Id"]); x.Add("finishMarks", 1); }
        }

        // the open gap between flights
        var gap = Step(x, "X", "stair_gap_x", new JObject { ["viewIds"] = new JArray(vid), ["mode"] = "apply", ["logPath"] = x.Log("gap") });
    }

    static double[] Pair(double a, double c) { return Sw ? new[] { c, a } : new[] { a, c }; }
}
