# Draw the Liquid Glass ribbon icons (96 x 96 icon.png) of the "claude using revit" tab with WPF.
# Same look as the windows (lib/liquid_glass.xaml): a superellipse in the tool colour, a frosted
# glass plate, a white glyph floating on it, a specular rim lit from the top left.
#   powershell -STA -File pyrevit\make_icons.ps1 [-Preview <png>]   then install.ps1 -PyRevit + pyRevit > Reload
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$tab = Join-Path $PSScriptRoot 'ClaudeUsingRevit.extension\claude using revit.tab'

# button folder, colour top, colour bottom, glyph (Segoe Fluent Icons), plate tint, text label on plate
$icons = @(
    @('Highlight.panel\Highlight.pushbutton',              '#FF6B6B', '#E0243A', [char]0xE7E6, '#FFFFFF', ''),
    @('Highlight.panel\Clear Highlight.pushbutton',        '#A1A1AA', '#4B4B52', [char]0xE75C, '#FF6B6B', ''),
    @('Untagged.panel\Highlight Untagged.pushbutton',      '#FFB54A', '#FF6A1F', [char]0xE8EC, '#FFFFFF', ''),
    @('Untagged.panel\Remove Untagged.pushbutton',         '#A1A1AA', '#4B4B52', [char]0xE75C, '#FFA23A', ''),
    @('PrintSet.panel\Print Set Sync.pushbutton',          '#4FA8FF', '#0062DB', [char]0xE749, '#FFFFFF', ''),
    @('PrintSet.panel\Sheet List Columns.pushbutton',      '#7D7BFF', '#3F3CC4', [char]0xE80A, '#FFFFFF', ''),
    @('PrintSet.panel\Export PDF.pushbutton',              '#FF6259', '#C4161C', '',           '#FFFFFF', 'PDF')
)

# superellipse |x|^5 + |y|^5 = 1 (the iOS icon shape), centred in 96 x 96 with a 4 px margin
function Squircle([double]$cx, [double]$cy, [double]$r, [double]$n = 5) {
    $pts = for ($i = 0; $i -lt 160; $i++) {
        $t = 2 * [Math]::PI * $i / 160
        $c = [Math]::Cos($t); $s = [Math]::Sin($t)
        $x = $cx + $r * [Math]::Sign($c) * [Math]::Pow([Math]::Abs($c), 2 / $n)
        $y = $cy + $r * [Math]::Sign($s) * [Math]::Pow([Math]::Abs($s), 2 / $n)
        '{0:0.##},{1:0.##}' -f $x, $y
    }
    'M ' + ($pts -join ' L ') + ' Z'
}
$shape = Squircle 48 48 44
$plate = Squircle 0 0 1   # unit plate, scaled per icon

function Icon($bundle, $top, $bottom, $glyph, $plateTint, $label) {
    # the Fluent glyphs are hairline outlines: stack a few 0.8 px shifts so they read at 32 px
    $bold = foreach ($m in '0,0', '0.8,0', '-0.8,0', '0,0.8', '0,-0.8') {
        "        <TextBlock Text=`"$glyph`" FontFamily=`"Segoe Fluent Icons, Segoe MDL2 Assets`" FontSize=`"44`" Foreground=`"White`" RenderTransform=`"1,0,0,1,$m`"/>"
    }
    $glyphXaml = if ($label) {
@"
      <Grid Width="50" Height="60" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,2,0,0">
        <Path Data="M 4,0 L 34,0 L 50,16 L 50,56 Q 50,60 46,60 L 4,60 Q 0,60 0,56 L 0,4 Q 0,0 4,0 Z" Fill="#E6FFFFFF">
          <Path.Effect><DropShadowEffect Color="$bottom" BlurRadius="8" ShadowDepth="2" Direction="270" Opacity="0.55"/></Path.Effect>
        </Path>
        <Path Data="M 34,0 L 34,12 Q 34,16 38,16 L 50,16 Z" Fill="#59FFFFFF"/>
        <Border CornerRadius="5" Background="$bottom" Margin="-8,30,0,0" HorizontalAlignment="Left"
                VerticalAlignment="Top" Padding="5,0,5,1">
          <TextBlock Text="$label" FontFamily="Segoe UI Black" FontSize="15" Foreground="White"/>
        </Border>
      </Grid>
"@
    } else {
@"
      <Grid HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,2,0,0">
        <Grid.Effect><DropShadowEffect Color="$bottom" BlurRadius="7" ShadowDepth="2" Direction="270" Opacity="0.7"/></Grid.Effect>
$($bold -join "`n")
      </Grid>
"@
    }
    $xaml = @"
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="96" Height="96">
  <Grid.Clip><PathGeometry Figures="$shape"/></Grid.Clip>
  <Rectangle>
    <Rectangle.Fill>
      <LinearGradientBrush StartPoint="0,0" EndPoint="0.35,1">
        <GradientStop Color="$top" Offset="0"/><GradientStop Color="$bottom" Offset="1"/>
      </LinearGradientBrush>
    </Rectangle.Fill>
  </Rectangle>
  <Ellipse Width="130" Height="90" Margin="-40,-58,0,0" HorizontalAlignment="Left" VerticalAlignment="Top">
    <Ellipse.Fill>
      <RadialGradientBrush><GradientStop Color="#73FFFFFF" Offset="0"/><GradientStop Color="#00FFFFFF" Offset="1"/></RadialGradientBrush>
    </Ellipse.Fill>
  </Ellipse>
  <!-- frosted glass plate, tilted, behind the glyph -->
  <Path Data="$plate" Stretch="Fill" Width="58" Height="58" Margin="22,26,0,0" HorizontalAlignment="Left" VerticalAlignment="Top"
        StrokeThickness="1.4" RenderTransformOrigin="0.5,0.5">
    <Path.RenderTransform><RotateTransform Angle="-9"/></Path.RenderTransform>
    <Path.Fill>
      <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="#66FFFFFF" Offset="0"/><GradientStop Color="#1F$($plateTint.Substring(1))" Offset="1"/>
      </LinearGradientBrush>
    </Path.Fill>
    <Path.Stroke>
      <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="#E6FFFFFF" Offset="0"/><GradientStop Color="#26FFFFFF" Offset="0.55"/>
        <GradientStop Color="#80FFFFFF" Offset="1"/>
      </LinearGradientBrush>
    </Path.Stroke>
  </Path>
$glyphXaml
  <!-- specular rim of the icon itself -->
  <Path Data="$shape" StrokeThickness="2.4">
    <Path.Stroke>
      <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="#F2FFFFFF" Offset="0"/><GradientStop Color="#40FFFFFF" Offset="0.3"/>
        <GradientStop Color="#00FFFFFF" Offset="0.55"/><GradientStop Color="#4DFFFFFF" Offset="0.9"/>
        <GradientStop Color="#99FFFFFF" Offset="1"/>
      </LinearGradientBrush>
    </Path.Stroke>
  </Path>
</Grid>
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

$all = foreach ($i in $icons) {
    $bmp = Icon @i
    $out = Join-Path $tab "$($i[0])\icon.png"
    SavePng $bmp $out
    "icon -> $out" | Write-Host
    $bmp
}

if ($Preview) {  # strip at 96 px and at the 32 px Revit shows, on the ribbon grey
    $n = $all.Count
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRectangle((New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(245, 245, 245))), $null,
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
