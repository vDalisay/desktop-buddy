# Desktop Buddy — Initial Demo / Next Fest / Full Release Scope

Status: **Draft for owner lock — consolidated after code/document audits**  
Recorded: 2026-09-08  
Base: `main` at `9adfbcb5a3e32aa50fe88da5fe4b772e7ec64eb8`  
Purpose: one authoritative product-scope matrix for the three Steam builds.

This document consolidates the current `main` implementation, the already-approved Normal Steam Demo roadmap, the owner-approved achievement baseline, and the later Scene/systemic-sandbox/moddability planning into one release split.

It supersedes older planning language where that language treats the Next Fest demo as a replacement/re-curation of the Initial Demo or treats all new Scene/systemic work as Full-Release-only.

---

## 1. Non-negotiable inheritance rule

Desktop Buddy has three cumulative Steam content builds:

```text
INITIAL STEAM DEMO
        ↓ adds new systems
STEAM NEXT FEST DEMO UPDATE
        ↓ adds breadth/depth
FULL RELEASE
```

Formally:

```text
Initial Steam Demo ⊂ Next Fest Demo ⊂ Full Release
```

The Next Fest build **never removes an Initial Demo feature merely to make its catalogue smaller**.

The Full Release **never removes a Demo feature merely because a larger replacement exists**, unless the owner separately approves an intentional redesign/migration.

The itch.io build is a separate reduced distribution and is not one of these three Steam scopes.

---

# 2. Build identities

Recommended Godot feature tags:

```text
Initial Steam Demo
    steam,steam_demo

Steam Next Fest Demo
    steam,steam_demo,next_fest_demo

Full Release
    steam,full_release
```

The Initial Demo and Next Fest Demo are two source/build profiles of the Steam Demo product. The Next Fest build is expected to become the public update for Demo AppID `5228990`; retaining an Initial Demo export profile is for reproducibility/regression testing, not a requirement to expose two public Demo products simultaneously.

Full Release uses the full-game AppID `5114950`.

`steam` means platform capability. It must never be used as a content-entitlement flag.

---

# 3. Initial Steam Demo — complete baseline

The Initial Steam Demo is **the current Steam-demo gameplay in `main` plus already-approved Normal Demo work that is not yet merged**.

No new People Playground / Mutilate-a-Doll-inspired construction system is required here.

## 3.1 Core Buddy / sandbox gameplay

Initial Demo includes:

- one live persistent Buddy;
- the existing six-body ragdoll/active-puppet physics;
- autonomous movement/recovery;
- grab resistance and elastic-limb behavior;
- mood/trust/fear/care reactions;
- hunger/fullness and food/drink care behavior;
- personality/preferences/fun/novelty;
- knockout/recovery;
- existing damage/pain attribution and economy;
- loose-object physics and current safety budgets;
- current Win98 Play/compact/fullscreen/window behavior;
- opt-in Gore Mode on Steam under the existing policy.

No multiple-simultaneous-Buddy runtime and no Scene library are exposed in the Initial Demo.

## 3.2 Initial Demo tool / interaction catalogue

Keep the complete current Normal Demo interaction toybox. Do not re-curate it for Next Fest.

Current launch interactions/tools are:

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

Their current progression/unlock/economy behavior remains part of Initial Demo scope.

## 3.3 Paint Buddy

Include the complete current Demo Paint Buddy implementation and its release polish:

- direct body-surface painting;
- current brush/tool set;
- Undo/Erase/reset flows;
- color selection/palettes;
- current Spray/Airbrush, Curved Line, Pen and semantic toolbar icons where already present;
- current mirror/backside/expanded-limb behavior where already approved/implemented;
- safe 512×512 per-part persistence;
- save/use/restart behavior;
- current performance/polish fixes.

This is ordinary Buddy customization, not the later generic item/mod sprite painter.

## 3.4 Buddy Studio

Initial Demo includes the current Demo-authorized Buddy Studio categories and content:

- Face
- Hair
- Brows
- Eyes
- Nose
- Mouth
- Ears
- Glasses
- Headwear

and the current:

- create/select local Buddy character documents;
- permanent ownership/purchase where applicable;
- fitting/position controls;
- color controls;
- deterministic Randomize;
- preview/equip/save/restart behavior;
- Work-glasses integration.

The existing static **Tops, Shoes and Accessories categories remain held back from the Initial Demo** under the current `DemoScope` policy unless separately promoted.

Player-drawn cosmetics and the larger Creator/My Creations/Shared Studio redesign are not Initial Demo scope.

## 3.5 Environment / room customization

Initial Demo product intent includes **one local room/environment** with:

- Paint Background;
- wallpaper;
- Room Decorator;
- permanent decoration ownership/storage;
- existing authored decoration categories/content;
- free placement/editing/rotation where currently supported;
- environment persistence.

Important audit finding: current `docs/ROADMAP.md` explicitly calls Room Decorator part of the Steam-demo Environment baseline, while current `DemoScope.IncludesRoomDecorator` still returns `IsFullRelease`. Treat this as a **scope implementation inconsistency to fix before Initial Demo RC**, not as the intended product rule.

Initial Demo does not have multiple named Scenes/rooms.

## 3.6 Work Mode

Initial Demo includes the current Work Mode typing companion:

- privacy-safe keyboard/mouse/action counting;
- current-session and lifetime counters;
- compact transparent companion presentation;
- current active Buddy appearance/cosmetics;
- Work milestones/rewards;
- Work glasses reward;
- drag/resize/window persistence;
- double-click/exit return to Play;
- crash-safe work progress;
- release polish for DPI/multi-monitor/audio/mute/reward clarity.

## 3.7 Tutorial / presentation

Initial Demo includes:

- complete Steam first-session tutorial for the Initial Demo feature set;
- Win98-style tutorial/dialog presentation;
- the approved playful text-presentation polish as it lands (typed text, restrained emphasis/effects, short speech-like SFX) without changing tutorial scope;
- normal release UI/SFX/VFX/accessibility/readability polish.

The later contextual office-helper character/system is not required for Initial Demo.

## 3.8 Steam platform / Workshop v1

Initial Demo includes the normal Steam release foundation:

- Steam bootstrap/fallback;
- local/cloud save boundary;
- stats/platform integration where approved;
- reproducible SteamPipe/export flow;
- installed/offline/clean-install validation;
- tray/recovery/launch behavior appropriate to the desktop-companion product.

Workshop v1 Initial Demo package types remain:

1. **Room Painting** — current painted background package.
2. **Buddy** — Buddy Studio configuration + declared Buddy paint surfaces.

Rules remain:

- data only;
- immutable incoming staging;
- exact path/type/hash/size validation;
- no auto-apply;
- imported local copies work offline;
- no `.tscn`, `.tres`, script, DLL, native code, shader or arbitrary executable content.

No contraption/Blueprint/content-pack Workshop package is required for Initial Demo.

## 3.9 Achievements

The already owner-approved 24-achievement baseline is part of the Initial Demo release program once merged.

Initial Demo behavior:

- evaluate the same conditions locally;
- persist qualifications/counters in progress;
- **do not unlock Steam achievements under the Demo AppID**;
- carry qualified achievements forward so the Full Release can reconcile them to the full game's Steam achievements.

Partial counters reset under the approved Reset Progress semantics; already-qualified achievement state remains durable.

## 3.10 Explicitly absent from Initial Demo

Do not expose:

- multiple simultaneous Buddies;
- named Scene tabs;
- systemic construction parts;
- general object Properties editor for the new systemic sandbox;
- Weld/Hinge joint framework;
- signal/device graph;
- systemic material/destruction framework;
- persistent Buddy structural integrity system;
- local contraption Blueprints;
- generic Creator Studio/mod item authoring;
- Potion Shop;
- player-drawn Buddy Studio cosmetics;
- interactive furniture/accessories;
- full Scene/contraption/content-pack Workshop sharing.

---

# 4. Steam Next Fest Demo — Initial Demo + vertical slice

The Next Fest Demo contains **every Initial Demo feature above**, then adds a compact but real vertical slice of the new Full Release direction.

The objective is for a new player to be able to:

```text
create/use two distinct Buddies
→ place them in a named Scene
→ switch to another Scene
→ build a simple physical contraption
→ change meaningful properties
→ connect a few devices
→ damage/repair a Buddy or structure
→ save it as a Blueprint
→ create/paint a simple custom item
→ share/use safe Demo-compatible UGC
```

## 4.1 Multi-Buddy RP slice

Next Fest Demo adds:

- maximum **2 active Buddies in one Scene**;
- distinct Buddy identities;
- distinct character appearance + Buddy paint per Buddy;
- independent mood/hunger/memory/personality/fun state;
- independent autonomy/recovery/reactions/knockout;
- shared player tools can target either Buddy;
- shared Grab resolves the owning Buddy;
- one focused Buddy for focus-dependent UI.

Not included:

- conversations;
- relationship scores;
- hugging/fighting/coordinated activities;
- Buddy-to-Buddy AI;
- intentional Buddy-to-Buddy collision/avoidance.

Initial Buddies may pass through/overlap one another if the existing Buddy-part collision policy remains unchanged.

## 4.2 Scene tabs / RP slice

Next Fest Demo adds maximum **2 named Scenes**.

Each Scene owns:

- name;
- painted background;
- wallpaper/decorations;
- Buddy roster;
- safe Buddy placement anchors;
- systemic sandbox graph;
- supported room-physics state.

Required actions:

- create;
- switch;
- rename;
- duplicate;
- delete with confirmation;
- add/remove/place Buddy.

Win98-style tab concept:

```text
[ Home ] [ Lab ] [ + ]
```

Only one Scene simulates at a time. Inactive Scenes are persisted/paused documents, never hidden live physics worlds.

## 4.3 Build/Edit foundation

Next Fest adds the real systemic Build/Edit workspace with:

- select;
- move;
- rotate;
- freeze/unfreeze;
- duplicate;
- delete;
- Properties;
- clear selection/Escape;
- appropriate right-click Win98 context menu;
- safe edit/play transition.

Do not require the full advanced editor QoL set yet. Multi-select, marquee, grouping and broad undo can remain Full Release if they threaten the slice.

## 4.4 Construction part slice

Ship these new construction primitives:

1. **Wood Beam**
2. **Metal Block / Plate**
3. **Wheel**

A small Wood Block may exist only if required by the implementation/feel gate; do not inflate the visible catalogue merely to increase item count.

The required slice should support recognizable constructions such as:

- hanging sign/pendulum;
- simple cart;
- crude launcher/trap;
- simple weapon-trigger contraption.

## 4.5 Constraint / attachment slice

Ship exactly the core recognizable vocabulary:

1. **Rope / World Anchor**
2. **Weld**
3. **Hinge**

Rope should generalize/reuse the existing Rope Suspender architecture where practical rather than creating a disconnected second rope system.

Spring, Slider, motorized joints, gears and specialist constraints remain Full Release.

## 4.6 Properties slice

Common systemic object Properties:

1. **Mass**
2. **Bounce**
3. **Gravity Scale**
4. **Frozen**

Weapon-specific showcase on Shotgun:

1. **Fire Rate / cadence**
2. **Spread**
3. **Knockback**

All values are typed/bounded semantic properties. No arbitrary Godot/CLR property name is ever exposed.

Modified variants may not increase economy payout beyond the canonical safe reward envelope.

## 4.7 Device / signal slice

Ship:

1. **Push Button**
2. **Timer**
3. **Piston**
4. **Weapon Trigger Adapter**
5. **Signal Lamp**

Required demonstrable graphs:

```text
Button -> Lamp
Button -> Piston
Button -> Timer -> Piston
Button -> Weapon Trigger -> Pistol/Shotgun
```

Use the real deterministic event/signal architecture; only the exposed device catalogue is small.

## 4.8 Materials / destruction slice

Ship:

- **Wood**;
- **Metal**;
- one reliable Wood damage/breakage path;
- bounded durability where the slice needs it.

Do not require Full Release heat/electricity/cutting/fracture breadth yet.

## 4.9 Buddy structural damage / repair slice

Add per-part structural integrity with semantic states such as:

```text
Healthy
Damaged
Critical
```

The Next Fest slice may react visually/behaviorally to structural damage but **does not physically detach limbs**.

Recovery:

- Repair Kit restores structural integrity under its authored rules;
- free `System Restore` safety command prevents a persistent soft-lock.

Mood/pain/gore and structural integrity remain separate systems.

## 4.10 Room Physics slice

Next Fest exposes only the high-value gravity experiment:

- Normal Gravity;
- Low Gravity;
- Zero Gravity.

No broad wind/temperature/time/electric environment control panel is required here.

## 4.11 Local Blueprints

Blueprints are mandatory Next Fest functionality.

A Blueprint contains a selected contraption graph made from available definitions:

- entities;
- transforms;
- validated property overrides;
- constraints;
- wires/devices.

Initial Next Fest product allowance: **5 local saved Blueprints**.

Blueprint load/spawn must validate the graph just like Workshop content.

A Blueprint cannot contain executable code, Godot Resources, scene paths or node paths.

## 4.12 Creator Studio Lite — Next Fest differentiator

Recommended Next Fest creator slice:

### Templates

1. **Simple Prop**
2. **Gun**

### Beginner workflow

```text
New Item
→ choose template
→ name it
→ Paint sprite
→ set bounded Properties
→ place semantic markers
→ Test Spawn
→ Save Locally
```

For Gun:

- paint/draw the item sprite in-game;
- use safe bounded firearm properties;
- place a Muzzle marker;
- place a Handle/Grip marker;
- select from trusted first-party projectile/effect choices allowed by the Demo;
- Test Spawn in a temporary/safe creator sandbox.

For Prop:

- paint/draw sprite;
- choose bounded collision archetype/hull generated by trusted code;
- set Mass/Bounce/Gravity/Frozen defaults;
- Test Spawn.

Reuse/generalize lower-level Paint infrastructure (`RasterCanvas`, brush/history/palette/save concepts) rather than cloning Buddy Paint UI logic.

The Creator sprite becomes a trusted bounded runtime presentation; user pixels never define arbitrary Godot code/resources.

### Not in Next Fest Creator Lite

- arbitrary behavior scripting;
- general node/behavior graph;
- custom materials with new systemic logic;
- custom signal devices;
- custom explosives/projectiles from arbitrary logic;
- imported DLL/GDScript/Godot scenes;
- arbitrary 3D meshes/shaders.

Those belong to the Full Release creator program or remain prohibited.

## 4.13 Next Fest Workshop expansion

Keep Initial Demo Workshop types and add safe package types after their local schemas are stable:

3. **Blueprint / Contraption**
4. **Creator Item Lite** (Simple Prop / Gun definitions created by the safe Next Fest Creator Studio)

Demo compatibility is derived by validation, not trusted from uploader tags.

A Next Fest Demo player may only import/use definitions and capabilities that exist in the Next Fest scope.

A Full Release player may import both Demo-compatible and Full-Release content.

All package types remain declarative hostile data and route through immutable staging + validation + local owned copy.

## 4.14 Next Fest product caps

Initial product targets, separately tuneable from hard engine safety caps:

- 2 Scenes;
- 2 active Buddies per Scene;
- 32 systemic sandbox entities per Scene;
- 24 simultaneously awake/dynamic systemic bodies;
- 32 constraints;
- 16 devices;
- 48 wires;
- 5 local Blueprints.

These are profiling/tuning targets, not claims that the engine cannot support more.

---

# 5. Full Release — Next Fest + complete launch breadth

Full Release contains everything from Initial Demo and Next Fest, then turns the vertical slice into a broad creative/RP sandbox.

## 5.1 Multi-Buddy / Scene breadth

Full Release adds:

- higher active-Buddy limit after profiling, with **4 active Buddies** as the first engineering target rather than a permanent promised cap;
- higher Scene count;
- larger Scene rosters;
- full safe migration from Initial/Next Fest data;
- better Scene/cast management UX;
- tab overflow/library polish;
- larger multi-Buddy paint/presentation memory budget based on measurement.

Buddy-to-Buddy social AI/relationships remain **outside the currently locked launch scope** unless explicitly promoted later.

## 5.2 Systemic Build/Edit breadth

Expand the editing toolset based on usability testing:

- multi-select;
- marquee;
- grouping where useful;
- copy/paste;
- broader undo/redo for editor commands;
- grid/snap controls;
- richer selection/property workflows;
- Blueprint management/library UX.

Physics simulation time reversal is not implied by editor undo.

## 5.3 Construction breadth

Expand beyond Beam/Plate/Wheel with a substantial reusable first-party catalogue, e.g.:

- additional beams/blocks/plates/platforms;
- multiple wheel/axle-oriented pieces;
- glass;
- rubber/elastic parts;
- additional structural materials;
- counterweights;
- hazards;
- specialist mechanisms where they add new verbs rather than item-count padding.

First-party content should exercise the same safe definition/capability registries used by declarative creator content wherever practical.

## 5.4 Constraint breadth

Add validated variants such as:

- Spring;
- Slider;
- motorized Hinge;
- breakaway joint;
- later Gear/Pulley/linkage primitives if they prove useful and stable.

Do not replace the routed project-owned simulation with an unrestricted native-joint soup.

## 5.5 Properties breadth

Expand the typed property descriptor registry to appropriate bounded values such as:

- Friction;
- Linear Damping;
- Angular Damping;
- Durability;
- Break Threshold;
- safe collision/impact modifiers;
- wider weapon/tool property sets;
- Grenade fuse/blast tuning;
- Fire Sprayer authored tuning;
- ball mass/bounce/launch tuning;
- rope length/stiffness/damping where supported;
- additional material/device properties.

Correctness-sensitive values remain bounded or unexposed.

## 5.6 Devices / automation breadth

Expand with safe reusable components such as:

- Toggle Switch;
- Pressure Plate;
- Proximity Sensor;
- AND / OR / NOT / XOR;
- Delay;
- Latch/Memory;
- Fan;
- Electromagnet;
- Heater;
- Motor;
- Conveyor;
- Door/actuator adapters;
- additional trusted weapon/tool adapters.

The same deterministic event/action budget applies to first-party and UGC graphs where practical.

## 5.7 Materials / destruction breadth

Expand toward the planned systemic material vocabulary:

- Wood;
- Metal;
- Glass;
- Rubber;
- additional approved material families;
- broader durability/breakage;
- heat/fire response and spread;
- conductivity/electricity where implemented;
- cutting/fracture where implemented and stable;
- authored fragment/effect behavior with strict budgets.

Avoid promising every candidate material effect until its performance/physics acceptance gate passes.

## 5.8 Deeper Buddy structural system

Full Release may add:

- deeper structural damage consequences;
- physical limb detachment;
- safe reattachment/repair;
- detached-part interaction;

**only after** active drive, recovery, presentation, paint, tool targeting, persistence and System Restore are explicitly safe when a body part is absent.

This is not required for the Next Fest slice.

## 5.9 Room Physics breadth

Expand the safe Control Panel beyond the gravity trio only where it adds useful sandbox verbs, for example:

- broader gravity controls;
- simulation speed controls that do not slow UI/platform callbacks;
- wind/forces;
- environmental heat/fire controls;
- other bounded room-level experiment settings.

Exact launch set remains subject to system-specific acceptance rather than exposing arbitrary engine physics settings.

## 5.10 Creator Studio — full program

Full Release expands Creator Studio into a first-class Win98 application.

Target safe templates:

- Simple Prop;
- Sharp/Melee Prop;
- Gun;
- Projectile;
- Explosive;
- Construction Part;
- Signal Device;
- Material Variant;
- Decorative Item;
- additional templates only when they can compile through trusted capabilities.

Full Creator Studio includes:

- sprite painting/editing;
- layers/editor metadata where useful locally;
- semantic markers/ports;
- typed Properties;
- capability selection;
- **Advanced Behavior** event/condition/action graph;
- test sandbox/hot reload;
- validator diagnostics;
- local content packs;
- dependency handling;
- generated preview/publish flow;
- Workshop publishing for approved declarative pack types.

Behavior graphs invoke only trusted semantic capabilities. They never expose Node, RID, arbitrary CLR object, reflection, filesystem, process launch or Steam publishing APIs.

## 5.11 Moddability / Workshop breadth

Full Release grows the safe declarative ecosystem around provider-qualified stable IDs and typed capabilities.

Candidate approved shareable units, each with its own schema/validator:

- existing Room Painting;
- existing Buddy character + paint;
- Blueprints/contraptions;
- Creator content packs/items;
- complete Scene/room configurations after the local format is stable;
- safe player-made cosmetics;
- customized item/tool presets where they fit the same property system.

Workshop remains **non-executable by default**.

Arbitrary C#/GDScript/DLL/scene/shader/native-code Workshop mods are not part of Full Release launch scope.

A future advanced scripted-mod tier requires a separate explicit trust/security design (for example local full-trust opt-in, process isolation, or capability-restricted WebAssembly after threat modeling). `.NET AssemblyLoadContext` is not a security boundary.

## 5.12 Environment / furniture breadth

Full Release adds:

- Scene-owned environment persistence for every Scene;
- larger decoration/wallpaper catalogue;
- complete safe Scene/room sharing after schema stability;
- authored functional furniture interactions, e.g. sit/rest/watch/toggle/use;
- safe handling when furniture is moved/deleted while Buddy is interacting.

## 5.13 Buddy Studio full release

Full Release unlocks/expands the Studio beyond the Initial Demo surface:

- Tops;
- Shoes;
- Accessories with a stronger finished content set;
- player-drawn safe cosmetic templates for supported categories;
- local custom-cosmetic library;
- bounded anchored cosmetic stretching/deformation;
- safe Workshop sharing/import for custom cosmetics;
- larger Browse / Equip / My Creations / Create-Edit / Shared UX.

Cosmetic customization remains visual-only to Buddy physics.

## 5.14 Potion Shop

Potion Shop stays Full Release scope.

Launch with a small polished temporary-effect set selected from the approved direction, with explicit duration/stacking/restart/reset/accessibility/economy rules.

Candidate effects include tail, glossy/shiny, RGB/cycling, glow-in-the-dark, metallic, poison/sickness and a flashlight companion where approved.

## 5.15 Interactive Accessories / gadgets

Full Release may include project-authored interactive accessories such as:

- phone/handheld props;
- small idle gadgets/toys;
- safe mood/Work/environment-reactive accessories;
- calibrated passive-income behavior only when explicitly balanced.

These use trusted capability IDs, never arbitrary scripts in character content.

## 5.16 Player voice recordings

Full Release target:

- optional local microphone recording;
- multiple clips;
- assignment to authored Buddy reaction categories;
- safe bounded local storage;
- volume normalization;
- optional original goofy voice filter.

Voice Workshop sharing is not automatically included.

## 5.17 Contextual tutorial/helper expansion

The Full Release may replace/augment first-use teaching with the approved original office-helper concept, e.g. an animated pen, for newly introduced systems.

It must remain original and clean-room; do not imitate Clippy art/copy/animation.

## 5.18 Achievements in the Full Release

Full Release:

- retains/continues the locally tracked achievement state from Demo play;
- reconciles qualified achievements to the full game's Steam achievement definitions;
- can add later Full-Release-specific achievements without renaming/reusing the permanent baseline IDs.

---

# 6. Not in the currently locked launch scope of any of the three builds

Unless separately promoted later:

- real-time multiplayer/shared live rooms;
- Steam lobbies/P2P/RPC gameplay;
- Buddy-to-Buddy social AI/relationships/coordinated behavior;
- unrestricted executable Workshop mods;
- arbitrary user Godot scenes/resources/scripts/shaders/native libraries;
- arbitrary full-trust C# downloaded from Workshop;
- Steam sharing of microphone recordings;
- Linux/macOS launch support.

These may be post-launch/future programs, but they must not silently become dependencies of the current three-build plan.

---

# 7. Release-scope matrix

| System | Initial Steam Demo | Next Fest Demo | Full Release |
| --- | --- | --- | --- |
| Existing full current tool toybox | **Yes** | **Yes** | **Yes** |
| One persistent live Buddy | **Yes** | **Yes** | **Yes** |
| Multiple simultaneous Buddies | No | **2 max** | **Higher profiled cap; 4 first target** |
| One room/environment | **Yes** | **Yes, migrated into Scenes** | **Yes** |
| Named Scene tabs | No | **2 max** | **Expanded** |
| Paint Buddy | **Yes** | **Yes** | **Yes + future compatible expansion** |
| Paint Background | **Yes** | **Yes, Scene-owned** | **Yes, Scene-owned** |
| Room Decorator | **Yes — fix current scope-gate mismatch** | **Yes** | **Yes + larger/functional content** |
| Buddy Studio current Demo categories | **Yes** | **Yes** | **Yes** |
| Tops / Shoes / Accessories | No under current Demo policy | No unless separately promoted | **Yes** |
| Player-drawn Buddy cosmetics | No | No | **Yes** |
| Work Mode | **Yes** | **Yes** | **Yes** |
| Gore Mode | **Yes, opt-in Steam policy** | **Yes** | **Yes** |
| Tutorial | **Yes** | **Yes + teaches new slice** | **Expanded/contextual help** |
| Workshop Room Painting | **Yes** | **Yes** | **Yes** |
| Workshop Buddy config + paint | **Yes** | **Yes** | **Yes** |
| Steam achievement qualification | **Local only** | **Local only** | **Steam reconciliation/unlock** |
| Systemic Build/Edit | No | **Core slice** | **Fuller editor** |
| Construction | No | **Beam / Plate / Wheel** | **Broad catalogue** |
| Rope/Weld/Hinge framework | Existing Rope Suspender only | **Rope / Weld / Hinge** | **+ Spring/Slider/Motor/etc.** |
| Systemic Properties | No | **Mass/Bounce/Gravity/Frozen + Shotgun trio** | **Broad typed registry** |
| Devices/signals | No | **Button/Timer/Piston/Weapon Trigger/Lamp** | **Sensors/logic/motors/etc.** |
| Materials/destruction | Existing authored damage/fire only | **Wood/Metal + Wood break** | **Broader materials/heat/electric/cutting** |
| Buddy structural integrity | No | **Healthy/Damaged/Critical** | **Deeper; detachment only after safe refactor** |
| Room Physics | No new systemic control | **Normal/Low/Zero Gravity** | **Broader bounded controls** |
| Local Blueprints | No | **Yes, 5 slots initial** | **Expanded library** |
| Workshop Blueprints | No | **Yes** | **Yes** |
| In-game generic item creator | No | **Creator Lite: Prop + Gun** | **Full Creator Studio** |
| In-game item sprite painting | No | **Prop/Gun painting** | **Broad creator painting** |
| Advanced behavior graph | No | No | **Yes, safe event/condition/action graph** |
| Workshop creator items | No | **Safe Prop/Gun packages** | **Broader safe content packs** |
| Complete Scene sharing | No | No initially | **Yes after local schema stable** |
| Potion Shop | No | No | **Yes** |
| Interactive furniture/accessories | No | No | **Yes, authored capabilities** |
| Player voice recordings | No | No | **Local Full Release feature** |
| Executable Workshop code mods | No | No | **No by default / future separate trust tier** |

---

# 8. Save / upgrade rule

Required upgrade direction:

```text
Initial Demo save
    ↓
Next Fest Demo save
    ↓
Full Release save
```

Requirements:

- Initial Demo progress remains valid when Next Fest systems appear;
- the old one-room/one-Buddy state migrates deterministically into the first Scene/Buddy identity when Next Fest is first run;
- Next Fest Scenes/Blueprints/Creator Lite content carry into Full Release without destructive format conversion;
- account wallet/unlocks/statistics/Work/achievement qualification remain preserved;
- Full Release content seen by an older Demo binary must not be destructively deleted merely because that build cannot activate it;
- Steam Cloud/handoff paths must not make cross-build downgrade accidentally overwrite richer state.

---

# 9. Implementation order implied by this scope

## Before Initial Demo RC

1. preserve/finish all current Demo systems and polish;
2. resolve Room Decorator DemoScope mismatch;
3. merge/reconcile achievement baseline against current `main`;
4. complete Steam/Workshop/Cloud/release gates;
5. freeze and ship Initial Demo.

## After Initial Demo / for Next Fest

1. mod-friendly stable IDs/registries/capability/event-action foundations;
2. account-vs-Buddy state split + one-Buddy compatibility;
3. production multi-Buddy host;
4. Scene documents/runtime/tabs + deterministic Initial Demo migration;
5. Scene-owned environment state;
6. systemic Build/Edit + parts;
7. constraints + Properties;
8. devices/signals + gravity;
9. material breakage + Buddy structural integrity/repair;
10. Blueprints;
11. Creator Studio Lite + painted Prop/Gun;
12. Blueprint/Creator Lite Workshop package types;
13. Next Fest performance/UX/marketing/RC pass.

## After Next Fest / for Full Release

Expand breadth on the same architecture rather than creating parallel systems:

- larger Scene/cast limits;
- richer construction/constraints/properties/devices/materials;
- full Creator Studio/behavior graph;
- broader Workshop content packs;
- deeper Buddy structure/destruction;
- larger Buddy Studio and Environment content;
- Potion Shop;
- functional furniture/accessories;
- player voice/local personalization;
- full-release progression/economy/polish/migration/RC.

---

# 10. Audit notes / known inconsistencies

1. **Room Decorator:** `docs/ROADMAP.md` calls it part of the current Steam-demo Environment baseline, while `DemoScope.IncludesRoomDecorator` currently gates it to Full Release. This document resolves product intent in favor of Initial Demo inclusion; code/tests/export behavior need correction.
2. **Achievement baseline:** owner-approved implementation exists on `steam-achievements-baseline`, but that branch has diverged substantially from current `main`; it must be ported/re-audited rather than blindly merged.
3. **Old Full Release planning branch:** `plan/full-release-systemic-sandbox` was created from `3966648...` and is now substantially behind current `main`. Its architecture/research remains useful, but implementation should branch from current `main` and port the relevant plans/decisions instead of coding directly on the stale base.
4. **Older docs saying all multi-Buddy/systemic work is Full-Release-only:** superseded for distribution scope. Next Fest receives the exact vertical slice defined here; Full Release retains the breadth.
5. **Older Next Fest draft that removed current Demo tools:** superseded. Initial Demo content is cumulative and remains present.

---

# 11. Owner lock checklist

Before implementation begins, the scope is considered locked once the owner accepts or edits these specific product choices:

- Initial Demo Room Decorator = **Yes**;
- Initial Demo current tool catalogue = **all current tools kept**;
- Initial Demo Buddy Studio Tops/Shoes/Accessories = **No under current policy**;
- Next Fest = **2 Buddies / 2 Scenes**;
- Next Fest construction = **Beam / Plate / Wheel**;
- Next Fest constraints = **Rope / Weld / Hinge**;
- Next Fest devices = **Button / Timer / Piston / Weapon Trigger / Lamp**;
- Next Fest gravity = **Normal / Low / Zero**;
- Next Fest Blueprints = **5 local slots + Workshop sharing**;
- Next Fest Creator Studio Lite = **painted Prop + painted Gun templates**;
- Full Release = **expanded systemic + Creator + RP breadth on the same architecture**;
- Buddy social AI/relationships = **not part of currently locked launch scope**;
- executable Workshop code mods = **not ordinary Workshop scope**.
