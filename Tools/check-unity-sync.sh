#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

errors=0
warnings=0
ok_count=0
verbose="${VERBOSE:-0}"

ok() {
  ok_count=$((ok_count + 1))
  if [[ "$verbose" == "1" ]]; then
    printf '[OK] %s\n' "$1"
  fi
}

warn() {
  warnings=$((warnings + 1))
  printf '[WARN] %s\n' "$1"
}

error() {
  errors=$((errors + 1))
  printf '[ERROR] %s\n' "$1"
}

check_path() {
  if [[ -e "$1" ]]; then
    ok "$1 exists"
  else
    error "$1 is missing"
  fi
}

check_path ".git"
check_path ".gitignore"
check_path ".gitattributes"
check_path "Assets"
check_path "Packages"
check_path "ProjectSettings"

tracked_generated="$(git ls-files Library Temp Obj Logs UserSettings Build Builds || true)"
if [[ -z "$tracked_generated" ]]; then
  ok "Unity generated folders are not tracked"
else
  error "Unity generated folders are tracked"
  printf '%s\n' "$tracked_generated" | sed 's/^/       tracked generated path: /'
fi

while IFS= read -r -d '' asset; do
  case "$asset" in
    *.meta|*.DS_Store|*Thumbs.db|*Desktop.ini) continue ;;
  esac

  if [[ -f "$asset.meta" ]]; then
    ok "Meta exists for $asset"
  else
    error "Missing meta for $asset"
  fi
done < <(find Assets -type f -print0)

while IFS= read -r -d '' meta; do
  asset="${meta%.meta}"
  if [[ -e "$asset" ]]; then
    ok "Meta has matching asset/folder $meta"
  else
    error "Orphan meta file $meta"
  fi
done < <(find Assets -name '*.meta' -type f -print0)

mapfile -d '' binaries < <(find Assets -type f \( \
  -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.gif' \
  -o -iname '*.psd' -o -iname '*.psb' -o -iname '*.ase' -o -iname '*.aseprite' \
  -o -iname '*.tga' -o -iname '*.tif' -o -iname '*.tiff' -o -iname '*.bmp' \
  -o -iname '*.exr' -o -iname '*.wav' -o -iname '*.mp3' -o -iname '*.ogg' \
  -o -iname '*.flac' -o -iname '*.mp4' -o -iname '*.mov' -o -iname '*.fbx' \
  -o -iname '*.blend' -o -iname '*.ttf' -o -iname '*.otf' \
\) -print0)

for ((start = 0; start < ${#binaries[@]}; start += 160)); do
  batch=("${binaries[@]:start:160}")
  batch_failed=0
  while IFS= read -r attr; do
    if [[ "$attr" != *"filter: lfs" ]]; then
      error "Git LFS filter missing: $attr"
      batch_failed=1
    fi
  done < <(git check-attr filter -- "${batch[@]}")
  if [[ "$batch_failed" == "0" ]]; then
    ok "Git LFS filter applies to binary batch $start"
  fi
done

status="$(git status --short)"
if [[ -n "$status" ]]; then
  warn "Working tree has pending changes; review before switching machines"
  printf '%s\n' "$status" | sed 's/^/       /'
else
  ok "Working tree is clean"
fi

printf 'Checks passed: %s\n' "$ok_count"

if [[ "$warnings" -gt 0 ]]; then
  printf '\nWarnings: %s\n' "$warnings"
fi

if [[ "$errors" -gt 0 ]]; then
  printf '\nErrors: %s\n' "$errors"
  exit 2
fi

printf '\nUnity sync check completed.\n'
