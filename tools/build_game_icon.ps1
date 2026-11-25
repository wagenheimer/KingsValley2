# Generates Icon.bmp (window/Dock icon used by SDL2/MonoGame) and Icon.ico (Windows executable icon).
# The macOS Dock takes its icon from SDL's window icon: an opaque black BMP paints a black
# Dock tile even though AppIcon.icns is transparent. Icon.bmp is therefore written as a
# true 32bpp BGRA image with an explicit alpha mask (0xFF000000), never as 32bpp RGB.
# On macOS, where PowerShell/System.Drawing may be unavailable, use the portable fallback:
#   python3 tools/build_game_icon.py --source src/KingsValley.Desktop/Icon.png --output src/KingsValley.Desktop/Icon.bmp
# Usage (Windows): powershell -File tools/build_game_icon.ps1 -SourceImage <source-art>
# The source artwork is NOT versioned (one-off); only the generated Icon.png/.bmp/.ico are.
param(
    [Parameter(Mandatory = $true, HelpMessage = "Source artwork (square PNG/JPG, >= 512x512)")]
    [string]$SourceImage
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $SourceImage)) {
    throw "Source image not found: $SourceImage"
}

$desktopDir = Resolve-Path "src/KingsValley.Desktop"
$destBmp = Join-Path $desktopDir "Icon.bmp"
$destIco = Join-Path $desktopDir "Icon.ico"
$destPng = Join-Path $desktopDir "Icon.png"

Write-Host "Loading source image: $SourceImage"
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourceImage).Path)

function Write-Bgra32Bitmap {
    param(
        [Parameter(Mandatory = $true)] [System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)] [string]$Path
    )

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $dibSize = 108                      # BITMAPV4HEADER: room for all four channel masks
    $pixelOffset = 14 + $dibSize        # 122
    $imageSize = $width * $height * 4
    $fileSize = $pixelOffset + $imageSize

    # Format32bppArgb stores bytes as B,G,R,A on little-endian machines: copy rows verbatim.
    $rect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $width, $height
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
        $writer = $null
        try {
            $writer = [System.IO.BinaryWriter]::new($stream)

            # BITMAPFILEHEADER
            $writer.Write([byte]0x42)
            $writer.Write([byte]0x4D)
            $writer.Write([uint32]$fileSize)
            $writer.Write([uint16]0)
            $writer.Write([uint16]0)
            $writer.Write([uint32]$pixelOffset)

            # BITMAPV4HEADER core
            $writer.Write([uint32]$dibSize)
            $writer.Write([int32]$width)
            $writer.Write([int32]$height)     # positive height => bottom-up rows
            $writer.Write([uint16]1)          # planes
            $writer.Write([uint16]32)         # bits per pixel
            $writer.Write([uint32]3)          # BI_BITFIELDS
            $writer.Write([uint32]$imageSize)
            $writer.Write([int32]3780)        # horizontal resolution (96 dpi)
            $writer.Write([int32]3780)        # vertical resolution
            $writer.Write([uint32]0)          # colors used
            $writer.Write([uint32]0)          # important colors

            # BGRA masks: the last one is what keeps the Dock background transparent.
            $writer.Write([uint32]16711680)   # 0x00FF0000 red
            $writer.Write([uint32]65280)      # 0x0000FF00 green
            $writer.Write([uint32]255)        # 0x000000FF blue
            $writer.Write([uint32]4278190080) # 0xFF000000 alpha

            $writer.Write([uint32]0x73524742) # LCS_sRGB => endpoints below are ignored
            for ($i = 0; $i -lt 9; $i++) { $writer.Write([uint32]0) } # CIEXYZTRIPLE endpoints
            $writer.Write([uint32]0)          # gamma red
            $writer.Write([uint32]0)          # gamma green
            $writer.Write([uint32]0)          # gamma blue

            $row = New-Object byte[] ($width * 4)
            for ($y = $height - 1; $y -ge 0; $y--) {
                $rowPtr = [System.IntPtr]::Add($data.Scan0, ($y * $data.Stride))
                [System.Runtime.InteropServices.Marshal]::Copy($rowPtr, $row, 0, $row.Length)
                $writer.Write($row)
            }
            $writer.Flush()
        } finally {
            if ($null -ne $writer) { $writer.Dispose() } else { $stream.Dispose() }
        }
    } finally {
        $Bitmap.UnlockBits($data)
    }
}

try {
    # 1. Save Icon.png master (512x512).
    $masterSize = 512
    $masterBmp = New-Object System.Drawing.Bitmap($masterSize, $masterSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($masterBmp)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.DrawImage($src, 0, 0, $masterSize, $masterSize)
    } finally { $g.Dispose() }

    $masterBmp.Save($destPng, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Icon.png generated: $destPng (512x512)"

    # 2. Generate Icon.bmp (256x256, 32bpp BGRA + alpha) for SDL2 / MonoGame DesktopGL.
    $bmpSize = 256
    $sdlBmp = New-Object System.Drawing.Bitmap($bmpSize, $bmpSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($sdlBmp)
    try {
        $g2.Clear([System.Drawing.Color]::Transparent)
        $g2.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $g2.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g2.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g2.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g2.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g2.DrawImage($masterBmp, 0, 0, $bmpSize, $bmpSize)
    } finally { $g2.Dispose() }

    Write-Bgra32Bitmap -Bitmap $sdlBmp -Path $destBmp
    $sdlBmp.Dispose()
    Write-Host "Icon.bmp generated: $destBmp (256x256, 32bpp BGRA with alpha; $((Get-Item $destBmp).Length) bytes)"

    # 3. Generate the multi-resolution Icon.ico for Windows.
    & (Join-Path (Get-Location) "tools/make_ico.ps1") -SourcePng $destPng -DestIco $destIco -Sizes @(16, 24, 32, 48, 64, 128, 256)
    Write-Host "Icon.ico generated successfully: $destIco"

    $masterBmp.Dispose()
} finally {
    $src.Dispose()
}
