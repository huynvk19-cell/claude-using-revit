# -*- coding: utf-8 -*-
# On pyRevit load/reload: keep %APPDATA%\claude-using-revit\open-docs\<pid>.json current (lib/open_docs.py),
# (re)start this window's web bridge and start the web app server if it is not running (lib/web_bridge.py).
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
for _slot, _end in (('ClaudeUsingRevit.Island', 'close'), ('ClaudeUsingRevit.RibbonStatus', 'stop')):
    _old = AppDomain.CurrentDomain.GetData(_slot)       # removed features (island, ribbon status) still running
    if _old is not None:
        try:
            getattr(_old, _end)()
        except Exception:
            pass
        AppDomain.CurrentDomain.SetData(_slot, None)
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
