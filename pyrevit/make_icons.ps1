# Draw the ribbon icons (96 x 96 icon.png) of the "claude using revit" tab with WPF: a raised slab of mica in the tool
# colour, straight edges with a small radius, its side showing below, a frosted top face, a white glyph set square on it.
#   powershell -STA -File pyrevit\make_icons.ps1 [-Preview <png>]   then install.ps1 -PyRevit + pyRevit > Reload
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$tab = Join-Path $PSScriptRoot 'ClaudeUsingRevit.extension\claude using revit.tab'

# button folder, colour top, colour bottom, glyph (Segoe Fluent Icons), plate tint, text label on plate
$icons = @(
    @('Panel.panel\Open Panel.pushbutton',                '#FFB84A', '#F0612A', [char]0xE80A, '#FFFFFF', ''),
    @('Highlight.panel\Highlight.pushbutton',              '#FF6B6B', '#E0243A', [char]0xE7E6, '#FFFFFF', ''),
    @('Highlight.panel\Clear Highlight.pushbutton',        '#A1A1AA', '#4B4B52', [char]0xE75C, '#FF6B6B', ''),
    @('Filter.panel\Add To Filter.pushbutton',            '#34D1B6', '#0B8F7E', [char]0xE71C, '#FFFFFF', ''),
    @('Untagged.panel\Highlight Untagged.pushbutton',      '#FFB54A', '#FF6A1F', [char]0xE8EC, '#FFFFFF', ''),
    @('Untagged.panel\Remove Untagged.pushbutton',         '#A1A1AA', '#4B4B52', [char]0xE75C, '#FFA23A', ''),
    @('PrintSet.panel\Print Set Sync.pushbutton',          '#4FA8FF', '#0062DB', [char]0xE749, '#FFFFFF', ''),
    @('PrintSet.panel\Sheet List Columns.pushbutton',      '#7D7BFF', '#3F3CC4', [char]0xE80A, '#FFFFFF', ''),
    @('PrintSet.panel\Export PDF.pushbutton',              '#FF6259', '#C4161C', '',           '#FFFFFF', 'PDF'),
    @('View3D.panel\3D From View.pushbutton',              '#3DDCC0', '#0A8F7E', [char]0xF158, '#FFFFFF', '')
)

function Icon($bundle, $top, $bottom, $glyph, $plateTint, $label) {
    # the Fluent glyphs are hairline outlines: stack a few 0.8 px shifts so they read at 32 px
    $bold = foreach ($m in '0,0', '0.8,0', '-0.8,0', '0,0.8', '0,-0.8') {
        "        <TextBlock Text=`"$glyph`" FontFamily=`"Segoe Fluent Icons, Segoe MDL2 Assets`" FontSize=`"44`" Foreground=`"White`" RenderTransform=`"1,0,0,1,$m`"/>"
    }
    $glyphXaml = if ($label) {
@"
      <Grid Width="46" Height="56" HorizontalAlignment="Center" VerticalAlignment="Center">
        <Path Data="M 2,0 L 32,0 L 46,14 L 46,54 Q 46,56 44,56 L 2,56 Q 0,56 0,54 L 0,2 Q 0,0 2,0 Z" Fill="#F2FFFFFF">
          <Path.Effect><DropShadowEffect Color="#000000" BlurRadius="3" ShadowDepth="2" Direction="270" Opacity="0.35"/></Path.Effect>
        </Path>
        <Path Data="M 32,0 L 32,14 L 46,14 Z" Fill="#40000000"/>
        <Border CornerRadius="2" Background="$bottom" Margin="-7,27,0,0" HorizontalAlignment="Left"
                VerticalAlignment="Top" Padding="4,0,4,1">
          <TextBlock Text="$label" FontFamily="Segoe UI Black" FontSize="15" Foreground="White"/>
        </Border>
      </Grid>
"@
    } else {
@"
      <Grid HorizontalAlignment="Center" VerticalAlignment="Center">
        <Grid.Effect><DropShadowEffect Color="#000000" BlurRadius="2" ShadowDepth="2" Direction="270" Opacity="0.4"/></Grid.Effect>
$($bold -join "`n")
      </Grid>
"@
    }
    $xaml = @"
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="96" Height="96">
  <!-- shadow on the ribbon, the side of the slab, then its top face -->
  <Border CornerRadius="13" Margin="5,12,5,1" Background="#000000" Opacity="0.3">
    <Border.Effect><BlurEffect Radius="5"/></Border.Effect>
  </Border>
  <Border CornerRadius="12" Margin="5,12,5,4" Background="$bottom"/>
  <Border CornerRadius="12" Margin="5,12,5,4" Background="#66000000"/>
  <Grid Margin="5,4,5,12">
    <Border CornerRadius="12">
      <Border.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
          <GradientStop Color="$top" Offset="0"/><GradientStop Color="$bottom" Offset="1"/>
        </LinearGradientBrush>
      </Border.Background>
    </Border>
    <!-- mica: a soft frost, lighter toward the top left -->
    <Border CornerRadius="12">
      <Border.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
          <GradientStop Color="#59FFFFFF" Offset="0"/><GradientStop Color="#1AFFFFFF" Offset="0.5"/>
          <GradientStop Color="#00FFFFFF" Offset="1"/>
        </LinearGradientBrush>
      </Border.Background>
    </Border>
    <Border CornerRadius="12" BorderThickness="1.6">
      <Border.BorderBrush>
        <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
          <GradientStop Color="#CCFFFFFF" Offset="0"/><GradientStop Color="#33FFFFFF" Offset="0.5"/>
          <GradientStop Color="#1AFFFFFF" Offset="1"/>
        </LinearGradientBrush>
      </Border.BorderBrush>
    </Border>
$glyphXaml
  </Grid>
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
