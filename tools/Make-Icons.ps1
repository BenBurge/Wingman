#Requires -Version 7
<#
.SYNOPSIS
    Renders the Wingman app icon, tray glyphs, logo, and social preview into assets/.

.DESCRIPTION
    Draws the Wingman "W" glyph (a single 2-unit stroke on a 16-unit grid,
    per docs/DESIGN.md "Distribution and background pieces" and the tray
    icon mockup in docs/mockups/wingman-screens.html) at each required
    pixel size, composes multi-resolution ICO files by hand, and writes
    them under assets/. It also writes PNGs for the README and GitHub: the
    app icon, the bare glyph as a logo, and the 1280x640 social preview
    image. Re-run any time the glyph or palette changes; every file is
    overwritten in place.

.NOTES
    Requires Windows (System.Drawing / GDI+ is not available cross-platform).
#>

if (-not $IsWindows) {
    Write-Error "Make-Icons.ps1 requires Windows (System.Drawing / GDI+ is Windows-only)."
    exit 1
}

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'assets'
$trayDir = Join-Path $assetsDir 'tray'
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
New-Item -ItemType Directory -Force -Path $trayDir | Out-Null

# Palette from docs/DESIGN.md.
$ColorAmber = [System.Drawing.ColorTranslator]::FromHtml('#F0B54A')
$ColorDark = [System.Drawing.ColorTranslator]::FromHtml('#171B26')
$ColorWhite = [System.Drawing.Color]::White
$ColorGray = [System.Drawing.ColorTranslator]::FromHtml('#6C7590')
$ColorRed = [System.Drawing.ColorTranslator]::FromHtml('#E5707A')
$ColorTitle = [System.Drawing.ColorTranslator]::FromHtml('#D8DEE9')
$ColorTagline = [System.Drawing.ColorTranslator]::FromHtml('#8FA3C7')

# Badge geometry, in 16-unit grid units: a diameter-6 circle in the
# bottom-right quadrant with a 1-unit transparent gap separating it from
# the glyph stroke underneath.
$BadgeCenterX = [float]12.0
$BadgeCenterY = [float]12.0
$BadgeRadius = [float]3.0
$BadgeGap = [float]1.0

function New-WGlyphPath {
    # The W: three straight strokes (down-up-down) followed by a curved
    # wing tip that lifts above the other peaks and flicks right, matching
    # the "A" glyph used throughout docs/mockups/wingman-screens.html
    # (path `M1.5 4 L4.3 12.5 L7 6 L9.7 12.5 C10.8 8 12.4 5 15 2.5`).
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddLine([float]1.5, [float]4.0, [float]4.3, [float]12.5)
    $path.AddLine([float]4.3, [float]12.5, [float]7.0, [float]6.0)
    $path.AddLine([float]7.0, [float]6.0, [float]9.7, [float]12.5)
    $path.AddBezier([float]9.7, [float]12.5, [float]10.8, [float]8.0, [float]12.4, [float]5.0, [float]15.0, [float]2.5)
    return $path
}

function New-RoundedRectPath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $d = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-GridGraphics {
    # Creates a transparent bitmap and a Graphics whose world transform
    # maps the 16-unit design grid onto it, so every draw call below is
    # written once in grid units and simply renders sharper at larger sizes.
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = $Size / 16.0
    $g.ScaleTransform($scale, $scale)
    return @{ Bitmap = $bmp; Graphics = $g }
}

function Add-Glyph {
    param([System.Drawing.Graphics]$Graphics, [System.Drawing.Color]$Color)
    $path = New-WGlyphPath
    $pen = New-Object System.Drawing.Pen ($Color, [float]2.0)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $Graphics.DrawPath($pen, $path)
    $pen.Dispose()
    $path.Dispose()
}

function Add-Tile {
    param([System.Drawing.Graphics]$Graphics)
    # Corner radius ~20% of the 16-unit tile.
    $path = New-RoundedRectPath -X 0 -Y 0 -Width 16 -Height 16 -Radius 3.2
    $brush = New-Object System.Drawing.SolidBrush $ColorAmber
    $Graphics.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()
}

function Add-BadgeGap {
    # Erases (rather than blends) a hole slightly larger than the badge so
    # the badge reads as a distinct dot against the glyph stroke beneath it.
    param([System.Drawing.Graphics]$Graphics)
    $holeRadius = $BadgeRadius + $BadgeGap
    $previousMode = $Graphics.CompositingMode
    $Graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
    $Graphics.FillEllipse($brush, $BadgeCenterX - $holeRadius, $BadgeCenterY - $holeRadius, $holeRadius * 2, $holeRadius * 2)
    $brush.Dispose()
    $Graphics.CompositingMode = $previousMode
}

function Add-BadgeDot {
    param([System.Drawing.Graphics]$Graphics, [System.Drawing.Color]$Color)
    Add-BadgeGap -Graphics $Graphics
    $brush = New-Object System.Drawing.SolidBrush $Color
    $Graphics.FillEllipse($brush, $BadgeCenterX - $BadgeRadius, $BadgeCenterY - $BadgeRadius, $BadgeRadius * 2, $BadgeRadius * 2)
    $brush.Dispose()
}

function Add-BadgeRing {
    param([System.Drawing.Graphics]$Graphics, [System.Drawing.Color]$Color)
    Add-BadgeGap -Graphics $Graphics
    $pen = New-Object System.Drawing.Pen ($Color, [float]1.5)
    $Graphics.DrawEllipse($pen, $BadgeCenterX - $BadgeRadius, $BadgeCenterY - $BadgeRadius, $BadgeRadius * 2, $BadgeRadius * 2)
    $pen.Dispose()
}

function Write-IcoFile {
    # Builds a multi-image ICO by hand (ICONDIR + ICONDIRENTRY[] + PNG
    # payloads) rather than System.Drawing.Icon.Save, which only ever
    # writes a single image.
    param([string]$Path, [System.Drawing.Bitmap[]]$Bitmaps)

    $pngBytes = foreach ($bmp in $Bitmaps) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
        $ms.Dispose()
    }

    $count = $Bitmaps.Count
    $offset = 6 + (16 * $count)

    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    try {
        $bw = New-Object System.IO.BinaryWriter $fs
        $bw.Write([UInt16]0)      # reserved, must be 0
        $bw.Write([UInt16]1)      # type: 1 = icon
        $bw.Write([UInt16]$count)

        for ($i = 0; $i -lt $count; $i++) {
            $bmp = $Bitmaps[$i]
            $widthByte = if ($bmp.Width -ge 256) { 0 } else { $bmp.Width }
            $heightByte = if ($bmp.Height -ge 256) { 0 } else { $bmp.Height }
            $bw.Write([byte]$widthByte)
            $bw.Write([byte]$heightByte)
            $bw.Write([byte]0)       # color palette count: none
            $bw.Write([byte]0)       # reserved
            $bw.Write([UInt16]1)     # color planes
            $bw.Write([UInt16]32)    # bits per pixel
            $bw.Write([UInt32]$pngBytes[$i].Length)
            $bw.Write([UInt32]$offset)
            $offset += $pngBytes[$i].Length
        }

        foreach ($png in $pngBytes) {
            $bw.Write($png)
        }
        $bw.Flush()
    }
    finally {
        $fs.Dispose()
    }
}

function New-IconSet {
    # Renders one glyph action at each requested size and writes the
    # resulting ICO, printing the file and its size list as it goes.
    param(
        [string]$Path,
        [int[]]$Sizes,
        [scriptblock]$Draw
    )
    $bitmaps = foreach ($size in $Sizes) {
        $grid = New-GridGraphics -Size $size
        & $Draw $grid.Graphics
        $grid.Graphics.Dispose()
        $grid.Bitmap
    }
    Write-IcoFile -Path $Path -Bitmaps $bitmaps
    foreach ($bmp in $bitmaps) { $bmp.Dispose() }
    $sizeList = ($Sizes -join ', ')
    Write-Host "Wrote $Path (sizes: $sizeList)"
}

# App icon: amber rounded tile, dark glyph.
New-IconSet -Path (Join-Path $assetsDir 'wingman.ico') -Sizes @(16, 24, 32, 48, 64, 128, 256) -Draw {
    param($g)
    Add-Tile -Graphics $g
    Add-Glyph -Graphics $g -Color $ColorDark
}

$traySizes = @(16, 20, 24, 32)

# Tray glyph only, no tile: white for dark taskbars, dark for light ones.
New-IconSet -Path (Join-Path $trayDir 'wingman-tray.ico') -Sizes $traySizes -Draw {
    param($g)
    Add-Glyph -Graphics $g -Color $ColorWhite
}

New-IconSet -Path (Join-Path $trayDir 'wingman-tray-dark.ico') -Sizes $traySizes -Draw {
    param($g)
    Add-Glyph -Graphics $g -Color $ColorDark
}

# Badge variants of the tray glyph, indicating background-task state.
New-IconSet -Path (Join-Path $trayDir 'wingman-tray-updates.ico') -Sizes $traySizes -Draw {
    param($g)
    Add-Glyph -Graphics $g -Color $ColorWhite
    Add-BadgeDot -Graphics $g -Color $ColorAmber
}

New-IconSet -Path (Join-Path $trayDir 'wingman-tray-working.ico') -Sizes $traySizes -Draw {
    param($g)
    Add-Glyph -Graphics $g -Color $ColorWhite
    Add-BadgeRing -Graphics $g -Color $ColorAmber
}

New-IconSet -Path (Join-Path $trayDir 'wingman-tray-failed.ico') -Sizes $traySizes -Draw {
    param($g)
    Add-Glyph -Graphics $g -Color $ColorWhite
    Add-BadgeDot -Graphics $g -Color $ColorRed
}

New-IconSet -Path (Join-Path $trayDir 'wingman-tray-paused.ico') -Sizes $traySizes -Draw {
    param($g)
    Add-Glyph -Graphics $g -Color $ColorGray
}

function Save-Png {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Path)
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Wrote $Path ($($Bitmap.Width)x$($Bitmap.Height))"
}

function Split-GreedyLines {
    # Packs tokens joined by $Separator into lines no wider than $MaxWidth,
    # so a wrapped line never starts or ends on a separator.
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Font]$Font,
        [string[]]$Tokens,
        [string]$Separator,
        [float]$MaxWidth
    )
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $lines = [System.Collections.Generic.List[string]]::new()
    $current = ''
    foreach ($token in $Tokens) {
        $candidate = if ($current) { $current + $Separator + $token } else { $token }
        $width = $Graphics.MeasureString($candidate, $Font, [System.Drawing.PointF]::Empty, $format).Width
        if ($current -and $width -gt $MaxWidth) {
            $lines.Add($current)
            $current = $token
        }
        else {
            $current = $candidate
        }
    }
    if ($current) { $lines.Add($current) }
    return , $lines.ToArray()
}

function Split-TextLines {
    # Wraps into as few lines as $MaxWidth allows, then narrows the width
    # for as long as the line count holds, so the lines come out close to
    # equal length instead of leaving a short orphan on the last one.
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Font]$Font,
        [string[]]$Tokens,
        [string]$Separator,
        [float]$MaxWidth
    )
    $lineCount = (Split-GreedyLines -Graphics $Graphics -Font $Font -Tokens $Tokens -Separator $Separator -MaxWidth $MaxWidth).Count
    $width = $MaxWidth
    while ($width -gt 4) {
        $narrower = Split-GreedyLines -Graphics $Graphics -Font $Font -Tokens $Tokens -Separator $Separator -MaxWidth ($width - 4)
        if ($narrower.Count -ne $lineCount) { break }
        $width -= 4
    }
    return , (Split-GreedyLines -Graphics $Graphics -Font $Font -Tokens $Tokens -Separator $Separator -MaxWidth $width)
}

# App icon as a PNG, for the README and anywhere an ICO does not fit.
$appPng = New-GridGraphics -Size 256
Add-Tile -Graphics $appPng.Graphics
Add-Glyph -Graphics $appPng.Graphics -Color $ColorDark
$appPng.Graphics.Dispose()
Save-Png -Bitmap $appPng.Bitmap -Path (Join-Path $assetsDir 'wingman.png')
$appPng.Bitmap.Dispose()

# README logo: the bare amber glyph reads on both of GitHub's themes.
$logoPng = New-GridGraphics -Size 512
Add-Glyph -Graphics $logoPng.Graphics -Color $ColorAmber
$logoPng.Graphics.Dispose()
Save-Png -Bitmap $logoPng.Bitmap -Path (Join-Path $assetsDir 'wingman-logo.png')
$logoPng.Bitmap.Dispose()

# Social preview: GitHub's 1280x640 card. The tile and the text block to
# its right share a vertical center, and the pair is centered horizontally
# once the text has been wrapped and measured.
$previewWidth = 1280
$previewHeight = 640
$margin = 112
$tileSize = 360
$textGap = 72

$preview = New-Object System.Drawing.Bitmap $previewWidth, $previewHeight, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($preview)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
$g.Clear($ColorDark)

$pixel = [System.Drawing.GraphicsUnit]::Pixel
$titleFont = New-Object System.Drawing.Font ('Segoe UI', [float]120, [System.Drawing.FontStyle]::Bold, $pixel)
$taglineFont = New-Object System.Drawing.Font ('Segoe UI', [float]44, [System.Drawing.FontStyle]::Regular, $pixel)
$featuresFont = New-Object System.Drawing.Font ('Segoe UI', [float]26, [System.Drawing.FontStyle]::Regular, $pixel)
$format = [System.Drawing.StringFormat]::GenericTypographic

$wrapWidth = $previewWidth - (2 * $margin) - $tileSize - $textGap
$taglineLines = Split-TextLines -Graphics $g -Font $taglineFont -Separator ' ' -MaxWidth $wrapWidth `
    -Tokens ('A keyboard-driven terminal UI for winget' -split ' ')
$featureLines = Split-TextLines -Graphics $g -Font $featuresFont -Separator ' · ' -MaxWidth $wrapWidth `
    -Tokens @('winget batches', 'elevation', 'update policies', 'bundles', 'scheduled checks', 'tray')

# Each entry is one line: its font, color, and the space above it.
$blocks = [System.Collections.Generic.List[object]]::new()
$blocks.Add(@{ Text = 'Wingman'; Font = $titleFont; Color = $ColorTitle; Before = 0 })
for ($i = 0; $i -lt $taglineLines.Count; $i++) {
    $before = if ($i -eq 0) { 8 } else { 0 }
    $blocks.Add(@{ Text = $taglineLines[$i]; Font = $taglineFont; Color = $ColorTagline; Before = $before })
}
for ($i = 0; $i -lt $featureLines.Count; $i++) {
    $before = if ($i -eq 0) { 28 } else { 2 }
    $blocks.Add(@{ Text = $featureLines[$i]; Font = $featuresFont; Color = $ColorGray; Before = $before })
}

$blockHeight = 0.0
$blockWidth = 0.0
foreach ($block in $blocks) {
    $blockHeight += $block.Before + $block.Font.GetHeight($g)
    $lineWidth = $g.MeasureString($block.Text, $block.Font, [System.Drawing.PointF]::Empty, $format).Width
    $blockWidth = [Math]::Max($blockWidth, $lineWidth)
}

$tileLeft = [float](($previewWidth - ($tileSize + $textGap + $blockWidth)) / 2)
$tileTop = [float](($previewHeight - $tileSize) / 2)
$g.TranslateTransform($tileLeft, $tileTop)
$g.ScaleTransform([float]($tileSize / 16.0), [float]($tileSize / 16.0))
Add-Tile -Graphics $g
Add-Glyph -Graphics $g -Color $ColorDark
$g.ResetTransform()

# GDI+ line height includes the font's internal leading and descent, which
# leaves the visible text sitting low; the small upward nudge recenters it
# on the tile optically.
$opticalLift = 10
$textLeft = [float]($tileLeft + $tileSize + $textGap)
$y = [float](($previewHeight - $blockHeight) / 2 - $opticalLift)
foreach ($block in $blocks) {
    $y += $block.Before
    $brush = New-Object System.Drawing.SolidBrush $block.Color
    $g.DrawString($block.Text, $block.Font, $brush, $textLeft, $y, $format)
    $brush.Dispose()
    $y += $block.Font.GetHeight($g)
}

$titleFont.Dispose()
$taglineFont.Dispose()
$featuresFont.Dispose()
$g.Dispose()
Save-Png -Bitmap $preview -Path (Join-Path $assetsDir 'social-preview.png')
$preview.Dispose()

# --- Verification --------------------------------------------------------

Write-Host ""
Write-Host "Verifying generated assets..."

$allFiles = @(
    (Join-Path $assetsDir 'wingman.ico'),
    (Join-Path $trayDir 'wingman-tray.ico'),
    (Join-Path $trayDir 'wingman-tray-dark.ico'),
    (Join-Path $trayDir 'wingman-tray-updates.ico'),
    (Join-Path $trayDir 'wingman-tray-working.ico'),
    (Join-Path $trayDir 'wingman-tray-failed.ico'),
    (Join-Path $trayDir 'wingman-tray-paused.ico')
)

$allOk = $true

$expectedPngs = [ordered]@{
    (Join-Path $assetsDir 'wingman.png')        = @(256, 256)
    (Join-Path $assetsDir 'wingman-logo.png')   = @(512, 512)
    (Join-Path $assetsDir 'social-preview.png') = @(1280, 640)
}
foreach ($file in $expectedPngs.Keys) {
    $expectedWidth, $expectedHeight = $expectedPngs[$file]
    $width = 0
    $height = 0
    if (Test-Path $file) {
        $image = [System.Drawing.Image]::FromFile($file)
        $width = $image.Width
        $height = $image.Height
        $image.Dispose()
    }
    $sizeOk = ($width -eq $expectedWidth -and $height -eq $expectedHeight)
    $status = if ($sizeOk) { 'OK' } else { 'FAIL'; $allOk = $false }
    Write-Host ("  {0,-4} {1} ({2}x{3})" -f $status, $file, $width, $height)
}

foreach ($file in $allFiles) {
    $exists = Test-Path $file
    $loads = $false
    $sizeBytes = 0
    if ($exists) {
        $sizeBytes = (Get-Item $file).Length
        try {
            $icon = [System.Drawing.Icon]::new($file)
            $icon.Dispose()
            $loads = $true
        }
        catch {
            $loads = $false
        }
    }
    $status = if ($exists -and $loads) { 'OK' } else { 'FAIL'; $allOk = $false }
    Write-Host ("  {0,-4} {1} ({2} bytes)" -f $status, $file, $sizeBytes)
}

$appIconPath = Join-Path $assetsDir 'wingman.ico'
$appIconBytes = [System.IO.File]::ReadAllBytes($appIconPath)
$headerOk = ($appIconBytes[0] -eq 0x00 -and $appIconBytes[1] -eq 0x00 -and $appIconBytes[2] -eq 0x01 -and $appIconBytes[3] -eq 0x00)
$countOk = ([BitConverter]::ToUInt16($appIconBytes, 4)) -eq 7
Write-Host ("  {0,-4} wingman.ico header is 00 00 01 00 with image count 7" -f $(if ($headerOk -and $countOk) { 'OK' } else { 'FAIL'; $allOk = $false }))

if (-not $allOk) {
    Write-Error "Asset verification failed; see FAIL lines above."
    exit 1
}

Write-Host ""
Write-Host "All assets generated and verified."
