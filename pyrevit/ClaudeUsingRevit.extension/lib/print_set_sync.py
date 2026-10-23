# -*- coding: utf-8 -*-
"""Print set (View/Sheet Set) kept equal to a sheet-list schedule.

The print set carries the schedule's exact name, so syncing again later is one click:
  - the sheets are the rows the schedule shows (its filters), placeholders left out;
  - their order is the schedule's row order (sheet number column), shown in the
    window; Revit itself keeps a print set unordered;
  - an existing set last changed by someone else is flagged before it is replaced.
"""
from Autodesk.Revit.DB import (
    BuiltInCategory, BuiltInParameter, CheckoutStatus, ElementId, FilteredElementCollector,
    PrintRange, SectionType, Transaction, ViewSchedule, ViewSet, ViewSheet,
    ViewSheetSet, WorksharingUtils)

SHEETS_CAT = int(BuiltInCategory.OST_Sheets)
SHEET_NUMBER = int(BuiltInParameter.SHEET_NUMBER)


def sheet_schedules(doc):
    """Sheet-list schedules of the model, by name."""
    out = []
    for vs in FilteredElementCollector(doc).OfClass(ViewSchedule):
        if vs.IsTemplate or vs.IsTitleblockRevisionSchedule:
            continue
        try:
            if vs.Definition.CategoryId.IntegerValue != SHEETS_CAT:
                continue
        except Exception:
            continue
        out.append(vs)
    return sorted(out, key=lambda v: v.Name.lower())


def print_sets(doc):
    return dict((s.Name, s) for s in FilteredElementCollector(doc).OfClass(ViewSheetSet))


def _number_column(vs):
    """Body column holding Sheet Number, or None (hidden / not in the schedule)."""
    d = vs.Definition
    col = 0
    for i in range(d.GetFieldCount()):
        f = d.GetField(i)
        if f.IsHidden:
            continue
        if f.ParameterId.IntegerValue == SHEET_NUMBER:
            return col
        col += 1
    return None


def schedule_sheets(doc, vs, with_placeholders=False):
    """(sheets in schedule order, placeholder count) for the rows the schedule shows."""
    shown = [s for s in FilteredElementCollector(doc, vs.Id).OfClass(ViewSheet)]
    placeholders = len([s for s in shown if s.IsPlaceholder])
    by_no = dict((s.SheetNumber, s) for s in shown if with_placeholders or not s.IsPlaceholder)
    ordered = []
    col = _number_column(vs)
    if col is not None:
        try:
            body = vs.GetTableData().GetSectionData(SectionType.Body)
            c = body.FirstColumnNumber + col
            for r in range(body.FirstRowNumber, body.LastRowNumber + 1):
                s = by_no.pop(vs.GetCellText(SectionType.Body, r, c).strip(), None)
                if s is not None:
                    ordered.append(s)
        except Exception:
            pass
    ordered += sorted(by_no.values(), key=lambda s: s.SheetNumber)
    return ordered, placeholders


def set_sheet_ids(pset):
    ids = set()
    for v in pset.Views:
        ids.add(v.Id.IntegerValue)
    return ids


def owner(doc, el):
    """(last changed by, creator, owned by someone else now) - empty when not workshared."""
    if not doc.IsWorkshared:
        return u"", u"", False
    info = WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id)
    busy = WorksharingUtils.GetCheckoutStatus(doc, el.Id) == CheckoutStatus.OwnedByOtherUser
    return info.LastChangedBy or u"", info.Creator or u"", busy


def is_others(doc, el):
    """True when the set was neither created nor last changed by the current user."""
    me = (doc.Application.Username or u"").lower()
    last, creator, _ = owner(doc, el)
    names = [n.lower() for n in (last, creator) if n]
    return bool(names) and me not in names


def compare(doc, vs, psets=None):
    """Everything the window shows for one schedule."""
    psets = print_sets(doc) if psets is None else psets
    sheets, placeholders = schedule_sheets(doc, vs)
    pset = psets.get(vs.Name)
    want = set(s.Id.IntegerValue for s in sheets)
    have = set_sheet_ids(pset) if pset else set()
    removed = []
    for i in have - want:
        v = doc.GetElement(ElementId(i))
        if v is not None:
            removed.append(v)
    return {
        "schedule": vs, "sheets": sheets, "placeholders": placeholders, "set": pset,
        "added": [s for s in sheets if s.Id.IntegerValue not in have] if pset else [],
        "removed": sorted(removed, key=lambda v: getattr(v, "SheetNumber", v.Name)),
        "in_sync": pset is not None and want == have,
    }


def save(doc, name, sheets, pset=None):
    """Create the print set `name`, or replace the sheets of `pset`. Returns its sheet count."""
    pm = doc.PrintManager
    vset = ViewSet()
    for s in sheets:
        vset.Insert(s)
    t = Transaction(doc, u"Print set " + name)
    t.Start()
    try:
        pm.PrintRange = PrintRange.Select    # ViewSheetSetting throws otherwise
        vss = pm.ViewSheetSetting
        if pset is not None:
            vss.CurrentViewSheetSet = pset
            vss.CurrentViewSheetSet.Views = vset
            vss.Save()
        else:
            vss.CurrentViewSheetSet.Views = vset
            vss.SaveAs(name)
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    saved = print_sets(doc).get(name)
    return saved.Views.Size if saved else 0


def sheet_label(s):
    return u"{0} - {1}".format(s.SheetNumber, s.Name) if isinstance(s, ViewSheet) else s.Name


def sync(doc, c):
    """Save comparison c as the print set named after its schedule, then renumber the
    schedule's No. column (Chuẩn cột danh mục) when it has one.

    Returns {"count", "created", "renumbered", "renumber_error"}.
    """
    import sheet_list_columns as slc    # it imports this module
    vs = c["schedule"]
    out = {"count": save(doc, vs.Name, c["sheets"], c["set"]), "created": c["set"] is None,
           "renumbered": 0, "renumber_error": None}
    try:
        res = slc.renumber(doc, vs)
        out["renumbered"] = res["written"] if res else 0
    except Exception as ex:
        out["renumber_error"] = u"{0}".format(ex)
    return out
