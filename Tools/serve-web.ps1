<#
.SYNOPSIS
Serves the web (WebGL) build on http://localhost:<Port>/ for local testing.
.DESCRIPTION
Browsers will not run a Unity web build opened from disk (file://), so it must be served
over HTTP. This is a minimal static server with the MIME types Unity's build needs. The
build uses Gzip with decompression fallback, so .gz files are served with
Content-Encoding: gzip when possible (faster) and still work without it.
Only listens on localhost; nothing is exposed to the network. Stop with Ctrl+C.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\serve-web.ps1
.EXAMPLE
.\Tools\serve-web.ps1 -Port 8090 -Open
#>
[CmdletBinding()]
param(
    [string]$Root,
    [ValidateRange(1024, 65535)]
    [int]$Port = 8080,
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds/WebGL' }
$Root = (Resolve-Path -LiteralPath $Root).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $Root 'index.html'))) {
    throw "No index.html in '$Root'. Build first: .\Tools\unity-check.ps1 -Mode BuildWeb"
}

$types = @{
    '.html' = 'text/html; charset=utf-8'; '.js' = 'application/javascript'; '.css' = 'text/css'
    '.wasm' = 'application/wasm'; '.data' = 'application/octet-stream'; '.json' = 'application/json'
    '.png' = 'image/png'; '.jpg' = 'image/jpeg'; '.ico' = 'image/x-icon'; '.svg' = 'image/svg+xml'
    '.txt' = 'text/plain'; '.mem' = 'application/octet-stream'; '.symbols.json' = 'application/json'
}

function Get-ContentInfo([string]$file) {
    $name = [IO.Path]::GetFileName($file)
    $encoding = $null
    if ($name.EndsWith('.gz')) { $encoding = 'gzip'; $name = $name.Substring(0, $name.Length - 3) }
    elseif ($name.EndsWith('.br')) { $encoding = 'br'; $name = $name.Substring(0, $name.Length - 3) }
    $ext = [IO.Path]::GetExtension($name).ToLowerInvariant()
    $type = $types[$ext]
    if (-not $type) { $type = 'application/octet-stream' }
    return @{ Type = $type; Encoding = $encoding }
}

$prefix = "http://localhost:$Port/"
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($prefix)
$listener.Start()
Write-Output "Serving $Root at $prefix  (Ctrl+C to stop)"
if ($Open) { Start-Process $prefix }

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response
        try {
            $relative = [Uri]::UnescapeDataString($request.Url.AbsolutePath.TrimStart('/'))
            if ([string]::IsNullOrEmpty($relative)) { $relative = 'index.html' }
            $file = [IO.Path]::GetFullPath((Join-Path $Root $relative))
            # Never serve anything outside the build folder.
            if (-not $file.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) {
                $response.StatusCode = 404
            }
            else {
                $info = Get-ContentInfo $file
                $response.ContentType = $info.Type
                if ($info.Encoding) { $response.AddHeader('Content-Encoding', $info.Encoding) }
                $response.AddHeader('Cache-Control', 'no-cache')
                $bytes = [IO.File]::ReadAllBytes($file)
                $response.ContentLength64 = $bytes.Length
                if ($request.HttpMethod -ne 'HEAD') { $response.OutputStream.Write($bytes, 0, $bytes.Length) }
            }
            Write-Output ("{0} {1} {2}" -f $response.StatusCode, $request.HttpMethod, $request.Url.AbsolutePath)
        }
        catch { $response.StatusCode = 500; Write-Warning $_.Exception.Message }
        finally { $response.Close() }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
