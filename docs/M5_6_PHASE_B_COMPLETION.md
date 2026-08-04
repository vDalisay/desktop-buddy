# Milestone 5.6 Phase B Painting — B6 Completion Record

**Branch:** `m5-6-b1-paint-mapping`  
**Implementation date:** 2026-08-04  
**Status:** implementation complete; final local automated validation remains required before merge.

## Scope completed

- Registered `paint_save_failure_preserves_working_copy`.
- Registered `paint_runtime_fidelity`.
- Added `character_paint_save_use_restart` to the established journey suite.
- Added deterministic paint save/use/restart predicates for editor working-copy state, persistence, runtime activation, and restart restoration.
- Replaced retained full-surface ordinary-stroke Undo snapshots with bounded dirty-rectangle patches.
- Added focused Undo memory and byte-exact restoration tests.
- Centralized Paint UI copy under `character_editor.paint.*` keys with a shipped English fallback.
- Preserved the existing Work/Play, full-screen, dock-window, launcher, physics, economy, and CI architecture.

## Save-failure gate

`paint_save_failure_preserves_working_copy` uses:

- `CharacterEditorSession`
- `PaintWorkspace`
- `CharacterPaintStore`
- an isolated temporary character root
- deterministic one-shot `ICharacterFileSystem` failure injection at staging activation

It verifies:

- failed save is reported;
- working paint pixels remain unchanged;
- dirty state remains set;
- Undo remains available and byte-exact;
- the previous valid disk state remains loadable;
- no partial new paint becomes active;
- retry succeeds and persists the new pixels.

## Runtime fidelity gate

`paint_runtime_fidelity` verifies:

- saved PNGs decode to exact source bytes;
- activation is not applied before `PhysicsTick()`;
- the exact paint payload is published after one fixed tick;
- runtime uploads occur once per changed part;
- equal payloads do not upload again;
- one changed part causes one additional upload;
- removing a part clears its underlay;
- source CPU bytes remain unchanged;
- trusted rig identity remains unchanged;
- face decals remain above paint.

## Journey

Journey ID:

```text
character_paint_save_use_restart
```

Predicates:

```text
b6_journey_paints_two_parts_and_tracks_dirty
b6_journey_eraser_undo_is_exact
b6_journey_erase_all_confirmation_result_is_undoable
b6_journey_save_and_use_activates_exact_paint
b6_journey_saved_pngs_match_editor_pixels
b6_journey_restart_restores_selection_pixels_and_rig
```

The repository's existing Phase A editor journey entrypoint is reused rather than introducing a second runner. The deterministic core exercises the production session, persistence, activation, runtime bridge, and restart boundaries. Real Windows pointer/control behavior remains part of the manual owner gate.

## Undo and memory closure

Ordinary gestures now retain only their affected rectangles. When a gesture expands, the retained before-state is expanded while preserving the original bytes already covered by the gesture.

Focused tests verify:

- a normal stroke retains less than one complete surface;
- many small strokes remain within the 48 MiB Undo budget;
- the newest complete command remains undoable;
- expanded stroke patches restore byte-exact original pixels;
- Erase All retains a complete before-state within the same cap.

Locked budgets remain unchanged:

```text
Working surfaces: 6 MiB
Saved baseline:    6 MiB
Undo:             48 MiB
Total editing:    64 MiB
Active GPU paint:  8 MiB maximum
```

## Localization closure

Paint UI strings now use the `character_editor.paint.*` namespace. `PaintUiText` first asks Godot's translation server for the key and then falls back to the shipped English copy. This prevents raw localization keys from appearing when a locale does not yet provide an override.

Localized surfaces include:

- Paint and Appearance Controls
- Brush and Eraser
- color tooltip
- brush-size label
- Undo and Erase All
- zoom and Reset View
- hover and input help
- Erase All title/body/action
- Paint status formatting

## Owner evidence

On 2026-08-04 the owner reported that the requested functional checks were working as intended, including painting multiple parts, save/use, restart persistence, runtime appearance, layer order, unchanged-payload behavior, and clearing paint from an individual part.

This records owner acceptance of the manually exercised functional behavior. DPI and mixed-monitor rows not explicitly exercised remain unverified and must not be reported as passed.

## Required local validation

Run from the repository root:

```bat
tools\build_game.bat
```

```bat
dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug
```

```bat
Godot_v4.6.1-stable_mono_win64_console.exe --headless --path . --import
```

Run all required Phase B scenarios:

```text
paint_frontal_uv_mapping
paint_stroke_and_eraser
paint_multi_part_stroke_undo
paint_erase_all_undo
paint_memory_budget
paint_under_expression_layer_order
paint_persistence_roundtrip
paint_invalid_png_rejected
paint_save_failure_preserves_working_copy
paint_preview_has_no_physics
paint_runtime_fidelity
```

Run the journey:

```bat
Godot_v4.6.1-stable_mono_win64_console.exe ^
  --headless ^
  --path . ^
  -- --journey=character_paint_save_use_restart --seed=1 --artifacts=.artifacts\paint-journey
```

Then run:

```bat
tools\quick_validate.bat
```

Manual launch:

```bat
tools\play_game.bat
```

## Final acceptance status

- Implementation: complete.
- Functional owner check: accepted on 2026-08-04.
- Local compilation after the final closure commits: pending.
- Full automated suite after the final closure commits: pending.
- DPI matrix: partially unverified.
- Mixed-DPI multi-monitor matrix: unverified unless separately recorded.
- CI: intentionally paused; no CI pass is claimed.
- Merge to `main`: not performed.
- Workshop/Phase C: not started.
- Milestone 6: not started.
