using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeUsingRevit.Setup
{
    /// <summary>One window: pick the Revit versions, Cài đặt / Gỡ cài đặt, a log of what happened.</summary>
    public class MainForm : Form
    {
        readonly InstallerCore _core = new InstallerCore();
        readonly Func<Stream> _payload;
        readonly string _version;
        readonly bool _uninstallMode;

        readonly CheckedListBox _revits = new CheckedListBox { CheckOnClick = true, Height = 110, Dock = DockStyle.Top, IntegralHeight = false };
        readonly CheckBox _skills = new CheckBox { Text = "Cài bộ kỹ năng vẽ (skill) cho Claude", Checked = true, AutoSize = true };
        readonly CheckBox _connect = new CheckBox { Text = "Nối Claude Code với Revit (máy chủ MCP)", Checked = true, AutoSize = true };
        readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White };
        readonly Button _install = new Button { Text = "Cài đặt", Width = 120, Height = 34 };
        readonly Button _uninstall = new Button { Text = "Gỡ cài đặt", Width = 120, Height = 34 };
        readonly Button _close = new Button { Text = "Đóng", Width = 100, Height = 34 };

        public MainForm(Func<Stream> payload, string version, bool uninstallMode)
        {
            _payload = payload;
            _version = version;
            _uninstallMode = uninstallMode;
            Text = "claude using revit - Cài đặt" + (string.IsNullOrEmpty(version) ? "" : " (" + version + ")");
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(600, 560);
            MinimumSize = new Size(520, 480);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location); } catch { }

            var title = new Label
            {
                Text = "claude using revit",
                Font = new Font("Segoe UI Semibold", 16f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2),
            };
            var intro = new Label
            {
                Text = "Tab công cụ trong Revit + kết nối để Claude làm việc trong Revit.\n" +
                       "Hãy đóng Revit và Claude trước khi cài. Không cần quyền quản trị.",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 12),
            };
            var pick = new Label { Text = "Cài cho các bản Revit:", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };

            var top = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            top.Controls.AddRange(new Control[] { title, intro, pick });
            var revitBox = new Panel { Dock = DockStyle.Top, Height = 114, Padding = new Padding(0, 0, 0, 4) };
            revitBox.Controls.Add(_revits);
            var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 8) };
            options.Controls.AddRange(new Control[] { _skills, _connect });
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 8, 0, 0) };
            buttons.Controls.AddRange(new Control[] { _close, _install, _uninstall });

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12) };
            body.Controls.Add(_log);
            body.Controls.Add(buttons);
            body.Controls.Add(options);
            body.Controls.Add(revitBox);
            body.Controls.Add(top);
            Controls.Add(body);

            _core.Log = s => BeginInvoke((Action)(() => { _log.AppendText(s + Environment.NewLine); }));
            _install.Click += (s, e) => Run(install: true);
            _uninstall.Click += (s, e) => Run(install: false);
            _close.Click += (s, e) => Close();
            Load += (s, e) => Fill();
        }

        void Fill()
        {
            List<string> inPayload;
            using (var zip = new ZipArchive(_payload(), ZipArchiveMode.Read)) inPayload = InstallerCore.PayloadRevit(zip);
            var installed = _core.InstalledRevit();
            foreach (string v in inPayload)
            {
                bool here = installed.Contains(v);
                _revits.Items.Add("Revit " + v + (here ? "" : "  (không thấy trên máy)"), here);
            }
            if (installed.Count == 0)
                Say("Không thấy Revit nào trong Program Files\\Autodesk. Tick bản Revit của bạn rồi bấm Cài đặt.");
            else
                Say("Thấy trên máy: Revit " + string.Join(", ", installed) + ".");
            var old = _core.InstalledAddins();
            if (old.Count > 0) Say("Đã cài trước đây cho: Revit " + string.Join(", ", old) + ". Cài lại sẽ thay bằng bản này.");
            _uninstall.Enabled = old.Count > 0;
            if (_uninstallMode)
            {
                _install.Enabled = false;
                Say("Bấm Gỡ cài đặt để gỡ add-in khỏi Revit.");
            }
        }

        void Say(string s) => _log.AppendText(s + Environment.NewLine);

        List<string> Picked() => _revits.CheckedItems.Cast<string>().Select(t => t.Substring("Revit ".Length, 4)).ToList();

        void Run(bool install)
        {
            if (InstallerCore.RevitRunning())
            {
                MessageBox.Show(this, "Revit đang mở. Hãy lưu và đóng mọi cửa sổ Revit, rồi bấm lại.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var versions = Picked();
            if (install && versions.Count == 0)
            {
                MessageBox.Show(this, "Hãy tick ít nhất một bản Revit.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if ((install ? _connect.Checked : true) && InstallerCore.ClaudeRunning())
            {
                var r = MessageBox.Show(this, "Claude đang mở. Nên đóng Claude trước, để nó không ghi đè cài đặt vừa thêm.\n\n" +
                                              "Đóng Claude xong bấm OK (hoặc Cancel để dừng).", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (r != DialogResult.OK) return;
            }
            if (!install && MessageBox.Show(this, "Gỡ add-in claude using revit khỏi mọi bản Revit?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            bool skills = _skills.Checked, connect = _connect.Checked;
            SetBusy(true);
            _log.AppendText(Environment.NewLine);
            var t = new Thread(() =>
            {
                string error = null;
                try
                {
                    if (install)
                    {
                        using (var zip = new ZipArchive(_payload(), ZipArchiveMode.Read))
                            _core.Install(zip, versions, skills, connect);
                        WindowsBits.Register(_core, _version);
                    }
                    else
                    {
                        _core.Uninstall();
                        WindowsBits.Unregister();
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                BeginInvoke((Action)(() => Done(install, error, versions)));
            }) { IsBackground = true };
            t.Start();
        }

        void SetBusy(bool busy)
        {
            _install.Enabled = !busy && !_uninstallMode;
            _uninstall.Enabled = !busy;
            _close.Enabled = !busy;
            _revits.Enabled = _skills.Enabled = _connect.Enabled = !busy;
            UseWaitCursor = busy;
        }

        void Done(bool install, string error, List<string> versions)
        {
            SetBusy(false);
            _uninstall.Enabled = _core.InstalledAddins().Count > 0;
            if (error != null)
            {
                Say("LỖI: " + error);
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (install)
            {
                Say(Environment.NewLine + "XONG. Mở Revit: tab \"claude using revit\" có thêm panel Claude, nút đầu tiên cho biết đã kết nối với Claude chưa.");
                Say("Mở Claude Code rồi nhờ nó, ví dụ: \"gọi hello_revit\".");
                MessageBox.Show(this, "Cài xong cho Revit " + string.Join(", ", versions) + ".\n\n" +
                                      "Mở Revit: tab \"claude using revit\", nút Claude đầu tiên cho biết đã kết nối chưa.\n" +
                                      "Lần đầu Revit có thể hỏi có tải add-in không: chọn \"Always Load\".",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Say(Environment.NewLine + "Đã gỡ xong.");
                MessageBox.Show(this, "Đã gỡ add-in khỏi Revit.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
