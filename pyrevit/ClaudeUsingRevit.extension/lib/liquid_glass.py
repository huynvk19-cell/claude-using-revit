# -*- coding: utf-8 -*-
"""Liquid Glass look for every window of the extension: one theme, one base window.

- liquid_glass.xaml : the theme (glass panel, tiles, capsules, segmented control, fields).
- GlassWindow       : forms.WPFWindow + the theme + Windows' own blur. A window's XAML puts
                      its content in <ContentControl x:Name="glass" Style="{StaticResource GlassPanel}">.
- alert / choose / Progress : glass versions of forms.alert, SelectFromList /
                      CommandSwitchWindow and forms.ProgressBar, same call shapes and results.

The blur is Windows' own (native_blur.py): live, follows a drag, costs the window nothing.
"""
import os

from pyrevit import forms, revit, script  # first: loads the WPF assemblies
from pyrevit import HOST_APP

from System import Action, Uri
from System.Windows import HorizontalAlignment, ResourceDictionary, TextWrapping, Thickness, Visibility
from System.Windows.Controls import Button, TextBlock
from System.Windows.Threading import DispatcherPriority

import native_blur

HERE = os.path.dirname(os.path.abspath(__file__))
THEME = os.path.join(HERE, "liquid_glass.xaml")
ICON_INFO, ICON_WARN, ICON_PDF, ICON_LIST = u"", u"", u"", u""


def load_theme(resources):
    theme = ResourceDictionary()
    theme.Source = Uri(THEME)
    resources.MergedDictionaries.Add(theme)


class GlassWindow(forms.WPFWindow):
    """forms.WPFWindow with the Liquid Glass theme on Windows' own blur; drag it by any empty part."""

    def __init__(self, xaml_source, literal_string=False, handle_esc=True):
        load_theme(self.Resources)  # before the XAML: its StaticResources need the theme
        self.backdrop = False
        forms.WPFWindow.__init__(self, xaml_source, literal_string=literal_string, handle_esc=handle_esc)
        native_blur.apply(self, self._backdrop_set)

    def _backdrop_set(self, ok):
        self.backdrop = ok

    def drag(self, sender, args):
        """Header MouseLeftButtonDown handler: move the window."""
        native_blur.drag(self)


# ---- dialogs ---------------------------------------------------------------

DIALOG = os.path.join(HERE, "liquid_glass_dialog.xaml")
PROGRESS = os.path.join(HERE, "liquid_glass_progress.xaml")


def _text(s):
    tb = TextBlock()
    tb.Text = s
    tb.TextWrapping = TextWrapping.Wrap
    return tb


class _Dialog(GlassWindow):
    def __init__(self, title, message, icon):
        GlassWindow.__init__(self, DIALOG)
        self.result = None
        self.header.MouseLeftButtonDown += self.drag
        self.close_btn.Click += lambda s, e: self.Close()
        self.icon.Content = icon
        self.head_title.Text = title or u"claude using revit"
        if message:
            self.message.Text = message
        else:
            self.message.Visibility = Visibility.Collapsed

    def add_button(self, label, value, primary=False):
        b = Button()
        b.Style = self.FindResource("Primary" if primary else "Secondary")
        b.Content = label
        b.Margin = Thickness(8, 0, 0, 0)
        b.Click += self._pick(value)
        self.buttons.Children.Add(b)
        return b

    def add_row(self, label, value):
        b = Button()
        b.Style = self.FindResource("Row")
        b.HorizontalContentAlignment = HorizontalAlignment.Left
        b.Content = _text(label)
        b.Click += self._pick(value)
        self.rows.Children.Add(b)
        self.rows_box.Visibility = Visibility.Visible
        return b

    def _pick(self, value):
        def handler(sender, args):
            self.result = value
            self.Close()
        return handler


def alert(msg, title=None, options=None, yes=False, no=False, exitscript=False, icon=None):
    """forms.alert look-alike: options -> the option picked (False when closed);
    yes/no -> True / False; plain -> True. exitscript: stop the script on OK / No / close."""
    warn = not options and not yes
    d = _Dialog(title, msg, icon or (ICON_WARN if warn and exitscript else ICON_INFO))
    if options:  # first option = primary, drawn last (rightmost)
        for o in list(options[1:]) + [options[0]]:
            d.add_button(o, o, primary=(o == options[0]))
    elif yes or no:
        d.add_button(u"Không", False)
        d.add_button(u"Có", True, primary=True)
    else:
        d.add_button(u"OK", True, primary=True)
    d.ShowDialog()
    res = d.result if d.result is not None else False
    if exitscript and (not res or not (options or yes or no)):
        script.exit()
    return res


def choose(items, title, message=None, icon=ICON_LIST):
    """One tap on a row picks it (SelectFromList single / CommandSwitchWindow); None when closed."""
    d = _Dialog(title, message, icon)
    for it in items:
        d.add_row(it, it)
    d.buttons_bar.Visibility = Visibility.Collapsed
    d.ShowDialog()
    return d.result


class Progress(GlassWindow):
    """forms.ProgressBar look-alike: a glass pill near the top of Revit.

    with Progress(title, cancellable=True) as pb:
        pb.title = u"..."; pb.update_progress(i, n); if pb.cancelled: ...
    """

    def __init__(self, title=u"", cancellable=False, icon=ICON_PDF):
        GlassWindow.__init__(self, PROGRESS, handle_esc=False)
        self.cancelled = False
        self.title = title
        self.icon.Content = icon
        self.label.Text = title
        if cancellable:
            self.cancel_btn.Click += self._cancel
        else:
            self.cancel_btn.Visibility = Visibility.Collapsed
        try:
            r = revit.ui.get_window_rectangle()
            s = 1.0 / HOST_APP.proc_screen_scalefactor
            self.Left = (r.Left + r.Right) / 2.0 * s - self.Width / 2.0
            self.Top = r.Top * s + 130
        except Exception:
            pass

    def _cancel(self, sender, args):
        self.cancelled = True
        self.cancel_text.Text = u"Đang dừng…"
        self.cancel_btn.IsEnabled = False

    def __enter__(self):
        self.Show()
        self._pump()
        return self

    def __exit__(self, exc_type, exc_value, tb):
        self.Close()

    def update_progress(self, value, max_value=1):
        self.bar.Maximum = max(max_value, 1)
        self.bar.Value = value
        self.label.Text = self.title
        self._pump()

    def _pump(self):
        # let WPF draw and take the Stop click (same trick as forms.ProgressBar)
        self.Dispatcher.Invoke(Action(lambda: None), DispatcherPriority.Background)
