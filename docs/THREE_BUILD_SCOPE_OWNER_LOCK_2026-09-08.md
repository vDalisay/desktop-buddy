# Desktop Buddy — Three-Build Scope Owner Lock

Status: **OWNER-APPROVED SCOPE — authoritative correction**  
Recorded: 2026-09-08  
Branch: `plan/three-build-release-scope`  
Base: current `main` at `9adfbcb5a3e32aa50fe88da5fe4b772e7ec64eb8`

This document supersedes conflicting scope statements in:

- `docs/INITIAL_DEMO_NEXT_FEST_FULL_RELEASE_SCOPE_2026-09-08.md`;
- `docs/NEXT_FEST_THREE_BUILD_SCOPE_SOURCE_ALIGNMENT_2026-09-07.md`;
- `docs/NEXT_FEST_DEMO_SYSTEMIC_SANDBOX_VERTICAL_SLICE_2026-09-07.md`;
- `docs/FULL_RELEASE_EXPANSION_ROADMAP.md` where that older roadmap still says all Scene/systemic work is Full-Release-only;
- older audit notes that put Room Decorator or achievements in the Initial Steam Demo;
- any earlier fixed 2-Buddy / 2-Scene or fixed 4-Buddy Full Release product caps.

The three Steam builds remain cumulative:

```text
INITIAL STEAM DEMO
        ↓
STEAM NEXT FEST DEMO
        ↓
FULL RELEASE
```

Nothing already included in an earlier Steam build is removed from a later build merely to make the later catalogue smaller.

The itch.io build remains a separate reduced distribution.

---

# 1. Build identities

Recommended feature tags remain:

```text
Initial Steam Demo
    steam,steam_demo

Steam Next Fest Demo
    steam,steam_demo,next_fest_demo

Full Release
    steam,full_release
```

`steam` identifies platform availability only. It is never a content-entitlement flag.

---

# 2. Initial Steam Demo

The Initial Steam Demo contains the current/pre-approved normal Demo gameplay, but **does not include Room Decorator and does not include the new achievement system yet**.

## 2.1 Core gameplay

Include:

- one live persistent Buddy;
- current six-body ragdoll / active-puppet physics;
- autonomy, recovery and grab resistance;
- mood/trust/fear/care;
- hunger/fullness;
- personality/preferences/fun/novelty;
- knockout/recovery;
- current damage/pain/economy systems;
- current loose-object physics and safety budgets;
- Win98 Play/compact/fullscreen/window behavior;
- opt-in Gore Mode under the existing Steam policy.

No production multi-Buddy runtime and no Scene library are exposed yet.

## 2.2 Current interaction/tool catalogue

Keep the existing current Demo toybox intact:

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

Current unlock/progression/economy behavior remains part of the Initial Demo.

## 2.3 Customization

Include:

- Paint Buddy and its current approved toolset/persistence;
- Buddy Studio current Demo-authorized categories and content;
- Paint Background;
- one persistent room/background;
- Work Mode;
- first-session tutorial and release presentation polish.

### Explicit Initial Demo exclusions

- **Room Decorator: NO — promoted to Steam Next Fest Demo.**
- **Steam achievements: NO — promoted to Steam Next Fest Demo.**
- named Scenes;
- production multi-Buddy;
- systemic Build/Edit;
- new construction/constraints/devices/properties/materials;
- local systemic Blueprints;
- generic Creator Studio;
- new Chemistry/Status system;
- Potion Shop.

## 2.4 Workshop

Initial Demo keeps Workshop v1 only:

1. Room Painting;
2. Buddy configuration + declared Buddy paint.

No Blueprint/Creator/Scene package types yet.

---

# 3. Steam Next Fest Demo

The Next Fest Demo includes **everything in the Initial Demo**, then adds the vertical slice below.

Its product job is to demonstrate the direction of the full systemic sandbox, RP/Scene system and creator ecosystem without shipping the full breadth.

---

## 3.1 Room Decorator — promoted here

Room Decorator first appears in the Steam Next Fest Demo.

Include:

- permanent decoration ownership/storage;
- wallpaper/decor catalogue approved for the Next Fest slice;
- placement/edit/rotation where supported;
- Scene-local environment persistence after Scene migration;
- selected representative room decorations rather than the entire eventual Full Release catalogue.

The Initial Steam Demo continues to have Paint Background but not Room Decorator.

---

## 3.2 Achievements — promoted here

The current owner-approved 24-achievement design first ships as part of the Steam Next Fest Demo program.

Next Fest behavior:

- evaluate/track conditions locally;
- persist qualification/counters;
- do not unlock Steam achievements under the Demo AppID;
- carry qualification into the Full Release;
- Full Release reconciles qualified IDs into the full game's Steam achievements.

Because some achievement rules reference Room Decorator, the two systems now enter public Demo scope together.

The existing achievement implementation branch must be ported/re-audited against current `main`; do not blindly merge its diverged history.

---

## 3.3 Multi-Buddy — no arbitrary product cap

The old fixed `2 active Buddies` Next Fest rule is superseded.

### Next Fest product rule

A Scene may include **as many of the player's created Buddy identities as the player has**.

Do not impose an artificial product cap such as 2 merely to distinguish Demo from Full Release.

Architecture requirements:

- every Buddy has independent appearance/paint;
- every Buddy has independent mood/hunger/harmful memory/personality/fun state;
- shared tools target the correct Buddy;
- one routed fixed simulation tick remains authoritative;
- Buddies are processed in stable deterministic order;
- no Buddy-to-Buddy social AI/relationships are implied;
- Buddy-vs-Buddy collision/avoidance remains out of the initial scope unless separately approved.

### Safety/performance rule

The runtime may enforce **measured technical safety budgets** to prevent pathological scenes from crashing or stalling the game. Those budgets are not marketing/product locks and should scale from profiling where possible.

UI should communicate when a device is near or beyond a safe measured simulation budget rather than silently imposing an arbitrary content-tier cap.

---

## 3.4 Scenes — up to 10 in Next Fest

Steam Next Fest Demo supports **up to 10 named Scenes**.

Each Scene owns:

- name;
- background paint;
- wallpaper/decor;
- Buddy roster and placement anchors;
- systemic sandbox graph;
- supported Room Physics state;
- creator/Blueprint instances.

Required operations:

- create;
- switch;
- rename;
- duplicate;
- delete with confirmation;
- add/remove/place Buddies;
- robust tab overflow/list UI once more than a few Scenes exist.

Only one Scene simulates at a time. Inactive Scenes are persisted documents, not hidden live physics worlds.

---

## 3.5 Systemic Build/Edit vertical slice

Include:

- select;
- move;
- rotate;
- freeze/unfreeze;
- duplicate;
- delete;
- Properties;
- Pause/Play;
- undo/redo for deliberate editor actions where the slice requires it;
- Win98 context/property windows.

Construction slice:

- Wood Beam;
- Metal Block/Plate;
- Wheel;
- optional small Wood Block only if needed for usability.

Constraints:

- Rope / World Anchor;
- Weld;
- Hinge.

Properties:

- Mass;
- Bounce;
- Gravity Scale;
- Frozen;
- Shotgun Fire Rate;
- Shotgun Spread;
- Shotgun Knockback.

Devices/signals:

- Push Button;
- Timer;
- Piston;
- Weapon Trigger Adapter;
- Signal Lamp.

Materials/destruction:

- Wood;
- Metal;
- one reliable Wood break/damage path.

Buddy structural slice:

- per-part integrity;
- Healthy / Damaged / Critical presentation/behavior;
- Repair Kit recovery;
- free System Restore safety path;
- no physical limb detachment yet.

Room Physics:

- Normal Gravity;
- Low Gravity;
- Zero Gravity.

---

## 3.6 Local Blueprints

Blueprints are mandatory for Next Fest.

They contain declarative:

- entities;
- transforms;
- validated property overrides;
- constraints;
- wires/devices.

Do **not** keep the earlier arbitrary `5 local Blueprint` product cap. Storage/UI should be bounded by practical disk/library/performance limits rather than a deliberately tiny demo restriction.

Workshop compatibility remains derived by validation.

---

## 3.7 Creator Studio Lite — expanded template set

Steam Next Fest Demo includes Creator Studio Lite with these templates:

1. **Simple Prop**
2. **Gun**
3. **Sword / Sharp Melee**
4. **Explosive**

All four use the same safe declarative capability model.

### Common workflow

```text
New Item
→ choose template
→ name item
→ Paint sprite
→ configure bounded Properties
→ place semantic markers
→ Test Spawn
→ Save Locally
→ Validate
→ optionally Publish
```

### Simple Prop

Allow:

- painted sprite;
- trusted collision archetype / generated bounded hull;
- Mass/Bounce/Gravity/Frozen;
- material choice from the Next Fest-safe set;
- Test Spawn.

### Gun

Allow:

- painted sprite;
- Muzzle marker;
- Handle/Grip marker;
- bounded Fire Rate/Spread/Knockback and other approved safe firearm parameters;
- trusted projectile/effect choices exposed by the Next Fest build;
- Test Spawn.

### Sword / Sharp Melee

Allow:

- painted sprite;
- Handle/Grip marker;
- one or more bounded Blade/Sharp regions or a project-generated sharp-edge descriptor;
- safe mass/handling/impact/piercing descriptors within approved envelopes;
- no arbitrary collision mesh or executable hit callback;
- Test Spawn.

### Explosive

Allow:

- painted sprite;
- bounded Fuse;
- bounded Blast Radius;
- bounded Blast Force;
- bounded structural/damage effect profile from trusted definitions;
- optional ignition/fire flag only through a trusted Next Fest-safe capability;
- strict spawn/blast budgets;
- Test Spawn in a safe creator sandbox.

### Not in Creator Lite

- arbitrary C#/GDScript;
- arbitrary node/scene loading;
- generalized behavior graph;
- arbitrary custom shaders/3D meshes;
- arbitrary filesystem/network/Steam access.

The full event/condition/action graph remains Full Release scope.

---

## 3.8 Workshop expansion in Next Fest

Keep the two Initial Demo package types and add, after local schema/validator stability:

3. Blueprint / Contraption;
4. Creator Item Lite — Prop / Gun / Sword / Explosive.

All are hostile declarative data.

Compatibility is calculated from actual definition/capability references; uploader tags are advisory only.

---

# 4. Chemistry / poison / status effects

A new **Chemistry / Status Effects** foundation is authorized for both Steam Next Fest and Full Release.

This should take heavy systems-level inspiration from the appeal of People Playground's chemistry/medical sandbox without copying its names, item art, UI, exact formulas or implementation.

Public PPG references show why the system is compelling:

- different liquids cause qualitatively different failures rather than only damage-over-time;
- examples include flesh damage, bone damage, joint locking, knockout, coagulation/healing, adrenaline/revival, strength/durability changes and reanimation-like effects;
- later/secret variants include muscle, circulation, vestibular and numbing effects;
- liquids can be moved through containers/wires/pressurizers and mixed into experimental combinations;
- medical counter-effects such as coagulation, adrenaline and mending make experimentation reversible and support player-created treatment scenarios.

Desktop Buddy should copy the **design principle**: several orthogonal body/status channels + recoveries + combinability.

## 4.1 Next Fest chemistry slice

Use original Desktop Buddy terminology and presentation.

Recommended minimum Next Fest effect families:

1. **Toxin** — progressive sickness/weakness, not just raw HP drain;
2. **Sedative** — reduced autonomy → drowsy/knockout;
3. **Paralytic / Joint Lock** — temporarily reduces limb/active-puppet drive;
4. **Corrosive** — damages structural integrity / visible body condition over time;
5. **Stimulant** — counteracts sedation and temporarily increases wakefulness/activity;
6. **Coagulant / Stabilizer** — reduces bleeding/ongoing wound effects where Gore is enabled and stabilizes damage state;
7. **Regenerative / Repair Serum** — bounded recovery counterpart for experimentation.

Optional Next Fest showcase if stable:

8. **Hallucinogenic / Dizzy effect** — visual/behavioral disorientation using accessible reduced-motion alternatives, without making UI unreadable.

### Delivery

Start with a small trusted first-party delivery set, for example:

- syringe/injector tool;
- throwable/breakable vial or spray only if it reuses existing projectile/fire plumbing safely.

Do not require a complete fluid simulation for Next Fest.

### Status architecture

Use engine-free typed status definitions, e.g.:

```text
StatusEffectDefinition
  StatusId
  Channels[]
  DoseCurve
  Duration/decay
  Stack/interaction policy
  PresentationId
  RecoveryTags[]
  CreatorExposure
```

Possible channels:

```text
Consciousness
MotorDrive
StructuralIntegrity
Bleeding/Stability
Pain
AutonomyEnergy
Perception
Temperature // later
Circulation // later
```

Effects must route through semantic capabilities rather than directly mutating arbitrary Buddy nodes.

### Multi-Buddy requirement

Dose/status state is per Buddy identity/runtime actor. Poisoning Buddy A must never leak into Buddy B's state.

### Creator/Workshop requirement

Next Fest Creator Studio may select only trusted first-party effect profiles for Gun/Explosive/Sharp-item behavior where explicitly exposed. Creator items cannot invent executable status logic.

---

## 4.2 Full Release chemistry breadth

Full Release expands the same subsystem rather than replacing it.

Candidate additions after testing:

- circulation/blood-pressure effects;
- muscle weakness/strength effects;
- vestibular/balance effects;
- numbness/pain suppression;
- oxygen/breathing effects if meaningful to the room systems;
- temperature-related chemistry;
- infection/decay/reanimation-like fictional effects under original Desktop Buddy presentation;
- antidotes/counter-agents;
- containers, transfer devices and mixing;
- chemistry-aware pumps/valves/tubes integrated with the device graph;
- custom safe mixtures based on bounded first-party channels;
- chemistry-aware Creator Studio capabilities;
- shareable declarative mixtures/presets after validation is stable.

If mixing ships, mixtures must resolve from approved typed channels and bounded dose math; they do not carry scripts.

The goal is emergent experimentation such as:

```text
sedative + stimulant
corrosive + regenerative
bleeding + stabilizer
weakness + strength modifier
```

with deterministic, bounded interaction rules rather than bespoke pairwise code for every possible combination.

---

# 5. Full Release caps and breadth

The previous fixed `4 Buddies` engineering target is **not a product cap**.

## 5.1 Buddies

Full Release supports **as many active Buddies as the player's PC can safely support**.

Implement this through measured dynamic/performance budgets and clear diagnostics, not a small arbitrary entitlement limit.

Possible runtime policy:

- profile/estimate simulation cost;
- warn when adding another Buddy may exceed the recommended budget;
- allow scalable quality/physics presentation reductions where safe;
- retain a hard emergency safety ceiling only to prevent crashes/runaway allocation, not as content monetization.

## 5.2 Scenes

Full Release supports **as many persisted Scenes as the player's PC/storage/UI can practically support**, rather than the Next Fest 10-Scene product limit.

Use scalable library/navigation UI once tabs alone become unwieldy.

Only the active Scene simulates, so large persisted Scene libraries should primarily be an I/O/storage/library concern rather than simultaneous physics load.

## 5.3 Full systemic breadth

Full Release retains all Next Fest systems and expands:

- construction catalogue;
- Spring/Slider/motor/breakaway/advanced constraints;
- broad typed Properties;
- sensors/logic/memory/fans/electromagnets/heaters/motors/conveyors;
- deeper materials/destruction/heat/fire/electricity/cutting;
- deeper Buddy structural damage and eventual detachment/reattachment only after rig safety work;
- broader Room Physics;
- full Creator Studio with behavior graph;
- broader safe Workshop content packs;
- Scene/room sharing after stable local schema;
- expanded Buddy Studio/player-drawn cosmetics;
- functional furniture/accessories;
- Potion Shop;
- player voice/local personalization where approved;
- full Chemistry/Status/mixing breadth.

---

# 6. Release-scope matrix

| System | Initial Steam Demo | Steam Next Fest Demo | Full Release |
| --- | --- | --- | --- |
| Current 18-tool toybox | **Yes** | **Yes** | **Yes** |
| Paint Buddy | **Yes** | **Yes** | **Yes** |
| Paint Background | **Yes** | **Yes, Scene-owned** | **Yes** |
| Buddy Studio current Demo categories | **Yes** | **Yes** | **Yes** |
| Work Mode | **Yes** | **Yes** | **Yes** |
| Gore | **Yes, opt-in** | **Yes** | **Yes** |
| Room Decorator | **No** | **Yes** | **Yes + broad/functional content** |
| Achievement system | **No** | **Yes, local qualification** | **Yes + Steam reconciliation/unlocks** |
| Multi-Buddy | No | **All player-created Buddies; no arbitrary product cap** | **As many as the PC safely supports** |
| Named Scenes | No | **Up to 10** | **As many as practical/PC+storage supports** |
| Build/Edit | No | **Core slice** | **Expanded editor** |
| Construction | No | **Beam / Plate / Wheel** | **Broad catalogue** |
| Constraints | Existing Rope Suspender only | **Rope / Weld / Hinge** | **+ Spring/Slider/Motor/etc.** |
| Properties | No new systemic editor | **Mass/Bounce/Gravity/Frozen + Shotgun showcase** | **Broad typed registry** |
| Devices | No | **Button/Timer/Piston/Weapon Trigger/Lamp** | **Broad automation/logic** |
| Materials/destruction | Existing authored damage/fire | **Wood/Metal + Wood break** | **Deep material system** |
| Buddy structural integrity | No | **Healthy/Damaged/Critical** | **Deeper + safe detachment later** |
| Room Physics | No new systemic controls | **Normal/Low/Zero Gravity** | **Broader controls** |
| Blueprints | No | **Yes; no tiny artificial save-slot cap** | **Expanded/scalable library** |
| Workshop Blueprints | No | **Yes after validator stability** | **Yes** |
| Creator Studio | No | **Lite** | **Full** |
| Creator templates | No | **Prop / Gun / Sword / Explosive** | **Broad template set** |
| In-game sprite painting | No generic item painting | **Yes for Creator Lite** | **Yes, broad creator workflow** |
| Advanced behavior graph | No | No | **Yes, safe event/condition/action graph** |
| Creator Workshop items | No | **Safe Lite packages** | **Broad safe content packs** |
| Chemistry/status effects | No | **Core poison/medical slice** | **Full chemistry/mixing breadth** |
| Potion Shop | No | No | **Yes** |
| Functional furniture/accessories | No | No | **Yes** |
| Executable Workshop mods | No | No | **No by default; future separate trust tier only** |

---

# 7. Save / migration rule

```text
Initial Demo save
    ↓
Next Fest Demo
    ↓
Full Release
```

Requirements:

- Initial Demo's one Buddy becomes the first Buddy identity cleanly;
- Initial Demo's one room/background becomes the first Scene;
- Next Fest may add up to 10 Scenes without losing old environment data;
- all Next Fest Buddy identities, Scenes, Blueprints, Creator Lite items, statuses/progress and Room Decorator state carry into Full Release;
- Full Release removes the 10-Scene product limit without changing Scene IDs;
- richer Full Release state must not be destructively stripped if an older executable encounters it;
- account wallet/unlocks/stats/Work progress remain shared/global where already intended;
- per-Buddy status effects and identity state remain isolated.

---

# 8. Implementation consequences

## Initial Demo RC

Do **not** block Initial Demo on:

- Room Decorator;
- achievements;
- multi-Buddy;
- Scenes;
- systemic sandbox;
- Creator Studio;
- Chemistry/status effects.

Finish/polish the existing Initial Demo scope and Workshop v1 only.

## Next Fest implementation order

Recommended order after Initial Demo:

1. port the research/source-alignment docs onto a current-main-based implementation branch;
2. build mod-friendly stable IDs/registries/capabilities/events/actions;
3. split account/Buddy state while preserving the one-Buddy save;
4. productionize multi-Buddy with no arbitrary Buddy product cap;
5. implement Scene documents/runtime and 10-Scene library cap;
6. move room/background state into Scenes;
7. enable Room Decorator in the Next Fest scope;
8. port/re-audit the achievement system for Next Fest local qualification;
9. Build/Edit + construction;
10. Rope/Weld/Hinge + Properties;
11. devices/signals + gravity;
12. materials + Buddy structural integrity/repair;
13. Blueprint persistence/library;
14. Creator Studio Lite: Prop/Gun/Sword/Explosive;
15. Chemistry/Status core slice;
16. Blueprint/Creator Workshop package types;
17. Next Fest onboarding/economy/performance/marketing/RC.

## Full Release continuation

Continue widening definitions/capabilities on the same architecture rather than creating parallel full-only implementations.

---

# 9. Research note — People Playground chemistry inspiration

Research performed 2026-09-08 against public People Playground Steam Community documentation/guides/news.

Useful principles to carry over cleanly:

1. **Orthogonal physiological effects are more interesting than generic poison DPS.** Different substances affect consciousness, movement, structural/body state, bleeding/stability and durability in visibly different ways.
2. **Counter-agents matter.** Coagulation, mending/recovery and adrenaline-like recovery tools make experimentation reversible and create medical rescue gameplay.
3. **Delivery and transfer are toys themselves.** PPG lets players use blood-vessel wires, pressurizers and containers to move liquids, turning chemistry into contraption gameplay rather than a menu-only status system.
4. **Mixing creates experimentation.** PPG includes crafted mixtures and user experimentation; Desktop Buddy should eventually support bounded typed-channel mixtures rather than hard-coded one-off combinations.
5. **The status system should integrate with construction/devices and Creator Studio.** A poison system isolated to one syringe would leave most of its systemic value unused.
6. **Do not copy PPG's exact names, liquid recipes, assets, UI or source behavior.** Reuse only the broad systems principles and implement original Desktop Buddy status definitions and presentation.

Public references consulted include Steam Community fluid/medical guides and current/archived PPG news describing syringe/liquid and physiology behavior.

---

# 10. Locked owner decisions from 2026-09-08

- Room Decorator first appears in **Steam Next Fest Demo**, not Initial Steam Demo.
- Achievements first appear in **Steam Next Fest Demo**, not Initial Steam Demo.
- Next Fest Multi-Buddy supports **as many Buddies as the player has**, with only technical safety/performance budgets rather than a small product cap.
- Next Fest supports **up to 10 Scenes**.
- Full Release supports **as many Buddies and Scenes as the player's PC/storage can safely/practically support**.
- Creator Studio Lite remains in Next Fest.
- Creator Studio Lite templates are **Prop, Gun, Sword/Sharp Melee, Explosive**.
- A People-Playground-inspired but original **Chemistry / poison / status-effect system** is part of the Next Fest direction and expands substantially in Full Release.
- Full Release continues the same systems/registries/schemas; it receives breadth and practical resource limits rather than parallel implementations.
