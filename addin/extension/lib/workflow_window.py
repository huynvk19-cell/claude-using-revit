# -*- coding: utf-8 -*-
"""The windows of the button Workflow: Workflow -> Skill -> Card, each its own small window owned by Revit.

Each window shows one level only; a card's sections stay closed until clicked. Opening an item reuses the open window
of that level unless it is pinned. Everything comes from the files (workflow_lib.py), so no window here needs Revit's API
and they read the same while Revit is busy; a command holding Revit's thread stops this window too until it ends.

"Giao cho Claude" writes the message for the project chat and copies it: the project starts the thread and takes it from
there. setup() runs from startup.py and costs little; the startup engine stays alive, so the timer keeps working.
"""
import ctypes
import io
import os
import re
import time

import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('System.Xaml')
clr.AddReference('WindowsBase')

from System import Action, AppDomain, TimeSpan, Uri
from System.Diagnostics import Process, ProcessStartInfo
from System.Windows import (CornerRadius, FontWeights, GridLength, GridUnitType, SizeToContent, TextAlignment, TextWrapping,
                            Thickness, VerticalAlignment, Visibility, Window, WindowStartupLocation, WindowState)
from System.Windows.Controls import (Border, Button, ColumnDefinition, ComboBox, ContentControl, Grid, RadioButton, RowDefinition,
                                     StackPanel, TextBlock, TextBox, Orientation, WrapPanel)
from System.Windows.Controls.Primitives import UniformGrid
from System.Windows.Documents import Bold, Run
from System.Windows.Input import Key
from System.Windows.Interop import WindowInteropHelper
from System.Windows.Markup import XamlReader
from System.Windows.Media import BrushConverter, FontFamily
from System.Windows.Shapes import Ellipse
from System.Windows.Threading import DispatcherPriority, DispatcherTimer

import native_blur
import workflow_lib as lib
try:
    import content_update
except Exception:
    content_update = None

SLOT = 'ClaudeUsingRevit.Workflow'
HERE = os.path.dirname(os.path.abspath(__file__))
THEME = os.path.join(HERE, 'liquid_glass.xaml')
XAML = os.path.join(HERE, 'workflow.xaml')
ICON = os.path.join(os.path.dirname(HERE), u'claude using revit.tab', u'Workflow.panel', u'Workflow.pushbutton', u'icon.png')
WIDTHS = {'workflow': 410, 'skill': 470, 'card': 490, 'give': 500}
CHEVRON_RIGHT, CHEVRON_DOWN, TICK = u'', u'', u''
MONO = FontFamily('Cascadia Mono, Consolas')
_INLINE = re.compile(r'(`[^`]+`|\*\*[^*]+\*\*)')
_LIST = re.compile(r'^(\s*)(- |\d+\. )(.*)$')


def _brush(color):
    return BrushConverter().ConvertFromString(color)


def _log(ex):
    try:
        from cur import get_logger
        get_logger('claude-using-revit').warning('workflow window: %s', ex)
    except Exception:
        pass


def copy_text(text):
    """To the clipboard through Windows itself: WPF's Clipboard (OLE) can fail where this does not. Stays after Revit closes."""
    user32, kernel32 = ctypes.windll.user32, ctypes.windll.kernel32
    kernel32.GlobalAlloc.restype = ctypes.c_void_p
    kernel32.GlobalLock.restype = ctypes.c_void_p
    kernel32.GlobalLock.argtypes = [ctypes.c_void_p]
    kernel32.GlobalUnlock.argtypes = [ctypes.c_void_p]
    user32.SetClipboardData.argtypes = [ctypes.c_uint, ctypes.c_void_p]
    user32.SetClipboardData.restype = ctypes.c_void_p
    for attempt in range(6):        # another program may hold the clipboard for a moment
        if user32.OpenClipboard(None):
            try:
                user32.EmptyClipboard()
                buf = ctypes.create_unicode_buffer(text)
                mem = kernel32.GlobalAlloc(0x0042, ctypes.sizeof(buf))     # GHND: the clipboard keeps it
                ctypes.memmove(kernel32.GlobalLock(mem), buf, ctypes.sizeof(buf))
                kernel32.GlobalUnlock(mem)
                return bool(user32.SetClipboardData(13, mem))              # CF_UNICODETEXT
            finally:
                user32.CloseClipboard()
        time.sleep(0.1)
    return False


# ---- markdown of a card, as far as cards use it ------------------------------------------------------------------------

def add_inlines(block, text, res):
    for part in _INLINE.split(text):
        if not part:
            continue
        if part.startswith(u'`'):
            r = Run(part[1:-1])
            r.FontFamily = MONO
            r.FontSize = 12
            r.Background = res.FindResource('Fill2')
            block.Inlines.Add(r)
        elif part.startswith(u'**'):
            block.Inlines.Add(Bold(Run(part[2:-2])))
        else:
            block.Inlines.Add(Run(part))


def _para(text, res, left=0, bottom=4):
    t = TextBlock()
    t.TextWrapping = TextWrapping.Wrap
    t.Margin = Thickness(left, 0, 0, bottom)
    t.LineHeight = 19
    add_inlines(t, text, res)
    return t


def _code(lines, res, left):
    box = Border()
    box.CornerRadius = CornerRadius(8)
    box.Background = res.FindResource('Fill2')
    box.Padding = Thickness(9, 6, 9, 6)
    box.Margin = Thickness(left, 2, 0, 7)
    t = TextBlock()
    t.Text = u'\n'.join(lines)
    t.FontFamily = MONO
    t.FontSize = 11.5
    t.TextWrapping = TextWrapping.Wrap
    box.Child = t
    return box


def _table(rows, res, left):
    g = Grid()
    g.Margin = Thickness(left, 2, 0, 8)
    cols = max(len(r) for r in rows)
    for c in range(cols):
        d = ColumnDefinition()
        d.Width = GridLength(1, GridUnitType.Star) if c == cols - 1 else GridLength.Auto
        g.ColumnDefinitions.Add(d)
    for n, row in enumerate(rows):
        g.RowDefinitions.Add(RowDefinition())
        for c, cell in enumerate(row):
            t = _para(cell, res, 0, 3)
            t.Margin = Thickness(0, 0, 10, 3)
            if n == 0:
                t.Foreground = res.FindResource('Ink2')
                t.FontWeight = _semi()
            Grid.SetRow(t, n)
            Grid.SetColumn(t, c)
            g.Children.Add(t)
    return g


def markdown(text, res):
    """A StackPanel: paragraphs, numbered and plain lists by their indent, code, tables, small headings."""
    panel = StackPanel()
    lines = text.split(u'\n')
    i = 0
    while i < len(lines):
        line = lines[i]
        s = line.strip()
        if not s:
            i += 1
            continue
        indent = len(line) - len(line.lstrip())
        if s.startswith(u'```'):
            code = []
            i += 1
            while i < len(lines) and not lines[i].strip().startswith(u'```'):
                code.append(lines[i][min(indent, len(lines[i]) - len(lines[i].lstrip())):])
                i += 1
            i += 1
            panel.Children.Add(_code(code, res, indent * 5))
            continue
        if s.startswith(u'|'):
            rows = []
            while i < len(lines) and lines[i].strip().startswith(u'|'):
                row = [c.strip() for c in lines[i].strip().strip(u'|').split(u'|')]
                if not all(re.match(r'^:?-+:?$', c) for c in row):
                    rows.append(row)
                i += 1
            if rows:
                panel.Children.Add(_table(rows, res, indent * 5))
            continue
        if s.startswith(u'#'):
            t = _para(s.lstrip(u'# '), res, 0, 3)
            t.FontWeight = _semi()
            panel.Children.Add(t)
            i += 1
            continue
        m = _LIST.match(line)
        if m:
            mark = m.group(2).strip() if m.group(2)[0] != u'-' else u'•'
            head = [m.group(3).strip()]
            i += 1
            while i < len(lines) and lines[i].strip() and not _LIST.match(lines[i]) and not lines[i].strip().startswith((u'```', u'|')) \
                    and (len(lines[i]) - len(lines[i].lstrip())) > indent:
                head.append(lines[i].strip())
                i += 1
            row = Grid()
            row.Margin = Thickness(indent * 5, 0, 0, 4)
            a, b = ColumnDefinition(), ColumnDefinition()
            a.Width = GridLength(20)
            b.Width = GridLength(1, GridUnitType.Star)
            row.ColumnDefinitions.Add(a)
            row.ColumnDefinitions.Add(b)
            dot = TextBlock()
            dot.Text = mark
            dot.Foreground = res.FindResource('Ink2')
            body = _para(u' '.join(head), res, 0, 0)
            Grid.SetColumn(body, 1)
            row.Children.Add(dot)
            row.Children.Add(body)
            panel.Children.Add(row)
            continue
        para = [s]
        i += 1
        while i < len(lines) and lines[i].strip() and not _LIST.match(lines[i]) and not lines[i].strip().startswith((u'```', u'|', u'#')):
            para.append(lines[i].strip())
            i += 1
        panel.Children.Add(_para(u' '.join(para), res, indent * 5))
    return panel


# ---- one window ----------------------------------------------------------------------------------------------------------

class Pane(object):
    def __init__(self, host, kind, parent=None):
        self.host = host
        self.kind = kind
        self.item = None
        self.opened = set()         # the open sections of this window's item
        self.result_index = 0
        with io.open(XAML, encoding='utf-8') as f:
            self.root = XamlReader.Parse(f.read().replace(u'__THEME__', Uri(THEME).AbsoluteUri))
        native_blur.tool_icon(self.root, ICON)
        find = self.root.FindName
        self.stack, self.body, self.msg = find('stack'), find('body'), find('msg')
        self.title, self.sub, self.crumbs = find('head_title'), find('head_sub'), find('crumbs')
        self.b_pin, self.b_open, self.b_give = find('b_pin'), find('b_open'), find('b_give')
        self.window = self._make_window(parent) if host.uiapp is not None or host.show_windows else None
        find('b_close').Click += lambda s, e: self.close()
        self.b_open.Click += self._open_file
        self.b_give.Click += self._copy if kind == 'give' else self._give
        self.combo = None
        self.result_combo = None

    def _make_window(self, parent):
        w = Window()
        w.Title = u'Workflow'
        w.ShowInTaskbar = False
        w.Width = WIDTHS[self.kind]
        w.SizeToContent = SizeToContent.Height
        w.Resources.MergedDictionaries.Add(self.root.Resources)
        w.FontFamily = self.root.FindResource('Ui')
        w.FontSize = 13
        w.Foreground = self.root.FindResource('Ink')
        w.Background = _brush('Transparent')
        glass = ContentControl()
        glass.Style = self.root.FindResource('GlassPanel')
        glass.Content = self.root
        w.Content = glass
        if self.host.uiapp is not None:
            WindowInteropHelper(w).Owner = self.host.uiapp.MainWindowHandle
        if parent is not None and parent.window is not None and parent.window.IsLoaded:
            w.WindowStartupLocation = WindowStartupLocation.Manual
            w.Left = parent.window.Left + parent.window.ActualWidth + 8
            w.Top = parent.window.Top
        else:
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner if self.host.uiapp is not None else WindowStartupLocation.CenterScreen
        native_blur.apply(w, body=self.body)
        w.KeyDown += self._key
        w.Closed += lambda s, e: self.host.forget(self)
        return w

    def _key(self, sender, e):
        if e.Key == Key.Escape:
            self.close()

    def close(self):
        if self.window is not None:
            self.window.Close()

    @property
    def pinned(self):
        return self.b_pin.IsChecked == True

    def show(self):
        w = self.window
        if w is None:
            return
        w.Show()
        if w.WindowState == WindowState.Minimized:
            w.WindowState = WindowState.Normal
        w.Activate()
        w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Action(lambda: w.Activate()))  # a ribbon click takes it back

    def set_item(self, item):
        if item != self.item:
            self.opened = set()
            self.result_index = 0
        self.item = item
        self.render()
        self.body.ScrollToTop()

    # -- pieces ------------------------------------------------------------------------------------------------------
    def res(self, name):
        return self.root.FindResource(name)

    def _text(self, text, color='Ink', size=None, wrap=True, margin=None):
        t = TextBlock()
        t.Text = text
        t.Foreground = self.res(color)
        if size:
            t.FontSize = size
        if wrap:
            t.TextWrapping = TextWrapping.Wrap
        if margin:
            t.Margin = margin
        return t

    def _caption(self, text):
        t = self._text(text, 'Ink2', 11.5, margin=Thickness(4, 8, 0, 4))
        t.FontWeight = _semi()
        return t

    def _state(self, state):
        if state == 'run':
            e = Ellipse()
            e.Width = e.Height = 9
            e.Fill = self.res('Accent')
            e.VerticalAlignment = VerticalAlignment.Center
            e.Margin = Thickness(8, 0, 0, 0)
            e.ToolTip = u'Đang chạy'
            return e
        if state == 'done':
            t = self._text(TICK, 'Ink')
            t.FontFamily = self.res('Icons')
            t.FontSize = 12
            t.Foreground = _brush('#248A3D')
            t.VerticalAlignment = VerticalAlignment.Center
            t.Margin = Thickness(8, 0, 0, 0)
            t.ToolTip = u'Xong theo checklist'
            return t
        return None

    def _row(self, number, text, note=None, state=None, click=None, sub=None, look=None, tip=None):
        """A line: number | text (and a second line) | state | note."""
        b = Button()
        b.Style = self.res('WfRow')
        g = Grid()
        for w in (GridLength.Auto, GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto):
            d = ColumnDefinition()
            d.Width = w
            g.ColumnDefinitions.Add(d)
        if number is not None:
            n = self._text(number, 'Ink3', 11, wrap=False, margin=Thickness(0, 1, 8, 0))
            n.FontFamily = MONO
            n.MinWidth = 20
            g.Children.Add(n)
        words = StackPanel()
        Grid.SetColumn(words, 1)
        words.Children.Add(self._text(text))
        if sub:
            words.Children.Add(self._text(sub, 'Ink2', 11.5))
        g.Children.Add(words)
        mark = self._state(state)
        if mark is not None:
            Grid.SetColumn(mark, 2)
            g.Children.Add(mark)
        if note:
            t = self._text(note, 'Ink2', 11.5, wrap=False, margin=Thickness(8, 1, 0, 0))
            t.VerticalAlignment = VerticalAlignment.Center
            Grid.SetColumn(t, 3)
            g.Children.Add(t)
        b.Content = g
        if look == 'todo':
            b.Background = _brush('#38FF9F0A')
            for child in words.Children:
                child.FontWeight = _semi()
            if note:
                t.Foreground = _brush('#9D5D00')
                t.FontWeight = _semi()
        elif look == 'quiet':
            b.Opacity = 0.55
        if tip:
            b.ToolTip = tip
        if click is not None:
            b.Click += lambda s, e: click()
        else:
            b.IsHitTestVisible = False
        return b

    def _note(self, text, style='WarnNote'):
        box = Border()
        box.Style = self.res(style)
        box.Margin = Thickness(0, 0, 0, 8)
        box.Child = self._text(text)
        return box

    def _section(self, key, label, count, build):
        """A closed line; a click opens what build() returns below it."""
        is_open = key in self.opened
        b = Button()
        b.Style = self.res('WfRow')
        g = Grid()
        for w in (GridLength.Auto, GridLength(1, GridUnitType.Star), GridLength.Auto):
            d = ColumnDefinition()
            d.Width = w
            g.ColumnDefinitions.Add(d)
        chev = self._text(CHEVRON_DOWN if is_open else CHEVRON_RIGHT, 'Ink2', 10, wrap=False, margin=Thickness(0, 0, 9, 0))
        chev.FontFamily = self.res('Icons')
        chev.VerticalAlignment = VerticalAlignment.Center
        g.Children.Add(chev)
        name = self._text(label)
        Grid.SetColumn(name, 1)
        g.Children.Add(name)
        if count:
            c = self._text(count, 'Ink2', 11.5, margin=Thickness(10, 1, 0, 0))
            c.MaxWidth = 230
            c.TextAlignment = TextAlignment.Right
            Grid.SetColumn(c, 2)
            g.Children.Add(c)
        b.Content = g
        b.Click += lambda s, e: self._toggle(key)
        self.stack.Children.Add(b)
        if is_open:
            inner = build()
            inner.Margin = Thickness(12, 3, 2, 9)
            self.stack.Children.Add(inner)

    def _toggle(self, key):
        if key in self.opened:
            self.opened.discard(key)
        else:
            self.opened.add(key)
        keep = self.body.VerticalOffset
        self.render()
        self.body.ScrollToVerticalOffset(keep)

    def _head(self, title, sub=None, crumbs=()):
        self.title.Text = title
        self.sub.Text = sub or u''
        self.sub.Visibility = Visibility.Visible if sub else Visibility.Collapsed
        self.crumbs.Children.Clear()
        self.crumbs.Visibility = Visibility.Visible if crumbs else Visibility.Collapsed
        for text, go in crumbs:
            b = Button()
            b.Style = self.res('Crumb')
            b.Content = text
            b.Click += (lambda f: (lambda s, e: f()))(go)
            self.crumbs.Children.Add(b)
            self.crumbs.Children.Add(self._text(u'  ›  ', 'Ink3', 12, wrap=False))

    def _running(self):
        """The strip of a run this window's item is part of."""
        st = self.host.status
        if not st:
            return
        text = st.get('message') or st.get('title') or u''
        if st.get('step'):
            text = u'{0} · {1}'.format(text, st.get('step'))
        self.stack.Children.Add(self._note(u'Claude đang chạy: {0}'.format(text), 'DoneNote'))

    # -- the levels ----------------------------------------------------------------------------------------------------
    def render(self):
        try:
            self.stack.Children.Clear()
            self.msg.Text = self.host.note()
            getattr(self, '_render_' + self.kind)()
        except Exception as ex:
            _log(ex)
            self.msg.Text = u'Lỗi: {0}'.format(ex)

    def _render_workflow(self):
        lib_ = self.host.library
        self._head(u'Workflow', u'Chọn quy trình, bấm một skill để xem card')
        self.b_give.IsEnabled = self.item != lib.OTHER
        self.b_open.IsEnabled = self.item != lib.OTHER
        if self.combo is None:
            self.combo = ComboBox()
            self.combo.Margin = Thickness(0, 0, 0, 10)
            native_blur.combo(self.combo)
            self.combo.SelectionChanged += self._picked
        self._filling = True
        names = [w.name for w in lib_.workflows] + [lib.OTHER]
        keys = [w.key for w in lib_.workflows] + [lib.OTHER]
        self.keys = keys
        self.combo.Items.Clear()
        for n in names:
            self.combo.Items.Add(n)
        self.combo.SelectedIndex = keys.index(self.item) if self.item in keys else 0
        self._filling = False
        self.stack.Children.Add(self.combo)
        if self.item == lib.OTHER:
            self._render_other()
            return
        w = lib_.workflow(self.item)
        if w is None:
            self.stack.Children.Add(self._text(u'Chưa thấy workflow nào trong ~/.claude/drafting-domain/workflows.', 'Ink2'))
            return
        st = self.host.status
        if st and st.get('workflow') == w.key:
            self._running()
        done = self.host.done_set()
        for n, r in enumerate(w.rows):
            if not r.skill:
                self.stack.Children.Add(self._row(r.number, u'Skill theo chủ đề của từng comment', sub=r.applies))
                continue
            state = None
            if st and st.get('workflow') == w.key and st.get('skill') == r.skill:
                state = 'run'
            elif (r.cards and all((r.number, c) in done for c in r.cards)) or (not r.cards and (r.number, None) in done):
                state = 'done'
            note = u'{0} card'.format(len(r.cards)) if r.cards else r.label
            self.stack.Children.Add(self._row(r.number, lib.short(r.skill), note, state,
                                              (lambda row: (lambda: self.host.open_skill(row.skill, w.key, row, self)))(r)))
        if w.why:
            self.stack.Children.Add(self._text(u'', size=4))
            self._section('why', u'Vì sao theo thứ tự này', u'', lambda: markdown(w.why, self.root))

    def _render_other(self):
        skills, cards = self.host.library.other()
        self.stack.Children.Add(self._caption(u'Không nằm trong workflow nào'))
        for s in skills:
            n = len(self.host.library.skills[s].cards)
            self.stack.Children.Add(self._row(None, lib.short(s), u'{0} card'.format(n) if n else u'',
                                              click=(lambda x: (lambda: self.host.open_skill(x, None, None, self)))(s)))
        for s, c in cards:
            card = self.host.library.skills[s].cards[c]
            self.stack.Children.Add(self._row(c, card.name, sub=lib.short(s),
                                              click=(lambda x, y: (lambda: self.host.open_card(x, y, None, self)))(s, c)))

    def _picked(self, sender, e):
        if self._filling or self.combo.SelectedIndex < 0:
            return
        self.item = self.keys[self.combo.SelectedIndex]
        self.host.last_workflow = self.item
        self.opened = set()
        self.render()

    def _render_skill(self):
        name, wf_key, row = self.item
        skill = self.host.library.skills.get(name)
        if skill is None:
            self._head(lib.short(name))
            self.stack.Children.Add(self._text(u'Skill này không còn trên máy.', 'Ink2'))
            return
        wf = self.host.library.workflow(wf_key) if wf_key else None
        crumbs = [(wf.name, lambda: self.host.open_workflow(wf.key))] if wf else []
        ids = list(row.cards) if row is not None and row.cards else skill.card_ids()
        self._head(lib.short(name), u'{0} card'.format(len(ids)) if ids else None, crumbs)
        self.b_give.IsEnabled = self.b_open.IsEnabled = True
        st = self.host.status
        running = st if st and st.get('skill') == name else None
        if running:
            self._running()
        marks = self._develop_block(skill)
        if ids:
            self.stack.Children.Add(self._caption(u'Card' if marks is None else u'Card, làm nốt phần lệnh chưa làm được'))
            done = self.host.done_set()
            todo, clear = marks if marks else ({}, [])
            for c in ids:
                card = skill.cards[c]
                state = 'run' if running and running.get('card') == c else 'done' if row is not None and (row.number, c) in done else None
                look, note, tip = None, None, None
                if c in todo:
                    look, note = 'todo', u'còn {0} chỗ'.format(todo[c])
                elif c in clear:
                    look, tip = 'quiet', u'Lệnh không báo còn sót rule này trên view đang chọn'
                self.stack.Children.Add(self._row(c, card.name, note, state,
                                                  (lambda x: (lambda: self.host.open_card(name, x, wf_key, self)))(c), look=look, tip=tip))
            self.stack.Children.Add(self._text(u'', size=4))
            self._section('skill', u'Mô tả skill (SKILL.md)', u'', lambda: markdown(skill.body, self.root))
        else:
            if skill.intro:
                self.stack.Children.Add(markdown(_no_title(skill.intro), self.root))
            for head, text in skill.sections:
                self._section(head, lib.section_label(head), lib.section_count(head, text), (lambda t: (lambda: markdown(t, self.root)))(text))

    def _develop_block(self, skill):
        """The command's part of a skill. Returns (cards still open, cards it reports nothing open on), or None."""
        if not lib.develops(skill.name):
            return None
        found = lib.results_for(skill.name, self.host.results)
        box = Border()
        box.Style = self.res('Well')
        box.CornerRadius = CornerRadius(14)
        box.Margin = Thickness(0, 0, 0, 8)
        inner = StackPanel()
        box.Child = inner
        top = Grid()
        a, b = ColumnDefinition(), ColumnDefinition()
        a.Width = GridLength(1, GridUnitType.Star)
        b.Width = GridLength.Auto
        top.ColumnDefinitions.Add(a)
        top.ColumnDefinitions.Add(b)
        left = StackPanel()
        name = self._text(u'view_develop', wrap=False)
        name.FontFamily = MONO
        left.Children.Add(name)
        left.Children.Add(self._text(u'Chạy trước: lệnh đặt được bao nhiêu thì đặt', 'Ink2', 11.5))
        top.Children.Add(left)
        inner.Children.Add(top)
        self.stack.Children.Add(box)
        if not found:
            inner.Children.Add(self._text(u'Chưa có kết quả nào trên máy này.', 'Ink2', 12, margin=Thickness(0, 6, 0, 0)))
            return None
        if self.result_index >= len(found):
            self.result_index = 0
        if self.result_combo is None:
            self.result_combo = ComboBox()
            self.result_combo.MaxWidth = 250
            self.result_combo.MinWidth = 170
            native_blur.combo(self.result_combo)
            self.result_combo.SelectionChanged += self._result_picked
        self._result_filling = True
        if self.result_combo.Parent is not None:
            self.result_combo.Parent.Children.Remove(self.result_combo)
        self.result_combo.Items.Clear()
        for r in found:
            self.result_combo.Items.Add(r.label())
        self.result_combo.SelectedIndex = self.result_index
        self._result_filling = False
        Grid.SetColumn(self.result_combo, 1)
        top.Children.Add(self.result_combo)
        r = found[self.result_index]
        if r.error:
            inner.Children.Add(self._text(u'Lỗi: {0}'.format(r.error), 'Ink', 12, margin=Thickness(0, 6, 0, 0)))
            return None
        tiles = UniformGrid()
        tiles.Rows = 1
        tiles.Margin = Thickness(0, 8, 0, 0)
        for value, label, hot in ((r.placed, u'Đã đặt', False), (r.ask, u'Cần hỏi', False), (r.left_total, u'Còn lại', r.left_total > 0),
                                  (r.errors, u'Lỗi', False), (r.overlaps, u'Chồng lấn', False)):
            tile = Border()
            tile.CornerRadius = CornerRadius(10)
            tile.Padding = Thickness(8, 4, 8, 5)
            tile.Margin = Thickness(0, 0, 4, 0)
            tile.Background = _brush('#38FF9F0A') if hot else self.res('TileFill')
            s = StackPanel()
            n = self._text(unicode(value), wrap=False)
            n.FontFamily = self.res('UiDisplay')
            n.FontSize = 16
            n.FontWeight = _semi()
            s.Children.Add(n)
            s.Children.Add(self._text(label, 'Ink2', 11, wrap=False))
            tile.Child = s
            tiles.Children.Add(tile)
        inner.Children.Add(tiles)
        foot = u'{0}, {1}'.format(r.at, r.doc) if r.doc else r.at
        inner.Children.Add(self._text(foot, 'Ink3', 11, margin=Thickness(2, 5, 0, 0)))
        todo, clear, loose = lib.card_marks(skill, r)
        if loose:
            inner.Children.Add(self._text(u'Còn lại không khớp card nào: {0}'.format(u', '.join(loose)), 'Ink2', 11.5, margin=Thickness(2, 3, 0, 0)))
        return todo, clear

    def _result_picked(self, sender, e):
        if self._result_filling or self.result_combo.SelectedIndex < 0:
            return
        self.result_index = self.result_combo.SelectedIndex
        self.host.dispatcher_later(self.render)

    def _render_card(self):
        name, cid, wf_key = self.item
        skill = self.host.library.skills.get(name)
        card = skill.cards.get(cid) if skill else None
        if card is None:
            self._head(u'{0} · {1}'.format(lib.short(name), cid))
            self.stack.Children.Add(self._text(u'Card này không còn trên máy.', 'Ink2'))
            return
        wf = self.host.library.workflow(wf_key) if wf_key else None
        crumbs = []
        if wf:
            crumbs.append((wf.name, lambda: self.host.open_workflow(wf.key)))
        crumbs.append((lib.short(name), lambda: self.host.open_skill(name, wf_key, None, self)))
        self._head(card.title, card.applies or None, crumbs)
        self.b_give.IsEnabled = self.b_open.IsEnabled = True
        st = self.host.status
        if st and st.get('skill') == name and st.get('card') == cid:
            self._running()
        if card.intro:
            self.stack.Children.Add(markdown(card.intro, self.root))
        for head, text in card.sections:
            self._section(head, lib.section_label(head), lib.section_count(head, text), (lambda t: (lambda: markdown(t, self.root)))(text))
        if card.tools:
            self._section(u'tools', u'Tools', u'{0} tool'.format(len(card.tools)), lambda: self._tools(card))
        if card.standard:
            self._section(u'standard', u'Tiêu chuẩn', u'', lambda: _para(card.standard, self.root))
        used = self.host.library.used_in(name, cid)
        self.stack.Children.Add(self._text(u'Dùng trong: {0}'.format(u', '.join(w.name for w in used)) if used else u'Không nằm trong workflow nào',
                                           'Ink2', 12, margin=Thickness(4, 8, 0, 0)))

    def _tools(self, card):
        wrap = WrapPanel()
        for tool in card.tools:
            t = self._text(tool, 'Ink', 11.5, wrap=False)
            t.FontFamily = MONO
            box = Border()
            box.CornerRadius = CornerRadius(6)
            box.Background = self.res('Fill2')
            box.Padding = Thickness(6, 1, 6, 2)
            box.Margin = Thickness(0, 0, 4, 4)
            box.Child = t
            wrap.Children.Add(box)
        return wrap

    # -- buttons ----------------------------------------------------------------------------------------------------------
    def _path(self):
        if self.kind == 'workflow':
            w = self.host.library.workflow(self.item)
            return w.path if w else None
        if self.kind == 'skill':
            s = self.host.library.skills.get(self.item[0])
            return s.path if s else None
        if self.kind == 'card':
            s = self.host.library.skills.get(self.item[0])
            c = s.cards.get(self.item[1]) if s else None
            return c.path if c else None
        return None

    def _open_file(self, sender, e):
        path = self._path()
        if path and os.path.isfile(path):
            info = ProcessStartInfo(path)
            info.UseShellExecute = True
            try:
                Process.Start(info)
            except Exception as ex:
                self.msg.Text = u'Không mở được file: {0}'.format(ex)

    def _subject(self):
        if self.kind == 'workflow':
            w = self.host.library.workflow(self.item)
            return u'Chạy workflow `{0}` ({1}).'.format(w.key, w.name)
        if self.kind == 'skill':
            name, _, row = self.item
            if row is not None and row.cards and row.cards != self.host.library.skills[name].card_ids():
                return u'Chạy skill `{0}`, các card {1}.'.format(name, u', '.join(row.cards))
            return u'Chạy skill `{0}`.'.format(name)
        name, cid, _ = self.item
        return u'Chạy card {0} {1} của skill `{2}`.'.format(cid, self.host.library.skills[name].cards[cid].name, name)

    def _give(self, sender, e):
        self.host.open_give(self._subject(), self)

    # -- hand-off --------------------------------------------------------------------------------------------------------
    def _render_give(self):
        subject = self.item
        self._head(u'Giao cho Claude', subject)
        self.b_pin.Visibility = Visibility.Collapsed
        self.b_open.Visibility = Visibility.Collapsed
        self.b_give.Content = u'Chép tin nhắn'
        model, shown, is_sheet = lib.parse_title(self.host.revit_title())
        self.model = model
        self.stack.Children.Add(self._caption(u'Phạm vi'))
        self.scopes = []
        if shown:
            self._scope(u'{0} đang mở: {1}'.format(u'Sheet' if is_sheet else u'View', shown.replace(u'Sheet: ', u'')),
                        u'{0} đang mở "{1}"'.format(u'sheet' if is_sheet else u'view', shown.replace(u'Sheet: ', u'')))
        self.named_box = TextBox()
        self.named_box.Margin = Thickness(0, 0, 0, 6)
        self.named_box.Visibility = Visibility.Collapsed
        self.named_box.TextChanged += lambda s, e: self._scope_changed()
        self._scope(u'Sheet hoặc view chỉ định (ghi bên dưới)', None)
        self.stack.Children.Add(self.named_box)
        self._scope(u'Toàn model: mọi sheet có loại bản vẽ này', u'toàn model, mọi sheet có loại bản vẽ này')
        self.stack.Children.Add(self._note(u'Khi lệnh chạy, Revit đứng tới khi lệnh xong. Đừng làm việc trong cửa sổ Revit này: phần đặt dùng chung ngăn Undo với thao tác của bạn.'))
        self.stack.Children.Add(self._caption(u'Tin nhắn'))
        self.message = TextBox()
        self.message.AcceptsReturn = True
        self.message.TextWrapping = TextWrapping.Wrap
        self.message.Padding = Thickness(10, 6, 10, 6)
        self.stack.Children.Add(self.message)
        self._scope_changed()

    def _scope(self, label, scope):
        r = RadioButton()
        r.Style = self.res('Pick')
        r.GroupName = u'scope{0}'.format(id(self))
        t = self._text(label)
        r.Content = t
        r.Checked += lambda s, e: self._scope_changed()
        self.scopes.append((r, scope))
        self.stack.Children.Add(r)

    def _scope_changed(self):
        chosen, named = None, False
        for r, scope in self.scopes:
            if r.IsChecked == True:
                named = scope is None
                chosen = scope if not named else (self.named_box.Text.strip() or None)
        self.named_box.Visibility = Visibility.Visible if named else Visibility.Collapsed
        self.b_give.IsEnabled = bool(chosen)
        self.message.Text = lib.handoff_text(self.item, chosen, self.host.pid(), self.model, lib.holder_pid()) if chosen else u''
        self.msg.Text = u'' if chosen else u'Chọn phạm vi trước.'

    def _copy(self, sender, e):
        if copy_text(self.message.Text):
            self.msg.Text = u'Đã chép. Dán vào project chat để giao việc.'
        else:
            self.msg.Text = u'Chưa chép được vì chương trình khác đang giữ clipboard. Bấm lại, hoặc chọn và chép trong ô tin nhắn.'


def _semi():
    return FontWeights.SemiBold


def _no_title(text):
    return u'\n'.join(l for l in text.split(u'\n') if not l.startswith(u'# ')).strip()


# ---- all the windows --------------------------------------------------------------------------------------------------

class Host(object):
    def __init__(self, uiapp, show_windows=False):
        self.uiapp = uiapp
        self.show_windows = show_windows
        self.panes = []
        self.library = None
        self.status = None
        self.results = []
        self.last_workflow = None
        self._stamp = None
        self.closed = False
        self.dispatcher = None
        self.timer = None

    def reload(self):
        self.library = lib.Library()
        self.results = lib.results()
        self.status = lib.run_status()

    def note(self):
        if self.library is not None and not self.library.workflows:
            return u'Chưa cài skill và workflow trên máy này.'
        return content_update.note() if content_update is not None else u''

    def done_set(self):
        st = self.status
        return lib.checklist_done(st.get('checklist')) if st and st.get('checklist') else set()

    def forget(self, pane):
        if pane in self.panes:
            self.panes.remove(pane)
        if not self.panes and self.timer is not None:
            self.timer.Stop()

    def _pane(self, kind, parent=None):
        for p in self.panes:
            if p.kind == kind and not p.pinned:
                return p
        p = Pane(self, kind, parent)
        self.panes.append(p)
        return p

    def _start_timer(self, pane):
        if pane.window is None:
            return
        self.dispatcher = pane.window.Dispatcher
        if self.timer is None:
            self.timer = DispatcherTimer()
            self.timer.Interval = TimeSpan.FromSeconds(2)
            self.timer.Tick += self._tick
        self.timer.Start()

    def _tick(self, sender, e):
        """Changes of a run or of a result, read from their files."""
        try:
            st = lib.run_status()
            sum_ = st.get('checklist') if st else None
            stamp = (repr(sorted(st.items())) if st else u'', lib.results_stamp(),
                     os.path.getmtime(sum_) if sum_ and os.path.isfile(sum_) else 0, self.note())
            if stamp != self._stamp:
                self._stamp = stamp
                self.status = st
                self.results = lib.results()
                for p in list(self.panes):
                    if p.kind != 'give':
                        keep = p.body.VerticalOffset
                        p.render()
                        p.body.ScrollToVerticalOffset(keep)
        except Exception as ex:
            _log(ex)

    def dispatcher_later(self, fn):
        if self.dispatcher is not None:
            self.dispatcher.BeginInvoke(DispatcherPriority.Background, Action(fn))
        else:
            fn()

    # -- what the panes open ---------------------------------------------------------------------------------------------
    def open_workflow(self, key=None):
        st = self.status
        key = key or (st.get('workflow') if st else None) or self.last_workflow or (self.library.workflows[0].key if self.library.workflows else lib.OTHER)
        p = self._pane('workflow')
        p.set_item(key)
        self._start_timer(p)
        p.show()
        return p

    def open_skill(self, name, wf_key, row, parent):
        p = self._pane('skill', parent)
        p.set_item((name, wf_key, row))
        self._start_timer(p)
        p.show()
        return p

    def open_card(self, name, cid, wf_key, parent):
        p = self._pane('card', parent)
        p.set_item((name, cid, wf_key))
        self._start_timer(p)
        p.show()
        return p

    def open_give(self, subject, parent):
        p = self._pane('give', parent)
        p.set_item(subject)
        self._start_timer(p)
        p.show()
        return p

    def show(self):
        """The ribbon button: the files are read again, then the Workflow window opens."""
        self.reload()
        for p in list(self.panes):
            if p.kind != 'give':
                p.render()
        self.open_workflow()
        return True

    def revit_title(self):
        try:
            p = Process.GetCurrentProcess()
            p.Refresh()
            return p.MainWindowTitle
        except Exception:
            return u''

    def pid(self):
        return Process.GetCurrentProcess().Id

    def close(self):
        """A reload replaces this host."""
        self.closed = True
        for p in list(self.panes):
            p.close()


def setup(uiapp):
    """Called by startup.py when claude using revit starts and on Tải lại."""
    old = AppDomain.CurrentDomain.GetData(SLOT)
    if old is not None:
        old.close()
    host = Host(uiapp)
    AppDomain.CurrentDomain.SetData(SLOT, host)
    return host
