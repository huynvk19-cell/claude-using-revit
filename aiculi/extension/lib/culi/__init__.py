# -*- coding: utf-8 -*-
"""The host API of AI Culi for the Python of this extension (it replaces the part of pyRevit the tools used).

    from culi import HOST_APP, EXEC_PARAMS, forms, revit, script

HOST_APP.uiapp                 UIApplication of this Revit window
HOST_APP.proc_screen_scalefactor  screen scale (1.0 = 96 dpi)
EXEC_PARAMS.config_mode        True on Shift+click of the ribbon button
EXEC_PARAMS.event_args         the Revit event args of a hook (hooks/*.py)
revit.doc / revit.uidoc        active document / UIDocument;  revit.ui.get_window_rectangle()
script.get_config() / save_config() / get_bundle_file(name) / exit()
forms.WPFWindow / alert / toast / select_views / select_sheets / pick_folder

The add-in (AiCuli.dll) sets the builtin __culi__ (AiCuli.Python.HostContext) before it runs a script.
"""
import logging


def _ctx():
    try:
        return __culi__  # noqa: F821 (set by the add-in)
    except NameError:
        return None


class _HostApp(object):
    @property
    def uiapp(self):
        c = _ctx()
        return c.UIApp if c is not None else None

    @property
    def app(self):
        u = self.uiapp
        return u.Application if u is not None else None

    @property
    def proc_screen_scalefactor(self):
        c = _ctx()
        return c.ScreenScale if c is not None else 1.0

    @property
    def proc_window(self):
        c = _ctx()
        from System import IntPtr
        return IntPtr(c.MainWindowHandle) if c is not None else IntPtr.Zero

    @property
    def version(self):
        a = self.app
        return a.VersionNumber if a is not None else None


class _ExecParams(object):
    @property
    def config_mode(self):
        c = _ctx()
        return bool(c.ShiftClick) if c is not None else False

    @property
    def event_args(self):
        c = _ctx()
        return c.EventArgs if c is not None else None

    @property
    def command_path(self):
        c = _ctx()
        return c.BundleDir if c is not None else None

    @property
    def command_name(self):
        c = _ctx()
        return c.BundleName if c is not None else None


HOST_APP = _HostApp()
EXEC_PARAMS = _ExecParams()


def data_dir():
    """%APPDATA%\\AiCuli (logs, open-docs, web-bridge, tool-config, ...)."""
    import os
    c = _ctx()
    return c.DataDir if c is not None else os.path.join(os.environ.get('APPDATA', ''), 'AiCuli')


def get_logger(name):
    """A logger that writes to the AI Culi log folder (%APPDATA%\\AiCuli\\logs\\python.log)."""
    import os
    log = logging.getLogger(name)
    if not log.handlers:
        try:
            folder = os.path.join(data_dir(), 'logs')
            if not os.path.isdir(folder):
                os.makedirs(folder)
            h = logging.FileHandler(os.path.join(folder, 'python.log'))
            h.setFormatter(logging.Formatter('%(asctime)s %(name)s %(levelname)s %(message)s'))
            log.addHandler(h)
        except Exception:
            pass
    return log


from culi import revit, script, forms  # noqa: E402  (they read HOST_APP / EXEC_PARAMS above)
