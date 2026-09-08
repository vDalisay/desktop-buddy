# Desktop Buddy — Master Release Plan

Status: **SOLE AUTHORITATIVE PLANNING SOURCE**  
Recorded: 2026-09-08  
Branch: `plan/three-build-release-scope`  
Base lineage: current `main` after `9adfbcb5a3e32aa50fe88da5fe4b772e7ec64eb8`

This document is the single current source of truth for the planned **Initial Steam Demo**, **Steam Next Fest Demo**, and **Full Release** of Desktop Buddy.

It consolidates the latest non-superseded decisions from the systemic-sandbox plan and audit, multi-Buddy/Scene planning, Next Fest planning, Workshop/moddability research, Creator Studio research, current Steam Demo roadmap, achievement baseline, and the owner corrections recorded on 2026-09-08.

## Authority rule

When this document conflicts with another planning, roadmap, audit, source-alignment, owner-lock, status-effect, Creator Studio, moddability, or release-scope document, **this document wins for current product scope, sequencing, and intended architecture**.

Older documents remain valuable research, audit evidence, implementation detail, and historical context. They are intentionally preserved unchanged. Do not edit an older source merely to make it agree with this master plan.

The detailed subsystem documents may still supply implementation detail where this master does not override them. A later explicit owner decision may supersede this master and should then be folded into a new revision of this file rather than creating another competing master plan.

---

# 1. Product model: three cumulative Steam builds

Desktop Buddy has three cumulative Steam content surfaces:

```text
INITIAL STEAM DEMO
        ↓ adds new systems
STEAM NEXT FEST DEMO
        ↓ adds breadth/depth and removes demo restrictions
FULL RELEASE
```

Formally:

```text
Initial Steam Demo ⊂ Steam Next Fest Demo ⊂ Full Release
```

A later Steam build does not remove an earlier feature merely to make its catalogue smaller. Vertical-slice curation applies only to **newly introduced systems/content**.

The itch.io build is a separate reduced distribution and is not one of these three Steam build surfaces.

## 1.1 Build feature tags

Use separate reproducible export profiles:

```text
Initial Steam Demo
    steam,steam_demo

Steam Next Fest Demo
    steam,steam_demo,next_fest_demo

Full Release
    steam,full_release
```

`steam` means Steam platform capability. It is never a content-entitlement flag.

The Initial Demo and Next Fest Demo are versions of the same Steam Demo product/AppID. The Next Fest version can replace the public Initial Demo during the event while the Initial Demo export profile remains available for regression/reproducibility.

Known AppIDs from the approved Steam integration work:

- Full game: `5114950`
- Steam Demo: `5228990`

---

# 2. Initial Steam Demo — locked scope

The Initial Steam Demo is the polished normal Demo product represented by current/pre-approved demo gameplay **without Room Decorator, without the new achievement system, and without the post-demo systemic sandbox/Scene/Creator expansion**.

## 2.1 Core Buddy sandbox

Include:

- one live persistent Buddy;
- the existing six-body ragdoll / active-puppet physics;
- autonomous movement, standing and recovery;
- grab resistance and elastic-limb behavior;
- mood, trust/fear and care reactions;
- hunger/fullness and Meal/Drink care behavior;
- personality/preferences/fun/novelty state;
- knockout and recovery;
- existing damage/pain attribution and economy;
- current loose-object physics and safety budgets;
- current Win98 Play/compact/fullscreen/window behavior;
- opt-in Gore Mode under the current Steam policy.

No production multi-Buddy runtime and no Scene library are exposed yet.

## 2.2 Current interaction/tool toybox

Keep the complete current Demo catalogue. Do not re-curate it for Next Fest:

1. Grab
2. Pet
3. Tickle
4. Boxing Glove
5. Baseball
6. Meal
7. Baseball Bat
8. Nerf Blaster
9. Pistol
10. Grenade
11. Fire Sprayer
12. Soccer Ball
13. Drink
14. Shotgun
15. Repair Kit
16. Power Grab
17. Rope Suspender
18. Sword

Their current progression/unlock/economy behavior remains part of the Initial Demo.

## 2.3 Paint Buddy

Include the complete current Demo Paint Buddy implementation and release polish, including the already approved/current tools and workflows such as direct body-surface painting, undo/erase/reset, color controls, current Spray/Airbrush, Curved Line and Pen support, semantic toolbar icons, current mirror/backside/expanded-limb behavior where implemented, safe 512×512 per-part persistence, save/use/restart behavior, and performance fixes.

This remains Buddy-specific appearance painting. It is not the later generic Creator Studio sprite painter.

## 2.4 Buddy Studio

Include the current Demo-authorized Buddy Studio surface and content:

- Face
- Hair
- Brows/Eyebrows
- Eyes
- Nose
- Mouth
- Ears
- Glasses
- Headwear

Retain current character creation/selection, ownership where applicable, fitting/position controls, color controls, deterministic Randomize, preview/equip/save/restart behavior and Work-glasses integration.

Current Tops, Shoes and Accessories remain held back from the Initial Demo unless the owner later promotes them.

Player-drawn cosmetics and the larger My Creations/Shared Studio workflow are not Initial Demo scope.

## 2.5 Environment

Initial Demo includes:

- one local room/environment;
- Paint Background;
- the current background-paint persistence and editing experience;
- current wallpaper/background behavior only where already part of the normal Demo without requiring the Room Decorator workspace.

**Room Decorator is not in the Initial Steam Demo. It is introduced in the Steam Next Fest Demo.**

Any older roadmap wording that calls Room Decorator an Initial/normal Demo feature is superseded by this owner decision.

## 2.6 Work Mode

Include the current Work Mode typing companion, privacy-safe action counting, current-session/lifetime counters, transparent compact companion presentation, current Buddy appearance/cosmetics, Work rewards/milestones, Work glasses reward, drag/window persistence, return-to-Play behavior, crash-safe progress and release polish for DPI/multi-monitor/audio/reward clarity.

## 2.7 Tutorial and presentation

Include the first-session Steam tutorial for the Initial Demo feature set plus current Win98-style presentation and the approved expressive-dialogue polish as it lands. Do not teach Next Fest-only systems in this build.

## 2.8 Steam platform and Workshop v1

Include the Steam release foundation: Steam bootstrap/fallback, local/cloud boundary, reproducible SteamPipe/export flow, installed/offline/clean-install behavior and diagnostics/recovery required by the current release program.

Initial Demo Workshop package types remain:

1. **Room Painting** — painted background package.
2. **Buddy** — Buddy Studio configuration + declared Buddy paint surfaces.

Workshop content is hostile data. V1 remains data-only, explicitly applied by the player, safely copied locally for offline use, and strictly validated. Never load Workshop `.tscn`, `.tres`, `.res`, script, DLL, native code, shader, arbitrary mesh or other executable/resource content.

## 2.9 Achievements

**The new 24-achievement system is not part of the Initial Steam Demo. It moves to the Steam Next Fest Demo for now.**

Do not merge the old achievement branch merely to get it into the Initial Demo.

---

# 3. Steam Next Fest Demo — Initial Demo plus the new vertical slice

The Next Fest Demo contains every Initial Demo feature above and adds the first real slice of the broader creative/RP/systemic direction.

The intended player journey is approximately:

```text
bring several created Buddies into a Scene
→ decorate and switch between Scenes
→ build/edit a physical contraption
→ change useful properties
→ connect simple devices
→ damage/repair structures or a Buddy
→ save/share a Blueprint
→ paint and create a custom item
→ experiment with a small set of familiar/funny status effects
```

## 3.1 Room Decorator

Room Decorator is introduced here.

It includes the already developed/approved environment decoration concepts: authored categories/content, permanent ownership/storage, wallpaper/decorations, free placement/editing/rotation where supported and persistence.

Under the new architecture, environment state becomes **Scene-owned** rather than remaining one global room layout.

Full Release broadens the catalogue and adds selected functional furniture; Next Fest does not need every planned furniture interaction.

## 3.2 Achievements

Port/reconcile the approved 24-achievement baseline into current `main` for Next Fest. The existing `steam-achievements-baseline` branch is substantially diverged and must be ported/re-audited rather than blindly merged.

The baseline names are:

1. First Impression
2. Lights Out
3. Retail Therapy
4. Full Toybox
5. Best Friends
6. Forgiven
7. Nice Catch
8. Variety Hour
9. Fire Drill
10. Desktop Shift
11. Air Bud
12. Bank Shot
13. Character Arc
14. Employee of the Day
15. Employee of the Week
16. Employee of the Month
17. Employee of the Year
18. Employee for Life
19. Make It Yours
20. Punching Bag
21. Fully Dressed
22. Home Sweet Home
23. Try Everything Once
24. Rube Goldberg Would Be Proud

Next Fest Demo behavior:

- evaluate qualification locally;
- persist qualification/counters in player progress;
- do **not** unlock Steam achievements under Demo AppID `5228990`;
- carry qualifications into Full Release;
- Full Release reconciles carried qualified achievements to the full-game Steam definitions under AppID `5114950`;
- qualified achievement state remains monotonic under the approved reset semantics while partial working counters can reset.

## 3.3 Multi-Buddy

Next Fest supports **as many Buddies in an active Scene as the player has created/owns**, not an artificial two-Buddy product cap.

This is a product entitlement rule, not a promise of infinite physics. The runtime may enforce measured emergency safety limits to prevent a pathological configuration from crashing, but such limits are engineering safeguards and should be as unobtrusive as practical.

Each Buddy has independent:

- stable identity;
- Character appearance and Buddy paint binding;
- mood;
- fullness/hunger;
- harmful-memory/trust state;
- traits/preferences/fun/novelty;
- autonomy/recovery/reactions;
- knockout/damage state;
- later structural integrity where applicable.

The player has one shared account wallet/tool inventory/progression ledger. Shared tools/pointer/grab can target any active Buddy.

Initial multi-Buddy deliberately does **not** include:

- Buddy-to-Buddy conversations;
- friendships/rivalries/relationship scores;
- coordinated activities;
- hugging/fighting/care between Buddies;
- social AI;
- intentional Buddy-to-Buddy collision/avoidance.

Current Buddy collision semantics may allow separate Buddies to overlap/pass through one another. That is preferable to accidentally creating an unplanned social/combat simulation.

Work Mode focuses one selected Buddy and suspends normal Play simulation for the rest.

## 3.4 Scenes

Next Fest supports **up to 10 named Scenes**.

A Scene is a complete RP/sandbox setup and owns:

- name;
- painted background;
- wallpaper/decorations;
- Buddy roster;
- safe Buddy placement anchors;
- systemic sandbox/contraption state;
- supported room-physics state;
- references to compatible Blueprint/Creator content used in it.

Required player operations:

- create;
- switch;
- rename;
- duplicate;
- delete with confirmation;
- add/remove/place Buddies;
- navigate tab overflow/library cleanly as the count grows.

Use a Win98-styled Scene strip such as:

```text
[ Home ] [ Garage ] [ Lab ] [ + ]
```

Only one Scene simulates at a time. Inactive Scenes are persisted/suspended documents, not hidden live physics worlds. Inactive Scenes do not walk, hunger, take damage, fire events, run projectiles or advance other gameplay simulation.

## 3.5 Build/Edit foundation

Ship a real systemic Build/Edit workspace with:

- select;
- move;
- rotate;
- freeze/unfreeze;
- duplicate;
- delete;
- Properties;
- Pause/Play or equivalent safe edit/simulation transition;
- clear selection/Escape;
- Win98-appropriate context actions.

Editor undo/redo is desirable if stable for the slice. Broad grouping, clipboard/marquee and advanced editor QoL may wait for Full Release.

## 3.6 Construction slice

Ship a small representative construction vocabulary:

- Wood Beam;
- Metal Block/Plate;
- Wheel;
- optional simple Wood Block only if it materially improves building usability.

This should be enough for recognizable carts, pendulums/hanging constructions, simple traps/launchers and weapon-trigger contraptions.

## 3.7 Constraints

Ship:

- Rope / World Anchor;
- Weld;
- passive Hinge.

Reuse/generalize the Rope Suspender concepts where practical rather than creating unrelated rope semantics.

Do not make Next Fest depend on Spring, Slider, motors, gears or pulleys.

## 3.8 Properties

Common object properties:

- Mass;
- Bounce;
- Gravity Scale;
- Frozen.

Weapon-property showcase on Shotgun:

- Fire Rate / cadence;
- Spread;
- Knockback.

All properties are stable typed/bounded semantic descriptors. Never expose arbitrary Godot/CLR property names or reflection. First-party canonical definitions remain immutable; per-instance variants store validated semantic overrides.

Do not expose unconstrained correctness-sensitive values such as arbitrary projectile speed where tunneling/simulation safety would become a problem.

Modified items may not multiply reward/economy payouts beyond a safe canonical envelope.

## 3.9 Devices and signals

Next Fest device vocabulary:

- Push Button;
- Timer;
- Piston;
- Weapon Trigger Adapter;
- Signal Lamp.

Required example chains:

```text
Button -> Lamp
Button -> Piston
Button -> Timer -> Piston
Button -> Weapon Trigger -> Pistol/Shotgun
```

Use a deterministic, project-owned two-phase event/signal model with budgets. Avoid immediate recursive signal execution.

## 3.10 Materials and destruction

Next Fest requires:

- Wood;
- Metal;
- bounded durability where necessary;
- one reliable/authored Wood breakage path.

Full heat/electricity/cutting/fracture breadth is not a Next Fest requirement.

## 3.11 Buddy structural damage and repair

Add per-part semantic structural integrity with states such as:

```text
Healthy
Damaged
Critical
```

Keep structural integrity distinct from pain, mood and gore.

Next Fest does **not** physically detach limbs.

Recovery:

- Repair Kit can restore structural state under authored rules;
- a free `System Restore` safety route prevents persistent soft-locks.

## 3.12 Room Physics

Expose the small high-value gravity experiment:

- Normal Gravity;
- Low Gravity;
- Zero Gravity.

Broader room simulation controls belong to Full Release only if useful and safe.

## 3.13 Blueprints

Blueprints are mandatory Next Fest functionality.

A Blueprint is a declarative selected contraption graph containing compatible semantic entities, transforms, validated property overrides, constraints and wires/devices.

There is **no artificial five-slot product cap**. Local Blueprint count is limited only by practical storage/UI/safety budgets.

Blueprint load/spawn validates the graph just like hostile shared content. A Blueprint cannot contain executable code, arbitrary Godot resources, arbitrary scene paths, NodePaths or transient runtime object IDs.

Start local-first. Enable Blueprint Workshop sharing only after the local schema/validator is stable.

## 3.14 Creator Studio Lite

Creator Studio Lite is a Next Fest differentiator.

Ship four beginner templates:

1. **Simple Prop**
2. **Gun**
3. **Sword / Sharp Melee**
4. **Explosive**

Beginner flow:

```text
New Item
→ choose template
→ name it
→ Paint sprite
→ set bounded Properties
→ place finite semantic markers
→ Test Spawn
→ Save Locally
→ Validate
→ optional Publish when Workshop support is enabled
```

### Prop

- paint/draw sprite in-game;
- trusted bounded collision shape/hull;
- Mass/Bounce/Gravity/Frozen defaults;
- compatible material choice where exposed;
- Test Spawn.

### Gun

- paint/draw sprite;
- semantic Handle/Grip and Muzzle markers;
- bounded cadence/spread/recoil/knockback and trusted projectile/effect choices;
- Test Spawn/Test Fire.

### Sword / Sharp Melee

- paint/draw sprite;
- Grip marker;
- finite Blade/Sharp region;
- bounded impact/pierce/sharp-contact semantics;
- no arbitrary hit scripts.

### Explosive

- paint/draw sprite;
- bounded fuse;
- bounded blast radius/force;
- trusted damage/effect profile;
- optional authored ignition capability where allowed;
- strict spawn/fragment/performance budgets.

Reuse/generalize low-level Paint Buddy raster/history/palette/save mechanics where useful, but do not reuse Buddy body-mapping assumptions.

User pixels are presentation data, never executable metadata. Physics collision is generated/selected through trusted bounded code rather than inferred into arbitrary unsafe geometry.

Creator Studio Lite does **not** expose arbitrary behavior scripting, Godot scenes/resources, C#/GDScript/DLLs, shaders, arbitrary 3D meshes, filesystem/network/process access or a general behavior graph.

Once local formats/validators are stable, Next Fest Workshop may add:

3. Blueprint / Contraption
4. Creator Item Lite packages for Prop/Gun/Sword/Explosive

Compatibility is derived by validation rather than trusted uploader tags.

## 3.15 Lightweight status effects

Desktop Buddy intentionally does **not** implement People Playground-style physiology, circulation or fluid chemistry—not in Next Fest and not in Full Release.

Use one small semantic effect layer with concepts such as:

```text
EffectDefinition
- EffectId
- duration/lifetime policy
- stacking policy
- optional periodic action
- modifiers into existing trusted gameplay systems
- visual/audio/reaction presentation
- clean removal/recovery rule
```

Effects reuse existing damage, knockout, active-puppet drive, gravity, bounce/restitution, recovery, structural integrity, reactions and VFX.

Explicitly do not build:

- circulation;
- oxygen/blood chemistry;
- nervous-system simulation;
- muscle simulation;
- organs;
- reagent concentrations;
- fluid mixtures/recipes;
- pumps/tubing/liquid networks;
- chemistry reaction graphs.

### Next Fest effect set

Keep the catalogue familiar and small:

- **Burning** — existing system; do not rewrite it just to fit the abstraction unless clearly beneficial.
- **Poisoned** — periodic trusted damage + sick reaction, possibly modest activity reduction.
- **Frozen / Chilled** — strongly reduced active drive/stiffened feel while preserving safe recovery.
- **Stunned / Shocked** — short movement/control interruption/flinch or ragdoll state.
- **Sleep / Knockout** — requests bounded temporary sleep/KO through existing consciousness/recovery behavior rather than a new health model.
- **Regeneration** — bounded gradual repair/healing with anti-reward-exploit policy.
- **Float** — unusual/fun effect giving the individual Buddy a temporary low/zero-gravity style state.
- **Rubberized / Bouncy** — unusual/fun effect increasing bounce/restitution and optionally altering safe blunt-impact response.

These effects are useful additions, but they must not delay the more important Next Fest pillars: Scenes, multi-Buddy, construction, Blueprints and Creator Studio Lite.

Use simple authored delivery (for example an injector/projectile rider/gas/spray/grenade) only where it can reuse existing systems cleanly. Creator items may choose only from whitelisted compatible effects.

---

# 4. Full Release — Next Fest plus broad creative/RP sandbox

Full Release contains everything above, removes artificial Demo product caps and expands the same architecture rather than creating parallel systems.

## 4.1 Buddies and Scenes scale with the player's PC

Full Release supports **as many simultaneous Buddies as the player's PC can safely support** rather than a fixed four-Buddy product cap.

Likewise, remove the Next Fest 10-Scene product limit. Saved Scene count should be constrained by practical storage/UI/safety considerations rather than an arbitrary entitlement cap. Since inactive Scenes are not simulated, large saved libraries should primarily be an I/O/library-management concern.

Use measured performance budgets, escalating stress tests, warnings or an emergency hard ceiling only where necessary to prevent a crash or unusable simulation.

## 4.2 Build/Edit breadth

Expand with useful proven editor features such as:

- multi-select;
- marquee;
- grouping where useful;
- copy/paste;
- broader undo/redo for editor commands;
- grid/snap controls;
- richer selection/property workflows;
- scalable Blueprint library management.

Editor undo does not imply reversing the live physics simulation.

## 4.3 Construction and constraints breadth

Expand the first-party catalogue with additional beams/blocks/plates/platforms, wheel/axle pieces, glass/rubber/other useful structural families, counterweights/hazards/mechanisms and other parts that add new verbs rather than item-count padding.

Constraint candidates include:

- Spring;
- Slider;
- motorized Hinge;
- breakaway joints;
- Gear/Pulley/linkage primitives only if useful/stable.

Retain project-owned routed semantics rather than an unrestricted native-joint soup or custom whole-world physics solver.

## 4.4 Property breadth

Expand the typed descriptor registry with appropriate bounded values such as:

- Friction;
- Linear Damping;
- Angular Damping;
- Durability;
- Break Threshold;
- safe collision/impact modifiers;
- wider weapon/tool settings;
- Grenade fuse/blast tuning;
- Fire Sprayer tuning;
- ball mass/bounce/launch tuning;
- rope length/stiffness/damping where supported;
- material/device settings.

Correctness-sensitive values remain bounded or unexposed.

## 4.5 Devices/automation breadth

Candidate reusable devices include:

- Toggle Switch;
- Pressure Plate;
- Proximity Sensor;
- AND / OR / NOT / XOR;
- Delay;
- Latch / Memory;
- Fan;
- Electromagnet;
- Heater;
- Motor;
- Conveyor;
- Door/actuator adapters;
- additional trusted weapon/tool adapters.

Keep deterministic event/action budgets for both first-party and declarative UGC behavior.

## 4.6 Materials/destruction breadth

Expand toward useful material behavior such as Wood, Metal, Glass, Rubber and additional approved families, with broader durability/breakage, heat/fire response/spread, conductivity/electricity and cutting/fracture only where implemented safely and performantly.

Do not promise every candidate effect merely because the architecture can represent it.

## 4.7 Deeper Buddy structural system

Full Release may add physical limb detachment/reattachment and detached-part interaction **only after** the rig, active drive, recovery, presentation, paint, targeting, persistence and System Restore paths explicitly support an absent limb safely.

If that refactor is not safe/polished, physical detachment is not a launch requirement merely because structural integrity exists.

## 4.8 Room Physics breadth

Expand beyond the gravity trio only when useful: richer gravity, simulation-speed controls that do not slow UI/platform callbacks, wind/forces, environmental heat/fire or similar bounded room-level experiment settings.

Do not expose arbitrary engine physics settings.

## 4.9 Full Creator Studio

Expand Creator Studio into a first-class Win98 application.

Target safe templates include:

- Simple Prop;
- Sharp/Melee;
- Gun;
- Projectile;
- Explosive;
- Construction Part;
- Signal Device;
- Material Variant;
- Decorative Item;
- additional templates only when they compile through trusted capabilities.

Suggested workspace pages:

```text
General | Paint | Physics | Function | Ports | Test | Publish
```

Full Creator capabilities may include:

- sprite painting/editing;
- a small local source layer stack/editor metadata;
- semantic markers and typed signal ports;
- typed Properties;
- approved capability selection;
- **Advanced Behavior** event/condition/action graph;
- isolated Test Spawn / hot-test loop;
- validator diagnostics and visible budgets;
- local content packs;
- dependency/version handling;
- generated preview/publish flow;
- Workshop publication for approved declarative package types.

The behavior graph invokes only trusted semantic capabilities with typed sockets. It never exposes arbitrary `Node`, `RID`, CLR objects, reflection, filesystem, process, environment, clipboard, shell, native, network or Steam-management APIs.

### Creator presentation

Custom painted 2D items need a trusted 3D presentation seam. Default toward a trusted textured card/thin primitive where it looks acceptable. Automatic silhouette extrusion may be explored only as capped R&D with deterministic contour extraction/simplification, hard vertex limits and failure fallback; it is not a promise.

Presentation geometry and authoritative physics collision remain separate.

### Dogfooding

Where practical, first-party simple items/devices/material variants should use the same semantic definition/capability vocabulary exposed to safe Creator content. Bespoke core behavior remains allowed where necessary, but exact-definition-ID special cases should be questioned.

## 4.10 Full status-effect pool

Full Release can add additional familiar or playful effects when each produces an interesting toy/interaction. Candidate pool, **not a promised checklist**:

- Slow;
- Haste / Hyper;
- Strength;
- Weakness / Vulnerable;
- Numb / Pain Resistance;
- Confused / Erratic;
- Curse / reduced healing;
- Shielded;
- Leech / Drain;
- Heavy;
- Lightweight;
- Magnetized if implementation is cheap/stable;
- Volatile / Explosive;
- Slippery;
- Sticky;
- Ghost/non-collision only if physics/recovery safety is proven.

Still no chemistry, circulation, organs, liquid mixing, tubing or PPG-style physiology.

## 4.11 Potion Shop

Potion Shop remains a Full Release feature. It can use the lightweight effect layer for appropriate temporary effects rather than creating a second status/effect architecture.

Retain the approved direction toward a small polished initial effect set, clear duration/stacking/restart/reset/accessibility/economy rules and no mutation of Paint Buddy/Buddy Studio documents.

## 4.12 Environment and functional furniture

Full Release expands Scene-owned Environment content with a larger catalogue and selected authored Buddy/furniture interactions such as sit/rest/watch/toggle/use. Furniture behavior uses trusted authored capabilities and safe recovery if the item moves/disappears while in use.

Complete Scene/room sharing through Workshop occurs only after the local Scene format is stable.

## 4.13 Buddy Studio expansion

Full Release expands/unlocks:

- Tops;
- Shoes;
- a stronger finished Accessories category;
- player-drawn safe cosmetic templates for approved categories;
- local custom-cosmetic library;
- bounded anchored cosmetic stretching/deformation;
- safe Workshop sharing/import;
- scalable Browse / Equip / My Creations / Create-Edit / Shared UX.

Cosmetics remain visual-only to Buddy physics.

## 4.14 Interactive accessories/gadgets

Full Release may add project-authored interactive accessories such as a phone/handheld toy, idle gadgets or safe mood/Work/environment-reactive behavior. Any passive income must be calibrated against the normal economy/Work Mode and may not stack uncontrollably.

## 4.15 Player voice recordings

Full Release may add optional local microphone recordings assigned to authored Buddy reaction categories, with bounded storage, preview, normalization and an original optional goofy voice filter.

Voice content remains local/private by default. Steam Workshop sharing of microphone recordings is not part of the current launch plan.

## 4.16 Contextual office helper

Full Release may augment teaching for expanded systems with an original office-themed helper (for example an animated pen). It must be clean-room/original and not copy Clippy art, wording, animation or presentation.

---

# 5. Persistence and migration architecture

The current single-Buddy save model must be split before production multi-Buddy.

## 5.1 Account state

Introduce/facade toward a `PlayerProgressState` owning global/account semantics such as:

- balance;
- permanent ownership/unlocks;
- selected tool;
- global lifetime statistics;
- Work/run statistics;
- achievement state/counters where appropriate.

There is one account state per save.

## 5.2 Buddy identity state

Introduce `BuddyIdentityState` owning one Buddy's persistent semantics:

- stable `BuddyIdentityId`;
- Character/appearance reference;
- mood;
- fullness/hunger;
- harmful memory/trust history;
- traits/preferences;
- fun/novelty;
- later structural state if persisted.

A Buddy identity is not the same thing as its live ragdoll actor.

## 5.3 Placement

Use semantic Scene placement data such as:

```text
BuddyPlacement
  PlacementId
  BuddyIdentityId
  CanonicalX
  CanonicalY
```

Do not persist six-body limb transforms/velocities. Restore a safe authored pose from a placement anchor.

Initially prevent the same `BuddyIdentityId` from appearing twice in one Scene; clones/twins require separate identities.

## 5.4 Suggested storage boundaries

```text
user://progress.json
user://buddy-identities/<buddy-id>.json
user://characters/...
user://scenes/index.json
user://scenes/<scene-id>/scene.json
user://scenes/<scene-id>/environment/background.png
user://scenes/<scene-id>/sandbox.json
```

Keep large/mutable Scene/sandbox data out of the account-progress aggregate.

## 5.5 Initial Demo -> Next Fest migration

On first Next Fest load of an Initial Demo save:

1. create one default Scene;
2. migrate the current environment/background atomically into the Scene root;
3. create one Buddy identity from the current persistent Buddy state;
4. associate the existing active Character appearance;
5. create one safe Buddy placement;
6. preserve wallet, unlocks, statistics and Work data;
7. preserve any existing compatible platform/progress data;
8. begin using local achievement state only after the Next Fest achievement feature is present;
9. mark migration complete only after the new documents commit successfully;
10. leave the old valid save recoverable if migration fails.

## 5.6 Next Fest -> Full Release

Preserve Scene IDs, Buddy IDs, Blueprint IDs, Creator definitions and compatible declarative data. Full Release should widen availability/caps rather than rewrite valid Next Fest content unnecessarily.

## 5.7 Downgrade safety

A richer Full Release save seen by an older Demo executable must not be destructively stripped merely because that build cannot activate some content. Review Steam Cloud/path policy before all build variants can write mutually visible files.

---

# 6. Runtime/systemic architecture

## 6.1 One routed fixed tick

Keep 2D physics authoritative with 3D presentation. Do not add a gameplay `_PhysicsProcess` to every new Buddy, object, device or constraint.

Use one routed authoritative physics tick. For the systemic sandbox, the intended seam remains conceptually:

```text
SystemicSandboxBootstrap       // composition/registration
SystemicSandboxCoordinator     // deterministic routed PhysicsTick
SandboxRoot                    // optional subsystem seam; not a god object
```

Multi-Buddy actors tick in deterministic stable Scene order, not incidental scene-tree discovery order.

## 6.2 Multi-Buddy runtime host

A focused Scene runtime host should own several per-Buddy actor runtimes. Components that read/mutate a specific Buddy must not remain globally singular.

Shared room/player services remain shared; per-Buddy damage/reaction/appearance/paint/persistent bindings belong to that Buddy actor.

Click/grab/hit ownership resolves the correct Buddy. Physical tools do not require UI focus to interact with another Buddy.

## 6.3 Scene switching

Safe switch sequence:

1. resolve incompatible modal/editor state;
2. capture safe Buddy anchors;
3. persist dirty committed Scene state;
4. tear down active Scene runtime;
5. switch environment;
6. instantiate target Buddy roster/systemic content;
7. apply appearance/paint;
8. restore input;
9. activate target Scene/tab.

Inactive Scenes never keep hidden gameplay simulation alive.

## 6.4 Systemic entity model

Use engine-free semantic definitions/state/registries for new systemic entities rather than reusing `LooseObjectBody` as a universal construction entity.

First-party trusted Godot `.tres` definitions may compile into engine-free semantic definitions. Hostile Workshop files never go through `ResourceLoader`.

Old numeric entity/constraint/device budgets from earlier planning are useful **stress/profiling starting points**, not current product entitlement caps. Re-profile them against current main and target hardware.

## 6.5 Constraint model

Use project-owned routed constraint semantics for the new sandbox. Do not introduce a custom whole-world solver or broad dependence on native `PinJoint2D` motor behavior.

## 6.6 Sandbox persistence

Sandbox state is Scene-owned and separately versioned. Persist semantic IDs, transforms, approved overrides, constraints/wires and stable authored state. Do not persist arbitrary NodePaths, RIDs, scripts/scenes, arbitrary engine properties or transient velocities as durable content identity.

---

# 7. Safe moddability and Workshop architecture

The guiding rule is:

> Give creators many safe gameplay capabilities to compose, not host-machine privileges.

## 7.1 One semantic model, two trust paths

```text
trusted internal .tres
    -> trusted compiler
    -> engine-free definition

Workshop JSON + approved assets
    -> hostile validator/compiler
    -> same engine-free definition model
```

Workshop content is never loaded as Godot Resource/Scene code.

## 7.2 Stable identities

Use provider-qualified semantic IDs, conceptually:

```text
core:construction/wood_beam
core:device/button
ugc:<pack-guid>/entity/my_launcher
```

Do not use Resource paths, NodePaths, CLR class names, filenames, Steam display names or Workshop PublishedFileId as gameplay identity. Workshop IDs are provenance only.

## 7.3 Capability exposure

Capabilities can be exposed as:

```text
CoreOnly
LocalDeclarative
WorkshopDeclarative
```

Default new capabilities to `CoreOnly` until hostile-input/performance review says otherwise.

Example safe capabilities include physics body, painted visual, durability/breakage, sharp contact, flammability/heat response, projectile/explosion/force emitters, repair, signal I/O, timer/piston/weapon trigger, constraint anchor, audio/particle/spawn emitters, Buddy damage/care and the lightweight `ApplyEffect` capability.

Avoid gameplay switches that special-case exact Definition IDs when a reusable capability is appropriate.

## 7.4 Safe behavior graph

Full Creator Studio behavior graphs use finite trusted events/conditions/actions.

Example event families:

- OnSpawn
- OnUse
- OnSignal
- OnTimer
- OnCollision
- OnImpactThreshold
- OnBreak/Destroyed
- OnIgnited/Extinguished
- OnBuddyContact
- OnProjectileHit
- OnPropertyChanged

Example actions:

- EmitSignal
- ApplyImpulse
- ApplyDamage
- Repair
- Ignite/Extinguish
- ApplyEffect
- Set bounded state/property
- Spawn approved definition
- PlaySound/Particle
- StartTimer
- DestroySelf

No immediate recursive cycles; cycles cross explicit timer/next-tick/cooldown boundaries. Enforce budgets for nodes/events/actions/spawns/timers/signal hops.

## 7.5 Package shape and hostile validation

Conceptual safe package:

```text
manifest.json
content/entities.json
content/materials.json
content/behaviors.json
assets/textures/...
assets/audio/...
preview.png
```

Start with tightly capped safe raster/audio formats. Reject unsupported/executable surfaces such as `.tscn`, `.tres`, `.res`, PCK, scripts, DLLs, native libraries, shaders, SVG/XML/HTML, arbitrary fonts, arbitrary 3D models and video unless a later explicit security review adds a safe path.

Validation path:

```text
Steam mutable folder
→ immutable project-owned snapshot
→ inventory paths
→ reject links/reparse/traversal/absolute paths
→ extension/byte/hash checks
→ bounded manifest/schema/dependency/ID/capability/behavior validation
→ derive build compatibility
→ compile engine-free definitions
→ project-owned validated package store
→ explicit enable/use
```

Never execute/read live gameplay content directly from the mutable Steam cache.

Changed package bytes create a new hostile snapshot and are validated from scratch by content hash. Trust is not inherited from the uploader or previous version. If a new version is invalid, retain the last-known-good validated version where possible.

Steam tags are advisory, not a security boundary.

## 7.6 Runtime budgets and forbidden powers

Budget entities/bodies/constraints/wires/devices/graph actions/spawns/timers/particles/audio/textures/decoded memory/string/dependency/JSON complexity.

Ordinary Workshop content cannot access reflection, arbitrary CLR/Godot instantiation, assemblies, ResourceLoader/PCKs, arbitrary files/writes, process launch, environment variables, clipboard/shell/native APIs, HTTP/network, Steam subscription/publish management, other Workshop items, wallet/unlocks/achievements or arbitrary save mutation.

Missing content degrades into a Missing Content placeholder while preserving the semantic record so reinstall can restore it.

## 7.7 Trust tiers

Current direction:

- **Tier A** — safe declarative UGC: paintings, Buddy configs, Blueprints, Scenes, presets, items/materials/devices where approved.
- **Tier B** — declarative behavior packs through the trusted behavior graph/interpreter.
- **Tier C** — future advanced scripted mods only through a separate explicit trust/security design, if ever pursued.

`.NET AssemblyLoadContext` is **not** a security sandbox. Arbitrary C#/GDScript downloaded from ordinary Workshop is not a Full Release launch feature. Capability-restricted WebAssembly is only future research, not a dependency.

---

# 8. Implementation sequence

## Phase 0 — Initial Steam Demo RC

1. finish current user-testing/release polish;
2. finish current Steam/platform/Workshop/cloud/export/release gates;
3. keep Room Decorator out of this build;
4. keep the new achievement system out of this build;
5. lock build-scope regression tests for Initial Demo;
6. ship/stabilize Initial Steam Demo.

Do not begin broad systemic features in a way that destabilizes the Initial Demo release candidate.

## Phase 1 — Next Fest foundations

1. add explicit `IsSteamDemo` / `IsNextFestDemo` / `IsFullRelease` policy and a separate Next Fest export preset;
2. add build-scope truth-table tests for Initial / Next Fest / Full / itch / untagged fallback;
3. introduce provider-qualified stable IDs, registries and capability/exposure seams needed by systemic/Creator/UGC work;
4. split/facade account state from Buddy identity state with deterministic migration;
5. productionize multi-Buddy runtime;
6. implement Scene documents/runtime/tabs and Initial Demo migration;
7. make Paint Background/Environment/Room Decorator Scene-owned and expose Room Decorator in Next Fest;
8. port/re-audit the 24-achievement baseline against current main;
9. implement systemic entity registry and Build/Edit;
10. implement Next Fest parts/constraints/Properties;
11. implement devices/signals and gravity controls;
12. implement Wood/Metal breakage and Buddy structural integrity/repair;
13. implement local Blueprints;
14. implement Creator Studio Lite: Prop, Gun, Sword, Explosive;
15. add the lightweight status-effect seam and selected Next Fest effects where stable;
16. add Blueprint/Creator Workshop package types only after their local validators are stable;
17. run performance/UX/marketing/RC gates for Next Fest.

Status effects are secondary to Scenes, multi-Buddy, construction, Blueprints and Creator Studio Lite; do not let optional effect polish hold the event build hostage.

## Phase 2 — Full Release breadth

On the same architecture:

- remove artificial demo product caps where hardware/storage permits;
- broaden Scene/cast management;
- broaden editor/construction/constraints/properties/devices/materials;
- deepen structural/destruction features only where safe;
- build full Creator Studio and behavior graph/content packs;
- broaden safe Workshop ecosystem;
- expand Environment/functional furniture;
- expand Buddy Studio/custom cosmetics;
- add Potion Shop;
- add selected additional status effects;
- add interactive accessories/gadgets;
- add local voice personalization;
- add original contextual helper if retained;
- recalibrate full-game economy/progression;
- run migration/performance/accessibility/content/full RC pass.

---

# 9. Verification and performance gates

## 9.1 Build matrix

Continuously prove:

```text
Initial Demo
    baseline only; no Next Fest-only UI/content

Next Fest
    every Initial Demo journey
    + Next Fest systems

Full Release
    every prior feature
    + full breadth

itch.io
    remains its separately reduced/compile-stripped distribution
```

A missing build tag must fail closed toward less content rather than accidentally shipping Full/Next Fest content.

## 9.2 Multi-Buddy/performance

Escalating stress tests should grow Buddy count rather than assuming a fixed product cap. Measure CPU physics, allocation, memory, paint/texture residency, presentation cost and recovery under increasing active Buddy count.

Old systemic stress scenarios remain useful starting shapes, e.g. multiple Buddies with dynamic entities, entity+constraint saturation, device/wire saturation, fire plus several Buddies, repeated Scene switching and repeated Blueprint place/delete. Their historical numeric budgets are profiling targets rather than release entitlements.

## 9.3 Persistence

Test:

- Initial -> Next Fest migration;
- Next Fest -> Full migration;
- failed/partial migration recovery;
- Scene create/duplicate/delete/switch/restart;
- missing Character/UGC references;
- Full save opened by older build without destructive stripping;
- Steam Cloud/path interactions across build variants.

## 9.4 Creator/Workshop hostile input

Test malformed/traversal/link/oversized/dependency-cycle/schema/capability/budget attacks, changed-content revalidation, last-known-good fallback, missing-content placeholders and explicit-enable behavior.

Creator Test Spawn must use the same compiler/definition path as normal gameplay rather than editor-only callbacks that bypass validation.

## 9.5 Status effects

Test stacking/refresh/removal, restart/Scene switch/repair/recovery, no stuck frozen/KO/physics states, deterministic expiration and no healing/reward exploit.

## 9.6 Windows/release

Retain Windows 10/11, DPI, multi-monitor, window recovery, offline Steam, clean install/reinstall, active/Work/hidden soak, accessibility/readability/audio and performance matrices appropriate to each RC.

---

# 10. Explicitly outside the current three-build launch plan

Unless later explicitly promoted:

- real-time multiplayer/shared live rooms;
- Steam lobbies/P2P/RPC gameplay;
- Buddy-to-Buddy social AI, relationships, conversations or coordinated behavior;
- unrestricted executable Workshop mods;
- arbitrary user-authored Godot scenes/resources/scripts/shaders/native libraries/meshes;
- arbitrary full-trust C# downloaded from ordinary Workshop;
- Steam sharing of microphone recordings;
- Linux/macOS launch support;
- People Playground-style circulation/oxygen/organs/chemistry/liquid mixtures/pumps/tubing;
- physical limb detachment unless the required absent-limb refactor proves safe enough for Full Release.

---

# 11. Source provenance and Astra audit lineage

The following source documents remain preserved for evidence, research and detailed implementation context. Their contents are not rewritten merely because later decisions supersede parts of them.

Key systemic planning lineage:

1. `docs/FULL_RELEASE_SYSTEMIC_SANDBOX_IMPLEMENTATION_PLAN.md`
   - original detailed systemic-sandbox implementation plan;
   - this is the plan corresponding to the local/Astra audit discussion.
2. `docs/FULL_RELEASE_SYSTEMIC_SANDBOX_CODEBASE_AUDIT_2026-09-07.md`
   - follow-up codebase audit of that implementation plan against the repository/main state;
   - treat it as audit findings/corrections for the plan, not as a second competing master product plan.
3. `docs/FULL_RELEASE_MULTI_BUDDY_SCENES_SOURCE_ALIGNMENT_2026-09-07.md`
   - source audit/planning for multi-Buddy and Scene architecture.
4. `docs/NEXT_FEST_DEMO_SYSTEMIC_SANDBOX_VERTICAL_SLICE_2026-09-07.md`
   - first Next Fest vertical-slice draft; historical where superseded.
5. `docs/NEXT_FEST_THREE_BUILD_SCOPE_SOURCE_ALIGNMENT_2026-09-07.md`
   - established the cumulative three-build model and corrected removal of existing Demo content.
6. `docs/MODDABILITY_AND_WORKSHOP_SECURITY_SOURCE_ALIGNMENT_2026-09-07.md`
   - moddability/security research and safe semantic-content direction.
7. `docs/IN_GAME_CREATOR_STUDIO_RESEARCH_AND_PLAN_2026-09-07.md`
   - Creator Studio research and detailed authoring workflow.

Later owner correction documents on this branch:

8. `docs/INITIAL_DEMO_NEXT_FEST_FULL_RELEASE_SCOPE_2026-09-08.md`
   - first consolidation draft; historical where corrected later.
9. `docs/THREE_BUILD_SCOPE_OWNER_LOCK_2026-09-08.md`
   - corrected Room Decorator, achievement, Buddy/Scene and Creator Lite decisions.
10. `docs/STATUS_EFFECTS_SCOPE_CORRECTION_2026-09-08.md`
    - rejected the over-engineered chemistry/physiology interpretation.
11. `docs/STATUS_EFFECTS_OWNER_LOCK_2026-09-08.md`
    - locked the lightweight RPG/MaD2-style effect direction and Next Fest effect set.

Existing project sources that continue to provide implemented-demo/platform detail include current `docs/ROADMAP.md`, `docs/M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md`, the Steam/GodotSteam plans, Buddy Studio/Paint/Environment implementation documents, and the owner-approved achievement baseline on `steam-achievements-baseline` pending port/re-audit.

## Astra naming note

There is no pushed branch/commit/file metadata in GitHub explicitly naming `Astra`, so GitHub alone cannot prove which local tool produced the audit. The repository lineage does make the audited artifact identifiable: **`FULL_RELEASE_SYSTEMIC_SANDBOX_IMPLEMENTATION_PLAN.md` is the input plan, and `FULL_RELEASE_SYSTEMIC_SANDBOX_CODEBASE_AUDIT_2026-09-07.md` is the subsequent repository audit of that plan.**

---

# 12. Superseded decisions — do not resurrect

The following older planning conclusions are explicitly superseded:

- Room Decorator in Initial Steam Demo -> **No; Next Fest onward.**
- 24-achievement system in Initial Steam Demo -> **No; Next Fest onward for now.**
- Next Fest limited to 2 Buddies -> **No; as many created Buddies as the player has, subject only to real safety/performance safeguards.**
- Next Fest limited to 2 Scenes -> **No; up to 10 Scenes.**
- Full Release fixed to 4 Buddies -> **No; scale to the player's PC. Four may remain a useful engineering benchmark, not entitlement cap.**
- Full Release fixed Scene cap -> **No; practical storage/UI/hardware limit only.**
- Next Fest Blueprints limited to 5 local saves -> **No artificial five-slot cap.**
- Creator Studio Lite only Prop + Gun -> **No; Next Fest includes Prop, Gun, Sword/Sharp Melee and Explosive templates.**
- PPG-style chemistry/circulation system in Next Fest or Full -> **No. Never part of the current plan.**
- Status-effect system as a major separate simulation -> **No; lightweight reusable effects that modify existing systems.**
- Next Fest may remove existing Initial Demo tools to curate the toybox -> **No; builds are cumulative.**
- All multi-Buddy/Scene/systemic work is Full-only -> **No; Next Fest receives the vertical slice defined here.**
- `steam_demo` alone means widened Next Fest -> **No; Next Fest has its own `next_fest_demo` feature tag/profile.**

This list exists specifically so future agents do not accidentally revive an older plan after reading a historical source file.
