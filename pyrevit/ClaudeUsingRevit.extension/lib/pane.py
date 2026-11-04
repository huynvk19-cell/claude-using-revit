# -*- coding: utf-8 -*-
"""The panel "Bảng & công cụ": a native WPF dockable pane of the add-in, no web page inside.

Bảng: any schedule as an Excel-like DataGrid. Each cell is a text column f<i> of a DataTable with a state column
s<i> (chg, ro, lock) that only a style trigger reads, so colours cost no Python per cell. Edits stay in the panel
until "Ghi vào Revit". Keys: Enter / Tab / arrows, F2 or typing edits, Delete clears, Ctrl+C / V (TSV, to and from
Excel), Ctrl+D / R fill, Ctrl+Z / Y, Ctrl+H find & replace, Ctrl+F filter, Ctrl+S write. The square at the corner of
the selection drags a fill or a number series down, up or sideways (Ctrl: plain copy); it sits on a canvas over the
grid and follows the selected cells. Sort and filters only change what the panel shows. Công cụ: what the tab's tools have left to do (task status_board).

Revit work goes through web_bridge's ExternalEvent queue as tasks (schedule_list, schedule_table,
schedule_table_write, status_board): a pane is outside any API context. A thread waits for the job, the answer
comes back on the UI thread.

Revit lets a dockable pane register only while starting up (or with no model open), so setup() runs from
startup.py and costs little. After a pyRevit reload with a model open, or a first install, Revit accepts the
registration but creates the pane only at its next start: show() returns False and the button says so. Revit
remembers a pane's place by its id, so PANE_ID changes when the first place has to change. The startup engine
stays alive, so the event handlers keep working.
"""
import io
import json
import os
import re
import threading

import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('System.Data')
clr.AddReference('System.Xaml')
clr.AddReference('WindowsBase')

from System import Action, AppDomain, DBNull, Guid, Int32, String
from System.Data import DataTable
from System import TimeSpan
from System.Windows import (ContentElement, DataTrigger, LogicalTreeHelper, MessageBox, MessageBoxButton, MessageBoxResult,
                            Point, Setter, SizeToContent, Style, TextWrapping, Thickness, Trigger, Visibility, Window,
                            WindowStartupLocation)
from System.Windows.Controls import (Button, Canvas, CheckBox, ComboBox, DataGridCell, DataGridCellInfo, DataGridEditAction,
                                     DataGridEditingUnit, DataGridLength, DataGridRow, DataGridTextColumn, ListBox, Page,
                                     RadioButton, ScrollViewer, StackPanel, TextBlock, TextBox, WrapPanel)
from System.Windows.Controls.Primitives import DataGridColumnHeadersPresenter
from System.Windows.Data import Binding, BindingMode, UpdateSourceTrigger
from System.Windows.Input import Key, Keyboard, ModifierKeys
from System.Windows.Interop import WindowInteropHelper
from System.Windows.Markup import XamlReader
from System.Windows.Media import BrushConverter, Visual, VisualTreeHelper
from System.Windows.Threading import DispatcherTimer
from Microsoft.Win32 import OpenFileDialog, SaveFileDialog

from Autodesk.Revit.DB import Rectangle
from Autodesk.Revit.UI import DockablePaneId, DockablePanes, DockablePaneState, DockPosition, IDockablePaneProvider

import table_ops as ops
import web_bridge

SLOT = 'ClaudeUsingRevit.Pane'
PANE_ID = Guid('c3a94e17-2d6b-4f08-8e51-7a0b9d4c6e30')
TITLE = u'Bảng & công cụ'
HERE = os.path.dirname(os.path.abspath(__file__))
SETTINGS = os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'pane.json')
_bc = BrushConverter()


def brush(c):
    return _bc.ConvertFrom(c)


def find_child(node, kind):
    """The first element of a type under node in the visual tree."""
    for k in range(VisualTreeHelper.GetChildrenCount(node)):
        c = VisualTreeHelper.GetChild(node, k)
        if isinstance(c, kind):
            return c
        hit = find_child(c, kind)
        if hit is not None:
            return hit
    return None


def find_parent(node, kind):
    while node is not None and not isinstance(node, kind):
        node = LogicalTreeHelper.GetParent(node) if isinstance(node, ContentElement) or not isinstance(node, Visual) else VisualTreeHelper.GetParent(node)
    return node


def load_settings():
    try:
        with io.open(SETTINGS, encoding='utf-8') as f:
            return json.load(f)
    except Exception:
        return {}


def save_settings(d):
    try:
        folder = os.path.dirname(SETTINGS)
        if not os.path.isdir(folder):
            os.makedirs(folder)
        with io.open(SETTINGS, 'w', encoding='utf-8') as f:
            f.write(u'{0}'.format(web_bridge._dumps(d)))
    except Exception:
        pass


class _Provider(IDockablePaneProvider):
    def __init__(self, element):
        self.element = element

    def SetupDockablePane(self, data):
        data.FrameworkElement = self.element
        data.VisibleByDefault = False
        state = DockablePaneState()
        # a tab beside the Project Browser. Not a new dock side: Revit 2023 opens DockPosition.Bottom as a sliver
        state.DockPosition = DockPosition.Tabbed
        state.TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
        state.SetFloatingRectangle(Rectangle(120, 80, 1500, 960))
        data.InitialState = state


# ---- small native dialogs ------------------------------------------------------------------

def _dialog(host, title, content, width=460):
    w = Window()
    w.Title = title
    w.Width = width
    w.SizeToContent = SizeToContent.Height
    w.WindowStartupLocation = WindowStartupLocation.CenterOwner
    w.ShowInTaskbar = False
    panel = StackPanel()
    panel.Margin = Thickness(10)
    for c in content:
        panel.Children.Add(c)
    w.Content = panel
    WindowInteropHelper(w).Owner = host.uiapp.MainWindowHandle
    return w


def _text(s, wrap=True, dim=False):
    t = TextBlock()
    t.Text = s
    t.Margin = Thickness(0, 0, 0, 5)
    if wrap:
        t.TextWrapping = TextWrapping.Wrap
    if dim:
        t.Foreground = brush('#5E5E5E')
    return t


def _row(*buttons):
    p = WrapPanel()
    p.Margin = Thickness(0, 5, 0, 0)
    for b in buttons:
        p.Children.Add(b)
    return p


def _button(label, click):
    b = Button()
    b.Content = label
    b.Padding = Thickness(10, 2, 10, 2)
    b.Margin = Thickness(0, 0, 4, 0)
    b.Click += lambda s, e: click()
    return b


def _check(label, value=False):
    c = CheckBox()
    c.Content = label
    c.IsChecked = value
    c.Margin = Thickness(0, 0, 10, 0)
    return c


# ---- Bảng ------------------------------------------------------------------------------------

class Table(object):
    def __init__(self, host, root):
        self.host = host
        self.f = lambda name: root.FindName(name)
        self.grid = self.f('grid')
        self.status = self.f('status')
        self.pick = self.f('pick')
        self.list, self.data, self.cols, self.rows = [], None, [], []
        self.dt, self.dv, self.drows = None, None, []
        self.shown, self.filters, self.columns_by_ci = [], {}, {}
        self.sort_col, self.sort_dir = None, 0
        self.undo_stack, self.redo_stack = [], []
        self.unlock, self.busy, self.editing, self.loading = False, False, False, False
        self.doc_title = None
        self.over, self.handle, self.fillbox = self.f('over'), self.f('handle'), self.f('fillbox')
        self.box, self.drag = None, None
        self.timer = DispatcherTimer()
        self.timer.Interval = TimeSpan.FromMilliseconds(60)
        self.timer.Tick += self._drag_tick
        self.handle.MouseLeftButtonDown += self._handle_down
        self.handle.MouseMove += self._handle_move
        self.handle.MouseLeftButtonUp += self._handle_up
        self.grid.LayoutUpdated += lambda s, e: self._place_handle()
        for name, fn in (('b_reload', self.reload), ('b_undo', self.undo), ('b_redo', self.redo), ('b_series', self.series),
                         ('b_fill', self.fill_down), ('b_find', self.find_replace), ('b_formula', self.formula),
                         ('b_add', self.add_rows), ('b_filter', self.to_filter), ('b_xlsx', lambda: self.export('xlsx')), ('b_csv', lambda: self.export('csv')),
                         ('b_import', self.import_file), ('b_save', self.save)):
            self.f(name).Click += self._clicked(fn)
        self.f('c_hidden').IsChecked = bool(load_settings().get('hidden'))
        self.f('c_hidden').Click += lambda s, e: self._hidden_changed()
        self.f('c_only').Click += lambda s, e: self.apply_filter()
        self.f('c_unlock').Click += lambda s, e: self._unlock_changed()
        self.f('q').TextChanged += lambda s, e: self.apply_filter()
        self.pick.SelectionChanged += self._picked
        self.grid.PreviewKeyDown += self._key
        self.grid.BeginningEdit += self._begin_edit
        self.grid.CellEditEnding += self._end_edit
        self.grid.Sorting += self._sorting
        self.grid.LoadingRow += self._row_loading
        self.grid.SelectedCellsChanged += self._selection_changed

    def _clicked(self, fn):
        def go(sender, e):
            try:
                fn()
            except Exception as ex:
                self.say(u'Lỗi: {0}'.format(ex))
        return go

    def say(self, text):
        self.status.Text = text

    # -- load ------------------------------------------------------------------------------------
    def dirty_count(self):
        return sum(1 if r.get('new') else sum(1 for a, b in zip(r['v'], r['o']) if a != b) for r in self.rows)

    def leave_ok(self):
        if not self.dirty_count():
            return True
        return MessageBox.Show(u'Bảng còn ô đã sửa chưa ghi vào Revit. Bỏ các thay đổi đó?', TITLE, MessageBoxButton.YesNo) == MessageBoxResult.Yes

    def refresh_doc(self):
        """The list of schedules follows the model on screen."""
        doc = self.host.uiapp.ActiveUIDocument
        title = doc.Document.Title if doc is not None else None
        if title == self.doc_title:
            return
        self.doc_title = title
        self.clear()
        if title:
            self.say(u'Đang đọc danh sách schedule…')
            self.host.run('task', 'schedule_list', {}, self._got_list)

    def clear(self):
        self.list, self.data, self.rows, self.cols = [], None, [], []
        self.undo_stack, self.redo_stack = [], []
        self.grid.ItemsSource = None
        self.grid.Columns.Clear()
        self.dt, self.dv, self.drows, self.columns_by_ci = None, None, [], {}
        self.loading = True
        self.pick.ItemsSource = None
        self.loading = False
        self.say(u'')

    def _got_list(self, res):
        self.list = sorted(res.get('schedules') or [], key=lambda s: (not s.get('sheetList'), (s.get('category') or u'').lower(), s['name'].lower()))
        labels = [(u'Danh mục bản vẽ' if s.get('sheetList') else s.get('category') or u'') + u' · ' + s['name'] for s in self.list]
        self.loading = True
        self.pick.ItemsSource = labels
        self.loading = False
        doc = self.host.uiapp.ActiveUIDocument
        try:
            active = doc.ActiveView.Id.IntegerValue
        except Exception:
            active = None
        last = load_settings().get('schedule')
        idx = next((i for i, s in enumerate(self.list) if s['id'] == active), None)
        if idx is None:
            idx = next((i for i, s in enumerate(self.list) if s['name'] == last), None)
        if idx is None:
            idx = 0 if self.list else None
        if idx is not None:
            self.pick.SelectedIndex = idx
        else:
            self.say(u'Model này không có schedule.')

    def _picked(self, sender, e):
        if self.loading or self.pick.SelectedIndex < 0 or self.pick.SelectedIndex >= len(self.list):
            return
        s = self.list[self.pick.SelectedIndex]
        if self.data is not None and self.data.get('id') == s['id']:
            return
        if not self.leave_ok():
            self.loading = True
            self.pick.SelectedIndex = next((i for i, x in enumerate(self.list) if self.data and x['id'] == self.data.get('id')), -1)
            self.loading = False
            return
        self.load(s['id'])

    def reload(self):
        if self.data is not None and self.leave_ok():
            self.load(self.data['id'], keep=True)
        elif self.data is None:
            self.doc_title = None
            self.refresh_doc()

    def load(self, sid, keep=False):
        self.say(u'Đang đọc bảng…')
        self.host.run('task', 'schedule_table', {'schedule': u'{0}'.format(sid)}, lambda d: self._got_table(d, keep))

    def _got_table(self, d, keep):
        self.data, self.cols = d, d['columns']
        self.rows = []
        for r in d['rows']:
            r['o'] = list(r['v'])
            self.rows.append(r)
        self.undo_stack, self.redo_stack = [], []
        if not keep:
            self.filters, self.sort_col, self.sort_dir = {}, None, 0
        st = load_settings()
        st['schedule'] = d['schedule']
        save_settings(st)
        self._build()

    # -- table + columns -----------------------------------------------------------------------------
    def state(self, row, ci):
        c = self.cols[ci]
        if row['v'][ci] != row['o'][ci]:
            return u'chg'
        return u'' if self.can_edit(row, ci) else (u'lock' if self._lock(row, c) else u'ro')

    def _lock(self, row, c):
        return None if row.get('new') else (row.get('typeLock') if c['source'] == 'type' else row.get('lock'))

    def can_edit(self, row, ci):
        c = self.cols[ci]
        if not c['editable'] and not (row.get('new') and c.get('role')):
            return False
        if row.get('new') and c['source'] == 'type':
            return False
        lk = self._lock(row, c)
        return not (lk == 'busy' or (lk == 'others' and not self.unlock))

    def _build(self):
        self.busy = True
        dt = DataTable()
        dt.CaseSensitive = False
        dt.Columns.Add('i', clr.GetClrType(Int32))
        dt.Columns.Add('n', clr.GetClrType(Int32))
        dt.Columns.Add('d', clr.GetClrType(Int32))
        for ci in range(len(self.cols)):
            dt.Columns.Add('f{0}'.format(ci), clr.GetClrType(String))
            dt.Columns.Add('s{0}'.format(ci), clr.GetClrType(String))
        self.drows = []
        dt.BeginLoadData()
        for i, r in enumerate(self.rows):
            self.drows.append(self._add_row(dt, i, r))
        dt.EndLoadData()
        dt.ColumnChanged += self._cell_changed
        self.dt = dt
        self.dv = dt.DefaultView
        self.dv.Sort = 'n ASC, i ASC'
        self.busy = False
        self._columns()
        if self.sort_col is not None:
            self._apply_sort()
        self.grid.ItemsSource = self.dv
        self.apply_filter()
        self.f('b_add').Visibility = Visibility.Visible if self.data.get('sheetList') and any(c.get('role') == 'sheetNumber' for c in self.cols) else Visibility.Collapsed
        self.grid.Focus()

    def _add_row(self, dt, i, r):
        row = dt.NewRow()
        row['i'] = i
        row['n'] = 1 if r.get('new') else 0
        row['d'] = 0
        for ci in range(len(self.cols)):
            row['f{0}'.format(ci)] = r['v'][ci]
            row['s{0}'.format(ci)] = self.state(r, ci)
        dt.Rows.Add(row)
        return row

    def _cell_style(self, ci):
        s = Style(clr.GetClrType(DataGridCell))
        for value, back, fore in ((u'chg', '#FFE9C9', '#1C1C1C'), (u'ro', '#FAFAFA', '#7A7A7A'), (u'lock', '#F2F2F2', '#9A9A9A')):
            t = DataTrigger()
            t.Binding = Binding('s{0}'.format(ci))
            t.Value = value
            t.Setters.Add(Setter(DataGridCell.BackgroundProperty, brush(back)))
            t.Setters.Add(Setter(DataGridCell.ForegroundProperty, brush(fore)))
            s.Triggers.Add(t)
        sel = Trigger()
        sel.Property = DataGridCell.IsSelectedProperty
        sel.Value = True
        sel.Setters.Add(Setter(DataGridCell.BackgroundProperty, brush('#CCE4F7')))
        sel.Setters.Add(Setter(DataGridCell.ForegroundProperty, brush('#1C1C1C')))
        sel.Setters.Add(Setter(DataGridCell.BorderBrushProperty, brush('#CCE4F7')))
        s.Triggers.Add(sel)
        foc = Trigger()
        foc.Property = DataGridCell.IsFocusedProperty
        foc.Value = True
        foc.Setters.Add(Setter(DataGridCell.BorderBrushProperty, brush('#0078D4')))
        foc.Setters.Add(Setter(DataGridCell.BorderThicknessProperty, Thickness(1)))
        s.Triggers.Add(foc)
        return s

    def _header(self, ci):
        c = self.cols[ci]
        p = StackPanel()
        title = TextBlock()
        title.Text = c['heading'] + (u'  (loại)' if c['source'] == 'type' else u'')
        tips = [c['name'] if c['name'] != c['heading'] else u'',
                u'Tham số loại: sửa một ô là đổi cho mọi phần tử cùng loại' if c['source'] == 'type' else u'',
                u'' if c['editable'] else (u'Revit tính, chỉ đọc' if c['kind'] == 'computed' else u'Chỉ đọc'),
                u'Cột ẩn trong schedule' if c['hidden'] else u'']
        tip = u'\n'.join(t for t in tips if t)
        if tip:
            title.ToolTip = tip
        if not c['editable']:
            title.Foreground = brush('#5E5E5E')
        box = TextBox()
        box.Text = self.filters.get(ci, u'')
        box.Margin = Thickness(0, 2, 0, 0)
        box.ToolTip = u'Lọc: chứa · =khớp hết · !không chứa · = (trống) · ! (có giá trị)'

        def changed(sender, e):
            self.filters[ci] = sender.Text
            self.apply_filter()
        box.TextChanged += changed
        p.Children.Add(title)
        p.Children.Add(box)
        return p

    def _columns(self):
        self.shown = [i for i, c in enumerate(self.cols) if self.f('c_hidden').IsChecked or not c['hidden']]
        self.grid.Columns.Clear()
        self.columns_by_ci = {}
        for ci in self.shown:
            col = DataGridTextColumn()
            b = Binding('f{0}'.format(ci))
            b.Mode = BindingMode.TwoWay
            b.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            col.Binding = b
            col.SortMemberPath = 'f{0}'.format(ci)
            col.Header = self._header(ci)
            col.CellStyle = self._cell_style(ci)
            n = max([len(self.cols[ci]['heading'])] + [len(r['v'][ci]) for r in self.rows[:300]])
            col.Width = DataGridLength(min(360, max(70, n * 6.6 + 18)))
            self.grid.Columns.Add(col)
            self.columns_by_ci[ci] = col
        self._show_sort()

    def _hidden_changed(self):
        st = load_settings()
        st['hidden'] = bool(self.f('c_hidden').IsChecked)
        save_settings(st)
        if self.data is not None:
            self._columns()
            self.apply_filter()

    def _unlock_changed(self):
        box = self.f('c_unlock')
        if box.IsChecked and MessageBox.Show(u'Cho sửa cả phần tử do người khác tạo và sửa lần cuối?\nPhần tử đang bị người khác mượn vẫn bị khoá.',
                                             TITLE, MessageBoxButton.YesNo) != MessageBoxResult.Yes:
            box.IsChecked = False
        self.unlock = bool(box.IsChecked)
        self._refresh_states()

    def _refresh_states(self):
        if self.dt is None:
            return
        self.busy = True
        for i, r in enumerate(self.rows):
            for ci in range(len(self.cols)):
                self.drows[i]['s{0}'.format(ci)] = self.state(r, ci)
        self.busy = False

    # -- which rows show --------------------------------------------------------------------------------
    def apply_filter(self):
        if self.dv is None:
            return
        parts = []

        def lit(s):
            return s.replace(u"'", u"''")

        def like(s):
            return u''.join(u'[' + ch + u']' if ch in u'[]%*' else ch for ch in lit(s))
        q = (self.f('q').Text or u'').strip()
        if q:
            parts.append(u'(' + u' OR '.join(u"f{0} LIKE '%{1}%'".format(ci, like(q)) for ci in self.shown) + u')')
        for ci, f in self.filters.items():
            if not f or ci not in self.shown:
                continue
            col = u'ISNULL(f{0},\'\')'.format(ci)
            if f.startswith(u'='):
                parts.append(u"{0} = '{1}'".format(col, lit(f[1:])))
            elif f.startswith(u'!'):
                parts.append(u"{0} NOT LIKE '%{1}%'".format(col, like(f[1:])) if len(f) > 1 else u"{0} <> ''".format(col))
            else:
                parts.append(u"{0} LIKE '%{1}%'".format(col, like(f)))
        if self.f('c_only').IsChecked:
            parts.append(u'd = 1')
        expr = u' AND '.join(parts)
        try:
            self.dv.RowFilter = u'n = 1 OR ({0})'.format(expr) if expr else u''
        except Exception as ex:
            self.say(u'Không lọc được: {0}'.format(ex))
        self._status()

    def _sorting(self, sender, e):
        e.Handled = True
        ci = int(e.Column.SortMemberPath[1:])
        if self.sort_col != ci:
            self.sort_col, self.sort_dir = ci, 1
        elif self.sort_dir > 0:
            self.sort_dir = -1
        else:
            self.sort_col, self.sort_dir = None, 0
        self._apply_sort()

    def _apply_sort(self):
        if self.sort_col is None:
            self.dv.Sort = 'n ASC, i ASC'
        else:
            key = 'k{0}'.format(self.sort_col)
            self.busy = True
            if not self.dt.Columns.Contains(key):
                self.dt.Columns.Add(key, clr.GetClrType(String))
            for i, r in enumerate(self.rows):
                self.drows[i][key] = ops.natural_key(r['v'][self.sort_col])
            self.busy = False
            self.dv.Sort = 'n ASC, {0} {1}, i ASC'.format(key, 'ASC' if self.sort_dir > 0 else 'DESC')
        self._show_sort()

    def _show_sort(self):
        from System.ComponentModel import ListSortDirection
        for ci, col in self.columns_by_ci.items():
            col.SortDirection = (ListSortDirection.Ascending if self.sort_dir > 0 else ListSortDirection.Descending) if ci == self.sort_col else None

    def _row_loading(self, sender, e):
        e.Row.Header = u'{0}'.format(e.Row.GetIndex() + 1)

    # -- edits + undo -------------------------------------------------------------------------------------
    def _put(self, i, ci, value):
        """The value in the model of the panel and in its table."""
        r = self.rows[i]
        r['v'][ci] = value
        self.busy = True
        d = self.drows[i]
        d['f{0}'.format(ci)] = value
        d['s{0}'.format(ci)] = self.state(r, ci)
        d['d'] = 1 if r.get('new') or any(a != b for a, b in zip(r['v'], r['o'])) else 0
        key = 'k{0}'.format(ci)
        if self.dt.Columns.Contains(key):
            d[key] = ops.natural_key(value)
        self.busy = False

    def set_cell(self, i, ci, value, batch, skipped):
        r, c = self.rows[i], self.cols[ci]
        if not self.can_edit(r, ci):
            skipped[0] += 1
            return
        v = ops.normalize(c['kind'], value)
        if v is None:
            skipped[1] += 1
            return
        if c['source'] == 'type' and r.get('type'):
            targets = [k for k, x in enumerate(self.rows) if x.get('type') == r['type']]
        else:
            targets = [i]
        for k in targets:
            if self.rows[k]['v'][ci] != v:
                batch.append((k, ci, self.rows[k]['v'][ci], v))
                self._put(k, ci, v)

    def commit(self, batch, skipped=None):
        if batch:
            self.undo_stack.append(batch)
            self.undo_stack = self.undo_stack[-200:]
            self.redo_stack = []
        self._status()
        if skipped and (skipped[0] or skipped[1]):
            note = [u'bỏ qua {0} ô chỉ đọc / bị khoá'.format(skipped[0]) if skipped[0] else u'',
                    u'{0} giá trị sai kiểu'.format(skipped[1]) if skipped[1] else u'']
            self.say(u' · '.join(n for n in note if n))

    def undo(self):
        if not self.undo_stack:
            return
        batch = self.undo_stack.pop()
        for k, ci, before, after in reversed(batch):
            self._put(k, ci, before)
        self.redo_stack.append(batch)
        self._status()

    def redo(self):
        if not self.redo_stack:
            return
        batch = self.redo_stack.pop()
        for k, ci, before, after in batch:
            self._put(k, ci, after)
        self.undo_stack.append(batch)
        self._status()

    def _begin_edit(self, sender, e):
        ci, i = self._cell_of(e.Column, e.Row.Item)
        if ci is None or not self.can_edit(self.rows[i], ci):
            e.Cancel = True
            if ci is not None:
                self._explain(i, ci)
            return
        self.editing = True

    def _end_edit(self, sender, e):
        self.editing = False
        if e.EditAction == DataGridEditAction.Commit:
            # finish the row too, or a later Esc would undo the cell in the table but not in self.rows
            self.grid.Dispatcher.BeginInvoke(Action(lambda: self.grid.CommitEdit(DataGridEditingUnit.Row, False)))

    def _cell_changed(self, sender, e):
        """A cell edited in the grid."""
        name = e.Column.ColumnName
        if self.busy or not name.startswith('f'):
            return
        i, ci = int(e.Row['i']), int(name[1:])
        typed = e.Row[name]
        typed = u'' if typed is None or isinstance(typed, DBNull) else typed
        old = self.rows[i]['v'][ci]
        batch, sk = [], [0, 0]
        self.set_cell(i, ci, typed, batch, sk)
        if sk[0] or sk[1]:
            self._put(i, ci, old)
        self.commit(batch, sk)

    def _cell_of(self, column, item):
        path = getattr(column, 'SortMemberPath', None)
        if not path or item is None:
            return None, None
        return int(path[1:]), int(item['i'])

    def _explain(self, i, ci):
        r, c = self.rows[i], self.cols[ci]
        lk = self._lock(r, c)
        by = (r.get('typeBy') if c['source'] == 'type' else r.get('by')) or u'người khác'
        if lk == 'busy':
            self.say(u'Đang bị {0} mượn'.format(by))
        elif lk == 'others':
            self.say(u'Phần tử của {0}: bật "Sửa phần tử người khác"'.format(by))
        else:
            self.say(u'Cột chỉ đọc')

    # -- selection as a block (view row, display column) ---------------------------------------------------
    def _view_rows(self):
        return [int(self.dv[p]['i']) for p in range(self.dv.Count)]

    def _display_cols(self):
        cols = sorted(self.grid.Columns, key=lambda c: c.DisplayIndex)
        return [int(c.SortMemberPath[1:]) for c in cols]

    def block(self):
        """(rows of the model in view order, column indexes in display order), the bounding box of the selection."""
        rows, cols = self._view_rows(), self._display_cols()
        where = dict((i, p) for p, i in enumerate(rows))
        ps, cs = [], []
        for info in self.grid.SelectedCells:
            ci, i = self._cell_of(info.Column, info.Item)
            if ci is not None and i in where:
                ps.append(where[i])
                cs.append(cols.index(ci))
        if not ps:
            cur = self.grid.CurrentCell
            ci, i = self._cell_of(cur.Column, cur.Item) if cur.Column is not None else (None, None)
            if ci is None or i not in where:
                return [], []
            ps, cs = [where[i]], [cols.index(ci)]
        return rows[min(ps):max(ps) + 1], cols[min(cs):max(cs) + 1]

    def _key(self, sender, e):
        if self.data is None or self.editing or isinstance(Keyboard.FocusedElement, TextBox):
            return                  # typing in a cell or a filter box
        ctrl = bool(Keyboard.Modifiers & ModifierKeys.Control)
        acts = {Key.V: self.paste, Key.D: self.fill_down, Key.R: self.fill_right, Key.Z: self.undo, Key.Y: self.redo,
                Key.H: self.find_replace, Key.S: self.save, Key.F: lambda: self.f('q').Focus()} if ctrl else {Key.Delete: self.clear_cells}
        fn = acts.get(e.Key)
        if fn is not None:
            e.Handled = True
            try:
                fn()
            except Exception as ex:
                self.say(u'Lỗi: {0}'.format(ex))

    def clear_cells(self):
        rows, cols = self.block()
        batch, sk = [], [0, 0]
        for i in rows:
            for ci in cols:
                if self.rows[i]['v'][ci] != u'':
                    self.set_cell(i, ci, u'', batch, sk)
        self.commit(batch, sk)

    def paste(self, text=None):
        if text is None:
            from System.Windows import Clipboard
            text = Clipboard.GetText()
        if not text:
            return
        m = ops.parse_delimited(re.sub(r'\r?\n$', u'', text), u'\t')
        rows_all, cols_all = self._view_rows(), self._display_cols()
        rows, cols = self.block()
        if not m or not rows:
            return
        h, w = len(m), max(len(r) for r in m)
        tile = (h == 1 and w == 1) or (len(rows) % h == 0 and len(cols) % w == 0 and (len(rows) > h or len(cols) > w))
        p0, c0 = rows_all.index(rows[0]), cols_all.index(cols[0])
        hh, ww = (len(rows), len(cols)) if tile else (h, w)
        batch, sk, outside = [], [0, 0], 0
        for a in range(hh):
            for b in range(ww):
                if p0 + a >= len(rows_all) or c0 + b >= len(cols_all):
                    outside += 1
                    continue
                line = m[a % h]
                if b % w < len(line):
                    self.set_cell(rows_all[p0 + a], cols_all[c0 + b], line[b % w], batch, sk)
        self.commit(batch, sk)
        if outside:
            self.say(u'{0} ô dán ra ngoài bảng bị bỏ'.format(outside))

    def fill_down(self):
        rows, cols = self.block()
        if not rows:
            return
        if len(rows) == 1:
            all_rows = self._view_rows()
            p = all_rows.index(rows[0])
            if p == 0:
                return
            rows = [all_rows[p - 1], rows[0]]
        batch, sk = [], [0, 0]
        for ci in cols:
            for i in rows[1:]:
                self.set_cell(i, ci, self.rows[rows[0]]['v'][ci], batch, sk)
        self.commit(batch, sk)

    def fill_right(self):
        rows, cols = self.block()
        if not rows:
            return
        if len(cols) == 1:
            all_cols = self._display_cols()
            c = all_cols.index(cols[0])
            if c == 0:
                return
            cols = [all_cols[c - 1], cols[0]]
        batch, sk = [], [0, 0]
        for i in rows:
            for ci in cols[1:]:
                self.set_cell(i, ci, self.rows[i]['v'][cols[0]], batch, sk)
        self.commit(batch, sk)

    def series(self):
        """The first cell (or the first two when both end in a number) of each selected column seeds the rest."""
        rows, cols = self.block()
        if len(rows) < 2:
            return self.say(u'Chọn từ ô đầu xuống tới ô cuối cần đánh số')
        batch, sk = [], [0, 0]
        tail = r'\d+(\D*)$'
        for ci in cols:
            v0, v1 = self.rows[rows[0]]['v'][ci], self.rows[rows[1]]['v'][ci]
            two = len(rows) > 2 and v0 != v1 and re.search(tail, v0) and re.search(tail, v1) and re.sub(tail, r'\1', v0) == re.sub(tail, r'\1', v1)
            seeds = [v0, v1] if two else [v0]
            for k, v in enumerate(ops.series_values(seeds, len(rows) - len(seeds))):
                self.set_cell(rows[len(seeds) + k], ci, v, batch, sk)
        self.commit(batch, sk)

    # -- the square at the corner of the selection: drag to fill ------------------------------------------------
    def _selection_changed(self, sender, e):
        rows, cols = self.block() if self.dv is not None else ([], [])
        if rows:
            all_rows, all_cols = self._view_rows(), self._display_cols()
            p, c = all_rows.index(rows[0]), all_cols.index(cols[0])
            self.box = (p, p + len(rows) - 1, c, c + len(cols) - 1)
        else:
            self.box = None
        self._place_handle()
        self._status()

    def _column_at(self, c):
        return sorted(self.grid.Columns, key=lambda x: x.DisplayIndex)[c]

    def _cell_at(self, p, c):
        """The cell on screen at view row p, display column c; None when it is scrolled away."""
        row = self.grid.ItemContainerGenerator.ContainerFromIndex(p)
        if row is None:
            return None
        content = self._column_at(c).GetCellContent(row)
        return content.Parent if content is not None else None

    def _head(self):
        """Height of the column headers, width of the row headers: the handle hides under them."""
        h = find_child(self.grid, DataGridColumnHeadersPresenter)
        return (h.ActualHeight if h is not None else 0), self.grid.RowHeaderActualWidth

    def _hide_handle(self):
        if self.handle.Visibility != Visibility.Collapsed:
            self.handle.Visibility = Visibility.Collapsed

    def _place_handle(self):
        if self.drag is not None:
            return
        try:
            if self.box is None or self.editing or self.dv is None or not self.grid.IsVisible:
                return self._hide_handle()
            cell = self._cell_at(self.box[1], self.box[3])
            if cell is None:
                return self._hide_handle()
            pt = cell.TranslatePoint(Point(cell.ActualWidth, cell.ActualHeight), self.over)
            top, left = self._head()
            if pt.Y <= top or pt.X <= left or pt.Y > self.over.ActualHeight or pt.X > self.over.ActualWidth:
                return self._hide_handle()
            x, y = pt.X - 5, pt.Y - 5
            if Canvas.GetLeft(self.handle) != x or Canvas.GetTop(self.handle) != y:
                Canvas.SetLeft(self.handle, x)
                Canvas.SetTop(self.handle, y)
            if self.handle.Visibility != Visibility.Visible:
                self.handle.Visibility = Visibility.Visible
        except Exception:
            self._hide_handle()

    def _handle_down(self, sender, e):
        if self.box is None:
            return
        e.Handled = True
        self.begin_fill()
        self.handle.CaptureMouse()
        self.timer.Start()

    def begin_fill(self):
        self.drag = {'p': self.box[1], 'c': self.box[3]}

    def _handle_move(self, sender, e):
        if self.drag is not None:
            self.drag_to(e.GetPosition(self.grid))

    def _handle_up(self, sender, e):
        if self.drag is None:
            return
        e.Handled = True
        self.timer.Stop()
        self.handle.ReleaseMouseCapture()
        self.drag_to(e.GetPosition(self.grid))
        self.finish_fill(bool(Keyboard.Modifiers & ModifierKeys.Control))

    def _drag_tick(self, sender, e):
        """Scroll while the mouse is held at or past an edge of the grid."""
        if self.drag is None:
            return self.timer.Stop()
        from System.Windows.Input import Mouse
        p = Mouse.GetPosition(self.grid)
        top, left = self._head()
        sv = find_child(self.grid, ScrollViewer)
        if sv is None:
            return
        moved = True
        if p.Y >= self.grid.ActualHeight - 6:
            sv.LineDown()
        elif p.Y <= top + 6:
            sv.LineUp()
        elif p.X >= self.grid.ActualWidth - 6:
            sv.LineRight()
        elif p.X <= left + 6:
            sv.LineLeft()
        else:
            moved = False
        if moved:
            self.drag_to(p)

    def drag_to(self, point):
        """The view row / display column under a grid point, kept inside the grid; the dashed box follows."""
        top, left = self._head()
        x = max(left + 2, min(point.X, self.grid.ActualWidth - 20))
        y = max(top + 2, min(point.Y, self.grid.ActualHeight - 20))
        hit = self.grid.InputHitTest(Point(x, y))
        cell = find_parent(hit, DataGridCell) if hit is not None else None
        if cell is None:
            return
        row = find_parent(cell, DataGridRow)
        self.drag['p'] = self.grid.ItemContainerGenerator.IndexFromContainer(row)
        self.drag['c'] = cell.Column.DisplayIndex
        self._draw_fill()

    def fill_range(self):
        """('rows' or 'cols', new end) of the drag, or None when it stays inside the selection. The direction
        with the longer way out wins, measured in row and column sizes."""
        p0, p1, c0, c1 = self.box
        p, c = self.drag['p'], self.drag['c']
        dr = p - p1 if p > p1 else p0 - p if p < p0 else 0
        dc = c - c1 if c > c1 else c0 - c if c < c0 else 0
        if not dr and not dc:
            return None
        return ('rows', p) if dr * 22 >= dc * 90 else ('cols', c)

    def _draw_fill(self):
        fr = self.fill_range()
        if fr is None:
            self.fillbox.Visibility = Visibility.Collapsed
            return
        p0, p1, c0, c1 = self.box
        if fr[0] == 'rows':
            p0, p1 = min(p0, fr[1]), max(p1, fr[1])
        else:
            c0, c1 = min(c0, fr[1]), max(c1, fr[1])
        a, b = self._cell_at(p0, c0), self._cell_at(p1, c1)
        top, left = self._head()
        pa = a.TranslatePoint(Point(0, 0), self.over) if a is not None else Point(left, top)
        pb = b.TranslatePoint(Point(b.ActualWidth, b.ActualHeight), self.over) if b is not None else Point(self.over.ActualWidth, self.over.ActualHeight)
        Canvas.SetLeft(self.fillbox, pa.X)
        Canvas.SetTop(self.fillbox, pa.Y)
        self.fillbox.Width = max(0, pb.X - pa.X)
        self.fillbox.Height = max(0, pb.Y - pa.Y)
        self.fillbox.Visibility = Visibility.Visible

    def finish_fill(self, copy=False):
        """The selection repeated to the new end: numbers count on (A-009, A-010), Ctrl copies as it is."""
        fr = self.fill_range()
        p0, p1, c0, c1 = self.box
        self.drag = None
        self.fillbox.Visibility = Visibility.Collapsed
        if fr is None:
            return self._place_handle()
        rows, cols = self._view_rows(), self._display_cols()
        batch, sk = [], [0, 0]

        def values(seeds, n):
            return [seeds[k % len(seeds)] for k in range(n)] if copy else ops.series_values(seeds, n)
        if fr[0] == 'rows':
            end = fr[1]
            for c in range(c0, c1 + 1):
                ci = cols[c]
                seeds = [self.rows[rows[p]]['v'][ci] for p in range(p0, p1 + 1)]
                if end > p1:
                    for k, v in enumerate(values(seeds, end - p1)):
                        self.set_cell(rows[p1 + 1 + k], ci, v, batch, sk)
                else:
                    for k, v in enumerate(values(seeds[::-1], p0 - end)):
                        self.set_cell(rows[p0 - 1 - k], ci, v, batch, sk)
            p0, p1 = min(p0, end), max(p1, end)
        else:
            end = fr[1]
            for p in range(p0, p1 + 1):
                i = rows[p]
                seeds = [self.rows[i]['v'][cols[c]] for c in range(c0, c1 + 1)]
                if end > c1:
                    for k, v in enumerate(values(seeds, end - c1)):
                        self.set_cell(i, cols[c1 + 1 + k], v, batch, sk)
                else:
                    for k, v in enumerate(values(seeds[::-1], c0 - end)):
                        self.set_cell(i, cols[c0 - 1 - k], v, batch, sk)
            c0, c1 = min(c0, end), max(c1, end)
        self.commit(batch, sk)
        self._select(p0, p1, c0, c1)

    def _select(self, p0, p1, c0, c1):
        self.grid.SelectedCells.Clear()
        columns = sorted(self.grid.Columns, key=lambda x: x.DisplayIndex)
        for p in range(p0, p1 + 1):
            item = self.dv[p]
            for c in range(c0, c1 + 1):
                self.grid.SelectedCells.Add(DataGridCellInfo(item, columns[c]))

    # -- dialogs --------------------------------------------------------------------------------------------
    def find_replace(self):
        if self.data is None:
            return
        rows, cols = self.block()
        find, repl = TextBox(), TextBox()
        find.Margin = repl.Margin = Thickness(0, 0, 0, 5)
        in_sel = _check(u'Chỉ vùng chọn', len(rows) * len(cols) > 1)
        whole, case, rx = _check(u'Khớp cả ô'), _check(u'Hoa / thường'), _check(u'Regex ($1 → \\1)')
        info = _text(u'', dim=True)

        def pattern():
            src = find.Text if rx.IsChecked else re.escape(find.Text)
            return re.compile(u'^(?:{0})$'.format(src) if whole.IsChecked else src, re.UNICODE | (0 if case.IsChecked else re.IGNORECASE))

        def cells():
            rr, cc = (rows, cols) if in_sel.IsChecked else (self._view_rows(), self._display_cols())
            return [(i, ci) for i in rr for ci in cc]

        def next_hit():
            try:
                pat = pattern()
            except Exception as ex:
                info.Text = u'Regex sai: {0}'.format(ex)
                return
            for i, ci in cells():
                if find.Text and pat.search(self.rows[i]['v'][ci]):
                    self._go_to(i, ci)
                    info.Text = u'{0} · {1}'.format(self.rows[i]['v'][ci][:40], self.cols[ci]['heading'])
                    return
            info.Text = u'Không thấy'

        def replace_all():
            try:
                pat = pattern()
            except Exception as ex:
                info.Text = u'Regex sai: {0}'.format(ex)
                return
            if not find.Text:
                return
            batch, sk = [], [0, 0]
            for i, ci in cells():
                old = self.rows[i]['v'][ci]
                if pat.search(old):
                    self.set_cell(i, ci, pat.sub(repl.Text if rx.IsChecked else repl.Text.replace(u'\\', u'\\\\'), old), batch, sk)
            self.commit(batch, sk)
            info.Text = u'Đã thay {0} ô'.format(len(batch)) + (u' · bỏ qua {0} ô chỉ đọc / bị khoá'.format(sk[0]) if sk[0] else u'')

        opts = WrapPanel()
        for c in (in_sel, whole, case, rx):
            opts.Children.Add(c)
        w = _dialog(self.host, u'Tìm & thay', [_text(u'Tìm', dim=True), find, _text(u'Thay bằng', dim=True), repl, opts, info,
                                               _row(_button(u'Tìm tiếp', next_hit), _button(u'Thay hết', replace_all), _button(u'Đóng', lambda: w.Close()))])
        find.Focus()
        w.ShowDialog()

    def _go_to(self, i, ci):
        rows = self._view_rows()
        if i not in rows or ci not in self.columns_by_ci:
            return
        item = self.dv[rows.index(i)]
        col = self.columns_by_ci[ci]
        self.grid.SelectedCells.Clear()
        info = DataGridCellInfo(item, col)
        self.grid.CurrentCell = info
        self.grid.SelectedCells.Add(info)
        self.grid.ScrollIntoView(item, col)

    def formula(self):
        """A template per selected cell: {v} value, {V} upper case, {n} / {n:000} position, {Heading} another column."""
        if self.data is None:
            return
        rows, cols = self.block()
        if not rows:
            return
        box = TextBox()
        box.Text = load_settings().get('formula', u'')
        heads = {}
        for k, c in enumerate(self.cols):
            heads[c['heading'].lower()] = k
            heads[c['name'].lower()] = k

        def apply_to(t, i, ci, n):
            def sub(m):
                k = m.group(1)
                r = self.rows[i]['v']
                if k == u'v':
                    return r[ci]
                if k == u'V':
                    return r[ci].upper()
                if k == u'n':
                    return u'{0}'.format(n)
                if re.match(r'^n:0+$', k):
                    return u'{0}'.format(n).zfill(len(k) - 2)
                j = heads.get(k.lower())
                return m.group(0) if j is None else r[j]
            return re.sub(r'\{([^{}]+)\}', sub, t)

        pv = _text(u'', dim=True)
        box.TextChanged += lambda s, e: setattr(pv, 'Text', u'Ô đầu: "{0}" → "{1}"'.format(self.rows[rows[0]]['v'][cols[0]], apply_to(box.Text, rows[0], cols[0], 1)))

        def go():
            st = load_settings()
            st['formula'] = box.Text
            save_settings(st)
            batch, sk = [], [0, 0]
            for ci in cols:
                for n, i in enumerate(rows):
                    self.set_cell(i, ci, apply_to(box.Text, i, ci, n + 1), batch, sk)
            self.commit(batch, sk)
            w.Close()
        w = _dialog(self.host, u'Công thức cho vùng chọn',
                    [_text(u'{v} giá trị đang có · {V} chữ hoa · {n} hoặc {n:000} số thứ tự · {Tên cột} giá trị cột khác cùng dòng', dim=True), box, pv,
                     _row(_button(u'Áp dụng', go), _button(u'Huỷ', lambda: w.Close()))])
        box.Focus()
        w.ShowDialog()

    def to_filter(self):
        """The chosen cells into a filter of a view template (task schedule_filter_add)."""
        if self.data is None:
            return
        rows, cols = self.block()
        rows = [i for i in rows if not self.rows[i].get('new')]
        if not rows:
            return self.say(u'Chọn các ô cần đưa vào filter')
        bad = [self.cols[ci]['heading'] for ci in cols if self.cols[ci]['param'] is None]
        if bad:
            return self.say(u'Cột {0}: Revit tính ra, không lọc được'.format(u', '.join(bad)))
        self.say(u'Đang đọc view template…')
        self.host.run('task', 'schedule_filter_options', {}, lambda o: self._filter_dialog(rows, cols, o))

    def _filter_dialog(self, rows, cols, opts):
        self._status()
        temps, filters = opts['templates'], opts['filters']
        tp = ComboBox()
        tp.IsEditable = True
        tp.ItemsSource = [t['name'] + (u'  (của {0})'.format(t['lastChangedBy']) if t['others'] else u'') for t in temps]
        last = load_settings().get('filterTemplate')
        tp.SelectedIndex = next((k for k, t in enumerate(temps) if t['name'] == last), 0 if temps else -1)
        fl = ComboBox()
        fl.IsEditable = True
        fl.ItemsSource = [f['name'] for f in filters]
        first = cols[0]
        vals = sorted(set(self.rows[i]['v'][first] for i in rows))
        fl.Text = (self.data['schedule'] + u' - ' + vals[0]) if len(vals) == 1 else self.data['schedule']
        hide, show = RadioButton(), RadioButton()
        hide.Content, show.Content = u'Hide', u'No hide'
        hide.IsChecked = bool(load_settings().get('filterHide', True))
        show.IsChecked = not hide.IsChecked
        hide.Margin = Thickness(0, 0, 14, 0)
        modes = WrapPanel()
        modes.Margin = Thickness(0, 6, 0, 0)
        modes.Children.Add(hide)
        modes.Children.Add(show)
        heads = u', '.join(self.cols[ci]['heading'] for ci in cols)
        shown = u', '.join(v or u'(trống)' for v in vals[:8]) + (u' …' if len(vals) > 8 else u'')
        info = _text(u'', dim=True)

        def apply_filter():
            if tp.SelectedIndex < 0 or not (fl.Text or u'').strip():
                info.Text = u'Chọn view template và đặt tên filter'
                return
            t = temps[tp.SelectedIndex]
            args = {'schedule': u'{0}'.format(self.data['id']), 'ids': sorted(set(self.rows[i]['id'] for i in rows)),
                    'cols': [self.cols[ci]['key'] for ci in cols], 'template': t['id'], 'filter': fl.Text.strip(),
                    'hide': bool(hide.IsChecked)}
            st = load_settings()
            st['filterTemplate'], st['filterHide'] = t['name'], bool(hide.IsChecked)
            save_settings(st)
            w.Close()
            self._send_filter(args)
        w = _dialog(self.host, u'Thêm vào filter của view template',
                    [_text(u'{0} dòng · cột {1}: {2}'.format(len(rows), heads, shown)),
                     _text(u'View template', dim=True), tp, _text(u'Filter (có sẵn thì mở rộng thêm các giá trị này, chưa có thì tạo mới)', dim=True), fl, modes, info,
                     _row(_button(u'Thêm', apply_filter), _button(u'Huỷ', lambda: w.Close()))], 520)
        w.ShowDialog()

    def _send_filter(self, args):
        def done(r):
            if r.get('blocked'):
                msg = u'Của người khác:\n{0}\n\nVẫn sửa?'.format(u'\n'.join(r['blocked']))
                if MessageBox.Show(msg, TITLE, MessageBoxButton.YesNo) == MessageBoxResult.Yes:
                    self._send_filter(dict(args, force=True))
                return
            self.say(u'{0} filter {1} · view template {2} · {3} · {4} giá trị'.format(
                u'Đã tạo' if r.get('newFilter') else u'Đã mở rộng', r['filter'], r['template'],
                u'Hide' if r['hide'] else u'No hide', sum(r['values'].values())))
        self.say(u'Đang ghi filter…')
        self.host.run('task', 'schedule_filter_add', args, done, lambda err: MessageBox.Show(u'Chưa thêm được: {0}'.format(err), TITLE))

    def add_rows(self):
        if self.data is None or not self.data.get('sheetList'):
            return
        box = TextBox()
        box.Text = u'1'
        result = []

        def ok():
            result.append(box.Text)
            w.Close()
        w = _dialog(self.host, u'Thêm sheet', [_text(u'Thêm bao nhiêu sheet mới? (điền số sheet, tên rồi Ghi vào Revit)'), box,
                                               _row(_button(u'Thêm', ok), _button(u'Huỷ', lambda: w.Close()))], 340)
        w.ShowDialog()
        try:
            n = min(500, int(result[0]))
        except Exception:
            return
        for _ in range(n):
            self._new_row([u''] * len(self.cols))
        self.apply_filter()

    def _new_row(self, values):
        r = {'id': u'new', 'new': True, 'v': list(values), 'o': [u''] * len(self.cols)}
        self.rows.append(r)
        self.busy = True
        self.drows.append(self._add_row(self.dt, len(self.rows) - 1, r))
        self.drows[-1]['d'] = 1
        self.busy = False

    # -- write --------------------------------------------------------------------------------------------
    def pending(self):
        changes, add, lines, seen = [], [], [], set()
        sn = next((k for k, c in enumerate(self.cols) if c.get('role') == 'sheetNumber'), None)
        nm = next((k for k, c in enumerate(self.cols) if c.get('role') == 'sheetName'), None)
        first = self.shown[0] if self.shown else 0
        for r in self.rows:
            if r.get('new'):
                if not any(r['v']):
                    continue
                vals = dict((self.cols[k]['key'], v) for k, v in enumerate(r['v']) if v and k not in (sn, nm) and self.cols[k]['editable'])
                add.append({'number': r['v'][sn] if sn is not None else u'', 'name': r['v'][nm] if nm is not None else u'', 'values': vals})
                lines.append(u'+ sheet {0} {1}'.format(add[-1]['number'] or u'(chưa có số)', add[-1]['name']))
                continue
            for ci, v in enumerate(r['v']):
                if v == r['o'][ci]:
                    continue
                c = self.cols[ci]
                key = ('t', r.get('type'), ci) if c['source'] == 'type' and r.get('type') else (r['id'], ci)
                if key in seen:
                    continue
                seen.add(key)
                changes.append({'id': r['id'], 'col': c['key'], 'value': v})
                label = r['v'][sn] if sn is not None else r['o'][first]
                lines.append(u'{0} · {1}{2}: {3} → {4}'.format(label, c['heading'], u' (loại)' if c['source'] == 'type' else u'',
                                                                r['o'][ci] or u'(trống)', v or u'(trống)'))
        return changes, add, lines

    def save(self):
        if self.data is None:
            return
        if self.editing:
            self.grid.CommitEdit()
        changes, add, lines = self.pending()
        if not changes and not add:
            return self.say(u'Chưa có gì để ghi')
        if any(not a['number'] for a in add):
            return self.say(u'Sheet mới cần số sheet')
        types = len([c for c in changes if next(x for x in self.cols if x['key'] == c['col'])['source'] == 'type'])
        doc = self.host.uiapp.ActiveUIDocument
        title = doc.Document.Title if doc is not None else u'?'
        head = u'Model {0} · schedule {1}: {2} ô'.format(title, self.data['schedule'], len(changes))
        head += u', {0} sheet mới'.format(len(add)) if add else u''
        head += u' · {0} ô là tham số loại (đổi cho mọi phần tử cùng loại)'.format(types) if types else u''
        head += u' · print set cùng tên sẽ đồng bộ theo danh mục' if self.data.get('sheetList') else u''
        lst = ListBox()
        lst.Height = 300
        lst.ItemsSource = lines[:500]
        args = {'schedule': u'{0}'.format(self.data['id']), 'changes': changes, 'add': add, 'force': self.unlock, 'detail': True}

        def go():
            w.Close()
            self.say(u'Đang ghi… (model cloud: lần đầu sửa một phần tử Revit phải mượn nó, có thể mất vài phút)')
            self.host.run('task', 'schedule_table_write', args, self._written, self._write_failed)
        w = _dialog(self.host, u'Ghi vào Revit?', [_text(head), lst, _row(_button(u'Ghi', go), _button(u'Huỷ', lambda: w.Close()))], 620)
        w.ShowDialog()

    def _written(self, r):
        bad = (r.get('errors') or []) + (r.get('locked') or []) + (r.get('others') or [])
        made = u' · tạo {0} sheet'.format(len(r['add']['created'])) if r.get('add') else u''
        ps = u' · print set: {0}'.format(r['printSet']['status']) if r.get('printSet') else u''
        msg = u'Đã ghi {0} ô{1}{2}'.format(r.get('written', 0), made, ps)
        if bad:
            names = dict((c['key'], c['heading']) for c in self.cols)
            msg += u'\n\nKhông ghi được:\n' + u'\n'.join(u'Id {0} · {1}: {2}'.format(x.get('id'), names.get(x.get('col'), x.get('col')), x.get('error') or (u'khoá, của {0}'.format(x['by']) if x.get('by') else u'khoá')) for x in bad[:30] if isinstance(x, dict))
            MessageBox.Show(msg, TITLE)
        self.load(self.data['id'], keep=True)
        self.after_load = msg

    def _write_failed(self, err):
        MessageBox.Show(u'Chưa ghi được: {0}'.format(err), TITLE)
        self._status()

    # -- files ------------------------------------------------------------------------------------------------
    def export(self, kind):
        if self.data is None:
            return
        dlg = SaveFileDialog()
        dlg.FileName = re.sub(r'[\\/:*?"<>|]+', u'_', self.data['schedule']) + u'.' + kind
        dlg.Filter = u'Excel (*.xlsx)|*.xlsx' if kind == 'xlsx' else u'CSV (*.csv)|*.csv'
        if not dlg.ShowDialog():
            return
        rows = [[u'Id'] + [self.cols[ci]['heading'] for ci in self.shown]]
        for i in self._view_rows():
            r = self.rows[i]
            if not r.get('new'):
                rows.append([u'{0}'.format(r['id'])] + [r['v'][ci] for ci in self.shown])
        if kind == 'csv':
            ops.write_csv(dlg.FileName, rows)
        else:
            ops.write_xlsx(dlg.FileName, self.data['schedule'], rows, [False] + [self.cols[ci]['kind'] in ('int', 'number') for ci in self.shown])
        self.say(u'Đã xuất {0} dòng: {1}'.format(len(rows) - 1, dlg.FileName))

    def import_file(self):
        if self.data is None:
            return
        dlg = OpenFileDialog()
        dlg.Filter = u'Excel / CSV (*.xlsx;*.csv;*.tsv;*.txt)|*.xlsx;*.csv;*.tsv;*.txt'
        if not dlg.ShowDialog():
            return
        path = dlg.FileName
        m = ops.read_xlsx(path) if path.lower().endswith('.xlsx') else ops.read_csv(path)
        self.say(self.import_rows(m, os.path.basename(path)))

    def import_rows(self, m, name):
        """Match rows by Id, else the sheet number, else the first file column unique in the table."""
        if not m or len(m) < 2:
            return u'File không có dữ liệu'
        head = [(u'{0}'.format(x or u'')).strip().lower() for x in m[0]]

        def col_of(h):
            k = next((i for i, c in enumerate(self.cols) if c['heading'].lower() == h), None)
            return k if k is not None else next((i for i, c in enumerate(self.cols) if c['name'].lower() == h), None)
        mapped = [col_of(h) for h in head]
        id_col = head.index(u'id') if u'id' in head else None
        olds = [r for r in self.rows if not r.get('new')]
        key_col, key_file = None, None
        if id_col is None:
            sn = next((k for k, c in enumerate(self.cols) if c.get('role') == 'sheetNumber'), None)
            for ci in [sn] + [x for x in mapped if x is not None]:
                if ci is None or ci not in mapped:
                    continue
                vals = [r['o'][ci] for r in olds]
                if all(vals) and len(set(vals)) == len(vals):
                    key_col, key_file = ci, mapped.index(ci)
                    break
            if key_col is None:
                return u'File cần cột Id (xuất từ bảng này) hoặc một cột có giá trị không trùng (vd số sheet)'
        index = dict((u'{0}'.format(r['id']) if id_col is not None else r['o'][key_col], self.rows.index(r)) for r in olds)
        batch, sk, unmatched = [], [0, 0], []
        at = id_col if id_col is not None else key_file
        for line in m[1:]:
            if not any((u'{0}'.format(v or u'')).strip() for v in line):
                continue
            k = (u'{0}'.format(line[at]) if at < len(line) else u'').strip()
            if k not in index:
                unmatched.append(line)
                continue
            i = index[k]
            for j, ci in enumerate(mapped):
                if ci is None or j == id_col or j >= len(line) or not self.cols[ci]['editable']:
                    continue
                if u'{0}'.format(line[j]) != self.rows[i]['v'][ci]:
                    self.set_cell(i, ci, u'{0}'.format(line[j]), batch, sk)
        added = 0
        sn = next((k for k, c in enumerate(self.cols) if c.get('role') == 'sheetNumber'), None)
        if unmatched and self.data.get('sheetList') and sn in mapped and (id_col is None or all(not (u'{0}'.format(l[id_col]) if id_col < len(l) else u'').strip() for l in unmatched)):
            if MessageBox.Show(u'{0} dòng trong file không có trong danh mục. Thêm thành sheet mới?'.format(len(unmatched)), TITLE, MessageBoxButton.YesNo) == MessageBoxResult.Yes:
                for line in unmatched:
                    v = [u''] * len(self.cols)
                    for j, ci in enumerate(mapped):
                        if ci is not None and j != id_col and j < len(line):
                            v[ci] = u'{0}'.format(line[j])
                    self._new_row(v)
                    added += 1
                self.apply_filter()
        self.commit(batch, sk)
        return u'Nhập {0}: {1} ô thay đổi'.format(name, len(batch)) + (u', {0} sheet mới'.format(added) if added else u'') + \
            (u', {0} dòng không khớp'.format(len(unmatched)) if unmatched and not added else u'') + u' · bấm Ghi vào Revit để ghi'

    # -- status line --------------------------------------------------------------------------------------------
    def _status(self):
        if self.data is None or self.dv is None:
            return
        dirty = self.dirty_count()
        self.f('b_save').Content = u'Ghi vào Revit ({0})'.format(dirty) if dirty else u'Ghi vào Revit'
        self.f('b_save').IsEnabled = bool(dirty)
        self.f('b_undo').IsEnabled = bool(self.undo_stack)
        self.f('b_redo').IsEnabled = bool(self.redo_stack)
        busy = len([r for r in self.rows if r.get('lock') == 'busy'])
        oth = len([r for r in self.rows if r.get('lock') == 'others'])
        parts = [u'{0}/{1} dòng'.format(self.dv.Count, len(self.rows)), u'{0} cột'.format(len(self.shown)),
                 u'thứ tự như schedule' if self.data.get('order') == 'schedule' else u'thứ tự theo cột sắp xếp của schedule']
        if busy:
            parts.append(u'{0} dòng đang bị người khác mượn'.format(busy))
        if oth:
            parts.append(u'{0} dòng của người khác{1}'.format(oth, u' (đang cho sửa)' if self.unlock else u' (khoá)'))
        if self.data.get('truncated'):
            parts.append(u'chỉ đọc 20000 dòng đầu')
        n = self.grid.SelectedCells.Count
        if n > 1:
            parts.append(u'chọn {0} ô'.format(n))
        elif n == 1:
            ci, i = self._cell_of(self.grid.SelectedCells[0].Column, self.grid.SelectedCells[0].Item)
            if ci is not None:
                r, c = self.rows[i], self.cols[ci]
                lk = self._lock(r, c)
                by = (r.get('typeBy') if c['source'] == 'type' else r.get('by')) or u'người khác'
                if lk == 'busy':
                    parts.append(u'ô này: đang bị {0} mượn'.format(by))
                elif lk == 'others' and not self.unlock:
                    parts.append(u'ô này: của {0}'.format(by))
                elif r['v'][ci] != r['o'][ci]:
                    parts.append(u'trước: {0}'.format(r['o'][ci] or u'(trống)'))
        note = getattr(self, 'after_load', None)
        if note:
            parts.insert(0, note.split(u'\n')[0])
            self.after_load = None
        self.say(u' · '.join(parts))


# ---- Công cụ: what the tools have left to do ---------------------------------------------------------

class Tools(object):
    def __init__(self, host, root):
        self.host = host
        self.list = root.FindName('t_list')
        root.FindName('t_refresh').Click += lambda s, e: self.refresh()
        root.FindName('t_sync').Click += lambda s, e: self.sync()
        self.list.ItemsSource = [u'Bấm Kiểm tra lại để xem các công cụ còn việc gì trong model này.']

    def refresh(self):
        self.list.ItemsSource = [u'Đang kiểm tra…']
        self.host.run('task', 'status_board', {}, self._got, lambda err: setattr(self.list, 'ItemsSource', [u'Lỗi: {0}'.format(err)]))

    def _got(self, b):
        out = []
        for r in b.get('printSets') or []:
            if not r.get('setExists'):
                out.append(u'Print set: chưa có cho {0} ({1} sheet)'.format(r['schedule'], r['sheets']))
            elif r.get('inSync'):
                out.append(u'Print set {0}: khớp danh mục ({1} sheet)'.format(r['schedule'], r['sheets']))
            else:
                out.append(u'Print set {0}: lệch danh mục +{1} −{2}'.format(r['schedule'], r['add'], r['remove']))
        for r in b.get('columns') or []:
            if not r.get('inSync') and not r.get('error'):
                out.append(u'Cột danh mục {0}: thiếu {1} cột, {2} số No.'.format(r['schedule'], r.get('addColumns', 0), r.get('numbersToWrite', 0)))
        u = b.get('untagged')
        if u:
            left = [u'{0} {1}'.format(n, k) for k, n in (u.get('counts') or {}).items() if n]
            out.append(u'Chưa tag ở {0}: {1}'.format(u.get('view'), u', '.join(left) if left else u'đủ'))
        if b.get('highlights'):
            out.append(u'Còn {0} vùng highlight'.format(b['highlights']))
        if b.get('untaggedMarks'):
            out.append(u'Còn {0} vùng tô cam chưa tag'.format(b['untaggedMarks']))
        self.list.ItemsSource = out or [u'Không còn việc']

    def sync(self):
        self.list.ItemsSource = [u'Đang đồng bộ…']
        self.host.run('task', 'print_set_sync', {'all': True}, lambda r: self.refresh(), lambda err: setattr(self.list, 'ItemsSource', [u'Lỗi: {0}'.format(err)]))


# ---- the pane ----------------------------------------------------------------------------------------

class Host(object):
    def __init__(self, uiapp, page=None):
        self.uiapp = uiapp
        self.page = page if page is not None else Page()
        with io.open(os.path.join(HERE, 'pane.xaml'), encoding='utf-8') as f:
            self.root = XamlReader.Parse(f.read())
        self.page.Content = self.root
        self.table = Table(self, self.root)
        self.tools = Tools(self, self.root)
        self.docked = False
        self.closed = False
        self.error = None
        self.page.Loaded += self._on_loaded
        self.page.IsVisibleChanged += self._on_loaded
        uiapp.ViewActivated += self._on_view
        uiapp.Application.DocumentClosed += self._on_doc_closed

    def run(self, kind, name, args, done, fail=None):
        """A job on the API thread; done(result) or fail(error) back on the UI thread."""
        job = web_bridge._Job(kind, name, {'args': args, 'label': name})
        web_bridge.BRIDGE.submit(job)

        def wait():
            job.done.wait()
            self.page.Dispatcher.BeginInvoke(Action(lambda: self._deliver(job.result, done, fail)))
        t = threading.Thread(target=wait)
        t.daemon = True
        t.start()

    def _deliver(self, text, done, fail):
        try:
            res = json.loads(text) if text else {'error': u'no result'}
        except Exception as ex:
            res = {'error': u'{0}'.format(ex)}
        try:
            if isinstance(res, dict) and res.get('error'):
                if fail is not None:
                    fail(res['error'])
                else:
                    self.table.say(u'Lỗi: {0}'.format(res['error']))
            else:
                done(res)
        except Exception as ex:
            self.table.say(u'Lỗi: {0}'.format(ex))

    # -- showing ---------------------------------------------------------------------------------
    def show(self):
        """Show the pane. False when Revit has not created it yet (it does at its next start)."""
        try:
            self.uiapp.GetDockablePane(DockablePaneId(PANE_ID)).Show()
        except Exception as ex:
            self.error = u'{0}'.format(ex)
            return False
        self.table.refresh_doc()
        return True

    def hide(self):
        try:
            self.uiapp.GetDockablePane(DockablePaneId(PANE_ID)).Hide()
        except Exception:
            pass

    def _shown(self):
        try:
            return self.uiapp.GetDockablePane(DockablePaneId(PANE_ID)).IsShown()
        except Exception:
            return False

    def _on_loaded(self, sender, e):
        # Revit loads a registered pane even while it is hidden: read the model only if it really shows
        if not self.closed and self._shown():
            self.table.refresh_doc()

    def _on_view(self, sender, e):
        if not self.closed and self._shown():
            self.table.refresh_doc()

    def _on_doc_closed(self, sender, e):
        if not self.closed and self.table.data is not None and not [d for d in self.uiapp.Application.Documents if not d.IsLinked]:
            self.table.doc_title = None
            self.table.clear()

    def close(self):
        """A reload replaces this host (a registered pane keeps the same page)."""
        self.closed = True
        try:
            self.page.Loaded -= self._on_loaded
            self.page.IsVisibleChanged -= self._on_loaded
            self.uiapp.ViewActivated -= self._on_view
            self.uiapp.Application.DocumentClosed -= self._on_doc_closed
            self.page.Content = None
        except Exception:
            pass


def setup(uiapp):
    """Called by startup.py on every pyRevit load."""
    old = AppDomain.CurrentDomain.GetData(SLOT)
    docked = old is not None and getattr(old, 'docked', False)
    page = getattr(old, 'page', None) if docked else None
    if old is not None:
        old.close()
    host = Host(uiapp, page)
    host.docked = docked
    if not docked:
        try:
            uiapp.RegisterDockablePane(DockablePaneId(PANE_ID), TITLE, _Provider(host.page))
            host.docked = True
        except Exception as ex:
            host.error = u'{0}'.format(ex)
    AppDomain.CurrentDomain.SetData(SLOT, host)
    if host.page.IsLoaded:       # reloaded with the pane open
        host._on_loaded(None, None)
    return host


def current(uiapp):
    host = AppDomain.CurrentDomain.GetData(SLOT)
    return host if host is not None else setup(uiapp)
