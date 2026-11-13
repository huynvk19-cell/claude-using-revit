# -*- coding: utf-8 -*-
"""Job queue of THIS Revit window: runs a job on Revit's API thread through one ExternalEvent and hands
back JSON. Used by the Bảng panel (lib/pane.py). The web app it once served is gone; the module keeps
its name so pane.py and startup.py need no other change.

Jobs (_Job(kind, name, {args, label})), run one at a time:
  info    : active document / view / selection of this window + its sheet-list schedules
  task    : a shared task of lib/tasks.py (the same code as the ribbon buttons)
  mcp     : a claude using revit MCP tool run in-process (ClaudeUsingRevit.Mcp.ToolRegistry, the same tools Claude sees)
startup.py starts it when claude using revit starts and on Tải lại (the new queue replaces the old one).
"""
import json
import os
import threading
import time
import traceback

from System import AppDomain
from Autodesk.Revit.UI import ExternalEvent, IExternalEventHandler

SLOT = 'ClaudeUsingRevit.WebBridge'      # AppDomain slot of the HTTP server of older versions (closed on start)
BRIDGE = None                            # this window's job queue


class _Job(object):
    def __init__(self, kind, name, data):
        self.kind, self.name, self.data = kind, name, data
        self.done = threading.Event()
        self.result = None                # JSON text
        self.started = None


def _dumps(obj):
    s = json.dumps(obj, ensure_ascii=False)
    return u''.join(c if ord(c) < 128 else u'\\u%04x' % ord(c) for c in s)


def _error(msg, trace=None):
    out = {'error': u'{0}'.format(msg)}
    if trace:
        out['trace'] = trace[-1500:]
    return _dumps(out)


# ---- what a job does (Revit API thread) ---------------------------------------------

def _info(uiapp):
    from System.Diagnostics import Process
    app = uiapp.Application
    uidoc = uiapp.ActiveUIDocument
    out = {'pid': Process.GetCurrentProcess().Id, 'revit': app.VersionNumber, 'build': app.VersionBuild,
           'user': app.Username, 'docs': [], 'doc': None, 'view': None, 'selection': [], 'schedules': []}
    for d in app.Documents:
        if not d.IsLinked:
            out['docs'].append(d.Title)
    if uidoc is None:
        return out
    doc = uidoc.Document
    out['doc'] = {'title': doc.Title, 'path': doc.PathName or u'', 'modified': bool(doc.IsModified),
                  'workshared': bool(doc.IsWorkshared), 'family': bool(doc.IsFamilyDocument)}
    v = doc.ActiveView
    if v is not None:
        out['view'] = {'id': v.Id.IntegerValue, 'name': v.Name, 'type': u'{0}'.format(v.ViewType)}
    out['selection'] = [i.IntegerValue for i in uidoc.Selection.GetElementIds()][:500]
    try:
        import print_set_sync as ps
        out['schedules'] = [s.Name for s in ps.sheet_schedules(doc)]
    except Exception:
        pass
    return out


def _registry():
    import clr
    clr.AddReference('ClaudeUsingRevit')
    from ClaudeUsingRevit.Mcp import ToolRegistry
    return ToolRegistry


def _run_mcp(uiapp, name, args):
    """An MCP tool of the add-in, on this (API) thread: JSON text back ({"error": ...} on failure)."""
    return _registry().CallJson(uiapp, name, json.dumps(args or {}))


_lib = {'mtime': 0}


def _tasks():
    """lib/tasks.py, re-imported (with the lib modules it uses) when a lib file changed since the
    last job, so install.ps1 applies without Tải lại (like cur_task)."""
    import sys
    lib = os.path.dirname(os.path.abspath(__file__))
    newest = max(os.path.getmtime(os.path.join(lib, f)) for f in os.listdir(lib) if f.endswith('.py'))
    if _lib['mtime'] and newest > _lib['mtime']:
        for name, mod in list(sys.modules.items()):
            try:
                f = getattr(mod, '__file__', None) if mod is not None else None
                if f and name != 'web_bridge' and os.path.dirname(os.path.abspath(f)) == lib:
                    del sys.modules[name]
            except Exception:      # odd __file__ values of built-in / assembly modules
                continue
    _lib['mtime'] = newest
    import tasks
    return tasks


def _run(uiapp, job):
    try:
        if job.kind == 'info':
            return _dumps(_info(uiapp))
        uidoc = uiapp.ActiveUIDocument
        if job.kind == 'task':
            if uidoc is None:
                return _error(u'no open document')
            return _tasks().run_json(job.name, json.dumps(job.data.get('args') or {}), uidoc.Document, uidoc)
        if job.kind == 'mcp':
            return _run_mcp(uiapp, job.name, job.data.get('args'))
        return _error(u'unknown kind: {0}'.format(job.kind))
    except Exception as ex:
        return _error(ex, traceback.format_exc())


# ---- queue + ExternalEvent ------------------------------------------------------------

class _Handler(IExternalEventHandler):
    def __init__(self, bridge):
        self.bridge = bridge

    def Execute(self, uiapp):
        self.bridge.run_next(uiapp)

    def GetName(self):
        return 'claude using revit - web bridge'


class Bridge(object):
    def __init__(self):
        self.lock = threading.Lock()
        self.queue = []
        self.current = None
        self.event = ExternalEvent.Create(_Handler(self))     # must be created on the API thread

    def submit(self, job):
        with self.lock:
            self.queue.append(job)
        self.event.Raise()

    def run_next(self, uiapp):
        with self.lock:
            job = self.queue.pop(0) if self.queue else None
            self.current = job
        if job is None:
            return
        job.started = time.time()
        try:
            job.result = _run(uiapp, job)
        finally:
            with self.lock:
                self.current = None
                more = bool(self.queue)
            job.done.set()
        if more:
            self.event.Raise()

    def ping(self):
        from System.Diagnostics import Process
        with self.lock:
            cur = self.current
            out = {'pid': Process.GetCurrentProcess().Id, 'queue': len(self.queue),
                   'running': None}
            if cur is not None:
                out['running'] = {'kind': cur.kind, 'name': cur.data.get('label') or cur.name,
                                  'seconds': int(time.time() - (cur.started or time.time()))}
        return out

    def cancel_queued(self, job):
        with self.lock:
            if job in self.queue:
                self.queue.remove(job)
                return True
        return False


def start(uiapp):
    """(Re)create this window's job queue; close the HTTP server an older version left running."""
    old = AppDomain.CurrentDomain.GetData(SLOT)
    if old is not None:
        try:
            old.shutdown()
            old.server_close()
        except Exception:
            pass
        AppDomain.CurrentDomain.SetData(SLOT, None)
    global BRIDGE
    BRIDGE = Bridge()
    return BRIDGE
