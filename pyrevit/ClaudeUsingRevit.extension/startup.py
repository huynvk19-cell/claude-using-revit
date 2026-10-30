# -*- coding: utf-8 -*-
# On pyRevit load/reload: keep %APPDATA%\claude-using-revit\open-docs\<pid>.json current (lib/open_docs.py),
# (re)start this window's web bridge and start the web app server if it is not running (lib/web_bridge.py),
# and keep the status of each tool on the ribbon tab (lib/ribbon_status.py; errors in
# %APPDATA%\claude-using-revit\ribbon_status.log).
import os
import sys

# the startup engine is kept between reloads: drop our lib modules so a reload runs the installed code
_lib = os.path.normcase(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lib'))
for _n, _m in list(sys.modules.items()):
    try:
        if _m is not None and os.path.normcase(os.path.dirname(os.path.abspath(_m.__file__))) == _lib:
            del sys.modules[_n]
    except Exception:
        pass

from pyrevit import HOST_APP
from System import AppDomain
_island = AppDomain.CurrentDomain.GetData('ClaudeUsingRevit.Island')    # the removed island window, if still open
if _island is not None:
    try:
        _island.close()
    except Exception:
        pass
    AppDomain.CurrentDomain.SetData('ClaudeUsingRevit.Island', None)
import open_docs
try:
    open_docs.write_state(HOST_APP.uiapp)
except Exception:
    pass
try:
    import web_bridge
    reload(web_bridge)      # noqa: F821 (IronPython 2)
    web_bridge.start(HOST_APP.uiapp)
    web_bridge.ensure_webapp()
except Exception as ex:
    from pyrevit.coreutils.logger import get_logger
    get_logger('claude using revit').warning('web bridge not started: %s', ex)
try:
    import ribbon_status
    reload(ribbon_status)   # noqa: F821
    ribbon_status.start(HOST_APP.uiapp)
except Exception:
    import traceback
    with open(os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'ribbon_status.log'), 'a') as f:
        f.write(traceback.format_exc() + '\n')
