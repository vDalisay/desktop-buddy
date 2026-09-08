# Desktop Buddy — Full Release Systemic Sandbox Implementation Plan

Status: **Owner-approved product direction; implementation plan**  
Planning branch: `plan/full-release-systemic-sandbox`  
Base when recorded: `main` at `3966648b7f9fb8c6a70f4cf6f8e586b9998a2641`  
Scope: **Full Steam game only**

---

## 0. Hard product decision: full-game only

Every system in this document is a **Full Release feature**. None of it is to be added to the Steam Demo or itch.io build unless the owner explicitly reverses that decision later.

This includes all new:

- object/property editing;
- construction parts;
- structure-building tools;
- new joint types;
- interactive physics furniture;
- devices, signals, logic and automation;
- room-physics controls;
- systemic materials/destruction additions;
- persistent structural Buddy damage;
- detachable/re-attachable Buddy parts;
- structural repair tools;
- scene/posing tools;
- local contraption/blueprint saving;
- Workshop contraption sharing;
- shareable customized-item/weapon presets;
- systemic-sandbox secrets/Easter eggs built on these systems.

Existing Demo behavior is not retroactively removed by this plan. In particular, existing systems that already ship in the Steam Demo remain governed by their existing scope rules. This document only says that **new expansion work described here is Full Release-only**.

The implementation must use the existing fail-closed distribution pattern in `DemoScope`: the public Demo is the default, while Full Release content requires the `full_release` feature. A build that forgets the tag must therefore omit this entire feature family rather than leak it into the Demo.

Add explicit scope seams rather than scattering `IsFullRelease` checks through unrelated code. At minimum:

```text
DemoScope.IncludesSystemicSandbox
DemoScope.IncludesConstruction
DemoScope.IncludesSandboxProperties
DemoScope.IncludesAutomation
DemoScope.IncludesStructuralDamage
DemoScope.IncludesContraptionWorkshop
DemoScope.IncludesSceneTools
```

These may initially all resolve to `IsFullRelease`; separate names keep later policy changes local and make scope tests readable.

Composition roots, menus, catalogue visibility, input routing, persistence loading, Workshop discovery and Workshop import must all check the relevant scope. Hiding only the UI is insufficient.

---

# 1. Product objective

Desktop Buddy should keep its existing identity — one persistent expressive Buddy, progression, customization, Work Mode, painting, the Win98 shell and desktop-companion behavior — while gaining the systemic sandbox depth that makes games such as *Mutilate-a-Doll 2* and *People Playground* replayable for hundreds of hours.

The goal is **not** to compete on raw item count or copy either game's exact mechanics. The goal is to give players a small set of reusable primitives whose combinations create many outcomes.

The target loop becomes:

```text
spawn / customize an object
        ↓
change its physical or functional properties
        ↓
connect it to other objects with joints or signals
        ↓
build a contraption / room setup / scene
        ↓
let the simulation run
        ↓
observe damage, failure, reactions and unexpected interactions
        ↓
repair / modify / rebuild
        ↓
save or share the result
```

The Win98 presentation should make the system feel native to Desktop Buddy rather than like a transplanted construction editor. The central metaphor is ordinary late-1990s desktop UI applied to absurd physics:

- right-click -> `Properties`;
- `Control Panel -> Room Physics`;
- Explorer-like object/blueprint browser;
- Win98 context menus;
- status/progress dialogs for imports;
- small device property windows;
- familiar Save / Save As / Duplicate / Delete flows.

---

# 2. Design pillars

## 2.1 Systems over isolated content

New content should preferentially add a reusable interaction channel rather than one bespoke animation.

A Button is valuable because it can trigger a Lamp, Piston, Gun, Door, Motor or Speaker. A Hinge is valuable because it can create a door, trap, arm, pendulum or vehicle. A material system is valuable because the same fire, impact and electrical rules can affect many objects.

## 2.2 MaD2-style editability, Desktop Buddy constraints

The player should be able to inspect and change bounded properties of supported objects and weapons. This is declarative customization, not arbitrary scripting.

The project owns:

- which properties exist;
- their types;
- their valid ranges;
- which objects expose them;
- how they affect simulation;
- which values are shareable;
- which values affect rewards.

No property UI may directly expose an arbitrary Godot property name, Resource path, script path, scene path or node path from user data.

## 2.3 Construction should invite experimentation

Building must not feel like the existing decoration purchase loop. Requiring another payment every time a plank is duplicated would discourage the exact trial-and-error behavior this feature is intended to create.

**Default implementation assumption:** construction/device definitions use permanent ownership/unlock semantics. Once a part is owned, the player may spawn unlimited copies subject to runtime performance caps. This is intentionally different from duplicate-paid visual room decorations.

Economy tuning can decide whether the initial basic kit is free in Full Release and advanced parts are purchased, or whether all parts are permanent purchases. The runtime architecture must support both without per-instance financial state.

## 2.4 Persistent Buddy consequences, never a soft-lock

Structural damage should matter because the Buddy is persistent, but destructive experimentation must always be reversible.

The player must always have a free recovery route, tentatively presented as:

```text
Control Panel -> Buddy -> System Restore...
```

This restores structural integrity and missing default body parts without resetting money, unlocks, character cosmetics, paint, room state, achievements or statistics.

## 2.5 Safe UGC before unrestricted mods

Contraptions and customized item presets should expand the current safe data-only Workshop approach.

Workshop content in this program is limited to whitelisted first-party definitions, transforms, bounded property overrides, joints, wires and device configuration. No user package may contain executable code.

An unrestricted scripting/mod loader remains outside this program.

## 2.6 Existing physics authority stays intact

The authoritative simulation remains the existing 2D physics world and routed fixed tick. The current 3D presentation remains a view of that state.

Do not migrate construction or damage to independent 3D physics. Do not add per-object `_PhysicsProcess` callbacks just because there are more objects. New systems register with the existing central simulation routing and operate in deterministic batches.

---

# 3. Existing foundations to reuse

The current codebase already supplies several useful foundations.

### 3.1 Loose physics objects

`LooseObjectBody`, `LooseObjectProfile` and `LooseObjectRegistry` already cover authored physics data, impact attribution, grabbing, holding, rest tracking and object presentation.

However, the current loose-object admission policy has an authoritative cap of 24. That is appropriate for balls, food, grenades and dropped tools, but not for structures containing dozens of parts.

**Decision:** do not simply increase the existing cap. Preserve the current ephemeral loose-object budget and introduce a separate persistent sandbox/construction registry with its own benchmarked budgets.

### 3.2 Rope Suspender

`RopeSuspensionComponent` is already a useful first construction primitive: it holds a grabbed rigid body to an anchor through a bounded damped elastic force and participates in the routed fixed tick.

It should be generalized into the new joint framework rather than deleted and reimplemented from scratch.

### 3.3 Existing damage/gore pipeline

Current impact attribution, pain, knockouts, Burning, Gore Mode, wounds and Sword impalement provide the event stream that structural damage can extend.

Structural integrity must be a separate layer from pain/mood. A painful hit does not automatically mean a broken limb, and structural repair must not silently erase mood/fear history unless an existing rule already does so.

### 3.4 Environment customization

Room Decorator already establishes full-release-only UI composition and persistent room placement concepts. Existing decoration data remains visual/non-physical by default.

Functional furniture and sandbox construction should build beside this system, not convert every existing decoration into a `RigidBody2D`.

### 3.5 Workshop safety model

Workshop v1 already has staging, validation, preview capture, an emulator, import/export coordination and Steam discovery abstractions. Contraptions should add a new validated package type through those same boundaries.

### 3.6 Versioned persistence

`ProgressSave`, atomic stores and environment progress already provide migration patterns. Large contraption graphs should **not** be dumped wholesale into `progress.json`, because they may contain hundreds of instance records and will change more often than progression.

---

# 4. High-level architecture

Introduce five layers.

```text
AUTHORED DEFINITIONS
  SandboxEntityDefinition / PropertyDescriptor / DeviceDefinition
                         ↓
INSTANCE DOMAIN STATE
  SandboxEntityState / JointState / WireState / StructuralBuddyState
                         ↓
RUNTIME 2D SIMULATION
  SandboxEntityBody2D / JointRuntime / DeviceRuntime / MaterialRuntime
                         ↓
PRESENTATION + EDITING
  3D mirrors / selection / context menu / Properties / Toolbox
                         ↓
PERSISTENCE + SHARING
  Room sandbox files / Blueprints / Workshop validated packages
```

No presentation layer becomes authoritative.

---

# 5. Core sandbox entity model

## 5.1 New stable identity types

Add engine-free domain identities similar to the existing environment IDs:

```text
SandboxDefinitionId
SandboxInstanceId (Guid-backed)
SandboxMaterialId
SandboxJointId
SandboxWireId
SandboxPortId
SandboxBlueprintId
ToolVariantId
RoomProfileId reference
```

Definitions use stable string IDs; runtime/persistent instances use GUID identities.

## 5.2 Sandbox entity kinds

Initial enum/domain classification:

```text
ConstructionPart
Device
FunctionalProp
WorldTool
PhysicsDecoration
DetachedBuddyPart   // local runtime/state only; never Workshop content
```

Keep this classification semantic. Do not infer behavior from file paths or node class names.

## 5.3 Authored definition

A first-party `SandboxEntityDefinition` should declare:

- stable content ID;
- display/localization key;
- category;
- allowed build scope;
- authored collision shape descriptor;
- base mass;
- friction;
- bounce/restitution;
- linear/angular damping;
- gravity scale;
- material ID;
- durability/structural settings;
- flammability/heat settings if supported;
- default frozen/static state;
- presentation kind / trusted mesh-builder identifier;
- selection bounds;
- allowed player-editable properties;
- allowed joints;
- optional trusted device capability ID;
- optional trusted furniture capability ID;
- optional economy ownership policy;
- optional Workshop permission.

No user save is allowed to introduce an unknown capability ID.

## 5.4 Instance state

Persistent instance state contains only semantic data:

```text
InstanceId
DefinitionId
RoomId
Position
Rotation
Scale          // only if definition explicitly permits bounded scale
Frozen
PropertyOverrides
DamageState
```

Never serialize live Godot node references, RIDs, velocities as permanent authored room state, or arbitrary Resources.

## 5.5 Separate construction registry

Add `SandboxEntityRegistry` rather than making `LooseObjectRegistry` own everything.

Responsibilities:

- instance lookup;
- spawn/despawn;
- protected state;
- current edit selection hooks;
- deterministic routed fixed-tick batching;
- sleep/wake bookkeeping;
- joint participation;
- save dirty-state notifications;
- safe cleanup on room switch;
- count/budget enforcement.

The existing 24 loose-object cap stays unchanged for its current content.

### Provisional benchmark targets

Do not lock these as product guarantees before profiling, but use them as engineering targets:

- 128 sandbox entity instances in a room;
- 96 simultaneously dynamic/awake bodies;
- 128 joints;
- 192 signal wires;
- 64 devices;
- existing 24-slot loose-object budget remains independent.

If 120 Hz cannot sustain those targets on the launch minimum PC, reduce the user-facing caps based on measured envelopes rather than hiding frame drops.

---

# 6. Sandbox edit mode and QoL foundation

This comes before advanced devices because every later feature needs usable selection/editing.

## 6.1 Mode

Add a Full Release-only `Sandbox`/`Build` workspace accessible from the Win98 shell.

Suggested top-level actions:

```text
Sandbox
  Toolbox...
  Blueprints...
  Pause Simulation
  Room Physics...
  Scene Tools...
```

The exact menu name can be polished later; avoid adding a second unrelated application shell.

## 6.2 Selection

Implement one shared selection service for sandbox entities:

- single click;
- Shift-click additive/toggle selection;
- drag rectangle/marquee;
- click-away clear;
- selection outline;
- selected-count status text;
- keyboard focus rules that do not steal typing from normal UI.

Buddy body parts may be selectable only by tools that explicitly support Buddy targeting. Ordinary construction multi-select must not accidentally include the Buddy.

## 6.3 Core context menu

Right-click a supported object:

```text
Properties...
Freeze / Unfreeze
Duplicate
Copy
Delete
Bring Forward / Send Back     // visual/static items only where meaningful
Save Selection as Blueprint...
Reset Properties
```

Multi-selection exposes only operations valid for the entire selection.

## 6.4 Transform editing

Provide:

- drag to move while simulation is paused or object is frozen;
- rotate handle or explicit rotate command;
- optional grid snap;
- duplicate at small deterministic offset;
- copy/paste preserving relative transforms;
- delete;
- undo/redo for edit commands.

Undo/redo covers deliberate editor actions, not time reversal of the physics simulation.

## 6.5 Simulation controls

At minimum:

```text
Pause / Play
0.25x
0.5x
1x
2x
```

These must route through simulation timing rather than globally slowing UI windows, mouse input, Steam callbacks or menu animations.

---

# 7. Win98 Properties system

This is one of the main MaD2-inspired systems and should be treated as a first-class framework, not custom UI per object.

## 7.1 Property descriptor registry

Each editable property is registered with a stable ID and descriptor:

```text
PropertyId
DisplayNameKey
Category/tab
ValueType
Minimum
Maximum
Step
Unit
DefaultSource
ReadOnly predicate
Shareable flag
Economy-sensitive flag
Runtime apply strategy
```

Possible value types:

- bool;
- bounded integer;
- bounded float;
- enum from a trusted list;
- color;
- first-party definition reference from a trusted list.

## 7.2 Common physics properties

Initial common property set:

- Mass;
- Bounce;
- Friction;
- Linear damping;
- Angular damping;
- Gravity scale;
- Frozen;
- Collision enabled where safe;
- Impact force multiplier where safe;
- Durability;
- Break threshold;
- Flammable;
- Heat resistance;
- Conductivity, once electricity exists.

Property ranges must be bounded to keep the solver stable. `NaN`, infinity and enormous values are rejected by domain validation before reaching Godot.

## 7.3 Weapon/tool properties

Expose only properties that map cleanly to existing authored tuning surfaces.

Examples:

### Pistol

- projectile speed;
- firing cadence;
- recoil;
- impact/impulse scale within a bounded range.

### Shotgun

- pellet count within a safe cap;
- spread;
- pellet speed;
- recoil;
- reload timing.

### Grenade

- fuse;
- blast radius;
- blast force;
- throw/launch tuning where applicable.

### Fire Sprayer

- range;
- emission rate within particle/performance caps;
- ignition strength/duration where the existing fire system supports it.

### Baseball / Soccer Ball

- mass;
- bounce;
- pullback launch strength.

### Bat / Sword

- mass/handling modifier where safe;
- impulse multiplier;
- piercing/breaking thresholds where supported.

### Rope Suspender / future rope joint

- stiffness;
- damping;
- maximum force;
- rope length once generalized.

## 7.4 Canonical definition vs user override

Never mutate catalogue Resources at runtime.

Resolution order:

```text
canonical authored definition
        +
validated local user override map
        =
resolved runtime tuning
```

`Reset to Default` simply removes overrides.

## 7.5 Tool variants

Allow the player to save a customized tool configuration as a named local variant:

```text
Pistol
  Default
  Hand Cannon
  Peashooter
  My Variant 4
```

A variant contains only:

- base tool ID;
- supported property overrides;
- player name;
- schema version;
- optional local preview metadata.

Later Workshop sharing can use the same validated format.

## 7.6 Economy exploit boundary

Editable physics must never become an infinite-money slider.

Add an explicit `SandboxRewardPolicy`.

Recommended rule:

- sandbox construction/device impacts either award no direct damage credits or use an authored capped generic reward;
- customized weapon variants may never produce a higher economy payout envelope than the canonical base weapon;
- increased projectile force/pellet count can change physical outcome, but reward calculation uses canonical per-use/per-time ceilings;
- imported Workshop variants obey the same rule;
- achievements that depend on ordinary weapon use decide explicitly whether modified variants qualify.

No reward logic should infer that a more extreme user property value deserves more money.

---

# 8. Construction system

## 8.1 Initial part library

Launch with a compact but combinatorial set rather than dozens of decorative shapes.

Suggested initial set:

- short wooden plank;
- long wooden beam;
- short metal bar;
- long metal beam;
- metal plate;
- wooden crate/block;
- rubber block;
- wheel;
- axle/hub part;
- glass pane;
- platform;
- spike/hazard plate;
- counterweight/heavy block.

Each part gets a clear collision shape, selection bounds, 2D visual and 3D presentation counterpart.

## 8.2 Joint framework

Generalize Rope Suspender concepts into a shared `SandboxJointService` with domain/persistence records and runtime implementations.

Required initial joint tools:

### Pin

Pins one local point of a body to a world anchor.

### Weld

Locks two objects together at their current relative transform.

### Hinge

Connects two bodies around one pivot and allows rotation.

### Spring

Elastic link with user-editable rest length, stiffness and damping.

### Rope

Connects body-body or body-world with maximum length / elastic tuning. Existing Rope Suspender behavior migrates onto this infrastructure without changing the existing tool's user-facing basic behavior.

Potential later joints:

- slider;
- motorized hinge;
- pulley abstraction;
- breakaway joint;
- gear linkage.

Do not add them until the first five are stable and shareable.

## 8.3 Joint creation UX

Use a predictable two-click workflow:

```text
select joint tool
click first target / anchor
click second target / world
preview
commit
```

Show attachment points while the tool is active. Esc/right-click cancels an incomplete joint.

Right-clicking a joint line exposes:

```text
Properties...
Delete
Disable
```

## 8.4 Breakable joints

Every joint may optionally expose a bounded break-force setting.

The solver/runtime detects sustained/peak force and publishes a semantic `JointBroken` event. Breaking a joint is not equivalent to deleting its connected bodies.

## 8.5 Construction persistence

Persist:

- instances;
- committed transforms;
- property overrides;
- joints;
- wire graph;
- frozen state;
- supported damage state.

Do not persist transient solver impulses, current contact manifolds, temporary projectile bodies or arbitrary in-flight velocity as permanent authored setup state.

A room reload should recreate the setup deterministically and let physics continue from there.

---

# 9. Material, heat and destruction layer

This system provides MaD2-like systemic interactions without attempting unrestricted polygon cutting in the first release.

## 9.1 Authored materials

Introduce a small material library:

- Wood;
- Metal;
- Rubber;
- Glass;
- Plastic;
- Fabric/soft material only if needed by future props.

Each material supplies bounded defaults for:

- density contribution;
- friction;
- restitution;
- structural toughness;
- heat tolerance;
- flammability;
- conductivity;
- cosmetic impact/break SFX category.

Individual definitions may override material defaults within authored constraints.

## 9.2 Heat

Extend the existing fire system toward a simple thermal model rather than a high-fidelity fluid simulation.

Per supported entity track:

```text
Temperature
Ignition threshold
Burning state
Heat resistance
Cooling rate
```

Heat sources:

- Fire Sprayer;
- burning neighboring objects;
- Heater device;
- explosions;
- later electrical overload if implemented.

Initial heat transfer can be proximity/contact based and fixed-tick batched. Do not simulate per-pixel temperature fields.

## 9.3 Destruction

Add structural durability to supported construction objects.

Failure outcomes are definition-authored:

- remain intact but damaged;
- break joint only;
- split into predefined fragments;
- shatter into bounded cosmetic fragments;
- despawn after destruction.

Examples:

- glass pane -> shatter;
- wooden plank -> split into two authored shorter segments;
- metal beam -> bend/damage visual first, break only at high force if approved;
- device -> disabled/broken state.

## 9.4 Cutting scope

Do **not** make arbitrary dynamic polygon cutting a prerequisite. It is expensive, hard to validate for Workshop, and conflicts with the current 3D presentation pipeline.

First release uses authored break points / segment replacement. True freeform cutting remains a later research item.

---

# 10. Devices, signals and automation

This is the second major systemic pillar after construction.

## 10.1 Signal graph rather than arbitrary scripts

Implement a project-owned signal network.

A device exposes authored ports:

```text
InputPort
OutputPort
PortValueKind
```

Initial value kinds:

- `Pulse`;
- `Bool`;
- bounded scalar `0..1`.

A `WireState` connects one output to one or more compatible inputs.

## 10.2 Deterministic propagation

Signals are processed through a central queue on the routed fixed tick.

Required protections:

- no recursive call chain between devices;
- deterministic ordering by stable instance/port identity;
- maximum events/hops per tick;
- cycle detection/deferral;
- malformed graph rejection during load/import;
- sleeping physics objects do not imply sleeping logic if a device remains powered/active.

An intentional feedback loop can operate across ticks; it may not freeze the frame in one recursive propagation pass.

## 10.3 Initial input devices

- Push Button;
- Toggle Switch;
- Pressure Plate;
- Proximity Sensor;
- Timer;
- optional Counter once core logic is stable.

## 10.4 Logic devices

- NOT;
- AND;
- OR;
- XOR;
- Delay;
- basic latch/memory component after the first gates are stable.

These are important because they let players build recognizable logic systems without user scripting.

## 10.5 Output/actuator devices

- Lamp;
- Buzzer/Speaker;
- Piston;
- Motor / motorized hinge controller;
- Fan;
- Electromagnet;
- Heater;
- Conveyor/platform motor;
- Door actuator;
- Trigger Adapter.

`Trigger Adapter` is the safe bridge into existing tools/weapons. It can activate only capabilities explicitly exposed by first-party definitions.

Examples:

```text
Pressure Plate -> Trigger Adapter -> Shotgun
Timer -> Piston
Proximity Sensor -> AND -> Lamp
Button -> Motor
Toggle -> Heater
```

## 10.6 Power model

Do not begin with a full electrical engineering simulator.

First implementation may model `Powered` as a normal trusted signal/capability requirement. A Battery/Power Supply device can produce a persistent power output for thematic readability.

Conductivity/electrical damage can be added after the logic graph is proven. Keep signal logic and physical electricity conceptually separate so a simple button does not require a Kirchhoff-law solver.

## 10.7 Wiring UX

Wire tool:

1. hover shows compatible ports;
2. click output;
3. compatible inputs highlight;
4. click input;
5. line appears;
6. right-click line -> Properties/Delete.

Invalid type connection is rejected before mutation.

Wire visuals are presentation only; they need not be physical ropes unless a later feature explicitly requests physical cables.

---

# 11. Control Panel -> Room Physics

Add a Full Release-only Win98 Control Panel applet for broad sandbox experimentation.

Initial controls:

- simulation speed;
- gravity multiplier;
- horizontal wind;
- ambient temperature once thermal simulation exists;
- global object damage multiplier within bounded range;
- optional fire-spread intensity;
- show/hide signal wires;
- show/hide joint guides;
- reset room physics to defaults.

Potential later controls:

- low/zero gravity preset;
- high gravity preset;
- low-friction room preset;
- heat/cold presets.

Buddy active-drive stability must be verified at every exposed gravity/time-scale range. If an extreme value makes the standing controller invalid, either clamp the range or intentionally transition the Buddy to a passive/floating behavior rather than allowing unstable recovery loops.

Room-physics configuration belongs to room semantic state, not machine-local graphics settings.

---

# 12. Buddy structural damage

## 12.1 Separate structural state from pain

Introduce a `BuddyStructuralState` with per-body-part integrity.

Suggested first model:

```text
Head
Torso
LeftArm
RightArm
LeftLeg
RightLeg
```

Per part:

```text
Integrity 0..100
DamageFlags
Attached / Detached
Socket state
Optional burn/electrical structural state
```

Pain, knockout, mood and fear stay in their existing models.

## 12.2 Damage channels

Extend authoritative impact/tool events with semantic damage kinds where necessary:

- Blunt;
- Piercing;
- Explosive;
- Burn;
- Electrical later;
- Cutting later if approved.

An event may affect pain and structure differently.

Examples:

- small Nerf hit: almost no structure damage;
- repeated bat impacts: blunt integrity loss;
- Sword: localized piercing/structural damage;
- explosion: distributed integrity loss;
- prolonged fire: heat structural loss.

## 12.3 Detachment

First release detachment should support arms and legs. Head detachment is a separate product/content decision and is not required by this plan's first structural-damage milestone.

When a limb crosses the detachment rule:

1. disable active-drive connection for that socket;
2. remove/disable the appropriate internal rig constraint safely;
3. retain the limb body as a normal physical body;
4. mark it as `DetachedBuddyPart`;
5. update behavior so standing/walking does not fight a missing limb;
6. trigger non-gore mechanical feedback;
7. if Gore Mode is enabled, feed the existing gore/wound presentation through the approved content gate.

Detachment mechanics are not themselves dependent on Gore Mode. Gore Mode controls graphic blood presentation, consistent with current Sword behavior where the physical impalement is separate from the blood layer.

## 12.4 Non-gore feedback

When Gore Mode is off, structural failure should still read clearly through stylized non-graphic effects such as:

- sparks;
- exposed connector/wire art;
- crack/damage decals;
- metal/plastic impact sounds;
- limp/missing-part behavior.

## 12.5 Repair

Do not overload the current consumable Repair Kit into an incompatible cursor tool without a migration plan. Add a dedicated Full Release structural repair interaction, tentatively one of:

- Repair Wrench;
- Soldering Iron;
- Repair Tool.

Required capabilities:

- restore integrity gradually;
- extinguish/clear only structural statuses it explicitly owns;
- reattach a detached default limb when held/aligned near its socket;
- clear crack/broken visuals as thresholds recover;
- produce progress feedback;
- never duplicate a limb.

The existing Repair Kit may remain a consumable care object and can optionally synergize later.

## 12.6 Reattachment

Reattachment transaction:

```text
correct detached part
+ matching empty socket
+ within snap distance/orientation tolerance
+ repair action held long enough
= atomic reattach
```

On commit:

- reparent/register correctly;
- restore rig constraint;
- remove loose-object/sandbox detached-part registration;
- wake the Buddy safely;
- restore active drive gradually rather than applying a single explosive corrective force.

## 12.7 Replacement/prosthetic expansion

After ordinary reattachment is stable, add trusted replacement-part definitions.

Possible later extensions:

- standard replacement arm/leg;
- heavy metal limb;
- spring limb;
- cosmetic prosthetic variants;
- whitelisted socket adapters.

Do not initially allow any arbitrary sandbox object to become an active-drive limb. That requires a much larger mass/shape/animation safety model.

## 12.8 System Restore

Always-available recovery flow:

```text
Control Panel -> Buddy -> System Restore...
```

Preview exactly what will change. Confirm restores:

- default part topology;
- structural integrity;
- stuck structural statuses.

Preserve:

- paint;
- Buddy Studio configuration;
- name/character slot;
- money/unlocks;
- mood unless specifically approved otherwise;
- room;
- Workshop subscriptions;
- achievements/statistics.

---

# 13. Functional furniture and physics props

Do not make every existing decoration physical.

Extend selected Full Release definitions with a trusted capability field:

```text
FurnitureCapabilityId
PhysicsCapabilityId
```

Examples:

- Chair/Sofa: sit/rest target;
- TV/Computer: watch/use idle target;
- Lamp: toggle/device capability;
- Fan: actuator/wind capability;
- Door: hinge + actuator;
- Shelf/Table: authored collider/static support;
- Bed: rest/sleep target.

Existing Demo decoration behavior remains non-physical and unchanged.

Full Release may optionally expose `Make Physical` / `Freeze in Place` for definitions that have an approved physical representation. Unsupported visual decorations do not receive generic collision automatically.

Furniture deletion/movement while the Buddy is using it must cancel the behavior and enter a deterministic safe recovery path.

---

# 14. Scene and posing tools

This is a secondary system, but it shares selection/freeze infrastructure and is useful to players who use ragdolls for scenes rather than only destruction.

Initial Scene Tools:

- Freeze selected object;
- Freeze selected Buddy part pose temporarily;
- Pose/drag limb while simulation paused;
- Look At Cursor / Look At Point;
- Sit / Stand / Lie command where valid;
- expression/mood presentation override for screenshot mode without mutating durable mood;
- hide selection outlines/UI for screenshot;
- restore live behavior.

Possible later:

- copy/paste pose;
- named pose presets;
- camera framing presets;
- scene save package separate from contraptions.

Do not let screenshot pose overrides overwrite durable personality/mood state.

---

# 15. Local blueprint / contraption system

## 15.1 What a blueprint contains

A blueprint is a selected subgraph of sandbox state stored relative to an origin:

```text
SchemaVersion
BlueprintId
Name
Description
Entity records
Relative transforms
Property overrides
Joint records
Wire records
Device settings
Required definition IDs
Optional preview metadata
```

Not included:

- Buddy identity;
- Buddy paint/cosmetics;
- Buddy structural state;
- detached Buddy parts;
- money;
- achievements;
- room background;
- arbitrary files;
- scripts/scenes/shaders.

## 15.2 Saving

`Save Selection as Blueprint...` validates the graph before writing.

If the selection references an unselected external object, the UI must either:

- omit that external connection and say so; or
- offer `Include connected objects`.

Never serialize a dangling raw node reference.

## 15.3 Loading

Loading previews a ghost placement. Commit allocates new instance/joint/wire GUIDs atomically.

If required first-party definitions are unavailable in this build, the blueprint is rejected or imported in a clearly degraded non-destructive mode only if the format explicitly supports placeholders.

Full Release-only definitions never downgrade into the Demo because the Demo cannot load this package type at all.

## 15.4 Storage

Keep player blueprints outside `progress.json`, e.g.:

```text
user://blueprints/<blueprint-guid>/manifest.json
user://blueprints/<blueprint-guid>/preview.png
```

Use atomic write/replace behavior and bounded file sizes.

Whether local blueprints participate in Steam Cloud should be configured deliberately rather than assumed from progress sync.

---

# 16. Persistent room sandbox state

Contraption graphs can become large and high-churn. Store them in a dedicated versioned room-sandbox document instead of expanding `ProgressSave` indefinitely.

Suggested layout after room profiles exist:

```text
user://rooms/<room-id>/sandbox.json
```

`ProgressSave` only needs enough semantic state to identify active room/profile and relevant unlock/index metadata.

If systemic sandbox implementation begins before multiple-room profiles are finished, still introduce a stable default `RoomProfileId` so the format does not need to be redesigned later.

### Persistence rules

Persist topology/configuration, not arbitrary frame state.

Dirty the save on:

- spawn/delete;
- deliberate transform edit;
- freeze/unfreeze;
- property commit;
- joint add/remove/edit;
- wire add/remove/edit;
- device configuration change;
- supported destruction state transition;
- part/device settling if the room is designed to remember physical relocation.

Use debounce/transaction semantics; never save 120 times per second because a body is moving.

Transient states such as one bullet, a current spark particle, active contact manifold or signal event queue are not durable.

---

# 17. Workshop contraptions

## 17.1 New content type

Extend Workshop with a new validated Full Release-only package type:

```text
content_type = contraption
required_scope = full_release
schema_version = 1
```

Use Steam tags/metadata as a discovery optimization, but never trust tags as the security boundary. Local package validation and `DemoScope` remain authoritative.

## 17.2 Demo isolation

Because Steam Demo/full-game Workshop discovery may share infrastructure, implement defense in depth:

1. Demo browse queries do not request `contraption` items.
2. Browse result filtering hides anything marked Full Release-only.
3. Preview/install actions refuse Full Release-only content in Demo scope.
4. Import coordinator checks package `required_scope`.
5. Package validator rejects the contraption schema entirely unless `IncludesContraptionWorkshop`.
6. Automated scope tests attempt direct import with UI bypassed.

A Demo user manually placing a Full Release contraption package in an install/cache folder must still be unable to instantiate it.

## 17.3 Package safety

Contraption package validator enforces:

- supported schema;
- max entity count;
- max joint count;
- max wire count;
- max device count;
- max JSON/file size;
- finite numbers only;
- transform bounds;
- known first-party definition IDs;
- property allowlists/ranges;
- known port IDs;
- compatible signal types;
- valid joint targets;
- no duplicate GUIDs within package;
- no cycles that violate graph policy;
- no paths outside package staging;
- no external URLs/resources;
- no scripts/scenes/shaders/native binaries;
- no Buddy/detached-part entity types;
- no economy/progression records.

## 17.4 Import transaction

Import to a local blueprint first. Do not mutate the live room directly from Steam's mutable install folder.

Flow:

```text
Steam install folder
 -> staging snapshot
 -> package validation
 -> local blueprint identity allocation
 -> atomic local blueprint write
 -> user chooses Place
 -> transactional room insertion
```

This mirrors the existing Workshop staging philosophy.

## 17.5 Preview

Reuse trusted local preview capture after validation. Never render arbitrary imported code/content.

---

# 18. Shareable customized item / weapon presets

Once the Properties framework is mature, add a second small Workshop-safe format for tool/item variants.

Suggested type:

```text
content_type = item_variant
required_scope = full_release
```

Payload:

- base first-party content ID;
- player display name;
- bounded property overrides;
- schema version.

No custom behavior code.

Contraption packages may embed/reference compatible validated property overrides so a creator's tuned piston, spring or firearm behaves the same after import.

If a referenced local variant is missing, the contraption package must contain the validated override values it needs rather than a machine-local path.

---

# 19. Progression and ownership

The systemic sandbox should coexist with Desktop Buddy's progression without making building tedious.

Recommended ownership model:

- basic Build tools become available as a Full Release feature/tutorial unlock;
- construction part/device definitions can be one-time purchases or progression unlocks;
- once owned, spawning copies is free;
- deleting/destroying a spawned part does not destroy ownership;
- Workshop import never grants permanent ownership of a definition the player has not legitimately unlocked unless explicitly approved;
- when placing a blueprint containing locked content, show missing/locked requirements rather than silently granting them.

A future creative/free-build mode could waive ownership restrictions, but that is not required for initial implementation.

---

# 20. Secrets and Easter eggs

Do not build a bespoke lore engine before the sandbox works.

Instead, expose a small internal semantic event bus that content can subscribe to through project-owned predicates. Full Release secrets can then react to unusual combinations such as:

- a specific signal sequence;
- an object reaching an extreme safe temperature;
- building a recognizable configuration;
- a certain device operating at a certain time;
- a hidden Control Panel interaction;
- repeated interaction with a fake file/system dialog.

Presentation opportunities:

- strange `.txt` files in an in-game fake filesystem;
- corrupted-looking Buddy thumbnails;
- hidden Control Panel applets;
- fake Win98 errors;
- CRT messages;
- obscure achievements;
- Buddy reactions to apparently off-screen events.

Secrets remain authored content using trusted game events. Workshop data cannot define secret predicates or execute arbitrary actions.

---

# 21. Performance architecture

This program can easily multiply active entities, so performance rules are part of the design rather than a late polish item.

## 21.1 Routed fixed tick only

New registries participate in one central deterministic update sequence. Avoid one `_PhysicsProcess` per device/joint/object.

Suggested ordering:

```text
input/edit commands
signal queue delivery
trusted device intent updates
joint/control forces
Buddy active-drive forces
physics engine step/contact outcomes
impact/damage attribution
structural transitions
thermal/fire updates
rest/sleep bookkeeping
save-dirty/event publication
presentation reads afterward
```

Exact placement must respect existing root ordering; this list is conceptual.

## 21.2 Sleeping and dirty sets

- sleeping bodies are skipped by custom per-tick calculations where possible;
- thermal updates use active/hot sets;
- signal devices are event-driven, not all polled every tick;
- joint service iterates dense active joint storage;
- deleted IDs are pruned without LINQ allocations in hot loops;
- 3D presentation reuses/pools visual slots.

## 21.3 Benchmark scenes

Add dedicated stress scenarios:

```text
sandbox_128_static
sandbox_96_dynamic
sandbox_joint_stress
sandbox_wire_stress
sandbox_fire_stress
sandbox_mixed_stress
```

Measure fixed-tick stability, allocations and runaway solver forces.

---

# 22. Save/migration rules

## 22.1 Demo -> Full Release

A Steam Demo save upgraded into Full Release starts with:

- its existing progress intact;
- no sandbox room document -> clean default sandbox state;
- no structural damage -> full integrity;
- no local blueprints unless created by a Full Release build;
- new Full Release unlock/tutorial state initialized safely.

No Demo migration should require contraption data.

## 22.2 Full Release -> Demo launch safety

If a player later launches the Demo executable against data from the Full Release, the Demo must ignore Full Release sandbox documents rather than deleting or rewriting them.

Do not let Demo reset/save routines erase unknown Full Release files merely because they are out of scope.

## 22.3 Versioning

Every new persistent document has its own schema version and migration policy:

- sandbox room schema;
- blueprint schema;
- tool variant schema;
- Workshop package schema;
- Buddy structural-state schema if not embedded in an existing compatible character/progress document.

Use the next available `ProgressSave` schema only for small index/ownership fields that genuinely belong in semantic progress.

---

# 23. Testing strategy

Each milestone requires engine-free tests where possible, headless scenarios for Godot state and real-input journeys for user workflows.

## 23.1 Scope tests

Required matrix:

```text
Demo default
Full Release override
itch.io override
editor/development
```

Assertions include:

- menus absent in Demo/itch;
- content IDs absent from Demo catalogue/browser;
- direct API calls cannot spawn Full Release sandbox content in Demo;
- Demo cannot load local blueprint/room sandbox documents;
- Demo cannot discover/import contraption Workshop type;
- Full Release can;
- itch remains strictest where its existing policy requires it.

## 23.2 Property tests

- every property has finite bounds;
- invalid type rejected;
- out-of-range imported value rejected or normalized according to explicit policy;
- reset returns canonical tuning;
- save/load round-trip stable;
- modified weapon cannot exceed reward policy.

## 23.3 Joint scenarios

- Pin holds expected point;
- Weld preserves transform;
- Hinge rotates without separation;
- Spring converges without runaway force;
- Rope obeys length/force bounds;
- joint delete cleans state;
- body delete cleans dependent joints;
- room load rebuilds graph;
- window resize does not produce NaN/explosive correction.

## 23.4 Device scenarios

- compatible wiring;
- invalid type rejection;
- button pulse;
- toggle persistence;
- timer deterministic timing;
- AND/OR/NOT/XOR truth tables;
- actuator response;
- feedback loop does not recurse forever;
- event budget protects frame;
- deleted device removes/invalidates wires safely.

## 23.5 Structural Buddy scenarios

- integrity loss by damage type;
- ordinary pain still functions;
- limb detaches exactly once;
- active drive stops commanding detached limb;
- remaining Buddy never enters infinite recovery loop;
- detached limb remains grabbable/physical;
- Gore off -> no blood;
- Gore on -> approved blood response;
- repair restores integrity;
- reattach transaction cannot duplicate part;
- save/restart restores exact structural topology;
- System Restore recovers a deliberately pathological state.

## 23.6 Blueprint/Workshop scenarios

- local save/load round-trip;
- relative transforms preserved;
- duplicate IDs remapped;
- missing definition handled;
- malicious path rejected;
- unknown capability rejected;
- NaN/infinity rejected;
- oversize counts rejected;
- Demo import rejected;
- Workshop emulator publish/install/import/place round-trip;
- unsubscribe/missing item does not damage an already imported local blueprint.

## 23.7 Real-input journeys

At minimum:

```text
sandbox_build_first_contraption
sandbox_edit_properties
sandbox_wire_button_to_piston
sandbox_damage_detach_repair
sandbox_save_place_blueprint
sandbox_workshop_roundtrip
sandbox_demo_scope_denial
```

---

# 24. Implementation sequence

The order is designed so each milestone leaves behind infrastructure the next one reuses.

## FR-SBX0 — Scope, architecture and benchmark harness

Deliver:

- `DemoScope` feature seams;
- new domain IDs/contracts;
- `SandboxEntityRegistry` skeleton;
- central tick integration;
- sandbox-room storage interface with no content yet;
- performance instrumentation;
- scope scenarios;
- no player-visible Sandbox menu in Demo/itch.

Exit gate:

- empty Full Release sandbox service composes cleanly;
- Demo/itch do not compose it;
- existing quick suite unchanged;
- no new per-body physics callbacks.

## FR-SBX1 — Selection, pause and edit QoL

Deliver:

- Build/Sandbox workspace;
- simulation pause/speeds;
- selection/multi-selection;
- move/rotate;
- freeze/unfreeze;
- duplicate/copy/delete;
- undo/redo command framework;
- right-click context menu.

Use a tiny internal test-part set first.

Exit gate:

- editor is pleasant enough to build a simple arrangement without debug controls;
- no economy or save corruption;
- UI works across supported DPI/window sizes.

## FR-SBX2 — Generic Properties + tool variants

Deliver:

- property descriptor registry;
- Win98 Properties window;
- common physics properties;
- selected existing tool property adapters;
- named local tool variants;
- reward normalization policy;
- property persistence/validation.

Start with Baseball, Pistol and Rope Suspender because together they exercise passive physics, cursor weapon tuning and force-link tuning.

Exit gate:

- values survive restart;
- canonical defaults are untouched;
- invalid values cannot reach physics;
- modified weapon cannot break economy ceilings.

## FR-SBX3 — Construction parts + five core joints

Deliver:

- initial part library;
- Pin/Weld/Hinge/Spring/Rope;
- joint selection/properties/delete;
- persistent topology;
- 3D presentation mirrors;
- generalized Rope Suspender path.

Exit gate demonstration:

- player can construct a hanging bridge/pendulum, wheeled cart and spring launcher using the same primitive set.

## FR-SBX4 — Materials, breakage and heat

Deliver:

- material definitions;
- structural durability for construction;
- glass/wood authored break outcomes;
- heat state;
- fire spread to approved sandbox entities;
- destroyed-device state hooks.

Exit gate:

- Fire Sprayer can ignite an authored wooden structure, damage propagates without runaway object counts, and metal/glass/wood visibly behave differently.

## FR-SBX5 — Signals and devices

Deliver first vertical slice:

```text
Button -> Wire -> Lamp
Button -> Timer -> Piston
Pressure Plate -> Trigger Adapter -> approved weapon
```

Then add logic gates and remaining launch devices.

Exit gate:

- player can build an automated trap/machine with no debug UI;
- cyclic graphs remain bounded;
- save/load reproduces function.

## FR-SBX6 — Room Physics Control Panel

Deliver:

- time scale;
- gravity;
- wind;
- ambient temperature if FR-SBX4 thermal model is ready;
- guide visibility;
- reset.

Exit gate:

- all supported ranges pass Buddy/contraption stability scenarios.

## FR-SBX7 — Buddy structural damage + detachment

Deliver:

- structural integrity model;
- damage channels;
- arm/leg detachment;
- behavior/active-drive missing-limb handling;
- non-gore structural feedback;
- Gore integration;
- persistence.

Exit gate:

- repeated destructive sessions cannot corrupt rig topology or strand the Buddy in an unrecoverable state.

## FR-SBX8 — Structural repair + System Restore

Deliver:

- dedicated structural repair interaction;
- integrity repair;
- limb reattachment;
- System Restore Control Panel flow;
- repair UI/VFX/SFX.

Exit gate:

- every structurally damaged state produced by shipped tools has a tested recovery path.

## FR-SBX9 — Functional furniture + scene tools

Deliver:

- selected physical/interactive furniture;
- sit/rest/use capabilities;
- freeze/pose/look-at screenshot controls;
- safe cancellation on furniture edit/delete.

Exit gate:

- room decoration, sandbox physics and Buddy autonomy coexist without conflicting ownership of the same object.

## FR-SBX10 — Local blueprints

Deliver:

- selection graph serializer;
- atomic local blueprint store;
- blueprint browser;
- ghost placement;
- ID remap;
- validators/caps;
- previews.

Exit gate:

- a nontrivial automated contraption can be saved, app restarted and placed into a clean room with identical behavior.

## FR-SBX11 — Workshop contraptions

Deliver:

- new package type;
- Steam tags/discovery filters;
- staging/validator/import;
- preview/publish UI;
- Demo hard isolation;
- Workshop emulator scenarios.

Exit gate:

- Full Release account can publish/install/place a contraption;
- Demo cannot discover/import/place the same package even through direct local/API bypass attempts.

## FR-SBX12 — Shareable item/weapon variants

Deliver:

- item-variant package;
- Workshop browse/import;
- contraption embedded override support;
- missing/locked base-item behavior.

Exit gate:

- customized tool behavior reproduces deterministically without granting content ownership or altering canonical tool Resources.

## FR-SBX13 — Secrets, content expansion and release polish

After the systems are stable:

- more parts/devices;
- authored weird interactions;
- Easter eggs;
- achievements;
- tutorial/helper entries;
- SFX/VFX;
- final art;
- clean-room/IP audit;
- accessibility;
- four/eight-hour sandbox soak;
- Windows DPI/multi-monitor matrix;
- Workshop abuse/malformed-package pass;
- economy/progression recalibration.

---

# 25. Relationship to existing Full Release roadmap

This program should become a new top-level `RELEASE-SBX` section in `FULL_RELEASE_EXPANSION_ROADMAP.md` after owner acceptance of this detailed plan.

Dependency guidance:

- Steam Demo must remain stabilized first.
- Existing RELEASE-0 Workshop/platform/data prerequisites remain prerequisites.
- FR-SBX0..3 may be developed before multiple room profiles if they use a stable default RoomProfileId from day one.
- Persistent sandbox room files should align with RELEASE-ENV1 multiple-room identity before shipping.
- Functional furniture overlaps RELEASE-ENV3 and should supersede duplicate implementation rather than create a second interaction framework.
- FR-SBX11 Workshop contraptions uses the existing UGC transport/staging layer and should be implemented only after the Workshop v1 path is considered stable.
- Buddy Studio, Potion Shop, Voice and other Full Release programs remain separate feature tracks; sandbox code must not absorb their responsibilities.

---

# 26. Explicit non-goals / deferred work

Not part of this initial systemic-sandbox program unless separately promoted:

- arbitrary C#/GDScript/Lua scripting by Workshop items;
- arbitrary imported Godot scenes;
- arbitrary shaders/native libraries;
- unrestricted user-authored meshes as functional physics definitions;
- full electrical circuit simulation;
- fluid simulation;
- arbitrary freeform polygon cutting;
- multiple simultaneous Buddies;
- multiplayer;
- user-authored AI behavior scripts;
- Workshop packages that include a player's Buddy identity/paint/structural state;
- exact People Playground or MaD2 UI/art/content replication.

---

# 27. Clean-room / inspiration boundary

The intended inspiration is at the level of generic sandbox ideas:

- editable object properties;
- joints;
- signals/triggers;
- construction;
- material interactions;
- damage/repair;
- reusable contraptions;
- Workshop sharing.

Desktop Buddy must use original:

- Win98-themed UI layouts;
- terminology where practical;
- icons/art;
- code;
- device appearance;
- sound;
- default contraptions;
- item tuning;
- secret content;
- property organization.

Do not reproduce competitor source code, art, sounds, exact interface arrangement, proprietary item sets or authored puzzle/secret sequences.

---

# 28. Product-level acceptance test

This program is successful when a Full Release player can do all of the following in one coherent session:

1. Open the Sandbox toolbox.
2. Spawn a beam, wheel, button, timer, piston and lamp.
3. Right-click the beam and change a physical property through a Win98 Properties dialog.
4. Build a small wheeled or hinged structure with joints.
5. Wire the button through logic/timing into the piston/lamp.
6. Attach or trigger an existing weapon safely from the device graph.
7. Start the simulation and watch the machine interact physically with the Buddy.
8. Damage a structure and see material-specific failure.
9. Cause structural Buddy damage severe enough to detach a limb.
10. Repair and reattach that limb, or use System Restore.
11. Pause, rearrange and duplicate parts without fighting the physics simulation.
12. Save the machine as a local Blueprint.
13. Restart the game and place the Blueprint again.
14. Publish it to Steam Workshop from the Full Release.
15. Install another player's contraption and run it without executable mod content.
16. Verify that the same contraption feature is absent and non-importable in the Steam Demo.

The key quality bar is not the number of parts. It is whether the small initial set combines cleanly enough that players can invent uses the implementation did not explicitly script.
