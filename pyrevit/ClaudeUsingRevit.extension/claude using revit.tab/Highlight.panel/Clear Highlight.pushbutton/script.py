# -*- coding: utf-8 -*-
"""Delete the highlight regions you created (whole model; Shift+click: this view only)."""
from Autodesk.Revit.DB import Transaction
from System.Collections.Generic import List
from Autodesk.Revit.DB import ElementId
from pyrevit import EXEC_PARAMS, forms, revit

import highlight_regions as hr

doc = revit.doc
user = doc.Application.Username
only_view = doc.ActiveView if EXEC_PARAMS.config_mode else None

regions = hr.find_marked(doc, view=only_view, user=user)
if not regions:
    forms.alert(u"Không có highlight nào do {0} tạo{1}.".format(
        user, u" trong view này" if only_view else u""), exitscript=True)

ids = List[ElementId]([r.Id for r in regions])
t = Transaction(doc, "Clear highlight")
t.Start()
try:
    doc.Delete(ids)
    t.Commit()
except Exception as ex:
    t.RollBack()
    forms.alert(u"Không xoá được highlight:\n{0}".format(ex), exitscript=True)
