# -*- coding: utf-8 -*-
# On pyRevit load/reload: keep %APPDATA%\claude-using-revit\open-docs\<pid>.json current (lib/open_docs.py).
from pyrevit import HOST_APP
import open_docs
try:
    open_docs.write_state(HOST_APP.uiapp)
except Exception:
    pass