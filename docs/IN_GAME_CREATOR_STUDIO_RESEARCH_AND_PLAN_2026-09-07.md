# Desktop Buddy — In-Game Creator Studio Research and Plan

Status: **owner-approved product direction; detailed creator/modding design**  
Recorded: 2026-09-07  
Planning branch: `plan/full-release-systemic-sandbox`  
Applies to: Next Fest systemic vertical slice planning, Full Release, local content authoring, future safe Workshop content packs

This document develops the owner-approved direction that Desktop Buddy should make safe mod/content creation unusually accessible by putting a creator workflow **inside the game itself**, styled as another Windows 98-era application.

It supplements `MODDABILITY_AND_WORKSHOP_SECURITY_SOURCE_ALIGNMENT_2026-09-07.md`. All security rules in that document remain binding: Workshop content is hostile declarative data, never arbitrary C#, GDScript, DLLs, Godot Resources/scenes, shaders, native libraries or executable PCKs.

The key product idea is:

> A player should be able to build a contraption, turn it into a Blueprint, create a new item, paint the item's appearance, configure safe behavior, test it immediately, and publish it — without leaving Desktop Buddy or writing code.

The creator should be progressively disclosed. A player who only wants to draw a ridiculous gun should never need to understand events, ports, schemas or dependency graphs. A technical creator should be able to open those deeper layers without changing tools.

---

# 1. Research findings and what Desktop Buddy should take from them

## 1.1 Mutilate-a-Doll 2 — MaD Lab plus in-game Detail editing

Research sources:

- developer answer explaining MaD Lab and Local Models: https://steamcommunity.com/app/665370/discussions/0/1840188800803657226/
- developer FAQ describing Custom Items, item property modification, combined functionality, sound/particle replacement and stamping: https://steamcommunity.com/app/665370/discussions/0/1795152172925405479/
- official Steam news / MaD Lab functionality expansion: https://steamcommunity.com/app/665370/allnews/
- MaD Lab documentation repository: https://gitlab.com/RavaGames/madlab/

Confirmed pattern:

- MaD Lab is a dedicated item editor built specifically for MaD2.
- Historically it exported PNG models with metadata; local models could be imported through the game.
- MaD Lab later gained XML-based functionality and became the developer's main content-creation workflow.
- The developer explicitly describes tweaking values in MaD Lab while MaD2 is running and refreshing to see changes in-game.
- MaD2 itself also exposes a large in-game Detail/Properties editing vocabulary.
- The Gunsmith/Detail workflow allows substantial firearm editing such as velocity, scatter and recoil.
- The developer deliberately generalized MaD Lab triggers/functions so the same functionality could be reused across many models.

What to take:

1. **Generic functions beat one-off content code.** The same trigger/action should be reusable by many items.
2. **Fast edit -> refresh -> test loops matter enormously.** Desktop Buddy should make this instantaneous rather than requiring an external editor.
3. **Properties are a creator API.** The Win98 Properties system is not merely player QoL; it is one of the easiest safe modding surfaces.
4. **Visual model authoring and functionality authoring should be separable.** A creator can make a visual prop first and add behavior later.

What to improve on:

- Do not require a separate application for the ordinary creator path.
- Do not expose raw XML-like free-form strings as the primary UX.
- Do not require functionality to be implemented in game code just because an item is custom.
- Give users a visual, typed event/action editor over the safe capability API.

## 1.2 People Playground — in-game contraptions, external C# mods, and creator demand

Research sources:

- official mod creation guide: https://www.studiominus.nl/ppg-modding/tutorials/tutorialCreatingMod.html
- Workshop landing page separating mods and contraptions: https://steamcommunity.com/workshop/about/?appid=1118200
- contraption save workflow discussion: https://steamcommunity.com/app/1118200/discussions/0/1640919103666405346/
- February 2026 official Workshop/security announcements: https://steamcommunity.com/app/1118200/announcements/
- community Live Sprite Editor: https://steamcommunity.com/sharedfiles/filedetails/?id=3718619896

Confirmed pattern:

- PPG has a very low-friction in-game contraption workflow: assemble things in the world, select them, save, and load from the saved-contraption surface.
- Deeper PPG mods are C# projects in the game's Mods folder.
- PPG's Workshop explicitly mixes mods and contraptions.
- In February 2026 a malicious executable Workshop mod propagated by overwriting users' Workshop uploads and also deleted/reset PPG-local content/state. PPG temporarily closed Workshop, added stronger security, made trust content-hash-aware, and stopped changed mods from silently auto-updating without renewed approval.
- In 2026 a community Live Sprite Editor became a popular Workshop mod. Its surfaced Workshop page reports tens of thousands of subscribers and supports editing characters/objects/guns directly in-game with brush/eraser/fill/picker, selection, layers, import/export, undo/redo and immediate preview.

What to take:

1. **Creation should start in the world.** Saving a selection as a Blueprint must remain one click away.
2. **Creator content should appear immediately in the same spawn/library UI.** No restart or manual file copying for normal creation.
3. **There is demonstrable demand for in-game sprite editing in this audience.** Desktop Buddy can make that first-party and integrated rather than a community workaround.
4. **Mods and Blueprints are different trust classes.** Contraption data can be very safe; arbitrary C# cannot.

What not to copy:

- Ordinary Workshop subscriptions must never become arbitrary executable C#.
- Do not trust an author merely because a prior version was approved.
- Do not let mod packages write to Workshop upload directories, arbitrary local saves or other packs.

## 1.3 Factorio — prototypes, events, explicit APIs and dependency ordering

Research sources:

- official API overview: https://lua-api.factorio.com/latest/index.html
- prototype API: https://lua-api.factorio.com/latest/index-prototype.html
- runtime events: https://lua-api.factorio.com/latest/events.html
- data lifecycle / dependency ordering: https://lua-api.factorio.com/latest/auxiliary/data-lifecycle.html

Confirmed pattern:

- Factorio separates startup settings/prototype definition from runtime behavior.
- Runtime interaction is event-driven through a deliberately exposed API.
- Mods have explicit dependencies and deterministic load ordering.
- Prototypes are validated into structures expected by the game rather than being arbitrary engine objects.

What to take:

1. Keep **definition compile/load** distinct from **runtime event execution**.
2. Make dependencies explicit and deterministic.
3. Give creators semantic APIs, not Godot internals.
4. Preserve schema/API versions independently from individual content-pack versions.

## 1.4 Besiege — in-game building and level editing plus a separate mod layer

Research sources:

- Steam product/workshop pages: https://store.steampowered.com/app/346010/ and https://steamcommunity.com/app/346010/workshop/

Confirmed pattern:

- Besiege's core building experience itself is a creator tool.
- It supports community machines, levels, skins and code-style mods as different content classes.
- Its level editor exposes logic inside the creative experience.
- Workshop categories distinguish machines, levels, skin packs and mods.

What to take:

1. A creator surface does not need to feel like developer tooling; it can be part of the game fantasy.
2. Distinguish **Blueprint/Scene/Content Pack** types rather than calling everything a mod.
3. Safe logic editing can be exposed visually alongside building.

## 1.5 Trailmakers — Blueprints as highly portable creator artifacts

Research source:

- official wiki Blueprint documentation: https://trailmakers.wiki.gg/wiki/Blueprints

Confirmed pattern:

- built creations are saved as Blueprints and are central to sharing.
- Trailmakers encodes vehicle data into a PNG so the same file is both visually recognizable and portable.

What to take:

- Every Desktop Buddy Blueprint/content pack should have a strong generated preview and be easy to move/share locally.

What not to copy literally:

- Do not hide authoritative Desktop Buddy content graphs inside PNG metadata. Separate versioned JSON/data from preview PNGs is simpler to validate, migrate and threat-model.

## 1.6 TABS — in-game unit/faction creation mapped to safe existing abilities

Research source:

- official Unit Creator release announcement: https://store.steampowered.com/news/posts/?appids=508440&feed=steam_community_announcements

Confirmed pattern:

- TABS brought Unit Creator and Faction Creator directly into the game.
- the Workshop/content service was extended to carry those user-created units/factions.
- creators compose units from game-defined weapons, clothing and abilities instead of needing to program a new unit class.

What to take:

- Template/archetype-based creation is an effective beginner layer over a deeper system.
- Safe first-party capabilities can still produce large visible variety.

## 1.7 Teardown — ship the same conceptual tools you use yourself

Research source:

- Steam page: https://store.steampowered.com/app/1167630/Teardown/

Confirmed pattern:

- Teardown advertises the same editor the developers use for custom maps/tools/vehicles/game modes, built-in mod support, examples, scripting and Workshop integration.

What to take:

- Desktop Buddy's first-party systemic content should increasingly be authored through the same semantic definitions/capabilities exposed by Creator Studio.
- Ship example content/templates that are real and editable, not toy examples disconnected from the production system.

Security difference:

- Desktop Buddy should not copy Teardown's executable scripting trust model for ordinary Workshop content. Our default Workshop tier remains declarative.

---

# 2. Product concept: a Win98 Creator Studio

Working design name in this document: **Creator Studio**.

Do not lock the final player-facing name yet. Avoid `Buddy Lab` because that name already exists internally for development/testing. Possible eventual names:

- Creator Studio
- Item Studio
- Object Maker
- Blueprint Studio
- Mod Maker

It should look like another late-1990s desktop application inside Desktop Buddy, not a detached developer SDK.

Possible shell entry:

```text
Create
  New Item...
  Blueprints...
  Content Packs...
```

or a Start/Programs-style entry if that better fits the final Win98 shell.

The user's conceptual ladder is:

```text
PLAY
  place/use things
      ↓
BUILD
  connect/configure things
      ↓
BLUEPRINT
  save a selection
      ↓
CREATE ITEM
  paint + properties + archetype
      ↓
ADVANCED BEHAVIOR
  events + conditions + actions
      ↓
CONTENT PACK
  group reusable definitions
      ↓
WORKSHOP
  validate + publish
```

At every step the player can stop and still have created something useful.

---

# 3. Beginner workflow: make a new item without knowing what a mod is

The primary workflow must be wizard/template driven.

```text
File -> New Item

What are you making?

[ Prop ]
[ Melee ]
[ Gun ]
[ Projectile ]
[ Explosive ]
[ Construction Part ]
[ Device ]
[ Decoration ]
```

The selected template defines safe starting capabilities and which pages are shown.

Example: `Gun` initially compiles to trusted capabilities such as:

```text
PhysicsBody
GrabTarget
PaintedItemVisual
ProjectileEmitter
UseInput
OptionalSignalInput
```

No generated C# class exists.

The creator sees ordinary controls:

```text
Name:            My Awful Gun
Weight:          [ 1.2 ]
Fire type:       [ Semi-auto v ]
Projectile:      [ Basic Bullet v ]
Fire rate:       [---|-----]
Spread:          [--|------]
Recoil:          [----|----]
Damage preset:   [ Medium v ]
```

Advanced exact bounded values can appear behind an `Advanced...` button.

---

# 4. Sprite / item painting should be first-class

This is a major differentiator and should reuse the project's existing painting architecture rather than creating a second unrelated raster editor.

## 4.1 Shared paint-core goal

Extract/generalize reusable editor services from Paint Buddy where appropriate:

- brush engine;
- eraser;
- fill if/when approved for item creator;
- color picker;
- swatches/history;
- undo/redo command history;
- zoom/pan;
- selection/copy/paste if Creator Studio needs it;
- layers for creator-source documents;
- PNG encoding through project-owned safe stores.

Do not entangle item painting with Buddy body-part mapping. Reuse low-level raster/editor primitives, not Buddy-specific geometry assumptions.

## 4.2 Suggested initial canvas

Do not lock exact production dimensions until art/performance profiling, but target a deliberately small pixel-friendly source canvas such as:

```text
64x64
128x128
256x256 maximum for ordinary items
```

The editor can scale pixels sharply for a retro workflow.

The runtime asset validator enforces the selected source sizes and decoded byte budget.

## 4.3 Layers

Creator source documents may support a small bounded layer stack because layers are extremely useful while drawing.

Example:

```text
[ Muzzle details ]
[ Main body ]
[ Grip ]
[ Sketch ] (hidden)
```

Published/runtime content does not need arbitrary compositor semantics. The creator can preserve layers in its **local editable source document**, while export flattens to one validated runtime PNG unless a later trusted rendering capability explicitly needs multiple surfaces.

This keeps Workshop runtime simple and safe while making the editor pleasant.

## 4.4 Markers

After painting a functional object, the player places semantic markers directly on the preview:

Gun:

```text
[Handle]
[Muzzle]
[Optional casing eject]
```

Melee:

```text
[Handle]
[Sharp region / impact region]
```

Device:

```text
[Input port]
[Output port]
```

Projectile:

```text
[Forward direction]
[Impact origin]
```

Markers are bounded semantic data. They never become node paths or arbitrary script callbacks.

---

# 5. Bridging a painted 2D item into Desktop Buddy's 3D presentation

Desktop Buddy's authoritative physics remains 2D while the accepted presentation is frontal/three-quarter 3D. Custom item sprites therefore need an explicit trusted presentation route.

Do **not** accept arbitrary meshes from Workshop merely to make custom items possible.

Plan a trusted capability:

```text
PaintedItemVisual
```

with two implementation candidates to prototype.

## Candidate A — textured card / thin trusted primitive

- creator PNG becomes a texture on a project-owned QuadMesh/thin box;
- known depth/thickness;
- transparent alpha;
- normal Desktop Buddy depth/interpolation routing;
- simplest and safest first version.

Pros:

- very low complexity;
- predictable GPU cost;
- no user geometry;
- robust validation.

Cons:

- can read flat at stronger three-quarter angles.

## Candidate B — safe automatic silhouette extrusion

A stronger long-term visual option:

```text
validated raster alpha
  -> project-owned contour extraction
  -> deterministic simplification
  -> hard vertex cap
  -> triangulation
  -> shallow extrusion
  -> front/back use creator texture
```

This could turn a player's painted gun silhouette into a small 3D object automatically while keeping all geometry generation under trusted code.

Safety/performance constraints:

- fixed source dimension caps;
- binary/thresholded alpha contour;
- maximum contour count;
- maximum vertices after simplification;
- reject pathological/non-finite geometry;
- deterministic triangulation;
- fixed maximum depth;
- no imported normals/materials/shaders/mesh streams.

This should be prototyped, not promised until it passes the transparent-3D look/performance gates.

## Physics collision stays separate

Presentation geometry must not silently become physics authority.

Creator chooses or accepts a generated **bounded 2D collision descriptor**:

```text
Box
Circle
Capsule
ConvexHullFromAlpha // trusted generated, simplified and hard capped
```

For the first Creator Studio slice, prefer one simple generated convex hull or one/two primitives. Never accept an arbitrary polygon with unbounded points from Workshop data.

Preview the physics outline visibly in the editor.

---

# 6. Item authoring pages

The app should feel like a simple editor first and expose depth progressively.

Suggested tabs/pages:

```text
[ General ]
[ Paint ]
[ Physics ]
[ Function ]
[ Ports ]
[ Test ]
[ Publish ]
```

`Advanced Behavior...` opens the event graph for creators who need it.

## General

- name;
- category/template;
- description;
- tags;
- icon/preview generated from item;
- local content-pack destination.

## Paint

- raster editor;
- layers in source file;
- anchor/marker placement;
- optional PNG import with the same hostile image validation used by Workshop.

## Physics

- mass;
- bounce;
- friction where exposed;
- gravity scale;
- collision preview;
- material;
- breakability/durability where allowed.

## Function

Template-specific safe settings.

Gun example:

- projectile;
- rate;
- burst;
- spread;
- recoil;
- magazine/reload only when implemented safely;
- muzzle sound from trusted/default library or safe custom audio when custom audio is authorized.

Melee example:

- blunt/sharp classification;
- impulse envelope;
- pierce capability if authorized;
- break threshold.

Explosive example:

- fuse;
- radius;
- force;
- ignition behavior;
- hard envelope caps.

## Ports

Visual placement and naming of typed signal ports.

Only safe types supported by the systemic signal framework exist.

## Test

One button should launch the local item into an isolated/temporary Creator Test scene or temporary sandbox layer using the **same compiled definition** that real gameplay will use.

Actions:

```text
[Test Spawn]
[Reset Test]
[Drop]
[Fire/Use]
[Validate]
```

No restart.

## Publish

- validation summary;
- build compatibility (`Next Fest compatible` / `Full Release required` derived, not user-selected as authority);
- dependencies;
- asset budget;
- preview;
- local Save;
- Publish/Update Workshop item.

---

# 7. Advanced creator: visual behavior graph

The event/action vocabulary from the moddability source alignment becomes a visual editor.

This should look more like a simple late-1990s flowchart than a modern Unreal Blueprint clone.

Example:

```text
[ On Signal: FIRE ]
          |
          v
[ Cooldown Ready? ] ---- no ----> [ stop ]
          |
         yes
          v
[ Fire Projectile ]
          |
          v
[ Play Sound ]
          |
          v
[ Apply Recoil ]
```

Another example:

```text
[ On Impact ]
      |
[ Impact > 400 ]
      |
[ Spawn: Sparks ]
      |
[ Emit Signal: HIT ]
```

Design rules:

- event/condition/action nodes are selected from trusted registries;
- typed sockets prevent invalid connections;
- invalid graphs are highlighted before save/publish;
- cycles require explicit Timer/next-tick boundaries;
- graph budget meter is visible to the creator;
- no text box exists for arbitrary C#/GDScript/XML expressions;
- no reflection/property-name scripting;
- no arbitrary file/network operations.

The raw JSON representation is an implementation/export format, not the normal creator UI.

---

# 8. Blueprint Studio and Item Studio should be one ecosystem

People Playground's strongest low-friction idea is that a creation assembled in the room can simply become a saved contraption. Besiege similarly makes the building experience itself the creator.

Desktop Buddy should allow:

```text
Select sandbox entities
  -> Save as Blueprint...
```

Then from Blueprint Library:

```text
Open
Duplicate
Edit in Scene
Package...
Publish...
```

A Blueprint is a graph of already-valid definitions/instances/constraints/wires. It is safer and simpler than a Content Pack because it introduces no new definitions.

A Content Pack can define new reusable items/materials/devices.

A Scene can reference both.

This gives clear content classes:

```text
Buddy Character
Room Painting
Scene
Blueprint
Content Pack
```

Do not make the user understand the security distinction, but keep it explicit internally and in Workshop tags/validation.

---

# 9. Creator source documents vs runtime packages

A major design improvement over many mod systems is to distinguish **editable source** from **runtime artifact**.

Local creator project:

```text
creator-project.json
sources/
  gun.layers.json
  gun-layer-0.png
  gun-layer-1.png
  behavior.graph.json
```

Compiled runtime pack:

```text
manifest.json
content/
  entities.json
  behaviors.json
assets/
  textures/gun.png
preview.png
```

Workshop receives only the compiled, validated runtime pack.

Benefits:

- source can preserve layers/editor layout/comments without expanding runtime attack surface;
- Workshop validation stays small;
- runtime packages remain deterministic;
- local creator projects can evolve independently from the public mod API when necessary;
- the game can always rebuild a runtime pack from source before publishing.

Do not require Workshop subscribers to receive editable source projects. Authors may optionally export/share source separately later if desired.

---

# 10. Hot reload / immediate test loop

Borrow the strongest part of MaD Lab's workflow but remove the external-tool boundary.

When a creator presses `Apply` or `Test Spawn`:

1. editor source state is validated;
2. compile to temporary semantic definition;
3. calculate content hash;
4. replace only the Creator Test provider's prior definition version;
5. safely despawn/recreate the test instance when schema/physics shape requires it;
6. otherwise update safe presentation/properties when hot replacement is supported;
7. display validation errors in a Win98 Problems/Status panel.

Never let unvalidated editor source directly mutate arbitrary live nodes.

The same compiler/validator used here must be used for Workshop packages, with local creator diagnostics richer than import diagnostics.

---

# 11. Provider-qualified stable IDs

Creator content must not collide with first-party IDs or another creator's IDs.

Use provider-qualified semantic identity conceptually like:

```text
core:weapon.pistol
local:<pack-guid>/weapon.harpoon
workshop:<published-file-id-or-local-pack-id>/weapon.harpoon
```

The exact serialized syntax can be designed during MOD-0, but requirements are:

- stable within the pack;
- no global string collisions;
- references inside a pack can use short/local aliases that compile to qualified IDs;
- imported Workshop publisher metadata is provenance, not authority;
- local clone/duplicate receives a new local pack identity when appropriate.

Blueprints/Scenes retain unresolved qualified references when a dependency is missing.

---

# 12. Dependency UX

Factorio/Besiege/Workshop ecosystems show that creator content often depends on other content.

Keep dependency rules simple:

- exact pack identity;
- minimum compatible version or API range;
- hard vs optional dependency only if optional dependencies become necessary;
- maximum dependency depth/count;
- no cyclic hard dependencies.

Creator Studio should resolve dependencies automatically when selecting another pack's definition.

Publish validation shows:

```text
Dependencies
  [x] Core Content
  [x] Acme Weapons Pack >= 1.2
```

At install/load time a missing dependency produces a typed placeholder instead of deletion/corruption.

---

# 13. Workshop security remains stricter than local creation

Creator Studio can make authoring feel permissive without making Workshop execution permissive.

Local source may contain editor-only fields, layer stacks and layout coordinates.

Workshop runtime pack accepts only the specific schemas/assets authorized by `MODDABILITY_AND_WORKSHOP_SECURITY_SOURCE_ALIGNMENT_2026-09-07.md`.

Every publish performs a complete hostile-style validation of the generated pack before Steam receives it. This catches the same issues the subscriber would otherwise hit.

On Workshop update:

- changed content gets a new content hash;
- subscriber validates from scratch;
- previous validated local copy remains available until replacement succeeds;
- an invalid/broken update cannot erase active Scene content.

No creator tool is allowed to write into another Workshop item's upload directory or arbitrary game saves.

---

# 14. Performance and abuse budgets should be visible during authoring

Creator Studio should show creators why something is invalid before Workshop rejects it.

Possible Status panel:

```text
Item budget
  Texture:       42 KB / 512 KB
  Collision:      8 / 12 vertices
  Graph nodes:   14 / 64
  Spawn actions:  1 / 4 per event
  Timers:          1 / 8

[ OK ]
```

For Blueprints:

```text
Entities     17 / 32 (Next Fest)
Constraints   9 / 32
Devices       3 / 16
Wires         7 / 48
```

Full Release can show its larger limits.

Compatibility is derived automatically from definitions/capabilities/budgets used.

---

# 15. Recommended phased delivery

The creator architecture should be laid during the systemic foundation even if the full UI arrives later.

## CREATOR-0 — authoring seams during SCENE/MOD foundation

Implement/design alongside MOD-0 and systemic foundations:

- provider-qualified stable IDs;
- registry-backed definitions;
- source-vs-compiled document separation;
- creator-local provider;
- shared compile/validation result model;
- capability exposure metadata;
- behavior graph DTOs even if graph UI is later;
- safe painted-item visual capability seam;
- semantic item markers (handle/muzzle/ports);
- missing-provider placeholder semantics.

Exit:

A locally constructed semantic definition can be compiled/validated/spawned without any bespoke switch on its exact content ID.

## CREATOR-1 — Blueprint-first in-game slice

- select Scene sandbox entities;
- Save as Blueprint;
- thumbnail/preview;
- rename/duplicate/delete;
- spawn Blueprint back into active Scene;
- validation/budget display.

This should be prioritized because it reuses the actual Next Fest building loop and gives immediate creator value.

## CREATOR-2 — simple Item Creator / Painter

Recommended first authorable templates:

1. Prop
2. Melee
3. Gun

Features:

- Win98 Creator Studio shell;
- General/Paint/Physics/Function/Test pages;
- pixel-friendly raster canvas;
- local creator layers;
- marker placement;
- safe collision generation/selection;
- Test Spawn;
- save into local content library.

The **Gun** template is valuable for marketing because the player can visibly draw a bizarre weapon and immediately fire it.

Keep the first Gun behavior deliberately bounded to existing trusted firearm/projectile capabilities.

## CREATOR-3 — local Content Packs

- group definitions;
- dependencies;
- versioning;
- package compiler;
- local enable/disable;
- example/template packs;
- creator source migration.

## CREATOR-4 — visual behavior graph

- event nodes;
- condition nodes;
- action nodes;
- typed sockets;
- graph diagnostics;
- runtime budgets;
- immediate test/hot reload.

## CREATOR-5 — Workshop publish/import for content packs

Only after local schemas and validator are stable:

- compile source -> runtime package;
- full publish validation;
- generated preview;
- Workshop tags;
- update existing item;
- immutable hostile import pipeline;
- last-known-good local version;
- dependency handling;
- explicit enable/use.

## CREATOR-6 — richer templates and presentation

Full Release expansion candidates:

- projectile creator;
- explosive creator;
- construction-part creator;
- device creator;
- material creator;
- audio where safe;
- richer paint tools;
- silhouette extrusion if prototype proves worthwhile;
- more semantic marker types;
- starter examples derived from real first-party definitions.

---

# 16. Next Fest recommendation

Do **not** allow Creator Studio to destabilize the required Next Fest systemic vertical slice.

However, a small Creator Studio slice would be unusually marketable if the foundation is ready in time.

Recommended optional Next Fest creator slice, in priority order:

1. **Blueprint save/load** — should already be part of the planned systemic vertical slice.
2. **Painted Prop creator** — draw an object, choose safe mass/bounce/material, Test Spawn.
3. **Painted Gun template** — draw a gun, place handle+muzzle markers, choose trusted projectile/fire-rate/spread/recoil ranges, Test Fire.

Do not put the visual behavior graph or general Workshop Content Packs on the critical Next Fest path unless the core systemic work is already stable.

The Normal Demo remains unchanged by this new creator system.

The cumulative build relationship remains:

```text
Normal Demo
  < Next Fest Demo (optional safe Creator Studio slice)
    < Full Release (full Creator Studio breadth)
```

If Creator-2 misses Next Fest, the underlying MOD/Creator seams still land with the systemic architecture so the feature can arrive later without refactoring.

---

# 17. UX details that can make Desktop Buddy distinct

## 17.1 Use familiar Win98 metaphors

Examples:

- `File > New Item...`
- `File > Save`
- `File > Publish to Workshop...`
- right-click `Properties...`
- property-sheet tabs;
- small `Problems` list on validation;
- floppy-disk Blueprint icon;
- wizard pages for template creation;
- status bar showing dimensions/budget.

Avoid modern node-editor chrome until Advanced Behavior is opened.

## 17.2 Test in one click

The most important creator button is not Publish. It is:

```text
[Test Spawn]
```

The user should be able to paint two pixels, test, come back, paint again and test again within seconds.

## 17.3 Fork an existing item safely

Right-click an eligible first-party or Workshop-safe definition:

```text
Create Variant...
```

This creates a new local source project initialized from the **public semantic definition**, not from internal Godot Resource paths/code.

This is the easiest way for new creators to learn.

First-party definitions must explicitly declare whether they are exposed as creator templates.

## 17.4 Separate visual remix from behavior remix

A player can choose:

```text
Appearance only
```

and reuse the exact trusted behavior of a base template.

This makes sprite painting accessible without asking the creator to reason about physics/functionality.

## 17.5 Generated preview should show the actual compiled item

Do not let authors upload an unrelated preview as the only in-game preview.

Generate a canonical preview from the validated compiled item. Steam can still permit description/screenshots through its normal item page if desired.

This improves discoverability and reduces deceptive previews.

---

# 18. First-party development should dogfood Creator Studio semantics

As systemic systems mature, new first-party content should preferentially be expressible through the same semantic definition/capability model used by creator content.

Examples:

- new gun = authored definition + trusted capabilities + art;
- new signal device = definition + ports + behavior capability;
- new construction material variant = bounded material definition;
- new simple prop = definition + visual + physics parameters.

This does **not** mean shipping first-party source files in Workshop format or forcing every specialized feature through the declarative layer.

It means bespoke hardcoded ID-specific behavior should be reviewed as a cost: could the behavior become a reusable capability/action instead?

This is how the Full Release content library can grow quickly after the Next Fest vertical slice while simultaneously expanding what users can create.

---

# 19. Security invariants specific to painted/custom items

1. Raster images are decoded only through project-owned validation with strict dimensions/bytes/pixel caps.
2. Creator PNG pixels never define executable metadata.
3. Runtime package data remains separate from preview/art image metadata.
4. Collision is a trusted bounded descriptor or trusted generated hull with hard complexity limits.
5. Painted presentation never changes Buddy rig geometry or global physics rules.
6. Item markers are semantic coordinates with finite/range validation.
7. User assets cannot select arbitrary shader/material/Resource paths.
8. User content cannot directly award credits, unlocks or achievements.
9. User-created weapons use bounded reward-safe/no-extra-payout policy.
10. Spawn/particle/audio/behavior budgets are enforced at compile and runtime.
11. Workshop Content Pack updates are revalidated by content hash from immutable staging.
12. Missing/invalid packs preserve unresolved Scene/Blueprint state rather than deleting it.
13. Local Creator Test content runs through the semantic compiler and capability layer, never arbitrary editor callbacks.

---

# 20. Acceptance journeys

## Beginner painted gun journey

1. Open Creator Studio.
2. `New Item -> Gun`.
3. Paint a crude gun silhouette using familiar paint controls.
4. Place Handle marker.
5. Place Muzzle marker.
6. Choose Basic Bullet.
7. Change Spread and Recoil within visible safe ranges.
8. Press Test Spawn.
9. Pick up/fire the custom gun in the test area.
10. Return to Paint, modify it, Test Spawn again without restarting.
11. Save locally.
12. Find the custom gun in the local creator category and spawn it in a normal eligible Scene.

No files, JSON, code or external applications are required.

## Advanced behavior journey

1. Duplicate the custom gun into a new variant.
2. Open Advanced Behavior.
3. Add `On Signal -> Fire Projectile`.
4. Add one Pulse input port.
5. Validate.
6. Place gun in Scene.
7. Wire Button -> custom gun.
8. Press Button; gun fires.
9. Save the setup as Blueprint.

## Workshop safety journey

1. Creator packages item for publishing.
2. Pack compilation flattens runtime sprite and strips editor-only layers/layout.
3. Validator derives capabilities, dependencies and build compatibility.
4. Publish uses immutable staging.
5. Another account subscribes.
6. Download copied once to incoming staging.
7. Full hostile validation succeeds.
8. Item becomes an explicitly enabled local copy.
9. Author later uploads a malformed update.
10. Update fails validation; prior local validated version continues to satisfy existing Scenes/Blueprints.

---

# 21. Design conclusion

Desktop Buddy can occupy a useful gap between existing sandbox creator ecosystems:

- People Playground: immediate contraptions, but deep mods normally require C# and carry executable-code risk.
- MaD2: deep generic item functionality plus a dedicated MaD Lab, but the deeper model-authoring workflow is separate from ordinary in-game play.
- Besiege: creator-first building/level logic and Workshop.
- TABS: approachable in-game template creator using safe built-in capabilities.
- Teardown: strong same-tools-as-developers philosophy, but scripting is a broader trust model than Desktop Buddy needs for ordinary Workshop subscriptions.

Desktop Buddy's differentiator should be:

> **Build it, paint it, give it safe behavior, test it, save it and share it — all inside the cursed Win98 desktop.**

That is both a product feature and an architecture constraint. The systemic foundation should be built so this editor is a thin authoring client over stable semantic definitions/capabilities, not a second gameplay implementation.