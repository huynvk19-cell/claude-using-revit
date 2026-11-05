# -*- coding: utf-8 -*-
"""revit.doc, revit.uidoc (read when used, like pyRevit's), revit.ui.get_window_rectangle()."""
import sys
import types


class _Ui(object):
    def get_window_rectangle(self):
        """Revit's main window in screen pixels (.Left .Top .Right .Bottom)."""
        from cur import HOST_APP
        return HOST_APP.uiapp.MainWindowExtents


class _RevitModule(types.ModuleType):
    ui = _Ui()

    @property
    def uidoc(self):
        from cur import HOST_APP
        u = HOST_APP.uiapp
        return u.ActiveUIDocument if u is not None else None

    @property
    def doc(self):
        ud = self.uidoc
        return ud.Document if ud is not None else None

    @property
    def docs(self):
        from cur import HOST_APP
        return list(HOST_APP.app.Documents) if HOST_APP.app is not None else []


_mod = _RevitModule(__name__, __doc__)
_mod.__file__ = __file__
sys.modules[__name__] = _mod
