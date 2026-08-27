# Desktop Buddy — Workshop UGC Platform Implementation Plan

Status: **Owner-directed, agent-ready implementation plan — implementation not started on this branch**  
Date: **2026-08-27**  
Planning branch: `plan/workshop-player-authored-assets`  
Base: Steam Workshop v1 draft branch `plan/godotsteam-workshop-social-features` at `9308476e29937dc6fab18fa540f73ce88f72255b`  
Base-game / Workshop-owner AppID: **5114950**

This document supersedes the earlier conservative player-authored-asset proposal on this branch.

The owner clarified that Steam Workshop is intended to be a primary long-term content pillar, in the spirit of physics-sandbox communities such as People Playground and Mutilate-a-Doll 2. Player content must therefore be capable of doing substantially more than replacing visuals.

The product direction is:

> **Give creators broad control over models, collisions, mass, friction, bounce, gravity, grabbing, joints, lights, damage interactions, spawning, triggers, and custom behavior, while keeping the default Workshop path isolated from the host OS and from arbitrary Godot/.NET execution.**

Desktop Buddy should be moddable as a sandbox, not merely skinnable.

The implementation must still protect the player's machine, save files, Steam credentials, game process, progression, and future leaderboards from an untrusted Workshop item.

---

# 1. Product principles

## 1.1 Workshop is a first-class product system

Workshop is not an afterthought attached to Paint Room or Buddy Studio.

It should eventually support:

- room paintings;
- complete Buddy configurations and paint;
- player-authored Buddy cosmetics;
- player-authored 3D models;
- physics props;
- room decorations;
- lights;
- weapons and tools built from reusable behavior components;
- multi-body contraptions;
- joints and links;
- reactive objects;
- scripted objects;
- reactive face cosmetics;
- packs containing several related assets;
- local creator testing before publication;
- Steam Workshop subscription/update workflows;
- future demo -> base-game cross-app consumption.

Do not design the runtime catalogue as if the number of UGC entries will remain small.

## 1.2 Maximum useful power, not arbitrary host access

Creators should be able to implement sophisticated gameplay behavior.

They do **not** need unrestricted access to:

- the player's filesystem;
- network sockets;
- environment variables;
- process launching;
- Steam credentials;
- arbitrary reflection;
- arbitrary Godot scene-tree traversal;
- arbitrary CLR assemblies;
- arbitrary GDScript;
- native libraries.

Those capabilities are unrelated to making a good sandbox item and turn a subscribed Workshop item into arbitrary code execution.

The default Workshop ecosystem should therefore expose a large, versioned **Desktop Buddy UGC API** rather than the host process itself.

## 1.3 Two creator layers

The platform should deliberately support both non-programmers and programmers.

### Layer A — Components + Behavior Graph

A visual editor exposes physics and gameplay components:

- body type;
- collision shapes;
- mass;
- damping;
- friction;
- restitution;
- gravity scale;
- continuous collision detection;
- grabbable state;
- lights;
- emitters;
- damage response;
- health/destruction;
- timers;
- collision triggers;
- use/activate triggers;
- force/impulse actions;
- spawn actions;
- status effects;
- links/signals;
- joints;
- simple conditions;
- state variables.

This is the MaD2-like path: powerful behavior is assembled without source code.

### Layer B — Sandboxed Lua

Advanced creators may author `*.lua` behavior scripts.

Lua receives only Desktop Buddy host APIs and immutable event data. It must not receive raw Godot `Node`, `GodotObject`, `SceneTree`, `RigidBody2D`, filesystem, network, CLR, or Steam objects.

The initial scripting implementation should use a managed Lua runtime behind `IUgcScriptEngine`. The preferred first implementation is MoonSharp configured as a hard sandbox, subject to a dependency/license/version spike in UGC-1. If that spike fails, keep the interface and implement the same contract with another embeddable interpreter rather than exposing CLR scripting.

The script API must be powerful enough to create weapons, traps, thrusters, timers, reactive props, simple AI-like controllers, projectile systems and contraptions.

## 1.4 An explicitly unsafe full-code tier is not part of the first implementation

People Playground's ecosystem demonstrates the creative reach of unrestricted C# mods, but in-process CLR mods can also read files, open sockets and execute arbitrary host code.

Do not accidentally make every Ready-to-Use Workshop subscription equivalent to downloading an executable.

If the owner later wants an explicit **Unsafe Code Mods** tier, treat it as a separate product feature with one of:

- a separately sandboxed/out-of-process host; or
- a very explicit user opt-in model that clearly states it grants arbitrary local-code execution.

Do not mix that future tier into the default UGC loader.

---

# 2. Lessons from comparable sandbox Workshops

## 2.1 People Playground

People Playground's Workshop demonstrates the scale possible when user content is a central product system. Steam currently exposes hundreds of thousands of ready-to-use entries, including a dedicated Mods category.

Its community ecosystem includes contraptions and code mods capable of altering behavior substantially.

Desktop Buddy should borrow the **product lesson** — a broad, documented creator surface compounds replayability — without requiring the same unrestricted in-process code model for the default Workshop path.

Reference:

- https://steamcommunity.com/workshop/about/?appid=1118200
- https://steamcommunity.com/app/1118200/workshop/

## 2.2 Mutilate-a-Doll 2

MaD2 demonstrates another valuable pattern: many useful custom behaviors can be represented as item properties and composable triggers rather than bespoke code.

Its public patch notes document trigger-style behaviors for forces, item spawning, radius effects, joints, lights, timers and linked behavior.

Desktop Buddy should expose the same kind of creator-friendly vocabulary through a typed component/behavior model, then let Lua fill the gaps for genuinely custom logic.

References:

- https://steamcommunity.com/workshop/about/?appid=665370
- https://steamcommunity.com/app/665370/allnews/

These references are design evidence only. Do not copy item names, code, UI, assets or implementation details.

---

# 3. Existing Desktop Buddy architecture to reuse

## 3.1 Asset Forge Core remains valuable

`devtools/AssetForge.Core` already owns deterministic source-image processing, canonical geometry, UV generation, GLB writing, recipe hashing, thumbnails and validation.

The UGC system should reuse its **pure generation/canonicalization logic**, not its repository-mutating exporter.

## 3.2 Existing canonical GLB is the preferred runtime model contract

`GlbWriter` already emits a deliberately small glTF 2.0 subset:

- one scene;
- one node;
- one mesh;
- one primitive;
- triangles only;
- POSITION;
- NORMAL;
- TEXCOORD_0;
- indices;
- embedded binary buffer.

Formalize that subset as `DesktopBuddyCanonicalMeshV1`.

Player input may begin as a complex Blender/Blockbench/glTF model in the Creator, but publication must rewrite the selected geometry into this canonical subset.

## 3.3 Do not use Godot Resource loading for Workshop content

Trusted developer Asset Forge content currently uses `.tres`, `PackedScene`, `Texture2D` Resource references and static `res://` catalogues.

Workshop content must use a separate runtime path.

Never call `GD.Load`, `ResourceLoader.Load`, `PackedScene.Instantiate`, or load `.tres` / `.tscn` from a Workshop/local UGC directory.

## 3.4 Prefer a canonical GLB reader over runtime scene import

Because the canonical GLB subset is intentionally tiny, the game should implement a pure `CanonicalGlbReader` that returns validated arrays:

- positions;
- normals;
- UV0;
- indices;
- bounds;
- triangle/vertex counts.

The main-thread presentation adapter then creates an `ArrayMesh` from already-validated arrays.

This is safer and more deterministic than handing a Workshop GLB to Godot's general scene importer and then trying to reject unexpected generated nodes after the fact.

The **Creator** may use Godot's general glTF/FBX import facilities as an authoring convenience before canonicalization. The game runtime does not.

## 3.5 Existing 2D physics remains authoritative

Desktop Buddy's physical sandbox is 2D while its current presentation is 3D.

UGC follows the same split:

```text
2D RigidBody/StaticBody + explicit 2D collision shapes
                      |
                      v
              runtime entity identity
                      |
                      v
              3D visual presentation
```

A creator's 3D mesh does not automatically become a physics collision mesh.

## 3.6 Existing grab and impact seams are useful

The current game already:

- grabs general `RigidBody2D` bodies through the grab tether;
- uses collision layers/picking rather than hard-coded tool classes for every contact;
- attributes impacts through `IImpactSource`;
- centralizes pain/economy/mood application in `InteractionDamageComponent`;
- stores physical values such as mass, damping and bounce in authored data for shipped loose objects.

UGC should join those seams rather than create a second physics engine.

---

# 4. Repository architecture

Introduce a shared pure managed project:

```text
domain/
  DesktopBuddy.Ugc.Core/
    DesktopBuddy.Ugc.Core.csproj
    Packages/
    Models/
    Physics/
    Behavior/
    Validation/
    Identity/
    Hashing/
```

It is referenced by:

```text
DesktopBuddy game
Desktop Buddy Creator
UGC unit tests
```

It must not depend on Godot.

Game-side adapters live under:

```text
src/Ugc/
  Catalog/
  Persistence/
  Runtime/
  Physics/
  Presentation/
  Behavior/
  Workshop/
  Ui/
```

Creator-side code lives under:

```text
devtools/DesktopBuddyCreator/
```

Do not add UGC responsibilities to `BuddyGeneratedCosmeticRegistry`, `EnvironmentDecorationRegistry`, `WorkshopBootstrap`, `LooseObjectProfile`, or `InteractionDamageComponent` directly when a focused adapter/service can own them.

---

# 5. Package model

## 5.1 Package container

Steam content remains folder-based.

One Workshop item may contain one asset or a pack of assets.

Recommended v2 layout:

```text
content/
  manifest.json
  package.json
  assets/
    <asset-key>/
      asset.json
      model.glb              # optional by content type
      albedo.png             # optional
      thumbnail.png
      collision.json         # optional
      behavior.json          # optional
      face_atlas.png         # optional face/decal content
      audio/                 # optional approved audio payloads in later phase
  prefabs/
    <prefab-key>.json         # optional contraptions
  scripts/
    <script-key>.lua         # optional
preview.png
```

Do not allow arbitrary additional files.

## 5.2 Runtime identity

Never trust an author-supplied global ID to be unique.

Runtime identity is derived from Steam provenance:

```text
ugc:<PublishedFileId>:<asset-key>
ugc:<PublishedFileId>:prefab:<prefab-key>
```

Rules:

- `asset-key` is package-local, normalized ASCII identifier;
- one Workshop item can update while retaining the same runtime IDs;
- Workshop content can never shadow built-in or trusted Asset Forge IDs;
- local pre-publication Creator testing uses a separate `localugc:<package-guid>:<asset-key>` namespace;
- character/environment save documents preserve the UGC identity even if the item is temporarily unavailable.

## 5.3 Versioning

Separate versions:

- package schema version;
- UGC runtime API version;
- canonical mesh version;
- behavior-graph version;
- Lua host API version;
- per-asset semantic version optional for creator diagnostics.

Unsupported future major versions fail closed and remain visible as incompatible content rather than disappearing silently.

---

# 6. Asset kinds

Initial architecture recognizes:

```text
BuddyCosmetic
FaceCosmetic
RoomDecoration
PhysicsProp
Contraption
AssetPack
```

One package may contain several compatible entries.

Future asset kinds must be added through schema evolution, not magic string interpretation in UI code.

---

# 7. Creator application

## 7.1 Do not ship the developer repository exporter as the public tool

Keep current Asset Forge for trusted source-control authoring.

Create **Desktop Buddy Creator** as a separate player-facing executable that reuses shared core libraries.

It must not contain:

- repository-root editing;
- `.tres` catalogue mutation;
- project source deletion;
- source-control maintenance commands;
- developer-only Verify All against repository roots.

## 7.2 Creator home

Player chooses:

```text
Create Cosmetic
Create Face Cosmetic
Create Prop / Decoration
Create Contraption
Create Pack
Import Existing Creator Project
```

## 7.3 Model creation paths

### Generate from image

Reuse Asset Forge templates/generation.

### Import my model

Initial external authoring support:

- `.glb`;
- `.gltf`.

Add FBX only after the glTF mapper is stable.

The Creator imports external content into an isolated authoring preview, then canonicalizes it.

Reject or strip:

- animation;
- skinning;
- skeletons;
- cameras;
- scene lights from the source file;
- scripts;
- custom extensions;
- external URIs in the published form;
- unsupported materials.

Creator lets the user select/combine relevant mesh data, then emits one canonical mesh per UGC asset.

## 7.4 Model mapper

For each model expose:

- orientation;
- scale;
- visual origin;
- Buddy anchor / room pivot;
- paired mirroring where appropriate;
- albedo preview;
- runtime triangle/vertex statistics;
- collision editor;
- physics inspector;
- light editor;
- interaction editor;
- behavior editor;
- Lua editor for advanced creators;
- local sandbox test button.

## 7.5 Collision editor

Supported dynamic shapes:

- circle;
- rectangle;
- capsule;
- convex polygon.

Static-only content may additionally use bounded concave polygon segments after a dedicated performance spike.

Creator provides:

- auto front-silhouette convex hull;
- simplified hull;
- multiple collider creation;
- manual vertex editing;
- collider enable/disable;
- collision preview over the 3D visual projection;
- mass-centre preview.

Do not generate a dynamic concave collision polygon from arbitrary mesh geometry.

---

# 8. UGC physics component model

Define pure records such as:

```text
UgcPhysicsDefinition
UgcCollisionShapeDefinition
UgcGrabDefinition
UgcImpactDefinition
UgcHealthDefinition
UgcJointDefinition
UgcLightDefinition
UgcBehaviorBinding
```

## 8.1 Body modes

Support:

```text
DecorationOnly
Static
Rigid
```

A later version may add controlled kinematic bodies if concrete creator use-cases require them.

## 8.2 Physical properties

Rigid bodies may author:

- mass;
- gravity scale;
- centre-of-mass offset within validated bounds;
- linear damping;
- angular damping;
- friction;
- bounce/restitution;
- lock rotation;
- continuous collision detection mode;
- initial linear/angular velocity for spawned projectiles;
- collision layer category chosen from UGC-safe semantic categories.

Values are finite and bounded by hard engine-safety limits.

Recommended values are soft guidance, not reasons to reject creative content.

## 8.3 Grabbing

UGC physics props can author:

- grabbable yes/no;
- grab point(s);
- grab-strength multiplier;
- optional maximum tether force;
- whether rotation follows the drag interaction;
- whether secondary/use input is accepted while grabbed.

Do not special-case Workshop items in pointer UI. Extend the current picking/grab contracts so UGC bodies participate through typed capability metadata.

## 8.4 Impact attribution

Every UGC physics body that can collide with Buddy implements the same semantic `IImpactSource` path as shipped loose objects/tools.

The source identifier is the UGC runtime ID.

Do not let Lua fabricate a trusted official content ID.

---

# 9. Joints, links and contraptions

## 9.1 Joint types

Support declarative Godot 2D equivalents for:

- pin joint;
- damped spring;
- groove/slider where the game's Godot version supports the required behavior reliably.

Creator exposes anchors visually.

## 9.2 Contraptions

A contraption is a prefab containing:

- multiple asset/body instances;
- local transforms;
- joints;
- named signal links;
- behavior bindings;
- optional initial state.

Contraptions must instantiate through one transaction so partial failure cannot leave half a machine in the sandbox.

## 9.3 Linking

Objects expose named signal endpoints.

Examples:

```text
trigger
activate
power
open
close
fire
reset
custom:<name>
```

The underlying runtime uses IDs/handles, not NodePaths supplied by Workshop data.

This lets non-programmers build button -> timer -> launcher and similar systems.

---

# 10. Player-authored lights

Player-authored lights are explicitly supported.

Expose a declarative light component using the same visual principles as current generated lamps.

Fields include:

- enabled;
- color;
- energy/brightness;
- range;
- local emitter position;
- optional spot direction/cone after a focused Godot presentation spike;
- emission strength on the visual material;
- shadow casting toggle.

Rules:

- finite values only;
- soft warnings for expensive light setups;
- a generous hard global safety cap prevents accidental thousands-of-lights crashes;
- shadow-casting lights are allowed but clearly marked as expensive;
- users may enable a local "allow heavy Workshop content" preference to raise soft runtime budgets, while absolute crash-safety bounds remain.

Do not prohibit lights merely because they may affect performance. Surface cost to the creator/player and enforce only safety bounds.

---

# 11. Damage, status and gameplay interactions

## 11.1 UGC may hurt the Buddy

Physics props and scripts may generate real Buddy pain/status interactions.

Supported host actions eventually include:

- physical impact through ordinary collision;
- direct bounded pain event;
- impulse to a Buddy part;
- approved status application (for example Burning when that status exists as a public capability);
- approved status clearing/healing;
- mood events;
- care-like events where explicitly supported.

Do not expose direct mutation of private Buddy rig tuning, constraints, collision geometry or save objects.

## 11.2 Progression integrity

UGC must not become a "spawn infinite money" exploit.

Introduce explicit impact/reward provenance:

```text
Official
CommunityVisualOnly
CommunityGameplay
```

Community-generated pain can affect Buddy reaction/mood/knockout as designed, but **community-authored damage pays zero credits by default**.

Scripts cannot set payout multipliers or emit trusted economy events.

Future challenge/leaderboard submissions are disabled or marked modded when gameplay UGC is active.

Pure visual cosmetics/room paint do not mark a session modded.

This preserves Workshop freedom without making progression/leaderboards meaningless.

---

# 12. Behavior Graph

Behavior Graph is the default custom-logic authoring route.

## 12.1 Events

Initial event vocabulary:

```text
OnSpawn
OnDespawn
OnCollisionEnter
OnCollisionExit
OnImpactBuddy
OnGrabbed
OnReleased
OnUsePrimary
OnUseSecondary
OnSignal
OnTimer
OnHealthChanged
OnDestroyed
OnJointBroken
OnTickInterval
```

Do not expose a required per-frame event when an interval/event can express the behavior.

## 12.2 Conditions

Examples:

- compare state variable;
- speed threshold;
- angular-speed threshold;
- timer elapsed;
- collision target category;
- Buddy part category;
- probability using deterministic per-instance RNG;
- distance/radius query result;
- grabbed state;
- health threshold.

## 12.3 Actions

Initial host actions:

- apply force;
- apply impulse;
- apply torque;
- set velocity within capability policy;
- toggle gravity;
- toggle collision shape;
- change color/emission;
- change light parameters;
- start/stop timer;
- spawn a package asset/prefab;
- despawn self/child;
- create/break an allowed joint;
- send named signal;
- play approved sound payload;
- spawn approved particle preset;
- damage/heal Buddy through UGC provenance;
- apply/clear approved status;
- perform bounded raycast/area query;
- store/read typed local variables.

Graph nodes and fields are typed/versioned; unknown nodes fail validation with a useful Creator error.

---

# 13. Sandboxed Lua

## 13.1 Script model

Workshop packages may contain whitelisted `scripts/*.lua` files only when declared in the manifest.

A script binds to one or more behavior events.

Example conceptual API:

```lua
function on_use_primary(ctx)
    ctx:apply_impulse(0, 1400)
    ctx:play_sound("fire")
    ctx:spawn("projectile", 0, -18)
end
```

The exact API is defined by typed host proxies, not raw Godot objects.

## 13.2 Hard sandbox

The Lua environment must not expose:

- `io`;
- `os`;
- CLR interop;
- reflection;
- filesystem;
- process creation;
- networking;
- environment variables;
- arbitrary package loading;
- raw threads;
- raw Godot objects;
- Steam objects;
- save-store objects.

Scripts receive only:

- event data;
- their own state;
- typed entity handles;
- package-local asset IDs;
- versioned UGC host functions.

## 13.3 Scheduling and denial-of-service protection

Custom logic runs through one `UgcBehaviorScheduler` on the routed simulation clock.

Requirements:

- per-event instruction budget;
- per-instance and per-Workshop-item time budget;
- bounded queued actions;
- bounded spawn rate;
- bounded timers;
- bounded query results;
- no unbounded recursion/coroutine proliferation;
- repeated budget violations suspend that script instance and surface a user-visible warning;
- one bad mod cannot stop the owning physics tick or crash the game loop.

Do not call arbitrary Lua directly inside a Godot contact callback.

Queue semantic events, execute through the scheduler, then apply validated commands at the controlled simulation boundary.

## 13.4 Versioned host API

Scripts declare a `ugcApiVersion`.

Maintain compatibility shims across minor versions where reasonable.

Breaking API changes require a new major version and explicit incompatible-item presentation.

---

# 14. Runtime entity architecture

Introduce:

```text
UgcRuntimeEntity
UgcRuntimeEntityFactory
UgcPhysicsBodyAdapter
UgcPresentationAdapter
UgcBehaviorInstance
UgcEntityRegistry
UgcRuntimeHandle
```

One runtime entity owns no arbitrary Workshop object references after validation; it owns typed validated definitions and local cached asset paths/decoded data.

## 14.1 Registry and budgets

Current shipped loose-object registry policy must be generalized or composed with a UGC registry so UGC cannot bypass object-count/eviction accounting.

Preferred direction:

- extract a general sandbox entity-budget service from the existing loose-object registry;
- shipped loose objects and UGC entities both register through it;
- retain compatibility wrappers for existing `LooseObjectRegistry` call sites during migration.

Do not create an unrelated second object cap that can be exceeded independently.

## 14.2 Scene-tree ownership

UGC definitions do not receive NodePaths.

Runtime adapters create known Desktop Buddy node types from validated definitions.

The only nodes generated from UGC data are nodes the game itself constructs, e.g.:

- `RigidBody2D`;
- `StaticBody2D`;
- `CollisionShape2D`;
- known joint types;
- `MeshInstance3D` using game-created `ArrayMesh`;
- known light types;
- known audio/particle components.

No Workshop scene is instantiated.

---

# 15. Dynamic UGC catalogues

## 15.1 Current problem

Current generated cosmetic/environment registries are static trusted `res://` catalogues loaded at startup.

Workshop subscriptions require runtime catalogues that can change without rebuilding the game.

## 15.2 Catalogue service

Introduce a versioned immutable snapshot service:

```text
IUgcCatalog
UgcCatalogService
UgcCatalogSnapshot
```

Sources:

```text
Built-in trusted content
Trusted developer Asset Forge content
Validated installed Workshop content
Local Creator test content
```

Each refresh creates a new immutable snapshot.

UI/session code receives the service/snapshot; it must stop reaching directly for global `BuddyGeneratedCosmeticRegistry.Current` where that would prevent runtime UGC.

## 15.3 Missing content

Save files retain requested UGC IDs.

When unavailable:

- Buddy cosmetic -> built-in slot fallback;
- room decoration/prop -> missing-content placeholder in editing UI, no arbitrary replacement;
- contraption -> keep serialized reference and show incomplete state rather than silently substituting a different item.

When the same Workshop item becomes available again, the original ID resolves again.

---

# 16. Buddy Studio UGC

## 16.1 3D slots

Support canonical-model UGC for appropriate slots, including:

- Hair;
- Nose;
- Ears;
- Accessories;
- Glasses;
- Headwear;
- Tops;
- Shoes.

Face shape can receive a dedicated replacement contract later if needed.

## 16.2 Attachment modes

Creator selects one of validated semantic modes:

```text
VisualAttachment
PartVisualReplacement
PairedPartVisualReplacement
PhysicsAttachment
PairedPhysicsAttachment
```

Physics attachments may add their own rigid body/colliders and attach to an approved Buddy socket/joint.

They do not directly rewrite `BuddyVisualProfile`, Buddy mass, part collision shapes or puppet-drive tuning.

Their mass/forces can nevertheless affect the Buddy naturally through the attached physics system, which is the intended sandbox behavior.

## 16.3 Reactive face content

Eyes, brows and mouths are not naturally GLB assets in the current renderer.

Define a separate `FaceCosmetic` package contract using PNG atlases plus semantic state metadata.

Examples:

Eyes may author states such as:

```text
open
blink
hurt
optional alternate
```

Mouth content may author:

```text
neutral
happy
hurt
unconscious
```

The exact state contract must follow the current face-state model so Workshop art preserves Buddy reactions.

Do not force reactive 2D face art through the 3D model loader.

---

# 17. Room Decorator and physics props

UGC room content can be:

### DecorationOnly

Placed and persisted like existing visual decorations.

### Static

Has collision but does not move.

### Rigid

Participates in physics, can be grabbed when enabled, can hit Buddy, can contain lights and behavior.

Room placement persistence stores:

- UGC runtime ID;
- transform;
- creator-exposed configurable instance parameters;
- stable instance ID where behavior state must persist.

Do not serialize arbitrary Lua VM internals. Scripts explicitly mark small primitive state keys as persistent if/when persistence is added.

---

# 18. Creator-authored audio and particles

Not required for the first Glasses/physics slice, but the architecture must leave room for them.

Recommended later payloads:

- OGG Vorbis for audio;
- PNG textures for particle visuals;
- game-owned particle/material templates with creator-set parameters.

Do not accept arbitrary shaders.

The Creator can expose rich effects by parameterizing trusted game shaders/particle systems rather than loading Workshop shader source.

---

# 19. Performance policy — power with transparency

Do not make conservative performance limits the product's creative ceiling.

Use **soft budgets + hard safety limits**.

## 19.1 Soft budgets

Creator shows warnings such as:

- high triangle count;
- many colliders;
- many rigid bodies;
- many joints;
- many lights;
- shadow-heavy setup;
- high script event rate;
- high spawn rate.

Publishing may still be allowed when only soft budgets are exceeded.

## 19.2 Heavy-content preference

Game settings include:

```text
Allow Heavy Workshop Content
```

When off, content above recommended budgets requires explicit user confirmation before activation.

When on, soft budget warnings do not block activation.

## 19.3 Hard limits

Absolute limits protect against crashes/resource exhaustion.

Initial provisional upper bounds to validate during implementation:

- package: 512 MiB;
- individual canonical mesh: 64 MiB;
- individual texture: 32 MiB;
- Lua source: 1 MiB per script;
- canonical mesh: 1,000,000 triangles absolute;
- collision shapes: 128 per entity;
- rigid bodies: 128 per contraption;
- joints: 256 per contraption;
- active UGC lights: 64 per contraption, plus global safety ceiling;
- spawn commands: bounded per simulation second;
- behavior event queues: bounded per entity/package.

These values are **provisional engineering limits**, not owner-facing gameplay rules. Benchmark and adjust them on representative low/mid/high PCs before release.

NaN/Infinity, invalid indices, out-of-range references, decompression bombs, path traversal and malformed formats always reject regardless of user preference.

---

# 20. Workshop trust pipeline

The required path is:

```text
Steam install/cache
        |
        v
ONE bounded immutable incoming snapshot
        |
        v
manifest/hash/path validation
        |
        v
package/schema validation
        |
        v
canonical GLB structural validation
        |
        v
physics/behavior/script validation
        |
        v
project-owned UGC library/cache
        |
        v
runtime catalogue snapshot
        |
        v
explicit activation/spawn/equip
```

Never inspect one version of a mutable Steam folder to determine content type and then copy a potentially different version later.

This explicitly incorporates the PR #41 design-review finding.

---

# 21. Local UGC library

Validated subscribed content is copied into a project-owned content-addressed/local library.

Recommended storage:

```text
user://ugc/
  workshop/
    <published-file-id>/
      current/
      provenance.json
      package-state.json
  local/
    <package-guid>/
```

Update transaction:

1. download new Workshop revision;
2. immutable incoming snapshot;
3. full validation;
4. stage new local revision;
5. atomically switch `current`;
6. refresh catalogue;
7. keep prior known-good revision until switch completes;
8. quarantine rejected revision without destroying the last valid local copy.

---

# 22. Workshop UX

Workshop UI evolves into a content manager rather than only import buttons.

Views:

```text
Browse Workshop
Installed
Updates
Creator / My Items
Missing Content
Disabled / Incompatible
Local Creator Projects
```

Each installed item shows capabilities:

```text
Visual
Physics
Collision
Grab
Lights
Behavior Graph
Lua Script
Contraption
```

Scripted content is clearly indicated.

Do not show a frightening arbitrary-code warning for sandboxed Lua; it does not have host access. Show capability/performance information instead.

If a future unsafe full-code tier exists, that tier gets a separate explicit warning/enable flow.

---

# 23. Steam Workshop tags for the expanded platform

Do not change the currently configured v1 tags until the new content system is implemented and the Steamworks settings are ready to publish.

Future Ready-to-Use tag design should include a **Content Type** category such as:

```text
Room Painting
Buddy
Cosmetic
Prop
Contraption
Pack
```

And useful feature/filter tags such as:

```text
Physics
Scripted
Lights
Destructible
```

Exact strings must be configured in Steamworks before the game submits them.

Keep internal schema/version identifiers in metadata rather than exposing them as ugly public tags.

---

# 24. Current Workshop v1 pre-merge findings

The expanded UGC work should **not** be built on top of unresolved orchestration defects in PR #41.

Before PR #41 is considered merge-ready, address the following source issues found during the 2026-08-27 review.

## M1 — snapshot before content-type detection — BLOCKER

Current coordinator can inspect `manifest.json` directly from Steam's mutable install directory before the importer takes its immutable snapshot.

Fix:

- take one incoming staging snapshot first;
- detect content type from that snapshot;
- pass the same `WorkshopIncomingStaging` to the selected importer;
- never validate/read package payload directly from Steam cache.

## M2 — `CreateItem` cancellation orphan — BLOCKER

Current Steam create request can continue remotely after caller cancellation while the returned PublishedFileId is discarded, potentially leaving an empty/orphan Workshop item.

Fix:

- model remote operation state separately from caller wait cancellation;
- once `CreateItem` is submitted, do not abandon its result;
- reconcile late callback and either continue the pending publish or surface recoverable item state;
- add deterministic tests for cancellation-before-callback and late callback.

## M3 — extract/test Steam callback operation state — BLOCKER

The hardest concurrency behavior currently lives inside the Godot transport and is not covered deeply enough by pure unit tests.

Fix:

- extract a pure callback/operation coordinator where practical;
- test duplicate callbacks;
- late callbacks;
- cancellation;
- mismatched AppID/item ID;
- concurrent publish rejection;
- shutdown with pending operation.

## M4 — remove changing `_appId` semantics — REQUIRED

Keep runtime AppID and Workshop-owner AppID in distinct fields for the complete transport lifetime.

Never repurpose one field after initialization.

## M5 — cancellation semantics — REQUIRED

Use one application-level cancellation model.

Persistence/export stages should normally propagate `OperationCanceledException`; the Workshop application coordinator converts it exactly once into `Cancelled`.

Do not infer cancellation from failure strings.

## M6 — heavy Buddy package work off main thread — REQUIRED

Character share import/export performs hashing, JSON, PNG and filesystem work before/after awaits in ways that can execute on the Godot main thread.

Move pure file/decode/validation work to deliberate workers.

GodotSteam calls and Godot object creation remain on the main thread.

## M7 — bootstrap/service coupling — REQUIRED BEFORE UGC EXPANSION

Workshop UI currently discovers other root/autoload services and uses concrete bootstrap classes as application services.

Before dynamic UGC catalogues are introduced:

- introduce narrow room snapshot/apply contracts;
- introduce command-registration contract;
- explicitly wire them from composition root;
- stop polling the scene tree for service availability.

This may be completed in PR #41 or as the first prerequisite commit of the UGC branch, but do not build the UGC catalogue system on the existing polling pattern.

## M8 — typed transport availability/query result — REQUIRED BEFORE CROSS-APP/UGC

Do not make "zero subscriptions" indistinguishable from "Steam unavailable".

Return typed query status and expose transport availability through the actual interface consumed by the application.

## M9 — cleanup failed native transport composition — REQUIRED

If real Steam transport initialization fails and bootstrap falls back to Null transport, free/disconnect temporary bridge/transport nodes.

## M10 — live Steam smoke — RELEASE GATE

Source-controlled CI is green on PR #41, including the real GodotSteam addon smoke, but GitHub runners do not have an authenticated Steam client.

Before release — and preferably before merging the Steam foundation — run at least one developer-account live smoke on AppID `5114950`:

- initialize Steam;
- publish a private/developer Workshop item;
- handle legal agreement if applicable;
- subscribe/download;
- import;
- update same item;
- verify offline reuse.

The complete second-account matrix may remain a release gate if a second account is not immediately available, but the first-account create/publish path should be proven before treating the platform adapter as production-ready.

---

# 25. Implementation phases

The following phases are ordered. Agents must not jump directly to scripting/contraptions before the foundation gates pass.

## UGC-0 — source alignment + PR #41 prerequisite fixes

Deliverables:

- close M1-M9 above;
- update `AGENTS.md`, architecture/source-alignment docs to authorize the expanded UGC platform when implementation is explicitly started;
- keep real-time multiplayer and arbitrary host-code mods separate;
- all PR #41 tests green.

Exit:

- Workshop foundation is mergeable independently;
- no known callback/staging/threading defect is knowingly inherited.

## UGC-1 — shared UGC Core + dependency spike

Deliverables:

- `DesktopBuddy.Ugc.Core`;
- package/version/identity models;
- canonical GLB parser/validator;
- physics definition models;
- behavior definition models;
- pure validators;
- MoonSharp dependency/license/security spike behind `IUgcScriptEngine`.

Tests:

- malformed GLB corpus;
- invalid indices;
- NaN/Infinity;
- duplicate IDs;
- schema future-version refusal;
- identifier normalization;
- script engine proves no filesystem/CLR/OS access.

## UGC-2 — local UGC library + immutable catalogue

Deliverables:

- local Workshop/local Creator library;
- atomic revision switch;
- provenance;
- immutable `UgcCatalogSnapshot`;
- update/missing/incompatible states;
- directory emulator package installation.

Exit:

- a validated fake package can be installed/updated/rolled back without rendering it.

## UGC-3 — canonical visual runtime vertical slice

First asset: **Glasses**.

Deliverables:

- canonical mesh -> `ArrayMesh` adapter;
- albedo -> trusted Desktop Buddy material;
- runtime UGC cosmetic definition;
- dynamic Buddy Studio catalogue entry;
- equip/save/restart/missing-content fallback;
- no physics yet.

Exit journey:

```text
install local UGC glasses
-> appears in Buddy Studio
-> equip
-> save
-> restart
-> still equipped
-> remove package
-> safe fallback
-> reinstall
-> original UGC selection resolves again
```

## UGC-4 — Desktop Buddy Creator visual publishing slice

Deliverables:

- player-facing Creator shell;
- generate-from-image Glasses path using AssetForge.Core;
- external GLB/glTF mapper;
- canonicalize output;
- local test;
- package generation;
- Workshop publish using existing transport;
- preview/thumbnail generation.

Exit:

- a non-developer can make and publish Glasses without touching repository files.

## UGC-5 — single-body physics prop

Deliverables:

- collision editor;
- Rigid/Static/Decoration modes;
- mass/damping/friction/bounce/gravity/CCD;
- runtime physics factory;
- presentation follows physics body;
- spawn/remove UI;
- entity budget registration.

Exit journey:

```text
Creator imports model
-> adds collider + mass
-> local test
-> publishes
-> subscriber spawns prop
-> prop falls/collides/bounces
```

## UGC-6 — grabbing + Buddy impacts + progression provenance

Deliverables:

- grabbable capability;
- pointer selection integration;
- grab metadata;
- UGC `IImpactSource`;
- Buddy pain/mood integration;
- zero-credit community-impact policy;
- modded-session marker for future competitive features.

Exit:

- player can grab/throw UGC prop into Buddy and receive real reaction without minting credits.

## UGC-7 — player lights

Deliverables:

- Creator light editor;
- runtime light component;
- emission control;
- shadow toggle;
- soft performance reporting;
- hard safety accounting.

Exit:

- published lamp/light follows prop transform and survives save/load where relevant.

## UGC-8 — Behavior Graph

Deliverables:

- typed events/conditions/actions;
- graph editor;
- host command queue;
- deterministic timers/RNG;
- collision/use/grab/signal events;
- forces/spawn/light/damage actions.

Exit examples:

- button toggles a light;
- impact launches another prop;
- timer pulses force;
- thrown object triggers an effect.

## UGC-9 — sandboxed Lua

Deliverables:

- hard-sandbox interpreter;
- versioned host API;
- event bindings;
- instruction/time/spawn/query budgets;
- script suspension diagnostics;
- Creator code editor/docs;
- no raw Godot/CLR access.

Required hostile tests:

- infinite loop;
- recursive call storm;
- attempted filesystem access;
- attempted CLR reflection;
- massive spawn loop;
- invalid entity handle;
- script exception;
- script from removed package;
- two mods throwing simultaneously.

## UGC-10 — joints + contraptions

Deliverables:

- joint authoring;
- multi-body prefab schema;
- link/signal editor;
- transactional spawn;
- save/restore;
- missing dependency presentation.

Exit examples:

- spring toy;
- hinged trap;
- button -> timer -> launcher machine.

## UGC-11 — full 3D Buddy cosmetic expansion

Deliverables:

- Hair;
- Nose;
- Ears;
- Accessories;
- Headwear;
- Tops;
- Shoes;
- paired/attachment/replacement policies;
- optional physics attachments through approved Buddy sockets.

Do not mutate trusted Buddy rig definition files.

## UGC-12 — reactive face Workshop content

Deliverables:

- face atlas schema;
- Eyes/Brows/Mouth semantic states;
- runtime renderer adapter;
- Creator face preview;
- missing-state fallback;
- content update compatibility.

## UGC-13 — Room Decorator deep integration

Deliverables:

- UGC Decoration/Static/Rigid entries in decorator/spawn browser;
- placement persistence;
- lights;
- behavior;
- physics activation semantics;
- missing-content placeholders.

## UGC-14 — Workshop content manager + discovery polish

Deliverables:

- capability badges;
- installed/update/incompatible/missing views;
- enable/disable item;
- heavy-content warnings/preferences;
- local Creator projects;
- public Workshop tag/filter integration;
- update rollback diagnostics.

## UGC-15 — release hardening

Deliverables:

- fuzz/property tests;
- package corruption corpus;
- script hostile corpus;
- performance soaks with many bodies/joints/lights;
- Workshop update rollback soak;
- low/mid/high hardware profiling;
- Steam two-account matrix;
- creator documentation;
- UGC API documentation;
- sample Workshop items made only through public APIs.

---

# 26. Required automated scenarios

At minimum add these journeys/scenarios as the relevant phase lands:

```text
ugc_visual_glasses_roundtrip
ugc_missing_cosmetic_fallback
ugc_model_parser_hostile
ugc_physics_prop_collision
ugc_physics_prop_grab_throw
ugc_impact_zero_credit
ugc_light_follow
ugc_behavior_graph_trigger
ugc_lua_sandbox_denies_host_access
ugc_lua_budget_suspends
ugc_lua_spawn_budget
ugc_joint_contraption
ugc_contraption_transaction_failure
ugc_workshop_update_atomic_swap
ugc_bad_update_keeps_last_good
ugc_missing_room_asset_placeholder
ugc_catalogue_refresh_no_restart
ugc_many_entities_budget
ugc_many_lights_budget
```

Creator CI additionally needs:

```text
creator_assetforge_glasses_publish_package
creator_external_glb_canonicalization
creator_collision_roundtrip
creator_physics_metadata_roundtrip
creator_behavior_graph_roundtrip
creator_lua_validation
creator_package_verify
```

---

# 27. Design rules for agents

1. **Do not create a second physics engine.** Use Godot 2D physics through typed UGC factories.
2. **Do not load Workshop Godot Resources/scenes.** Construct known runtime nodes from validated data.
3. **Do not expose raw Godot objects to Lua.** Use handles/proxies and queued commands.
4. **Do not make community item IDs trusted global IDs.** Derive identity from Workshop provenance.
5. **Do not let UGC mint credits or trusted leaderboard scores.** Track provenance.
6. **Do not use hard-coded checks for each Workshop item.** Add capabilities/components.
7. **Do not put UGC logic into `Bootstrap`.** Bootstrap composes services only.
8. **Do not make `BuddyGeneratedCosmeticRegistry.Current` the UGC registry.** Introduce a runtime catalogue service.
9. **Do not let one mod own unbounded callbacks/timers/entities/lights.** Central scheduler/registry owns budgets.
10. **Do not treat expensive content as invalid merely because it is expensive.** Use warnings/opt-in until a hard safety boundary is crossed.
11. **Do not silently downgrade or rewrite a creator package in the game.** Creator canonicalizes; runtime validates.
12. **Do not delete the last known-good local UGC revision until its replacement validates and atomically activates.**
13. **Do not make Workshop subscription automatically activate scripted/physics content in an existing room.** Subscription makes content available; spawning/equipping remains explicit.
14. **Do not add arbitrary full-code mods by accident.** Any future host-code tier requires separate owner authorization and UX/security design.

---

# 28. Definition of done for the UGC platform

The UGC platform is not complete merely because one GLB can render.

The target is satisfied when:

- players can create content without repository/dev tooling;
- Asset Forge-generated and externally modeled assets converge on one canonical format;
- Workshop packages can contain interactive physics objects;
- collision, mass, damping, friction, bounce and gravity are creator-controlled;
- objects may be grabbed when configured;
- player-authored lights work;
- objects can affect Buddy physically;
- behavior graphs support useful no-code interactions;
- Lua supports genuine custom behavior without host-machine access;
- joints/contraptions work;
- 3D Buddy cosmetics dynamically appear from Workshop;
- reactive face content has its own correct 2D contract;
- room content supports decorative/static/rigid modes;
- installed Workshop updates are transactional;
- missing content degrades safely and non-destructively;
- UGC cannot impersonate official content/economy events;
- heavy content is user-manageable rather than arbitrarily forbidden;
- a bad script/package cannot take down the main simulation;
- live Steam publish/subscribe/update flows pass with real accounts;
- public creator/API documentation is sufficient to build content without reverse-engineering Desktop Buddy internals.

---

# 29. Immediate next decision / execution order

Do **not** open an implementation PR for this player-UGC plan until PR #41's pre-merge source findings M1-M9 are fixed or deliberately split into a prerequisite branch.

Recommended repository sequence:

```text
1. Finish PR #41 cleanup/review findings
2. Run first-account live Steam smoke on AppID 5114950
3. Mark PR #41 ready and merge it
4. Rebase/create UGC implementation branch from the merged Workshop foundation
5. Start UGC-0/UGC-1
6. Open the UGC implementation PR after the shared-core/package foundation is coherent
```

The current `plan/workshop-player-authored-assets` branch is documentation/planning only and should not be merged as production code by itself unless the owner wants the plan/source-alignment history preserved in `main` first.
