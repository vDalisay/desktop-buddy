# Desktop Buddy — Next Fest Demo Systemic Sandbox Vertical Slice

Status: **owner-approved product direction / implementation source alignment**  
Recorded: 2026-09-07  
Planning branch: `plan/full-release-systemic-sandbox`  
Audited base: `main` at `3966648b7f9fb8c6a70f4cf6f8e586b9998a2641`  
Target distribution: **updated Steam Demo for the next Steam Next Fest**

This document records the owner's decision to make the next Steam Demo a curated vertical slice of the newly planned Full Release systems.

It **supersedes the earlier rule that every new systemic-sandbox and multi-Buddy feature is Full Release-only**. The Full Release still owns the breadth, higher caps, deeper content libraries and later systems. The Next Fest Demo receives only the deliberately selected slice defined here.

The itch.io build remains outside this new scope.

---

# 1. Product goal

The Next Fest Demo should prove the Full Release fantasy quickly:

> Create a small cast of Buddies, place them into named Win98-style Scenes, customize the room, build a simple physics contraption from familiar sandbox parts, modify a few properties, automate it, hurt or repair a Buddy, save the setup, and optionally share a demo-compatible contraption through Steam Workshop.

The goal is **not** to make the Demo a miniature complete game.

The Demo should expose the most recognizable and frequently useful mechanics from the same broad families that People Playground and Mutilate-a-Doll players understand immediately, while preserving the parts that make Desktop Buddy distinct:

- persistent custom Buddies;
- mood/personality/care;
- Windows 98 presentation;
- Paint Buddy / Paint Background / Buddy Studio;
- Work Mode;
- room/Scene roleplay;
- a smaller but real systemic physics sandbox.

The Full Release sells **breadth and depth**, not a different underlying game.

---

# 2. Distribution model

The current export preset already distinguishes the Steam Demo with:

```text
steam,steam_demo
```

and the Full Release with:

```text
steam,full_release
```

Use that existing distinction rather than inventing a second Demo product.

Add an explicit runtime seam:

```csharp
DemoScope.IsSteamDemo
```

with semantics equivalent to:

```text
steam_demo && !full_release && !itch_io
```

The new capability boundary should then distinguish:

```text
IncludesNextFestSandboxSlice
IncludesFullSystemicSandbox
```

Conceptually:

```text
Next Fest slice = Full Release OR Steam Demo
Full breadth     = Full Release only
```

Do not use `steam` alone as the permission check. `steam` also exists on the Full Release and represents platform availability, not content entitlement.

The untagged/editor/test cases should remain fail-closed unless a scenario override deliberately opts in.

---

# 3. Scope philosophy: same systems, smaller trusted catalogues

The Demo should not receive alternate simplified implementations.

For every shared feature:

```text
same domain model
same physics/runtime implementation
same persistence schema
same property validation
same blueprint validator
same Workshop package format
```

The Demo differs through:

- available definition IDs;
- available property IDs;
- available device IDs;
- Scene/Buddy/entity caps;
- Workshop compatibility validation;
- progression/unlock policy;
- UI catalogue filtering.

This is important because a Demo-created Scene or Blueprint should promote into the Full Release without conversion to a different gameplay model.

Use explicit trusted release metadata instead of scattered `if demo` branches where possible.

Example conceptual availability:

```text
Availability = DemoAndFull
Availability = FullOnly
```

for first-party definitions and property descriptors.

---

# 4. Next Fest Demo headline limits

These are product caps for the vertical slice, separate from internal performance safety caps.

## Scenes

Demo:

- maximum **2 named Scenes**;
- create, rename, duplicate, delete and switch through Win98 tabs;
- only one Scene simulates at a time.

Full Release:

- substantially higher Scene count bounded only by the normal storage/performance policy.

## Buddies

Demo:

- maximum **2 active Buddies in one Scene**;
- each can use a different player-created Buddy appearance;
- each has independent persistent mood, hunger, personality/fun state and harmful-memory state;
- no Buddy-to-Buddy social AI or intentional interaction.

Full Release:

- higher active-Buddy cap after profiling;
- larger Scene rosters;
- future Buddy-to-Buddy interactions remain a separate feature and are **not promised by this Demo plan**.

## Sandbox graph

Initial Demo product caps:

- 32 placed systemic sandbox entities per Scene;
- 24 simultaneously awake/dynamic sandbox bodies;
- 32 constraints;
- 16 devices;
- 48 signal wires;
- 5 locally saved contraption Blueprints.

Full Release starts from the larger benchmark targets in the systemic-sandbox audit and may raise them after profiling.

These numbers are tunable engineering/product caps, not promises that the engine cannot handle more.

---

# 5. Curated existing gameplay-tool roster

The Next Fest Demo should be re-curated around a recognizable crossover toybox instead of exposing every existing tool merely because it already exists.

Recommended Demo gameplay tools:

| Family | Demo tool | Why it earns a slot |
| --- | --- | --- |
| Manipulation | **Grab** | universal sandbox interaction |
| Care | **Pet** | preserves Desktop Buddy's companion identity |
| Care/prop | **Meal** | care + loose-object interaction |
| Blunt melee | **Baseball Bat** | instantly understood physics-sandbox staple |
| Sharp melee | **Sword** | familiar cutting/piercing fantasy; already supports impalement |
| Firearm | **Pistol** | baseline firearm |
| Firearm | **Shotgun** | strong physics feedback and best weapon-property showcase |
| Explosive | **Grenade** | familiar chain-reaction toy |
| Fire | **Fire Sprayer** | familiar burn/fire category |
| Repair | **Repair Kit** | closes damage -> repair loop |
| Attachment | **Rope Suspender** | bridges current gameplay into the construction vocabulary |

Recommended Full-Release-only existing breadth for this Demo cut:

- Tickle;
- Boxing Glove;
- Baseball;
- Nerf Blaster;
- Soccer Ball;
- Drink;
- Power Grab;
- future tool variants and systemic additions.

The final Demo roster may retain one of these for marketing/feel reasons, but the principle remains: **one or two strong representatives per family, not the whole catalogue**.

A locked Full Game catalogue can show selected unavailable entries as previews, but should not clutter the first-session flow.

---

# 6. System group A — Multi-Buddy roleplay slice

Required Demo behavior:

- player can create/select multiple Buddy identities from the existing character library;
- add up to two Buddies to the active Scene;
- each has its own appearance and paint;
- each runs standing, recovery, autonomy, reactions, knockout and care independently;
- player tools can hit either Buddy;
- grabbing resolves the owning Buddy actor;
- focus-dependent UI actions target the selected/focused Buddy;
- Buddies do not intentionally perceive or interact with one another.

Initial collision behavior remains the current safe behavior:

- Buddy parts do not collide with Buddy parts;
- therefore two Buddies may visually overlap/pass through each other;
- no accidental Buddy-vs-Buddy combat system is introduced.

The Demo is the first production proof of the `PlayerProgressState` / `BuddyIdentityState` split defined by the multi-Buddy Scene source alignment.

---

# 7. System group B — Win98 Scenes

Required Demo Scene strip:

```text
[ Home ] [ Lab ] [ + ]
```

with a maximum of two Scene documents.

Each Scene owns:

- name;
- painted background;
- wallpaper/environment state;
- placed decorations;
- Buddy roster and safe placement anchors;
- systemic sandbox graph;
- selected Room Physics preset/value subset;
- local Blueprint placements where relevant.

Required actions:

- switch Scene;
- create;
- rename;
- duplicate;
- delete with confirmation.

Scene switching must save/deactivate the outgoing Scene and activate the target without leaving hidden physics worlds running.

---

# 8. System group C — Room customization slice

The existing Steam Demo already demonstrates Paint Background. Keep it.

Promote a **limited Room Decorator catalogue** into the Next Fest Demo instead of keeping the entire decorator Full Release-only.

Recommended Demo decoration representatives:

- one floor lamp;
- one sofa/chair;
- one table;
- one wall painting/poster;
- one plant;
- one wallpaper.

These remain primarily visual decorations in the Demo unless a specific item is reused by the device system.

Full Release retains:

- the larger decoration catalogue;
- additional wallpapers;
- functional furniture interactions;
- deeper room content.

The Scene migration must make the background and decorations Scene-local before systemic sandbox persistence is finalized.

---

# 9. System group D — Construction parts

The Demo should include only the smallest set that can generate many structures.

Required:

1. **Wood Beam**
2. **Metal Block/Plate**
3. **Wheel**

Optional fourth part only if needed by the vertical-slice feel gate:

4. **Small Wood Block**

Do not ship separate short/medium/long variants merely to inflate the catalogue if bounded resizing/scaling can safely serve the same purpose. If scale is not safe in the first implementation, author one additional beam length rather than generalizing scale prematurely.

Player should be able to build at least:

- a hanging sign/pendulum;
- a simple cart;
- a crude launcher/trap.

Full Release adds the broader construction catalogue: additional beams, plates, glass, rubber, counterweights, platforms, hazards, gears and later specialist pieces.

---

# 10. System group E — Constraints / joints

Demo constraint vocabulary:

1. **Rope / world anchor**
2. **Weld**
3. **Hinge**

This is enough to cover hanging, rigid structures and rotating mechanisms.

Use Desktop Buddy's project-owned routed constraint architecture where required; do not introduce a dependency on Godot `PinJoint2D` motors merely for familiarity.

Full Release candidates:

- Spring;
- Slider;
- motorized hinge;
- breakaway joint;
- gear linkage;
- more advanced rope options.

---

# 11. System group F — Properties

The Demo exposes the real Win98 `Properties` framework with a deliberately tiny shared vocabulary.

Generic object properties:

1. **Mass**
2. **Bounce**
3. **Gravity Scale**
4. **Frozen**

This is the minimum set that immediately communicates MaD2/PPG-style editability.

For the weapon-specific showcase, use the **Shotgun**:

1. **Fire Rate / cadence**
2. **Spread**
3. **Knockback**

Do not expose correctness-sensitive values such as unconstrained projectile speed simply because they exist in `GunProfile`.

Modified Demo weapon variants remain economy-safe:

- physical tuning may vary;
- payout cannot exceed the canonical reward envelope;
- imported presets cannot create extra money.

Full Release adds the larger bounded property vocabulary per item family.

---

# 12. System group G — Devices and automation

Demo device vocabulary:

1. **Push Button**
2. **Timer**
3. **Piston**
4. **Weapon Trigger Adapter**

Required Demo graph examples:

```text
Button -> Piston
Button -> Timer -> Piston
Button -> Weapon Trigger -> Shotgun/Pistol
```

If one additional output substantially improves teaching/visibility, add **Lamp** as the fifth device because `Button -> Lamp` is the clearest possible first signal tutorial.

Do not add a broad logic catalogue yet.

Full Release adds:

- Toggle Switch;
- Pressure Plate;
- Proximity Sensor;
- NOT / AND / OR / XOR;
- Delay/latch/memory;
- Fan;
- Electromagnet;
- Heater;
- conveyor/motor;
- doors and more adapters.

The signal engine itself must already be the safe deterministic full system; only the Demo's allowed device definitions are restricted.

---

# 13. System group H — Materials and destruction

Demo materials:

1. **Wood**
2. **Metal**

Demo destruction behavior:

- Wood has one visible damage/break state and one reliable authored break outcome;
- Metal can take damage or resist more strongly, but does not need a broad deformation system;
- fire can affect Wood if the thermal path is stable enough for the slice;
- no arbitrary polygon cutting.

Full Release expands material breadth and destruction outcomes.

---

# 14. System group I — Buddy structural consequences

The Demo should prove systemic Buddy consequences without taking on the risky full detachment refactor yet.

Required:

- per-part structural integrity `0..100`;
- clear healthy / damaged / critical feedback;
- severe impacts, piercing/explosive/fire events feed the structural model through trusted channels;
- Gore Mode remains optional and presentation-only relative to structural rules;
- structural damage is independent per Buddy identity.

Not in the Next Fest Demo:

- physical limb detachment;
- prosthetics;
- arbitrary replacement limbs;
- head detachment.

Those are strong Full Release expansion hooks after the six-body assumptions are safely refactored.

---

# 15. System group J — Repair

Use the **existing Repair Kit** rather than creating another Demo-only repair tool.

For the Next Fest slice a successful Repair Kit use should:

- keep its existing care/status behavior;
- restore a meaningful amount of structural integrity;
- visibly clear/reduce structural damage presentation;
- never duplicate body parts or alter permanent appearance.

Also provide a free fail-safe:

```text
Control Panel -> Buddy -> System Restore...
```

System Restore exists to guarantee that experimentation cannot permanently ruin a Buddy save.

Full Release can later add more tactile repair tools or repair furniture if desired.

---

# 16. System group K — Room Physics

The Demo should include one extremely recognizable Room Physics control:

## Gravity

Recommended presets:

```text
Normal
Low
Zero
```

or a small bounded slider if it remains understandable and Buddy stability is proven across the range.

Do not expose the entire Room Physics roadmap in the Demo.

Full Release later adds:

- broader gravity range;
- wind;
- temperature;
- time-scale controls;
- fire spread tuning;
- additional simulation presets.

This one setting gives both reference communities an immediately familiar sandbox experiment without requiring the whole environmental simulation system.

---

# 17. System group L — Build/edit QoL

The Demo must include enough editing quality-of-life that building does not feel like a prototype.

Required selection/edit commands:

- select one;
- multi-select if stable by the Demo polish gate;
- move;
- rotate;
- Freeze/Unfreeze;
- Duplicate;
- Delete;
- Properties;
- undo/redo for deliberate editor commands;
- Pause/Play simulation.

Recommended context menu:

```text
Properties...
Freeze / Unfreeze
Duplicate
Delete
Save Selection as Blueprint
```

Do not spend the Next Fest schedule on broad scene-pose tooling before the build loop itself is comfortable.

---

# 18. System group M — Local Blueprints

Blueprints are required because creating a contraption and then losing it makes the construction slice feel disposable.

Demo:

- save selected construction as a local Blueprint;
- name it;
- preview/place it;
- delete it;
- maximum 5 local Blueprints;
- Blueprint uses the same validated declarative schema planned for Full Release.

A Demo Blueprint may contain only Demo-available:

- entity definitions;
- property overrides;
- constraints;
- wires;
- devices.

Full Release removes/raises the product cap and supports the larger definition catalogue.

---

# 19. System group N — Steam Workshop contraptions

Because Steam Workshop already ships in the Steam Demo, contraption sharing is a high-value final Next Fest slice once local Blueprints are stable.

Use one contraption package format for Demo and Full Release.

Add an explicit compatibility classification conceptually equivalent to:

```text
DemoCompatible
FullReleaseRequired
```

A package is Demo-compatible only when every included:

- definition ID;
- property ID/value;
- device type;
- constraint type;
- graph count;
- schema feature

is valid under the Demo whitelist and caps.

### Demo behavior

- may publish only Demo-compatible contraptions;
- in-game browser shows/imports only Demo-compatible contraptions;
- manually placed or malicious Full-Release packages are rejected before instantiation;
- installing a Blueprint never grants locked ownership/content.

### Full Release behavior

- may publish Demo-compatible or Full-Release contraptions;
- can browse/import both;
- can use all valid first-party definitions it owns/is allowed to instantiate.

This directly supports the earlier owner goal that Demo players can share among the Demo-compatible ecosystem while Full Release players can consume that content too.

If live Steam Workshop contraption publishing threatens the Next Fest date, **local Blueprints remain the hard launch gate and Workshop contraptions are the first post-demo patch candidate**. Do not weaken validation to hit a date.

---

# 20. What remains Full Release-only

The Full Release should visibly have room to grow.

Examples held back from the Next Fest Demo:

## Multi-Buddy / Scenes

- more than 2 simultaneous Buddies;
- more than 2 Scenes;
- larger Scene rosters;
- future Buddy-to-Buddy social/relationship behaviors.

## Construction

- larger part catalogue;
- specialist materials;
- glass/rubber/plastic/fabric breadth;
- Springs and advanced constraints;
- motors/gear systems;
- advanced destruction.

## Devices

- sensors;
- logic-gate catalogue;
- electromagnets;
- fans/heaters;
- conveyors;
- motorized doors;
- richer signal utilities.

## Properties

- broad per-tool and per-device tuning;
- larger material/function property catalogue;
- shareable customized item/weapon variants.

## Buddy damage

- detachable limbs;
- reattachment/prosthetics;
- deeper electrical/cutting state.

## Rooms / RP

- larger decoration catalogue;
- functional furniture;
- advanced Room Physics;
- pose/scene tools;
- deeper secrets/Easter eggs.

## Workshop

- Full-Release-only contraptions;
- future item variants;
- broader content libraries.

The Demo should communicate these locked categories without advertising features that are not actually approved/implemented for Full Release.

---

# 21. Demo -> Full Release continuity

A player who buys the Full Release after Next Fest should keep their work.

Required continuity:

- Buddy identities survive;
- custom Character documents/paint survive;
- both Demo Scenes survive;
- painted backgrounds survive;
- Demo decorations survive;
- Demo systemic sandbox graphs survive;
- local Demo Blueprints survive;
- imported Demo-compatible Workshop Blueprints survive;
- account currency/unlocks/statistics migrate normally.

On first Full Release boot:

- Demo caps disappear;
- existing Scene/Blueprint IDs are preserved;
- no duplicate Buddy identities are created;
- no Demo content is silently replaced;
- Full-Release-only catalogue entries simply become newly available according to progression/ownership rules.

Full Release -> Demo downgrade safety remains fail-closed:

- Demo can ignore or mark unsupported Full-Release Scenes/Blueprints as `Requires Full Game`;
- Demo must never delete or rewrite unsupported Full-Release-only content merely because it cannot instantiate it.

---

# 22. Progression/onboarding for Next Fest

The Next Fest Demo should put the systemic sandbox in the player's hands quickly.

Recommended pacing:

### First 5 minutes

- existing Buddy tutorial fundamentals;
- customize/use a Buddy;
- introduce Scene strip;
- basic Grab / Pet.

### First 10–15 minutes

- unlock/open Build Mode;
- place Beam + Block + Wheel;
- teach Rope/Weld/Hinge through one small construction objective.

### Next 10 minutes

- Properties;
- Button -> Piston;
- first Blueprint save.

### Sandbox thereafter

- damage tools unlock rapidly enough to encourage experimentation;
- second Buddy and second Scene become available early enough to demonstrate the RP direction during an ordinary Next Fest session.

Do not make players grind most of the existing 2.5-hour progression before reaching the features that are the reason for this updated Demo.

A separate Demo progression tuning pass should define exact credit prices and unlock ordering after the systems are playable.

---

# 23. Suggested Demo tool unlock order

Provisional order for testing, not economy-lock until feel calibration:

```text
Grab / Pet
Meal
Baseball Bat
Build Mode basics
Pistol
Sword
Grenade
Shotgun
Fire Sprayer
Repair Kit
```

Rope/Weld/Hinge and the first Beam/Block/Wheel definitions should be introduced as construction capabilities rather than hidden deep in the combat-tool grind.

The order should create recognizable escalation while ensuring Build Mode is encountered early.

---

# 24. Revised implementation order

The previous Full Release planning order must change because the Demo now depends on the same foundations.

## NF-0 — Source alignment and distribution policy

Implement/record:

- `DemoScope.IsSteamDemo`;
- shared-slice vs Full-only capability policy;
- definition/property availability metadata;
- direct API tests proving Demo cannot instantiate Full-only content;
- export scope tests for `steam_demo`, `full_release`, `itch_io`.

Exit:

- scope is fail-closed at UI, catalogue, runtime spawn, persistence load and Workshop validation layers.

## NF-1 — Account/Buddy state split

Implement the `PlayerProgressState` / `BuddyIdentityState` separation required for multiple Buddies.

Migrate current one-Buddy save deterministically.

Exit:

- two Buddy identities can hold different mood/hunger/memory/personality state while sharing one wallet/unlock ledger.

## NF-2 — Production multi-Buddy runtime

Implement the Scene-owned Buddy actor runtime and stable routed ticking.

Exit:

- two different custom Buddies coexist in ordinary gameplay and tools/care route to the correct one.

## NF-3 — Scene documents + Win98 tabs

Implement:

- max-two Demo Scenes;
- Scene persistence;
- tabs;
- create/rename/duplicate/delete/switch;
- Scene-local background/decorations/Buddy placements.

Exit:

- restart restores both Scenes and their rosters.

## NF-4 — Curated Room Decorator slice

Promote the small Demo decoration set and make the existing editor Scene-aware.

## NF-5 — Build/edit foundation

Implement:

- systemic entity registry;
- Beam/Block/Plate/Wheel subset;
- selection/move/rotate/freeze/duplicate/delete;
- Pause/Play.

Exit:

- player can build a free-standing arrangement without debug UI.

## NF-6 — Constraints + Properties

Implement:

- Rope;
- Weld;
- Hinge;
- Mass/Bounce/Gravity/Frozen;
- Shotgun cadence/spread/knockback overrides.

Exit:

- cart/pendulum and visibly different Shotgun variants work reliably.

## NF-7 — Devices + gravity

Implement:

- Button;
- Timer;
- Piston;
- Weapon Trigger;
- optional Lamp;
- Normal/Low/Zero Gravity.

Exit:

- one automated weapon trap and one non-weapon mechanism can be built entirely through player UI.

## NF-8 — Materials + structural Buddy damage + repair

Implement:

- Wood/Metal;
- Wood breakage;
- per-Buddy structural integrity;
- damaged/critical feedback;
- Repair Kit integration;
- System Restore.

Exit:

- damage -> visible consequence -> repair loop is reliable across both active Buddies.

## NF-9 — Scene sandbox persistence + local Blueprints

Implement:

- Scene-local `sandbox.json`;
- debounced topology/configuration persistence;
- local Blueprint save/load/placement;
- five-Blueprint Demo cap.

Exit:

- restart reproduces a meaningful contraption and both Scene setups.

## NF-10 — Workshop contraption compatibility

Implement only after NF-9 is stable:

- contraption package type;
- Demo compatibility validation;
- publish/install/import pipeline;
- Demo browse filtering;
- Full Release accepts Demo-compatible content.

## NF-11 — Next Fest curation/polish

- tutorial/onboarding rewrite for new flow;
- economy/unlock pacing;
- locked Full Game previews;
- SFX/VFX feedback;
- performance profiling;
- input/window/work-mode transitions;
- Workshop live-Steam checks;
- capture-ready presentation;
- accessibility;
- soak testing.

---

# 25. Performance gates

Multi-Buddy + systemic entities increases the physical workload enough that performance must be measured continuously rather than at NF-11 only.

Required Demo stress cases:

```text
nextfest_two_buddies_baseline
nextfest_two_buddies_24_dynamic
nextfest_32_entities_32_constraints
nextfest_16_devices_48_wires
nextfest_fire_plus_two_buddies
nextfest_scene_switch_stress
nextfest_blueprint_repeated_place_delete
```

Keep the one routed gameplay tick architecture.

Do not introduce per-entity `_PhysicsProcess` callbacks as the easy way to ship the Demo.

---

# 26. Required scope tests

Automated scope matrix must cover at least:

```text
Steam Demo        steam + steam_demo
Full Release      steam + full_release
itch.io           itch_io
untagged/debug     fail-closed unless explicit test override
```

For each build verify:

- Build Mode command visibility;
- Scene cap;
- active Buddy cap;
- construction catalogue;
- device catalogue;
- property allowlist;
- existing gameplay-tool catalogue;
- Room Decorator catalogue;
- Blueprint import;
- direct runtime spawn;
- Workshop import;
- Full-Release-only package rejection in Demo.

A UI-hidden Full-only definition that can still be spawned by a Blueprint/import/API is a failed gate.

---

# 27. Next Fest acceptance journey

The Demo is ready only when a clean Steam Demo player can complete this journey without development tools:

1. launch the Demo;
2. create/customize Buddy A;
3. create/customize Buddy B;
4. place both in `Home`;
5. interact with each independently and observe independent state;
6. paint or decorate the room;
7. create a second Scene called `Lab`;
8. switch to `Lab` through the Win98 Scene tab;
9. place a Beam, Block/Plate and Wheel;
10. connect a Rope/Weld/Hinge structure;
11. edit Mass/Bounce/Gravity/Frozen on an object;
12. modify the Shotgun's allowed properties;
13. wire Button -> Timer -> Piston or Weapon Trigger;
14. change the room to Low or Zero Gravity;
15. use a familiar weapon/tool against a Buddy or breakable Wood part;
16. observe structural damage;
17. repair the Buddy with the Repair Kit;
18. save the contraption as a Blueprint;
19. switch to `Home` and back to `Lab`;
20. restart the game;
21. verify both Scenes, both Buddies, room state and contraption return;
22. if NF-10 ships, publish/install a Demo-compatible contraption through the in-game Workshop browser;
23. encounter clear Full Game locks on additional content without any broken/empty controls.

If this flow is fun and stable, the Demo communicates the Full Release direction even though most of the content breadth remains locked.

---

# 28. Full Release value proposition after this Demo

The Next Fest Demo intentionally gives away the **verbs** but limits the **vocabulary**.

Demo players learn that they can:

- create a cast;
- create Scenes;
- build;
- connect;
- modify;
- automate;
- destroy;
- repair;
- save/share.

The Full Release then expands what those verbs can act on:

- more Buddies and Scenes;
- many more tools;
- more construction parts;
- more materials;
- more constraints;
- more devices and logic;
- more editable properties;
- advanced damage/detachment;
- bigger room/decor catalogue;
- functional furniture;
- richer Room Physics;
- larger Blueprint/Workshop ecosystem;
- Full-Release-only contraptions and later item variants;
- other already-approved Full Release systems such as Potion Shop, expanded Buddy Studio, accessories and voice features.

That is a stronger Demo/full-game split than withholding the systems entirely: players can understand why the game is interesting, while the purchase unlocks substantially more combinatorial possibility.
