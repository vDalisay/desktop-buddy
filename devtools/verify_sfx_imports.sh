#!/usr/bin/env bash
set -euo pipefail

root="assets/sfx"
if [[ ! -d "$root" ]]; then
  echo "SFX root '$root' is missing." >&2
  exit 1
fi

missing=0
count=0
while IFS= read -r -d '' audio; do
  count=$((count + 1))
  sidecar="${audio}.import"
  if [[ ! -f "$sidecar" ]]; then
    echo "Missing Godot import sidecar: $sidecar" >&2
    missing=1
  fi
done < <(find "$root" -type f \( -iname '*.mp3' -o -iname '*.wav' \) -print0)

orphaned=0
while IFS= read -r -d '' sidecar; do
  source="${sidecar%.import}"
  case "${source,,}" in
    *.mp3|*.wav)
      if [[ ! -f "$source" ]]; then
        echo "Orphaned Godot audio import sidecar: $sidecar" >&2
        orphaned=1
      fi
      ;;
  esac
done < <(find "$root" -type f -name '*.import' -print0)

if [[ $count -eq 0 ]]; then
  echo "No authored SFX files were found under '$root'." >&2
  exit 1
fi

if [[ $missing -ne 0 || $orphaned -ne 0 ]]; then
  exit 1
fi

echo "SFX import sidecars complete for $count authored audio files."
