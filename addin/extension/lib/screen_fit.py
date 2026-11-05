# -*- coding: utf-8 -*-
"""Every tool window in proportion to its screen, on any resolution.

The sizes in the windows' XAML were drawn for a 1080p screen. Here they become shares of the screen the window is on:
wider and taller lists on a big screen, never past the work area on a small one. The header and the action buttons
stay put; only the middle gives way: first its lists shrink, then it scrolls.

A window takes part with a ScrollViewer around its middle, between the header row and the buttons row (Height="*"):
    <ScrollViewer x:Name="body" Grid.Row="1" VerticalScrollBarVisibility="Auto" Margin="24,4,0,0" Padding="0,0,24,0">
A list inside it (ScrollViewer or ListBox with a MaxHeight or Height) is what gives way first.

native_blur.apply does this for every window, so a new tool only needs that XAML.
Plain IronPython + WPF: the startup engine and the button scripts both use it.
"""
import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')
clr.AddReference('System.Drawing')
clr.AddReference('System.Windows.Forms')

from System import Double, IntPtr
from System.Drawing import Rectangle
from System.Windows.Controls import ListBox, ScrollViewer
from System.Windows.Forms import Screen
from System.Windows.Interop import HwndSource, WindowInteropHelper
from System.Windows.Media import Visual, VisualTreeHelper

REF_WIDTH, REF_HEIGHT = 1920.0, 1040.0      # the work area the XAML sizes were drawn for: 1080p with the taskbar
WIDTH_MAX = 1.6                             # a window's width follows a wider screen, this far; narrower only if it must
MARGIN = 8                                  # kept free above and below / left and right of a window
LIST_MIN = 110                              # a list shrinks to this, then the middle scrolls


def work_area(window, opening=False, box=None):
    """(left, top, right, bottom) of the usable part of the window's screen, in WPF units.
    opening: it is not on its screen yet, it will open on its owner's, or where its box (left, top, width, height) says."""
    helper = WindowInteropHelper(window)
    hwnd = helper.Handle
    source = HwndSource.FromHwnd(hwnd).CompositionTarget
    if box:
        d = source.TransformToDevice
        screen = Screen.FromRectangle(Rectangle(int(box[0] * d.M11), int(box[1] * d.M22), int(box[2] * d.M11), int(box[3] * d.M22)))
    elif opening and helper.Owner != IntPtr.Zero:
        screen = Screen.FromHandle(helper.Owner)
    else:
        screen = Screen.FromHandle(hwnd)
    area = screen.WorkingArea
    m = source.TransformFromDevice
    return area.Left * m.M11, area.Top * m.M22, area.Right * m.M11, area.Bottom * m.M22


def _lists(element, found):
    """The lists that give way, inside element."""
    for i in range(VisualTreeHelper.GetChildrenCount(element)):
        child = VisualTreeHelper.GetChild(element, i)
        if isinstance(child, (ScrollViewer, ListBox)) and (_sized(child.MaxHeight) or _sized(child.Height)):
            found.append(child)
        else:
            _lists(child, found)
    return found


def _sized(value):
    return not Double.IsNaN(value) and not Double.IsInfinity(value)


def share_out(wants, room):
    """How high each list may be. wants = [(its content, its limit)]; a list that needs little keeps what it needs and
    leaves the rest to the others, which split it in proportion to their limits, down to LIST_MIN."""
    give = [limit for _, limit in wants]
    open_ = list(range(len(wants)))
    while open_:
        total = float(sum(wants[i][1] for i in open_))
        small = [i for i in open_ if wants[i][0] <= room * wants[i][1] / total]
        if not small:
            for i in open_:
                give[i] = max(LIST_MIN, min(wants[i][1], room * wants[i][1] / total))
            break
        for i in small:
            room -= wants[i][0]
            open_.remove(i)
    return give


class _Fit(object):
    def __init__(self, window, body, scale_width):
        self.w = window
        self.body = body
        self.scale_width = scale_width
        self.lists = None       # [(the list, its property, its height in the XAML)]
        self.busy = False
        window.SourceInitialized += self.opening
        window.Loaded += self.loaded
        window.SizeChanged += self.refit
        if body is not None:
            body.ScrollChanged += self.scrolled
            window.PreviewMouseWheel += self.wheel

    def opening(self, sender, args):
        """Before the first layout: the window is born at its size, without a jump."""
        left, top, right, bottom = work_area(self.w, opening=True)
        self.w.MaxWidth = right - left - 2 * MARGIN
        self.w.MaxHeight = bottom - top - 2 * MARGIN
        if self.scale_width and _sized(self.w.Width):
            k = max(1.0, min(WIDTH_MAX, (right - left) / REF_WIDTH))
            self.w.Width = min(self.w.MaxWidth, self.w.Width * k)

    def loaded(self, sender, args):
        if self.body is not None:
            self.lists = []
            for e in _lists(self.body, []):
                prop = 'Height' if _sized(e.Height) else 'MaxHeight'
                self.lists.append((e, prop, getattr(e, prop)))
        self.refit()

    def scrolled(self, sender, args):
        if args.ExtentHeightChange or args.ViewportHeightChange:      # not on a plain scroll: nothing changed size
            self.refit()

    def refit(self, sender=None, args=None):
        if self.busy or self.lists is None and self.body is not None or not self.w.IsLoaded:
            return
        self.busy = True
        try:
            left, top, right, bottom = work_area(self.w)
            self.w.MaxWidth = right - left - 2 * MARGIN
            self.w.MaxHeight = bottom - top - 2 * MARGIN
            if self.body is not None:
                self._share(bottom - top)
            self._on_screen(left, top, right, bottom)
        finally:
            self.busy = False

    def _share(self, screen):
        lists = [item for item in self.lists if item[0].IsVisible]
        if not lists:
            return
        k = screen / REF_HEIGHT
        others = self.w.ActualHeight - self.body.ViewportHeight + self.body.ExtentHeight \
            - sum(e.ActualHeight for e, _, _ in lists)
        wants = []
        for e, prop, drawn in lists:
            limit = drawn * k
            wants.append((e.ExtentHeight if prop == 'MaxHeight' and isinstance(e, ScrollViewer) else limit, limit))
        for (e, prop, _), height in zip(lists, share_out(wants, max(0, self.w.MaxHeight - others))):
            if abs(getattr(e, prop) - height) > 1:
                setattr(e, prop, height)

    def _on_screen(self, left, top, right, bottom):
        w = self.w
        for pos, size, low, high, name in ((w.Top, w.ActualHeight, top, bottom, 'Top'), (w.Left, w.ActualWidth, left, right, 'Left')):
            if not _sized(pos):
                continue
            moved = max(low + MARGIN, min(pos, high - MARGIN - size))
            if abs(moved - pos) > 0.5:
                setattr(w, name, moved)

    def wheel(self, sender, args):
        """The wheel over a list that has reached its end goes on to the middle: at low screens the list may fill it."""
        e = args.OriginalSource
        inner = None
        while e is not None and e is not self.body and isinstance(e, Visual):
            if isinstance(e, ScrollViewer) and e.ScrollableHeight > 0:
                inner = inner or e
            e = VisualTreeHelper.GetParent(e)
        if e is not self.body or inner is None or self.body.ScrollableHeight <= 0:
            return
        at_end = inner.VerticalOffset <= 0 if args.Delta > 0 else inner.VerticalOffset >= inner.ScrollableHeight
        if at_end:
            self.body.ScrollToVerticalOffset(self.body.VerticalOffset - args.Delta / 3.0)
            args.Handled = True


def fit(window, body=None, scale_width=True):
    """Call before the window shows. body: the ScrollViewer of its middle."""
    return _Fit(window, body, scale_width)


def resizable(window, box=None, share=(0.52, 0.65)):
    """A window the user sizes: opens at this share of the screen, or at its last box (left, top, width, height)
    brought back onto the screen. Call again when it shows after a while: the screen may have changed."""
    def place(sender=None, args=None):
        opening = sender is not None
        left, top, right, bottom = work_area(window, opening, box if opening else None)
        full_w, full_h = right - left - 2 * MARGIN, bottom - top - 2 * MARGIN
        window.MinWidth = min(window.MinWidth, full_w)
        window.MinHeight = min(window.MinHeight, full_h)
        if opening and box:
            window.Left, window.Top, window.Width, window.Height = box
        elif opening:
            window.Width = max(window.MinWidth, (right - left) * share[0])
            window.Height = max(window.MinHeight, (bottom - top) * share[1])
        window.Width = min(window.Width, full_w)
        window.Height = min(window.Height, full_h)
        if box or not opening:
            window.Left = max(left + MARGIN, min(window.Left, right - MARGIN - window.Width))
            window.Top = max(top + MARGIN, min(window.Top, bottom - MARGIN - window.Height))
    window.SourceInitialized += place
    return place
