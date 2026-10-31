# -*- coding: utf-8 -*-
"""Faster views: a light mode per view that can be undone, and a check of what slows
a model down.

Light mode (one view at a time, the user's own views):
  - detail level Coarse, Realistic -> Shading, anti-aliasing / silhouettes / sketchy
    lines / depth cueing off: only settings the view controls itself (a view template
    that controls Detail Level or Graphic Display Options is left alone and reported);
  - imported / linked CAD, Planting, Entourage, point clouds (and Revit links when asked)
    hidden with Temporary Hide, which is not saved with the model.
The view's own values are kept in %APPDATA%/claude-using-revit/view_speed.json (per model,
per view UniqueId) and put back by restore(). Views created and last changed by someone
else are not changed (only the temporary hide), unless forced.

Revit's own graphics engine (hardware acceleration, Revit 2026 Accelerated Graphics) is
not reachable from an add-in; check() reads what it can (Revit.ini, the GPU Revit draws
on) and says where to change it.
"""
import io
import json
import os

from Autodesk.Revit.DB import (
    BuiltInCategory, BuiltInParameter, CheckoutStatus, DisplayStyle, ElementId,
    FilteredElementCollector, FilteredWorksetCollector, Family, FamilyInstance,
    FamilyInstanceFilter, Group, ImportInstance, ModelPathUtils, RevitLinkInstance,
    RevitLinkType, TemporaryViewMode, Transaction, View, ViewDetailLevel, ViewSheet,
    ViewType, Viewport, WorksetKind, WorksharingUtils)

STATE = os.path.join(os.environ.get("APPDATA", u""), u"claude-using-revit", u"view_speed.json")
LIGHT_STYLES = {DisplayStyle.Realistic: DisplayStyle.Shading,
                DisplayStyle.RealisticWithEdges: DisplayStyle.ShadingWithEdges,
                DisplayStyle.Rendering: DisplayStyle.Shading}
HIDE_CATEGORIES = (BuiltInCategory.OST_Planting, BuiltInCategory.OST_Entourage)
NO_LIGHT = (ViewType.DrawingSheet, ViewType.Schedule, ViewType.ColumnSchedule,
            ViewType.PanelSchedule, ViewType.Legend, ViewType.DraftingView, ViewType.Report,
            ViewType.ProjectBrowser, ViewType.SystemBrowser, ViewType.Internal,
            ViewType.Undefined, ViewType.Walkthrough, ViewType.Rendering)


class SpeedError(Exception):
    pass


# ---- saved state ----------------------------------------------------------------

def model_key(doc):
    try:
        if doc.IsWorkshared:
            return ModelPathUtils.ConvertModelPathToUserVisiblePath(
                doc.GetWorksharingCentralModelPath())
    except Exception:
        pass
    return doc.PathName or doc.Title


def _load():
    try:
        with io.open(STATE, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def _save(data):
    s = json.dumps(data, indent=1, ensure_ascii=False)
    s = u"".join(c if ord(c) < 128 else u"\\u%04x" % ord(c) for c in s)
    d = os.path.dirname(STATE)
    if not os.path.isdir(d):
        os.makedirs(d)
    with io.open(STATE, "w", encoding="utf-8") as f:
        f.write(s)


def light_views(doc):
    """{UniqueId: saved entry} of this model's views now in light mode."""
    return _load().get(model_key(doc), {})


def is_light(doc, view):
    return view.UniqueId in light_views(doc)


# ---- one view -------------------------------------------------------------------

def _controlled(doc, view, bip):
    """Name of the view template that controls this parameter, else None."""
    tid = view.ViewTemplateId
    if tid is None or tid == ElementId.InvalidElementId:
        return None
    tpl = doc.GetElement(tid)
    if tpl is None:
        return None
    free = set(i.IntegerValue for i in tpl.GetNonControlledTemplateParameterIds())
    return None if int(bip) in free else tpl.Name


def _others(doc, view):
    """Reason the view is someone else's, else None."""
    if not doc.IsWorkshared:
        return None
    if WorksharingUtils.GetCheckoutStatus(doc, view.Id) == CheckoutStatus.OwnedByOtherUser:
        return u"đang bị người khác mượn"
    me = (doc.Application.Username or u"").lower()
    info = WorksharingUtils.GetWorksharingTooltipInfo(doc, view.Id)
    names = [n.lower() for n in (info.LastChangedBy, info.Creator) if n]
    if names and me not in names:
        return u"của {0}".format(info.LastChangedBy or info.Creator)
    return None


def sheets_of(doc, view):
    """Sheet numbers the view is placed on."""
    out = []
    for vp in FilteredElementCollector(doc).OfClass(Viewport):
        if vp.ViewId == view.Id:
            s = doc.GetElement(vp.SheetId)
            if s is not None:
                out.append(s.SheetNumber)
    return out


def target_view(doc, uidoc, view=None):
    v = view or doc.ActiveView
    if v is None or v.IsTemplate or v.ViewType in NO_LIGHT:
        raise SpeedError(u"Mở một view mặt bằng / mặt cắt / mặt đứng / 3D rồi bấm "
                         u"(view '{0}' là {1}).".format(v.Name if v else u"?",
                                                        v.ViewType if v else u"?"))
    return v


def _hide_ids(doc, view, links):
    """Elements and categories to hide temporarily: what the view shows of them."""
    ids, cats = [], []
    for cls in (ImportInstance,) + ((RevitLinkInstance,) if links else ()):
        ids += [e.Id for e in FilteredElementCollector(doc, view.Id).OfClass(cls)]
    try:
        from Autodesk.Revit.DB import PointCloudInstance
        ids += [e.Id for e in FilteredElementCollector(doc, view.Id).OfClass(PointCloudInstance)]
    except Exception:
        pass
    for bic in HIDE_CATEGORIES:
        cid = ElementId(bic)
        try:
            if (view.CanCategoryBeHiddenTemporary(cid) and not view.GetCategoryHidden(cid)
                    and FilteredElementCollector(doc, view.Id).OfCategory(bic)
                    .WhereElementIsNotElementType().GetElementCount()):
                cats.append(cid)
        except Exception:
            pass
    return ids, cats


def plan_light(doc, view, links=False):
    """What light mode would change in this view: (changes, skipped, hide_ids, hide_cats)."""
    changes, skipped = [], []
    owner = _others(doc, view)
    if view.HasDetailLevel() and view.DetailLevel != ViewDetailLevel.Coarse:
        tpl = _controlled(doc, view, BuiltInParameter.VIEW_DETAIL_LEVEL)
        if tpl:
            skipped.append(u"Detail Level theo View Template '{0}'".format(tpl))
        elif owner:
            skipped.append(u"Detail Level (view {0})".format(owner))
        elif view.CanModifyDetailLevel():
            changes.append(("detail", u"{0}".format(view.DetailLevel), u"Coarse"))
    tpl = _controlled(doc, view, BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS)
    if tpl:
        skipped.append(u"Graphic Display Options theo View Template '{0}'".format(tpl))
    elif owner:
        skipped.append(u"Graphic Display Options (view {0})".format(owner))
    else:
        if view.HasDisplayStyle() and view.CanModifyDisplayStyle() \
                and view.DisplayStyle in LIGHT_STYLES:
            changes.append(("style", u"{0}".format(view.DisplayStyle),
                            u"{0}".format(LIGHT_STYLES[view.DisplayStyle])))
        try:
            m = view.GetViewDisplayModel()
            if m.SmoothEdges:
                changes.append(("smooth", True, False))
            if m.EnableSilhouettes:
                changes.append(("silhouettes", True, False))
        except Exception:
            pass
        try:
            if view.GetSketchyLines().EnableSketchyLines:
                changes.append(("sketchy", True, False))
        except Exception:
            pass
        try:
            if view.CanUseDepthCueing() and view.GetDepthCueing().EnableDepthCueing:
                changes.append(("depthCue", True, False))
        except Exception:
            pass
    ids, cats = ([], [])
    if view.CanUseTemporaryVisibilityModes():
        ids, cats = _hide_ids(doc, view, links)
    return changes, skipped, ids, cats


def _set(doc, view, key, value):
    if key == "detail":
        view.DetailLevel = getattr(ViewDetailLevel, value)
    elif key == "style":
        view.DisplayStyle = getattr(DisplayStyle, value)
    elif key in ("smooth", "silhouettes"):
        m = view.GetViewDisplayModel()
        if key == "smooth":
            m.SmoothEdges = value
        else:
            m.EnableSilhouettes = value
        view.SetViewDisplayModel(m)
    elif key == "sketchy":
        s = view.GetSketchyLines()
        s.EnableSketchyLines = value
        view.SetSketchyLines(s)
    elif key == "depthCue":
        d = view.GetDepthCueing()
        d.EnableDepthCueing = value
        view.SetDepthCueing(d)


def apply_light(doc, view, links=False, dry_run=False):
    """Put one view in light mode; returns a report dict."""
    from System.Collections.Generic import List
    if is_light(doc, view):
        raise SpeedError(u"View '{0}' đang ở chế độ nhẹ.".format(view.Name))
    changes, skipped, ids, cats = plan_light(doc, view, links)
    rep = {"view": view.Name, "id": view.Id.IntegerValue,
           "change": [u"{0}: {1} -> {2}".format(k, a, b) for k, a, b in changes],
           "skipped": skipped, "hideElements": len(ids), "hideCategories": len(cats),
           "onSheets": sheets_of(doc, view)}
    if dry_run:
        return rep
    temp_was_on = view.IsTemporaryHideIsolateActive()
    done, failed = [], []
    t = Transaction(doc, "Light view")
    t.Start()
    try:
        for k, old, new in changes:
            try:
                _set(doc, view, k, new)
                done.append([k, old])
            except Exception as ex:
                failed.append(u"{0}: {1}".format(k, ex))
        if ids:
            view.HideElementsTemporary(List[ElementId](ids))
        if cats:
            view.HideCategoriesTemporary(List[ElementId](cats))
        t.Commit()
    except Exception:
        t.RollBack()
        raise
    data = _load()
    data.setdefault(model_key(doc), {})[view.UniqueId] = {
        "name": view.Name, "saved": done, "tempHide": bool(ids or cats),
        "tempWasOn": temp_was_on}
    _save(data)
    rep["failed"] = failed
    rep["tempWasOn"] = temp_was_on
    return rep


def restore(doc, views=None):
    """Put views back as they were (views: View list, None = every light view of the model)."""
    data = _load()
    key = model_key(doc)
    saved = data.get(key, {})
    todo = []
    for uid, entry in list(saved.items()):
        v = doc.GetElement(uid)
        if views is not None and not any(x.UniqueId == uid for x in views):
            continue
        todo.append((uid, entry, v))
    if views is not None and not todo:
        raise SpeedError(u"View '{0}' không ở chế độ nhẹ.".format(
            u", ".join(v.Name for v in views)))
    out, kept_temp, failed = [], [], []
    if todo:
        t = Transaction(doc, "Restore view")
        t.Start()
        try:
            for uid, entry, v in todo:
                if isinstance(v, View):
                    for k, old in entry.get("saved", []):
                        try:
                            _set(doc, v, k, old)
                        except Exception as ex:
                            failed.append(u"{0} {1}: {2}".format(v.Name, k, ex))
                    if entry.get("tempHide") and v.IsTemporaryHideIsolateActive():
                        if entry.get("tempWasOn"):
                            kept_temp.append(v.Name)
                        else:
                            v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)
                    out.append(v.Name)
                saved.pop(uid, None)
            t.Commit()
        except Exception:
            t.RollBack()
            raise
    if saved:
        data[key] = saved
    else:
        data.pop(key, None)
    _save(data)
    return {"restored": out, "failed": failed, "tempHideLeftOn": kept_temp}


# ---- check: what slows this model / view down ------------------------------------

def _ini_graphics(app):
    path = os.path.join(os.environ.get("APPDATA", u""), u"Autodesk", u"Revit",
                        u"Autodesk Revit {0}".format(app.VersionNumber), u"Revit.ini")
    out, section = {}, None
    try:
        with io.open(path, "rb") as f:
            raw = f.read()
    except Exception:
        return out
    utf16 = raw[:2] in (b"\xff\xfe", b"\xfe\xff") or (len(raw) > 1 and raw[1:2] == b"\x00")
    lines = raw.decode("utf-16" if utf16 else "utf-8", "ignore").splitlines()
    for line in lines:
        line = line.strip().lstrip(u"﻿")
        if line.startswith(u"[") and line.endswith(u"]"):
            section = line[1:-1]
        elif section == u"Graphics" and u"=" in line:
            k, v = line.split(u"=", 1)
            out[k.strip()] = v.strip()
    return out


def _gpus():
    """[(name, dedicated MB, MB Revit uses on it)]: DirectX adapters + GPU Process Memory."""
    import System
    from Microsoft.Win32 import Registry
    adapters = {}
    try:
        root = Registry.LocalMachine.OpenSubKey(u"SOFTWARE\\Microsoft\\DirectX")
        for sub in root.GetSubKeyNames():
            k = root.OpenSubKey(sub)
            name, luid = k.GetValue(u"Description"), k.GetValue(u"AdapterLuid")
            if name and luid is not None and u"Basic Render" not in name:
                adapters[u"0x{0:08x}".format(int(luid) & 0xffffffff)] = [
                    name, int(k.GetValue(u"DedicatedVideoMemory") or 0) // 1048576, 0]
    except Exception:
        pass
    try:
        from System.Diagnostics import PerformanceCounter, PerformanceCounterCategory, Process
        pid = Process.GetCurrentProcess().Id
        cat = PerformanceCounterCategory(u"GPU Process Memory")
        for inst in cat.GetInstanceNames():
            if inst.startswith(u"pid_{0}_".format(pid)):
                luid = u"0x" + inst.split(u"_luid_")[1].split(u"_")[1][2:].lower()
                pc = PerformanceCounter(u"GPU Process Memory", u"Dedicated Usage", inst, True)
                if luid in adapters:
                    adapters[luid][2] += int(pc.RawValue) // 1048576
    except Exception:
        pass
    return sorted((tuple(a) for a in adapters.values()), key=lambda a: -a[1])


def check_machine(doc):
    import pdf_export as pe
    app = doc.Application
    ini = _ini_graphics(app)
    free, total = pe.memory_mb()
    gpus = _gpus()
    return {"revit": u"{0} ({1})".format(app.VersionName, app.VersionBuild),
            "ramMB": total, "freeMB": free,
            "gpus": [{"name": n, "vramMB": v, "revitUsesMB": u} for n, v, u in gpus],
            "revitIni": ini}


def _count(doc, cls=None, bic=None):
    c = FilteredElementCollector(doc)
    c = c.OfClass(cls) if cls is not None else c.OfCategory(bic)
    return c.WhereElementIsNotElementType().GetElementCount()


def check_model(doc, top=10):
    out = {"model": doc.Title}
    try:
        if doc.PathName and os.path.isfile(doc.PathName):
            out["fileMB"] = int(os.path.getsize(doc.PathName) // 1048576)
    except Exception:
        pass
    out["elements"] = FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount()
    out["warnings"] = len(doc.GetWarnings())
    views = [v for v in FilteredElementCollector(doc).OfClass(View) if not v.IsTemplate]
    out["views"] = len([v for v in views if not isinstance(v, ViewSheet)])
    out["sheets"] = len([v for v in views if isinstance(v, ViewSheet)])
    out["families"] = FilteredElementCollector(doc).OfClass(Family).GetElementCount()

    cad = []
    for e in FilteredElementCollector(doc).OfClass(ImportInstance):
        ov = doc.GetElement(e.OwnerViewId) if e.ViewSpecific else None
        cad.append({"id": e.Id.IntegerValue,
                    "name": e.Category.Name if e.Category else u"?",
                    "linked": bool(e.IsLinked),
                    "view": ov.Name if ov is not None else None})
    out["cad"] = cad
    out["cadImported3d"] = len([c for c in cad if not c["linked"] and c["view"] is None])

    inplace = []
    for f in FilteredElementCollector(doc).OfClass(Family):
        if f.IsInPlace:
            n = 0
            for sid in f.GetFamilySymbolIds():
                n += FilteredElementCollector(doc).WherePasses(
                    FamilyInstanceFilter(doc, sid)).GetElementCount()
            inplace.append({"family": f.Name, "category": f.FamilyCategory.Name
                            if f.FamilyCategory else u"?", "instances": n})
    out["inPlace"] = sorted(inplace, key=lambda r: -r["instances"])

    groups = {}
    for g in FilteredElementCollector(doc).OfClass(Group):
        if g.Category is None or g.Category.Id.IntegerValue != int(BuiltInCategory.OST_IOSModelGroups):
            continue
        name = g.GroupType.Name if g.GroupType else g.Name
        r = groups.setdefault(name, {"group": name, "instances": 0, "members": 0})
        r["instances"] += 1
        r["members"] = max(r["members"], g.GetMemberIds().Count)
    out["modelGroups"] = sorted(groups.values(),
                                key=lambda r: -r["instances"] * r["members"])[:top]
    out["rasterImages"] = _count(doc, bic=BuiltInCategory.OST_RasterImages)
    try:
        from Autodesk.Revit.DB import PointCloudInstance
        out["pointClouds"] = _count(doc, cls=PointCloudInstance)
    except Exception:
        out["pointClouds"] = 0
    links = []
    for lt in FilteredElementCollector(doc).OfClass(RevitLinkType):
        n = FilteredElementCollector(doc).OfClass(RevitLinkInstance).WhereElementIsNotElementType()
        n = len([i for i in n if i.GetTypeId() == lt.Id])
        links.append({"link": lt.Name, "loaded": bool(RevitLinkType.IsLoaded(doc, lt.Id)),
                      "instances": n})
    out["links"] = links
    if doc.IsWorkshared:
        ws = list(FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))
        out["worksets"] = {"open": len([w for w in ws if w.IsOpen]), "total": len(ws)}
    out["lightViews"] = [e.get("name") for e in light_views(doc).values()]
    return out


def check_view(doc, view, top=8):
    out = {"view": view.Name, "id": view.Id.IntegerValue, "type": u"{0}".format(view.ViewType),
           "light": is_light(doc, view)}
    tid = view.ViewTemplateId
    out["template"] = doc.GetElement(tid).Name if tid and tid != ElementId.InvalidElementId \
        and doc.GetElement(tid) else None
    if view.HasDetailLevel():
        out["detail"] = u"{0}".format(view.DetailLevel)
    if view.HasDisplayStyle():
        out["style"] = u"{0}".format(view.DisplayStyle)
    try:
        m = view.GetViewDisplayModel()
        out["antiAliasing"] = bool(m.SmoothEdges)
        out["silhouettes"] = bool(m.EnableSilhouettes)
        out["transparency"] = m.Transparency
    except Exception:
        pass
    try:
        out["sketchy"] = bool(view.GetSketchyLines().EnableSketchyLines)
    except Exception:
        pass
    try:
        out["cropActive"] = bool(view.CropBoxActive)
    except Exception:
        pass
    p = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR)
    if p is not None and view.ViewType in (ViewType.Section, ViewType.Elevation, ViewType.Detail):
        out["farClip"] = bool(p.AsInteger())
        d = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)
        if d is not None and p.AsInteger():
            out["farClipMm"] = int(round(d.AsDouble() * 304.8))
    if view.ViewType == ViewType.ThreeD:
        try:
            out["sectionBox"] = bool(view.IsSectionBoxActive)
        except Exception:
            pass
    cats, n = {}, 0
    for e in FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType():
        n += 1
        c = e.Category.Name if e.Category else u"(không category)"
        cats[c] = cats.get(c, 0) + 1
    out["elementsShown"] = n
    out["topCategories"] = [u"{0}: {1}".format(c, k) for c, k in
                            sorted(cats.items(), key=lambda x: -x[1])[:top]]
    out["linksShown"] = [e.Name for e in FilteredElementCollector(doc, view.Id)
                         .OfClass(RevitLinkInstance)]
    out["cadShown"] = [(e.Category.Name if e.Category else u"?") for e in
                       FilteredElementCollector(doc, view.Id).OfClass(ImportInstance)]
    out["onSheets"] = sheets_of(doc, view)
    return out


def tips(machine, model, view):
    """Plain advice lines, most useful first (Vietnamese, exact Revit UI names)."""
    t = []
    ini = machine.get("revitIni", {}) if machine else {}
    if machine:
        g = [x for x in machine["gpus"] if x["revitUsesMB"]]
        if machine["gpus"] and not g:
            t.append(u"Không đọc được Revit đang vẽ bằng card nào; kiểm tra Windows Settings > "
                     u"System > Display > Graphics: Revit.exe = High performance.")
        elif g and len(machine["gpus"]) > 1 and g[0]["vramMB"] < max(x["vramMB"] for x in machine["gpus"]):
            t.append(u"Revit đang vẽ bằng {0}, không phải card mạnh nhất. Windows Settings > System > "
                     u"Display > Graphics: thêm Revit.exe, chọn High performance; cắm màn hình vào "
                     u"card rời.".format(g[0]["name"]))
        if ini.get(u"Antialiasing") == u"1":
            t.append(u"Anti-aliasing đang bật: File > Options > Graphics > Smooth lines with "
                     u"anti-aliasing: bỏ 'Use for all views' (chỉ 'Allow for views'); chế độ nhẹ tắt "
                     u"nó trong từng view.")
        if ini.get(u"OptimizedViewNavigation") == u"0":
            t.append(u"Bật File > Options > Graphics > Simplify display during view navigation.")
    if view:
        if view.get("farClip") is False:
            t.append(u"View '{0}' không có Far Clipping (vẽ hết model phía sau): đặt Far Clipping "
                     u"= Clip without line và Far Clip Offset vừa đủ.".format(view["view"]))
        if view.get("type") == u"ThreeD" and view.get("sectionBox") is False:
            t.append(u"3D '{0}' không bật Section Box: dùng nút '3D theo view' để chỉ vẽ phần cần "
                     u"xem.".format(view["view"]))
        if view.get("detail") == u"Fine" or view.get("antiAliasing") or view.get("silhouettes") \
                or view.get("sketchy") or view.get("style") in (u"Realistic", u"RealisticWithEdges"):
            t.append(u"View '{0}' đang để Fine / anti-aliasing / silhouettes / sketchy / Realistic: "
                     u"bấm 'Chế độ nhẹ' khi làm việc, bấm lại để trả về.".format(view["view"]))
        if view.get("cadShown"):
            t.append(u"View hiện {0} file CAD: chế độ nhẹ ẩn tạm chúng.".format(len(view["cadShown"])))
        if len(view.get("linksShown") or []) and view.get("elementsShown", 0) > 0:
            t.append(u"View hiện {0} link Revit: Shift+click 'Chế độ nhẹ' > ẩn tạm cả link nếu không "
                     u"cần.".format(len(view["linksShown"])))
    if model:
        if model.get("cadImported3d"):
            t.append(u"{0} file CAD import (không link) vào model 3D: nặng nhất là loại này, nên "
                     u"link thay vì import, hoặc đặt Current view only.".format(model["cadImported3d"]))
        if model.get("warnings", 0) > 500:
            t.append(u"{0} warnings: Manage > Warnings, dọn bớt (warnings nhiều làm chậm "
                     u"sync / mở file).".format(model["warnings"]))
        if len(model.get("inPlace", [])) > 20:
            t.append(u"{0} in-place family: chậm khi vẽ lại; family thường nhẹ hơn.".format(
                len(model["inPlace"])))
        ws = model.get("worksets")
        if ws and ws["open"] == ws["total"] and ws["total"] > 3:
            t.append(u"Mở model chỉ với worksets cần làm (Open > Worksets: Specify) cho nhẹ; nhớ "
                     u"mở đủ trước khi xuất PDF.")
        if model.get("lightViews"):
            t.append(u"Đang ở chế độ nhẹ: {0}. Tắt trước khi in / xuất PDF.".format(
                u", ".join(model["lightViews"])))
    return t
