using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AiCuli.Ui
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

        /// <summary>The status button: a rounded tile in the state colour with a white mark ("AI" or a glyph).</summary>
        public static ImageSource Status(Color top, Color bottom, string mark, int dip)
        {
            int px = (int)Math.Round(dip * Dpi.Scale);
            double s = px / 32.0;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var fill = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0.4, 1));
                dc.DrawRoundedRectangle(fill, null, new Rect(1.5 * s, 1.5 * s, 29 * s, 29 * s), 8 * s, 8 * s);
                var gloss = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0), new Point(0, 0.6));
                dc.DrawRoundedRectangle(gloss, null, new Rect(1.5 * s, 1.5 * s, 29 * s, 16 * s), 8 * s, 8 * s);
                var text = new FormattedText(mark, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    (mark.Length > 1 ? 13 : 17) * s, Brushes.White, 1.0);
                dc.DrawText(text, new Point((px - text.Width) / 2, (px - text.Height) / 2));
            }
            var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return WithDpi(rtb, px);
        }
    }
}
