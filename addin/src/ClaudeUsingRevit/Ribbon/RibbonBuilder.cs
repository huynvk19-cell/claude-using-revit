using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClaudeUsingRevit.Core;
using ClaudeUsingRevit.Ui;
using Autodesk.Revit.UI;

namespace ClaudeUsingRevit.Ribbon
{
    public class ButtonInfo
    {
        public int Slot;
        public string Name;      // folder name without .pushbutton
        public string Dir;
        public string Script;
        public string Title;
        public string Tooltip;
        public string Icon;
    }

    /// <summary>
    /// The ribbon tab "claude using revit":
    ///   panel "Claude"  (C#): the MCP status button + Tải lại (reload the Python of the extension)
    ///   then one panel per extension\*.tab\*.panel, one button per *.pushbutton (script.py, bundle.yaml, icon.png),
    ///   in the order of the bundle.yaml "layout" lists. Buttons run their script.py in IronPython (PyHost).
    /// Revit cannot add ribbon buttons later: a new *.pushbutton folder needs a Revit restart; an edit of a
    /// script needs nothing (each click runs the file again).
    /// </summary>
    public static class RibbonBuilder
    {
        public static readonly List<ButtonInfo> Buttons = new List<ButtonInfo>();
        public static PushButton StatusButton { get; private set; }
        public static string TabName { get; private set; }

        public static void Build(UIControlledApplication app)
        {
            string asm = Assembly.GetExecutingAssembly().Location;
            string tabDir = Directory.Exists(Paths.Extension)
                ? Directory.GetDirectories(Paths.Extension, "*.tab").OrderBy(d => d).FirstOrDefault()
                : null;
            TabName = Config.Current.TabName
                      ?? (tabDir != null ? Path.GetFileNameWithoutExtension(tabDir) : "claude using revit");
            try { app.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* already there */ }

            // ---- panel Claude: status + reload
            var claude = app.CreateRibbonPanel(TabName, "Claude");
            var status = new PushButtonData("cur_status", "Claude\nĐang mở", asm, typeof(StatusCommand).FullName)
            {
                AvailabilityClassName = typeof(AlwaysAvailable).FullName,
                ToolTip = "Kết nối với Claude (máy chủ MCP trong add-in). Bấm để xem chi tiết, bật / tắt, nhận kết nối về cửa sổ này.",
            };
            StatusButton = (PushButton)claude.AddItem(status);
            var reload = new PushButtonData("cur_reload", "Tải\nlại", asm, typeof(ReloadCommand).FullName)
            {
                AvailabilityClassName = typeof(AlwaysAvailable).FullName,
                ToolTip = "Chạy lại phần Python của claude using revit (startup.py: cầu nối web, cửa sổ Bảng). " +
                          "Sửa script của một nút thì không cần: mỗi lần bấm nút đã chạy file mới.",
                LargeImage = Icons.Status(System.Windows.Media.Color.FromRgb(0x8C, 0x9B, 0xB0),
                    System.Windows.Media.Color.FromRgb(0x4A, 0x5A, 0x70), "↻", 32),
                Image = Icons.Status(System.Windows.Media.Color.FromRgb(0x8C, 0x9B, 0xB0),
                    System.Windows.Media.Color.FromRgb(0x4A, 0x5A, 0x70), "↻", 16),
            };
            claude.AddItem(reload);
            StatusUpdater.Paint();

            if (tabDir == null)
            {
                Log.Write("ribbon: no *.tab folder in " + Paths.Extension + " (install.ps1 not run?)");
                return;
            }

            // ---- extension panels
            var tabYaml = BundleYaml.Load(tabDir);
            foreach (string panelDir in Ordered(tabDir, "*.panel", tabYaml.GetList("layout")))
            {
                var py = BundleYaml.Load(panelDir);
                string title = py.Get("title") ?? Path.GetFileNameWithoutExtension(panelDir);
                RibbonPanel panel;
                try { panel = app.CreateRibbonPanel(TabName, title); }
                catch (Exception ex) { Log.Error("ribbon panel " + title, ex); continue; }
                foreach (string btnDir in Ordered(panelDir, "*.pushbutton", py.GetList("layout")))
                {
                    string script = Path.Combine(btnDir, "script.py");
                    if (!File.Exists(script)) continue;
                    if (Buttons.Count >= Slots.Count)
                    {
                        Log.Write($"ribbon: more than {Slots.Count} buttons, {btnDir} left out (add slots in Slots.cs)");
                        continue;
                    }
                    var by = BundleYaml.Load(btnDir);
                    var info = new ButtonInfo
                    {
                        Slot = Buttons.Count,
                        Name = Path.GetFileNameWithoutExtension(btnDir),
                        Dir = btnDir,
                        Script = script,
                        Title = by.Get("title") ?? Path.GetFileNameWithoutExtension(btnDir),
                        Tooltip = by.Get("tooltip"),
                        Icon = Path.Combine(btnDir, "icon.png"),
                    };
                    var data = new PushButtonData("cur_btn_" + info.Slot, info.Title, asm, Slots.ClassName(info.Slot))
                    {
                        LargeImage = Icons.Load(info.Icon, 32),
                        Image = Icons.Load(info.Icon, 16),
                    };
                    SetTooltip(data, info.Tooltip);
                    try
                    {
                        panel.AddItem(data);
                        Buttons.Add(info);
                    }
                    catch (Exception ex) { Log.Error("ribbon button " + info.Name, ex); }
                }
            }
            Log.Write($"ribbon: tab '{TabName}', {Buttons.Count} buttons from {tabDir}");
        }

        /// <summary>Sub-folders matching pattern: first those named in layout (in that order), then the rest by name.</summary>
        static IEnumerable<string> Ordered(string dir, string pattern, List<string> layout)
        {
            var all = Directory.GetDirectories(dir, pattern).ToList();
            var byName = all.ToDictionary(d => Path.GetFileNameWithoutExtension(d), StringComparer.OrdinalIgnoreCase);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in layout)
                if (byName.TryGetValue(n, out var d) && done.Add(d)) yield return d;
            foreach (string d in all.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                if (done.Add(d)) yield return d;
        }

        static void SetTooltip(ButtonData data, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length <= 250) { data.ToolTip = text; return; }
            int cut = text.LastIndexOf(". ", 248, StringComparison.Ordinal);
            if (cut < 60) cut = text.LastIndexOf(' ', 248);
            data.ToolTip = text.Substring(0, cut + 1).Trim();
            data.LongDescription = text.Substring(cut + 1).Trim();
        }
    }
}
