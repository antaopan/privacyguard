$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [Math]::Max(2, [int]($s * 0.16))
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $bg.AddArc($s - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
    $bg.AddArc($s - $r * 2 - 1, $s - $r * 2 - 1, $r * 2, $r * 2, 0, 90)
    $bg.AddArc(0, $s - $r * 2 - 1, $r * 2, $r * 2, 90, 90)
    $bg.CloseFigure()
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        [System.Drawing.Point]::new(0, 0),
        [System.Drawing.Point]::new($s, $s),
        [System.Drawing.Color]::FromArgb(255, 13, 32, 51),
        [System.Drawing.Color]::FromArgb(255, 8, 62, 66))
    $g.FillPath($br, $bg)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(110, 45, 212, 191), [Math]::Max(1, $s / 48))
    $g.DrawPath($pen, $bg)

    $cx = $s / 2.0
    $top = $s * 0.17
    $w = $s * 0.52
    $h = $s * 0.62
    $sh = New-Object System.Drawing.Drawing2D.GraphicsPath
    $sh.AddBezier([float]($cx - $w / 2), [float]($top + $h * 0.12), [float]($cx - $w / 2), [float]($top + $h * 0.45), [float]($cx - $w * 0.18), [float]($top + $h * 0.62), [float]$cx, [float]($top + $h))
    $sh.AddBezier([float]($cx + $w * 0.18), [float]($top + $h * 0.62), [float]($cx + $w / 2), [float]($top + $h * 0.45), [float]($cx + $w / 2), [float]($top + $h * 0.12), [float]$cx, [float]($top + $h * 0.02))
    $sh.CloseFigure()
    $sg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        [System.Drawing.Point]::new(0, [int]$top),
        [System.Drawing.Point]::new(0, [int]($top + $h)),
        [System.Drawing.Color]::FromArgb(255, 94, 234, 212),
        [System.Drawing.Color]::FromArgb(255, 13, 148, 136))
    $g.FillPath($sg, $sh)

    $frame = [System.Drawing.Color]::FromArgb(255, 7, 25, 40)
    $kw = [Math]::Max(1.2, $s * 0.055)
    $keyBrush = New-Object System.Drawing.SolidBrush($frame)
    $g.FillEllipse($keyBrush, [float]($cx - $kw), [float]($s * 0.43), [float]($kw * 2), [float]($kw * 2))
    $pts = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float]($cx - $kw * 0.72), [float]($s * 0.49)),
        [System.Drawing.PointF]::new([float]($cx + $kw * 0.72), [float]($s * 0.49)),
        [System.Drawing.PointF]::new([float]($cx + $kw * 0.35), [float]($s * 0.60)),
        [System.Drawing.PointF]::new([float]($cx - $kw * 0.35), [float]($s * 0.60)))
    $g.FillPolygon($keyBrush, $pts)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @($s, $ms.ToArray())
    $g.Dispose()
    $bmp.Dispose()
    $ms.Dispose()
}

$icoPath = Join-Path $assets 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $sz = [int]$f[0]
    $data = [byte[]]$f[1]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) {
    $bw.Write([byte[]]$f[1])
}
$bw.Flush()
$bw.Close()
$fs.Close()

$icon = New-Object System.Drawing.Icon($icoPath, 256, 256)
$icon.ToBitmap().Save((Join-Path $assets 'app-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$icon.Dispose()
Write-Output "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes)"
