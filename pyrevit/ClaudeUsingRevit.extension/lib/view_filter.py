# -*- coding: utf-8 -*-
"""Elements chosen in the view, those inside links too, into a filter of a view template (hide or no hide).

The filter is made of the values the elements hold in the parameters chosen: values of one parameter are
alternatives (OR), parameters narrow each other (AND). The rules are read by the host model, so an element from a
link counts by its value: ids mean nothing across models and are left out, family and type go by name. Linked
elements follow a host filter only where the view shows the link By Host View.
"""
from System import Enum
from System.Collections.Generic import List

from Autodesk.Revit.DB import (BuiltInParameter, Element, ElementFilter, ElementId, FilteredElementCollector,
                               ElementParameterFilter, LabelUtils, LogicalAndFilter, LogicalOrFilter,
                               ParameterFilterUtilities, RevitLinkInstance, SharedParameterElement, StorageType, ViewSchedule, ViewSheet)

import schedule_table as st

FIRST = (BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, BuiltInParameter.ELEM_TYPE_PARAM,
         BuiltInParameter.ELEM_FAMILY_PARAM)
SAME_AS_FIRST = set(int(b) for b in st.FAMILY_NAMES + st.TYPE_NAMES)
TEXT_EMPTY = u"(trống)"


# ---- what is chosen ------------------------------------------------------------------------------

def _link_document(doc, inst):
    """The model of a link instance. Revit can answer None with the link loaded: look among the loaded ones by name."""
    ld = inst.GetLinkDocument()
    if ld is not None:
        return ld
    typ = doc.GetElement(inst.GetTypeId())
    name = Element.Name.__get__(typ) if typ is not None else u""
    for d in doc.Application.Documents:
        if d.IsLinked and (d.Title + u".rvt").lower() == name.lower():
            return d
    return None


def _from_reference(doc, r):
    if r.LinkedElementId == ElementId.InvalidElementId:
        return doc.GetElement(r.ElementId)
    inst = doc.GetElement(r.ElementId)
    ld = _link_document(doc, inst) if isinstance(inst, RevitLinkInstance) else None
    return ld.GetElement(r.LinkedElementId) if ld is not None else None


def from_references(doc, refs):
    """The elements picked, each once, those of a link as the link's own elements."""
    out, seen = [], set()
    for r in refs:
        el = _from_reference(doc, r)
        if el is None or isinstance(el, RevitLinkInstance):
            continue
        key = (el.Document.Title, el.Id.IntegerValue)
        if key not in seen:
            seen.add(key)
            out.append(el)
    return out


def selected(doc, uidoc):
    """(elements selected, whole links selected: a link instance has no values of its own to filter by)."""
    sel = uidoc.Selection
    refs = list(sel.GetReferences())
    holders = set(r.ElementId.IntegerValue for r in refs if r.LinkedElementId != ElementId.InvalidElementId)
    plain, whole = [], 0
    for i in sel.GetElementIds():
        el = doc.GetElement(i)
        if isinstance(el, RevitLinkInstance):
            whole += 0 if i.IntegerValue in holders else 1
        elif el is not None:
            plain.append(el)
    out, seen = [], set()
    for el in plain + from_references(doc, refs):
        key = (el.Document.Title, el.Id.IntegerValue)
        if key not in seen:
            seen.add(key)
            out.append(el)
    return out, whole


def _in_link(el):
    return el.Document.IsLinked


def _usable(elements):
    """(elements a filter can hold, their category ids, names of the categories left out)."""
    allowed = ParameterFilterUtilities.GetAllFilterableCategories()
    usable, cats, skipped = [], set(), {}
    for el in elements:
        c = el.Category
        if c is not None and allowed.Contains(c.Id):
            usable.append(el)
            cats.add(c.Id.IntegerValue)
        else:
            name = c.Name if c is not None else u"?"
            skipped[name] = skipped.get(name, 0) + 1
    return usable, cats, skipped


def _filterable(doc, cats):
    ids = List[ElementId]([ElementId(c) for c in cats])
    return set(i.IntegerValue for i in ParameterFilterUtilities.GetFilterableParametersInCommon(doc, ids))


# ---- values ------------------------------------------------------------------------------------

def _getter(doc, k):
    """How to find a host parameter on an element of any model: a shared one by GUID, a project one by name."""
    if k < 0:
        bip = Enum.ToObject(BuiltInParameter, k)
        return lambda x: x.get_Parameter(bip)
    pe = doc.GetElement(ElementId(k))
    if isinstance(pe, SharedParameterElement):
        guid = pe.GuidValue
        return lambda x: x.get_Parameter(guid)
    name = Element.Name.__get__(pe)
    return lambda x: x.LookupParameter(name)


def _read(get, el):
    p = get(el)
    if p is not None:
        return p
    tid = el.GetTypeId()
    typ = el.Document.GetElement(tid) if tid is not None and tid != ElementId.InvalidElementId else None
    return get(typ) if typ is not None else None


def _column(doc, k, elements, filterable, linked):
    """(element filters, texts) of parameter k over the elements; None when it cannot hold all of them."""
    pid = ElementId(k)
    get = _getter(doc, k)
    by_name = k in st.NAME_FIELDS
    rules, texts, seen = [], [], set()
    for el in elements:
        p = _read(get, el)
        if p is None:
            return None
        if by_name:
            text = st.text_of(p)
            if not text:
                return None
            if text.lower() not in seen:
                seen.add(text.lower())
                try:
                    rules.append(st._text_filter(pid, text, filterable))
                except ValueError:
                    return None
                texts.append(text)
            continue
        kind = p.StorageType
        if kind not in (StorageType.String, StorageType.Integer, StorageType.Double, StorageType.ElementId):
            return None
        if kind == StorageType.ElementId and linked:
            return None
        key = st._value_key(p)
        if key in seen:
            continue
        seen.add(key)
        r = st._rule(p, pid)
        if r is None:
            return None
        rules.append(ElementParameterFilter(r))
        texts.append(st.text_of(p) or TEXT_EMPTY)
    return rules, texts


def _label(doc, k):
    if k < 0:
        try:
            return LabelUtils.GetLabelFor(Enum.ToObject(BuiltInParameter, k))
        except Exception:
            return u"{0}".format(Enum.ToObject(BuiltInParameter, k))
    return Element.Name.__get__(doc.GetElement(ElementId(k)))


def _choices(doc, elements, filterable):
    linked = any(_in_link(e) for e in elements)
    head, rest = [], []
    first = [int(b) for b in FIRST]
    for k in first + sorted(k for k in filterable if k not in first and k not in SAME_AS_FIRST):
        try:
            col = _column(doc, k, elements, filterable, linked)
        except Exception:
            continue
        if col is None or not [t for t in col[1] if t != TEXT_EMPTY]:
            continue
        (head if k in first else rest).append({"id": k, "name": _label(doc, k), "values": col[1]})
    return head + sorted(rest, key=lambda c: c["name"].lower())


def _targets(doc):
    """View templates + the open view when it has no template; (list, the id to start on or None)."""
    items = list(st.filter_options(doc)["templates"])
    view = doc.ActiveView
    start = None
    try:
        tid = view.ViewTemplateId
        if tid != ElementId.InvalidElementId:
            start = tid.IntegerValue
        elif not isinstance(view, (ViewSheet, ViewSchedule)) and view.AreGraphicsOverridesAllowed():
            items.insert(0, {"id": view.Id.IntegerValue, "name": u"View đang mở: {0}".format(view.Name), "kind": u"view",
                             "others": st.ps.is_others(doc, view), "lastChangedBy": st.ps.owner(doc, view)[0]})
            start = view.Id.IntegerValue
    except Exception:
        pass
    return items, start


def options(doc, elements):
    """What this choice of elements can be filtered by, and where the filter can go."""
    usable, cats, skipped = _usable(elements)
    if not usable:
        names = u", ".join(sorted(skipped)) or u"?"
        raise ValueError(u"Category {0} không dùng được cho filter".format(names))
    targets, start = _targets(doc)
    return {"elements": len(usable), "linked": len([e for e in usable if _in_link(e)]),
            "categories": sorted(e.Category.Name for e in dict((c.Category.Id.IntegerValue, c) for c in usable).values()),
            "skipped": skipped, "choices": _choices(doc, usable, _filterable(doc, cats)),
            "targets": targets, "target": start, "filters": st.filter_options(doc)["filters"]}


def default_name(categories, choice):
    """A filter name from what it holds; Revit refuses some characters, apply_filter cleans them."""
    head = u", ".join(categories[:2]) + (u"…" if len(categories) > 2 else u"")
    if choice and len(choice["values"]) == 1:
        return u"{0} - {1}".format(head, choice["values"][0])
    return head


def add(doc, elements, params, target, filter_name, hide, force=False, dry_run=False):
    """The filter of these elements' values in parameters params, into view template (or view) target."""
    usable, cats, skipped = _usable(elements)
    if not usable:
        raise ValueError(u"Không có phần tử nào dùng được cho filter")
    if not params:
        raise ValueError(u"Chọn ít nhất một tham số")
    filterable = _filterable(doc, cats)
    linked = any(_in_link(e) for e in usable)
    groups, values = [], {}
    for k in params:
        k = int(k)
        col = _column(doc, k, usable, filterable, linked)
        if col is None:
            raise ValueError(u"Tham số {0}: không lọc được với các phần tử này".format(_label(doc, k)))
        rules = col[0]
        values[_label(doc, k)] = len(rules)
        groups.append(rules[0] if len(rules) == 1 else LogicalOrFilter(List[ElementFilter](rules)))
    built = groups[0] if len(groups) == 1 else LogicalAndFilter(List[ElementFilter](groups))
    return st.apply_filter(doc, built, cats, target, filter_name, hide, force, dry_run,
                           {"values": values, "elements": len(usable), "skipped": skipped,
                            "linked": len([e for e in usable if _in_link(e)])})
