# Desktop Buddy — Steam Demo objective polish audit

Status: **IMPLEMENTED — LOCAL / FULL-CI VERIFICATION REQUIRED**  
Branch: `audit`  
Base: `agent/steam-demo-polish` at `7c9d97b8140b0d493e99219d34cea5ae830a2109`  
Recorded: 2026-08-19

## Scope

This audit implements the remaining objective polish from DEMO-7/DEMO-8 without deciding owner-gated taste or scope questions. It covers:

- Buy / Equip / Save / disabled-state wording and explanations;
- keyboard focus and hotkey-capture correctness;
- tutorial/status-help consistency;
- accessibility/readability discoverability where controls become unavailable;
- safe hot-path reductions in always-running UI/runtime glue;
- focused regression scenarios for the behavior-changing optimizations.

The audit deliberately does **not** decide final economy pacing, final tutorial-character feel, final models/SFX, Room Decorator Demo visibility, or final visual taste.

## Findings and changes

### 1. Inventory / tool vocabulary

The unified Inventory already owns both purchase and equip behavior. The audit keeps that contract and normalizes player-facing state:

- unowned affordable item: `Buy` with permanent-purchase explanation;
- unowned unaffordable item: disabled `Buy` with exact price/current-balance explanation;
- owned item: `Equip`;
- active item: disabled `Equipped` with an explicit active-state explanation;
- the legacy tool picker uses `Equip` rather than `Select` and explains why unowned rows are disabled.

No ownership, price, or economy rules changed.

### 2. Settings hotkey capture

A real state bug existed in the shared Settings hotkey capture: cancelling a later row could restore the previous row's chord, and a bare modifier could prematurely end capture.

The capture is now isolated per row:

- each capture retains its own original chord;
- bare Ctrl / Shift / Alt waits for the main key;
- Escape restores exactly that row's prior chord;
- beginning another capture safely cancels the old one;
- hiding Settings cancels an unfinished capture;
- status text explains the current capture state.

A focused `settings_hotkey_capture` scenario exercises these paths through Godot input dispatch.

### 3. First-session wording

The onboarding flow still referred to `Shop` after the shipping command became `Inventory`. Player-facing guidance now uses `Inventory`, while the persisted internal tutorial step ID is unchanged for save compatibility.

`Dismiss` and `Skip Tutorial` also explain their different effects.

### 4. Character-slot UX and disk work

At capacity, New Character, Duplicate, and Buy Slot now explain exactly why they are disabled. Slot purchase help includes the next price and current balance.

The capacity UI previously enumerated `user://characters` roughly every refresh while the editor was open. Disk occupancy is now cached for a bounded interval and invalidated immediately when:

- the working character changes; or
- a dirty working character becomes clean after save.

An unsaved new/duplicated working character continues to reserve a slot before it reaches disk, so the optimization does not loosen the capacity rule.

### 5. Paint editor objective polish

Safe paint bootstrap costs were reduced without altering paint authority:

- focus-graph scanning is throttled rather than recursively rebuilt every rendered frame;
- modal theme composition stops polling after both persistent dialogs are themed;
- tool and File/Edit/View menu composition stop processing after successful one-time wiring;
- keyboard tool shortcuts and F10/Alt menu input remain active independently of `_Process`;
- brush-size, zoom, and reset-view controls explain min/max/default disabled states.

The larger `Win98PaintUxPolishBootstrap` was intentionally not broadly rewritten in this branch because it is owner-tested layout correction glue and requires local profiling before an event-driven refactor.

### 6. Buddy Studio catalogue / performance

The largest objective editor hot path found was catalogue churn:

- `Win98CatalogGrid.SetItems()` rebuilt every Button/preview/label tree whenever only ownership, price, tooltip, badge, or accent changed;
- Buddy Studio's compatibility layer reconciled its Asset Forge catalogue every rendered frame;
- the historical shipped-only refresh could temporarily replace the full generated catalogue during same-category edits, forcing another structural rebuild.

The shared catalogue grid now:

- updates existing tile controls in place when ID/order structure is unchanged;
- preserves selection across presentation-only updates;
- clears selection if the selected item becomes unavailable;
- avoids recreating persistent accent styleboxes when accent state did not change;
- retains normal structural replacement semantics by default;
- exposes a narrow opt-in compatibility mode that preserves an already-composed superset when a legacy caller submits a strict subset.

Buddy Studio enables that subset-compatibility mode only for its historical shipped-only refresh path. Category changes still replace normally, while same-category color/transform/balance changes no longer delete generated tiles and rebuild the entire grid.

Asset Forge reconciliation is bounded to 10 Hz while direct preview navigation remains frame-responsive.

The `win98_catalog_grid_update` scenario covers in-place refresh, selected-item disable, opt-in subset preservation, and genuine structural rebuild.

### 7. Always-running runtime glue

Several bootstraps were doing work after their composition task was already complete:

- Inventory chrome retirement now applies once rather than every physics tick;
- active-tool status only reformats when the selected tool changes;
- Customize command ordering is sorted on registry mutation instead of every snapshot;
- Buddy Studio startup polling stops after command registration;
- Environment customization caches its shipping Sandbox lookup;
- dropped-tool input sleeps between actual drop/double-click requests after attachment;
- paint tool/menu/modal composition stops after it has finished.

These changes preserve dynamic input and mode behavior while removing idle callback work.

## Audited and intentionally left unchanged

### Buddy mood/state read

The current reaction system already has five distinct persistent face bands (`:(`, `:/`, neutral, `:)`, `^_^`) with behavior coverage. Adding a separate textual mood HUD would be a visual/taste decision rather than an objective correctness fix, so it remains an owner visual gate.

### Paint Background

The existing flow already uses the owner-tested vocabulary and status model, including `Bucket Fill`, `Save and Exit`, `Discard`, and `Keep Editing`. No normalization was applied over those deliberate labels.

### Room Decorator

The existing workspace already distinguishes Buy vs Place, shows cost/funds, and provides staged Save/Revert/Keep Editing semantics. The audit fixes no scope and does not expand or hide the feature; Demo visibility remains an owner gate.

## Larger profiling candidates not rewritten without a local runtime loop

These remain worthwhile profiling targets only if local measurements show they are material:

1. **`Win98CommandBarBootstrap` central refresh loop** — still performs several shell/layout/ownership refreshes each frame and contains legacy recursive lookups. A deeper event-driven refactor touches central window lifecycle and should follow local profiling.
2. **`Win98PaintUxPolishBootstrap`** — still contains many owner-tested recursive layout corrections while Paint is open. Its risk is higher than the smaller bootstraps already optimized.
3. **Environment procedural catalogue previews** — legacy procedural decoration previews can create fresh `Image`/`ImageTexture` objects during presentation refresh. Generated Asset Forge thumbnails are already trusted resources. Cache this only if local profiling shows the path is material.

## Regression gates

The full `main`-target CI job now explicitly invokes:

- `shop_panel_purchase`;
- `settings_hotkey_capture`;
- `win98_catalog_grid_update`.

The `audit` PR is intentionally stacked on `agent/steam-demo-polish`, not `main`. Repository CI only runs the full Godot sweep for PRs targeting `main`; branch pushes use the quick build/unit-test job. Therefore these new full scenarios must be observed when the changes are integrated into a main-target PR or run manually.

## Local verification boundary

The remaining checks require the actual game/runtime rather than more connector-only code review:

1. **Inventory:** unaffordable explanations; Buy -> Equip -> Equipped; active-tool status.
2. **Settings:** bare-modifier capture, Escape cancellation, switching between Work/Drop hotkey rows, and closing Settings during capture.
3. **Character slots:** capacity explanations and immediate count correctness after creating/duplicating/saving near the slot limit.
4. **Buddy Studio:** rapid color/transform edits, category switching, generated + shipped tiles, preview/equipped distinction, and perceived responsiveness.
5. **Paint:** Tab traversal, S/P/F/C shortcuts, F10/Alt menus, disabled min/max help, and modal theming.
6. **Dropped tools:** after idle time, Drop Tool and double-click re-equip must still react on the next physics boundary.
7. **Environment / Work Mode:** environment presentation should still hide/show correctly across Work Mode transitions.
8. **Buddy state:** verify the existing five face bands are visually clear enough; any stronger presentation is an owner visual decision.

After those checks, objective regressions belong on this branch; economy pacing, Room Decorator visibility, final assets/audio, and visual taste return to the parent Demo owner-gate pass.
