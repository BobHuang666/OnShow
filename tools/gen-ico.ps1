# Generate multi-size onshow.ico (rounded accent square + white "O", matches tray icon)
# ASCII only (PowerShell 5.1 reads .ps1 as ANSI).
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$entries = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int][Math]::Floor($s * 0.04))
    $d = $s - 1 - $pad
    $rad = [int][Math]::Floor($s * 0.28)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($pad, $pad, $rad * 2, $rad * 2, 180, 90)
    $p.AddArc($d - $rad * 2, $pad, $rad * 2, $rad * 2, 270, 90)
    $p.AddArc($d - $rad * 2, $d - $rad * 2, $rad * 2, $rad * 2, 0, 90)
    $p.AddArc($pad, $d - $rad * 2, $rad * 2, $rad * 2, 90, 90)
    $p.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 138, 180, 248))
    $g.FillPath($brush, $p)

    $fs = [float][Math]::Floor($s * 0.62)
    $font = New-Object System.Drawing.Font("Segoe UI", $fs, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $white = [System.Drawing.Brushes]::White
    $g.DrawString("O", $font, $white, (New-Object System.Drawing.RectangleF(0, 0, $s, $s)), $fmt)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += , @($s, $ms.ToArray())
    $g.Dispose()
    $bmp.Dispose()
    $p.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e[0]
    $data = $e[1]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($e in $entries) { $bw.Write($e[1]) }
$bw.Flush()

$dest = Join-Path $PSScriptRoot "..\src\OnShow.App\Assets\onshow.ico"
[System.IO.Directory]::CreateDirectory((Split-Path $dest -Parent)) | Out-Null
[System.IO.File]::WriteAllBytes($dest, $out.ToArray())
Write-Host "generated $dest ($($out.Length) bytes, $($entries.Count) sizes)"
