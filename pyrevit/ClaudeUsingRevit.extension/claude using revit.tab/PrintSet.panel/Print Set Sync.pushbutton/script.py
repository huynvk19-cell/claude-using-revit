# -*- coding: utf-8 -*-
"""Print set with the exact name of a sheet-list schedule, holding exactly its sheets.

One window: pick the schedule, see what the print set gains and loses, update.
Shift+click: update the last used schedule's print set without the window
(the window still opens when that set was last changed by someone else).
"""
from pyrevit import forms, revit, script  # first: loads the WPF assemblies

from System.Windows import (FontWeights, GridLength, GridUnitType, TextTrimming,
                            Thickness, VerticalAlignment, Visibility)
from System.Windows.Controls import (ColumnDefinition, Grid, RadioButton,
                                     StackPanel, TextBlock)
from System.Windows.Input import Cursors, Mouse
from System.Windows.Media import BrushConverter

import print_set_sync as ps

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()
_bc = BrushConverter()

GREEN, ORANGE, RED, GREY = "#22A06B", "#D9730D", "#D64545", "#8A8F98"


def brush(hex_color):
    return _bc.ConvertFrom(hex_color)


def text(s, size=None, bold=False, color=None, margin=None):
    tb = TextBlock()
    tb.Text = s
    tb.VerticalAlignment = VerticalAlignment.Center
    tb.TextTrimming = TextTrimming.CharacterEllipsis
    if size:
        tb.FontSize = size
    if bold:
        tb.FontWeight = FontWeights.SemiBold
    if color:
        tb.Foreground = brush(color)
    if margin:
        tb.Margin = Thickness(*margin)
    return tb


def status(c):
    """(short line, colour) for one schedule's comparison."""
    if c["set"] is None:
        return u"chưa có print set cùng tên", GREY
    if c["in_sync"]:
        return u"print set đã khớp", GREEN
    parts = []
    if c["added"]:
        parts.append(u"+{0} thêm".format(len(c["added"])))
    if c["removed"]:
        parts.append(u"−{0} bỏ".format(len(c["removed"])))
    return u"print set lệch: " + u" · ".join(parts), ORANGE


def default_schedule(schedules):
    names = [s.Name for s in schedules]
    av = doc.ActiveView.Id.IntegerValue
    for s in schedules:
        if s.Id.IntegerValue == av:
            return s.Name
    last = cfg.get_option("schedule", u"")
    if last in names:
        return last
    psets = ps.print_sets(doc)
    for n in names:
        if n in psets:
            return n
    return names[0] if names else None


def save_compare(c):
    """Write the print set of comparison c; returns the done line."""
    name = c["schedule"].Name
    res = ps.sync(doc, c)
    cfg.schedule = name
    script.save_config()
    if res["created"]:
        done = u"Đã tạo print set \"{0}\": {1} sheet.".format(name, res["count"])
    else:
        done = u"Đã cập nhật print set \"{0}\": {1} sheet (+{2}, −{3}).".format(
            name, res["count"], len(c["added"]), len(c["removed"]))
    if res["renumber_error"]:
        return done + u" Chưa đánh lại được No.: {0}".format(res["renumber_error"])
    if res["renumbered"]:
        return done + u" Đã đánh lại No. {0} sheet.".format(res["renumbered"])
    return done


class PrintSetWindow(forms.WPFWindow):
    def __init__(self):
        forms.WPFWindow.__init__(self, script.get_bundle_file("ui.xaml"))
        self.schedules = ps.sheet_schedules(doc)
        self.cmp, self.radio = {}, {}
        self.current = None
        self.open_target = None

        self.header.MouseLeftButtonDown += lambda s, e: self.DragMove()
        self.close_btn.Click += lambda s, e: self.Close()
        self.apply_btn.Click += self.on_apply
        self.open_btn.Click += self.on_open
        self.all_chip.Checked += lambda s, e: self.refresh()
        self.all_chip.Unchecked += lambda s, e: self.refresh()
        self.force_chk.Checked += lambda s, e: self.refresh()
        self.force_chk.Unchecked += lambda s, e: self.refresh()

        self.compare_all()
        if not self.schedules:
            self.schedules_list.Children.Add(text(
                u"Model chưa có schedule danh mục bản vẽ (Sheet List).", size=12, color=GREY))
        for vs in self.schedules:
            self._add_row(vs)
        first = default_schedule(self.schedules)
        if first:
            self.radio[first].IsChecked = True
        else:
            self.refresh()

    def compare_all(self):
        psets = ps.print_sets(doc)
        for vs in self.schedules:
            self.cmp[vs.Name] = ps.compare(doc, vs, psets)

    # ---- building -------------------------------------------------------

    def _add_row(self, vs):
        rb = RadioButton()
        rb.Style = self.FindResource("Pick")
        rb.GroupName = "schedule"
        rb.Checked += self._on_pick(vs.Name)
        self.schedules_list.Children.Add(rb)
        self.radio[vs.Name] = rb
        self._fill_row(vs.Name)

    def _on_pick(self, name):
        def handler(sender, args):
            self.pick(name)
        return handler

    def _fill_row(self, name):
        c = self.cmp[name]
        g = Grid()
        for w in (GridLength(1, GridUnitType.Star), GridLength.Auto):
            cd = ColumnDefinition()
            cd.Width = w
            g.ColumnDefinitions.Add(cd)
        sp = StackPanel()
        sp.Children.Add(text(name, bold=True))
        line, color = status(c)
        sp.Children.Add(text(line, size=12, color=color, margin=(0, 2, 0, 0)))
        g.Children.Add(sp)
        n = text(u"{0} sheet".format(len(c["sheets"])), size=12, color=GREY, margin=(10, 0, 0, 0))
        Grid.SetColumn(n, 1)
        g.Children.Add(n)
        self.radio[name].Content = g

    def _sheet_line(self, s, mark, color):
        g = Grid()
        for w in (GridLength(18), GridLength(1, GridUnitType.Star)):
            cd = ColumnDefinition()
            cd.Width = w
            g.ColumnDefinitions.Add(cd)
        m = text(mark, bold=True, color=color)
        g.Children.Add(m)
        label = text(ps.sheet_label(s), size=12, color=color if mark else None, margin=(0, 2, 0, 2))
        Grid.SetColumn(label, 1)
        g.Children.Add(label)
        return g

    # ---- state ----------------------------------------------------------

    def pick(self, name):
        self.current = name
        self.done_box.Visibility = Visibility.Collapsed
        self.force_chk.IsChecked = False
        self.refresh()

    def refresh(self):
        c = self.cmp.get(self.current)
        self.sheet_list.Children.Clear()
        self.warn_box.Visibility = Visibility.Collapsed
        self.open_btn.IsEnabled = c is not None
        if c is None:
            self.big_count.Text = u"–"
            self.summary.Text = u"Chưa chọn schedule"
            self.summary_sub.Text = u""
            self.apply_btn.IsEnabled = False
            return

        n = len(c["sheets"])
        self.big_count.Text = str(n)
        self.big_count.Foreground = brush("#3B5BDB" if n else GREY)
        self.summary.Text = u"sheet trong schedule \"{0}\"".format(self.current)
        sub = []
        if c["set"] is None:
            sub.append(u"Print set cùng tên chưa có, sẽ được tạo mới.")
        elif c["in_sync"]:
            sub.append(u"Print set \"{0}\" đã đúng như schedule.".format(self.current))
        else:
            sub.append(u"Print set hiện có {0} sheet · {1}.".format(
                c["set"].Views.Size, status(c)[0].split(u": ")[-1]))
        if c["placeholders"]:
            sub.append(u"{0} placeholder sheet không in được nên bỏ qua.".format(c["placeholders"]))
        self.summary_sub.Text = u" ".join(sub)

        show_all = bool(self.all_chip.IsChecked) or c["set"] is None
        added = set(s.Id.IntegerValue for s in c["added"])
        if show_all:
            self.list_caption.Text = u"SHEET THEO THỨ TỰ SCHEDULE"
            for s in c["sheets"]:
                new = s.Id.IntegerValue in added
                self.sheet_list.Children.Add(self._sheet_line(s, u"+" if new else u"", GREEN if new else None))
        else:
            self.list_caption.Text = u"THAY ĐỔI"
            for s in c["added"]:
                self.sheet_list.Children.Add(self._sheet_line(s, u"+", GREEN))
        for v in c["removed"]:
            self.sheet_list.Children.Add(self._sheet_line(v, u"−", RED))
        if not self.sheet_list.Children.Count:
            self.sheet_list.Children.Add(text(
                u"Không có gì thay đổi." if n else u"Schedule không hiện sheet nào.",
                size=12, color=GREY, margin=(0, 4, 0, 4)))

        blocked = False
        if c["set"] is not None and not c["in_sync"]:
            last, creator, busy = ps.owner(doc, c["set"])
            if busy:
                self.warn_text.Text = (u"Print set \"{0}\" đang do người khác giữ (borrowed). "
                                       u"Nhờ họ Sync, rồi bấm lại.").format(self.current)
                self.force_chk.Visibility = Visibility.Collapsed
                self.warn_box.Visibility = Visibility.Visible
                blocked = True
            elif ps.is_others(doc, c["set"]):
                self.warn_text.Text = (u"Print set \"{0}\" do {1} tạo, {2} sửa lần cuối, "
                                       u"không phải của bạn.").format(
                    self.current, creator or u"?", last or u"?")
                self.force_chk.Visibility = Visibility.Visible
                self.warn_box.Visibility = Visibility.Visible
                blocked = not self.force_chk.IsChecked

        if c["in_sync"]:
            self.apply_text.Text = u"Đã khớp"
        elif c["set"] is None:
            self.apply_text.Text = u"Tạo print set"
        else:
            self.apply_text.Text = u"Cập nhật print set"
        self.apply_btn.IsEnabled = n > 0 and not c["in_sync"] and not blocked

    # ---- actions --------------------------------------------------------

    def on_apply(self, sender, args):
        c = self.cmp[self.current]
        Mouse.OverrideCursor = Cursors.Wait
        try:
            done = save_compare(c)
        except Exception as ex:
            Mouse.OverrideCursor = None
            forms.alert(u"Không lưu được print set \"{0}\":\n{1}".format(self.current, ex))
            return
        finally:
            Mouse.OverrideCursor = None
        self.compare_all()
        for name in self.radio:
            self._fill_row(name)
        self.refresh()
        self.done_text.Text = done
        self.done_box.Visibility = Visibility.Visible

    def on_open(self, sender, args):
        self.open_target = self.cmp[self.current]["schedule"]
        self.Close()


def quick_update():
    """Shift+click: True when it handled the update itself."""
    name = cfg.get_option("schedule", u"")
    vs = [s for s in ps.sheet_schedules(doc) if s.Name == name]
    if not vs:
        return False
    c = ps.compare(doc, vs[0])
    if c["set"] is not None and not c["in_sync"]:
        if ps.owner(doc, c["set"])[2] or ps.is_others(doc, c["set"]):
            return False
    if c["in_sync"]:
        forms.toast(u"Print set \"{0}\" đã khớp schedule ({1} sheet).".format(name, len(c["sheets"])),
                    title=u"Print set")
        return True
    if not c["sheets"]:
        return False
    try:
        forms.toast(save_compare(c), title=u"Print set")
    except Exception as ex:
        forms.alert(u"Không lưu được print set \"{0}\":\n{1}".format(name, ex))
    return True


if not (__shiftclick__ and quick_update()):  # noqa: F821 (pyRevit global)
    win = PrintSetWindow()
    win.ShowDialog()
    if win.open_target:
        try:
            uidoc.ActiveView = win.open_target
        except Exception as ex:
            forms.alert(u"Không mở được schedule {0}:\n{1}".format(win.open_target.Name, ex))
