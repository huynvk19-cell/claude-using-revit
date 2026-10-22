/* mcp-tool
{
  "description": "ONE plan view: place room tags so the tag BODY centre sits at a given point (model mm), optional straight leader to an end point inside the room: the leader is made exactly vertical or horizontal on the sheet by aligning the body centre with the end (the leader starts from the body centre). preview | apply (logPath) | undo (logPath).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": { "type": "number" },
      "items": { "type": "array", "items": { "type": "object" }, "description": "{roomId, typeName, cx, cy, ex?, ey?, path?} model mm; ex/ey = leader end inside the room; path HV | VH = one right-angle bend (H then V or V then H), else straight" },
      "mode": { "type": "string", "enum": ["preview", "apply", "undo"] },
      "logPath": { "type": "string" },
      "itemsPath": { "type": "string", "description": "read items from this JSON file (room_tags_missing outPath)" },
      "brief": { "type": "boolean", "description": "return counts + errors only (default false)" }
    },
    "required": ["mode"]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details ----
// itemsPath: a JSON file holding an items array, or an object with "Items" (room_tags_missing outPath). Items in the
// room_tags_missing / annot_place form {elementId, typeName, right, up, leader, endRight, endUp} (view frame mm) are
// converted to {roomId, cx, cy, ex, ey, path} (model mm) here. Leader end / elbow take the room point height (rooms
// of other levels); when Revit returns no tag at the leader end it retries at the room point.
// The tag is created at the end point (or the centre) so Revit accepts it, set to the type, leader off, then the head is
// moved so the bbox centre of the tag body (no leader) equals (cx,cy). With a leader: vertical intent (|dy|>=|dx|) -> ex := cx,
// horizontal intent -> ey := cy; LeaderEnd = (ex,ey); elbow (if any) put on the straight line. preview rolls back.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class RoomtagPlaceOrtho
{
    const double MM = 304.8;
    public static object Run(UIApplication app, JObject args)
    {
        Document doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"];
        if (mode == "undo")
        {
            var log = JObject.Parse(File.ReadAllText((string)args["logPath"]));
            var ids = ((JArray)log["created"]).Select(x => new ElementId((int)x)).Where(id => doc.GetElement(id) != null).ToList();
            using (var t = new Transaction(doc, "Room tags undo")) { t.Start(); doc.Delete(ids); t.Commit(); }
            if ((bool?)args["brief"] == true) return new { deleted = ids.Count };
            return new { deleted = ids.Select(i => i.IntegerValue).ToList() };
        }
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        double z = (v as ViewPlan)?.GenLevel?.ProjectElevation ?? 0;
        var done = new List<object>(); var errors = new List<string>(); var created = new List<int>();
        JArray src = args["items"] as JArray ?? new JArray();
        string ip = (string)args["itemsPath"];
        if (!string.IsNullOrEmpty(ip))
        {
            var tok = JToken.Parse(File.ReadAllText(ip));
            src = tok is JArray ja ? ja : (JArray)(tok["Items"] ?? tok["items"]);
        }
        XYZ vo = v.Origin, vr = v.RightDirection, vu = v.UpDirection;
        Func<double, double, XYZ> VF = (r, u) => vo + vr * (r / MM) + vu * (u / MM);
        var items = new JArray();
        foreach (JObject it in src)
        {
            if (it["roomId"] != null) { items.Add(it); continue; }
            var c = VF((double)it["right"], (double)it["up"]);
            var o = new JObject { ["roomId"] = it["elementId"], ["typeName"] = it["typeName"], ["cx"] = c.X * MM, ["cy"] = c.Y * MM };
            if ((bool?)it["leader"] == true && it["endRight"] != null) { var e = VF((double)it["endRight"], (double)it["endUp"]); o["ex"] = e.X * MM; o["ey"] = e.Y * MM; if (it["path"] != null) o["path"] = it["path"]; }
            items.Add(o);
        }
        using (var t = new Transaction(doc, "Room tags"))
        {
            t.Start();
            int i = 0;
            foreach (JObject it in items)
            {
                i++;
                ElementId made = null;
                try
                {
                    var room = doc.GetElement(new ElementId((int)it["roomId"])) as Room;
                    var typeName = (string)it["typeName"];
                    var sym = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                        .FirstOrDefault(x => (x.Name == typeName || x.FamilyName + " : " + x.Name == typeName) && x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RoomTags);
                    if (room == null || sym == null) { errors.Add(i + ": room or type not found"); continue; }
                    double cx = (double)it["cx"] / MM, cy = (double)it["cy"] / MM;
                    bool leader = it["ex"] != null;
                    double ex = leader ? (double)it["ex"] / MM : cx, ey = leader ? (double)it["ey"] / MM : cy;
                    var rt = doc.Create.NewRoomTag(new LinkElementId(room.Id), new UV(ex, ey), v.Id);
                    var rlp = (room.Location as LocationPoint)?.Point;
                    if (rt == null && rlp != null) rt = doc.Create.NewRoomTag(new LinkElementId(room.Id), new UV(rlp.X, rlp.Y), v.Id); // retry at the room point
                    if (rt == null) { errors.Add(i + ": Revit makes no room tag for " + room.Number + " in this view"); continue; }
                    double zr = rlp != null ? rlp.Z : z; // leader end at the room height (rooms of other levels)
                    made = rt.Id;
                    rt.ChangeTypeId(sym.Id);
                    rt.HasLeader = false;
                    doc.Regenerate();
                    var b = rt.get_BoundingBox(v);
                    var h = rt.TagHeadPosition;
                    // no box (Revit draws nothing yet, seen on ceiling plans): keep the head on the point
                    double ox = b == null ? 0 : (b.Min.X + b.Max.X) / 2 - h.X, oy = b == null ? 0 : (b.Min.Y + b.Max.Y) / 2 - h.Y;
                    if (leader) rt.HasLeader = true;
                    try { rt.TagHeadPosition = new XYZ(cx - ox, cy - oy, z); } catch { if (leader) throw; }
                    string dir = "none";
                    if (leader)
                    {
                        string path = (string)it["path"];
                        rt.HasLeader = true;
                        if (path == "HV" || path == "VH")
                        {
                            dir = path;
                            rt.LeaderEnd = new XYZ(ex, ey, zr);
                            double pdx = it["adx"] != null ? (double)it["adx"] / MM : 0, pdy = it["ady"] != null ? (double)it["ady"] / MM : 0;
                            rt.LeaderElbow = path == "HV" ? new XYZ(ex, cy + pdy, zr) : new XYZ(cx + pdx, ey, zr);
                        }
                        else
                        {
                            double adx = it["adx"] != null ? (double)it["adx"] / MM : 0, ady = it["ady"] != null ? (double)it["ady"] / MM : 0;
                            if (Math.Abs(ey - cy) >= Math.Abs(ex - cx)) { ex = cx + adx; dir = "V"; } else { ey = cy + ady; dir = "H"; }
                            rt.LeaderEnd = new XYZ(ex, ey, zr);
                            try { if (rt.HasElbow) rt.LeaderElbow = new XYZ((cx + ex) / 2, (cy + ey) / 2, z); } catch { }
                        }
                    }
                    doc.Regenerate();
                    var b2 = rt.get_BoundingBox(v);
                    created.Add(rt.Id.IntegerValue);
                    done.Add(new
                    {
                        item = i, id = rt.Id.IntegerValue, room = room.Number + " " + room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString(), dir,
                        body = b == null ? "no box" : Math.Round(b.Max.X * MM - b.Min.X * MM) + " x " + Math.Round(b.Max.Y * MM - b.Min.Y * MM),
                        box = b2 == null ? "no box" : Math.Round(b2.Min.X * MM) + ".." + Math.Round(b2.Max.X * MM) + " , " + Math.Round(b2.Min.Y * MM) + ".." + Math.Round(b2.Max.Y * MM),
                        end = leader ? Math.Round(ex * MM) + "," + Math.Round(ey * MM) : null, text = rt.TagText
                    });
                }
                catch (Exception e) { errors.Add(i + ": " + e.Message); if (made != null && doc.GetElement(made) != null) doc.Delete(made); }
            }
            if (mode == "apply") t.Commit(); else t.RollBack();
        }
        string lp = (string)args["logPath"];
        if (mode == "apply" && !string.IsNullOrEmpty(lp))
            File.WriteAllText(lp, new JObject { ["view"] = v.Id.IntegerValue, ["created"] = new JArray(created), ["done"] = JArray.FromObject(done) }.ToString(Formatting.Indented));
        if ((bool?)args["brief"] == true) return new { view = v.Name, mode, placed = done.Count, errors };
        return new { view = v.Name, mode, done, errors };
    }
}
