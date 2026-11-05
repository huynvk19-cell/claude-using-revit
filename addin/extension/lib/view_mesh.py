# -*- coding: utf-8 -*-
"""Triangles of a 3D view, for the web app's GPU viewer.

Revit's CustomExporter walks what the 3D view shows (its section box, hidden categories,
phase, links) and hands over tessellated polymeshes with their render material. They are
written once to %APPDATA%/claude-using-revit/meshes/<name>.bin + <name>.json:
  .json: origin (feet, model coordinates), counts, elements [[id, category, link]],
         categories, byte offsets of the buffers in .bin;
  .bin : positions float32 x3 (metres, relative to origin), colours uint8 x4 (material colour,
         alpha from transparency), element index uint32 per vertex, triangle indices uint32 x3.
The browser then draws it with WebGL on the GPU; Revit is not asked again while you orbit.
Nothing in the model changes.
"""
import array
import io
import json
import os
import re
import time

from Autodesk.Revit.DB import (
    CustomExporter, ElementId, FilteredElementCollector, IExportContext, RenderNodeAction,
    Transform, View3D)

FOLDER = os.path.join(os.environ.get("APPDATA", u""), u"claude-using-revit", u"meshes")
FT = 0.3048
MAX_TRIANGLES = 6000000   # stop early above this (a whole big model would not fit the browser)
KEEP = 6                  # newest exports kept in FOLDER


class MeshError(Exception):
    pass


def target_view(doc, uidoc, view=None):
    """The 3D view to export: the one given, else the active 3D view, else your '3D Box' view."""
    import view_3d as v3d
    v = view or doc.ActiveView
    if isinstance(v, View3D) and not v.IsTemplate:
        return v
    box = v3d.find_box_view(doc)
    if box is not None:
        return box
    raise MeshError(u"Mở một view 3D (hoặc bấm '3D theo view' trước) rồi chạy lại.")


class _Context(IExportContext):
    """Collects polymeshes; one stack of transforms (instances, links) and documents (links)."""

    def __init__(self, doc, origin, limit):
        self.docs = [doc]
        self.xf = [Transform.Identity]
        self.origin = origin
        self.limit = limit
        self.pos = array.array("f")
        self.col = bytearray()
        self.elem = array.array("L" if array.array("L").itemsize == 4 else "I")
        self.idx = array.array("L" if array.array("L").itemsize == 4 else "I")
        self.rgba = b"\xb4\xb4\xb4\xff"
        self.elements, self.el_index = [], -1
        self.categories, self.cat_index = [], {}
        self.vertices, self.triangles = 0, 0
        self.truncated = False

    # -- walk -------------------------------------------------------------------
    def Start(self):
        return True

    def Finish(self):
        pass

    def IsCanceled(self):
        return self.truncated

    def OnViewBegin(self, node):
        return RenderNodeAction.Proceed

    def OnViewEnd(self, eid):
        pass

    def OnElementBegin(self, eid):
        d = self.docs[-1]
        el = d.GetElement(eid)
        cat = el.Category.Name if el is not None and el.Category is not None else u"(khác)"
        ci = self.cat_index.get(cat)
        if ci is None:
            ci = self.cat_index[cat] = len(self.categories)
            self.categories.append(cat)
        self.elements.append([eid.IntegerValue, ci, 1 if len(self.docs) > 1 else 0])
        self.el_index = len(self.elements) - 1
        return RenderNodeAction.Proceed

    def OnElementEnd(self, eid):
        pass

    def OnInstanceBegin(self, node):
        self.xf.append(self.xf[-1].Multiply(node.GetTransform()))
        return RenderNodeAction.Proceed

    def OnInstanceEnd(self, node):
        self.xf.pop()

    def OnLinkBegin(self, node):
        self.xf.append(self.xf[-1].Multiply(node.GetTransform()))
        self.docs.append(node.GetDocument() or self.docs[-1])
        return RenderNodeAction.Proceed

    def OnLinkEnd(self, node):
        self.xf.pop()
        self.docs.pop()

    def OnFaceBegin(self, node):
        return RenderNodeAction.Proceed

    def OnFaceEnd(self, node):
        pass

    def OnLight(self, node):
        pass

    def OnRPC(self, node):
        pass

    def OnMaterial(self, node):
        c = node.Color
        if c is None or not c.IsValid:
            self.rgba = b"\xb4\xb4\xb4\xff"
            return
        a = int(round(255 * (1.0 - max(0.0, min(1.0, node.Transparency)))))
        self.rgba = bytes(bytearray([c.Red, c.Green, c.Blue, max(a, 40)]))

    def OnPolymesh(self, mesh):
        n = mesh.NumberOfPoints
        nf = mesh.NumberOfFacets
        if self.triangles + nf > self.limit:
            self.truncated = True
            return
        t = self.xf[-1]
        ox, oy, oz = self.origin
        pos = self.pos
        if t.IsIdentity:
            for p in mesh.GetPoints():
                pos.extend(((p.X - ox) * FT, (p.Y - oy) * FT, (p.Z - oz) * FT))
        else:
            for p in mesh.GetPoints():
                q = t.OfPoint(p)
                pos.extend(((q.X - ox) * FT, (q.Y - oy) * FT, (q.Z - oz) * FT))
        self.col.extend(self.rgba * n)
        self.elem.extend([self.el_index] * n)
        base = self.vertices
        idx = self.idx
        for f in mesh.GetFacets():
            idx.extend((base + f.V1, base + f.V2, base + f.V3))
        self.vertices += n
        self.triangles += nf


def _origin(view):
    """Centre of the section box (model coordinates, feet), else 0,0,0."""
    try:
        if view.IsSectionBoxActive:
            b = view.GetSectionBox()
            c = b.Transform.OfPoint((b.Min + b.Max) * 0.5)
            return (c.X, c.Y, c.Z)
    except Exception:
        pass
    return (0.0, 0.0, 0.0)


def _safe(name):
    return re.sub(u"[^0-9A-Za-z_-]+", u"-", name).strip(u"-")[:60] or u"view"


def _tidy():
    files = sorted((f for f in os.listdir(FOLDER) if f.endswith(u".json")),
                   key=lambda f: os.path.getmtime(os.path.join(FOLDER, f)), reverse=True)
    for f in files[KEEP:]:
        for ext in (u".json", u".bin"):
            try:
                os.remove(os.path.join(FOLDER, f[:-5] + ext))
            except Exception:
                pass


def export(doc, view, limit=None):
    """Write the view's triangles; returns the manifest (+ name, file sizes, seconds)."""
    t0 = time.time()
    origin = _origin(view)
    ctx = _Context(doc, origin, int(limit or MAX_TRIANGLES))
    ex = CustomExporter(doc, ctx)
    ex.IncludeGeometricObjects = False
    ex.ShouldStopOnError = False
    ex.Export(view)
    if not ctx.triangles and ctx.truncated:
        raise MeshError(u"Vượt giới hạn {0:,} tam giác ngay từ đầu.".format(ctx.limit))
    if not ctx.triangles:
        raise MeshError(u"View '{0}' không có gì để vẽ (bật Section Box quá nhỏ, hoặc mọi "
                        u"category bị ẩn?).".format(view.Name))
    if not os.path.isdir(FOLDER):
        os.makedirs(FOLDER)
    name = u"{0}-{1}".format(_safe(view.Name), time.strftime("%Y%m%d-%H%M%S"))
    blobs = [("position", ctx.pos.tostring()), ("color", bytes(ctx.col)),
             ("element", ctx.elem.tostring()), ("index", ctx.idx.tostring())]
    offsets, at = {}, 0
    with io.open(os.path.join(FOLDER, name + u".bin"), "wb") as f:
        for key, b in blobs:
            offsets[key] = [at, len(b)]
            f.write(b)
            at += len(b)
    man = {"version": 1, "name": name, "model": doc.Title, "view": view.Name,
           "viewId": view.Id.IntegerValue, "unit": "m", "originFt": list(origin),
           "vertices": ctx.vertices, "triangles": ctx.triangles, "truncated": ctx.truncated,
           "limit": ctx.limit, "categories": ctx.categories, "elements": ctx.elements,
           "buffers": offsets, "exported": time.strftime("%Y-%m-%d %H:%M:%S"),
           "seconds": round(time.time() - t0, 1)}
    s = json.dumps(man, ensure_ascii=False)
    s = u"".join(c if ord(c) < 128 else u"\\u%04x" % ord(c) for c in s)
    with io.open(os.path.join(FOLDER, name + u".json"), "w", encoding="utf-8") as f:
        f.write(s)
    _tidy()
    return man
