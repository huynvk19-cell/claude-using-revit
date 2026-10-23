# -*- coding: utf-8 -*-
"""Delete the highlight regions you created (whole model; Shift+click: this view only)."""
from pyrevit import EXEC_PARAMS, forms, revit

import tasks

doc = revit.doc
args = {"view": doc.ActiveView.Id.IntegerValue} if EXEC_PARAMS.config_mode else {}

try:
    res = tasks.run("highlight_clear", args, doc, revit.uidoc)
except Exception as ex:
    forms.alert(u"Không xoá được highlight:\n{0}".format(ex), exitscript=True)

if not res["removed"]:
    forms.alert(u"Không có highlight nào do {0} tạo{1}.".format(
        doc.Application.Username, u" trong view này" if args else u""))
