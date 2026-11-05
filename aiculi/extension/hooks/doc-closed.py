# -*- coding: utf-8 -*-
# Keep %APPDATA%\AiCuli\open-docs\<pid>.json current (lib/open_docs.py).
from culi import HOST_APP
import open_docs
try:
    open_docs.write_state(HOST_APP.uiapp)
except Exception:
    pass