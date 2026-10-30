# -*- coding: utf-8 -*-
"""The island: a black capsule at the top centre of the Revit window, like Apple's Dynamic Island.

Compact      : what is left to do in this model ("2 việc", orange) or a check when nothing is.
Live activity: while a job runs in this window (from the island, the web app or a button that goes
               through the web bridge) the capsule widens: spinner, what runs, seconds.
Result       : when a job ends the capsule shows the outcome for a few seconds.
Expanded     : a click opens it: model and view, the six tools (orange = work left), one tap runs
               the tool's main action (a confirm line first when it writes), Web opens the full app.
It never blocks Revit: jobs go to the web bridge queue (one ExternalEvent) and a timer polls them.
The work count comes from the read-only task status_board, refreshed when the capsule opens, after
each job and when a model opens. Started by startup.py; turn off with "enabled": false in
%APPDATA%\\claude-using-revit\\island.json.
"""
import json
import os
import time

import clr
clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')
clr.AddReference('System.Xaml')

from System import AppDomain, TimeSpan, Uri, UriKind
from System.Diagnostics import Process
from System.Windows import (CornerRadius, Duration, HorizontalAlignment, PresentationSource,
                            Size, Thickness, VerticalAlignment, Visibility, Window)
from System.Windows.Controls import Border, Grid, Image, StackPanel, TextBlock, WrapPanel, Orientation
from System.Windows.Input import Cursors
from System.Windows.Interop import WindowInteropHelper
from System.Windows.Markup import XamlReader
from System.Windows.Media import BrushConverter, RotateTransform
from System.Windows.Media.Animation import BackEase, DoubleAnimation, EasingMode, RepeatBehavior
from System.Windows.Media.Imaging import BitmapImage
from System.Windows.Threading import DispatcherTimer

import web_bridge

SLOT = 'ClaudeUsingRevit.Island'
LIB = os.path.dirname(os.path.abspath(__file__))
TAB = os.path.join(os.path.dirname(LIB), 'claude using revit.tab')
DATA = os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit')
WORK = '#FF9F0A'
_bc = BrushConverter()

COMPACT_W, LIVE_W, OPEN_W, PILL_H = 196, 300, 452, 36

XAML = u'''
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="{x:Null}" ShowInTaskbar="False"
        ResizeMode="NoResize" Width="480" Height="330" ShowActivated="False" Topmost="False"
        FontFamily="Segoe UI Variable Text, Segoe UI" Foreground="White">
  <Grid>
    <Border x:Name="pill" Background="#000000" CornerRadius="18" Width="196" Height="36"
            HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,6,0,0" ClipToBounds="True" Cursor="Hand">
      <Border.Effect><DropShadowEffect BlurRadius="18" ShadowDepth="3" Opacity="0.45" Direction="270"/></Border.Effect>
      <Grid>
        <Grid x:Name="compact" Height="36" VerticalAlignment="Top" Margin="14,0,14,0">
          <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <Grid x:Name="lead" Width="18" Height="18" VerticalAlignment="Center">
            <Ellipse x:Name="dot" Width="9" Height="9" Fill="#8E8E93"/>
            <Ellipse x:Name="spin" Width="15" Height="15" Stroke="#FFFFFF" StrokeThickness="2" StrokeDashArray="5 3"
                     RenderTransformOrigin="0.5,0.5" Visibility="Collapsed"/>
            <TextBlock x:Name="mark" FontSize="13" FontWeight="SemiBold" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
          </Grid>
          <TextBlock x:Name="title" Grid.Column="1" Margin="10,0,8,0" VerticalAlignment="Center" FontSize="13.5"
                     FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
          <TextBlock x:Name="trail" Grid.Column="2" VerticalAlignment="Center" FontSize="13" Foreground="#99FFFFFF"/>
        </Grid>
        <StackPanel x:Name="open" Margin="22,46,22,18" Visibility="Collapsed" Opacity="0">
          <Grid>
            <StackPanel>
              <TextBlock x:Name="model" FontSize="15" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
              <TextBlock x:Name="view" FontSize="12.5" Foreground="#8CFFFFFF" TextTrimming="CharacterEllipsis"/>
            </StackPanel>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Top">
              <Border x:Name="refresh" Background="#26FFFFFF" CornerRadius="13" Height="26" Padding="11,0" Margin="0,0,6,0">
                <TextBlock Text="Kiểm tra" FontSize="12" VerticalAlignment="Center"/></Border>
              <Border x:Name="web" Background="#26FFFFFF" CornerRadius="13" Height="26" Padding="11,0">
                <TextBlock Text="Web ↗" FontSize="12" VerticalAlignment="Center"/></Border>
            </StackPanel>
          </Grid>
          <WrapPanel x:Name="tools" Margin="0,14,0,0" HorizontalAlignment="Center"/>
          <TextBlock x:Name="msg" Margin="2,10,2,0" FontSize="13" TextWrapping="Wrap" Foreground="#B3FFFFFF"/>
          <StackPanel x:Name="ask" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0" Visibility="Collapsed">
            <Border x:Name="no" Background="#26FFFFFF" CornerRadius="15" Height="30" Padding="16,0" Margin="0,0,8,0">
              <TextBlock Text="Huỷ" FontSize="13" VerticalAlignment="Center"/></Border>
            <Border x:Name="yes" Background="#FF9F0A" CornerRadius="15" Height="30" Padding="16,0">
              <TextBlock x:Name="yesText" Text="Làm" FontSize="13" FontWeight="SemiBold" Foreground="Black" VerticalAlignment="Center"/></Border>
          </StackPanel>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Window>'''

KIND = {'Room': u'room', 'Door': u'cửa đi', 'Window': u'cửa sổ', 'Wall': u'tường', 'Floor': u'sàn'}


def brush(c):
    return _bc.ConvertFrom(c)


def _settings():
    try:
        with open(os.path.join(DATA, 'island.json')) as f:
            return json.load(f)
    except Exception:
        return {}


def _icon(panel, button):
    p = os.path.join(TAB, panel + '.panel', button + '.pushbutton', 'icon.png')
    if not os.path.isfile(p):
        return None
    bi = BitmapImage()
    bi.BeginInit()
    bi.UriSource = Uri(p, UriKind.Absolute)
    bi.DecodePixelWidth = 64
    bi.EndInit()
    return bi


# ---- what is left to do (same rules as the web app) -----------------------------------------

def _official(b):
    named = set(r['schedule'] for r in b['printSets'] if r['setExists'])
    return [r for r in b['columns'] if not named or r['schedule'] in named]


def state_of(key, b):
    """(level work|done|idle, short text, count)"""
    if b is None:
        return 'idle', u'chưa kiểm tra', 0
    if b.get('error'):
        return 'idle', u'không kiểm tra được', 0
    if key == 'printSet':
        off = [r for r in b['printSets'] if r['setExists'] and not r['inSync']]
        if off:
            return 'work', u'{0} print set lệch danh mục'.format(len(off)), len(off)
        return ('done', u'khớp danh mục', 0) if any(r['setExists'] for r in b['printSets']) else ('idle', u'chưa có print set', 0)
    if key == 'columns':
        rows = _official(b)
        off = [r for r in rows if not r.get('inSync') and not r.get('error')]
        if off:
            return 'work', u'{0} danh mục chưa đúng chuẩn cột'.format(len(off)), len(off)
        return ('done', u'đúng chuẩn', 0) if rows else ('idle', u'chưa có danh mục', 0)
    if key == 'untagged':
        u = b.get('untagged')
        left = [(k, n) for k, n in (u['counts'].items() if u else []) if n]
        if left:
            return 'work', u', '.join(u'{0} {1}'.format(n, KIND.get(k, k)) for k, n in left) + u' chưa tag', sum(n for _, n in left)
        if u and b.get('untaggedMarks'):
            return 'work', u'còn {0} vùng tô cam'.format(b['untaggedMarks']), b['untaggedMarks']
        return ('done', u'view đã tag đủ', 0) if u else ('idle', u'mở mặt bằng / mặt cắt', 0)
    if key == 'highlight':
        n = b.get('highlights') or 0
        return ('work', u'còn {0} vùng highlight'.format(n), n) if n else ('idle', u'tô đối tượng đang chọn', 0)
    return 'idle', u'', 0


TOOLS = [  # key, label, panel, button
    ('printSet', u'Print set', 'PrintSet', 'Print Set Sync'),
    ('columns', u'Cột DM', 'PrintSet', 'Sheet List Columns'),
    ('untagged', u'Chưa tag', 'Untagged', 'Highlight Untagged'),
    ('highlight', u'Highlight', 'Highlight', 'Highlight'),
    ('export', u'Xuất', 'PrintSet', 'Export PDF'),
    ('view3d', u'3D', 'View3D', '3D From View'),
]


class Island(object):
    def __init__(self, uiapp):
        self.uiapp = uiapp
        self.win = XamlReader.Parse(XAML)
        for n in ('pill', 'compact', 'lead', 'dot', 'spin', 'mark', 'title', 'trail', 'open', 'model', 'view',
                  'refresh', 'web', 'tools', 'msg', 'ask', 'no', 'yes', 'yesText'):
            setattr(self, n, self.win.FindName(n))
        self.board, self.checking, self.expanded = None, False, False
        self.pending = []           # (job, label, say)
        self.flash_until = 0
        self.confirm = None
        self.doc_title = None
        self.hover_out = None
        self.icons = {}
        for key, label, panel, button in TOOLS:
            self.icons[key] = _icon(panel, button)
        self.pill.MouseLeftButtonUp += self.on_click
        self.pill.MouseLeave += self.on_leave
        self.pill.MouseEnter += lambda s, e: setattr(self, 'hover_out', None)
        self.refresh.MouseLeftButtonUp += lambda s, e: (self.check(), setattr(e, 'Handled', True))
        self.web.MouseLeftButtonUp += lambda s, e: (self.open_web(None), setattr(e, 'Handled', True))
        self.no.MouseLeftButtonUp += lambda s, e: (self.ask_clear(), setattr(e, 'Handled', True))
        self.yes.MouseLeftButtonUp += self.on_yes
        for b in (self.refresh, self.web, self.no, self.yes):
            b.Cursor = Cursors.Hand
        rot = RotateTransform()
        self.spin.RenderTransform = rot
        a = DoubleAnimation(0, 360, Duration(TimeSpan.FromSeconds(0.9)))
        a.RepeatBehavior = RepeatBehavior.Forever
        rot.BeginAnimation(RotateTransform.AngleProperty, a)
        WindowInteropHelper(self.win).Owner = uiapp.MainWindowHandle    # above Revit, hides with it
        self.timer = DispatcherTimer()
        self.timer.Interval = TimeSpan.FromMilliseconds(250)
        self.timer.Tick += self.tick
        self.last_rect = None
        self.cur_w = COMPACT_W
        self.view_name = u''
        try:
            doc = uiapp.ActiveUIDocument.Document if uiapp.ActiveUIDocument else None
            self.doc_title = doc.Title if doc else None
            self.view_name = doc.ActiveView.Name if doc and doc.ActiveView else u''
        except Exception:
            pass
        self.uiapp.ViewActivated += self.on_view       # API context: the model / view names
        self.win.Show()
        self.place()
        self.timer.Start()
        self.check()

    # ---- position: top centre of the Revit window ------------------------------------------
    def place(self):
        try:
            import ctypes
            from ctypes import wintypes
            r = wintypes.RECT()
            ctypes.windll.user32.GetWindowRect(wintypes.HWND(int(self.uiapp.MainWindowHandle.ToInt64())), ctypes.byref(r))
            rect = (r.left, r.top, r.right, r.bottom)
        except Exception:
            ext = self.uiapp.MainWindowExtents
            rect = (ext.Left, ext.Top, ext.Right, ext.Bottom)
        if rect == self.last_rect:
            return
        self.last_rect = rect
        try:      # a maximised window starts above the screen edge
            import System.Windows.Forms as wf
            top = wf.Screen.FromHandle(self.uiapp.MainWindowHandle).Bounds.Top
            rect = (rect[0], max(rect[1], top), rect[2], rect[3])
        except Exception:
            pass
        src = PresentationSource.FromVisual(self.win)
        k = src.CompositionTarget.TransformFromDevice.M11 if src else 1.0
        self.win.Left = (rect[0] + (rect[2] - rect[0]) / 2.0) * k - self.win.Width / 2.0
        self.win.Top = rect[1] * k + 1

    # ---- size animations (spring) ------------------------------------------------------------
    def size_to(self, w, h):
        self.cur_w = w
        ease = BackEase()
        ease.Amplitude = 0.35
        ease.EasingMode = EasingMode.EaseOut
        for prop, to in ((Border.WidthProperty, w), (Border.HeightProperty, h)):
            a = DoubleAnimation(to, Duration(TimeSpan.FromSeconds(0.42)))
            a.EasingFunction = ease
            self.pill.BeginAnimation(prop, a)
        self.pill.CornerRadius = CornerRadius(min(h / 2.0, 30))

    def fade(self, el, to, secs=0.25):
        el.BeginAnimation(StackPanel.OpacityProperty, DoubleAnimation(to, Duration(TimeSpan.FromSeconds(secs))))

    # ---- states --------------------------------------------------------------------------------
    def work_count(self):
        return sum(1 for key, _, _, _ in TOOLS if state_of(key, self.board)[0] == 'work')

    def compact_view(self):
        """Leading mark + title + trailing text for the closed capsule (live job > flash > work)."""
        bridge = web_bridge.BRIDGE
        cur = bridge.current if bridge is not None else None
        self.spin.Visibility = Visibility.Visible if cur is not None or self.checking else Visibility.Collapsed
        self.dot.Visibility = self.mark.Visibility = Visibility.Collapsed
        if cur is not None:
            self.title.Text = cur.data.get('label') or cur.name
            self.title.Foreground = brush('#FFFFFF')
            self.trail.Text = u'{0}s'.format(int(time.time() - (cur.started or time.time())))
            return LIVE_W
        if time.time() < self.flash_until:
            self.spin.Visibility = Visibility.Collapsed
            self.mark.Visibility = Visibility.Visible
            self.title.Text, self.trail.Text = self.flash_text, u''
            self.mark.Text = self.flash_mark
            self.mark.Foreground = self.title.Foreground = brush('#FF6961' if self.flash_mark == u'✕' else '#FFFFFF')
            return LIVE_W
        if self.checking:
            self.title.Text, self.trail.Text = u'Đang kiểm tra', u''
            self.title.Foreground = brush('#B3FFFFFF')
            return COMPACT_W
        n = self.work_count()
        self.trail.Text = u''
        if self.board is None or self.board.get('error'):
            self.dot.Visibility = Visibility.Visible
            self.dot.Fill = brush('#8E8E93')
            self.title.Text = self.doc_title or u'Revit'
            self.title.Foreground = brush('#B3FFFFFF')
        elif n:
            self.dot.Visibility = Visibility.Visible
            self.dot.Fill = brush(WORK)
            self.title.Text = u'{0} việc còn'.format(n)
            self.title.Foreground = brush(WORK)
        else:
            self.mark.Visibility = Visibility.Visible
            self.mark.Text, self.mark.Foreground = u'✓', brush('#FFFFFF')
            self.title.Text = u'Đã xong'
            self.title.Foreground = brush('#B3FFFFFF')
        return COMPACT_W

    def render_open(self):
        self.model.Text = self.doc_title or u'Chưa mở model'
        self.view.Text = self.view_name
        self.tools.Children.Clear()
        for key, label, _, _ in TOOLS:
            level, text, count = state_of(key, self.board)
            cell = StackPanel()
            cell.Width, cell.Margin, cell.Cursor = 62, Thickness(2, 0, 2, 0), Cursors.Hand
            cell.ToolTip = text or label
            ring = Border()
            ring.Width = ring.Height = 50
            ring.CornerRadius = CornerRadius(25)
            ring.HorizontalAlignment = HorizontalAlignment.Center
            ring.Background = brush('#40FF9F0A' if level == 'work' else '#24FFFFFF')
            ring.BorderBrush = brush(WORK if level == 'work' else '#00000000')
            ring.BorderThickness = Thickness(1.5)
            g = Grid()
            if self.icons.get(key) is not None:
                img = Image()
                img.Source, img.Width, img.Height = self.icons[key], 28, 28
                if level != 'work':
                    img.Opacity = 0.75
                g.Children.Add(img)
            if level == 'work' and count:
                badge = Border()
                badge.Background, badge.CornerRadius = brush(WORK), CornerRadius(8)
                badge.MinWidth, badge.Height, badge.Padding = 16, 16, Thickness(4, 0, 4, 0)
                badge.HorizontalAlignment, badge.VerticalAlignment = HorizontalAlignment.Right, VerticalAlignment.Top
                badge.Margin = Thickness(0, -3, -5, 0)
                t = TextBlock()
                t.Text, t.FontSize, t.Foreground = (u'99+' if count > 99 else str(count)), 10.5, brush('#000000')
                t.HorizontalAlignment = HorizontalAlignment.Center
                badge.Child = t
                g.Children.Add(badge)
            ring.Child = g
            name = TextBlock()
            name.Text, name.FontSize, name.Margin = label, 11.5, Thickness(0, 5, 0, 0)
            name.HorizontalAlignment = HorizontalAlignment.Center
            name.Foreground = brush(WORK if level == 'work' else '#B3FFFFFF')
            cell.Children.Add(ring)
            cell.Children.Add(name)
            cell.MouseLeftButtonUp += self._tool_handler(key)
            self.tools.Children.Add(cell)
        if not self.msg.Text:
            works = [state_of(k, self.board)[1] for k, _, _, _ in TOOLS if state_of(k, self.board)[0] == 'work']
            self.msg.Text = (u'Còn: ' + u' · '.join(works)) if works else (u'Không còn việc nào.' if self.board else u'')

    def open_height(self):
        self.open.Measure(Size(OPEN_W, 1000))       # the panel's own margins are inside this width
        return self.open.DesiredSize.Height      # includes the panel's margins

    def relayout(self):
        if self.expanded:
            self.render_open()
            self.size_to(OPEN_W, self.open_height())
        else:
            self.size_to(self.compact_view(), PILL_H)

    # ---- events --------------------------------------------------------------------------------
    def on_click(self, sender, e):
        if e.Handled:
            return
        if self.expanded:
            if e.OriginalSource is self.pill or e.GetPosition(self.pill).Y < PILL_H + 6:
                self.collapse()
            return
        self.expanded = True
        self.msg.Text = u''
        self.ask_clear(relayout=False)
        self.open.Visibility = Visibility.Visible
        self.fade(self.open, 1.0, 0.35)
        self.compact.Visibility = Visibility.Collapsed
        self.relayout()
        self.check()

    def collapse(self):
        self.expanded = False
        self.confirm = None
        self.open.Visibility = Visibility.Collapsed
        self.open.Opacity = 0
        self.compact.Visibility = Visibility.Visible
        self.relayout()

    def on_leave(self, sender, e):
        if self.expanded and self.confirm is None:
            self.hover_out = time.time()

    def _tool_handler(self, key):
        def handler(sender, e):
            e.Handled = True
            self.tool(key)
        return handler

    def ask(self, text, yes, action):
        self.msg.Text = text
        self.yesText.Text = yes
        self.confirm = action
        self.ask.Visibility = Visibility.Visible
        self.relayout()

    def ask_clear(self, relayout=True):
        self.confirm = None
        self.ask.Visibility = Visibility.Collapsed
        if relayout:
            self.msg.Text = u''
            self.relayout()

    def on_yes(self, sender, e):
        e.Handled = True
        action, self.confirm = self.confirm, None
        self.ask.Visibility = Visibility.Collapsed
        if action:
            action()

    def say(self, text):
        self.msg.Text = text
        self.relayout()

    # ---- the six tools: one main action each -----------------------------------------------
    def tool(self, key):
        b = self.board or {}
        level, text, count = state_of(key, self.board)
        if key == 'printSet':
            if level == 'work':
                self.ask(u'Đồng bộ print set với danh mục cùng tên?', u'Đồng bộ', lambda: self.run(
                    'print_set_sync', {'all': True}, u'Đồng bộ print set',
                    lambda r: u' · '.join(u'{0}: {1}'.format(x['schedule'], x['status']) for x in r.get('schedules', [])) or u'Không có gì để đồng bộ'))
            elif level == 'done':
                self.say(u'Print set đã khớp danh mục.')
            else:
                self.open_web('printSet')
        elif key == 'columns':
            off = [r['schedule'] for r in _official(b) if not r.get('inSync')] if b and not b.get('error') else []
            if off:
                self.ask(u'Áp dụng cột chuẩn + số No. cho: ' + u', '.join(off) + u'?', u'Áp dụng', lambda: self.run(
                    'sheet_list_columns', {'schedules': off}, u'Chuẩn cột danh mục',
                    lambda r: (u'Bị chặn (người khác đang giữ / sửa cuối): ' + u', '.join(r['blocked'])) if r.get('blocked')
                    else u'Thêm {0} cột, ghi {1} số No.'.format(r.get('fieldsAdded', 0), r.get('numbersWritten', 0))))
            else:
                self.say(u'Danh mục đã đúng chuẩn cột.' if level == 'done' else u'Model chưa có danh mục bản vẽ.')
        elif key == 'untagged':
            u = b.get('untagged') if b else None
            if u and any(u['counts'].values()):
                self.ask(u'Tô cam {0} trong view {1}?'.format(text, u['view']), u'Tô', lambda: self.run(
                    'untagged_highlight', {'views': [u['id']]}, u'Tô chỗ chưa tag',
                    lambda r: u'Đã tô chỗ chưa tag trong {0}.'.format(u['view'])))
            elif b and b.get('untaggedMarks'):
                self.ask(u'Xoá {0} vùng tô cam của bạn trong model?'.format(b['untaggedMarks']), u'Xoá', lambda: self.run(
                    'untagged_remove', {}, u'Xoá vùng tô', lambda r: u'Đã xoá {0} vùng tô.'.format(r.get('removed', 0))))
            else:
                self.say(text[:1].upper() + text[1:] + u'.' if text else u'')
        elif key == 'highlight':
            if level == 'work':
                self.ask(u'Xoá {0} vùng highlight của bạn trong model?'.format(count), u'Xoá', lambda: self.run(
                    'highlight_clear', {}, u'Xoá highlight', lambda r: u'Đã xoá {0} vùng.'.format(r.get('removed', 0))))
            else:
                self.run('highlight', {}, u'Highlight', lambda r: u'Đã tô {0} đối tượng.'.format(r.get('created', 0)))
        elif key == 'export':
            self.open_web('export')
        elif key == 'view3d':
            self.run('view_3d_from_view', {}, u'Mở 3D theo view', lambda r: u'Đã mở {0}.'.format(r.get('view3d') or u'view 3D'))

    def open_web(self, tool):
        try:
            with open(os.path.join(DATA, 'webapp.json')) as f:
                cfg = json.load(f)
            url = u'http://localhost:{0}/?k={1}'.format(cfg.get('port', 8780), cfg.get('token', ''))
            if tool:
                url += u'&open=' + tool
            web_bridge.ensure_webapp()
            Process.Start(url)
        except Exception as ex:
            self.say(u'Không mở được web app: {0}'.format(ex))

    # ---- jobs ------------------------------------------------------------------------------------
    def run(self, task, args, label, say=None):
        bridge = web_bridge.BRIDGE
        if bridge is None:
            return self.say(u'Cầu nối chưa chạy (pyRevit Reload).')
        job = web_bridge._Job('task', task, {'args': args, 'label': label})
        bridge.submit(job)
        self.pending.append((job, label, say))
        if self.expanded:
            self.collapse()          # the capsule shows the job as a live activity
        else:
            self.relayout()

    def check(self):
        if self.checking or web_bridge.BRIDGE is None:
            return
        if self.doc_title is None:
            self.board = None
            return self.relayout()
        self.checking = True
        job = web_bridge._Job('task', 'status_board', {'args': {}, 'label': u'Kiểm tra'})
        web_bridge.BRIDGE.submit(job)
        self.pending.append((job, None, None))
        self.relayout()

    def tick(self, sender, e):
        try:
            self.place()
            done = [p for p in self.pending if p[0].done.is_set()]
            for p in done:
                self.pending.remove(p)
                self.finished(*p)
            if self.hover_out and time.time() - self.hover_out > 0.9:
                self.hover_out = None
                self.collapse()
            if not self.expanded:
                w = self.compact_view()
                if w != self.cur_w:
                    self.size_to(w, PILL_H)
        except Exception:
            pass

    def finished(self, job, label, say):
        try:
            res = json.loads(job.result) if job.result else {'error': u'không có kết quả'}
        except Exception as ex:
            res = {'error': u'{0}'.format(ex)}
        if job.name == 'status_board':
            self.checking = False
            self.board = res
            self.relayout()
            return
        if res.get('error'):
            self.flash_text, self.flash_mark = u'{0}: {1}'.format(label, res['error'])[:80], u'✕'
        else:
            try:
                self.flash_text = say(res) if say else u'{0}: xong'.format(label)
            except Exception:
                self.flash_text = u'{0}: xong'.format(label)
            self.flash_mark = u'✓'
        self.flash_until = time.time() + 4
        self.relayout()
        self.check()

    def on_view(self, sender, e):
        try:
            doc = e.Document
            title = doc.Title if doc else None
            self.view_name = e.CurrentActiveView.Name if e.CurrentActiveView else u''
            if self.expanded:
                self.view.Text = self.view_name
            if title != self.doc_title:      # another model: its own work list
                self.doc_title = title
                self.board = None
                self.check()
        except Exception:
            pass

    def close(self):
        try:
            self.uiapp.ViewActivated -= self.on_view
        except Exception:
            pass
        try:
            self.timer.Stop()
            self.win.Close()
        except Exception:
            pass


def start(uiapp):
    """(Re)open the island of this Revit window (pyRevit reload replaces the old one)."""
    old = AppDomain.CurrentDomain.GetData(SLOT)
    if old is not None:
        try:
            old.close()
        except Exception:
            pass
        AppDomain.CurrentDomain.SetData(SLOT, None)
    if _settings().get('enabled', True) is False:
        return None
    isl = Island(uiapp)
    AppDomain.CurrentDomain.SetData(SLOT, isl)
    return isl
