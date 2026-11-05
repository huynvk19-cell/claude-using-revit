using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    /// <summary>The window of the status button: state, clients, last calls, and the buttons to act on them.</summary>
    public class StatusWindow : Window
    {
        static StatusWindow _open;

        readonly Ellipse _dot = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0) };
        readonly TextBlock _state = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold };
        readonly TextBlock _note = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0) };
        readonly TextBlock _info = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 12, 0, 0) };
        readonly TextBlock _calls = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, LineHeight = 18 };
        readonly Button _toggle = Btn("Tắt");
        readonly Button _take = Btn("Nhận kết nối về cửa sổ này");
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

        static Button Btn(string text) => new Button
        {
            Content = text,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 70,
        };

        StatusWindow()
        {
            Title = "claude using revit · Claude";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            ShowInTaskbar = false;

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(_dot);
            head.Children.Add(_state);

            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(_toggle);
            buttons.Children.Add(_take);
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
                if (!ok) MessageBox.Show(this, "Chưa nhận được cổng: " + McpServer.Note, "claude using revit");
                Refresh();
            };
            copy.Click += (s, e) =>
            {
                string cmd = $"claude mcp add --transport http --scope user claude-using-revit http://127.0.0.1:{McpServer.Port}/mcp";
                try { Clipboard.SetText(cmd); copy.Content = "Đã chép"; } catch { MessageBox.Show(this, cmd, "claude using revit"); }
            };
            log.Click += (s, e) => Open(Log.File);
            cmds.Click += (s, e) => Open(Paths.Commands);

            var callsBox = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA)),
                CornerRadius = new CornerRadius(6),
                Child = new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _calls },
            };

            var root = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            root.Children.Add(head);
            root.Children.Add(_note);
            root.Children.Add(_info);
            root.Children.Add(buttons);
            root.Children.Add(new TextBlock { Text = "Lệnh gần đây", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            root.Children.Add(callsBox);
            Content = root;

            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
            Closed += (s, e) => _timer.Stop();
            Refresh();
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
