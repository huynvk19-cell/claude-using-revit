# -*- coding: utf-8 -*-
"""Standard columns of a sheet-list schedule, and its "No." numbering.

Standard (only ever ADDS - no field or parameter is removed, no heading renamed):
  - shown first, in this order: No., Sheet Number, Sheet Name, Current Revision,
    Current Revision Date (made visible when hidden);
  - added hidden when missing: Checked By, Approved By, Designed By, Drawn By
    (left as they are when already there, shown or hidden; set_signers shows or
    hides the four together);
  - every other field keeps its place after them.
"No." is a sheet instance parameter (shared, fixed GUID) created when the model has
none. The tool alone writes it: 1..n in the schedule's row order, placeholders
included, only on sheets whose value changes; sheets borrowed by someone else are
skipped. Nothing reads it, so it drives no other parameter.
"""
import os
import tempfile

from System import Guid
from System.Collections.Generic import List

from Autodesk.Revit.DB import (
    BuiltInCategory, BuiltInParameter, CheckoutStatus, ExternalDefinitionCreationOptions,
    FilteredElementCollector, GroupTypeId, InstanceBinding, ParameterElement,
    ScheduleFieldId, SpecTypeId, StorageType, Transaction, TransactionGroup,
    WorksharingUtils)

import print_set_sync as ps

NO_NAME = u"No."
NO_GUID = Guid("6d2f1a7c-4b3e-4c8a-9f15-2e7b0c9d4a31")
NO_KEY = "no"
VISIBLE = [NO_KEY,
           int(BuiltInParameter.SHEET_NUMBER),
           int(BuiltInParameter.SHEET_NAME),
           int(BuiltInParameter.SHEET_CURRENT_REVISION),
           int(BuiltInParameter.SHEET_CURRENT_REVISION_DATE)]
HIDDEN = [int(BuiltInParameter.SHEET_CHECKED_BY),
          int(BuiltInParameter.SHEET_APPROVED_BY),
          int(BuiltInParameter.SHEET_DESIGNED_BY),
          int(BuiltInParameter.SHEET_DRAWN_BY)]
LABELS = {NO_KEY: u"No.",
          int(BuiltInParameter.SHEET_NUMBER): u"Sheet Number",
          int(BuiltInParameter.SHEET_NAME): u"Sheet Name",
          int(BuiltInParameter.SHEET_CURRENT_REVISION): u"Revision",
          int(BuiltInParameter.SHEET_CURRENT_REVISION_DATE): u"Revision Date",
          int(BuiltInParameter.SHEET_CHECKED_BY): u"Checked By",
          int(BuiltInParameter.SHEET_APPROVED_BY): u"Approved By",
          int(BuiltInParameter.SHEET_DESIGNED_BY): u"Designed By",
          int(BuiltInParameter.SHEET_DRAWN_BY): u"Drawn By"}


# ---- the "No." parameter ---------------------------------------------------

def _bindings(doc):
    it = doc.ParameterBindings.ForwardIterator()
    while it.MoveNext():
        yield it.Key, it.Current


def no_param(doc):
    """(ParameterElement of "No.", bound to sheets) or (None, False)."""
    pe = None
    for p in FilteredElementCollector(doc).OfClass(ParameterElement):
        try:
            if p.GetDefinition().Name == NO_NAME:
                pe = p
                if getattr(p, "GuidValue", None) == NO_GUID:
                    break
        except Exception:
            continue
    if pe is None:
        return None, False
    cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Sheets)
    for d, b in _bindings(doc):
        if d.Name == NO_NAME and b.Categories.Contains(cat):
            return pe, True
    return pe, False


def is_ours(pe):
    """True when "No." is the parameter this tool creates (not one someone else made)."""
    return getattr(pe, "GuidValue", None) == NO_GUID


def ensure_no_param(doc):
    """Create "No." (or add Sheets to its instance binding). Call inside a transaction."""
    pe, bound = no_param(doc)
    if bound:
        return pe
    app = doc.Application
    cats = app.Create.NewCategorySet()
    cats.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Sheets))
    if pe is not None:
        for d, b in _bindings(doc):
            if d.Name != NO_NAME:
                continue
            if not isinstance(b, InstanceBinding):
                raise Exception(u"Tham số \"No.\" đã có nhưng là tham số type, không gắn được cho sheet.")
            for c in b.Categories:
                cats.Insert(c)
            doc.ParameterBindings.ReInsert(d, app.Create.NewInstanceBinding(cats), GroupTypeId.IdentityData)
            return pe
    old = app.SharedParametersFilename
    path = os.path.join(tempfile.gettempdir(), "sheet_list_no_param.txt")
    open(path, "w").close()
    try:
        app.SharedParametersFilename = path
        group = app.OpenSharedParameterFile().Groups.Create("Sheet list")
        opt = ExternalDefinitionCreationOptions(NO_NAME, SpecTypeId.Int.Integer)
        opt.GUID = NO_GUID
        opt.Description = u"Số thứ tự trong danh mục bản vẽ, do tool đánh."
        definition = group.Definitions.Create(opt)
        doc.ParameterBindings.Insert(definition, app.Create.NewInstanceBinding(cats), GroupTypeId.IdentityData)
    finally:
        try:
            app.SharedParametersFilename = old or u""
        except Exception:
            pass
        try:
            os.remove(path)
        except Exception:
            pass
    return no_param(doc)[0]


# ---- one schedule ----------------------------------------------------------

def _key(field, no_id):
    pid = field.ParameterId.IntegerValue
    return NO_KEY if no_id is not None and pid == no_id else pid


def _fields(vs, no_id):
    d = vs.Definition
    out = {}
    for i in range(d.GetFieldCount()):
        f = d.GetField(i)
        out.setdefault(_key(f, no_id), f)
    return out


def _visible_keys(vs, no_id):
    d = vs.Definition
    return [_key(d.GetField(i), no_id) for i in range(d.GetFieldCount()) if not d.GetField(i).IsHidden]


def _sorted_by_no(vs, no_field):
    if no_field is None:
        return False
    d = vs.Definition
    for i in range(d.GetSortGroupFieldCount()):
        if d.GetSortGroupField(i).FieldId.IntegerValue == no_field.FieldId.IntegerValue:
            return True
    return False


def _read_no(p):
    if p is None or not p.HasValue:
        return None
    if p.StorageType == StorageType.Integer:
        return p.AsInteger()
    if p.StorageType == StorageType.Double:
        return int(round(p.AsDouble()))
    try:
        return int((p.AsString() or u"").strip())
    except Exception:
        return None


def _write_no(p, n):
    if p.StorageType == StorageType.Integer:
        p.Set(n)
    elif p.StorageType == StorageType.Double:
        p.Set(float(n))
    else:
        p.Set(str(n))


def busy(doc, el):
    return doc.IsWorkshared and \
        WorksharingUtils.GetCheckoutStatus(doc, el.Id) == CheckoutStatus.OwnedByOtherUser


def plan(doc, vs, pe=None):
    """What applying the standard would change in schedule vs."""
    if pe is None:
        pe, bound = no_param(doc)
        pe = pe if bound else None
    no_id = pe.Id.IntegerValue if pe is not None else None
    fields = _fields(vs, no_id)
    add_visible = [k for k in VISIBLE if k not in fields]
    add_hidden = [k for k in HIDDEN if k not in fields]
    show = [k for k in VISIBLE if k in fields and fields[k].IsHidden]
    reorder = _visible_keys(vs, no_id)[:len(VISIBLE)] != VISIBLE
    sheets = ps.schedule_sheets(doc, vs, with_placeholders=True)[0]
    definition = pe.GetDefinition() if pe is not None else None
    numbers, skipped = [], []
    for i, s in enumerate(sheets):
        old = _read_no(s.get_Parameter(definition)) if definition is not None else None
        if old != i + 1:
            if busy(doc, s):
                skipped.append(s)
            else:
                numbers.append((s, old, i + 1))
    last, creator, held = ps.owner(doc, vs)
    return {
        "schedule": vs, "sheets": sheets, "add_visible": add_visible, "add_hidden": add_hidden,
        "show": show, "reorder": reorder, "numbers": numbers, "skipped": skipped,
        "sorted_by_no": _sorted_by_no(vs, fields.get(NO_KEY)),
        "columns_ok": not (add_visible or add_hidden or show or reorder),
        "in_sync": not (add_visible or add_hidden or show or reorder or numbers),
        "busy": held, "others": ps.is_others(doc, vs), "last": last, "creator": creator,
    }


def _apply_columns(doc, vs, no_id):
    d = vs.Definition
    fields = _fields(vs, no_id)
    schedulable = {}
    for sf in d.GetSchedulableFields():
        k = NO_KEY if no_id is not None and sf.ParameterId.IntegerValue == no_id else sf.ParameterId.IntegerValue
        schedulable.setdefault(k, sf)
    added = 0
    for k in VISIBLE + HIDDEN:
        if k in fields:
            continue
        sf = schedulable.get(k)
        if sf is None:
            continue
        f = d.AddField(sf)
        if k == NO_KEY:
            f.ColumnHeading = NO_NAME
        f.IsHidden = k in HIDDEN
        fields[k] = f
        added += 1
    for k in VISIBLE:
        if k in fields and fields[k].IsHidden:
            fields[k].IsHidden = False
    first = [fields[k].FieldId for k in VISIBLE if k in fields]
    first_ids = set(f.IntegerValue for f in first)
    order = list(first) + [f for f in d.GetFieldOrder() if f.IntegerValue not in first_ids]
    d.SetFieldOrder(List[ScheduleFieldId](order))
    return added


def signers_shown(doc, schedules):
    """True when every schedule shows all of Checked / Approved / Designed / Drawn By."""
    for vs in schedules:
        fields = _fields(vs, None)
        if any(k not in fields or fields[k].IsHidden for k in HIDDEN):
            return False
    return bool(schedules)


def set_signers(doc, schedules, show):
    """Show or hide the four signer columns together (added first when missing).
    Other hidden fields are left alone. Returns the number of fields changed."""
    changed = 0
    t = Transaction(doc, u"Hiện cột ký" if show else u"Ẩn cột ký")
    t.Start()
    try:
        for vs in schedules:
            d = vs.Definition
            fields = _fields(vs, None)
            schedulable = dict((sf.ParameterId.IntegerValue, sf) for sf in d.GetSchedulableFields())
            for k in HIDDEN:
                f = fields.get(k)
                if f is None and k in schedulable:
                    f = d.AddField(schedulable[k])
                    f.IsHidden = not show
                    changed += 1
                elif f is not None and f.IsHidden == show:
                    f.IsHidden = not show
                    changed += 1
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    return changed


# ---- one value for every sheet ---------------------------------------------

SKIP_FILL = (int(BuiltInParameter.SHEET_NUMBER), int(BuiltInParameter.SHEET_NAME))


def schedules_sheets(doc, schedules):
    """Sheets shown by the schedules (placeholders included), each once."""
    out, seen = [], set()
    for vs in schedules:
        for s in ps.schedule_sheets(doc, vs, with_placeholders=True)[0]:
            if s.Id.IntegerValue not in seen:
                seen.add(s.Id.IntegerValue)
                out.append(s)
    return out


def fillable(doc, sheets):
    """[(param id, name)] of text parameters the user may set on the sheets:
    signer columns first, then the rest by name. Not Sheet Number / Name (unique
    per sheet), not No. (the tool numbers it), not read-only ones."""
    if not sheets:
        return []
    pe, bound = no_param(doc)
    skip = set(SKIP_FILL)
    if bound:
        skip.add(pe.Id.IntegerValue)
    found = {}
    for p in sheets[0].Parameters:
        if p.IsReadOnly or p.StorageType != StorageType.String or p.Id.IntegerValue in skip:
            continue
        found.setdefault(p.Id.IntegerValue, p.Definition.Name)
    first = [k for k in HIDDEN if k in found]
    rest = sorted((k for k in found if k not in first), key=lambda k: found[k].lower())
    return [(k, found[k]) for k in first + rest]


def _param(el, pid):
    for p in el.Parameters:
        if p.Id.IntegerValue == pid:
            return p
    return None


def current_values(sheets, pid):
    """{value: count} of a parameter over the sheets ("" = empty)."""
    out = {}
    for s in sheets:
        p = _param(s, pid)
        v = (p.AsString() or u"") if p is not None else u""
        out[v] = out.get(v, 0) + 1
    return out


def fill(doc, sheets, pid, value):
    """Set parameter pid to value on every sheet (empty value clears it). Only
    sheets whose value differs are written; borrowed ones are skipped."""
    written, skipped = 0, []
    t = Transaction(doc, u"Gán giá trị cho sheet")
    t.Start()
    try:
        for s in sheets:
            p = _param(s, pid)
            if p is None or p.IsReadOnly or (p.AsString() or u"") == value:
                continue
            if busy(doc, s):
                skipped.append(s)
                continue
            p.Set(value)
            written += 1
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    return written, skipped


def _number(doc, schedules, pe):
    """Write No. for each schedule in turn. A sheet already numbered by an earlier
    schedule of this run keeps that number (reported as a clash)."""
    done, written, clashes, skipped = {}, 0, [], []
    definition = pe.GetDefinition()
    for vs in schedules:
        sheets = ps.schedule_sheets(doc, vs, with_placeholders=True)[0]
        for i, s in enumerate(sheets):
            sid = s.Id.IntegerValue
            if sid in done:
                if done[sid][0] != i + 1:
                    clashes.append((s, vs.Name, done[sid][1]))
                continue
            done[sid] = (i + 1, vs.Name)
            p = s.get_Parameter(definition)
            if p is None or _read_no(p) == i + 1:
                continue
            if busy(doc, s) or p.IsReadOnly:
                skipped.append(s)
                continue
            _write_no(p, i + 1)
            written += 1
    return written, clashes, skipped


def apply(doc, schedules, columns=True):
    """Columns (when columns) + numbering for the schedules. Returns a result dict."""
    res = {"added": 0, "written": 0, "clashes": [], "skipped": [], "created_param": False}
    tg = TransactionGroup(doc, u"Chuẩn cột danh mục bản vẽ")
    tg.Start()
    try:
        t = Transaction(doc, u"Chuẩn cột danh mục bản vẽ")
        t.Start()
        pe, bound = no_param(doc)
        if not bound:
            if not columns:
                t.RollBack()
                tg.RollBack()
                return res
            res["created_param"] = pe is None
            pe = ensure_no_param(doc)
        if columns:
            for vs in schedules:
                res["added"] += _apply_columns(doc, vs, pe.Id.IntegerValue)
        t.Commit()
        t = Transaction(doc, u"Đánh số No.")
        t.Start()
        res["written"], res["clashes"], res["skipped"] = _number(doc, schedules, pe)
        t.Commit()
        tg.Assimilate()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        if tg.HasStarted() and not tg.HasEnded():
            tg.RollBack()
        raise
    return res


def has_no_field(doc, vs):
    pe, bound = no_param(doc)
    return bound and NO_KEY in _fields(vs, pe.Id.IntegerValue)


def renumber(doc, vs):
    """Numbering only, for a schedule that already shows No.; None when it does not."""
    if not has_no_field(doc, vs):
        return None
    return apply(doc, [vs], columns=False)
