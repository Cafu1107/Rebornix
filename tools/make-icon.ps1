# Rebornix uygulama ikonunu (Assets\Rebornix.ico) üretir.
# Tasarım, Themes\Dark.xaml içindeki "LogoImage" vektör çizimiyle aynıdır (64x64 birimlik tasarım alanı).
# Kullanım: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\Rebornix\Assets\Rebornix.ico'))

Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0
    $g.ScaleTransform($s, $s)

    $bg = New-RoundRect 0 0 64 64 16
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 64, 64), ([System.Drawing.Color]::FromArgb(255, 0x7C, 0x5C, 0xFF)), ([System.Drawing.Color]::FromArgb(255, 0x5A, 0xA9, 0xFF))
    $g.FillPath($brush, $bg)

    # Dairesel ok: merkez (32,32), yarıçap 18, üstten saat yönünde ~293 derece
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 6
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, 14, 14, 36, 36, -90, 292.6)

    # Ok ucu
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $tri = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 8, 22), (New-Object System.Drawing.PointF 20, 14), (New-Object System.Drawing.PointF 21, 28))
    $g.FillPolygon($white, $tri)

    # Ortadaki disk
    $disk = New-RoundRect 25 27 14 12 3
    $g.FillPath($white, $disk)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = Render $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

# ICO kapsayıcısı (PNG sıkıştırmalı girdiler)
$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $pngs[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $Out) ).Path + '\' + (Split-Path $Out -Leaf), $fs.ToArray())

# Önizleme için 256 px PNG
$preview = Join-Path (Split-Path $Out) 'icon-256.png'
[System.IO.File]::WriteAllBytes($preview, $pngs[-1])
"İkon yazıldı: $Out"
