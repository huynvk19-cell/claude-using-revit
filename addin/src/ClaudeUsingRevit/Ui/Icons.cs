using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeUsingRevit.Ui
{
    public static class Dpi
    {
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();

        static double _scale;
        /// <summary>Screen scale of the system (1.0 = 96 dpi, 1.5 = 144 dpi).</summary>
        public static double Scale
        {
            get
            {
                if (_scale > 0) return _scale;
                try { _scale = Math.Max(1.0, GetDpiForSystem() / 96.0); }
                catch { _scale = 1.0; }
                return _scale;
            }
        }
    }

    public static class Icons
    {
        /// <summary>An icon file sized to <paramref name="dip"/> device-independent pixels, sharp at the screen scale.</summary>
        public static ImageSource Load(string file, int dip)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            try
            {
                int px = (int)Math.Round(dip * Dpi.Scale);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;       // do not keep the file open (install can replace it)
                img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                img.UriSource = new Uri(file);
                img.DecodePixelWidth = px;
                img.EndInit();
                return WithDpi(img, px);
            }
            catch { return null; }
        }

        static BitmapSource WithDpi(BitmapSource src, int px)
        {
            var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int stride = conv.PixelWidth * 4;
            var pixels = new byte[stride * conv.PixelHeight];
            conv.CopyPixels(pixels, stride, 0);
            double dpi = 96.0 * Dpi.Scale;
            var bmp = BitmapSource.Create(conv.PixelWidth, conv.PixelHeight, dpi, dpi, PixelFormats.Pbgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>The status button: Apple's switch in the state colour on the mica plate of the other buttons.
        /// A mark ("!") sits on the knob.</summary>
        public static ImageSource Switch(Color top, Color bottom, bool on, string mark, int dip)
        {
            string knob = (on ? "16.5" : "4.5");
            string text = string.IsNullOrEmpty(mark) ? "" :
                "<TextBlock Canvas.Left='" + knob + "' Canvas.Top='8.6' Width='11' TextAlignment='Center' Text='" + mark +
                "' FontFamily='Segoe UI' FontWeight='Bold' FontSize='9.5' Foreground='#D13438'/>";
            return Plate(
                // the track, with its inner shade
                "<Rectangle Canvas.Left='3' Canvas.Top='8' Width='26' Height='15' RadiusX='7.5' RadiusY='7.5'>" +
                "<Rectangle.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='" + Hex(top) + "' Offset='0'/>" +
                "<GradientStop Color='" + Hex(bottom) + "' Offset='1'/></LinearGradientBrush></Rectangle.Fill>" +
                "<Rectangle.Effect><DropShadowEffect Color='#16202C' BlurRadius='1.6' ShadowDepth='0.6' Direction='270' Opacity='0.4'/></Rectangle.Effect></Rectangle>" +
                "<Rectangle Canvas.Left='3' Canvas.Top='8' Width='26' Height='8' RadiusX='7.5' RadiusY='4'>" +
                "<Rectangle.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#26000000' Offset='0'/>" +
                "<GradientStop Color='#00000000' Offset='1'/></LinearGradientBrush></Rectangle.Fill></Rectangle>" +
                // the knob
                "<Ellipse Canvas.Left='" + knob + "' Canvas.Top='9.5' Width='12' Height='12'>" +
                "<Ellipse.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#FFFFFF' Offset='0'/>" +
                "<GradientStop Color='#E4E9F0' Offset='1'/></LinearGradientBrush></Ellipse.Fill>" +
                "<Ellipse.Effect><DropShadowEffect Color='#10161E' BlurRadius='2' ShadowDepth='0.8' Direction='270' Opacity='0.5'/></Ellipse.Effect></Ellipse>" +
                text, dip);
        }

        /// <summary>A round blue badge with a white Fluent glyph on the plate, as on the other buttons.</summary>
        public static ImageSource Badge(string glyph, int dip)
        {
            return Plate(
                "<Grid Canvas.Left='6' Canvas.Top='5.5' Width='20' Height='20'>" +
                "<Ellipse><Ellipse.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#6CC7FF' Offset='0'/>" +
                "<GradientStop Color='#0F6CBD' Offset='1'/></LinearGradientBrush></Ellipse.Fill>" +
                "<Ellipse.Effect><DropShadowEffect Color='#16202C' BlurRadius='1.8' ShadowDepth='0.7' Direction='280' Opacity='0.45'/></Ellipse.Effect></Ellipse>" +
                "<TextBlock Text='" + glyph + "' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontWeight='Bold' FontSize='12' Foreground='White' " +
                "HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid>", dip);
        }

        static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        /// <summary>The raised plate of mica of every button, on a 32 grid: the same drawing as addin/icons/make_icons.ps1.</summary>
        static ImageSource Plate(string inner, int dip)
        {
            int px = (int)Math.Round(dip * Dpi.Scale);
            string xaml =
                "<Viewbox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='" + px + "' Height='" + px + "'>" +
                "<Grid Width='32' Height='32'>" +
                "<Border Margin='1.5,3,1.5,0' CornerRadius='3.5' Background='#000000' Opacity='0.28'><Border.Effect><BlurEffect Radius='1.6'/></Border.Effect></Border>" +
                "<Border Margin='1,2.5,1,0.8' CornerRadius='3.5' Background='#8C95A1'/>" +
                "<Border Margin='1,1,1,2' CornerRadius='3.5'><Border.Background><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>" +
                "<GradientStop Color='#F6F8FA' Offset='0'/><GradientStop Color='#DCE1E8' Offset='0.45'/><GradientStop Color='#B4BCC8' Offset='1'/>" +
                "</LinearGradientBrush></Border.Background></Border>" +
                "<Border Margin='1,1,1,2' CornerRadius='3.5' BorderThickness='0.7'><Border.BorderBrush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'>" +
                "<GradientStop Color='#FFFFFFFF' Offset='0'/><GradientStop Color='#80FFFFFF' Offset='0.4'/><GradientStop Color='#338A93A0' Offset='1'/>" +
                "</LinearGradientBrush></Border.BorderBrush></Border>" +
                "<Path Data='M 2.2,2 L 21,2 L 8,18 L 2.2,24 Z' Opacity='0.55'><Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>" +
                "<GradientStop Color='#A6FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></LinearGradientBrush></Path.Fill></Path>" +
                "<Canvas Width='32' Height='32'>" + inner + "</Canvas>" +
                "</Grid></Viewbox>";
            var el = (FrameworkElement)XamlReader.Parse(xaml);
            el.Measure(new Size(px, px));
            el.Arrange(new Rect(0, 0, px, px));
            el.UpdateLayout();
            var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);
            return WithDpi(rtb, px);
        }
    }
}
