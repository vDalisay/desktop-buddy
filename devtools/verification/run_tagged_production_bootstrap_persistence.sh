#!/usr/bin/env bash
set -euo pipefail

: "${GODOT_BIN:?GODOT_BIN must point at the pinned Godot Mono editor}"
: "${GODOT_VERSION:=4.6.1-stable}"
GODOT_TEMPLATE_VERSION="${GODOT_TEMPLATE_VERSION:-4.6.1.stable.mono}"
ROOT="$(pwd)"
ARTIFACT_ROOT="$ROOT/.artifacts/production-bootstrap"

rm -rf "$ARTIFACT_ROOT"
mkdir -p "$ARTIFACT_ROOT/initial" "$ARTIFACT_ROOT/next-fest"

# The verification exports use the same native Steam addon expected by the real
# Steam presets. This is a disposable CI materialization; proprietary binaries
# remain untracked and the repository binary guard remains authoritative.
pwsh -File ./tools/install_godotsteam.ps1 -Force

TEMPLATE_NAME="Godot_v${GODOT_VERSION}_mono_export_templates.tpz"
TEMPLATE_URL="https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}/${TEMPLATE_NAME}"
TEMPLATE_ZIP="${RUNNER_TEMP:-/tmp}/$TEMPLATE_NAME"
TEMPLATE_UNPACK="${RUNNER_TEMP:-/tmp}/desktop-buddy-godot-templates"
TEMPLATE_DIR="$HOME/.local/share/godot/export_templates/$GODOT_TEMPLATE_VERSION"

curl -fL --retry 3 -o "$TEMPLATE_ZIP" "$TEMPLATE_URL"
rm -rf "$TEMPLATE_UNPACK" "$TEMPLATE_DIR"
mkdir -p "$TEMPLATE_UNPACK" "$TEMPLATE_DIR"
unzip -q "$TEMPLATE_ZIP" -d "$TEMPLATE_UNPACK"
cp -a "$TEMPLATE_UNPACK/templates/." "$TEMPLATE_DIR/"
test -f "$TEMPLATE_DIR/linux_debug.x86_64"

# Import first so the C# project and imported resources are ready before export. The two Linux
# verification presets are checked-in repository configuration; CI must never patch export presets
# at runtime because Godot validates/rematerializes that file during editor startup.
xvfb-run -a "$GODOT_BIN" --headless --path . --import

grep -Fq 'name="CI Linux Initial Demo Verification"' export_presets.cfg
grep -Fq 'name="CI Linux Next Fest Verification"' export_presets.cfg
grep -Fq 'platform="Linux"' export_presets.cfg

export DesktopBuddySteamDemoScope=true
export DesktopBuddyNextFestDemoScope=false
xvfb-run -a "$GODOT_BIN" --headless --path . \
  --export-debug "CI Linux Initial Demo Verification" \
  "$ARTIFACT_ROOT/initial/DesktopBuddy.x86_64"
chmod +x "$ARTIFACT_ROOT/initial/DesktopBuddy.x86_64"
"$ARTIFACT_ROOT/initial/DesktopBuddy.x86_64" --headless -- \
  --journey=production_bootstrap_persistence \
  --seed=1 \
  --artifacts="$ARTIFACT_ROOT/initial/journey"

export DesktopBuddySteamDemoScope=true
export DesktopBuddyNextFestDemoScope=true
xvfb-run -a "$GODOT_BIN" --headless --path . \
  --export-debug "CI Linux Next Fest Verification" \
  "$ARTIFACT_ROOT/next-fest/DesktopBuddy.x86_64"
chmod +x "$ARTIFACT_ROOT/next-fest/DesktopBuddy.x86_64"
"$ARTIFACT_ROOT/next-fest/DesktopBuddy.x86_64" --headless -- \
  --journey=production_bootstrap_persistence \
  --seed=1 \
  --artifacts="$ARTIFACT_ROOT/next-fest/journey"
