<#
.SYNOPSIS
  Genera el logo y los íconos de Local133 a partir de tools/brand/local133-logo-source.png.

.DESCRIPTION
  El original es un PNG de 1254×1254 (toldo blanco sobre rosa) con ruido en el fondo (#F20382–#F40483).
  El script lo aplana a dos colores (#F40284 y blanco, conservando el antialias de los bordes) y escribe:

    img/local133-logo-{48,96,144,160,320,480}.png   barras (1×/2×/3×), home del portal y cierre de la portada
    apple-touch-icon.png                             180 px (iOS)
    favicon.ico                                      16, 32 y 48 px (ICO con PNG embebidos)
    icon-192.png, icon-512.png                       PWA (solo Web usa el de 512)
    icon-maskable-512.png                            PWA, logo al 78 % para que Android no recorte el toldo
    img/email/local133-email-logo.png                correo, 192 px con esquinas redondeadas y fuera transparente

  Lo compartido se copia a Local133.Web/wwwroot y Local133.LandingPage/wwwroot (SharedAssetsTests los compara).
  Usa System.Drawing (incluido en Windows PowerShell 5.1): sin npm ni DLL propias (Smart App Control).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/brand-assets.ps1
#>
[CmdletBinding()]
param(
    [string]$Source
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# En Windows PowerShell 5.1, $PSScriptRoot no existe todavía en los valores por defecto de param().
if (-not $Source) { $Source = Join-Path $PSScriptRoot 'brand/local133-logo-source.png' }

$repo = Split-Path $PSScriptRoot -Parent
$web = Join-Path $repo 'src/Local133.Web/wwwroot'
$landing = Join-Path $repo 'src/Local133.LandingPage/wwwroot'

# Rosa del fondo del logo (= $landing-logo-pink).
$pink = @{ R = 0xF4; G = 0x02; B = 0x84 }

function New-Canvas([int]$size, [System.Drawing.Imaging.PixelFormat]$format) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, $format
    $bmp.SetResolution(72, 72)
    return $bmp
}

function Set-HighQuality([System.Drawing.Graphics]$g) {
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
}

# Aplana el ruido: cada píxel se lleva a la recta rosa→blanco según su canal verde (rosa ≈ 3, blanco ≈ 253).
function Get-Flattened([System.Drawing.Bitmap]$src) {
    $w = $src.Width; $h = $src.Height
    $bmp = New-Canvas $w ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    # Rectángulo explícito: DrawImageUnscaled respeta los DPI del original y lo encogería.
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), 0, 0, $w, $h, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()

    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, $bmp.PixelFormat)
    $stride = $data.Stride
    $bytes = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

    # Tabla por valor de verde: evita aritmética en el ciclo.
    $lutR = New-Object byte[] 256; $lutG = New-Object byte[] 256; $lutB = New-Object byte[] 256
    for ($v = 0; $v -lt 256; $v++) {
        $t = [Math]::Min(1.0, [Math]::Max(0.0, ($v - 6) / (248.0 - 6)))
        $lutR[$v] = [byte][Math]::Round($pink.R + (255 - $pink.R) * $t)
        $lutG[$v] = [byte][Math]::Round($pink.G + (255 - $pink.G) * $t)
        $lutB[$v] = [byte][Math]::Round($pink.B + (255 - $pink.B) * $t)
    }

    for ($y = 0; $y -lt $h; $y++) {
        $row = $y * $stride
        $end = $row + $w * 3
        for ($i = $row; $i -lt $end; $i += 3) {
            $gv = $bytes[$i + 1]          # BGR
            $bytes[$i] = $lutB[$gv]
            $bytes[$i + 1] = $lutG[$gv]
            $bytes[$i + 2] = $lutR[$gv]
        }
    }

    [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    $bmp.UnlockBits($data)
    return $bmp
}

# Logo cuadrado y opaco de $size px; $scale < 1 lo reduce sobre el fondo rosa (maskable).
function New-Square([System.Drawing.Bitmap]$clean, [int]$size, [double]$scale = 1.0) {
    $bmp = New-Canvas $size ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    Set-HighQuality $g
    $g.Clear([System.Drawing.Color]::FromArgb($pink.R, $pink.G, $pink.B))
    $inner = $size * $scale
    $offset = ($size - $inner) / 2
    $attrs = New-Object System.Drawing.Imaging.ImageAttributes
    $attrs.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # sin halo en los bordes
    $dest = New-Object System.Drawing.RectangleF ([single]$offset), ([single]$offset), ([single]$inner), ([single]$inner)
    $g.DrawImage($clean, [System.Drawing.Rectangle]::Round($dest), 0, 0, $clean.Width, $clean.Height, [System.Drawing.GraphicsUnit]::Pixel, $attrs)
    $attrs.Dispose(); $g.Dispose()
    return $bmp
}

# Logo con esquinas redondeadas (radio = $radiusRatio del lado) y transparente fuera: para el correo.
function New-Rounded([System.Drawing.Bitmap]$clean, [int]$size, [double]$radiusRatio) {
    $square = New-Square $clean $size
    $bmp = New-Canvas $size ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    Set-HighQuality $g
    $g.Clear([System.Drawing.Color]::Transparent)
    $d = [single]($size * $radiusRatio * 2)
    $s = [single]$size
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.TextureBrush $square
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose(); $g.Dispose(); $square.Dispose()
    return $bmp
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
    $dir = Split-Path $path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray(); $ms.Dispose()
    return , $bytes
}

# ICO con PNG embebidos (Vista+): encabezado de 6 bytes, 16 por entrada y luego los PNG.
function Save-Ico([System.Drawing.Bitmap]$clean, [int[]]$sizes, [string]$path) {
    $images = @()
    foreach ($size in $sizes) {
        $bmp = New-Square $clean $size
        $images += , (Get-PngBytes $bmp)
        $bmp.Dispose()
    }
    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    $w.Dispose(); $fs.Dispose()
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$original = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
$clean = Get-Flattened $original
$original.Dispose()
Write-Host "Aplanado en $([int]$sw.Elapsed.TotalSeconds) s"

$shared = @()   # rutas relativas que van en las dos apps

foreach ($size in 48, 96, 144, 160, 320, 480) {
    $rel = "img/local133-logo-$size.png"
    $bmp = New-Square $clean $size; Save-Png $bmp (Join-Path $web $rel); $bmp.Dispose()
    $shared += $rel
}

$bmp = New-Square $clean 180; Save-Png $bmp (Join-Path $web 'apple-touch-icon.png'); $bmp.Dispose()
$shared += 'apple-touch-icon.png'

Save-Ico $clean @(16, 32, 48) (Join-Path $web 'favicon.ico')
$shared += 'favicon.ico'

$bmp = New-Square $clean 192; Save-Png $bmp (Join-Path $web 'icon-192.png'); $bmp.Dispose()
$shared += 'icon-192.png'

$bmp = New-Square $clean 512; Save-Png $bmp (Join-Path $web 'icon-512.png'); $bmp.Dispose()
$bmp = New-Square $clean 512 0.78; Save-Png $bmp (Join-Path $web 'icon-maskable-512.png'); $bmp.Dispose()

$bmp = New-Rounded $clean 192 0.22; Save-Png $bmp (Join-Path $web 'img/email/local133-email-logo.png'); $bmp.Dispose()

$clean.Dispose()

foreach ($rel in $shared) {
    $dest = Join-Path $landing $rel
    $dir = Split-Path $dest -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    Copy-Item (Join-Path $web $rel) $dest -Force
}

Write-Host "Listo en $([int]$sw.Elapsed.TotalSeconds) s. Copiados a la portada: $($shared -join ', ')"
