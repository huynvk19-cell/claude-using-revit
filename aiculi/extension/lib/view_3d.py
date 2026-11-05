# -*- coding: utf-8 -*-
"""A 3D view boxed to what a plan / section / elevation shows.

One 3D view per Revit user (BOX_NAME), reused on every run: its section box is set to
  - plan (floor, ceiling, structural, area, callout): crop region x the view range
    (lowest to highest of top / cut / bottom / view depth planes);
  - section / elevation / detail: crop region x the far clip depth from the view plane.
Phase and phase filter follow the source view. Nothing but that 3D view is changed.
"""
from Autodesk.Revit.DB import (
    BoundingBoxXYZ, BuiltInParameter, ElementId, FilteredElementCollector, Level,
    PlanViewPlane, PlanViewRange, Transaction, Transform, View3D, ViewFamily,
    ViewFamilyType, ViewPlan, ViewSection, ViewType, WorksharingUtils, XYZ)

BOX_NAME = u"3D Box - {0}"     # { } are kept for Revit's own {3D - user} view
MM = 1 / 304.8
NO_RANGE_FT = 20000 * MM         # view range plane set to Unlimited: 20 m past the level
NO_FAR_CLIP_FT = 1000000 * MM    # elevation / section without far clipping: 1 km deep

PLAN_TYPES = (ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan,
              ViewType.AreaPlan)
SECTION_TYPES = (ViewType.Section, ViewType.Elevation, ViewType.Detail)


class View3DError(Exception):
    pass


def source_view(doc, uidoc, view=None):
    """The view to follow: the one given, else a viewport picked on the active sheet,
    else the active view."""
    from Autodesk.Revit.DB import Viewport
    v = view or doc.ActiveView
    if v.ViewType == ViewType.DrawingSheet:
        vps = [doc.GetElement(i) for i in uidoc.Selection.GetElementIds()]
        vps = [p for p in vps if isinstance(p, Viewport)]
        if len(vps) != 1:
            raise View3DError(u"Trên sheet: chọn đúng 1 viewport mặt bằng / mặt cắt / mặt đứng.")
        v = doc.GetElement(vps[0].ViewId)
    if v.ViewType not in PLAN_TYPES + SECTION_TYPES:
        raise View3DError(u"View '{0}' ({1}) không phải mặt bằng / mặt cắt / mặt đứng.".format(
            v.Name, v.ViewType))
    return v


# ---- box ----------------------------------------------------------------------

def _levels(doc):
    return sorted(FilteredElementCollector(doc).OfClass(Level),
                  key=lambda l: l.ProjectElevation)


def _plane_z(doc, view, vr, plane):
    """Height (internal feet) of one view-range plane, None when Unlimited."""
    lid = vr.GetLevelId(plane)
    off = vr.GetOffset(plane)
    gen = view.GenLevel
    if lid == PlanViewRange.Unlimited or lid == ElementId.InvalidElementId:
        return None
    if lid == PlanViewRange.Current:
        lvl = gen
    elif lid == PlanViewRange.LevelAbove or lid == PlanViewRange.LevelBelow:
        up = lid == PlanViewRange.LevelAbove
        z0 = gen.ProjectElevation
        cand = [l for l in _levels(doc) if (l.ProjectElevation > z0 + 1e-6 if up
                                            else l.ProjectElevation < z0 - 1e-6)]
        if not cand:
            return None
        lvl = cand[0] if up else cand[-1]
    else:
        lvl = doc.GetElement(lid)
    if lvl is None:
        return None
    return lvl.ProjectElevation + off


def _plan_box(doc, view, cut=False):
    """cut: stop at the cut plane (floor plans: box top; ceiling plans, which look up:
    box bottom), so the 3D shows the very cut the plan shows."""
    vr = view.GetViewRange()
    zs = [_plane_z(doc, view, vr, p) for p in (PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane,
                                               PlanViewPlane.BottomClipPlane,
                                               PlanViewPlane.ViewDepthPlane)]
    z0 = view.GenLevel.ProjectElevation if view.GenLevel else 0.0
    known = [z for z in zs if z is not None]
    lo = min(known) if known else z0 - NO_RANGE_FT
    hi = max(known) if known else z0 + NO_RANGE_FT
    if zs[0] is None:                      # top Unlimited
        hi = max(hi, z0 + NO_RANGE_FT)
    if zs[3] is None:                      # view depth Unlimited
        lo = min(lo, z0 - NO_RANGE_FT)
    if cut and zs[1] is not None:
        if view.ViewType == ViewType.CeilingPlan:
            lo = zs[1]
        else:
            hi = zs[1]
    crop = view.CropBox
    t = crop.Transform
    pts = [t.OfPoint(XYZ(x, y, 0)) for x in (crop.Min.X, crop.Max.X)
           for y in (crop.Min.Y, crop.Max.Y)]
    pts = [XYZ(p.X, p.Y, z) for p in pts for z in (lo, hi)]
    return _zup_box(pts, t.BasisX), {"bottomMm": round(lo / MM), "topMm": round(hi / MM),
                 "unlimited": [n for n, z in zip(("top", "cut", "bottom", "depth"), zs)
                               if z is None]}


def _far_clip(view):
    """(depth in feet, clipped?) of a section / elevation."""
    on = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING)
    off = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)
    clipped = on is None or on.AsInteger() != 0
    if clipped and off is not None and off.AsDouble() > 1e-6:
        return off.AsDouble(), True
    return NO_FAR_CLIP_FT, False


def _section_box(doc, view):
    # crop box z: 0 = the view plane, negative = into the view (far clip)
    crop = view.CropBox
    t = crop.Transform
    depth, clipped = _far_clip(view)
    near = crop.Max.Z
    pts = [t.OfPoint(XYZ(x, y, z)) for x in (crop.Min.X, crop.Max.X)
           for y in (crop.Min.Y, crop.Max.Y) for z in (near - depth, near)]
    return _zup_box(pts, t.BasisX), {"depthMm": round(depth / MM) if clipped else None,
                                     "farClip": clipped}


def _zup_box(pts, along):
    """Smallest box around model points, turned only about Z (a section box cannot
    tilt) so its x runs along `along` laid flat."""
    bx = XYZ(along.X, along.Y, 0)
    bx = bx.Normalize() if bx.GetLength() > 1e-9 else XYZ.BasisX
    frame = Transform.Identity
    frame.BasisX = bx
    frame.BasisY = XYZ.BasisZ.CrossProduct(bx)
    frame.BasisZ = XYZ.BasisZ
    inv = frame.Inverse
    loc = [inv.OfPoint(p) for p in pts]
    box = BoundingBoxXYZ()
    box.Transform = frame
    box.Min = XYZ(min(p.X for p in loc), min(p.Y for p in loc), min(p.Z for p in loc))
    box.Max = XYZ(max(p.X for p in loc), max(p.Y for p in loc), max(p.Z for p in loc))
    return box


def box_for(doc, view, cut=False):
    if view.ViewType in PLAN_TYPES:
        return _plan_box(doc, view, cut)
    return _section_box(doc, view)


# ---- the 3D view ----------------------------------------------------------------

def box_view_name(doc):
    return BOX_NAME.format(doc.Application.Username)


def find_box_view(doc):
    name = box_view_name(doc)
    for v in FilteredElementCollector(doc).OfClass(View3D):
        if not v.IsTemplate and v.Name == name:
            return v
    return None


def _creator(doc, el):
    if not doc.IsWorkshared:
        return None
    return WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id).Creator


def _orientation(view, box):
    """Isometric look: plans from the south-east of the crop, sections / elevations from
    their own side, a little from the left and above, so the cut face shows."""
    f = box.Transform
    c = f.OfPoint((box.Min + box.Max) * 0.5)
    if view.ViewType in PLAN_TYPES:
        fwd = f.BasisX.Negate() + f.BasisY - XYZ.BasisZ
    else:
        fwd = view.ViewDirection.Negate() * 1.6 + view.RightDirection * 0.7 - XYZ.BasisZ * 0.6
    fwd = fwd.Normalize()
    right = fwd.CrossProduct(XYZ.BasisZ).Normalize()
    up = right.CrossProduct(fwd).Normalize()
    from Autodesk.Revit.DB import ViewOrientation3D
    size = (box.Max - box.Min).GetLength()
    return ViewOrientation3D(c - fwd * size, up, fwd)


def _copy_phase(src, dst):
    for bip in (BuiltInParameter.VIEW_PHASE, BuiltInParameter.VIEW_PHASE_FILTER):
        a, b = src.get_Parameter(bip), dst.get_Parameter(bip)
        if a is not None and b is not None and not b.IsReadOnly and a.AsElementId() != b.AsElementId():
            b.Set(a.AsElementId())


def apply(doc, view, cut=False):
    """Set (create on first use) this user's 3D box view to `view`. Returns (View3D, info)."""
    box, info = box_for(doc, view, cut)
    v3 = find_box_view(doc)
    user = doc.Application.Username
    if v3 is not None:
        who = _creator(doc, v3)
        if who and who != user:
            raise View3DError(u"View '{0}' do {1} tạo, không đụng vào.".format(v3.Name, who))
        if doc.IsWorkshared:
            from Autodesk.Revit.DB import CheckoutStatus
            if WorksharingUtils.GetCheckoutStatus(doc, v3.Id) == CheckoutStatus.OwnedByOtherUser:
                raise View3DError(u"View '{0}' đang bị người khác mượn.".format(v3.Name))
    t = Transaction(doc, "3D section box from view")
    t.Start()
    try:
        created = v3 is None
        if created:
            vft = [x for x in FilteredElementCollector(doc).OfClass(ViewFamilyType)
                   if x.ViewFamily == ViewFamily.ThreeDimensional][0]
            v3 = View3D.CreateIsometric(doc, vft.Id)
            v3.Name = box_view_name(doc)
        if v3.IsPerspective:
            raise View3DError(u"View '{0}' là phối cảnh.".format(v3.Name))
        if v3.CropBoxActive and not v3.IsLocked:
            v3.CropBoxActive = False
        v3.IsSectionBoxActive = True
        v3.SetSectionBox(box)
        if not v3.IsLocked:
            v3.SetOrientation(_orientation(view, box))
        _copy_phase(view, v3)
        t.Commit()
    except Exception:
        t.RollBack()
        raise
    info.update({"created": created, "view3d": v3.Name, "view3dId": v3.Id.IntegerValue,
                 "source": view.Name, "sourceId": view.Id.IntegerValue,
                 "sourceType": u"{0}".format(view.ViewType), "locked": v3.IsLocked})
    return v3, info


def show(uidoc, v3):
    """Open the 3D view and zoom to its section box (outside any transaction)."""
    uidoc.ActiveView = v3
    box = v3.GetSectionBox()
    f = box.Transform
    pts = [f.OfPoint(XYZ(x, y, z)) for x in (box.Min.X, box.Max.X)
           for y in (box.Min.Y, box.Max.Y) for z in (box.Min.Z, box.Max.Z)]
    for uv in uidoc.GetOpenUIViews():
        if uv.ViewId == v3.Id:
            lo = XYZ(min(p.X for p in pts), min(p.Y for p in pts), min(p.Z for p in pts))
            hi = XYZ(max(p.X for p in pts), max(p.Y for p in pts), max(p.Z for p in pts))
            uv.ZoomAndCenterRectangle(lo, hi)
            break
