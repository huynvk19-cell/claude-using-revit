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

# ---------- window ----------
$st = Read-Status
$w = New-Object System.Windows.Window
$w.Title = 'Đang xử lý'
$w.Width = 430; $w.Height = 150
$w.ResizeMode = 'NoResize'; $w.WindowStyle = 'ToolWindow'
$w.Topmost = $true; $w.ShowActivated = $false; $w.ShowInTaskbar = $true
$wa = [System.Windows.SystemParameters]::WorkArea
$w.Left = $wa.Right - $w.Width - 12; $w.Top = $wa.Bottom - $w.Height - 12
$w.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString('#FAF9F5'))

$root = New-Object System.Windows.Controls.DockPanel; $root.Margin = '10,8,12,8'
$pic = New-Object System.Windows.Controls.Border; $pic.Child = $canvas; $pic.Margin = '0,0,12,0'; $pic.CornerRadius = 6
$pic.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString('#EAF4FB'))
$pic.ClipToBounds = $true
[System.Windows.Controls.DockPanel]::SetDock($pic, 'Left'); [void]$root.Children.Add($pic)
$sp = New-Object System.Windows.Controls.StackPanel; $sp.VerticalAlignment = 'Center'
$tTitle = New-Object System.Windows.Controls.TextBlock; $tTitle.FontWeight = 'Bold'; $tTitle.FontSize = 13; $tTitle.TextTrimming = 'CharacterEllipsis'
$tMsg = New-Object System.Windows.Controls.TextBlock; $tMsg.FontSize = 12; $tMsg.Margin = '0,4,0,6'; $tMsg.TextWrapping = 'Wrap'; $tMsg.MaxHeight = 34
$bar = New-Object System.Windows.Controls.ProgressBar; $bar.IsIndeterminate = $true; $bar.Height = 8
$tFoot = New-Object System.Windows.Controls.TextBlock; $tFoot.FontSize = 11; $tFoot.Foreground = [System.Windows.Media.Brushes]::Gray; $tFoot.Margin = '0,5,0,0'
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
