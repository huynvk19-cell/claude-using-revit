# Draw the ribbon icons (96 x 96 icon.png) of the "claude using revit" tab with WPF: the objects of Revit's own ribbon
# (grey with a dark slate outline, one accent: blue, red or gold) on a plate of mica, slightly raised: light from the top
# left, a darker side below, a soft shadow under the plate and under the object. Drawn on a 32 x 32 grid, the size Revit
# shows it at.
#   powershell -STA -File pyrevit\make_icons.ps1 [-Preview <png>]   then install.ps1 -PyRevit + pyRevit > Reload
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$tab = Join-Path $PSScriptRoot 'ClaudeUsingRevit.extension\claude using revit.tab'

$ink = '#3E444C'
function Grad($a, $b) { "<LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='$a' Offset='0'/><GradientStop Color='$b' Offset='1'/></LinearGradientBrush>" }
$grey = Grad '#FBFCFD' '#C3C8CF'
$greyDark = Grad '#D5D9DF' '#979EA8'
$blue = Grad '#B9E3FA' '#4CA6E2'
$red = Grad '#FF8A80' '#E0352B'
$gold = Grad '#FFD978' '#E89B1C'

function P($data, $fill, $stroke = $ink, $w = 1) {
    "<Path Data='$data' StrokeThickness='$w' Stroke='$stroke' StrokeLineJoin='Round'><Path.Fill>$fill</Path.Fill></Path>"
}
function L($data, $stroke = $ink, $w = 1) { "<Path Data='$data' Stroke='$stroke' StrokeThickness='$w' StrokeLineJoin='Round'/>" }

# a sheet with a folded corner
function Sheet($x = 6, $y = 3, $w = 20, $h = 26, $fold = 6) {
    $r = $x + $w; $b = $y + $h; $f = $r - $fold; $g = $y + $fold
    (P "M $x.5,$y.5 L $f.5,$y.5 L $r.5,$g.5 L $r.5,$b.5 L $x.5,$b.5 Z" $grey) +
    (P "M $f.5,$y.5 L $f.5,$g.5 L $r.5,$g.5 Z" $greyDark)
}
# a schedule: dark header, grid, optional blue cells given as path data
function Table($cells = '') {
    (P 'M 3.5,6.5 L 28.5,6.5 L 28.5,26.5 L 3.5,26.5 Z' $grey) +
    $(if ($cells) { "<Path Data='$cells'><Path.Fill>$blue</Path.Fill></Path>" }) +
    (P 'M 3.5,6.5 L 28.5,6.5 L 28.5,11.5 L 3.5,11.5 Z' (Grad '#6A727C' '#454B53')) +
    (L 'M 3.5,16.5 L 28.5,16.5 M 3.5,21.5 L 28.5,21.5 M 11.5,11.5 L 11.5,26.5 M 20.5,11.5 L 20.5,26.5' '#7F8791')
}
# a cube seen from above, centred about (cx, cy), half width s
function Cube($cx = 15, $cy = 16, $s = 8) {
    $t = $cy - $s; $q = [int]($s / 2); $m = $t + $q; $n = $m + $q; $b = $cy + $s; $o = $b - $q
    $l = $cx - $s; $r = $cx + $s
    (P "M $cx.5,$t.5 L $r.5,$m.5 L $cx.5,$n.5 L $l.5,$m.5 Z" (Grad '#FFFFFF' '#E4E7EB')) +
    (P "M $l.5,$m.5 L $cx.5,$n.5 L $cx.5,$b.5 L $l.5,$o.5 Z" (Grad '#E2E5E9' '#B9BEC6')) +
    (P "M $r.5,$m.5 L $cx.5,$n.5 L $cx.5,$b.5 L $r.5,$o.5 Z" (Grad '#B5BBC3' '#858C96'))
}
# corners of a selection around an object, as on Revit's view tools
function Corners($stroke = $ink, $a = 2, $b = 29, $n = 5) {
    $c = $a + $n; $d = $b - $n
    L "M $a.5,$c.5 L $a.5,$a.5 L $c.5,$a.5 M $d.5,$a.5 L $b.5,$a.5 L $b.5,$c.5 M $b.5,$d.5 L $b.5,$b.5 L $d.5,$b.5 M $c.5,$b.5 L $a.5,$b.5 L $a.5,$d.5" $stroke 1.2
}
# a red cross in the lower right corner
function RedX {
    P 'M 19,16 L 23,20 L 27,16 L 30,19 L 26,23 L 30,27 L 27,30 L 23,26 L 19,30 L 16,27 L 20,23 L 16,19 Z' $red '#9E1F17' 1
}
# a tag: a label with a point on the left
function TagShape {
    P 'M 2.5,13.5 L 9.5,6.5 L 25.5,6.5 L 25.5,20.5 L 9.5,20.5 Z' $grey
}

$icons = [ordered]@{
    # the schedule editor: a table with a row in blue and a pencil
    'Panel.panel\Open Panel.pushbutton' = (Table 'M 12,12 L 28,12 L 28,16 L 12,16 Z') +
        (P 'M 16.5,31.5 L 17.5,26.5 L 27.5,16.5 L 31.5,20.5 L 21.5,30.5 Z' $gold '#8A5A08' 1) +
        (L 'M 17.5,26.5 L 21.5,30.5' '#8A5A08')
    # an object in its selection corners with the red see-through region over it
    'Highlight.panel\Highlight.pushbutton' = (Corners) + (Cube 15 16 8) +
        "<Path Data='M 6,7 L 25,7 L 25,25 L 6,25 Z' Stroke='#C4261C' StrokeThickness='1.2' Fill='#73E0352B'/>"
    # the same object, its region gone
    'Highlight.panel\Clear Highlight.pushbutton' = (Corners) + (Cube 13 14 8) + (RedX)
    # an object and a funnel, with a plus for "add to"
    'Filter.panel\Add To Filter.pushbutton' = (Corners '#3E444C' 2 21 4) + (Cube 11 11 6) +
        (P 'M 13.5,14.5 L 30.5,14.5 L 30.5,16.5 L 24.5,23.5 L 24.5,30.5 L 19.5,28.5 L 19.5,23.5 L 13.5,16.5 Z' $grey) +
        (P 'M 3,23 L 6,23 L 6,20 L 10,20 L 10,23 L 13,23 L 13,27 L 10,27 L 10,30 L 6,30 L 6,27 L 3,27 Z' $blue '#1D6FAE' 1)
    # a tag with the gold mark of something missing
    'Untagged.panel\Highlight Untagged.pushbutton' = (TagShape) +
        (L 'M 12.5,11.5 L 22.5,11.5 M 12.5,15.5 L 19.5,15.5' '#7F8791') +
        (P 'M 22.5,14.5 L 30.5,29.5 L 14.5,29.5 Z' $gold '#8A5A08' 1) +
        (L 'M 22.5,19.5 L 22.5,24.5 M 22.5,26 L 22.5,27.5' '#3A2600' 1.4)
    # the tag with a red cross
    'Untagged.panel\Remove Untagged.pushbutton' = (TagShape) +
        (L 'M 12.5,11.5 L 22.5,11.5' '#7F8791') + (RedX)
    # sheets kept in step: two sheets and a blue arrow
    'PrintSet.panel\Print Set Sync.pushbutton' = (Sheet 9 2 17 21 5) + (Sheet 4 7 17 21 5) +
        (L 'M 8.5,17.5 L 16.5,17.5 M 8.5,21.5 L 16.5,21.5' '#7F8791') +
        (P 'M 18,19 A 7,7 0 0 1 30,19 L 31,15 L 31,23 L 24,23 L 27,20 A 4,4 0 0 0 21,21 Z' $blue '#1D6FAE' 1) +
        (P 'M 30,26 A 7,7 0 0 1 18,27 L 17,31 L 17,24 L 24,24 L 21,27 A 4,4 0 0 0 27,25 Z' $blue '#1D6FAE' 1)
    # the sheet list: a table with one column in blue
    'PrintSet.panel\Sheet List Columns.pushbutton' = Table 'M 12,12 L 20,12 L 20,26 L 12,26 Z'
    # a sheet with a red PDF band
    'PrintSet.panel\Export PDF.pushbutton' = (Sheet 7 2 19 27 6) +
        (L 'M 11.5,10.5 L 18.5,10.5 M 11.5,25.5 L 22.5,25.5' '#7F8791') +
        (P 'M 2.5,13.5 L 26.5,13.5 L 26.5,22.5 L 2.5,22.5 Z' $red '#9E1F17' 1) +
        "<TextBlock Canvas.Left='3' Canvas.Top='12' Width='23' TextAlignment='Center' Text='PDF' FontFamily='Arial' FontWeight='Bold' FontSize='9.5' Foreground='White'/>"
    # a cube in blue corners: the 3D of a view
    'View3D.panel\3D From View.pushbutton' = (Corners '#1D6FAE') + (Cube 15 16 9)
}

function Icon($inner) {
    $xaml = @"
<Viewbox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='96' Height='96'>
  <Grid Width='32' Height='32'>
    <!-- shadow under the plate -->
    <Border Margin='1.5,3,1.5,0' CornerRadius='3.5' Background='#000000' Opacity='0.28'>
      <Border.Effect><BlurEffect Radius='1.6'/></Border.Effect>
    </Border>
    <!-- the side of the plate, then its face: frost lit from the top left, darker to the bottom right -->
    <Border Margin='1,2.5,1,0.8' CornerRadius='3.5' Background='#8C95A1'/>
    <Border Margin='1,1,1,2' CornerRadius='3.5'>
      <Border.Background>
        <LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
          <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#E9EDF2' Offset='0.45'/>
          <GradientStop Color='#C6CDD6' Offset='1'/>
        </LinearGradientBrush>
      </Border.Background>
    </Border>
    <!-- light on the top edge, shade on the lower edge -->
    <Border Margin='1,1,1,2' CornerRadius='3.5' BorderThickness='0.7'>
      <Border.BorderBrush>
        <LinearGradientBrush StartPoint='0,0' EndPoint='0,1'>
          <GradientStop Color='#FFFFFFFF' Offset='0'/><GradientStop Color='#80FFFFFF' Offset='0.4'/>
          <GradientStop Color='#338A93A0' Offset='1'/>
        </LinearGradientBrush>
      </Border.BorderBrush>
    </Border>
    <!-- the sheen across the mica -->
    <Path Data='M 2.2,2 L 21,2 L 8,18 L 2.2,24 Z' Opacity='0.55' IsHitTestVisible='False'>
      <Path.Fill>
        <LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
          <GradientStop Color='#A6FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/>
        </LinearGradientBrush>
      </Path.Fill>
    </Path>
    <!-- the object, a little smaller, standing on the plate -->
    <Canvas Width='32' Height='32' RenderTransformOrigin='0.5,0.5' Margin='0,-0.6,0,0.6'>
      <Canvas.RenderTransform><ScaleTransform ScaleX='0.76' ScaleY='0.76'/></Canvas.RenderTransform>
      <Canvas.Effect><DropShadowEffect Color='#1E242C' BlurRadius='2.2' ShadowDepth='1' Direction='290' Opacity='0.5'/></Canvas.Effect>
$inner
    </Canvas>
  </Grid>
</Viewbox>
"@
    $el = [Windows.Markup.XamlReader]::Parse($xaml)
    $el.Measure((New-Object Windows.Size 96, 96)); $el.Arrange((New-Object Windows.Rect 0, 0, 96, 96)); $el.UpdateLayout()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap 96, 96, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($el)
    $bmp
}

function SavePng($bmp, $path) {
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [IO.File]::Create($path); $enc.Save($fs); $fs.Close()
}

$all = foreach ($name in $icons.Keys) {
    $bmp = Icon $icons[$name]
    $out = Join-Path $tab "$name\icon.png"
    SavePng $bmp $out
    "icon -> $out" | Write-Host
    $bmp
}

if ($Preview) {  # strip at 96 px and at the 32 px Revit shows, on the ribbon grey
    $n = $all.Count
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRectangle((New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(240, 240, 240))), $null,
                      (New-Object Windows.Rect 0, 0, (110 * $n), 160))
    for ($k = 0; $k -lt $n; $k++) {
        $dc.DrawImage($all[$k], (New-Object Windows.Rect (7 + 110 * $k), 7, 96, 96))
        $dc.DrawImage($all[$k], (New-Object Windows.Rect (39 + 110 * $k), 118, 32, 32))
    }
    $dc.Close()
    $strip = New-Object Windows.Media.Imaging.RenderTargetBitmap (110 * $n), 160, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $strip.Render($dv)
    SavePng $strip $Preview
    "preview -> $Preview" | Write-Host
}
