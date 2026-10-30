# -*- coding: utf-8 -*-
"""Thin bridge for the web app (webapp/ in the repo): a small HTTP server inside THIS Revit window.

The web app (Node, LAN) is the user interface; this bridge only runs what it is asked on Revit's
API thread and hands back JSON. It never listens on the network: 127.0.0.1 only, and every call
must carry the key from %APPDATA%\\claude-using-revit\\web-bridge\\<pid>.json (a browser page cannot
read that file, so a web page cannot drive Revit through it). One bridge per Revit window, port
48990 and up, so it never competes with the Revit MCP port 8964 or with Claude sessions.

Jobs (POST /run, JSON {kind, name, args, source}), run one at a time through one ExternalEvent:
  info    : active document / view / selection of this window + its sheet-list schedules
  task    : a shared task of lib/tasks.py (the same code as the ribbon buttons)
  mcp     : a Revit MCP add-in command run in-process (CommandExecutor of RevitMCP.dll), e.g.
            run_dynamic_command {name, source, args} for the commands in revit-commands/
GET /ping answers at once (no API context): pid, the job running now, queue length.
startup.py starts it on every pyRevit load (a reload replaces the old server on the same port)
and starts the web app server when it is not running yet.
"""
import json
import os
import random
import socket
import threading
import time
import traceback

from BaseHTTPServer import BaseHTTPRequestHandler, HTTPServer
from SocketServer import ThreadingMixIn

from System import AppDomain
from Autodesk.Revit.UI import ExternalEvent, IExternalEventHandler

FOLDER = os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'web-bridge')
WEBAPP_DIR = os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'webapp')
WEBAPP_PORT = 8780
PORTS = range(48990, 49010)
SLOT = 'ClaudeUsingRevit.WebBridge'      # AppDomain slot: the live server survives pyRevit reloads
MAX_BODY = 8 * 1024 * 1024
BRIDGE = None                            # this window's job queue (lib/island.py submits to it too)


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


_mcp = {}


def _mcp_types():
    """CommandExecutor / RevitCommandRequest of the loaded Revit MCP add-in + its own Newtonsoft."""
    if _mcp:
        return _mcp
    from System import Array, Object, String, Type
    from System.Reflection import Assembly
    asm = None
    for a in AppDomain.CurrentDomain.GetAssemblies():
        if not a.IsDynamic and a.GetName().Name == 'RevitMCP':
            asm = a
    if asm is None:
        raise Exception(u'Revit MCP add-in (RevitMCP.dll) is not loaded in this Revit')
    nj = [n for n in asm.GetReferencedAssemblies() if n.Name == 'Newtonsoft.Json'][0]
    njasm = Assembly.Load(nj)
    jobject = njasm.GetType('Newtonsoft.Json.Linq.JObject')
    convert = njasm.GetType('Newtonsoft.Json.JsonConvert')
    _mcp.update({
        'executor': asm.GetType('RevitMCP.Core.CommandExecutor'),
        'request': asm.GetType('RevitMCP.Models.RevitCommandRequest'),
        'parse': jobject.GetMethod('Parse', Array[Type]([String])),
        'serialize': convert.GetMethod('SerializeObject', Array[Type]([Object])),
    })
    return _mcp


def _run_mcp(uiapp, name, args):
    from System import Activator, Array, Object
    t = _mcp_types()
    executor = Activator.CreateInstance(t['executor'], Array[Object]([uiapp]))
    req = Activator.CreateInstance(t['request'])
    req.CommandName = name
    req.Parameters = t['parse'].Invoke(None, Array[Object]([json.dumps(args or {})]))
    resp = executor.ExecuteCommand(req)
    return t['serialize'].Invoke(None, Array[Object]([resp]))


_lib = {'mtime': 0}


def _tasks():
    """lib/tasks.py, re-imported (with the lib modules it uses) when a lib file changed since the
    last job, so install.ps1 -PyRevit applies without pyRevit > Reload (like pyrevit_task)."""
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


# ---- HTTP -----------------------------------------------------------------------------

class _Server(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = False       # Windows SO_REUSEADDR would let two windows share a port


def _make_handler(bridge, key):
    class H(BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def _send(self, code, text):
            body = text.encode('utf-8') if isinstance(text, unicode) else text   # noqa: F821
            self.send_response(code)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def _authorised(self):
            if self.headers.getheader('x-bridge-key') != key or self.headers.getheader('origin'):
                self._send(403, _error(u'forbidden'))
                return False
            return True

        def do_GET(self):
            if not self._authorised():
                return
            if self.path.startswith('/ping'):
                return self._send(200, _dumps(bridge.ping()))
            self._send(404, _error(u'not found'))

        def do_POST(self):
            if not self._authorised():
                return
            if not self.path.startswith('/run'):
                return self._send(404, _error(u'not found'))
            n = int(self.headers.getheader('content-length') or 0)
            if n > MAX_BODY:
                return self._send(413, _error(u'request too large'))
            try:
                data = json.loads(self.rfile.read(n).decode('utf-8')) if n else {}
            except Exception as ex:
                return self._send(400, _error(u'bad JSON: {0}'.format(ex)))
            kind = data.get('kind')
            if kind not in ('info', 'task', 'mcp'):
                return self._send(400, _error(u'kind must be info, task or mcp'))
            job = _Job(kind, data.get('name') or kind, data)
            bridge.submit(job)
            # waitSeconds: give up when the job has not STARTED by then (Revit busy / modal
            # dialog open); a job that started always runs to the end and is answered.
            wait = data.get('waitSeconds')
            job.done.wait(float(wait) if wait else None)
            if not job.done.is_set():
                if bridge.cancel_queued(job):
                    return self._send(503, _error(u'Revit is busy (job not started)'))
                job.done.wait()
            self._send(200, job.result or _error(u'no result'))
    return H


def _state_file(pid):
    return os.path.join(FOLDER, '{0}.json'.format(pid))


def _write_state(pid, port, key, version):
    if not os.path.isdir(FOLDER):
        os.makedirs(FOLDER)
    target = _state_file(pid)
    with open(target + '.tmp', 'w') as f:
        json.dump({'pid': pid, 'port': port, 'key': key, 'revit': version,
                   'startedUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}, f)
    if os.path.exists(target):
        os.remove(target)
    os.rename(target + '.tmp', target)


def start(uiapp):
    """(Re)start this window's bridge. Returns its port."""
    from System.Diagnostics import Process
    pid = Process.GetCurrentProcess().Id
    old = AppDomain.CurrentDomain.GetData(SLOT)
    prefer = None
    if old is not None:
        try:
            prefer = old.server_address[1]
            old.shutdown()
            old.server_close()
        except Exception:
            pass
        AppDomain.CurrentDomain.SetData(SLOT, None)
    key = '%032x' % random.SystemRandom().getrandbits(128)
    global BRIDGE
    bridge = BRIDGE = Bridge()
    server = None
    for port in ([prefer] if prefer else []) + [p for p in PORTS if p != prefer]:
        try:
            server = _Server(('127.0.0.1', port), _make_handler(bridge, key))
            break
        except socket.error:
            continue
    if server is None:
        raise Exception(u'no free port in {0}-{1}'.format(PORTS[0], PORTS[-1]))
    t = threading.Thread(target=server.serve_forever)
    t.daemon = True
    t.start()
    AppDomain.CurrentDomain.SetData(SLOT, server)
    _write_state(pid, server.server_address[1], key, uiapp.Application.VersionNumber)
    return server.server_address[1]


def ensure_webapp():
    """Start the web app server (node webapp/server.mjs) unless something answers on its port."""
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(0.3)
    try:
        s.connect(('127.0.0.1', WEBAPP_PORT))
        return False
    except Exception:
        pass
    finally:
        s.close()
    script = os.path.join(WEBAPP_DIR, 'server.mjs')
    node = _node()
    if not os.path.isfile(script) or node is None:
        return False
    from System.Diagnostics import Process, ProcessStartInfo, ProcessWindowStyle
    psi = ProcessStartInfo(node, u'"{0}"'.format(script))
    psi.WorkingDirectory = WEBAPP_DIR
    psi.UseShellExecute = False
    psi.CreateNoWindow = True
    psi.WindowStyle = ProcessWindowStyle.Hidden
    Process.Start(psi)
    return True


def _node():
    cands = [os.path.join(os.environ.get(v, ''), 'nodejs', 'node.exe') for v in ('ProgramFiles', 'ProgramW6432')]
    cands.append(os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Programs', 'nodejs', 'node.exe'))
    cands += [os.path.join(p, 'node.exe') for p in os.environ.get('PATH', '').split(os.pathsep) if p]
    for c in cands:
        if os.path.isfile(c):
            return c
    return None
