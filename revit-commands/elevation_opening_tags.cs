/* mcp-tool
{
  "description": "Door/window tags on elevation and section views. Finds the doors/windows really visible in each view (rays from the view plane), adds a tag to every one without a tag and checks the tags already there. Placement rules: the tag head never sits on its own element; door tags only directly ABOVE the door, window tags directly above or below, touching the element (gapMm); best spot = tag over plain wall face / empty. Every spot is scored by what lies under the tag head: model elements seen in the view (ray cast in the default 3D view, links included: stairs, railings, lifts/equipment, columns, beams, other doors/windows... = hard; floors, roofs, curtain panels/mullions, levels/grids lines = soft) and annotation (dimension lines/texts, other tags, text notes = hard). Existing tags: reports on_host, door tag not above, window tag beside, far from the element, covering things; fixExisting moves the wrongly placed ones (no leader) to a valid spot. Tags still covering a dimension or a model element are coloured red in the view (original overrides saved in logPath). mode=preview (default, nothing kept) | apply | undo (deletes created tags, moves tags back, restores overrides from logPath). Never syncs.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "mode": { "type": "string", "enum": ["preview", "apply", "undo", "clearhighlight"], "description": "clearhighlight: restore the original overrides of the red tags (tags stay where they are)." },
      "repositionAll": { "type": "boolean", "description": "Re-place every door/window tag of the view by the rules (above + centred first), not only wrong ones. Default false." },
      "addLeader": { "type": "boolean", "description": "Give every processed tag a free-end vertical leader ending leaderInsetMm inside the element edge. Logged for undo. Default false." },
      "leaderInsetMm": { "type": "number", "description": "Paper mm the leader end goes inside the element. Default 1.5." },
      "allowMultipleViews": { "type": "boolean", "description": "Default false: one view per call (elevations/sections are heavy)." },
      "maxSeconds": { "type": "number", "description": "Time budget; when exceeded everything is rolled back. Default 90." },
      "maxOffsetSteps": { "type": "number", "description": "Candidate offsets from the element (1 / 2.5 / 4 mm paper ...). Default 3." },
      "debugIds": { "type": "array", "items": { "type": "number" } },
      "debugXY": { "type": "array", "items": { "type": "number" } },
      "viewIds": { "type": "array", "items": { "type": "number" } },
      "sheetNumbers": { "type": "array", "items": { "type": "string" }, "description": "Use the Elevation/Section views placed on these sheets." },
      "excludeIds": { "type": "array", "items": { "type": "number" }, "description": "Doors/windows never tagged (e.g. known hidden ones)." },
      "doorTagTypeId": { "type": "number", "description": "Default: the door tag type used most in the chosen views." },
      "windowTagTypeId": { "type": "number" },
      "gapMm": { "type": "number", "description": "Paper mm between element and tag head. Default 1.0." },
      "maxGapMm": { "type": "number", "description": "Paper mm: an existing tag (no leader) farther than this from its element is reported 'far'. Default 4." },
      "fixExisting": { "type": "boolean", "description": "Move existing tags (no leader) that sit on their element, a door tag not above its door, a window tag beside its window, or that cover a dim/model element, to a clean valid spot. Default false." },
      "highlight": { "type": "boolean", "description": "Colour tags that still cover a dim or model element red (apply only). Default true." },
      "logPath": { "type": "string", "description": "JSON log (created tags, moves, original overrides). Required for apply/undo." },
      "rollupInside": { "type": "boolean", "description": "Roll-up doors ONLY (family name contains a rollupFamilies part): when the tag head is <= 1/3 of the visible door width and <= 1/4 of its visible height, the tag may sit inside the door, centred, no leader; such existing tags are accepted. Default true." },
      "rollupFamilies": { "type": "array", "items": { "type": "string" }, "description": "Default ['ROLL UP']." },
      "maxItems": { "type": "number", "description": "Max rows per list per view. Default 300." }
    }
  },
  "timeoutSeconds": 1800,
  "readOnly": false
}
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ElevationOpeningTags
{
    const double MM = 304.8;

    class R
    {
        public double X0, X1, Y0, Y1; public string What; public bool Soft;
        public R(double a, double b, double c, double d, string w) { X0 = Math.Min(a, b); X1 = Math.Max(a, b); Y0 = Math.Min(c, d); Y1 = Math.Max(c, d); What = w; }
        public bool Hits(R o) { return X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1; }
        public R Move(double dx, double dy) { return new R(X0 + dx, X1 + dx, Y0 + dy, Y1 + dy, What) { Soft = Soft }; }
        public double Cx { get { return (X0 + X1) / 2; } }
        public double Cy { get { return (Y0 + Y1) / 2; } }
    }
    class RedItem { public int Tag; public string Text, Opening, Covers; public bool New; }
    class Opening { public FamilyInstance Fi; public R Rc; public bool IsDoor; public string Name; public List<IndependentTag> Tags = new List<IndependentTag>(); }
    class Score
    {
        public List<string> Hard = new List<string>(); public List<string> Soft = new List<string>(); public bool Outside;
        public int H { get { return Outside ? 999 : Hard.Count; } }
        public string Text { get { return (Outside ? "outside crop; " : "") + string.Join(", ", Hard) + (Soft.Count > 0 ? (Hard.Count > 0 ? " | soft: " : "soft: ") + string.Join(", ", Soft) : ""); } }
    }

    // ---------- per-view state ----------
    static Document doc; static View v;
    static XYZ O, Rg, Up, Vd; static double P1;      // P1 = 1 paper mm in model feet
    static ReferenceIntersector occl; static double far;
    static R crop;
    static Dictionary<string, List<Tuple<double, int>>> rayCache;   // visible occluder hits per point
    class Ob { public R Rc; public double Near; public bool Cut; public int HostId = -1; public string Name; public Element E; public Transform Tf; public List<Solid> Solids; public List<double[]> Segs; public bool? Complex; }
    static HashSet<string> complexSkipped = new HashSet<string>(); static int filterHiddenCount;
    static List<Ob> modelObs;
    static int rays;
    static System.Diagnostics.Stopwatch clock; static double budget;
    static List<string> stages = new List<string>();
    static void Stage(string s) { stages.Add(s + " @" + Math.Round(clock.Elapsed.TotalSeconds, 1) + "s rays " + rays + " obSeen " + obSeenCalls + "/" + Math.Round(obSeenSec, 1) + "s"); }
    static void CheckTime()
    {
        if (clock != null && clock.Elapsed.TotalSeconds > budget)
            throw new TimeoutException("time budget of " + budget + " s exceeded - nothing kept (rolled back). Stages: " + string.Join(" | ", stages) + " | stopped @" + Math.Round(clock.Elapsed.TotalSeconds, 1) + "s rays " + rays + " obSeen " + obSeenCalls + "/" + Math.Round(obSeenSec, 1) + "s. Slowest: " + TopCost());
    }

    static double VX(XYZ p) { return (p - O).DotProduct(Rg); }
    static double VY(XYZ p) { return (p - O).DotProduct(Up); }
    static double Depth(XYZ p) { return -(p - O).DotProduct(Vd); }   // > 0 behind the view plane
    static XYZ W(double x, double y) { return O + Rg * x + Up * y; }

    static R Proj(BoundingBoxXYZ bb, string what)
    {
        if (bb == null) return null;
        var t = bb.Transform ?? Transform.Identity;
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
        foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                {
                    var p = t.OfPoint(new XYZ(x, y, z));
                    x0 = Math.Min(x0, VX(p)); x1 = Math.Max(x1, VX(p)); y0 = Math.Min(y0, VY(p)); y1 = Math.Max(y1, VY(p));
                }
        return new R(x0, x1, y0, y1, what);
    }

    // box (model coords, optional transform) -> view rect + depth range
    static Tuple<R, double, double> ProjDepth(BoundingBoxXYZ bb, Transform tf, string what)
    {
        if (bb == null) return null;
        var t = (tf ?? Transform.Identity).Multiply(bb.Transform ?? Transform.Identity);
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue, d0 = double.MaxValue, d1 = double.MinValue;
        foreach (var x in new[] { bb.Min.X, bb.Max.X }) foreach (var y in new[] { bb.Min.Y, bb.Max.Y }) foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                {
                    var p = t.OfPoint(new XYZ(x, y, z));
                    x0 = Math.Min(x0, VX(p)); x1 = Math.Max(x1, VX(p)); y0 = Math.Min(y0, VY(p)); y1 = Math.Max(y1, VY(p));
                    d0 = Math.Min(d0, Depth(p)); d1 = Math.Max(d1, Depth(p));
                }
        return Tuple.Create(new R(x0, x1, y0, y1, what), d0, d1);
    }

    // extents of the element's geometry as drawn in the view
    static R Extents(Element e, string what)
    {
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue; int n = 0;
        Action<XYZ> add = p => { x0 = Math.Min(x0, VX(p)); x1 = Math.Max(x1, VX(p)); y0 = Math.Min(y0, VY(p)); y1 = Math.Max(y1, VY(p)); n++; };
        Action<GeometryElement, Transform> walk = null;
        walk = (g, tr) =>
        {
            if (g == null) return;
            foreach (var o in g)
            {
                if (o is GeometryInstance gi) walk(gi.GetSymbolGeometry(), tr.Multiply(gi.Transform));
                else if (o is Solid s && s.Volume > 1e-9) foreach (Edge ed in s.Edges) foreach (var p in ed.Tessellate()) add(tr.OfPoint(p));
                else if (o is Curve c) foreach (var p in c.Tessellate()) add(tr.OfPoint(p));
            }
        };
        try { walk(e.get_Geometry(new Options { View = v }), Transform.Identity); } catch { }
        if (n < 2 || x1 - x0 < 1e-6 || y1 - y0 < 1e-6) return Proj(e.get_BoundingBox(v) ?? e.get_BoundingBox(null), what);
        return new R(x0, x1, y0, y1, what);
    }

    static bool CatHidden(Element e)
    {
        try { if (e.Category != null && v.GetCategoryHidden(e.Category.Id)) return true; } catch { }
        return FilterHidden(e);
    }

    // view filters (of the view or its template) whose Visibility is off
    static List<Tuple<ElementFilter, HashSet<int>>> hiddenFilters;
    static Dictionary<string, bool> filterCache;
    static void LoadFilters()
    {
        hiddenFilters = new List<Tuple<ElementFilter, HashSet<int>>>(); filterCache = new Dictionary<string, bool>();
        var src = v.ViewTemplateId != ElementId.InvalidElementId && doc.GetElement(v.ViewTemplateId) is View tv ? tv : v;
        foreach (var fid in src.GetFilters())
        {
            bool visible = true; try { visible = src.GetFilterVisibility(fid); } catch { }
            if (visible) continue;
            if (doc.GetElement(fid) is ParameterFilterElement pf)
            {
                ElementFilter ef = null; try { ef = pf.GetElementFilter(); } catch { }
                hiddenFilters.Add(Tuple.Create(ef, new HashSet<int>(pf.GetCategories().Select(c => c.IntegerValue))));
            }
            else if (doc.GetElement(fid) is SelectionFilterElement sf)
                hiddenFilters.Add(Tuple.Create((ElementFilter)new ElementIdSetFilter(sf.GetElementIds()), (HashSet<int>)null));
        }
    }
    static bool FilterHidden(Element e)
    {
        if (hiddenFilters == null || hiddenFilters.Count == 0 || e.Category == null) return false;
        string k = e.Document.Title + ":" + e.Id.IntegerValue;
        if (filterCache.TryGetValue(k, out var r)) return r;
        r = false;
        foreach (var f in hiddenFilters)
        {
            if (f.Item2 != null && !f.Item2.Contains(e.Category.Id.IntegerValue)) continue;
            if (f.Item2 == null && e.Document != doc) continue;
            try { if (f.Item1 == null ? f.Item2 != null : f.Item1.PassesFilter(e)) { r = true; break; } } catch { }
        }
        filterCache[k] = r;
        return r;
    }

    // walls / slabs / roofs / curtain panels of the host model that hide what is behind them
    static readonly BuiltInCategory[] OccluderCats = {
        BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_CurtainWallPanels,
        BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows };   // glazing hides what is behind it in hidden line

    // elements that must not be covered by a tag (looked up in the host model and in visible links)
    static readonly BuiltInCategory[] HardCats = {
        BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsRuns, BuiltInCategory.OST_StairsLandings, BuiltInCategory.OST_StairsStringerCarriage,
        BuiltInCategory.OST_StairsRailing, BuiltInCategory.OST_Railings, BuiltInCategory.OST_RailingTopRail, BuiltInCategory.OST_RailingHandRail,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_SpecialityEquipment,
        BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_Furniture,
        BuiltInCategory.OST_Casework, BuiltInCategory.OST_PlumbingFixtures };
    static readonly HashSet<int> CompactCats = new HashSet<int>(new[] {
        BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns }.Select(c => (int)c));
    static readonly BuiltInCategory[] SlabCats = {
        BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFoundation };

    // true when a host occluder lies between the view plane and depth d at (x,y)
    static List<Tuple<double, int>> OccHits(double x, double y)
    {
        string k = Math.Round(x * MM / 50) + "|" + Math.Round(y * MM / 50);
        if (!rayCache.TryGetValue(k, out var hits))
        {
            hits = new List<Tuple<double, int>>();
            rays++;
            foreach (var h in occl.Find(W(x, y) + Vd * (5 / MM), -Vd))
            {
                var e = doc.GetElement(h.GetReference().ElementId);
                if (e == null || e.IsHidden(v) || CatHidden(e)) continue;
                hits.Add(Tuple.Create(h.Proximity - 5 / MM, e.Id.IntegerValue));
            }
            hits = hits.OrderBy(h => h.Item1).ToList();
            rayCache[k] = hits;
        }
        return hits;
    }

    static bool Occluded(double x, double y, double d, HashSet<int> excl)
    {
        if (d <= 0) return false;
        // 5 mm: a wall face just in front of an element buried in the wall (beam/column in the wall thickness) hides it
        return OccHits(x, y).Any(h => h.Item1 < d - 5 / MM && (excl == null || !excl.Contains(h.Item2)));
    }

    // first height (from the top / bottom of the element's box inwards) where the element itself is the first thing seen:
    // its drawn edge. Parts behind the host wall (roll-up door coil boxes, frames in the wall) do not count.
    static double VisibleEdge(Opening op, double x, bool top)
    {
        var H = op.Rc; int id = op.Fi.Id.IntegerValue;
        double step = 50 / MM;
        for (int i = 0; i < 200; i++)
        {
            double y = top ? H.Y1 - 10 / MM - i * step : H.Y0 + 10 / MM + i * step;
            if (top ? y < H.Cy : y > H.Cy) break;
            var first = OccHits(x, y).FirstOrDefault();
            if (first != null && first.Item2 == id) return y;
        }
        return top ? H.Y1 : H.Y0;
    }

    static List<Solid> SolidsOf(Ob ob)
    {
        if (ob.Solids != null) return ob.Solids;
        var list = new List<Solid>();
        Action<GeometryElement> walk = null;
        walk = g =>
        {
            if (g == null) return;
            foreach (var o in g)
            {
                if (o is Solid s && s.Volume > 1e-9) list.Add(ob.Tf == null ? s : SolidUtils.CreateTransformed(s, ob.Tf));
                else if (o is GeometryInstance gi) walk(gi.GetInstanceGeometry());
            }
        };
        // host elements: only the geometry this view draws (hidden subcategories such as clearance envelopes drop out)
        try { walk(ob.E.get_Geometry(ob.Tf == null ? new Options { View = v } : new Options { DetailLevel = ViewDetailLevel.Medium })); } catch { }
        ob.Solids = list;
        return list;
    }

    // depth of the element's first face along the sight line at (x,y); null = the line misses the element
    static double? HitDepth(Ob ob, double x, double y)
    {
        var solids = SolidsOf(ob);
        if (solids.Count == 0) return null;                      // no solids (curves / meshes only): the edge test decides
        var p = W(x, y);
        var line = Line.CreateBound(p + Vd * (1 / MM), p - Vd * (far > 0 ? far : 3000));
        double best = double.MaxValue;
        foreach (var s in solids)
        {
            try
            {
                var r = s.IntersectWithCurve(line, new SolidCurveIntersectionOptions());
                for (int k = 0; k < r.SegmentCount; k++) best = Math.Min(best, Depth(r.GetCurveSegment(k).GetEndPoint(0)));
            }
            catch { }
        }
        return best == double.MaxValue ? (double?)null : Math.Max(0, best);
    }

    // solid edges projected on the view: {x0, y0, x1, y1, depth0, depth1}
    static List<double[]> SegsOf(Ob ob)
    {
        if (ob.Segs != null) return ob.Segs;
        var list = new List<double[]>();
        Action<IList<XYZ>> addPts = pts => { for (int i = 1; i < pts.Count; i++) list.Add(new[] { VX(pts[i - 1]), VY(pts[i - 1]), VX(pts[i]), VY(pts[i]), Depth(pts[i - 1]), Depth(pts[i]) }); };
        foreach (var s in SolidsOf(ob))
            foreach (Edge e in s.Edges) { try { addPts(e.Tessellate()); } catch { } }
        // curves and meshes the family draws (some equipment has no solids at all)
        Action<GeometryElement> walk = null;
        walk = g =>
        {
            if (g == null) return;
            foreach (var o in g)
            {
                if (o is GeometryInstance gi) walk(gi.GetInstanceGeometry());
                else if (o is Curve c) { try { addPts(c.Tessellate().Select(p => ob.Tf == null ? p : ob.Tf.OfPoint(p)).ToList()); } catch { } }
                else if (o is PolyLine pl) addPts(pl.GetCoordinates().Select(p => ob.Tf == null ? p : ob.Tf.OfPoint(p)).ToList());
                else if (o is Mesh m)
                    for (int i = 0; i < m.NumTriangles; i++)
                    {
                        var tr = m.get_Triangle(i);
                        var p = new[] { tr.get_Vertex(0), tr.get_Vertex(1), tr.get_Vertex(2), tr.get_Vertex(0) }.Select(q => ob.Tf == null ? q : ob.Tf.OfPoint(q)).ToList();
                        addPts(p);
                    }
            }
        };
        if (list.Count < 200000)
            try { walk(ob.E.get_Geometry(ob.Tf == null ? new Options { View = v } : new Options { DetailLevel = ViewDetailLevel.Medium })); } catch { }
        ob.Segs = list;
        return list;
    }

    // Liang-Barsky: parameter range [t0,t1] of segment inside rect, or null
    static double[] Clip(double x0, double y0, double x1, double y1, R r)
    {
        double dx = x1 - x0, dy = y1 - y0, t0 = 0, t1 = 1;
        double[] p = { -dx, dx, -dy, dy }, q = { x0 - r.X0, r.X1 - x0, y0 - r.Y0, r.Y1 - y0 };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-12) { if (q[i] < 0) return null; continue; }
            double tt = q[i] / p[i];
            if (p[i] < 0) t0 = Math.Max(t0, tt); else t1 = Math.Min(t1, tt);
            if (t0 > t1) return null;
        }
        return new[] { t0, t1 };
    }

    // the element is really drawn somewhere under rect t: a visible edge inside t, or geometry on a sight line inside t
    static bool ObSeen(Ob ob, R t)
    {
        double x0 = Math.Max(ob.Rc.X0, t.X0), x1 = Math.Min(ob.Rc.X1, t.X1), y0 = Math.Max(ob.Rc.Y0, t.Y0), y1 = Math.Min(ob.Rc.Y1, t.Y1);
        var excl = ob.HostId > 0 ? new HashSet<int> { ob.HostId } : null;
        // compact elements cut by the section (slab / roof / beam / column / footing poche): their box on the plane is their drawing
        if (ob.Cut && ob.E.Category != null && CompactCats.Contains(ob.E.Category.Id.IntegerValue)) return true;
        // cheap pre-check: an occluder in front of the element's nearest face over the whole overlap -> hidden there
        if (!ob.Cut)
        {
            bool open = false;
            for (int i = 0; i < 3 && !open; i++)
                for (int j = 0; j < 3 && !open; j++)
                    if (!Occluded(x0 + (x1 - x0) * (0.1 + 0.4 * i), y0 + (y1 - y0) * (0.1 + 0.4 * j), ob.Near, excl)) open = true;
            if (!open) return false;
        }
        obSeenCalls++;
        if (ob.Complex == null) ob.Complex = SolidsOf(ob).Sum(s => s.Edges.Size) > 15000 || SegsOf(ob).Count > 60000;
        if (ob.Complex == true) { complexSkipped.Add(ob.Name); return false; }   // site fences etc.: too heavy to test, reported
        var swS = System.Diagnostics.Stopwatch.StartNew();
        try
        {
        // cut elements (section poche) and very complex ones: sight-line sampling only
        foreach (var sg in SegsOf(ob))
        {
            var c = Clip(sg[0], sg[1], sg[2], sg[3], t);
            if (c == null) continue;
            double len = (c[1] - c[0]) * Math.Sqrt((sg[2] - sg[0]) * (sg[2] - sg[0]) + (sg[3] - sg[1]) * (sg[3] - sg[1]));
            if (len < 0.3 * P1) continue;
            double m = (c[0] + c[1]) / 2, mx = sg[0] + (sg[2] - sg[0]) * m, my = sg[1] + (sg[3] - sg[1]) * m, md = sg[4] + (sg[5] - sg[4]) * m;
            if (md < 0 || (far > 0 && md > far)) continue;           // in front of the view plane / beyond far clip: not drawn
            if (md < 20 / MM || !Occluded(mx, my, md, excl))
            {
                if (dbg) debugRows.Add("  " + ob.Name + " SEG at x " + Math.Round(mx * MM) + " y " + Math.Round(my * MM) + " depth " + Math.Round(md * MM) + " seg " + Math.Round(sg[0] * MM) + "," + Math.Round(sg[1] * MM) + " -> " + Math.Round(sg[2] * MM) + "," + Math.Round(sg[3] * MM) + " solids " + SolidsOf(ob).Count);
                return true;
            }
        }
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 3; j++)
            {
                double x = x0 + (x1 - x0) * (0.05 + 0.9 * i / 4), y = y0 + (y1 - y0) * (0.1 + 0.8 * j / 2);
                var d = HitDepth(ob, x, y);
                if (d == null) continue;
                if (d.Value < 20 / MM || !Occluded(x, y, d.Value, excl))
                {
                    if (dbg) debugRows.Add("  " + ob.Name + " LINE at x " + Math.Round(x * MM) + " y " + Math.Round(y * MM) + " depth " + Math.Round(d.Value * MM));
                    return true;
                }
            }
        return false;
        }
        finally
        {
            obSeenSec += swS.Elapsed.TotalSeconds;
            obCost[ob.Name] = (obCost.TryGetValue(ob.Name, out var c0) ? c0 : 0) + swS.Elapsed.TotalSeconds;
        }
    }
    static int obSeenCalls; static double obSeenSec; static Dictionary<string, double> obCost = new Dictionary<string, double>();
    static string TopCost() { return string.Join("; ", obCost.OrderByDescending(k => k.Value).Take(8).Select(k => k.Key + " " + Math.Round(k.Value, 1) + "s")); }

    static double TextH(Dimension d)
    {
        try { return d.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * v.Scale; } catch { return 2.5 * P1; }
    }
    static R TextBox(bool vertical, double x, double y, string txt, double h)
    {
        double w = Math.Max(1, (txt ?? "").Length) * 0.6 * h + 0.3 * h;
        return vertical ? new R(x - 1.4 * h, x - 0.1 * h, y - w / 2, y + w / 2, "dim text")
                        : new R(x - w / 2, x + w / 2, y + 0.1 * h, y + 1.4 * h, "dim text");
    }

    // ---------- scoring ----------
    static Score Evaluate(R t, List<R> obst, Element self, Element ownHost)
    {
        CheckTime();
        var s = new Score();
        if (crop != null && (t.X0 < crop.X0 || t.X1 > crop.X1 || t.Y0 < crop.Y0 || t.Y1 > crop.Y1)) s.Outside = true;
        foreach (var o in obst)
            if (o.Hits(t)) { if (o.Soft) { if (!s.Soft.Contains(o.What)) s.Soft.Add(o.What); } else if (!s.Hard.Contains(o.What)) s.Hard.Add(o.What); }
        foreach (var ob in modelObs)
            if (ob.Rc.Hits(t) && !s.Hard.Contains(ob.Name) && !s.Soft.Contains(ob.Name) && ObSeen(ob, t))
            {
                // linked beams that are not cut, over WINDOW tags (window rows under ring beams) were accepted by the user: soft.
                // over door tags the user lifted the tag above the beam: hard.
                bool winTag = ownHost?.Category != null && ownHost.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows;
                if (winTag && ob.HostId < 0 && !ob.Cut && ob.E.Category != null && ob.E.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming) s.Soft.Add(ob.Name);
                else s.Hard.Add(ob.Name);
            }
        return s;
    }

    static bool Better(Score a, Score b) { return b == null || a.H < b.H || (a.H == b.H && a.Soft.Count < b.Soft.Count); }

    // candidate head rects for a tag of size w x h around opening rect H
    static IEnumerable<Tuple<R, string>> Candidates(Opening op, double w, double h, double gap, string onlySide = null)
    {
        var H = op.Rc; double hw = H.X1 - H.X0;
        var xs = new List<double> { H.Cx };
        foreach (var f in new[] { 0.2, 0.4, 0.6, 0.8 })
        {
            double d = f * Math.Max(hw / 2, w / 2);
            xs.Add(H.Cx - d); xs.Add(H.Cx + d);
        }
        xs = xs.Where(x => x >= H.X0 - 1e-6 && x <= H.X1 + 1e-6).ToList();   // tag centre stays over the element
        var sides = op.IsDoor ? new[] { "above" } : new[] { "above", "below" };
        if (onlySide != null) sides = sides.Where(s => s == onlySide).ToArray();
        for (int k = 0; k < maxSteps; k++)
            foreach (var x in xs)
                foreach (var side in sides)
                {
                    double off = gap + k * stepMm * P1;
                    var r = side == "above" ? new R(x - w / 2, x + w / 2, H.Y1 + off, H.Y1 + off + h, "new tag")
                                            : new R(x - w / 2, x + w / 2, H.Y0 - off - h, H.Y0 - off, "new tag");
                    yield return Tuple.Create(r, side + (k > 0 ? " +" + (k * stepMm) + "mm" : "") + (Math.Abs(x - H.Cx) > 1e-6 ? " shifted" : ""));
                }
    }

    static bool dbg; static int maxSteps = 3;   // offsets 1 / 2.5 / 4 mm paper: stay close, a covered close spot is coloured instead
    static HashSet<int> debugIds = new HashSet<int>(); static List<string> debugRows = new List<string>();
    static string Mm(R r) { return "x " + Math.Round(r.X0 * MM) + ".." + Math.Round(r.X1 * MM) + " y " + Math.Round(r.Y0 * MM) + ".." + Math.Round(r.Y1 * MM); }

    static bool leaderMode; static double stepMm = 1.5;

    // ---------- roll-up doors: tag inside the door when the tag is small next to it (user, 2026-10-06; roll-up doors only) ----------
    static List<string> rollupFams = new List<string>(); static bool rollupInside = true;
    static bool IsRollup(Opening op) { return op.IsDoor && rollupFams.Any(f => (op.Fi.Symbol.FamilyName ?? "").ToUpperInvariant().Contains(f)); }
    static R VisRect(Opening op) { var H = op.Rc; double top = VisibleEdge(op, H.Cx, true); return new R(H.X0, H.X1, H.Y0, Math.Max(H.Y0 + 1e-6, Math.Min(H.Y1, top)), "visible"); }
    static bool InsideOk(Opening op, double w, double h)
    {
        if (!rollupInside || !IsRollup(op)) return false;
        var V = VisRect(op); return w <= (V.X1 - V.X0) / 3 + 1e-9 && h <= (V.Y1 - V.Y0) / 4 + 1e-9;
    }
    static bool IsInside(Opening op, R t) { var V = VisRect(op); return t.X0 >= V.X0 && t.X1 <= V.X1 && t.Y0 >= V.Y0 && t.Y1 <= V.Y1; }
    static Tuple<R, string, Score> InsideSpot(Opening op, double w, double h, List<R> obst, Element self)
    {
        var V = VisRect(op); double cx = (V.X0 + V.X1) / 2, cy = (V.Y0 + V.Y1) / 2, vw = V.X1 - V.X0, vh = V.Y1 - V.Y0;
        var own = obst.Where(o => o != op.Rc).ToList();
        Tuple<R, string, Score> best = null;
        foreach (var fy in new[] { 0.0, 0.15, -0.15, 0.3, -0.3 })
            foreach (var fx in new[] { 0.0, 0.2, -0.2 })
            {
                var r = new R(cx + fx * vw - w / 2, cx + fx * vw + w / 2, cy + fy * vh - h / 2, cy + fy * vh + h / 2, "new tag");
                if (!IsInside(op, r)) continue;
                var s = Evaluate(r, own, self, op.Fi);
                s.Hard.RemoveAll(n => n == op.Name || n.Contains(op.Fi.Id.IntegerValue.ToString()));   // the door itself is not an obstacle here
                if (best == null || s.H < best.Item3.H) best = Tuple.Create(r, "inside roll-up door" + (fx != 0 || fy != 0 ? " shifted" : ""), s);
                if (s.H == 0) return best;
            }
        return best;
    }

    // right / left of the door, tag top level with the door top (then a little lower)
    static Tuple<R, string, Score> Beside(Opening op, double w, double h, double gap, List<R> obst, Element self)
    {
        var H = op.Rc; Tuple<R, string, Score> best = null;
        foreach (var dy in new[] { 0.0, 1.5 * P1, 3 * P1 })
            foreach (var right in new[] { true, false })
            {
                double x0 = right ? H.X1 + 1.5 * P1 : H.X0 - 1.5 * P1 - w;
                var r = new R(x0, x0 + w, H.Y1 - dy - h, H.Y1 - dy, "new tag");
                var s = Evaluate(r, obst, self, op.Fi);
                if (best == null || s.H < best.Item3.H) best = Tuple.Create(r, "beside " + (right ? "right" : "left") + (dy > 0 ? " -" + Math.Round(dy / P1, 1) + "mm" : ""), s);
                if (s.H == 0) return best;
            }
        return best;
    }

    static Tuple<R, string, Score> BestSpot(Opening op, double w, double h, double gap, List<R> obst, Element self)
    {
        // roll-up doors only: a small tag goes inside the door when a clean spot exists there
        if (InsideOk(op, w, h))
        {
            var ins = InsideSpot(op, w, h, obst, self);
            if (ins != null && ins.Item3.H == 0) return ins;
        }
        // user's rule: tag ABOVE its own element, centred, touching; level / grid lines may cross it.
        // below (windows only) is used only when it covers fewer things than the best spot above.
        Func<string, Tuple<R, string, Score>> search = side =>
        {
            Tuple<R, string, Score> best = null;
            foreach (var c in Candidates(op, w, h, gap, side))      // order: closest first, centred first
            {
                var s = Evaluate(c.Item1, obst, self, op.Fi);
                if (debugIds.Contains(op.Fi.Id.IntegerValue))
                    debugRows.Add(op.Name + " | host " + Mm(op.Rc) + " | " + c.Item2 + " " + Mm(c.Item1) + " -> " + s.Text);
                if (best == null || s.H < best.Item3.H) best = Tuple.Create(c.Item1, c.Item2, s);
                if (s.H == 0) break;
            }
            return best;
        };
        var above = search("above");
        if (above != null && above.Item3.H == 0) return above;
        if (leaderMode)
        {
            // with a leader the user lifts the tag onto the next clean stretch of wall (up to ~10 mm paper)
            int keep = maxSteps; maxSteps = 13; stepMm = 0.75;          // 1 .. 10 mm paper in 0.75 mm steps
            var higher = search("above");
            maxSteps = keep; stepMm = 1.5;
            if (higher != null && higher.Item3.H < (above?.Item3.H ?? 999)) above = higher;
            if (above != null && above.Item3.H == 0) return above;
            if (op.IsDoor)
            {
                // still blocked: the user puts the tag beside the door, level with its top, leader across
                var side = Beside(op, w, h, gap, obst, self);
                if (side != null && side.Item3.H < (above?.Item3.H ?? 999)) return side;
            }
        }
        if (op.IsDoor) return above;
        var below = search("below");
        return below != null && (above == null || below.Item3.H < above.Item3.H) ? below : above;
    }

    // ---------- overrides (same format as door_window_tag_check) ----------
    static JArray C(Color c) { return c != null && c.IsValid ? new JArray(c.Red, c.Green, c.Blue) : null; }
    static Color C(JToken t) { return t is JArray a ? new Color((byte)(int)a[0], (byte)(int)a[1], (byte)(int)a[2]) : null; }
    static JObject Save(OverrideGraphicSettings o)
    {
        return new JObject
        {
            ["pLinePat"] = o.ProjectionLinePatternId.IntegerValue, ["pLineCol"] = C(o.ProjectionLineColor), ["pLineW"] = o.ProjectionLineWeight,
            ["cLinePat"] = o.CutLinePatternId.IntegerValue, ["cLineCol"] = C(o.CutLineColor), ["cLineW"] = o.CutLineWeight,
            ["sFgPat"] = o.SurfaceForegroundPatternId.IntegerValue, ["sFgCol"] = C(o.SurfaceForegroundPatternColor), ["sFgVis"] = o.IsSurfaceForegroundPatternVisible,
            ["sBgPat"] = o.SurfaceBackgroundPatternId.IntegerValue, ["sBgCol"] = C(o.SurfaceBackgroundPatternColor), ["sBgVis"] = o.IsSurfaceBackgroundPatternVisible,
            ["cFgPat"] = o.CutForegroundPatternId.IntegerValue, ["cFgCol"] = C(o.CutForegroundPatternColor), ["cFgVis"] = o.IsCutForegroundPatternVisible,
            ["cBgPat"] = o.CutBackgroundPatternId.IntegerValue, ["cBgCol"] = C(o.CutBackgroundPatternColor), ["cBgVis"] = o.IsCutBackgroundPatternVisible,
            ["transp"] = o.Transparency, ["halftone"] = o.Halftone, ["detail"] = o.DetailLevel.ToString()
        };
    }
    static OverrideGraphicSettings Load(JObject j)
    {
        var o = new OverrideGraphicSettings();
        Func<string, int> pat = k => (int?)j[k] ?? -1;
        if (pat("pLinePat") > 0) o.SetProjectionLinePatternId(new ElementId(pat("pLinePat")));
        if (C(j["pLineCol"]) is Color a) o.SetProjectionLineColor(a);
        if ((int)j["pLineW"] > 0) o.SetProjectionLineWeight((int)j["pLineW"]);
        if (pat("cLinePat") > 0) o.SetCutLinePatternId(new ElementId(pat("cLinePat")));
        if (C(j["cLineCol"]) is Color b) o.SetCutLineColor(b);
        if ((int)j["cLineW"] > 0) o.SetCutLineWeight((int)j["cLineW"]);
        if (pat("sFgPat") > 0) o.SetSurfaceForegroundPatternId(new ElementId(pat("sFgPat")));
        if (C(j["sFgCol"]) is Color c1) o.SetSurfaceForegroundPatternColor(c1);
        o.SetSurfaceForegroundPatternVisible((bool)j["sFgVis"]);
        if (pat("sBgPat") > 0) o.SetSurfaceBackgroundPatternId(new ElementId(pat("sBgPat")));
        if (C(j["sBgCol"]) is Color c2) o.SetSurfaceBackgroundPatternColor(c2);
        o.SetSurfaceBackgroundPatternVisible((bool)j["sBgVis"]);
        if (pat("cFgPat") > 0) o.SetCutForegroundPatternId(new ElementId(pat("cFgPat")));
        if (C(j["cFgCol"]) is Color c3) o.SetCutForegroundPatternColor(c3);
        o.SetCutForegroundPatternVisible((bool)j["cFgVis"]);
        if (pat("cBgPat") > 0) o.SetCutBackgroundPatternId(new ElementId(pat("cBgPat")));
        if (C(j["cBgCol"]) is Color c4) o.SetCutBackgroundPatternColor(c4);
        o.SetCutBackgroundPatternVisible((bool)j["cBgVis"]);
        o.SetSurfaceTransparency((int)j["transp"]);
        o.SetHalftone((bool)j["halftone"]);
        if (Enum.TryParse((string)j["detail"], out ViewDetailLevel d)) o.SetDetailLevel(d);
        return o;
    }

    // ---------- entry ----------
    public static object Run(UIApplication app, JObject args)
    {
        doc = app.ActiveUIDocument?.Document;
        if (doc == null) return new { Error = "No active document" };
        string mode = (args.Value<string>("mode") ?? "preview").ToLowerInvariant();
        string logPath = args.Value<string>("logPath");
        if ((mode == "apply" || mode == "undo") && string.IsNullOrWhiteSpace(logPath)) return new { Error = "logPath is required for " + mode };
        if (mode == "undo") return Undo(logPath);
        if (mode == "clearhighlight") return ClearHighlight(logPath);

        var views = new List<View>();
        var ids = args["viewIds"]?.Values<int>().ToList();
        if (ids != null && ids.Count > 0) views = ids.Select(i => doc.GetElement(new ElementId(i)) as View).Where(x => x != null).ToList();
        var sheetNos = args["sheetNumbers"]?.Values<string>().ToList();
        var sheetOf = new Dictionary<int, string>();
        foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
        {
            var sh = doc.GetElement(vp.SheetId) as ViewSheet; if (sh == null) continue;
            sheetOf[vp.ViewId.IntegerValue] = sh.SheetNumber + " - " + sh.Name;
            if (sheetNos != null && sheetNos.Contains(sh.SheetNumber))
            {
                var pv = doc.GetElement(vp.ViewId) as View;
                if (pv != null && (pv.ViewType == ViewType.Elevation || pv.ViewType == ViewType.Section) && !views.Any(x => x.Id == pv.Id)) views.Add(pv);
            }
        }
        if (views.Count == 0) return new { Error = "No views (give viewIds or sheetNumbers with Elevation/Section views)" };
        // elevations/sections are heavy: one view per call unless explicitly allowed
        if (views.Count > 1 && !(args.Value<bool?>("allowMultipleViews") ?? false))
            return new { Error = "One view per call. Views found: " + string.Join(", ", views.Select(x => x.Name + " (" + x.Id.IntegerValue + ")")) };
        budget = args.Value<double?>("maxSeconds") ?? 90;
        clock = System.Diagnostics.Stopwatch.StartNew();

        var exclude = new HashSet<int>((args["excludeIds"] as JArray)?.Select(t => (int)t) ?? new int[0]);
        double gapMm = args.Value<double?>("gapMm") ?? 1.0, maxGapMm = args.Value<double?>("maxGapMm") ?? 4.5;
        bool fixExisting = args.Value<bool?>("fixExisting") ?? false;
        maxSteps = args.Value<int?>("maxOffsetSteps") ?? 3;
        bool repositionAll = args.Value<bool?>("repositionAll") ?? false;
        bool addLeader = args.Value<bool?>("addLeader") ?? false;
        leaderMode = addLeader;
        double leaderInMm = args.Value<double?>("leaderInsetMm") ?? 1.5;
        var leaderFamilies = ((args["leaderFamilies"] as JArray)?.Select(t => ((string)t).ToUpperInvariant()) ?? new string[0]).ToList();
        // roll-up doors ONLY: a tag that is small next to the door (<= 1/3 width, <= 1/4 visible height) sits inside it, centred, no leader
        rollupFams = ((args["rollupFamilies"] as JArray)?.Select(t => ((string)t).ToUpperInvariant()) ?? new[] { "ROLL UP" }).ToList();
        rollupInside = args.Value<bool?>("rollupInside") ?? true;
        var reportIds = new HashSet<int>((args["reportTagIds"] as JArray)?.Select(t => (int)t) ?? new int[0]);
        debugIds = new HashSet<int>((args["debugIds"] as JArray)?.Select(t => (int)t) ?? new int[0]); debugRows = new List<string>();
        bool highlight = args.Value<bool?>("highlight") ?? true;
        int maxItems = args.Value<int?>("maxItems") ?? 300;

        var v3 = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && !x.IsSectionBoxActive && x.Name == "{3D}")
              ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && !x.IsSectionBoxActive && !x.IsPerspective);
        if (v3 == null) return new { Error = "No 3D view without section box for ray casting" };
        occl = new ReferenceIntersector(new ElementMulticategoryFilter(OccluderCats.ToList()), FindReferenceTarget.Element, v3) { FindReferencesInRevitLinks = false };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // tag types: given, else the one used most in these views
        var tagCats = new[] { (int)BuiltInCategory.OST_DoorTags, (int)BuiltInCategory.OST_WindowTags, (int)BuiltInCategory.OST_MultiCategoryTags };
        Func<BuiltInCategory, BuiltInCategory, string, ElementId> pickType = (hostCat, tagCat, argName) =>
        {
            int given = args.Value<int?>(argName) ?? 0;
            if (given > 0) return new ElementId(given);
            var used = views.SelectMany(x => new FilteredElementCollector(doc, x.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                .Where(t => t.Category != null && t.Category.Id.IntegerValue == (int)tagCat).GroupBy(t => t.GetTypeId().IntegerValue)
                .OrderByDescending(g => g.Count()).FirstOrDefault();
            if (used != null) return new ElementId(used.Key);
            return new FilteredElementCollector(doc).OfCategory(tagCat).WhereElementIsElementType().FirstElementId();
        };
        var doorTagType = pickType(BuiltInCategory.OST_Doors, BuiltInCategory.OST_DoorTags, "doorTagTypeId");
        var winTagType = pickType(BuiltInCategory.OST_Windows, BuiltInCategory.OST_WindowTags, "windowTagTypeId");
        Func<ElementId, string> typeName = id => { var t = doc.GetElement(id) as ElementType; return t == null ? "(none)" : t.FamilyName + " : " + t.Name; };

        JObject log = null;
        if (mode == "apply")
        {
            log = File.Exists(logPath) ? JObject.Parse(File.ReadAllText(logPath)) : new JObject();
            if (log["Document"] != null && (string)log["Document"] != doc.Title) return new { Error = "logPath belongs to " + log["Document"] };
            log["Document"] = doc.Title;
            if (log["created"] == null) log["created"] = new JArray();
            if (log["moved"] == null) log["moved"] = new JArray();
            if (log["overrides"] == null) log["overrides"] = new JObject();
            if (log["leaders"] == null) log["leaders"] = new JArray();
        }
        var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

        var results = new List<object>();
        try
        {
        foreach (var view in views)
        {
            v = view;
            if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, v.Id) == CheckoutStatus.OwnedByOtherUser)
            { results.Add(new { View = v.Name, Skipped = "view owned by " + WorksharingUtils.GetWorksharingTooltipInfo(doc, v.Id).Owner }); continue; }
            O = v.Origin; Rg = v.RightDirection; Up = v.UpDirection; Vd = v.ViewDirection; P1 = v.Scale / MM;
            LoadFilters();
            rayCache = new Dictionary<string, List<Tuple<double, int>>>(); rays = 0; stages = new List<string>(); obSeenCalls = 0; obSeenSec = 0; obCost = new Dictionary<string, double>(); complexSkipped = new HashSet<string>(); filterHiddenCount = 0;
            // occluders = only the walls / slabs / panels / doors / windows this view shows (crop + far clip), not the whole model
            var occIds = new FilteredElementCollector(doc, v.Id).WherePasses(new ElementMulticategoryFilter(OccluderCats.ToList()))
                .WhereElementIsNotElementType().ToElementIds();
            if (occIds.Count == 0) occIds.Add(ElementId.InvalidElementId);
            occl = new ReferenceIntersector(occIds, FindReferenceTarget.Element, v3) { FindReferencesInRevitLinks = false };
            Stage("occluders " + occIds.Count);
            double t0 = sw.Elapsed.TotalSeconds;
            far = 0;
            var pa = v.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR); var po = v.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
            if (pa != null && pa.AsInteger() == 1 && po != null) far = po.AsDouble();
            crop = v.CropBoxActive ? Proj(v.CropBox, "crop") : null;
            double gap = gapMm * P1, maxGap = maxGapMm * P1, tol = 0.5 * P1;

            var row = new Dictionary<string, object>();
            var created = new List<object>(); var existing = new List<object>(); var red = new List<RedItem>(); var notVisible = new List<string>(); var errors = new List<string>();
            int moved = 0;

            using (var tx = new Transaction(doc, "Door/window tags " + v.Name))
            {
                tx.Start();
                var placedTags = new List<Tuple<IndependentTag, Opening, R>>();
                var tagReport = new List<object>();
                // ---------- openings really seen in the view ----------
                var openings = new List<Opening>();
                foreach (var bic in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
                    foreach (FamilyInstance fi in new FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
                    {
                        CheckTime();
                        if (fi.SuperComponent != null || exclude.Contains(fi.Id.IntegerValue)) continue;
                        if (FilterHidden(fi)) continue;
                        string nm = (fi.Symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString() ?? "?") + " #" + fi.Id.IntegerValue;
                        if (Math.Abs(fi.FacingOrientation.DotProduct(Vd)) < 0.5) continue;      // seen (nearly) edge-on; angled walls (45°) still count
                        var rc = Extents(fi, "opening " + nm);
                        if (rc == null) continue;
                        if (crop != null && (rc.X1 < crop.X0 || rc.X0 > crop.X1 || rc.Y1 < crop.Y0 || rc.Y0 > crop.Y1)) continue;
                        // seen = not hidden behind a host wall / slab / roof / curtain panel at some point of its face
                        var pd = ProjDepth(fi.get_BoundingBox(null), null, nm);
                        bool seen = pd == null || pd.Item2 <= 0;
                        var excl = new HashSet<int> { fi.Id.IntegerValue };
                        if (fi.Host != null) excl.Add(fi.Host.Id.IntegerValue);
                        foreach (var fx in new[] { 0.5, 0.25, 0.75 })
                            foreach (var fy in new[] { 0.5, 0.3, 0.7 })
                                if (!seen && !Occluded(rc.X0 + (rc.X1 - rc.X0) * fx, rc.Y0 + (rc.Y1 - rc.Y0) * fy, pd.Item2, excl)) seen = true;
                        if (!seen) { notVisible.Add(nm); continue; }
                        openings.Add(new Opening { Fi = fi, Rc = rc, IsDoor = bic == BuiltInCategory.OST_Doors, Name = nm });
                    }
                var byId = openings.ToDictionary(o => o.Fi.Id.IntegerValue);

                Stage("openings " + openings.Count);
                // ---------- tags already in the view ----------
                var allTags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
                var headRect = new Dictionary<int, R>();
                var leaderTags = allTags.Where(t => { try { return t.HasLeader; } catch { return false; } }).ToList();
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    foreach (var t in leaderTags) { try { t.HasLeader = false; } catch { } }
                    doc.Regenerate();
                    foreach (var t in allTags) { var r = Proj(t.get_BoundingBox(v), "tag " + SafeText(t)); if (r != null) headRect[t.Id.IntegerValue] = r; }
                    st.RollBack();
                }
                var owTags = new List<IndependentTag>();   // door/window tags that tag a visible opening
                foreach (var t in allTags)
                {
                    if (t.Category == null || !tagCats.Contains(t.Category.Id.IntegerValue)) continue;
                    List<ElementId> hosts;
                    try { hosts = t.GetTaggedLocalElementIds().ToList(); } catch { continue; }
                    foreach (var hid in hosts)
                        if (byId.TryGetValue(hid.IntegerValue, out var op)) { op.Tags.Add(t); owTags.Add(t); }
                }

                Stage("tag heads");
                // ---------- model obstacles: stairs, railings, lifts/equipment, columns, beams... (host + visible links), cut slabs ----------
                modelObs = new List<Ob>();
                double cropArea = crop == null ? double.MaxValue : (crop.X1 - crop.X0) * (crop.Y1 - crop.Y0);
                Action<Element, Transform, string, int, bool> addOb = (e, tf, prefix, hostId, slab) =>
                {
                    if (e is FamilyInstance fi2 && fi2.SuperComponent != null) return;
                    if (CatHidden(e)) { filterHiddenCount++; return; }                     // category off / hidden by a view filter
                    var pd = ProjDepth(e.get_BoundingBox(null), tf, ""); if (pd == null) return;
                    var rc = pd.Item1; double d0 = pd.Item2, d1 = pd.Item3;
                    if (d1 < 0 || (far > 0 && d0 > far)) return;                       // in front of the plane / beyond far clip
                    if (crop != null && !rc.Hits(crop)) return;
                    bool cut = d0 < 0;
                    if (slab && (!cut || rc.Y1 - rc.Y0 > 6000 / MM)) return;             // slabs only matter where the section cuts them
                    if ((rc.X1 - rc.X0) * (rc.Y1 - rc.Y0) > 0.3 * cropArea) return;      // site-size objects
                    string tn = e is FamilyInstance fx ? fx.Symbol.FamilyName + " : " + fx.Name : e.Name;
                    rc.What = (cut ? "cut " : "") + prefix + e.Category?.Name + " '" + tn + "' #" + e.Id.IntegerValue;
                    modelObs.Add(new Ob { Rc = rc, Near = Math.Max(0, d0), Cut = cut, HostId = hostId > 0 ? e.Id.IntegerValue : -1, Name = rc.What, E = e, Tf = tf });
                };
                foreach (var e in new FilteredElementCollector(doc, v.Id).WherePasses(new ElementMulticategoryFilter(HardCats.ToList())).WhereElementIsNotElementType())
                    addOb(e, null, "", 1, false);
                foreach (var e in new FilteredElementCollector(doc, v.Id).WherePasses(new ElementMulticategoryFilter(SlabCats.ToList())).WhereElementIsNotElementType())
                    addOb(e, null, "", 1, true);
                var linkNames = new List<string>();
                if (!v.GetCategoryHidden(new ElementId(BuiltInCategory.OST_RvtLinks)))
                    foreach (RevitLinkInstance li in new FilteredElementCollector(doc, v.Id).OfClass(typeof(RevitLinkInstance)))
                    {
                        var ld = li.GetLinkDocument(); if (ld == null || li.IsHidden(v)) continue;
                        var tf = li.GetTotalTransform(); string pre = "link " + ld.Title + ": ";
                        int before = modelObs.Count;
                        foreach (var e in new FilteredElementCollector(ld).WherePasses(new ElementMulticategoryFilter(HardCats.ToList())).WhereElementIsNotElementType())
                        {
                            if (e.Category != null && CatHidden(e)) continue;
                            addOb(e, tf, pre, 0, false);
                            CheckTime();
                        }
                        foreach (var e in new FilteredElementCollector(ld).WherePasses(new ElementMulticategoryFilter(SlabCats.ToList())).WhereElementIsNotElementType())
                            addOb(e, tf, pre, 0, true);
                        linkNames.Add(ld.Title + " " + (modelObs.Count - before));
                    }

                Stage("model obstacles " + modelObs.Count);
                // ---------- annotation obstacles ----------
                var obst = new List<R>();
                foreach (var op in openings) obst.Add(op.Rc);
                foreach (var d in new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => d.OwnerViewId == v.Id && d.GetType() == typeof(Dimension)))
                {
                    var ln = d.Curve as Line; if (ln == null) continue;
                    bool vert = Math.Abs(ln.Direction.DotProduct(Up)) > 0.99, hor = Math.Abs(ln.Direction.DotProduct(Rg)) > 0.99;
                    if (!vert && !hor) { var pr = Proj(d.get_BoundingBox(v), "dim " + d.Id.IntegerValue); if (pr != null) obst.Add(pr); continue; }
                    double h = TextH(d), line = vert ? VX(ln.Origin) : VY(ln.Origin);
                    var bnd = new List<double>();
                    var texts = new List<R>();
                    if (d.NumberOfSegments > 1)
                        foreach (DimensionSegment sg in d.Segments)
                        {
                            double m = vert ? VY(sg.Origin) : VX(sg.Origin), val = sg.Value ?? 0;
                            bnd.Add(m - val / 2); bnd.Add(m + val / 2);
                            texts.Add(TextBox(vert, VX(sg.TextPosition), VY(sg.TextPosition), sg.ValueString, h));
                        }
                    else
                    {
                        double m = vert ? VY(d.Origin) : VX(d.Origin), val = d.Value ?? 0;
                        bnd.Add(m - val / 2); bnd.Add(m + val / 2);
                        texts.Add(TextBox(vert, VX(d.TextPosition), VY(d.TextPosition), d.ValueString, h));
                    }
                    double lo = bnd.Min(), hi = bnd.Max();
                    string w = "dim " + d.Id.IntegerValue;
                    obst.Add(vert ? new R(line - 0.4 * P1, line + 0.4 * P1, lo, hi, w) : new R(lo, hi, line - 0.4 * P1, line + 0.4 * P1, w));
                    foreach (var tb in texts) { tb.What = w + " text"; obst.Add(tb); }
                }
                foreach (var t in allTags.Where(t => !owTags.Contains(t)))
                    if (headRect.TryGetValue(t.Id.IntegerValue, out var r)) { r.What = "tag " + t.Id.IntegerValue + " '" + SafeText(t) + "'"; obst.Add(r); }
                foreach (var el in new FilteredElementCollector(doc, v.Id).WherePasses(new LogicalOrFilter(new List<ElementFilter> {
                         new ElementClassFilter(typeof(SpatialElementTag)), new ElementClassFilter(typeof(TextNote)), new ElementClassFilter(typeof(SpotDimension)) })))
                { var r = Proj(el.get_BoundingBox(v), el.GetType().Name + " " + el.Id.IntegerValue); if (r != null) obst.Add(r); }
                foreach (var el in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_GenericAnnotation).WhereElementIsNotElementType())
                { var r = Proj(el.get_BoundingBox(v), "annotation " + el.Id.IntegerValue); if (r != null) obst.Add(r); }
                foreach (var el in new FilteredElementCollector(doc, v.Id).OfClass(typeof(FilledRegion)))
                { var r = Proj(el.get_BoundingBox(v), "filled region"); if (r != null) { r.Soft = true; obst.Add(r); } }
                double big = 1e5;
                foreach (Level lv in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Level)))
                {
                    double y = VY(new XYZ(O.X, O.Y, lv.ProjectElevation));
                    obst.Add(new R(-big, big, y - 0.3 * P1, y + 0.3 * P1, "level line " + lv.Name) { Soft = true });
                }
                foreach (Grid g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)))
                {
                    var gl = g.Curve as Line; if (gl == null || Math.Abs(gl.Direction.DotProduct(Rg)) > 0.01) continue;
                    double x = VX(gl.Origin);
                    obst.Add(new R(x - 0.3 * P1, x + 0.3 * P1, -big, big, "grid line " + g.Name) { Soft = true });
                }

                Stage("annotation obstacles " + obst.Count);
                // ---------- existing tags: check (and optionally move) ----------
                var existingRect = new Dictionary<int, R>();
                foreach (var op in openings)
                {
                    foreach (var t in op.Tags)
                    {
                        if (!headRect.TryGetValue(t.Id.IntegerValue, out var tr)) continue;
                        bool leader = false; try { leader = t.HasLeader; } catch { }
                        var H = op.Rc; var issues = new List<string>();
                        bool above = tr.Y0 >= H.Y1 - tol, below = tr.Y1 <= H.Y0 + tol, aligned = tr.Cx >= H.X0 - tol && tr.Cx <= H.X1 + tol;
                        bool onHost = Math.Min(tr.X1, H.X1) - Math.Max(tr.X0, H.X0) > tol && Math.Min(tr.Y1, H.Y1) - Math.Max(tr.Y0, H.Y0) > tol;
                        // roll-up door with a small tag sitting inside it: allowed (T5)
                        bool rollIn = onHost && InsideOk(op, tr.X1 - tr.X0, tr.Y1 - tr.Y0) && IsInside(op, tr);
                        if (rollIn) onHost = false;
                        double gp = above ? tr.Y0 - H.Y1 : below ? H.Y0 - tr.Y1 : 0;
                        if (onHost) issues.Add("on_host");
                        if (!leader && !rollIn)
                        {
                            if (op.IsDoor && !onHost && (!above || !aligned)) issues.Add("door_tag_not_above");
                            if (!op.IsDoor && !onHost && (!(above || below) || !aligned)) issues.Add("window_tag_beside");
                            if ((above || below) && aligned && gp > maxGap) issues.Add("far " + Math.Round(gp / P1, 1) + "mm");
                        }
                        if (op.Tags.Count > 1) issues.Add("duplicate");
                        var others = obst.Where(o => o != H).ToList();
                        dbg = debugIds.Contains(op.Fi.Id.IntegerValue);
                        var sc = Evaluate(tr, others.Concat(existingRect.Where(kv => kv.Key != t.Id.IntegerValue).Select(kv => kv.Value)).ToList(), t, op.Fi);
                        dbg = false;
                        if (rollIn) sc.Hard.RemoveAll(n => n.Contains("#" + op.Fi.Id.IntegerValue));
                        bool covers = sc.H > 0;
                        if (debugIds.Contains(op.Fi.Id.IntegerValue))
                            debugRows.Add("EXISTING tag " + t.Id.IntegerValue + " " + Mm(tr) + " host " + Mm(op.Rc) + " -> " + sc.Text);
                        if (reportIds.Contains(t.Id.IntegerValue))
                        {
                            string ld = "none";
                            try
                            {
                                if (t.HasLeader)
                                {
                                    var rf0 = t.GetTaggedReferences().FirstOrDefault();
                                    var le = rf0 != null && t.LeaderEndCondition == LeaderEndCondition.Free ? t.GetLeaderEnd(rf0) : null;
                                    ld = t.LeaderEndCondition + (le != null ? " end dx " + Math.Round((VX(le) - H.Cx) / P1, 1) + " dy-from-top " + Math.Round((VY(le) - H.Y1) / P1, 1) + " dy-from-bottom " + Math.Round((VY(le) - H.Y0) / P1, 1) : "");
                                }
                            }
                            catch (Exception lex) { ld = "? " + lex.Message; }
                            var tool = BestSpot(op, tr.X1 - tr.X0, tr.Y1 - tr.Y0, gap, obst.Concat(existingRect.Values).ToList(), t);
                            tagReport.Add(new
                            {
                                Tag = t.Id.IntegerValue, Text = SafeText(t), Opening = op.Name, Family = op.Fi.Symbol.FamilyName,
                                Side = onHost ? "ON element" : above ? "above" : below ? "below" : "beside",
                                GapPaperMm = Math.Round((above ? tr.Y0 - H.Y1 : below ? H.Y0 - tr.Y1 : 0) / P1, 1),
                                CentreOffsetPaperMm = Math.Round((tr.Cx - H.Cx) / P1, 1),
                                OpeningPaperMm = Math.Round((H.X1 - H.X0) / P1, 1) + " x " + Math.Round((H.Y1 - H.Y0) / P1, 1),
                                Leader = ld, NowCovers = sc.Text,
                                ToolWouldPick = tool == null ? null : tool.Item2 + " -> " + (tool.Item3.Text.Length > 0 ? tool.Item3.Text : "clean")
                            });
                        }
                        string movedTo = null;
                        bool wrong = issues.Any(i => i == "on_host" || i.StartsWith("door_tag") || i.StartsWith("window_tag")) || covers || issues.Any(i => i.StartsWith("far")) || repositionAll;
                        if (wrong && (!leader || repositionAll) && issues.All(i => i != "duplicate"))
                        {
                            double w = tr.X1 - tr.X0, h = tr.Y1 - tr.Y0;
                            var spot = BestSpot(op, w, h, gap, obst.Concat(existingRect.Values).ToList(), t);
                            // a broken position rule (on the element / door tag not above / window tag beside / too far) is always fixed,
                            // even when the closest valid spot covers something (that tag is then coloured red)
                            bool ruleBroken = issues.Any(i => i == "on_host" || i.StartsWith("door_tag") || i.StartsWith("window_tag") || i.StartsWith("far"));
                            bool differs = spot != null && (Math.Abs(spot.Item1.Cx - tr.Cx) > 0.2 * P1 || Math.Abs(spot.Item1.Cy - tr.Cy) > 0.2 * P1);
                            if (spot != null && spot.Item3.H < 999 && differs && (ruleBroken || (repositionAll && spot.Item3.H <= sc.H) || (spot.Item3.H == 0 && (spot.Item3.H < sc.H || spot.Item3.Soft.Count < sc.Soft.Count))))
                            {
                                if (fixExisting)
                                {
                                    var oldHead = t.TagHeadPosition;
                                    t.TagHeadPosition = oldHead + Rg * (spot.Item1.Cx - tr.Cx) + Up * (spot.Item1.Cy - tr.Cy);
                                    if (log != null) ((JArray)log["moved"]).Add(new JObject { ["id"] = t.Id.IntegerValue, ["x"] = oldHead.X, ["y"] = oldHead.Y, ["z"] = oldHead.Z });
                                    tr = spot.Item1.Move(0, 0); tr.What = "tag " + SafeText(t); sc = spot.Item3; covers = sc.H > 0; moved++;
                                    movedTo = "MOVED " + spot.Item2;
                                }
                                else movedTo = "would move: " + spot.Item2 + (spot.Item3.Soft.Count > 0 ? " (soft: " + string.Join(", ", spot.Item3.Soft) + ")" : "");
                            }
                            else if (wrong && !(repositionAll && !differs)) movedTo = "no clean spot" + (spot != null ? " (best: " + spot.Item2 + " -> " + spot.Item3.Text + ")" : "");
                        }
                        tr.What = "tag " + t.Id.IntegerValue + " '" + SafeText(t) + "'";
                        existingRect[t.Id.IntegerValue] = tr;
                        placedTags.Add(Tuple.Create(t, op, tr));
                        if (issues.Count > 0 || covers || movedTo != null)
                            existing.Add(new { Tag = t.Id.IntegerValue, Text = SafeText(t), Opening = op.Name, Leader = leader, Issues = string.Join(", ", issues), Covers = sc.Text, Action = movedTo });
                        bool stillOnHost = onHost && (movedTo == null || !movedTo.StartsWith("MOVED"));
                        if (covers || stillOnHost)
                            red.Add(new RedItem { Tag = t.Id.IntegerValue, Text = SafeText(t), Opening = op.Name, New = false,
                                Covers = (stillOnHost ? "OWN " + op.Name + (sc.Text.Length > 0 ? ", " : "") : "") + sc.Text });
                    }
                }
                obst.AddRange(existingRect.Values);

                Stage("existing tags");
                // ---------- new tags ----------
                var untagged = openings.Where(o => o.Tags.Count == 0).OrderBy(o => o.Rc.Y0).ThenBy(o => o.Rc.X0).ToList();
                var made = new List<Tuple<Opening, IndependentTag>>();
                foreach (var op in untagged)
                {
                    try
                    {
                        var tt = op.IsDoor ? doorTagType : winTagType;
                        if (tt == null || tt == ElementId.InvalidElementId) { errors.Add(op.Name + ": no tag type"); continue; }
                        var t = IndependentTag.Create(doc, tt, v.Id, new Reference(op.Fi), false, TagOrientation.Horizontal, W(op.Rc.Cx, op.Rc.Cy));
                        made.Add(Tuple.Create(op, t));
                    }
                    catch (Exception ex) { errors.Add(op.Name + ": " + ex.Message); }
                }
                doc.Regenerate();
                foreach (var m in made)
                {
                    var op = m.Item1; var t = m.Item2;
                    var tr = Proj(t.get_BoundingBox(v), "new tag");
                    if (tr == null) { errors.Add(op.Name + ": tag has no box"); continue; }
                    var head = t.TagHeadPosition;
                    double dx = tr.Cx - VX(head), dy = tr.Cy - VY(head);
                    var spot = BestSpot(op, tr.X1 - tr.X0, tr.Y1 - tr.Y0, gap, obst, t);
                    t.TagHeadPosition = W(spot.Item1.Cx - dx, spot.Item1.Cy - dy);
                    var placed = spot.Item1; placed.What = "new tag " + t.Id.IntegerValue + " '" + SafeText(t) + "'";
                    obst.Add(placed);
                    placedTags.Add(Tuple.Create(t, op, placed));
                    if (log != null) ((JArray)log["created"]).Add(t.Id.IntegerValue);
                    created.Add(new { Tag = t.Id.IntegerValue, Text = SafeText(t), Opening = op.Name, Spot = spot.Item2, Under = spot.Item3.Text });
                    if (spot.Item3.H > 0) red.Add(new RedItem { Tag = t.Id.IntegerValue, Text = SafeText(t), Opening = op.Name, New = true, Covers = spot.Item3.Text });
                }

                // ---------- leaders: short vertical leader from the tag into the edge of its element ----------
                int leadersSet = 0;
                if (addLeader)
                {
                    foreach (var pt in placedTags)
                    {
                        var t = pt.Item1; var H = pt.Item2.Rc; var tr = pt.Item3;
                        if (leaderFamilies.Count > 0 && !leaderFamilies.Any(f => (pt.Item2.Fi.Symbol.FamilyName ?? "").ToUpperInvariant().Contains(f))) continue;
                        if (IsRollup(pt.Item2) && IsInside(pt.Item2, tr)) continue;   // tag inside a roll-up door: no leader
                        try
                        {
                            var rf = t.GetTaggedReferences().FirstOrDefault();
                            if (rf == null) continue;
                            bool tagAbove = tr.Cy > H.Cy;
                            bool beside = tr.Cx > H.X1 || tr.Cx < H.X0;
                            double inset = Math.Min(leaderInMm * P1, (H.Y1 - H.Y0) / 3);
                            XYZ end, elbow;
                            if (beside)
                            {
                                // tag next to the door: horizontal leader into the door, at the tag's height (kept below the drawn top)
                                bool right = tr.Cx > H.X1;
                                double inX = Math.Min(inset, (H.X1 - H.X0) / 3);
                                double top = VisibleEdge(pt.Item2, (H.X0 + H.X1) / 2, true);
                                double y = Math.Max(H.Y0 + inset, Math.Min(top - inset, tr.Cy));
                                end = W(right ? H.X1 - inX : H.X0 + inX, y);
                                elbow = W(right ? tr.X0 : tr.X1, y);
                            }
                            else
                            {
                                double x = Math.Max(H.X0 + inset, Math.Min(H.X1 - inset, tr.Cx));
                                double edge = VisibleEdge(pt.Item2, x, tagAbove);         // drawn edge, not the hidden box behind the wall
                                end = W(x, tagAbove ? edge - inset : edge + inset);
                                elbow = W(x, tagAbove ? tr.Y0 : tr.Y1);                  // straight vertical leader
                            }
                            bool had = t.HasLeader;
                            if (log != null) ((JArray)log["leaders"]).Add(new JObject { ["id"] = t.Id.IntegerValue, ["had"] = had });
                            if (!had) t.HasLeader = true;
                            t.LeaderEndCondition = LeaderEndCondition.Free;
                            t.SetLeaderEnd(rf, end);
                            t.SetLeaderElbow(rf, elbow);
                            leadersSet++;
                        }
                        catch (Exception ex) { errors.Add("leader " + t.Id.IntegerValue + ": " + ex.Message); }
                    }
                }

                // ---------- red highlight ----------
                int coloured = 0, uncoloured = 0;
                if (mode == "apply")
                {
                    // tags coloured by an earlier run that are fine now: put their original overrides back
                    var ovPrev = (JObject)log["overrides"];
                    var redIds = new HashSet<int>(red.Select(rr => rr.Tag));
                    foreach (var p in ovPrev.Properties().ToList())
                    {
                        var parts = p.Name.Split(':');
                        if (parts[0] != v.Id.IntegerValue.ToString()) continue;
                        int tid = int.Parse(parts[1]);
                        if (redIds.Contains(tid) || doc.GetElement(new ElementId(tid)) == null) continue;
                        v.SetElementOverrides(new ElementId(tid), Load((JObject)p.Value));
                        ovPrev.Remove(p.Name); uncoloured++;
                    }
                }
                if (mode == "apply" && highlight && red.Count > 0)
                {
                    var redC = new Color(255, 0, 0);
                    var ov = (JObject)log["overrides"];
                    foreach (var rr in red)
                    {
                        var id = new ElementId(rr.Tag);
                        string key = v.Id.IntegerValue + ":" + id.IntegerValue;
                        if (ov[key] == null) ov[key] = Save(v.GetElementOverrides(id));
                        var o = new OverrideGraphicSettings();
                        o.SetProjectionLineColor(redC); o.SetProjectionLineWeight(6);
                        v.SetElementOverrides(id, o); coloured++;
                    }
                }

                if (mode == "apply")
                {
                    File.WriteAllText(logPath, log.ToString(Formatting.Indented));   // before commit: never lose originals
                    tx.Commit();
                }
                else tx.RollBack();

                row["View"] = v.Name; row["ViewId"] = v.Id.IntegerValue; row["ViewType"] = v.ViewType.ToString();
                row["Sheet"] = sheetOf.TryGetValue(v.Id.IntegerValue, out var shn) ? shn : null;
                row["Scale"] = v.Scale;
                row["OpeningsVisible"] = openings.Count; row["AlreadyTagged"] = openings.Count(o => o.Tags.Count > 0);
                row["NewTags"] = created.Count; row["ExistingWithRemarks"] = existing.Count; row["ExistingMoved"] = moved;
                row["Red"] = red.Count; row["Coloured"] = coloured; row["RedRemoved"] = uncoloured; row["LeadersSet"] = leadersSet;
                row["Created"] = created.Take(maxItems).ToList();
                row["Existing"] = existing.Take(maxItems).ToList();
                row["RedList"] = red.Take(maxItems).ToList();
                row["NotVisibleSkipped"] = notVisible.Take(maxItems).ToList();
                row["Errors"] = errors;
                row["ModelObstacles"] = modelObs.Count; row["LinkObstacles"] = linkNames; row["Rays"] = rays;
                row["Seconds"] = Math.Round(sw.Elapsed.TotalSeconds - t0, 1);
                row["Stages"] = stages;
                if (tagReport.Count > 0) row["TagReport"] = tagReport;
                row["TooComplexNotChecked"] = complexSkipped.ToList();
                row["HiddenByFiltersOrCategory"] = filterHiddenCount; row["HiddenFilters"] = hiddenFilters.Count;
                var dxy = args["debugXY"] as JArray;
                if (dxy != null && dxy.Count == 2)
                {
                    double px = (double)dxy[0] / MM, py = (double)dxy[1] / MM;
                    var pt = W(px, py);
                    var hitsAll = occl.Find(pt + Vd * (5 / MM), -Vd).OrderBy(h => h.Proximity).Take(10)
                        .Select(h => { var e = doc.GetElement(h.GetReference().ElementId); return Math.Round((h.Proximity - 5 / MM) * MM) + "mm " + e?.Category?.Name + " '" + e?.Name + "' #" + e?.Id.IntegerValue + (e != null && (e.IsHidden(v) || CatHidden(e)) ? " (hidden)" : ""); }).ToList();
                    var under = modelObs.Where(o => o.Rc.X0 <= px && px <= o.Rc.X1 && o.Rc.Y0 <= py && py <= o.Rc.Y1)
                        .Select(o => { var hd = HitDepth(o, px, py); return o.Name + " hit " + (hd == null ? "miss" : Math.Round(hd.Value * MM) + "mm occluded " + Occluded(px, py, hd.Value, o.HostId > 0 ? new HashSet<int> { o.HostId } : null)); }).ToList();
                    row["DebugPoint"] = new { Occluders = hitsAll, Obstacles = under, FarMm = Math.Round(far * MM) };
                }
                if (debugRows.Count > 0)
                {
                    row["Debug"] = debugRows.Take(60).ToList();
                    row["DebugObstacles"] = modelObs.Where(o => o.Name.Contains("Stair") || o.Name.Contains("Rail")).Select(o => o.Name + " " + Mm(o.Rc) + " near " + Math.Round(o.Near * MM) + " solids " + SolidsOf(o).Count).Take(40).ToList();
                }
                results.Add(row);
            }
        }
        }
        catch (TimeoutException tex)
        {
            // the open transaction was disposed without commit = rolled back; the log file was not written
            return new { Error = tex.Message, View = v?.Name, Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1) };
        }
        return new
        {
            Document = doc.Title, Mode = mode, RayView = v3.Name,
            DoorTagType = typeName(doorTagType), WindowTagType = typeName(winTagType),
            LogPath = mode == "apply" ? logPath : null,
            Views = results
        };
    }

    static string SafeText(IndependentTag t) { try { return t.TagText; } catch { return "?"; } }

    // put the original overrides of the red tags back, keep tags and positions
    static object ClearHighlight(string logPath)
    {
        if (!File.Exists(logPath)) return new { Error = "logPath not found" };
        var log = JObject.Parse(File.ReadAllText(logPath));
        if ((string)log["Document"] != doc.Title) return new { Error = "Log is for " + log["Document"] };
        var ov = (JObject)log["overrides"] ?? new JObject();
        int restored = 0;
        using (var tx = new Transaction(doc, "Clear door/window tag highlight"))
        {
            tx.Start();
            foreach (var p in ov.Properties().ToList())
            {
                var parts = p.Name.Split(':');
                var vw = doc.GetElement(new ElementId(int.Parse(parts[0]))) as View; var id = new ElementId(int.Parse(parts[1]));
                if (vw != null && doc.GetElement(id) != null) { vw.SetElementOverrides(id, Load((JObject)p.Value)); restored++; }
                ov.Remove(p.Name);
            }
            File.WriteAllText(logPath, log.ToString(Formatting.Indented));
            tx.Commit();
        }
        return new { OverridesRestored = restored };
    }

    static object Undo(string logPath)
    {
        if (!File.Exists(logPath)) return new { Error = "logPath not found" };
        var log = JObject.Parse(File.ReadAllText(logPath));
        if ((string)log["Document"] != doc.Title) return new { Error = "Log is for " + log["Document"] };
        int restored = 0, back = 0, deleted = 0;
        using (var tx = new Transaction(doc, "Undo door/window tags"))
        {
            tx.Start();
            foreach (var p in ((JObject)log["overrides"] ?? new JObject()).Properties())
            {
                var parts = p.Name.Split(':');
                var vw = doc.GetElement(new ElementId(int.Parse(parts[0]))) as View; var id = new ElementId(int.Parse(parts[1]));
                if (vw == null || doc.GetElement(id) == null) continue;
                vw.SetElementOverrides(id, Load((JObject)p.Value)); restored++;
            }
            // leaders first (moving a tag head with a free leader keeps the leader end)
            var firstState = new Dictionary<int, bool>();
            foreach (JObject l in (JArray)log["leaders"] ?? new JArray()) { int id = (int)l["id"]; if (!firstState.ContainsKey(id)) firstState[id] = (bool)l["had"]; }
            foreach (var kv in firstState)
            {
                var t = doc.GetElement(new ElementId(kv.Key)) as IndependentTag;
                if (t != null && !kv.Value) { try { t.HasLeader = false; } catch { } }
            }
            foreach (JObject m in ((JArray)log["moved"] ?? new JArray()).Reverse())
            {
                var t = doc.GetElement(new ElementId((int)m["id"])) as IndependentTag; if (t == null) continue;
                t.TagHeadPosition = new XYZ((double)m["x"], (double)m["y"], (double)m["z"]); back++;
            }
            var del = ((JArray)log["created"] ?? new JArray()).Select(x => new ElementId((int)x)).Where(id => doc.GetElement(id) != null).ToList();
            if (del.Count > 0) doc.Delete(del);
            deleted = del.Count;
            tx.Commit();
        }
        File.Move(logPath, logPath + ".undone-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        return new { Deleted = deleted, MovedBack = back, OverridesRestored = restored };
    }
}
