# -*- coding: utf-8 -*-
"""The drawing list (or the sheets ticked in it) to PDF: one file per sheet.

Which list: the sheet-list schedule open now, else the one picked. Then one glass
window lists its sheets in schedule order: all ticked (or only the ones selected in
Revit), filter by number / name, untick what is not wanted, optionally sort the files
into sub-folders by a sheet parameter (Project Browser grouping suggested), Xuất.
Files are never joined into one PDF. Shift+click: retry
the views / sheets left out after a crash (once the model is fixed). All the export
logic is in lib/pdf_export.py (shared with Claude's pyrevit_task "pdf_export").
"""
import os

from pyrevit import forms, revit, script

from System.Windows import (FontWeights, GridLength, GridUnitType, TextTrimming,
                            Thickness, Visibility)
from System.Windows.Controls import CheckBox, ColumnDefinition, Grid, TextBlock
from System.Windows.Input import Key, Keyboard, ModifierKeys, Mouse, MouseButtonState

import liquid_glass as lg
import pdf_export as pe
import print_set_sync as ps

doc = revit.doc
uidoc = revit.uidoc


def pick_schedule():
    schedules = ps.sheet_schedules(doc)
    if not schedules:
        lg.alert(u"Model chưa có schedule danh mục bản vẽ (Sheet List).", exitscript=True)
    av = doc.ActiveView.Id.IntegerValue
    for s in schedules:
        if s.Id.IntegerValue == av:
            return s
    last = pe.settings().get("schedule")
    names = sorted((s.Name for s in schedules), key=lambda n: (n != last, n.lower()))
    name = lg.choose(names, u"Xuất PDF: chọn danh mục bản vẽ", icon=lg.ICON_PDF)
    if not name:
        script.exit()
    return [s for s in schedules if s.Name == name][0]


class SheetPicker(lg.GlassWindow):
    """Tick the sheets of the drawing list to export (all ticked, or the ones selected
    in Revit), filter by number / name, pick the folder, export."""

    def __init__(self, schedule, sheets, picked_ids):
        lg.GlassWindow.__init__(self, script.get_bundle_file("ui.xaml"))
        self.result = None
        self.folder = pe.settings().get("folder")
        if self.folder and not os.path.isdir(self.folder):
            self.folder = None
        self.icon.Content = lg.ICON_PDF
        self.subtitle.Text = schedule.Name
        self.header.MouseLeftButtonDown += self.drag
        self.close_btn.Click += lambda s, e: self.Close()
        want = set(picked_ids or [])
        self.boxes = []
        self.anchor, self.dragging, self.paint, self.before, self.bulk = None, False, True, [], False
        for k, sh in enumerate(sheets):
            cb = CheckBox()
            cb.Style = self.FindResource("PickCheck")
            cb.Content = self._row(sh)
            cb.IsChecked = not want or sh.Id.IntegerValue in want
            cb.Checked += self.refresh
            cb.Unchecked += self.refresh
            cb.PreviewMouseLeftButtonDown += self._press(k)
            cb.MouseEnter += self._enter(k)
            self.sheet_list.Children.Add(cb)
            self.boxes.append((sh, cb))
        self.PreviewMouseLeftButtonUp += self._release
        self.PreviewKeyDown += self._key
        self.scroller.PreviewMouseMove += self._autoscroll
        # sub-folders: no / Project Browser / one sheet parameter (browser ones suggested)
        self.groups = pe.group_options(doc, sheets)
        self.levels = pe.browser_levels(doc)
        self.paths = {}
        last = pe.settings().get("group")
        pick = 0
        for k, (label, spec, suggested) in enumerate(self.groups):
            self.group_box.Items.Add(label + (u"  · gợi ý" if suggested else u""))
            if spec == last:
                pick = k
        self.group_box.SelectedIndex = pick
        self.group_box.SelectionChanged += self.on_group
        self.search.TextChanged += self.on_search
        self.pick_all.Click += self.on_all
        self.folder_btn.Click += self.on_folder
        self.export_btn.Click += self.on_export
        self.retry_btn.Click += self.on_retry
        self.sheets = sheets
        self.show_left_out()
        self.refresh()

    @property
    def group(self):
        return self.groups[max(0, self.group_box.SelectedIndex)][1]

    def on_group(self, sender, args):
        self.paths = {}
        self.refresh()

    def _preview(self, picked):
        """One line: the sub-folders the ticked sheets go to."""
        if not self.group:
            return u"Tất cả file PDF nằm thẳng trong thư mục lưu."
        names = []
        for sh in picked:
            sid = sh.Id.IntegerValue
            if sid not in self.paths:
                self.paths[sid] = u"\\".join(pe.group_path(doc, sh, self.group, self.levels))
            if self.paths[sid] not in names:
                names.append(self.paths[sid])
        shown = u", ".join(names[:6]) + (u", …" if len(names) > 6 else u"")
        return u"{0} thư mục con: {1}".format(len(names), shown) if names else u""

    def _row(self, sh):
        g = Grid()
        for w in (GridLength.Auto, GridLength(1, GridUnitType.Star)):
            cd = ColumnDefinition()
            cd.Width = w
            g.ColumnDefinitions.Add(cd)
        no = TextBlock()
        no.Text = sh.SheetNumber
        no.FontWeight = FontWeights.SemiBold
        g.Children.Add(no)
        name = TextBlock()
        name.Text = sh.Name
        name.Margin = Thickness(12, 0, 0, 0)
        name.TextTrimming = TextTrimming.CharacterEllipsis
        name.Foreground = self.FindResource("Ink2")
        Grid.SetColumn(name, 1)
        g.Children.Add(name)
        return g

    def picked(self):
        return [sh for sh, cb in self.boxes if cb.IsChecked]

    def visible(self):
        return [cb for _, cb in self.boxes if cb.Visibility == Visibility.Visible]

    def show_left_out(self):
        """Tell before export which views Revit died on (they go out alone or in tiles)."""
        lo = pe.crash_notes(doc, self.sheets)
        if not lo:
            self.warn_box.Visibility = Visibility.Collapsed
            return
        lines = [u"{0}: {1}".format(s.SheetNumber, u", ".join(
            v if n == 1 else u"{0} (chia {1}x{1} ô)".format(v, n) for v, n in rows))
            for s, rows in lo[:4]] + ([u"…"] if len(lo) > 4 else [])
        self.warn_text.Text = (u"Lần trước Revit văng khi vẽ các view này; lần này tool xuất riêng "
                               u"hoặc chia ô để vẫn ra đủ view:\n" + u"\n".join(lines) +
                               u"\nĐã sửa model? Bấm Thử lại để xuất như bình thường.")
        self.warn_box.Visibility = Visibility.Visible

    def on_retry(self, sender, args):
        pe.forget(doc)
        self.show_left_out()

    # ---- Photos-style picking: press = toggle and start a drag; dragging over rows sets
    # them all like the first one (moving back gives rows back their state); Shift+click
    # = the whole range from the last clicked row; Ctrl+A = every row shown.

    @staticmethod
    def _mod(m):
        return (Keyboard.Modifiers & m) == m

    @staticmethod
    def _held():
        return Mouse.LeftButton == MouseButtonState.Pressed

    def _set(self, states):
        """{row index: checked} in one go, one refresh."""
        self.bulk = True
        try:
            for i, v in states.items():
                cb = self.boxes[i][1]
                if cb.Visibility == Visibility.Visible and bool(cb.IsChecked) != v:
                    cb.IsChecked = v
        finally:
            self.bulk = False
        self.refresh()

    def _press(self, k):
        def handler(sender, e):
            e.Handled = True        # the row is toggled here, not by the CheckBox
            if self._mod(ModifierKeys.Shift) and self.anchor is not None:
                v = bool(self.boxes[self.anchor][1].IsChecked)
                lo, hi = sorted((self.anchor, k))
                self._set(dict((i, v) for i in range(lo, hi + 1)))
                return
            self.anchor = k
            self.paint = not bool(self.boxes[k][1].IsChecked)
            self.before = [bool(cb.IsChecked) for _, cb in self.boxes]
            self.dragging = True
            self._set({k: self.paint})
        return handler

    def _enter(self, k):
        def handler(sender, e):
            if not self.dragging:
                return
            if not self._held():
                self.dragging = False
                return
            lo, hi = sorted((self.anchor, k))
            self._set(dict((i, self.paint if lo <= i <= hi else self.before[i])
                           for i in range(len(self.boxes))))
        return handler

    def _release(self, sender, e):
        self.dragging = False

    def _key(self, sender, e):
        if e.Key == Key.A and self._mod(ModifierKeys.Control) \
                and not self.search.IsKeyboardFocusWithin:
            self._set(dict((i, True) for i in range(len(self.boxes))))
            e.Handled = True

    def _autoscroll(self, sender, e):
        """Dragging near the top / bottom edge of the list scrolls it."""
        if not self.dragging:
            return
        y = e.GetPosition(self.scroller).Y
        if y < 28:
            self.scroller.LineUp()
        elif y > self.scroller.ActualHeight - 28:
            self.scroller.LineDown()

    def refresh(self, sender=None, args=None):
        if self.bulk:
            return
        picked = self.picked()
        n = len(picked)
        self.big_count.Text = str(n)
        self.summary.Text = u"file PDF riêng · {0} sheet trong danh mục".format(len(self.boxes))
        self.folder_text.Text = self.folder or u"Chưa chọn thư mục lưu"
        self.group_preview.Text = self._preview(picked)
        self.export_text.Text = u"Xuất {0} file".format(n) if n else u"Chưa chọn sheet"
        self.export_btn.IsEnabled = n > 0
        vis = self.visible()
        self.pick_all.IsChecked = bool(vis) and all(cb.IsChecked for cb in vis)

    def on_search(self, sender, args):
        key = (self.search.Text or u"").strip().lower()
        self.search_hint.Visibility = Visibility.Collapsed if key else Visibility.Visible
        for sh, cb in self.boxes:
            hit = not key or key in sh.SheetNumber.lower() or key in sh.Name.lower()
            cb.Visibility = Visibility.Visible if hit else Visibility.Collapsed
        self.refresh()

    def on_all(self, sender, args):
        self._set(dict((i, bool(self.pick_all.IsChecked)) for i in range(len(self.boxes))))

    def on_folder(self, sender=None, args=None):
        f = forms.pick_folder(title=u"Thư mục lưu PDF")
        if f:
            self.folder = f
            pe.remember_folder(f)
        self.refresh()

    def on_export(self, sender, args):
        if not self.folder:
            self.on_folder()
            if not self.folder:
                return
        self.result = self.picked()
        s = pe.settings()
        s["group"] = self.group
        pe.save_settings(s)
        self.Close()


if __shiftclick__ and pe.crash_memory(doc):  # noqa: F821 (pyRevit global)
    if lg.alert(u"Xoá trí nhớ văng của model này (model đã sửa) để xuất mọi view như bình thường?",
                title=u"Xuất PDF", icon=lg.ICON_PDF, yes=True, no=True):
        pe.forget(doc)

vs = pick_schedule()
s = pe.settings()
s["schedule"] = vs.Name
pe.save_settings(s)
sheets, total = pe.sheets_of(doc, vs)
if not sheets:
    lg.alert(u"Schedule \"{0}\" không hiện sheet nào in được.".format(vs.Name), exitscript=True)
win = SheetPicker(vs, sheets, [i.IntegerValue for i in uidoc.Selection.GetElementIds()])
win.ShowDialog()
if not win.result:
    script.exit()
with lg.Progress(u"Xuất PDF", cancellable=True) as pb:
    def progress(i, n, text):
        pb.title = u"Xuất PDF {0}/{1} · {2}".format(i + 1, n, text)
        pb.update_progress(i, n)
    res = pe.run(doc, win.result, win.folder, win.group, progress=progress,
                 cancelled=lambda: pb.cancelled)

lines = []
lines.append(u"Đã xuất {0}/{1} file PDF{2} vào {3}{4}.".format(
    len(res["done"]), res["total"],
    u" ({0} file có sẵn từ lần trước)".format(len(res["skipped"])) if res["skipped"] else u"",
    res["folder"],
    u", chia {0} thư mục con".format(len(res["subfolders"])) if res["subfolders"] else u""))
if res["split"]:
    lines.append(u"Sheet nặng xuất từng phần rồi ghép: " +
                 u", ".join(u"{0} ({1} phần)".format(sh.SheetNumber, n) for sh, n in res["split"]))
for t in res["learnt"]:
    lines.append(u"Lần trước Revit văng: " + t)
if res["tiled"]:
    lines.append(u"View từng làm Revit văng, đã xuất từng ô vùng crop rồi ghép (đủ view, vector):")
    lines += [u"  {0}: {1}".format(sh.SheetNumber, u", ".join(
        u"{0} ({1}x{1})".format(v, n) for v, n in rows)) for sh, rows in res["tiled"]]
if res["failed"]:
    lines.append(u"Chưa xuất được {0} sheet (không có file thiếu view), bấm lại để thử lại:".format(
        len(res["failed"])))
    lines += [u"  {0}: {1}".format(sh.SheetNumber, e) for sh, e in res["failed"][:8]]
if res["failed"] or res["learnt"]:
    lines.append(u"Nếu Revit văng giữa chừng: mở lại và bấm Xuất PDF lần nữa, tool nhớ "
                 u"chỗ văng và tự né.")
if res["stopped"]:
    lines.append(u"Đã dừng an toàn ({0}). Bấm lại để xuất tiếp phần còn lại.".format(res["stopped"]))
choice = lg.alert(u"\n".join(lines), title=u"Xuất PDF", icon=lg.ICON_PDF,
                  options=[u"Mở thư mục", u"Đóng"])
if choice == u"Mở thư mục":
    os.startfile(res["folder"])
