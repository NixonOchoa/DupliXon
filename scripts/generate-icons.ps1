# Renders the DupliXon icon (same design as wwwroot/icon.svg) to PNG/ICO with System.Drawing.
param([string]$Root = (Resolve-Path "$PSScriptRoot\.."))
Add-Type -AssemblyName System.Drawing

function New-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    function RR([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = 2 * $r
        $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure(); $p
    }
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($size, $size)), ([System.Drawing.Color]::FromArgb(43, 124, 255)), ([System.Drawing.Color]::FromArgb(0, 194, 184))
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(43, 124, 255), [System.Drawing.Color]::FromArgb(125, 92, 255), [System.Drawing.Color]::FromArgb(0, 194, 184))
    $blend.Positions = @(0, 0.55, 1); $bg.InterpolationColors = $blend
    $g.FillPath($bg, (RR 0 0 $size $size (58 * $s)))

    $white = [System.Drawing.Color]::White
    $glass = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 255, 255, 255))
    $pen1 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(204, 255, 255, 255)), (5 * $s)
    $back = RR (40 * $s) (62 * $s) (120 * $s) (84 * $s) (14 * $s)
    $g.FillPath($glass, $back); $g.DrawPath($pen1, $back)

    $front = RR (100 * $s) (104 * $s) (116 * $s) (92 * $s) (16 * $s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(72, 255, 255, 255))), $front)
    $g.DrawPath((New-Object System.Drawing.Pen $white, (6 * $s)), $front)

    $stand = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(204, 255, 255, 255)), (8 * $s)
    $stand.StartCap = 'Round'; $stand.EndCap = 'Round'
    $g.DrawLine($stand, 70 * $s, 176 * $s, 122 * $s, 176 * $s)
    $x = New-Object System.Drawing.Pen $white, (10 * $s); $x.StartCap = 'Round'; $x.EndCap = 'Round'
    $g.DrawLine($x, 138 * $s, 132 * $s, 178 * $s, 164 * $s); $g.DrawLine($x, 178 * $s, 132 * $s, 138 * $s, 164 * $s)
    $g.Dispose(); $bmp
}

function Save-Png([int]$size, [string]$path) { $b = New-Icon $size; $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose() }

$assets = Join-Path $Root 'src\DupliXon.App\Assets'
$web = Join-Path $Root 'src\DupliXon.Server\wwwroot'
New-Item -ItemType Directory -Force $assets | Out-Null
Save-Png 512 (Join-Path $assets 'logo.png')
Save-Png 180 (Join-Path $web 'apple-touch-icon.png')
Save-Png 512 (Join-Path $web 'icon-512.png')

# Multi-size ICO with embedded PNG frames.
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($sz in $sizes) { $b = New-Icon $sz; $ms = New-Object IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }
$out = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $d = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$frames[$i].Length); $w.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
[IO.File]::WriteAllBytes((Join-Path $assets 'DupliXon.ico'), $out.ToArray())
"Icons generated."
