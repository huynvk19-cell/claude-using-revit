# -*- coding: utf-8 -*-
"""Red see-through filled region around elements, and its removal.

One region per view holds every highlighted element: outlines close to each
other flow together like water drops (region_merge), so thousands of elements
cost one sketch and one regeneration, not one each.

Every region this module creates is marked twice, so the clear command can
never remove anything else:
  - its type is TYPE_NAME (solid red fill, created on first use);
  - it carries an extensible-storage entity (SCHEMA_GUID) holding the Revit
    user name of whoever created it.
Clear removes only regions that have the entity AND were created by the
current Revit user. Names written into the model stay neutral.
"""
from System import Guid, String
from System.Collections.Generic import List

from Autodesk.Revit.DB import (
    Color, CurveLoop, Element, ElementId, FilledRegion,
    FilledRegionType, FillPatternElement, FillPatternTarget,
    FilteredElementCollector, Line, Options, OverrideGraphicSettings,
    RevitLinkInstance, ImportInstance, ViewType, WorksharingUtils,
    CheckoutStatus, XYZ, Solid, Curve, Mesh, Point, PolyLine,
    GeometryInstance)
from Autodesk.Revit.DB.ExtensibleStorage import (
    AccessLevel, Entity, Schema, SchemaBuilder)

import region_merge as rm

TYPE_NAME = "Highlight - Red 50%"
SCHEMA_GUID = Guid("9b7be38d-ea96-4e61-b8df-dc0843a30083")
SCHEMA_NAME = "HighlightRegionMark"
FIELD_USER = "CreatedBy"
RED = Color(255, 0, 0)
TRANSPARENCY = 50          # percent
OFFSET_PAPER_MM = 2.5      # gap between the object and the region, on paper
BLEND_PAPER_MM = 2.5       # regions closer than twice this flow into each other
SEED_PAPER_MM = 0.2        # an element's own outline is never thinner than this
MAX_POINTS = 20000

NO_REGION_VIEWS = (ViewType.ThreeD, ViewType.Schedule, ViewType.ColumnSchedule,
                   ViewType.PanelSchedule, ViewType.Report, ViewType.Walkthrough,
                   ViewType.Rendering, ViewType.ProjectBrowser,
                   ViewType.SystemBrowser, ViewType.Internal, ViewType.Undefined)


# ---- marker -------------------------------------------------------------

def get_schema(create=False):
    schema = Schema.Lookup(SCHEMA_GUID)
    if schema is None and create:
        sb = SchemaBuilder(SCHEMA_GUID)
        sb.SetSchemaName(SCHEMA_NAME)
        sb.SetReadAccessLevel(AccessLevel.Public)
        sb.SetWriteAccessLevel(AccessLevel.Public)
        sb.SetDocumentation("Marks highlight regions so they can be cleared safely.")
        sb.AddSimpleField(FIELD_USER, String)
        schema = sb.Finish()
    return schema


def mark(region, user):
    ent = Entity(get_schema(create=True))
    ent.Set[String](FIELD_USER, user)
    region.SetEntity(ent)


def marked_by(region, schema):
    """Creator user name stored on the region, or None when not ours."""
    if schema is None:
        return None
    ent = region.GetEntity(schema)
    if ent is None or not ent.IsValid():
        return None
    return ent.Get[String](FIELD_USER)


# ---- type ---------------------------------------------------------------

def solid_fill_id(doc):
    for fp in FilteredElementCollector(doc).OfClass(FillPatternElement):
        pat = fp.GetFillPattern()
        if pat.IsSolidFill and pat.Target == FillPatternTarget.Drafting:
            return fp.Id
    for fp in FilteredElementCollector(doc).OfClass(FillPatternElement):
        if fp.GetFillPattern().IsSolidFill:
            return fp.Id
    return None


def get_region_type(doc):
    """The highlight FilledRegionType; created on first use (needs a transaction)."""
    types = list(FilteredElementCollector(doc).OfClass(FilledRegionType))
    for t in types:
        if Element.Name.__get__(t) == TYPE_NAME:  # .Name fails on types in IronPython
            return t
    if not types:
        raise Exception("No Filled Region type in this model to start from.")
    new = types[0].Duplicate(TYPE_NAME)
    solid = solid_fill_id(doc)
    if solid is None:
        raise Exception("No solid fill pattern in this model.")
    new.ForegroundPatternId = solid
    new.ForegroundPatternColor = RED
    new.BackgroundPatternId = ElementId.InvalidElementId
    new.IsMasking = False
    new.LineWeight = 1
    return new


# ---- outline ------------------------------------------------------------

def _collect(geom, pts):
    if geom is None:
        return
    for g in geom:
        if len(pts) > MAX_POINTS:
            return
        if isinstance(g, GeometryInstance):
            _collect(g.GetInstanceGeometry(), pts)
        elif isinstance(g, Solid):
            if g.Volume == 0 and g.Faces.Size == 0:
                continue
            for e in g.Edges:
                pts.extend(e.Tessellate())
        elif isinstance(g, Curve):
            pts.extend(g.Tessellate())
        elif isinstance(g, Mesh):
            pts.extend(g.Vertices)
        elif isinstance(g, PolyLine):
            pts.extend(g.GetCoordinates())
        elif isinstance(g, Point):
            pts.append(g.Coord)


def _bbox_points(elem, view):
    bb = elem.get_BoundingBox(view)
    if bb is None:
        bb = elem.get_BoundingBox(None)
    if bb is None:
        return []
    t, a, b = bb.Transform, bb.Min, bb.Max
    return [t.OfPoint(XYZ(x, y, z))
            for x in (a.X, b.X) for y in (a.Y, b.Y) for z in (a.Z, b.Z)]


def element_points(elem, view):
    """3D points describing what the element draws in the view."""
    pts = []
    if not isinstance(elem, (RevitLinkInstance, ImportInstance)):
        try:
            opts = Options()
            opts.View = view
            _collect(elem.get_Geometry(opts), pts)
        except Exception:
            pts = []
    if len(pts) < 2:
        pts = _bbox_points(elem, view)
    return pts


def linked_points(elem, transform):
    """3D points of an element of a linked model, in the host's coordinates."""
    pts = []
    try:
        _collect(elem.get_Geometry(Options()), pts)
    except Exception:
        pts = []
    if len(pts) < 2:
        pts = _bbox_points(elem, None)
    return [transform.OfPoint(p) for p in pts]


def _cross(o, a, b):
    return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])


def convex_hull(points):
    pts = sorted(set(points))
    if len(pts) < 3:
        return pts
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and _cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and _cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def outline_2d(pts3d, view, offset):
    """Convex outline (view coordinates, feet) grown by `offset` all round."""
    o, r, u = view.Origin, view.RightDirection, view.UpDirection
    flat = []
    for p in pts3d:
        d = p.Subtract(o)
        flat.append((round(d.DotProduct(r), 4), round(d.DotProduct(u), 4)))
    hull = convex_hull(flat)
    # grow: an octagon of points round every hull corner, then hull again
    k = 0.7071 * offset
    ring = [(offset, 0), (k, k), (0, offset), (-k, k),
            (-offset, 0), (-k, -k), (0, -offset), (k, -k)]
    grown = [(x + dx, y + dy) for (x, y) in hull for (dx, dy) in ring]
    return convex_hull(grown)


def loop_from_2d(poly, view, min_len):
    o, r, u = view.Origin, view.RightDirection, view.UpDirection
    pts = [o.Add(r.Multiply(x)).Add(u.Multiply(y)) for (x, y) in poly]
    clean = []
    for p in pts:
        if not clean or p.DistanceTo(clean[-1]) > min_len:
            clean.append(p)
    while len(clean) > 3 and clean[0].DistanceTo(clean[-1]) <= min_len:
        clean.pop()
    if len(clean) < 3:
        return None
    loop = CurveLoop()
    for i in range(len(clean)):
        loop.Append(Line.CreateBound(clean[i], clean[(i + 1) % len(clean)]))
    return loop


# ---- commands -----------------------------------------------------------

def owner_view(doc, elem, active_view):
    if elem.ViewSpecific and elem.OwnerViewId != ElementId.InvalidElementId:
        return doc.GetElement(elem.OwnerViewId)
    return active_view


def region_polys(region, view):
    """The loops of a filled region as 2D polygons in the view's coordinates."""
    o, r, u = view.Origin, view.RightDirection, view.UpDirection
    polys = []
    for loop in region.GetBoundaries():
        poly = []
        for c in loop:
            for p in list(c.Tessellate())[:-1]:
                d = p.Subtract(o)
                poly.append((d.DotProduct(r), d.DotProduct(u)))
        if len(poly) >= 3:
            polys.append(poly)
    return polys


def create_merged(doc, view, rtype_id, shapes, min_len, keep=None):
    """ONE filled region for all the shapes of a view, merged like water drops
    (region_merge). shapes = [[outer, hole, ...]] per element, 2D view feet;
    keep = loops of a region to fold in unchanged.

    Returns [region]; several only when Revit refuses the loops as one sketch
    (then one region per separate patch), [] when nothing could be drawn.
    """
    polys = rm.merge(shapes, OFFSET_PAPER_MM, BLEND_PAPER_MM, max(view.Scale, 1) / 304.8, keep)

    def make(ps):
        loops = List[CurveLoop]()
        for p in ps:
            loop = loop_from_2d(p, view, min_len)
            if loop is not None:
                loops.Add(loop)
        if loops.Count == 0:
            return None
        return FilledRegion.Create(doc, rtype_id, view.Id, loops)

    try:
        region = make(polys)
        return [region] if region is not None else []
    except Exception:
        pass
    out = []
    for group in rm.groups(polys):
        region = None
        for ps in (group, group[:1]):
            try:
                region = make(ps)
                break
            except Exception:
                continue
        if region is not None:
            out.append(region)
    return out


def highlight(doc, elems, active_view, linked=()):
    """One region per view over all the elements (each in its owner view), merged
    with the highlight you already have there; linked = [(element of a linked model,
    link transform)] in the active view. Call inside a transaction.

    Returns (highlighted element count, {view name: count}, [skip messages]).
    """
    user = doc.Application.Username
    rtype = get_region_type(doc)
    schema = get_schema(create=True)
    min_len = doc.Application.ShortCurveTolerance * 2
    ogs = OverrideGraphicSettings()
    ogs.SetSurfaceTransparency(TRANSPARENCY)
    ogs.SetProjectionLineColor(RED)
    per_view, skipped = {}, []
    jobs, order = {}, []      # view id -> (view, shapes)
    for e, link in [(x, None) for x in elems] + list(linked):
        if isinstance(e, FilledRegion) and marked_by(e, schema):
            continue  # already a highlight
        view = active_view if link is not None else owner_view(doc, e, active_view)
        if view is None or view.ViewType in NO_REGION_VIEWS or view.IsTemplate:
            skipped.append(u"{0}: view does not take filled regions".format(e.Id))
            continue
        pts = linked_points(e, link) if link is not None else element_points(e, view)
        if not pts:
            skipped.append(u"{0}: no geometry in the view".format(e.Id))
            continue
        poly = outline_2d(pts, view, SEED_PAPER_MM / 304.8 * max(view.Scale, 1))
        if len(poly) < 3:
            skipped.append(u"{0}: outline too small".format(e.Id))
            continue
        vid = view.Id.IntegerValue
        if vid not in jobs:
            jobs[vid] = (view, [])
            order.append(vid)
        jobs[vid][1].append([poly])
    for vid in order:
        view, shapes = jobs[vid]
        try:
            old = [r for r in find_marked(doc, view, user) if r.OwnerViewId == view.Id]
            keep = [p for r in old for p in region_polys(r, view)]
            regions = create_merged(doc, view, rtype.Id, shapes, min_len, keep)
        except Exception as ex:
            skipped.append(u"{0}: {1}".format(view.Name, ex))
            continue
        if not regions:
            skipped.append(u"{0}: no region could be drawn".format(view.Name))
            continue
        if old:
            doc.Delete(List[ElementId]([r.Id for r in old]))
        for region in regions:
            mark(region, user)
            view.SetElementOverrides(region.Id, ogs)
        per_view[view.Name] = per_view.get(view.Name, 0) + len(shapes)
    return sum(per_view.values()), per_view, skipped


def find_marked(doc, view=None, user=None):
    """Highlight regions in the view (or the whole model), optionally by creator."""
    schema = get_schema()
    if schema is None:
        return []
    col = (FilteredElementCollector(doc, view.Id) if view is not None
           else FilteredElementCollector(doc))
    found = []
    for r in col.OfClass(FilledRegion):
        who = marked_by(r, schema)
        if who is None or (user is not None and who != user):
            continue
        if doc.IsWorkshared:
            st = WorksharingUtils.GetCheckoutStatus(doc, r.Id)
            if st == CheckoutStatus.OwnedByOtherUser:
                continue
        found.append(r)
    return found
