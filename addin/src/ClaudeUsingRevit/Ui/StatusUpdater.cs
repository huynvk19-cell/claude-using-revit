using System;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsingRevit.Core;
using ClaudeUsingRevit.Mcp;
using ClaudeUsingRevit.Ribbon;

namespace ClaudeUsingRevit.Ui
{
    /// <summary>
    /// The status button of panel "Claude": its colour, text and tooltip follow the MCP server, once a second.
    ///   green  Đã kết nối / Đang chạy   a client is connected (open event stream or a request in the last 3 min)
    ///   blue   Chờ kết nối               this window holds the port, no client yet
    ///   amber  Cửa sổ khác               another Revit window holds the port (this one takes it when free)
    ///   grey   Đã tắt                    the server is off (turned off in the status window, or autoStart false)
    ///   red    Lỗi                       the port could not be opened
    /// </summary>
    public static class StatusUpdater
    {
        static DispatcherTimer _timer;
        static string _painted;

        public static void Start()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Paint();
            _timer.Start();
        }

        public class Look
        {
            public string Key, Text, Mark, Tip;
            public bool On;             // the switch of the button: on while this window holds the port
            public Color Top, Bottom;
        }

        public static Look Current()
        {
            var job = RevitQueue.Current;
            switch (McpServer.State)
            {
                case ServerState.Listening when job != null:
                    return new Look { Key = "run", Text = "Claude\nĐang chạy", On = true, Top = C(0x5B, 0xE3, 0x8C), Bottom = C(0x10, 0x8A, 0x3E),
                                      Tip = "Đang chạy: " + job.Label };
                case ServerState.Listening when McpServer.Connected:
                    return new Look { Key = "on", Text = "Claude\nĐã kết nối", On = true, Top = C(0x5B, 0xE3, 0x8C), Bottom = C(0x10, 0x8A, 0x3E),
                                      Tip = "Đã kết nối" + (McpServer.LastClient != null ? ": " + McpServer.LastClient : "") };
                case ServerState.Listening:
                    return new Look { Key = "wait", Text = "Claude\nChờ kết nối", On = true, Top = C(0x6C, 0xC7, 0xFF), Bottom = C(0x0F, 0x6C, 0xBD),
                                      Tip = "Cửa sổ này giữ cổng, chưa có Claude nào kết nối" };
                case ServerState.Standby:
                    return new Look { Key = "standby:" + McpServer.Note, Text = "Claude\nCửa sổ khác", Top = C(0xFF, 0xD8, 0x6B), Bottom = C(0xF3, 0x9A, 0x0B),
                                      Tip = McpServer.Note + ". Bấm để mở, rồi Nhận kết nối về cửa sổ này." };
                case ServerState.Error:
                    return new Look { Key = "err:" + McpServer.Note, Text = "Claude\nLỗi", Mark = "!", Top = C(0xFF, 0x8F, 0x82), Bottom = C(0xD1, 0x34, 0x38),
                                      Tip = "Không mở được cổng: " + McpServer.Note };
                default:
                    return new Look { Key = "off", Text = "Claude\nĐã tắt", Top = C(0xB4, 0xBE, 0xCC), Bottom = C(0x6B, 0x78, 0x8A),
                                      Tip = "Máy chủ MCP đang tắt. Bấm để mở cửa sổ trạng thái và bật." };
            }
        }

        static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

        public static void Paint()
        {
            var btn = RibbonBuilder.StatusButton;
            if (btn == null) return;
            try
            {
                var look = Current();
                if (look.Key == _painted) return;
                _painted = look.Key;
                btn.ItemText = look.Text;
                btn.LargeImage = Icons.Switch(look.Top, look.Bottom, look.On, look.Mark, 32);
                btn.Image = Icons.Switch(look.Top, look.Bottom, look.On, look.Mark, 16);
                btn.ToolTip = look.Tip + $"\nhttp://127.0.0.1:{McpServer.Port}/mcp · claude using revit {McpServer.Version}";
            }
            catch (Exception ex) { Log.Error("status paint", ex); }
        }
    }
}
