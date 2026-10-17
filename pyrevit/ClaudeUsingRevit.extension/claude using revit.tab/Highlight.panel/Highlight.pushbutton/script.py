# -*- coding: utf-8 -*-
"""Red 50% see-through filled region around each selected object, in its own view."""
from Autodesk.Revit.DB import Transaction
from Autodesk.Revit.Exceptions import OperationCanceledException
from Autodesk.Revit.UI.Selection import ObjectType
from pyrevit import forms, revit

import highlight_regions as hr

doc = revit.doc
uidoc = revit.uidoc
view = doc.ActiveView

ids = list(uidoc.Selection.GetElementIds())
if not ids:
    try:
        refs = uidoc.Selection.PickObjects(
            ObjectType.Element, u"Chọn đối tượng cần highlight rồi bấm Finish")
        ids = [r.ElementId for r in refs]
    except OperationCanceledException:
        ids = []

if ids:
    elems = [doc.GetElement(i) for i in ids]
    t = Transaction(doc, "Highlight")
    t.Start()
    try:
        created, per_view, skipped = hr.highlight(doc, elems, view)
        t.Commit()
    except Exception as ex:
        t.RollBack()
        forms.alert(u"Không tạo được highlight:\n{0}".format(ex), exitscript=True)

    lines = [u"Đã highlight {0}/{1} đối tượng.".format(created, len(elems))]
    lines += [u"  {0}: {1}".format(n, c) for n, c in sorted(per_view.items())]
    if skipped:
        lines.append(u"Bỏ qua {0}:".format(len(skipped)))
        lines += [u"  " + s for s in skipped[:10]]
    if skipped or len(per_view) > 1:
        forms.alert(u"\n".join(lines))
