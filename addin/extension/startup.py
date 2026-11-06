# -*- coding: utf-8 -*-
# Run by the claude using revit add-in when Revit has started, and again by the button Tải lại (panel Claude):
# keep %APPDATA%\claude-using-revit\open-docs\<pid>.json current (lib/open_docs.py), (re)start this window's web bridge and
# start the web app server if it is installed and not running (lib/web_bridge.py), register the Bảng window (lib/pane.py) and
# the Workflow windows (lib/workflow_window.py), check GitHub for new content (lib/content_update.py).
# It runs in the startup engine, which lives as long as Revit: what it starts keeps running after it ends.
import os
import sys

# Tải lại runs this in a new engine; drop lib modules anyway in case one is cached
_lib = os.path.normcase(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lib'))
for _n, _m in list(sys.modules.items()):
    try:
        if _m is not None and os.path.normcase(os.path.dirname(os.path.abspath(_m.__file__))) == _lib:
            del sys.modules[_n]
    except Exception:
        pass

from cur import HOST_APP, get_logger
import open_docs
try:
    open_docs.write_state(HOST_APP.uiapp)
except Exception:
    pass
try:
    import web_bridge
    web_bridge.start(HOST_APP.uiapp)
    web_bridge.ensure_webapp()
    import pane
    pane.setup(HOST_APP.uiapp)
except Exception as ex:
    get_logger('claude-using-revit').warning('web bridge / panel not started: %s', ex)
    raise
try:
    import workflow_window
    workflow_window.setup(HOST_APP.uiapp)
    import content_update
    content_update.start()
except Exception as ex:
    get_logger('claude-using-revit').warning('workflow window / update not started: %s', ex)
