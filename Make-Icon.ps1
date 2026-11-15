# Draws app.ico (an amber rounded square with a white d20). Only needed if the icon design changes.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $m = $size * 0.06; $w = $size - 2 * $m; $r = $w * 0.45
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $r, $r, 180, 90); $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90); $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new(0, $size)), ([System.Drawing.Color]::FromArgb(255, 255, 184, 64)), ([System.Drawing.Color]::FromArgb(255, 245, 128, 0))
    $g.FillPath($brush, $path)

    $c = $size / 2; $rad = $size * 0.30
    $pts = 0..5 | % { $a = (-90 + 60 * $_) * [Math]::PI / 180; [System.Drawing.PointF]::new($c + $rad * [Math]::Cos($a), $c + $rad * [Math]::Sin($a)) }
    $in = 0..2 | % { $a = (-90 + 120 * $_) * [Math]::PI / 180; [System.Drawing.PointF]::new($c + 0.55 * $rad * [Math]::Cos($a), $c + 0.1 * $rad + 0.55 * $rad * [Math]::Sin($a)) }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.2, $size * 0.045))
    $pen.LineJoin = 'Round'
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$pts)
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$in)
    $g.DrawLine($pen, $in[0], $pts[0]); $g.DrawLine($pen, $in[1], $pts[2]); $g.DrawLine($pen, $in[2], $pts[4])
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$pngs = $sizes | % { , (New-IconPng $_) }
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
Write-Host 'Wrote app.ico'
