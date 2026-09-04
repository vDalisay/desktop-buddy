# Milestone 5.6 Character Painting — Authoritative Source Alignment

> **Historical scope note:** This document governs Phase B painting. Its Phase C Workshop deferral statements are superseded by `docs/M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md`; Workshop v1 is included in the Steam Demo and full Steam release, and excluded from itch.io.

Status: **Phase B Task B0 complete — implementation may begin at B1.**  
Owner authorization date: **2026-08-03**.  
Effective baseline: `main` after Character Editor Phase A and the merged Work/Play redesign.  
Historical umbrella plan: `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`.

This document is the normative Phase B supplement to:

- `docs/PRODUCT_REQUIREMENTS.md`;
- `docs/ARCHITECTURE.md`;
- `docs/TEST_PLAN.md`;
- `docs/AGENT_VERIFICATION_AND_E2E.md`;
- `docs/ROADMAP.md`;
- the short deferred Phase B summary in `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`.

For Phase B painting, this supplement resolves conflicts with older documents that still
classify freehand painting as deferred. `docs/DECISIONS.md` remains the general owner-decision
log; the newer 2026-08-03 owner authorization and interaction decisions recorded here
supersede only earlier Phase B deferral wording. Phase C Steam Workshop remains deferred.

## 1. Scheduling and scope authorization

Character Editor Phase A is complete and merged. Phase B is scheduled now as **Milestone
5.6**, before Milestone 6. It has no Steam or Workshop dependency.

Phase B authorizes only:

- freehand color painting on the six existing trusted buddy body parts;
- a dedicated painting workspace inside the existing character editor;
- CPU-owned per-part RGBA paint surfaces;
- a color wheel;
- one circular paint brush with adjustable size;
- brush-size adjustment through both the mouse wheel and visible decrement/increment buttons;
- an eraser using the same size controls;
- one-step-at-a-time stroke undo through an Undo button;
- an Erase All command that clears all six painted surfaces after confirmation and can itself
  be undone;
- a zoomed-in fixed frontal view of the current buddy working copy;
- panning when the zoomed buddy does not fit in the visible painting canvas;
- paint persistence in the existing local character directory;
- binding paint beneath the existing face and torso-accent decals;
- automated, headless, real-input, persistence, performance, and Windows acceptance gates.

Phase B does **not** authorize:

- Steam Workshop, package import/export, subscriptions, publishing, moderation, or arbitrary
  downloaded content;
- gameplay, physics, collision, mass, rig, connector, damage, mood, economy, tool, or behavior
  customization;
- material sliders such as metallic or roughness;
- eyedropper sampling from the desktop or game world;
- pattern, texture, gradient, spray, smudge, stamp, fill-bucket, or symmetry tools;
- layers, blend modes, opacity controls, custom brush files, pressure sensitivity, or tablet
  integration;
- 3D orbit painting, back-side painting, or arbitrary camera rotation in the first Phase B
  release;
- copied MECCHA CHAMELEON code, assets, shaders, UI layout, button arrangement, text,
  branding, sound, animation, or distinctive visual presentation;
- cosmetic prices, unlocks, achievements, or progression.

## 2. Clean-room reference intent

The ideal interaction target is a **simplified, original, clean-room behavioral analogue** of
MECCHA CHAMELEON's body-painting flow: the player sees a character as a paintable surface,
chooses a color, changes brush size, and paints directly on the body. The reference is used
only to identify broad interaction principles.

Public reference evidence used for B0:

- The official Steam page describes painting a white body to mimic the stage:
  `https://store.steampowered.com/app/4704690/MECCHA_CHAMELEON/`.
- Official Steam Community update 2.5.0 records paint-brush resolution improvements and an
  experimental color palette:
  `https://steamcommunity.com/app/4704690/`.
- Public Steam Community discussion documents brush-size adjustment through a mouse gesture
  and repeated player requests for an explicit slider or buttons:
  `https://steamcommunity.com/app/4704690/discussions/0/571541224066118931/`.

Desktop Buddy deliberately improves discoverability rather than copying that control scheme:
mouse-wheel sizing and visible size buttons are both required. Reference observations belong
in documentation only. No reverse engineering, decompilation, asset extraction, shader
copying, screenshot tracing, or source copying is permitted.

## 3. Product requirements supplement

### US-11 — Paint my buddy

As a player, I want to paint directly on my buddy's body in a focused editor so I can create
an original appearance without changing how the buddy behaves.

### FR-026 — Painting workspace

1. Painting SHALL be entered from the existing Character Editor and SHALL edit its current
   working copy.
2. If the active appearance is the immutable built-in buddy, entering painting SHALL create
   or use a new unsaved character working copy initialized from the built-in appearance; it
   SHALL never mutate built-in content.
3. The painting workspace SHALL use the same editor pause/window ownership as Phase A and
   SHALL contain no live `BuddyRoot`, `RigidBody2D`, damage authority, economy authority, or
   gameplay clock.
4. The preview SHALL show a stable, zoomed-in, dead-frontal rest pose of the current working
   copy.
5. The first Phase B release SHALL not rotate or orbit the buddy. The front view is locked so
   brush targeting and UV behavior remain predictable.
6. The player SHALL be able to zoom through visible zoom controls. `Ctrl+mouse-wheel` MAY
   mirror those controls.
7. The player SHALL be able to pan the canvas using middle-mouse drag and `Space+left-drag`.
   Panning SHALL clamp so the buddy cannot be lost permanently off-canvas.
8. A Reset View action SHALL restore the default frontal framing, zoom, and pan.
9. Painting SHALL target whichever trusted body-part surface is under the brush. The UI SHALL
   expose the hovered/active part name for feedback but SHALL not require selecting a part
   before every stroke.
10. Face and torso-accent decals SHALL remain visible above paint at all times.

### FR-027 — Brush, color, eraser, and destructive commands

1. The primary color control SHALL be a hue/saturation color wheel with a value/brightness
   control and a visible current-color swatch.
2. Paint colors SHALL be opaque RGB. Phase B SHALL not expose paint opacity.
3. Left-button drag on a valid body surface SHALL paint one continuous stroke using the
   current color and brush size.
4. The brush SHALL be one original circular brush. Brush hardness is fixed by trusted tuning
   for the initial release and is not a player control.
5. Mouse-wheel movement over the painting canvas SHALL adjust brush size in bounded steps.
6. Visible `−` and `+` controls SHALL adjust the same brush-size value for discoverability and
   accessibility. The current size SHALL be displayed numerically.
7. Brush-size changes SHALL not zoom the camera unless the explicit zoom modifier/control is
   used.
8. Eraser mode SHALL remove paint alpha using the same cursor, interpolation, size bounds,
   and spacing as the paint brush.
9. Undo SHALL revert exactly the most recently completed paint, erase, or Erase All command.
10. Undo SHALL be disabled when history is empty. Redo is not required in the first Phase B
    release.
11. Erase All SHALL require confirmation, clear all six painted-part surfaces, create one
    undoable command, and leave base colors/features untouched.
12. Escaping, changing character, closing, or discarding SHALL use the existing Phase A dirty
    working-copy flow: Save / Discard / Continue Editing.

### FR-028 — Paint application and runtime invariants

1. Paint SHALL be an appearance underlay only. It SHALL not alter trusted geometry,
   collision, mass, rig links, depth lanes, drives, forces, reactions, activities, damage,
   economy, or tools.
2. Each body part SHALL own one optional 512×512 RGBA8 paint surface.
3. Blank pixels SHALL be transparent and reveal the character's selected base part color.
4. The existing `BuddyVisualRigView.SetSurfaceUnderlay(part, texture)` seam SHALL be the only
   runtime paint binding boundary.
5. The runtime and editor preview SHALL consume the same persisted paint pixels and trusted
   UV conventions.
6. Paint updates while stroking SHALL be coalesced to at most one GPU texture upload per
   rendered frame per dirty part.
7. Equal images/revisions SHALL not upload or mutate materials redundantly.
8. Scorch and other trusted runtime effects SHALL continue to compose according to the
   accepted appearance architecture and SHALL not rewrite saved paint pixels.
9. Character activation SHALL remain fixed-tick and visual-only; paint loading and PNG decode
   SHALL occur before the prepared appearance request reaches the physics boundary.

### FR-029 — Paint persistence and failure behavior

1. Character documents SHALL bump schema sequentially and add only declared painted-part file
   references. Stroke history SHALL not be persisted.
2. Painted PNGs SHALL live below the character GUID directory using a fixed whitelist:

   ```text
   paint/head.png
   paint/torso.png
   paint/left_hand.png
   paint/right_hand.png
   paint/left_foot.png
   paint/right_foot.png
   ```

3. Missing paint references/files SHALL mean an empty transparent surface for that part.
4. Older Phase A documents SHALL migrate deterministically to an empty paint set.
5. Each decoded image SHALL be exactly 512×512 RGBA8. Unexpected dimensions, formats,
   paths, traversal, links, excessive encoded bytes, or excessive decoded bytes SHALL be
   rejected without executing or importing arbitrary content.
6. Saving SHALL stage the JSON and changed PNGs, validate all staged outputs, then commit the
   character transaction without exposing a JSON file that references missing new paint.
7. A failed save SHALL preserve the previous valid character, working paint pixels, dirty
   state, and undo history.
8. Corrupt paint attached to a character SHALL produce a visible recoverable load failure or
   backup recovery according to the character-store policy; GPU absence SHALL never be
   classified as paint corruption.
9. Character paint remains local-only and excluded from Steam Cloud under the same Phase A
   character-directory policy.

## 4. Locked interaction model

### 4.1 Pointer ownership

```text
Left drag on body       -> paint or erase stroke
Mouse wheel             -> brush size
Minus / plus buttons    -> brush size
Middle drag             -> pan
Space + left drag       -> pan
Ctrl + mouse wheel      -> zoom (optional shortcut; visible zoom buttons remain required)
Undo button / Ctrl+Z    -> undo last completed command
Escape                  -> existing dirty-close/back behavior
```

A stroke starts only after the pointer hits a valid paintable part. Crossing from one part to
another closes the current part segment and continues the same user gesture as one undoable
multi-part command. Movement through empty canvas creates no pixels and does not bridge a
stroke across unrelated surfaces.

### 4.2 View and framing

- The preview camera is orthographic and locked dead-frontal.
- Default framing fits the whole buddy with a small margin.
- The player may zoom in until a small body region fills the canvas.
- Pan is enabled at every zoom but clamps against a recoverable framing envelope.
- A visible Reset View control is always available.
- The buddy uses a deterministic paint pose with hands/feet separated enough to make all six
  trusted surfaces reachable from the front.
- The paint pose is presentation-only and does not alter the gameplay rig.

### 4.3 Color and tool state

The editor session owns:

```text
SelectedTool: Brush | Eraser
SelectedColor: opaque Rgba32
BrushDiameterPixels: bounded integer
Zoom
Pan
HoveredPart
UndoAvailability
DirtyPaintParts
```

Controls are views over that state. They do not mutate images or character files directly.

## 5. Paint data, memory, and file budgets

### 5.1 Surface resolution

The Phase B resolution is reconfirmed as **512×512 RGBA8 per part**, six parts maximum.
This is sufficient for the current sphere/capsule screen scale while keeping CPU image,
upload, and persistence costs bounded.

Raw size:

```text
512 × 512 × 4 bytes = 1 MiB per part
6 parts             = 6 MiB active paint pixels
```

### 5.2 Editor memory budget

- Active working paint images: maximum 6 MiB.
- Last-saved/discard baseline: maximum 6 MiB.
- Undo history: maximum 48 MiB of compressed or dirty-rectangle before-data.
- Total CPU paint-editing budget: maximum 64 MiB, excluding ordinary node/UI overhead.
- Active GPU underlay textures: maximum 8 MiB including reasonable texture-object overhead.

Undo history SHALL store bounded affected rectangles or compressed snapshots, not six full
surfaces per ordinary stroke. When adding a command would exceed the history budget, discard
the oldest complete commands until the new command fits. Never retain a partial command.
Erase All may retain one complete six-surface before-state and remains within the same cap.

### 5.3 Encoded file budget and future packages

- Exactly six whitelisted PNGs maximum.
- Maximum encoded size: 2 MiB per PNG.
- Maximum aggregate encoded paint payload: 12 MiB per character.
- Character JSON and other existing local files remain outside that 12 MiB paint subtotal.

Phase C package design remains deferred, but it must not later choose limits smaller than a
valid Phase B character. The historical Phase C placeholder of 1 MiB per texture / 8 MiB per
package is therefore not binding after this B0 decision. C0 must reconfirm a package cap of at
least 16 MiB or define an encoding/packaging strategy that accepts every valid local Phase B
character without destructive recompression.

## 6. Architecture supplement

### 6.1 Data flow

```text
Pointer sample in paint viewport
  -> trusted frontal ray hit against trusted part mesh
  -> analytic part-local UV
  -> PaintStrokeBuilder
  -> CPU Image mutation + dirty rectangle
  -> coalesced ImageTexture update
  -> BuddyVisualRigView.SetSurfaceUnderlay(part, texture)
```

Save flow:

```text
CharacterEditorSession working copy
  -> normalized paint manifest
  -> staged PNG encoding for changed parts
  -> staged character.json
  -> validate dimensions, paths, byte caps, and references
  -> atomic character-directory transaction
  -> advance saved baseline and clear dirty/undo state as specified
```

Runtime load flow:

```text
character.json
  -> document migration/validation
  -> whitelisted PNG validation/decode off fixed tick
  -> immutable prepared paint payload/revisions
  -> fixed-tick appearance request
  -> main-thread ImageTexture creation/binding
```

### 6.2 Ownership

| Owner | Responsibility | Forbidden responsibility |
| --- | --- | --- |
| `PaintDocument`/manifest domain types | Stable part IDs, declared paths, limits, migration | Godot images, nodes, files, gameplay |
| frontal UV mapper | Trusted primitive hit and UV math | File I/O, editor controls, physics mutation |
| CPU paint surface | Pixels, stroke interpolation, eraser, dirty rectangles | Godot materials, persistence |
| undo history | Completed command before-data under memory cap | Persisted history, UI ownership |
| paint editor session/controller | Tool/color/size/view state, command routing, dirty state | Direct runtime physics mutation |
| paint texture bridge | Coalesced main-thread `ImageTexture` updates | Authoritative pixels, document rules |
| `CharacterStore` paint transaction | Staging, validation, atomic JSON/PNG commit, recovery | Rendering, Workshop, gameplay |
| `BuddyVisualRigView` | Trusted underlay binding below decals | Stroke logic, file paths, untrusted geometry |

### 6.3 Threading and clocks

- Pointer sampling and editor command routing occur on the Godot main thread.
- CPU pixel work may use a focused worker only when no Godot object is touched and ordering is
  preserved; the first implementation may remain main-thread if it meets the frame budget.
- `ImageTexture` creation/update and rig binding occur on the main thread.
- PNG encode/decode and file I/O occur outside the fixed physics tick.
- The painting workspace uses editor pause ownership; gameplay remains paused and no paint
  action advances gameplay time.

## 7. Task order after B0

### Task B1 — Frontal hit mapping and paint-view camera

- Implement analytic trusted ray-to-UV mapping for the existing sphere/capsule primitives.
- Lock the deterministic frontal paint pose, orthographic camera, zoom, pan, clamps, Reset
  View, and part-hover diagnostics.
- Verify center, silhouette, seam, miss, mirrored limb, and high-zoom precision cases against
  the real Godot meshes once, then keep engine-free tests authoritative.

### Task B2 — CPU surfaces, brush, eraser, and bounded undo

- Add six optional 512×512 RGBA8 surfaces.
- Implement the one circular brush, fixed hardness, spacing/interpolation, opaque color,
  wheel/button size adjustment, eraser, dirty rectangles, multi-part stroke commands,
  Undo, and undoable Erase All.
- Enforce the 64 MiB CPU editing budget and deterministic image hashes.

### Task B3 — Preview underlay and render invalidation

- Bind CPU images through `SetSurfaceUnderlay` beneath face/accent decals.
- Coalesce dirty uploads to at most one per rendered frame per part.
- Preserve headless revisions/hashes without requiring GPU pixels.

### Task B4 — Production painting UI

- Integrate a Paint mode into the existing Character Editor working-copy flow.
- Add color wheel/value control, Brush/Eraser controls, brush-size display and buttons, Undo,
  Erase All confirmation, zoom controls, Reset View, canvas, hover feedback, and translated
  help prompts.
- Verify keyboard/mouse focus and dirty-close behavior through real input.

### Task B5 — Schema migration and atomic PNG persistence

- Bump the character schema sequentially.
- Add only whitelisted painted-part references.
- Implement staged atomic JSON/PNG saves, backup recovery, size/dimension/path rejection, old
  document migration, and local-only library behavior.

### Task B6 — Exit verification and owner feel gate

- Complete unit, headless, journey, performance, save-failure, layer-order, and Windows
  matrices.
- Run the painting workspace with real mouse input at supported DPI scales.
- Owner accepts brush feel, framing/panning, undo/erase behavior, and editor-to-runtime pixel
  fidelity.

No B2–B6 production implementation may be folded into B1. Preserve one reviewable task
boundary per numbered task.

## 8. Verification supplement

### 8.1 Required domain/unit coverage

- Primitive hit-to-UV mapping for every part type and seam boundary.
- Brush bounds, spacing, interpolation, clipping, and deterministic pixel hashes.
- Eraser alpha behavior.
- One gesture crossing multiple parts remains one undoable command.
- Empty-canvas movement never bridges paint.
- Undo restores byte-identical prior pixels.
- Erase All clears all six and Undo restores all six byte-identically.
- History eviction respects the 48 MiB cap and removes only complete oldest commands.
- Encoded/decoded limits and strict path whitelist.
- Sequential migration from Phase A documents to empty paint references.
- Paint domain/editor assemblies do not reference gameplay physics/economy namespaces.

### 8.2 Required headless scenarios

- `paint_frontal_uv_mapping`
- `paint_stroke_and_eraser`
- `paint_multi_part_stroke_undo`
- `paint_erase_all_undo`
- `paint_memory_budget`
- `paint_under_expression_layer_order`
- `paint_persistence_roundtrip`
- `paint_invalid_png_rejected`
- `paint_save_failure_preserves_working_copy`
- `paint_preview_has_no_physics`
- `paint_runtime_fidelity`

Headless tests use CPU image bytes, hashes, revisions, declared layer order, and dependency
scans. They do not depend on screenshot OCR or GPU readback.

### 8.3 Required real-input journey

`character_paint_save_use_restart`

1. Open the Character Editor through the production UI.
2. Open or create a character working copy.
3. Enter Paint mode.
4. Choose a color on the wheel.
5. Increase and decrease brush size using the mouse wheel and visible buttons.
6. Paint at least two body parts.
7. Pan and zoom, then Reset View.
8. Switch to Eraser and remove part of a stroke.
9. Undo the erase.
10. Invoke Erase All, confirm, then Undo it.
11. Save and Use Character.
12. Exit the editor and verify paint appears on the live buddy beneath expressions/accents.
13. Restart and verify pixel-identical persisted paint and active selection.

### 8.4 Manual Windows matrix

- Windows 10 and Windows 11 standalone builds.
- 100%, 125%, 150%, and 200% DPI.
- Minimum supported editor area and common 16:9/ultrawide desktops.
- Single monitor and mixed-DPI multi-monitor.
- Mouse-wheel size adjustment does not scroll another panel or zoom unintentionally.
- Middle-drag and Space+drag pan do not paint.
- High-zoom strokes remain under the cursor without seam jumps.
- Toolbar/editor focus, Escape, dirty dialog, and Work/Play restoration remain recoverable.
- Save failure and corrupt/oversize PNG messages remain actionable.
- Runtime pixels visually match editor pixels within the trusted lighting/material pipeline.

### 8.5 Performance acceptance

- No PNG, JSON, or file I/O on the fixed tick.
- No full-surface clone per ordinary stroke.
- At most one texture upload per dirty part per rendered frame.
- Idle Paint mode performs no image mutation or upload.
- CPU paint editing memory remains at or below 64 MiB.
- Active GPU paint surfaces remain at or below 8 MiB.
- Painting does not rebuild or replace the live physics rig.

## 9. B0 completion record

Task B0 is complete when this document and `AGENTS.md`/`ROADMAP.md` agree that:

- Phase A is complete;
- Phase B is scheduled as Milestone 5.6;
- Task B1 is the next executable task;
- the simplified clean-room MECCHA CHAMELEON-inspired interaction goal is explicit;
- resolution, brush UX, view/pan behavior, memory budget, persistence limits, and future
  package compatibility are locked;
- Workshop and arbitrary package work remain deferred;
- no Phase B production code was introduced by B0.

All conditions above are established by the 2026-08-03 B0 documentation commit.
