# Draw the ribbon icons (96 x 96 icon.png) of the "claude using revit" tab with WPF, in today's manner: rounded shapes in
# layers with no outlines, each lit by its own gradient and lying on the next with a soft shadow, a round badge for the
# action, all on a slightly raised plate of mica (light from the top left, a darker side). Drawn on a 32 x 32 grid.
#   powershell -STA -File pyrevit\make_icons.ps1 [-Preview <png>]   then install.ps1 -PyRevit + pyRevit > Reload
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$tab = Join-Path $PSScriptRoot 'ClaudeUsingRevit.extension\claude using revit.tab'

function Grad($a, $b, $sx = 0, $sy = 0, $ex = 1, $ey = 1) { "<LinearGradientBrush StartPoint='$sx,$sy' EndPoint='$ex,$ey'><GradientStop Color='$a' Offset='0'/><GradientStop Color='$b' Offset='1'/></LinearGradientBrush>" }
$paper = Grad '#FFFFFF' '#DDE6F0'
$cell = '#D3DDE9'
$blue = Grad '#6CC7FF' '#0F6CBD'
$red = Grad '#FF8F82' '#D13438'
$gold = Grad '#FFD86B' '#F39A0B'
$teal = Grad '#5BE3C8' '#0B8F82'
$slate = Grad '#8C9BB0' '#4A5A70'

# no outlines: a shape is its own soft gradient, its corners rounded by a stroke of the same fill
function P($data, $fill) {
    "<Path Data='$data' StrokeThickness='0.8' StrokeLineJoin='Round'><Path.Fill>$fill</Path.Fill><Path.Stroke>$fill</Path.Stroke></Path>"
}
function Box($x, $y, $w, $h, $r, $fill) {
    "<Rectangle Canvas.Left='$x' Canvas.Top='$y' Width='$w' Height='$h' RadiusX='$r' RadiusY='$r'><Rectangle.Fill>$fill</Rectangle.Fill></Rectangle>"
}
# a layer with its own small shadow, so layers read as lying on each other
function Layer($inner, $o = 0.35) {
    "<Canvas><Canvas.Effect><DropShadowEffect Color='#16202C' BlurRadius='1.8' ShadowDepth='0.7' Direction='280' Opacity='$o'/></Canvas.Effect>$inner</Canvas>"
}
# a round badge at the lower right with a white Fluent glyph
function Badge($fill, $glyph, $cx = 24, $cy = 24, $d = 13, $size = 7.5, $font = 'Segoe Fluent Icons, Segoe MDL2 Assets') {
    $x = $cx - $d / 2; $y = $cy - $d / 2
    Layer ("<Grid Canvas.Left='$x' Canvas.Top='$y' Width='$d' Height='$d'><Ellipse><Ellipse.Fill>$fill</Ellipse.Fill></Ellipse>" +
           "<Ellipse Margin='0.5' Opacity='0.55'><Ellipse.Stroke><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#FFFFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='0.6'/></LinearGradientBrush></Ellipse.Stroke></Ellipse>" +
           "<TextBlock Text='$glyph' FontFamily='$font' FontWeight='Bold' FontSize='$size' Foreground='White' HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid>") 0.45
}
$glyphX = [char]0xE711; $glyphPlus = [char]0xE710; $glyphSync = [char]0xE895; $glyphWarn = '!'

# a table as a card of rounded cells; $on lists the cells in blue as 'column,row'
function Table($on = @()) {
    $body = (Box 3 5 26 22 3.2 $paper) +
            "<Path Data='M 3,8.2 A 3.2,3.2 0 0 1 6.2,5 L 25.8,5 A 3.2,3.2 0 0 1 29,8.2 L 29,11 L 3,11 Z'><Path.Fill>$slate</Path.Fill></Path>"
    $cells = foreach ($c in 0..2) { foreach ($r in 0..2) {
        $fill = if ($on -contains "$c,$r") { $blue } else { $cell }
        Box (5 + 7.6 * $c) (12.8 + 4.6 * $r) 6.6 3.6 1 $fill
    } }
    Layer ($body + ($cells -join ''))
}
# a cube of three lit faces
function Cube($cx = 16, $cy = 16, $s = 10) {
    $t = $cy - $s; $q = $s / 2; $m = $t + $q; $n = $m + $q; $b = $cy + $s; $o = $b - $q; $l = $cx - $s; $r = $cx + $s
    Layer ((P "M $cx,$t L $r,$m L $cx,$n L $l,$m Z" (Grad '#EAF4FD' '#BBD6F0' 0 0 0 1)) +
           (P "M $l,$m L $cx,$n L $cx,$b L $l,$o Z" (Grad '#7CC0F5' '#2F86D6' 0 0 0 1)) +
           (P "M $r,$m L $cx,$n L $cx,$b L $r,$o Z" (Grad '#2F7FCB' '#154E94' 0 0 0 1)))
}
function Sheet($x = 6, $y = 3, $w = 20, $h = 26, $fill = $paper) {
    $r = $x + $w; $b = $y + $h; $f = $r - 7; $g = $y + 7; $a = $x + 2.4; $c = $r - 2.4; $d = $b - 2.4; $e = $y + 2.4
    Layer ((P "M $a,$y L $f,$y L $r,$g L $r,$d Q $r,$b $c,$b L $a,$b Q $x,$b $x,$d L $x,$e Q $x,$y $a,$y Z" $fill) +
           (P "M $f,$y L $r,$g L $($f + 2),$g Q $f,$g $f,$($g - 2) Z" (Grad '#C9D6E5' '#94A6BC')))
}
function Tag($fill) {
    Layer ((P 'M 3.5,15 L 12,6.6 Q 13,5.6 14.6,5.6 L 25,5.6 Q 28,5.6 28,8.6 L 28,21.4 Q 28,24.4 25,24.4 L 14.6,24.4 Q 13,24.4 12,23.4 Z' $fill) +
           "<Ellipse Canvas.Left='10' Canvas.Top='12.5' Width='5' Height='5' Fill='#E6FFFFFF'/>")
}

$icons = [ordered]@{
    # the schedule editor: a table with a row in blue and a pencil
    'Panel.panel\Open Panel.pushbutton' = (Table @('0,0', '1,0', '2,0')) +
        (Layer ((P 'M 17,29.5 L 18,25 L 26.5,16.5 Q 28,15 29.5,16.5 L 30.2,17.2 Q 31.5,18.7 30,20.2 L 21.5,28.7 Z' $gold) +
                (P 'M 17,29.5 L 18,25 L 21.5,28.7 Z' (Grad '#FFF1CF' '#E9C58A'))) 0.45)
    # an object under the red see-through region
    'Highlight.panel\Highlight.pushbutton' = (Cube 16 16 10) +
        "<Rectangle Canvas.Left='3.5' Canvas.Top='5' Width='25' Height='22' RadiusX='4' RadiusY='4' Fill='#55E5453A' Stroke='#E6D13438' StrokeThickness='1'/>"
    # the object, its region gone
    'Highlight.panel\Clear Highlight.pushbutton' = (Cube 13 13 10) + (Badge $red $glyphX)
    # a funnel with a plus: add to a filter
    'Filter.panel\Add To Filter.pushbutton' = (Layer (P 'M 3.5,6.5 Q 3.5,4 6,4 L 26,4 Q 28.5,4 28.5,6.5 Q 28.5,8.2 27.2,9.6 L 19,18.5 L 19,26 Q 19,27.2 18,27.7 L 14.8,29.3 Q 13,30 13,28 L 13,18.5 L 4.8,9.6 Q 3.5,8.2 3.5,6.5 Z' $teal)) +
        (Badge $blue $glyphPlus 24.5 24.5)
    # a tag with the gold mark of something missing
    'Untagged.panel\Highlight Untagged.pushbutton' = (Tag $slate) + (Badge $gold $glyphWarn 24.5 24 13 10.5 'Segoe UI')
    # the tag taken away
    'Untagged.panel\Remove Untagged.pushbutton' = (Tag $slate) + (Badge $red $glyphX 24.5 24)
    # sheets kept in step
    'PrintSet.panel\Print Set Sync.pushbutton' = (Sheet 10 2 17 22 (Grad '#B9D9F5' '#7FB0E0')) + (Sheet 4 6 17 22) +
        (Box 7.5 16 10 1.8 0.9 $cell) + (Box 7.5 20 7 1.8 0.9 $cell) + (Badge $blue $glyphSync 24 24 13 8)
    # the sheet list: a table with one column in blue
    'PrintSet.panel\Sheet List Columns.pushbutton' = Table @('1,0', '1,1', '1,2')
    # a sheet with a red PDF band
    'PrintSet.panel\Export PDF.pushbutton' = (Sheet 7 2 20 27) +
        (Layer ((Box 2.5 14 24 10 2.4 $red) +
                "<TextBlock Canvas.Left='2.5' Canvas.Top='13.8' Width='24' TextAlignment='Center' Text='PDF' FontFamily='Segoe UI' FontWeight='Bold' FontSize='8.5' Foreground='White'/>") 0.45)
    # a cube in soft corners: the 3D of a view
    'View3D.panel\3D From View.pushbutton' = (Cube 16 16 9.5) +
        "<Path Data='M 3,9 L 3,6 Q 3,3 6,3 L 9,3 M 23,3 L 26,3 Q 29,3 29,6 L 29,9 M 29,23 L 29,26 Q 29,29 26,29 L 23,29 M 9,29 L 6,29 Q 3,29 3,26 L 3,23' Stroke='#0F6CBD' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>"
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
          <GradientStop Color='#F6F8FA' Offset='0'/><GradientStop Color='#DCE1E8' Offset='0.45'/>
          <GradientStop Color='#B4BCC8' Offset='1'/>
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
      <Canvas.RenderTransform><ScaleTransform ScaleX='0.8' ScaleY='0.8'/></Canvas.RenderTransform>
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
