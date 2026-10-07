/* mcp-tool
{
  "description": "Stair core PLAN, ONE view: stair path (Fixed Up, no UP/DOWN), tread numbers, run tags (outside with leader by default). preview | apply | undo.",
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
          "apply",
          "undo"
        ]
      },
      "parts": {
        "type": "array",
        "items": {
          "type": "string",
          "enum": [
            "path",
            "numbers",
            "runTags"
          ]
        }
      },
      "pathTypeName": {
        "type": "string"
      },
      "numberSide": {
        "type": "string",
        "enum": [
          "left",
          "right",
          "center",
          "leftQuarter",
          "rightQuarter"
        ]
      },
      "numberTypeName": {
        "type": "string"
      },
      "runTagTypeName": {
        "type": "string"
      },
      "runTagPlace": {
        "type": "string",
        "enum": [
          "outside",
          "inside"
        ],
        "description": "outside (default, as the project sheets)"
      },
      "runTagOffsetMm": {
        "type": "number",
        "description": "outside: paper mm from the outer wall face to the tag head point, default 5"
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "mode"
    ]
  },
  "timeoutSeconds": 120,
  "readOnly": false
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Stair core PLAN, ONE view (drafting-stair-plan.md): places the deterministic parts. parts (default all three):
//    'path' (SD) - one stair path per stairs that has a seen run, of a type in family Fixed Up Direction
//    (pathTypeName, else the project's most-used Fixed Up type, else any existing one; never creates a type), Show
//    Up/Down Text off; an existing path stays where it is: its type is changed to Fixed Up and its text turned off.
//    'numbers' (C) - tread numbers on every seen run that has none; V1 sharing a lane with V3 gets the mirrored side
//    so the two never overlap (numberSide relative to walking up: left | right | center | leftQuarter | rightQuarter,
//    default left; numberTypeName, else the project's most-used type). 'runTags' (SB1) - a Stair Run tag for every
//    untagged seen run, aimed at its SEEN part (V1 beyond the cut line, V2 middle, V3 before the cut line): default
//    runTagPlace outside = head just outside the side wall of the run's lane (text along the run), free-end leader
//    into the run; inside = head in the seen part, no leader (runTagTypeName, else the project's most-used stair run
//    tag type; none used in the project -> error, ask the user). Runs are classified like stair_plan_audit. mode
//    preview (rolled back) | apply (logPath) | undo (logPath: deletes what was created, restores path types / text).
// Parameters:
//   runTagPlace: outside (default, as the project sheets): head just outside the side wall of the run's lane, text
//    along the run, leader into the seen part; inside: head inside the seen part, no leader
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class StairPlanAnnotate
{
    const double MM = 304.8;
    static XYZ O, Rg, Up;
    static double VX(XYZ p) { return (p - O).DotProduct(Rg) * MM; }
    static double VY(XYZ p) { return (p - O).DotProduct(Up) * MM; }

    class RunI { public StairsRun R; public Stairs S; public double Z0, Z1, X0, X1, Y0, Y1; public string State, Label; public XYZ P0, P1; }
    class PathChange { public int Id; public int OldType; public bool Up, Down; }
    class Log { public int ViewId; public List<int> Created = new List<int>(); public List<PathChange> Paths = new List<PathChange>(); }

    static double Ov(double a0, double a1, double b0, double b1) { return Math.Min(a1, b1) - Math.Max(a0, b0); }
    static StairsNumberSystemReferenceOption Mirror(StairsNumberSystemReferenceOption o)
    {
        return o == StairsNumberSystemReferenceOption.Left ? StairsNumberSystemReferenceOption.Right : o == StairsNumberSystemReferenceOption.Right ? StairsNumberSystemReferenceOption.Left
            : o == StairsNumberSystemReferenceOption.LeftQuarter ? StairsNumberSystemReferenceOption.RightQuarter : o == StairsNumberSystemReferenceOption.RightQuarter ? StairsNumberSystemReferenceOption.LeftQuarter : o;
    }
    static ElementId MostUsed(IEnumerable<Element> els) { var g = els.GroupBy(e => e.GetTypeId().IntegerValue).OrderByDescending(x => x.Count()).FirstOrDefault(); return g == null ? null : new ElementId(g.Key); }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], logPath = (string)args["logPath"];
        if (mode != "preview" && string.IsNullOrEmpty(logPath)) return new { Error = "logPath is required for apply / undo" };
        if (mode == "undo")
        {
            var lg = JsonConvert.DeserializeObject<Log>(File.ReadAllText(logPath)); var done = new List<string>();
            using (var t = new Transaction(doc, "Stair plan annotate undo"))
            {
                t.Start();
                foreach (var id in lg.Created) { var e = doc.GetElement(new ElementId(id)); if (e != null) { doc.Delete(e.Id); done.Add("deleted " + id); } }
                foreach (var pc in lg.Paths)
                {
                    var p = doc.GetElement(new ElementId(pc.Id)) as StairsPath; if (p == null) continue;
                    if (p.GetTypeId().IntegerValue != pc.OldType) p.ChangeTypeId(new ElementId(pc.OldType));
                    p.ShowUpText = pc.Up; p.ShowDownText = pc.Down; done.Add("restored path " + pc.Id);
                }
                t.Commit();
            }
            return new { Undone = done };
        }

        var v = doc.GetElement(new ElementId(args.Value<int>("viewId"))) as ViewPlan;
        if (v == null) return new { Error = "viewId must be a plan view" };
        O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection;
        var parts = (args["parts"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string> { "path", "numbers", "runTags" };
        var errors = new List<string>(); var notes = new List<string>(); var done1 = new List<object>(); var log = new Log { ViewId = v.Id.IntegerValue };

        // ---- cut plane, runs seen (same classification as stair_plan_audit)
        double lvZ = v.GenLevel != null ? v.GenLevel.ProjectElevation : 0, cut = double.NaN, bottom = double.NegativeInfinity;
        try
        {
            var vr = v.GetViewRange();
            var cl = doc.GetElement(vr.GetLevelId(PlanViewPlane.CutPlane)) as Level; if (cl != null) cut = cl.ProjectElevation + vr.GetOffset(PlanViewPlane.CutPlane);
            var dl = doc.GetElement(vr.GetLevelId(PlanViewPlane.ViewDepthPlane)) as Level; if (dl != null) bottom = dl.ProjectElevation + vr.GetOffset(PlanViewPlane.ViewDepthPlane);
        }
        catch { }
        if (double.IsNaN(cut)) cut = lvZ + 1200 / MM;
        var runs = new List<RunI>();
        foreach (var s in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().OfType<Stairs>())
        {
            double sb = s.BaseElevation;
            var pl = s.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
            var bl = pl != null && pl.StorageType == StorageType.ElementId ? doc.GetElement(pl.AsElementId()) as Level : null;
            if (bl != null) sb = bl.ProjectElevation + (s.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET)?.AsDouble() ?? 0);
            foreach (var rid in s.GetStairsRuns())
            {
                var r = doc.GetElement(rid) as StairsRun; if (r == null) continue;
                var ri = new RunI { R = r, S = s, Z0 = sb + r.BaseElevation, Z1 = sb + r.TopElevation };
                try { var pc = r.GetStairsPath().Cast<Curve>().ToList(); ri.P0 = pc.First().GetEndPoint(0); ri.P1 = pc.Last().GetEndPoint(1); } catch { }
                var fp = new List<XYZ>(); try { foreach (Curve c in r.GetFootprintBoundary()) fp.AddRange(c.Tessellate()); } catch { }
                if (ri.P0 == null || fp.Count == 0) { errors.Add("run " + rid.IntegerValue + ": no path / footprint, skipped"); continue; }
                ri.X0 = fp.Min(VX); ri.X1 = fp.Max(VX); ri.Y0 = fp.Min(VY); ri.Y1 = fp.Max(VY);
                double eps = 10 / MM;
                ri.State = ri.Z0 >= cut - eps ? "above" : ri.Z1 <= cut + eps ? (ri.Z1 < bottom ? "below view depth" : "below") : "cut";
                runs.Add(ri);
            }
        }
        var cutRuns = runs.Where(r => r.State == "cut").ToList();
        foreach (var r in runs.Where(r => r.State == "below"))
        {
            double area = (r.X1 - r.X0) * (r.Y1 - r.Y0);
            r.State = cutRuns.Any(c => { double ox = Ov(r.X0, r.X1, c.X0, c.X1), oy = Ov(r.Y0, r.Y1, c.Y0, c.Y1); return ox > 0 && oy > 0 && ox * oy > 0.3 * area; }) ? "beyond cut" : "full";
        }
        var vis = runs.Where(r => r.State == "beyond cut" || r.State == "full" || r.State == "cut").OrderBy(r => r.Z0).ToList();
        foreach (var r in vis) r.Label = r.State == "beyond cut" ? "V1" : r.State == "full" ? "V2" : "V3";
        if (vis.Count == 0) return new { View = v.Name, Error = "no stair run is seen in this view" };

        // ---- types
        StairsPathType pathType = null; ElementId numType = null, tagType = null;
        if (parts.Contains("path"))
        {
            var fixedTypes = new FilteredElementCollector(doc).OfClass(typeof(StairsPathType)).Cast<StairsPathType>().Where(t => (t.FamilyName ?? "").ToLower().Contains("fixed")).ToList();
            var want = (string)args["pathTypeName"];
            if (want != null) pathType = fixedTypes.FirstOrDefault(t => t.Name == want);
            else
            {
                var used = MostUsed(new FilteredElementCollector(doc).OfClass(typeof(StairsPath)).ToElements().Where(e => fixedTypes.Any(t => t.Id == e.GetTypeId())));
                pathType = used != null ? (StairsPathType)doc.GetElement(used) : fixedTypes.FirstOrDefault();
            }
            if (pathType == null) errors.Add("path: no stair path type of family Fixed Up Direction" + (want != null ? " named '" + want + "'" : "") + " in the project: ask the user");
        }
        if (parts.Contains("numbers"))
        {
            var types = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StairsTriserNumbers).WhereElementIsElementType().ToElements();
            var want = (string)args["numberTypeName"];
            numType = want != null ? types.FirstOrDefault(t => t.Name == want)?.Id : MostUsed(new FilteredElementCollector(doc).OfClass(typeof(NumberSystem)).ToElements()) ?? types.FirstOrDefault()?.Id;
            if (numType == null) errors.Add("numbers: no tread number type" + (want != null ? " named '" + want + "'" : "") + " in the project");
        }
        if (parts.Contains("runTags"))
        {
            var want = (string)args["runTagTypeName"];
            if (want != null) tagType = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StairsRunTags).WhereElementIsElementType().ToElements().FirstOrDefault(t => t.Name == want)?.Id;
            else tagType = MostUsed(new FilteredElementCollector(doc).OfClass(typeof(IndependentTag)).ToElements().Where(e => e.Category != null && e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StairsRunTags));
            if (tagType == null) errors.Add("runTags: " + (want != null ? "no stair run tag type named '" + want + "'" : "no stair run tag is used in the project") + ": ask the user which type");
        }
        string side = ((string)args["numberSide"] ?? "left").ToLower();
        string place = ((string)args["runTagPlace"] ?? "outside").ToLower();
        double headMm = args.Value<double?>("runTagOffsetMm") ?? 5;
        var opt = side == "right" ? StairsNumberSystemReferenceOption.Right : side == "center" ? StairsNumberSystemReferenceOption.Center
            : side == "leftquarter" ? StairsNumberSystemReferenceOption.LeftQuarter : side == "rightquarter" ? StairsNumberSystemReferenceOption.RightQuarter : StairsNumberSystemReferenceOption.Left;

        using (var t = new Transaction(doc, "Stair plan annotate"))
        {
            t.Start();
            // SD: stair paths
            if (pathType != null)
            {
                var existing = new FilteredElementCollector(doc, v.Id).OfClass(typeof(StairsPath)).Cast<StairsPath>().ToList();
                foreach (var s in vis.Select(r => r.S).GroupBy(s => s.Id.IntegerValue).Select(g => g.First()))
                {
                    var mine = existing.Where(p => p.StairsId.HostElementId == s.Id).ToList();
                    if (mine.Count == 0)
                    {
                        try
                        {
                            var p = StairsPath.Create(doc, new LinkElementId(s.Id), pathType.Id, v.Id);
                            p.ShowUpText = false; p.ShowDownText = false; log.Created.Add(p.Id.IntegerValue);
                            done1.Add(new { Part = "path", Stairs = s.Id.IntegerValue, Created = p.Id.IntegerValue, Type = pathType.FamilyName + " : " + pathType.Name });
                        }
                        catch (Exception e) { errors.Add("path for stairs " + s.Id.IntegerValue + ": " + e.Message); }
                        continue;
                    }
                    if (mine.Count > 1) errors.Add("stairs " + s.Id.IntegerValue + " has " + mine.Count + " paths in this view: delete the extra one by hand");
                    foreach (var p in mine)
                    {
                        var ty = doc.GetElement(p.GetTypeId()) as ElementType;
                        bool wrongType = !(ty?.FamilyName ?? "").ToLower().Contains("fixed");
                        if (!wrongType && !p.ShowUpText && !p.ShowDownText) continue;
                        log.Paths.Add(new PathChange { Id = p.Id.IntegerValue, OldType = p.GetTypeId().IntegerValue, Up = p.ShowUpText, Down = p.ShowDownText });
                        try
                        {
                            if (wrongType) p.ChangeTypeId(pathType.Id);
                            p.ShowUpText = false; p.ShowDownText = false;
                            done1.Add(new { Part = "path", Stairs = s.Id.IntegerValue, Fixed = p.Id.IntegerValue, Was = ty?.FamilyName + " : " + ty?.Name, Now = pathType.FamilyName + " : " + pathType.Name + ", no UP/DOWN text" });
                        }
                        catch (Exception e) { errors.Add("path " + p.Id.IntegerValue + ": " + e.Message); }
                    }
                }
            }
            // C: tread numbers
            if (numType != null)
            {
                var numbered = new HashSet<int>();
                foreach (var ns in new FilteredElementCollector(doc, v.Id).OfClass(typeof(NumberSystem)).Cast<NumberSystem>())
                    try { numbered.Add(ns.NumberedElementId.HostElementId.IntegerValue); } catch { }
                foreach (var r in vis.Where(r => !numbered.Contains(r.R.Id.IntegerValue)))
                {
                    try
                    {
                        var o = opt;
                        if (r.State == "beyond cut" && cutRuns.Any(x => Ov(r.X0, r.X1, x.X0, x.X1) > 0 && Ov(r.Y0, r.Y1, x.Y0, x.Y1) > 0)) o = Mirror(opt); // V1 and V3 share the lane
                        var ns = NumberSystem.Create(doc, v.Id, new LinkElementId(r.R.Id), o, new LinkElementId(numType));
                        log.Created.Add(ns.Id.IntegerValue);
                        done1.Add(new { Part = "numbers", Run = r.Label, RunId = r.R.Id.IntegerValue, Created = ns.Id.IntegerValue, Risers = r.R.ActualRisersNumber, Treads = r.R.ActualTreadsNumber, Side = o.ToString() });
                    }
                    catch (Exception e) { errors.Add("numbers on run " + r.Label + " " + r.R.Id.IntegerValue + ": " + e.Message); }
                }
            }
            // SB1: run tags inside the seen part
            if (tagType != null)
            {
                var tagged = new HashSet<int>();
                foreach (var tg in new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                    try { foreach (var id in tg.GetTaggedLocalElementIds()) tagged.Add(id.IntegerValue); } catch { }
                var hostWalls = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Wall)).Cast<Wall>().ToList();
                var coreMid = vis.Select(r => (r.P0 + r.P1) / 2).Aggregate(XYZ.Zero, (acc, p) => acc + p) / vis.Count;
                double paper = v.Scale / MM; // 1 paper mm in model feet
                foreach (var r in vis.Where(r => !tagged.Contains(r.R.Id.IntegerValue)))
                {
                    var d = r.P1 - r.P0; d = new XYZ(d.X, d.Y, 0); double len = d.GetLength(); if (len < 1e-6) continue; var u = d / len;
                    double f0 = 0, f1 = 1;
                    if (r.State == "cut") f1 = Math.Max(0.2, Math.Min(0.9, (cut - r.Z0) / (r.Z1 - r.Z0)));
                    else if (r.State == "beyond cut")
                    {   // the seen part starts at the cut line of the cut run in the same lane
                        var c = cutRuns.FirstOrDefault(x => Ov(r.X0, r.X1, x.X0, x.X1) > 0 && Ov(r.Y0, r.Y1, x.Y0, x.Y1) > 0);
                        if (c != null)
                        {
                            double fc = Math.Max(0, Math.Min(1, (cut - c.Z0) / (c.Z1 - c.Z0)));
                            var pc = c.P0 + (c.P1 - c.P0) * fc;
                            f0 = Math.Max(0, Math.Min(0.8, (new XYZ(pc.X, pc.Y, r.P0.Z) - r.P0).DotProduct(u) / len));
                        }
                    }
                    double w = r.R.ActualRunWidth;
                    var left = XYZ.BasisZ.CrossProduct(u); // left of walking up
                    var mid = r.P0 + u * (len * (f0 + f1) / 2);
                    // outward = away from the core centre (single lane: left)
                    double so = (new XYZ(mid.X - coreMid.X, mid.Y - coreMid.Y, 0)).DotProduct(left);
                    var n = Math.Abs(so) < 0.1 ? left : left * Math.Sign(so);
                    XYZ head, end = null; bool outside = place != "inside";
                    if (outside)
                    {   // outer face of the wall stack beside the lane (plan bounding boxes of host walls parallel to the run)
                        double half = w / 2, outer = double.NaN;
                        var spans = new List<double[]>();
                        foreach (var wl in hostWalls)
                        {
                            var ln = (wl.Location as LocationCurve)?.Curve as Line; if (ln == null || Math.Abs(ln.Direction.DotProduct(u)) < 0.999) continue;
                            var bb = wl.get_BoundingBox(null); if (bb == null) continue;
                            var cs = new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, 0), new XYZ(bb.Max.X, bb.Min.Y, 0) };
                            double a0 = cs.Min(p => (new XYZ(p.X, p.Y, 0) - new XYZ(r.P0.X, r.P0.Y, 0)).DotProduct(u)), a1 = cs.Max(p => (new XYZ(p.X, p.Y, 0) - new XYZ(r.P0.X, r.P0.Y, 0)).DotProduct(u));
                            if (Ov(a0, a1, 0, len) <= 0) continue;
                            double n0 = cs.Min(p => (new XYZ(p.X - mid.X, p.Y - mid.Y, 0)).DotProduct(n)), n1 = cs.Max(p => (new XYZ(p.X - mid.X, p.Y - mid.Y, 0)).DotProduct(n));
                            if (n0 >= half - 50 / MM && n0 <= half + 1000 / MM) spans.Add(new[] { n0, n1 });
                        }
                        if (spans.Count > 0)
                        {
                            double near = spans.Min(x => x[0]); outer = spans.Where(x => Math.Abs(x[0] - near) < 5 / MM).Max(x => x[1]);
                            for (bool grew = true; grew;) { grew = false; foreach (var x in spans) if (Math.Abs(x[0] - outer) < 5 / MM && x[1] > outer) { outer = x[1]; grew = true; } }
                        }
                        else { outer = half + 300 / MM; notes.Add("run " + r.Label + ": no host wall beside it, tag placed 300 mm outside the run"); }
                        head = mid + n * (outer + headMm * paper);
                        end = mid + n * (w / 4);
                    }
                    else
                    {
                        double sgn = opt == StairsNumberSystemReferenceOption.Left || opt == StairsNumberSystemReferenceOption.LeftQuarter ? -1 : 1;
                        head = mid + left * (sgn * w / 4);
                    }
                    bool vertical = Math.Abs(u.DotProduct(Up)) > Math.Abs(u.DotProduct(Rg));
                    try
                    {
                        var tg = IndependentTag.Create(doc, tagType, v.Id, new Reference(r.R), outside, vertical ? TagOrientation.Vertical : TagOrientation.Horizontal, head);
                        if (outside)
                        {
                            tg.LeaderEndCondition = LeaderEndCondition.Free;
                            tg.SetLeaderEnd(new Reference(r.R), end);
                            tg.TagHeadPosition = head;
                        }
                        log.Created.Add(tg.Id.IntegerValue);
                        done1.Add(new { Part = "runTag", Run = r.Label, RunId = r.R.Id.IntegerValue, Created = tg.Id.IntegerValue, Text = tg.TagText, Place = outside ? "outside the wall, leader into the run" : "inside the run", HeadMm = Math.Round(VX(head)) + "," + Math.Round(VY(head)) });
                    }
                    catch (Exception e) { errors.Add("tag on run " + r.Label + " " + r.R.Id.IntegerValue + ": " + e.Message); }
                }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(log, Formatting.Indented)); }
            else t.RollBack();
        }
        return new
        {
            View = v.Name, Mode = mode,
            Runs = vis.Select(r => r.Label + " " + r.R.Id.IntegerValue + " (" + r.State + ", " + r.R.ActualTreadsNumber + "T)").ToList(),
            Done = done1, Notes = notes, Errors = errors
        };
    }
}
