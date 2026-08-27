# Desktop Buddy — Player-Authored Workshop Assets Plan

Status: **Owner-requested research / implementation proposal — not yet an implementation authorization**  
Date: **2026-08-27**  
Planning branch: `plan/workshop-player-authored-assets`  
Base: Workshop v1 draft branch at `9308476e29937dc6fab18fa540f73ce88f72255b`  
Base-game / Workshop-owner AppID: **5114950**

This plan answers the owner request to support player-created Desktop Buddy content through Steam Workshop, including:

- models generated with Asset Forge;
- player-owned external 3D models that are mapped into Desktop Buddy;
- Buddy Studio cosmetics;
- Environment Decorator models;
- eventually every Buddy Studio customization slot, including face-detail content that is not naturally represented as a 3D mesh.

It deliberately does **not** authorize arbitrary mods, scripts, scenes, PCKs, DLLs, shaders, gameplay logic, collision, custom physics, or executable Workshop content.

The central design decision is:

> **Players may author arbitrary source content in a separate creator tool, but the shipped game consumes only a narrow canonical Desktop Buddy UGC format.**

The game should never load arbitrary Godot Resources or arbitrary third-party scene trees from Workshop.

---

## 1. Executive decision

### 1.1 Yes, the game needs changes

The existing Workshop v1 cannot safely be extended by merely adding `.glb` to its path whitelist.

Today:

- Workshop v1 intentionally accepts only JSON + PNG data packages;
- `BuddyGeneratedCosmeticRegistry` loads generated cosmetics as trusted `res://` Godot Resources;
- `GeneratedBuddyCosmeticResource` stores trusted `PackedScene`, `Texture2D`, and thumbnail Resource references;
- `BuddyVisualRigView` instantiates those trusted PackedScenes;
- `EnvironmentDecorationResource` likewise stores trusted generated `PackedScene`/`Texture2D` references;
- Asset Forge exports directly into trusted repository `assets/` and `data/` roots;
- the feature catalogues are composed from static trusted content at startup;
- Asset Forge v1 only has complete generated Buddy model paths for Glasses, Tops and Shoes; Hair, Headwear and generic Accessories remain planned category seams;
- eyes, brows and mouths use the `ParametricFaceCompositor` and renderer registry rather than the generated-mesh path.

Those assumptions are correct for developer-authored content, but they are the wrong trust model for Workshop models.

### 1.2 Do not ship the internal repository exporter as the player tool

The current Asset Forge application is a developer tool with repository-writing/export/maintenance behavior. Keep it.

Create a **player-facing Desktop Buddy Creator** application that reuses the deterministic generation pieces from `AssetForge.Core` but does not expose:

- repository root selection;
- repository mutation;
- trusted `.tres` generation;
- catalogue patching;
- Verify All/Delete Asset operations against source control;
- developer-only maintenance paths.

The two applications should share generation/canonicalization code but have separate outer shells:

```text
                     shared deterministic core
                     +------------------------+
                     | AssetForge.Core        |
                     | canonical mesh format  |
                     | image codecs           |
                     | validation/budgets     |
                     +-----------+------------+
                                 |
                  +--------------+--------------+
                  |                             |
                  v                             v
      Developer Asset Forge          Desktop Buddy Creator
      repository authoring           player-facing UGC tool
      trusted .tres export            safe package + Workshop publish
```

### 1.3 Never consume a player's original model directly in the game

A player may select an arbitrary model in Desktop Buddy Creator, but the Creator must **canonicalize** it before publishing.

The game consumes only the canonical output.

This has two large benefits:

1. Asset Forge-generated and externally-authored models converge on one runtime format.
2. The game does not need to support every glTF/FBX feature, material system, animation system, extension, hierarchy or DCC quirk.

---

## 2. Existing architecture we should reuse

### 2.1 Canonical Asset Forge GLB is already an excellent UGC target

`devtools/AssetForge.Core/GlbWriter.cs` already emits a deliberately tiny glTF 2.0 binary shape:

- one scene;
- one node;
- one mesh;
- one triangle primitive;
- `POSITION`;
- `NORMAL`;
- `TEXCOORD_0`;
- indices;
- one embedded binary buffer;
- no scripts;
- no Godot Resources;
- no scene logic.

This is much safer and simpler than accepting arbitrary Godot scenes.

The UGC format should formalize and strengthen this existing contract rather than replace it.

### 2.2 Current generated runtime already overrides model materials

`BuddyVisualRigView.AddGeneratedAsset` currently instantiates the trusted generated model, finds exactly one `MeshInstance3D`, and replaces its material with a Desktop Buddy-controlled material using the separately-authored albedo texture.

That behavior should become the runtime UGC rule:

> UGC geometry supplies geometry + UVs only. Desktop Buddy supplies the actual runtime material/shader policy.

Player models must not be able to bring arbitrary shaders or Godot materials into the game.

### 2.3 Existing Workshop staging remains the first boundary

Keep the Workshop v1 rule:

```text
Steam install/cache
        |
        v
bounded immutable incoming snapshot
        |
        v
package validation
        |
        v
local UGC library/cache
        |
        v
runtime loader
```

The current design-review finding that content-type detection reads directly from Steam's mutable cache should be fixed before player-model scope is built. The expanded system must snapshot exactly once and route/validate from that immutable snapshot.

### 2.4 Existing unknown-feature fallback is useful

Character compilation already resolves unknown feature IDs to a slot fallback and reports a warning. Preserve the original requested feature ID in the character document.

That gives UGC a desirable missing-content behavior:

- subscribed cosmetic available -> render it;
- UGC temporarily unavailable -> render safe built-in fallback;
- cosmetic becomes available again -> original character can resolve to it again without destructive migration.

---

## 3. Player-facing creator workflows

Desktop Buddy Creator should expose three authoring paths.

### 3.1 Generate from image — Asset Forge mode

Existing deterministic workflow, made player-safe:

```text
1024x1024 transparent PNG
        |
        v
choose Desktop Buddy category/template
        |
        v
AssetForge.Core generation
        |
        v
preview against real Buddy / room reference
        |
        v
canonical model + albedo
        |
        v
UGC package validation
        |
        v
Publish to Workshop
```

This is the easiest path for players and should be the first creator release.

### 3.2 Import my own model — Model Mapper mode

Players can use Blender, Maya, Blockbench, etc., then select their model in Desktop Buddy Creator.

Recommended input formats in the **creator tool only**:

- `.glb` / `.gltf` first;
- optional `.fbx` later.

Godot supports runtime glTF loading through `GLTFDocument` / `GLTFState`, and runtime FBX loading through `FBXDocument` / `FBXState`. This should be used only inside the Creator/importer boundary, not as permission for arbitrary Workshop scenes.

Creator workflow:

```text
external model
    |
    v
Creator imports into isolated preview
    |
    +-- reject animation/rig/lights/cameras
    +-- choose relevant mesh(es)
    +-- validate finite geometry
    +-- triangulate/flatten transforms
    +-- normalize orientation
    +-- normalize unit scale
    +-- require/generate UV0
    +-- choose/bake one albedo texture
    +-- choose Desktop Buddy slot/category
    +-- map anchor/pivot/size
    |
    v
convert to Desktop Buddy CanonicalMesh
    |
    v
re-export through project-owned GlbWriter
    |
    v
canonical GLB + PNG + JSON only
```

Important: even if the original source is an arbitrary glTF/FBX hierarchy, the published output is **not** that original hierarchy. It is a sanitized Desktop Buddy canonical asset.

### 3.3 Face Detail / Decal mode

Eyes, brows and mouths currently use 2D parametric rendering into the face compositor. Do not force these through 3D models.

Provide a separate safe PNG/decal authoring path.

Initial decal candidates:

- Brows;
- Face details;
- Torso Accessories/accents.

Eyes and Mouth should be a later semantic-frame contract because the current game changes them based on blink/expression state.

Possible future package semantics:

```text
eyes/
  open.png
  blink.png
  optional_pupil_mask.png

mouth/
  neutral.png
  happy.png
  hurt.png
  optional_open.png
```

The exact expression-state contract must be explicitly defined before enabling those slots. A static image that destroys Buddy's reactions would be a regression.

---

## 4. Canonical model contract

The game should accept only a project-defined subset of GLB.

### 4.1 Canonical GLB v1

Required:

- glTF 2.0 binary (`.glb`);
- exactly one scene;
- exactly one node;
- exactly one mesh;
- exactly one primitive;
- primitive mode = triangles;
- indexed geometry;
- `POSITION` float VEC3;
- `NORMAL` float VEC3;
- `TEXCOORD_0` float VEC2;
- exactly one binary buffer contained in the GLB;
- all values finite;
- all indices in range;
- non-empty geometry;
- bounded world-space coordinates after canonicalization.

Forbidden in canonical UGC GLB v1:

- external URIs;
- external buffers;
- embedded images;
- materials;
- textures;
- samplers;
- skins;
- joints;
- morph targets;
- animations;
- cameras;
- lights;
- physics metadata;
- extras used as executable/config channels;
- arbitrary glTF extensions;
- multiple nodes/hierarchies;
- multiple primitives/material slots.

The current `GlbWriter` is already close to this output. `ValidateSingleMesh` must be upgraded from only checking one mesh/node to fully validating the canonical subset above.

### 4.2 Why material-free GLB

Keep albedo as an independent whitelisted PNG:

```text
model.glb
albedo.png
```

At runtime Desktop Buddy creates a known `StandardMaterial3D`/look-library material, applies the validated albedo and any allowed player tint, and ignores the original author's material model entirely.

That keeps:

- rendering consistent with Desktop Buddy;
- shaders trusted;
- render priority trusted;
- transparency rules trusted;
- outline policy trusted;
- lighting policy trusted.

### 4.3 Recommended initial model budgets

These should be treated as engineering defaults to profile before lock-in, not owner-fixed tuning yet.

Suggested first budgets:

| Content | Triangles | Vertices | GLB bytes | Runtime albedo |
| --- | ---: | ---: | ---: | ---: |
| face/head attachment | 20,000 | 30,000 | 4 MiB | max 1024x1024 PNG |
| torso/paired foot replacement | 30,000 | 45,000 | 6 MiB | max 1024x1024 PNG |
| environment decoration | 40,000 | 60,000 | 8 MiB | max 1024x1024 PNG |

Keep the complete Workshop package inside the existing **16 MiB incoming snapshot budget** initially if profiling shows that is practical.

The Creator should warn well below the hard game-side limits.

The game remains authoritative: hand-editing a published package must not bypass limits.

---

## 5. Workshop package types

Do not use `.tres` or `.tscn` in player packages.

### 5.1 Buddy model cosmetic

```text
content/
  manifest.json
  asset.json
  model.glb
  albedo.png
  thumbnail.png
preview.png              # Steam preview, outside imported content root as today
```

`asset.json` should contain only typed data such as:

```json
{
  "schemaVersion": 1,
  "assetKind": "buddy-model-cosmetic",
  "displayName": "Round Glasses",
  "slot": "glasses",
  "applicationMode": "attachment",
  "anchor": "eye-group",
  "secondaryAnchor": null,
  "defaultScale": 1.0,
  "defaultOffsetX": 0.0,
  "defaultOffsetY": 0.0,
  "mirrorSecondary": false,
  "hidesHair": false,
  "allowPlayerTint": true,
  "canonicalAssetHash": "..."
}
```

Do **not** trust a player-provided feature/content ID.

The runtime identity should be derived from Workshop provenance, for example:

```text
ugc.<PublishedFileId>.asset
```

If future packages contain multiple assets, add a validated package-local slug:

```text
ugc.<PublishedFileId>.<assetSlug>
```

This makes collision with shipped/trusted content structurally impossible.

### 5.2 Environment decoration

Same model/albedo structure with an environment definition:

```json
{
  "schemaVersion": 1,
  "assetKind": "environment-decoration",
  "displayName": "Beanbag",
  "category": "sofa",
  "anchorKind": "floor",
  "renderBand": "behind-buddy-floor",
  "allowsRotation": true,
  "rotationStepDegrees": 15,
  "pivotX": 0.5,
  "pivotY": 1.0,
  "defaultScale": 1.0,
  "canonicalAssetHash": "..."
}
```

UGC decorations remain visual-only:

- no collision;
- no rigid body;
- no grab component;
- no damage;
- no scripts;
- no gameplay signals.

Player-authored light sources should be deferred until a separate bounded light-profile policy is tested. An unrestricted light range/count is an easy performance abuse path.

### 5.3 Face/decal cosmetic

Data-only JSON + PNG; no GLB.

For static decal-compatible slots:

```text
content/
  manifest.json
  asset.json
  decal.png
  thumbnail.png
```

Eyes/Mouth receive a separate semantic-frame schema later rather than pretending a static decal is equivalent to their current reactive renderer.

---

## 6. Runtime loading — no `GD.Load` for UGC

The existing prohibition against `GD.Load` / `ResourceLoader` on Workshop content should remain.

After the canonical GLB has passed byte-level structural validation, load it with the runtime glTF API rather than Godot's imported-project Resource pipeline.

Recommended service boundary:

```text
IPlayerAssetGeometryLoader
    LoadCanonicalModelAsync(ValidatedCanonicalModel model)
        -> RuntimeModelHandle
```

Godot implementation:

1. receive only an already validated local canonical GLB;
2. use `GLTFDocument` + `GLTFState` from the trusted game assembly;
3. generate the scene;
4. require the result to contain exactly the expected single `MeshInstance3D`;
5. discard/reject anything else defensively;
6. apply the trusted Desktop Buddy material;
7. disable shadows/collision/processing as appropriate;
8. never attach imported scripts or imported game behavior.

For PNGs, use bounded byte reads + `Image.LoadPngFromBuffer` / project-owned texture creation rather than loading an arbitrary Godot Resource from disk.

---

## 7. Dynamic runtime catalogue — the largest game architecture change

The current trusted catalogues are effectively static.

Player Workshop content needs a runtime-composed content layer.

### 7.1 Buddy Studio catalogue provider

Introduce an interface such as:

```text
ICharacterFeatureCatalogProvider
    Current -> immutable CharacterFeatureCatalog snapshot
    Changed event
```

Composition:

```text
Shipped definitions
    + trusted repository Asset Forge definitions
    + validated active Workshop cosmetic definitions
        = current immutable runtime snapshot
```

Do not make `CharacterFeatureCatalog` itself a mutable global dictionary. Preserve immutable snapshots and replace the snapshot when UGC changes.

Update consumers that currently hold a one-time catalogue:

- `CharacterStore`;
- character editor/session/compiler composition;
- `BuddyGeneratedCosmeticRegistry` replacement/composition seam;
- `BuddyCosmeticVisualCatalog`;
- Buddy Studio tile population;
- character preview/runtime selection.

### 7.2 Visual registry

Do not force a `GeneratedBuddyCosmeticResource` to represent untrusted runtime assets.

Create a neutral runtime model:

```text
BuddyVisualAssetDefinition
    Id
    Slot
    Anchor
    SecondaryAnchor
    ApplicationMode
    DisplayName
    Thumbnail
    RuntimeModelHandle / geometry reference
    Trusted-vs-UGC provenance
```

Then compose visual providers:

```text
BuiltInProceduralBuddyVisualProvider
TrustedGeneratedBuddyVisualProvider
WorkshopBuddyVisualProvider
```

`BuddyCosmeticVisualCatalog` should consume the composed provider rather than directly referencing `BuddyGeneratedCosmeticRegistry.Current`.

### 7.3 Environment registry

Similarly replace the static `Launch + Generated` composition assumption with:

```text
Launch
+ Trusted Asset Forge generated
+ Active validated Workshop decorations
= Runtime Environment catalogue snapshot
```

The Room Decorator should not need separate UGC browse/purchase code once the definitions are represented through its normal domain catalogue.

---

## 8. Buddy Studio slot roadmap

“All cosmetics” should be supported, but not all slots should use the same asset representation.

| Slot | Player format | Mapping | Recommended phase |
| --- | --- | --- | --- |
| Glasses | model | EyeGroup attachment | first |
| Hair | model | HeadCrown attachment | first model expansion |
| Headwear | model | HeadCrown attachment | first model expansion |
| Nose | model | HeadFront attachment | first model expansion |
| Ears | model | paired LeftEar/RightEar | first model expansion |
| Tops | model | TorsoBody part replacement | first |
| Shoes | model | paired foot replacement | first |
| Accessories | model or decal | explicit torso/head anchor policy | second |
| Face | decal/model overlay policy | HeadFront | second |
| Brows | decal | face compositor | decal phase |
| Eyes | semantic decal frames | face compositor + blink/look behavior | later |
| Mouth | semantic decal frames | face compositor + mood/pain behavior | later |

Asset Forge v1 currently has complete generated model authoring for Glasses/Tops/Shoes and explicitly leaves Hair/Headwear/Accessories as post-v1 seams. Player UGC is a good reason to finish those category templates, but it should happen through the same canonical model contract rather than bespoke Workshop rendering code.

---

## 9. Player UGC ownership/economy

Recommended rule:

> **Subscribed/active Workshop cosmetics are free to equip; they do not enter the credits economy.**

Reasons:

- player-created content should not invent prices in the game's progression economy;
- allowing Workshop metadata to create economy entries would make untrusted data affect progression;
- user-authored IDs must never enter the trusted generated commerce catalogue;
- Workshop subscription is the availability boundary.

Official/first-party Asset Forge content continues using the current permanent-credit-unlock path.

This keeps a clean distinction:

```text
first-party cosmetic -> trusted catalogue + credits ownership
Workshop cosmetic    -> validated subscription/local UGC availability
```

If the owner wants Workshop cosmetics to cost credits later, define one fixed game-controlled price policy by category. Never accept player-specified prices as authoritative.

---

## 10. Subscription and offline semantics

Player assets should behave differently from imported Buddy/room snapshots.

Recommended:

- Buddy/room shares remain **explicit imported copies** as Workshop v1 currently defines.
- reusable model/decal assets become **subscription-scoped UGC library entries**.
- after successful validation, cache a verified canonical copy under `user://ugc/...` so subscribed assets continue working offline.
- while Steam is offline, use the last verified subscription state/cache.
- after a confirmed unsubscribe, deactivate the UGC definition; optionally retain cache bytes for a bounded cleanup period.
- characters/environments that reference missing UGC retain their IDs but use a safe visual fallback/placeholder.
- resubscribing restores the content without rewriting character saves.

Do not silently leave an unsubscribed cosmetic permanently active merely because its old bytes still exist locally.

---

## 11. Character packages that use UGC cosmetics

A shared Buddy configuration can eventually depend on player cosmetic Workshop items.

Add to the Buddy share manifest/payload:

```text
requiredWorkshopItems: [123..., 456...]
```

Import flow:

1. validate Buddy package;
2. inspect referenced UGC IDs;
3. if dependencies are installed/validated, compile normally;
4. if dependencies are missing, import the Buddy anyway with safe built-in fallbacks and show “Missing Workshop cosmetics”; 
5. offer the Steam item pages/subscription flow.

Future polish can mirror these relations into Steam Workshop item dependencies (`AddDependency`) so the web page also exposes required items. Steam documents Workshop-item dependencies as soft dependencies; the game still owns actual validation/use policy.

Do not embed arbitrary external cosmetic models inside a Buddy character share by default. Reusable assets should remain reusable Workshop items.

---

## 12. Steam Workshop taxonomy

Keep `Ready-to-Use` Workshop.

Recommended visible tag categories:

### Creation Type

- `Room Painting`
- `Buddy`
- `Cosmetic`
- `Decoration`

### Cosmetic Slot

- `Glasses`
- `Hair`
- `Headwear`
- `Nose`
- `Ears`
- `Top`
- `Shoes`
- `Accessory`
- `Face Detail`
- `Brows`
- `Eyes`
- `Mouth`

### Decoration Type

- `Lamp`
- `Sofa`
- `Table`
- `Plant`
- `Painting`
- `Other Decoration` only if a safe generic placement policy exists.

Keep schema/generator information in developer metadata/manifest, not as public-facing version tags.

Steam requires configured browsing tags to exactly match tags submitted by the game/tool.

---

## 13. Creator distribution on Steam

Valve explicitly supports an upload/editor application separate from the game and allows its AppID under the base game's Workshop **App Publish Permissions**.

Two viable shipping models:

### Recommended initial model — creator executable in the Desktop Buddy depot

Ship a separate executable/launch option with Desktop Buddy:

```text
Desktop Buddy
Desktop Buddy Creator
```

Advantages:

- no second creator AppID required initially;
- same entitlement/application identity;
- easiest developer/test setup;
- still process-isolated from the game;
- internal developer Asset Forge remains unshipped.

### Later model — separate Steam Tool/App

If Creator deserves its own Steam application later:

- create Creator AppID;
- add it under base AppID `5114950` -> Workshop Configuration -> App Publish Permissions;
- configure Cloud quota for both AppIDs;
- initialize Steam under Creator AppID;
- create/publish Workshop items against owner `5114950`.

The existing runtime-AppID vs Workshop-owner split from PR #41 already anticipates this architecture.

The future Demo can use the same pattern once its AppID exists.

---

## 14. Security model

Player models are untrusted even if they were produced by Desktop Buddy Creator, because authors can modify the Workshop files after export.

Game-side validation is mandatory on every install/update.

### 14.1 Package rules

Reject:

- path traversal;
- absolute paths;
- links/reparse points;
- undeclared files;
- duplicate paths;
- wrong hashes;
- byte-limit violations;
- unsupported schemas;
- unknown asset kinds;
- invalid/unsupported slot mappings;
- non-finite transform values;
- model/decal dimensions outside policy.

### 14.2 Model rules

Reject canonical GLB containing any feature outside Section 4.

Do the byte-level GLB/JSON validation **before** asking Godot to build a runtime scene.

After Godot generates the scene, validate the resulting node tree again before attaching it.

### 14.3 Runtime authority rules

UGC may never create or change:

- collision shapes;
- RigidBody2D/RigidBody3D;
- mass;
- joints;
- buddy rig sizes;
- connectors;
- pain/damage values;
- economy prices/balances;
- tool behavior;
- scripts;
- shaders;
- native extensions;
- network behavior;
- save paths;
- process callbacks from author code.

This remains a **visual-content system**, not a generalized mod API.

---

## 15. Performance policy

A subscribed Workshop library can contain many assets, so do not instantiate every model at startup.

Recommended runtime model:

- validate metadata on Worker threads;
- catalogue stores lightweight definitions;
- thumbnails are lazy/cached;
- model geometry loads only when selected/equipped/placed;
- maintain an LRU cache of decoded runtime model handles;
- unload unused UGC geometry after a memory budget is exceeded;
- never decode model/PNG data on the physics tick;
- Godot texture/mesh object creation remains on the main thread.

Room Decorator should instantiate only placed decorations plus currently-previewed catalogue item(s), not an entire subscribed model library.

---

## 16. Required refactors before player-model implementation

The design review of Workshop v1 identified issues that should be fixed first because UGC models amplify them:

1. **Snapshot once before content detection.** Never inspect Steam's mutable install folder to determine package type.
2. **Fix CreateItem cancellation semantics.** Do not allow caller cancellation to create orphan empty Workshop items.
3. **Separate runtime and Workshop-owner AppID fields.** Do not repurpose `_appId` during transport initialization.
4. **Move heavy import/export hashing/PNG/JSON/model work off the Godot main thread.**
5. **Add pure tests for callback state/cancellation/late callbacks.**
6. **Replace Workshop UI scene-tree polling with explicit/narrow composition interfaces.**
7. **Make transport availability/error state typed rather than returning “empty subscriptions” on failure.**

These are not cosmetic cleanups for player UGC; they are prerequisites for a larger Workshop surface.

---

## 17. Proposed implementation architecture

Suggested new boundaries (names are illustrative):

```text
Domain/UGC
  PlayerAssetKind
  PlayerAssetDescriptor
  BuddyUgcCosmeticDefinition
  EnvironmentUgcDecorationDefinition
  CanonicalModelPolicy
  PlayerAssetValidationResult

Persistence/UGC
  PlayerAssetPackageReader
  PlayerAssetLibraryStore
  PlayerAssetSubscriptionStateStore
  PlayerAssetProvenanceStore

Sharing
  PlayerAssetShareExporter
  PlayerAssetShareImporter
  WorkshopPlayerAssetCoordinator

Platform/Rendering
  IPlayerAssetGeometryLoader
  GodotCanonicalGlbLoader
  PlayerAssetTextureLoader

Character runtime
  ICharacterFeatureCatalogProvider
  RuntimeCharacterFeatureCatalogProvider
  IVisualAssetProvider
  WorkshopBuddyVisualProvider

Environment runtime
  IEnvironmentDecorationCatalogProvider
  RuntimeEnvironmentDecorationCatalogProvider
  WorkshopEnvironmentVisualProvider

Creator
  DesktopBuddy.Creator.Core / shared AssetForge.Core pieces
  GeneratedImageAssetWorkflow
  ExternalModelMapperWorkflow
  DecalWorkflow
  UGC package preview/validator
  Workshop publisher
```

Do not introduce a global service locator. Compose providers explicitly from `Bootstrap` / feature composition roots.

---

## 18. Implementation phases

### UGC-0 — owner/source-of-truth gate

Before production code:

- authorize the new canonical model/decal Workshop formats;
- supersede the current “no Workshop meshes” rule only for the exact canonical format;
- confirm Workshop-cosmetic economy policy;
- confirm Creator distribution model;
- confirm first public UGC categories.

### UGC-1 — canonical model format + hostile validator

- strengthen `GlbWriter.ValidateSingleMesh` into full canonical validation or add `CanonicalGlbPolicy`;
- validate finite floats, accessors, indices, buffer bounds and forbidden glTF sections;
- add size/triangle/vertex budgets;
- package schema + exact whitelist;
- fuzz/adversarial fixtures.

No Workshop/UI yet.

### UGC-2 — runtime GLB loader

- `GLTFDocument` runtime loader behind `IPlayerAssetGeometryLoader`;
- require exactly one generated `MeshInstance3D`;
- trusted material override;
- no scripts/physics/lights;
- lazy model cache;
- headless/runtime scenarios.

### UGC-3 — local UGC library + dynamic catalogues

- validated local asset store;
- runtime catalogue snapshot providers;
- collision-free `ugc.<WorkshopId>...` IDs;
- add/remove/reload behavior;
- missing-content fallbacks;
- no Steam dependency yet.

### UGC-4 — first Buddy model vertical slice

Recommended first slice: **Glasses**.

- Creator package export;
- local package import;
- Buddy Studio tile;
- equip/save/restart;
- missing subscription fallback;
- re-enable after asset returns.

Then Tops/Shoes because the trusted Asset Forge model path already exists for them.

### UGC-5 — Environment decoration vertical slice

Recommended first slice: Sofa/Table generic static floor decoration.

- no lights;
- no collision;
- subscription-scoped catalogue entry;
- placement/save/restart/missing-content placeholder behavior.

### UGC-6 — Desktop Buddy Creator player mode

- separate safe application shell;
- image-generation workflow using AssetForge.Core;
- UGC package export;
- no repository-writing tools;
- safe preview against trusted Buddy/room reference;
- publish title/description/tags/legal-agreement flow.

### UGC-7 — external model mapper

- creator-only arbitrary glTF import;
- model selection/flattening;
- canonicalization;
- mapping UI for anchor/pivot/scale/orientation;
- re-export via project-owned canonical GLB writer;
- same package/runtime validator as generated models.

### UGC-8 — remaining 3D Buddy slots

- Hair;
- Headwear;
- Nose;
- Ears;
- Accessories where a clear model anchor policy exists.

Extend trusted Asset Forge templates at the same time so first-party and player authoring share contracts.

### UGC-9 — decal slots

- face/accessory static decals first;
- brows;
- eyes only after blink/look semantic frame contract;
- mouth only after expression/pain semantic frame contract.

### UGC-10 — Workshop subscription integration

- new Creation Type tags;
- Workshop item query/detail metadata sufficient to identify player assets;
- auto-refresh active subscribed asset catalogue;
- update/revalidation;
- offline cached operation;
- unsubscribe/deactivate flow.

### UGC-11 — Buddy package dependencies

- `requiredWorkshopItems` in Buddy shares;
- missing dependency UX;
- optional Steam Workshop `AddDependency` integration.

### UGC-12 — hardening/release matrix

- malformed/crafted GLBs;
- decompression/image bombs;
- huge coordinate bounds;
- NaN/Infinity geometry;
- invalid indices;
- update while equipped/placed;
- unsubscribe while referenced;
- offline startup;
- hundreds of subscribed metadata entries;
- memory/cache budgets;
- two-account author/consumer flow;
- Creator AppID/demo cross-app flows when those AppIDs exist.

---

## 19. Testing requirements

### Pure/domain tests

- canonical GLB accepts only project writer output;
- reject every forbidden glTF top-level feature;
- buffer/accessor overflow and out-of-range indices;
- finite coordinate/UV/normal checks;
- triangle/vertex/byte limits;
- package path/hash/size/schema validation;
- UGC ID derivation cannot collide with trusted IDs;
- slot/anchor/application-mode combinations are closed enums;
- player metadata cannot create prices/gameplay values.

### Persistence tests

- Steam folder -> one immutable snapshot -> validation;
- update creates new verified revision atomically;
- corrupt update keeps last good cached version unavailable/available according to explicit policy;
- unsubscribe deactivates definition without corrupting character/environment saves;
- stale caches clean safely.

### Godot runtime tests

- canonical GLB creates exactly one trusted-rendered mesh;
- material is always game-owned;
- no imported node can process or collide;
- Glasses/Hair/Headwear/Nose/Ears anchor placement;
- Torso/foot replacement preserves physics and paint hide/restore contract;
- Environment UGC stays visual-only;
- lazy loading/cache release;
- face/decal compositing.

### End-to-end Workshop tests

- player generates asset in Creator;
- publishes;
- second account subscribes;
- game validates and displays item;
- equip/place;
- save/restart;
- offline reuse;
- author updates item;
- subscriber receives/revalidates update;
- unsubscribe -> safe fallback;
- resubscribe -> restoration;
- maliciously modified Workshop payload -> quarantine/disable, never runtime load.

---

## 20. Steamworks changes when this is implemented

Current base Workshop settings can stay Ready-to-Use.

Add visible tags before live testing player assets:

- `Cosmetic`
- `Decoration`
- slot/category tags for the public categories enabled in that release.

If Desktop Buddy Creator later receives its own AppID:

- add Creator AppID under AppID `5114950` -> Workshop Configuration -> App Publish Permissions;
- configure Cloud quota for Creator AppID as well as base game;
- publish the Steamworks setting changes.

Valve explicitly supports a separate editing/publishing application publishing into a base application's Workshop.

---

## 21. Recommendation

Build this feature, but **do not turn Workshop into a general mod loader**.

The best long-term architecture is:

```text
Player source
  PNG generated in Creator
  OR external Blender/glTF model
        |
        v
Desktop Buddy Creator
  import / map / sanitize / canonicalize
        |
        v
Desktop Buddy canonical UGC package
  JSON + canonical GLB + PNG only
        |
        v
Steam Workshop
        |
        v
immutable snapshot
        |
        v
strict game-side validation again
        |
        v
runtime UGC catalogue
        |
        +--> Buddy Studio visual provider
        |
        +--> Environment Decorator visual provider
```

This gives players substantial creative freedom while preserving the most important existing Desktop Buddy architectural guarantee:

> **Untrusted player content may change appearance, but never game code, physics, economy authority, save paths, or gameplay behavior.**

It also lets first-party Asset Forge content and community content converge on the same canonical geometry format instead of maintaining two rendering systems.

---

## 22. Owner decisions required before UGC-0 closes

Recommended defaults are shown in **bold**.

1. Workshop cosmetics economy:
   - **free while subscribed/active**;
   - or fixed game-controlled category price.

2. Creator distribution:
   - **separate `Desktop Buddy Creator` executable shipped in the main Desktop Buddy depot first**;
   - later separate Steam Tool/App if useful.

3. First public player-model categories:
   - **Glasses + Tops + Shoes + one static Environment category first**;
   - then Hair/Headwear/Nose/Ears/Accessories.

4. Unsubscribe behavior:
   - **deactivate UGC definition after confirmed unsubscribe, preserve character/reference IDs and use fallback; retain cache only as implementation/offline cache**;
   - or permanently import/copy every UGC asset like current room shares.

5. External model support:
   - **only through Creator canonicalization; raw arbitrary Workshop GLB is never accepted**.

6. Reactive face content:
   - **defer player Eyes/Mouth until semantic blink/expression frames are specified**;
   - static Brows/face decals can arrive earlier.
