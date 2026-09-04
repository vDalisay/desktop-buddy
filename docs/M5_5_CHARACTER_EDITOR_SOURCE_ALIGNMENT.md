# Milestone 5.5 Character Editor — Authoritative Source Alignment

> **Historical scope note:** This document governs Phase A. Its Workshop deferral statements are superseded by `docs/M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md`; Workshop v1 is included in the Steam Demo and full Steam release, and excluded from itch.io.

Status: **A0 complete — implementation may begin at A1.**  
Effective baseline: `main` after M5 Task 13 commit `7e4c88763e7afdf9e290b472920623b86786cfe4`.  
Task-level implementation plan: `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`.

This document is the normative Milestone 5.5 supplement to:

- `docs/PRODUCT_REQUIREMENTS.md`;
- `docs/ARCHITECTURE.md`;
- `docs/TEST_PLAN.md`;
- `docs/AGENT_VERIFICATION_AND_E2E.md`;
- `docs/ROADMAP.md`.

For Milestone 5.5 only, this supplement resolves conflicts between those older baseline
documents and the scheduled Character Editor Phase A. `docs/DECISIONS.md` remains higher
priority. The task-level contracts in `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` remain the
implementation source of truth where this supplement does not restate them.

## 1. Scope authorization and milestone order

Milestone 5 is complete and owner-accepted as of 2026-08-02. Character Editor **Phase A**
is Milestone 5.5 and is authorized now, before Milestone 6.

Phase A includes only:

- an engine-free parametric character document;
- six buddy-part base colors;
- parametric eyes, brows, mouth, and one torso-front accent;
- stable shipped feature IDs and deterministic randomization;
- a local uncapped character library with lazy indexing;
- one selected character appearance applied to the existing buddy;
- a physics-free live editor preview;
- editor-mode pause/window transitions;
- local persistence and active-selection persistence;
- tests, scenarios, a real-input journey, and Windows validation.

Phase A does **not** authorize:

- freehand painting, brush/stroke types, paint PNGs, or paint schema fields;
- Steam Workshop, packages, import/export, subscriptions, or moderation systems;
- arbitrary Resources, scenes, scripts, DLLs, or mods;
- multiple simultaneous buddies or multiple gameplay save profiles;
- gameplay/physics shape customization;
- cosmetic progression, prices, unlocks, achievements, or economy integration.

The only Phase B seam allowed in Phase A is the internal nullable surface-underlay binding
named in the task plan. Every Phase A production caller passes `null`.

The legacy statements in `AGENTS.md` and `PRODUCT_REQUIREMENTS.md` that categorize all buddy
coloring/custom buddies as deferred are superseded **only for the Phase A items above**.
Painting and Workshop remain deferred.

## 2. Product requirements supplement

### US-10 — Character identity

As a player, I want to create, save, preview, and select an original visual identity for my
single buddy without changing gameplay behavior or progression.

### FR-020 — Parametric character documents

1. WHEN a new character is created THEN it SHALL receive a fresh GUID, a valid default name,
   the six built-in part colors, the default known eye/brow/mouth IDs, and `accent.none`.
2. WHEN a character is edited THEN the available persisted axes SHALL be exactly feature ID,
   normalized X/Y offset, uniform scale, opaque color, and the six part colors.
3. Character data SHALL NOT contain rotation, opacity, mesh dimensions, rig data, collision,
   mass, drives, forces, connector data, depth, gameplay tuning, economy fields, or scripts.
4. Display names SHALL contain 1–40 Unicode scalar values after trimming and SHALL reject
   controls, line breaks, and Windows-invalid filename characters.
5. Colors SHALL serialize as opaque uppercase `#RRGGBB`; offsets SHALL normalize to
   `[-1,+1]`; scale SHALL normalize to `[0.75,1.25]`.
6. Unknown feature IDs SHALL remain in the saved document and SHALL render as the slot's
   built-in default in the current build without rewriting the unknown ID.
7. Unsupported future document schemas SHALL not be quarantined as corrupt.
8. The built-in buddy and the default character document SHALL use the same parameterized
   face rendering path and SHALL remain semantically equivalent.

### FR-021 — Editor working copy and commands

1. WHEN a saved character is opened THEN editing SHALL occur in an in-memory working copy.
2. WHEN controls change the working copy THEN the preview SHALL update without changing the
   saved file or active runtime appearance.
3. WHEN Save succeeds THEN the normalized document SHALL be atomically persisted and the
   saved baseline SHALL advance.
4. WHEN Save fails THEN the editor SHALL stay open and retain the working copy and dirty state.
5. WHEN closing, deleting, or opening another character while dirty THEN the editor SHALL
   offer exactly Save, Discard, and Continue Editing.
6. WHEN Discard is chosen THEN the last successfully saved document and preview SHALL be restored.
7. A new character SHALL NOT become persisted, indexed, or active until its first successful save.
8. Duplicate SHALL assign a fresh GUID and SHALL NOT copy active-selection state.
9. Randomize SHALL be deterministic for a supplied seed and SHALL produce only known feature
   IDs and values within valid bounds.
10. Library-row selection SHALL open a character for editing but SHALL NOT activate it;
    activation SHALL use a separate `Use Character` action.

### FR-022 — Runtime appearance and expressions

1. WHEN a character is activated THEN only the existing buddy's appearance SHALL change.
2. Appearance activation SHALL NOT recreate or alter buddy bodies, collision, mass, rig links,
   drives, position, velocity, activities, mood, pain, damage, payout, economy, or tool state.
3. Activation SHALL be prepared outside the physics tick and applied at the owning scene
   root's next fixed-tick boundary.
4. Semantic reaction strings and their existing priority SHALL remain authoritative.
5. Every shipped eye, brow, and mouth renderer SHALL support every existing semantic pose.
6. Face expressions SHALL remain above any future paint and SHALL not be suppressible.
7. The existing head-front face quad SHALL remain the Phase A face mount; a trusted
   torso-front decal quad SHALL carry the single accent.
8. Equal compiled appearance/render keys SHALL not cause redundant material mutation or repaint.
9. Headless execution SHALL preserve semantic render keys and counters without requiring GPU pixels.

### FR-023 — Character library and persistence

1. Character documents SHALL live under `user://characters/<guid>/character.json` with one
   rolling backup and atomic temp-flush-replace behavior.
2. Character files SHALL remain local and SHALL be excluded from Steam Cloud.
3. The library SHALL have no hard count cap and SHALL load only bounded row metadata for
   visible/paged entries.
4. Startup SHALL fully load and compile only the selected valid character.
5. A corrupt primary SHALL be quarantined before backup recovery; an unsupported future
   version SHALL not be quarantined.
6. A missing, invalid, corrupt, or unsupported selected-character target SHALL render the
   built-in appearance while preserving the stored unknown selection value.
7. Deleting the active character SHALL first apply the built-in appearance at a safe boundary,
   then clear active selection, request an immediate progress save, and delete the directory.
8. Reset Progress SHALL clear active character selection to built-in because it constructs
   first-run progress, but SHALL NOT delete local character files.

### FR-024 — Editor mode and window behavior

1. WHEN the editor opens THEN gameplay SHALL pause while the application, editor UI, and
   rendering continue.
2. Editor mode SHALL NOT reuse hidden-to-tray lifecycle accounting or rendering suspension.
3. Editor time SHALL count as foreground inactive time, not hidden time.
4. The same native window SHALL be temporarily changed to an opaque 960×720 editor workspace,
   clamped to a usable monitor area.
5. Entry SHALL capture window rect, transparency state, borderless state, topmost state,
   input mode, MSAA, and VSync.
6. Exit SHALL restore every captured field, recover against current monitor topology, and
   queue exactly one sandbox-boundary resize for the restored client size.
7. Editor-generated resize events SHALL not resize the gameplay sandbox.
8. Escape while dirty SHALL invoke dirty-close handling rather than independently toggling
   Work/Play mode.
9. Unexpected editor teardown SHALL restore the captured shell state.
10. The editor SHALL be free from launch, with no purchase, unlock, credit cost, or Phase A
    achievement.

### FR-025 — Settings/dock integration boundary

The M5 closeout left the retractable dock/settings surface as separate scheduled work.
Phase A SHALL NOT create a second production settings architecture merely to expose the editor.

- A1–A7 may proceed without the dock.
- A8 may build and test the editor scene/session through the journey/test composition root.
- The release-visible editor entry SHALL bind to the approved settings/panel surface that exists
  when A8 integrates. If the dock is still absent, final Phase A promotion waits for that entry
  seam or an owner-approved replacement; a debug hotkey is not a launch substitute.
- Reset Progress remains owned by the dock settings surface and its existing armed tray seam;
  the character editor SHALL not relocate or duplicate reset behavior.

## 3. Architecture supplement

### 3.1 Locked data flow

```text
character.json
  -> CharacterDocumentPolicy.DecodeAndMigrate
  -> CharacterDocumentNormalizer.Normalize
  -> CharacterDocumentValidator.Validate
  -> CharacterCompiler.Compile
  -> immutable CompiledCharacterAppearance
  -> prepared CharacterAppearanceApplyRequest
  -> next SandboxRoot fixed tick
  -> BuddyVisualRigView.ApplyAppearance
```

Runtime gameplay presentation:

```text
BuddyRoot + live reactions/activities/look-at + transform source
  -> BuddyVisualPresenter
  -> BuddyVisualPoseFrame + FaceRenderState
  -> BuddyVisualRigView
```

Editor preview:

```text
CharacterEditorSession working copy
  -> CharacterCompiler
  -> CharacterPreviewController
  -> fixed rest pose + selected semantic expression
  -> the same BuddyVisualRigView
```

The preview composition contains no `BuddyRoot`, `RigidBody2D`, damage/economy service,
activity selector, or live reaction component.

### 3.2 Ownership boundaries

| Owner | Responsibility | Forbidden responsibility |
| --- | --- | --- |
| `domain/.../Characters` | Documents, stable IDs, normalization, validation, migration, compiler | Godot nodes/Resources, files, physics, UI |
| `CompiledCharacterAppearance` | Six opaque colors and four compiled visual features | Geometry, tuning, paths, mutable collections, Godot types |
| `BuddyVisualProfile` | Trusted built-in geometry and presentation tuning | User-authored character data |
| `BuddyVisualRigView` | Mesh/material/socket/decal ownership; pose and appearance application | Gameplay sampling, persistence, selection |
| `BuddyVisualPresenter` | Sampling live gameplay presentation state and producing pose frames | Character files/editor session |
| compositors/renderers | Stable-ID feature drawing and render-key invalidation | Reactions, physics, files, economy |
| `CharacterStore` | Character atomic save/load/backup/quarantine/lazy index | Progress mutation, rendering, Workshop |
| `CharacterSelectionService` | Prepare/compile, queue fixed-boundary appearance, progress selection transaction | File I/O during physics tick |
| `EditorModeCoordinator` | Pause reason and shell transition/restoration | Character document rules |
| `CharacterEditorSession` | Working copy, baseline, dirty state, typed commands | Direct runtime/physics mutation |

### 3.3 M5 persistence baseline

M5 completed with progress schema 6 and an in-place reset transaction:

- `ProgressReset` creates first-run state and calls `BuddyProgressState.Adopt`;
- holders of `BuddyProgressState` are not rebound;
- failed reset persistence restores the prior snapshot;
- settings remain a separate untouched payload.

A6 SHALL build on this design:

1. Bump from the schema version present when A6 begins (currently 6); never hard-code an
   assumption if another accepted migration lands first.
2. Add nullable/extension-safe active-character selection to `ProgressSave`, snapshots,
   migration, and `BuddyProgressState.Apply/Adopt` paths.
3. The first-run factory and Reset Progress SHALL produce built-in selection (`null`).
4. Reset SHALL preserve the character library directory while clearing only active selection.
5. Add `ProgressChange.CharacterSelected`; `SaveCoordinator` requests immediate durable flush.
6. Selection save failure leaves the selected appearance active and progress dirty for retry;
   it is not a character-file corruption.

### 3.4 Rendering topology

- Six base colors tint existing per-part lit material instances.
- One transparent 200×200 `SubViewport` feeds the existing head-front face quad.
- One transparent 256×256 `SubViewport` feeds a trusted torso-front accent quad.
- Both use `RenderTargetUpdateMode.Once` and direct `ViewportTexture` binding.
- Phase A performs no GPU readback, PNG encoding, or per-part `ImageTexture` baking.
- The current scorch amount is reapplied from the active custom base color; fading scorch to
  zero restores that custom base.
- Semantic state and compiled appearance form immutable value render keys; equality suppresses work.

### 3.5 Pause and window lifecycle

Introduce a single pause coordinator with explicit reasons including HiddenToTray,
OperatingSystemSuspend, Laboratory, and CharacterEditor. Existing paths stop independently
writing `SceneTree.Paused` after this coordinator lands. CharacterEditor keeps the window
visible and render loop enabled. Editor/coordinator nodes use `ProcessMode.Always`.

Window state capture/restore includes rect, requested/active transparency, borderless,
always-on-top, input mode, MSAA, and VSync. Monitor recovery uses the existing M2 policy;
there is no second placement algorithm.

## 4. Verification supplement

All new IDs below must be registered in the repository's scenario/journey catalogues and
run under the same timeout/artifact discipline as existing tests.

### 4.1 Domain/unit coverage

- schema roundtrip, canonical GUID/color formatting, bounds, name validation;
- non-finite rejection and sequential migration behavior;
- unknown feature-ID preservation plus runtime default resolution warning;
- compiled-appearance forbidden-type architecture test;
- catalog/renderer exact-ID equality and pose coverage;
- character-store atomic save, backup recovery, quarantine, future-version handling,
  traversal/reparse rejection, deterministic paging;
- active-selection migration, fixed-boundary/idempotent transaction, immediate-save request;
- deterministic randomization and editor working-copy state machine.

### 4.2 Required headless scenarios

- `editor_document_roundtrip`
- `editor_unknown_feature_preserved`
- `editor_invalid_primary_backup_recovery`
- `editor_invalid_quarantine`
- `editor_future_schema_fallback`
- `expression_renderer_coverage`
- `character_appearance_invalidation`
- `character_swap_physics_invariant`
- `character_selection_immediate_save`
- `character_selection_save_failure_dirty`
- `character_active_delete_reverts`
- `editor_preview_has_no_physics`
- `editor_mode_lifecycle_accounting`
- `editor_window_restore`
- `editor_window_monitor_removed`
- `editor_resize_boundary_isolation`
- `library_large_enumeration`

Required quantitative assertions include:

- 500 character directories: one full active load/compile, zero eager thumbnails, bounded
  metadata work for the requested page;
- equal appearance/render key: zero additional mutations/renders;
- each relevant changed field: exactly one affected mutation/render;
- queued selection applies only at a fixed tick and last request wins;
- identical seeded strike before/after appearance swap preserves accepted pain, velocity
  envelope, payout, collision, rig, and drive Resources;
- all captured window state restores, editor resize sends zero gameplay boundary requests,
  and exit sends exactly one restored-size request.

### 4.3 Required real-input journey

Journey ID: `character_editor_create_use_and_react`.

1. Open the approved settings/panel route through real input.
2. Enter the character editor.
3. Create a new character.
4. Set a name, part colors, feature variants, offsets/scales/colors.
5. Randomize with a fixed seed.
6. Save and assert the dirty baseline clears.
7. Invoke `Use Character` and assert application occurs at a fixed tick.
8. Exit and assert all shell/window state restores.
9. Strike the buddy through the real tool input path.
10. Assert the active appearance remains and the semantic reaction renders.
11. Restart and assert active selection persists.

Run seeds 1 and 7 in the shipping Mii3D presentation. Run relevant gameplay-invariant
substeps in LegacyCircles where the existing dual-presentation policy still applies; the editor
itself is a Mii3D feature and need not expose a Legacy editor preview.

### 4.4 Manual Windows matrix

- Windows 10 and Windows 11 standalone exports;
- 100%, 125%, 150%, and 200% DPI;
- single and multiple monitors;
- disconnect/remove the entry monitor while editor is open;
- transparency-supported and opaque-fallback configurations;
- minimum supported desktop usable area;
- editor focus, input capture, readability, restoration, and global recovery;
- default-document visual parity and reaction readability.

### 4.5 Performance and regression gates

- no JSON/file work or async wait on a physics tick;
- no live buddy physics-rig reconstruction on editor enter/exit or character swap;
- no allocation/repaint for equal keys;
- startup loads/compiles only the selected character;
- library UI cost scales with visible rows, not full document/render count;
- the existing 120 Hz zero-allocation simulation budget remains unchanged;
- existing M3.5, M3.6, M4, and M5 suites remain green.

## 5. Agent verification workflow supplement

Before changing code, an A1+ agent must:

1. read `docs/DECISIONS.md`;
2. read this supplement;
3. read `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` through the assigned task and dependencies;
4. inspect the current implementation seams named by that task;
5. capture current `dotnet build`, `dotnet test`, and `devtools\verification\quick_validate.bat` verdicts.

Each task handoff must report:

- task ID and prerequisites confirmed;
- exact files changed;
- public contracts introduced/changed;
- invariants and non-goals preserved;
- migrations/failure behavior where applicable;
- commands, seeds, presentations, verdicts, and artifact paths;
- interactive MCP evidence for player-visible behavior;
- remaining external gate and next task.

Interactive verification never replaces automated coverage. Player-visible editor behavior must
first be exercised through the configured Godot MCP server using real input and semantic state,
then promoted into the committed journey/scenario suite.

## 6. Roadmap and dependency resolution

Milestone 5.5 order is A1 through A9 from `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`.
A0 is complete by this document and the corresponding `AGENTS.md` update.

Task dependencies remain those in the handoff plan. Additionally:

- A6 must target the post-M5 schema/reset architecture described in §3.3 above.
- A8's production entry is integration-gated by the approved settings/panel surface in §2 FR-025.
- A9 cannot pass the release-visible journey until that entry exists.
- Phase B and Phase C remain unscheduled and cannot be partially implemented.

## 7. Conflict audit result

The following apparent contradictions are resolved:

- “custom buddies/cosmetics are deferred” now means painting, Workshop, arbitrary packages,
  progression cosmetics, multiple buddies, and physics customization; Phase A's bounded local
  visual editor is authorized.
- `BuddyVisualProfile` remains trusted geometry/tuning and is not the compiler output.
- preview reuses `BuddyVisualRigView`, not a fake/live `BuddyRoot`.
- editor pause is not hidden-to-tray.
- the face remains on the existing front quad; this is not deferred to an exit gate.
- feature persistence uses stable IDs, not atlas indices.
- the semantic expression map remains variant-independent.
- active character persistence extends schema 6 and `Adopt`, rather than replacing M5 reset design.
- Reset Progress clears selection but not local character files.
- no release-visible duplicate settings UI is introduced while the dock remains separate work.

A new agent may begin **Task A1** without requesting another scope decision.
