# -*- coding: utf-8 -*-
"""Any schedule as a plain table for the web app grid (Bảng): read it, write cells back.

read(): one row per element the schedule shows (itemized, even when the schedule groups), in
  the schedule's row order: the body rows are read and matched on the texts of all shown
  parameter columns (else on one column unique per element: Sheet Number, Mark...); when fewer
  than half match, the rows follow the schedule's sort / group fields (natural order).
  A schedule that includes elements in links also gets their rows, read-only, as Revit shows them.
  Columns are the schedule fields in order (hidden ones flagged). A cell is editable when the
  field is an instance or type parameter, the parameter is not read-only and holds text, a whole
  number, yes/no or a measurement (an element id - Level, Phase... - stays read-only). Computed
  fields (formula, count, room of a door...) take the text Revit shows in the matched row.
  Locks: an element (or its type) borrowed by another user is locked; one created AND last
  changed by others is flagged "others" and only written with force. In a cloud / workshared
  model the first write to an element borrows it, and Revit may Reload Latest first (slow, and a
  transaction group can no longer roll back what came before it: there is no "write and undo").
write(): the cells in one transaction (undo in Revit = one Ctrl+Z); a type field writes the
  type, so every row of that type changes. Numbers go through SetValueString (project units).
add_sheets(): new sheets for a sheet-list schedule (title block = the one most of its sheets
  use, a placeholder sheet when it has none), then the given cells.
A sheet-list schedule with a print set of its own name gets that set synced after a write
(print_set_sync: same rules, a set borrowed or last changed by someone else is left alone).
"""
import re

from System import Enum

from System.Collections.Generic import List

from Autodesk.Revit.DB import (
    BuiltInCategory, BuiltInParameter, Category, CheckoutStatus, ElementFilter, ElementId, ElementParameterFilter,
    FamilyInstance, FilteredElementCollector, LogicalAndFilter, LogicalOrFilter, ParameterFilterElement,
    ParameterFilterRuleFactory, ParameterFilterUtilities, ScheduleFieldType, SectionType, SpecTypeId, StorageType,
    RevitLinkInstance, Transaction, View, ViewSchedule, ViewSheet, WorksharingUtils)

import print_set_sync as ps

SHEETS_CAT = int(BuiltInCategory.OST_Sheets)
ROLES = {int(BuiltInParameter.SHEET_NUMBER): "sheetNumber", int(BuiltInParameter.SHEET_NAME): "sheetName"}
PARAM_FIELDS = (ScheduleFieldType.Instance, ScheduleFieldType.ElementType)
YES = (u"yes", u"y", u"1", u"true", u"có", u"x", u"✓")
NO = (u"no", u"n", u"0", u"false", u"không", u"")


def schedules(doc):
    """Every schedule a user can open (no templates, revision tables or internal keynote lists)."""
    out = []
    for vs in FilteredElementCollector(doc).OfClass(ViewSchedule):
        if vs.IsTemplate or vs.IsTitleblockRevisionSchedule or vs.IsInternalKeynoteSchedule:
            continue
        try:
            d = vs.Definition
            cat = Category.GetCategory(doc, d.CategoryId) if d.CategoryId.IntegerValue != -1 else None
        except Exception:
            continue
        out.append({"id": vs.Id.IntegerValue, "name": vs.Name,
                    "category": cat.Name if cat is not None else u"Nhiều loại",
                    "sheetList": d.CategoryId.IntegerValue == SHEETS_CAT,
                    "key": bool(d.IsKeySchedule)})
    return sorted(out, key=lambda s: (s["category"].lower(), s["name"].lower()))


def find(doc, name_or_id):
    for vs in FilteredElementCollector(doc).OfClass(ViewSchedule):
        if vs.Name == name_or_id or u"{0}".format(vs.Id.IntegerValue) == u"{0}".format(name_or_id):
            return vs
    return None


# ---- parameters -----------------------------------------------------------------------

class _Params(object):
    """get(el, pid): the parameter pid of el, cached definition lookup (fast on thousands)."""
    def __init__(self, doc):
        self.doc, self.defs = doc, {}

    def get(self, el, pid):
        if el is None:
            return None
        if pid < 0:
            try:
                return el.get_Parameter(Enum.ToObject(BuiltInParameter, pid))
            except Exception:
                return None
        if pid not in self.defs:
            pe = self.doc.GetElement(ElementId(pid))
            try:
                self.defs[pid] = pe.GetDefinition() if pe is not None else None
            except Exception:
                self.defs[pid] = None
        d = self.defs[pid]
        return el.get_Parameter(d) if d is not None else None


def _is_yes_no(p):
    try:
        return p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo
    except Exception:
        return False


def kind_of(p):
    """text | int | yesno | number | id (read-only here)."""
    st = p.StorageType
    if st == StorageType.String:
        return "text"
    if st == StorageType.Integer:
        return "yesno" if _is_yes_no(p) else "int"
    if st == StorageType.Double:
        return "number"
    return "id"


def text_of(p):
    if p is None or not p.HasValue:
        return u""
    st = p.StorageType
    if st == StorageType.String:
        return p.AsString() or u""
    if st == StorageType.Integer and _is_yes_no(p):
        return u"Yes" if p.AsInteger() == 1 else u"No"
    s = p.AsValueString()
    if s is None and st == StorageType.Integer:
        s = u"{0}".format(p.AsInteger())
    return s or u""


def set_text(p, value):
    """Write the text the grid holds; raises ValueError with a short reason."""
    value = (value or u"").strip() if p.StorageType != StorageType.String else (value or u"")
    k = kind_of(p)
    if k == "text":
        return p.Set(value)
    if k == "yesno":
        v = value.lower()
        if v in YES:
            return p.Set(1)
        if v in NO:
            return p.Set(0)
        raise ValueError(u"Yes / No")
    if k == "int":
        try:
            return p.Set(int(value or u"0"))
        except Exception:
            raise ValueError(u"cần số nguyên")
    if k == "number":
        if not p.SetValueString(value or u"0"):
            raise ValueError(u"không đọc được số")
        return True
    raise ValueError(u"tham số kiểu id, không sửa ở đây")


# ---- who may change what --------------------------------------------------------------

class _Locks(object):
    def __init__(self, doc):
        self.doc, self.cache, self.mine = doc, {}, set()
        self.me = (doc.Application.Username or u"").lower()

    def of(self, eid):
        """None | ("busy", name) | ("others", name)."""
        if not self.doc.IsWorkshared:
            return None
        if eid not in self.cache:
            res = None
            try:
                i = ElementId(eid)
                info = WorksharingUtils.GetWorksharingTooltipInfo(self.doc, i)
                status = WorksharingUtils.GetCheckoutStatus(self.doc, i)
                if status == CheckoutStatus.OwnedByOtherUser:
                    res = ("busy", info.Owner or u"")
                elif status == CheckoutStatus.OwnedByCurrentUser:
                    self.mine.add(eid)
                else:
                    names = [n for n in (info.LastChangedBy, info.Creator) if n]
                    if names and self.me not in [n.lower() for n in names]:
                        res = ("others", info.LastChangedBy or info.Creator)
            except Exception:
                pass
            self.cache[eid] = res
        return self.cache[eid]


# ---- read -----------------------------------------------------------------------------

def _fields(doc, vs):
    d = vs.Definition
    out, col = [], 0
    for i in range(d.GetFieldCount()):
        f = d.GetField(i)
        ft = f.FieldType
        pid = f.ParameterId.IntegerValue
        out.append({"key": u"f{0}".format(i), "index": i, "heading": f.ColumnHeading or f.GetName(),
                    "name": f.GetName(), "hidden": bool(f.IsHidden),
                    "source": "type" if ft == ScheduleFieldType.ElementType else
                              "instance" if ft == ScheduleFieldType.Instance else u"{0}".format(ft).lower(),
                    "param": pid if ft in PARAM_FIELDS and pid != -1 else None,
                    "body": None if f.IsHidden else col})
        if pid in ROLES and ft == ScheduleFieldType.Instance:
            out[-1]["role"] = ROLES[pid]
        if not f.IsHidden:
            col += 1
    return out


def _body(vs):
    """Body cell texts row by row (column headings included; matched rows are what counts)."""
    try:
        body = vs.GetTableData().GetSectionData(SectionType.Body)
    except Exception:
        return []
    rows = []
    for r in range(body.FirstRowNumber, body.LastRowNumber + 1):
        rows.append([vs.GetCellText(SectionType.Body, r, c).strip()
                     for c in range(body.FirstColumnNumber, body.LastColumnNumber + 1)])
    return rows


def _type_of(doc, el, cache):
    tid = el.GetTypeId()
    if tid is None or tid == ElementId.InvalidElementId:
        return None
    k = tid.IntegerValue
    if k not in cache:
        cache[k] = doc.GetElement(tid)
    return cache[k]


def _match(rows, body, key, shown):
    """(rows in body order, rows not found) matching each body row on the key columns' texts;
    the matched rows take the body's text for computed columns."""
    pending = {}
    for r in rows:
        pending.setdefault(tuple(r["v"][f["index"]].strip() for f in key), []).append(r)
    ordered = []
    for cells in body:
        if any(f["body"] >= len(cells) for f in key):
            continue
        hit = pending.get(tuple(cells[f["body"]] for f in key))
        if not hit:
            continue
        r = hit.pop(0)
        for g in shown:
            if g["param"] is None and g["body"] < len(cells):
                r["v"][g["index"]] = cells[g["body"]]
        ordered.append(r)
    left = [r for rs in pending.values() for r in rs]
    return ordered, left


def _with_linked(rows, body, key, shown, fields):
    """A schedule that includes elements in links: its rows in body order, host elements as usual, the others
    (their elements live in another model) as read-only rows holding the text Revit shows. Headings, blank rows,
    group headers and totals (one filled cell) are left out."""
    pending = {}
    for r in rows:
        pending.setdefault(tuple(r["v"][f["index"]].strip() for f in key), []).append(r)
    heads = [(f["heading"] or u"").strip().lower() for f in shown]
    out = []
    for cells in body:
        filled = [c for c in cells if c]
        if not filled or (len(shown) > 1 and len(filled) == 1) or [c.lower() for c in cells[:len(heads)]] == heads:
            continue
        hit = pending.get(tuple(cells[f["body"]] if f["body"] < len(cells) else u"" for f in key))
        if hit:
            r = hit.pop(0)
            for g in shown:
                if g["param"] is None and g["body"] < len(cells):
                    r["v"][g["index"]] = cells[g["body"]]
            out.append(r)
            continue
        v = [u""] * len(fields)
        for g in shown:
            if g["body"] < len(cells):
                v[g["index"]] = cells[g["body"]]
        out.append({"id": None, "linked": True, "v": v})
    return out + [r for rs in pending.values() for r in rs]


def _natural(text):
    return [(0, float(t), u"") if t[0].isdigit() else (1, 0, t.lower())
            for t in re.findall(r"\d+(?:\.\d+)?|\D+", text or u"")]


def _sorted(vs, rows):
    """Rows by the schedule's sort / group fields (natural order), then id."""
    d = vs.Definition
    rules = []
    for i in range(d.GetSortGroupFieldCount()):
        sg = d.GetSortGroupField(i)
        try:
            rules.append((d.GetFieldIndex(sg.FieldId), u"{0}".format(sg.SortOrder) == u"Descending"))
        except Exception:
            continue
    rows = sorted(rows, key=lambda r: r["id"])
    for idx, desc in reversed(rules):
        rows.sort(key=lambda r: _natural(r["v"][idx]), reverse=desc)
    return rows


def read(doc, vs, limit=20000):
    fields = _fields(doc, vs)
    params, locks, types = _Params(doc), _Locks(doc), {}
    els = list(FilteredElementCollector(doc, vs.Id).WhereElementIsNotElementType())
    linked = bool(vs.Definition.IncludeLinkedFiles)
    if linked and vs.Definition.CategoryId.IntegerValue != int(BuiltInCategory.OST_RvtLinks):
        els = [e for e in els if not isinstance(e, RevitLinkInstance)]    # the collector returns the links themselves
    truncated = len(els) > limit
    els = els[:limit]

    rows, kinds, editable = [], {}, {}
    for el in els:
        typ = None
        vals = []
        for f in fields:
            if f["param"] is None:
                vals.append(u"")
                continue
            target = el
            if f["source"] == "type":
                typ = typ or _type_of(doc, el, types)
                target = typ
            p = params.get(target, f["param"])
            vals.append(text_of(p))
            if p is not None:
                k = kind_of(p)
                kinds.setdefault(f["key"], k)
                if not p.IsReadOnly and k != "id":
                    editable[f["key"]] = True
        row = {"id": el.Id.IntegerValue, "v": vals}
        if typ is not None:
            row["type"] = typ.Id.IntegerValue
        lk = locks.of(el.Id.IntegerValue)
        if lk:
            row["lock"], row["by"] = lk
        elif el.Id.IntegerValue in locks.mine:
            row["mine"] = True          # borrowed by this user already: writing borrows nothing new
        if typ is not None:
            tl = locks.of(typ.Id.IntegerValue)
            if tl:
                row["typeLock"], row["typeBy"] = tl
        if isinstance(el, ViewSheet) and el.IsPlaceholder:
            row["placeholder"] = True
        rows.append(row)

    # row order + computed cells: match the schedule's body rows on the texts of shown parameter
    # columns (all of them, else a single column unique per element), else its sort fields
    shown = [f for f in fields if f["body"] is not None]
    body = _body(vs) if rows or linked else []
    params_shown = [f for f in shown if f["param"] is not None]
    keys = [params_shown] if params_shown else []
    for f in params_shown:
        texts = [r["v"][f["index"]].strip() for r in rows]
        if all(texts) and len(set(texts)) == len(texts):
            keys.append([f])
    order = None
    if linked:
        rows, order = _with_linked(rows, body, params_shown or shown, shown, fields), u"schedule"
    for key in keys if not linked else []:
        ordered, left = _match(rows, body, key, shown)
        if len(ordered) * 2 >= len(rows):
            rows, order = ordered + _sorted(vs, left), u"schedule"
            break
    if order is None:
        rows, order = _sorted(vs, rows), u"sort fields"
    for f in fields:
        f["kind"] = kinds.get(f["key"], "computed" if f["param"] is None else "text")
        f["editable"] = bool(editable.get(f["key"]))
        del f["body"]
    return {"schedule": vs.Name, "id": vs.Id.IntegerValue,
            "sheetList": vs.Definition.CategoryId.IntegerValue == SHEETS_CAT,
            "itemized": bool(vs.Definition.IsItemized), "order": order,
            "user": doc.Application.Username, "workshared": bool(doc.IsWorkshared),
            "columns": fields, "rows": rows, "truncated": truncated}


# ---- write ----------------------------------------------------------------------------

def _field_by_key(vs, key):
    d = vs.Definition
    try:
        i = int(u"{0}".format(key).lstrip(u"f"))
        return d.GetField(i)
    except Exception:
        raise ValueError(u"không có cột {0}".format(key))


def _targets(doc, vs, changes, types):
    """[(target element, ScheduleField, value, change)] with type fields on the type."""
    out = []
    for c in changes:
        el = doc.GetElement(ElementId(int(c["id"])))
        f = _field_by_key(vs, c["col"])
        if el is None:
            out.append((None, f, c.get("value"), c))
            continue
        if f.FieldType == ScheduleFieldType.ElementType:
            el = _type_of(doc, el, types)
        elif f.FieldType != ScheduleFieldType.Instance:
            el = None
        out.append((el, f, c.get("value"), c))
    return out


def write(doc, vs, changes, force=False, dry_run=False, tname=None):
    """changes: [{id, col: "f<i>", value}]. Returns counts + problems (short)."""
    params, locks, types = _Params(doc), _Locks(doc), {}
    res = {"written": 0, "same": 0, "errors": [], "locked": [], "others": [], "typeRows": 0}
    plan, seen = [], {}
    for el, f, value, c in _targets(doc, vs, changes, types):
        where = {"id": c["id"], "col": c["col"]}
        if el is None:
            res["errors"].append(dict(where, error=u"không sửa được cột này"))
            continue
        p = params.get(el, f.ParameterId.IntegerValue)
        if p is None or p.IsReadOnly or kind_of(p) == "id":
            res["errors"].append(dict(where, error=u"tham số chỉ đọc"))
            continue
        lk = locks.of(el.Id.IntegerValue)
        if lk and lk[0] == "busy":
            res["locked"].append(dict(where, by=lk[1]))
            continue
        if lk and not force:
            res["others"].append(dict(where, by=lk[1]))
            continue
        k = (el.Id.IntegerValue, f.ParameterId.IntegerValue)
        if k in seen:          # a type cell edited on several rows: last value wins
            plan[seen[k]] = (p, value, where)
            continue
        if text_of(p) == (value or u""):
            res["same"] += 1
            continue
        seen[k] = len(plan)
        plan.append((p, value, where))
        if f.FieldType == ScheduleFieldType.ElementType:
            res["typeRows"] += 1
    if dry_run or not plan:
        res["toWrite"] = len(plan)
        return res
    t = Transaction(doc, tname or u"Bảng {0}: sửa {1} ô".format(vs.Name, len(plan)))
    t.Start()
    try:
        for p, value, where in plan:
            try:
                set_text(p, value)
                res["written"] += 1
            except Exception as ex:
                res["errors"].append(dict(where, error=u"{0}".format(ex)))
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    return res


def _title_block(doc, sheets):
    """Most used title block type among the sheets, else any title block type, else None."""
    count = {}
    for s in sheets:
        for tb in FilteredElementCollector(doc, s.Id).OfCategory(BuiltInCategory.OST_TitleBlocks) \
                .WhereElementIsNotElementType():
            if isinstance(tb, FamilyInstance):
                k = tb.GetTypeId().IntegerValue
                count[k] = count.get(k, 0) + 1
    if count:
        return ElementId(max(count, key=lambda k: count[k]))
    any_tb = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks) \
        .WhereElementIsElementType().FirstElementId()
    return any_tb if any_tb != ElementId.InvalidElementId else None


def add_sheets(doc, vs, rows, dry_run=False):
    """rows: [{number, name, values: {"f<i>": text}}] -> new sheets, then their other cells."""
    if vs.Definition.CategoryId.IntegerValue != SHEETS_CAT:
        raise ValueError(u"chỉ thêm dòng được ở danh mục bản vẽ (schedule của Sheets)")
    existing = set(s.SheetNumber for s in FilteredElementCollector(doc).OfClass(ViewSheet))
    bad = [r.get("number") for r in rows if not (r.get("number") or u"").strip()
           or r["number"].strip() in existing]
    if bad:
        raise ValueError(u"số sheet trống hoặc đã có: " + u", ".join(u"{0}".format(b) for b in bad))
    if dry_run:
        return {"toCreate": len(rows)}
    sheets = ps.schedule_sheets(doc, vs)[0]
    tb = _title_block(doc, sheets)
    created, cells = [], []
    t = Transaction(doc, u"Bảng {0}: thêm {1} sheet".format(vs.Name, len(rows)))
    t.Start()
    try:
        for r in rows:
            s = ViewSheet.Create(doc, tb) if tb is not None else ViewSheet.CreatePlaceholder(doc)
            s.SheetNumber = r["number"].strip()
            if r.get("name"):
                s.Name = r["name"]
            created.append({"id": s.Id.IntegerValue, "number": s.SheetNumber})
            for k, v in (r.get("values") or {}).items():
                cells.append({"id": s.Id.IntegerValue, "col": k, "value": v})
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    out = {"created": created, "titleBlock": tb.IntegerValue if tb is not None else None}
    if cells:
        out["cells"] = write(doc, vs, cells, force=True)
    return out


def sync_print_set(doc, vs):
    """After a sheet-list change: the print set of the schedule's name follows its rows."""
    if vs.Definition.CategoryId.IntegerValue != SHEETS_CAT:
        return None
    c = ps.compare(doc, vs)
    if c["set"] is None:
        return {"status": u"no print set named {0}".format(vs.Name)}
    if c["in_sync"]:
        return {"status": u"in sync", "sheets": len(c["sheets"])}
    if ps.owner(doc, c["set"])[2]:
        return {"status": u"skipped: print set borrowed by another user"}
    if ps.is_others(doc, c["set"]):
        return {"status": u"skipped: print set last changed by someone else"}
    r = ps.sync(doc, c)
    return {"status": u"updated", "sheets": r["count"], "added": len(c["added"]),
            "removed": len(c["removed"]), "renumbered": r["renumbered"]}


# ---- selected rows into a filter of a view template ------------------------------------

def filter_options(doc):
    """View templates that take filters (others = someone else made and last changed it) + the filters of the model."""
    templates = []
    for v in _filterable_views(doc):
        templates.append({"id": v.Id.IntegerValue, "name": v.Name, "kind": u"{0}".format(v.ViewType),
                          "others": ps.is_others(doc, v), "lastChangedBy": ps.owner(doc, v)[0]})
    filters = [{"id": f.Id.IntegerValue, "name": f.Name} for f in FilteredElementCollector(doc).OfClass(ParameterFilterElement)]
    return {"templates": sorted(templates, key=lambda t: t["name"].lower()),
            "filters": sorted(filters, key=lambda f: f["name"].lower())}


def _filterable_views(doc):
    out = []
    for v in FilteredElementCollector(doc).OfClass(View):
        try:
            if v.IsTemplate and v.AreGraphicsOverridesAllowed():
                out.append(v)
        except Exception:
            continue
    return out


def _rule(p, pid):
    """An equals rule on the value this parameter really holds; None when it has none to compare."""
    st = p.StorageType
    if not p.HasValue:
        return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(pid)
    if st == StorageType.String:
        return ParameterFilterRuleFactory.CreateEqualsRule(pid, p.AsString() or u"", False)
    if st == StorageType.Integer:
        return ParameterFilterRuleFactory.CreateEqualsRule(pid, p.AsInteger())
    if st == StorageType.Double:
        return ParameterFilterRuleFactory.CreateEqualsRule(pid, p.AsDouble(), 1e-6)
    if st == StorageType.ElementId:
        return ParameterFilterRuleFactory.CreateEqualsRule(pid, p.AsElementId())
    return None


def _value_key(p):
    st = p.StorageType
    if not p.HasValue:
        return None
    if st == StorageType.String:
        return (p.AsString() or u"").lower()
    if st == StorageType.Integer:
        return p.AsInteger()
    if st == StorageType.Double:
        return round(p.AsDouble(), 6)
    return p.AsElementId().IntegerValue


def add_to_filter(doc, vs, ids, cols, template_id, filter_name, hide, force=False, dry_run=False):
    """A filter from the values the chosen elements hold in the chosen columns, added to a view template.
    Values of one column are alternatives (OR), columns narrow each other (AND). An existing filter of that
    name is widened with them (OR); a new one is made. Hide = filter off in the template (V/G: Visibility
    unticked), no hide = on. Nothing else in the template changes."""
    params, types = _Params(doc), {}
    fields = []
    for key in cols:
        f = _field_by_key(vs, key)
        if f.FieldType not in PARAM_FIELDS or f.ParameterId.IntegerValue == -1:
            raise ValueError(u"Cột {0}: Revit tính ra, không lọc được".format(f.ColumnHeading or f.GetName()))
        fields.append(f)
    elements = [doc.GetElement(ElementId(int(i))) for i in ids]
    elements = [e for e in elements if e is not None]
    if not elements or not fields:
        raise ValueError(u"Chọn ít nhất một ô")
    allowed = ParameterFilterUtilities.GetAllFilterableCategories()
    cats = set()
    for e in elements:
        if e.Category is None or not allowed.Contains(e.Category.Id):
            raise ValueError(u"Category {0} không dùng được cho filter".format(e.Category.Name if e.Category else u"?"))
        cats.add(e.Category.Id.IntegerValue)
    cat_ids = List[ElementId]([ElementId(c) for c in cats])
    filterable = set(i.IntegerValue for i in ParameterFilterUtilities.GetFilterableParametersInCommon(doc, cat_ids))
    groups, values = [], {}
    for f in fields:
        pid = f.ParameterId
        name = f.ColumnHeading or f.GetName()
        if pid.IntegerValue not in filterable:
            raise ValueError(u"Cột {0}: tham số này Revit không cho dùng trong filter".format(name))
        rules, seen = [], set()
        for e in elements:
            target = _type_of(doc, e, types) if f.FieldType == ScheduleFieldType.ElementType else e
            p = params.get(target, pid.IntegerValue)
            if p is None:
                continue
            k = _value_key(p)
            if k in seen:
                continue
            seen.add(k)
            r = _rule(p, pid)
            if r is not None:
                rules.append(r)
        if not rules:
            raise ValueError(u"Cột {0}: không có giá trị để lọc".format(name))
        values[name] = len(rules)
        one = [ElementParameterFilter(r) for r in rules]
        groups.append(one[0] if len(one) == 1 else LogicalOrFilter(List[ElementFilter](one)))
    built = groups[0] if len(groups) == 1 else LogicalAndFilter(List[ElementFilter](groups))
    template = doc.GetElement(ElementId(int(template_id)))
    if not isinstance(template, View) or not template.IsTemplate:
        raise ValueError(u"Không thấy view template")
    existing = next((x for x in FilteredElementCollector(doc).OfClass(ParameterFilterElement) if x.Name == filter_name), None)
    blocked = []
    if ps.owner(doc, template)[2]:
        raise ValueError(u"View template {0} đang bị người khác mượn".format(template.Name))
    if ps.is_others(doc, template):
        blocked.append(u"View template {0}: {1}".format(template.Name, ps.owner(doc, template)[0]))
    if existing is not None:
        if ps.owner(doc, existing)[2]:
            raise ValueError(u"Filter {0} đang bị người khác mượn".format(filter_name))
        if ps.is_others(doc, existing):
            blocked.append(u"Filter {0}: {1}".format(filter_name, ps.owner(doc, existing)[0]))
    res = {"filter": filter_name, "template": template.Name, "hide": bool(hide), "values": values,
           "categories": [Category.GetCategory(doc, ElementId(c)).Name for c in cats],
           "newFilter": existing is None, "elements": len(elements)}
    if blocked and not force:
        res["blocked"] = blocked
        return res
    if dry_run:
        return res
    t = Transaction(doc, u"Bảng: filter {0}".format(filter_name))
    t.Start()
    try:
        if existing is None:
            pf = ParameterFilterElement.Create(doc, filter_name, cat_ids, built)
        else:
            pf = existing
            cur = set(i.IntegerValue for i in pf.GetCategories())
            if not cats.issubset(cur):
                pf.SetCategories(List[ElementId]([ElementId(c) for c in cur | cats]))
            old = pf.GetElementFilter()
            pf.SetElementFilter(built if old is None else LogicalOrFilter(List[ElementFilter]([old, built])))
        if not template.IsFilterApplied(pf.Id):
            template.AddFilter(pf.Id)
        template.SetFilterVisibility(pf.Id, not hide)
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    res["id"] = pf.Id.IntegerValue
    return res
