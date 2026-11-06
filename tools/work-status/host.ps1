# Standalone "work in progress" window (own process, always on top of every app).
# Reads status.json next to this script every second: { title, message, step, state }.
# state = "close" (or the file is deleted) closes the window.
param([string]$StatusFile = (Join-Path $PSScriptRoot 'status.json'))

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$pidFile = Join-Path $PSScriptRoot 'host.pid'
Set-Content -Path $pidFile -Value $PID

function Read-Status {
  try { return (Get-Content -LiteralPath $StatusFile -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}

# ---------- pixel-art fisherman (24 x 24 grid, 4 px per cell) ----------
$S = 4
$colors = New-Object System.Collections.Hashtable ([StringComparer]::Ordinal)
$colors['O'] = '#D97757'   # body (warm orange)
$colors['K'] = '#2B1B14'   # eyes
$colors['R'] = '#8B5A2B'   # rod
$colors['D'] = '#9C6B3F'   # dock
$colors['d'] = '#6E4A2A'   # dock post
$colors['W'] = '#3E8ED0'   # water
$colors['w'] = '#7FC0F0'   # wave highlight
$grid = @(
  '........................',
  '........................',
  '........................',
  '........................',
  '.....................R..',
  '....................R...',
  '...................R....',
  '..................R.....',
  '.................R......',
  '................R.......',
  '....OOOOOOOO...R........',
  '....OOOOOOOO..R.........',
  '....OKOOOOKO.R..........',
  '..OOOOOOOOOOOR..........',
  '..OOOOOOOOOOOO..........',
  '....OOOOOOOO............',
  '....O.O..O.O............',
  '....O.O..O.O............',
  'DDDDDDDDDDDDD...........',
  '..d......d..WWWWWWWWWWWW',
  '..d......d.WWWwWWWWWwWWW',
  'WWdWWWWWWdWWWWWWWWWWWWWW',
  'WWWWwWWWWWWWWWwWWWWWWWWW',
  'WWWWWWWWWWWWWWWWWWWWWWWW'
)
$canvas = New-Object System.Windows.Controls.Canvas
$canvas.Width = 24 * $S; $canvas.Height = 24 * $S
$brushes = New-Object System.Collections.Hashtable ([StringComparer]::Ordinal)
foreach ($k in $colors.Keys) { $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($colors[$k])); $b.Freeze(); $brushes[$k] = $b }
$waveCells = New-Object System.Collections.ArrayList
for ($y = 0; $y -lt 24; $y++) {
  for ($x = 0; $x -lt 24; $x++) {
    $c = [string]$grid[$y][$x]
    if ($c -eq '.') { continue }
    $r = New-Object System.Windows.Shapes.Rectangle
    $r.Width = $S; $r.Height = $S; $r.Fill = $brushes[$c]
    [System.Windows.Controls.Canvas]::SetLeft($r, $x * $S); [System.Windows.Controls.Canvas]::SetTop($r, $y * $S)
    [void]$canvas.Children.Add($r)
    if ($c -eq 'W' -or $c -eq 'w') { [void]$waveCells.Add(@($r, $x, $y)) }
  }
}
# fishing line from rod tip down to the bobber
$line = New-Object System.Windows.Shapes.Line
$line.X1 = 21.5 * $S; $line.Y1 = 4.5 * $S; $line.X2 = 21.5 * $S; $line.Y2 = 18 * $S
$line.Stroke = [System.Windows.Media.Brushes]::LightGray; $line.StrokeThickness = 1
[void]$canvas.Children.Add($line)
# bobber (red top, white bottom)
$bobTop = New-Object System.Windows.Shapes.Rectangle; $bobTop.Width = $S * 2; $bobTop.Height = $S; $bobTop.Fill = [System.Windows.Media.Brushes]::Crimson
$bobBot = New-Object System.Windows.Shapes.Rectangle; $bobBot.Width = $S * 2; $bobBot.Height = $S; $bobBot.Fill = [System.Windows.Media.Brushes]::White
[void]$canvas.Children.Add($bobTop); [void]$canvas.Children.Add($bobBot)
# "!" when a fish bites
$bang = New-Object System.Windows.Controls.TextBlock; $bang.Text = '!'; $bang.FontSize = 18; $bang.FontWeight = 'Bold'
$bang.Foreground = [System.Windows.Media.Brushes]::Gold; $bang.Visibility = 'Hidden'
[System.Windows.Controls.Canvas]::SetLeft($bang, 7 * $S); [System.Windows.Controls.Canvas]::SetTop($bang, 4 * $S)
[void]$canvas.Children.Add($bang)
function Set-Bobber([double]$dy) {
  $x = 20.5 * $S; $y = 17.5 * $S + $dy
  [System.Windows.Controls.Canvas]::SetLeft($bobTop, $x); [System.Windows.Controls.Canvas]::SetTop($bobTop, $y)
  [System.Windows.Controls.Canvas]::SetLeft($bobBot, $x); [System.Windows.Controls.Canvas]::SetTop($bobBot, $y + $S)
  $line.Y2 = $y
}
Set-Bobber 0

# ---------- window ---------- (same look as the add-in's tool windows: lib/liquid_glass.xaml, lib/native_blur.py)
Add-Type -Namespace WorkStatus -Name Native -MemberDefinition @'
[DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
[DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
[DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
'@
function Brush([string]$c) { $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($c)); $b.Freeze(); return $b }
$ink = Brush '#1D1D1F'; $ink2 = Brush '#6E6E73'; $fill2 = Brush '#1F767680'; $tile = Brush '#61FFFFFF'; $rim = Brush '#24000000'
$accent = New-Object System.Windows.Media.LinearGradientBrush ([System.Windows.Media.ColorConverter]::ConvertFromString('#2E95FF')), ([System.Windows.Media.ColorConverter]::ConvertFromString('#0071E3')), 90
$font = New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Text, Segoe UI'
$display = New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Display, Segoe UI'

$st = Read-Status
$w = New-Object System.Windows.Window
$w.Title = 'Đang xử lý'
$w.Width = 440; $w.Height = 148
$w.ResizeMode = 'NoResize'; $w.WindowStyle = 'SingleBorderWindow'
$w.Topmost = $true; $w.ShowActivated = $false; $w.ShowInTaskbar = $true
$wa = [System.Windows.SystemParameters]::WorkArea
$w.Left = $wa.Right - $w.Width - 12; $w.Top = $wa.Bottom - $w.Height - 12
$w.Background = [System.Windows.Media.Brushes]::Transparent
$w.FontFamily = $font; $w.Foreground = $ink
$w.SetValue([System.Windows.Media.TextOptions]::TextFormattingModeProperty, [System.Windows.Media.TextFormattingMode]::Display)
$chrome = New-Object System.Windows.Shell.WindowChrome
$chrome.CaptionHeight = 0; $chrome.ResizeBorderThickness = 0; $chrome.CornerRadius = 0; $chrome.GlassFrameThickness = -1; $chrome.UseAeroCaptionButtons = $false
[System.Windows.Shell.WindowChrome]::SetWindowChrome($w, $chrome)
$w.Add_SourceInitialized({
  $hwnd = (New-Object System.Windows.Interop.WindowInteropHelper $w).Handle
  $blur = $false
  try {
    [void][WorkStatus.Native]::SetWindowLong($hwnd, -16, ([WorkStatus.Native]::GetWindowLong($hwnd, -16) -band (-bnot 0x00080000)))   # no DWM close button
    $v = 0; [void][WorkStatus.Native]::DwmSetWindowAttribute($hwnd, 20, [ref]$v, 4)      # light: the ink is dark
    $v = 2; [void][WorkStatus.Native]::DwmSetWindowAttribute($hwnd, 33, [ref]$v, 4)      # round corners
    $v = 3; $blur = [WorkStatus.Native]::DwmSetWindowAttribute($hwnd, 38, [ref]$v, 4) -eq 0   # Acrylic
  } catch { }
  if ($blur) { [System.Windows.Interop.HwndSource]::FromHwnd($hwnd).CompositionTarget.BackgroundColor = [System.Windows.Media.Colors]::Transparent }
  else { $w.Background = Brush '#F4F5F8' }
})
$w.Add_MouseLeftButtonDown({ try { $w.DragMove() } catch { } })

$root = New-Object System.Windows.Controls.DockPanel; $root.Margin = '12,12,14,12'
$pic = New-Object System.Windows.Controls.Border; $pic.Child = $canvas; $pic.Margin = '0,0,14,0'; $pic.CornerRadius = 12
$pic.Background = Brush '#EAF4FB'; $pic.VerticalAlignment = 'Center'
$pic.ClipToBounds = $true
[System.Windows.Controls.DockPanel]::SetDock($pic, 'Left'); [void]$root.Children.Add($pic)
$closeBtn = [System.Windows.Markup.XamlReader]::Parse(@'
<Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="26" Height="26" Cursor="Hand" ToolTip="Đóng" Foreground="#6E6E73" VerticalAlignment="Top">
  <Button.Template>
    <ControlTemplate TargetType="Button">
      <Grid>
        <Ellipse x:Name="e" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Fill="#61FFFFFF" Stroke="#24000000" StrokeThickness="1"/>
        <TextBlock Text="&#xE711;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="9" FontWeight="Bold" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="e" Property="Fill" Value="#99FFFFFF"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Button.Template>
</Button>
'@)
$closeBtn.Add_Click({ $w.Close() })
[System.Windows.Controls.DockPanel]::SetDock($closeBtn, 'Right'); $closeBtn.Margin = '8,0,0,0'; [void]$root.Children.Add($closeBtn)
$sp = New-Object System.Windows.Controls.StackPanel; $sp.VerticalAlignment = 'Center'
$tTitle = New-Object System.Windows.Controls.TextBlock; $tTitle.FontFamily = $display; $tTitle.FontWeight = 'SemiBold'; $tTitle.FontSize = 15; $tTitle.TextTrimming = 'CharacterEllipsis'; $tTitle.Foreground = $ink
$tMsg = New-Object System.Windows.Controls.TextBlock; $tMsg.FontSize = 12; $tMsg.Margin = '0,3,0,8'; $tMsg.TextWrapping = 'Wrap'; $tMsg.MaxHeight = 34; $tMsg.Foreground = $ink
# the theme's bar (liquid_glass.xaml, Bar) with a running piece: it has no end to reach
$bar = New-Object System.Windows.Controls.Border; $bar.Height = 6; $bar.CornerRadius = 3; $bar.Background = $fill2; $bar.ClipToBounds = $true
$barRun = New-Object System.Windows.Controls.Border; $barRun.Width = 70; $barRun.CornerRadius = 3; $barRun.Background = $accent; $barRun.HorizontalAlignment = 'Left'
$barMove = New-Object System.Windows.Media.TranslateTransform; $barRun.RenderTransform = $barMove; $bar.Child = $barRun
$tFoot = New-Object System.Windows.Controls.TextBlock; $tFoot.FontSize = 11; $tFoot.Foreground = $ink2; $tFoot.Margin = '0,6,0,0'
foreach ($c in $tTitle, $tMsg, $bar, $tFoot) { [void]$sp.Children.Add($c) }
[void]$root.Children.Add($sp)
$w.Content = $root

$script:step = ''
function Apply($s) {
  if ($null -eq $s) { return }
  if ($s.title) { $tTitle.Text = $s.title }
  if ($s.message) { $tMsg.Text = $s.message }
  if ($null -ne $s.step) { $script:step = [string]$s.step }
}
Apply $st

# taskbar icon = the pixel art
$w.Add_ContentRendered({
  try {
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap 96, 96, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($canvas); $w.Icon = $rtb
  } catch { }
})

$start = Get-Date
$script:tick = 0
$anim = New-Object System.Windows.Threading.DispatcherTimer
$anim.Interval = [TimeSpan]::FromMilliseconds(120)
$anim.Add_Tick({
  $script:tick++
  $t = $script:tick
  if ($bar.ActualWidth -gt 0) { $span = $bar.ActualWidth + 70; $barMove.X = (($t * 9) % $span) - 70 }
  $phase = $t % 70                      # a bite every ~8.4 s
  if ($phase -ge 60) { Set-Bobber ((($phase - 60) % 2) * $S + $S); $bang.Visibility = 'Visible' }
  else { Set-Bobber ([math]::Round([math]::Sin($t / 4.0) * 1.5) * 1.0); $bang.Visibility = 'Hidden' }
  if ($t % 4 -eq 0) {                   # waves drift
    foreach ($cell in $waveCells) {
      $isHi = ((($cell[1] + [int]($t / 4)) % 9) -eq 0) -and ($cell[2] -ge 20)
      $cell[0].Fill = if ($isHi) { $brushes['w'] } else { $brushes['W'] }
    }
  }
})
$anim.Start()

$poll = New-Object System.Windows.Threading.DispatcherTimer
$poll.Interval = [TimeSpan]::FromSeconds(1)
$poll.Add_Tick({
  $el = (Get-Date) - $start
  $tFoot.Text = 'Thời gian: ' + ('{0:00}:{1:00}' -f [int][math]::Floor($el.TotalMinutes), $el.Seconds) + $(if ($script:step) { '   ·   Bước ' + $script:step } else { '' })
  if (-not (Test-Path -LiteralPath $StatusFile)) { $w.Close(); return }
  $s = Read-Status
  if ($s -and $s.state -eq 'close') { $w.Close(); return }
  Apply $s
})
$poll.Start()

$w.Add_Closed({ $anim.Stop(); $poll.Stop(); Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue })
$app = New-Object System.Windows.Application
[void]$app.Run($w)
