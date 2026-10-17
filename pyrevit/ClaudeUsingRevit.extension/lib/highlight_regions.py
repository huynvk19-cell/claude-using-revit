# -*- coding: utf-8 -*-
"""Red see-through filled regions around elements, and their removal.

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

TYPE_NAME = "Highlight - Red 50%"
SCHEMA_GUID = Guid("9b7be38d-ea96-4e61-b8df-dc0843a30083")
SCHEMA_NAME = "HighlightRegionMark"
FIELD_USER = "CreatedBy"
RED = Color(255, 0, 0)
TRANSPARENCY = 50          # percent
OFFSET_PAPER_MM = 2.5      # gap between the object and the region, on paper
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


def highlight(doc, elems, active_view):
    """Create one region per element in its owner view. Call inside a transaction.

    Returns (created_count, {view name: count}, [skip messages]).
    """
    user = doc.Application.Username
    rtype = get_region_type(doc)
    schema = get_schema(create=True)
    min_len = doc.Application.ShortCurveTolerance * 2
    ogs = OverrideGraphicSettings()
    ogs.SetSurfaceTransparency(TRANSPARENCY)
    ogs.SetProjectionLineColor(RED)
    created, per_view, skipped = 0, {}, []
    for e in elems:
        if isinstance(e, FilledRegion) and marked_by(e, schema):
            continue  # already a highlight
        view = owner_view(doc, e, active_view)
        if view is None or view.ViewType in NO_REGION_VIEWS or view.IsTemplate:
            skipped.append(u"{0}: view does not take filled regions".format(e.Id))
            continue
        offset = OFFSET_PAPER_MM / 304.8 * max(view.Scale, 1)
        pts = element_points(e, view)
        if not pts:
            skipped.append(u"{0}: no geometry in the view".format(e.Id))
            continue
        loop = loop_from_2d(outline_2d(pts, view, offset), view, min_len)
        if loop is None:
            skipped.append(u"{0}: outline too small".format(e.Id))
            continue
        try:
            loops = List[CurveLoop]()
            loops.Add(loop)
            region = FilledRegion.Create(doc, rtype.Id, view.Id, loops)
        except Exception as ex:
            skipped.append(u"{0}: {1}".format(e.Id, ex))
            continue
        mark(region, user)
        view.SetElementOverrides(region.Id, ogs)
        created += 1
        per_view[view.Name] = per_view.get(view.Name, 0) + 1
    return created, per_view, skipped


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
