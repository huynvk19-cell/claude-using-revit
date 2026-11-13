# -*- coding: utf-8 -*-
"""One red 50% see-through filled region per view around the selected objects (near ones flow together)."""
from Autodesk.Revit.Exceptions import OperationCanceledException
from Autodesk.Revit.UI.Selection import ObjectType
from cur import revit

import liquid_glass as lg
import tasks

doc = revit.doc
uidoc = revit.uidoc

ids = list(uidoc.Selection.GetElementIds())
linked = tasks._selected_in_links(doc, uidoc)
if not ids and not linked:
    try:
        refs = uidoc.Selection.PickObjects(
            ObjectType.Element, u"Chọn đối tượng cần highlight rồi bấm Finish")
        ids = [r.ElementId for r in refs]
    except OperationCanceledException:
        ids = []

if ids or linked:
    try:
        # no ids: the task takes the selection, elements inside links included
        res = tasks.run("highlight", {"detail": True} if linked else {"ids": [i.IntegerValue for i in ids], "detail": True},
                        doc, uidoc)
    except Exception as ex:
        lg.alert(u"Không tạo được highlight:\n{0}".format(ex), exitscript=True)

    per_view, skipped = res["perView"], res["skipped"]
    lines = [u"Đã highlight {0}/{1} đối tượng.".format(res["created"], len(ids) + len(linked))]
    lines += [u"  {0}: {1}".format(n, c) for n, c in sorted(per_view.items())]
    if skipped:
        lines.append(u"Bỏ qua {0}:".format(len(skipped)))
        lines += [u"  " + s for s in skipped[:10]]
    if skipped or len(per_view) > 1:
        lg.alert(u"\n".join(lines))
