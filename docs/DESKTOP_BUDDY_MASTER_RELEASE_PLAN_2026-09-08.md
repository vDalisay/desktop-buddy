# Desktop Buddy — Master Release Plan

Status: **SOLE AUTHORITATIVE PLANNING SOURCE**  
Recorded: 2026-09-08  
Branch: `feature/master-release-plan-2026-09-08`
Last implementation-queue update: 2026-09-10
Base lineage: current `main` after `9adfbcb5a3e32aa50fe88da5fe4b772e7ec64eb8`

This document is the single current source of truth for the planned **Initial Steam Demo**, **Steam Next Fest Demo**, and **Full Release** of Desktop Buddy.

It consolidates the latest non-superseded decisions from the systemic-sandbox plan and audit, multi-Buddy/Scene planning, Next Fest planning, Workshop/moddability research, Creator Studio research, current Steam Demo roadmap, achievement baseline, and the owner corrections recorded on 2026-09-08.

## Authority rule

When this document conflicts with another planning, roadmap, audit, source-alignment, owner-lock, status-effect, Creator Studio, moddability, or release-scope document, **this document wins for current product scope, sequencing, and intended architecture**.

Older documents remain valuable research, audit evidence, implementation detail, and historical context. They are intentionally preserved unchanged. Do not edit an older source merely to make it agree with this master plan.

The detailed subsystem documents may still supply implementation detail where this master does not override them. A later explicit owner decision may supersede this master and should then be folded into a new revision of this file rather than creating another competing master plan.

## Active implementation direction

The current implementation queue is in [Phase 1](#current-implementation-queue--owner-steering-2026-09-10). The next deliverable is **NF-1: Scene strip and cast management**. It must end with a player creating, populating, switching and reopening Scenes through normal game controls.

Implementation progress is measured by usable player behavior. Tests, journeys and CI prove that behavior; they are not standalone milestones. Do not add another foundation layer while the next listed player operation can be built with the existing Scene, persistence, runtime and semantic-content seams.

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

## Phase 1 — Next Fest implementation

The build-scope policy/preset, semantic IDs/capability registry, split progress model, migration/transaction recovery and Scene document/runtime foundations already exist on this branch. Keep them stable and use them to deliver the remaining player-facing work:

1. ship Scene and cast controls over the existing Scene runtime;
2. make every active Buddy independently targetable, customizable and selectable for Work;
3. ship Build/Edit with the first construction parts, constraints and bounded Properties;
4. ship devices/signals and per-Scene gravity controls;
5. ship Wood/Metal durability plus Buddy structural damage and recovery;
6. ship local Blueprint save/library/place;
7. ship Creator Studio Lite with all four promised templates;
8. share stable Blueprint/Creator formats through the existing safe Workshop path;
9. finish achievements, selected lightweight effects and onboarding against the implemented systems;
10. run the RC gates on the resulting playable build.

Status effects remain secondary to Scenes, multi-Buddy, construction, Blueprints and Creator Studio Lite. Do not let optional effect polish hold the event build hostage.

### Current implementation queue — owner steering, 2026-09-10

This queue supersedes the **immediate work ordering** in the historical audits below. The owner asked for concrete functionality implementation rather than continued test/CI expansion. Product scope in sections 2–7 is unchanged. Start the next implementation with **NF-1**; each packet must leave a usable in-game operation, its persistence/failure handling, and the relevant checks together.

Source baseline inspected: local branch `feature/master-release-plan-2026-09-08` at `36f99afc`. This is a source inventory, not a new runtime acceptance claim or a claim about current remote CI.

**Already present; extend rather than rebuild:**

- Build policy/export profiles, semantic IDs/capability registry, split account/Buddy/Work persistence, migration and transaction recovery.
- `SceneProgressCoordinator` exposes Scene create/rename/duplicate/delete and roster operations. `SandboxRoot.SceneRuntime` composes additional actors; `SandboxRoot.SceneSwitch` implements transactional runtime switching; environment/appearance rebind paths exist.
- Switching currently has a verification caller in `ProductionBootstrapJourneyProbe`, but no player-facing Scene strip/cast controller was found. The runtime still rejects an empty roster and retains the first authored actor as a compatibility target.
- The achievement catalogue/adapters and production-bootstrap journey/probe already exist. Their existence does not establish complete attribution, interactive acceptance, or live Steam acceptance.
- No corresponding production Build/Edit, Blueprint or Creator workspace was found in this source inventory.

**Work rule:** finish a playable slice before expanding its infrastructure. Tests are acceptance work inside that slice. Add a CI change only when an existing job cannot run a required check, or to repair a concrete failure/trigger-policy violation. Do not reopen completed persistence fixes or build another general verification framework. Existing failing safety checks must be fixed before the affected functionality is handed off; pending external Steam/Windows release evidence does not prevent local Next Fest feature development.

```text
NF-1 Scenes/cast
  -> NF-2 independent Buddies
      -> NF-3 Build/Edit + construction
          -> NF-4 devices/signals
          -> NF-5 break/repair
          -> NF-6 Blueprints (after NF-4)
          -> NF-7 Creator Prop
              -> NF-8 Creator Gun/Sword/Explosive
NF-6 + NF-8 -> NF-9 Workshop sharing
NF-1 through NF-8 -> NF-10 effects/onboarding/RC
```

After NF-3, NF-4, NF-5 and NF-7 may proceed independently where their touched files do not overlap. NF-6 waits for the device graph it must serialize. NF-9 waits for the local Blueprint and Creator formats it must treat as hostile input.

#### NF-1 — Scene strip and cast management (next)

**Player result:** create a second named Scene, add owned Buddies, place them, switch rooms, and return after restart to the saved cast and room.

- Wire a Win98 Scene strip and compact Scene menu to the existing coordinator and `SwitchSceneAsync`: create, rename, duplicate, confirmed delete, active-tab state and overflow. Apply the existing Next Fest 10-Scene policy; hide the surface in Initial Demo/itch/fallback. Tab reordering remains deferred unless it is effectively free.
- Add the initial cast commands under the Scene menu: **Add Buddy...** and **Remove Focused Buddy**. Add Buddy browses existing Buddy identities and owned Character appearances. It can create/register a new Buddy identity from a chosen Character using the existing new-Buddy defaults, or reuse an existing identity to preserve that Buddy's state across Scenes. It previews the chosen Buddy at the pointer and commits a safe position on click. Reject adding an identity already present in that Scene. Removing a placement must preserve the Buddy identity and local Character/paint library.
- Complete empty-Scene composition so creating a room or removing its last Buddy does not require a hidden replacement Buddy or fail on the compatibility actor. Replace singular assumptions only where this flow reaches them. The authored Buddy scene may remain the implementation source for the first actor, but it must no longer imply that every active Scene contains one.
- Finish duplication of Scene-owned background/decor assets as well as document state. The duplicate keeps the same Buddy identity references but receives new Scene/placement identities and independent mutable room assets. Bind Paint Background and Room Decorator to the active Scene.
- Show busy/save/load failures in the initiating UI; keep the old Scene usable on failure. Resolve active editors through their existing save/discard behavior before switching.
- **Reuse:** `domain/.../Scenes/SceneLibraryState.cs`, `src/Persistence/SceneProgressCoordinator.cs`, existing Scene runtime/switch and Environment rebind code. Keep UI composition focused; do not grow `SandboxRoot` into the Scene library UI.
- **Acceptance:** perform create → add/place → decorate/paint → duplicate → switch → rename/delete → restart from actual controls, including an empty Scene. Changes in one room must not alter the other's copied background/layout. Promote that interaction into the existing journey system and cover transaction failures in existing tests.

##### NF-1 implementation tasks

Implement and hand off these tasks in order. A task is complete only when its named controls work in a normal Next Fest build.

1. **NF-1A — Open and switch.** Add the Scene strip from the existing Play chrome. WHEN the player clicks another Scene tab THEN the game SHALL run the existing safe switch transaction, show that tab as active only after success, and leave the current Scene active if save/load/composition fails. Empty target Scenes SHALL be valid.
2. **NF-1B — Create and name.** Add `+` and Rename with the 1–64 visible-character validation already enforced by `SceneDocument`. WHEN the player creates a Scene THEN the game SHALL add an empty named room without changing another Scene. The new tab SHALL be visible immediately and use the normal tab action for switching. WHEN ten Scenes already exist in Next Fest THEN the game SHALL disable creation and explain the limit.
3. **NF-1C — Create/add, place and remove Buddies.** Add the Scene menu commands, local-library picker and pointer placement preview. WHEN an owned Character has no Buddy identity and the player chooses it as a new Buddy THEN the game SHALL create/register an identity with clean authored Buddy defaults and bind that appearance. WHEN placement is confirmed THEN the game SHALL add the selected identity at a safe canonical anchor and compose it into the active runtime. WHEN the focused Buddy is removed THEN the game SHALL remove only that placement and keep its identity, Character and paint files. Removing the final Buddy SHALL leave a usable empty room. A missing/deleted Character reference SHALL fall back visibly and preserve the unresolved reference for recovery.
4. **NF-1D — Duplicate and delete.** WHEN a Scene is duplicated THEN the game SHALL create a new adjacent Scene with copied room state, copied Buddy references, new Scene/placement IDs and independent mutable background/decor files. WHEN deletion is confirmed THEN the game SHALL delete that Scene, choose the adjacent surviving Scene if needed, and switch safely. The last Scene SHALL not be deletable.
5. **NF-1E — Restore.** WHEN the game restarts THEN it SHALL reopen the committed active Scene with its name, environment and complete cast at safe anchors. WHEN a switch or commit fails THEN it SHALL surface an actionable error and retain the last committed usable Scene.

**NF-1 handoff:** report which controls are usable, which player operation remains unavailable, and which external release evidence remains. Do not report Scene persistence or runtime composition alone as NF-1 completion.

#### NF-2 — Every Buddy is independently usable

**Depends on:** NF-1. **Player result:** interact with any cast member, customize the intended Buddy, and take the selected Buddy into Work Mode.

- Route grab, care, projectiles, melee, explosions, damage/recovery and ropes to the actor that was actually hit. Trace shared tool callers through `SceneRuntimeHost.TryResolveActor`; remove first-actor attribution assumptions in those routes.
- Route Buddy Studio/Paint Buddy selection and save/apply to the intended identity. Keep each actor's appearance, hunger, mood and damage independent.
- Bind Work focus to the selected Buddy; suspend the remaining Play actors and restore the room/input on return. Keep wallet, tool ownership and Work rewards shared.
- Attach/detach achievement observers with actor lifecycle so switching/removing actors neither drops qualifying actions nor duplicates credit. Preserve approved qualification semantics.
- **Acceptance:** with several visually distinct Buddies, hit/feed/customize each, enter Work with a non-first Buddy, return, switch and restart. Verify correct actor state and one shared reward ledger. Use targeted actor-routing coverage plus the playable journey.

#### NF-3 — Build a physical cart

**Depends on:** NF-1–2. **Player result:** enter Build/Edit, place a Wood Beam and Wheels, connect passive Hinges, then press Play and move the cart.

- Implement the Build/Edit entry, selection, move/rotate, freeze/unfreeze, duplicate/delete, Properties, Escape and safe Pause/Play transition.
- Register and render Wood Beam, Metal Block/Plate and Wheel as trusted systemic entities with Scene-owned semantic state and one routed fixed tick.
- Implement Rope/World Anchor, Weld and passive Hinge creation/removal. Show valid endpoints and reject invalid links without leaving partial constraints.
- Expose bounded Mass, Bounce, Gravity Scale and Frozen overrides; restore canonical defaults without mutating shared definitions.
- Persist parts, transforms, overrides and links in the Scene sandbox document; reconstruct them on restart.
- **Acceptance:** build and play the cart plus a hanging beam, edit while paused, duplicate/delete parts, switch Scenes and reload. Check constraint cleanup, invalid overrides and fixed-tick ownership. Advanced grouping/clipboard is Full Release work.

**NF-3 status 2026-09-11.** Usable in game: Build/Edit entry with the room paused; place, select,
drag, rotate, duplicate and delete parts; freeze/unfreeze; Properties for Mass, Bounce, Gravity and
Frozen with Reset; Rope (part–part or part–room), Hinge (where two parts overlap, or a part pinned to
the room) and Weld; links deleted with their parts; hinged and welded assemblies move and turn as one
in Build; parts, transforms, overrides and links saved in the Scene's sandbox document and rebuilt on
restart; rope force on the routed fixed tick. The tagged production journey builds and plays a
two-wheel hinged cart and a beam hanging on a rope, and checks the tuning and a rope survive a real
restart. Deviation from the joint-UX draft in the systemic-sandbox plan: Hinge and Weld are one click
on an overlap (as an axle through a wheel and a beam) rather than two; Rope keeps two clicks.
Parts draw as lit 3D shapes in the frontal presentation (`SandboxPartVisual3D`: chamfered boxes for
beams, blocks and plates, a tyre and hub for the wheel) under the same dark outline the Buddy's
parts have, and flat in the legacy one (scenario `built_part_look`). NF-3T traversal below is done
for steps and piles a hop clears. The Scene-switch pass is done: the journey pins a Wheel to the room, duplicates the Scene,
switches to the copy and back, and checks each room rebuilds its own parts, links and live joints
with none of the outgoing room's left behind.
Known feel issue to tune with the owner: a shoved cart partly slides rather than rolls.

##### NF-3T — Buddies must traverse what the player builds (owner requirement 2026-09-10)

The owner built a floor-wide pile of Wood Beams and found the Buddy standing still against it
instead of crossing it. Traversal was never written down as a requirement, so nothing in NF-3
delivers it; this sub-packet records the requirement and what the investigation found.

**Requirement.** WHEN a Buddy's committed walk meets built parts between it and its goal THEN it
SHALL climb or step over them and continue, rather than treating the structure as a wall.

**Why it does not work today** (traced 2026-09-10, no code changed):

1. `SandboxPartBody` is a plain `RigidBody2D` on the loose-object layer. `AutonomousMotionComponent`
   probes that layer already — `collision_mask = 4` on `LeftObstacleCast`/`RightObstacleCast` — so
   the parts *are* seen. This is not a sensing gap.
2. `ObstacleInCommittedPath` excludes only soccer balls and consumables, so every built part reads
   as an obstacle. The walk goal aborts, `ObstructedTicks` accumulates, and at
   `Profile.ObstacleGiveUpTicks` the planner turns the Buddy around. That is the "just stands
   still" the owner saw.
3. The one existing way past an obstacle is the obstacle hop, and it is trait-gated at
   `BehaviorArbiterProfile.HopPropensityThreshold = 35` of a uniform 0–100 — roughly a third of
   Buddies can never hop anything (DECISIONS 2026-07-20, "too random"). Even a Buddy that can hop
   gets one impulse, which clears a single low object, not a stack.
4. Locomotion has no ground model beyond a rectangle: `AutonomousMotionComponent.SetWalkableBounds`
   takes a `Rect2` and uses it for wall clearance only. There is no notion of standing on anything
   but the room floor, so a Buddy that did get on top of the pile has nothing holding it there and
   `RecoveryComponent` returns it to its safe pose — the respawning the owner described.

**Status 2026-09-11.** Point 4 is fixed: `PuppetPartBody` now accepts a `SandboxPartBody` as foot
support (commit `4ab8f10d`), so a Buddy standing on a built floor stands, balances and is no longer
reset by recovery. Points 2 and 3 remain: a built part in the committed walk still reads as a wall,
and the only way over it is the trait-gated single hop, so piles and steps are still impassable. The
remaining work below is about getting *onto* and *across* structures, not standing on them.

**What this needs.** A ground model rather than a floor line: a downward probe per foot, a step-up
height budget, and `RecoveryComponent` accepting a resting surface above the floor as valid footing.
The trait gate stays for the *decorative* hop; traversal must not be trait-gated, or a third of the
cast could never leave a room they built. Sizing it as its own packet is deliberate — it touches
locomotion, recovery and the arbiter, all of which are shared with the Initial Demo surface.

**Status 2026-09-11, points 2 and 3.** Fixed without the ground model: a built part is now a
*blocking* obstacle (`AutonomousMotionComponent.BlockingObstacleInCommittedPath`), the same class
as a dropped tool, so every Buddy hops it whatever its trait — the arbiter's existing untrait-gated
hop. A room-interest walk no longer abandons its errand at a part it will hop. The feet reach about
110 px at the top of a hop, so steps and piles well under that are crossed (the new
`built_part_traversal` scenario: a zero-trait Buddy crosses a two-beam stack in one or two hops on
seeds 7 and 11, and turns around without the fix). Anything taller still reads as a wall and the
give-up timer turns the Buddy around, which is the intended behaviour for a real wall. The step-up
ground model stays unbuilt until a structure the hop cannot handle is reported.

**Ordering.** Not a blocker for the rest of NF-3 (placement, links, Properties, persistence), but it
must land before NF-3 can be reported complete: a room the player can build but not walk through
does not satisfy "build a physical cart" in spirit.

#### NF-4 — Wire a working machine

**Depends on:** NF-3. **Player result:** place devices and wire Button → Lamp, Button → Piston, Button → Timer → Piston and Button → Weapon Trigger → Pistol/Shotgun.

- Implement visible device placement, typed ports, wire creation/removal and signal-state feedback.
- Route the two-phase signal queue, timer scheduling, piston motion and weapon adapter through the existing authoritative tick, with bounded work and cleanup on deletion.
- Add Shotgun cadence/spread/knockback overrides through typed Properties, preserving the canonical reward envelope.
- Add Scene-owned Normal/Low/Zero Gravity controls and restore them when switching rooms.
- **Acceptance:** operate all four chains, remove a live connection/device, switch/reload the machine, and reject invalid connections/cycle overload safely. No general behavior-graph editor is required for this packet.

**NF-4 status 2026-09-11.** Split into packets. **4A done (domain only):** Button, Timer, Piston,
Weapon Trigger and Lamp are core part definitions with authored `in`/`out` pulse ports; wires live
in the Scene's sandbox document (schema 3, at most 192, cut with their devices, copied on Scene
duplication, bad ones dropped on load); `SandboxSignalNetwork` is the two-phase engine — pulses due
this tick are gathered first, then delivered in stable part order, a Timer only ever schedules so a
loop runs across ticks instead of recursing, and a tick is capped at 256 deliveries and a Timer at
16 pulses in flight. Every Next Fest port carries a pulse, so there are no value kinds yet.
**4B done:** Button, Timer and Lamp are in the Build palette with drawn faces (red cap, clock, bulb
that glows when lit); Build has a Wire tool (key 5: click a sender, then a receiver; right-click a
wire cuts it) and wires draw as arrowed green lines; a click on a Button in Play presses it and is
not also taken by the held tool; `SandboxRoot` ticks the network on the routed tick and re-reads the
document whenever its revision changes. The journey wires Button → Timer → Lamp, sees the Lamp light
one second after the press, and cuts the wire with a pulse in flight.
**4C done:** the Piston is in the palette. A pulse throws its head out of its top face (it turns with
the part) and every unfrozen body in front of it — parts, loose objects, Buddy parts — gets the same
push speed whatever it weighs; the head stays out a quarter second and ignores pulses meanwhile, and
an unfrozen piston is pushed back by what it pushes (nail it to make it a wall). Push speed, hold
time and reach are constants in `SandboxRoot.Devices.cs`, left for owner feel tuning. The journey
adds Button → Piston with a Metal Block on the head and checks the block is thrown.
Next: Weapon Trigger, then the gravity presets with Shotgun overrides.

#### NF-5 — Break and repair

**Depends on:** NF-3; integrate weapon-device damage from NF-4 when available. **Player result:** break an authored Wood structure, damage a Buddy part, and restore the Buddy with Repair Kit or free System Restore.

- Add Wood/Metal material behavior, bounded durability and one authored Wood breakage path with correct constraint cleanup and bounded debris.
- Add per-part Healthy/Damaged/Critical integrity feedback separately from pain, mood and gore. Integrate existing damage sources and approved Repair Kit rules.
- Implement free System Restore and safe persistence/recovery of structural state. No physical limb detachment in Next Fest.
- **Acceptance:** damage → break/repair → save/switch/restart leaves no dangling links, stuck Buddy state or repeatable reward exploit.

#### NF-6 — Save and reuse a Blueprint

**Depends on:** NF-3–4; preserve NF-5 state only where the durable schema permits it. **Player result:** select a contraption, name/save it, find it in a local library and place another copy in a different Scene.

- Add the selection flow needed to capture a compatible subgraph, including an explicit handling/diagnostic path for links outside the selection.
- Serialize semantic definitions, relative transforms, overrides, internal constraints and wires. Validate on both save and spawn; remap instance identities on placement.
- Implement local library browsing, placement preview/confirm, and actionable missing/invalid-content messages. Keep local count free of a five-slot cap.
- **Acceptance:** save the NF-4 timer machine, restart, place two independent copies and operate both. A malformed or missing-dependency graph must not partially spawn or corrupt the Scene.

#### NF-7 — Creator Lite: paint and spawn a Prop

**Depends on:** NF-3 and the local semantic compiler. **Player result:** New Item → Simple Prop → name → Paint → Properties → Test Spawn → Save Locally → use after restart.

- Build the actual Creator workspace and local item library. Reuse raster/history/palette mechanics without Buddy body mapping.
- Compile a bounded trusted collision shape and typed physics/material defaults; render pixels through the trusted item presentation seam.
- Make Test Spawn and normal placement use the same validated definition path. Surface validation errors in the editor and preserve the working copy on failed save.
- **Acceptance:** draw an asymmetric Prop, change its mass/bounce, test it, save/reopen and place it in a Scene. Reject invalid dimensions/bytes/properties without creating runtime content.

#### NF-8 — Creator Lite: Gun, Sword and Explosive

**Depends on:** NF-7 and applicable weapon/damage seams from NF-4–5. **Player result:** create and actually use one item from each remaining beginner template.

- **Gun:** Grip/Muzzle markers, bounded cadence/spread/recoil/knockback, trusted projectile/effect choice and Test Fire.
- **Sword:** Grip plus finite Blade/Sharp region, bounded sharp-contact behavior and usable melee interaction.
- **Explosive:** bounded fuse/radius/force, trusted damage/effect profile and budgeted detonation.
- Add template-specific marker editing, diagnostics and local save/reopen; use shared semantic capabilities in both tests and normal play.
- **Acceptance:** paint, configure, test, save, restart and use all three items; invalid markers and values fail before spawn. Prop-only completion does not satisfy Creator Lite.

#### NF-9 — Share the new creations

**Depends on:** NF-6 and NF-8 stable local schemas/validators. **Player result:** publish a Blueprint/Creator item, import a validated local copy, and explicitly place/use it offline.

- Define exact versioned package whitelists and compatibility for these approved types; connect preview/staging/publish and download/import UI to the existing Workshop service.
- Snapshot and validate hostile bytes, preserve provenance separately, and retain last-known-good content when updates fail. Show missing/incompatible content with an actionable diagnosis.
- **Acceptance:** exercise publish/import with the directory emulator, explicit placement and offline restart; add hostile-input checks for the new formats. Record actual live Steam/two-account publication as an external release gate until performed.

#### NF-10 — Secondary effects and Next Fest completion

**Depends on:** the playable NF-1–8 pillars; NF-9 remains the sharing track.

- Integrate the section 3.15 effect set through existing damage/drive/gravity/recovery systems, with visible duration/removal feedback and a trusted compatible delivery route. Retain existing Burning; do not rewrite it for uniformity.
- Connect the completed systems to concise Next Fest teaching: create a Scene/cast, build a machine, save a Blueprint and author an item. Initial Demo teaching remains scoped to its own features.
- Finish outstanding achievement qualification/attribution against the approved 24-rule baseline, including actual new-system actions. Resolve any unclear baseline rule from owner-authorized sources before implementing it.
- **Acceptance:** one first-session journey reaches the major creative operations without debug commands; effects expire/recover without stuck state. Secondary effects must not postpone the core creative pillars.
- Run the existing full RC/migration/performance/accessibility checks against the resulting build. Treat this as release acceptance of implemented functionality, not another feature-infrastructure phase.

#### How to report remaining work

For each NF packet report **usable behavior implemented**, **functional gaps still open**, and **verification/external gates** separately. A domain type, test count or green workflow alone does not complete a player feature. Name the next missing in-game action in the handoff. Do not default back to adding tests/CI when the next functional packet is ready to implement.

### Historical progress audit (2026-09-09)

The following audit and merge/CI observations describe their named historical heads. They are retained as evidence, not the current implementation queue; some stated gaps have since received source implementations as recorded above.

Audited implementation head: `0db908b49699d61c4717e9b05bbb52ef8608dbd6` on `feature/master-release-plan-2026-09-08`, after fetching origin. The branch contains 188 commits across 132 changed files relative to `origin/main`. This is an implementation-progress audit, not player acceptance or a complete line-by-line correctness review.

| Plan area | Evidence and current status |
|---|---|
| Phase 0 Initial Demo RC | Remains a separate release gate; foundation code does not establish installed Windows, live Workshop or cross-machine Cloud acceptance. |
| Phase 1 steps 1–3 | Build-scope policy, Next Fest preset, truth-table tests and semantic ID/capability registry are implemented. Physical achievement exclusion/inclusion also passes the four-profile build check at the audited head. Semantic registries are foundations, not a completed systemic runtime. |
| Step 4 split persistence | Implemented with production manifest-first boot, account/Buddy/Work bindings, migration, transaction recovery and reset coverage. The four findings below have source fixes and managed regression coverage; production Godot migration/restart acceptance remains open. |
| Steps 5–6 multi-Buddy/Scenes | Partial. Documents, library operations, bindings and ordered actor host exist. `Bootstrap.SceneProgress` still requires `BuddyIdentityId.LegacyPrimary`; `SandboxRoot.SceneRuntime` resolves its reserved placement and constructs exactly one actor. Production cast creation/removal, general roster restoration, Scene tabs and switching are not complete. |
| Step 7 Scene environment | Scene-owned storage, paint migration, selection/reset/rebind seams and Next Fest Room Decorator gating are implemented. Actual cross-Scene runtime switching and restart still require integration acceptance. |
| Step 8 achievements | The 24-rule catalogue, split-state qualification, monotonic reset, Full-only publishing and usage/customization adapters are implemented. Port/re-audit remains incomplete: new runtime observer adapters lack direct integration tests, and attribution still assumes the primary Buddy. |
| Steps 9–17 | No completion evidence in this branch for Build/Edit, construction, devices, structural repair, Blueprints, Creator Lite, effects or expanded Workshop packages. Keep them pending. |

**Verification actually observed:** [CI run 34318934403](https://github.com/vDalisay/desktop-buddy/actions/runs/34318934403) built the solution, passed all **1,753 managed tests** and the binary guard. [Achievement Build Scope run 34318934405](https://github.com/vDalisay/desktop-buddy/actions/runs/34318934405) passed. `CI / build-test` was skipped because this was a push. There is no open PR for this branch, and no new Godot scenarios/journeys accompany its production Scene or achievement wiring. This audit did not run interactive or live Steam checks.

**Historical next-work recommendation (superseded by the 2026-09-10 queue):**

1. Close the runtime verification gap: add/run Godot journeys through production Bootstrap for Initial save migration, committed restart, malformed/incomplete generation refusal/recovery, purchases/Work/character/background persistence and transactional reset. Prove Initial Demo refuses upgraded saves without modifying semantic files. Exercise the Initial and Next Fest build tags explicitly; untagged single-Buddy fixtures cannot prove these routes.
2. Finish the planned Scene/cast vertical slice before extending achievements or starting Build/Edit. Remove the reserved-primary assumptions through actual roster composition and target ownership; verify independent actors, shared wallet, inactive-Scene suspension, Work focus, environment rebind and repeated switching/restart. Do not count managed library operations as shipped Scene UI.
3. Cover achievement adapters through real successful/failed/unchanged actions, restart/reset, asynchronous character selection changes and observer teardown. Audit customization qualification against the approved baseline, including color/placement-only edits and imported appearances; `HasStudioCustomization` currently compares feature IDs only. Keep Demo Steam unlock prohibition and qualified-state monotonicity covered. Do not silently choose new qualification semantics.
4. Restore the documented CI trigger split: the new `achievement-build-scope.yml` currently adds a four-build matrix to every push. Keep this matrix on PR/manual triggers; retain the existing quick push job. This is workflow steering, not permission to weaken build-scope verification.
5. Keep Steamworks Auto-Cloud row publication and real cross-machine interrupted-generation recovery external until exercised. Source whitelist tests do not prove Partner configuration was published.

**Merge recommendation:** extract the contiguous foundation prefix `fa6a9174` (build scope/preset) and `4da49f82` (semantic IDs/capabilities) as the first small PR. These nine files are separable from activating split persistence and achievements. They are review-ready candidates, not yet merge-approved: run the applicable full PR gates on the extracted head and verify its Initial Demo scope before merging. Keep the later persistence/runtime/achievement work together until its integration gaps above close; do not merge the entire branch based only on push checks.

The separate open [PR #60, “Consolidate distribution hardening and NativeAOT compatibility work”](https://github.com/vDalisay/desktop-buddy/pull/60), at `5e56cdfd`, has green build-test, Asset Forge and native Steam smoke, but its **NativeAOT Initial Steam Demo export check fails**. Hold that merge pending a passing export/smoke result on its final head. It is not evidence that this branch's head passed those gates.

### Historical finding detail — Scene persistence audit follow-up (2026-09-08)

**SOURCE FIXES IMPLEMENTED; RUNTIME ACCEPTANCE OPEN — superseded in status by the 2026-09-09 audit above.** Preserve the original findings and acceptance criteria below as the verification checklist. Fixes include lazy manifest-first production bootstrap; Cloud-eligible committed `.next` bytes plus simulated Cloud-copy recovery tests; explicit backend selection for nullable extensions in both bindings; and fresh-generation dirty tracking with failure/retry coverage. Do not reimplement these fixes. Complete their production journey gate.

Evidence: the audit examined `9094f33e`; the follow-up was refreshed after fetching and pulling origin through `ac46db25`. That newer head adds `SceneProgressBootstrapCoordinator`, split-safe `RunContext` bindings, and managed bootstrap/compatibility tests. Reuse those additions rather than introducing another coordinator. Production `Bootstrap` still constructs the legacy store directly. Recheck the latest head before editing because implementation is ongoing.

1. **[P1] Wire save-format selection before any legacy decode or write.**
   - Concern: `src/App/Bootstrap.cs` constructs `JsonProgressStore` directly, bypassing `LegacyProgressCompatibilityStore`. A targeted check confirmed `ProgressSavePolicy.Decode` accepts serialized account-only `PlayerProgressSave` as valid legacy data. Subsequent legacy writes can overwrite the account document and invalidate the committed Scene graph.
   - Proposed fix: inspect the Scene manifest before loading legacy semantic progress. For Scene-enabled builds, use the existing `SceneProgressBootstrapCoordinator` to load the committed graph or migrate genuine legacy data. Do not first decode a committed account document as legacy merely to satisfy the coordinator's legacy argument; adjust that API minimally if needed. For Initial Demo/itch/fallback, inject `LegacyProgressCompatibilityStore` and preserve the existing safe refusal behavior. Route the resulting account, Work, actor bindings and saves through the existing split seams, with no parallel legacy writer for the same files. Keep settings independent.
   - Acceptance: production-bootstrap coverage proves committed Scene data bypasses legacy decode; Initial Demo refuses the richer format without modifying any semantic file; a genuine Initial Demo save migrates once and restarts with wallet, ownership, Work, identity and Scene state intact. Include an incomplete-promotion generation and a malformed manifest; neither may fall through to destructive legacy recovery.

2. **[P1] Make the Cloud save generation self-contained after interrupted promotion.**
   - Concern: `SteamCloudSavePolicy` includes `scene-progress.commit.json` but excludes `.next`. `SceneProgressTransactionStore` intentionally reports a committed generation as successful even when its newest document bytes remain only in `.next`. Copying only Cloud-eligible files then leaves another machine with a manifest whose hashes cannot be resolved.
   - Proposed fix: keep one transaction format and make every document needed by its committed manifest available through the Cloud boundary, including recovery bytes when canonical promotion is incomplete. Evaluate the smallest safe whitelist/configuration change against actual Auto-Cloud matching; do not assume an in-process eligibility predicate filters Steam's files. Alternatively, synchronize an explicitly durable, complete generation. A clean-exit promotion alone is insufficient because crashes must also recover. In the new bootstrap coordinator, do not report `CanonicalPromotionComplete: true` merely because loading succeeded: loading can resolve staged bytes without promoting them.
   - Acceptance: extend the existing injected promotion-failure test into a simulated Cloud copy to an empty destination, then load the exact committed account/Work/Buddy/Scene values there. Cover interruption after manifest commit and midway through promotion, and verify configured Cloud rows include the required recovery paths while excluding unrelated temporary/quarantined content. Keep live Steam Cloud cross-machine validation explicitly external until run.

3. **[P2] Preserve null extensions in both split progress bindings.**
   - Concern: `PlayerRuntimeProgressBinding.Extensions` and `BuddyRuntimeProgressBinding.Extensions` use `?? RequireLegacy()` to select the backend. A valid split player with null extensions therefore throws `InvalidOperationException`; character-slot entitlement reads are one affected consumer.
   - Proposed fix: select the backend by whether the split binding exists, not whether its nullable `Extensions` value exists. Return null unchanged. Apply the same correction to both bindings; no new abstraction is needed.
   - Acceptance: tests read null extensions through both split bindings without throwing, then set/read an extension successfully. Retain legacy behavior and check character-slot capacity with no extension present.

4. **[P2] Persist a fresh Scene coordinator's initial generation.**
   - Concern: the `SceneProgressCoordinator` constructor initializes saved revisions to current revisions even when `committedRevision == -1`. A targeted check confirmed `FlushAsync(force: true)` on a fresh coordinator with an empty store creates no manifest.
   - Proposed fix: treat an uncommitted generation as dirty until its first successful commit, using the existing committed-revision state where possible. Already-loaded or successfully migrated generations should remain clean. A failed initial commit must remain retryable.
   - Acceptance: force-flush a fresh unchanged graph and reload it; verify failure/retry; verify an unchanged loaded generation does not generate redundant commits. The new bootstrap migration normally commits before constructing its coordinator, but that does not fix the public fresh-coordinator contract.

**Handoff gate:** record fixes and regression results against the implementing head, run the complete managed suite and solution build, then run the relevant Godot bootstrap/migration/restart journeys for the production wiring. The original audit passed 1,691 managed tests and built with 12 CA2255 warnings; those results predate the refreshed origin head and are not verification of these fixes. No interactive or live Cloud acceptance was claimed. Do not mark Scene/multi-Buddy completion from managed seams alone: the current host still wraps the existing single Buddy, and cast UI, tabs, switching and Scene-owned environment remain subsequent integration work.

## Phase 2 — Full Release breadth

Implement these as extensions of the playable Next Fest packets. Each row names a concrete operation and inherits the relevant persistence, validation and interactive/automated acceptance requirements above. Candidate features in section 4 remain candidates; this breakdown does not silently promote them to launch requirements.

| Order / packet | Concrete implementation deliverable | Acceptance example / dependency |
|---|---|---|
| FR-1 — Larger Scene/cast libraries | Remove the Next Fest 10-Scene entitlement restriction in Full; make overflow/library navigation usable at larger counts while retaining measured runtime safety limits. | Create an eleventh Scene, locate/switch it, reopen older Next Fest content, and add owned Buddies without a fixed four-Buddy cap. Extends NF-1–2. |
| FR-2 — Faster building | Add multi-select/marquee, copy/paste, grid/snap and editor-command undo/redo; add grouping where it improves those operations. | Select a machine, copy it, snap it into place, undo/delete/redo without altering the original or trying to rewind live physics. Extends NF-3/6. |
| FR-3 — Useful construction breadth | Deliver selected section 4.3–4.6 parts/constraints/materials/devices as complete place → configure → operate → save flows. Choose an authored mechanism for each addition; do not implement every candidate as a generic registry exercise. | A chosen Spring/Slider/motor or sensor must enable a working mechanism that the Next Fest vocabulary cannot express, and survive Blueprint save/reload. Specific candidate selection is still pending; do not invent a mandatory catalogue. |
| FR-4 — Full Creator authoring | Extend the existing workspace with approved additional templates, typed ports, finite event/condition/action editing, diagnostics and a safe test loop. Add local packs and dependency/version handling around working items. | Author a signal-driven item with a timed action, test/save it, reuse it from a pack and receive useful diagnostics for a missing dependency or over-budget graph. Extends NF-7–8; no scripting/runtime privileges. |
| FR-5 — Scene and pack sharing | Add validated complete-Scene/content-pack export/import and explicit local activation after the formats above stabilize. | Share a furnished Scene containing cast references and compatible creations; import offline, show missing-content placeholders, and restore references when dependencies become available. Extends NF-9; live Steam remains a separate acceptance gate. |
| FR-6 — Functional furniture | Expand the Scene-owned catalogue with selected authored sit/rest/watch/toggle/use interactions. | Place and use a chosen furniture item, then move/remove it while occupied; Buddy safely recovers and the layout survives restart. Exact furniture selection follows approved content definitions. |
| FR-7 — Full Buddy Studio | Expose finished Tops/Shoes/Accessories and implement approved painted cosmetic templates, local library, anchored deformation and validated sharing through Browse/Equip/My Creations/Create-Edit/Shared flows. | Create a cosmetic, equip it on the intended Buddy, reopen/edit it and import a shared copy while preserving visual-only physics separation. |
| FR-8 — Potion Shop | Connect a small approved effect catalogue to purchase/use UI, clear durations/stacking, effect removal/recovery and the shared economy. | Buy/use an approved potion, observe its effect and expiration, restart/reset safely, and verify no repeated reward exploit or appearance-document mutation. Extends NF-10; use approved values rather than inventing prices/durations. |
| FR-9 — Full progression and onboarding | Reconcile unlock/pricing/reward rules with the expanded playable catalogue, explain the new workflows, and preserve qualified achievements into Full Steam reconciliation. | Upgrade a Next Fest save, retain creations/progress, use the new systems and reconcile qualifying achievements under the full AppID without duplicate rewards. Balance values require the documented tuning/owner process. |

**Conditional follow-ons, not blockers for the packets above:**

- Physical detachment: only after every absent-limb path listed in section 4.7 is supported; the deliverable is detach → interact → reattach/System Restore → restart safely.
- Additional effects/gadgets: select a useful approved interaction first, then implement its delivery, feedback and clean removal; no catalogue-padding framework.
- Local voice: if retained, record → preview/normalize → assign an authored reaction → hear it → delete it, with bounded private local storage and no Workshop microphone upload.
- Original contextual helper: if retained, teach a concrete expanded workflow and allow dismissal; avoid adding a separate helper platform.
- Broader room physics: if retained, expose a bounded useful control, save it per Scene, and restore normal simulation without slowing UI/platform callbacks.

Full RC closes migration, performance, accessibility, content and installed/live-platform evidence for these implemented operations. It does not replace their implementation or turn the optional candidates into launch promises.

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
