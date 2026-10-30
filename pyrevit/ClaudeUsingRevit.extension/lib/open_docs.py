# -*- coding: utf-8 -*-
"""Snapshot of the documents open in THIS Revit window, for tools outside Revit.

Every Revit window writes %APPDATA%\\claude-using-revit\\open-docs\\<pid>.json when pyRevit loads (startup.py) and
whenever a document opens or closes (hooks/doc-opened.py, hooks/doc-closed.py). The MCP command revit_docs writes the
same file for the window it runs in, and refuses to open a model that another window already has open.
tools/revit-port/revit-port.ps1 prints them per window. Linked documents are left out (they are read-only copies).
"""
import datetime
import json
import os

FOLDER = os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'open-docs')


def _path_of(doc):
    try:
        if doc.IsModelInCloud:
            cp = doc.GetCloudModelPath()
            return 'CLD://{}/{}/{}'.format(cp.Region, cp.GetProjectGUID(), cp.GetModelGUID()), str(cp.GetModelGUID())
    except Exception:
        pass
    return doc.PathName or '', ''


def write_state(uiapp, closing=None):
    """closing: a document being closed (DocumentClosing) to leave out."""
    from System.Diagnostics import Process
    proc = Process.GetCurrentProcess()
    active = None
    try:
        active = uiapp.ActiveUIDocument.Document if uiapp.ActiveUIDocument else None
    except Exception:
        pass
    docs = []
    app = getattr(uiapp, 'Application', uiapp)   # UIApplication in Revit; Application under `pyrevit run`
    for d in app.Documents:
        try:
            if d.IsLinked or (closing is not None and d.Equals(closing)):
                continue
            path, guid = _path_of(d)
            docs.append({'title': d.Title, 'path': path, 'modelGuid': guid, 'modified': bool(d.IsModified),
                         'active': bool(active is not None and d.Equals(active))})
        except Exception:
            continue
    state = {'pid': proc.Id, 'updatedUtc': datetime.datetime.utcnow().isoformat() + 'Z', 'source': 'pyrevit',
             'window': proc.MainWindowTitle, 'docs': docs}
    if not os.path.isdir(FOLDER):
        os.makedirs(FOLDER)
    target = os.path.join(FOLDER, '{}.json'.format(proc.Id))
    tmp = target + '.tmp'
    with open(tmp, 'w') as f:
        json.dump(state, f, indent=1)
    if os.path.exists(target):
        os.remove(target)
    os.rename(tmp, target)
    return state
