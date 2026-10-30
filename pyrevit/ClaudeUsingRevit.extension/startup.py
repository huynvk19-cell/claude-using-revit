# -*- coding: utf-8 -*-
# On pyRevit load/reload: keep %APPDATA%\claude-using-revit\open-docs\<pid>.json current (lib/open_docs.py),
# (re)start this window's web bridge and start the web app server if it is not running (lib/web_bridge.py),
# and open the island at the top of the Revit window (lib/island.py; errors in %APPDATA%\claude-using-revit\island.log).
from pyrevit import HOST_APP
import open_docs
try:
    open_docs.write_state(HOST_APP.uiapp)
except Exception:
    pass
try:
    import web_bridge
    web_bridge.start(HOST_APP.uiapp)
    web_bridge.ensure_webapp()
except Exception as ex:
    from pyrevit.coreutils.logger import get_logger
    get_logger('claude using revit').warning('web bridge not started: %s', ex)
try:
    import island
    island.start(HOST_APP.uiapp)
except Exception:
    import os, traceback
    with open(os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'island.log'), 'a') as f:
        f.write(traceback.format_exc() + '\n')
