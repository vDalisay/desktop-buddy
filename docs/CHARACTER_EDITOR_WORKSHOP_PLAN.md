# Character Editor, Custom Painting, and Steam Workshop — Agent Handoff Plan

Status: **Phase A is scheduled** as Roadmap Milestone 5.5 (owner, 2026-08-02) and runs
after the Milestone 5 exit gate and before Milestone 6. Phase A has no Steam dependency.
**Phase B (painting) and Phase C (Workshop) remain deferred and unschedulable.**

Task A0 is the mandatory first task. It aligns the authoritative documents so implementation
agents are no longer blocked by the current conflict between `ROADMAP.md` and `AGENTS.md`.
No Phase A production code may start until A0 is complete. Phase B/C code, schemas, UI,
services, and affordances remain forbidden during Phase A except for the single generic
surface-underlay seam named in A2.

Dependencies:

- M3.5 is complete and accepted. Its trusted `BuddyVisualProfile`, 3D mesh construction,
  look materials, and `IBuddyVisualTransformSource` remain the geometry substrate.
- M3.6 is complete and accepted. Its semantic face-state catalog, blink/chew/look-at
  composition, and current face quad are the expression substrate.
- Milestone 5 must pass its exit gate before Phase A begins.
- Phase C additionally requires the Milestone 6 platform/Steam adapter.

## Design intent

A Mii-editor-inspired creator for the original robot buddy:

- **Phase A — Parametric editor:** six part colors, three parametric facial feature slots,
  one torso accent slot, a local character library, deterministic randomization, and a
  physics-free live preview.
- **Phase B — Painting:** per-part freehand paint placed beneath face and accent decals.
- **Phase C — Workshop:** file packages, local import/export, Steam Workshop publishing,
  subscription, validation, quarantine, and moderation handling.

Each phase must leave the game independently shippable.

## Prime invariants

1. **Customization is visual-only by construction.** Character data cannot reach rig,
   mass, collision, drives, forces, mesh radius, capsule height, connector geometry,
   depth lanes, rotation policies, look tuning, activities, damage, economy, or physics.
2. **Trusted geometry remains separate from untrusted appearance.**
   `BuddyVisualProfile` remains a built-in trusted Resource. Character compilation never
   creates, clones, replaces, or mutates a `BuddyVisualProfile`.
3. **Exactly one active buddy identity.** A character selection changes the appearance of
   the existing buddy; it never creates a second simulation buddy.
4. **Safe-boundary application.** Runtime appearance swaps are queued and applied from the
   owning scene root at the next fixed tick. Compilation and file I/O happen before the
   request is queued.
5. **Semantic expressions remain authoritative.** `BuddyReactionComponent` keeps emitting
   the existing semantic strings and `FaceExpressionCatalog` keeps translating those
   strings to `FaceFeaturePose`. Phase A changes renderers, not reaction priority or meaning.
6. **Expressions remain visible above future paint.** The face stays on the existing
   head-front quad and the torso accent uses a torso-front decal quad. Both render above
   any future part-surface paint. There is no setting or alternate layer order.
7. **Editor mode pauses gameplay, not the application.** The editor never uses the
   hidden-to-tray path: the window remains visible, rendering remains enabled, and editor
   time is foreground inactive time, not hidden time.
8. **Character files are local-only.** `progress.json` remains the sole Cloud-eligible
   file and stores only the selected character identifier. Character directories are
   excluded from Steam Cloud.
9. **Unknown identifiers are preserved.** Unknown feature IDs and missing active-character
   IDs resolve to safe defaults at runtime without being rewritten away merely because the
   current build does not recognize them.
10. **Headless correctness does not depend on pixels.** Validation, migration, compilation,
    activation, layer ordering, invalidation keys, and store behavior are CPU-testable.
    GPU absence never causes character quarantine.
11. **Phase A does not prebuild Phase B/C.** It introduces no paint document fields, brush
    types, PNG persistence, package schemas, Workshop types, or Workshop UI. The only
    forward seam is `BuddyVisualRigView.SetSurfaceUnderlay(part, texture)`, always called
    with `null` in Phase A.
12. **All shipped art and copy are original.** User-generated content is addressed only
    when Phase C is scheduled.

## Locked Phase A architecture

### Runtime data flow

```text
character.json
    -> CharacterDocumentPolicy.DecodeAndMigrate
    -> CharacterDocumentNormalizer.Normalize
    -> CharacterCompiler.Compile
    -> CompiledCharacterAppearance
    -> CharacterSelectionService queues CharacterAppearanceApplyRequest
    -> SandboxRoot applies request on the next fixed tick
    -> BuddyVisualRigView.ApplyAppearance
```

The runtime gameplay presentation path is:

```text
BuddyRoot + reactions + activities + look-at + transform source
    -> BuddyVisualPresenter produces BuddyVisualPoseFrame and FaceRenderState
    -> BuddyVisualRigView renders pose and applies CompiledCharacterAppearance
```

The editor preview path is:

```text
CharacterEditorSession working copy
    -> CharacterCompiler
    -> CharacterPreviewController
    -> fixed rest-pose BuddyVisualPoseFrame + chosen semantic expression
    -> the same BuddyVisualRigView
```

The preview path contains no `BuddyRoot`, `RigidBody2D`, damage component, economy service,
activity selector, or live reaction component.

### Rendering decision

Phase A keeps the existing face quad. It does **not** bake the face into the sphere texture.

- Six base colors are applied to the existing per-part lit material instances.
- The current face quad becomes parameterized by the compiled eye, brow, and mouth
  appearances.
- A new torso-front decal quad renders the single body accent.
- Face and accent outputs are bound directly as `ViewportTexture`s. Phase A performs no
  GPU readback and creates no per-part `ImageTexture`.
- The current scorch path moves into `BuddyVisualRigView` and tints from the active
  appearance color, not from the original `PartVisualDefinition.Color`.
- The generic surface-underlay texture slot remains null in Phase A. Phase B may later bind
  an `ImageTexture` to that slot without changing character geometry or decal ordering.

### Appearance boundary

`CompiledCharacterAppearance` is an engine-free immutable value. It contains only:

```text
Guid CharacterId
PartColorSet:
    Head, Torso, LeftHand, RightHand, LeftFoot, RightFoot (Rgba32, opaque)
CompiledFeatureAppearance Eyes
CompiledFeatureAppearance Brows
CompiledFeatureAppearance Mouth
CompiledFeatureAppearance TorsoAccent
```

Each `CompiledFeatureAppearance` contains only:

```text
ResolvedFeatureId
NormalizedOffset X/Y
UniformScale
Rgba32 Color
```

It contains no Godot object, Resource, texture, mesh, physics type, tuning value, file path,
Workshop field, or mutable collection.

`CharacterCompiler` accepts a `CharacterDocument` and the engine-free
`CharacterFeatureCatalog`. Unknown feature IDs compile to the slot default while returning
a warning; the original unknown ID remains unchanged in the document.

### Semantic face contract

The existing map remains:

```text
semantic face string -> FaceFeaturePose
```

Every eye renderer must render every `FaceEyePose`, every brow renderer every
`FaceBrowPose`, and every mouth renderer every `FaceMouthPose`. Phase A does not create a
per-character expression table and does not create a character-by-expression cross-product
in data.

### Initial shipped feature catalog

Stable string IDs are persistence contracts and may never be reordered, reused, or changed
after release.

| Slot | Stable IDs |
| --- | --- |
| Eyes | `eyes.soft_oval`, `eyes.round_dot`, `eyes.horizontal_led` |
| Brows | `brows.soft_arc`, `brows.straight`, `brows.segmented` |
| Mouth | `mouth.rounded`, `mouth.pixel`, `mouth.line` |
| Torso accent | `accent.none`, `accent.panel`, `accent.chevron`, `accent.bolts` |

The default document uses `eyes.soft_oval`, `brows.soft_arc`, `mouth.rounded`, and
`accent.none`, matching the built-in buddy as closely as the parameterized renderer permits.
These IDs are separate from `FaceStyleId` and do not unlock, expose, or repurpose any
shop-reserved face style.

### Document bounds and serialization contract

`CharacterDocument` schema version 1 uses named properties, not array indices:

```text
schemaVersion
id
displayName
partColors:
    head, torso, leftHand, rightHand, leftFoot, rightFoot
features:
    eyes, brows, mouth, torsoAccent
```

Each feature contains:

```text
featureId
offsetX
offsetY
scale
color
```

Rules:

- `id` is a canonical `Guid` in `D` format.
- Display names are trimmed and contain 1–40 Unicode scalar values.
- Display names reject control characters, line breaks, and `\ / : * ? " < > |`.
- Colors serialize as uppercase `#RRGGBB`; alpha is not serialized and is always opaque.
- Offsets are normalized local coordinates and clamp to `[-1.0, +1.0]`.
- Uniform scale clamps to `[0.75, 1.25]`.
- The eye slot controls both eyes as one symmetrical group; eye separation is renderer-owned.
- The brow slot controls both brows as one symmetrical group.
- The torso accent is fixed to the torso-front decal; documents cannot choose another part.
- Rotation, opacity, shape, mesh size, depth, z-order, and physics parameters do not exist.
- Unknown feature IDs are valid and preserved. The compiler resolves them to the slot default.
- Missing known fields migrate/default deterministically. Malformed required values,
  non-finite numbers, invalid GUIDs, invalid color syntax, and unsupported future schema
  versions do not silently normalize into a different identity.
- Top-level unknown JSON fields are retained through `[JsonExtensionData]`.
- Migrations are sequential (`N -> N+1`) and never skip versions.
- `DecodeAndMigrate`, `Normalize`, `Validate`, and `Compile` are separate operations with
  separate result types; loading code must not conflate a rendering/environment failure
  with invalid document data.

### Editor working-copy contract

- Opening a character creates an in-memory working copy.
- Controls modify only the working copy and preview.
- `Save` normalizes, validates, atomically writes, then updates the session baseline.
- Editing the active character applies the new appearance only after the save succeeds.
- A new character is not persisted or active until its first successful save.
- `Cancel` restores the last successfully saved document.
- Closing, deleting, or changing selection while dirty shows exactly:
  **Save / Discard / Continue Editing**.
- A save failure keeps the working copy, dirty flag, and editor open.
- Duplicate creates a fresh GUID before writing and never copies active-selection state.
- Rename is an ordinary document save; directory identity remains the GUID.
- Randomize is deterministic for a supplied seed and uses only known catalog IDs and valid
  bounds.

### Active-selection transaction

Selecting an existing character:

1. Load, migrate, normalize, validate, and compile off the fixed-tick path.
2. Queue `CharacterAppearanceApplyRequest`.
3. At the next fixed tick, apply the appearance to the existing `BuddyVisualRigView`.
4. After successful application, mutate `BuddyProgressState.ActiveCharacterId`.
5. Emit `ProgressChange.CharacterSelected`; `SaveCoordinator` immediately requests a
   durable progress flush.
6. If the durable save fails, the runtime appearance remains active and progress remains
   dirty for retry. The failure is visible to the UI/log and is never treated as a
   character-file failure.

Startup resolution:

- `null` active ID means built-in.
- A valid ID with a valid local character loads and applies that character.
- A missing, invalid, corrupt, or unsupported character falls back to built-in while the
  stored active ID is preserved.
- A corrupt primary first attempts its rolling backup.
- An unsupported future schema is not quarantined.

Deleting the active character:

1. Confirm deletion.
2. Queue the built-in appearance.
3. On safe-boundary application, set active ID to `null` and request immediate progress save.
4. Delete the character directory.
5. A deletion failure reports the error; it never reactivates the deleted appearance.

## Design seams and ownership

| Worker | Home | Responsibility |
| --- | --- | --- |
| Document DTOs/policies | `domain/DesktopBuddy.Domain/Characters` | Engine-free schema, migration, normalization, validation, stable IDs, colors, bounds. |
| Character compiler | `domain/DesktopBuddy.Domain/Characters` | Pure document -> `CompiledCharacterAppearance`, default resolution, warnings. |
| Feature catalog data | `domain/DesktopBuddy.Domain/Characters` | Stable IDs, defaults, allowed slot membership. |
| Visual rig view | `src/Buddy/Presentation3D` | Trusted geometry/material/decal ownership; applies pose and narrow appearance. |
| Runtime presenter | `src/Buddy/Presentation3D` | Samples gameplay state and produces pose/expression values for the view. |
| Feature compositors | `src/Buddy/Presentation3D/Characters` | Face and torso-accent render targets, renderer registry, render-key invalidation. |
| Character store/index | `src/Persistence/Characters` | Atomic documents, backups, quarantine, directory operations, lazy metadata. |
| Selection service | `src/Characters` | Load/compile, safe-boundary request, progress selection transaction. |
| Editor mode | `src/App` + `src/Platform` | Pause reason, shell/window capture, visible opaque editor transition, restoration. |
| Editor session/UI | `src/UI/Editor` + `scenes/editor` | Working copy, dirty state, commands, virtualized library, preview, translations. |
| Phase B paint | `src/Editor/Paint` | Deferred. CPU images, brush/strokes, UV mapping, underlay binding. |
| Phase C packages | `domain/.../Characters/Packages` | Deferred. Data-only package validation and checksums. |
| Phase C Workshop | `src/Platform` + `DesktopBuddy.Steam` | Deferred. Local emulator and Steamworks.NET UGC adapter. |

## Phase A — Parametric editor

### Task A0 — Align source-of-truth documents

**Prerequisite:** none. This is the only Phase A task allowed before the document conflict
is resolved.

**Files to update:**

- `AGENTS.md`
- `docs/PRODUCT_REQUIREMENTS.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/AGENT_VERIFICATION_AND_E2E.md`
- `docs/ROADMAP.md` only if its Milestone 5.5 summary no longer matches this plan
- `docs/DECISIONS.md` only to cross-reference already confirmed owner decisions; do not
  create new owner decisions

**Required changes:**

1. Change `AGENTS.md` so Phase A is permitted only during Milestone 5.5 while painting,
   packages, Workshop, multiple buddies, profiles, and generalized modding remain forbidden.
2. Add observable Phase A behavior to product requirements.
3. Add the locked architecture and data flow above to architecture documentation.
4. Register every A9 scenario/journey and Windows manual check in the test documents.
5. State that this file is the Phase A task-level source of truth after the higher-priority
   owner decisions and product requirements.
6. Remove wording that tells an implementation agent both to implement and not implement
   the same feature.

**Tests/checks:**

- Documentation link/path check if the repository has one.
- Search for contradictory active prohibitions after the edit.
- Confirm Phase B/C remain explicitly deferred in every source-of-truth document.

**Non-goals:** production code, Resource creation, scenes, schemas.

**Done when:** a new agent following `AGENTS.md` in order can legally begin A1 without
encountering a source conflict.

---

### Task A1 — Engine-free character schema, migration, and compiler

**Prerequisite:** A0.

**Add under `domain/DesktopBuddy.Domain/Characters`:**

- `CharacterDocument.cs`
- `CharacterDocumentPolicy.cs`
- `CharacterDocumentNormalizer.cs`
- `CharacterDocumentValidator.cs`
- `CharacterFeatureCatalog.cs`
- `CharacterCompiler.cs`
- `CharacterResults.cs`
- `Rgba32.cs`
- `NormalizedFeatureTransform.cs`

**Public contracts:**

```csharp
public static class CharacterDocumentPolicy
{
    public const int CurrentSchemaVersion = 1;
    public static CharacterDecodeResult DecodeAndMigrate(string json);
    public static string Serialize(CharacterDocument document);
}

public static class CharacterDocumentNormalizer
{
    public static CharacterNormalizationResult Normalize(CharacterDocument document);
}

public static class CharacterDocumentValidator
{
    public static CharacterValidationResult Validate(CharacterDocument document);
}

public static class CharacterCompiler
{
    public static CharacterCompileResult Compile(
        CharacterDocument normalizedDocument,
        CharacterFeatureCatalog catalog);
}
```

`CharacterCompileResult` contains either a `CompiledCharacterAppearance` or fatal errors,
plus non-fatal unknown-ID warnings. No compiler result contains a Godot type.

**Implementation steps:**

1. Implement the exact schema/bounds in this plan.
2. Use stable feature IDs; never persist atlas positions.
3. Preserve top-level extension data and unknown feature IDs.
4. Implement one-version-at-a-time migrations even though schema 1 has no predecessor yet;
   the migration loop and unsupported-future result must exist from day one.
5. Keep normalization pure: return a new document and a list of changed fields.
6. Keep validation pure and non-mutating.
7. Compile only normalized, valid documents.
8. Resolve unknown feature IDs to catalog defaults in the compiled value without changing
   the document.
9. Add an architecture test that recursively inspects the public fields/properties of
   `CompiledCharacterAppearance` and fails if they reference Godot, physics, persistence,
   platform, file, Resource, mutable collection, or tuning namespaces.

**Unit tests:**

- JSON roundtrip for every field.
- Canonical GUID/color serialization.
- Name boundaries: empty, 1, 40, 41 Unicode scalars, forbidden/control characters.
- Offset and scale normalization at/inside/outside every bound.
- Non-finite number rejection.
- Missing-field defaults.
- Unknown feature ID preserved after decode/normalize/serialize.
- Unknown feature ID compiles to the slot default with one warning.
- Unsupported future schema is returned without quarantine semantics.
- Sequential migration harness refuses a missing migration step.
- Default document compiles to the catalog defaults.
- Compiled appearance boundary contains no forbidden type.
- Feature IDs are unique globally and belong to exactly one slot.
- Catalog defaults exist and belong to their declared slot.

**Non-goals:** Godot nodes, textures, files, UI, active selection, paint fields.

**Done when:** domain tests pass and the compiled type is provably appearance-only.

---

### Task A2 — Extract `BuddyVisualRigView` from live gameplay sampling

**Prerequisites:** A0–A1.

**Add/modify under `src/Buddy/Presentation3D`:**

- Add `BuddyVisualRigView.cs`
- Add `BuddyVisualPoseFrame.cs`
- Add `StaticBuddyVisualTransformSource.cs`
- Refactor `BuddyVisualPresenter.cs`
- Refactor the current face ownership only as required by A4
- Keep `BuddyVisualProfile.cs` trusted and built-in

**Public contracts:**

```csharp
public partial class BuddyVisualRigView : Node3D
{
    public void Initialize(
        BuddyVisualProfile trustedProfile,
        IBuddyVisualTransformSource geometrySource);

    public void ApplyPose(in BuddyVisualPoseFrame frame);
    public void ApplyAppearance(in CompiledCharacterAppearance appearance);
    public void ApplyBuiltInAppearance();
    public void SetPartScorch(BuddyPartId partId, float amount, Color scorchColor);

    // The only allowed Phase B seam. Every Phase A caller passes null.
    internal void SetSurfaceUnderlay(BuddyPartId partId, Texture2D? texture);
}
```

`BuddyVisualPoseFrame` contains resolved visual transforms/yaw/offsets and the already
composed `FaceRenderState`; it contains no mutable gameplay authority.

**Implementation steps:**

1. Move stable socket, mesh, connector, material, outline, face-plate, and new accent-plate
   ownership from `BuddyVisualPresenter` into `BuddyVisualRigView`.
2. Keep mesh radii, capsule height, connector definitions, depth lanes, rotation policies,
   look profile, and material tuning sourced only from the trusted `BuddyVisualProfile`
   and geometry source.
3. Keep gameplay sampling, interpolation, activities, facing, look-at, refusal, hit-lag
   offsets, and reaction semantics in `BuddyVisualPresenter`.
4. Have the presenter produce a `BuddyVisualPoseFrame` and call `RigView.ApplyPose`.
5. Move scorch bookkeeping to the rig view. Store active base color and current scorch
   amount per part; applying a new appearance must reapply existing scorch from the new
   base, and fading to zero must return to the custom base color.
6. `ApplyAppearance` may mutate material colors and compositor appearance only. It must not
   rebuild meshes, sockets, connectors, collision, transform sources, or presentation tuning.
7. Implement `StaticBuddyVisualTransformSource` with the trusted six-part rest radii and
   fixed rest transforms for preview use.
8. Preserve all existing runtime scenario observability or move it to equivalent rig-view
   properties so current tests do not lose their oracle.

**Tests/scenarios:**

- Existing M3.5/M3.6 presentation scenarios remain green.
- Applying two appearances changes no mesh Resource, mesh radius, connector count,
  connector geometry, socket count, trusted profile reference, or transform source.
- Existing scorch amount survives an appearance swap and fades to the new custom base.
- `StaticBuddyVisualTransformSource` builds a view without `BuddyRoot`.
- A preview fixture contains zero physics nodes/components.
- Reapplying an equal appearance performs no material/compositor mutation.
- `SetSurfaceUnderlay` remains null for all Phase A production paths.

**Non-goals:** file loading, selection, editor controls, paint texture creation.

**Done when:** gameplay and preview can share the same visual rig without preview code
constructing fake gameplay state.

---

### Task A3 — Parameterized feature renderer registry

**Prerequisites:** A1–A2.

**Add under `src/Buddy/Presentation3D/Characters`:**

- `CharacterFeatureRendererRegistry.cs`
- `ICharacterEyeRenderer.cs`
- `ICharacterBrowRenderer.cs`
- `ICharacterMouthRenderer.cs`
- `ICharacterAccentRenderer.cs`
- renderer implementations for every locked stable ID
- `CharacterFeaturePainterControl.cs`

**Contracts:**

- The registry maps exactly the stable catalog IDs to renderer instances.
- Eye renderers accept every `FaceEyePose`, blink state, and pupil offset.
- Brow renderers accept every `FaceBrowPose`.
- Mouth renderers accept every `FaceMouthPose`.
- Accent renderers draw the one torso accent; `accent.none` draws nothing.
- Renderers receive normalized offset, uniform scale, requested color, and trusted outline
  color. They never receive physics, files, economy, active progress, or editor state.

**Implementation steps:**

1. Port the accepted Soft Oval painter into the new renderer interfaces without changing
   semantic expression mapping.
2. Implement the additional original procedural variants named in the catalog.
3. Draw a trusted outline/backing stroke beneath user-selected feature colors so reaction
   shapes remain readable even when the selected color approaches the part base color.
4. Keep renderer geometry in normalized face/accent units. Document transforms are applied
   by one shared transform helper, not reimplemented per renderer.
5. Add startup validation that the engine-free catalog ID set and Godot renderer registry
   ID set are identical.
6. Do not expose shop-reserved `FaceStyleId` values in this registry.

**Tests:**

- Registry/catalog exact-set equality.
- Every eye renderer accepts every `FaceEyePose`.
- Every brow renderer accepts every `FaceBrowPose`.
- Every mouth renderer accepts every `FaceMouthPose`.
- Every semantic face string resolves through the unchanged `FaceExpressionCatalog`, then
  through every renderer family without fallback or exception.
- Offset/scale transform helper tests at min/zero/max bounds.
- `accent.none` produces an empty accent draw command list.
- No renderer references gameplay/physics namespaces.

**Non-goals:** changing face-state strings, reaction priority, blink/chew timing, paint.

**Done when:** all known catalog IDs have complete semantic renderers and the existing
expression map remains unchanged.

---

### Task A4 — Face and torso-accent compositors with exact invalidation

**Prerequisites:** A2–A3.

**Add/modify under `src/Buddy/Presentation3D/Characters`:**

- `ParametricFaceCompositor.cs`
- `BodyAccentCompositor.cs`
- `CharacterRenderKeys.cs`
- adapt or replace the current `FaceCompositor` with a runtime controller that produces
  `FaceRenderState` and delegates drawing

**Locked render-target design:**

- Face: one transparent 200×200 `SubViewport`, existing 40×40 world-unit head quad.
- Accent: one transparent 256×256 `SubViewport`, fixed torso-front quad whose size/depth
  come from trusted presentation code/Resource, never the character document.
- Both use `RenderTargetUpdateMode.Once`.
- Both bind their `ViewportTexture` directly to an unshaded alpha material.
- No `GetTexture().GetImage()`, GPU readback, PNG encoding, or `ImageTexture` creation.
- Head and torso decal quads remain children of their respective sockets and inherit pose.
- Layer order is physical and singular: part material/optional underlay, then decal quad.

**Render keys:**

```text
FaceRenderKey:
    Compiled eyes/brows/mouth appearances
    FaceRenderState
    trusted outline color

AccentRenderKey:
    Compiled torso accent appearance
    trusted outline color
```

Value equality controls invalidation. Appearance changes and semantic state changes each
cause at most one render; equal keys cause none.

**Implementation steps:**

1. Split gameplay semantic sampling from pixel drawing.
2. Runtime `BuddyVisualPresenter`/face controller continues to produce blink, pupils, chew,
   and reaction-priority `FaceRenderState`.
3. `CharacterPreviewController` supplies a deterministic preview state and can switch among
   all semantic face strings without a live buddy.
4. Bind the active compiled appearance to both compositors through `BuddyVisualRigView`.
5. Preserve headless behavior: update semantic keys and render counters with no viewport.
6. Expose `LastRenderKey` and `RenderCount` as scenario oracles.
7. Default built-in appearance must use this same compositor path; no parallel custom-only
   face implementation remains.

**Tests/scenarios:**

- Equal key -> zero additional renders.
- One appearance field change -> exactly one relevant compositor render.
- One semantic face change -> exactly one face render and zero accent renders.
- Base part color change -> material update without face repaint unless a render key field
  actually changes.
- Every semantic face string updates the face render key.
- Headless mode updates keys/counts without a viewport.
- Face and accent quads are above the part surface-underlay slot.
- Built-in and default-document semantic outputs match.
- No GPU failure or headless absence can mark a document corrupt.

**Non-goals:** full sphere/capsule texture baking, paint, thumbnails, GPU readback.

**Done when:** the built-in and custom buddy use one parameterized decal pipeline with
testable on-change rendering.

---

### Task A5 — Character store, backup recovery, quarantine, and lazy index

**Prerequisites:** A1.

**Add under `src/Persistence/Characters`:**

- `ICharacterFileSystem.cs`
- `CharacterFileSystem.cs`
- `CharacterStore.cs`
- `CharacterLibraryIndex.cs`
- `CharacterStoreResults.cs`
- `CharacterPaths.cs`

**Filesystem contract must support:**

- file and directory existence;
- directory creation;
- directory enumeration;
- UTF-8 text read;
- durable UTF-8 write;
- atomic replace with backup;
- file move;
- directory move;
- recursive directory delete;
- deterministic injected UTC clock for quarantine names.

All `user://` paths are resolved on the Godot main thread before these Godot-free services
run off-thread.

**Directory contract:**

```text
user://characters/<canonical-guid>/
    character.json
    character.json.bak
```

Phase A creates no PNG or thumbnail files.

**Load policy:**

1. If primary is valid, return it.
2. If primary is malformed/invalid, quarantine only the primary file and try the backup.
3. If backup is valid, return `BackupRecovered`.
4. If backup is malformed/invalid, quarantine it and return failure.
5. If either file has an unsupported future schema, return `UnsupportedFutureVersion`
   without quarantine.
6. A compiler or renderer/environment failure is not a store failure and never moves files.
7. Directory name is parsed as a GUID before constructing child paths. Symlinks/reparse
   points are rejected or not followed by enumeration.

**Save policy:**

- Write `character.json.tmp` durably.
- Existing primary: replace primary and roll it to `.bak`.
- First save: move temp to primary.
- Cancellation before replace leaves the old primary intact.
- Save errors leave the editor dirty and do not change active appearance.

**Lazy index policy:**

- Enumerate directories and read only bounded metadata from `character.json` using
  `Utf8JsonReader`: schema version, ID, and display name.
- Do not deserialize, migrate, compile, or render every document during enumeration.
- Invalid metadata produces a visible disabled index row with an error reason; full recovery
  occurs only when selected.
- Library list is paged/virtualized. Phase A list rows are text/status only; no eager
  thumbnails.
- Sort by display name using ordinal-ignore-case with GUID as deterministic tie-breaker.

**Operations:**

```csharp
Task<CharacterLoadResult> LoadAsync(Guid id, CancellationToken token);
Task<CharacterSaveResult> SaveAsync(CharacterDocument document, CancellationToken token);
Task<CharacterDeleteResult> DeleteAsync(Guid id, CancellationToken token);
Task<IReadOnlyList<CharacterIndexEntry>> ReadPageAsync(
    int offset, int count, CancellationToken token);
```

Duplicate is implemented at the session/service layer: load source, assign fresh GUID,
supply a new name, and save through `CharacterStore`.

**Tests:**

- First save, replacement save, rolling backup.
- Cancellation/error before replace preserves old primary.
- Corrupt primary + valid backup recovers.
- Corrupt primary + corrupt backup quarantines both individually.
- Unsupported future primary is not quarantined.
- Directory traversal, invalid GUID directory, and reparse/symlink cases are ignored/rejected.
- Duplicate gets a fresh GUID.
- Rename keeps directory GUID.
- Delete removes only the selected GUID directory.
- 500 directories: one page reads bounded metadata only; startup fully loads/compiles only
  the active document.
- Enumeration order is deterministic.
- Index never renders thumbnails or touches GPU.

**Non-goals:** active progress mutation, UI, Workshop/import/export, PNGs.

**Done when:** library storage has failure-safe transaction behavior and unbounded-count
cost is proportional to visible rows plus the active document.

---

### Task A6 — Active-character progress and safe-boundary selection

**Prerequisites:** A1, A2, A5.

**Modify/add:**

- `domain/DesktopBuddy.Domain/Persistence/ProgressSave.cs`
- `domain/DesktopBuddy.Domain/Persistence/BuddyProgressState.cs`
- progress migration/policy tests
- `src/Persistence/SaveCoordinator.cs`
- add `src/Characters/CharacterSelectionService.cs`
- add `src/Characters/CharacterAppearanceApplyRequest.cs`
- modify the owning gameplay scene root (`SandboxRoot` or its focused child coordinator)

**Progress contract:**

- Bump `ProgressSave.CurrentSchemaVersion` from whatever version exists when A6 begins.
- Add `string? ActiveCharacterId`; `null` means built-in.
- Preserve an unknown/missing/non-GUID stored string rather than silently rewriting it.
- Add the same field to `ProgressSnapshot`, construction/restoration, and save conversion.
- Add `ProgressChange.CharacterSelected`.
- Add one `BuddyProgressState.SelectCharacter(string? id)` mutation path.
- `SaveCoordinator` treats `CharacterSelected` like a purchase/unlock and requests an
  immediate flush.

**Selection service contract:**

```csharp
public sealed class CharacterSelectionService
{
    public Task<CharacterSelectionPrepareResult> PrepareAsync(
        string? requestedId,
        CancellationToken token);

    public void QueuePrepared(CharacterSelectionPrepareResult prepared);
    public void PhysicsTick();
}
```

`PrepareAsync` handles store load/domain compile only. `PhysicsTick` applies the already
prepared immutable appearance; it performs no file I/O, JSON, allocation-heavy rendering,
or async wait.

**Implementation steps:**

1. Add built-in appearance as an explicit prepared result.
2. At startup resolve the stored ID without changing it on fallback.
3. Queue requests last-write-wins, matching the existing queued boundary pattern.
4. Apply to the existing rig view on a fixed tick.
5. Only after successful application mutate progress and request immediate save.
6. Surface save failure independently from selection/file validity.
7. Implement active deletion ordering exactly as locked above.
8. Ensure selecting the already active/equal appearance is idempotent.

**Tests/scenarios:**

- Progress migration defaults active ID to null.
- Active ID roundtrip and restart resolution.
- Missing ID -> built-in runtime, stored ID preserved.
- Malformed ID -> built-in runtime, stored string preserved.
- Unsupported character -> built-in, no quarantine, stored ID preserved.
- Selection applies only on a fixed tick.
- Two queued selections before a tick apply only the last.
- Character swap leaves position, velocity, mass, collision, pain thresholds, accepted pain,
  balance, mood, activity state, and rig/drive Resources unchanged.
- Selection emits `CharacterSelected` and requests immediate flush.
- Flush failure leaves state dirty while the appearance remains active.
- Deleting active queues built-in before deleting the directory.
- Deleting non-active never changes active selection.
- Startup compiles exactly one document: the valid active one.

**Non-goals:** editor layout, direct UI calls into the rig view, paint/Workshop.

**Done when:** appearance selection is a durable, safe-boundary, visual-only transaction.

---

### Task A7 — Visible editor mode and window-state restoration

**Prerequisites:** A0, A2.

**Add/modify:**

- add `src/App/GameplayPauseCoordinator.cs`
- add `src/App/EditorModeCoordinator.cs`
- modify `src/App/LifecycleCoordinator.cs`
- modify `src/Platform/DesktopWindowController.cs`
- modify `src/Platform/DesktopShellController.cs`
- update platform/domain window state types and tests

**Pause contract:**

```text
GameplayPauseReason:
    HiddenToTray
    OperatingSystemSuspend
    Laboratory
    CharacterEditor
```

`GameplayPauseCoordinator` owns the effective `SceneTree.Paused` state. Existing paths stop
writing `GetTree().Paused` independently and acquire/release their reason instead.

- `HiddenToTray` may hide and throttle rendering.
- `OperatingSystemSuspend` follows existing clock-reset semantics.
- `CharacterEditor` pauses gameplay only: visible window, render loop enabled, foreground
  inactive lifecycle accounting.
- The editor branch and coordinators run with `ProcessMode.Always`.
- Tool/gameplay input is disabled while editor mode owns the shell.

**Window contract:**

```csharp
public readonly record struct DesktopWindowState(
    Rect2I Rect,
    bool TransparentRequested,
    bool TransparencyActive,
    bool Borderless,
    bool AlwaysOnTop,
    InputMode InputMode,
    int MsaaLevel,
    bool Vsync);

public DesktopWindowState CaptureState();
public void ApplyEditorWindow(Vector2I requestedClientSize);
public void RestoreState(
    in DesktopWindowState state,
    bool recoverAgainstCurrentMonitors);
```

Editor requested client size is **960×720**. Placement is recovered/clamped through the
existing monitor policy if the usable monitor cannot contain it. The editor is opaque,
uses full-window input capture, preserves the existing borderless strategy, and draws its
own opaque panel/background.

**Shell behavior:**

1. Capture state before any editor mutation.
2. Enter editor capture so the whole client area receives UI input.
3. Ignore editor-generated resize events for sandbox boundary requests.
4. On exit, recover the stored rect against current monitor topology, restore all captured
   flags/settings, then queue exactly one boundary request for the restored client size.
5. Escape is routed to editor close/dirty handling while editor mode is active; it does not
   independently toggle Work/Play mode.
6. Global/tray recovery can still force exit and restore a usable Work mode.
7. Unexpected editor scene removal invokes restoration in a finally/fallback path.

**Tests/scenarios:**

- Enter pauses gameplay but leaves lifecycle/rendering/editor processing active.
- Editor time counts foreground inactive, not hidden.
- Hidden-to-tray behavior remains unchanged.
- Requested 960×720 editor state is opaque and fully interactive.
- Exit restores rect, transparency, borderless, always-on-top, input mode, MSAA, and VSync.
- Removed entry monitor -> restored rect is on-screen through existing recovery policy.
- Editor resize creates zero gameplay boundary requests.
- Exit creates exactly one restored-size boundary request.
- Repeated enter/exit is idempotent and does not leak pause reasons.
- Unexpected editor teardown restores the shell.
- Escape with a dirty session opens the dirty prompt instead of exiting.

**Non-goals:** editor controls and character files.

**Done when:** editor mode is visible, recoverable, and cannot accidentally use hidden-time
or hidden-rendering behavior.

---

### Task A8 — Character editor session, preview, and UI

**Prerequisites:** A1–A7.

**Add:**

- `src/UI/Editor/CharacterEditorSession.cs`
- `src/UI/Editor/CharacterEditorController.cs`
- `src/UI/Editor/CharacterPreviewController.cs`
- `src/UI/Editor/CharacterLibraryViewModel.cs`
- `scenes/editor/character_editor.tscn`
- translation keys/resources
- settings/panel entry point

**Session commands:**

```text
CreateNew
Open
SetDisplayName
SetPartColor
SetFeatureId
SetFeatureOffset
SetFeatureScale
SetFeatureColor
Randomize(seed)
Save
Discard
Duplicate
Delete
RequestClose
RequestOpenOther
```

Every command returns a typed result suitable for UI messaging. The session, not individual
Controls, owns dirty state and the last saved baseline.

**Preview contract:**

- Uses `BuddyVisualRigView`, `StaticBuddyVisualTransformSource`, and
  `CharacterPreviewController`.
- Contains no `BuddyRoot` and no physics.
- Uses fixed rest-pose transforms.
- Provides a preview expression selector covering all semantic face strings; default neutral.
- Compiles the normalized working copy after a coalesced UI change.
- Color-slider drags may coalesce to at most one appearance apply per rendered frame.
- Equal compiled appearances do not reapply/repaint.

**UI layout:**

- Left: virtualized/paged character library, New, Duplicate, Rename, Delete.
- Center: physics-free live buddy preview and expression selector.
- Right: part colors; eyes, brows, mouth, torso-accent type/offset/scale/color controls.
- Bottom/right actions: Randomize, Save, Discard/Cancel, Close.
- Dirty-state dialog: Save / Discard / Continue Editing.
- All visible strings use translation keys.
- Editor entry is available from launch with no purchase, unlock, achievement, or credit cost.
- No painting or Workshop controls/placeholders are visible.

**Persistence/selection behavior:**

- Save current active character -> save succeeds, then queue appearance update.
- Save inactive character -> save succeeds, active runtime remains unchanged.
- Selecting a library row opens it for editing; it does not activate it.
- A separate `Use Character` action performs A6 selection.
- New character becomes selectable only after first save.
- Delete uses the A6 active-deletion transaction when required.
- UI remains open and shows actionable errors for save/load/delete/selection failures.

**Tests/scenarios:**

- Working copy changes preview but not disk/runtime active character.
- Save success resets dirty baseline.
- Save failure preserves working copy and dirty state.
- Discard restores saved data and preview.
- Dirty close/open/delete paths show exactly three choices.
- New unsaved character is neither indexed nor active.
- Duplicate uses fresh GUID and independent subsequent edits.
- Library row selection does not activate.
- `Use Character` activates only at fixed tick.
- Deterministic randomize: same seed/document/catalog -> same result.
- Randomize emits only known IDs and in-range values.
- Preview scene dependency scan finds no physics/gameplay authority.
- Every control label/action has a translation key.
- No deferred feature is advertised.

**Non-goals:** undo/redo for parameter edits, paint, thumbnails, Workshop, economy integration.

**Done when:** the full editor workflow is usable through real input with failure-safe
working-copy semantics.

---

### Task A9 — Phase A verification, performance, and handoff evidence

**Prerequisites:** A1–A8.

**Domain/unit suites:**

- All A1, A3, A5, A6 policy tests.
- Existing domain tests remain green.

**Headless scenarios:**

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

**Required scenario assertions:**

- `library_large_enumeration`: 500 synthetic directories, exactly one full document
  load/compile at startup, zero eager thumbnail renders, and work proportional to the
  requested page plus active entry.
- `character_swap_physics_invariant`: same seeded strike before and after swap; accepted pain,
  resulting velocity envelope, payout, and collision/rig Resources remain equal.
- `character_appearance_invalidation`: equal key gives zero renders; each relevant field
  change gives exactly one renderer/material mutation.
- `editor_window_restore`: all captured window fields restore, not only size/position.

**Real-input journey:**

`character_editor_create_use_and_react`

1. Open settings/panel through the real input path.
2. Enter the editor.
3. Create a character.
4. Set a name, change part colors and feature variants, randomize with a fixed seed.
5. Save.
6. Use Character.
7. Exit editor.
8. Verify restored shell/window state.
9. Strike the buddy through the real tool input path.
10. Verify the active appearance remains and the correct semantic reaction renders.
11. Restart and verify active selection persists.

**Manual Windows matrix:**

- Windows 10 and 11 standalone export.
- 100%, 125%, 150%, and 200% DPI.
- Single monitor and multi-monitor.
- Remove/disconnect the entry monitor while editor is open.
- Transparency available and opaque-fallback configurations.
- Minimum supported desktop usable area.
- Editor usability, readability, focus, global recovery, and default-buddy parity.
- MCP interactive pass per `AGENTS.md`, with evidence promoted to committed automation.

**Performance budgets:**

- No document/file work on physics tick.
- Equal render keys allocate and repaint nothing.
- Startup loads/compiles only the active document.
- Library paging does not scale with total character count beyond directory enumeration.
- Enter/exit editor does not rebuild the live buddy physics rig.
- Existing simulation zero-allocation budget remains unchanged.

**Commands before handoff:**

```bash
dotnet build
dotnet test
godot --headless --path . -- --scenario=editor_document_roundtrip --seed=1
godot --headless --path . -- --scenario=character_swap_physics_invariant --seed=1
godot --headless --path . -- --scenario=library_large_enumeration --seed=1
godot --headless --path . -- --journey=character_editor_create_use_and_react --seed=1
```

Use the repository's pinned Godot executable name/path where `godot` is not on `PATH`.
Run every new scenario, the complete affected regression suite, and the standalone Windows
matrix before promotion.

**Phase A exit gate:**

- All automated tests and the journey pass.
- Existing M3.5/M3.6/M4/M5 regressions pass.
- The owner accepts editor usability and default-document visual parity on real Windows.
- The multi-monitor restoration path passes.
- No Phase B/C type, field, file, or visible affordance exists except the null surface-underlay
  method explicitly allowed by this plan.
- Documentation reflects the implemented public contracts and schema.

## Phase B — Painting (deferred)

Phase B cannot be scheduled or partially implemented during Phase A.

### Task B0 — Schedule and source-of-truth gate

Resolve Phase B scheduling, update authoritative documents, and verify no Workshop dependency
is introduced. Reconfirm texture resolution, brush UX, memory budget, and package-size
interaction before code.

### Task B1 — Frontal ray-to-UV mapping

Engine-free analytic inverse UV for the trusted M3.5 sphere/capsule primitives, matching
Godot 4.6.1 primitive UV conventions. Verify once against real meshes, then lock tests for
center, silhouette, cap/side seam, miss, and mirror symmetry.

### Task B2 — CPU paint surface and stroke model

Per-part RGBA8 512×512 `Image`, brush size/hardness/color/eraser, stroke-command undo/redo,
and a measured memory cap with snapshots at a fixed documented cadence. CPU paint data is
the test oracle.

### Task B3 — Paint preview and underlay binding

Bind each CPU paint `ImageTexture` through the A2 `SetSurfaceUnderlay` seam. Update at most
once per rendered frame while stroking. Face and torso accent decals remain physically above
paint.

### Task B4 — Paint UI

Paint directly on the frontal preview, surface-under-brush part targeting, mirror toggle,
brush controls, undo/redo, and clear-part confirmation.

### Task B5 — Paint persistence

Schema bump adding only declared painted-part file references; PNGs in the character
directory. Enforce dimensions and byte caps before decode/after decode as appropriate.
Older documents migrate to an empty paint set.

### Task B6 — Verification

CPU-image hash change, undo restoration, pixel-identical roundtrip, oversize rejection,
paint-under-expression layer-order semantics, real brush feel, and paint-to-game fidelity.

**Phase B exit gate:** automated suites pass and owner accepts brush feel/fidelity.

## Phase C — File packages and Steam Workshop (deferred; requires M6)

Phase C cannot be scheduled before Milestone 6 and owner decision 6.

### Task C0 — Policy and source-of-truth gate

Resolve Workshop content-rating stance, report/hide policy, legal copy, and moderation
ownership into `DECISIONS.md`. Update requirements, architecture, test plan, and agent rules.

### Task C1 — Package format and validator

Versioned `.buddychar` ZIP:

- `manifest.json`: schema version, minimum app version, embedded character document,
  per-file SHA-256, author metadata.
- Declared PNG files only.
- Manifest <= 64 KB.
- Each texture <= 512×512 and <= 1 MB.
- Whole package <= 8 MB.
- Strict entry whitelist; reject traversal, absolute paths, duplicates, unexpected entries,
  links, and non-GUID local identities.
- Enforce compressed and uncompressed byte budgets before extraction/decode.
- Future-major and minimum-app-version gates.
- Validate completely in a staging directory before atomically importing.

The same package is used for non-Steam import/export.

### Task C2 — `IWorkshopService`

Interface in `src/Platform`; fully functional local directory emulator for development/CI.
Steam implementation in `DesktopBuddy.Steam` using Steamworks.NET UGC. Optional assembly,
queued idempotent operations, non-fatal offline behavior, legal-agreement status exposed.

### Task C3 — Publish/update flow

Validate before submit; auto-render preview from editor viewport; title, description,
visibility, legal agreement state; retryable idempotent failures; no partial upload.

### Task C4 — Subscribe/import flow

Download to staging, validate, import read-only entries keyed by Workshop item ID rather
than embedded GUID. A collision cannot shadow a local character or hijack active resolution.
`Duplicate to edit` creates a fresh local GUID. Unsubscribe removes cache and safely reverts
if active. Startup revalidates cached subscriptions.

### Task C5 — Moderation and local controls

Approved policy document, Steam report deep-link, local hide list in settings, no
auto-activation, visible quarantine reasons.

### Task C6 — Verification

Emulated publish/roundtrip, subscribe/import, tamper quarantine, duplicate-entry ZIP,
zip-slip, decompression budget, GUID collision, unsubscribe revert, hide list, offline
behavior. Manual installed-depot account A publish/account B subscribe/legal matrix.

**Phase C exit gate:** depot matrix and policy approval pass.

## Owner decisions

Resolved by the owner on 2026-08-02 and recorded in `docs/DECISIONS.md`:

| # | Decision | Resolution | Lands in |
| --- | --- | --- | --- |
| 1 | Feature axes/art direction | Eyes, brows, mouth, one body accent; type/offset/scale/color; no rotation or shape modifiers | A1, A3, A8 |
| 2 | Editor window strategy | Temporarily resize the same window, opaque while open, restore geometry/transparency | A7 |
| 3 | Expressions vs paint | Expressions always above paint and not user-suppressible | invariants, A4, B3 |
| 4 | Character Cloud scope | Character files local/Workshop only; progress remains sole Cloud file | A5, A6 |
| 5 | Editor access | Free from launch, no progression gate or Phase A achievements | A8 |
| 7 | Local library cap | Uncapped; lazy index and paged/virtualized list | A5, A8 |

Outstanding and deferred:

6. **Workshop content-rating stance and report/hide policy sign-off.** Resolve only when
Phase C is scheduled.

## Engineering resolutions locked by this review

These are implementation contracts derived from the approved product decisions, not new
owner-facing feature choices:

- Keep the M3.6 face quad; do not defer this decision to the exit gate.
- Add a torso-front accent decal quad.
- Keep `BuddyVisualProfile` trusted and separate from compiled character appearance.
- Extract `BuddyVisualRigView`; preview does not instantiate gameplay.
- Stable string feature IDs replace persisted atlas indices.
- Existing semantic expression map remains unchanged.
- Requested editor size is 960×720 with monitor recovery/clamping.
- Editor has a working copy and Save/Discard/Continue Editing behavior.
- Editor uses an explicit pause reason, never hidden-to-tray.
- Active selection applies at a fixed tick, then mutates progress and immediately saves.
- Phase A uses direct `ViewportTexture`s and no GPU readback/per-part texture baking.
- Phase A library rows are text/status only and render no eager thumbnails.

## Effort estimate

| Phase | Focused effort | Status | Ships alone? |
| --- | --- | --- | --- |
| A — Parametric editor | 4–6 weeks | Scheduled after M5, before M6 | Yes |
| B — Painting | 2–3 weeks | Deferred | Yes, on A |
| C — Packages/Workshop | 2–3 weeks after M6 | Deferred | Yes, on A; B optional |

The Phase A estimate includes the required visual-rig extraction, pause/window refactor,
persistence transaction work, automated coverage, and Windows matrix. It replaces the prior
3–5 week estimate, which did not account for those required architectural seams.

## Progress

- Original deferred-feature plan written 2026-07-14.
- Package-cap arithmetic, package path validation, Workshop GUID-collision handling,
  headless GPU boundaries, authoritative face-state coverage, and paused-editor processing
  were added during earlier review.
- Phase A scheduled by the owner on 2026-08-02 after Milestone 5 and before Milestone 6.
- M3.5 and M3.6 dependencies are complete and accepted.
- This revision resolves the handoff blockers found in the 2026-08-02 architecture review:
  source-document conflict, unsafe `BuddyVisualProfile` compiler target, live-presenter
  preview coupling, hidden-to-tray misuse, incomplete window restoration, undefined
  compositor topology, deferred face-mounting decision, underspecified schema, expression
  map duplication, incomplete persistence/selection transactions, and missing editor
  working-copy behavior.
- No Phase A production task has started. A0 is the next executable task.
