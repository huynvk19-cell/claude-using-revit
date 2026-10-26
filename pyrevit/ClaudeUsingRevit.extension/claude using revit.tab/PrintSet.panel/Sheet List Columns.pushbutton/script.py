# -*- coding: utf-8 -*-
"""Standard columns + "No." numbering for one or more sheet-list schedules.

One window: tick the schedules, see what each gains (columns, numbers), apply.
Shift+click: apply to the schedules used last time without the window
(the window still opens when one of them is someone else's).
"""
from pyrevit import forms, revit, script  # first: loads the WPF assemblies

from System.Windows import (FontWeights, GridLength, GridUnitType, TextTrimming,
                            TextWrapping, Thickness, VerticalAlignment, Visibility)
from System.Windows.Controls import CheckBox, ColumnDefinition, Grid, StackPanel, TextBlock
from System.Windows.Input import Cursors, Mouse
from System.Windows.Media import BrushConverter

import liquid_glass as lg
import print_set_sync as ps
import sheet_list_columns as slc

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()
_bc = BrushConverter()

GREEN, ORANGE, RED, GREY, BLUE = "#22A06B", "#D9730D", "#D64545", "#8A8F98", "#3B5BDB"
MAX_NUMBER_LINES = 150


def brush(hex_color):
    return _bc.ConvertFrom(hex_color)


def text(s, size=None, bold=False, color=None, margin=None, wrap=False):
    tb = TextBlock()
    tb.Text = s
    tb.VerticalAlignment = VerticalAlignment.Center
    if wrap:
        tb.TextWrapping = TextWrapping.Wrap
    else:
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


def labels(keys):
    return u", ".join(slc.LABELS[k] for k in keys)


def status(p):
    """(short line, colour) for one schedule's plan."""
    if p["in_sync"]:
        return u"đúng chuẩn", GREEN
    parts = []
    added = len(p["add_visible"]) + len(p["add_hidden"])
    if added:
        parts.append(u"+{0} cột".format(added))
    if p["show"]:
        parts.append(u"hiện {0} cột".format(len(p["show"])))
    if p["reorder"] and not (p["add_visible"] or p["show"]):
        parts.append(u"lệch thứ tự cột")
    if p["numbers"]:
        parts.append(u"No. lệch {0} sheet".format(len(p["numbers"])))
    return u" · ".join(parts), ORANGE


def last_names():
    names = cfg.get_option("schedules", [])
    return [names] if isinstance(names, basestring) else list(names)  # noqa: F821 (IronPython)


def remember(names):
    cfg.schedules = list(names)
    script.save_config()


def done_line(res, names):
    out = []
    if res["created_param"]:
        out.append(u"Đã tạo tham số No. cho sheet.")
    out.append(u"{0}: thêm {1} cột, đánh lại No. {2} sheet.".format(
        u", ".join(u"\"{0}\"".format(n) for n in names), res["added"], res["written"]))
    if res["skipped"]:
        out.append(u"{0} sheet đang do người khác giữ, chưa đánh số: {1}.".format(
            len(res["skipped"]), u", ".join(s.SheetNumber for s in res["skipped"][:8])))
    if res["clashes"]:
        out.append(u"{0} sheet nằm trong nhiều schedule, giữ số của schedule đầu: {1}.".format(
            len(res["clashes"]), u", ".join(s.SheetNumber for s, _, _ in res["clashes"][:8])))
    return u" ".join(out)


class ColumnsWindow(lg.GlassWindow):
    def __init__(self):
        lg.GlassWindow.__init__(self, script.get_bundle_file("ui.xaml"))
        self.schedules = ps.sheet_schedules(doc)
        self.plans, self.boxes = {}, {}
        self.open_target = None
        self._bulk = False

        self.header.MouseLeftButtonDown += lambda s, e: self.DragMove()
        self.close_btn.Click += lambda s, e: self.Close()
        self.apply_btn.Click += self.on_apply
        self.open_btn.Click += self.on_open
        self.signers_btn.Click += self.on_signers
        self.fill_btn.Click += self.on_fill
        self.fill_param.SelectionChanged += lambda s, e: self.update_fill()
        self.fill_value.TextChanged += lambda s, e: self.update_fill()
        self.fill_sheets, self.fill_items = [], []
        self.pick_all.Checked += lambda s, e: self.tick_all(True)
        self.pick_all.Unchecked += lambda s, e: self.tick_all(False)
        for chk in (self.all_chip, self.force_chk):
            chk.Checked += lambda s, e: self.refresh()
            chk.Unchecked += lambda s, e: self.refresh()

        self.plan_all()
        if not self.schedules:
            self.schedules_list.Children.Add(text(
                u"Model chưa có schedule danh mục bản vẽ (Sheet List).", size=12, color=GREY))
        for vs in self.schedules:
            self._add_row(vs)
        self._bulk = True
        for name in self.default_names():
            self.boxes[name].IsChecked = True
        self._bulk = False
        self.refresh()

    def plan_all(self):
        pe, bound = slc.no_param(doc)
        self.no_exists = bound
        self.no_foreign = pe is not None and not slc.is_ours(pe)
        for vs in self.schedules:
            self.plans[vs.Name] = slc.plan(doc, vs, pe if bound else None)

    def default_names(self):
        av = doc.ActiveView.Id.IntegerValue
        for vs in self.schedules:
            if vs.Id.IntegerValue == av:
                return [vs.Name]
        return [n for n in last_names() if n in self.boxes]

    def selected(self):
        return [vs for vs in self.schedules if self.boxes[vs.Name].IsChecked]

    # ---- building -------------------------------------------------------

    def _add_row(self, vs):
        cb = CheckBox()
        cb.Style = self.FindResource("PickCheck")
        cb.Checked += lambda s, e: self.on_tick()
        cb.Unchecked += lambda s, e: self.on_tick()
        self.schedules_list.Children.Add(cb)
        self.boxes[vs.Name] = cb
        self._fill_row(vs.Name)

    def _fill_row(self, name):
        p = self.plans[name]
        g = Grid()
        for w in (GridLength(1, GridUnitType.Star), GridLength.Auto):
            cd = ColumnDefinition()
            cd.Width = w
            g.ColumnDefinitions.Add(cd)
        sp = StackPanel()
        sp.Children.Add(text(name, bold=True))
        line, color = status(p)
        sp.Children.Add(text(line, size=12, color=color, margin=(0, 2, 0, 0)))
        g.Children.Add(sp)
        n = text(u"{0} sheet".format(len(p["sheets"])), size=12, color=GREY, margin=(10, 0, 0, 0))
        Grid.SetColumn(n, 1)
        g.Children.Add(n)
        self.boxes[name].Content = g

    def _line(self, mark, s, color=None, indent=0):
        g = Grid()
        for w in (GridLength(18 + indent), GridLength(1, GridUnitType.Star)):
            cd = ColumnDefinition()
            cd.Width = w
            g.ColumnDefinitions.Add(cd)
        g.Children.Add(text(mark, bold=True, color=color, margin=(indent, 0, 0, 0)))
        t = text(s, size=12, color=color, margin=(0, 2, 0, 2), wrap=True)
        Grid.SetColumn(t, 1)
        g.Children.Add(t)
        return g

    # ---- state ----------------------------------------------------------

    def tick_all(self, on):
        self._bulk = True
        for cb in self.boxes.values():
            cb.IsChecked = on
        self._bulk = False
        self.on_tick()

    def on_tick(self):
        if self._bulk:
            return
        self.done_box.Visibility = Visibility.Collapsed
        self.force_chk.IsChecked = False
        self.refresh()

    def refresh(self):
        sel = self.selected()
        rows = self.sheet_list.Children
        rows.Clear()
        self.warn_box.Visibility = Visibility.Collapsed
        self.open_btn.IsEnabled = bool(sel)
        self.signers_btn.IsEnabled = False
        shown = slc.signers_shown(doc, sel)
        self.signers_text.Text = u"Ẩn cột ký" if shown else u"Hiện cột ký"
        self.signers_icon.Text = u"" if shown else u""
        self.refresh_fill(sel)
        if not sel:
            self.big_count.Text = u"–"
            self.big_count.Foreground = brush(GREY)
            self.summary.Text = u"Chưa chọn schedule"
            self.summary_sub.Text = u"Tick một hay nhiều schedule danh mục bản vẽ ở trên."
            self.apply_btn.IsEnabled = False
            return

        plans = [self.plans[vs.Name] for vs in sel]
        todo = [p for p in plans if not p["in_sync"]]
        self.big_count.Text = str(len(todo))
        self.big_count.Foreground = brush(BLUE if todo else GREEN)
        self.summary.Text = u"/ {0} schedule cần áp chuẩn".format(len(sel))
        sub = []
        if self.no_foreign:
            sub.append(u"Model đã có tham số No. không do tool tạo: tool sẽ dùng và ghi số vào nó.")
        elif not self.no_exists:
            sub.append(u"Tham số No. chưa có, sẽ được tạo (tham số sheet, nhóm Identity Data).")
        sub.append(u"Chỉ thêm cột, không xoá cột hay tham số nào.")
        self.summary_sub.Text = u" ".join(sub)

        self.list_caption.Text = u"THAY ĐỔI"
        show_numbers = bool(self.all_chip.IsChecked)
        seen, clashes = {}, set()
        for p in plans:
            for s in p["sheets"]:
                sid = s.Id.IntegerValue
                if sid in seen and seen[sid] != p["schedule"].Name:
                    clashes.add(s.SheetNumber)
                seen.setdefault(sid, p["schedule"].Name)
        for p in plans:
            rows.Add(text(p["schedule"].Name, bold=True, margin=(0, 6 if rows.Count else 2, 0, 2)))
            if p["scale_missing"]:
                rows.Add(self._line(u"!", u"Model chưa có tham số sheet SCALE: chưa thêm được cột Scale", ORANGE))
            if p["in_sync"]:
                rows.Add(self._line(u"✓", u"Đã đúng chuẩn.", GREEN))
                continue
            if p["add_visible"]:
                rows.Add(self._line(u"+", u"Thêm cột: " + labels(p["add_visible"]), GREEN))
            if p["add_hidden"]:
                rows.Add(self._line(u"+", u"Thêm cột ẩn: " + labels(p["add_hidden"]), GREEN))
            if p["show"]:
                rows.Add(self._line(u"◐", u"Hiện lại: " + labels(p["show"]), BLUE))
            if p["reorder"]:
                rows.Add(self._line(u"⇅", u"Xếp các cột chuẩn lên đầu, cột khác giữ thứ tự sau đó", BLUE))
            if p["numbers"]:
                rows.Add(self._line(u"#", u"Đánh lại No. {0} / {1} sheet".format(
                    len(p["numbers"]), len(p["sheets"])), BLUE))
                if show_numbers:
                    for s, old, new in p["numbers"][:MAX_NUMBER_LINES]:
                        rows.Add(self._line(u"", u"{0} → {1}   {2}".format(
                            u"–" if old is None else old, new, ps.sheet_label(s)), GREY, indent=18))
                    if len(p["numbers"]) > MAX_NUMBER_LINES:
                        rows.Add(self._line(u"", u"… và {0} sheet nữa".format(
                            len(p["numbers"]) - MAX_NUMBER_LINES), GREY, indent=18))
            if p["skipped"]:
                rows.Add(self._line(u"!", u"{0} sheet đang do người khác giữ, sẽ bỏ qua: {1}".format(
                    len(p["skipped"]), u", ".join(s.SheetNumber for s in p["skipped"][:8])), ORANGE))
            if p["sorted_by_no"]:
                rows.Add(self._line(u"!", u"Schedule đang sắp xếp theo No.: nên đổi sang Sheet Number "
                                          u"để số đi theo thứ tự bản vẽ.", ORANGE))
        if clashes:
            rows.Add(self._line(u"!", u"{0} sheet nằm trong nhiều schedule đã chọn, giữ số của schedule "
                                      u"đầu: {1}".format(len(clashes), u", ".join(sorted(clashes)[:8])), ORANGE))

        blocked = False
        busy = [p for p in plans if p["busy"]]
        others = [p for p in plans if p["others"] and not p["busy"]]
        if busy:
            self.warn_text.Text = (u"{0} đang do người khác giữ (borrowed). Nhờ họ Sync, rồi bấm lại.").format(
                u", ".join(u"\"{0}\"".format(p["schedule"].Name) for p in busy))
            self.force_chk.Visibility = Visibility.Collapsed
            self.warn_box.Visibility = Visibility.Visible
            blocked = True
        elif others:
            self.warn_text.Text = u"Không phải schedule của bạn: " + u"; ".join(
                u"\"{0}\" do {1} tạo, {2} sửa lần cuối".format(
                    p["schedule"].Name, p["creator"] or u"?", p["last"] or u"?") for p in others) + u"."
            self.force_chk.Visibility = Visibility.Visible
            self.warn_box.Visibility = Visibility.Visible
            blocked = not self.force_chk.IsChecked

        self.apply_text.Text = u"Đã đúng chuẩn" if not todo else u"Áp chuẩn"
        self.apply_btn.IsEnabled = bool(todo) and not blocked
        self.signers_btn.IsEnabled = not blocked

    def refresh_fill(self, sel):
        """Sheets of the ticked schedules + the parameters offered for them."""
        self.fill_sheets = slc.schedules_sheets(doc, sel)
        items = slc.fillable(doc, self.fill_sheets)
        if items != self.fill_items:
            keep = self.fill_param.SelectedItem
            self.fill_items = items
            self.fill_param.Items.Clear()
            for _, name in items:
                self.fill_param.Items.Add(name)
            names = [n for _, n in items]
            if names:
                self.fill_param.SelectedIndex = names.index(keep) if keep in names else 0
        self.update_fill()

    def fill_pid(self):
        i = self.fill_param.SelectedIndex
        return self.fill_items[i][0] if 0 <= i < len(self.fill_items) else None

    def update_fill(self):
        value = self.fill_value.Text or u""
        self.fill_hint.Visibility = Visibility.Collapsed if value else Visibility.Visible
        pid = self.fill_pid()
        sheets = self.fill_sheets
        self.fill_btn.IsEnabled = bool(sheets) and pid is not None
        if not sheets or pid is None:
            self.fill_info.Text = u"Tick schedule ở trên để chọn sheet."
            return
        vals = sorted(slc.current_values(sheets, pid).items(), key=lambda kv: -kv[1])
        now = u" · ".join(u"{0} ×{1}".format(u"\"{0}\"".format(v) if v else u"(trống)", n) for v, n in vals[:4])
        if len(vals) > 4:
            now += u" · {0} giá trị khác".format(len(vals) - 4)
        change = sum(n for v, n in vals if v != value)
        self.fill_info.Text = u"Đang có: {0}. Bấm Gán: {1} / {2} sheet đổi thành {3}.".format(
            now, change, len(sheets), u"\"{0}\"".format(value) if value else u"trống")
        self.fill_btn.IsEnabled = change > 0

    def on_fill(self, sender, args):
        pid, value = self.fill_pid(), self.fill_value.Text or u""
        name = self.fill_param.SelectedItem
        if not value and not lg.alert(u"Xoá giá trị {0} của mọi sheet đang chọn?".format(name),
                                      yes=True, no=True):
            return
        try:
            written, skipped = slc.fill(doc, self.fill_sheets, pid, value)
        except Exception as ex:
            lg.alert(u"Không gán được {0}:\n{1}".format(name, ex))
            return
        done = u"Đã gán {0} = {1} cho {2} sheet.".format(
            name, u"\"{0}\"".format(value) if value else u"trống", written)
        if skipped:
            done += u" {0} sheet đang do người khác giữ, chưa đổi: {1}.".format(
                len(skipped), u", ".join(s.SheetNumber for s in skipped[:8]))
        self.update_fill()
        self.done_text.Text = done
        self.done_box.Visibility = Visibility.Visible

    # ---- actions --------------------------------------------------------

    def on_apply(self, sender, args):
        sel = [p["schedule"] for p in (self.plans[vs.Name] for vs in self.selected())]
        Mouse.OverrideCursor = Cursors.Wait
        try:
            res = slc.apply(doc, sel)
        except Exception as ex:
            Mouse.OverrideCursor = None
            lg.alert(u"Không áp được chuẩn cột:\n{0}".format(ex))
            return
        finally:
            Mouse.OverrideCursor = None
        names = [vs.Name for vs in sel]
        remember(names)
        self.plan_all()
        for name in self.boxes:
            self._fill_row(name)
        self.refresh()
        self.done_text.Text = done_line(res, names)
        self.done_box.Visibility = Visibility.Visible

    def on_signers(self, sender, args):
        sel = self.selected()
        show = not slc.signers_shown(doc, sel)
        try:
            n = slc.set_signers(doc, sel, show)
        except Exception as ex:
            lg.alert(u"Không đổi được cột ký:\n{0}".format(ex))
            return
        self.plan_all()
        for name in self.boxes:
            self._fill_row(name)
        self.refresh()
        self.done_text.Text = u"{0} Checked By, Approved By, Designed By, Drawn By ({1} cột) trong {2} schedule.".format(
            u"Đã hiện" if show else u"Đã ẩn", n, len(sel))
        self.done_box.Visibility = Visibility.Visible

    def on_open(self, sender, args):
        self.open_target = self.selected()[0]
        self.Close()


def quick_apply():
    """Shift+click: True when it handled the update itself."""
    names = last_names()
    sel = [vs for vs in ps.sheet_schedules(doc) if vs.Name in names]
    if not sel:
        return False
    plans = [slc.plan(doc, vs) for vs in sel]
    todo = [p for p in plans if not p["in_sync"]]
    if any(p["busy"] or p["others"] for p in todo):
        return False
    if not todo:
        forms.toast(u"Đã đúng chuẩn: " + u", ".join(vs.Name for vs in sel), title=u"Chuẩn cột")
        return True
    try:
        res = slc.apply(doc, sel)
        forms.toast(done_line(res, [vs.Name for vs in sel]), title=u"Chuẩn cột")
    except Exception as ex:
        lg.alert(u"Không áp được chuẩn cột:\n{0}".format(ex))
    return True


if not (__shiftclick__ and quick_apply()):  # noqa: F821 (pyRevit global)
    win = ColumnsWindow()
    win.ShowDialog()
    if win.open_target:
        try:
            uidoc.ActiveView = win.open_target
        except Exception as ex:
            lg.alert(u"Không mở được schedule {0}:\n{1}".format(win.open_target.Name, ex))
