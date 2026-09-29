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
    # Marka gradyanı: mor → indigo → mavi
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 64, 64), ([System.Drawing.Color]::White), ([System.Drawing.Color]::White)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(255, 0x8B, 0x5C, 0xF6), [System.Drawing.Color]::FromArgb(255, 0x63, 0x66, 0xF1), [System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6))
    $blend.Positions = [single[]]@(0, 0.5, 1)
    $brush.InterpolationColors = $blend
    $g.FillPath($brush, $bg)

    # Üstten hafif parlaklık
    $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, -1), (New-Object System.Drawing.PointF 0, 65), ([System.Drawing.Color]::White), ([System.Drawing.Color]::White)
    $sb = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $sb.Colors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(51, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $sb.Positions = [single[]]@(0, 0.55, 1)
    $shine.InterpolationColors = $sb
    $g.FillPath($shine, $bg)

    # Yeniden doğuş halkası: merkez (32,32), yarıçap 24, -50°'den saat yönünde 280°
    $ringColor = [System.Drawing.Color]::FromArgb(217, 255, 255, 255)
    $pen = New-Object System.Drawing.Pen $ringColor, 4.2
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, 8, 8, 48, 48, -50, 280)
    $tri = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 20.78, 10.08), (New-Object System.Drawing.PointF 18.51, 18.26), (New-Object System.Drawing.PointF 12.34, 10.91))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $ringColor), $tri)

    # Monogram R
    $rPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 5.5
    $rPen.StartCap = 'Round'; $rPen.EndCap = 'Round'; $rPen.LineJoin = 'Round'
    $r = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r.AddLine(25, 44, 25, 20)
    $r.AddLine(25, 20, 33, 20)
    $r.AddArc(26, 20, 14, 14, -90, 180)
    $r.AddLine(33, 34, 25, 34)
    $g.DrawPath($rPen, $r)
    $g.DrawLine($rPen, 32, 34, 40, 44)

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
