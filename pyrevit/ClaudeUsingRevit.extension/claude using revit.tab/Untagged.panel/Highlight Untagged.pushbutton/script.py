# -*- coding: utf-8 -*-
"""Orange 50% regions over untagged rooms, doors, windows, walls and floors without a spot elevation.

One window: pick the scope and the kinds, see the counts before anything is drawn,
draw, then click a view in the result to open it with its untagged elements selected.
"""
from pyrevit import forms, revit, script  # first: loads the WPF assemblies

from System.Collections.Generic import List
from System.Windows import (CornerRadius, FontWeights, GridLength, GridUnitType,
                            Thickness, VerticalAlignment, Visibility)
from System.Windows.Controls import (Border, Button, ColumnDefinition, Grid,
                                     Orientation, StackPanel, TextBlock)
from System.Windows.Controls.Primitives import ToggleButton
from System.Windows.Input import Cursors, Mouse
from System.Windows.Media import BrushConverter

from Autodesk.Revit.DB import ElementId

import untagged_marks as um

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()
_bc = BrushConverter()


def brush(hex_color):
    return _bc.ConvertFrom(hex_color)


def text(s, size=None, bold=False, color=None, margin=None):
    tb = TextBlock()
    tb.Text = s
    tb.VerticalAlignment = VerticalAlignment.Center
    if size:
        tb.FontSize = size
    if bold:
        tb.FontWeight = FontWeights.SemiBold
    if color:
        tb.Foreground = brush(color)
    if margin:
        tb.Margin = Thickness(*margin)
    return tb


def kinds_line(counts, kinds):
    return u" · ".join(u"{0} {1}".format(um.LABELS[k], counts[k]) for k in kinds if counts.get(k))


class UntaggedWindow(forms.WPFWindow):
    def __init__(self):
        forms.WPFWindow.__init__(self, script.get_bundle_file("ui.xaml"))
        self.picked = um.views_from_ids(doc, list(uidoc.Selection.GetElementIds()))
        self.active = um.views_from_ids(doc, [doc.ActiveView.Id])
        self.views, self.scope_text, self.prev_scope = [], u"", None
        self.found = {}
        self.open_target = None
        self.count_tb, self.chip = {}, {}
        for k in um.KINDS:
            self._add_chip(k)

        self.header.MouseLeftButtonDown += lambda s, e: self.DragMove()
        self.close_btn.Click += lambda s, e: self.Close()
        self.done_btn.Click += lambda s, e: self.Close()
        self.back_btn.Click += self.on_back
        self.apply_btn.Click += self.on_apply
        self.clear_btn.Click += self.on_clear
        self.sc_sel.Checked += lambda s, e: self.set_scope(
            self.sc_sel, self.picked, u"{0} view từ sheet / view đang chọn".format(len(self.picked)))
        self.sc_active.Checked += lambda s, e: self.set_scope(
            self.sc_active, self.active, self._active_text())
        self.sc_sheets.Click += self.on_pick_sheets
        self.sc_views.Click += self.on_pick_views

        self.sc_sel.Content = u"Đang chọn ({0})".format(len(self.picked)) if self.picked else u"Đang chọn"
        self.sc_sel.IsEnabled = bool(self.picked)
        self.sc_active.IsEnabled = bool(self.active)
        if self.picked:
            self.sc_sel.IsChecked = True
        elif self.active:
            self.sc_active.IsChecked = True
        else:
            self.refresh()

    # ---- building -------------------------------------------------------

    def _add_chip(self, k):
        tb = ToggleButton()
        tb.Style = self.FindResource("Chip")
        tb.IsChecked = bool(cfg.get_option("kind_" + k, True))
        sp = StackPanel()
        sp.Orientation = Orientation.Horizontal
        sp.Children.Add(text(um.LABELS[k]))
        badge = Border()
        badge.CornerRadius = CornerRadius(9)
        badge.Background = brush("#FFFFFF")
        badge.Padding = Thickness(7, 1, 7, 1)
        badge.Margin = Thickness(8, 0, 0, 0)
        cnt = text(u"–", size=11, bold=True, color="#9CA3AF")
        badge.Child = cnt
        sp.Children.Add(badge)
        tb.Content = sp
        tb.Checked += self.on_kind
        tb.Unchecked += self.on_kind
        self.chips.Children.Add(tb)
        self.chip[k], self.count_tb[k] = tb, cnt

    def _active_text(self):
        v = doc.ActiveView
        if isinstance(v, um.ViewSheet):
            return u"Sheet {0}: {1} view".format(v.SheetNumber, len(self.active))
        return v.Name

    def kinds(self):
        return [k for k in um.KINDS if self.chip[k].IsChecked]

    # ---- scope ----------------------------------------------------------

    def set_scope(self, radio, views, label):
        Mouse.OverrideCursor = Cursors.Wait
        try:
            um.scan(doc, views, um.KINDS, self.found)
        finally:
            Mouse.OverrideCursor = None
        self.views, self.scope_text, self.prev_scope = views, label, radio
        self.refresh()

    def _restore_scope(self):
        if self.prev_scope is not None:
            self.prev_scope.IsChecked = True
        else:
            self.sc_sheets.IsChecked = self.sc_views.IsChecked = False

    def on_pick_sheets(self, sender, args):
        sheets = forms.select_sheets(title=u"Chọn sheet", button_name=u"Chọn") or []
        views = um.views_from_ids(doc, [s.Id for s in sheets])
        if not views:
            return self._restore_scope()
        self.set_scope(self.sc_sheets, views, u"{0} view trên {1} sheet".format(len(views), len(sheets)))

    def on_pick_views(self, sender, args):
        sel = forms.select_views(title=u"Chọn view", button_name=u"Chọn",
                                 filterfunc=um.is_checkable) or []
        views = um.views_from_ids(doc, [v.Id for v in sel])
        if not views:
            return self._restore_scope()
        label = views[0].Name if len(views) == 1 else u"{0} view".format(len(views))
        self.set_scope(self.sc_views, views, label)

    def on_kind(self, sender, args):
        self.refresh()

    # ---- state ----------------------------------------------------------

    def refresh(self):
        kinds = self.kinds()
        counts = um.unique_counts(self.views, self.found, um.KINDS)
        for k in um.KINDS:
            n = counts.get(k, 0)
            self.count_tb[k].Text = str(n) if self.views else u"–"
            self.count_tb[k].Foreground = brush("#FF5A36" if n and k in kinds else "#9CA3AF")
        total = sum(counts.get(k, 0) for k in kinds)
        self.big_count.Text = str(total)
        if not self.views:
            self.scope_info.Text = u"Chọn sheet hoặc view ở Project Browser, hoặc bấm Chọn sheet… / Chọn view…"
            self.summary.Text = u"Chưa có phạm vi"
            self.summary_sub.Text = u"Chỉ xét mặt bằng, mặt bằng trần, mặt cắt, mặt đứng."
        else:
            self.scope_info.Text = self.scope_text
            if not kinds:
                self.summary.Text = u"Bật ít nhất một loại đối tượng"
                self.summary_sub.Text = u""
            elif total:
                self.summary.Text = u"đối tượng chưa tag"
                self.summary_sub.Text = u"trong {0} view · {1}".format(
                    len(self.views), kinds_line(counts, kinds))
            else:
                self.summary.Text = u"Tất cả đã có tag"
                self.summary_sub.Text = u"Không còn gì để tô trong {0} view.".format(len(self.views))
        self.big_count.Foreground = brush("#FF5A36" if total else "#22A06B")
        self.apply_btn.IsEnabled = total > 0
        self.apply_text.Text = u"Tô {0} đối tượng".format(total) if total else u"Tô vùng"
        marked = um.marked_count(doc, self.views) if self.views else 0
        self.clear_btn.IsEnabled = marked > 0
        self.clear_text.Text = u"Xoá vùng tô ({0})".format(marked) if marked else u"Xoá vùng tô"

    # ---- actions --------------------------------------------------------

    def on_clear(self, sender, args):
        try:
            um.remove(doc, self.views)
        except Exception as ex:
            forms.alert(u"Không xoá được vùng tô:\n{0}".format(ex))
        self.refresh()

    def on_back(self, sender, args):
        self.result_panel.Visibility = Visibility.Collapsed
        self.setup_panel.Visibility = Visibility.Visible
        self.head_title.Text = u"Đối tượng chưa tag"
        self.head_sub.Text = u"Tìm và tô cam những gì còn thiếu tag trong view"
        self.refresh()

    def on_apply(self, sender, args):
        kinds = self.kinds()
        for k in um.KINDS:
            setattr(cfg, "kind_" + k, bool(self.chip[k].IsChecked))
        script.save_config()
        Mouse.OverrideCursor = Cursors.Wait
        try:
            report, problems = um.highlight_views(doc, self.views, kinds, self.found)
        except Exception as ex:
            Mouse.OverrideCursor = None
            forms.alert(u"Không tạo được vùng tô:\n{0}".format(ex))
            return
        finally:
            Mouse.OverrideCursor = None
        self.show_result(report, problems, kinds)

    def show_result(self, report, problems, kinds):
        total = sum(sum(c.values()) for _, c in report)
        self.head_title.Text = u"Đã tô {0} đối tượng".format(total)
        self.head_sub.Text = u"trong {0} view · khi xong, bấm Xoá vùng tô".format(
            len(report))
        self.result_list.Children.Clear()
        clean = 0
        for view, counts in report:
            elems = [e for k in kinds for e in self.found.get(view.Id.IntegerValue, {}).get(k, [])]
            if not elems:
                clean += 1
                continue
            self.result_list.Children.Add(self._row(view, kinds, elems))
        if clean:
            self.result_list.Children.Add(text(
                u"{0} view còn lại đã đủ tag.".format(clean), size=12, color="#22A06B",
                margin=(4, 4, 0, 0)))
        if problems:
            self.notes.Text = u"Lưu ý:\n" + u"\n".join(problems[:8]) + (
                u"\n… và {0} lưu ý nữa".format(len(problems) - 8) if len(problems) > 8 else u"")
            self.notes_box.Visibility = Visibility.Visible
        else:
            self.notes_box.Visibility = Visibility.Collapsed
        self.setup_panel.Visibility = Visibility.Collapsed
        self.result_panel.Visibility = Visibility.Visible

    def _row(self, view, kinds, elems):
        counts = {}
        for k in kinds:
            counts[k] = len(self.found[view.Id.IntegerValue].get(k, []))
        b = Button()
        b.Style = self.FindResource("Row")
        g = Grid()
        for w in (GridLength(1, GridUnitType.Star), GridLength.Auto):
            cd = ColumnDefinition()
            cd.Width = w
            g.ColumnDefinitions.Add(cd)
        sp = StackPanel()
        sp.Children.Add(text(view.Name, bold=True))
        sp.Children.Add(text(kinds_line(counts, kinds), size=12, color="#8A8F98", margin=(0, 2, 0, 0)))
        g.Children.Add(sp)
        arrow = text(u"", size=12, color="#B0B4BB")
        arrow.FontFamily = self.FindResource("Icons")
        Grid.SetColumn(arrow, 1)
        g.Children.Add(arrow)
        b.Content = g
        b.ToolTip = u"Mở view và chọn sẵn {0} đối tượng chưa tag".format(len(elems))
        ids = [e.Id for e in elems]

        def go(s, e):
            self.open_target = (view, ids)
            self.Close()
        b.Click += go
        return b


win = UntaggedWindow()
win.ShowDialog()
if win.open_target:
    view, ids = win.open_target
    try:
        uidoc.ActiveView = view
        uidoc.Selection.SetElementIds(List[ElementId](ids))
    except Exception as ex:
        forms.alert(u"Không mở được view {0}:\n{1}".format(view.Name, ex))
