param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\HonorControl\Assets\HonorControl.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 48, 64, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $inset = [float]($size * 0.04)
        $edge = [float]($size * 0.92)
        $corner = [float]($size * 0.22)
        $backgroundPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $backgroundPath.AddArc($inset, $inset, $corner * 2, $corner * 2, 180, 90)
        $backgroundPath.AddArc($inset + $edge - $corner * 2, $inset, $corner * 2, $corner * 2, 270, 90)
        $backgroundPath.AddArc($inset + $edge - $corner * 2, $inset + $edge - $corner * 2, $corner * 2, $corner * 2, 0, 90)
        $backgroundPath.AddArc($inset, $inset + $edge - $corner * 2, $corner * 2, $corner * 2, 90, 90)
        $backgroundPath.CloseFigure()
        $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 30, 39, 49))
        $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 62, 83, 99), [float][Math]::Max(1, $size * 0.015))
        try {
            $graphics.FillPath($background, $backgroundPath)
            $graphics.DrawPath($border, $backgroundPath)
        }
        finally {
            $border.Dispose()
            $background.Dispose()
            $backgroundPath.Dispose()
        }

        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float]($size * 0.58), [float]($size * 0.17)),
            [System.Drawing.PointF]::new([float]($size * 0.30), [float]($size * 0.55)),
            [System.Drawing.PointF]::new([float]($size * 0.48), [float]($size * 0.55)),
            [System.Drawing.PointF]::new([float]($size * 0.41), [float]($size * 0.84)),
            [System.Drawing.PointF]::new([float]($size * 0.73), [float]($size * 0.43)),
            [System.Drawing.PointF]::new([float]($size * 0.54), [float]($size * 0.43))
        )
        $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 142, 222, 250))
        try {
            $graphics.FillPolygon($accent, $points)
        }
        finally {
            $accent.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$file = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $frame = $frames[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Length
    }
    foreach ($frame in $frames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Generated $OutputPath"
