# Desktop Buddy — Moddability and Workshop Security Source Alignment

Status: **owner-directed architecture/research supplement**  
Recorded: 2026-09-07  
Planning branch: `plan/full-release-systemic-sandbox`  
Applies to: Normal Demo compatibility, Next Fest systemic vertical slice, Full Release systemic expansion, later Workshop content

This document records the architecture direction that new Desktop Buddy systems should be deliberately designed for **high moddability without treating Steam Workshop content as trusted executable code**.

It supplements the existing Workshop hostile-data rules and the systemic-sandbox plans. It does not authorize arbitrary C#, GDScript, DLL, Godot Resource, scene, shader, native-code, or PCK execution from Workshop content.

The central rule is:

> Give modders many safe gameplay capabilities to compose, not host-machine privileges.

The target is for most useful mods to be creatable as declarative content packs, behavior graphs, item definitions, material definitions, device definitions, presets and Blueprints. First-party content and mod content should converge on the same engine-free semantic model wherever possible, while retaining separate trusted loaders: internal authored Godot Resources may compile into that model, but hostile Workshop packages never go through Godot's general ResourceLoader.

---

# 1. Why this is a product requirement, not a post-release add-on

The systemic sandbox is exactly the point where the project either becomes naturally moddable or becomes expensive to retrofit later.

If every new gun, device, material, effect and interaction is implemented as a bespoke `switch` over first-party IDs, later mod support will require rewriting gameplay architecture.

If instead each system is built around:

- stable semantic definition IDs;
- typed registries;
- capability descriptors;
- bounded property descriptors;
- event inputs;
- action outputs;
- engine-free persistent state;
- validated references between definitions;

then first-party expansion and user-created content are both consumers of the same underlying systems.

Therefore all new Next Fest and Full Release systemic work should be evaluated with the question:

> Could a validated external definition use this system without needing a new C# code path?

The answer does not always need to be yes. Some behaviors will remain first-party-only capabilities. But bespoke ID checks should be the exception rather than the default design.

---

# 2. Research findings

## 2.1 People Playground demonstrates both the value and risk of executable Workshop mods

People Playground's modding documentation has historically exposed C# scripts. This is extremely powerful and helps explain its large mod ecosystem, but executable code creates a much larger trust boundary than declarative content.

In February 2026, People Playground temporarily disabled its Workshop after a malicious mod propagated by overwriting users' own Workshop uploads and damaged game data. Its subsequent changes included stronger scanning, content-hash-aware trust and requiring changed mods to be manually approved rather than silently auto-updating.

Relevant public references:

- https://steamcommunity.com/app/1118200/announcements/
- https://github.com/studio-minus/people-playground-changelog
- https://www.studiominus.nl/ppg-modding/tutorials/tutorialCreatingMod.html

Desktop Buddy should learn from the creative success of the API without inheriting the same executable-code default.

## 2.2 Mutilate-a-Doll 2 shows how far generic properties and triggers can go

MaD2's content tooling exposes generic item properties, triggers, projectile behavior, environmental responses and composable item functionality. Its developer has repeatedly generalized functionality so the same behavior can be reused by different authored models.

This is a strong fit for Desktop Buddy: many creator needs can be satisfied by a sufficiently broad vocabulary of safe triggers, conditions and actions rather than arbitrary scripting.

Relevant public references:

- https://steamcommunity.com/app/665370/discussions/0/1795152172925405479/
- https://steamcommunity.com/app/665370/discussions/0/1840188800803657226/
- https://steamcommunity.com/app/665370/allnews/

## 2.3 Factorio demonstrates staged, API-driven mod architecture

Factorio's official mod API separates prototype/data definition from runtime/event behavior. Mods consume explicit game APIs rather than directly owning engine internals.

Desktop Buddy should borrow the architectural principle, not the exact Lua implementation:

- definitions/prototypes first;
- explicit runtime events;
- explicit actions/capabilities;
- deterministic ownership and lifecycle;
- versioned compatibility.

Reference:

- https://lua-api.factorio.com/latest/

## 2.4 Luanti demonstrates capability restriction and explicit trust elevation

Luanti runs ordinary mods in a restricted environment and separates normal mod capabilities from explicitly trusted access to insecure/OS-level functionality. Its documentation warns against disabling mod security.

The useful principle for Desktop Buddy is that **content capability and host capability are different things**. A mod may be allowed to spawn a projectile or ignite an object without being allowed to read arbitrary files, execute programs or access the network.

References:

- https://docs.luanti.org/for-players/installing-mods/
- https://docs.luanti.org/for-creators/api/lua-environment/

## 2.5 .NET cannot safely sandbox arbitrary in-process C# with AssemblyLoadContext

Microsoft explicitly states that `AssemblyLoadContext` provides assembly loading isolation but **does not provide security**; loaded code has the process's permissions. Modern .NET also does not support Code Access Security as a meaningful sandbox boundary and recommends OS-level isolation for unknown code.

Therefore:

- a custom `AssemblyLoadContext` is useful for dependency/version management;
- it must never be represented as protection against malicious Workshop code;
- arbitrary Workshop C# loaded into the Desktop Buddy process is out of scope for the safe mod path.

References:

- https://learn.microsoft.com/dotnet/api/system.runtime.loader.assemblyloadcontext
- https://learn.microsoft.com/dotnet/standard/security/secure-coding-guidelines
- https://learn.microsoft.com/dotnet/core/porting/net-framework-tech-unavailable

## 2.6 Steam Workshop transports folders; the game still decides what those bytes mean

Steam's ISteamUGC system installs Workshop item folders for the application to consume. Steam supports tags, developer metadata and soft dependencies, but application compatibility/enforcement remains the game's responsibility.

Therefore Workshop tags such as `Demo Compatible`, `Blueprint`, `Content Pack`, or dependency metadata are useful for discovery but **never security assertions**. Desktop Buddy validates the actual installed snapshot before import/use.

References:

- https://partner.steamgames.com/doc/features/workshop/implementation
- https://partner.steamgames.com/doc/api/isteamugc
- https://partner.steamgames.com/doc/features/workshop/tags

## 2.7 WebAssembly is a credible future script boundary, not a launch dependency

Wasmtime describes WebAssembly as sandboxed by design and WASI filesystem access as capability-based. A maintained .NET embedding exists.

This makes WebAssembly a possible future path for advanced scripted mods, provided the host exposes only explicit imports and enforces memory/execution budgets. It still adds a significant runtime/security/compatibility dependency and should not be required for the Next Fest or initial Full Release systemic architecture.

References:

- https://docs.wasmtime.dev/security.html
- https://docs.wasmtime.dev/
- https://github.com/bytecodealliance/wasmtime-dotnet

---

# 3. Three-tier mod model

Desktop Buddy should distinguish three fundamentally different trust levels.

## Tier A — Safe declarative UGC

Examples:

- room paintings;
- Buddy characters/paint;
- contraption Blueprints;
- Scene templates where later authorized;
- tool/property presets;
- custom sandbox entities built from approved capabilities;
- material definitions within bounded parameters;
- devices assembled from approved signal/action components;
- custom visual/audio assets within approved formats and budgets.

Tier A is the preferred Steam Workshop format.

Properties:

- no executable code;
- no Godot Resource loading from the package;
- no arbitrary scene/node/class names;
- no OS/network/filesystem capability;
- strict versioned schemas;
- bounded values and counts;
- safe to auto-download from Steam because it is still validated before activation;
- changed Workshop contents are revalidated by content hash before use.

## Tier B — Declarative behavior packs

This is still data-only Workshop content, but more expressive than static definitions.

A behavior pack may define:

- events;
- conditions;
- bounded state variables;
- timers;
- signal ports;
- action graphs;
- references to other validated definitions.

It is interpreted by project-owned runtime code. It cannot invoke arbitrary methods.

This tier is the main mechanism for achieving high moddability safely.

## Tier C — Advanced scripted mods

This is **not authorized for Workshop implementation by this document**.

If the project later wants arbitrary logic beyond the declarative graph, choose one of these trust models explicitly:

1. local-only advanced C#/Godot mods with a clear warning that they have full process privileges;
2. out-of-process code with an OS sandbox and narrow IPC API;
3. WebAssembly modules with an explicit capability host, no ambient WASI access by default, and strict memory/execution limits.

Do not mix Tier C into ordinary one-click Workshop subscriptions merely for parity with People Playground.

---

# 4. Unified semantic definition architecture

The most important implementation decision is that first-party systemic content and external declarative content resolve into the same engine-free domain definitions.

Raw authoring formats may differ:

```text
first-party .tres / source data
        -> trusted compiler
        -> engine-free definition

Workshop JSON + approved assets
        -> hostile-data validator/compiler
        -> engine-free definition
```

After compilation, runtime gameplay should not care whether a definition originated from an internal Resource or a validated package, except where an explicit capability is marked first-party-only.

Do **not** make external packages `.tres`, `.tscn`, PCKs or copied project folders simply to reuse the internal loader.

## 4.1 Stable provider-qualified IDs

All moddable definitions require globally stable semantic IDs.

Conceptual form:

```text
core:construction/wood_beam
core:device/button
ugc:<pack-guid>/entity/my_launcher
ugc:<pack-guid>/material/blue_rubber
```

The exact syntax can change, but it must provide:

- a built-in namespace;
- a package namespace;
- stable definition identity;
- no filesystem semantics;
- no class/node names;
- no Steam display-name dependence.

Workshop `PublishedFileId` is provenance, not the semantic identity of every definition in the pack.

## 4.2 Provider metadata

Compiled definitions carry trusted provenance metadata outside normal gameplay properties:

```text
ProviderKind      // Core, LocalPack, WorkshopPack
PackId
PackVersion
ContentHash
SourceWorkshopId? // provenance only
Availability      // NormalDemo, NextFestAndFull, FullOnly, etc.
```

The runtime does not let the package assign itself a more privileged availability class. Compatibility is derived by the validator from actual referenced capabilities/definitions.

---

# 5. Capability composition instead of bespoke item code

New sandbox objects should increasingly be composed from project-owned capabilities.

Initial capability families can include:

```text
PhysicsBody
RenderableSprite
Durability
Breakable
SharpContact
Flammable
HeatReactive
ProjectileEmitter
ExplosionEmitter
ForceEmitter
RepairEffect
SignalInput
SignalOutput
Timer
PistonActuator
WeaponTrigger
ConstraintAnchor
AudioEmitter
ParticleEmitter
SpawnEmitter
BuddyDamageEffect
BuddyCareEffect
```

A capability is implemented once in trusted C#/domain/runtime code.

A definition configures it using bounded typed parameters.

Example:

```text
Custom Harpoon Launcher
  PhysicsBody
  RenderableSprite
  SignalInput(port = fire)
  ProjectileEmitter(
      projectile = ugc:.../harpoon,
      count = 1,
      speed = 700,
      spread = 0
  )
```

The package cannot say:

```text
CSharpClass = "System.Diagnostics.Process"
Method = "Start"
```

or:

```text
NodePath = "/root/..."
PropertyName = "script"
```

## 5.1 Capability exposure levels

Every capability declares one exposure level:

```text
CoreOnly
LocalDeclarative
WorkshopDeclarative
```

This lets the project implement a specialized trusted capability for first-party content without accidentally exposing it to hostile packages.

The default for a new capability should be `CoreOnly` until its parameters, performance envelope and hostile-input behavior are reviewed.

---

# 6. Declarative behavior graph

The behavior graph is the main answer to "how do we make mods powerful without arbitrary code?"

## 6.1 Event nodes

Initial safe event vocabulary can grow over time:

```text
OnSpawn
OnUse
OnSignal
OnTimer
OnCollision
OnImpactThreshold
OnBreak
OnDestroyed
OnIgnited
OnExtinguished
OnBuddyContact
OnProjectileHit
OnPropertyChanged
```

Events expose a small typed context, not raw Godot objects.

Example collision context:

```text
SelfInstanceId
OtherSemanticKind
RelativeSpeed
ContactNormal
ImpactMagnitude
```

No RID, Node, object reference or arbitrary reflection surface is exposed.

## 6.2 Condition nodes

Examples:

```text
CompareNumber
CompareBool
HasTag
MaterialIs
ImpactAbove
StateEquals
And
Or
Not
RandomChance
CooldownReady
```

Random behavior uses a project-owned deterministic RNG stream, not system randomness or clocks.

## 6.3 Action nodes

Examples:

```text
EmitSignal
ApplyImpulse
ApplyDamage
Repair
Ignite
Extinguish
SetState
SetSafeProperty
SpawnDefinition
PlaySound
PlayParticleEffect
StartTimer
DestroySelf
```

Every action has:

- a schema;
- type/range validation;
- per-call cost;
- per-tick budget behavior;
- release availability.

## 6.4 Cycles and runaway graphs

Immediate recursive graph execution is prohibited.

Cycles must cross an explicit delayed boundary such as:

- Timer;
- next simulation tick signal;
- cooldown/event re-entry.

The runtime enforces budgets such as:

```text
max behavior nodes per definition
max event deliveries per instance per tick
max actions per pack per tick
max spawned children per action
max spawned children alive per parent/pack
max timers
max signal hops per tick
```

When a budget is exceeded, the offending behavior is throttled/disabled with a typed diagnostic rather than hanging the game.

---

# 7. Mod-friendly design requirements for each new systemic feature

## 7.1 Construction

Do not hardcode the toolbox to an enum of Beam/Block/Wheel.

Use a registry query:

```text
category = ConstructionPart
availability <= current build
provider enabled
```

Each part definition owns semantic dimensions, collision descriptor, material and allowed constraints.

Workshop definitions may initially use only bounded trusted primitive collision shapes. Arbitrary imported collision meshes are not required for useful modding.

## 7.2 Properties

Properties already need stable IDs and typed descriptors. Make that registry public to the declarative layer.

A mod definition can expose/configure only descriptors marked Workshop-safe.

Do not let mod data invent arbitrary property names interpreted through reflection.

## 7.3 Materials

Material behavior should be registry-driven:

```text
MaterialId
Density range
Restitution range
Friction range
Durability range
Flammability class
Heat response class
Conductivity class // when implemented
Break effect reference
```

Workshop custom materials use bounded parameter envelopes. A mod cannot install a custom material C# class.

## 7.4 Devices/signals

Ports are stable semantic IDs and types:

```text
Bool
Pulse
Number // only when a later device needs it
```

Devices query behavior/capability registries. Avoid one giant switch on `DefinitionId`.

## 7.5 Weapons/projectiles

Separate:

- launcher behavior;
- projectile definition;
- impact effect;
- visual/audio presentation.

This allows creators to combine safe pieces in many ways.

Projectile speed, count, lifetime and spawn rate remain bounded by safe descriptors.

## 7.6 Damage/repair

Expose semantic actions (`ApplyStructuralDamage`, `RepairIntegrity`, `Ignite`) rather than exposing `BuddyProgressState`, `RigidBody2D`, limb nodes or `EconomyService`.

Workshop behavior cannot directly award credits, achievements, unlocks or mutate global progression.

## 7.7 Scenes/Blueprints

Scene and Blueprint documents reference stable definition IDs plus validated semantic overrides.

If a required mod pack is missing, load a placeholder and preserve the original serialized instance state so reinstalling the pack can restore it. Never silently delete unknown mod-owned objects during load/save.

---

# 8. Authoring experience: moddability must be easy, not merely possible

A safe schema is insufficient if creators need to hand-edit large JSON documents.

Plan toward a project-owned **Desktop Buddy Mod Lab** / Content Lab built on the same Win98 tooling principles.

Initial creator workflow:

```text
New Content Pack
  -> choose template
  -> choose image
  -> choose object archetype
  -> configure bounded physics/properties
  -> add capabilities
  -> wire events/conditions/actions
  -> Test Spawn
  -> Validate Pack
  -> Save Locally
  -> Publish to Workshop
```

Templates should include:

- simple prop;
- sharp prop;
- throwable/explosive;
- firearm;
- projectile;
- construction part;
- signal device;
- material variant;
- decorative item.

The editor should generate canonical package JSON; modders should not be required to know the raw schema for ordinary content.

Also publish:

- JSON Schema files;
- example packs;
- stable ID/API reference;
- migration notes;
- package validator CLI or headless command;
- local hot-reload command for creator content.

Local hot reload may be permissive for iteration but still routes through the same semantic validator before runtime activation.

---

# 9. Workshop package format

Conceptual content pack:

```text
manifest.json
content/
  entities.json
  materials.json
  behaviors.json
assets/
  textures/...
  audio/...
preview.png
```

Exact paths should be schema-owned and whitelist-validated.

## 9.1 Manifest

```text
SchemaVersion
PackId
PackVersion
DisplayName
Description
RequiredGameApi
Definitions[]
Dependencies[]
DeclaredAssets[]
ContentHashes
```

Do not deserialize arbitrary CLR type metadata from package JSON.

## 9.2 Safe asset policy

Begin conservatively.

Candidates for Workshop-safe runtime assets:

- PNG raster images with strict byte/dimension/pixel caps;
- Ogg Vorbis audio with strict compressed-byte, decoded-duration/sample-rate/channel caps;
- optionally WAV for creator convenience if the decoded memory budget is enforced.

Do not initially accept:

- `.tscn`;
- `.tres`;
- `.res`;
- PCK/ZIP packages interpreted as Godot projects;
- scripts;
- DLLs;
- executables;
- native libraries;
- shaders;
- SVG/XML/HTML;
- fonts;
- arbitrary 3D model formats;
- arbitrary video.

New formats require explicit threat/performance review.

---

# 10. Hostile Workshop import pipeline

Extend the current immutable Workshop staging model rather than weakening it.

Required flow:

```text
Steam installed folder
  -> one immutable project-owned incoming snapshot
  -> inventory every path
  -> reject links/reparse points/traversal/absolute paths
  -> enforce total/per-file byte caps
  -> enforce exact allowed extensions/paths
  -> hash files
  -> parse bounded manifest
  -> validate schema/version
  -> validate dependency graph
  -> validate stable IDs and references
  -> validate capability exposure
  -> validate behavior graph and complexity budgets
  -> decode/validate assets under memory/dimension/duration caps
  -> derive build compatibility
  -> compile engine-free definitions
  -> copy to locally owned validated package store
  -> activate only through explicit local enable/use state
```

Never execute or load data directly from Steam's mutable Workshop cache.

## 10.1 Content hashes and updates

Store a canonical content hash for each validated package.

When Steam updates a package:

- treat changed bytes as a new hostile snapshot;
- validate from scratch;
- do not inherit trust merely from publisher identity or previous approval;
- keep the last known-good locally imported version available until the new version validates;
- do not corrupt an active Scene because a Workshop author pushed a bad update.

This keeps Workshop updates convenient without copying People Playground's historical executable auto-update risk.

## 10.2 Steam metadata is advisory

Use Steam tags/key-value metadata for discovery such as:

```text
Content Pack
Blueprint
Next Fest Compatible
Full Release Required
```

but calculate compatibility from the package after download.

A malicious uploader cannot unlock Full-only or unsafe capabilities by setting a tag.

---

# 11. Runtime resource budgets

Safety includes denial-of-service resistance, not only code execution.

Every moddable subsystem needs explicit budgets.

Examples:

- entities per Scene;
- awake bodies;
- constraints;
- wires;
- devices;
- behavior graph node count;
- events/actions per tick;
- spawned child entities;
- particle emissions;
- concurrent audio voices;
- texture bytes;
- decoded audio bytes;
- timers;
- string lengths;
- dependency depth;
- JSON depth/collection counts.

Budgets apply to first-party and mod content where feasible so the normal game exercises the same paths. Trusted first-party content may have separately measured higher limits only when required.

A package that exceeds a runtime budget should fail or throttle locally; it must never stall the routed 120 Hz gameplay loop indefinitely.

---

# 12. Security boundaries that remain non-negotiable

Workshop declarative content must never be able to:

- use reflection;
- instantiate arbitrary CLR/Godot types;
- call arbitrary methods;
- load assemblies;
- load arbitrary Godot Resources;
- mount resource packs;
- read arbitrary files;
- write outside its own project-owned data namespace;
- launch processes;
- access environment variables;
- access clipboard/shell/native APIs;
- make network/HTTP requests;
- invoke Steam publishing/update APIs directly;
- change another Workshop item;
- mutate Steam subscriptions;
- alter player wallet/unlocks/achievements directly;
- modify save files outside semantic save APIs.

Only the host application owns SteamUGC calls. This specifically prevents a behavior equivalent to the 2026 People Playground incident from being expressible through the safe Workshop API.

---

# 13. Dependencies and interoperability

High moddability requires mods to work together.

## 13.1 Explicit dependency graph

Packs can declare required/optional package IDs and version ranges.

Resolve dependencies before activation. Reject cycles where ordering would be ambiguous.

Steam Workshop dependencies may be mirrored for user convenience, but the in-game package resolver remains authoritative because Steam describes those relationships as soft dependencies.

## 13.2 No global ID collisions

Provider-qualified IDs prevent two packs from both defining `laser_gun` globally.

## 13.3 Extension points

Prefer extension by composition:

- new definition references an existing capability;
- behavior listens to public semantic event;
- behavior emits public semantic action;
- pack declares compatibility with another pack.

Do not support runtime monkey-patching/replacing arbitrary first-party C# methods in the safe Workshop tier.

## 13.4 Overrides

If creator overrides are later useful, make them explicit and narrow:

```text
patch target = semantic definition ID
allowed fields = explicitly patchable descriptors
priority = deterministic
```

Workshop packs should not silently replace core definitions by matching a filename or Resource path.

---

# 14. Save compatibility and missing mods

Every Scene/Blueprint records the semantic package/definition references it depends on.

On missing content:

- preserve unknown records;
- spawn a lightweight `Missing Content` placeholder where needed;
- show which pack is required;
- do not simulate unknown behavior;
- allow the Scene to save without discarding the unresolved records.

When the package returns, the original instance can be restored.

This is important for long-lived RP Scenes and Workshop Blueprints.

Package schema migrations must be explicit and data-only for Tier A/B. A package may not run arbitrary migration code during save load.

---

# 15. Build compatibility

The cumulative build invariant remains:

```text
Normal Demo
  < Next Fest Demo
  < Full Release
```

A package's compatibility is derived from every capability/definition/property it uses.

Conceptual result:

```text
NormalDemoCompatible
NextFestCompatible
FullReleaseRequired
Unsupported
```

A Full Release player may use all lower-scope packages.

A Next Fest Demo player may use Normal-Demo-safe and Next-Fest-safe content but not packages requiring Full-only capabilities.

The normal Demo's current Workshop package types remain governed by their existing scope. Do not widen the normal Demo merely because the architecture becomes more moddable internally.

---

# 16. Future scripted mod path

Do not block future code mods architecturally, but do not depend on them.

The declarative API should define clean semantic host interfaces so that a future script runtime could call the same commands rather than gaining direct engine access.

If WebAssembly is later selected, the host should expose calls similar to:

```text
get_property(instance, property_id)
set_property(instance, property_id, bounded_value)
emit_signal(instance, port_id)
spawn(definition_id, transform)
apply_impulse(instance, vector)
subscribe(event_id)
```

and deliberately expose **no filesystem/network/process API by default**.

The runtime must also enforce:

- module memory limits;
- execution fuel/time budgets;
- instance count;
- host-call quotas;
- deterministic/safe clock/random interfaces;
- package-scoped storage only if later authorized.

This remains future research, not a requirement for the Next Fest slice.

---

# 17. Implementation changes to make now

Moddability should influence the new systemic implementation immediately even before the public Content Lab exists.

## MOD-0 — Registry and identity seams

While implementing Scene/systemic foundations:

- provider-qualified semantic IDs;
- definition registry independent of Godot Resource paths;
- property registry by stable IDs;
- capability registry;
- availability metadata;
- provenance metadata separate from gameplay state.

Exit gate: gameplay lookup does not depend on filename/node/class identity.

## MOD-1 — First-party systemic content through reusable definitions

As Next Fest parts/devices/materials are implemented:

- avoid definition-ID switches when capability composition suffices;
- add typed capability parameter descriptors;
- add public semantic event/action contracts;
- classify each new capability `CoreOnly` or `WorkshopDeclarative` intentionally.

Exit gate: at least several first-party Next Fest objects can be represented through the same semantic structures a future external definition would use.

## MOD-2 — Safe local content-pack compiler

Before broad Workshop content packs:

- versioned manifest;
- JSON schema;
- safe asset declarations;
- definition compiler;
- behavior-graph validator;
- dependency validator;
- budgets;
- local folder transport for CI.

Exit gate: a hand-authored local data pack can add a new safe prop/device without changing game C#.

## MOD-3 — Creator tooling

Add in-game or companion Content Lab workflows for templates, validation, test spawn and export.

Exit gate: a non-programmer can create a basic custom prop and a signal-driven item without manually writing JSON.

## MOD-4 — Workshop declarative content packs

Reuse existing Workshop immutable staging/emulator/provider architecture.

Exit gate: publish -> subscribe -> validate -> import/enable -> offline reuse works for one content pack and rejects hostile fixtures.

## MOD-5 — Dependency/update resilience

- package dependency graph;
- last-known-good version retention;
- missing-content placeholders;
- content-hash revalidation;
- version compatibility diagnostics.

Exit gate: broken or malicious updates do not destroy existing Scenes.

## MOD-X — Script runtime research

Only after the declarative API has proven insufficient:

- evaluate WebAssembly host prototype;
- measure Godot/.NET integration and binary footprint;
- threat model runtime escape/DoS;
- compare against out-of-process Windows sandbox;
- owner gate before any Workshop execution.

---

# 18. Testing requirements

Every Workshop-exposed definition/capability needs hostile-input tests.

Test classes include:

- unknown capability IDs;
- CoreOnly capability requested by UGC;
- invalid ranges, NaN, infinity;
- duplicate semantic IDs;
- dependency cycles;
- missing required dependencies;
- excessively deep/numerous behavior nodes;
- immediate recursive signal loops;
- spawn storms;
- huge strings/arrays;
- unknown file types;
- oversized images/audio;
- path traversal;
- absolute paths;
- symlinks/reparse points;
- undeclared files;
- hash mismatch;
- future schemas;
- changed Workshop content hash;
- missing mod while loading Scene;
- invalid mod update while previous validated version is active;
- Demo attempting to activate Full-required pack.

Fuzz the package parser/validator separately from Godot runtime where practical.

No malformed package should reach `ResourceLoader`, reflection, assembly loading or arbitrary Godot object construction.

---

# 19. Architectural decision summary

Desktop Buddy should aim for **MaD2-like declarative expressiveness plus Factorio-like explicit APIs**, while learning from the security cost of PPG-style executable Workshop mods.

The recommended hierarchy is:

```text
MOST USERS / WORKSHOP
  Blueprints
  Presets
  Safe content packs
  Declarative behavior graphs
          |
          | project-owned typed capabilities
          v
  Desktop Buddy semantic runtime
          |
          v
  Godot / .NET internals

ADVANCED CODE MODS
  separate future trust boundary
  never implicitly granted by Workshop subscription
```

The core engineering principle is that **moddability is achieved by making the game systems generic and composable, not by making the host process unrestricted**.
