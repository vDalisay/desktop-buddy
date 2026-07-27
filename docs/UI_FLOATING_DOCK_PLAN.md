# Floating Dock UI — Implementation Plan

Status: **DRAFT — owner reviewed mockup direction 2026-07-27 (Nintendo/Mii style
floating dock chosen from three candidates)**. Mockup reference:
`ui-dock-nintendo.html` (session scratchpad; copy into `docs/mockups/` if it
should be tracked). This plan is written for agent handoff: each task is
self-contained with acceptance criteria and names the real seams it touches.

The dock is the retractable panel required by FR-003.2 (tools / shop / settings
in a retractable in-window panel). It replaces the bare `MoneyHud` corner panel
with a movable, collapsible, Mii-styled dock that hosts money, tool selection,
shop, paint (stub), and system commands.

## Authoritative sources

- `docs/PRODUCT_REQUIREMENTS.md` — FR-003.1 (money always visible in compact
  HUD), FR-003.2 (retractable panel), FR-003.4 (coalesced `+$N.N` feedback),
  FR-011.15 (whole-credit display), FR-019 (Strength Upgrade occupies shop slot
  but never tool selection), §9 (painting out of launch scope), §10 (paint =
  future scope).
- `docs/ROADMAP.md` — Milestone 5 "retractable tool/shop/settings panel";
  final responsive UI layouts polish lands later.
- `docs/ARCHITECTURE.md` — §5 (stable string IDs, `IProgressStore`), §9
  (window/boundary rebuild), §12 (save architecture), §24 (tray commands).
- Existing code seams (verified 2026-07-27):
  - `src/Platform/DesktopShellController.cs` — `UpdateWorkModeHitRegions(...)`
    (line ~119) merges dynamic Work-Mode hit regions; fallback region logic.
  - `src/App/SandboxRoot.cs` — `RefreshWorkModeHitRegions()` (line ~358)
    composes buddy hit regions and calls the shell seam.
  - `src/UI/MoneyHudPresenter.cs` — balance + reward feedback presenter,
    subscribed to `InteractionDamageComponent` and `EconomyService`.
  - `scenes/sandbox.tscn` — `OverlayUi` CanvasLayer hosting `MoneyHud`.
  - `src/Platform/TrayCommandComponent.cs` — `RequestHideShow()` /
    `RequestSaveAndQuit()` seams the dock's System entries must reuse.

## Design language (from accepted mockup)

- **Surfaces:** cream (`#fdf8ec`) pill/card panels, 3px white borders, large
  radii (dock 26px, cards 22px, tool cells 16px), hard offset shadow
  (bottom-offset solid + soft blur) for the toy/plastic look.
- **Category colors:** tools blue `#58a7f0`, shop orange `#f5a742`, paint pink
  `#f078a8`, system teal `#58c9b4`, money gold `#f7c948`; each button a circle
  with vertical two-tone gradient and darker same-hue bottom shadow.
- **Type:** rounded heavy-weight font, ink `#4a5361` on cream; white on
  category bands. No thin weights.
- **Motion:** overshoot bounce `cubic-bezier(.34,1.56,.64,1)` ≈ Godot
  `Tween` `TransitionType.Back` + `EaseType.Out`. Hover scale 1.12–1.14 with
  ±3–4° tilt, press scale 0.94, flyout "bloom" scale 0.6→1.0 over ~0.28 s,
  locked-item wobble ±8° over 0.26 s. Reward pop: rise ~46 px, fade, ~1 s.
- **Structure:** grip (drag dots) → coin chip → category buttons → collapse.
  Flyout is a single card beside the dock with colored header band; exactly one
  flyout open; outside click closes. Collapse shrinks dock to grip + coin chip
  pill (money stays visible per FR-003.1).
- **Shop rows:** icon tile + name + gold-coin price button; owned items show a
  green `OWNED` chip; purchases permanent. Locked tools appear in the tools
  grid greyed with a lock and wobble on click (discoverability of the shop).

## Hard constraints

1. **Work-Mode click passthrough.** Every visible dock/flyout rect must be
   registered as a Work-Mode hit region or clicks fall through to the desktop.
   Regions must update on drag, orientation toggle, collapse/expand, flyout
   open/close, and window resize.
2. **Transparent window.** All chrome is Godot `Control` UI in `OverlayUi`;
   no OS widgets. Rounded corners are `StyleBoxFlat` — no textures required
   for v1 (icons can be placeholder emoji/`Label` glyphs until art pass).
3. **Bounds clamping.** Dock and flyout must stay inside the sandbox window;
   reflow on window resize and on zoom changes.
4. **Persistence.** Dock position (normalized to window size), orientation,
   and collapsed state persist via the M4 save architecture (`IProgressStore`,
   ARCHITECTURE §5/§12). Absent/corrupt values fall back to defaults
   (vertical, right-center, expanded).
5. **Content phasing.** Tools/shop content binds to the M5 catalogue; until it
   exists the grid reads from a placeholder catalogue resource. Paint flyout
   ships hidden behind a debug flag (future-scope §10) — build the seam, not
   the feature.
6. **No gameplay input theft.** Dock input is `Control.gui_input` /
   mouse-filter scoped; it must not consume clicks outside its rects. The
   single-input-reader rule for gameplay (`ToolInputFrame`) is untouched.

## Architecture

New namespace `DesktopBuddy.UI.Dock` under `src/UI/Dock/`:

- **`DockController`** (`PanelContainer`) — root dock node in `OverlayUi`.
  Owns layout state (position, orientation, collapsed), drag handling via the
  grip, clamping, and a `LayoutChanged` event carrying its client-pixel rects.
  Exposes `IReadOnlyList<Rect2I> HitRects` (dock rect + open flyout rect).
- **`DockLayoutState`** (pure C#, `domain/` if trivially portable, else
  plain record in `src/UI/Dock/`) — serializable layout state + normalization
  and clamping rules. Unit-testable without Godot.
- **`DockFlyoutHost`** (`PanelContainer`) — one card; swaps content sections
  (tools / shop / paint / system); bloom-in tween; `Opened`/`Closed` events.
- **`DockToolsSection`**, **`DockShopSection`**, **`DockSystemSection`**
  (Control scripts) — content controls. System section raises the existing
  `TrayCommandComponent` seams; it holds no lifecycle logic itself.
- **`DockTheme.tres`** — Theme resource with the StyleBoxFlats, colors, and
  font sizes from the design language; all controls read from theme, no
  per-node style overrides.
- **`MoneyHudPresenter`** — reused as-is; its labels reparent into the coin
  chip. Reward feedback (`+$N.N`) becomes a pop label anchored to the chip.

Wiring: `SandboxRoot` composes `DockController`, subscribes `LayoutChanged`,
and extends `RefreshWorkModeHitRegions()` to concatenate buddy regions with
`DockController.HitRects` before the existing
`Shell.UpdateWorkModeHitRegions(...)` call. Persistence flows through the same
save/load path M4 Task 0 establishes (string-ID keys, e.g. `ui.dock.*`).

## Tasks

### Task 1 — Layout state + persistence (pure logic first)

Build `DockLayoutState`: normalized anchor position, orientation
(vertical/horizontal), collapsed flag; clamp rules against a window size;
serialization keys `ui.dock.pos.x`, `ui.dock.pos.y`, `ui.dock.orientation`,
`ui.dock.collapsed`.

**Accept:** unit tests — clamp keeps dock fully inside arbitrary window sizes
including smaller-than-dock windows; roundtrip serialize/deserialize; corrupt
or missing values yield defaults; resize re-clamp preserves relative position.

### Task 2 — DockController shell

`DockController` + `DockTheme.tres` + scene wiring in `sandbox.tscn`
(`OverlayUi/Dock`). Grip drag (pointer capture equivalent: `gui_input` +
`grab_focus`/mouse capture), double-click grip toggles orientation, collapse
button toggles pill mode with money chip visible. Tween specs from the design
language. `LayoutChanged` fires on every rect-affecting change.

**Accept:** headless scenario — dock spawns at default; simulated drag moves
it and `LayoutChanged` rect matches node rect; orientation toggle swaps
container axis; collapse hides category buttons but keeps coin chip;
relaunch restores persisted state.

### Task 3 — Hit-region integration

Extend `SandboxRoot.RefreshWorkModeHitRegions()` to merge
`DockController.HitRects`. Subscribe dock `LayoutChanged` → refresh. Verify
Play Mode unaffected (whole window already interactive).

**Accept:** headless scenario using `EmulatedWindowsDesktopAdapter.
LastWorkModeHitRegions` — regions include dock rect; drag updates them;
collapse shrinks them; opening a flyout adds its rect; closing removes it;
buddy region still present throughout.

### Task 4 — Flyout host + system section

`DockFlyoutHost` with bloom tween, single-open invariant, outside-click and
Escape close, edge-flip placement (opens on the free side of the dock,
mirroring the mockup's `reposition()` logic). `DockSystemSection` rows: Sound
toggle (binds existing audio bus setting), Zoom toggle (existing shell zoom),
Hide to tray → `TrayCommandComponent.RequestHideShow()`, Save & quit →
`RequestSaveAndQuit()`.

**Accept:** headless — open/close cycle; only one flyout open; flyout rect
tracked in hit regions (Task 3 scenario extended); system rows raise the same
events the hotkeys raise (assert via `HideShowRequestCount` /
`SaveAndQuitRequestCount`).

### Task 5 — Money migration

Reparent balance + reward labels into the coin chip; delete the old
`MoneyHud` PanelContainer; keep `MoneyHudPresenter` API and its
`InteractionDamageComponent`/`EconomyService` subscriptions intact. Reward
pop animates per design language but keeps FR-003.4 coalescing behavior
(presenter already owns timing).

**Accept:** existing MoneyHud tests/scenarios pass against the new parent;
balance shows whole credits (FR-011.15); reward label visible during feedback
window then hidden; collapsed dock still shows balance.

### Task 6 — Tools + shop sections (M5 catalogue binding)

`DockToolsSection` grid bound to catalogue: owned tools selectable (selection
ring + check badge), unowned greyed with lock + wobble; selection raises the
tool-change seam the M5 tool system defines. `DockShopSection` rows with
price buttons; purchase flow calls the M5 purchase service; owned rows swap
price button for `OWNED` chip; Strength Upgrade appears in shop but never in
the tools grid (FR-019). Until the catalogue lands, both sections read a
placeholder `DockCatalogueStub` resource so Tasks 1–5 are not blocked.

**Accept:** unit — FR-019 filtering (passive upgrades excluded from tools
grid); insufficient-funds purchase rejected with feedback and no state
change; purchase marks owned, unlocks tool cell, and persists. Headless —
buy → tool appears unlocked after relaunch.

### Task 7 — Paint stub (flagged off)

Paint button hidden unless a debug/config flag enables it. Flyout renders
swatches + brush/fill/wash selector but emits a no-op `PaintIntent` event.
No paint gameplay.

**Accept:** flag off → no paint button, no hit-region contribution; flag on →
flyout opens and intent events fire; nothing else changes.

## Sequencing and ownership

Tasks 1–5 are buildable now against existing M4-era code (only the
placeholder catalogue is stubbed). Task 6 blocks on the M5 catalogue/purchase
services; build it in the M5 milestone branch. Task 7 anytime after Task 4.
Recommended order: 1 → 2 → 3 → 4 → 5, then 6/7 with M5.

## Out of scope

- Final art/icons, SFX, accessibility pass (later "responsive UI layouts"
  roadmap line). Emoji/label glyph placeholders are fine.
- Paint gameplay, cosmetics economy (future scope §10).
- Native tray icon and restore-from-hidden (M6, ARCHITECTURE §24).
- Controller navigation (mouse/keyboard product; revisit only if product
  scope changes).
- Buddy-anchored radial quick menu (owner liked it as a possible later
  addition; not part of this plan).

## Test commands

Use the standard three from the toolchain memory: domain unit suite, headless
scenario runner, and the Godot build check. New scenarios register in
`src/Testing/ScenarioCatalog.cs` following existing naming.
