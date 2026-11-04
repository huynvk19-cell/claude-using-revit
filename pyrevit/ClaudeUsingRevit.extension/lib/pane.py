# -*- coding: utf-8 -*-
"""The panel "claude using revit" inside the Revit window: a dockable pane (WebView2) showing the page in ui/.

No server and no network: the page sends each job as a message (ui/host.js), web_bridge runs it through its
ExternalEvent queue like any other job and the answer goes back as a message. Files reach the page through
virtual host names: ui.cur.example = ui/, tab.cur.example = the ribbon tab (icons), data.cur.example =
%APPDATA%\\claude-using-revit (background photo, meshes).

Revit lets a dockable pane register only while starting up (or with no model open), so setup() runs from
startup.py and costs nothing more: WebView2 loads when the pane first shows. After a pyRevit reload with a
model open, or a first install, there is no pane until Revit restarts: show() then uses a floating window
owned by Revit with the same content.

WebView2 SDK: the dlls Revit ships (Revit 2024 folder, Revit 2023 AddIns\\DynamoForRevit). Runtime: Edge
WebView2, part of Windows 11. The startup engine stays alive, so its event handlers keep working.
"""
import glob
import io
import json
import os
import re
import shutil
import subprocess
import threading

import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')
clr.AddReference('System.Drawing')

from System import Action, AppDomain, Environment, Guid
from System.Diagnostics import Process
from System.IO import DriveInfo
from System.Windows import TextWrapping, Thickness, Window, WindowStartupLocation
from System.Windows.Controls import Grid, TextBlock
from System.Windows.Interop import WindowInteropHelper
from System.Windows.Media import Color, SolidColorBrush

from Autodesk.Revit.DB import Rectangle
from Autodesk.Revit.UI import DockablePaneId, DockablePaneState, DockPosition, IDockablePaneProvider

import web_bridge

SLOT = 'ClaudeUsingRevit.Pane'
PANE_ID = Guid('5b1c7c2e-8f0a-4c55-9a34-1d2e6f7a9c10')
TITLE = 'claude using revit'
HERE = os.path.dirname(os.path.abspath(__file__))
EXT = os.path.dirname(HERE)
DATA = os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit')
HEADER_RE = re.compile(r'/\*\s*mcp-tool\s*([\s\S]*?)\*/')
NAME_RE = re.compile(r'^[a-z][a-z0-9_]{0,63}$')
NODE_ONLY = ('stop_mcp_service', 'list_dynamic_commands', 'run_dynamic_command')
BACKGROUND = (0x02, 0x03, 0x0A)


# ---- what the page can ask without the Revit API ----------------------------------------------

def _mcp_entry():
    """(Revit MCP server entry file, its env) from ~/.claude.json, or (None, {})."""
    try:
        with io.open(os.path.join(os.path.expanduser('~'), '.claude.json'), encoding='utf-8') as f:
            servers = json.load(f).get('mcpServers') or {}
        for k, v in servers.items():
            if 'revit' in k.lower() and v.get('args'):
                return v['args'][0], v.get('env') or {}
    except Exception:
        pass
    return None, {}


def _commands_dir():
    entry, env = _mcp_entry()
    if env.get('REVIT_MCP_DYNAMIC_DIR'):
        return env['REVIT_MCP_DYNAMIC_DIR']
    if os.environ.get('REVIT_MCP_COMMANDS'):
        return os.environ['REVIT_MCP_COMMANDS']
    return os.path.normpath(os.path.join(os.path.dirname(entry), '..', '..', 'dynamic-commands')) if entry else None


def _own_commands():
    """The commands of this repo (install.ps1 -PyRevit writes the list next to this file)."""
    try:
        with io.open(os.path.join(HERE, 'revit-commands.txt'), encoding='utf-8') as f:
            return set(l.strip() for l in f if l.strip())
    except Exception:
        return set()


def _read_commands():
    folder, out, own = _commands_dir(), [], _own_commands()
    if not folder or not os.path.isdir(folder):
        return out
    for f in sorted(os.listdir(folder)):
        name = f[:-3]
        if not f.endswith('.cs') or not NAME_RE.match(name):
            continue
        c = {'kind': 'command', 'name': name, 'description': u'', 'readOnly': False, 'own': name in own,
             'inputSchema': {'type': 'object', 'properties': {}}}
        try:
            with io.open(os.path.join(folder, f), encoding='utf-8') as src:
                m = HEADER_RE.search(src.read())
            if m:
                h = json.loads(m.group(1))
                for k in ('description', 'inputSchema', 'readOnly'):
                    if k in h:
                        c[k] = h[k]
        except Exception:
            pass
        out.append(c)
    return out


def _base_tools():
    """The MCP add-in's own commands: their list only exists as a Node module of the MCP server."""
    entry, _ = _mcp_entry()
    index = entry and os.path.join(os.path.dirname(entry), 'tools', 'index.js')
    if not index or not os.path.isfile(index):
        return []
    code = ("process.env.MCP_PROFILE='full';console.error=()=>{};"
            "const m=await import(process.argv[1]);"
            "process.stdout.write(JSON.stringify(m.registerRevitTools()))")
    url = 'file:///' + index.replace('\\', '/')
    try:
        info = subprocess.STARTUPINFO()
        info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        p = subprocess.Popen(['node', '--input-type=module', '-e', code, url], stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE, startupinfo=info)
        out, _ = p.communicate()
        tools = json.loads(out.decode('utf-8'))
    except Exception:
        return []
    return [{'kind': 'tool', 'name': t['name'], 'description': t.get('description') or u'',
             'inputSchema': t.get('inputSchema') or {'type': 'object', 'properties': {}},
             'readOnly': bool((t.get('annotations') or {}).get('readOnlyHint'))}
            for t in tools if t['name'] not in NODE_ONLY]


def _list_dirs(path):
    if not path:
        return {'path': u'', 'parent': None, 'dirs': [d.Name for d in DriveInfo.GetDrives() if d.IsReady]}
    full = os.path.abspath(path)
    dirs = sorted((d for d in os.listdir(full) if os.path.isdir(os.path.join(full, d)) and not d.startswith('$')),
                  key=lambda s: s.lower())
    parent = os.path.dirname(full)
    return {'path': full, 'parent': u'' if parent == full else parent, 'dirs': dirs}


def _list_meshes():
    out = []
    for f in glob.glob(os.path.join(DATA, 'meshes', '*.json')):
        try:
            with io.open(f, encoding='utf-8') as src:
                m = json.load(src)
            out.append(dict((k, m.get(k)) for k in ('name', 'model', 'view', 'triangles', 'truncated', 'exported')))
        except Exception:
            continue                # half written
    return sorted(out, key=lambda m: m.get('exported') or u'', reverse=True)


def _photo_from_claude_store():
    """A background photo saved by a Claude session sits in the Claude app's private AppData copy; Revit
    sees the real folder, so bring it over once."""
    if glob.glob(os.path.join(DATA, 'background.*')):
        return
    pattern = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Packages', 'Claude_*', 'LocalCache', 'Roaming',
                           'claude-using-revit', 'background.*')
    for f in glob.glob(pattern):
        try:
            shutil.copy2(f, DATA)
        except Exception:
            pass


def _sdk_folder():
    root = os.path.dirname(Process.GetCurrentProcess().MainModule.FileName)
    for d in (root, os.path.join(root, 'AddIns', 'DynamoForRevit')):
        if os.path.isfile(os.path.join(d, 'Microsoft.Web.WebView2.Wpf.dll')):
            return d
    raise Exception(u'Không thấy WebView2 SDK trong thư mục Revit: ' + root)


# ---- the content: a grid that takes the WebView2 the first time it shows ------------------------

class _Provider(IDockablePaneProvider):
    def __init__(self, root):
        self.root = root

    def SetupDockablePane(self, data):
        data.FrameworkElement = self.root
        data.VisibleByDefault = False
        state = DockablePaneState()
        state.DockPosition = DockPosition.Floating
        state.SetFloatingRectangle(Rectangle(120, 80, 1500, 960))
        data.InitialState = state


class Host(object):
    def __init__(self, uiapp, root=None):
        self.uiapp = uiapp
        self.root = root if root is not None else Grid()
        self.root.Children.Clear()
        self.root.Background = SolidColorBrush(Color.FromRgb(*BACKGROUND))
        self.root.Loaded += self._on_loaded
        self.web = None
        self.window = None
        self.docked = False
        self.closed = False
        self.debug_port = None
        self.error = None

    # -- showing ---------------------------------------------------------------------------------
    def show(self, debug_port=None):
        self.debug_port = debug_port or self.debug_port
        if self.docked and self.window is None:
            try:
                self.uiapp.GetDockablePane(DockablePaneId(PANE_ID)).Show()
                if self.root.IsLoaded:      # a reload kept the pane: its Loaded event will not come again
                    self._on_loaded(None, None)
                return u'pane'
            except Exception as ex:    # registered with a model open: Revit accepts it, but creates it next start
                self.error = u'{0}'.format(ex)
        if self.window is None:
            w = Window()
            w.Title = TITLE
            w.Width, w.Height = 1400, 880
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen
            w.Content = self.root
            WindowInteropHelper(w).Owner = self.uiapp.MainWindowHandle
            w.Closing += self._keep_window
            self.window = w
        self.window.Show()
        self.window.Activate()
        return u'window'

    def hide(self):
        if self.window is not None:
            self.window.Hide()
        elif self.docked:
            try:
                self.uiapp.GetDockablePane(DockablePaneId(PANE_ID)).Hide()
            except Exception:
                pass

    def _keep_window(self, sender, e):
        e.Cancel = True
        sender.Hide()

    def _on_loaded(self, sender, e):
        if not self.closed and self.web is None:
            try:
                self._create_web()
            except Exception as ex:
                self._fail(ex)

    def _create_web(self):
        sdk = _sdk_folder()
        clr.AddReferenceToFileAndPath(os.path.join(sdk, 'Microsoft.Web.WebView2.Core.dll'))
        clr.AddReferenceToFileAndPath(os.path.join(sdk, 'Microsoft.Web.WebView2.Wpf.dll'))
        loader = os.path.join(sdk, 'runtimes', 'win-x64', 'native')
        loader = loader if os.path.isfile(os.path.join(loader, 'WebView2Loader.dll')) else sdk
        Environment.SetEnvironmentVariable('PATH', loader + os.pathsep + (Environment.GetEnvironmentVariable('PATH') or ''))
        from Microsoft.Web.WebView2.Wpf import CoreWebView2CreationProperties, WebView2
        from System.Drawing import Color as DrawingColor
        props = CoreWebView2CreationProperties()
        # one folder for every Revit window (they share one browser process), which needs equal options:
        # a test run with a debug port keeps its own
        props.UserDataFolder = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'claude-using-revit',
                                            'pane-webview-{0}'.format(self.debug_port) if self.debug_port else 'pane-webview')
        Environment.SetEnvironmentVariable('WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS',
                                           '--remote-debugging-port={0}'.format(self.debug_port) if self.debug_port else None)
        web = WebView2()
        web.CreationProperties = props
        web.DefaultBackgroundColor = DrawingColor.FromArgb(*BACKGROUND)
        web.CoreWebView2InitializationCompleted += self._on_ready
        self.web = web
        self.root.Children.Add(web)
        web.EnsureCoreWebView2Async(None)

    def _on_ready(self, sender, e):
        if not e.IsSuccess:
            return self._fail(e.InitializationException)
        try:
            from Microsoft.Web.WebView2.Core import CoreWebView2HostResourceAccessKind as Access
            core = self.web.CoreWebView2
            core.Settings.IsStatusBarEnabled = False
            core.Settings.AreDevToolsEnabled = bool(self.debug_port)
            if not os.path.isdir(DATA):
                os.makedirs(DATA)
            _photo_from_claude_store()
            core.SetVirtualHostNameToFolderMapping('ui.cur.example', os.path.join(EXT, 'ui'), Access.Allow)
            core.SetVirtualHostNameToFolderMapping('tab.cur.example', os.path.join(EXT, 'claude using revit.tab'), Access.Allow)
            core.SetVirtualHostNameToFolderMapping('data.cur.example', DATA, Access.Allow)
            core.WebMessageReceived += self._on_message
            core.Navigate('https://ui.cur.example/index.html')
        except Exception as ex:
            self._fail(ex)

    def _fail(self, ex):
        self.error = u'{0}'.format(ex)
        self.root.Children.Clear()
        t = TextBlock()
        t.Text = u'Không mở được panel (WebView2): ' + self.error
        t.Margin = Thickness(16)
        t.Foreground = SolidColorBrush(Color.FromRgb(255, 255, 255))
        t.TextWrapping = TextWrapping.Wrap
        self.root.Children.Add(t)

    # -- messages from the page ------------------------------------------------------------------
    def _reply(self, mid, text):
        try:
            self.web.CoreWebView2.PostWebMessageAsString(u'{{"id":{0},"result":{1}}}'.format(mid, text))
        except Exception:
            pass                    # the page is gone (reloaded, panel closed)

    def _on_message(self, sender, e):
        try:
            m = json.loads(e.TryGetWebMessageAsString())
        except Exception:
            return
        mid, kind = m.get('id'), m.get('kind')
        try:
            if kind in ('info', 'task', 'tool', 'command'):
                self._run_job(mid, kind, m.get('name'), m.get('args') or {}, m.get('label'))
            elif kind == 'catalog':
                self._later(mid, lambda: {'commands': _read_commands(), 'tools': _base_tools()})
            elif kind == 'fs':
                self._reply(mid, web_bridge._dumps(_list_dirs((m.get('args') or {}).get('path') or u'')))
            elif kind == 'meshes':
                self._reply(mid, web_bridge._dumps({'meshes': _list_meshes()}))
            else:
                self._reply(mid, web_bridge._error(u'unknown kind: {0}'.format(kind)))
        except Exception as ex:
            self._reply(mid, web_bridge._error(ex))

    def _later(self, mid, work, raw=False):
        """Slow work off the UI thread; the answer back on it. raw = work returns JSON text already."""
        def go():
            try:
                text = work() if raw else web_bridge._dumps(work())
            except Exception as ex:
                text = web_bridge._error(ex)
            self.root.Dispatcher.BeginInvoke(Action(lambda: self._reply(mid, text)))
        t = threading.Thread(target=go)
        t.daemon = True
        t.start()

    def _run_job(self, mid, kind, name, args, label):
        data = {'args': args, 'label': label}
        if kind == 'tool':
            kind = 'mcp'
        elif kind == 'command':
            if not NAME_RE.match(name or ''):
                raise Exception(u'bad command name')
            with io.open(os.path.join(_commands_dir() or u'', name + '.cs'), encoding='utf-8') as f:
                data['args'] = {'name': name, 'source': f.read(), 'args': args}
            kind, name = 'mcp', 'run_dynamic_command'
        job = web_bridge._Job(kind, name, data)
        web_bridge.BRIDGE.submit(job)

        def answer():
            job.done.wait()
            return job.result or web_bridge._error(u'no result')
        self._later(mid, answer, raw=True)

    def close(self):
        """A reload replaces this host (a registered pane keeps the same root)."""
        self.closed = True
        try:
            self.root.Loaded -= self._on_loaded
            self.root.Children.Clear()
            if self.web is not None:
                self.web.Dispose()
            if self.window is not None:
                self.window.Closing -= self._keep_window
                self.window.Content = None
                self.window.Close()
        except Exception:
            pass


def setup(uiapp, debug_port=None):
    """Called by startup.py on every pyRevit load."""
    old = AppDomain.CurrentDomain.GetData(SLOT)
    docked = old is not None and old.docked
    root = old.root if docked else None
    if old is not None:
        old.close()
    host = Host(uiapp, root)
    host.docked = docked
    host.debug_port = debug_port
    if not docked:
        try:
            uiapp.RegisterDockablePane(DockablePaneId(PANE_ID), TITLE, _Provider(host.root))
            host.docked = True
        except Exception as ex:      # a model is open: floating window until Revit restarts
            host.error = u'{0}'.format(ex)
    AppDomain.CurrentDomain.SetData(SLOT, host)
    if host.root.IsLoaded and host.root.IsVisible:       # reloaded with the pane open
        host._on_loaded(None, None)
    return host


def current(uiapp):
    host = AppDomain.CurrentDomain.GetData(SLOT)
    return host if host is not None else setup(uiapp)
