#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

EXPECTED_VERSION="2022.3.62f3"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$EXPECTED_VERSION/Unity.app/Contents/MacOS/Unity}"
LAUNCH="${LAUNCH:-0}"
STRICT="${STRICT:-0}"
errors=0
warnings=0
ok_count=0

ok() {
  ok_count=$((ok_count + 1))
  printf '[OK] %s\n' "$1"
}

warn() {
  warnings=$((warnings + 1))
  printf '[WARN] %s\n' "$1"
}

error() {
  errors=$((errors + 1))
  printf '[ERROR] %s\n' "$1"
}

if [[ -f ProjectSettings/ProjectVersion.txt ]]; then
  ok "ProjectVersion.txt exists"
  if grep -q "$EXPECTED_VERSION" ProjectSettings/ProjectVersion.txt; then
    ok "Project uses Unity $EXPECTED_VERSION"
  else
    error "ProjectVersion.txt does not mention Unity $EXPECTED_VERSION"
  fi
else
  error "ProjectSettings/ProjectVersion.txt is missing"
fi

if [[ -x "$UNITY_PATH" ]]; then
  ok "Unity executable found: $UNITY_PATH"
else
  warn "Unity executable not found at $UNITY_PATH"
fi

if [[ -e Temp/UnityLockfile ]]; then
  warn "Tide project has a Unity lockfile: Temp/UnityLockfile"
else
  ok "Tide project has no Unity lockfile"
fi

unity_processes="$(pgrep -fl "Unity" || true)"
if [[ -z "$unity_processes" ]]; then
  warn "No Unity Editor process is running"
else
  ok "Unity process list is available"
  printf '%s\n' "$unity_processes" | sed 's/^/       /'
fi

tide_processes="$(printf '%s\n' "$unity_processes" | grep -F "$ROOT" || true)"
if [[ -n "$tide_processes" ]]; then
  ok "At least one Unity process appears to be opened on Tide"
else
  warn "No running Unity process command line contains the Tide project path"
fi

if [[ "$OSTYPE" == darwin* ]]; then
  editor_log="$HOME/Library/Logs/Unity/Editor.log"
else
  editor_log="${LOCALAPPDATA:-}/Unity/Editor/Editor.log"
fi

if [[ -n "$editor_log" && -f "$editor_log" ]]; then
  ok "Editor.log exists: $editor_log"
  printf '       last write: %s\n' "$(date -r "$editor_log" '+%Y-%m-%d %H:%M:%S' 2>/dev/null || stat -c '%y' "$editor_log")"
  if grep -q "error CS[0-9]" "$editor_log"; then
    warn "Editor.log contains C# errors; check whether they are stale or from another project"
    grep "error CS[0-9]" "$editor_log" | tail -5 | sed 's/^/       /'
  else
    ok "Editor.log has no C# errors"
  fi
else
  warn "Editor.log not found"
fi

if [[ -f Library/Bee/tundra.log.json ]]; then
  ok "Tide Bee log exists"
  printf '       last write: %s\n' "$(date -r Library/Bee/tundra.log.json '+%Y-%m-%d %H:%M:%S' 2>/dev/null || stat -c '%y' Library/Bee/tundra.log.json)"
  if grep -q "error CS[0-9]" Library/Bee/tundra.log.json; then
    warn "Tide Bee log contains prior C# errors; run Unity recompile to refresh this log"
    grep "error CS[0-9]" Library/Bee/tundra.log.json | tail -5 | sed 's/^/       /'
  else
    ok "Tide Bee log has no C# errors"
  fi
else
  warn "Tide Bee log not found; Unity has not generated Library/Bee yet"
fi

if [[ "$LAUNCH" == "1" ]]; then
  if [[ ! -x "$UNITY_PATH" ]]; then
    error "Cannot launch Unity because executable was not found"
  elif [[ -n "$tide_processes" ]]; then
    ok "Tide Unity Editor is already running; launch skipped"
  elif [[ -e Temp/UnityLockfile ]]; then
    error "Cannot launch because Tide lockfile exists: Temp/UnityLockfile"
  else
    printf '[ACTION] Launching Tide in Unity %s...\n' "$EXPECTED_VERSION"
    "$UNITY_PATH" -projectPath "$ROOT" >/dev/null 2>&1 &
  fi
fi

printf '\nChecks passed: %s\n' "$ok_count"

if [[ "$warnings" -gt 0 ]]; then
  printf 'Warnings: %s\n' "$warnings"
fi

if [[ "$errors" -gt 0 ]]; then
  printf 'Errors: %s\n' "$errors"
  if [[ "$STRICT" == "1" ]]; then
    exit 1
  fi
  exit 2
fi

printf 'Tide Unity Editor check completed.\n'
