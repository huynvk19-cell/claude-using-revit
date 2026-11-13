# -*- coding: utf-8 -*-
"""Windows for the tools (what they used of pyRevit's forms module).

WPFWindow(xaml, literal_string=False, handle_esc=True)  a Window loaded from XAML; x:Name elements become
                                                        attributes, Click="handler" calls self.handler
alert(msg, title)                                       a message box (Revit TaskDialog)
toast(msg, title)                                       a short note at the lower right of Revit, closes itself
select_views(title, button_name, filterfunc, multiple)  pick views: the shared pick list (lib/pick_list.py)
select_sheets(title, button_name, multiple)             pick sheets: the same list as Xuất PDF / CAD
select_from_list(items, title, name_attr)               pick any other items (checkbox list with a filter box)
pick_folder(title)                                      a folder path or None
"""
import os

import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')
clr.AddReference('System.Xaml')
import wpf  # noqa: E402  (IronPython.Wpf, loaded by the add-in)

from System import TimeSpan  # noqa: E402
from System.IO import StringReader  # noqa: E402
from System.Windows import (CornerRadius, FontWeights, SizeToContent, Thickness, TextWrapping,  # noqa: E402
                            Visibility, Window, WindowStyle)
from System.Windows.Controls import Border, CheckBox, StackPanel, TextBlock  # noqa: E402
from System.Windows.Input import Key  # noqa: E402
from System.Windows.Interop import WindowInteropHelper  # noqa: E402
from System.Windows.Media import Color, FontFamily, SolidColorBrush  # noqa: E402
from System.Windows.Threading import DispatcherTimer  # noqa: E402

from cur import HOST_APP  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


class WPFWindow(Window):
    """A WPF window from a XAML file (or text with literal_string=True), owned by Revit's main window."""

    def __init__(self, xaml_source, literal_string=False, handle_esc=True, set_owner=True):
        if set_owner:
            try:
                WindowInteropHelper(self).Owner = HOST_APP.proc_window
            except Exception:
                pass
        if literal_string:
            wpf.LoadComponent(self, StringReader(xaml_source))
        else:
            wpf.LoadComponent(self, xaml_source)
        if handle_esc:
            self.PreviewKeyDown += self._cur_esc

    def _cur_esc(self, sender, args):
        if args.Key == Key.Escape:
            self.Close()

    def show(self, modal=False):
        if modal:
            return self.ShowDialog()
        self.Show()

    def show_dialog(self):
        return self.ShowDialog()

    def hide(self):
        self.Hide()

    @staticmethod
    def hide_element(*elements):
        for e in elements:
            e.Visibility = Visibility.Collapsed

    @staticmethod
    def show_element(*elements):
        for e in elements:
            e.Visibility = Visibility.Visible


def alert(msg, title=None, sub_msg=None, exitscript=False, **kwargs):
    from Autodesk.Revit.UI import TaskDialog
    td = TaskDialog(title or u"claude using revit")
    td.MainInstruction = msg
    if sub_msg:
        td.MainContent = sub_msg
    td.Show()
    if exitscript:
        import sys
        sys.exit()
    return True


# ---- toast ------------------------------------------------------------------------------

def toast(message, title=u"", appid=None, click=None, actions=None, seconds=5):
    """A small dark note at the lower right of Revit's window; closes itself after a few seconds."""
    w = Window()
    w.WindowStyle = getattr(WindowStyle, 'None')      # 'None' is a Python keyword
    w.AllowsTransparency = True
    w.Background = SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
    w.ShowInTaskbar = False
    w.ShowActivated = False
    w.Topmost = True
    w.SizeToContent = SizeToContent.WidthAndHeight
    try:
        WindowInteropHelper(w).Owner = HOST_APP.proc_window
    except Exception:
        pass
    box = Border()
    box.CornerRadius = CornerRadius(12)
    box.Padding = Thickness(16, 12, 18, 14)
    box.Background = SolidColorBrush(Color.FromArgb(235, 28, 34, 44))
    stack = StackPanel()
    stack.MaxWidth = 380
    if title:
        t = TextBlock()
        t.Text = title
        t.FontWeight = FontWeights.SemiBold
        t.Foreground = SolidColorBrush(Color.FromRgb(255, 255, 255))
        t.Margin = Thickness(0, 0, 0, 4)
        stack.Children.Add(t)
    m = TextBlock()
    m.Text = message
    m.TextWrapping = TextWrapping.Wrap
    m.Foreground = SolidColorBrush(Color.FromRgb(225, 230, 238))
    stack.Children.Add(m)
    box.Child = stack
    w.Content = box
    w.FontFamily = FontFamily(u"Segoe UI")
    w.FontSize = 13

    def place(sender, args):
        try:
            r = HOST_APP.uiapp.MainWindowExtents
            s = 1.0 / HOST_APP.proc_screen_scalefactor
            w.Left = r.Right * s - w.ActualWidth - 28
            w.Top = r.Bottom * s - w.ActualHeight - 48
        except Exception:
            pass
    w.Loaded += place
    w.MouseLeftButtonDown += lambda s, e: w.Close()
    timer = DispatcherTimer()
    timer.Interval = TimeSpan.FromSeconds(seconds)

    def done(sender, args):
        timer.Stop()
        w.Close()
    timer.Tick += done
    w.Show()
    timer.Start()
    return w


# ---- pick from a list ---------------------------------------------------------------------

def select_from_list(items, title=u"Chọn", button_name=u"Chọn", multiple=True, name_attr=None, checked=None):
    """items: objects; name_attr: a function item -> label (default unicode(item)). Returns the picked items
    (a list; one item or None when multiple=False), None when closed."""
    import liquid_glass as lg
    label = name_attr or (lambda x: u"{0}".format(x))

    class _Pick(lg.GlassWindow):
        def __init__(self):
            lg.GlassWindow.__init__(self, os.path.join(HERE, "select_list.xaml"))
            self.result = None
            self.header.MouseLeftButtonDown += self.drag
            self.close_btn.Click += lambda s, e: self.Close()
            self.cancel_btn.Click += lambda s, e: self.Close()
            self.ok_btn.Content = button_name
            self.ok_btn.Click += self._ok
            self.icon.Content = lg.ICON_LIST
            self.head_title.Text = title
            self.boxes = []
            for it in items:
                cb = CheckBox()
                cb.Content = label(it)
                cb.Tag = it
                cb.Margin = Thickness(0, 3, 0, 3)
                cb.IsChecked = bool(checked and it in checked)
                cb.Checked += self._changed
                cb.Unchecked += self._changed
                self.rows.Children.Add(cb)
                self.boxes.append(cb)
            self.search.TextChanged += self._filter
            self.all_btn.Click += lambda s, e: self._set_all(True)
            self.none_btn.Click += lambda s, e: self._set_all(False)
            if not multiple:
                self.all_btn.Visibility = self.none_btn.Visibility = Visibility.Collapsed
            self._changed(None, None)
            self.Loaded += lambda s, e: self.search.Focus()

        def _visible(self):
            return [b for b in self.boxes if b.Visibility == Visibility.Visible]

        def _set_all(self, on):
            for b in self._visible():
                b.IsChecked = on

        def _filter(self, sender, args):
            q = (self.search.Text or u"").strip().lower()
            for b in self.boxes:
                b.Visibility = Visibility.Visible if (not q or q in (b.Content or u"").lower()) else Visibility.Collapsed
            self._changed(None, None)

        def _changed(self, sender, args):
            if not multiple and sender is not None and sender.IsChecked:
                for b in self.boxes:
                    if b is not sender:
                        b.IsChecked = False
            n = len([b for b in self.boxes if b.IsChecked])
            self.count.Text = u"{0} / {1} đã chọn".format(n, len(self.boxes))
            self.ok_btn.IsEnabled = n > 0

        def _ok(self, sender, args):
            self.result = [b.Tag for b in self.boxes if b.IsChecked]
            self.Close()

    d = _Pick()
    d.ShowDialog()
    if d.result is None:
        return None
    if not multiple:
        return d.result[0] if d.result else None
    return d.result


def select_views(title=u"Chọn view", button_name=u"Chọn", width=None, multiple=True, filterfunc=None,
                 doc=None, use_selection=False, checked=None):
    """Pick views in the shared pick list (lib/pick_list.py): folding groups, pick by set, several
    names at once, drag / Shift+click. Returns the views (one view when multiple=False), None when closed."""
    import pick_list
    from cur import revit
    if use_selection and checked is None:
        checked = list(revit.uidoc.Selection.GetElementIds())
    return pick_list.pick_views(doc, title, button_name, multiple, filterfunc, checked)


def select_sheets(title=u"Chọn sheet", button_name=u"Chọn", width=None, multiple=True, filterfunc=None,
                  doc=None, include_placeholder=True, use_selection=False, checked=None):
    """Pick sheets in the shared pick list (lib/pick_list.py), as in Xuất PDF / CAD. Returns the sheets
    (one sheet when multiple=False), None when closed."""
    import pick_list
    from cur import revit
    if use_selection and checked is None:
        checked = list(revit.uidoc.Selection.GetElementIds())
    return pick_list.pick_sheets(doc, title, button_name, multiple, filterfunc, include_placeholder, checked)


def pick_folder(title=None, owner=None):
    clr.AddReference('System.Windows.Forms')
    from System.Windows.Forms import DialogResult, FolderBrowserDialog
    dlg = FolderBrowserDialog()
    if title:
        dlg.Description = title
    try:
        dlg.UseDescriptionForTitle = True      # .NET 8 (Revit 2025+)
    except Exception:
        pass
    dlg.ShowNewFolderButton = True
    if dlg.ShowDialog() == DialogResult.OK:
        return dlg.SelectedPath
    return None
