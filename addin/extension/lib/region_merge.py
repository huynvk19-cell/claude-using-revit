# -*- coding: utf-8 -*-
"""Many 2D outlines -> the loops of ONE filled region, merged like water drops.

A filled region per element costs one sketch and one regeneration each; thousands
of them take minutes. Here every outline of a view goes into one shape:
  1. union of all outlines;
  2. grown by `grow + blend` with round corners;
  3. shrunk back by `blend`: outlines closer than 2 x blend flow into each other
     through a rounded neck, the rest keeps a `grow` margin with round corners.
The polygon work is WPF's (System.Windows.Media.Geometry: native, exact, fast);
the helpers below it are plain Python (addin/tests/region_merge_check.py).

Coordinates: the caller's 2D view coordinates (feet). `mm` = feet per paper mm
(view scale / 304.8), so sizes are given on paper.
"""
UNIT_MM = 0.1        # one WPF unit = 0.1 mm on paper
TOL = 0.5            # WPF units: curve flattening inside the geometry calls
SIMPLIFY_MM = 0.1    # paper: points nearer than this to the outline are dropped
MIN_AREA_MM2 = 1.0   # paper: smaller loops (slivers) are dropped


# ---- plain Python ----------------------------------------------------------

def signed_area(poly):
    a = 0.0
    for i in range(len(poly)):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % len(poly)]
        a += x1 * y2 - x2 * y1
    return a / 2


def oriented(poly, ccw):
    return poly if (signed_area(poly) >= 0) == ccw else poly[::-1]


def _seg_dist(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    d2 = dx * dx + dy * dy
    if d2 == 0:
        return ((p[0] - a[0]) ** 2 + (p[1] - a[1]) ** 2) ** 0.5
    t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / d2))
    return ((p[0] - a[0] - t * dx) ** 2 + (p[1] - a[1] - t * dy) ** 2) ** 0.5


def _chain(pts, eps):
    """Douglas-Peucker on an open chain, without recursion."""
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        i, j = stack.pop()
        far, at = eps, -1
        for n in range(i + 1, j):
            d = _seg_dist(pts[n], pts[i], pts[j])
            if d > far:
                far, at = d, n
        if at >= 0:
            keep[at] = True
            stack.append((i, at))
            stack.append((at, j))
    return [p for p, k in zip(pts, keep) if k]


def simplify(poly, eps):
    """Closed polygon with the points nearer than eps to the outline removed."""
    pts = []
    for p in poly:
        if not pts or p != pts[-1]:
            pts.append(p)
    while len(pts) > 1 and pts[0] == pts[-1]:
        pts.pop()
    if len(pts) < 4:
        return pts
    far = max(range(len(pts)), key=lambda n: (pts[n][0] - pts[0][0]) ** 2 + (pts[n][1] - pts[0][1]) ** 2)
    a = _chain(pts[:far + 1], eps)
    b = _chain(pts[far:] + pts[:1], eps)
    return a[:-1] + b[:-1]


def _inside(p, poly):
    x, y = p
    hit = False
    for i in range(len(poly)):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % len(poly)]
        if (y1 > y) != (y2 > y) and x < x1 + (y - y1) * (x2 - x1) / (y2 - y1):
            hit = not hit
    return hit


def groups(polys):
    """[[outer, hole, ...]] from loops that never cross: each outer with its own holes."""
    items = sorted(((abs(signed_area(p)), p) for p in polys), key=lambda t: -t[0])
    boxes = [(min(x for x, _ in p), min(y for _, y in p), max(x for x, _ in p), max(y for _, y in p))
             for _, p in items]
    out, group_of = [], {}
    for n, (_, p) in enumerate(items):
        x, y = p[0]
        around = [m for m in range(n)
                  if boxes[m][0] <= x <= boxes[m][2] and boxes[m][1] <= y <= boxes[m][3]
                  and _inside(p[0], items[m][1])]
        if len(around) % 2 == 0:
            group_of[n] = len(out)
            out.append([p])
        else:
            out[group_of[around[-1]]].append(p)   # around[-1]: the smallest loop round it
    return out


# ---- WPF -------------------------------------------------------------------

def _media():
    import clr
    clr.AddReference('PresentationCore')
    clr.AddReference('WindowsBase')
    import System.Windows.Media as media
    return media


def _geometry(shapes, k, nonzero):
    """shapes [[polygon, ...]] -> one geometry. nonzero: every shape is outer + holes and
    shapes may overlap (union); else the polygons are the loops of a finished region."""
    from System.Collections.Generic import List
    from System.Windows import Point
    media = _media()
    g = media.StreamGeometry()
    g.FillRule = media.FillRule.Nonzero if nonzero else media.FillRule.EvenOdd
    ctx = g.Open()
    try:
        for polys in shapes:
            outer = max(polys, key=lambda p: abs(signed_area(p)))
            for p in polys:
                if len(p) < 2:
                    continue
                if nonzero:
                    p = oriented(p, p is outer)
                ctx.BeginFigure(Point(p[0][0] * k, p[0][1] * k), True, True)
                rest = List[Point]()
                for x, y in p[1:]:
                    rest.Add(Point(x * k, y * k))
                ctx.PolyLineTo(rest, True, False)
    finally:
        ctx.Close()
    g.Freeze()
    return g


def _band(g, r):
    """The strip of half-width r along the outline of g, round corners."""
    media = _media()
    pen = media.Pen(media.Brushes.Black, 2.0 * r)
    pen.LineJoin = media.PenLineJoin.Round
    return g.GetWidenedPathGeometry(pen, TOL, media.ToleranceType.Absolute)


def _combine(a, b, mode):
    media = _media()
    return media.Geometry.Combine(a, b, getattr(media.GeometryCombineMode, mode), None,
                                  TOL, media.ToleranceType.Absolute)


def merge(shapes, grow_mm, blend_mm, mm, keep=None):
    """Loops (outer ones and holes, never crossing) of the merged shape.

    shapes  [[outer, hole, ...]] per element, 2D feet; they may overlap
    grow_mm margin round every element, on paper
    blend_mm elements whose margins are closer than 2 x blend_mm flow together
    mm      feet per paper mm
    keep    loops of a region already drawn, added as they are
    """
    media = _media()
    k = 1.0 / (mm * UNIT_MM)
    g = _geometry(shapes, k, True)
    grow, blend = grow_mm / UNIT_MM, blend_mm / UNIT_MM
    if grow + blend > 0:
        g = _combine(g, _band(g, grow + blend), "Union")
    if blend > 0:
        g = _combine(g, _band(g, blend), "Exclude")
    if keep:
        g = _combine(g, _geometry([list(keep)], k, False), "Union")
    flat = g.GetFlattenedPathGeometry(TOL, media.ToleranceType.Absolute)
    eps, min_area = SIMPLIFY_MM * mm, MIN_AREA_MM2 * mm * mm
    out = []
    for fig in flat.Figures:
        pts = [fig.StartPoint]
        for seg in fig.Segments:
            if isinstance(seg, media.PolyLineSegment):
                pts.extend(seg.Points)
            elif isinstance(seg, media.LineSegment):
                pts.append(seg.Point)
        poly = simplify([(p.X / k, p.Y / k) for p in pts], eps)
        if len(poly) >= 3 and abs(signed_area(poly)) >= min_area:
            out.append(poly)
    return out
