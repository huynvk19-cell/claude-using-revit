# -*- coding: utf-8 -*-
"""Windows' own blur for a WPF window: Windows 11's Acrylic backdrop (DWM).

The blur is live, follows a drag and costs the window nothing. It only works on a top-level window that is not
layered: no AllowsTransparency, a frame kept for DWM (corners, shadow) and hidden by WindowChrome. Where Windows has
none (older than 11 22H2) the window stays opaque.
Plain IronPython + WPF: the startup engine and the button scripts both use it.
"""
import ctypes
import os

import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')

from System import Uri
from System.Windows import CornerRadius, PresentationSource, Thickness, WindowStyle
from System.Windows.Input import Mouse, MouseButtonState
from System.Windows.Interop import HwndSource, WindowInteropHelper
from System.Windows.Media import Brushes, Colors
from System.Windows.Media.Imaging import BitmapCacheOption, BitmapImage
from System.Windows.Shell import WindowChrome

import screen_fit

PANEL_ICON = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), u'claude using revit.tab', u'Panel.panel',
                          u'Open Panel.pushbutton', u'icon.png')

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


class _Accent(ctypes.Structure):
    _fields_ = [('state', ctypes.c_int), ('flags', ctypes.c_int), ('color', ctypes.c_uint), ('animation', ctypes.c_int)]


class _CompositionData(ctypes.Structure):
    _fields_ = [('attribute', ctypes.c_int), ('data', ctypes.c_void_p), ('size', ctypes.c_size_t)]


class _Margins(ctypes.Structure):
    _fields_ = [('left', ctypes.c_int), ('right', ctypes.c_int), ('top', ctypes.c_int), ('bottom', ctypes.c_int)]


ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
WCA_ACCENT_POLICY = 19
POPUP_TINT = 0xB8F4F2F0         # AABBGGRR: light, like the window's body


def blur_popup(hwnd):
    """Blur behind a window that never takes focus, as the list of a combo box. False where it cannot."""
    try:
        accent = _Accent(ACCENT_ENABLE_ACRYLICBLURBEHIND, 0, POPUP_TINT, 0)
        data = _CompositionData(WCA_ACCENT_POLICY, ctypes.cast(ctypes.pointer(accent), ctypes.c_void_p), ctypes.sizeof(accent))
        ok = ctypes.windll.user32.SetWindowCompositionAttribute(ctypes.c_void_p(hwnd), ctypes.byref(data))
        _dwm_set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)
        return bool(ok)
    except Exception:
        return False


def combo(box):
    """The drop-down list of this combo box (theme template, PART_Popup) on the blur."""
    def opened(sender, args):
        popup = sender
        src = PresentationSource.FromVisual(popup.Child)
        if src is None:
            return
        if blur_popup(src.Handle.ToInt64()):
            src.CompositionTarget.BackgroundColor = Colors.Transparent
            popup.Child.Background = Brushes.Transparent

    def hook(sender, args):
        popup = box.Template.FindName('PART_Popup', box) if box.Template is not None else None
        if popup is not None:
            popup.Opened += opened
    box.Loaded += hook


def tool_icon(element, path=None):
    """Give this element's header icon the picture of the ribbon button: path, or the button of the Bảng window."""
    if not path or not os.path.isfile(path):
        path = PANEL_ICON
    image = BitmapImage()
    image.BeginInit()
    image.CacheOption = BitmapCacheOption.OnLoad
    image.UriSource = Uri(path)
    image.EndInit()
    image.Freeze()
    element.Resources['ToolIcon'] = image


def drag(window):
    """Move the window, if the button is still down: a header that moved it already comes here with it up."""
    if Mouse.LeftButton == MouseButtonState.Pressed:
        window.DragMove()


def apply(window, done=None, resizable=False, body=None):
    """Call before the window shows. The theme's GlassBase is the plain body where there is no blur. done(has blur).
    A window that is not resizable also follows its screen (screen_fit.py); body: the ScrollViewer of its middle."""
    window.WindowStyle = WindowStyle.SingleBorderWindow  # DWM blurs, rounds and shades a framed window
    chrome = WindowChrome()
    chrome.CaptionHeight = 0
    chrome.ResizeBorderThickness = Thickness(6 if resizable else 0)
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
    if not resizable:
        screen_fit.fit(window, body)
