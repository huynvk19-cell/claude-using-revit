param([string]$In, [string]$Out, [double]$Cx, [double]$Cy, [double]$W = 0.2, [double]$H = 0.15, [int]$MaxW = 1600)
# Crop a region given as fractions of the image (centre Cx,Cy; size W,H) and scale to MaxW px wide.
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($In)
$x = [int][Math]::Max(0, ($Cx - $W / 2) * $img.Width); $y = [int][Math]::Max(0, ($Cy - $H / 2) * $img.Height)
$w = [int][Math]::Min($img.Width - $x, $W * $img.Width); $h = [int][Math]::Min($img.Height - $y, $H * $img.Height)
$s = [Math]::Min(1.0, $MaxW / $w)
$bmp = New-Object System.Drawing.Bitmap ([int]($w * $s)), ([int]($h * $s))
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.InterpolationMode = 'HighQualityBicubic'
$g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height), (New-Object System.Drawing.Rectangle $x, $y, $w, $h), 'Pixel')
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose(); $img.Dispose()
"$Out ${w}x${h} -> $([int]($w*$s))"
