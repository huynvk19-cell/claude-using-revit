using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;
using ClaudeUsingRevit.Core;

namespace ClaudeUsingRevit.Ui
{
    /// <summary>The look of the Python tool windows for a C# window: the theme lib/liquid_glass.xaml on Windows' own blur (as lib/native_blur.py).</summary>
    public static class Glass
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMWCP_ROUND = 2, DWMSBT_TRANSIENTWINDOW = 3, GWL_STYLE = -16, WS_SYSMENU = 0x00080000;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        static int Set(IntPtr hwnd, int attr, int value) => DwmSetWindowAttribute(hwnd, attr, ref value, 4);

        /// <summary>Call first: the window's styles come from the theme. False: the theme is not there, the window keeps Windows' plain look.</summary>
        public static bool Theme(Window window)
        {
            try
            {
                using (var file = File.OpenRead(Path.Combine(Paths.ExtensionLib, "liquid_glass.xaml")))
                    window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(file));
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("theme not loaded: " + ex.Message);
                return false;
            }
        }

        /// <summary>Call before the window shows: Acrylic where Windows has it, the theme's plain body elsewhere; drag it by any empty part.</summary>
        public static void Frame(Window window)
        {
            window.WindowStyle = WindowStyle.SingleBorderWindow;    // DWM blurs, rounds and shades a framed window
            window.Background = Brushes.Transparent;
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(-1),
                UseAeroCaptionButtons = false,
            });
            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                bool blur = false;
                try
                {
                    SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
                    Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 0);    // light: the theme's ink is dark
                    Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
                    blur = Set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW) == 0;
                }
                catch (Exception) { }
                if (blur) HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                else window.Background = window.TryFindResource("GlassBase") as Brush ?? Brushes.WhiteSmoke;
            };
            window.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { window.DragMove(); } catch (InvalidOperationException) { } } };
        }
    }
}
