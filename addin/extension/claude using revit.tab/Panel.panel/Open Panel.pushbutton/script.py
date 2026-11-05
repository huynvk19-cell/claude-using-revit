# -*- coding: utf-8 -*-
"""Show the window. It lives in the startup engine (startup.py, lib/pane.py), so it keeps running after this
script ends; the button only asks it to show itself."""
from System import AppDomain

host = AppDomain.CurrentDomain.GetData('ClaudeUsingRevit.Pane')
if host is None:
    from cur import forms
    forms.alert(u'Bảng chưa sẵn sàng: bấm Tải lại (panel Claude) rồi bấm lại.')
else:
    host.show()
