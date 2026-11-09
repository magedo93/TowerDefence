<#
.SYNOPSIS
Publishes the web (WebGL) build to the gh-pages branch for GitHub Pages.
.DESCRIPTION
Copies Builds/WebGL (build it first: .\Tools\unity-check.ps1 -Mode BuildWeb) into the
gh-pages branch using a temporary git worktree, so the main working tree and branch are
never touched. gh-pages contains only the built game (plus .nojekyll so GitHub serves the
Build/ files as-is). The build uses Gzip + decompression fallback, which GitHub Pages
needs because it sends no Content-Encoding header for Unity's .unityweb files.
Pushes with retries (unstable connections). One-time setup on GitHub:
Settings > Pages > Build and deployment > Source: "Deploy from a branch", Branch: gh-pages / (root).
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\deploy-web.ps1
#>
[CmdletBinding()]
param(
    [string]$BuildDir,
    [string]$Branch = 'gh-pages',
    [string]$Remote = 'origin',
    [int]$PushAttempts = 6
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $BuildDir) { $BuildDir = Join-Path $root 'Builds/WebGL' }
$BuildDir = (Resolve-Path -LiteralPath $BuildDir).ProviderPath
if (-not (Test-Path (Join-Path $BuildDir 'index.html'))) { throw "No web build in '$BuildDir'. Run: .\Tools\unity-check.ps1 -Mode BuildWeb" }

# Runs git and returns its exit code; output is echoed. git writes progress and notes to
# stderr, which Windows PowerShell 5.1 would otherwise turn into terminating errors.
# No param block on purpose: git flags such as -q / -c must pass through untouched.
function Invoke-GitCode {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git @args 2>&1 | ForEach-Object { Write-Host "  git: $_" } }
    finally { $ErrorActionPreference = $previous }
    return $LASTEXITCODE
}
function Invoke-Git {
    $code = Invoke-GitCode @args
    if ($code -ne 0) { throw "git $($args -join ' ') failed ($code)" }
}

Push-Location $root
$work = Join-Path $env:TEMP ('td-pages-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$created = $false
try {
    $env:GIT_TERMINAL_PROMPT = '0'
    $source = (& git rev-parse --short HEAD).Trim()
    $hasRemote = (Invoke-GitCode fetch -q $Remote $Branch) -eq 0
    if ($hasRemote) {
        Invoke-Git worktree add -q -B $Branch $work "$Remote/$Branch"
    }
    else {
        Write-Host "No $Remote/$Branch yet; creating it."
        Invoke-Git worktree add -q --detach $work
        Push-Location $work; try { Invoke-Git switch -q --orphan $Branch } finally { Pop-Location }
    }
    $created = $true

    Push-Location $work
    try {
        # Replace everything with the current build.
        Get-ChildItem -Force | Where-Object Name -ne '.git' | Remove-Item -Recurse -Force
        Copy-Item -Path (Join-Path $BuildDir '*') -Destination $work -Recurse -Force
        New-Item -ItemType File -Force '.nojekyll' | Out-Null
        Invoke-Git add -A
        if ((Invoke-GitCode diff --cached --quiet) -eq 0) { Write-Output 'Web build unchanged; nothing to commit.' }
        else {
            $email = (& git config user.email); if (-not $email) { $email = 'shadow.software@gmail.com' }
            Invoke-Git -c "user.email=$email" -c core.safecrlf=false commit -q -m "Publish web build from $source"
            Write-Output "Committed web build from $source to $Branch."
        }
        $pushed = $false
        for ($i = 1; $i -le $PushAttempts -and -not $pushed; $i++) {
            if ((Invoke-GitCode push $Remote "${Branch}:${Branch}") -eq 0) { $pushed = $true }
            else { Write-Output "Push attempt $i failed; retrying."; Start-Sleep -Seconds (10 * $i) }
        }
        if (-not $pushed) { throw "Could not push $Branch after $PushAttempts attempts." }
        Write-Output "Pushed $Branch."
    }
    finally { Pop-Location }

    $url = (& git remote get-url $Remote).Trim()
    if ($url -match 'github\.com[:/]([^/]+)/([^/.]+)') { Write-Output "GitHub Pages URL: https://$($Matches[1].ToLower()).github.io/$($Matches[2])/" }
}
finally {
    if ($created) { $null = Invoke-GitCode worktree remove --force $work }
    $null = Invoke-GitCode worktree prune
    Pop-Location
}
