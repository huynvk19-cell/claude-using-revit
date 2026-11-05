# Draw the panel icon (macOS style: a squircle with depth, light from the top) with WPF: the ribbon button
# (icon.png, 96 px) and the web page icon (webapp/public/app-icon.png, 256 px). Designs: A sheet (drawing sheet; -Palette orange = the one in use, or blue), B galaxy (schedule grid on glass over
# the galaxy, like the page), C tower (white building on warm orange).
#   powershell -STA -File aiculi\icons\make_app_icon.ps1 [-Design A] [-Palette orange] [-Preview <png with all designs>]
param([string]$Design = 'A', [ValidateSet('orange', 'blue')][string]$Palette = 'orange', [string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# superellipse |x|^5 + |y|^5 = r^5 (the Apple icon shape) as path data
function Squircle([double]$cx, [double]$cy, [double]$r, [double]$n = 5) {
    $pts = for ($i = 0; $i -lt 240; $i++) {
        $t = 2 * [Math]::PI * $i / 240
        $c = [Math]::Cos($t); $s = [Math]::Sin($t)
        '{0:0.#},{1:0.#}' -f ($cx + $r * [Math]::Sign($c) * [Math]::Pow([Math]::Abs($c), 2 / $n)),
                             ($cy + $r * [Math]::Sign($s) * [Math]::Pow([Math]::Abs($s), 2 / $n))
    }
    'M ' + ($pts -join ' L ') + ' Z'
}
$sq = Squircle 512 512 468
$plate = Squircle 512 512 250

# shared: shadow under the body, gloss from the top, a thin light rim
function Body($fill, $inner) {
@"
<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="1024" Height="1024">
  <Path Data="$sq" Fill="#000000" Opacity="0.35"><Path.RenderTransform><TranslateTransform Y="14"/></Path.RenderTransform>
    <Path.Effect><BlurEffect Radius="26"/></Path.Effect></Path>
  <Canvas><Canvas.Clip><PathGeometry Figures="$sq"/></Canvas.Clip>
    <Rectangle Width="1024" Height="1024">$fill</Rectangle>
$inner
    <Rectangle Width="1024" Height="1024">
      <Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
        <GradientStop Color="#40FFFFFF" Offset="0"/><GradientStop Color="#00FFFFFF" Offset="0.45"/>
        <GradientStop Color="#00000000" Offset="0.7"/><GradientStop Color="#33000000" Offset="1"/>
      </LinearGradientBrush></Rectangle.Fill></Rectangle>
  </Canvas>
  <Path Data="$sq" StrokeThickness="5"><Path.Stroke><LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="#80FFFFFF" Offset="0"/><GradientStop Color="#14FFFFFF" Offset="0.5"/><GradientStop Color="#33FFFFFF" Offset="1"/>
  </LinearGradientBrush></Path.Stroke></Path>
</Canvas>
"@
}

function Design-A {   # a drawing sheet (plan + title block) on orange (Huy 2026-11-03: A with the colours of C) or blue
    $p = if ($Palette -eq "blue") { @{ top = "#4C8DFF"; bottom = "#1B3FB8"; shadow = "#0A1A5C"; strip = "#E3E9F7"; mark = "#1B3FB8"; line = "#9DB0DC"; ink = "#1D2A4D"; accent = "#FF8A1F" } }
         else { @{ top = "#FFC24A"; bottom = "#FF5A2E"; shadow = "#7A2000"; strip = "#FBE9DC"; mark = "#E8501F"; line = "#E9B99A"; ink = "#3A2416"; accent = "#FF6A1F" } }
    $fill = "<Rectangle.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0.3,1'><GradientStop Color='$($p.top)' Offset='0'/><GradientStop Color='$($p.bottom)' Offset='1'/></LinearGradientBrush></Rectangle.Fill>"
    $inner = @"
    <Canvas>
      <Canvas.RenderTransform><RotateTransform Angle="-7" CenterX="512" CenterY="540"/></Canvas.RenderTransform>
      <Rectangle Canvas.Left="210" Canvas.Top="300" Width="604" Height="440" RadiusX="22" RadiusY="22" Fill="#FFFFFF">
        <Rectangle.Effect><DropShadowEffect BlurRadius="40" ShadowDepth="16" Direction="270" Opacity="0.45" Color="$($p.shadow)"/></Rectangle.Effect></Rectangle>
      <Rectangle Canvas.Left="690" Canvas.Top="300" Width="124" Height="440" Fill="$($p.strip)"/>
      <Rectangle Canvas.Left="708" Canvas.Top="640" Width="88" Height="12" RadiusX="6" RadiusY="6" Fill="$($p.mark)"/>
      <Rectangle Canvas.Left="708" Canvas.Top="670" Width="60" Height="10" RadiusX="5" RadiusY="5" Fill="$($p.line)"/>
      <Rectangle Canvas.Left="708" Canvas.Top="694" Width="74" Height="10" RadiusX="5" RadiusY="5" Fill="$($p.line)"/>
      <Path Stroke="$($p.ink)" StrokeThickness="16" StrokeLineJoin="Round" StrokeStartLineCap="Round" StrokeEndLineCap="Round"
            Data="M 260,350 L 640,350 L 640,690 L 260,690 Z M 450,350 L 450,560 M 260,560 L 400,560 M 490,560 L 640,560"/>
      <Path Stroke="$($p.accent)" StrokeThickness="10" Data="M 330,630 L 380,630 M 540,450 L 590,450" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
    </Canvas>
"@
    Body $fill $inner
}

function Design-B {   # a schedule grid on frosted glass over the galaxy (the page's look)
    $fill = '<Rectangle.Fill><RadialGradientBrush Center="0.35,0.3" GradientOrigin="0.35,0.3" RadiusX="0.9" RadiusY="0.9"><GradientStop Color="#3A2A7A" Offset="0"/><GradientStop Color="#141A3D" Offset="0.5"/><GradientStop Color="#05060F" Offset="1"/></RadialGradientBrush></Rectangle.Fill>'
    $rnd = New-Object Random 7
    $stars = (1..70 | ForEach-Object { $s = 3 + $rnd.NextDouble() * 6
        "<Ellipse Canvas.Left=`"$([int]($rnd.NextDouble()*1000))`" Canvas.Top=`"$([int]($rnd.NextDouble()*1000))`" Width=`"$s`" Height=`"$s`" Fill=`"#FFFFFF`" Opacity=`"$('{0:0.00}' -f (0.25 + $rnd.NextDouble()*0.6))`"/>" }) -join "`n"
    $inner = @"
    <Canvas>$stars</Canvas>
    <Ellipse Canvas.Left="60" Canvas.Top="420" Width="904" Height="260" StrokeThickness="46" Opacity="0.85">
      <Ellipse.RenderTransform><RotateTransform Angle="-24" CenterX="452" CenterY="130"/></Ellipse.RenderTransform>
      <Ellipse.Stroke><LinearGradientBrush StartPoint="0,0" EndPoint="1,0"><GradientStop Color="#00B07CFF" Offset="0"/><GradientStop Color="#C8B07CFF" Offset="0.3"/><GradientStop Color="#E6FFD6A0" Offset="0.55"/><GradientStop Color="#C86EA8FF" Offset="0.8"/><GradientStop Color="#006EA8FF" Offset="1"/></LinearGradientBrush></Ellipse.Stroke>
      <Ellipse.Effect><BlurEffect Radius="22"/></Ellipse.Effect></Ellipse>
    <Ellipse Canvas.Left="432" Canvas.Top="432" Width="160" Height="160" Fill="#FFE9C8" Opacity="0.55"><Ellipse.Effect><BlurEffect Radius="70"/></Ellipse.Effect></Ellipse>
    <Path Data="$plate" Fill="#2EFFFFFF" StrokeThickness="5">
      <Path.Stroke><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#B3FFFFFF" Offset="0"/><GradientStop Color="#26FFFFFF" Offset="1"/></LinearGradientBrush></Path.Stroke>
      <Path.Effect><DropShadowEffect BlurRadius="40" ShadowDepth="14" Direction="270" Opacity="0.5" Color="#000000"/></Path.Effect></Path>
    <Rectangle Canvas.Left="352" Canvas.Top="368" Width="320" Height="64" RadiusX="20" RadiusY="20" Fill="#FFFFFF"/>
    <Path Stroke="#FFFFFF" StrokeThickness="18" StrokeStartLineCap="Round" StrokeEndLineCap="Round"
          Data="M 362,508 L 662,508 M 362,584 L 662,584 M 362,660 L 662,660 M 462,470 L 462,680"/>
    <Rectangle Canvas.Left="560" Canvas.Top="566" Width="102" Height="36" RadiusX="10" RadiusY="10" Fill="#FF9F0A"/>
"@
    Body $fill $inner
}

function Design-C {   # a white building on warm orange
    $fill = '<Rectangle.Fill><LinearGradientBrush StartPoint="0,0" EndPoint="0.3,1"><GradientStop Color="#FFC24A" Offset="0"/><GradientStop Color="#FF5A2E" Offset="1"/></LinearGradientBrush></Rectangle.Fill>'
    $box = { param($y, $w)   # one isometric slab: top, left, right faces
        $x = 512; $h = 120; $d = $w * 0.5
        "<Path Fill=`"#FFFFFF`" Data=`"M $x,$($y-$d) L $($x+$w),$y L $x,$($y+$d) L $($x-$w),$y Z`"/>
         <Path Fill=`"#F1E3D6`" Data=`"M $($x-$w),$y L $x,$($y+$d) L $x,$($y+$d+$h) L $($x-$w),$($y+$h) Z`"/>
         <Path Fill=`"#DDC6B2`" Data=`"M $($x+$w),$y L $x,$($y+$d) L $x,$($y+$d+$h) L $($x+$w),$($y+$h) Z`"/>
         <Path Stroke=`"#FF7A2E`" StrokeThickness=`"12`" StrokeStartLineCap=`"Round`" StrokeEndLineCap=`"Round`" Data=`"M $($x-$w*0.75),$($y+$d*0.5+40) L $($x-$w*0.25),$($y+$d*0.75+48) M $($x+$w*0.25),$($y+$d*0.75+48) L $($x+$w*0.75),$($y+$d*0.5+40)`"/>" }
    $inner = "<Canvas><Canvas.Effect><DropShadowEffect BlurRadius='36' ShadowDepth='16' Direction='270' Opacity='0.35' Color='#7A2000'/></Canvas.Effect>" +
        (& $box 640 250) + (& $box 500 210) + (& $box 370 170) + "</Canvas>"
    Body $fill $inner
}

function Render($xaml, [int]$size) {
    $v = [Windows.Markup.XamlReader]::Parse($xaml)
    $v.RenderTransform = New-Object Windows.Media.ScaleTransform ($size / 1024), ($size / 1024)
    $v.Measure((New-Object Windows.Size $size, $size)); $v.Arrange((New-Object Windows.Rect 0, 0, $size, $size))
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($v)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); , $ms.ToArray()
}

if ($Preview) {    # every design at 256, 64, 32 and 16 px on dark and light strips
    $canvas = '<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="1200" Height="420" Background="#1E1E24"><Rectangle Canvas.Top="330" Width="1200" Height="90" Fill="#ECECEC"/>'
    $x = 40
    foreach ($d in 'A', 'B', 'C') {
        $xaml = & "Design-$d"
        foreach ($s in 256, 64, 32, 16) {
            $png = Render $xaml $s
            $f = Join-Path $env:TEMP "cur_icon_$d$s.png"; [IO.File]::WriteAllBytes($f, $png)
            $top = if ($s -eq 256) { 30 } else { 350 + (32 - [Math]::Min($s, 64)) / 2 - 8 }
            $left = if ($s -eq 256) { $x } else { $x + @{64 = 20; 32 = 120; 16 = 190 }[$s] }
            if ($s -eq 64) { $top = 343 }
            $canvas += "<Image Source=`"$f`" Width=`"$s`" Height=`"$s`" Canvas.Left=`"$left`" Canvas.Top=`"$top`"/>"
            if ($s -le 32) { $canvas += "<Image Source=`"$f`" Width=`"$s`" Height=`"$s`" Canvas.Left=`"$($left)`" Canvas.Top=`"$($top - 70)`"/>" }
        }
        $canvas += "<TextBlock Text=`"$d`" Foreground=`"White`" FontSize=`"28`" FontWeight=`"Bold`" Canvas.Left=`"$($x + 300)`" Canvas.Top=`"30`"/>"
        $x += 390
    }
    $canvas += '</Canvas>'
    $v = [Windows.Markup.XamlReader]::Parse($canvas)
    $v.Measure((New-Object Windows.Size 1200, 420)); $v.Arrange((New-Object Windows.Rect 0, 0, 1200, 420)); $v.UpdateLayout()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap 1200, 420, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($v)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [IO.File]::Create($Preview); $enc.Save($fs); $fs.Close()
    "preview -> $Preview"
    return
}

$xaml = & "Design-$Design"
$ext = Join-Path $here '..\extension'
[IO.File]::WriteAllBytes((Join-Path $ext 'AI Culi.tab\Panel.panel\Open Panel.pushbutton\icon.png'), (Render $xaml 96))
[IO.File]::WriteAllBytes((Join-Path (Split-Path (Split-Path $here)) 'webapp\public\app-icon.png'), (Render $xaml 256))
"design $Design -> icon.png, webapp\public\app-icon.png"
