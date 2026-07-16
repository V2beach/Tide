param([switch]$VerboseOutput)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$failures = [System.Collections.Generic.List[string]]::new()
$passes = 0

function Test-Gate([bool]$condition, [string]$message) {
    if ($condition) {
        $script:passes++
        if ($VerboseOutput) { Write-Host "[OK] $message" }
    } else {
        $script:failures.Add($message)
        Write-Host "[FAIL] $message"
    }
}

function Read-ProjectText([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path)) { return "" }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8
}

$required = @(
    "Assets/Scenes/Tide_StiltHouse_FirstSlice.unity",
    "Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs",
    "Assets/Scripts/StiltHouse/TideBarrenIslandController.cs",
    "Assets/Scripts/StiltHouse/TideRainCisternModel.cs",
    "Assets/Scripts/StiltHouse/TideMooringRopeModel.cs",
    "Assets/Scripts/StiltHouse/TideSailboatDynamicsModel.cs",
    "Assets/Scripts/StiltHouse/TideStormRescueModel.cs",
    "Assets/Editor/TideCoreLoopConvergenceProbe.cs",
    "Docs/ai-work-prompts.md",
    "Docs/tide-task-tracking.md"
)
foreach ($file in $required) {
    Test-Gate (Test-Path -LiteralPath (Join-Path $root $file)) "required: $file"
}

$controller = Read-ProjectText "Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs"
Test-Gate ($controller.Contains("TickBarrenIslandNaturalState")) "island natural state is integrated"
Test-Gate ($controller.Contains("HandleMooringRopeInput")) "physical mooring input is integrated"
Test-Gate ($controller.Contains("TideSailboatDynamicsModel.Advance")) "sailing uses the dynamics model"
Test-Gate ($controller.Contains("TickStormRescue")) "storm rescue advances in the world tick"
Test-Gate ($controller.Contains("KeyCode.F3")) "debug HUD remains bound to F3"

$buildSettings = Read-ProjectText "ProjectSettings/EditorBuildSettings.asset"
Test-Gate ($buildSettings.Contains("Assets/Scenes/Tide_StiltHouse_FirstSlice.unity")) "build settings contain the canonical scene"
Test-Gate (-not $buildSettings.Contains("SampleScene.unity")) "retired SampleScene is absent from build settings"

$attributes = Read-ProjectText ".gitattributes"
Test-Gate ($attributes.Contains("filter=lfs")) "binary art is routed through Git LFS"
$prompts = Read-ProjectText "Docs/ai-work-prompts.md"
Test-Gate ($prompts.Contains("日常开发收敛 Prompt") -and
    $prompts.Contains("Playtest 优先 Prompt") -and
    $prompts.Contains("架构收敛 Prompt")) "three convergence prompts are present"

$pythonFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.py -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch "[\\/](Library|Temp|Logs)[\\/]" })
Test-Gate ($pythonFiles.Count -eq 0) "one-off Python generators are absent"
Test-Gate (-not (Test-Path -LiteralPath (Join-Path $root "Assets/Screenshots"))) "automatic screenshot output is absent"
Test-Gate (-not (Test-Path -LiteralPath (Join-Path $root "Assets/Scenes/Prototype_01.unity"))) "retired prototype scene is absent"

$generatedRoot = Join-Path $root "Assets/Art/GeneratedAI"
$generatedBytes = if (Test-Path $generatedRoot) {
    (Get-ChildItem -LiteralPath $generatedRoot -Recurse -File | Measure-Object Length -Sum).Sum
} else { 0 }
Test-Gate ($generatedBytes -lt 500MB) "generated runtime art remains below 500 MiB"

if ($failures.Count -gt 0) {
    Write-Host "Tide core static gate failed: $($failures.Count) failure(s), $passes pass(es)."
    exit 1
}

Write-Host "Tide core static gate passed: $passes checks; GeneratedAI=$([math]::Round($generatedBytes / 1MB, 1)) MiB."
