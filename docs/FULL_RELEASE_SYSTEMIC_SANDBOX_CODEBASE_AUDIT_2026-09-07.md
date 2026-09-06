# Desktop Buddy — Full Release Systemic Sandbox Codebase Audit

Status: **owner-directed audit / initial-slice source alignment**  
Recorded: 2026-09-07  
Planning branch: `plan/full-release-systemic-sandbox`  
Audited base: `main` at `3966648b7f9fb8c6a70f4cf6f8e586b9998a2641`  
Scope: **Full Steam game only**

This document audits `docs/FULL_RELEASE_SYSTEMIC_SANDBOX_IMPLEMENTATION_PLAN.md` against the code that actually exists on the audited `main` commit.

It does **not** cancel the larger systemic-sandbox direction. Instead it narrows the first implementation program to the most important / most reusable functions from each system group. The original plan remains the long-term capability backlog; where the two disagree about the **initial vertical slice, architecture fit, or task order**, this audit is authoritative for the first implementation pass.

---

## 1. Executive result

The overall product direction fits the current project, but the first plan was too broad for an initial slice. Several systems already have strong foundations that should be extended rather than replaced, while two areas in particular need less ambitious first versions:

1. **Construction constraints must follow Desktop Buddy's project-owned routed-force architecture, not assume a native Godot joint/motor stack.** The repository deliberately avoided `PinJoint2D` motor behavior after documenting Godot 2D joint issues.
2. **Physical Buddy limb detachment is not a safe first-slice feature.** The current active puppet, recovery, standing, presentation and object-interaction systems assume exactly six always-present bodies. Structural integrity can be added cleanly first; physical detachment should follow a dedicated inactive/missing-part refactor.

The first slice should prove one complete sandbox loop:

```text
open Build Mode
  -> place a few reusable physics parts
  -> move / rotate / freeze / duplicate them
  -> change a few safe properties
  -> connect parts with a small joint set
  -> connect a small device graph
  -> run the contraption
  -> damage the Buddy / a breakable part
  -> repair the Buddy with the existing Repair Kit
  -> save the contraption locally
  -> restart and load it again
```

That proves the architecture and the player fantasy without implementing the entire MaD2 feature surface at once.

Workshop contraption sharing, true limb detachment, a broad materials simulation, advanced logic gates, scene posing, functional furniture and advanced Room Physics remain planned follow-up slices.

---

# 2. Hard distribution boundary

Nothing in this document changes the owner decision that the new systemic sandbox is **Full Release only**.

The existing distribution model is a good fit:

- the public Steam Demo is the default scope;
- the Full Release opts in through the `full_release` build feature;
- itch.io is a stricter scope and wins if tags are accidentally combined;
- `DemoScope.IncludesRoomDecorator` already demonstrates a Full-Release-only entry point.

### Audit correction

The systemic sandbox should be gated more strongly than the current Room Decorator composition pattern.

For the new system:

```csharp
public static bool IncludesSystemicSandbox => IsFullRelease;
```

is the authoritative top-level seam for the initial slice.

Do **not** initially create seven aliases that all evaluate to the same value unless a second policy boundary actually appears. The original plan proposed separate flags for construction, properties, automation, structural damage, Workshop and scene tools. That is unnecessary ceremony for the first slice.

Add narrower flags only when the feature genuinely needs a different release policy, for example later:

```text
IncludesSystemicSandbox      // full game base sandbox
IncludesContraptionWorkshop // full game + Steam Workshop availability
```

### Required fail-closed checks

When `IncludesSystemicSandbox == false`:

- no Build Mode command is registered;
- no construction/device catalogue is exposed;
- no systemic-sandbox runtime coordinator is composed;
- no systemic-sandbox room/blueprint file is loaded into live gameplay;
- no structural-integrity runtime hooks are attached;
- no systemic-sandbox input is routed;
- later Workshop contraption items are neither importable nor instantiable.

Hiding a menu item alone is not sufficient.

---

# 3. Composition and fixed-tick audit

## Current code

`SandboxRoot` is already the normal-play composition root and sole gameplay `_PhysicsProcess` router. It initializes and ticks the window shell, boundaries, Grab, Rope Suspender, loose objects, launcher, cursor tools, guns, Buddy behavior, damage, grenades, fire and presentation.

It is already large enough that adding every future sandbox component as another exported property would work technically but would move the project toward the all-purpose-root design forbidden by `AGENTS.md`.

`EnvironmentCustomizationBootstrap` demonstrates a better pattern for large optional feature families: it owns its own composition/UI registration and receives the real `SandboxRoot` through explicit `Configure` injection from `Bootstrap`.

## Revised architecture

Add:

```text
SystemicSandboxBootstrap
    owns feature composition / Win98 command registration / stores

SystemicSandboxCoordinator
    owns one deterministic batch PhysicsTick(delta)

SandboxRoot
    owns exactly one optional coordinator reference/tick seam
```

Normal boot:

```text
Bootstrap
  -> if DemoScope.IncludesSystemicSandbox
       create/configure SystemicSandboxBootstrap
  -> add SandboxRoot
  -> bootstrap composes focused subsystem nodes/services
  -> SandboxRoot routes SystemicSandboxCoordinator.PhysicsTick(delta)
```

The new feature must **not** create another gameplay `_PhysicsProcess` owner.

UI nodes may use `_Process` for Win98 layout/presentation in the same way existing editors do, but authoritative physics/device/damage state advances only through the routed gameplay tick or explicit event ingress that is consumed on that tick.

### Verdict

**Original direction valid, integration approach tightened.**

---

# 4. Physics-entity audit

## Current code

`LooseObjectBody` + `LooseObjectProfile` + `LooseObjectRegistry` are useful reference implementations for:

- authored physics tuning;
- runtime registry ownership;
- impact attribution;
- no per-body gameplay tick;
- safe bounded object population;
- grab compatibility;
- 3D presentation mirroring.

They are not a suitable generic construction entity implementation because:

- `LooseObjectBody` is fundamentally a circular loose-object body;
- Buddy object sensing is explicitly built around `LooseObjectRegistry.Capacity` and `LooseObjectBody` semantics;
- the authoritative loose-object cap is 24 and is part of existing game behavior;
- construction needs rectangles, wheels and persistent identities;
- a beam should not be interpreted by Buddy autonomy as a ball/food/ordinary catch candidate.

## Revised architecture

Keep the original plan's separate registry decision:

```text
SandboxEntityRegistry
SandboxEntityBody2D
SandboxEntityDefinition
SandboxEntityState
```

Do not inherit `SandboxEntityBody2D` from `LooseObjectBody` simply to reuse a few fields. Share small helpers/value models where useful.

### Collision layer

The current collision table has dedicated layers for Room, Buddy, Loose Objects, Projectiles, Physical Tools, Interaction Sense and Flame.

Add a dedicated named 2D layer:

```text
SandboxEntities
```

and explicitly update the masks.

Initial collision contract:

- Sandbox Entity <-> Room: yes
- Sandbox Entity <-> Buddy: yes
- Sandbox Entity <-> Sandbox Entity: yes
- Sandbox Entity <-> Loose Object: yes
- Sandbox Entity <-> Physical Tool: yes
- Sandbox Entity <-> Projectile: yes where the projectile semantics support world impacts
- Sandbox Entity <-> Buddy InteractionSense: **no** in VS1
- Sandbox Entity <-> Flame: defer until generalized material/fire interaction exists

The exact projectile/world-impact path needs a focused implementation because current projectiles are primarily authored around Buddy/loose-object contacts. Do not imply destructible construction gets gun damage merely by adding one collision mask bit.

### Initial capacity

Do not start by promising the original provisional 128/96 budgets.

VS1 engineering target:

```text
48 sandbox entities total
32 simultaneously awake/dynamic
48 constraints
32 devices
64 signal wires
```

These are benchmark targets, not product promises. Increase after profiling rather than starting with large public limits.

### Verdict

**Separate construction registry is correct and should stay. Initial capacity should be conservative.**

---

# 5. Build/Edit UX audit

## Current reusable foundations

`EnvironmentDecorator` already solves several difficult interaction problems:

- room-space input surface;
- disabling/restoring normal gameplay pointer input while editing;
- placed-item selection;
- move mode;
- rotation;
- delete mode;
- free placement;
- Win98 catalogue/category/value controls;
- staged editing and cancel/save behavior;
- room-coordinate handling during resizes.

`EnvironmentEditSession` also demonstrates engine-free working-copy/checkpoint semantics.

## Do not merge the systems

Construction is not Room Decorator v2.

Room Decorator has paid-copy/storage semantics and visual/non-physical definitions. Construction should use permanent unlocks and physics entities.

Reuse/extract UI and coordinate concepts, not the financial/domain model.

## VS1 Build Mode controls

Implement only the highest-value edit operations:

```text
Select one object
Move
Rotate
Freeze / Unfreeze
Duplicate
Delete
Properties...
```

Required quality-of-life:

- visible selected outline;
- Escape cancels current transform/action;
- Delete key may delete selected object when no text field owns focus;
- right-click opens Win98 context menu;
- duplicate uses a deterministic small offset;
- optional basic grid snap if the existing placement helper makes this cheap.

## Defer from VS1

- marquee selection;
- multi-select;
- copy/paste;
- group operations;
- full editor undo/redo stack;
- bring forward/send backward;
- generalized scene posing;
- transform gizmo suite;
- save-selection-as-blueprint from arbitrary multi-selection.

The first blueprint command can simply save the whole current contraption/workspace or one connected component graph.

### Verdict

**Original editor plan was over-scoped. Existing Environment UX substantially lowers VS1 cost.**

---

# 6. Properties-system audit

## Current code

The game already authors meaningful runtime tuning in typed Resources:

- `LooseObjectProfile` contains mass, damping and bounce;
- `GunProfile` contains cadence, reload, projectile count/spread, projectile speed/mass/gravity and several correctness-sensitive fields;
- Rope Suspender exposes stiffness/damping/maximum force;
- other tools similarly use dedicated typed profiles.

This validates the core idea of a generic property-descriptor UI, but it also shows why the player must not receive every existing Resource field.

Several fields are implementation invariants, not safe sandbox knobs. For example, `GunProfile.MaximumTravelPerTickPx` documents a geometric correctness ceiling that prevents fast projectiles from tunneling through the Buddy while preserving impact impulse.

## VS1 generic property framework

Build the descriptor/override mechanism once, but expose only:

### All supported construction bodies

```text
Mass
Bounce
Gravity Scale
Frozen
```

Friction may be added in the same slice if a clean PhysicsMaterial merge policy is established; do not block VS1 on it.

### One representative weapon

Use **Shotgun** as the first customized weapon because spread is immediately visible and familiar.

Expose only:

```text
Fire Rate       -> bounded ShotIntervalTicks
Spread          -> bounded spread half-angle range
Knockback       -> bounded contact-shove tuning
```

Do **not** expose `MuzzleSpeed` in VS1. It affects both physical scoring behavior and projectile coverage correctness.

Do not expose pool capacities, CCD/collision details, lifetimes, path/resource IDs, projectile radius, or arbitrary Resource properties.

## Runtime resolution

Keep canonical authored Resources immutable.

```text
canonical profile
+ validated semantic override set
= resolved runtime settings
```

A player variant never edits the `.tres` Resource object shared by the game.

## Economy boundary

For VS1, impacts generated with a non-default customized weapon preset should be **sandbox/non-paying**.

That is simpler and safer than attempting a compensation formula for extreme fire rates/spread/knockback before the system is proven.

Default authored weapons retain existing progression/economy behavior.

### Named variants

Local named weapon variants are useful but not required to prove the first Build Mode vertical slice. If the generic override serializer is already needed for blueprints, add `Save Variant...`; otherwise defer it to the next properties slice.

### Verdict

**Generic descriptor architecture is useful; initial exposed property count should be deliberately tiny.**

---

# 7. Constraint / joint audit

## Current code

`RopeSuspensionComponent` already implements a project-owned bounded spring/damping constraint from a rigid body point to a world anchor, advanced from the routed fixed tick.

Separately, the repository's reference research explicitly records problems with Godot 2D joint limits/motors, and `AGENTS.md` forbids making `PinJoint2D` motor behavior a dependency of the Buddy architecture.

Construction does not need to ban every Godot passive joint forever, but the systemic sandbox should not make an unproven native joint stack its architectural foundation.

## VS1 constraint set

Only three high-value semantics:

### 1. Weld

Keeps two bodies' relative position and relative angle together using bounded corrective force/torque.

Uses:

- rigid structures;
- attaching wheels/plates before a hinge is substituted;
- simple machines;
- player expectation from physics sandboxes.

### 2. Hinge

Keeps two anchor points together while allowing relative rotation.

Uses:

- doors;
- wheels;
- pendulums;
- levers;
- machine arms.

Motorized hinge behavior is deferred. VS1 hinge is passive.

### 3. Rope / World Anchor

Generalize or adapt the existing Rope Suspender mathematics to support construction entities and explicit persistent endpoints.

VS1 may keep rope as body-to-world only if body-to-body rope complicates serialization. Body-to-body rope is the first follow-up.

## Defer

- Spring as a separate player tool;
- sliders/prismatic joints;
- motors;
- gears;
- breakable joint strength UI;
- pulleys;
- generic pin joint catalogue.

Weld + Hinge + Rope are enough to make recognizable structures and machines.

### Verdict

**Reduce five+ planned joints to three semantic constraints and keep them on the established routed-force model.**

---

# 8. Initial construction content audit

The first slice does not need a large catalogue.

Ship only three reusable geometry primitives:

```text
Beam   - long rectangular body
Block  - compact rectangular body
Wheel  - circular body
```

These three plus Weld/Hinge/Rope already allow:

- platforms;
- hanging traps;
- pendulums;
- crude carts;
- doors;
- levers;
- rotating arms;
- weighted machines.

Optional fourth item if required for usability:

```text
Plate - wide thin rectangle
```

Use trusted first-party shape definitions and matching trusted 3D primitive presentation. No imported/custom meshes.

### Verdict

**3–4 construction parts are enough for the first vertical slice.**

---

# 9. Device/signal audit

## Namespace correction

The codebase already uses `DesktopBuddy.Domain.Automation` for test/input trace and runner automation. Gameplay electronics should not reuse the generic `Automation` namespace.

Prefer:

```text
DesktopBuddy.Domain.Contraptions
DesktopBuddy.Contraptions
```

with optional `Signals` and `Devices` sub-namespaces later.

## VS1 signal model

Keep the deterministic graph, but begin with boolean activation plus one timer duration parameter.

Initial device set:

```text
Button
Timer
Piston
Weapon Trigger
```

This proves both signal flow and physical output.

Example chains:

```text
Button -> Piston
Button -> Timer -> Piston
Button -> Weapon Trigger -> Shotgun
Button -> Timer -> Weapon Trigger -> Shotgun
```

### Button

- player click/activation produces signal;
- momentary output;
- no analog value in VS1.

### Timer

- input edge starts a bounded authored/player-configurable delay;
- produces one output pulse;
- deterministic routed-tick timing.

### Piston

- two authored anchor/body references;
- bounded target stroke;
- project-owned force application;
- no unrestricted speed/force values.

### Weapon Trigger

- targets one compatible first-party weapon/device instance;
- invokes the same semantic fire request path as player use where possible;
- cannot invent arbitrary method/node names.

## Defer

- pressure plate;
- proximity sensor;
- AND/OR/XOR/NOT;
- memory latch;
- randomizer;
- fan;
- motor;
- conveyor;
- electromagnet;
- heater;
- door device;
- speaker;
- analog signal values.

Logic gates become worthwhile after there are enough devices to connect.

### Verdict

**Four devices are enough to prove electronics/automation.**

---

# 10. Materials and object destruction audit

The original material plan is too broad for VS1. Temperature, conductivity, flammability, glass fracture and multiple material behaviors each introduce their own cross-system rules.

## VS1

Create the semantic material slot now, but ship only:

```text
Metal
Wood
```

Initial material differences may be limited to:

- default mass/density tuning;
- bounce/friction defaults where applicable;
- durability;
- break threshold;
- presentation/SFX choice.

### Breakage

Implement one reliable break model first:

- Wood Beam/Block accumulates structural damage from qualifying impacts;
- reaching zero durability marks it broken;
- first implementation may remove the original body and create **two authored fragment bodies** with bounded impulse.

Metal may dent/show damage state but remain unbreakable in VS1 if stable fragmentation for every shape is unnecessary.

## Defer

- heat simulation;
- conductivity/electricity;
- fire spreading to construction;
- glass;
- rubber;
- plastic;
- arbitrary cutting;
- fracture tessellation;
- melting.

The current Flame collision design is intentionally isolated; generalized fire/material interactions should be a deliberate later slice rather than quietly widening the Flame mask.

### Verdict

**Keep material identity + durability, defer the environmental chemistry simulation.**

---

# 11. Buddy structural-damage audit

## Current architecture constraint

The Buddy is not currently an entity whose body parts can disappear safely.

The rig owns exactly six fixed bodies:

```text
Head
Torso
LeftHand
RightHand
LeftFoot
RightFoot
```

Multiple systems iterate `PuppetRigProfile.RequiredPartCount`, and active behavior directly addresses particular hands/feet/head/torso. Recovery resets all six bodies. Work-mode hit regions and presentation similarly assume all six remain valid.

Therefore a physical detachment implementation in VS1 would require a broad refactor before the actual feature could be trusted.

## VS1 structural integrity

Add an engine-free state for each part:

```text
Integrity: 0..100
DamageBand: Healthy / Damaged / Critical
```

Input source:

- listen to the existing accepted damage event stream (`InteractionDamageComponent.ImpactAccepted`);
- use accepted part + impulse/pain/source information;
- do not modify the core contact detector for the first version.

Presentation:

- cracks / scratches / sparks / darker mechanical wear;
- no new gore requirement;
- existing Gore Mode continues to own blood/wounds.

At zero integrity in VS1:

- part becomes **disabled/critical in state**, but does not physically leave the rig;
- optionally reduce an authored function such as hand action strength only if owner-approved and regression-safe;
- simplest first version is visual/state consequence only plus achievement/stat hooks.

Persistence:

- structural integrity survives restart in the Full Release;
- System Restore and Repair Kit can restore it.

## Detachment follow-up prerequisite

Before actual limbs can pop off, add a dedicated `BuddyPartAvailability` / inactive-part abstraction and make all of these tolerant of missing/disabled parts:

- ActiveDrive;
- StandingDetector;
- Recovery;
- object holding/catching;
- grab/stretch semantics;
- containment;
- 2D/3D presentation;
- face/head behavior;
- Work-mode hit regions;
- painting/cosmetic sockets;
- hard recovery/session resume;
- save/load.

Only after that refactor should physical detach/reattach be implemented.

### Verdict

**Integrity: VS1. Physical detachment: later dedicated slice.**

---

# 12. Repair audit

The original plan proposed a dedicated structural repair tool. The current game already has a Repair Kit and that item is explicitly the existing healing/cleanup object.

Do not add a second repair tool in VS1.

When `IncludesSystemicSandbox` is active, extend the existing Repair Kit success path to additionally:

```text
restore Buddy structural integrity
```

Existing behavior remains:

- clears harmful statuses;
- clears burning/pain/wounds through current seams;
- mood/care behavior stays as authored.

The new structural restore must be additive and Full-Release systemic-sandbox scoped.

Also provide the free safety route from the original plan:

```text
Control Panel -> Buddy -> System Restore...
```

For VS1 this may be a simple confirmation action that restores all part integrity. It must not reset money, progress, room content, paint or cosmetics.

### Verdict

**Reuse Repair Kit; do not create a duplicate repair tool.**

---

# 13. Room Physics audit

Room-wide physics editing is attractive but not required to prove the first construction loop, and changing global gravity/simulation speed interacts with the carefully tuned active puppet.

VS1 already permits per-construction-entity `Gravity Scale` in Properties.

Therefore:

### VS1

- no full `Control Panel -> Room Physics` applet required;
- per-entity gravity scale is enough for experimentation.

### Follow-up

Add Room Physics only after a focused Buddy regression spike for:

```text
Gravity
Wind
```

Simulation speed should remain a separate decision. Avoid using `Engine.TimeScale` as a quick implementation because UI, presentation timing and other engine systems may be affected along with gameplay.

### Verdict

**Defer global Room Physics from VS1.**

---

# 14. Functional furniture and scene tools audit

The current Full Release roadmap already separately plans authored furniture interactions. Mixing that work into the first systemic-sandbox slice would unnecessarily join two behavior systems.

Likewise, pose/scene tools are useful but not a prerequisite for construction.

### VS1

Defer both.

### Later reuse

- physical furniture bodies can use `SandboxEntityDefinition` where appropriate;
- authored sit/use behaviors remain trusted capability IDs owned by the existing/full-release environment interaction program;
- scene tools can reuse Build Mode selection infrastructure later.

### Verdict

**Keep these as follow-up consumers of the sandbox framework rather than VS1 blockers.**

---

# 15. Persistence and blueprint audit

## Current code

`SaveCoordinator` serializes progression, character selection, Work and Environment semantic state into the central progress save with revision tracking.

This is appropriate for relatively small semantic progress. A frequently edited contraption graph should not be added as another large property on `ProgressSave`.

## VS1 local sandbox store

Use a separate versioned JSON store, for example:

```text
user://sandbox/active-room.json
user://sandbox/blueprints/<guid>.json
```

Exact path can be aligned with future room-profile work before implementation.

Initial active-room document:

```text
schemaVersion
entities[]
  instanceId
  definitionId
  position
  rotation
  frozen
  validated propertyOverrides
constraints[]
  id
  kind
  endpoint A/B
  anchors
  bounded settings
wires[]
  id
  source port
  target port
devices[] / device settings embedded in entities
```

Do not serialize:

- Godot node paths;
- RIDs;
- Resource paths supplied by the user;
- live object references;
- scripts/scenes;
- arbitrary property names;
- transient velocities as the canonical saved build.

### Blueprint VS1 UX

Keep it simple:

```text
Save Contraption...
Load Contraption...
Delete Blueprint...
```

Save the whole systemic-sandbox graph in VS1. Selection-based partial blueprints can follow multi-select later.

### Autosave

The active room graph may autosave/coalesce outside the physics tick. File I/O must never happen in `PhysicsTick`.

### Verdict

**Original separate-store decision is correct. Simplify blueprint UX to whole-graph save/load.**

---

# 16. Workshop audit

The current Workshop architecture is a good future foundation but is intentionally explicit and narrow.

Current share content types are only:

```text
RoomPainting
BuddyCharacter
```

The manifest policy has exact path allowlists and byte caps. Workshop browse, import-type detection and Steam public-tag mapping also explicitly switch on those two content types.

Therefore adding contraptions is feasible, but it is a real package-format extension rather than a free side effect of having JSON serialization.

## VS1

**Do not implement Steam Workshop contraptions yet.**

First prove the exact same semantic graph through local blueprint save -> validate -> load.

## Follow-up Workshop slice

Then add:

```text
ShareContentType.Contraption
wire value: contraption
payload: contraption.json
```

with:

- strict schema;
- strict count/byte caps;
- first-party definition allowlist;
- property descriptor validation;
- constraint endpoint validation;
- wire/port validation;
- fresh local instance IDs on import;
- no scripts/resources/scenes/shaders/native code;
- explicit Full Release gating during discovery, import and placement.

The Workshop browser/filter/tag/coordinator paths must all be updated together.

### Source-of-truth note

`AGENTS.md` and the existing Workshop source-alignment documents currently authorize only room painting and Buddy-character sharing and explicitly prohibit arbitrary new package types without a later owner gate. The owner has now approved systemic-sandbox/contraption direction for the **Full Release**, but this new decision must be promoted into the source-of-truth documentation before implementation reaches the Workshop task.

### Verdict

**Architecture fits; defer Workshop until local blueprint format is stable.**

---

# 17. Source-of-truth audit

Before gameplay code begins, FR-SBX0 must reconcile documentation.

Current conflicts/gaps:

- `FULL_RELEASE_EXPANSION_ROADMAP.md` does not yet include the systemic-sandbox program;
- its consolidated UGC section currently lists complete rooms and player cosmetics, not contraptions;
- `AGENTS.md` still treats generalized/new Workshop package scope as deferred without a new owner decision;
- older README/scope notes may still describe earlier tool/gore state and should not be used as the systemic-sandbox authority.

## FR-SBX0 documentation changes

Update at minimum:

```text
AGENTS.md
  -> add this source-alignment/audit document to source-of-truth order
  -> state Full Release systemic sandbox is approved
  -> preserve no-arbitrary-code/mod-loader boundary

FULL_RELEASE_EXPANSION_ROADMAP.md
  -> add RELEASE-SANDBOX program
  -> reference original plan + this audit
  -> place Workshop contraptions after local blueprint validation

DECISIONS.md
  -> record Full Release-only scope
  -> record reduced first vertical slice
  -> record construction economy: permanent unlock / unlimited spawned copies subject to caps
  -> record no physical limb detachment in VS1
```

Do this before agents are asked to implement later tasks from separate branches/worktrees.

---

# 18. Revised initial vertical slice

The initial implementation should be split into four small deliverable slices rather than the original broad FR-SBX0..13 program.

## VS1-A — Build foundation

### Scope/composition

- `DemoScope.IncludesSystemicSandbox`;
- `SystemicSandboxBootstrap`;
- `SystemicSandboxCoordinator`;
- one routed tick seam from `SandboxRoot`;
- no subsystem composition in Demo/itch.

### Entity runtime

- `SandboxEntityDefinition`;
- `SandboxEntityState`;
- `SandboxEntityRegistry`;
- dedicated collision layer/masks;
- Beam, Block, Wheel;
- simple trusted 3D mirrors.

### Editor

- open/close Build Mode;
- single select;
- move;
- rotate;
- freeze/unfreeze;
- duplicate;
- delete;
- right-click context menu.

### Properties

- Mass;
- Bounce;
- Gravity Scale;
- Frozen.

### Constraints

- Weld;
- passive Hinge;
- Rope/world anchor.

### VS1-A exit test

Player can build a hanging/hinged wheeled structure from three primitives, change mass/bounce, freeze/unfreeze it and run the simulation without affecting Demo builds.

---

## VS1-B — Device chain

Add only:

- Button;
- Timer;
- Piston;
- Weapon Trigger;
- boolean/pulse wires;
- deterministic routed evaluation order;
- visible wire endpoints/connection feedback.

Add Shotgun property override vertical slice:

- fire rate;
- spread;
- knockback;
- non-default customized weapon impacts are non-paying.

### VS1-B exit test

Player can wire:

```text
Button -> Timer -> Piston
```

and:

```text
Button -> Weapon Trigger -> Shotgun
```

and the same saved seed/input sequence produces the same activation order.

---

## VS1-C — Consequence / repair loop

### Construction durability

- Metal + Wood IDs;
- durability;
- qualifying impact damage;
- one breakable Wood path with bounded authored fragments.

### Buddy integrity

- per-part 0..100 state;
- accepts existing `ImpactAccepted` event data;
- healthy/damaged/critical presentation;
- persistence;
- no physical detachment.

### Repair

- existing Repair Kit restores integrity in Full Release systemic-sandbox scope;
- `System Restore...` resets structural integrity for free.

### VS1-C exit test

A player-built mechanism can damage a wooden part and reduce Buddy part integrity; the Repair Kit restores Buddy integrity without resetting normal progress.

---

## VS1-D — Local persistence / blueprint

- versioned active-room sandbox JSON;
- strict semantic validation;
- save/load whole contraption;
- local blueprint library;
- safe fresh IDs where required;
- missing/unknown definitions fail safely;
- no file I/O on fixed tick.

### VS1-D exit test

Player builds the VS1-B machine, saves it, exits the game, relaunches, loads it, and receives the same entity/property/constraint/device graph.

---

# 19. Explicitly deferred until after the initial vertical slice

These remain part of the long-term systemic-sandbox direction but are **not VS1 requirements**:

- physical Buddy limb detachment and reattachment;
- multi-select;
- marquee selection;
- copy/paste;
- full undo/redo history;
- grouping;
- Spring joint;
- motorized hinge;
- sliders/gears/pulleys;
- pressure plates;
- proximity sensors;
- AND/OR/NOT/XOR gates;
- memory/latches;
- fans;
- conveyors;
- motors;
- electromagnets;
- heaters;
- lamps/speakers unless needed for polish;
- broad weapon property editing;
- arbitrary projectile-speed editing;
- Glass/Rubber/Plastic material families;
- heat/temperature simulation;
- electricity/conductivity;
- construction fire spread;
- generalized cutting/fracture;
- global Room Physics UI;
- functional furniture behaviors;
- scene/posing tools;
- selected-subgraph blueprints;
- blueprint thumbnails;
- Steam Workshop contraption upload/download;
- shared weapon presets;
- systemic-sandbox Easter eggs/ARG content;
- large final construction/device catalogue.

No deferred item should leave a disabled visible button in the VS1 UI.

---

# 20. File/component touch map for VS1

The exact names may evolve during implementation, but ownership should roughly stay here.

## Existing files expected to change

```text
src/App/DemoScope.cs
  add IncludesSystemicSandbox

src/App/Bootstrap.cs
  compose/configure systemic sandbox only for Full Release

src/App/SandboxRoot.cs
  one optional coordinator/tick integration seam

src/App/CollisionLayers.cs
project.godot
  add SandboxEntities layer and masks

src/Tools/CursorGunComponent.cs and/or focused resolver seam
  consume validated Shotgun override snapshot without mutating GunProfile

src/App/SandboxRoot.cs repair-success integration or focused repair listener
  route existing Repair Kit success into structural-integrity restore
```

Avoid widening `SandboxRoot` beyond the one subsystem seam and any unavoidable event registration.

## New domain area

```text
domain/DesktopBuddy.Domain/Contraptions/
  SandboxEntityIds.cs
  SandboxEntityModels.cs
  SandboxPropertyModels.cs
  SandboxConstraintModels.cs
  SignalGraph.cs
  DeviceModels.cs
  StructuralIntegrityModel.cs
  BlueprintModels.cs
  BlueprintPolicy.cs
```

Keep Godot-free validation and state transition logic here.

## New runtime area

```text
src/Contraptions/
  SystemicSandboxBootstrap.cs
  SystemicSandboxCoordinator.cs
  SandboxEntityRegistry.cs
  SandboxEntityBody2D.cs
  SandboxConstraintRuntime.cs
  SandboxSignalRuntime.cs
  devices/
  presentation/
  editing/
```

## New persistence area

```text
src/Persistence/Contraptions/
  SandboxRoomStore.cs
  BlueprintStore.cs
```

Workshop integration is deliberately absent from the VS1 touch map.

---

# 21. Constraint runtime rules

Because VS1 may contain dozens of constrained bodies, the runtime must retain the project's fixed-tick discipline.

- no per-entity `_PhysicsProcess`;
- no per-constraint `_PhysicsProcess`;
- no LINQ/allocation in the 120 Hz hot path;
- fixed/list-backed registry iteration;
- invalid/deleted endpoints pruned at a deterministic point;
- finite-force validation before application;
- authored maximum corrective force/torque;
- no transform teleport as ordinary constraint enforcement;
- explicit sleep/wake behavior;
- constraint graph is data, not node paths.

If a Weld/Hinge cannot be made stable within bounded corrective forces at the VS1 entity budget, reduce the budget/tuning or spike a safe passive Godot joint implementation. Do not introduce a custom whole-world solver.

---

# 22. Device evaluation rules

Use a simple deterministic two-phase model:

```text
1. sample input/device state for tick N
2. evaluate signal graph from immutable tick-N snapshot
3. queue outputs
4. apply physical/device commands at the documented routed point
5. commit signal state for tick N+1
```

Do not recursively call downstream devices as UI events fire. A graph with cycles must have defined tick-delayed behavior rather than recursion/order dependence.

VS1 can reject invalid/self-referential wire graphs during editing if necessary, but the persisted validator must also reject dangling ports and unknown capability IDs.

---

# 23. Testing plan for VS1

Every slice needs engine-free tests plus focused headless Godot scenarios.

## Scope

- Demo has no Build Mode command;
- Demo does not compose coordinator/registry;
- Full Release scope does;
- malformed local sandbox files cannot bypass scope.

## Domain

- property bounds reject NaN/infinity/out-of-range;
- unknown property IDs rejected;
- constraint endpoints must exist;
- wires require valid source/target ports;
- IDs unique;
- budget limits enforced;
- blueprint decode rejects unknown definitions/capabilities.

## Physics

- Weld remains bounded and finite under load;
- Hinge anchor error stays within accepted envelope;
- Rope remains finite;
- deleting an endpoint removes/invalidates its constraints safely;
- 48-entity target scenario maintains stable state;
- no loose-object capacity regression.

## Editor

- move/rotate/freeze/duplicate/delete mutate only selected sandbox entity;
- opening Build Mode suppresses normal gameplay pointer ownership and closing restores it;
- text fields retain keyboard focus;
- room resize keeps edit mapping correct.

## Devices

- Button pulse deterministic;
- Timer exact tick count;
- Piston bounded;
- Weapon Trigger cannot call unknown tools/methods;
- cyclic/invalid graphs do not hang.

## Damage/repair

- accepted impacts reduce the correct Buddy part only;
- rejected/non-paying/non-damage events do not reduce integrity unless explicitly authored;
- integrity cannot under/overflow;
- Repair Kit restores integrity once through its existing success seam;
- System Restore restores integrity without mutating wallet/unlocks/paint/environment.

## Persistence

- save -> load semantic equality;
- atomic write/backup/quarantine behavior appropriate to the store;
- unknown future schema is not partially loaded;
- invalid count/path/property graph rejected;
- no filesystem work occurs on physics tick.

## End-to-end journey

One Full Release journey should eventually automate:

```text
open Build Mode
place Beam + Block + Wheel
hinge Wheel
change Beam mass
place Button + Timer + Piston
wire graph
run it against Buddy
observe integrity drop
apply Repair Kit
save contraption
restart process
load contraption
verify graph restored
```

A companion Demo scenario proves the Build Mode command and runtime are absent.

---

# 24. Performance gate

VS1 should establish a benchmark before expanding the catalogue.

Representative stress fixture:

```text
48 entities
32 awake
24 constraints
16 devices
32 wires
Buddy active
12 ordinary loose objects
normal 3D presentation enabled
```

The test target remains the project's 120 Hz authoritative physics design.

Measure:

- worst/average physics frame cost;
- allocations during steady-state tick;
- constraint anchor error;
- non-finite recovery incidents;
- registry churn;
- save file size and save/load time outside gameplay tick.

Only after this fixture is stable should the public cap move upward toward the larger long-term targets in the original plan.

---

# 25. Clean-room/product identity rule

The initial slice deliberately uses common physics-sandbox primitives rather than reproducing another game's distinctive catalogue/UI/layout.

The familiar capabilities are generic:

- edit object properties;
- connect rigid bodies;
- buttons/timers/actuators;
- save contraptions;
- damage/repair.

Presentation remains Desktop Buddy's original Win98 language and persistent Buddy loop.

Do not reproduce MaD2's exact property categories, UI order, names, default values, item art or distinctive tool arrangement.

---

# 26. Final audited VS1 scope

The first systemic-sandbox release candidate is therefore:

```text
FULL RELEASE ONLY

BUILD MODE
  Select / Move / Rotate / Freeze / Duplicate / Delete / Properties

PARTS
  Beam / Block / Wheel

PROPERTIES
  Mass / Bounce / Gravity Scale / Frozen
  Shotgun: Fire Rate / Spread / Knockback

CONSTRAINTS
  Weld / Hinge / Rope-world-anchor

DEVICES
  Button / Timer / Piston / Weapon Trigger

MATERIAL CONSEQUENCE
  Wood / Metal
  Durability + one Wood break path

BUDDY CONSEQUENCE
  Per-part integrity
  Damaged/critical presentation
  Existing Repair Kit restores integrity
  Free System Restore
  NO physical detachment yet

PERSISTENCE
  Active sandbox room JSON
  Whole-contraption local blueprints

NOT YET
  Workshop contraptions
  advanced logic/devices
  global Room Physics
  full materials/heat/electricity
  multi-select/advanced editor
  furniture/scene tools
  limb detachment
```

This is enough to answer the key product question: **does Desktop Buddy become substantially more replayable when a player can build, tune, automate, break, repair and preserve their own little machines?**

If that loop is fun and stable, the original comprehensive plan provides the expansion backlog. If it is not, the project has not spent a full-release development cycle implementing dozens of secondary sandbox features before learning that lesson.
