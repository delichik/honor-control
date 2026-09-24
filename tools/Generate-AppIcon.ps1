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
        $rect = [System.Drawing.RectangleF]::new(0, 0, $size, $size)
        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 101, 78, 255),
            [System.Drawing.Color]::FromArgb(255, 0, 164, 224),
            45
        )
        try {
            $graphics.FillEllipse($background, [float]($size * 0.05), [float]($size * 0.05), [float]($size * 0.90), [float]($size * 0.90))
        }
        finally {
            $background.Dispose()
        }

        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float]($size * 0.56), [float]($size * 0.18)),
            [System.Drawing.PointF]::new([float]($size * 0.28), [float]($size * 0.55)),
            [System.Drawing.PointF]::new([float]($size * 0.47), [float]($size * 0.55)),
            [System.Drawing.PointF]::new([float]($size * 0.40), [float]($size * 0.83)),
            [System.Drawing.PointF]::new([float]($size * 0.73), [float]($size * 0.43)),
            [System.Drawing.PointF]::new([float]($size * 0.53), [float]($size * 0.43))
        )
        $graphics.FillPolygon([System.Drawing.Brushes]::White, $points)

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
