# -*- coding: utf-8 -*-
"""Delete the untagged regions you created (whole model; Shift+click: selected / open views)."""
from pyrevit import EXEC_PARAMS, forms, revit

import untagged_marks as um

doc = revit.doc
uidoc = revit.uidoc
views = None
if EXEC_PARAMS.config_mode:
    views = (um.views_from_ids(doc, list(uidoc.Selection.GetElementIds()))
             or um.views_from_ids(doc, [doc.ActiveView.Id]))
    if not views:
        forms.alert(u"Chưa chọn sheet / view nào.", exitscript=True)

try:
    n = um.remove(doc, views)
except Exception as ex:
    forms.alert(u"Không xoá được vùng tô:\n{0}".format(ex), exitscript=True)

if not n:
    forms.alert(u"Không có vùng tô chưa tag nào do {0} tạo{1}.".format(
        doc.Application.Username, u" trong các view này" if views else u""))
