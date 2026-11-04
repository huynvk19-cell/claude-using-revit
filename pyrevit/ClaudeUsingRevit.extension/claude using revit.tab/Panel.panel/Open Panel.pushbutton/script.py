# -*- coding: utf-8 -*-
"""Show the panel. It lives in the startup engine (startup.py, lib/pane.py), so it keeps running after this
script ends; the button only asks it to show itself."""
from System import AppDomain

host = AppDomain.CurrentDomain.GetData('ClaudeUsingRevit.Pane')
if host is None:
    from pyrevit import forms
    forms.alert(u'Panel chưa sẵn sàng: pyRevit > Reload rồi bấm lại.')
elif not host.show():
    from pyrevit import forms
    forms.alert(u'Panel nằm trong cửa sổ Revit nên Revit chỉ tạo nó lúc khởi động: đóng rồi mở lại Revit là có.')
