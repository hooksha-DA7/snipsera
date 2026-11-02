param([string]$Output = (Join-Path $PSScriptRoot '../src/Framelet/Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$null = New-Item -ItemType Directory -Path $Output -Force
# Render the existing capture-corner glyph at native package/icon resolutions.
foreach ($size in @(44,50,150,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, [single](2.8 * $size / 32))
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::FromArgb(32,95,219))
        foreach ($corner in @(@(7,13,7,7,13,7),@(19,7,25,7,25,13),@(25,19,25,25,19,25),@(13,25,7,25,7,19))) {
            $points = for ($index = 0; $index -lt 6; $index += 2) {
                [Drawing.PointF]::new([single]($corner[$index] * $size / 32), [single]($corner[$index + 1] * $size / 32))
            }
            $graphics.DrawLines($pen,[Drawing.PointF[]]$points)
        }
        $bitmap.Save((Join-Path $Output "Snipsera-$size.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
$png = [IO.File]::ReadAllBytes((Join-Path $Output 'Snipsera-256.png'))
$stream = [IO.File]::Create((Join-Path $Output 'Snipsera.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte[]]@(0,0,0,0)); $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output "Rendered Snipsera package assets in $Output"
