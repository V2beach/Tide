param(
    [string]$UnityPath = "",
    [switch]$Launch,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = $projectRoot.Path
$projectVersionPath = Join-Path $projectPath "ProjectSettings\ProjectVersion.txt"
$expectedVersion = "2022.3.62f3"
$defaultUnityPath = "D:\UnityEditor\$expectedVersion\Editor\Unity.exe"
$unityExe = if ($UnityPath) { $UnityPath } else { $defaultUnityPath }
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$okCount = 0

function Add-Result {
    param(
        [bool]$Condition,
        [string]$Message,
        [bool]$WarningOnly = $false
    )

    if ($Condition) {
        $script:okCount += 1
        Write-Host "[OK] $Message"
        return
    }

    if ($WarningOnly) {
        $warnings.Add($Message)
        Write-Host "[WARN] $Message"
        return
    }

    $errors.Add($Message)
    Write-Host "[ERROR] $Message"
}

function Shorten-Text {
    param([string]$Text, [int]$Max = 180)

    if ([string]::IsNullOrWhiteSpace($Text) -or $Text.Length -le $Max) {
        return $Text
    }

    return $Text.Substring(0, $Max) + "..."
}

Set-Location $projectPath

Add-Result (Test-Path $projectVersionPath) "ProjectVersion.txt exists"
if (Test-Path $projectVersionPath) {
    $versionText = Get-Content -LiteralPath $projectVersionPath -Raw
    Add-Result ($versionText -match [regex]::Escape($expectedVersion)) "Project uses Unity $expectedVersion"
}

Add-Result (Test-Path $unityExe) "Unity executable found: $unityExe" $true

$lockfilePath = Join-Path $projectPath "Temp\UnityLockfile"
Add-Result (-not (Test-Path $lockfilePath)) "Tide project has no Unity lockfile" $true

$unityProcesses = @(Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" -ErrorAction SilentlyContinue)
$normalizedProjectPath = $projectPath.TrimEnd("\")
$tideProcesses = @(
    $unityProcesses | Where-Object {
        $_.CommandLine -and $_.CommandLine.Replace("/", "\").Contains($normalizedProjectPath)
    }
)

if ($unityProcesses.Count -eq 0) {
    Add-Result $false "No Unity Editor process is running" $true
} else {
    Add-Result $true "Unity Editor process count: $($unityProcesses.Count)"
    foreach ($process in $unityProcesses) {
        Write-Host "       PID $($process.ProcessId): $(Shorten-Text $process.CommandLine)"
    }
}

if ($tideProcesses.Count -gt 0) {
    Add-Result $true "At least one Unity Editor is opened on Tide"
    foreach ($process in $tideProcesses) {
        Write-Host "       Tide PID $($process.ProcessId)"
    }
} else {
    Add-Result $false "No Unity Editor process is opened on Tide" $true
}

$editorLogPath = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
if (Test-Path $editorLogPath) {
    $editorLog = Get-Item -LiteralPath $editorLogPath
    Add-Result $true "Editor.log last write: $($editorLog.LastWriteTime)"
    $lastErrors = @(Select-String -LiteralPath $editorLogPath -Pattern "error CS[0-9]+" -CaseSensitive | Select-Object -Last 5)
    if ($lastErrors.Count -gt 0) {
        Add-Result $false "Editor.log still contains C# errors; check whether they are stale or from another project" $true
        foreach ($entry in $lastErrors) {
            Write-Host "       $($entry.Line)"
        }
    } else {
        Add-Result $true "Editor.log has no C# errors"
    }
} else {
    Add-Result $false "Editor.log not found" $true
}

$beeLogPath = Join-Path $projectPath "Library\Bee\tundra.log.json"
if (Test-Path $beeLogPath) {
    $beeLog = Get-Item -LiteralPath $beeLogPath
    Add-Result $true "Tide Bee log last write: $($beeLog.LastWriteTime)"
    $beeErrors = @(Select-String -LiteralPath $beeLogPath -Pattern "error CS[0-9]+" | Select-Object -Last 5)
    if ($beeErrors.Count -gt 0) {
        Add-Result $false "Tide Bee log contains prior C# errors; run Unity recompile to refresh this log" $true
        foreach ($entry in $beeErrors) {
            Write-Host "       $($entry.Line)"
        }
    } else {
        Add-Result $true "Tide Bee log has no C# errors"
    }
} else {
    Add-Result $false "Tide Bee log not found; Unity has not generated Library/Bee yet" $true
}

if ($Launch) {
    if (-not (Test-Path $unityExe)) {
        Add-Result $false "Cannot launch Unity because executable was not found"
    } elseif ($tideProcesses.Count -gt 0) {
        Add-Result $true "Tide Unity Editor is already running; launch skipped"
    } elseif (Test-Path $lockfilePath) {
        Add-Result $false "Cannot launch because Tide lockfile exists: $lockfilePath"
    } else {
        Write-Host "[ACTION] Launching Tide in Unity $expectedVersion..."
        Start-Process -FilePath $unityExe -ArgumentList @("-projectPath", $projectPath)
    }
}

Write-Host ""
Write-Host "Checks passed: $okCount"

if ($warnings.Count -gt 0) {
    Write-Host "Warnings: $($warnings.Count)"
}

if ($errors.Count -gt 0) {
    Write-Host "Errors: $($errors.Count)"
    if ($Strict) {
        exit 1
    }

    exit 2
}

Write-Host "Tide Unity Editor check completed."
exit 0
