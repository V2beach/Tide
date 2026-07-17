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
    "Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.EditorDiagnostics.cs",
    "Assets/Scripts/StiltHouse/TideBarrenIslandController.cs",
    "Assets/Scripts/StiltHouse/TideIslandInteractionModel.cs",
    "Assets/Scripts/StiltHouse/TideWreckDismantleModel.cs",
    "Assets/Scripts/StiltHouse/TideRainCisternModel.cs",
    "Assets/Scripts/StiltHouse/TideRepairWorkPhaseModel.cs",
    "Assets/Scripts/StiltHouse/TideRepairWorkController.cs",
    "Assets/Scripts/StiltHouse/TideRepairRecipeModel.cs",
    "Assets/Scripts/StiltHouse/TideSalvageMaterialModel.cs",
    "Assets/Scripts/StiltHouse/TideHeavyWreckTidalLiftModel.cs",
    "Assets/Scripts/StiltHouse/TideHeavyWreckPieceOwnershipModel.cs",
    "Assets/Scripts/StiltHouse/TideHeavyWreckSalvageController.cs",
    "Assets/Scripts/StiltHouse/TideV85HeavyWreckCatalog.cs",
    "Assets/Scripts/StiltHouse/TideMooringRopeModel.cs",
    "Assets/Scripts/StiltHouse/TideMooringRopeController.cs",
    "Assets/Scripts/StiltHouse/TideBoatConditionPerformanceModel.cs",
    "Assets/Scripts/StiltHouse/TideSailboatDynamicsModel.cs",
    "Assets/Scripts/StiltHouse/TideSailingWaveHandlingModel.cs",
    "Assets/Scripts/StiltHouse/TideAuthoritativeOceanModel.cs",
    "Assets/Scripts/StiltHouse/TideSailingReefModel.cs",
    "Assets/Scripts/StiltHouse/TideSailingReefController.cs",
    "Assets/Scripts/StiltHouse/TideContinuousSalvageModel.cs",
    "Assets/Scripts/StiltHouse/TideSailingSalvageController.cs",
    "Assets/Scripts/StiltHouse/TideStormRescueModel.cs",
    "Assets/Scripts/StiltHouse/TideStormRescueController.cs",
    "Assets/Scripts/StiltHouse/TideForecastTideNotchController.cs",
    "Assets/Scripts/StiltHouse/TideForecastSnapshotModel.cs",
    "Assets/Scripts/StiltHouse/TideNetEncounterModel.cs",
    "Assets/Scripts/StiltHouse/TideWrackDepositModel.cs",
    "Assets/Scripts/StiltHouse/TideWrackLineController.cs",
    "Assets/Editor/TideCoreLoopConvergenceProbe.cs",
    "Assets/Editor/TideRepairSceneConvergenceProbe.cs",
    "Assets/Editor/TideVisualSceneConvergenceProbe.cs",
    "Assets/Editor/TideV85HeavyWreckCatalogBuilder.cs",
    "Assets/Resources/StiltFirstSliceAI/V85HeavyWreckCatalog.asset",
    "Docs/ai-work-prompts.md",
    "Docs/tide-task-tracking.md"
)
foreach ($file in $required) {
    Test-Gate (Test-Path -LiteralPath (Join-Path $root $file)) "required: $file"
}

$controller = Read-ProjectText "Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs"
$editorDiagnostics = Read-ProjectText "Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.EditorDiagnostics.cs"
$repairRecipe = Read-ProjectText "Assets/Scripts/StiltHouse/TideRepairRecipeModel.cs"
$repairWork = Read-ProjectText "Assets/Scripts/StiltHouse/TideRepairWorkController.cs"
$barrenIsland = Read-ProjectText "Assets/Scripts/StiltHouse/TideBarrenIslandController.cs"
$waveEvents = Read-ProjectText "Assets/Scripts/StiltHouse/TideWaveEventFieldModel.cs"
$authoritativeOcean = Read-ProjectText "Assets/Scripts/StiltHouse/TideAuthoritativeOceanModel.cs"
$waveHandling = Read-ProjectText "Assets/Scripts/StiltHouse/TideSailingWaveHandlingModel.cs"
$boatPerformance = Read-ProjectText "Assets/Scripts/StiltHouse/TideBoatConditionPerformanceModel.cs"
Test-Gate ($controller.Contains("TickBarrenIslandNaturalState")) "island natural state is integrated"
Test-Gate ($controller.Contains("TickDismantleNearestPart") -and
    $controller.Contains("wreckOcean.Agitation01")) "wreck dismantling consumes continuous input and the authoritative ocean sample"
Test-Gate ($editorDiagnostics.Contains("RunEditorHeavyWreckTidalLiftIntegrationProbe")) "tidal heavy-wreck lift is covered by editor diagnostics"
Test-Gate ($controller.Contains("public partial class TideStiltHouseFirstSliceController")) "runtime controller supports focused partial files"
Test-Gate ($editorDiagnostics.Contains("#if UNITY_EDITOR") -and
    $editorDiagnostics.Contains("public partial class TideStiltHouseFirstSliceController")) "preview and probe code is editor-only"
Test-Gate (-not $controller.Contains("SetEditorNetRigHoldPreviewPose")) "the contiguous editor preview block stays out of the runtime controller"
Test-Gate ($controller.Contains("heavyWreckSalvage.IsCarryingPiece")) "heavy pieces impose a physical drag cost"
Test-Gate ($controller.Contains("GetRepairStagedPartMask")) "heavy pieces only enter compatible final repairs"
Test-Gate ($controller.Contains("TideRepairRecipeModel.GetMaterialNeeds") -and
    $controller.Contains("TideRepairRecipeModel.GetStagingDestination")) "runtime controller consumes the canonical repair recipe model"
Test-Gate ($repairRecipe.Contains("GetArrivalRepairTarget") -and
    $repairRecipe.Contains("GetMaterialNeeds")) "repair targets, first salvage routes, and material needs share one pure model"
Test-Gate ($controller.Contains("TideRepairWorkController repairWork") -and
    $controller.Contains("repairWork.Advance") -and
    $controller.Contains("repairWork.Complete")) "repair session has one runtime state owner"
Test-Gate ($repairWork.Contains("public void Begin") -and
    $repairWork.Contains("public bool Advance") -and
    $repairWork.Contains("public void Complete")) "repair runtime owns begin, pause, progress, and commit transitions"
Test-Gate ($barrenIsland.Contains("CisternWaterSurface") -and
    $barrenIsland.Contains("CisternSaltLine") -and
    $barrenIsland.Contains("CisternLeakStream")) "cistern current water, historical salt, and active leak stay separate"
Test-Gate ($controller.Contains("HandleMooringRopeInput")) "physical mooring input is integrated"
Test-Gate ($controller.Contains("mooringRope.AdvanceEnvironment")) "mooring runtime orchestration is extracted"
Test-Gate ($controller.Contains("TideSailboatDynamicsModel.Advance")) "sailing uses the dynamics model"
Test-Gate ($controller.Contains("GetBoatConditionPerformance") -and
    $controller.Contains("boatPerformance.BailRateMultiplier") -and
    $controller.Contains("boatPerformance.BailingDragMultiplier") -and
    $controller.Contains("boatPerformance.HullSpeedMultiplier")) "runtime controller maps each repaired boat component to its owned handling consequence"
Test-Gate ($boatPerformance.Contains("BaseLeakRatePerSecond") -and
    $boatPerformance.Contains("SailDriveEfficiency01") -and
    $boatPerformance.Contains("SailTrimRatePerSecond") -and
    $boatPerformance.Contains("BallastShiftRatePerSecond")) "boat component performance is isolated as a pure readable model"
Test-Gate (-not $controller.Contains("GetSailingLeakRatePerSecond") -and
    -not $controller.Contains("GetSailingWindAssist") -and
    -not $controller.Contains("GetEffectiveSailingAcceleration")) "runtime keeps no duplicate probe-only sailing formulas"
Test-Gate ($controller.Contains("TideAuthoritativeOceanModel.Sample") -and
    -not $controller.Contains("TideOceanFieldModel.Sample")) "all runtime ocean consumers use the visible-and-physical composition"
Test-Gate ($controller.Contains("GetOceanEventTimeSeconds") -and
    $controller.Contains("worldElapsedRealSeconds") -and
    $controller.Contains("? Mathf.Max(0f, worldElapsedRealSeconds)")) "ocean event time has an explicit real-second runtime authority"
Test-Gate ($authoritativeOcean.Contains("TideOceanFieldModel.Sample") -and
    $authoritativeOcean.Contains("TideWaveEventFieldModel.SamplePhysicalInfluence")) "authoritative ocean combines continuous sea and local visible waves"
Test-Gate ($waveEvents.Contains("TideWaveEventPhysicalSample") -and
    $waveEvents.Contains("SamplePhysicalInfluence")) "visible wave events expose deterministic local physics"
Test-Gate (-not $waveEvents.Contains("clampedAgitation")) "local wave identity does not feed its own composed agitation back into generation"
Test-Gate ($waveEvents.Contains("EvaluateTravelFactor") -and
    $authoritativeOcean.Contains("slackWaterContinuous")) "local wave direction crosses slack water continuously"
Test-Gate ($controller.Contains("GetSailingLocalWaterVelocity(ocean)") -and
    $controller.Contains("GetNaturalCurrentSpeed() + mooredOcean.HorizontalVelocity")) "boat and mooring consume local visible-wave push"
Test-Gate ($controller.Contains("ocean.LocalWaveContact01") -and
    $controller.Contains("TickSailingWaveImpactFeedback") -and
    $controller.Contains("GetSailingBallastVisualOffset")) "sailing runtime turns visible wave contact and ballast into physical feedback"
Test-Gate ($waveHandling.Contains("TideSailingWaveHandlingSample") -and
    $waveHandling.Contains("MomentumDampingPerSecond") -and
    $waveHandling.Contains("IngressMultiplier")) "wave handling is isolated as a pure readable model"
Test-Gate ($controller.Contains("LocalWaveRendererCount = 9") -and
    $controller.Contains("LocalWaveRendererCount,")) "local wave renderer coverage includes wide-screen edge cells"
Test-Gate ($controller.Contains("TideSailboatDynamicsState sailingDynamics") -and
    $controller.Contains("get => sailingDynamics.HorizontalVelocity") -and
    $controller.Contains("get => sailingDynamics.HeaveY") -and
    $controller.Contains("get => sailingDynamics.Ingress01") -and
    $controller.Contains("get => sailingDynamics.SailRaised01")) "sailing dynamics state is the single owner of motion, trim, and ingress"
Test-Gate (-not $controller.Contains("private float sailingBoatVelocity;") -and
    -not $controller.Contains("private float sailingBoatLaneY;") -and
    -not $controller.Contains("private float sailingWaterIngress01;") -and
    -not $controller.Contains("private float sailingSailTrim01;")) "main controller keeps no duplicate sailing dynamics fields"
Test-Gate ($controller.Contains("sailingReef.ResolveMovement")) "sailing reef runtime owns physical collision"
Test-Gate ($controller.Contains("sailingReef.UpdatePresentation")) "sailing reef runtime binds exposure and breaker to the same tide"
Test-Gate ($controller.Contains("sailingSalvage.Advance")) "sailing salvage runtime owns drift, hook and hauling progression"
Test-Gate (-not $controller.Contains("TickFreeSailingSalvage")) "main controller does not retain a second free-drift implementation"
Test-Gate ($controller.Contains("stormRescue.Advance")) "storm rescue runtime owns release, hoist and washout progression"
Test-Gate (-not $controller.Contains("TideStormRescueModel.Advance")) "main controller does not retain a second storm-cargo progression loop"
Test-Gate (-not $controller.Contains("stormRescueItems")) "storm rescue runtime is the only item-state owner"
Test-Gate ($editorDiagnostics.Contains("RunEditorStormManifestOwnershipProbe") -and
    $editorDiagnostics.Contains("RunEditorStormRestIntegrityProbe")) "storm cargo diagnostics stay editor-only"
Test-Gate (-not $controller.Contains("RunEditor") -and
    -not $controller.Contains("GetEditor")) "runtime controller contains no editor probe entry points"
Test-Gate ($controller.Contains("private static Vector2 GetBoatPassengerVisualPivot") -and
    -not $editorDiagnostics.Contains("private static Vector2 GetBoatPassengerVisualPivot")) "runtime boat passenger geometry stays available outside UNITY_EDITOR"
Test-Gate ($controller.Contains("KeyCode.F3")) "debug HUD remains bound to F3"
Test-Gate ($controller.Contains("forecastTideNotches.UpdatePresentation")) "forecast is projected onto physical stilt notches"
Test-Gate ($controller.Contains("TideForecastSnapshotModel.Capture")) "forecast observations are immutable astronomical-cycle snapshots"
Test-Gate ($controller.Contains("TideNetEncounterModel.Advance")) "net catch uses physical batch/mesh encounters"
Test-Gate ($controller.Contains("tideDriftFieldCycleOrdinal")) "drift batches use astronomical cycles instead of story rounds"
Test-Gate ($controller.Contains("wrackLine.TrySettle")) "missed tide batches can leave a physical ebb deposit"
$coreProbe = Read-ProjectText "Assets/Editor/TideCoreLoopConvergenceProbe.cs"
Test-Gate ($coreProbe.Contains("ProbeForecastSnapshot")) "core gate covers forecast snapshot lifetime"
Test-Gate ($coreProbe.Contains("ProbeWreckDismantle")) "core gate covers persistent work, footing, wave load, and part-specific durations"
Test-Gate ($coreProbe.Contains("ProbeRepairWorkSession")) "core gate covers repair pause, retarget, and commit semantics"
Test-Gate ($coreProbe.Contains("ProbeSailingDynamics")) "core gate covers the authoritative sailing dynamics state"
Test-Gate ($coreProbe.Contains("ProbeBoatConditionPerformance")) "core gate proves component-specific boat repair feedback in the real integrator"
Test-Gate ($coreProbe.Contains("ProbeVisibleWavePhysicalCoupling")) "core gate couples visible breakers to local ocean physics"
Test-Gate ($coreProbe.Contains("ProbeSailingWaveHandling")) "core gate compares prepared and exposed wave handling"
Test-Gate ($coreProbe.Contains("ProbeNetEncounter")) "core gate rejects preload, overtopping, stale contact, and skipped windows"
Test-Gate ($coreProbe.Contains("ProbeWrackDeposit")) "core gate covers ebb deposit and refloat lifecycle"
$visualProbe = Read-ProjectText "Assets/Editor/TideVisualSceneConvergenceProbe.cs"
$repairProbe = Read-ProjectText "Assets/Editor/TideRepairSceneConvergenceProbe.cs"
Test-Gate ($repairProbe.Contains("RunEditorBoatComponentHandlingFeedbackProbe")) "repair gate covers hull, sail, and cabin handling ownership"
Test-Gate ($visualProbe.Contains("RunEditorBoatPassengerScaleProbe")) "visual gate covers complete boat passenger"
Test-Gate ($visualProbe.Contains("RunEditorWalkSurfacePathContinuityProbe")) "visual gate covers authored walk surfaces"
Test-Gate ($visualProbe.Contains("RunEditorFirstDayAutonomyProbe")) "visual gate covers first-day autonomy"
Test-Gate ($visualProbe.Contains("RunEditorWreckDismantleTideWindowProbe")) "visual gate covers physical wreck dismantling in a tide window"
Test-Gate ($visualProbe.Contains("RunEditorArrivalSalvagePayoffProbe")) "visual gate covers six immediate arrival-salvage payoffs"
Test-Gate ($visualProbe.Contains("RunEditorMixedSemidiurnalOpportunityProbe")) "visual gate covers unequal adjacent-tide opportunities"
Test-Gate ($visualProbe.Contains("RunEditorLocalWaveEventFieldProbe")) "visual gate couples visible local waves to scene physics"
Test-Gate ($visualProbe.Contains("RunEditorSailingWaveHandlingFeedbackProbe")) "visual gate covers wave-slap and ballast feedback"
Test-Gate ($visualProbe.Contains("TideStormRescueTradeoffConvergenceProbe.Run")) "visual gate covers storm rescue tradeoff"
Test-Gate ($visualProbe.Contains("RunEditorStormManifestOwnershipProbe")) "visual gate covers storm cargo conservation"
Test-Gate ($visualProbe.Contains("RunEditorSailingTideContinuityProbe")) "visual gate covers authoritative sailing tide"
Test-Gate ($visualProbe.Contains("RunEditorFirstSailingTideDecisionProbe")) "visual gate covers the first sailing tide decision"
Test-Gate ($visualProbe.Contains("RunEditorTideForecastAutonomyProbe")) "visual gate covers tide-forecast autonomy and physical geometry"
Test-Gate ($visualProbe.Contains("RunEditorLiveNetControlAndLoadPhysicsProbe")) "visual gate covers runtime net encounter integration"
Test-Gate ($visualProbe.Contains("RunEditorNetExcursionWindowProbe")) "visual gate covers leaving the net during a natural tide"
Test-Gate ($visualProbe.Contains("RunEditorFirstTideRouteChoiceProbe")) "visual gate covers first-tide route ownership and risk"
Test-Gate ($visualProbe.Contains("RunEditorWrackLineLifecycleProbe")) "visual gate covers the physical wrack-line lifecycle"

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
