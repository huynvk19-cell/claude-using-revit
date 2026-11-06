/* mcp-tool
{
  "description": "ONE view, the whole job in one call: finds what is missing, solves positions, places, marks, checks; returns only counts and questions. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "dimType": { "type": "string", "description": "official dim type" },
      "runTagType": { "type": "string" },
      "railTagType": { "type": "string" },
      "pathType": { "type": "string" },
      "spotType": { "type": "string" },
      "numberSourceId": { "type": "number", "description": "NumberSystem whose settings are copied (sections)" },
      "suffix": { "type": "string", "description": "mark type suffix of the profile" },
      "me": { "type": "string", "description": "Revit user whose annotations may be edited" },
      "logPath": { "type": "string", "description": "created ids; required for apply and undo" },
      "outPath": { "type": "string", "description": "full plan as JSON (not returned)" }
    },
    "required": ["viewId", "mode"]
  },
  "timeoutSeconds": 180
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
// Modules: FloorPlan stair core plan with the flights along the view's horizontal (path, run tags, railing tags, run
//    length chain with formula, wall thickness). Section along the flights (LB1, LB2, LA3, LA4, LA5, LB3, LC; stairs
//    with or without walls). Others return an Error: use the step cards for them.
//    Section args: runTagType = the RISER tag type, spotType, numberSourceId, la5TargetMm (default 1200).
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
    static double A(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double C(XYZ p) { return (p - O).DotProduct(Up) * MM; }
    static bool Plan;
    static XYZ P(double a, double c) { var q = O + Rg * (a / MM) + Up * (c / MM); return Plan ? new XYZ(q.X, q.Y, Z0) : q; }

    static JObject Call(UIApplication app, string name, JObject args)
    {
        var r = DynamicCommands.Run(app, name, null, args);
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
                if (Math.Abs(l.Direction.DotProduct(Up)) < 0.99) continue;
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
                    bool horizontal = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.99;
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
        var sw = Stopwatch.StartNew();
        var doc = app.ActiveUIDocument.Document;
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
        if (V.ViewType != ViewType.FloorPlan && V.ViewType != ViewType.Section) return new { View = V.Name, Error = "no module for " + V.ViewType + " yet: use the step cards" };
        O = V.Origin; Rg = V.RightDirection; Up = V.UpDirection; Z0 = V.Origin.Z; S = V.Scale; Plan = V.ViewType == ViewType.FloorPlan;
        if (!Plan) return SectionModule(app, doc, args, mode, logPath, outPath, sw);
        string me = (string)args["me"] ?? "", suffix = (string)args["suffix"] ?? "";
        string dimName = ((string)args["dimType"] ?? "") + suffix;
        string tmp = Path.Combine(Path.GetTempPath(), "view_develop"); Directory.CreateDirectory(tmp);
        string aPath = Path.Combine(tmp, V.Id.IntegerValue + "-audit.json"), snap = Path.Combine(tmp, V.Id.IntegerValue + "-snap.json");

        var placed = new Dictionary<string, int> { ["path"] = 0, ["runTags"] = 0, ["railTags"] = 0, ["runChains"] = 0, ["wallDims"] = 0, ["marked"] = 0 };
        var ask = new List<string>(); var errors = new List<string>(); var undrawn = new List<string>(); var created = new List<int>(); var plan = new JArray();

        Call(app, "stair_plan_audit", new JObject { ["viewId"] = V.Id.IntegerValue, ["outPath"] = aPath });
        var au = JObject.Parse(File.ReadAllText(aPath));
        if (au["Runs"] == null || !au["Runs"].Any()) return new { View = V.Name, Error = "no stair run seen" };
        if (!((string)au["Travel"] ?? "").Contains("horizontal")) return new { View = V.Name, Error = "flights along the view's vertical: no module yet, use the step cards" };

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
            if (lanes.Count <= 1 && open("top") != open("bottom")) return open("top") ? "top" : "bottom";
            var lane = lanes.FirstOrDefault(l => ((string)l["Runs"] ?? "").Split('/').Contains(rd.Label)) ?? lanes.FirstOrDefault();
            return (string)lane?["DimSide"] ?? "bottom";
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
                var grids = new FilteredElementCollector(doc, V.Id).OfClass(typeof(Grid)).Cast<Grid>().Select(g => g.Curve as Line).Where(l => l != null && Math.Abs(l.Direction.DotProduct(Up)) > 0.99).Select(l => A(l.Origin)).ToList();
                var linesOf = runs.Select(r => r.S).GroupBy(s => s.Id.IntegerValue).ToDictionary(g => g.Key, g => StairLines(doc, g.First()));

                foreach (var group in runs.GroupBy(sideOf))
                {
                    int sg = group.Key == "top" ? 1 : -1;
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
                            var tg2 = IndependentTag.Create(doc, runTag.Id, V.Id, new Reference(r.R), true, TagOrientation.Horizontal, P(a.Value, c));
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
                        if (pts.Count >= 2 && far.Count > 0)
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
                foreach (var side in new[] { "top", "bottom" })
                {
                    var w = wall(side); if (open(side)) continue;
                    bool missing = (au["Expected"] ?? new JArray()).Any(e => (string)e["Rule"] == "SA4" && ((string)e["Item"] ?? "").StartsWith("wall " + side) && (string)e["Status"] == "missing");
                    if (!missing) continue;
                    double finish = (double)w["FinishFaceMm"], outerF = (double)w["OuterFaceMm"];
                    double lineA = runs.Max(r => r.A1) + 400;
                    foreach (var d0 in new FilteredElementCollector(doc, V.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
                    {
                        var ln = d0.Curve as Line; if (ln == null || Math.Abs(ln.Direction.DotProduct(Up)) < 0.99) continue;
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
                        ["dims"] = new JArray(new JObject { ["name"] = "wall " + side, ["measure"] = "up", ["lineMm"] = lineA, ["positions"] = positions })
                    });
                    foreach (var dn in res["Done"] ?? new JArray()) if (dn["Id"] != null) { created.Add((int)dn["Id"]); placed["wallDims"]++; }
                    foreach (var er in res["Errors"] ?? new JArray()) errors.Add("SA4: " + er);
                }

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
            if (mode == "apply") { tg.Assimilate(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else tg.RollBack();

            return new
            {
                View = V.Name, Mode = mode, Placed = placed.Where(k => k.Value > 0).ToDictionary(k => k.Key, k => k.Value),
                Ask = ask, Undrawn = undrawn, Errors = errors,
                Left = left.GroupBy(x => x.Split(':')[0]).ToDictionary(g => g.Key, g => g.Count()),
                Overlaps = new { Total = (int?)ov["Pairs"] ?? 0, WithNew = ovNew },
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
        if (fls.Count == 0) return new { View = V.Name, Error = "no cut flight in this section: use drafting-stair-section-cross for a cross section" };

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
                double on = A(ln.Origin), val = (d0.Value ?? 0) * MM;
                return on > sa0 - 3000 && on < sa1 + 3000 && val > 1400 && Math.Abs(val - (fls.Max(f => f.Z1) - fls.Min(f => f.Z0))) > 5;
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
                        var size = Measure(doc, runTag, f.R) ?? new[] { 1800.0, 360.0 };
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
                            var g = IndependentTag.Create(doc, runTag.Id, V.Id, new Reference(f.R), true, TagOrientation.Horizontal, P(best, z + zOff));
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
            int ovNew = (ov["Ids"] ?? new JArray()).Select(x => (int)x).Count(created.Contains);
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, new JObject { ["view"] = V.Name, ["created"] = new JArray(created), ["left"] = new JArray(left) }.ToString());
            if (mode == "apply") { tg.Assimilate(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); } else tg.RollBack();
            return new { View = V.Name, Mode = mode, Placed = placed, Ask = ask, Errors = errors, Left = left.GroupBy(x => x.Split(':')[0]).ToDictionary(g => g.Key, g => g.Count()), Overlaps = new { Total = (int?)ov["Pairs"] ?? 0, WithNew = ovNew }, Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1) };
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
}
