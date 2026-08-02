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
  FR-011.15 (whole-credit display), FR-013 (16 selectable interactions,
  unrestricted purchases, and confirmed Reset Progress), FR-019 (Power Grab is
  purchased in the shop and becomes selectable), §9 (painting out of launch
  scope), §10 (paint = future scope).
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
   (vertical, right-center, expanded). Reset Progress preserves this dock state
   and all other preference/settings fields.
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
  (Control scripts) — content controls. System raises existing tray seams and a
  Reset Progress intent; it holds no lifecycle or persistence logic.
- **`ResetProgressDialog`** — modal confirmation view only. It lists erased and
  preserved categories, defaults focus to Cancel, maps Escape/window-close to
  Cancel, and produces a typed confirmation token only from the destructive
  button. The application reset service owns candidate construction, validation,
  atomic persistence, and committed-state publication.
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

### Task 4 — Flyout host + Settings menu

**Owner decision 2026-08-02:** the dock carries a dedicated **Settings** button, and its
menu is the settings surface FR-003.2 requires. Reset Progress is a button inside it
(Task 7) and is reachable nowhere else — no hotkey, no tray item, no top-level dock button.

`DockFlyoutHost` with bloom tween, single-open invariant, outside-click and
Escape close, edge-flip placement (opens on the free side of the dock,
mirroring the mockup's `reposition()` logic). `DockSettingsSection` rows: Sound
toggle (binds existing audio bus setting), Zoom toggle (existing shell zoom),
Hide to tray → `TrayCommandComponent.RequestHideShow()`, Save & quit →
`RequestSaveAndQuit()`, and Reset Progress last, visually separated as destructive.

**Accept:** headless — open/close cycle; only one flyout open; flyout rect
tracked in hit regions (Task 3 scenario extended); settings rows raise the same
events the hotkeys raise (assert via `HideShowRequestCount` /
`SaveAndQuitRequestCount`); the Reset Progress row only *arms*
(`ResetProgressRequestCount == 1`, `ResetProgressConfirmCount == 0`, save unchanged).

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

`DockToolsSection` binds to `CataloguePolicy.SelectableEntries`: owned tools
are selectable (selection ring + check badge); unowned tools remain absent or
locked according to the accepted catalogue presentation. `DockShopSection`
binds to all visible unowned purchasables, uses the authoritative purchase
service, and swaps a successful row to `OWNED`.

Power Grab appears in the shop between Fire Sprayer and Repair Kit. After its
one-time purchase it becomes a normal selectable inventory cell; Normal Grab
remains selectable. The dock must not carry a passive-upgrade exception or a
hand-maintained tool list. Catalogue display order is not a prerequisite chain:
a later visible entry can be bought while earlier entries remain unowned.

**Accept:** exact four starting/twelve purchasable grid; insufficient funds and
duplicate purchase are non-mutating; save/skip purchase works; Power Grab unlocks
a selectable cell; Normal/Power switching persists and safely cancels an active
grab; relaunch derives the same inventory from catalogue plus ownership.

### Task 7 — Reset Progress confirmation

**The seam already exists (M5 Task 13A).** `TrayCommandComponent.RequestResetProgress()`
arms a reset and raises `ResetProgressRequested` — this is the dialog's cue, and it mutates
nothing. `ConfirmResetProgress()` within the arming window raises `ResetProgressConfirmed`,
which the composition root turns into `ProgressReset.ResetAsync`; `CancelResetProgress()`,
a lapsed window, or any other tray command disarms it. The dialog binds to those three
calls and must not implement a second mutation path or its own reset service.

Add Reset Progress to the Settings menu built in Task 4 — the settings button on the dock
is the only route to it. Selecting it opens
`ResetProgressDialog`; the first action never mutates progress. Copy names the
erased categories: money, purchased tools, mood/buddy memory and traits, gameplay
statistics, achievement progress, and play timers. It also states that settings,
window/dock preferences, and already-unlocked platform achievements are kept.

Cancel has initial focus. Escape, outside-dismiss, and window close equal Cancel.
Only the explicit destructive confirmation calls the typed reset service. Disable
repeat activation while the transaction is pending; on success refresh presenters
from committed state and close the dialog; on failure retain the dialog/state and
show a recoverable error.

**Accept:** confirmed reset produces first-run gameplay with Normal Grab selected;
all language/audio/control/accessibility/comfort/presentation/window/zoom/dock
preferences compare equal; platform achievements receive no revoke call; cancel,
dismiss, missing/stale confirmation, validation failure, and injected save failure
leave complete in-memory and persisted snapshots equal to before.

### Task 8 — Paint stub (flagged off)

Paint button hidden unless a debug/config flag enables it. Flyout renders
swatches + brush/fill/wash selector but emits a no-op `PaintIntent` event.
No paint gameplay.

**Accept:** flag off → no paint button, no hit-region contribution; flag on →
flyout opens and intent events fire; nothing else changes.

## Sequencing and ownership

Tasks 1–5 are buildable against existing shell/save seams after the dock's clean-room
design gate. Task 6 consumes the M5 catalogue/purchase/selection services. Task 7
consumes the Task 13 transactional reset service and must not implement its own
mutation. Task 8 follows Task 4 and remains flagged off.

Recommended order: 1 → 2 → 3 → 4 → 5 → 6 → 7; Task 8 may follow Task 4.

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
