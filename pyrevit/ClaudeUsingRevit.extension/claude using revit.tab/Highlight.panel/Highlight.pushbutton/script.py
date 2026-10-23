# -*- coding: utf-8 -*-
"""Red 50% see-through filled region around each selected object, in its own view."""
from Autodesk.Revit.Exceptions import OperationCanceledException
from Autodesk.Revit.UI.Selection import ObjectType
from pyrevit import forms, revit

import tasks

doc = revit.doc
uidoc = revit.uidoc

ids = list(uidoc.Selection.GetElementIds())
if not ids:
    try:
        refs = uidoc.Selection.PickObjects(
            ObjectType.Element, u"Chọn đối tượng cần highlight rồi bấm Finish")
        ids = [r.ElementId for r in refs]
    except OperationCanceledException:
        ids = []

if ids:
    try:
        res = tasks.run("highlight", {"ids": [i.IntegerValue for i in ids], "detail": True},
                        doc, uidoc)
    except Exception as ex:
        forms.alert(u"Không tạo được highlight:\n{0}".format(ex), exitscript=True)

    per_view, skipped = res["perView"], res["skipped"]
    lines = [u"Đã highlight {0}/{1} đối tượng.".format(res["created"], len(ids))]
    lines += [u"  {0}: {1}".format(n, c) for n, c in sorted(per_view.items())]
    if skipped:
        lines.append(u"Bỏ qua {0}:".format(len(skipped)))
        lines += [u"  " + s for s in skipped[:10]]
    if skipped or len(per_view) > 1:
        forms.alert(u"\n".join(lines))
