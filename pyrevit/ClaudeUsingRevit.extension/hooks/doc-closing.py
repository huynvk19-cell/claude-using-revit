# -*- coding: utf-8 -*-
# Keep %APPDATA%\claude-using-revit\open-docs\<pid>.json current (lib/open_docs.py), without the closing document.
from pyrevit import HOST_APP, EXEC_PARAMS
import open_docs
try:
    open_docs.write_state(HOST_APP.uiapp, closing=EXEC_PARAMS.event_args.Document)
except Exception:
    pass