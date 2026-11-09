<#
.SYNOPSIS
Builds the Egyptian-theme art from sources; no Unity needed (Windows PowerShell 5.1).
.DESCRIPTION
Phase 1 (map), phase 2 (enemies: Art/Egypt/enemies.html) and phase 3 (towers: Art/Egypt/towers.html
for sheet cells, Art/Egypt/icons.html for the build-button icons):
  * Recolors the terrain cells of the original Kenney sheet (Art/Source/) into the
    Egyptian palette (Tools/Art/EgyptTerrain.cs).
  * Draws the decoration cells (130-137) from Art/Egypt/decorations.html (vector art,
    rendered by headless Microsoft Edge, downscaled with high-quality filtering).
The result has the exact size and cell layout of the original sheet, so Unity's existing
sprite slices (GUIDs, names, rects) and every tile/prefab reference keep working.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Art\build-egypt-art.ps1
.EXAMPLE
.\Tools\Art\build-egypt-art.ps1 -Output "$env:TEMP\preview.png"   # preview without touching Assets
#>
[CmdletBinding()]
param(
    [string]$Output,
    [string]$LandmarksDir,
    [string]$IconsDir,
    [string]$Edge
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Output) { $Output = Join-Path $root 'Assets/Sprites/Sheets/towerDefense_tilesheet.png' }
$source = Join-Path $root 'Art/Source/towerDefense_tilesheet_kenney.png'
$work = Join-Path $env:TEMP ('egypt-art-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'EgyptTerrain.cs') -ReferencedAssemblies System.Drawing

# 1. Terrain recolor (decoration cells are drawn separately).
$decorationCells = 130..137
$terrain = Join-Path $work 'terrain.png'
$count = [EgyptTerrain]::RecolorSheet($source, $terrain, [int[]]$decorationCells)
Write-Output "Terrain cells recolored: $count"

# 2. Decorations: render the vector art page with headless Edge, then paste into the cells.
$decorationsPage = Join-Path $root 'Art/Egypt/decorations.html'
if (Test-Path $decorationsPage) {
    if (-not $Edge) {
        $Edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $Edge) { throw 'Microsoft Edge not found; pass -Edge <path to msedge.exe>.' }
    # Screenshots an HTML page with headless Edge (transparent background); returns the PNG path.
    function Render-Page([string]$PagePath, [int]$Width, [int]$Height) {
        $name = [IO.Path]::GetFileNameWithoutExtension($PagePath)
        $png = Join-Path $work "$name.png"
        $edgeArgs = @('--headless', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
                      "--window-size=$Width,$Height", "--screenshot=$png", "--user-data-dir=$(Join-Path $work "edge-$name")",
                      ('file:///' + $PagePath.Replace([char]92, [char]47)))
        $proc = Start-Process -FilePath $Edge -ArgumentList $edgeArgs -PassThru -Wait -WindowStyle Hidden
        if (-not (Test-Path $png)) { throw "Edge did not produce $png (exit $($proc.ExitCode))." }
        return $png
    }
    $shot = Join-Path $work 'decorations.png'
    $scale = 8   # cells are drawn at 512 px and downscaled to 64 px
    $count = $decorationCells.Count
    $args = @('--headless', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
              "--window-size=$(64 * $scale * $count),$(64 * $scale)", "--screenshot=$shot",
              "--user-data-dir=$(Join-Path $work 'edge-profile')", ('file:///' + ($decorationsPage -replace '\\', '/')))
    $p = Start-Process -FilePath $Edge -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
    if (-not (Test-Path $shot)) { throw "Edge did not produce $shot (exit $($p.ExitCode))." }

    $sheet = New-Object System.Drawing.Bitmap $terrain
    $big = New-Object System.Drawing.Bitmap $shot
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    for ($i = 0; $i -lt $count; $i++) {
        $cell = $decorationCells[$i]
        $dest = New-Object System.Drawing.Rectangle (($cell % 23) * 64), ([math]::Floor($cell / 23) * 64), 64, 64
        $src = New-Object System.Drawing.Rectangle ($i * 64 * $scale), 0, (64 * $scale), (64 * $scale)
        $g.DrawImage($big, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
    }
    $big.Dispose()

    # 2b. More cell pages, one 512 px slot per cell, same downscale. Cell lists match the
    #     order of the <svg> slots in each page.
    $cellPages = @(
        @('enemies.html', @(245, 268, 291, 271, 294)),          # phase 2: soldier, tank body, tank cannon, plane, plane shadow
        @('towers.html', @(180, 249, 229, 275, 251, 182, 250))  # phase 3: base, cannon gun, rocket gun, ball, rocket, laser base, laser gun
    )
    foreach ($entry in $cellPages) {
        $page = Join-Path $root "Art/Egypt/$($entry[0])"
        if (-not (Test-Path $page)) { continue }
        $cells = [int[]]$entry[1]
        $shot = Render-Page $page (64 * $scale * $cells.Count) (64 * $scale)
        $big = New-Object System.Drawing.Bitmap $shot
        for ($i = 0; $i -lt $cells.Count; $i++) {
            $cell = $cells[$i]
            $dest = New-Object System.Drawing.Rectangle (($cell % 23) * 64), ([math]::Floor($cell / 23) * 64), 64, 64
            $src = New-Object System.Drawing.Rectangle ($i * 64 * $scale), 0, (64 * $scale), (64 * $scale)
            $g.DrawImage($big, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
        }
        $big.Dispose()
        Write-Output "$($entry[0]): $($cells.Count) cells drawn"
    }
    $g.Dispose()
    $final = Join-Path $work 'final.png'
    $sheet.Save($final, [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
    Copy-Item $final $Output -Force
    Write-Output "Decorations drawn: $count"
}
else {
    Copy-Item $terrain $Output -Force
}
Write-Output "Wrote $Output"

# 3. Landmarks: render Art/Egypt/landmarks.html at 4x and save each slot at in-game size
#    (1 px = 1 world unit) as its own sprite.
$landmarksPage = Join-Path $root 'Art/Egypt/landmarks.html'
if (Test-Path $landmarksPage) {
    if (-not $LandmarksDir) { $LandmarksDir = Join-Path $root 'Assets/Sprites/Egypt/Landmarks' }
    New-Item -ItemType Directory -Force $LandmarksDir | Out-Null
    $shot = Join-Path $work 'landmarks.png'
    $edgeArgs = @('--headless', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
                  '--window-size=3328,768', "--screenshot=$shot", "--user-data-dir=$(Join-Path $work 'edge-profile2')",
                  ('file:///' + ($landmarksPage -replace '\\', '/')))
    $p = Start-Process -FilePath $Edge -ArgumentList $edgeArgs -PassThru -Wait -WindowStyle Hidden
    if (-not (Test-Path $shot)) { throw "Edge did not produce $shot (exit $($p.ExitCode))." }
    $big = New-Object System.Drawing.Bitmap $shot
    # name, slot left (4x px), width, height (final px)
    $slots = @(@('Pyramids', 0, 320, 192), @('Sphinx', 1280, 256, 128), @('RamsesSquare', 2304, 192, 192), @('Obelisk', 3072, 64, 128))
    foreach ($s in $slots) {
        $bmp = New-Object System.Drawing.Bitmap $s[2], $s[3], ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $src = New-Object System.Drawing.Rectangle $s[1], 0, ($s[2] * 4), ($s[3] * 4)
        $g.DrawImage($big, (New-Object System.Drawing.Rectangle 0, 0, $s[2], $s[3]), $src, [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $file = Join-Path $LandmarksDir "$($s[0]).png"
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        Write-Output "Landmark: $file ($($s[2])x$($s[3]))"
    }
    $big.Dispose()
}

# 4. Build-button icons (phase 3): Art/Egypt/icons.html, three 1362x1155 slots, saved over the
#    existing icon files at their original pixel sizes (GUIDs and import settings unchanged).
$iconsPage = Join-Path $root 'Art/Egypt/icons.html'
if (Test-Path $iconsPage) {
    if (-not $IconsDir) { $IconsDir = Join-Path $root 'Assets/Sprites' }
    $shot = Render-Page $iconsPage 4086 1155
    $big = New-Object System.Drawing.Bitmap $shot
    # file (relative to IconsDir), slot, output width, height
    $icons = @(@('gun.png', 0, 165, 140), @('rocket.png', 1, 165, 140), @('Towers/LaserEmitter.png', 2, 1362, 1155))
    foreach ($icon in $icons) {
        $bmp = New-Object System.Drawing.Bitmap $icon[2], $icon[3], ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $src = New-Object System.Drawing.Rectangle ($icon[1] * 1362), 0, 1362, 1155
        $g.DrawImage($big, (New-Object System.Drawing.Rectangle 0, 0, $icon[2], $icon[3]), $src, [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $file = Join-Path $IconsDir $icon[0]
        New-Item -ItemType Directory -Force (Split-Path -Parent $file) | Out-Null
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        Write-Output "Icon: $file ($($icon[2])x$($icon[3]))"
    }
    $big.Dispose()
}
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
