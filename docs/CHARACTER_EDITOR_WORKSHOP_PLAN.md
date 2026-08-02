# Character Editor, Custom Painting, and Steam Workshop — Deferred Feature Plan

Status: **Phase A is scheduled** (owner, 2026-08-02) and runs after the Milestone 5 exit
gate, before Milestone 6 — it has no Steam dependency. Owner decisions 1, 2, 3, 4, 5, and 7
are resolved and recorded in `docs/DECISIONS.md`; decision 6 (Workshop moderation stance)
is deferred with Phase C. **Phases B and C remain deferred** and unschedulable: they keep
their own owner gates, and `AGENTS.md` forbids prebuilding them — no paint or Workshop code,
type, schema field, or UI affordance may land during Phase A beyond the two seams this plan
names explicitly (the compositor's empty paint slot in A2, and nothing else).

Dependencies: the M3.5 slice must be complete and accepted
(`docs/M3_5_3D_PRESENTATION_PLAN.md` — the `BuddyVisualProfile` seam and
`BuddyVisualPresenter` are this plan's rendering substrate, and M3.5 Task 4's injectable
transform-source seam is what lets Task A6 drive the presenter with fixed rest-pose
transforms and no physics); the M3.6 expressive slice
(`docs/M3_6_EXPRESSIVE_PRESENTATION_PLAN.md`) must also be complete — it builds the
face compositor and `FaceExpressionMap` that Phase A parameterizes; Phase C additionally
requires the Milestone 6 Steam adapter.

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
   ≤ 1 MB each; whole package ≤ 8 MB (six fully painted parts at the per-texture cap
   plus manifest and preview must still fit — a 6 MB package cap would reject a
   maximally painted but otherwise legal character). Violations reject at validation,
   never at render.
3. **Safe-boundary swaps**: activating a character applies at the scene root's next fixed
   tick (the queued-request pattern `BoundaryController` already uses), never mid-tick.
   Swap is a pure view change; a scenario witnesses accepted-pain equality across a swap.
4. **Expressions stay readable**: reaction/knockout expressions composite *above* the
   paint layer so gameplay states (`"x_x"`, `">_<"`) can never be painted over. Owner
   decision 3 resolved 2026-08-02: **always above, not user-suppressible** — this is a
   fixed layer-order invariant with no setting and no second code path.
5. **Editor mode is not gameplay**: entering the editor pauses the sandbox (the
   hidden-to-tray suspension path); the §23 zero-allocation rule applies to simulation
   ticks only, but the compositor still re-renders on change, not per frame.
6. **Persistence discipline**: character files follow the §12 atomic
   temp-flush-replace/backup/quarantine pattern; unknown active-character GUIDs fall back
   to the built-in buddy while preserving the stored value (§12 unknown-ID rule).
   Character files are excluded from Steam Cloud (owner decision 4, resolved 2026-08-02:
   **local + Workshop only**); `progress.json` remains the only Cloud file (§13), carrying
   the active-character GUID and nothing else.
7. **Clean-room + UGC boundary**: everything *shipped* (feature sprites, defaults, UI) is
   original; what *users* draw is user content governed by the Phase C policy and
   Steam's UGC terms, not by the clean-room audit.
8. **Non-Steam builds keep full value**: the editor, library, painting, and file-based
   import/export (`.buddychar` zip = the same package format) work with no Steam present;
   only Workshop browse/publish is Steam-gated.

## Phase A — Parametric editor

### Task A1 — Character document schema (Domain, headless-testable)
`CharacterDocument` in `Domain/Characters`: GUID id, display name, `schemaVersion`,
per-part colors, and a bounded feature list. Owner decision 1 resolved 2026-08-02 —
**lean axis set**, exactly four feature slots:

| Slot | Axes |
| --- | --- |
| Eyes | type index into the shipped atlas, offset, scale, color |
| Brows | type index, offset, scale, color |
| Mouth | type index, offset, scale, color |
| Body accent (one) | type index, offset, scale, color |

Per-feature **rotation is out of scope**, as are head/body shape modifiers — the fixed
collision primitives are physics, and constraint 1 keeps them unreachable. Per-part base
color applies to all six M3.5 parts. The slot set is fixed data in the schema; adding a
fifth slot later is a migration, not a Phase A option.

`Validate()` and `ClampToBounds()`; sequential migrations mirroring the save-DTO pattern.
xUnit: bounds and clamping, JSON roundtrip, migration N→N+1, unknown-major rejection,
name length/character limits, unknown type index → clamp to the atlas default rather than
reject (a document from a build with a larger atlas must load, per the §12 unknown-ID rule).

### Task A2 — Feature atlas and part compositor (integration/presentation)
Original robot feature art (eyes, brows, mouths, and robot accents) drawn procedurally
in-engine (`CanvasItem` draw into the compositor viewport — consistent with the M3.5
procedural-asset decision; no external art pipeline). The compositor core and its
re-render-on-change discipline ship in M3.6 for the built-in face; this task extends
them: a parametric feature atlas, per-document parameters, and full-part compositing —
base color → (Phase B paint slot) → features — into per-part `ImageTexture`s assigned
as the Unshaded material's albedo on the M3.5 meshes. Re-render triggers: document
change, expression change. The M3.6 face mounting (feature layer on a head-front quad)
either migrates into the head albedo here or stays a quad above it — resolve at the
Phase A exit gate; the M3.5 `Label3D` emoticon is already retired by M3.6.

### Task A3 — Face expression map (integration/presentation)
`FaceExpressionMap` ships in M3.6 for the built-in face (face-state string → feature
pose for every state the reaction component emits, driven by the authoritative
face-state constant exported beside the resolver — currently ten strings: `":|"`,
`"x_x"`, `">_<"`, `">:("`, `"o_o"`, `":)"`, `":3"`, `"^_^"`, `":("`, `":/"`). This task
extends the same map to parametric characters: every per-document feature variant must
resolve a pose for every state, and the coverage scenario iterates the same constant so
a future face string cannot silently bypass the map into the A4 fallback. The reaction component and its priority
rules do not change; `BuddyReactionComponent` keeps writing strings and the presenter
resolves them. Scenario-checkable without pixels: the map lookup result and the
compositor's last-rendered state are exposed as semantic properties.

### Task A4 — Document→profile compiler (integration)
Pure function `CharacterCompiler.Compile(CharacterDocument) → compiled visual data`
consumed by `BuddyVisualPresenter` exactly where `BuddyVisualProfile` data flows today.
Compile failures (invalid document at load) follow §16: log, quarantine the file, fall
back to the built-in buddy, never crash. Compile and validate are CPU-only by
construction: a headless/renderless compositor failure is an environment condition, not
document invalidity, and must never trigger quarantine — CI runs every scenario without
a GPU.

### Task A5 — Character library persistence (integration)
`user://characters/<guid>/character.json` with the atomic write/backup discipline;
quarantine renames use the existing `.corrupt-<timestamp>` convention. Active-character
GUID persisted in `progress.json` (extension-safe). Library operations: create,
duplicate, rename, delete (soft confirm), select-active. Deletion of the active
character reverts to built-in.

Owner decision 7 resolved 2026-08-02: **no library cap**. Two consequences are
requirements, not optimizations, because nothing else bounds the count:

- Startup and library-open enumerate **directory entries and each `character.json`'s name
  field only**; full document parse, compile, and thumbnail render happen for the active
  character and for a list entry when it is selected — never for the whole library.
- The library panel list is paged or virtualized, so its cost is per visible row.

Scenario `library_large_enumeration`: 500 synthetic character directories, assert startup
completes and that exactly one document is compiled (the active one).

### Task A6 — Editor UI (integration/presentation)
`scenes/editor/character_editor.tscn`: part selector, parameter controls, color pickers,
seeded randomize (injectable RNG per §23 — presentation stream), name entry, library
panel, and a live preview that reuses `BuddyVisualPresenter` through the M3.5 Task 4
transform-source seam, fed **fixed rest-pose transforms** — the preview runs no physics.
Entry from the settings/panel surface; sandbox paused while open — and because that
pause suspends the tree, the editor branch itself must run with `ProcessMode`
Always/WhenPaused or the editor UI freezes with the sandbox. Every string is a
translation key.

Owner decisions 2 and 5 resolved 2026-08-02:

- **Window strategy — temporary resize, same window.** On entry the existing shell stores
  its geometry, resizes to the editor working size, and turns opaque (per-pixel
  transparency off, borders as in Work Mode); on exit it restores the stored size,
  position, and transparency. No second window: the M2 focus, always-on-top, DPI, and
  off-screen-recovery paths stay single-window. The restore must survive an editor exit on
  a monitor that has since disappeared — reuse the M2 off-screen recovery rather than
  writing a second placement rule.
- **Access — free from launch.** A settings-panel entry available on every save, with no
  credit cost, catalogue prerequisite, or unlock flag. The editor is deliberately not an
  economy sink; nothing here touches the M5 balance. No editor achievements in Phase A
  (achievement definitions are M6 scope and the confirmed ten are already fixed).

### Task A7 — Phase A scenarios and journey (integration/testing)
Scenarios: `editor_document_roundtrip` (create → save → load → compile → applied at a
safe-boundary swap), `editor_invalid_quarantine` (corrupt file → quarantine + built-in
fallback + preserved GUID), `expression_map_coverage` (every reaction state resolves to a
pose and re-renders the compositor), `character_swap_physics_invariant` (strike before
and after a swap; accepted pain equal), `editor_window_restore` (enter → exit returns the
stored size, position, and transparency; and enter → exit with the entry monitor gone
lands on-screen through the M2 recovery path), `library_large_enumeration` (A5). Journey:
create → randomize → save → select → strike → correct expression state, through the real
input path. MCP interactive pass per `AGENTS.md` before promotion.

**Phase A exit gate (owner-manual):** editor usability and parametric-face parity accepted
on real Windows; the enter/exit window transition accepted on a real multi-monitor desktop;
default-buddy pipeline unification approved (the A2 head-front-quad question).

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
`paint_stroke_applies` (stroke changes the hash of the CPU-side paint `Image` — never
the composited `SubViewport` output, which is empty headless), `paint_undo_restores`
(hash returns), `paint_roundtrip` (save/load pixel-identical on the paint `Image`),
`paint_oversize_rejected`, `expression_over_paint` (paint fully covering the face still
shows the knockout state — asserted through the A3 semantic layer-order properties, not
pixels, so it holds headless).

**Phase B exit gate (owner-manual):** brush feel and paint→game fidelity accepted.

## Phase C — Steam Workshop (requires M6)

### Task C1 — Package format and validator (Domain, headless-testable)
Versioned package: `manifest.json` (schema version, minimum app version, embedded
character document, per-file SHA-256 checksums, author metadata) plus texture PNGs.
Validator enforces constraint 2 caps, checksums, schema/app-version gates, and part
completeness — plus filesystem hygiene: zip entry names validate against a strict
whitelist (`manifest.json` and the manifest-declared PNG names only; reject `../`,
absolute paths, and anything unexpected — zip-slip), the character id must parse as a
GUID before it is ever used as a `user://characters/<guid>/` path segment, and PNG byte
caps are enforced before decode with dimension caps after. The same format is the local
`.buddychar` import/export file. xUnit: happy path, tampered checksum, oversize,
future-major rejection, minimum-app-version gate, checksum-of-missing-file, zip-slip
entry name, non-GUID id.

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
entries keyed by Workshop item id, never by the embedded document GUID alone — a
subscribed item whose GUID collides with a local character (duplicate or malicious) must
not shadow it or hijack the active-GUID resolution; "duplicate to edit" creates a local
copy under a freshly generated GUID. Invalid downloads quarantine with a visible reason.
Unsubscribing removes the cached entry (and reverts to built-in if it was active).
Startup revalidates cached subscriptions.

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

## Owner decisions

Resolved by the owner 2026-08-02 and recorded in `docs/DECISIONS.md`:

| # | Decision | Resolution | Lands in |
| --- | --- | --- | --- |
| 1 | Feature axes and art direction | Lean set: eyes, brows, mouth, one body accent; type/offset/scale/color each; no rotation, no shape modifiers | A1, A2, A6 |
| 2 | Editor window strategy | Temporary resize of the same window, opaque while open, geometry and transparency restored on exit | A6, A7 |
| 3 | Expressions vs paint | Always composite above paint; not user-suppressible | Constraint 4, B5 |
| 4 | Character file Cloud scope | Local + Workshop only; `progress.json` stays the sole Cloud file | Constraint 6, A5 |
| 5 | Editor access | Free from launch, no progression gate, no Phase A achievements | A6 |
| 7 | Local library cap | Uncapped, with lazy enumeration and a paged list | A5 |

Still outstanding, deferred with its phase:

6. **Workshop content-rating stance and report/hide policy sign-off.** Phase C only. Move
   to `docs/OPEN_QUESTIONS.md` when Phase C is scheduled — it cannot be scheduled before
   Milestone 6 regardless.

## Effort estimate

| Phase | Focused effort | Status | Ships alone? |
| --- | --- | --- | --- |
| A — Parametric editor | 3–5 weeks | **Scheduled** — after the M5 exit gate, before M6 | Yes — full creator without paint/Workshop |
| B — Painting | 2–3 weeks | Deferred | Yes — on top of A |
| C — Workshop | 2–3 weeks (after M6) | Deferred | Yes — on top of A (B optional) |

Total 7–11 focused weeks if all three ever run. Phase gates are owner-manual; each phase
leaves the game shippable. Scheduled scope today is Phase A alone: 3–5 weeks.

## Progress

Deferred-feature plan written 2026-07-14 at owner request, on the analysis worktree
(baseline `m3-sol` `80fb22b`). Not scheduled; no tasks started; the ROADMAP deferred
status is unchanged. The near-term obligations this plan creates are already inside
M3.5: keep the `BuddyVisualProfile` seam clean, and keep the presenter's transform
source injectable (M3.5 Task 4), because every phase here compiles into the former and
the A6 preview drives the latter.

Amended 2026-07-14 after a code-verified review: package cap arithmetic fixed (8 MB
whole-package so six max-size textures fit), zip-slip/GUID-path validation added to C1,
Workshop GUID-collision policy added to C4, the headless GPU boundary made explicit
(A4/B5 assert against CPU-side data only), the authoritative face-state list pinned to
the resolver's actual ten states (A3), and the editor branch's `ProcessMode` under
sandbox pause noted (A6).

**Scheduled 2026-08-02.** The owner scheduled **Phase A only**, to run after the Milestone 5
exit gate and before Milestone 6, and resolved decisions 1, 2, 3, 4, 5, and 7 (decision 6
deferred with Phase C). Those resolutions are folded into constraints 4 and 6 and into
tasks A1, A5, A6, and A7 above, and recorded in `docs/DECISIONS.md`. Phases B and C stay
deferred; the only forward seam Phase A may build is the compositor's empty paint slot
(A2). Both hard dependencies — M3.5 and M3.6 — are complete and accepted, so Phase A is
unblocked apart from M5 finishing. No tasks started.

Amended again 2026-07-14 for the owner's expressiveness direction: the face compositor
and `FaceExpressionMap` now build first in the M3.6 expressive slice
(`docs/M3_6_EXPRESSIVE_PRESENTATION_PLAN.md`) for the built-in buddy; Phase A here
parameterizes them per character document instead of building them, and M3.6 joins
M3.5 as a hard dependency.
