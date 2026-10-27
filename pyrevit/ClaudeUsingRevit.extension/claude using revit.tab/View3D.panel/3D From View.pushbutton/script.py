# -*- coding: utf-8 -*-
"""Open your own 3D view, section-boxed to the active plan / section / elevation."""
from pyrevit import revit

import liquid_glass as lg
import tasks

try:
    tasks.run("view_3d_from_view", {}, revit.doc, revit.uidoc)
except Exception as ex:
    lg.alert(u"Không mở được 3D:\n{0}".format(ex), exitscript=True)
