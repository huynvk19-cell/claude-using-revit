# -*- coding: utf-8 -*-
"""Sheets of a drawing list to PDF without bringing Revit down.

Output: one vector PDF per sheet in the chosen folder, optionally in sub-folders by a
sheet parameter or by the Project Browser's sheet folders; never one joined file.

Per sheet:
  - paper from the title block (ISO / ANSI / ARCH size it matches, else the sheet
    size), zoom 100 %, centred, vector;
  - weight = what Revit has to draw: elements each view shows (+ visible links,
    x for 3D / shaded / fine), summed over the sheet's viewports;
  - heavy sheet (weight over the limit) -> exported in parts: each part is the sheet
    with only some views drawn (the others blanked inside a transaction group
    that is rolled back, so the model is left as it was), then the parts are drawn
    over each other into one page (pdf_merge; parts sit in %TEMP% meanwhile);
  - between sheets: .NET garbage collection + released API objects purged, free RAM
    checked; too little left -> stop cleanly, the next run carries on from there.

A journal (journals\ next to the settings) records each sheet as started / done, so
a run cut short carries on where it stopped. Revit can also die inside its own
drawing code on one bad view whatever the RAM (GeomUtil crash): the crash memory in
pdf_export.json (%APPDATA%\\claude-using-revit) remembers the pass Revit died on and
narrows it down run after run: sheet -> one view per part -> that view again alone ->
that view in 2x2, 3x3, 4x4 crop tiles. No view is ever left out of a PDF (always
vector, never raster); a sheet that cannot be drawn whole fails and is named.
Revit can also refuse a pass without dying (Export returns False): the same narrowing
runs at once, in the same run, until the sheet goes out whole or the steps run out.

A model opened with some of its worksets closed prints its views without the elements
of those worksets (walls, grids, levels... gone, titles and tags still there), and a
link that is not loaded prints nothing: the run stops before any sheet when a workset
of the model is closed, and a sheet showing a link that is not loaded fails, unless the
caller says it is on purpose (allow_incomplete).
"""
import io
import json
import os
import re
import time
import zlib

from Autodesk.Revit.DB import (
    BoundingBoxXYZ, BrowserOrganization, BuiltInCategory, BuiltInParameter, CheckoutStatus,
    Element, ElementId,
    ExportPaperFormat, FilteredElementCollector, FilteredWorksetCollector,
    PageOrientationType, PaperPlacementType,
    GraphicsStyleType, Line, PDFExportOptions, RevitLinkInstance,
    Transaction, TransactionGroup, FailureProcessingResult, FailureSeverity,
    IFailuresPreprocessor,
    ViewDetailLevel, XYZ,
    StorageType, Viewport, ViewSheet, ViewType, WorksetKind, WorksharingUtils, ZoomType)
from System import GC
from System.Collections.Generic import List

import pdf_merge

MM = 304.8
LIMIT = 250000          # weight one export pass may carry on a PC with 8 GB free
MIN_FREE_MB = 1500      # stop before the next sheet below this much free RAM
PARTS_DIR = os.path.join(os.environ.get("TEMP", u"C:\\Temp"), u"claude-using-revit", u"pdf-parts")
SETTINGS = os.path.join(os.environ.get("APPDATA", u""), u"claude-using-revit", u"pdf_export.json")

# (format, long side mm, short side mm)
PAPERS = [("ISO_A0", 1189, 841), ("ISO_A1", 841, 594), ("ISO_A2", 594, 420),
          ("ISO_A3", 420, 297), ("ISO_A4", 297, 210), ("ISO_B1", 1000, 707),
          ("ISO_B2", 707, 500), ("ISO_B3", 500, 353), ("ISO_B4", 353, 250),
          ("ANSI_E", 1117.6, 863.6), ("ANSI_D", 863.6, 558.8), ("ANSI_C", 558.8, 431.8),
          ("ANSI_B", 431.8, 279.4), ("ANSI_A", 279.4, 215.9),
          ("ARCH_E", 1219.2, 914.4), ("ARCH_E1", 1066.8, 762), ("ARCH_D", 914.4, 609.6),
          ("ARCH_C", 609.6, 457.2), ("ARCH_B", 457.2, 304.8), ("ARCH_A", 304.8, 228.6)]
HEAVY_STYLES = ("Shading", "ShadingWithEdges", "Realistic", "RealisticWithEdges",
                "FlatColors", "Textures")


# ---- settings learnt on this PC ---------------------------------------------------

def dumps(data):
    """JSON text; non-ASCII escaped by hand (ensure_ascii trips on it in IronPython)."""
    s = json.dumps(data, indent=1, ensure_ascii=False)
    return u"".join(c if ord(c) < 128 else u"\\u%04x" % ord(c) for c in s)


def settings():
    try:
        with io.open(SETTINGS, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def save_settings(s):
    try:
        d = os.path.dirname(SETTINGS)
        if not os.path.isdir(d):
            os.makedirs(d)
        with io.open(SETTINGS, "w", encoding="utf-8") as f:
            f.write(dumps(s))
    except Exception:
        pass


def base_limit():
    return int(settings().get("limit", LIMIT))


# ---- memory / cpu -----------------------------------------------------------------

def memory_mb():
    """(free, total) physical memory in MB."""
    import clr
    clr.AddReference("Microsoft.VisualBasic")
    from Microsoft.VisualBasic.Devices import ComputerInfo
    ci = ComputerInfo()
    return int(ci.AvailablePhysicalMemory / 1048576), int(ci.TotalPhysicalMemory / 1048576)


def limit_now(base=None):
    """Limit lowered when RAM is short: half at 4 GB free. Never raised: plenty of RAM
    does not stop Revit's drawing code from dying on a heavy view."""
    free = memory_mb()[0]
    k = min(1.0, max(0.5, free / 8192.0))
    return int((base or base_limit()) * k)


def tidy(app):
    """Hand back what can be handed back between two sheets."""
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    try:
        app.PurgeReleasedAPIObjects()
    except Exception:
        pass


def wait_for_memory(app, min_free):
    """Tidy and wait a little until min_free MB are free; returns free MB."""
    free = memory_mb()[0]
    for _ in range(4):
        if free >= min_free:
            break
        tidy(app)
        time.sleep(2)
        free = memory_mb()[0]
    return free


class LowPriority(object):
    """Revit at below-normal CPU priority while exporting, so the PC stays usable."""

    def __enter__(self):
        from System.Diagnostics import Process, ProcessPriorityClass
        self.p = Process.GetCurrentProcess()
        self.old = self.p.PriorityClass
        try:
            self.p.PriorityClass = ProcessPriorityClass.BelowNormal
        except Exception:
            pass
        return self

    def __exit__(self, *a):
        try:
            self.p.PriorityClass = self.old
        except Exception:
            pass


# ---- crash memory ("black box") ---------------------------------------------------
# Revit can die inside its own drawing code on one bad view (GeomUtil / hidden-line
# crash), whatever the RAM. Before every export pass the views it draws are written
# here; a pass still written down at the next run is the one Revit died on (only when
# Windows logged Revit crashing / hanging; a Revit closed by hand teaches nothing):
#   - a pass of several views -> that sheet is isolated: one view per part;
#   - a pass of one view      -> its crash count goes up: 1 = exported alone again,
#                                2+ = cut into 2x2, 3x3, 4x4 tiles of its crop region.
# No view is ever left out (vector only, never raster); a sheet that still cannot be
# drawn fails and is named, so no PDF ever misses a view. Kept per model (document
# title) across days and folders; forget() clears it once the model is fixed.

def _doc_key(doc):
    # Title carries ".rvt" or not depending on the Windows "hide extensions" setting
    return re.sub(u"(?i)\\.rvt$", u"", doc.Title)


def crash_memory(doc):
    return settings().get("crash", {}).get(_doc_key(doc), {})


def _crash_save(doc, mem):
    s = settings()
    s.setdefault("crash", {})[_doc_key(doc)] = mem
    save_settings(s)


def forget(doc):
    """Clear the crash memory of this model (after fixing the views); returns its size."""
    mem = crash_memory(doc)
    n = len(mem.get("views", {})) + len(mem.get("sheets", []))
    _crash_save(doc, {})
    return n


def crashes(mem, view_id):
    """How many times Revit died drawing this view alone ("skip" from older runs = 2)."""
    v = mem.get("views", {}).get(str(view_id), 0)
    return 2 if v == "skip" else int(v)


def tiles_for(n):
    """Tiles per side for a view Revit died on n times alone."""
    return 1 if n <= 1 else min(n, 4)


def crash_notes(doc, sheets):
    """[(sheet, [(view name, tiles per side)])] for views the crash memory tiles."""
    mem = crash_memory(doc)
    out = []
    for s in sheets:
        rows = []
        for vpid in s.GetAllViewports():
            vid = doc.GetElement(vpid).ViewId
            n = tiles_for(crashes(mem, vid.IntegerValue))
            if crashes(mem, vid.IntegerValue):
                rows.append((doc.GetElement(vid).Name, n))
        if rows:
            out.append((s, rows))
    return out


def mark_pass(doc, sheet, view_ids, tiles=1):
    mem = crash_memory(doc)
    from System import DateTime
    mem["pending"] = {"sheet": sheet.Id.IntegerValue, "views": list(view_ids), "tiles": tiles,
                      "t": DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}
    _crash_save(doc, mem)


def revit_died_since(utc_iso):
    """True when Windows logged Revit crashing / hanging (Application Error, WER,
    Application Hang: events 1000 / 1001 / 1002) after that UTC time. None when the
    log cannot be read. A Revit closed by the user mid-export leaves no such event."""
    try:
        from System import DateTime
        from System.Diagnostics import EventLog
        since = DateTime.Parse(utc_iso).ToUniversalTime()
        log = EventLog(u"Application")
        entries = log.Entries
        i = entries.Count - 1
        while i >= 0:                       # newest first, stop at the pass start
            e = entries[i]
            if e.TimeGenerated.ToUniversalTime() < since:
                break
            if e.InstanceId & 0xFFFF in (1000, 1001, 1002) and \
                    u"revit.exe" in (e.Message or u"").lower():
                return True
            i -= 1
        return False
    except Exception:
        return None


def clear_pass(doc):
    mem = crash_memory(doc)
    if mem.pop("pending", None) is not None:
        _crash_save(doc, mem)


def recover(doc):
    """Read a pass left pending by a crash; returns lines for the user."""
    mem = crash_memory(doc)
    p = mem.pop("pending", None)
    if not p:
        return []
    sheet = doc.GetElement(ElementId(int(p["sheet"])))
    label = sheet.SheetNumber if isinstance(sheet, ViewSheet) else str(p["sheet"])
    if p.get("t") and revit_died_since(p["t"]) is False:
        # Revit was closed, not crashed: nothing to learn, export it as usual
        _crash_save(doc, mem)
        return [u"{0}: lần trước Revit bị đóng giữa lúc xuất sheet này (không văng), "
                u"lần này xuất lại bình thường".format(label)]
    line = _learn(doc, mem, p, label, u"Revit văng")
    _crash_save(doc, mem)
    return [line]


def learn_failed(doc):
    """The pending pass Revit refused (Export returned False, Revit alive) is learnt
    like a crash, so the sheet can be retried at once in smaller passes. Returns the
    line for the user, or None when no pass was pending."""
    mem = crash_memory(doc)
    p = mem.pop("pending", None)
    if not p:
        return None
    sheet = doc.GetElement(ElementId(int(p["sheet"])))
    label = sheet.SheetNumber if isinstance(sheet, ViewSheet) else str(p["sheet"])
    # alone again would fail the same way in this session: straight to tiles
    line = _learn(doc, mem, p, label, u"Revit không xuất được", first=2)
    _crash_save(doc, mem)
    return line


def _learn(doc, mem, p, label, what, first=1):
    """Narrow the crash memory one step for pass p; returns the line for the user."""
    sheets = mem.setdefault("sheets", [])
    if len(p["views"]) == 1 or (p["views"] and p["sheet"] in sheets):
        # one view, or one group of dependent views of an isolated sheet
        names, n = [], 1
        for vid in p["views"]:
            v = doc.GetElement(ElementId(int(vid)))
            n = max(crashes(mem, vid) + 1, first)
            mem.setdefault("views", {})[str(vid)] = n
            names.append(v.Name if v is not None else str(vid))
        k = tiles_for(n)
        return u"{0}: {1} view \"{2}\" (lần {3}), lần này {4}".format(
            label, what, u"\", \"".join(names), n,
            u"xuất riêng lại view này" if k == 1 else
            u"chia view thành {0}x{0} ô theo vùng crop rồi ghép lại".format(k))
    if p["sheet"] in sheets:
        return u"{0}: {1} khung tên / ghi chú của sheet, lần này thử lại".format(label, what)
    sheets.append(p["sheet"])
    return u"{0}: {1} sheet này, lần này tách từng view để tìm view lỗi".format(label, what)


# ---- model completeness -----------------------------------------------------------
# Elements of a closed workset are not loaded at all: views print without them, with
# nothing to say so (titles, tags, links still there). Same for a link not loaded.
# Worksets closed inside a LINK are left alone: projects close them on purpose
# (e.g. the link's shared levels and grids).

def closed_worksets(doc):
    """Names of this model's own user worksets that are closed."""
    if not doc.IsWorkshared:
        return []
    return sorted(w.Name for w in FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                  if not w.IsOpen)


def unloaded_links(doc, sheet):
    """Names of the Revit links not loaded that a view of the sheet would show."""
    lis = [li for li in FilteredElementCollector(doc).OfClass(RevitLinkInstance)
           if li.GetLinkDocument() is None]
    if not lis:
        return []
    cat = ElementId(BuiltInCategory.OST_RvtLinks)
    out = set()
    for vp in viewports(doc, sheet):
        v = doc.GetElement(vp.ViewId)
        try:
            if v.GetCategoryHidden(cat):
                continue
        except Exception:
            pass
        for li in lis:
            if not li.IsHidden(v):
                t = doc.GetElement(li.GetTypeId())
                out.add(Element.Name.__get__(t) if t is not None else li.Name)
    return sorted(out)


# ---- paper ------------------------------------------------------------------------

def titleblock(doc, sheet):
    tbs = list(FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
               .WhereElementIsNotElementType())
    best, area = None, 0
    for tb in tbs:
        w, h = _size(tb, sheet)
        if w * h > area:
            best, area = tb, w * h
    return best


def _size(tb, sheet):
    w = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH)
    h = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT)
    if w and h and w.AsDouble() > 0 and h.AsDouble() > 0:
        return w.AsDouble() * MM, h.AsDouble() * MM
    bb = tb.get_BoundingBox(sheet)
    if bb is None:
        return 0, 0
    return (bb.Max.X - bb.Min.X) * MM, (bb.Max.Y - bb.Min.Y) * MM


def paper(doc, sheet):
    """{"format", "landscape", "label", "titleblock"} from the sheet's title block."""
    tb = titleblock(doc, sheet)
    if tb is None:
        return {"format": "Default", "landscape": True, "label": u"khổ sheet",
                "titleblock": u""}
    w, h = _size(tb, sheet)
    sym = doc.GetElement(tb.GetTypeId())
    name = u"{0}: {1}".format(sym.FamilyName, tb.Name) if sym else tb.Name
    lo, sh = max(w, h), min(w, h)
    for fmt, a, b in PAPERS:
        if abs(lo - a) <= 3 and abs(sh - b) <= 3:
            return {"format": fmt, "landscape": w >= h, "label": fmt.split("_")[-1],
                    "titleblock": name}
    return {"format": "Default", "landscape": w >= h,
            "label": u"{0:.0f}x{1:.0f}".format(w, h), "titleblock": name}


def options(p, file_name):
    o = PDFExportOptions()
    o.FileName = file_name
    o.Combine = True
    o.PaperFormat = getattr(ExportPaperFormat, p["format"])
    o.PaperOrientation = PageOrientationType.Landscape if p["landscape"] else PageOrientationType.Portrait
    o.ZoomType = ZoomType.Zoom
    o.ZoomPercentage = 100
    o.PaperPlacement = PaperPlacementType.Center
    o.AlwaysUseRaster = False       # vector only: raster PDFs are not accepted
    o.HideCropBoundaries = True
    o.HideScopeBoxes = True
    o.HideReferencePlane = True
    o.HideUnreferencedViewTags = True
    return o


# ---- weight -----------------------------------------------------------------------

class Weigher(object):
    """Weight of views; link element counts cached per link document."""

    def __init__(self, doc):
        self.doc = doc
        self.links = {}
        self.views = {}

    def link_count(self, li):
        ld = li.GetLinkDocument()
        if ld is None:
            return 0
        key = ld.PathName or ld.Title
        if key not in self.links:
            self.links[key] = FilteredElementCollector(ld).WhereElementIsNotElementType().GetElementCount()
        return self.links[key]

    def view(self, v):
        if v.Id.IntegerValue in self.views:
            return self.views[v.Id.IntegerValue]
        doc = self.doc
        n = FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().GetElementCount()
        three_d = v.ViewType == ViewType.ThreeD
        cropped = v.CropBoxActive and not three_d
        links = 0
        try:
            for li in FilteredElementCollector(doc, v.Id).OfClass(RevitLinkInstance):
                links += self.link_count(li)
        except Exception:
            pass
        w = n + links * (0.35 if cropped else 1.0)
        notes = []
        if three_d:
            w *= 2
            notes.append(u"3D")
        try:
            if str(v.DisplayStyle) in HEAVY_STYLES:
                w *= 1.5
                notes.append(u"tô bóng")
            if v.DetailLevel == ViewDetailLevel.Fine:
                w *= 1.3
        except Exception:
            pass
        if links:
            notes.append(u"link")
        res = (int(w), notes)
        self.views[v.Id.IntegerValue] = res
        return res


def viewports(doc, sheet):
    return [doc.GetElement(i) for i in sheet.GetAllViewports()]


def assess(doc, sheet, weigher, limit, crash=None):
    """Weight of the sheet and how to export it. No view is ever left out.

    Returns {"weight", "views": [(viewport, weight, notes)], "mode": whole | parts,
             "parts": [{"vps": [viewport ids], "tiles": per side}] (first = base part
             with the title block), "tiled": [(view name, tiles)], "note"}.
    """
    crash = crash or {}
    rows = []
    for vp in viewports(doc, sheet):
        w, notes = weigher.view(doc.GetElement(vp.ViewId))
        rows.append((vp, w, notes))
    total = sum(w for _, w, _ in rows)
    out = {"weight": total, "views": rows, "mode": "whole", "parts": None,
           "tiled": [], "note": u""}
    sid = sheet.Id.IntegerValue
    isolate = sid in crash.get("sheets", []) or any(
        crashes(crash, vp.ViewId.IntegerValue) for vp, _, _ in rows)
    if not isolate and (total <= limit or len(rows) < 2):
        if total > limit:
            out["note"] = u"một view nặng, không chia được"
        return out
    why = _cannot_split(doc, sheet, rows)
    if why:                              # whole again (a crash on it is retried)
        out["note"] = why
        return out
    out["mode"] = "parts"
    units = _units(doc, rows)
    if isolate:
        # base = title block + sheet notes only; each view (or dependent group) alone;
        # a view Revit died on alone is cut into tiles of its crop region
        parts = [{"vps": [], "tiles": 1}]
        for unit in units:
            n = max(tiles_for(crashes(crash, vp.ViewId.IntegerValue)) for vp, _ in unit)
            if n > 1:
                bad = [r for r in (_tile_blocker(doc.GetElement(vp.ViewId)) for vp, _ in unit) if r]
                if bad:
                    out["note"] = u"không chia ô được ({0}), xuất riêng lại".format(bad[0])
                    n = 1
                else:
                    out["tiled"] += [(doc.GetElement(vp.ViewId).Name, n) for vp, _ in unit]
            parts.append({"vps": [vp.Id for vp, _ in unit], "tiles": n})
        out["parts"] = parts
        out["note"] = out["note"] or u"tách từng view (lần trước Revit văng)"
        return out
    if len(units) < 2:
        out["mode"] = "whole"
        out["note"] = u"các view cùng một view gốc, không chia được"
        return out
    cap = limit if total > limit else max(1, total // 2)
    groups = []                      # [weight, [viewport ids]] first fit, heaviest first
    for unit in sorted(units, key=lambda u: -sum(w for _, w in u)):
        w = sum(x for _, x in unit)
        for g in groups:
            if g[0] + w <= cap:
                g[0] += w
                g[1] += [vp.Id for vp, _ in unit]
                break
        else:
            groups.append([w, [vp.Id for vp, _ in unit]])
    if len(groups) < 2:
        groups = [[sum(x for _, x in u), [vp.Id for vp, _ in u]] for u in units]
    groups.sort(key=lambda g: g[0])          # lightest first = the base part
    out["parts"] = [{"vps": g[1], "tiles": 1} for g in groups]
    if any(w > limit for _, w, _ in rows):
        out["note"] = u"có view vượt ngưỡng một mình"
    return out


def _tile_blocker(v):
    """Why view v cannot be cut into crop tiles, or u""."""
    if not v.CropBoxActive:
        return u"view không bật crop"
    try:
        if v.GetCropRegionShapeManager().ShapeSet:
            return u"vùng crop không phải hình chữ nhật"
    except Exception:
        pass
    return u""


def view_root(doc, view_id):
    """Primary view of a dependent view, else the view itself (id as int). Dependent
    views share their primary's Visibility/Graphics, so they are blanked together."""
    v = doc.GetElement(view_id)
    pid = v.GetPrimaryViewId()
    return pid.IntegerValue if pid != ElementId.InvalidElementId else v.Id.IntegerValue


def _units(doc, rows):
    """Viewports grouped by root view, in sheet order: [[(viewport, weight)]]."""
    order, by_root = [], {}
    for vp, w, _ in rows:
        r = view_root(doc, vp.ViewId)
        if r not in by_root:
            by_root[r] = []
            order.append(r)
        by_root[r].append((vp, w))
    return [by_root[r] for r in order]


def _cannot_split(doc, sheet, rows):
    """Reason the sheet must go out whole, or u"". Parts are aligned on the title block,
    so everything printed must sit inside it."""
    if doc.IsWorkshared:
        ids = [sheet.Id] + [vp.Id for vp, _, _ in rows] + [vp.ViewId for vp, _, _ in rows]
        if any(WorksharingUtils.GetCheckoutStatus(doc, i) == CheckoutStatus.OwnedByOtherUser
               for i in ids):
            return u"sheet / view đang do người khác giữ, không tạm làm trống view được"
    tb = titleblock(doc, sheet)
    if tb is None:
        return u"không có khung tên để căn các phần"
    box = tb.get_BoundingBox(sheet)
    tol = 1e-3
    for e in FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType():
        if e.Id == tb.Id or e.Category is None or not e.CanBeHidden(sheet) or e.IsHidden(sheet):
            continue
        if isinstance(e, Viewport):
            o = e.GetBoxOutline()
            lo, hi = o.MinimumPoint, o.MaximumPoint
        else:
            bb = e.get_BoundingBox(sheet)
            if bb is None:
                continue
            lo, hi = bb.Min, bb.Max
        if (lo.X < box.Min.X - tol or lo.Y < box.Min.Y - tol or
                hi.X > box.Max.X + tol or hi.Y > box.Max.Y + tol):
            return u"có phần tử lấn ra ngoài khung tên"
    return u""


# ---- export -----------------------------------------------------------------------

_BAD = re.compile(u'[\\\\/:*?"<>|\\r\\n\\t]')


def file_name(sheet):
    return _BAD.sub(u"_", u"{0} - {1}".format(sheet.SheetNumber, sheet.Name)).strip(u" .")


def _remove(path):
    if os.path.exists(path):
        os.remove(path)


class ExportFailed(Exception):
    """Revit refused one export pass (alive); the pass is still pending in the crash memory."""


def _export(doc, sheet, folder, name, p):
    path = os.path.join(folder, name + u".pdf")
    _remove(path)
    ok = doc.Export(folder, List[ElementId]([sheet.Id]), options(p, name))
    if not ok or not os.path.exists(path):
        raise ExportFailed(u"Revit không xuất được {0}".format(name))
    return path


def _anchors(doc, sheet, box):
    """Two tiny lines on the title block's corners: they hold the print extents (so
    Revit centres the part exactly like the whole sheet) while the title block itself
    is hidden, so it is not drawn twice. Invisible line style: no ink at all."""
    gs = None
    try:
        cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_InvisibleLines)
        gs = cat.GetGraphicsStyle(GraphicsStyleType.Projection)
    except Exception:
        pass      # default line style: 1 mm drawn over the title block border
    e = 1.0 / MM
    for a, b in ((XYZ(box.Min.X, box.Min.Y, 0), XYZ(box.Min.X + e, box.Min.Y, 0)),
                 (XYZ(box.Max.X - e, box.Max.Y, 0), XYZ(box.Max.X, box.Max.Y, 0))):
        c = doc.Create.NewDetailCurve(sheet, Line.CreateBound(a, b))
        if gs is not None:
            c.LineStyle = gs


def _blank(doc, v):
    """Hide every category in view v (template detached first): it then draws nothing."""
    if v.ViewTemplateId != ElementId.InvalidElementId:
        v.ViewTemplateId = ElementId.InvalidElementId
    for c in doc.Settings.Categories:
        try:
            if v.CanCategoryBeHidden(c.Id):
                v.SetCategoryHidden(c.Id, True)
        except Exception:
            pass


class _Quiet(IFailuresPreprocessor):
    """Warnings of the throw-away part transaction are dismissed, never shown."""

    def PreprocessFailures(self, fa):
        for f in fa.GetFailureMessages():
            if f.GetSeverity() == FailureSeverity.Warning:
                fa.DeleteWarning(f)
        return FailureProcessingResult.Continue


def _tile_crop(v, i, j, n):
    """Crop view v to tile (i, j) of an n x n grid of its own crop box. The model stays
    where it is on the sheet; only what is drawn shrinks."""
    bb = v.CropBox
    mn, mx = bb.Min, bb.Max
    w, h = (mx.X - mn.X) / n, (mx.Y - mn.Y) / n
    nb = BoundingBoxXYZ()
    nb.Transform = bb.Transform
    nb.Min = XYZ(mn.X + i * w, mn.Y + j * h, mn.Z)
    nb.Max = XYZ(mn.X + (i + 1) * w, mn.Y + (j + 1) * h, mx.Z)
    v.CropBox = nb


def _no_title(doc, vp, k):
    """The viewport without its title for a tile pass (its box moves with the tile, the
    title would move too); the title comes once from its own pass."""
    t = doc.GetElement(vp.GetTypeId())
    nt = t.Duplicate(u"{0} ({1})".format(Element.Name.__get__(t), k))
    prm = nt.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL)
    if prm is not None and not prm.IsReadOnly:
        prm.Set(0)
    vp.ChangeTypeId(nt.Id)


def _passes(plan):
    """[(kept viewport ids, kind, tile)] for the parts of a plan. kind: base / view /
    title / tile. A tiled part = one pass for its title(s) + n x n tile passes."""
    out = []
    for k, part in enumerate(plan["parts"]):
        n = part.get("tiles", 1)
        if k == 0:
            out.append((part["vps"], "base", None))
        elif n <= 1:
            out.append((part["vps"], "view", None))
        else:
            out.append((part["vps"], "title", None))
            out += [(part["vps"], "tile", (i, j, n)) for i in range(n) for j in range(n)]
    return out


def export_sheet(doc, sheet, folder, plan, p, tidy_between=None):
    """Export one sheet to folder/<number - name>.pdf (parts joined into one page).

    Part 1 = the sheet minus the viewports of the other parts (title block, sheet
    notes, schedules stay). Part 2.. = only their viewports + two corner anchors; a
    view Revit died on is drawn tile by tile (crop cut into n x n). Blanking / hiding /
    anchors / crops happen in a transaction group that is rolled back, so the model is
    left exactly as it was. Every view is drawn; nothing is left out.
    Each pass is written to the crash memory first (mark_pass / clear_pass).
    """
    name = file_name(sheet)
    view_of = dict((vp.Id.IntegerValue, vp.ViewId.IntegerValue) for vp in viewports(doc, sheet))
    if plan["mode"] == "whole":
        mark_pass(doc, sheet, view_of.values())
        path = _export(doc, sheet, folder, name, p)
        clear_pass(doc)
        return path
    on_sheet = [e for e in FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType()
                if e.Category is not None and e.CanBeHidden(sheet) and not e.IsHidden(sheet)]
    box = titleblock(doc, sheet).get_BoundingBox(sheet)
    if not os.path.isdir(PARTS_DIR):
        os.makedirs(PARTS_DIR)
    files = []
    try:
        for k, (vps, kind, tile) in enumerate(_passes(plan)):
            keep = set(i.IntegerValue for i in vps)
            if kind == "base":
                drop = [e for e in on_sheet
                        if e.Id.IntegerValue in view_of and e.Id.IntegerValue not in keep]
            else:
                drop = [e for e in on_sheet if e.Id.IntegerValue not in keep]
            # A hidden viewport still prints its view (only its title goes), and taking
            # it off the sheet would drop the tags that point at it (unreferenced). So
            # its view is blanked for the pass: every category hidden, nothing drawn.
            hide = [e.Id for e in drop]
            # never blank a view of the same root as a kept one (it would blank both)
            kept_roots = set(view_root(doc, ElementId(view_of[i])) for i in keep)
            blank = [doc.GetElement(ElementId(view_of[e.Id.IntegerValue]))
                     for e in drop if e.Id.IntegerValue in view_of and
                     view_root(doc, ElementId(view_of[e.Id.IntegerValue])) not in kept_roots]
            kept = [doc.GetElement(ElementId(i)) for i in keep]
            if kind == "title":          # only the titles of the tiled views
                blank += [doc.GetElement(vp.ViewId) for vp in kept]
            mark_pass(doc, sheet, [view_of[i] for i in keep], tile[2] if tile else 1)
            tg = TransactionGroup(doc, u"PDF part")
            tg.Start()
            try:
                t = Transaction(doc, u"PDF part")
                fho = t.GetFailureHandlingOptions()
                fho.SetFailuresPreprocessor(_Quiet())
                t.SetFailureHandlingOptions(fho)
                t.Start()
                for v in blank:
                    _blank(doc, v)
                if hide:
                    sheet.HideElements(List[ElementId](hide))
                if kind != "base":
                    _anchors(doc, sheet, box)
                if kind == "tile":
                    for vp in kept:
                        _no_title(doc, vp, k)
                        _tile_crop(doc.GetElement(vp.ViewId), *tile)
                t.Commit()
                files.append(_export(doc, sheet, PARTS_DIR, u"{0}__part{1}".format(name, k + 1), p))
            finally:
                tg.RollBack()
            clear_pass(doc)
            if tidy_between:
                tidy_between()
        path = os.path.join(folder, name + u".pdf")
        _remove(path)
        pdf_merge.join([(None, files[0], files[1:])], path)
        return path
    finally:
        for f in files:
            try:
                _remove(f)
            except Exception:
                pass


# ---- journal ----------------------------------------------------------------------

class Journal(object):
    """Which sheets of this run are done, so a run cut short carries on. Kept with the
    settings (not in the user's folder), one file per folder + sheet list."""

    def __init__(self, folder, key):
        name = u"{0:08x}.json".format(zlib.crc32((folder + u"|" + key).encode("utf-8")) & 0xffffffff)
        self.path = os.path.join(os.path.dirname(SETTINGS), u"journals", name)
        self.data = {"key": key, "done": False, "sheets": {}}
        try:
            with io.open(self.path, encoding="utf-8") as f:
                old = json.load(f)
            if old.get("key") == key and not old.get("done"):
                self.data = old
        except Exception:
            pass

    def resumed(self):
        return bool(self.data["sheets"])

    def state(self, sid):
        return self.data["sheets"].get(str(sid), {})

    def set(self, sid, **kw):
        self.data["sheets"].setdefault(str(sid), {}).update(kw)
        self.save()

    def save(self):
        d = os.path.dirname(self.path)
        if not os.path.isdir(d):
            os.makedirs(d)
        with io.open(self.path, "w", encoding="utf-8") as f:
            f.write(dumps(self.data))


# ---- sub-folders by a sheet parameter ---------------------------------------------
# group = None (all files in the folder), {"browser": True} (the Project Browser's
# sheet folders, every level) or {"param": <parameter id>} (one sheet parameter).

EMPTY = u"(trống)"


def _value(sheet, pid):
    for prm in sheet.Parameters:
        if prm.Id.IntegerValue == pid:
            v = prm.AsString() if prm.StorageType == StorageType.String else prm.AsValueString()
            return v or u""
    return u""


def browser_levels(doc):
    """[(parameter id, name)] the Project Browser groups sheets by, outer first."""
    org = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc)
    if org is None:
        return []
    out = []
    for s in FilteredElementCollector(doc).OfClass(ViewSheet):
        try:
            items = org.GetFolderItems(s.Id)
        except Exception:
            continue
        if items and items.Count:
            for it in items:
                pid = it.ElementId.IntegerValue
                out.append((pid, _param_name(doc, s, pid)))
            break
    return out


def _param_name(doc, sheet, pid):
    for prm in sheet.Parameters:
        if prm.Id.IntegerValue == pid:
            return prm.Definition.Name
    return str(pid)


def group_options(doc, sheets):
    """[(label, group spec, suggested)] for the window: no sub-folder, the Project
    Browser's folders, then the sheet parameters that actually split these sheets
    (at least two different values; browser ones first, marked as suggested)."""
    levels = browser_levels(doc)
    out = [(u"Không chia, tất cả trong một thư mục", None, False)]
    if levels:
        out.append((u"Như Project Browser: " + u" › ".join(n for _, n in levels),
                    {"browser": True}, True))
    seen = set(pid for pid, _ in levels)
    for pid, n in (levels if len(levels) > 1 else []):    # one level = the line above
        out.append((n, {"param": pid}, True))
    skip = set(int(b) for b in (
        BuiltInParameter.SHEET_NUMBER, BuiltInParameter.SHEET_NAME,
        BuiltInParameter.ELEM_FAMILY_PARAM, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
        BuiltInParameter.ELEM_TYPE_PARAM, BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM,
        BuiltInParameter.SYMBOL_FAMILY_AND_TYPE_NAMES_PARAM, BuiltInParameter.VIEW_DEPENDENCY,
        BuiltInParameter.SHEET_SCHEDULED))
    names, values = {}, {}
    for s in sheets:
        for prm in s.Parameters:
            pid = prm.Id.IntegerValue
            if pid in seen or pid in skip or prm.StorageType not in (StorageType.String,
                                                                     StorageType.Integer):
                continue
            v = prm.AsString() if prm.StorageType == StorageType.String else prm.AsValueString()
            if v:
                names[pid] = prm.Definition.Name
                values.setdefault(pid, set()).add(v)
    labels = set(l for l, _, _ in out)
    for pid, n in sorted(names.items(), key=lambda x: x[1].lower()):
        if len(values[pid]) >= 2 and n not in labels:
            labels.add(n)
            out.append((n, {"param": pid}, False))
    return out


def group_path(doc, sheet, group, levels=None):
    """Sub-folder names for a sheet under the chosen grouping ([] = none)."""
    if not group:
        return []
    if group.get("browser"):
        levels = levels if levels is not None else browser_levels(doc)
        pids = [pid for pid, _ in levels]
    else:
        pids = [int(group["param"])]
    return [_BAD.sub(u"_", _value(sheet, pid)).strip(u" .") or EMPTY for pid in pids]


# ---- the whole run ----------------------------------------------------------------

def plan_sheets(doc, sheets, limit=None):
    """(RO) Paper + weight + mode of each sheet, nothing exported."""
    lim = limit or limit_now()
    crash = crash_memory(doc)
    wg = Weigher(doc)
    out = []
    for s in sheets:
        a = assess(doc, s, wg, lim, crash)
        out.append({"sheet": s, "paper": paper(doc, s), "weight": a["weight"],
                    "mode": a["mode"], "parts": len(a["parts"] or [1]), "note": a["note"],
                    "tiled": a["tiled"]})
    return lim, out


RETRIES = 5    # sheet -> each view alone -> view alone again -> 2x2 -> 3x3 -> 4x4 tiles


def _shape(plan):
    return (plan["mode"], [(sorted(i.IntegerValue for i in pt["vps"]), pt.get("tiles", 1))
                           for pt in plan["parts"] or []])


def run(doc, sheets, folder, group=None, progress=None, cancelled=None, limit=None,
        min_free=MIN_FREE_MB, allow_incomplete=False):
    """Export sheets (in this order): one vector PDF per sheet, in folder or in its
    sub-folders by `group`. Never joined into one file.

    progress(i, n, text) and cancelled() are optional callbacks. allow_incomplete:
    export even with worksets of the model closed / links not loaded (on purpose).
    Returns a summary dict (files, counts, stopped reason, crash notes).
    """
    app = doc.Application
    levels = browser_levels(doc) if group and group.get("browser") else None
    key = u",".join(str(s.Id.IntegerValue) for s in sheets) + u"|" + dumps(group)
    jr = Journal(folder, key)
    resumed = jr.resumed()
    learnt = recover(doc)          # the pass Revit died on last time, if any
    crash = crash_memory(doc)
    wg = Weigher(doc)
    done, skipped, failed, split, tiled = [], [], [], [], []
    subfolders = set()
    stopped = None
    closed = [] if allow_incomplete else closed_worksets(doc)
    if closed:
        stopped = u"workset của model đang đóng, view sẽ in thiếu phần tử: " + u", ".join(closed)
    with LowPriority():
        for i, s in enumerate([] if closed else sheets):
            sid = s.Id.IntegerValue
            st = jr.state(sid)
            if st.get("state") == "done" and os.path.exists(st.get("file", u"")):
                done.append((s, st["file"]))
                skipped.append(s)
                continue
            if cancelled and cancelled():
                stopped = u"đã huỷ"
                break
            free = wait_for_memory(app, min_free)
            if free < min_free:
                stopped = u"RAM còn {0:,} MB".format(free)
                break
            links = [] if allow_incomplete else unloaded_links(doc, s)
            if links:
                err = u"link chưa load, view sẽ in thiếu: " + u", ".join(links)
                jr.set(sid, state="failed", error=err)
                failed.append((s, err))
                continue
            lim = limit or limit_now()
            plan = assess(doc, s, wg, lim, crash)
            p = paper(doc, s)
            sub = group_path(doc, s, group, levels)
            target = os.path.join(folder, *sub) if sub else folder
            if not os.path.isdir(target):
                os.makedirs(target)
            if sub:
                subfolders.add(os.path.join(*sub))
            if progress:
                progress(i, len(sheets), u"{0} - {1} · {2}{3}".format(
                    s.SheetNumber, s.Name, p["label"],
                    u" · {0} phần".format(len(plan["parts"])) if plan["mode"] == "parts" else u""))
            jr.set(sid, state="started", weight=plan["weight"], mode=plan["mode"])
            for attempt in range(RETRIES + 1):
                try:
                    path = export_sheet(doc, s, target, plan, p, lambda: tidy(app))
                    jr.set(sid, state="done", file=path)
                    done.append((s, path))
                    if plan["mode"] == "parts":
                        split.append((s, len(plan["parts"])))
                    if plan["tiled"]:
                        tiled.append((s, plan["tiled"]))
                    break
                except ExportFailed as ex:
                    # Revit refused a pass but lives: narrow it now, as after a crash
                    line = learn_failed(doc) if attempt < RETRIES else None
                    new = assess(doc, s, wg, lim, crash_memory(doc)) if line else None
                    if new is None or _shape(new) == _shape(plan):
                        clear_pass(doc)
                        jr.set(sid, state="failed", error=u"{0}".format(ex))
                        failed.append((s, u"{0}".format(ex)))
                        break
                    learnt.append(line)
                    plan = new
                    jr.set(sid, state="started", weight=plan["weight"], mode=plan["mode"])
                    tidy(app)
                except Exception as ex:
                    clear_pass(doc)        # an error Revit survived is not a crash
                    jr.set(sid, state="failed", error=u"{0}".format(ex))
                    failed.append((s, u"{0}".format(ex)))
                    break
            crash = crash_memory(doc)
            tidy(app)
    if not stopped and not failed:
        jr.data["done"] = True
        jr.save()
    return {"folder": folder, "done": done, "skipped": skipped, "failed": failed,
            "split": split, "stopped": stopped, "resumed": resumed, "learnt": learnt,
            "tiled": tiled, "subfolders": sorted(subfolders),
            "total": len(sheets)}


# ---- what to export ---------------------------------------------------------------

def default_folder():
    return settings().get("folder") or os.path.join(
        os.environ.get("USERPROFILE", u"C:\\"), u"Documents", u"PDF")


def remember_folder(folder):
    s = settings()
    s["folder"] = folder
    save_settings(s)


def sheets_of(doc, schedule, picked_ids=None):
    """Printable sheets of the schedule in its row order; picked_ids (sheet ids) narrow it.
    Returns (sheets, all sheet count)."""
    import print_set_sync as ps
    allsheets = ps.schedule_sheets(doc, schedule)[0]
    if not picked_ids:
        return allsheets, len(allsheets)
    want = set(int(i) for i in picked_ids)
    return [s for s in allsheets if s.Id.IntegerValue in want], len(allsheets)
