# -*- coding: utf-8 -*-
"""Show the Workflow window. It lives in the startup engine (startup.py, lib/workflow_window.py), so it keeps running after
this script ends; the button only asks it to show itself."""
from System import AppDomain

host = AppDomain.CurrentDomain.GetData('ClaudeUsingRevit.Workflow')
if host is None:
    from cur import forms
    forms.alert(u'Workflow chưa sẵn sàng: bấm Tải lại (panel Claude) rồi bấm lại.')
else:
    host.show()
