#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
failures=0
passes=0

gate() {
  if "$@"; then
    passes=$((passes + 1))
  else
    failures=$((failures + 1))
    printf '[FAIL] %s\n' "$*"
  fi
}

required=(
  Assets/Scenes/Tide_StiltHouse_FirstSlice.unity
  Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs
  Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.EditorDiagnostics.cs
  Assets/Scripts/StiltHouse/TideBarrenIslandController.cs
  Assets/Scripts/StiltHouse/TideIslandInteractionModel.cs
  Assets/Scripts/StiltHouse/TideRainCisternModel.cs
  Assets/Scripts/StiltHouse/TideRepairWorkPhaseModel.cs
  Assets/Scripts/StiltHouse/TideSalvageMaterialModel.cs
  Assets/Scripts/StiltHouse/TideHeavyWreckTidalLiftModel.cs
  Assets/Scripts/StiltHouse/TideHeavyWreckPieceOwnershipModel.cs
  Assets/Scripts/StiltHouse/TideHeavyWreckSalvageController.cs
  Assets/Scripts/StiltHouse/TideV85HeavyWreckCatalog.cs
  Assets/Scripts/StiltHouse/TideMooringRopeModel.cs
  Assets/Scripts/StiltHouse/TideMooringRopeController.cs
  Assets/Scripts/StiltHouse/TideSailboatDynamicsModel.cs
  Assets/Scripts/StiltHouse/TideSailingReefModel.cs
  Assets/Scripts/StiltHouse/TideSailingReefController.cs
  Assets/Scripts/StiltHouse/TideContinuousSalvageModel.cs
  Assets/Scripts/StiltHouse/TideSailingSalvageController.cs
  Assets/Scripts/StiltHouse/TideStormRescueModel.cs
  Assets/Scripts/StiltHouse/TideStormRescueController.cs
  Assets/Scripts/StiltHouse/TideForecastTideNotchController.cs
  Assets/Scripts/StiltHouse/TideForecastSnapshotModel.cs
  Assets/Scripts/StiltHouse/TideNetEncounterModel.cs
  Assets/Scripts/StiltHouse/TideWrackDepositModel.cs
  Assets/Scripts/StiltHouse/TideWrackLineController.cs
  Assets/Editor/TideCoreLoopConvergenceProbe.cs
  Assets/Editor/TideRepairSceneConvergenceProbe.cs
  Assets/Editor/TideVisualSceneConvergenceProbe.cs
  Assets/Editor/TideV85HeavyWreckCatalogBuilder.cs
  Assets/Resources/StiltFirstSliceAI/V85HeavyWreckCatalog.asset
  Docs/ai-work-prompts.md
  Docs/tide-task-tracking.md
)
for file in "${required[@]}"; do gate test -f "$root/$file"; done

controller="$root/Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs"
editor_diagnostics="$root/Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.EditorDiagnostics.cs"
for token in TickBarrenIslandNaturalState heavyWreckSalvage.IsCarryingPiece GetRepairStagedPartMask HandleMooringRopeInput mooringRope.AdvanceEnvironment TideSailboatDynamicsModel.Advance sailingReef.ResolveMovement sailingReef.UpdatePresentation sailingSalvage.Advance stormRescue.Advance RunEditorStormManifestOwnershipProbe KeyCode.F3 forecastTideNotches.UpdatePresentation TideForecastSnapshotModel.Capture TideNetEncounterModel.Advance tideDriftFieldCycleOrdinal wrackLine.TrySettle; do
  gate grep -q "$token" "$controller"
done
gate grep -q RunEditorHeavyWreckTidalLiftIntegrationProbe "$editor_diagnostics"
gate grep -q 'public partial class TideStiltHouseFirstSliceController' "$controller"
gate grep -q '#if UNITY_EDITOR' "$editor_diagnostics"
gate grep -q 'public partial class TideStiltHouseFirstSliceController' "$editor_diagnostics"
if grep -q SetEditorNetRigHoldPreviewPose "$controller"; then failures=$((failures + 1)); else passes=$((passes + 1)); fi
gate grep -q ProbeForecastSnapshot "$root/Assets/Editor/TideCoreLoopConvergenceProbe.cs"
gate grep -q ProbeNetEncounter "$root/Assets/Editor/TideCoreLoopConvergenceProbe.cs"
gate grep -q ProbeWrackDeposit "$root/Assets/Editor/TideCoreLoopConvergenceProbe.cs"
if grep -q TickFreeSailingSalvage "$controller"; then failures=$((failures + 1)); else passes=$((passes + 1)); fi
if grep -q TideStormRescueModel.Advance "$controller"; then failures=$((failures + 1)); else passes=$((passes + 1)); fi
if grep -q stormRescueItems "$controller"; then failures=$((failures + 1)); else passes=$((passes + 1)); fi
gate grep -q RunEditorBoatPassengerScaleProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorWalkSurfacePathContinuityProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorFirstDayAutonomyProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorMixedSemidiurnalOpportunityProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q TideStormRescueTradeoffConvergenceProbe.Run "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorStormManifestOwnershipProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorSailingTideContinuityProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorFirstSailingTideDecisionProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorTideForecastAutonomyProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorLiveNetControlAndLoadPhysicsProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorNetExcursionWindowProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorFirstTideRouteChoiceProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q RunEditorWrackLineLifecycleProbe "$root/Assets/Editor/TideVisualSceneConvergenceProbe.cs"
gate grep -q 'Assets/Scenes/Tide_StiltHouse_FirstSlice.unity' "$root/ProjectSettings/EditorBuildSettings.asset"
if grep -q 'SampleScene.unity' "$root/ProjectSettings/EditorBuildSettings.asset"; then failures=$((failures + 1)); else passes=$((passes + 1)); fi
gate grep -q 'filter=lfs' "$root/.gitattributes"
for token in '日常开发收敛 Prompt' 'Playtest 优先 Prompt' '架构收敛 Prompt'; do gate grep -q "$token" "$root/Docs/ai-work-prompts.md"; done

if find "$root" -type f -name '*.py' -not -path '*/Library/*' -not -path '*/Temp/*' -not -path '*/Logs/*' | grep -q .; then
  failures=$((failures + 1))
else
  passes=$((passes + 1))
fi
[[ ! -d "$root/Assets/Screenshots" ]] && passes=$((passes + 1)) || failures=$((failures + 1))
[[ ! -f "$root/Assets/Scenes/Prototype_01.unity" ]] && passes=$((passes + 1)) || failures=$((failures + 1))

if (( failures > 0 )); then
  printf 'Tide core static gate failed: %d failure(s), %d pass(es).\n' "$failures" "$passes"
  exit 1
fi
printf 'Tide core static gate passed: %d checks.\n' "$passes"
