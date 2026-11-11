/* mcp-tool
{
  "description": "Create the views of a sheet register: plans, duplicates, dependents, elevations, sections, callouts, drafting, legend / schedule copies, new schedules / drawing lists, with name, template, scale, scope box, crop. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "items": { "type": "array", "items": { "type": "object" }, "description": "{kind, name, level, sourceViewId, template, scale, scopeBox, marker|line|box}" },
      "itemsPath": { "type": "string", "description": "read items from this JSON file instead" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string", "description": "created ids; required for apply and undo" },
      "verbose": { "type": "boolean", "description": "one row per item (default: ids + problems only)" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 300
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// Rules VC1-VC12 of drafting-view-create.md. One transaction; each item in its own sub-transaction, so one bad item
// never stops the others. preview does the whole job and rolls back (real errors, no ids).
// item, all lengths in model mm:
//    kind        floorPlan | ceilingPlan     level (name or id)
//                duplicate | dependent       sourceViewId; duplicate copies no detailing unless withDetailing:true (VC6)
//                legendCopy | scheduleCopy   sourceViewId (a legend / a schedule); legendCopy copies the content
//                elevation                   sourceViewId = the plan the marker sits on, marker {x, y, direction, depth?,
//                                            width?, bottom?, top?}. direction = where the view LOOKS: east | north |
//                                            west | south, degrees counter-clockwise from +X, or {x, y}. depth = far
//                                            clip; width = crop width centred on the marker; bottom / top = crop
//                                            heights, mm above the internal origin. Interior elevation: one item per
//                                            wall, viewType = the project's interior elevation type.
//                section                     line {start{x,y}, end{x,y}, depth?, bottom?, top?}: the view looks to the
//                                            LEFT of start -> end (swap the ends to look the other way); depth default
//                                            3000; bottom / top = mm above the internal origin, default lowest level
//                                            - 1000 and highest level + 3000.
//                callout | detail            sourceViewId = parent view, box {min{x,y,z?}, max{x,y,z?}}. callout in a plan
//                                            = a plan callout of the parent's family; detail (and any callout in a
//                                            section / elevation) = a detail view. z default: the plan's level.
//                drafting                    nothing else
//                sheetList                   a new drawing list (schedule of sheets), fields [names] default
//                                            ["Sheet Number", "Sheet Name"]
//                schedule                    a new schedule: category (BuiltInCategory name, e.g. OST_Doors, or the
//                                            category name), fields [names]; a field not in the category = Warn
//    name        required, unique in the model (VC2). A view of the same kind that already has this name is REUSED:
//                nothing is created, its id comes back (VC1). The name on a view of another kind = error.
//    template    view template name. Named but not in the model = error, the view is not created (VC3).
//    scale       1:n. A template or a parent that fixes another scale = Warn, nothing is forced (VC4).
//    scopeBox    scope box name (plans, dependents: VC5, VC9).   viewType  view family type name, default most used.
//    params      {parameter name: value} text / integer / number instance parameters (Project Browser grouping, VC10).
// Crop and annotation crop are switched on for every model view (VC5).
// Result: Ids[i] per item (apply: view id; preview: -1 = would be created; reused view id; 0 = failed), Problems
// {I, Name, Error | Warn | Reused | Similar}. Similar = up to 3 plans of the same level (and scope box) not on a sheet:
// look at them before creating a second one (VC1). apply writes logPath {Created, Markers, Rows}; undo deletes the
// views of the log that are not on a sheet and not the active view, and the elevation markers left empty.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ViewCreate
{
    const double MM = 304.8;

    class Ctx
    {
        public Document Doc;
        public List<View> Views;
        public Dictionary<string, View> Names, Templates;
        public Dictionary<string, Level> Levels;
        public Dictionary<string, Element> ScopeBoxes;
        public List<ViewFamilyType> Types;
        public Dictionary<int, int> TypeUse;
        public HashSet<int> Placed;
        public double MinZ, MaxZ;
        public string FieldWarn;

        public Level Level(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) throw new Exception("level missing");
            if (t.Type == JTokenType.Integer)
            {
                var byId = Doc.GetElement(new ElementId((int)t)) as Level;
                if (byId == null) throw new Exception("level id not found: " + t);
                return byId;
            }
            Level l;
            if (!Levels.TryGetValue(((string)t).Trim(), out l)) throw new Exception("level not found: " + t);
            return l;
        }

        public ViewFamilyType Type(ViewFamily family, string typeName)
        {
            var ofFamily = Types.Where(x => x.ViewFamily == family).ToList();
            if (ofFamily.Count == 0) throw new Exception("no view type of family " + family);
            if (!string.IsNullOrEmpty(typeName))
            {
                var named = ofFamily.FirstOrDefault(x => x.Name == typeName);
                if (named == null) throw new Exception("view type not found: " + typeName);
                return named;
            }
            return ofFamily.OrderByDescending(x => TypeUse.ContainsKey(x.Id.IntegerValue) ? TypeUse[x.Id.IntegerValue] : 0)
                .ThenBy(x => x.Name, StringComparer.Ordinal).First();
        }
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", log = (string)args["logPath"];
        if (mode != "preview" && mode != "apply" && mode != "undo") return new { Error = "mode: preview | apply | undo" };
        if (mode != "preview" && string.IsNullOrEmpty(log)) return new { Error = "logPath is required for " + mode };
        if (mode == "undo") return Undo(app, doc, log);

        var items = args["items"] as JArray;
        string itemsPath = (string)args["itemsPath"];
        if (items == null && !string.IsNullOrEmpty(itemsPath))
        {
            var tok = JToken.Parse(File.ReadAllText(itemsPath));
            items = tok as JArray ?? tok["items"] as JArray ?? tok["Items"] as JArray;
        }
        if (items == null || items.Count == 0) return new { Error = "no items" };
        bool verbose = args.Value<bool?>("verbose") ?? false;

        var c = new Ctx { Doc = doc };
        c.Views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        c.Names = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
        c.Templates = new Dictionary<string, View>(StringComparer.Ordinal);
        c.TypeUse = new Dictionary<int, int>();
        foreach (var v in c.Views)
        {
            if (v.IsTemplate) { c.Templates[v.Name] = v; continue; }
            if (!c.Names.ContainsKey(v.Name)) c.Names[v.Name] = v;
            int ty = v.GetTypeId().IntegerValue;
            c.TypeUse[ty] = (c.TypeUse.ContainsKey(ty) ? c.TypeUse[ty] : 0) + 1;
        }
        c.Levels = new Dictionary<string, Level>(StringComparer.Ordinal);
        foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()) c.Levels[l.Name] = l;
        c.MinZ = c.Levels.Count > 0 ? c.Levels.Values.Min(l => l.ProjectElevation) : 0;
        c.MaxZ = c.Levels.Count > 0 ? c.Levels.Values.Max(l => l.ProjectElevation) : 0;
        c.ScopeBoxes = new Dictionary<string, Element>(StringComparer.Ordinal);
        foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType()) c.ScopeBoxes[e.Name] = e;
        c.Types = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList();
        c.Placed = new HashSet<int>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(vp => vp.ViewId.IntegerValue));
        foreach (var si in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()) c.Placed.Add(si.ScheduleId.IntegerValue);

        var ids = new int[items.Count];
        var problems = new List<object>();
        var rows = new List<object>();
        var created = new List<int>();
        var markers = new List<int>();
        int nCreated = 0, nReused = 0, nFailed = 0;

        using (var t = new Transaction(doc, "Create views"))
        {
            t.Start();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i] as JObject;
                string name = it == null ? "" : ((string)it["name"] ?? "").Trim();
                string kind = it == null ? null : (string)it["kind"];
                try
                {
                    if (it == null) throw new Exception("item is not an object");
                    if (name.Length == 0) throw new Exception("name missing");
                    View src = it["sourceViewId"] != null && it["sourceViewId"].Type == JTokenType.Integer ? doc.GetElement(new ElementId((int)it["sourceViewId"])) as View : null;
                    ViewType expect = Expect(kind, src);

                    // VC1 / VC2: an existing view with this name is reused when it is the same kind of view
                    View same;
                    if (c.Names.TryGetValue(name, out same))
                    {
                        if (same.ViewType != expect) throw new Exception("name taken by a " + same.ViewType + " view (id " + same.Id.IntegerValue + ")");
                        ids[i] = same.Id.IntegerValue; nReused++;
                        problems.Add(Row(i, name, "Reused", c.Placed.Contains(same.Id.IntegerValue) ? "already on a sheet" : "exists, not on a sheet"));
                        if (verbose) rows.Add(Describe(doc, i, same, "reused"));
                        continue;
                    }

                    // everything the item names must exist before anything is created (VC3)
                    View tpl = null; Element scope = null;
                    string tplName = (string)it["template"], scopeName = (string)it["scopeBox"];
                    if (!string.IsNullOrEmpty(tplName) && !c.Templates.TryGetValue(tplName, out tpl)) throw new Exception("template not found: " + tplName);
                    if (!string.IsNullOrEmpty(scopeName) && !c.ScopeBoxes.TryGetValue(scopeName, out scope)) throw new Exception("scope box not found: " + scopeName);

                    var similar = Similar(c, kind, it, scope);
                    if (similar.Count > 0) problems.Add(Row(i, name, "Similar", similar));

                    View v; ElementId markerId; string warn;
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        try
                        {
                            c.FieldWarn = null;
                            v = Make(c, it, kind, src, out markerId);
                            if (v == null) throw new Exception("Revit returned no view");
                            warn = Setup(doc, v, it, kind, name, tpl, scope);
                            if (c.FieldWarn != null) warn = warn == null ? c.FieldWarn : warn + "; " + c.FieldWarn;
                            st.Commit();
                        }
                        catch { st.RollBack(); throw; }
                    }
                    c.Names[name] = v;
                    nCreated++;
                    ids[i] = mode == "apply" ? v.Id.IntegerValue : -1;
                    created.Add(v.Id.IntegerValue);
                    if (markerId != ElementId.InvalidElementId) markers.Add(markerId.IntegerValue);
                    if (warn != null) problems.Add(Row(i, name, "Warn", warn));
                    if (verbose || mode == "apply") rows.Add(Describe(doc, i, v, "created"));
                }
                catch (Exception ex)
                {
                    nFailed++; ids[i] = 0;
                    problems.Add(Row(i, name, "Error", Short(ex.Message)));
                }
            }
            if (mode == "apply")
            {
                if (t.Commit() != TransactionStatus.Committed) return new { Error = "the transaction was not committed (a Revit warning stopped it)" };
                File.WriteAllText(log, JsonConvert.SerializeObject(new { Created = created, Markers = markers, Rows = rows }, Formatting.Indented));
            }
            else t.RollBack();
        }
        var result = new Dictionary<string, object>
        {
            { "mode", mode }, { "Items", items.Count }, { "Created", nCreated }, { "Reused", nReused }, { "Failed", nFailed },
            { "Ids", ids }, { "Problems", problems }
        };
        if (mode == "apply") result["logPath"] = log;
        if (verbose) result["Rows"] = rows;
        return result;
    }

    // ---- one item ------------------------------------------------------------------------

    static ViewType Expect(string kind, View src)
    {
        switch (kind)
        {
            case "floorPlan": return ViewType.FloorPlan;
            case "ceilingPlan": return ViewType.CeilingPlan;
            case "elevation": return ViewType.Elevation;
            case "section": return ViewType.Section;
            case "drafting": return ViewType.DraftingView;
            case "legendCopy": return ViewType.Legend;
            case "scheduleCopy": return ViewType.Schedule;
            case "sheetList": return ViewType.Schedule;
            case "schedule": return ViewType.Schedule;
            case "detail": return ViewType.Detail;
            case "duplicate":
            case "dependent":
                if (src == null) throw new Exception("sourceViewId: not a view");
                return src.ViewType;
            case "callout":
                if (src == null) throw new Exception("sourceViewId: not a view");
                return src is ViewPlan ? src.ViewType : ViewType.Detail;
            default: throw new Exception("kind: floorPlan | ceilingPlan | duplicate | dependent | elevation | section | callout | detail | drafting | legendCopy | scheduleCopy | sheetList | schedule");
        }
    }

    static View Make(Ctx c, JObject it, string kind, View src, out ElementId markerId)
    {
        var doc = c.Doc;
        markerId = ElementId.InvalidElementId;
        string typeName = (string)it["viewType"];
        switch (kind)
        {
            case "floorPlan":
            case "ceilingPlan":
            {
                var level = c.Level(it["level"]);
                var family = kind == "floorPlan" ? ViewFamily.FloorPlan : ViewFamily.CeilingPlan;
                return ViewPlan.Create(doc, c.Type(family, typeName).Id, level.Id);
            }
            case "duplicate":
            case "dependent":
            case "legendCopy":
            case "scheduleCopy":
            {
                if (kind == "legendCopy" && src.ViewType != ViewType.Legend) throw new Exception("sourceViewId is not a legend");
                if (kind == "scheduleCopy" && !(src is ViewSchedule)) throw new Exception("sourceViewId is not a schedule");
                bool detailing = it.Value<bool?>("withDetailing") ?? (kind == "legendCopy");
                var option = kind == "dependent" ? ViewDuplicateOption.AsDependent
                    : detailing ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate;
                if (!src.CanViewBeDuplicated(option)) throw new Exception("this view cannot be duplicated as " + option);
                return doc.GetElement(src.Duplicate(option)) as View;
            }
            case "elevation":
            {
                var plan = src as ViewPlan;
                if (plan == null) throw new Exception("sourceViewId: the plan the elevation marker sits on");
                var mk = it["marker"] as JObject;
                if (mk == null) throw new Exception("marker {x, y, direction}");
                double z = plan.GenLevel != null ? plan.GenLevel.ProjectElevation : 0;
                var origin = new XYZ(Num(mk, "x") / MM, Num(mk, "y") / MM, z);
                XYZ look = Look(mk["direction"]);
                int scale = it.Value<int?>("scale") ?? 100;
                var marker = ElevationMarker.CreateElevationMarker(doc, c.Type(ViewFamily.Elevation, typeName).Id, origin, scale);
                var view = marker.CreateElevation(doc, plan.Id, 0);
                doc.Regenerate();
                // ViewDirection points at the viewer: turn the marker until it is the opposite of where the view looks
                XYZ cur = view.ViewDirection, want = look.Negate();
                double angle = Math.Atan2(cur.X * want.Y - cur.Y * want.X, cur.X * want.X + cur.Y * want.Y);
                if (Math.Abs(angle) > 1e-9) ElementTransformUtils.RotateElement(doc, marker.Id, Line.CreateBound(origin, origin + XYZ.BasisZ), angle);
                double? depth = mk.Value<double?>("depth");
                if (depth != null && depth > 0)
                {
                    var far = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                    if (far != null && !far.IsReadOnly) far.Set(depth.Value / MM);
                }
                double? width = mk.Value<double?>("width"), bottom = mk.Value<double?>("bottom"), top = mk.Value<double?>("top");
                if (width != null || bottom != null || top != null)
                {
                    doc.Regenerate();
                    var cb = view.CropBox;
                    XYZ local = cb.Transform.Inverse.OfPoint(origin);   // the marker in crop coordinates: X along the facade, Y up
                    double x0 = cb.Min.X, x1 = cb.Max.X, y0 = cb.Min.Y, y1 = cb.Max.Y;
                    if (width != null && width > 0) { x0 = local.X - width.Value / 2 / MM; x1 = local.X + width.Value / 2 / MM; }
                    if (bottom != null) y0 = local.Y + bottom.Value / MM - origin.Z;
                    if (top != null) y1 = local.Y + top.Value / MM - origin.Z;
                    if (x1 > x0 && y1 > y0)
                    {
                        cb.Min = new XYZ(x0, y0, cb.Min.Z); cb.Max = new XYZ(x1, y1, cb.Max.Z);
                        view.CropBox = cb;
                    }
                }
                markerId = marker.Id;
                return view;
            }
            case "section":
            {
                var ln = it["line"] as JObject;
                var s = ln == null ? null : ln["start"] as JObject;
                var e = ln == null ? null : ln["end"] as JObject;
                if (s == null || e == null) throw new Exception("line {start{x,y}, end{x,y}, depth}");
                XYZ a = new XYZ(Num(s, "x") / MM, Num(s, "y") / MM, 0), b = new XYZ(Num(e, "x") / MM, Num(e, "y") / MM, 0);
                double length = a.DistanceTo(b);
                if (length < 10 / MM) throw new Exception("line: start and end are the same point");
                double depth = (ln.Value<double?>("depth") ?? 3000) / MM;
                double bottom = ln.Value<double?>("bottom") != null ? ln.Value<double>("bottom") / MM : c.MinZ - 1000 / MM;
                double top = ln.Value<double?>("top") != null ? ln.Value<double>("top") / MM : c.MaxZ + 3000 / MM;
                if (top <= bottom) throw new Exception("line: top must be above bottom");
                XYZ dir = (b - a).Normalize();
                var tr = Transform.Identity;
                tr.Origin = (a + b) / 2;
                tr.BasisX = dir; tr.BasisY = XYZ.BasisZ; tr.BasisZ = dir.CrossProduct(XYZ.BasisZ);   // looks along -BasisZ = left of a -> b
                var box = new BoundingBoxXYZ { Transform = tr, Min = new XYZ(-length / 2, bottom, -depth), Max = new XYZ(length / 2, top, 0) };
                return ViewSection.CreateSection(doc, c.Type(ViewFamily.Section, typeName).Id, box);
            }
            case "callout":
            case "detail":
            {
                if (src == null) throw new Exception("sourceViewId: the parent view of the callout");
                var bx = it["box"] as JObject;
                var mn = bx == null ? null : bx["min"] as JObject;
                var mx = bx == null ? null : bx["max"] as JObject;
                if (mn == null || mx == null) throw new Exception("box {min{x,y}, max{x,y}}");
                bool planParent = src is ViewPlan;
                var parentType = doc.GetElement(src.GetTypeId()) as ViewFamilyType;
                var family = kind == "detail" || !planParent || parentType == null ? ViewFamily.Detail : parentType.ViewFamily;
                double z0 = planParent && src.GenLevel != null ? src.GenLevel.ProjectElevation * MM : 0;
                var p1 = new XYZ(Num(mn, "x") / MM, Num(mn, "y") / MM, (mn.Value<double?>("z") ?? z0) / MM);
                var p2 = new XYZ(Num(mx, "x") / MM, Num(mx, "y") / MM, (mx.Value<double?>("z") ?? z0) / MM);
                return ViewSection.CreateCallout(doc, src.Id, c.Type(family, typeName).Id, p1, p2);
            }
            case "drafting":
                return ViewDrafting.Create(doc, c.Type(ViewFamily.Drafting, typeName).Id);
            case "sheetList":
            {
                var vs = ViewSchedule.CreateSheetList(doc);
                AddFields(doc, vs, it["fields"] as JArray ?? new JArray("Sheet Number", "Sheet Name"), c);
                return vs;
            }
            case "schedule":
            {
                string cat = ((string)it["category"] ?? "").Trim();
                if (cat.Length == 0) throw new Exception("category missing");
                ElementId catId;
                BuiltInCategory bic;
                if (Enum.TryParse(cat, out bic)) catId = new ElementId(bic);
                else
                {
                    var found = doc.Settings.Categories.Cast<Category>().FirstOrDefault(x => string.Equals(x.Name, cat, StringComparison.OrdinalIgnoreCase));
                    if (found == null) throw new Exception("category not found: " + cat);
                    catId = found.Id;
                }
                var vs = ViewSchedule.CreateSchedule(doc, catId);
                AddFields(doc, vs, it["fields"] as JArray, c);
                return vs;
            }
        }
        throw new Exception("kind not handled: " + kind);
    }

    /// <summary>Schedule fields by their names, in order; names not offered by the schedule go to c.FieldWarn.</summary>
    static void AddFields(Document doc, ViewSchedule vs, JArray names, Ctx c)
    {
        c.FieldWarn = null;
        if (names == null) return;
        var def = vs.Definition;
        var offered = def.GetSchedulableFields();
        var missing = new List<string>();
        foreach (var n in names.Select(x => ((string)x ?? "").Trim()).Where(x => x.Length > 0))
        {
            var f = offered.FirstOrDefault(x => string.Equals(x.GetName(doc), n, StringComparison.OrdinalIgnoreCase));
            if (f == null) { missing.Add(n); continue; }
            def.AddField(f);
        }
        if (missing.Count > 0) c.FieldWarn = "fields not found: " + string.Join(", ", missing);
    }

    /// <summary>Name, scale, scope box, template, crop, parameters. Returns a warning or null.</summary>
    static string Setup(Document doc, View v, JObject it, string kind, string name, View tpl, Element scope)
    {
        var warns = new List<string>();
        v.Name = name;
        bool paper = v is ViewSchedule || v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView;
        int? scale = it.Value<int?>("scale");
        if (scale != null && scale > 0 && !(v is ViewSchedule) && kind != "dependent")
        {
            try { if (v.Scale != scale.Value) v.Scale = scale.Value; } catch { }
        }
        if (scope != null)
        {
            var sp = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (sp == null || sp.IsReadOnly) warns.Add("scope box cannot be set on this view");
            else sp.Set(scope.Id);
        }
        if (tpl != null)
        {
            if (!v.IsValidViewTemplate(tpl.Id)) throw new Exception("template '" + tpl.Name + "' is for another kind of view");
            v.ViewTemplateId = tpl.Id;
        }
        if (!paper)
        {
            try { if (!v.CropBoxActive) v.CropBoxActive = true; } catch { warns.Add("crop not switched on"); }
            var ac = v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (ac != null && !ac.IsReadOnly && ac.AsInteger() != 1) ac.Set(1);
        }
        var pars = it["params"] as JObject;
        if (pars != null)
        {
            foreach (var p in pars.Properties())
            {
                var par = v.LookupParameter(p.Name);
                if (par == null || par.IsReadOnly) { warns.Add("parameter not set: " + p.Name); continue; }
                try
                {
                    if (par.StorageType == StorageType.String) par.Set((string)p.Value);
                    else if (par.StorageType == StorageType.Integer) par.Set((int)p.Value);
                    else if (par.StorageType == StorageType.Double) par.Set((double)p.Value);
                    else warns.Add("parameter not set: " + p.Name);
                }
                catch { warns.Add("parameter not set: " + p.Name); }
            }
        }
        doc.Regenerate();
        if (scale != null && scale > 0 && !(v is ViewSchedule) && v.Scale != scale.Value)
            warns.Add("scale is 1:" + v.Scale + " (template or parent view), asked 1:" + scale.Value);
        return warns.Count == 0 ? null : string.Join("; ", warns);
    }

    /// <summary>VC1: plans of the same level (and scope box) that sit on no sheet yet.</summary>
    static List<int> Similar(Ctx c, string kind, JObject it, Element scope)
    {
        var found = new List<int>();
        if (kind != "floorPlan" && kind != "ceilingPlan") return found;
        Level level;
        try { level = c.Level(it["level"]); } catch { return found; }
        var type = kind == "floorPlan" ? ViewType.FloorPlan : ViewType.CeilingPlan;
        foreach (var v in c.Views)
        {
            if (v.IsTemplate || v.ViewType != type || v.GenLevel == null || v.GenLevel.Id != level.Id) continue;
            if (c.Placed.Contains(v.Id.IntegerValue)) continue;
            if (scope != null)
            {
                var sp = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (sp == null || sp.AsElementId() != scope.Id) continue;
            }
            found.Add(v.Id.IntegerValue);
            if (found.Count == 3) break;
        }
        return found;
    }

    static XYZ Look(JToken t)
    {
        if (t == null || t.Type == JTokenType.Null) throw new Exception("marker.direction: east | north | west | south, degrees or {x, y}");
        if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
        {
            double a = (double)t * Math.PI / 180;
            return new XYZ(Math.Cos(a), Math.Sin(a), 0);
        }
        var o = t as JObject;
        if (o != null)
        {
            var d = new XYZ(Num(o, "x"), Num(o, "y"), 0);
            if (d.GetLength() < 1e-9) throw new Exception("marker.direction {x, y} is zero");
            return d.Normalize();
        }
        switch (((string)t).Trim().ToLowerInvariant())
        {
            case "east": case "e": return XYZ.BasisX;
            case "north": case "n": return XYZ.BasisY;
            case "west": case "w": return XYZ.BasisX.Negate();
            case "south": case "s": return XYZ.BasisY.Negate();
        }
        throw new Exception("marker.direction: east | north | west | south, degrees or {x, y}");
    }

    static double Num(JObject o, string key)
    {
        var t = o[key];
        if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) throw new Exception(key + " missing (number, mm)");
        return (double)t;
    }

    static Dictionary<string, object> Row(int i, string name, string key, object value)
    {
        return new Dictionary<string, object> { { "I", i }, { "Name", name }, { key, value } };
    }

    static object Describe(Document doc, int i, View v, string what)
    {
        var tpl = doc.GetElement(v.ViewTemplateId) as View;
        return new
        {
            I = i, Id = v.Id.IntegerValue, Name = v.Name, What = what, ViewType = v.ViewType.ToString(),
            Scale = v is ViewSchedule ? 0 : v.Scale, Template = tpl == null ? null : tpl.Name,
            Level = v.GenLevel == null ? null : v.GenLevel.Name
        };
    }

    static string Short(string message)
    {
        message = (message ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length > 160 ? message.Substring(0, 160) : message;
    }

    // ---- undo ----------------------------------------------------------------------------

    static object Undo(UIApplication app, Document doc, string log)
    {
        if (!File.Exists(log)) return new { Error = "log not found: " + log };
        var j = JObject.Parse(File.ReadAllText(log));
        var onSheet = new HashSet<int>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(vp => vp.ViewId.IntegerValue));
        foreach (var si in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()) onSheet.Add(si.ScheduleId.IntegerValue);
        int active = app.ActiveUIDocument.ActiveView == null ? -1 : app.ActiveUIDocument.ActiveView.Id.IntegerValue;
        int deleted = 0;
        var kept = new List<object>();
        using (var t = new Transaction(doc, "Create views: undo"))
        {
            t.Start();
            foreach (int id in (j["Created"] as JArray ?? new JArray()).Select(x => (int)x))
            {
                var v = doc.GetElement(new ElementId(id)) as View;
                if (v == null) continue;
                if (id == active) { kept.Add(new { Id = id, Why = "active view: open another view, then undo again" }); continue; }
                if (onSheet.Contains(id)) { kept.Add(new { Id = id, Why = "placed on a sheet" }); continue; }
                try { doc.Delete(v.Id); deleted++; }
                catch (Exception ex) { kept.Add(new { Id = id, Why = Short(ex.Message) }); }
            }
            doc.Regenerate();
            foreach (int id in (j["Markers"] as JArray ?? new JArray()).Select(x => (int)x))
            {
                var marker = doc.GetElement(new ElementId(id)) as ElevationMarker;
                if (marker == null || marker.CurrentViewCount > 0) continue;
                try { doc.Delete(marker.Id); } catch { }
            }
            t.Commit();
        }
        return new { mode = "undo", Deleted = deleted, Kept = kept };
    }
}
