# -*- coding: utf-8 -*-
"""Shared tasks: one function per repeated job, used by the ribbon buttons and by
Claude through the Revit MCP command `pyrevit_task` (no windows, short results).

A task is `def name(doc, uidoc, args) -> dict`, registered with @task(help). It only
calls the same lib functions the buttons call, so a job is written once:
  - button      : its window, then the lib function;
  - pyrevit_task: {"task": "<name>", "args": {...}} -> run_json() -> the task -> JSON.
Add a task here and Claude can call it at once (no C# change, no new MCP session).

Common args: dryRun (report only, nothing written), detail (full lists),
limit (list length when not detail, default 20).
"""
from Autodesk.Revit.DB import Element, ElementId, Transaction, View, ViewSheet

import highlight_regions as hr
import pdf_export as pe
import print_set_sync as ps
import sheet_list_columns as slc
import untagged_marks as um
import view_3d as v3d

TASKS = {}     # name -> (function, help)


class TaskError(Exception):
    pass


def task(help_text):
    def deco(f):
        TASKS[f.__name__] = (f, help_text)
        return f
    return deco


def run(name, args, doc, uidoc):
    if name not in TASKS:
        raise TaskError(u"unknown task '{0}'; tasks: {1}".format(name, u", ".join(sorted(TASKS))))
    return TASKS[name][0](doc, uidoc, args or {})


def _default(o):
    if isinstance(o, Element):
        return o.Id.IntegerValue
    return u"{0}".format(o)


def run_json(name, args_json, doc, uidoc):
    """Entry for pyrevit_task: JSON in, JSON out; errors come back as {"error": ...}."""
    import json
    try:
        args = json.loads(args_json) if args_json else {}
        out = run(name, args, doc, uidoc)
    except TaskError as ex:
        out = {"error": u"{0}".format(ex)}
    except Exception as ex:
        import traceback
        out = {"error": u"{0}".format(ex), "trace": traceback.format_exc()[-1500:]}
    # ensure_ascii=True trips on non-ASCII text in IronPython; escape it by hand
    s = json.dumps(out, ensure_ascii=False, default=_default)
    return u"".join(c if ord(c) < 128 else u"\\u%04x" % ord(c) for c in s)


# ---- helpers ------------------------------------------------------------------

def _cut(items, args):
    """Whole list with detail, else the first `limit` (+ a "... N more" line)."""
    items = list(items)
    if args.get("detail"):
        return items
    n = int(args.get("limit", 20))
    return items[:n] + ([u"... {0} more".format(len(items) - n)] if len(items) > n else [])


def _names(args, one, many):
    v = args.get(many) or args.get(one)
    if v is None:
        return []
    return [v] if isinstance(v, basestring) else list(v)    # noqa: F821 (IronPython 2)


def _schedules(doc, uidoc, args):
    """Sheet-list schedules named in args (schedule / schedules), else the active view."""
    allv = ps.sheet_schedules(doc)
    names = _names(args, "schedule", "schedules")
    if not names:
        av = doc.ActiveView.Id.IntegerValue
        found = [s for s in allv if s.Id.IntegerValue == av]
        if found:
            return found
        raise TaskError(u"pass schedule; sheet-list schedules: " + u", ".join(s.Name for s in allv))
    by_name = dict((s.Name, s) for s in allv)
    missing = [n for n in names if n not in by_name]
    if missing:
        raise TaskError(u"not a sheet-list schedule: {0}; have: {1}".format(
            u", ".join(missing), u", ".join(sorted(by_name))))
    return [by_name[n] for n in names]


def _views(doc, uidoc, args):
    """Views to check: ids of views / sheets / viewports (views), sheet numbers (sheets),
    else the selection, else the active view. Sheets bring their placed views."""
    ids = [ElementId(int(i)) for i in (args.get("views") or [])]
    numbers = set(_names(args, "sheet", "sheets"))
    if numbers:
        from Autodesk.Revit.DB import FilteredElementCollector
        for s in FilteredElementCollector(doc).OfClass(ViewSheet):
            if s.SheetNumber in numbers:
                ids.append(s.Id)
                numbers.discard(s.SheetNumber)
        if numbers:
            raise TaskError(u"sheet not found: " + u", ".join(sorted(numbers)))
    if not ids:
        ids = list(uidoc.Selection.GetElementIds()) or [doc.ActiveView.Id]
    views = um.views_from_ids(doc, ids)
    if not views:
        raise TaskError(u"no plan / elevation / section among the given views")
    return views


def _kinds(args):
    kinds = _names(args, "kind", "kinds") or list(um.KINDS)
    bad = [k for k in kinds if k not in um.KINDS]
    if bad:
        raise TaskError(u"unknown kind {0}; kinds: {1}".format(u", ".join(bad), u", ".join(um.KINDS)))
    return kinds


def _owner_line(doc, el):
    last, creator, busy = ps.owner(doc, el)
    return {"lastChangedBy": last, "creator": creator, "borrowedByOther": busy,
            "others": ps.is_others(doc, el)}


# ---- print set = drawing list ---------------------------------------------------

@task(u"(RO) Each sheet-list schedule vs the print set of the same name: sheets, in sync, "
      u"+/- counts. args: schedule(s) optional.")
def print_sets_status(doc, uidoc, args):
    names = set(_names(args, "schedule", "schedules"))
    psets = ps.print_sets(doc)
    rows = []
    for vs in ps.sheet_schedules(doc):
        if names and vs.Name not in names:
            continue
        c = ps.compare(doc, vs, psets)
        rows.append({"schedule": vs.Name, "sheets": len(c["sheets"]),
                     "placeholders": c["placeholders"], "setExists": c["set"] is not None,
                     "inSync": c["in_sync"], "add": len(c["added"]),
                     "remove": len(c["removed"])})
    return {"schedules": rows}


@task(u"Print set named after a sheet-list schedule = its sheets (schedule order, no "
      u"placeholders), then No. renumbered. args: schedule(s) or all, dryRun, force.")
def print_set_sync(doc, uidoc, args):
    # all: every schedule that already has a print set of its name and is out of step.
    # force: replace a set last changed by someone else (never one borrowed right now).
    psets = ps.print_sets(doc)
    if args.get("all"):
        schedules = [vs for vs in ps.sheet_schedules(doc) if vs.Name in psets]
    else:
        schedules = _schedules(doc, uidoc, args)
    out = []
    for vs in schedules:
        c = ps.compare(doc, vs, psets)
        row = {"schedule": vs.Name, "before": len(ps.set_sheet_ids(c["set"])) if c["set"] else 0,
               "after": len(c["sheets"]), "created": c["set"] is None,
               "added": _cut([ps.sheet_label(s) for s in c["added"]], args),
               "removed": _cut([ps.sheet_label(s) for s in c["removed"]], args)}
        renumber = slc.has_no_field(doc, vs) and bool(slc.plan(doc, vs)["numbers"])
        if c["in_sync"] and not renumber:
            row["status"] = "in sync"
        elif not c["sheets"]:
            row["status"] = "skipped: schedule shows no sheet"
        elif c["set"] is not None and not c["in_sync"] and ps.owner(doc, c["set"])[2]:
            row["status"] = "skipped: print set borrowed by another user"
        elif c["set"] is not None and not c["in_sync"] and ps.is_others(doc, c["set"]) \
                and not args.get("force"):
            row.update(_owner_line(doc, c["set"]))
            row["status"] = "skipped: print set last changed by someone else (force to replace)"
        elif args.get("dryRun"):
            row["status"] = ("would renumber No." if c["in_sync"] else
                             "would " + ("create" if c["set"] is None else "update")
                             + (" + renumber No." if renumber else ""))
        elif c["in_sync"]:
            res = slc.renumber(doc, vs)
            row["status"] = "renumbered"
            row["renumbered"] = res["written"] if res else 0
        else:
            res = ps.sync(doc, c)
            row["status"] = "created" if res["created"] else "updated"
            row["after"] = res["count"]
            row["renumbered"] = res["renumbered"]
            if res["renumber_error"]:
                row["renumberError"] = res["renumber_error"]
        out.append(row)
    return {"schedules": out}


# ---- PDF export (Xuất PDF) -----------------------------------------------------

@task(u"Sheets of a sheet-list schedule to PDF: paper from title block, 100%, centred, "
      u"heavy sheets in parts, one PDF per sheet (never joined). args: schedule, sheets, folder, "
      u"groupBy (browser | sheet param name: sub-folders), dryRun, limit, forget (crash memory), "
      u"allowIncomplete (export with closed worksets / unloaded links on purpose).")
def pdf_export(doc, uidoc, args):
    # Blocks Revit until done (minutes on a big set). A run cut short (RAM, crash)
    # carries on where it stopped when called again with the same sheets the same day.
    if args.get("forget"):
        return {"forgotten": pe.forget(doc)}
    vs = _schedules(doc, uidoc, args)[0]
    numbers = set(_names(args, "sheet", "sheets"))
    sheets, total = pe.sheets_of(doc, vs)
    if numbers:
        missing = numbers - set(s.SheetNumber for s in sheets)
        if missing:
            raise TaskError(u"not in {0}: {1}".format(vs.Name, u", ".join(sorted(missing))))
        sheets = [s for s in sheets if s.SheetNumber in numbers]
    if not sheets:
        raise TaskError(u"schedule {0} shows no printable sheet".format(vs.Name))
    limit = int(args["limit"]) if args.get("limit") else None
    if args.get("dryRun"):
        lim, rows = pe.plan_sheets(doc, sheets, limit)
        heavy = [r for r in rows if r["mode"] == "parts" or r["note"]]
        return {"sheets": len(rows), "limit": lim, "freeMB": pe.memory_mb()[0],
                "papers": dict((p, len([r for r in rows if r["paper"]["label"] == p]))
                               for p in set(r["paper"]["label"] for r in rows)),
                "heaviest": _cut([u"{0}: {1:,}".format(r["sheet"].SheetNumber, r["weight"])
                                  for r in sorted(rows, key=lambda r: -r["weight"])], args),
                "split": _cut([u"{0}: {1} parts {2}".format(r["sheet"].SheetNumber, r["parts"],
                                                             r["note"]) for r in heavy], args),
                "crashMemory": pe.crash_memory(doc),
                "closedWorksets": pe.closed_worksets(doc),
                "unloadedLinks": _cut([u"{0}: {1}".format(r["sheet"].SheetNumber, u", ".join(l))
                                       for r, l in ((r, pe.unloaded_links(doc, r["sheet"]))
                                                    for r in rows) if l], args),
                "groupBy": [l for l, spec, _ in pe.group_options(doc, sheets) if spec][:12]}
    folder = args.get("folder") or pe.default_folder()
    group = _pdf_group(doc, sheets, args.get("groupBy"))
    r = pe.run(doc, sheets, folder, group, limit=limit,
               allow_incomplete=bool(args.get("allowIncomplete")))
    return {"folder": r["folder"], "subfolders": _cut(r["subfolders"], args),
            "exported": len(r["done"]) - len(r["skipped"]), "resumedSkipped": len(r["skipped"]),
            "total": r["total"], "stopped": r["stopped"], "learnt": r["learnt"],
            "viewsTiled": [u"{0}: {1}".format(s.SheetNumber, u", ".join(
                u"{0} ({1}x{1})".format(v, n) for v, n in rows)) for s, rows in r["tiled"]],
            "split": _cut([u"{0}: {1} parts".format(s.SheetNumber, n) for s, n in r["split"]], args),
            "failed": _cut([u"{0}: {1}".format(s.SheetNumber, e) for s, e in r["failed"]], args)}


def _pdf_group(doc, sheets, by):
    """groupBy: none / "browser" / a sheet parameter name -> the run's group spec."""
    if not by:
        return None
    if by == "browser":
        if not pe.browser_levels(doc):
            raise TaskError(u"the Project Browser does not group sheets into folders")
        return {"browser": True}
    opts = [(label, spec) for label, spec, _ in pe.group_options(doc, sheets)
            if spec and "param" in spec]
    for label, spec in opts:
        if label == by:
            return spec
    raise TaskError(u"groupBy: browser or one of: " + u", ".join(l for l, _ in opts))


# ---- sheet-list columns (Chuẩn cột danh mục) -----------------------------------

@task(u"Standard columns + No. numbering on sheet-list schedules. args: schedule(s), "
      u"columns (default true; false = numbering only), dryRun, force.")
def sheet_list_columns(doc, uidoc, args):
    schedules = _schedules(doc, uidoc, args)
    plans, blocked = [], []
    for vs in schedules:
        p = slc.plan(doc, vs)
        plans.append({"schedule": vs.Name, "addColumns": len(p["add_visible"]) + len(p["add_hidden"]),
                      "showColumns": len(p["show"]), "reorder": p["reorder"],
                      "numbersToWrite": len(p["numbers"]),
                      "skippedBorrowed": _cut([ps.sheet_label(s) for s in p["skipped"]], args),
                      "inSync": p["in_sync"], "scaleMissing": p["scale_missing"]})
        if p["busy"] or (p["others"] and not args.get("force")):
            blocked.append(vs.Name)
    if args.get("dryRun") or blocked:
        res = {"plan": plans}
        if blocked:
            res["blocked"] = blocked
            res["note"] = u"borrowed by another user, or last changed by someone else (force)"
        return res
    r = slc.apply(doc, schedules, columns=args.get("columns", True))
    return {"plan": plans, "fieldsAdded": r["added"], "numbersWritten": r["written"],
            "createdNoParameter": r["created_param"],
            "clashes": _cut([u"{0}: {1} vs {2}".format(ps.sheet_label(s), a, b)
                             for s, a, b in r["clashes"]], args),
            "skippedBorrowed": _cut([ps.sheet_label(s) for s in r["skipped"]], args)}


@task(u"Show / hide the four signer columns (Checked/Approved/Designed/Drawn By) together. "
      u"args: schedule(s), show (bool).")
def sheet_list_signers(doc, uidoc, args):
    schedules = _schedules(doc, uidoc, args)
    if "show" not in args:
        return {"shown": slc.signers_shown(doc, schedules)}
    return {"fieldsChanged": slc.set_signers(doc, schedules, bool(args["show"]))}


@task(u"One text value for a sheet parameter on every sheet of the schedules (empty = "
      u"clear). args: schedule(s), param (name), value, dryRun.")
def sheet_list_fill(doc, uidoc, args):
    sheets = slc.schedules_sheets(doc, _schedules(doc, uidoc, args))
    params = slc.fillable(doc, sheets)
    by_name = dict((n, pid) for pid, n in params)
    name = args.get("param")
    if not name:
        return {"sheets": len(sheets), "params": [n for _, n in params]}
    if name not in by_name:
        raise TaskError(u"param must be one of: " + u", ".join(n for _, n in params))
    pid = by_name[name]
    now = slc.current_values(sheets, pid)
    if args.get("dryRun") or "value" not in args:
        return {"sheets": len(sheets), "values": now}
    written, skipped = slc.fill(doc, sheets, pid, args["value"] or u"")
    return {"sheets": len(sheets), "written": written,
            "skippedBorrowed": _cut([ps.sheet_label(s) for s in skipped], args)}


# ---- untagged (Highlight Untagged / Remove Untagged) ---------------------------

@task(u"(RO) Rooms / doors / windows / walls / floors a view shows without their tag. "
      u"args: views (ids) or sheets (numbers), kinds, detail.")
def untagged_scan(doc, uidoc, args):
    views, kinds = _views(doc, uidoc, args), _kinds(args)
    found = um.scan(doc, views, kinds)
    rows = []
    for v in views:
        fv = found[v.Id.IntegerValue]
        row = {"view": v.Name, "id": v.Id.IntegerValue}
        for k in kinds:
            if fv[k]:
                row[k] = len(fv[k])
                if args.get("detail"):
                    row[k + "Ids"] = [e.Id.IntegerValue for e in fv[k]]
        rows.append(row)
    return {"total": um.unique_counts(views, found, kinds), "views": rows}


@task(u"Orange 50% region around everything untagged (replaces your earlier ones). "
      u"args: views or sheets, kinds.")
def untagged_highlight(doc, uidoc, args):
    views, kinds = _views(doc, uidoc, args), _kinds(args)
    report, problems = um.highlight_views(doc, views, kinds)
    return {"views": [dict([("view", v.Name)] + [(k, n) for k, n in c.items() if n])
                      for v, c in report],
            "problems": _cut(problems, args)}


@task(u"Delete your untagged regions. args: views or sheets (default: whole model).")
def untagged_remove(doc, uidoc, args):
    views = _views(doc, uidoc, args) if (args.get("views") or args.get("sheets")) else None
    return {"removed": um.remove(doc, views)}


# ---- highlight (Highlight / Clear Highlight) ------------------------------------

@task(u"Red 50% region around elements (annotations: in their own view; model elements: "
      u"in view). args: ids (default: selection), view (id; default: active view).")
def highlight(doc, uidoc, args):
    ids = [ElementId(int(i)) for i in (args.get("ids") or [])] or \
        list(uidoc.Selection.GetElementIds())
    if not ids:
        raise TaskError(u"pass ids or select elements")
    view = doc.GetElement(ElementId(int(args["view"]))) if args.get("view") else doc.ActiveView
    if not isinstance(view, View):
        raise TaskError(u"view not found: {0}".format(args.get("view") or u"no active view"))
    elems = [e for e in (doc.GetElement(i) for i in ids) if e is not None]
    t = Transaction(doc, "Highlight")
    t.Start()
    try:
        created, per_view, skipped = hr.highlight(doc, elems, view)
        t.Commit()
    except Exception:
        t.RollBack()
        raise
    return {"created": created, "perView": per_view, "skipped": _cut(skipped, args)}


@task(u"Delete your highlight regions. args: view (id; default: whole model).")
def highlight_clear(doc, uidoc, args):
    from System.Collections.Generic import List
    view = doc.GetElement(ElementId(int(args["view"]))) if args.get("view") else None
    if args.get("view") and not isinstance(view, View):
        raise TaskError(u"view not found: {0}".format(args["view"]))
    regions = hr.find_marked(doc, view=view, user=doc.Application.Username)
    if not regions:
        return {"removed": 0}
    t = Transaction(doc, "Clear highlight")
    t.Start()
    try:
        doc.Delete(List[ElementId]([r.Id for r in regions]))
        t.Commit()
    except Exception:
        t.RollBack()
        raise
    return {"removed": len(regions)}


# ---- 3D from view (3D theo view) -----------------------------------------------

@task(u"Your own 3D view '3D Box - <user>' (reused) boxed to a plan (crop x view range) "
      u"or section / elevation (crop x far clip), then opened. args: view (id; default: "
      u"active view or the viewport selected on a sheet), cut (plans: stop at the cut plane), "
      u"activate (default true), dryRun.")
def view_3d_from_view(doc, uidoc, args):
    view = doc.GetElement(ElementId(int(args["view"]))) if args.get("view") else None
    if args.get("view") and not isinstance(view, View):
        raise TaskError(u"view not found: {0}".format(args["view"]))
    try:
        src = v3d.source_view(doc, uidoc, view)
        if args.get("dryRun"):
            box, info = v3d.box_for(doc, src, bool(args.get("cut")))
            size = box.Max - box.Min
            info.update({"source": src.Name, "sourceType": u"{0}".format(src.ViewType),
                         "view3d": v3d.box_view_name(doc),
                         "exists": v3d.find_box_view(doc) is not None,
                         "sizeMm": [int(round(c / v3d.MM)) for c in (size.X, size.Y, size.Z)]})
            return info
        view3d, info = v3d.apply(doc, src, bool(args.get("cut")))
    except v3d.View3DError as ex:
        raise TaskError(u"{0}".format(ex))
    if args.get("activate", True):
        v3d.show(uidoc, view3d)
        info["opened"] = True
    return info


# ---- data for the web app screens (no windows) --------------------------------------

@task(u"(RO) Sheets a sheet-list schedule shows, in its row order (id, number, name, selected), "
      u"the PDF folder used last and the groupBy choices. args: schedule.")
def schedule_sheets(doc, uidoc, args):
    vs = _schedules(doc, uidoc, args)[0]
    sheets, total = pe.sheets_of(doc, vs)
    sel = set(i.IntegerValue for i in uidoc.Selection.GetElementIds())
    group = [u"browser"] if pe.browser_levels(doc) else []
    group += [l for l, spec, _ in pe.group_options(doc, sheets) if spec and "param" in spec]
    return {"schedule": vs.Name, "total": total, "pdfFolder": pe.default_folder(),
            "groupBy": group[:13],
            "sheets": [{"id": s.Id.IntegerValue, "number": s.SheetNumber, "name": s.Name,
                        "selected": s.Id.IntegerValue in sel} for s in sheets]}


@task(u"(RO) What each tool has left to do in this model: print sets vs their sheet lists, "
      u"standard columns, untagged in the active view, your highlight / untagged marks. args: parts "
      u"(lists, marks, untagged; default all).")
def status_board(doc, uidoc, args):
    import time
    parts = set(_names(args, "part", "parts") or ["lists", "marks", "untagged"])
    user = doc.Application.Username
    out, ms = {}, {}
    t = time.time()
    if "lists" in parts:
        psets = ps.print_sets(doc)
        sets, cols = [], []
        for vs in ps.sheet_schedules(doc):
            c = ps.compare(doc, vs, psets)
            sets.append({"schedule": vs.Name, "sheets": len(c["sheets"]), "setExists": c["set"] is not None,
                         "inSync": c["in_sync"], "add": len(c["added"]), "remove": len(c["removed"])})
            try:
                p = slc.plan(doc, vs)
                cols.append({"schedule": vs.Name, "inSync": p["in_sync"],
                             "addColumns": len(p["add_visible"]) + len(p["add_hidden"]),
                             "numbersToWrite": len(p["numbers"])})
            except Exception as ex:
                cols.append({"schedule": vs.Name, "error": u"{0}".format(ex)})
        out.update({"printSets": sets, "columns": cols})
        ms["lists"], t = int((time.time() - t) * 1000), time.time()
    if "marks" in parts:
        out.update({"highlights": len(hr.find_marked(doc, user=user)),
                    "untaggedMarks": len(um.find_marked(doc, None, user))})
        ms["marks"], t = int((time.time() - t) * 1000), time.time()
    if "untagged" in parts:
        out["untagged"] = None
        v = doc.ActiveView
        if v is not None and um.is_checkable(v):
            found = um.scan(doc, [v], list(um.KINDS))[v.Id.IntegerValue]
            out["untagged"] = {"view": v.Name, "id": v.Id.IntegerValue,
                               "counts": dict((k, len(found[k])) for k in um.KINDS)}
        ms["untagged"] = int((time.time() - t) * 1000)
    out["ms"] = ms
    return out


@task(u"(RO) Check now and repaint the status on the ribbon tab 'claude using revit' (tab dot, "
      u"panel lines, button dots); returns what it shows. args: board (paint this board instead; preview).")
def ribbon_status_now(doc, uidoc, args):
    import ribbon_status as rs
    from System import AppDomain
    board = args.get("board") or status_board(doc, uidoc, {})
    rs.paint(board)
    tab = rs._tab()
    return {"watching": AppDomain.CurrentDomain.GetData(rs.SLOT) is not None, "tab": tab is not None,
            "tabs": [t.Title for t in rs.aw.ComponentManager.Ribbon.Tabs][:40] if tab is None else None,
            "panels": dict((k, [l[1] for l in v]) for k, v in rs.lines_for(board).items()), "ms": board.get("ms")}


@task(u"Open a view and select elements in it; nothing in the model changes. "
      u"args: view (id), ids.")
def show_elements(doc, uidoc, args):
    from System.Collections.Generic import List
    view = doc.GetElement(ElementId(int(args["view"]))) if args.get("view") else None
    if args.get("view") and not isinstance(view, View):
        raise TaskError(u"view not found: {0}".format(args["view"]))
    if view is not None:
        try:
            uidoc.ActiveView = view
        except Exception:
            uidoc.RequestViewChange(view)     # opens once Revit is idle again
    ids = [ElementId(int(i)) for i in (args.get("ids") or [])]
    ids = List[ElementId]([i for i in ids if doc.GetElement(i) is not None])
    uidoc.Selection.SetElementIds(ids)
    if ids.Count:
        try:
            uidoc.ShowElements(ids)
        except Exception:
            pass
    return {"view": (view or doc.ActiveView).Name, "selected": ids.Count}


@task(u"(RO) This list: every task with its args.")
def list_tasks(doc, uidoc, args):
    return {"tasks": [{"task": n, "help": TASKS[n][1]} for n in sorted(TASKS)]}
