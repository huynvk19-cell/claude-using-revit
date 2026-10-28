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
narrows it down run after run: sheet -> one view per part -> that view left out of
the PDF (always vector, never raster), and the user is told which view it is.
"""
import io
import json
import os
import re
import time
import zlib

from Autodesk.Revit.DB import (
    BrowserOrganization, BuiltInCategory, BuiltInParameter, CheckoutStatus, ElementId,
    ExportPaperFormat, FilteredElementCollector, PageOrientationType, PaperPlacementType,
    GraphicsStyleType, Line, PDFExportOptions, RevitLinkInstance,
    Transaction, TransactionGroup, FailureProcessingResult, FailureSeverity,
    IFailuresPreprocessor,
    ViewDetailLevel, XYZ,
    StorageType, Viewport, ViewSheet, ViewType, WorksharingUtils, ZoomType)
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
# here; a pass still written down at the next run is the one Revit died on:
#   - a pass of several views -> that sheet is isolated: one view per part;
#   - a pass of one view      -> that view is left out (vector only, never raster)
#                                and named, so it can be fixed in the model;
#   - died again on an isolated sheet that cannot be split -> sheet left out.
# Kept per model (document title) across days and folders; forget() clears it once
# the model is fixed.

def _doc_key(doc):
    # Title carries ".rvt" or not depending on the Windows "hide extensions" setting
    return re.sub(u"(?i)\.rvt$", u"", doc.Title)


def crash_memory(doc):
    return settings().get("crash", {}).get(_doc_key(doc), {})


def _crash_save(doc, mem):
    s = settings()
    s.setdefault("crash", {})[_doc_key(doc)] = mem
    save_settings(s)


def forget(doc):
    """Clear the crash memory of this model (after fixing the views); returns its size."""
    mem = crash_memory(doc)
    n = len(mem.get("views", {})) + len(mem.get("sheets", [])) + len(mem.get("dead", []))
    _crash_save(doc, {})
    return n


def mark_pass(doc, sheet, view_ids):
    mem = crash_memory(doc)
    mem["pending"] = {"sheet": sheet.Id.IntegerValue, "views": list(view_ids)}
    _crash_save(doc, mem)


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
    sheets = mem.setdefault("sheets", [])
    if len(p["views"]) == 1:
        vid = str(p["views"][0])
        v = doc.GetElement(ElementId(int(vid)))
        mem.setdefault("views", {})[vid] = "skip"
        line = u"{0}: Revit văng khi vẽ view \"{1}\", lần này bỏ view này ra khỏi PDF".format(
            label, v.Name if v is not None else vid)
    elif p["sheet"] in sheets:
        mem.setdefault("dead", []).append(p["sheet"])
        line = u"{0}: Revit văng cả khi tách view, lần này bỏ qua sheet này".format(label)
    else:
        sheets.append(p["sheet"])
        line = u"{0}: Revit văng ở sheet này, lần này tách từng view để tìm view lỗi".format(label)
    _crash_save(doc, mem)
    return [line]


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
    """Weight of the sheet and how to export it.

    Returns {"weight", "views": [(viewport, weight, notes)], "mode": whole | parts | dead,
             "parts": [[viewport ids]] (first = base part with the title block),
             "skipped": [view names left out], "note"}.
    """
    crash = crash or {}
    bad = crash.get("views", {})
    rows = []
    for vp in viewports(doc, sheet):
        w, notes = weigher.view(doc.GetElement(vp.ViewId))
        rows.append((vp, w, notes))
    total = sum(w for _, w, _ in rows)
    out = {"weight": total, "views": rows, "mode": "whole", "parts": None,
           "skipped": [], "note": u""}
    sid = sheet.Id.IntegerValue
    if sid in crash.get("dead", []):
        out["mode"] = "dead"
        out["note"] = u"Revit văng cả khi tách view"
        return out
    isolate = sid in crash.get("sheets", []) or any(
        str(vp.ViewId.IntegerValue) in bad for vp, _, _ in rows)
    if not isolate and (total <= limit or len(rows) < 2):
        if total > limit:
            out["note"] = u"một view nặng, không chia được"
        return out
    why = _cannot_split(doc, sheet, rows)
    if why:
        out["note"] = why
        if isolate:                      # Revit died on it and it cannot be split
            out["mode"] = "dead"
            out["note"] = u"Revit đã văng ở sheet này và không tách được: " + why
        return out
    out["mode"] = "parts"
    if isolate:
        # base = title block + sheet notes only; each view alone; bad views left out
        parts = [[]]
        for vp, _, _ in rows:
            if bad.get(str(vp.ViewId.IntegerValue)) == "skip":
                out["skipped"].append(doc.GetElement(vp.ViewId).Name)
            else:
                parts.append([vp.Id])
        out["parts"] = parts
        out["note"] = u"tách từng view (lần trước Revit văng)"
        return out
    cap = limit if total > limit else max(1, total // 2)
    groups = []                      # [weight, [viewport ids]] first fit, heaviest first
    for vp, w, _ in sorted(rows, key=lambda r: -r[1]):
        for g in groups:
            if g[0] + w <= cap:
                g[0] += w
                g[1].append(vp.Id)
                break
        else:
            groups.append([w, [vp.Id]])
    if len(groups) < 2:
        groups = [[w, [vp.Id]] for vp, w, _ in rows]
    groups.sort(key=lambda g: g[0])          # lightest first = the base part
    out["parts"] = [g[1] for g in groups]
    if any(w > limit for _, w, _ in rows):
        out["note"] = u"có view vượt ngưỡng một mình"
    return out


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


def _export(doc, sheet, folder, name, p):
    path = os.path.join(folder, name + u".pdf")
    _remove(path)
    ok = doc.Export(folder, List[ElementId]([sheet.Id]), options(p, name))
    if not ok or not os.path.exists(path):
        raise Exception(u"Revit không xuất được {0}".format(name))
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


def export_sheet(doc, sheet, folder, plan, p, tidy_between=None):
    """Export one sheet to folder/<number - name>.pdf (parts joined into one page).

    Part 1 = the sheet minus the viewports of the other parts (title block, sheet
    notes, schedules stay). Part 2.. = only their viewports + two corner anchors.
    Removing / hiding / anchors happen in a transaction group that is rolled back,
    so the model is left exactly as it was. Views left out (crash memory) are in no
    part, so they are blanked in every pass.
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
        for k, part in enumerate(plan["parts"]):
            keep = set(i.IntegerValue for i in part)
            if k == 0:
                drop = [e for e in on_sheet
                        if e.Id.IntegerValue in view_of and e.Id.IntegerValue not in keep]
            else:
                drop = [e for e in on_sheet if e.Id.IntegerValue not in keep]
            # A hidden viewport still prints its view (only its title goes), and taking
            # it off the sheet would drop the tags that point at it (unreferenced). So
            # its view is blanked for the pass: every category hidden, nothing drawn.
            hide = [e.Id for e in drop]
            blank = [doc.GetElement(ElementId(view_of[e.Id.IntegerValue]))
                     for e in drop if e.Id.IntegerValue in view_of]
            mark_pass(doc, sheet, [view_of[i] for i in keep])
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
                if k > 0:
                    _anchors(doc, sheet, box)
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
                    "skipped": a["skipped"]})
    return lim, out


def run(doc, sheets, folder, group=None, progress=None, cancelled=None, limit=None,
        min_free=MIN_FREE_MB):
    """Export sheets (in this order): one vector PDF per sheet, in folder or in its
    sub-folders by `group`. Never joined into one file.

    progress(i, n, text) and cancelled() are optional callbacks.
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
    done, skipped, failed, split, missing_views = [], [], [], [], []
    subfolders = set()
    stopped = None
    with LowPriority():
        for i, s in enumerate(sheets):
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
            lim = limit or limit_now()
            plan = assess(doc, s, wg, lim, crash)
            if plan["mode"] == "dead":
                failed.append((s, plan["note"]))
                jr.set(sid, state="failed", error=plan["note"])
                continue
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
            try:
                path = export_sheet(doc, s, target, plan, p, lambda: tidy(app))
                jr.set(sid, state="done", file=path)
                done.append((s, path))
                if plan["mode"] == "parts":
                    split.append((s, len(plan["parts"])))
                if plan["skipped"]:
                    missing_views.append((s, plan["skipped"]))
            except Exception as ex:
                clear_pass(doc)        # an error Revit survived is not a crash
                jr.set(sid, state="failed", error=u"{0}".format(ex))
                failed.append((s, u"{0}".format(ex)))
            tidy(app)
    if not stopped and not failed:
        jr.data["done"] = True
        jr.save()
    return {"folder": folder, "done": done, "skipped": skipped, "failed": failed,
            "split": split, "stopped": stopped, "resumed": resumed, "learnt": learnt,
            "missing_views": missing_views, "subfolders": sorted(subfolders),
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
