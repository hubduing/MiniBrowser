# Генерирует многоуровневый .ico: скруглённый синий квадрат с белым глобусом.
param([string]$OutFile = (Join-Path $PSScriptRoot '..\src\MiniBrowser\Assets\app.ico'))

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Скруглённый квадрат с градиентом
    $pad = [float]($s * 0.03)
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad))
    $radius = [float]($s * 0.21)
    $d = $radius * 2

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 0x6A, 0xA1, 0xFF),
        [System.Drawing.Color]::FromArgb(255, 0x1B, 0x54, 0xD0),
        45)
    $g.FillPath($brush, $path)

    # Белый глобус: окружность + меридиан + широты
    $cx = $s / 2.0
    $cy = $s / 2.0
    $rad = $s * 0.28
    $penW = [float][Math]::Max(1.0, $s / 14.0)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(240, 255, 255, 255), $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $circle = New-Object System.Drawing.RectangleF(($cx - $rad), ($cy - $rad), (2 * $rad), (2 * $rad))
    $g.DrawEllipse($pen, $circle)
    $g.DrawEllipse($pen, ($cx - $rad * 0.42), ($cy - $rad), ($rad * 0.84), (2 * $rad))

    $latitudes = if ($s -le 24) { @(0.0) } else { @(-0.48, 0.0, 0.48) }
    foreach ($f in $latitudes) {
        $y = $cy + $rad * $f
        $halfW = $rad * [Math]::Sqrt(1 - $f * $f)
        $g.DrawLine($pen, ($cx - $halfW), $y, ($cx + $halfW), $y)
    }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    return ,$bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = New-IconPng $s }

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
if (Test-Path $OutFile) { Remove-Item $OutFile -Force }

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)          # reserved
$bw.Write([uint16]1)          # type: icon
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $dim = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $bw.Write($dim)           # width
    $bw.Write($dim)           # height
    $bw.Write([byte]0)        # colors
    $bw.Write([byte]0)        # reserved
    $bw.Write([uint16]1)      # planes
    $bw.Write([uint16]32)     # bpp
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }

$bw.Flush()
$fs.Close()

Write-Host "OK: $OutFile ($((Get-Item $OutFile).Length) bytes)"
