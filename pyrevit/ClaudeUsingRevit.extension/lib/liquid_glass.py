# -*- coding: utf-8 -*-
"""Liquid Glass look for every window of the extension: one theme, one base window.

- liquid_glass.xaml : the theme (glass panel, tiles, capsules, segmented control, fields).
- GlassWindow       : forms.WPFWindow + the theme + Windows' own blur. A window's XAML puts
                      its content in <ContentControl x:Name="glass" Style="{StaticResource GlassPanel}">.
- alert / choose / Progress : glass versions of forms.alert, SelectFromList /
                      CommandSwitchWindow and forms.ProgressBar, same call shapes and results.

The blur is Windows 11's Acrylic backdrop (DWM), so it is live, follows a drag and costs the window nothing.
It only works on a window that is not layered: no AllowsTransparency, a frame kept for DWM (corners, shadow) and
hidden by WindowChrome. Without it (older Windows) the body is plain frosted white.
"""
import os

import ctypes

from pyrevit import forms, revit, script  # first: loads the WPF assemblies
from pyrevit import HOST_APP

from System import Action, Uri
from System.Windows import (CornerRadius, HorizontalAlignment, ResourceDictionary, TextWrapping, Thickness, Visibility,
                            WindowStyle)
from System.Windows.Controls import Button, TextBlock
from System.Windows.Input import Mouse, MouseButtonState
from System.Windows.Interop import HwndSource, WindowInteropHelper
from System.Windows.Media import Colors
from System.Windows.Shell import WindowChrome
from System.Windows.Threading import DispatcherPriority

HERE = os.path.dirname(os.path.abspath(__file__))
THEME = os.path.join(HERE, "liquid_glass.xaml")
# DwmSetWindowAttribute
DWMWA_USE_IMMERSIVE_DARK_MODE = 20
DWMWA_WINDOW_CORNER_PREFERENCE = 33
DWMWA_SYSTEMBACKDROP_TYPE = 38
DWMWCP_ROUND = 2
DWMSBT_TRANSIENTWINDOW = 3      # Acrylic
GWL_STYLE = -16
WS_SYSMENU = 0x00080000         # DWM draws its own close button on the frame while this is on

ICON_INFO, ICON_WARN, ICON_PDF, ICON_LIST = u"", u"", u"", u""


def _dwm_set(hwnd, attr, value):
    v = ctypes.c_int(value)
    return ctypes.windll.dwmapi.DwmSetWindowAttribute(ctypes.c_void_p(hwnd), attr, ctypes.byref(v), 4)


def acrylic(hwnd):
    """Windows' own blur behind this window; False where Windows has none (older than 11 22H2)."""
    try:
        user32 = ctypes.windll.user32
        user32.SetWindowLongW(ctypes.c_void_p(hwnd), GWL_STYLE,
                              user32.GetWindowLongW(ctypes.c_void_p(hwnd), GWL_STYLE) & ~WS_SYSMENU)
        _dwm_set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 0)  # light: the theme's ink is dark
        _dwm_set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)
        return _dwm_set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW) == 0
    except Exception:
        return False


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
        self.WindowStyle = WindowStyle.SingleBorderWindow  # DWM blurs, rounds and shades a framed window
        chrome = WindowChrome()
        chrome.CaptionHeight = 0
        chrome.ResizeBorderThickness = Thickness(0)
        chrome.CornerRadius = CornerRadius(0)
        chrome.GlassFrameThickness = Thickness(-1)
        chrome.UseAeroCaptionButtons = False
        WindowChrome.SetWindowChrome(self, chrome)
        self.SourceInitialized += self._backdrop_on
        self.MouseLeftButtonDown += self.drag

    def _backdrop_on(self, sender, args):
        hwnd = WindowInteropHelper(self).Handle
        self.backdrop = acrylic(hwnd.ToInt64())
        if self.backdrop:
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent
        else:
            self.Background = self.FindResource("GlassBase")

    def drag(self, sender, args):
        """Move the window. A header that moved it already comes here with the button up."""
        if Mouse.LeftButton == MouseButtonState.Pressed:
            self.DragMove()


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
