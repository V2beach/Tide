param(
    [string]$UnityPath = "D:\UnityEditor\2022.3.62f3\Editor\Unity.exe",
    [switch]$SkipUnity
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

& pwsh -NoProfile -File (Join-Path $PSScriptRoot "check-prototype-loop.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& pwsh -NoProfile -File (Join-Path $PSScriptRoot "check-unity-sync.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($SkipUnity) {
    Write-Host "Tide readiness passed without Unity probe (-SkipUnity)."
    exit 0
}

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity executable not found: $UnityPath"
}

$logPath = Join-Path $root "Logs\readiness-convergence-probes.log"
$arguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $root,
    "-executeMethod", "TideCoreLoopConvergenceProbe.RunFromCommandLine",
    "-logFile", $logPath
)
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
$log = Get-Content -LiteralPath $logPath -Raw -Encoding UTF8
if ($process.ExitCode -ne 0 -or
    $log -match "error CS\d+" -or
    $log -match "TIDE_CORE_LOOP_PROBE FAIL" -or
    $log -match "TIDE_REPAIR_SCENE_PROBE FAIL" -or
    $log -notmatch "TIDE_CORE_LOOP_PROBE PASS" -or
    $log -notmatch "TIDE_REPAIR_SCENE_PROBE PASS") {
    $evidence = $log -split "`r?`n" |
        Select-String -Pattern "error CS|TIDE_CORE_LOOP_PROBE|TIDE_REPAIR_SCENE_PROBE|executeMethod method" |
        Select-Object -Last 12
    $evidence | ForEach-Object { Write-Host $_.Line }
    throw "Unity core-loop probe failed; see $logPath"
}

($log -split "`r?`n" | Select-String -Pattern "TIDE_CORE_LOOP_PROBE PASS" | Select-Object -Last 1).Line |
    Write-Host
($log -split "`r?`n" | Select-String -Pattern "TIDE_REPAIR_SCENE_PROBE PASS" | Select-Object -Last 1).Line |
    Write-Host
Write-Host "Tide play readiness passed. Visual acceptance still requires the user's original Game View/video."
