# Renders the "Stack Fault" logo with WPF (same geometry as logo.svg / logo-small.svg)
# and writes a multi-size PNG-compressed .ico plus PNG exports.
# Usage: powershell -STA -File branding\make-icon.ps1 [-OutDir <dir>]
param([string]$OutDir = (Join-Path $PSScriptRoot "out"))

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
New-Item -ItemType Directory -Force $OutDir | Out-Null

function C([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Grad([string]$a, [string]$b, [bool]$horizontal = $false) {
    $end = if ($horizontal) { New-Object System.Windows.Point(1, 0) } else { New-Object System.Windows.Point(1, 1) }
    $g = New-Object System.Windows.Media.LinearGradientBrush((C $a), (C $b), (New-Object System.Windows.Point(0, 0)), $end)
    $g.Freeze(); $g
}
function RR($x, $y, $w, $h, $r) {
    New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect($x, $y, $w, $h)), $r, $r)
}
function Geo([string]$d) { [System.Windows.Media.Geometry]::Parse($d) }

function Draw-Full($dc) {
    $dc.DrawGeometry((Grad "#1D1834" "#0B0A13"), $null, (RR 16 16 480 480 124))
    $pen = New-Object System.Windows.Media.Pen((New-Object System.Windows.Media.SolidColorBrush((C "#17FFFFFF"))), 1.5)
    $dc.DrawGeometry($null, $pen, (RR 16.5 16.5 479 479 123.5))
    $dc.DrawGeometry((Grad "#A495FF" "#6A55F7"), $null, (RR 112 132 288 64 22))
    $dc.DrawGeometry((Grad "#7F6BFB" "#4A36E0"), $null, (Geo "M134 224H262L250 240L266 256L252 272L262 288H134A22 22 0 0 1 112 266V246A22 22 0 0 1 134 224Z"))
    $dc.DrawGeometry((Grad "#FF5E62" "#FFA24A" $true), $null, (Geo "M292 214H414A22 22 0 0 1 436 236V256A22 22 0 0 1 414 278H292L282 262L296 246L280 230Z"))
    $dc.DrawGeometry((New-Object System.Windows.Media.SolidColorBrush((C "#B3FFA24A"))), $null, (RR 450 226 18 18 5))
    $dc.PushOpacity(0.85)
    $dc.DrawGeometry((Grad "#7F6BFB" "#4A36E0"), $null, (RR 112 316 288 64 22))
    $dc.Pop()
}

function Draw-Small($dc) {
    $dc.DrawGeometry((Grad "#231C40" "#0E0C18"), $null, (RR 0 0 512 512 112))
    $b = Grad "#B0A3FF" "#7461FA"
    $dc.DrawGeometry($b, $null, (RR 80 76 352 96 32))
    $dc.DrawGeometry($b, $null, (RR 80 208 176 96 32))
    $dc.DrawGeometry((Grad "#FF5E62" "#FFA24A" $true), $null, (RR 288 196 160 96 32))
    $dc.DrawGeometry($b, $null, (RR 80 340 352 96 32))
}

function Render-Png([int]$size, [bool]$small) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $s = $size / 512.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($s, $s)))
    if ($small) { Draw-Small $dc } else { Draw-Full $dc }
    $dc.Pop(); $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

# Optical switch: <= 24 px uses the simplified variant
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = @()
foreach ($sz in $sizes) {
    $png = Render-Png $sz ($sz -le 24)
    $images += , @{ Size = $sz; Bytes = $png }
    [System.IO.File]::WriteAllBytes((Join-Path $OutDir "logo-$sz.png"), $png)
}
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "logo-512.png"), (Render-Png 512 $false))

# ICO container (PNG-compressed entries, Vista+)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$img.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write($img.Bytes) }
$bw.Flush()
$icoPath = Join-Path $OutDir "dumpfinder.ico"
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
Write-Output "Wrote $icoPath ($([math]::Round((Get-Item $icoPath).Length / 1KB, 1)) KB, $($images.Count) sizes)"
