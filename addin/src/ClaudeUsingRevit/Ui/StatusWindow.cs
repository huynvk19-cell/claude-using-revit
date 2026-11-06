using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClaudeUsingRevit.Core;
using ClaudeUsingRevit.Mcp;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace ClaudeUsingRevit.Ui
{
    /// <summary>The window of the status button: state, clients, last calls, and the buttons to act on them. Same look as the tool windows (Glass).</summary>
    public class StatusWindow : Window
    {
        static StatusWindow _open;

        readonly Ellipse _dot = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0) };
        readonly TextBlock _state = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock _note = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 2, 0, 0) };
        readonly TextBlock _info = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(4, 12, 4, 0) };
        readonly TextBlock _calls = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, LineHeight = 18 };
        readonly TextBlock _say = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(4, 0, 4, 8), Visibility = Visibility.Collapsed };
        readonly Button _toggle = new Button { Content = "Tắt", Margin = new Thickness(0, 0, 8, 8) };
        readonly Button _take = new Button { Content = "Nhận kết nối về cửa sổ này", Margin = new Thickness(0, 0, 8, 8) };
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool _busy;

        public static void ShowOrActivate(UIApplication app)
        {
            if (_open != null) { _open.Activate(); return; }
            _open = new StatusWindow();
            try { new WindowInteropHelper(_open).Owner = app.MainWindowHandle; } catch { }
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }

        void Use(FrameworkElement e, string style)
        {
            if (TryFindResource(style) is Style s) e.Style = s;
        }

        Button Btn(string text)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
            Use(b, "Secondary");
            return b;
        }

        void Paint(TextBlock t, string brush)
        {
            if (TryFindResource(brush) is Brush b) t.Foreground = b;
        }

        StatusWindow()
        {
            Title = "claude using revit · Claude";
            bool themed = Glass.Theme(this);
            Width = 600;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = TryFindResource("Ui") as FontFamily ?? new FontFamily("Segoe UI");
            FontSize = 13;
            ShowInTaskbar = false;
            if (themed) Glass.Frame(this);
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };

            Paint(_state, "Ink");
            Paint(_note, "Ink2");
            Paint(_info, "Ink");
            Paint(_calls, "Ink");
            Paint(_say, "Ink2");
            if (TryFindResource("UiDisplay") is FontFamily display) _state.FontFamily = display;
            Use(_toggle, "Secondary");
            Use(_take, "Primary");

            var dotAndState = new StackPanel { Orientation = Orientation.Horizontal };
            dotAndState.Children.Add(_dot);
            dotAndState.Children.Add(_state);
            var words = new StackPanel();
            words.Children.Add(dotAndState);
            words.Children.Add(_note);
            var close = new Button { ToolTip = "Đóng (Esc)", VerticalAlignment = VerticalAlignment.Top, Content = themed ? null : "Đóng" };
            Use(close, "CloseBtn");
            close.Click += (s, e) => Close();
            var head = new Grid { Margin = new Thickness(24, 18, 18, 0), Background = Brushes.Transparent };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(close, 1);
            head.Children.Add(words);
            head.Children.Add(close);

            var buttons = new WrapPanel { Margin = new Thickness(24, 12, 16, 10) };
            buttons.Children.Add(_take);
            buttons.Children.Add(_toggle);
            var copy = Btn("Chép lệnh nối Claude Code");
            var log = Btn("Mở log");
            var cmds = Btn("Thư mục lệnh");
            buttons.Children.Add(copy);
            buttons.Children.Add(log);
            buttons.Children.Add(cmds);

            _toggle.Click += (s, e) =>
            {
                if (McpServer.Enabled) McpServer.Stop("tắt từ cửa sổ trạng thái");
                else McpServer.Start();
                Refresh();
            };
            _take.Click += async (s, e) =>
            {
                _busy = true;
                _take.IsEnabled = false;
                _take.Content = "Đang nhận…";
                bool ok = await Task.Run(() => McpServer.TakeOver());
                _busy = false;
                _take.Content = "Nhận kết nối về cửa sổ này";
                Say(ok ? "" : "Chưa nhận được cổng: " + McpServer.Note);
                Refresh();
            };
            copy.Click += (s, e) =>
            {
                string cmd = $"claude mcp add --transport http --scope user claude-using-revit http://127.0.0.1:{McpServer.Port}/mcp";
                try { Clipboard.SetText(cmd); copy.Content = "Đã chép"; } catch { Say(cmd); }
            };
            log.Click += (s, e) => Open(Log.File);
            cmds.Click += (s, e) => Open(Paths.Commands);

            // in proportion to the screen (as lib/screen_fit.py for the Python tools): only the list of calls gives way
            var area = SystemParameters.WorkArea;
            Width = Math.Min(area.Width - 16, 600 * Math.Max(1.0, Math.Min(1.6, area.Width / 1920)));
            MaxHeight = area.Height - 16;

            var scroll = new ScrollViewer { MaxHeight = 260 * area.Height / 1040, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _calls };
            var callsBox = new Border { Margin = new Thickness(0, 6, 0, 0), Child = scroll };
            if (TryFindResource("Well") is Style well) { callsBox.Style = well; callsBox.CornerRadius = new CornerRadius(14); }

            var callsTitle = new TextBlock { Text = "Lệnh gần đây", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 14, 0, 0) };
            Paint(callsTitle, "Ink2");
            var middle = new Grid { Margin = new Thickness(24, 0, 24, 0) };
            middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            middle.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(callsTitle, 1);
            Grid.SetRow(callsBox, 2);
            middle.Children.Add(_info);
            middle.Children.Add(callsTitle);
            middle.Children.Add(callsBox);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(middle, 1);
            Grid.SetRow(buttons, 2);
            Grid.SetRow(_say, 3);
            _say.Margin = new Thickness(28, 0, 24, 14);
            root.Children.Add(head);
            root.Children.Add(middle);
            root.Children.Add(buttons);
            root.Children.Add(_say);
            var glass = new ContentControl { Content = root };
            Use(glass, "GlassPanel");
            Content = glass;

            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
            Closed += (s, e) => _timer.Stop();
            Refresh();
        }

        void Say(string text)
        {
            _say.Text = text;
            _say.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        void Refresh()
        {
            var look = StatusUpdater.Current();
            _dot.Fill = new LinearGradientBrush(look.Top, look.Bottom, 90);
            _state.Text = look.Text.Replace("\n", ": ");
            _note.Text = look.Tip;
            var snap = (JObject)McpServer.Snapshot(false);
            var others = (JArray)snap["otherWindows"];
            var clients = (JArray)snap["clients"];
            _info.Text =
                $"Địa chỉ MCP: {snap["url"]}\n" +
                $"Cửa sổ này: Revit {snap["revit"]}, PID {snap["pid"]} · claude using revit {snap["version"]}\n" +
                "Client: " + (clients.Count == 0 ? "chưa có" : string.Join(", ", clients.Select(c => $"{c["name"]} {c["version"]}"))) +
                $" · luồng mở: {snap["openStreams"]}\n" +
                (snap["running"] is JObject r ? $"Đang chạy: {r["tool"]} ({r["seconds"]} s), chờ: {snap["queued"]}\n" : "") +
                "Cửa sổ Revit khác: " + (others.Count == 0 ? "không có" : string.Join(", ", others.Select(o => $"PID {o["pid"]} ({o["role"]})")));
            _toggle.Content = McpServer.Enabled ? "Tắt máy chủ" : "Bật máy chủ";
            if (!_busy) _take.IsEnabled = McpServer.State != ServerState.Listening;
            var calls = CallLog.Recent(15);
            _calls.Text = calls.Count == 0
                ? "(chưa có)"
                : string.Join("\n", calls.Select(c => $"{c.At:HH:mm:ss}  {(c.Ok ? "✓" : "✗")}  {c.Tool,-26} {c.Ms,7} ms  {c.Note}"));
        }
    }
}
