# Milestone 5.5 Character Editor Phase A Completion

Status: implementation complete through Task A9; acceptance is determined by the permanent `Phase A Character Editor` workflow and the Windows manual matrix.

## Delivered task boundaries

- A0 — source-of-truth alignment and scope gate.
- A1 — engine-free schema-v1 character document, validation, migration, stable feature catalog, and appearance compiler.
- A2 — shared `BuddyVisualRigView`, trusted geometry lock, scorch-over-custom-base handling, and physics-free static transform source.
- A3 — closed procedural renderer registry for all shipped eye, brow, mouth, and torso-accent IDs.
- A4 — parameterized semantic face and torso-accent compositors with exact-key invalidation.
- A5 — failure-safe local character store, quarantine/recovery, canonical GUID paths, and bounded library indexing.
- A6 — progress schema 7 active selection, fixed-tick last-request-wins activation, immediate save, fallback, delete, and reset integration.
- A7 — single-owner gameplay pause, same-window editor transition, complete window restoration, monitor recovery, and resize isolation.
- A8 — approved dock Settings entry, working-copy editor session, physics-free preview, complete parametric controls, deterministic randomization, local library, and unsaved-change handling.
- A9 — aggregate release workflow and `character_editor_create_use_and_react` scenario/journey.

## Phase A scope retained

The implementation remains visual-only. Character data cannot replace or mutate trusted rig geometry, collision, masses, drives, forces, connectors, pain, mood, economy, tool behavior, or gameplay resources. Local character documents remain outside Steam Cloud; only the nullable active character GUID is progress state.

The Phase B surface-underlay seam remains null. Freehand painting, paint files, package import/export, Workshop, arbitrary resources/scripts, multiple buddies, gameplay profiles, physics customization, and cosmetic economy remain deferred.

## Automated exit gate

`.github/workflows/phase-a-character-editor.yml` rebuilds the solution, runs all non-baseline domain tests, imports Godot, executes every A3–A8 focused scenario, runs the A9 exit scenario, runs the A9 journey at seeds 1 and 7, and repeats presentation/window/lifecycle regressions.

The known Linux-only `ProgressStoreTests` Windows-path baseline remains documented separately and is excluded only from this focused workflow; production persistence behavior is not weakened.

## Manual Windows acceptance still required

Before owner acceptance, run the documented Windows 10/11 matrix at 100%, 125%, 150%, and 200% DPI, including multi-monitor removal, transparency fallback, focus/input recovery, complete window restoration, readability, and real-input editor use. The automated journey is not a substitute for this platform-specific acceptance pass.
