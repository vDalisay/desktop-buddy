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

# Keep verification presets out of the checked-in export configuration. They
# intentionally retain tests/journeys because the parent process is a debug-only
# journey orchestrator, while excluding generated artifact directories so an
# export can never recursively package a previous export.
cat >> export_presets.cfg <<'EOF'

[preset.4]

name="CI Linux Initial Demo Verification"
platform="Linux/BSD"
runnable=false
advanced_options=false
dedicated_server=false
custom_features="steam,steam_demo"
export_filter="all_resources"
include_filter=""
exclude_filter=".artifacts/*, build/*, docs/*, devtools/*, authoring/*, *.md"
export_path=".artifacts/production-bootstrap/initial/DesktopBuddy.x86_64"
patches=PackedStringArray()
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.4.options]

custom_template/debug=""
custom_template/release=""
binary_format/architecture="x86_64"
binary_format/embed_pck=false
debug/export_console_wrapper=1
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false

[preset.5]

name="CI Linux Next Fest Verification"
platform="Linux/BSD"
runnable=false
advanced_options=false
dedicated_server=false
custom_features="steam,steam_demo,next_fest_demo"
export_filter="all_resources"
include_filter=""
exclude_filter=".artifacts/*, build/*, docs/*, devtools/*, authoring/*, *.md"
export_path=".artifacts/production-bootstrap/next-fest/DesktopBuddy.x86_64"
patches=PackedStringArray()
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.5.options]

custom_template/debug=""
custom_template/release=""
binary_format/architecture="x86_64"
binary_format/embed_pck=false
debug/export_console_wrapper=1
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false
EOF

xvfb-run -a "$GODOT_BIN" --headless --path . --import

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
