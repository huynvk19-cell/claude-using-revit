# -*- coding: utf-8 -*-
"""One pick list for every tool: sheets or views in thin rows under folding groups.

Pick by group, by a set (print set, drawing list…), by several numbers / names at once (a
column pasted from Excel), by dragging over rows, Shift+click (a range) or Ctrl+A. Only the
rows on screen are drawn, so 1000 sheets stay light.

PickList(window, items, …)   the list inside a tool's own window. The window's XAML holds
                             <ListBox x:Name="scroller" Style="{StaticResource PickList}" MaxHeight="420"/>
                             and the controls search, search_hint, pick_all, only_on, open_all
                             (as in lib/pick_list.xaml).
pick_sheets(doc, …)          a window of its own: the sheets picked, None when closed
pick_views(doc, …)           the same for views
                             (forms.select_sheets / forms.select_views open these two).

What is picked lives in lib/pick_model.py (plain Python, tested outside Revit).
"""
import os

from cur import forms, revit, script  # first: loads the WPF assemblies

from System import Double, Object, String
from System.Collections.Generic import Dictionary, List
from System.Windows import FontWeights, FrameworkElement, GridLength, Size, Visibility
from System.Windows.Controls import ListBoxItem, TextBlock
from System.Windows.Input import Key, Keyboard, ModifierKeys, Mouse, MouseButtonState
from System.Windows.Media import VisualTreeHelper

import liquid_glass as lg
import pick_model as pm

HERE = os.path.dirname(os.path.abspath(__file__))
NO_GROUP = u"(không nhóm)"
PICK_BY = u"Chọn theo danh mục / print set…"


def _row(**kw):
    """A row the list binds to."""
    d = Dictionary[String, Object]()
    for k, v in kw.items():
        d[k] = GridLength(v) if k == "indent" else v
    return d


def _no_width(labels):
    """Width of the number column: names line up in thin rows."""
    longest = max((no for no, _, _ in labels), key=len) if labels else u""
    if not longest:
        return 0.0
    tb = TextBlock()
    tb.Text = longest
    tb.FontSize = 12
    tb.FontWeight = FontWeights.SemiBold
    tb.Measure(Size(Double.PositiveInfinity, Double.PositiveInfinity))
    return tb.DesiredSize.Width + 8


def element_id(item):
    return item.Id.IntegerValue


class PickList(object):
    """The pick list of a window: wires its controls, keeps what is picked.

    items: any objects; no / name / note: functions item -> text of the bold column, the
    grey column and the right column (no may give u"" for every item: one column of names).
    on: the start state per item (default none). changed: called after every change.
    Call set_groups (optional) then show() once the window is ready."""

    def __init__(self, win, items, no, name, note=None, on=None, changed=None, single=False, keys=None):
        self.win, self.items, self.changed_cb = win, list(items), changed
        labels = [(no(it) or u"", name(it) or u"", (note(it) if note else u"") or u"") for it in self.items]
        self.model = pm.PickModel(labels, on, row=_row, single=single, keys=keys)
        self.sets, self.set_box, self.ident = [], None, element_id
        self.box, self.search, self.search_hint = win.scroller, win.search, win.search_hint
        self.pick_all, self.only_on, self.open_all = win.pick_all, win.only_on, win.open_all
        win.Resources["NoWidth"] = _no_width(labels)
        self.box.PreviewMouseLeftButtonDown += self._press
        self.box.PreviewMouseMove += self._move
        win.PreviewMouseLeftButtonUp += self._release
        win.PreviewKeyDown += self._key
        self.search.TextChanged += self._search
        self.pick_all.Click += self._all
        self.only_on.Click += self.show
        self.open_all.Click += self._open_all
        if single:
            self.pick_all.Visibility = Visibility.Collapsed
        self.set_groups(None)

    # ---- for the window ---------------------------------------------------

    def picked(self):
        return [self.items[k] for k in self.model.picked()]

    def set_groups(self, paths, fold_if_many=False):
        """paths: one group label per item, or None (flat list)."""
        m = self.model
        m.set_groups(paths)
        if fold_if_many and len(self.items) > pm.OPEN_UP_TO:
            m.fold_all()
        self.open_all.Visibility = Visibility.Visible if m.group_rows else Visibility.Collapsed

    def set_on(self, on):
        self.model.set_on(on)
        self.show()

    def bind_sets(self, combo, prompt, sets, ident=None):
        """A combo that picks exactly the items of a set. sets: [(label, function -> the ids
        of its items)]; ident: item -> id (default its element id)."""
        self.sets, self.set_box, self.ident = list(sets), combo, ident or element_id
        combo.Items.Clear()
        for label in [prompt] + [s[0] for s in self.sets]:
            combo.Items.Add(label)
        combo.SelectedIndex = 0
        combo.SelectionChanged += self._set_picked
        if not self.sets:
            combo.Visibility = Visibility.Collapsed

    def show(self, sender=None, args=None):
        """Rebuild what the list shows, from the filter and the folded groups."""
        m = self.model
        m.only_on = bool(self.only_on.IsChecked)
        m.rebuild()
        self.box.ItemsSource = List[Object](m.shown)
        self._notify()

    def changed(self):
        """What is picked changed: redraw the rows on screen, recount."""
        m = self.model
        if self.only_on.IsChecked and not m.dragging:
            self.show()
            return
        m.paint_rows()
        self.box.Items.Refresh()
        self._notify()

    def _notify(self):
        m = self.model
        self.pick_all.IsChecked = m.all_matching_on()
        self.open_all.IsChecked = not m.closed
        if self.changed_cb:
            self.changed_cb()

    # ---- mouse and keys ---------------------------------------------------

    @staticmethod
    def _mod(m):
        return (Keyboard.Modifiers & m) == m

    def _hit(self, source):
        """(row under this element, its tag "box" / "arrow" / None); no row on the scroll bar."""
        tag = None
        el = source
        while el is not None and not isinstance(el, ListBoxItem):
            if tag is None and isinstance(el, FrameworkElement) and el.Tag in ("box", "arrow"):
                tag = el.Tag
            try:
                el = VisualTreeHelper.GetParent(el)
            except Exception:
                el = None
        # a recycled row waiting for use holds no row
        row = el.Content if el is not None else None
        return (row, tag) if isinstance(row, Dictionary[String, Object]) else (None, None)

    def _press(self, sender, e):
        row, tag = self._hit(e.OriginalSource)
        if row is None:
            return
        e.Handled = True
        if self.model.press(row, tag, self._mod(ModifierKeys.Shift)) == pm.SHOW:
            self.show()
        else:
            self.changed()

    def _move(self, sender, e):
        m = self.model
        if not m.dragging:
            return
        if Mouse.LeftButton != MouseButtonState.Pressed:
            m.dragging = False
            return
        row, _ = self._hit(e.OriginalSource)
        if row is not None and row["kind"] == "s" and row["k"] in m.where:
            if m.range_to(row["k"]):
                self.changed()
            i = m.where[row["k"]]
            y = e.GetPosition(self.box).Y        # near the edges the list scrolls on
            if y < 24 and i > 0:
                self.box.ScrollIntoView(m.shown[i - 1])
            elif y > self.box.ActualHeight - 24 and i + 1 < len(m.shown):
                self.box.ScrollIntoView(m.shown[i + 1])

    def _release(self, sender, e):
        m = self.model
        was = m.dragging
        m.dragging = False
        if was and self.only_on.IsChecked:
            self.show()

    def _key(self, sender, e):
        if e.Key == Key.Return and self.search.IsKeyboardFocusWithin:
            e.Handled = True        # the box takes pasted lines, Enter adds none
        if e.Key == Key.A and self._mod(ModifierKeys.Control) and not self.search.IsKeyboardFocusWithin:
            self.model.set_matching(True)
            self.changed()
            e.Handled = True

    def _search(self, sender, args):
        text = self.search.Text or u""
        self.search_hint.Visibility = Visibility.Collapsed if text.strip() else Visibility.Visible
        self.model.terms = pm.parse_terms(text)
        self.show()

    def _all(self, sender, args):
        self.model.set_matching(bool(self.pick_all.IsChecked))
        self.changed()

    def _open_all(self, sender, args):
        self.model.fold_all(not self.open_all.IsChecked)
        self.show()

    def _set_picked(self, sender, args):
        i = self.set_box.SelectedIndex - 1
        if i < 0 or i >= len(self.sets):
            return
        ids = set(self.sets[i][1]())
        self.set_on([self.ident(it) in ids for it in self.items])


# ---- a window of its own ----------------------------------------------------------------

class PickWindow(lg.GlassWindow):
    def __init__(self, items, no, name, note, paths, sets, on, title, subtitle, noun, button, multiple,
                 icon, hint, keys):
        lg.GlassWindow.__init__(self, os.path.join(HERE, "pick_list.xaml"))
        self.result = None
        self.noun, self.button = noun, button
        self.icon.Content = icon
        self.head_title.Text = title
        self.subtitle.Text = subtitle
        self.search_hint.Text = hint
        self.header.MouseLeftButtonDown += self.drag
        self.close_btn.Click += lambda s, e: self.Close()
        self.cancel_btn.Click += lambda s, e: self.Close()
        self.ok_btn.Click += self._ok
        self.list = PickList(self, items, no, name, note, on=on, changed=self.refresh, single=not multiple,
                             keys=keys)
        self.list.bind_sets(self.set_box, PICK_BY, sets if multiple else [])
        self.list.set_groups(paths, fold_if_many=True)
        if not multiple:
            self.tips.Text = u"Bấm tên nhóm: mở / thu · bấm một dòng: chọn"
        self.list.show()
        self.Loaded += lambda s, e: self.search.Focus()

    def refresh(self):
        n = len(self.list.model.picked())
        self.count.Text = u"{0} / {1} {2} đã chọn".format(n, len(self.list.items), self.noun)
        self.ok_text.Text = u"{0} {1} {2}".format(self.button, n, self.noun) if n else u"Chưa chọn {0}".format(self.noun)
        self.ok_btn.IsEnabled = n > 0

    def _ok(self, sender, args):
        self.result = self.list.picked()
        self.Close()


def pick(items, no, name, note=None, paths=None, sets=None, checked=None, title=u"Chọn", subtitle=u"",
         noun=u"mục", button=u"Chọn", multiple=True, icon=None, hint=u"Lọc theo tên, nhiều mục cách nhau dấu phẩy",
         keys=None):
    """The pick window for any items. paths: a group label per item; sets: [(label, function
    -> ids)]; checked: ids ticked at the start. Returns the picked items (one item when
    multiple=False), None when closed."""
    items = list(items)
    want = set(checked or [])
    on = [element_id(it) in want for it in items] if want else None
    w = PickWindow(items, no, name, note, paths, sets or [], on, title, subtitle, noun, button, multiple,
                   icon or lg.ICON_LIST, hint, keys)
    w.ShowDialog()
    if w.result is None:
        return None
    if not multiple:
        return w.result[0] if w.result else None
    return w.result


# ---- sheets and views of a model ------------------------------------------------------------

VIEW_TYPES = {
    "FloorPlan": u"Mặt bằng", "CeilingPlan": u"Mặt bằng trần", "EngineeringPlan": u"Mặt bằng kết cấu",
    "AreaPlan": u"Mặt bằng diện tích", "Elevation": u"Mặt đứng", "Section": u"Mặt cắt", "Detail": u"Chi tiết",
    "ThreeD": u"3D", "DraftingView": u"Drafting", "Legend": u"Legend", "Schedule": u"Schedule",
    "Rendering": u"Rendering", "Walkthrough": u"Walkthrough"}


def view_type_label(view):
    t = u"{0}".format(view.ViewType)
    return VIEW_TYPES.get(t, t)


def _ids(checked):
    """Element ids from elements, ElementIds or numbers."""
    out = set()
    for c in checked or []:
        try:
            out.add(c.Id.IntegerValue)
        except Exception:
            try:
                out.add(c.IntegerValue)
            except Exception:
                out.add(int(c))
    return out


def browser_paths(doc, elements, sheets):
    """The Project Browser folder of each sheet / view (u"" where the browser has none)."""
    from Autodesk.Revit.DB import BrowserOrganization
    try:
        org = (BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc) if sheets
               else BrowserOrganization.GetCurrentBrowserOrganizationForViews(doc))
    except Exception:
        org = None
    out = []
    for el in elements:
        path = u""
        if org is not None:
            try:
                path = u" › ".join((it.Name or NO_GROUP) for it in org.GetFolderItems(el.Id))
            except Exception:
                path = u""
        out.append(path)
    return out


def _sheet_sets(doc):
    """[(label, function -> sheet ids)]: the drawing lists, then the print sets."""
    import print_set_sync as ps
    out = []
    for vs in ps.sheet_schedules(doc):
        out.append((u"Danh mục: " + vs.Name,
                    lambda vs=vs: [s.Id.IntegerValue for s in ps.schedule_sheets(doc, vs, True)[0]]))
    for n, pset in sorted(ps.print_sets(doc).items(), key=lambda x: x[0].lower()):
        out.append((u"Print set: " + n, lambda pset=pset: ps.set_sheet_ids(pset)))
    return out


def pick_sheets(doc=None, title=u"Chọn sheet", button=u"Chọn", multiple=True, filterfunc=None,
                include_placeholder=True, checked=None):
    """Pick sheets of the model: by number, under the Project Browser's folders; a set = a
    drawing list or a print set. checked: sheets / ids ticked at the start."""
    from Autodesk.Revit.DB import FilteredElementCollector, ViewSheet
    doc = doc or revit.doc
    sheets = [s for s in FilteredElementCollector(doc).OfClass(ViewSheet)
              if (include_placeholder or not s.IsPlaceholder) and (filterfunc is None or filterfunc(s))]
    sheets.sort(key=lambda s: s.SheetNumber)
    paths = [p or NO_GROUP for p in browser_paths(doc, sheets, True)]
    return pick(sheets, lambda s: s.SheetNumber, lambda s: s.Name, paths=paths, sets=_sheet_sets(doc),
                checked=_ids(checked), title=title, subtitle=u"{0} sheet trong model".format(len(sheets)),
                noun=u"sheet", button=button, multiple=multiple,
                hint=u"Lọc số / tên sheet, nhiều mục cách nhau dấu phẩy")


def pick_views(doc=None, title=u"Chọn view", button=u"Chọn", multiple=True, filterfunc=None, checked=None):
    """Pick views of the model: by name, under the Project Browser's folders (else the view
    type), with the sheet each one is on; a set = on a sheet / not on a sheet, the views of
    a drawing list or of a print set. checked: views / ids ticked at the start."""
    from Autodesk.Revit.DB import FilteredElementCollector, View, ViewSheet, ViewType, Viewport
    doc = doc or revit.doc
    views = []
    for v in FilteredElementCollector(doc).OfClass(View):
        try:
            if v.IsTemplate or isinstance(v, ViewSheet) or v.ViewType in (
                    ViewType.ProjectBrowser, ViewType.SystemBrowser, ViewType.Internal, ViewType.Undefined):
                continue
        except Exception:
            continue
        if filterfunc is not None and not filterfunc(v):
            continue
        views.append(v)
    on_sheet, numbers = {}, {}          # view id -> sheet id, sheet id -> sheet number
    for vp in FilteredElementCollector(doc).OfClass(Viewport):
        sid = vp.SheetId.IntegerValue
        if sid not in numbers:
            sheet = doc.GetElement(vp.SheetId)
            numbers[sid] = sheet.SheetNumber if sheet is not None else u""
        on_sheet.setdefault(vp.ViewId.IntegerValue, sid)
    folders = browser_paths(doc, views, False)
    rows = sorted(((folders[i] or view_type_label(v), v.Name.lower(), i) for i, v in enumerate(views)))
    paths = [r[0] for r in rows]
    views = [views[r[2]] for r in rows]
    ids = [v.Id.IntegerValue for v in views]

    def note(v):
        return numbers.get(on_sheet.get(v.Id.IntegerValue), u"")

    def on_sheets(sheet_ids):
        want = set(sheet_ids)
        return [i for i in ids if on_sheet.get(i) in want]

    sets = [(u"View đã đặt trên sheet", lambda: [i for i in ids if i in on_sheet]),
            (u"View chưa đặt trên sheet", lambda: [i for i in ids if i not in on_sheet])]
    for label, fn in _sheet_sets(doc):
        # a print set may hold views as well as sheets
        sets.append((label, lambda fn=fn: (lambda got: on_sheets(got) + list(got))(set(fn()))))
    keys = [u"{0} {1} {2}".format(v.Name, note(v), view_type_label(v)) for v in views]
    return pick(views, lambda v: u"", lambda v: v.Name, note=note, paths=paths, sets=sets, checked=_ids(checked),
                title=title, subtitle=u"{0} view trong model".format(len(views)), noun=u"view", button=button,
                multiple=multiple, hint=u"Lọc tên view / số sheet, nhiều mục cách nhau dấu phẩy", keys=keys)
