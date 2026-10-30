# claude using revit - desktop island: the web app as a small capsule at the top centre of the screen.
# Inactive: a see-through pill (model, work left, the job running). Click it: it grows into the full web app.
# Click elsewhere: it shrinks back and fades. Tray icon: open / reload / quit. One instance per user.
# Hosts the page in WebView2 (Microsoft Edge runtime, part of Windows 11); the pill itself is drawn natively. The WebView2 SDK dlls are not
# shipped in this repo: install.ps1 -WebApp copies them from a local install (see Find-WebView2Sdk).
# Run: powershell -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File island.ps1
param([string]$Url)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$data = Join-Path $env:APPDATA 'claude-using-revit'
$lib = Join-Path $here 'lib'
foreach ($d in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') {
    if (-not (Test-Path (Join-Path $lib $d))) { throw "missing $lib\$d (run install.ps1 -WebApp)" }
}
if (-not $Url) {
    $cfg = Get-Content (Join-Path $data 'webapp.json') -Raw | ConvertFrom-Json
    $port = if ($cfg.port) { $cfg.port } else { 8780 }
    # the web app server: start it when nothing answers on its port
    $c = New-Object Net.Sockets.TcpClient
    try { $c.Connect('127.0.0.1', $port) } catch {
        $node = (Get-Command node -ErrorAction SilentlyContinue).Source
        if ($node) { Start-Process $node -ArgumentList "`"$data\webapp\server.mjs`"" -WorkingDirectory "$data\webapp" -WindowStyle Hidden; Start-Sleep 1 }
    } finally { $c.Close() }
    $Url = "http://localhost:$port/?k=$($cfg.token)"
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Name Dpi -Namespace CUR -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v); [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool SetDllDirectory(string p);'
[CUR.Dpi]::SetDllDirectory($lib) | Out-Null     # WebView2Loader.dll (native, x64) is found next to the SDK dlls
[CUR.Dpi]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null     # per-monitor v2: sharp text, real pixels
Add-Type -Path (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll')
Add-Type -Path (Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll')
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Web.Extensions, (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll') -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// The pill is drawn here, not by the page: WebView2 cannot render inside a see-through (layered) window.
// Open: the window is opaque and shows the web app. Pill: the window is layered (see-through) and this
// control covers the page, showing the line the page reports (window.pillState()).
public class Pill : Control
{
    public string Cls = "", Main = "claude using revit", Sub = "";
    public float Angle;
    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Color.Black;
        Cursor = Cursors.Hand;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Black);
        float s = DeviceDpi / 96f;
        Color work = Color.FromArgb(255, 159, 10);
        using (var fMain = new Font("Segoe UI Semibold", 10.5f))
        using (var fSub = new Font("Segoe UI", 9.5f))
        {
            var main = Main ?? ""; var sub = Sub ?? "";
            SizeF m = g.MeasureString(main, fMain), n = sub.Length > 0 ? g.MeasureString(sub, fSub) : SizeF.Empty;
            float dot = 10 * s, gap = 9 * s;
            float total = dot + gap + m.Width + (sub.Length > 0 ? gap + n.Width : 0);
            float x = Math.Max(14 * s, (Width - total) / 2), cy = Height / 2f;
            if (Cls == "busy")
            {
                using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), 2 * s)) g.DrawEllipse(p, x, cy - dot / 2, dot, dot);
                using (var p = new Pen(Color.White, 2 * s)) g.DrawArc(p, x, cy - dot / 2, dot, dot, Angle, 90);
            }
            else
            {
                Color c = Cls == "work" ? work : Cls == "done" ? Color.White : Color.FromArgb(142, 142, 147);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, x + dot * 0.1f, cy - dot * 0.4f, dot * 0.8f, dot * 0.8f);
            }
            x += dot + gap;
            using (var b = new SolidBrush(Cls == "work" ? work : Color.White)) g.DrawString(main, fMain, b, x, cy - m.Height / 2);
            x += m.Width + gap;
            if (sub.Length > 0)
                using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                    g.DrawString(sub, fSub, b, new RectangleF(x, cy - n.Height / 2, Math.Max(0, Width - x - 12 * s), n.Height),
                                 new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
        }
    }
}

public class Island : Form
{
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);

    const double PillW = 360, PillH = 44, OpenW = 580, OpenH = 780, Gap = 8;
    readonly WebView2 web = new WebView2();
    readonly Pill pill = new Pill();
    readonly Timer timer = new Timer();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    bool open, layered, ready;
    string log;
    double t;              // 0 = pill, 1 = open window
    double alpha = 140;
    double s = 1;          // device pixels per DIP
    int ticks;

    public Island(string url, string dataDir, Icon icon)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Text = "claude using revit";
        if (icon != null) Icon = icon;
        web.DefaultBackgroundColor = Color.Black;
        Controls.Add(pill);
        Controls.Add(web);
        pill.BringToFront();
        log = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dataDir), "island.log");
        Shown += async delegate {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Navigate(url);
                ready = true;
            }
            catch (Exception ex) { System.IO.File.AppendAllText(log, ex + Environment.NewLine); }
        };
        pill.MouseDown += delegate { Activate(); };
        Activated += delegate { open = true; };
        Deactivate += delegate { open = false; };
        timer.Interval = 15;
        timer.Tick += delegate { Step(); };
        // WebView2 is created in the opaque window (Shown): see-through only once it is up
        Load += delegate { s = DeviceDpi / 96.0; Place(0); timer.Start(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }   // starts as the pill

    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x80; return p; }    // tool window: not in Alt+Tab
    }

    public void Toggle() { if (open) { open = false; pill.Focus(); } else { Activate(); } }
    public void Reload() { if (web.CoreWebView2 != null) web.CoreWebView2.Reload(); }

    void SetLayered(bool on, byte a)
    {
        int ex = GetWindowLong(Handle, -20);
        if (on)
        {
            if (!layered) { SetWindowLong(Handle, -20, ex | 0x80000); layered = true; }
            SetLayeredWindowAttributes(Handle, 0, a, 2);
        }
        else if (layered) { SetWindowLong(Handle, -20, ex & ~0x80000); layered = false; }
    }

    void Step()
    {
        double target = open ? 1 : 0;
        t += (target - t) * (target > t ? 0.22 : 0.3);
        if (Math.Abs(target - t) < 0.002) t = target;
        if (t > 0.02)
        {       // opening / open: opaque, the page shows
            SetLayered(false, 255);
            if (pill.Visible) pill.Visible = false;
        }
        else
        {       // the pill: see-through, brighter under the mouse
            if (!pill.Visible) { pill.Visible = true; pill.BringToFront(); }
            double a = Bounds.Contains(Cursor.Position) ? 240 : 140;
            alpha += (a - alpha) * 0.2;
            if (ready) SetLayered(true, (byte)alpha);
        }
        Place(t);
        if (pill.Visible && pill.Cls == "busy") { pill.Angle = (pill.Angle + 12) % 360; pill.Invalidate(); }
        if (++ticks % 40 == 0) ReadState();     // ~0.6 s
    }

    async void ReadState()
    {
        if (web.CoreWebView2 == null) return;
        try
        {
            string r = await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.pillState ? window.pillState() : null)");
            var str = json.Deserialize<string>(r);
            if (string.IsNullOrEmpty(str)) return;
            var d = json.Deserialize<Dictionary<string, object>>(str);
            string cls = d["cls"] as string ?? "", main = d["main"] as string ?? "", sub = d["sub"] as string ?? "";
            if (cls != pill.Cls || main != pill.Main || sub != pill.Sub) { pill.Cls = cls; pill.Main = main; pill.Sub = sub; pill.Invalidate(); }
        }
        catch { }
    }

    Rectangle last;
    void Place(double f)
    {
        var area = Screen.PrimaryScreen.WorkingArea;
        double e = 1 - Math.Pow(1 - f, 3);
        int w = (int)Math.Round(s * (PillW + (Math.Min(OpenW, area.Width / s - 40) - PillW) * e));
        int h = (int)Math.Round(s * (PillH + (Math.Min(OpenH, area.Height / s - 30) - PillH) * e));
        var r = new Rectangle(area.Left + (area.Width - w) / 2, area.Top + (int)(Gap * s), w, h);
        if (r == last) return;
        last = r;
        Bounds = r;
        pill.Bounds = web.Bounds = new Rectangle(0, 0, w, h);
        int rad = (int)Math.Round(s * (PillH + (56 - PillH) * e));
        SetWindowRgn(Handle, CreateRoundRectRgn(0, 0, w + 1, h + 1, Math.Min(rad, h), Math.Min(rad, h)), true);
    }
}
"@

$mutex = New-Object Threading.Mutex($false, 'Local\ClaudeUsingRevitIsland')
if (-not $mutex.WaitOne(0)) { return }     # already running
[System.Windows.Forms.Application]::EnableVisualStyles()
$iconPng = Get-ChildItem (Join-Path $env:APPDATA 'pyRevit\Extensions\ClaudeUsingRevit.extension') -Recurse -Filter icon.png -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*Print Set Sync*' } | Select-Object -First 1
$icon = $null
if ($iconPng) { $bmp = New-Object Drawing.Bitmap $iconPng.FullName; $icon = [Drawing.Icon]::FromHandle($bmp.GetHicon()) }
$form = New-Object Island($Url, (Join-Path $data 'island-webview'), $icon)

$tray = New-Object Windows.Forms.NotifyIcon
$tray.Icon = if ($icon) { $icon } else { [Drawing.SystemIcons]::Application }
$tray.Text = 'claude using revit'
$menu = New-Object Windows.Forms.ContextMenuStrip
$menu.Items.Add('Mở / thu gọn', $null, { $form.Toggle() }) | Out-Null
$menu.Items.Add('Tải lại', $null, { $form.Reload() }) | Out-Null
$menu.Items.Add('Thoát', $null, { $tray.Visible = $false; $form.Close() }) | Out-Null
$tray.ContextMenuStrip = $menu
$tray.add_MouseClick({ param($s, $e) if ($e.Button -eq 'Left') { $form.Toggle() } })
$tray.Visible = $true
$form.add_FormClosed({ [System.Windows.Forms.Application]::ExitThread() })
$form.Show()                                   # shown without taking focus from Revit (ShowWithoutActivation)
[System.Windows.Forms.Application]::Run()
$tray.Visible = $false
$mutex.ReleaseMutex()
