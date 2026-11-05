# -*- coding: utf-8 -*-
"""Orange see-through filled regions over elements that miss their tag, and their removal.

Checked per view (only what the view shows, host model only):
  Room    - no room tag of the room in the view;
  Door    - no tag of the door (material and keynote tags do not count);
  Window  - same as Door;
  Wall    - no tag on the wall, nor on a wall of the same type joined in line
            with it (one tag per wall run, rule WT2);
  Floor   - no spot elevation on the floor in the view.
A dependent view also counts the tags of its primary view.

Every region is marked like highlight_regions does it, with its own type and
schema, so Remove never touches the plain Highlight regions or anything else:
  - type TYPE_NAME (solid orange fill, created on first use);
  - extensible-storage entity (SCHEMA_GUID): Revit user name + element kind.
"""
from System import Guid, String
from System.Collections.Generic import List

from Autodesk.Revit.DB import (
    BuiltInCategory, BuiltInParameter, Color, CurveLoop, Element, ElementId,
    FamilyInstance, FilledRegion, FilledRegionType, FilteredElementCollector,
    HostObjectUtils, IndependentTag, Line, OverrideGraphicSettings,
    SpatialElementBoundaryOptions, Transaction, TransactionGroup, View,
    ViewSheet, ViewType, WorksharingUtils, CheckoutStatus)
from Autodesk.Revit.DB.ExtensibleStorage import (
    AccessLevel, Entity, Schema, SchemaBuilder)

import highlight_regions as hr

TYPE_NAME = "Highlight - Untagged 50%"
SCHEMA_GUID = Guid("4f0b6c2e-8d1a-4c57-9e63-2a7d5b9f1c08")
SCHEMA_NAME = "UntaggedRegionMark"
FIELD_USER = "CreatedBy"
FIELD_KIND = "Kind"
ORANGE = Color(255, 128, 0)
TRANSPARENCY = 50

KINDS = ("Room", "Door", "Window", "Wall", "Floor")
LABELS = {"Room": u"Room", "Door": u"Cửa đi", "Window": u"Cửa sổ",
          "Wall": u"Tường", "Floor": u"Sàn (cao độ)"}
CATS = {"Room": BuiltInCategory.OST_Rooms, "Door": BuiltInCategory.OST_Doors,
        "Window": BuiltInCategory.OST_Windows, "Wall": BuiltInCategory.OST_Walls,
        "Floor": BuiltInCategory.OST_Floors}

CHECK_VIEWS = (ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.AreaPlan,
               ViewType.EngineeringPlan, ViewType.Section, ViewType.Elevation,
               ViewType.Detail)
JOIN_TOL = 0.05     # ft, wall ends closer than this are joined (~15 mm)
LINE_TOL = 0.01     # ft, off-line distance still counted collinear (~3 mm)


# ---- marker -------------------------------------------------------------

def get_schema(create=False):
    schema = Schema.Lookup(SCHEMA_GUID)
    if schema is None and create:
        sb = SchemaBuilder(SCHEMA_GUID)
        sb.SetSchemaName(SCHEMA_NAME)
        sb.SetReadAccessLevel(AccessLevel.Public)
        sb.SetWriteAccessLevel(AccessLevel.Public)
        sb.SetDocumentation("Marks untagged-element regions so they can be removed safely.")
        sb.AddSimpleField(FIELD_USER, String)
        sb.AddSimpleField(FIELD_KIND, String)
        schema = sb.Finish()
    return schema


def _mark(region, schema, user, kind):
    ent = Entity(schema)
    ent.Set[String](FIELD_USER, user)
    ent.Set[String](FIELD_KIND, kind)
    region.SetEntity(ent)


def _marked_by(region, schema):
    ent = region.GetEntity(schema)
    if ent is None or not ent.IsValid():
        return None
    return ent.Get[String](FIELD_USER)


def marked_kind(region):
    ent = region.GetEntity(get_schema())
    return ent.Get[String](FIELD_KIND) if ent is not None and ent.IsValid() else None


def find_marked(doc, view=None, user=None):
    """Untagged regions in the view (or the whole model), optionally by creator."""
    schema = get_schema()
    if schema is None:
        return []
    col = (FilteredElementCollector(doc, view.Id) if view is not None
           else FilteredElementCollector(doc))
    found = []
    for r in col.OfClass(FilledRegion):
        who = _marked_by(r, schema)
        if who is None or (user is not None and who != user):
            continue
        if doc.IsWorkshared and WorksharingUtils.GetCheckoutStatus(
                doc, r.Id) == CheckoutStatus.OwnedByOtherUser:
            continue
        found.append(r)
    return found


def get_region_type(doc):
    types = list(FilteredElementCollector(doc).OfClass(FilledRegionType))
    for t in types:
        if Element.Name.__get__(t) == TYPE_NAME:
            return t
    if not types:
        raise Exception("No Filled Region type in this model to start from.")
    solid = hr.solid_fill_id(doc)
    if solid is None:
        raise Exception("No solid fill pattern in this model.")
    new = types[0].Duplicate(TYPE_NAME)
    new.ForegroundPatternId = solid
    new.ForegroundPatternColor = ORANGE
    new.BackgroundPatternId = ElementId.InvalidElementId
    new.IsMasking = False
    new.LineWeight = 1
    return new


# ---- which views --------------------------------------------------------

def views_from_ids(doc, ids):
    """Views to check from selected ids: views, sheets (their placed views), viewports."""
    out, seen = [], set()

    def add(v):
        if (v is not None and isinstance(v, View) and not v.IsTemplate
                and v.ViewType in CHECK_VIEWS and v.Id.IntegerValue not in seen):
            seen.add(v.Id.IntegerValue)
            out.append(v)

    for i in ids:
        e = doc.GetElement(i)
        if isinstance(e, ViewSheet):
            for vid in e.GetAllPlacedViews():
                add(doc.GetElement(vid))
        elif isinstance(e, View):
            add(e)
        elif e is not None and e.Category is not None and \
                e.Category.Id.IntegerValue == int(BuiltInCategory.OST_Viewports):
            add(doc.GetElement(e.ViewId))
    return out


def is_checkable(view):
    return isinstance(view, View) and not view.IsTemplate and view.ViewType in CHECK_VIEWS


# ---- what is tagged -----------------------------------------------------

def _anno_views(doc, view):
    ids = [view.Id]
    try:
        pid = view.GetPrimaryViewId()
        if pid != ElementId.InvalidElementId:
            ids.append(pid)
    except Exception:
        pass
    return ids


def _tagged_ids(doc, view):
    """(room ids with a room tag, element ids with a category tag, floor ids with a spot)."""
    rooms, elems, floors = set(), set(), set()
    keynote = int(BuiltInCategory.OST_KeynoteTags)
    for vid in _anno_views(doc, view):
        for t in FilteredElementCollector(doc, vid).OfCategory(
                BuiltInCategory.OST_RoomTags).WhereElementIsNotElementType():
            try:
                rid = t.TaggedLocalRoomId
                if rid != ElementId.InvalidElementId:
                    rooms.add(rid.IntegerValue)
            except Exception:
                pass
        for t in FilteredElementCollector(doc, vid).OfClass(IndependentTag):
            if t.IsMaterialTag or (t.Category is not None and
                                   t.Category.Id.IntegerValue == keynote):
                continue
            try:
                ids = list(t.GetTaggedLocalElementIds())
            except Exception:
                ids = [t.TaggedLocalElementId]
            for i in ids:
                if i != ElementId.InvalidElementId:
                    elems.add(i.IntegerValue)
        for s in FilteredElementCollector(doc, vid).OfCategory(
                BuiltInCategory.OST_SpotElevations).WhereElementIsNotElementType():
            try:
                for r in s.References:
                    if r.ElementId != ElementId.InvalidElementId:
                        floors.add(r.ElementId.IntegerValue)
            except Exception:
                pass
    return rooms, elems, floors


# ---- what the view shows ------------------------------------------------

def _is_plan(view):
    return abs(view.ViewDirection.Z) > 0.99


def _in_crop(view, p):
    if not view.CropBoxActive:
        return True
    cb = view.CropBox
    q = cb.Transform.Inverse.OfPoint(p)
    return cb.Min.X <= q.X <= cb.Max.X and cb.Min.Y <= q.Y <= cb.Max.Y


def _rooms_in(doc, view):
    col = [r for r in FilteredElementCollector(doc, view.Id).OfCategory(
        BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()]
    if not col and _is_plan(view) and view.GenLevel is not None:
        # Rooms category hidden in the view: the rooms of its level, phase and crop
        vph = view.get_Parameter(BuiltInParameter.VIEW_PHASE)
        vph = vph.AsElementId() if vph is not None else None
        for r in FilteredElementCollector(doc).OfCategory(
                BuiltInCategory.OST_Rooms).WhereElementIsNotElementType():
            if r.LevelId != view.GenLevel.Id or r.Location is None:
                continue
            rph = r.get_Parameter(BuiltInParameter.ROOM_PHASE)
            if vph is not None and rph is not None and rph.AsElementId() != vph:
                continue
            if _in_crop(view, r.Location.Point):
                col.append(r)
    return [r for r in col if r.Area > 0]


def _elems_in(doc, view, kind):
    out = []
    for e in FilteredElementCollector(doc, view.Id).OfCategory(
            CATS[kind]).WhereElementIsNotElementType():
        if isinstance(e, FamilyInstance) and e.SuperComponent is not None:
            continue  # nested in another family
        out.append(e)
    return out


def _wall_runs(walls):
    """Union-find of walls into runs: same type, straight, in line, joined end to end."""
    parent = dict((w.Id.IntegerValue, w.Id.IntegerValue) for w in walls)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    lines = []
    for w in walls:
        c = getattr(w.Location, "Curve", None)
        if isinstance(c, Line):
            lines.append((w, c))
    by_type = {}
    for w, c in lines:
        by_type.setdefault(w.GetTypeId().IntegerValue, []).append((w, c))
    for group in by_type.values():
        for i in range(len(group)):
            wa, ca = group[i]
            da, pa0, pa1 = ca.Direction, ca.GetEndPoint(0), ca.GetEndPoint(1)
            for j in range(i + 1, len(group)):
                wb, cb = group[j]
                if abs(da.CrossProduct(cb.Direction).Z) > 1e-3:
                    continue
                pb0, pb1 = cb.GetEndPoint(0), cb.GetEndPoint(1)
                off = pb0.Subtract(pa0)
                if abs(off.X * da.Y - off.Y * da.X) > LINE_TOL:
                    continue
                if min(pa0.DistanceTo(pb0), pa0.DistanceTo(pb1),
                       pa1.DistanceTo(pb0), pa1.DistanceTo(pb1)) > JOIN_TOL:
                    continue
                parent[find(wa.Id.IntegerValue)] = find(wb.Id.IntegerValue)
    return find


def untagged_in_view(doc, view, kinds):
    """{kind: [elements]} of what the view shows without its tag."""
    rooms_t, elems_t, floors_t = _tagged_ids(doc, view)
    res = {}
    for kind in kinds:
        if kind == "Room":
            res[kind] = [r for r in _rooms_in(doc, view)
                         if r.Id.IntegerValue not in rooms_t]
        elif kind == "Floor":
            res[kind] = [f for f in _elems_in(doc, view, kind)
                         if f.Id.IntegerValue not in floors_t]
        elif kind == "Wall":
            walls = _elems_in(doc, view, kind)
            find = _wall_runs(walls)
            tagged_runs = set(find(w.Id.IntegerValue) for w in walls
                              if w.Id.IntegerValue in elems_t)
            res[kind] = [w for w in walls
                         if find(w.Id.IntegerValue) not in tagged_runs]
        else:
            res[kind] = [e for e in _elems_in(doc, view, kind)
                         if e.Id.IntegerValue not in elems_t]
    return res


# ---- region outlines ----------------------------------------------------

def _flat(view, pts):
    o, r, u = view.Origin, view.RightDirection, view.UpDirection
    return [(p.Subtract(o).DotProduct(r), p.Subtract(o).DotProduct(u)) for p in pts]


def _area(poly):
    a = 0.0
    for i in range(len(poly)):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % len(poly)]
        a += x1 * y2 - x2 * y1
    return abs(a) / 2


def _curves_poly(view, curves):
    pts = []
    for c in curves:
        t = list(c.Tessellate())
        pts.extend(t[:-1])
    return _flat(view, pts)


def _room_polys(room, view):
    opts = SpatialElementBoundaryOptions()
    polys = []
    for loop in room.GetBoundarySegments(opts) or []:
        poly = _curves_poly(view, [s.GetCurve() for s in loop])
        if len(poly) >= 3:
            polys.append(poly)
    return polys


def _floor_face_polys(floor, view):
    """One list of polygons (outer + holes) per top face of the floor."""
    faces = []
    for ref in HostObjectUtils.GetTopFaces(floor):
        face = floor.GetGeometryObjectFromReference(ref)
        if face is None:
            continue
        polys = [_curves_poly(view, list(cl)) for cl in face.GetEdgesAsCurveLoops()]
        polys = [p for p in polys if len(p) >= 3]
        if polys:
            faces.append(polys)
    return faces


def _outline_sets(doc, view, elem, kind, offset):
    """Candidate polygon sets for the region, best first."""
    sets = []
    if _is_plan(view):
        try:
            if kind == "Room":
                polys = _room_polys(elem, view)
                if polys:
                    sets.append([polys])
            elif kind == "Floor":
                faces = _floor_face_polys(elem, view)
                if faces:
                    sets.append(faces)
        except Exception:
            pass
    pts = hr.element_points(elem, view)
    if pts:
        sets.append([[hr.outline_2d(pts, view, offset)]])
    return sets


def _make_region(doc, view, rtype_id, polys, min_len):
    """One filled region from polygons (outer + holes); falls back to the largest alone."""
    tries = [polys]
    if len(polys) > 1:
        tries.append([max(polys, key=_area)])
    for ps in tries:
        loops = List[CurveLoop]()
        for p in ps:
            loop = hr.loop_from_2d(p, view, min_len)
            if loop is not None:
                loops.Add(loop)
        if loops.Count == 0:
            continue
        try:
            return FilledRegion.Create(doc, rtype_id, view.Id, loops)
        except Exception:
            continue
    return None


# ---- commands -----------------------------------------------------------

def view_key(view):
    """Dependent views share annotations with their primary: same key for all of them."""
    try:
        pid = view.GetPrimaryViewId()
        if pid != ElementId.InvalidElementId:
            return pid.IntegerValue
    except Exception:
        pass
    return view.Id.IntegerValue


def scan(doc, views, kinds=KINDS, cache=None):
    """{view id: {kind: [elements]}} for the views; reuses (and fills) `cache`."""
    cache = {} if cache is None else cache
    for v in views:
        if v.Id.IntegerValue not in cache:
            cache[v.Id.IntegerValue] = untagged_in_view(doc, v, kinds)
    return cache


def unique_counts(views, found, kinds):
    """{kind: count} over the views, an element seen in two dependents of one primary once."""
    seen = dict((k, set()) for k in kinds)
    for v in views:
        key = view_key(v)
        for k in kinds:
            for e in found.get(v.Id.IntegerValue, {}).get(k, []):
                seen[k].add((key, e.Id.IntegerValue))
    return dict((k, len(s)) for k, s in seen.items())


def marked_count(doc, views):
    """How many untagged regions of the current user the views show."""
    user = doc.Application.Username
    ids = set()
    for v in views:
        for r in find_marked(doc, v, user):
            ids.add(r.Id.IntegerValue)
    return len(ids)


def highlight_views(doc, views, kinds, found=None):
    """Replace the current user's untagged regions in each view with fresh ones.

    One transaction per view (a view that fails is rolled back alone).
    `found` = scan() result to reuse. Returns ([(view, {kind: count})], [problems]).
    """
    user = doc.Application.Username
    min_len = doc.Application.ShortCurveTolerance * 2
    ogs = OverrideGraphicSettings()
    ogs.SetSurfaceTransparency(TRANSPARENCY)
    ogs.SetProjectionLineColor(ORANGE)
    report, problems, done = [], [], set()
    tg = TransactionGroup(doc, "Highlight untagged")
    tg.Start()
    try:
        t = Transaction(doc, "Untagged region type")
        t.Start()
        rtype = get_region_type(doc)
        schema = get_schema(create=True)
        t.Commit()
        for view in views:
            key = view_key(view)
            offset = hr.OFFSET_PAPER_MM / 304.8 * max(view.Scale, 1)
            counts = dict((k, 0) for k in kinds)
            t = Transaction(doc, "Highlight untagged")
            t.Start()
            try:
                old = [r.Id for r in find_marked(doc, view, user)
                       if marked_kind(r) in kinds]
                if old:
                    doc.Delete(List[ElementId](old))
                fv = (found or {}).get(view.Id.IntegerValue)
                if fv is None:
                    fv = untagged_in_view(doc, view, kinds)
                for kind in kinds:
                    for e in fv[kind]:
                        if (key, e.Id.IntegerValue) in done:
                            continue
                        made = 0
                        for polyset in _outline_sets(doc, view, e, kind, offset):
                            regions = [_make_region(doc, view, rtype.Id, ps, min_len)
                                       for ps in polyset]
                            regions = [r for r in regions if r is not None]
                            for r in regions:
                                _mark(r, schema, user, kind)
                                view.SetElementOverrides(r.Id, ogs)
                            made = len(regions)
                            if made:
                                break
                        if made:
                            done.add((key, e.Id.IntegerValue))
                            counts[kind] += 1
                        else:
                            problems.append(u"{0}: {1} {2} không vẽ được vùng tô".format(
                                view.Name, LABELS[kind], e.Id))
                t.Commit()
                report.append((view, counts))
                if any(counts.values()) and view.GetCategoryHidden(
                        ElementId(BuiltInCategory.OST_DetailComponents)):
                    problems.append(u"{0}: Detail Items đang tắt trong view, vùng tô "
                                    u"không hiện".format(view.Name))
            except Exception as ex:
                if t.HasStarted() and not t.HasEnded():
                    t.RollBack()
                problems.append(u"{0}: {1}".format(view.Name, ex))
        tg.Assimilate()
    except Exception:
        if tg.HasStarted() and not tg.HasEnded():
            tg.RollBack()
        raise
    return report, problems


def remove(doc, views=None):
    """Delete the current user's untagged regions (in the views, or the whole model)."""
    user = doc.Application.Username
    ids = set()
    for v in (views or [None]):
        for r in find_marked(doc, v, user):
            ids.add(r.Id.IntegerValue)
    if not ids:
        return 0
    t = Transaction(doc, "Remove untagged highlight")
    t.Start()
    try:
        doc.Delete(List[ElementId]([ElementId(i) for i in ids]))
        t.Commit()
    except Exception:
        t.RollBack()
        raise
    return len(ids)
