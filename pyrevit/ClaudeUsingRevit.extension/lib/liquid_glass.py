# -*- coding: utf-8 -*-
"""Liquid Glass look for every window of the extension: one theme, one base window.

- liquid_glass.xaml : the theme (glass panel, tiles, capsules, segmented control, fields).
- GlassWindow       : forms.WPFWindow + the theme + the see-through body. A window's XAML puts
                      its content in <ContentControl x:Name="glass" Style="{StaticResource GlassPanel}">.
- alert / choose / Progress : glass versions of forms.alert, SelectFromList /
                      CommandSwitchWindow and forms.ProgressBar, same call shapes and results.

Why a snapshot and not real blur-behind: the DWM accent blur and the Windows 11 backdrop
both come out flat grey / milky with square corners for these layered WPF windows, so the
screen behind is captured once just before the window opens, shrunk, made more vivid and
blurred, then shown through the body (dragging pans it). Revit does not repaint under a
modal window, so it stays true. If the capture fails the body is plain frosted white.
"""
import os

import clr
clr.AddReference("System.Drawing")
clr.AddReference("System.Windows.Forms")

from pyrevit import forms, revit, script  # first: loads the WPF assemblies
from pyrevit import HOST_APP

from System import Action, Array, Single, Uri
from System.Drawing import Bitmap, Graphics, GraphicsUnit, Rectangle
from System.Drawing.Drawing2D import InterpolationMode
from System.Drawing.Imaging import ColorMatrix, ImageAttributes, ImageFormat
from System.IO import MemoryStream
from System.Windows import (HorizontalAlignment, Point, PresentationSource, Rect,
                            ResourceDictionary, Size, TextWrapping, Thickness, Visibility)
from System.Windows.Controls import Button, Image, StackPanel, TextBlock
from System.Windows.Forms import SystemInformation
from System.Windows.Media import BrushMappingMode, ImageBrush, PixelFormats, Stretch
from System.Windows.Media.Effects import BlurEffect, KernelType, RenderingBias
from System.Windows.Media.Imaging import BitmapCacheOption, BitmapImage, RenderTargetBitmap
from System.Windows.Threading import DispatcherPriority

HERE = os.path.dirname(os.path.abspath(__file__))
THEME = os.path.join(HERE, "liquid_glass.xaml")
SHRINK = 4        # snapshot kept at 1/4 size: cheaper blur, smoother look
BLUR = 9          # px at 1/4 size (about 36 px on screen)
VIVID = 1.45      # saturation of what shows through the glass

ICON_INFO, ICON_WARN, ICON_PDF, ICON_LIST = u"", u"", u"", u""


def _vivid_matrix(s=VIVID, lift=0.035):
    r, g, b = 0.3086 * (1 - s), 0.6094 * (1 - s), 0.0820 * (1 - s)
    rows = [[r + s, r, r, 0, 0],
            [g, g + s, g, 0, 0],
            [b, b, b + s, 0, 0],
            [0, 0, 0, 1, 0],
            [lift, lift, lift, 0, 1]]
    return ColorMatrix(Array[Array[Single]]([Array[Single](row) for row in rows]))


def snapshot():
    """(blurred BitmapSource of all screens at 1/SHRINK, virtual screen rect) or (None, None)."""
    try:
        vs = SystemInformation.VirtualScreen
        full = Bitmap(vs.Width, vs.Height)
        g = Graphics.FromImage(full)
        g.CopyFromScreen(vs.Left, vs.Top, 0, 0, full.Size)
        g.Dispose()
        w, h = max(1, vs.Width // SHRINK), max(1, vs.Height // SHRINK)
        small = Bitmap(w, h)
        g = Graphics.FromImage(small)
        g.InterpolationMode = InterpolationMode.HighQualityBilinear
        ia = ImageAttributes()
        ia.SetColorMatrix(_vivid_matrix())
        g.DrawImage(full, Rectangle(0, 0, w, h), 0, 0, vs.Width, vs.Height, GraphicsUnit.Pixel, ia)
        g.Dispose()
        full.Dispose()
        ms = MemoryStream()
        small.Save(ms, ImageFormat.Bmp)
        small.Dispose()
        ms.Position = 0
        src = BitmapImage()
        src.BeginInit()
        src.CacheOption = BitmapCacheOption.OnLoad
        src.StreamSource = ms
        src.EndInit()
        src.Freeze()
        img = Image()
        img.Source = src
        img.Stretch = Stretch.Fill
        fx = BlurEffect()
        fx.Radius = BLUR
        fx.KernelType = KernelType.Gaussian
        fx.RenderingBias = RenderingBias.Quality
        img.Effect = fx
        img.Measure(Size(w, h))
        img.Arrange(Rect(0, 0, w, h))
        out = RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32)
        out.Render(img)
        out.Freeze()
        return out, vs
    except Exception:
        return None, None


def load_theme(resources):
    theme = ResourceDictionary()
    theme.Source = Uri(THEME)
    resources.MergedDictionaries.Add(theme)


class GlassWindow(forms.WPFWindow):
    """forms.WPFWindow with the Liquid Glass theme and a see-through body named "glass"."""

    def __init__(self, xaml_source, literal_string=False, handle_esc=True):
        load_theme(self.Resources)  # before the XAML: its StaticResources need the theme
        self._shot, self._screen = snapshot()  # before the window shows: the screen behind it
        self._brush = self._back = None
        forms.WPFWindow.__init__(self, xaml_source, literal_string=literal_string, handle_esc=handle_esc)
        self.Loaded += self._glass_on
        self.LocationChanged += self._glass_pan
        self.SizeChanged += self._glass_pan

    def _glass_on(self, sender, args):
        panel = getattr(self, "glass", None)
        if panel is None or self._shot is None:
            return
        panel.ApplyTemplate()
        self._back = panel.Template.FindName("PART_Back", panel)
        if self._back is None:
            return
        self._brush = ImageBrush(self._shot)
        self._brush.ViewboxUnits = BrushMappingMode.Absolute
        self._brush.Stretch = Stretch.Fill
        self._back.Background = self._brush
        self._glass_pan(None, None)

    def _glass_pan(self, sender, args):
        """Show the part of the snapshot that is right behind the body."""
        back = self._back
        if self._brush is None or not back.IsVisible or back.ActualWidth <= 0:
            return
        src = PresentationSource.FromVisual(back)
        if src is None:
            return
        k = src.CompositionTarget.TransformToDevice
        p = back.PointToScreen(Point(0, 0))
        vs = self._screen
        self._brush.Viewbox = Rect((p.X - vs.Left) / float(SHRINK), (p.Y - vs.Top) / float(SHRINK),
                                   back.ActualWidth * k.M11 / SHRINK, back.ActualHeight * k.M22 / SHRINK)

    def drag(self, sender, args):
        """Header MouseLeftButtonDown handler: move the window."""
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
