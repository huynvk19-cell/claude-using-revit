# -*- coding: utf-8 -*-
"""Windows' own blur for a WPF window: Windows 11's Acrylic backdrop (DWM).

The blur is live, follows a drag and costs the window nothing. It only works on a top-level window that is not
layered: no AllowsTransparency, a frame kept for DWM (corners, shadow) and hidden by WindowChrome. A docked pane sits
inside Revit's own window, so it cannot have it. Where Windows has none (older than 11 22H2) the window stays opaque.
No pyRevit here: the startup engine and the button scripts both use it.
"""
import ctypes

import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')

from System.Windows import CornerRadius, Thickness, WindowStyle
from System.Windows.Input import Mouse, MouseButtonState
from System.Windows.Interop import HwndSource, WindowInteropHelper
from System.Windows.Media import Colors
from System.Windows.Shell import WindowChrome

# DwmSetWindowAttribute
DWMWA_USE_IMMERSIVE_DARK_MODE = 20
DWMWA_WINDOW_CORNER_PREFERENCE = 33
DWMWA_SYSTEMBACKDROP_TYPE = 38
DWMWCP_ROUND = 2
DWMSBT_TRANSIENTWINDOW = 3      # Acrylic
GWL_STYLE = -16
WS_SYSMENU = 0x00080000         # DWM draws its own close button on the frame while this is on


def _dwm_set(hwnd, attr, value):
    v = ctypes.c_int(value)
    return ctypes.windll.dwmapi.DwmSetWindowAttribute(ctypes.c_void_p(hwnd), attr, ctypes.byref(v), 4)


def acrylic(hwnd):
    """Windows' own blur behind this window; False where Windows has none."""
    try:
        user32 = ctypes.windll.user32
        user32.SetWindowLongW(ctypes.c_void_p(hwnd), GWL_STYLE,
                              user32.GetWindowLongW(ctypes.c_void_p(hwnd), GWL_STYLE) & ~WS_SYSMENU)
        _dwm_set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 0)  # light: the theme's ink is dark
        _dwm_set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)
        return _dwm_set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW) == 0
    except Exception:
        return False


def drag(window):
    """Move the window, if the button is still down: a header that moved it already comes here with it up."""
    if Mouse.LeftButton == MouseButtonState.Pressed:
        window.DragMove()


def apply(window, done=None):
    """Call before the window shows. The theme's GlassBase is the plain body where there is no blur. done(has blur)."""
    window.WindowStyle = WindowStyle.SingleBorderWindow  # DWM blurs, rounds and shades a framed window
    chrome = WindowChrome()
    chrome.CaptionHeight = 0
    chrome.ResizeBorderThickness = Thickness(0)
    chrome.CornerRadius = CornerRadius(0)
    chrome.GlassFrameThickness = Thickness(-1)
    chrome.UseAeroCaptionButtons = False
    WindowChrome.SetWindowChrome(window, chrome)

    def on(sender, args):
        hwnd = WindowInteropHelper(window).Handle
        ok = acrylic(hwnd.ToInt64())
        if ok:
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent
        else:
            window.Background = window.FindResource('GlassBase')
        if done is not None:
            done(ok)
    window.SourceInitialized += on
    window.MouseLeftButtonDown += lambda s, e: drag(window)
