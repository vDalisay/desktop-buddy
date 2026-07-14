# Character Editor, Custom Painting, and Steam Workshop — Deferred Feature Plan

Status: detailed pre-planning written 2026-07-14 at owner request. This feature is a
**nice-to-have and remains deferred**: `docs/ROADMAP.md` "Deferred Roadmap" already lists
buddy painting/coloring, cosmetics, and Steam Workshop/custom buddy packages, and
`AGENTS.md` forbids prebuilding deferred features. **No implementation may begin until the
owner schedules this milestone and resolves the decisions in the last section into
`docs/DECISIONS.md`.** Dependencies: the M3.5 slice must be complete and accepted
(`docs/M3_5_3D_PRESENTATION_PLAN.md` — the `BuddyVisualProfile` seam and
`BuddyVisualPresenter` are this plan's rendering substrate); Phase C additionally requires
the Milestone 6 Steam adapter.

**Prime invariants, every phase:** customization is visual-only forever (M3.5 decision) —
no schema field, package, or editor control may reference rig, drive, mass, collision, or
any physics tuning. Exactly one active buddy (ARCHITECTURE §18): the library selects which
single visual identity is active; nothing may assume multiple simultaneous buddies.
Workshop packages are data-only: JSON and PNG, never scenes, scripts, DLLs, or arbitrary
Resources (§18).

## Design intent

A Mii-editor-inspired creator for the original robot buddy: parametric features and
colors, freehand painting on faces and bodies, a local character library, and sharing
through Steam Workshop plus a file-based fallback. Three independently shippable phases:

- **Phase A — Parametric editor**: character document model, feature compositing,
  expression mapping, library persistence, editor UI.
- **Phase B — Painting**: per-part freehand paint layered into the Phase A compositor.
- **Phase C — Workshop**: package format, upload/subscribe, validation/quarantine,
  moderation policy. Requires M6.

## Design seams

| Worker | Home | Responsibility |
| --- | --- | --- |
| Character document | `Domain/Characters` | Versioned pure-data model (GUID, name, parameters), bounds clamping, validation, sequential migrations — Godot-free, xUnit-tested. |
| Profile compiler | `src/Buddy/Presentation3D` | Pure function compiling a character document into the `BuddyVisualProfile`-shaped runtime data the presenter consumes (the M3.5 seam). |
| Face expression map | `src/Buddy/Presentation3D` | Maps the existing reaction face-state strings (`":|"`, `">_<"`, `"x_x"`, …) to parametric feature poses; the strings stay the semantic contract so no reaction code changes. |
| Part compositor | `src/Buddy/Presentation3D` | Renders base color + paint layer + expression features into per-part `ImageTexture`s via an offscreen `SubViewport`, re-rendered on change only, never per frame. |
| Paint surface + ray→UV | `src/Editor/Paint` (math in `Domain/Characters`) | Analytic frontal-ray → sphere/capsule UV inverse (Domain-testable), brush stamping into `Image`, stroke undo/redo. |
| Character library | `src/Persistence/Characters` | `user://characters/<guid>/` documents + textures with the §12 atomic write/backup/quarantine discipline; active-character GUID in `progress.json`. |
| Editor UI | `src/UI/Editor` + `scenes/editor` | Screens, parameter controls, paint tools, live preview, randomize, library management; all text via translation keys. |
| Workshop service | `src/Platform` + `DesktopBuddy.Steam` | `IWorkshopService` seam with a fully functional emulated local implementation; Steamworks.NET UGC behind it. |
| Package validator | `Domain/Characters/Packages` | Data-only package schema, size caps, checksums, version gates; quarantine decisions. |

## Global constraints (all phases)

1. **Visual-only enforcement is structural**, not reviewed-in: the compiler's output type
   is the visual-profile surface and nothing else, so a malicious or buggy document
   physically cannot reach physics.
2. **Data-only packages with hard caps**: manifest ≤ 64 KB; textures PNG, ≤ 512×512,
   ≤ 1 MB each; whole package ≤ 6 MB. Violations reject at validation, never at render.
3. **Safe-boundary swaps**: activating a character applies at the scene root's next fixed
   tick (the queued-request pattern `BoundaryController` already uses), never mid-tick.
   Swap is a pure view change; a scenario witnesses accepted-pain equality across a swap.
4. **Expressions stay readable**: reaction/knockout expressions composite *above* the
   paint layer so gameplay states (`"x_x"`, `">_<"`) can never be painted over
   (pending owner decision 3).
5. **Editor mode is not gameplay**: entering the editor pauses the sandbox (the
   hidden-to-tray suspension path); the §23 zero-allocation rule applies to simulation
   ticks only, but the compositor still re-renders on change, not per frame.
6. **Persistence discipline**: character files follow the §12 atomic
   temp-flush-replace/backup/quarantine pattern; unknown active-character GUIDs fall back
   to the built-in buddy while preserving the stored value (§12 unknown-ID rule).
   Character files are excluded from Steam Cloud (owner decision 4); `progress.json`
   remains the only Cloud file (§13).
7. **Clean-room + UGC boundary**: everything *shipped* (feature sprites, defaults, UI) is
   original; what *users* draw is user content governed by the Phase C policy and
   Steam's UGC terms, not by the clean-room audit.
8. **Non-Steam builds keep full value**: the editor, library, painting, and file-based
   import/export (`.buddychar` zip = the same package format) work with no Steam present;
   only Workshop browse/publish is Steam-gated.

## Phase A — Parametric editor

### Task A1 — Character document schema (Domain, headless-testable)
`CharacterDocument` in `Domain/Characters`: GUID id, display name, `schemaVersion`,
per-part colors, and a bounded feature list (per feature: type index into the shipped
atlas, position offset, scale, rotation, color — final axes are owner decision 1).
`Validate()` and `ClampToBounds()`; sequential migrations mirroring the save-DTO pattern.
xUnit: bounds and clamping, JSON roundtrip, migration N→N+1, unknown-major rejection,
name length/character limits.

### Task A2 — Feature atlas and part compositor (integration/presentation)
Original robot feature art (eyes, brows, mouths, and robot accents) drawn procedurally
in-engine (`CanvasItem` draw into the compositor viewport — consistent with the M3.5
procedural-asset decision; no external art pipeline). The compositor renders base color →
(Phase B paint slot) → features into a per-part `ImageTexture`, assigned as the Unshaded
material's albedo texture on the M3.5 meshes. Re-render triggers: document change,
expression change. The head texture replaces the M3.5 `Label3D` emoticon **for parametric
characters**; the built-in default buddy keeps its accepted `Label3D` face until the
Phase A exit gate accepts parametric parity, after which one pipeline remains.

### Task A3 — Face expression map (integration/presentation)
`FaceExpressionMap`: face-state string → feature pose (eye/mouth variants and offsets)
for every state the reaction component emits — consciousness `"x_x"`, pain `">_<"`, pet
and delight smiles, and the mood-band idle set. The reaction component and its priority
rules do not change; `BuddyReactionComponent` keeps writing strings and the presenter
resolves them. Scenario-checkable without pixels: the map lookup result and the
compositor's last-rendered state are exposed as semantic properties.

### Task A4 — Document→profile compiler (integration)
Pure function `CharacterCompiler.Compile(CharacterDocument) → compiled visual data`
consumed by `BuddyVisualPresenter` exactly where `BuddyVisualProfile` data flows today.
Compile failures (invalid document at load) follow §16: log, quarantine the file, fall
back to the built-in buddy, never crash.

### Task A5 — Character library persistence (integration)
`user://characters/<guid>/character.json` with the atomic write/backup discipline;
quarantine renames use the existing `.corrupt-<timestamp>` convention. Active-character
GUID persisted in `progress.json` (extension-safe). Library operations: create,
duplicate, rename, delete (soft confirm), select-active. Deletion of the active
character reverts to built-in.

### Task A6 — Editor UI (integration/presentation)
`scenes/editor/character_editor.tscn`: part selector, parameter controls, color pickers,
seeded randomize (injectable RNG per §23 — presentation stream), name entry, library
panel, and a live preview that reuses `BuddyVisualPresenter` fed **fixed rest-pose
transforms** — the preview runs no physics. Entry from the settings/panel surface;
sandbox paused while open; window sizing per owner decision 2. Every string is a
translation key.

### Task A7 — Phase A scenarios and journey (integration/testing)
Scenarios: `editor_document_roundtrip` (create → save → load → compile → applied at a
safe-boundary swap), `editor_invalid_quarantine` (corrupt file → quarantine + built-in
fallback + preserved GUID), `expression_map_coverage` (every reaction state resolves to a
pose and re-renders the compositor), `character_swap_physics_invariant` (strike before
and after a swap; accepted pain equal). Journey: create → randomize → save → select →
strike → correct expression state, through the real input path. MCP interactive pass per
`AGENTS.md` before promotion.

**Phase A exit gate (owner-manual):** editor usability and parametric-face parity accepted
on real Windows; default-buddy pipeline unification approved.

## Phase B — Painting

### Task B1 — Frontal ray→UV mapping (Domain, headless-testable)
Analytic inverse UV for the frontal orthographic view of the M3.5 primitives
(sphere/capsule), matching Godot 4.6.1 primitive UV conventions — verify the convention
empirically once against the real meshes, then lock it with tests. Mirror-X support.
xUnit: center, silhouette edge, capsule cap/side seams, off-surface miss, mirror
symmetry.

### Task B2 — Paint surface and stroke model (integration)
Per-part RGBA8 `Image` (512×512) with brush stamping (size, hardness, color, eraser).
Stroke-command undo/redo with memory-capped snapshots every N strokes.
`ImageTexture.Update` throttled to at most once per rendered frame while stroking. The
paint layer feeds the Task A2 compositor between base color and expression features.

### Task B3 — Paint mode UI (integration/presentation)
Painting happens directly on the frontal live preview: per-part target selection
follows the surface under the brush via Task B1; brush controls, clear-part with
confirm, mirror toggle. Painting over the face is allowed — expressions still composite
above (constraint 4).

### Task B4 — Paint persistence (integration)
PNGs per painted part inside the character directory; caps from constraint 2 enforced at
save and at load (oversize on load → quarantine). Document schema minor bump; migration
adds empty paint set to older documents.

### Task B5 — Phase B scenarios (integration/testing)
`paint_stroke_applies` (stroke changes the part texture hash), `paint_undo_restores`
(hash returns), `paint_roundtrip` (save/load pixel-identical), `paint_oversize_rejected`,
`expression_over_paint` (paint fully covering the face still shows the knockout state).

**Phase B exit gate (owner-manual):** brush feel and paint→game fidelity accepted.

## Phase C — Steam Workshop (requires M6)

### Task C1 — Package format and validator (Domain, headless-testable)
Versioned package: `manifest.json` (schema version, minimum app version, embedded
character document, per-file SHA-256 checksums, author metadata) plus texture PNGs.
Validator enforces constraint 2 caps, checksums, schema/app-version gates, and part
completeness. The same format is the local `.buddychar` import/export file. xUnit:
happy path, tampered checksum, oversize, future-major rejection, minimum-app-version
gate, checksum-of-missing-file.

### Task C2 — `IWorkshopService` seam (integration)
Interface in `src/Platform` with a fully functional emulated implementation (local
directory acting as the Workshop) used by development and CI. The Steam implementation
lives in `DesktopBuddy.Steam` on Steamworks.NET UGC: create item, submit update,
enumerate subscriptions, download state, and the Workshop legal-agreement status
surfaced to UI. Follows the §13 adapter rules: optional assembly, queued idempotent
operations, failures non-fatal.

### Task C3 — Publish flow (integration)
Publish/update the selected character: auto-rendered preview PNG from the editor
preview viewport, title/description fields, visibility choice, Valve legal-agreement
acceptance surfaced before first publish. Publish failures queue for retry like platform
stat operations; no partial packages ever upload (validate before submit).

### Task C4 — Subscribe/import flow (integration)
Enumerate subscribed items → download → C1 validation → import as **read-only** library
entries ("duplicate to edit" creates a local copy). Invalid downloads quarantine with a
visible reason. Unsubscribing removes the cached entry (and reverts to built-in if it
was active). Startup revalidates cached subscriptions.

### Task C5 — Moderation and policy (integration + docs)
Content policy document (what the game surfaces vs. what Steam moderates); in-app
"report" deep-links to the Steam item page; local hide-list persisted in
`settings.json`. Workshop items never auto-activate — activation is always an explicit
user selection.

### Task C6 — Phase C verification (integration/testing + manual)
Headless scenarios against the emulated service: publish/roundtrip, subscribe/import,
tamper-quarantine, unsubscribe-revert, hide-list. Manual Steam matrix from an installed
depot: publish from account A, subscribe on account B, tampered-file rejection, legal
agreement not yet accepted, offline behavior.

**Phase C exit gate (owner-manual):** depot matrix passes; policy doc approved.

## Owner decisions required before scheduling

Per `AGENTS.md`, move these into `docs/OPEN_QUESTIONS.md` when this milestone is
scheduled, and resolve them into `docs/DECISIONS.md` before implementation:

1. The parametric feature-axis list and art direction for the original robot features.
2. Editor window strategy inside the 480×360-minimum transparent shell (temporary
   resize, separate opaque window, or maximize-within-window).
3. Expressions always composite above paint (recommended for gameplay readability), or
   user-suppressible per character.
4. Character files local + Workshop only (recommended; `progress.json` stays the sole
   Cloud file per §13), or extend Steam Cloud.
5. Editor access free at launch versus progression-gated; any editor/Workshop
   achievements.
6. Workshop content-rating stance and sign-off on the report/hide policy.
7. Local library cap (recommend a soft cap around 64 with list paging).

## Effort estimate

| Phase | Focused effort | Ships alone? |
| --- | --- | --- |
| A — Parametric editor | 3–5 weeks | Yes — full creator without paint/Workshop |
| B — Painting | 2–3 weeks | Yes — on top of A |
| C — Workshop | 2–3 weeks (after M6) | Yes — on top of A (B optional) |

Total 7–11 focused weeks. Phase gates are owner-manual; each phase leaves the game
shippable.

## Progress

Deferred-feature plan written 2026-07-14 at owner request, on the analysis worktree
(baseline `m3-sol` `80fb22b`). Not scheduled; no tasks started; the ROADMAP deferred
status is unchanged. The only near-term obligation this plan creates is already inside
M3.5: keep the `BuddyVisualProfile` seam clean, because every phase here compiles into
it.
