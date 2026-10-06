/* mcp-tool
{
  "description": "Lay out grid dimensions in ONE view (the view that owns them, e.g. the parent of dependent views). dir 'H' = dims running along view right (number grids), 'V' = along view up (letter grids). Lists the host grids of that orientation visible in the view with their position along the dim direction, and the existing all-grid dims of that direction (id, position across, grids). create: [{acrossMm, kind:'chain'|'overall', fromGrid?, toGrid?}] makes new dims (chain = every grid between from and to, overall = from and to only) at that position (mm from the view origin across the dim direction). deleteIds: existing dims to delete (their grids, position, type are logged so they can be recreated). typeName: dimension type for new dims. mode preview (rolled back) | apply | undo (deletes created dims and recreates deleted ones from logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" }, "dir": { "type": "string", "enum": ["H", "V"] },
      "create": { "type": "array", "items": { "type": "object" } },
      "deleteIds": { "type": "array", "items": { "type": "number" } },
      "typeName": { "type": "string" }, "mode": { "type": "string", "enum": ["preview", "apply", "undo"] }, "logPath": { "type": "string" }
    },
    "required": ["viewId", "dir", "mode", "logPath"]
  },
  "timeoutSeconds": 300,
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

public static class GridDimsLayout
{
    const double MM = 304.8;
    static Dimension Make(Document doc, View v, XYZ along, XYZ across, List<Grid> gs, double acrossFt, DimensionType type)
    {
        var ra = new ReferenceArray(); foreach (var g in gs) ra.Append(new Reference(g));
        double a0 = gs.Min(g => Pos(v, g, along)), a1 = gs.Max(g => Pos(v, g, along));
        double z = v.Origin.DotProduct(v.ViewDirection);
        XYZ p0 = v.Origin + along * (a0 / MM) + across * acrossFt, p1 = v.Origin + along * (a1 / MM) + across * acrossFt;
        var d = doc.Create.NewDimension(v, Line.CreateBound(p0, p1), ra);
        if (type != null) d.ChangeTypeId(type.Id);
        return d;
    }
    // position of a grid along 'along' (mm from view origin), grid curve perpendicular to 'along'
    static double Pos(View v, Grid g, XYZ along) => (((Line)g.Curve).Origin - v.Origin).DotProduct(along) * MM;

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"], log = (string)args["logPath"];
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        bool H = (string)args["dir"] == "H";
        XYZ along = H ? v.RightDirection : v.UpDirection, across = H ? v.UpDirection : v.RightDirection;
        var grids = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>()
            .Where(g => g.Curve is Line l && Math.Abs(l.Direction.DotProduct(along)) < 0.01)
            .OrderBy(g => Pos(v, g, along)).ToList();
        var types = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>();
        if (mode == "undo")
        {
            var L = JObject.Parse(File.ReadAllText(log)); int del = 0, re = 0;
            using (var t = new Transaction(doc, "Grid dims layout undo"))
            {
                t.Start();
                foreach (var id in L["Created"]) { if (doc.GetElement(new ElementId((int)id)) != null) { doc.Delete(new ElementId((int)id)); del++; } }
                foreach (JObject o in L["Deleted"])
                {
                    var names = ((string)o["Grids"]).Split(',');
                    var gs = names.Select(n => grids.FirstOrDefault(g => g.Name == n)).Where(g => g != null).ToList();
                    var ty = types.FirstOrDefault(x => x.Name == (string)o["Type"]);
                    if (gs.Count >= 2) { Make(doc, v, along, across, gs, (double)o["AcrossMm"] / MM, ty); re++; }
                }
                t.Commit();
            }
            return new { DeletedCreated = del, Recreated = re };
        }
        Func<Dimension, double> acrossOf = d => (((Line)d.Curve).Origin - v.Origin).DotProduct(across) * MM;
        var existing = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(d => d.OwnerViewId == v.Id && d.Curve is Line ln && Math.Abs(ln.Direction.DotProduct(along)) > 0.99)
            .Select(d => new { d, names = d.References.Cast<Reference>().Select(r => r.LinkedElementId == ElementId.InvalidElementId ? doc.GetElement(r.ElementId) as Grid : null).ToList() })
            .Where(x => x.names.Count >= 2 && x.names.All(g => g != null)).ToList();
        var exRows = existing.Select(x => new { Id = x.d.Id.IntegerValue, Type = x.d.DimensionType.Name, AcrossMm = Math.Round(acrossOf(x.d)), Segs = x.d.NumberOfSegments, Grids = string.Join(",", x.names.Select(g => g.Name)) }).OrderBy(r => r.AcrossMm).ToList();
        var created = new List<int>(); var deleted = new List<object>(); var errors = new List<string>();
        var typeName = (string)args["typeName"];
        var type = typeName == null ? null : types.FirstOrDefault(x => x.Name == typeName);
        if (typeName != null && type == null) return new { Error = "type not found: " + typeName };
        using (var t = new Transaction(doc, "Grid dims layout"))
        {
            t.Start();
            var delIds = (args["deleteIds"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>();
            if (args.Value<bool?>("deleteAll") ?? false) delIds.AddRange(exRows.Select(r => r.Id));
            string delSide = (string)args["deleteSide"]; // 'high' = top (H) / right (V), 'low' = bottom / left, relative to the middle of the perpendicular grids
            if (delSide != null)
            {
                var perp = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>()
                    .Where(g => g.Curve is Line l && Math.Abs(l.Direction.DotProduct(along)) > 0.99).Select(g => Pos(v, g, across)).ToList();
                if (perp.Count > 0)
                {
                    double mid = (perp.Min() + perp.Max()) / 2;
                    delIds.AddRange(exRows.Where(r => delSide == "high" ? r.AcrossMm > mid : r.AcrossMm < mid).Select(r => r.Id));
                }
            }
            foreach (var id in delIds.Distinct())
            {
                var row = exRows.FirstOrDefault(r => r.Id == id);
                if (row == null) { errors.Add("not a grid dim of this view/direction: " + id); continue; }
                deleted.Add(row); doc.Delete(new ElementId(id));
            }
            foreach (JObject c in (args["create"] as JArray) ?? new JArray())
            {
                string from = (string)c["fromGrid"], to = (string)c["toGrid"];
                int i0 = from == null ? 0 : grids.FindIndex(g => g.Name == from), i1 = to == null ? grids.Count - 1 : grids.FindIndex(g => g.Name == to);
                if (i0 < 0 || i1 < 0) { errors.Add("grid not found " + from + "/" + to); continue; }
                if (i0 > i1) { var k = i0; i0 = i1; i1 = k; }
                var gs = (string)c["kind"] == "overall" ? new List<Grid> { grids[i0], grids[i1] } : grids.GetRange(i0, i1 - i0 + 1);
                try
                {
                    var d = Make(doc, v, along, across, gs, (double)c["acrossMm"] / MM, type); created.Add(d.Id.IntegerValue);
                    foreach (var hv in (c["hideInViews"] as JArray)?.Select(x => (int)x) ?? Enumerable.Empty<int>())
                        ((View)doc.GetElement(new ElementId(hv))).HideElements(new List<ElementId> { d.Id });
                }
                catch (Exception e) { errors.Add("create failed: " + e.Message); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(log, JsonConvert.SerializeObject(new { View = v.Name, Created = created, Deleted = deleted }, Formatting.Indented)); }
            else t.RollBack();
        }
        if (args.Value<bool?>("compact") ?? false)
            return new { mode, View = v.Name, Dir = H ? "H" : "V", Created = created, Deleted = deleted.Count, Errors = errors, Remaining = exRows.Where(r => !deleted.Any(d => ((dynamic)d).Id == r.Id)).Select(r => r.Id + "@" + r.AcrossMm + (r.Segs > 0 ? " chain " + r.Grids.Split(',').Length : " overall " + r.Grids)) };
        return new
        {
            mode, View = v.Name, Dir = H ? "H" : "V",
            Grids = string.Join(" ", grids.Select(g => g.Name + "@" + Math.Round(Pos(v, g, along)))),
            Existing = exRows, Created = created.Count, Deleted = deleted.Count, Errors = errors
        };
    }
}
