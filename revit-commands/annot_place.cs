/* mcp-tool
{
  "description": "ONE plan view: place tags / spot elevations at view-frame points (Right/Up mm); orthogonal leaders. preview | apply | undo.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "viewId": {
        "type": "number"
      },
      "items": {
        "type": "array",
        "items": {
          "type": "object"
        }
      },
      "mode": {
        "type": "string",
        "enum": [
          "preview",
          "apply",
          "undo"
        ]
      },
      "logPath": {
        "type": "string"
      }
    },
    "required": [
      "mode"
    ]
  },
  "timeoutSeconds": 120
}
*/
// ---- Details (kept out of the MCP header so the tool list stays short; read when unsure) ----
// ONE plan view: place tags and spot elevations at given points in the view frame (mm along the view's Right / Up
//    from the view origin, as stair_plan_audit / view_elem_boxes ViewBox). items: {kind:'tag', elementId, typeName
//    (tag type name, category from the element), right, up (head), leader (default false), endRight, endUp (free
//    leader end on the element, optional; leaders are kept orthogonal: if the end is not straight above/beside the
//    head an elbow is added, elbowFirst V (default: vertical from the head, then horizontal) or H, or explicit
//    elbowRight / elbowUp)} | {kind:'spot', elementId (floor / landing / run: its highest horizontal face), typeName
//    (spot elevation type), right, up (point on the face)} | {kind:'roomtag', elementId (room, any level seen in the
//    view), typeName (room tag type), right, up, leader, endRight, endUp} | {kind:'stairpath', elementId (stairs),
//    typeName (stair path type)}. Types must already exist in the project (never created).
//    mode preview (rolled back) | apply (logPath) | undo (logPath: deletes what was created).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class AnnotPlace
{
    const double MM = 304.8;

    static PlanarFace TopFace(Element e, out double topZ)
    {
        PlanarFace best = null; topZ = double.NegativeInfinity;
        var stack = new Stack<GeometryElement>(); stack.Push(e.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine }));
        while (stack.Count > 0)
        {
            var ge = stack.Pop(); if (ge == null) continue;
            foreach (var g in ge)
            {
                if (g is GeometryInstance gi) { stack.Push(gi.GetInstanceGeometry()); continue; }
                if (!(g is Solid s)) continue;
                foreach (Face f in s.Faces)
                    if (f is PlanarFace pf && pf.Reference != null && pf.FaceNormal.Z > 0.999 && pf.Origin.Z > topZ) { best = pf; topZ = pf.Origin.Z; }
            }
        }
        return best;
    }

    public static object Run(UIApplication app, JObject args)
    {
        var doc = app.ActiveUIDocument.Document;
        string mode = (string)args["mode"] ?? "preview", logPath = (string)args["logPath"];
        if (mode == "undo")
        {
            var ids = JsonConvert.DeserializeObject<List<int>>(File.ReadAllText(logPath)); var gone = new List<int>();
            using (var t = new Transaction(doc, "Undo annot place"))
            {
                t.Start();
                foreach (var id in ids) if (doc.GetElement(new ElementId(id)) != null) { doc.Delete(new ElementId(id)); gone.Add(id); }
                t.Commit();
            }
            return new { Mode = "undo", Deleted = gone };
        }
        if (mode == "apply" && string.IsNullOrEmpty(logPath)) return new { Error = "apply needs logPath" };
        var v = (View)doc.GetElement(new ElementId((int)args["viewId"]));
        XYZ O = v.Origin, R = v.RightDirection, U = v.UpDirection;
        double z0 = v.GenLevel != null ? v.GenLevel.ProjectElevation : O.Z;
        Func<double, double, double, XYZ> P = (r, u, z) => new XYZ(O.X, O.Y, z) + R * (r / MM) + U * (u / MM);
        var created = new List<int>(); var done = new List<object>(); var errors = new List<string>();
        using (var t = new Transaction(doc, "Annot place"))
        {
            t.Start();
            int i = 0;
            foreach (var it in (JArray)args["items"] ?? new JArray())
            {
                i++;
                string kind = ((string)it["kind"] ?? "tag").ToLower(), typeName = (string)it["typeName"];
                var e = doc.GetElement(new ElementId((int)it["elementId"]));
                if (e == null) { errors.Add(i + ": element " + it["elementId"] + " not found"); continue; }
                double r = (double?)it["right"] ?? 0, u = (double?)it["up"] ?? 0;
                var sub = new SubTransaction(doc); sub.Start(); int before = created.Count;
                try
                {
                    if (kind == "spot")
                    {
                        var st = new FilteredElementCollector(doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().FirstOrDefault(x => x.Name == typeName && x.StyleType == DimensionStyleType.SpotElevation);
                        if (st == null) { errors.Add(i + ": no spot elevation type '" + typeName + "'"); continue; }
                        // the view's own geometry first (Stairs: landing faces as drawn in plan; spots on it display), else the 3D top face
                        PlanarFace pf = null; double topZ = double.NegativeInfinity;
                        var probe = P(r, u, 0);
                        var st2 = new Stack<GeometryElement>(); st2.Push(e.get_Geometry(new Options { ComputeReferences = true, View = v }));
                        while (st2.Count > 0)
                        {
                            var ge = st2.Pop(); if (ge == null) continue;
                            foreach (var g in ge)
                            {
                                if (g is GeometryInstance gi) { st2.Push(gi.GetInstanceGeometry()); continue; }
                                if (!(g is Solid s)) continue;
                                foreach (Face f in s.Faces)
                                {
                                    var hp = f as PlanarFace; if (hp == null || hp.Reference == null || hp.FaceNormal.Z < 0.999) continue;
                                    var pr = hp.Project(new XYZ(probe.X, probe.Y, hp.Origin.Z));
                                    if (pr != null && pr.Distance < 1e-4 && hp.Origin.Z > topZ) { pf = hp; topZ = hp.Origin.Z; }
                                }
                            }
                        }
                        string via = "view face";
                        if (pf == null) { pf = TopFace(e, out topZ); via = "3D top face"; }
                        if (pf == null) { errors.Add(i + ": no horizontal top face on " + e.Id.IntegerValue); continue; }
                        var pt = P(r, u, topZ);
                        var sd = doc.Create.NewSpotElevation(v, pf.Reference, pt, pt, pt, pt, false);
                        sd.ChangeTypeId(st.Id);
                        created.Add(sd.Id.IntegerValue);
                        done.Add(new { Item = i, Kind = "spot", Id = sd.Id.IntegerValue, On = e.Id.IntegerValue, Via = via, ElevationMm = Math.Round(topZ * MM), Type = typeName });
                    }
                    else if (kind == "roomtag")
                    {
                        var room = e as Autodesk.Revit.DB.Architecture.Room;
                        if (room == null) { errors.Add(i + ": " + e.Id.IntegerValue + " is not a room"); continue; }
                        var rtt = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                            .FirstOrDefault(x => x.Name == typeName && x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RoomTags);
                        if (rtt == null) { errors.Add(i + ": no room tag type '" + typeName + "'"); continue; }
                        var head = P(r, u, z0);
                        bool leader = it.Value<bool?>("leader") ?? false;
                        // created inside the room (leader end when given), then the head is moved
                        var at = leader && it["endRight"] != null ? P((double)it["endRight"], (double)it["endUp"], z0) : head;
                        var rt = doc.Create.NewRoomTag(new LinkElementId(room.Id), new UV(at.X, at.Y), v.Id);
                        if (rt == null) { errors.Add(i + ": room tag not created for " + e.Id.IntegerValue); continue; }
                        rt.ChangeTypeId(rtt.Id);
                        rt.HasLeader = leader;
                        if (leader && it["endRight"] != null) rt.LeaderEnd = P((double)it["endRight"], (double)it["endUp"], z0);
                        rt.TagHeadPosition = head;
                        created.Add(rt.Id.IntegerValue);
                        done.Add(new { Item = i, Kind = "roomtag", Id = rt.Id.IntegerValue, On = e.Id.IntegerValue, Text = room.Number + " " + room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString(), TagText = rt.TagText, Type = typeName });
                    }
                    else if (kind == "stairpath")
                    {
                        var spt = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.StairsPathType)).FirstOrDefault(x => x.Name == typeName);
                        if (spt == null) { errors.Add(i + ": no stair path type '" + typeName + "'"); continue; }
                        var sp = Autodesk.Revit.DB.Architecture.StairsPath.Create(doc, new LinkElementId(e.Id), spt.Id, v.Id);
                        created.Add(sp.Id.IntegerValue);
                        done.Add(new { Item = i, Kind = "stairpath", Id = sp.Id.IntegerValue, On = e.Id.IntegerValue, Type = typeName });
                    }
                    else
                    {
                        var catId = e.Category.Id.IntegerValue;
                        var tagType = new FilteredElementCollector(doc).WhereElementIsElementType().OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                            .FirstOrDefault(x => x.Name == typeName && x.Category != null && x.Category.CategoryType == CategoryType.Annotation);
                        if (tagType == null) { errors.Add(i + ": no tag type '" + typeName + "'"); continue; }
                        bool leader = it.Value<bool?>("leader") ?? false;
                        var head = P(r, u, z0);
                        var tg = IndependentTag.Create(doc, tagType.Id, v.Id, new Reference(e), leader, TagOrientation.Horizontal, head);
                        if (leader && it["endRight"] != null)
                        {
                            tg.LeaderEndCondition = LeaderEndCondition.Free;
                            tg.SetLeaderEnd(new Reference(e), P((double)it["endRight"], (double)it["endUp"], z0));
                        }
                        tg.TagHeadPosition = head;
                        if (leader && it["endRight"] != null)
                        {
                            // leaders are orthogonal (user rule): straight H / V, else one elbow
                            double er = (double)it["endRight"], eu = (double)it["endUp"];
                            bool alignedR = Math.Abs(er - r) < 1, alignedU = Math.Abs(eu - u) < 1;
                            if (!alignedR && !alignedU)
                            {
                                XYZ elb;
                                if (it["elbowRight"] != null) elb = P((double)it["elbowRight"], (double)it["elbowUp"], z0);
                                else if (((string)it["elbowFirst"] ?? "V").ToUpper() == "H") elb = P(er, u, z0);
                                else elb = P(r, eu, z0);
                                tg.SetLeaderElbow(new Reference(e), elb);
                            }
                        }
                        created.Add(tg.Id.IntegerValue);
                        done.Add(new { Item = i, Kind = "tag", Id = tg.Id.IntegerValue, On = e.Id.IntegerValue, Text = tg.TagText, Type = typeName });
                    }
                    sub.Commit();
                }
                catch (Exception ex)
                {
                    // a failed item leaves nothing behind (no orphan tag / spot)
                    if (created.Count > before) created.RemoveRange(before, created.Count - before);
                    errors.Add(i + ": " + ex.Message);
                }
                finally { if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack(); sub.Dispose(); }
            }
            if (mode == "apply") { t.Commit(); File.WriteAllText(logPath, JsonConvert.SerializeObject(created)); }
            else t.RollBack();
        }
        return new { View = v.Name, Mode = mode, Done = done, Errors = errors };
    }
}
