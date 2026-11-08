<#
.SYNOPSIS
Validates a Windows Unity installation, compiles a project, or runs Editor tests.
.DESCRIPTION
Check (default) does not launch Unity. Compile imports/compiles in batch mode.
EditMode and PlayMode run Unity Test Framework without the premature -quit flag.
Logs and NUnit XML results go into unique directories under AutomationLogs/.
Batch runs can update Unity's generated caches. API updating is not enabled.
Close the project's Editor first. Existing project locks are never removed.
Exit codes: 0 success; 1 validation, process, log or test failure.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\unity-check.ps1 -Mode Compile
.EXAMPLE
.\Tools\unity-check.ps1 -Mode EditMode -EditorPath 'D:\Unity\Editor\Unity.exe'
.EXAMPLE
.\Tools\unity-check.ps1 -Mode Build   # Windows 64-bit player into Builds/Windows/
#>
[CmdletBinding()]
param(
    [ValidateSet('Check', 'Compile', 'EditMode', 'PlayMode', 'Build')]
    [string]$Mode = 'Check',
    [string]$ProjectPath,
    [string]$EditorPath,
    [ValidateRange(1, 1440)]
    [int]$TimeoutMinutes = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runLog = $null
$runMutex = $null
$ownsMutex = $false
$unityProcess = $null
$unityStarted = $false
$resultCode = 1

function Write-Status([string]$Message) {
    Write-Output $Message
    if ($script:runLog) {
        Add-Content -LiteralPath $script:runLog -Value $Message -Encoding UTF8
    }
}

function Get-EditorVersion([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
}

function Assert-ProjectAvailable([string]$Root) {
    $lockPath = Join-Path $Root 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lockPath) {
        # Failed startup can leave an unlocked marker behind. Probe read-only:
        # never delete it or hold Unity's own lock while launching the Editor.
        try {
            $lockProbe = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $lockProbe.Dispose()
        }
        catch {
            throw "Project lock is held or inaccessible: '$lockPath'. Close Unity before continuing. This script never removes locks."
        }
    }
    $editors = @(Get-Process -Name Unity -ErrorAction SilentlyContinue)
    if ($editors.Count -eq 0) { return }
    try {
        $processInfo = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'")
    }
    catch {
        throw 'Unity is running, but project inspection was denied. Close Unity before a batch run.'
    }
    if ($processInfo.Count -ne $editors.Count) {
        throw 'Unity processes changed during inspection. Close Unity and retry.'
    }
    foreach ($editor in $processInfo) {
        # Unity Hub also ships a unity.exe CLI service. It is not an Editor
        # and does not open projects. Match both its installation and command.
        $executable = [string]$editor.ExecutablePath
        $hubServicePath = '(?i)\\(?:UnityTechnologies\.UnityHub_[^\\]+\\app|Unity Hub)\\resources\\unity\.exe$'
        $hubServiceCommand = '^"?' + [regex]::Escape($executable) + '"?\s+serve(?:\s|$)'
        if ($executable -match $hubServicePath -and [string]$editor.CommandLine -match $hubServiceCommand) {
            continue
        }
        $pathMatch = [regex]::Match([string]$editor.CommandLine, '(?i)(?:^|\s)-projectPath\s+(?:"([^"]+)"|(\S+))')
        if (-not $pathMatch.Success) {
            throw "Cannot establish the project used by Unity PID $($editor.ProcessId). Close that Editor first."
        }
        $openPath = $pathMatch.Groups[1].Value
        if (-not $openPath) { $openPath = $pathMatch.Groups[2].Value }
        if (-not [IO.Path]::IsPathRooted($openPath)) {
            throw "Unity PID $($editor.ProcessId) uses a relative project path. Close it first."
        }
        $openRoot = [IO.Path]::GetFullPath($openPath).TrimEnd('\', '/')
        if ($openRoot -ieq $Root.TrimEnd('\', '/')) {
            throw "This project is open in Unity PID $($editor.ProcessId). Close the Editor first."
        }
    }
}

function Assert-UnityLog([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Unity produced no log: '$Path'."
    }
    $text = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($text)) { throw 'Unity produced an empty log.' }
    $pattern = '(?im)^.*(?:\berror\s+(?:CS|BC|IL|MSB)\d+\b|\bcompiler errors?\b|\bcompilation (?:failed|failure)\b|\bcompiling.*failed\b|Assembly has duplicate references|Aborting batchmode|Fatal error|another Unity instance|project is (?:already )?open|Failed to (?:load|open) project|No valid Unity Editor license|Failed to activate.*license).*$'
    $failures = @([regex]::Matches($text, $pattern))
    if ($failures.Count -gt 0) {
        foreach ($failure in ($failures | Select-Object -First 20)) {
            Write-Status $failure.Value.Trim()
        }
        throw "Unity log contains compilation/startup failures ($($failures.Count) matching lines), regardless of process exit code. See '$Path'."
    }
    if ($text -notmatch '(?i)Exiting batchmode successfully|Batchmode quit successfully|Application is exiting with return code 0|Test run completed\. Exiting with code 0 \(Ok\)\. Run completed\.') {
        throw "Unity log has no successful batch completion marker. Inspect '$Path'."
    }
}

function Assert-TestResults([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Unity did not produce test results: '$Path'."
    }
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    $root = $document.DocumentElement
    if ($null -eq $root -or $root.Name -notin @('test-run', 'test-results')) {
        throw 'Unrecognized test XML. Expected NUnit test results.'
    }
    foreach ($attribute in @('failed', 'failures', 'errors')) {
        $value = $root.GetAttribute($attribute)
        if ($value -and [int]$value -gt 0) {
            throw "Test results report $attribute=$value. See '$Path'."
        }
    }
    $failedNodes = @($document.SelectNodes("//*[@result='Failed' or @result='Error' or @result='Failure' or @result='Cancelled' or @result='Inconclusive' or @success='False' or @success='false'] | //failure | //error"))
    if ($failedNodes.Count -gt 0) { throw "Test XML contains unsuccessful results. See '$Path'." }
    $cases = @($document.SelectNodes('//test-case'))
    $executed = @($cases | Where-Object {
        $_.GetAttribute('result') -in @('Passed', 'Success', 'Failed', 'Failure', 'Error') -and
        $_.GetAttribute('executed') -notin @('False', 'false')
    })
    if ($executed.Count -eq 0) {
        throw 'Zero tests executed. Missing test assemblies or all tests skipped is not a passing run.'
    }
    Write-Status "Test results validated: $($executed.Count) executed tests; no failures or errors. XML: $Path"
}

try {
    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        $toolsDirectory = $PSScriptRoot
        if ([string]::IsNullOrWhiteSpace($toolsDirectory)) {
            $toolsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
        }
        $ProjectPath = Split-Path -Parent $toolsDirectory
    }
    $projectRoot = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
    if (-not (Test-Path -LiteralPath $projectRoot -PathType Container)) {
        throw "Project path is not a directory: '$projectRoot'."
    }
    foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
        if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $directory) -PathType Container)) {
            throw "Missing Unity project directory: $directory"
        }
    }
    $versionFile = Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'
    $versionMatch = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), '(?m)^m_EditorVersion:\s*(\S+)')
    if (-not $versionMatch.Success) { throw 'Cannot read m_EditorVersion from ProjectVersion.txt.' }
    $requiredVersion = $versionMatch.Groups[1].Value
    $manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
    if (-not $manifest.PSObject.Properties['dependencies']) { throw 'Package manifest has no dependencies object.' }
    if ($Mode -in @('EditMode', 'PlayMode') -and -not $manifest.dependencies.PSObject.Properties['com.unity.test-framework']) {
        throw 'Unity Test Framework is not declared in Packages/manifest.json.'
    }

    # Serialize this tool's runs for the canonical project path across sessions.
    $hashProvider = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = [BitConverter]::ToString($hashProvider.ComputeHash([Text.Encoding]::UTF8.GetBytes($projectRoot.TrimEnd('\', '/').ToUpperInvariant()))).Replace('-', '')
    }
    finally { $hashProvider.Dispose() }
    $runMutex = New-Object System.Threading.Mutex($false, "Global\UnityCheck_$hash")
    try { $ownsMutex = $runMutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex) { throw 'Another unity-check run is already using this project.' }

    $runId = '{0}-{1}-{2}' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), $Mode, ([guid]::NewGuid().ToString('N').Substring(0, 8))
    $runDirectory = Join-Path $projectRoot "AutomationLogs/$runId"
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $runLog = Join-Path $runDirectory 'automation.log'
    Write-Status "Mode: $Mode"
    Write-Status "Project: $projectRoot"
    Write-Status "Required Unity version: $requiredVersion"
    Write-Status "Automation log: $runLog"

    $versionPattern = '^' + [regex]::Escape($requiredVersion) + '(?:_|\s|\(|$)'
    if ([string]::IsNullOrWhiteSpace($EditorPath)) {
        $candidates = @()
        if ($env:UNITY_EDITOR_PATH) { $candidates += $env:UNITY_EDITOR_PATH }
        if ($env:ProgramFiles) {
            $candidates += Join-Path $env:ProgramFiles "Unity/Hub/Editor/$requiredVersion/Editor/Unity.exe"
            $candidates += Join-Path $env:ProgramFiles 'Unity/Editor/Unity.exe'
        }
        $unityCommand = Get-Command Unity.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($unityCommand) { $candidates += $unityCommand.Source }
        foreach ($candidate in ($candidates | Select-Object -Unique)) {
            if ((Get-EditorVersion $candidate) -match $versionPattern) { $EditorPath = $candidate; break }
        }
        if ([string]::IsNullOrWhiteSpace($EditorPath)) {
            throw "Unity $requiredVersion not found. Install it through Unity Hub or specify -EditorPath '...\Editor\Unity.exe'."
        }
    }
    $installedVersion = Get-EditorVersion $EditorPath
    if ($installedVersion -notmatch $versionPattern) {
        throw "Editor '$EditorPath' has version '$installedVersion'; expected '$requiredVersion'."
    }
    $EditorPath = (Resolve-Path -LiteralPath $EditorPath).ProviderPath
    if ([IO.Path]::GetFileName($EditorPath) -ine 'Unity.exe') { throw '-EditorPath must point to Unity.exe.' }
    Write-Status "Editor: $EditorPath ($installedVersion)"

    if ($Mode -eq 'Check') {
        try {
            Assert-ProjectAvailable $projectRoot
            Write-Status 'Project is available for a batch run.'
        }
        catch {
            Write-Status "Batch readiness warning: $($_.Exception.Message)"
        }
        Write-Status 'Check passed: installation, project directories, version, and package JSON validated. No compilation or tests were run.'
    }
    else {
        Assert-ProjectAvailable $projectRoot
        $unityLog = Join-Path $runDirectory 'Unity.log'
        $testResults = Join-Path $runDirectory 'TestResults.xml'
        $unityArguments = @('-batchmode', '-projectPath', $projectRoot, '-logFile', $unityLog)
        if ($Mode -eq 'Compile') {
            $unityArguments += @('-nographics', '-quit')
        }
        elseif ($Mode -eq 'Build') {
            # Windows 64-bit player of the enabled Build Settings scenes (Assets/Editor/BuildScript.cs).
            $buildOutput = Join-Path $projectRoot 'Builds/Windows/TowerDefense.exe'
            $unityArguments += @('-quit', '-buildTarget', 'Win64', '-executeMethod', 'BuildScript.BuildWindows', '-buildOutput', $buildOutput)
        }
        else {
            # The test runner owns shutdown. Keep graphics available for Play Mode.
            $unityArguments += @('-runTests', '-testPlatform', $Mode, '-testResults', $testResults)
        }
        Write-Status "Unity log: $unityLog"
        Write-Status ('Arguments: ' + (($unityArguments | ForEach-Object { '"' + $_ + '"' }) -join ' '))
        # Quote arguments independently. No shell evaluation or API updater.
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $EditorPath
        $startInfo.Arguments = ($unityArguments | ForEach-Object { '"' + $_.TrimEnd('\') + '"' }) -join ' '
        $startInfo.WorkingDirectory = $projectRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $unityProcess = New-Object System.Diagnostics.Process
        $unityProcess.StartInfo = $startInfo
        Assert-ProjectAvailable $projectRoot
        if (-not $unityProcess.Start()) { throw 'Failed to start Unity.' }
        $unityStarted = $true
        Write-Status "Started Unity PID $($unityProcess.Id); timeout $TimeoutMinutes minutes."
        $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
        while (-not $unityProcess.WaitForExit(1000)) {
            if ([DateTime]::UtcNow -ge $deadline) {
                $unityProcess.Kill()
                $unityProcess.WaitForExit()
                throw "Unity exceeded the $TimeoutMinutes minute timeout. The automation-owned Editor was stopped. Inspect '$unityLog'."
            }
        }
        Write-Status "Unity exit code: $($unityProcess.ExitCode)"
        # Inspect the actual log even if Unity reports a failing exit code.
        Assert-UnityLog $unityLog
        if ($unityProcess.ExitCode -ne 0) { throw "Unity exited with code $($unityProcess.ExitCode). Inspect '$unityLog'." }
        if ($Mode -in @('EditMode', 'PlayMode')) { Assert-TestResults $testResults }
        if ($Mode -eq 'Build') {
            $buildLog = Get-Content -LiteralPath $unityLog -Raw
            if ($buildLog -notmatch '\[BuildScript\] Player build succeeded\.') { throw "Build success marker missing from '$unityLog'." }
            if (-not (Test-Path -LiteralPath $buildOutput -PathType Leaf)) { throw "Player executable missing: '$buildOutput'." }
            Write-Status "Player build written to: $buildOutput"
        }
        Write-Status "$Mode passed: Unity completed and its log contains no detected compilation/startup failures."
        if ($Mode -eq 'Compile') { Write-Status 'This validates Editor compilation, not a standalone player build.' }
    }
    $resultCode = 0
}
catch {
    $failureMessage = $_.Exception.Message
    try { Write-Status "FAILED: $failureMessage" }
    catch { Write-Output "FAILED: $failureMessage (also unable to write automation log)" }
    [Console]::Error.WriteLine($failureMessage)
}
finally {
    if ($unityProcess) {
        try {
            if ($unityStarted -and -not $unityProcess.HasExited) {
                $unityProcess.Kill()
                $unityProcess.WaitForExit()
            }
        }
        catch { [Console]::Error.WriteLine("Unity cleanup: $($_.Exception.Message)") }
        $unityProcess.Dispose()
    }
    if ($ownsMutex) { $runMutex.ReleaseMutex() }
    if ($runMutex) { $runMutex.Dispose() }
}
exit $resultCode
