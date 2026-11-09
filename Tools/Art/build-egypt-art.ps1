<#
.SYNOPSIS
Builds the Egyptian-theme art from sources; no Unity needed (Windows PowerShell 5.1).
.DESCRIPTION
Phase 1 (map) and phase 2 (enemies, cells 245/268/291/271/294 from Art/Egypt/enemies.html):
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

    # 2b. Enemies (phase 2): Art/Egypt/enemies.html, one 512 px slot per cell, same downscale.
    $enemiesPage = Join-Path $root 'Art/Egypt/enemies.html'
    if (Test-Path $enemiesPage) {
        $enemyCells = @(245, 268, 291, 271, 294)  # soldier, tank body, tank cannon, plane, plane shadow
        $shot = Join-Path $work 'enemies.png'
        $edgeArgs = @('--headless', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
                      "--window-size=$(64 * $scale * $enemyCells.Count),$(64 * $scale)", "--screenshot=$shot",
                      "--user-data-dir=$(Join-Path $work 'edge-profile3')", ('file:///' + ($enemiesPage -replace '\\', '/')))
        $p = Start-Process -FilePath $Edge -ArgumentList $edgeArgs -PassThru -Wait -WindowStyle Hidden
        if (-not (Test-Path $shot)) { throw "Edge did not produce $shot (exit $($p.ExitCode))." }
        $big = New-Object System.Drawing.Bitmap $shot
        for ($i = 0; $i -lt $enemyCells.Count; $i++) {
            $cell = $enemyCells[$i]
            $dest = New-Object System.Drawing.Rectangle (($cell % 23) * 64), ([math]::Floor($cell / 23) * 64), 64, 64
            $src = New-Object System.Drawing.Rectangle ($i * 64 * $scale), 0, (64 * $scale), (64 * $scale)
            $g.DrawImage($big, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
        }
        $big.Dispose()
        Write-Output "Enemies drawn: $($enemyCells.Count)"
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
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
