# claude using revit - the web app as its own desktop application (a normal window, taskbar icon,
# remembers its size and place in %APPDATA%\claude-using-revit\app.json). Opening it again brings the
# open window to the front. Hosts the page in WebView2 (Microsoft Edge runtime, part of Windows 11).
# The WebView2 SDK dlls are not shipped in this repo: install.ps1 -WebApp copies them from a local
# install (x64 loader) and adds the Start Menu shortcut "claude using revit".
# Run: powershell -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File app.ps1
param([string]$Url)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$data = Join-Path $env:APPDATA 'claude-using-revit'
$lib = Join-Path $here 'lib'
foreach ($d in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') {
    if (-not (Test-Path (Join-Path $lib $d))) { throw "missing $lib\$d (run install.ps1 -WebApp)" }
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Name Win -Namespace CUR -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool SetDllDirectory(string p);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
'@

$mutex = New-Object Threading.Mutex($false, 'Local\ClaudeUsingRevitApp')
if (-not $mutex.WaitOne(0)) {                  # already open: bring it to the front
    $h = [CUR.Win]::FindWindow($null, 'claude using revit')
    if ($h -ne [IntPtr]::Zero) { [CUR.Win]::ShowWindow($h, 9) | Out-Null; [CUR.Win]::SetForegroundWindow($h) | Out-Null }
    return
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

[CUR.Win]::SetDllDirectory($lib) | Out-Null                       # WebView2Loader.dll next to the SDK dlls
[CUR.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null  # per-monitor v2: sharp text
Add-Type -Path (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll')
Add-Type -Path (Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll')
Add-Type -ReferencedAssemblies System.Windows.Forms, (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll') -TypeDefinition @"
using System;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
public static class CurWeb
{
    // own data folder, then the page; a failure is written to the log next to it
    public static async void Start(WebView2 web, string dataDir, string url, string log)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Navigate(url);
        }
        catch (Exception ex) { System.IO.File.AppendAllText(log, DateTime.Now + " " + ex + Environment.NewLine); }
    }
}
"@
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object Windows.Forms.Form
$form.Text = 'claude using revit'
$form.BackColor = [Drawing.Color]::FromArgb(2, 3, 10)
$form.StartPosition = 'Manual'
$form.MinimumSize = New-Object Drawing.Size 420, 480
$iconPng = Get-ChildItem (Join-Path $env:APPDATA 'pyRevit\Extensions\ClaudeUsingRevit.extension') -Recurse -Filter icon.png -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*Print Set Sync*' } | Select-Object -First 1
if ($iconPng) { $form.Icon = [Drawing.Icon]::FromHandle((New-Object Drawing.Bitmap $iconPng.FullName).GetHicon()) }

# size and place of last time (only when that place is still on a screen)
$state = Join-Path $data 'app.json'
$area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$b = New-Object Drawing.Rectangle ($area.Left + [int]($area.Width * 0.15)), ($area.Top + [int]($area.Height * 0.08)), ([int]($area.Width * 0.7)), ([int]($area.Height * 0.84))
$max = $false
try {
    $s = Get-Content $state -Raw | ConvertFrom-Json
    $r = New-Object Drawing.Rectangle $s.x, $s.y, $s.w, $s.h
    if ([Windows.Forms.Screen]::AllScreens | Where-Object { $_.WorkingArea.IntersectsWith($r) }) { $b = $r; $max = [bool]$s.max }
} catch { }
$form.Bounds = $b
if ($max) { $form.WindowState = 'Maximized' }
$form.add_FormClosing({
    $r = if ($form.WindowState -eq 'Normal') { $form.Bounds } else { $form.RestoreBounds }
    @{ x = $r.X; y = $r.Y; w = $r.Width; h = $r.Height; max = ($form.WindowState -eq 'Maximized') } | ConvertTo-Json | Set-Content $state -Encoding ascii
})

$web = New-Object Microsoft.Web.WebView2.WinForms.WebView2
$web.Dock = 'Fill'
$web.DefaultBackgroundColor = $form.BackColor
$form.Controls.Add($web)
$form.add_Shown({ [CurWeb]::Start($web, (Join-Path $data 'app-webview'), $Url, (Join-Path $data 'app.log')) })
[System.Windows.Forms.Application]::Run($form)
$mutex.ReleaseMutex()
