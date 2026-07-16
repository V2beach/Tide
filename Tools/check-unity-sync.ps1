param(
    [switch]$Strict,
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
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
        if ($VerboseOutput) {
            Write-Host "[OK] $Message"
        }
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

Set-Location $projectRoot

Add-Result (Test-Path ".git") "Git repository exists at project root"
Add-Result (Test-Path ".gitignore") ".gitignore exists"
Add-Result (Test-Path ".gitattributes") ".gitattributes exists"
Add-Result (Test-Path "Assets") "Assets folder exists"
Add-Result (Test-Path "Packages") "Packages folder exists"
Add-Result (Test-Path "ProjectSettings") "ProjectSettings folder exists"

$trackedGenerated = git ls-files Library Temp Obj Logs UserSettings Build Builds 2>$null
Add-Result (-not $trackedGenerated) "Unity generated folders are not tracked"
if ($trackedGenerated) {
    $trackedGenerated | ForEach-Object { Write-Host "       tracked generated path: $_" }
}

$assetFiles = Get-ChildItem -Path "Assets" -Recurse -File -Force |
    Where-Object { $_.Name -notlike "*.meta" -and $_.Name -notin @(".DS_Store", "Thumbs.db", "Desktop.ini") }

foreach ($file in $assetFiles) {
    $metaPath = "$($file.FullName).meta"
    Add-Result (Test-Path $metaPath) "Meta exists for $($file.FullName.Substring($projectRoot.Path.Length + 1))"
}

$metaFiles = Get-ChildItem -Path "Assets" -Recurse -File -Filter "*.meta" -Force
foreach ($meta in $metaFiles) {
    $assetPath = $meta.FullName.Substring(0, $meta.FullName.Length - 5)
    Add-Result ((Test-Path $assetPath -PathType Leaf) -or (Test-Path $assetPath -PathType Container)) "Meta has matching asset/folder $($meta.FullName.Substring($projectRoot.Path.Length + 1))"
}

$binaryExtensions = @(
    ".png", ".jpg", ".jpeg", ".gif", ".psd", ".psb", ".ase", ".aseprite",
    ".tga", ".tif", ".tiff", ".bmp", ".exr", ".wav", ".mp3", ".ogg",
    ".flac", ".mp4", ".mov", ".fbx", ".blend", ".ttf", ".otf"
)

$binaryPaths = @(Get-ChildItem -Path "Assets" -Recurse -File -Force |
    Where-Object { $binaryExtensions -contains $_.Extension.ToLowerInvariant() } |
    ForEach-Object { $_.FullName.Substring($projectRoot.Path.Length + 1).Replace("\", "/") })

# Spawning one Git process per image made this check take roughly two minutes. Check
# attributes in bounded batches so Windows and macOS validate the same paths quickly.
for ($start = 0; $start -lt $binaryPaths.Count; $start += 160) {
    $end = [Math]::Min($start + 159, $binaryPaths.Count - 1)
    $batch = @($binaryPaths[$start..$end])
    $attributes = @(& git check-attr filter -- @batch)
    $failed = @($attributes | Where-Object { $_ -notmatch "filter: lfs$" })
    Add-Result ($failed.Count -eq 0) "Git LFS filter applies to binary batch $start..$end"
    $failed | ForEach-Object { Write-Host "       $_" }
}

$status = git status --short
if ($status) {
    Add-Result $false "Working tree has pending changes; review before switching machines" $true
    $status | ForEach-Object { Write-Host "       $_" }
} else {
    $okCount += 1
    if ($VerboseOutput) {
        Write-Host "[OK] Working tree is clean"
    }
}

Write-Host "Checks passed: $okCount"

if ($warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "Warnings: $($warnings.Count)"
}

if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Host "Errors: $($errors.Count)"
    if ($Strict) {
        exit 1
    }

    exit 2
}

Write-Host ""
Write-Host "Unity sync check completed."
exit 0
