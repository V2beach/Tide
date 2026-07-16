#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity_path="${UNITY_PATH:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity}"

"$root/Tools/check-prototype-loop.sh"
"$root/Tools/check-unity-sync.sh"

if [[ "${SKIP_UNITY:-0}" == "1" ]]; then
  printf 'Tide readiness passed without Unity probe (SKIP_UNITY=1).\n'
  exit 0
fi
if [[ ! -x "$unity_path" ]]; then
  printf 'Unity executable not found: %s\n' "$unity_path" >&2
  exit 1
fi

log_path="$root/Logs/readiness-convergence-probes.log"
"$unity_path" -batchmode -nographics -quit \
  -projectPath "$root" \
  -executeMethod TideCoreLoopConvergenceProbe.RunFromCommandLine \
  -logFile "$log_path"

if grep -Eq 'error CS[0-9]+|TIDE_CORE_LOOP_PROBE FAIL|TIDE_REPAIR_SCENE_PROBE FAIL|TIDE_VISUAL_SCENE_PROBE FAIL|executeMethod method .* threw exception' "$log_path" ||
   ! grep -q 'TIDE_CORE_LOOP_PROBE PASS' "$log_path" ||
   ! grep -q 'TIDE_REPAIR_SCENE_PROBE PASS' "$log_path" ||
   ! grep -q 'TIDE_VISUAL_SCENE_PROBE PASS' "$log_path"; then
  grep -E 'error CS|TIDE_CORE_LOOP_PROBE|TIDE_REPAIR_SCENE_PROBE|TIDE_VISUAL_SCENE_PROBE|executeMethod method' "$log_path" | tail -n 12 >&2 || true
  exit 1
fi
grep 'TIDE_CORE_LOOP_PROBE PASS' "$log_path" | tail -n 1
grep 'TIDE_REPAIR_SCENE_PROBE PASS' "$log_path" | tail -n 1
grep 'TIDE_VISUAL_SCENE_PROBE PASS' "$log_path" | tail -n 1
printf "Tide play readiness passed. Visual acceptance still requires the user's original Game View/video.\n"
