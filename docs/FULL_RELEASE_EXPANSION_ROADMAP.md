# Desktop Buddy — Full Release Expansion Roadmap

Status: **Owner-approved direction; begins after the Steam demo ships**  
Recorded: 2026-08-11  
Updated: 2026-09-07 — multi-Buddy Scenes promoted to high-priority Full Release foundation

This roadmap collects the currently approved post-demo expansion directions into one sequence. Detailed implementation plans remain authoritative for the systems that already have them, especially `docs/FULL_RELEASE_MULTI_BUDDY_SCENES_SOURCE_ALIGNMENT_2026-09-07.md`, Potion Shop, Environment Customization, Buddy Studio, and the Full Release systemic-sandbox plans.

The Steam demo is intentionally narrower: one room/profile, one live Buddy, authored non-physical room items, the current Buddy Studio, Work Mode, Paint Buddy/Background, core tools, and data-only Workshop v1 sharing for room paintings and Buddy Studio configuration plus declared buddy paint. **All multi-Buddy Scene work and the systemic-sandbox expansion are Full Release-only.** Potion Shop temporary effects are also Full Release scope and must not be pulled back into the Steam Demo unless the owner explicitly reverses that decision.

---

## RELEASE-0 — platform / data prerequisites

Workshop v1 prerequisites were promoted into the Steam Demo program. Before expanding shareable content for the Full Release:

- complete the Milestone 6 local/Steam platform abstraction during the Steam Demo program;
- establish stable cloud/local data boundaries;
- retain the safe versioned package/import primitives delivered for Workshop v1;
- retain the approved Steam UGC/Workshop policy, moderation and failure behavior;
- rehearse migrations from the Steam demo data formats;
- ensure missing/unsubscribed shared content degrades safely without corrupting saves.

No arbitrary script/mod loader is introduced as a prerequisite for these features.

---

## RELEASE-SCENE — multi-Buddy Scenes / RP foundation

Reference: `docs/FULL_RELEASE_MULTI_BUDDY_SCENES_SOURCE_ALIGNMENT_2026-09-07.md`.

This is promoted to a high-priority Full Release architecture foundation because later room, systemic-sandbox and contraption persistence should be Scene-owned from their first durable implementation rather than being built around one global room and migrated immediately afterwards.

### RELEASE-SCENE0 — account/Buddy state separation

Split the current single-Buddy persistent semantics so the player retains one shared account economy/tool inventory while each Buddy identity can retain its own mood, hunger/fullness, harmful memory, traits/preferences and fun/novelty state.

The existing one-Buddy Demo behavior must remain bit-for-bit compatible through migration/facade seams.

### RELEASE-SCENE1 — multiple live Buddy actors

- productionize the existing two-`BuddyRoot` laboratory proof into normal Play Mode;
- support several created Buddies in one active room;
- each Buddy has its own appearance/paint binding, autonomy, recovery, reactions and Buddy-specific persistent state;
- shared player tools/pointer/grab can target any Buddy;
- no intentional Buddy-to-Buddy behavior or relationship system yet;
- initial Buddy bodies continue not to collide with Buddy bodies, matching the existing collision-layer contract and keeping Buddy-to-Buddy interaction out of scope;
- use **4 simultaneously active Buddies** as the first engineering/performance target, not yet a permanent product cap.

### RELEASE-SCENE2 — Scene library and Win98 Scene tabs

A Scene is a complete roleplay setup rather than merely a room profile:

- named Scene;
- painted background;
- wallpaper/decorations;
- Buddy roster and safe placement anchors;
- later systemic-sandbox/contraption state.

Provide Win98-styled fast switching, conceptually:

```text
[ Home ] [ Garage ] [ Lab ] [ + ]
```

Initial operations:

- create;
- rename;
- duplicate;
- delete;
- switch;
- add/remove/place Buddies.

Only one Scene simulates at a time. Inactive Scenes are persisted and paused rather than hidden live physics worlds.

### RELEASE-SCENE3 — deterministic Demo migration

On first Full Release load:

- convert the Demo's one environment into a default Scene;
- convert the existing persistent Buddy into one Buddy identity/placement;
- retain its selected Character appearance;
- preserve wallet, unlocks, statistics, Work data and other account progress;
- migrate the current painted background into the Scene-specific asset root atomically.

### RELEASE-SCENE4 — switch/persistence/polish gate

- safe placement-anchor persistence without serializing six-body ragdoll poses/velocities;
- save/restart restores Scenes and rosters;
- Work Mode uses one focused Buddy and suspends the rest of normal Play Mode;
- character deletion/missing appearance references degrade safely;
- character paint residency is explicitly budgeted for multiple active Buddies;
- Scene switching is tested under resize/DPI/modal-editor and repeated-switch soak conditions.

Buddy-to-Buddy conversations, relationships, coordinated activities, fighting, shared-object behavior and other social AI remain a later program.

---

## RELEASE-POTION — Potion Shop / temporary buddy effects

Reference: `docs/POTION_SHOP_CONCEPT.md`.

Potion Shop remains an early Full Release feature after the Steam demo ships/stabilizes. It provides temporary, highly visible buddy effects without mutating permanent Paint Buddy or Buddy Studio data.

Start with a small polished initial set rather than a large catalogue. Candidate ideas include:

- temporary tail;
- glossy/shiny treatment;
- RGB/cycling-color effect;
- glow-in-the-dark treatment, potentially reacting to a flashlight;
- metallic treatment with matching SFX and a possible gameplay modifier only if separately approved;
- poison/sickness effect;
- flashlight as a possible separate buyable toy/effect companion.

Before implementation, explicitly lock:

- initial effect set;
- purchase/consume model;
- durations and whether timers advance in Work/hidden modes;
- stacking/compatibility policy;
- normal credits vs Work Mode reward integration;
- reset/restart/mode-transition cleanup;
- active-effect HUD/status treatment;
- reduced-motion/flashing/accessibility treatment;
- VFX/SFX requirements.

Do not add a second economy ledger by assumption. Any gameplay-changing potion uses an explicit trusted authored effect policy rather than arbitrary scripting.

Exit gate:

- purchase/use/expiry is understandable without debug UI;
- effects cleanly restore prior buddy visual/gameplay state;
- no stuck effect survives restart/reset/mode changes;
- Paint Buddy, Buddy Studio, room/environment, physics, tools and saves remain intact;
- initial entries have production-quality VFX/SFX and owner acceptance.

---

## RELEASE-ENV — Scene environment expansion

Environment expansion now builds on RELEASE-SCENE rather than introducing a separate competing room-profile system.

### RELEASE-ENV1 — Scene-owned environment editing

- each Scene owns its own environment layout and painted background;
- existing Environment Decorator/Paint Background target the active Scene;
- Scene duplication duplicates the environment configuration and local painted-background asset safely;
- environment assets remain isolated and atomic.

### RELEASE-ENV2 — complete Scene/room sharing through Steam

Only after the local Scene format is stable:

- safe versioned room/Scene package policy;
- share wallpaper/background paint plus placed decoration configuration and compatible authored content references;
- decide separately whether Buddy roster references belong in the first shared Scene package;
- validate imported package paths, dimensions, IDs and size caps;
- downloaded content receives safe local identities;
- missing content uses non-destructive fallback behavior.

### RELEASE-ENV3 — authored buddy/furniture interactions

Turn selected furniture from visual-only decoration into trusted authored interaction targets.

Examples include:

- sit/rest on chairs/sofas;
- watch/use a TV or screen;
- inspect/toggle lamps or other authored props;
- context-sensitive idle activities around room objects.

These are project-authored capabilities, not arbitrary scripts embedded in room files. Furniture interactions must preserve ragdoll safety and provide deterministic escape/recovery when an item is moved/deleted while in use.

---

## RELEASE-SBX — systemic sandbox / construction

References:

- `docs/FULL_RELEASE_SYSTEMIC_SANDBOX_IMPLEMENTATION_PLAN.md`;
- `docs/FULL_RELEASE_SYSTEMIC_SANDBOX_CODEBASE_AUDIT_2026-09-07.md`.

The initial systemic-sandbox vertical slice follows the Scene foundation so all durable construction state is Scene-owned from day one.

Initial slice remains intentionally smaller than the long-term MaD2/People Playground-inspired backlog:

- Build/Edit mode;
- select, move, rotate, freeze, duplicate, delete, Properties;
- Beam, Block, Wheel;
- Mass, Bounce, Gravity Scale, Frozen;
- Shotgun Fire Rate, Spread, Knockback customization;
- project-owned routed constraints: Weld, passive Hinge, Rope/World Anchor;
- Button, Timer, Piston, Weapon Trigger;
- Wood and Metal with basic durability;
- one reliable Wood breakage path;
- Buddy structural integrity before physical limb detachment;
- Repair Kit + System Restore recovery;
- Scene-local contraption persistence/local blueprints before Workshop sharing.

Broader materials, logic gates, sensors, electricity, heat, physical limb detachment, advanced Room Physics, functional furniture, scene posing and Workshop contraptions follow after this slice proves the reusable system architecture.

---

## RELEASE-BS — Buddy Studio expansion

The detailed implementation sequence remains in `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md`.

Core approved goals:

### RELEASE-BS1..3 — player-drawn cosmetics

Players receive trusted category-specific drawing templates and paint their own Hair, Eyes, Mouth, Tops, Shoes and other supported cosmetic visuals. The resulting art maps to the existing trusted Buddy Studio anchor/render pipeline.

The project uses the generic clean-room idea `paint constrained template -> map onto a trusted buddy region`; no Nintendo/Drawn to Life art, UI or source behavior is copied.

### RELEASE-BS4 — bounded cosmetic stretching/deformation

Supported cosmetics gain small, safe local deformation controls so players can stretch/squash parts of the cosmetic while the authored attachment point stays fixed. This remains visual-only and cannot modify buddy physics or Paint Buddy UV geometry.

### RELEASE-BS5 — Steam sharing for player-made cosmetics

Share/import safe declarative custom-cosmetic packages through the Steam platform/UGC layer. User packages cannot provide arbitrary scripts, scenes, shaders, meshes or executable behavior.

### RELEASE-BS6 — larger Buddy Studio UX redesign

Rework Studio around the larger full-release library:

- Browse / Equip;
- My Creations;
- Create / Edit;
- Shared / Steam.

The current demo UI is not required to scale unchanged to these workflows.

---

## RELEASE-ACC — interactive accessories / authored gadgets

The demo may hide the current Accessories category if it cannot offer a strong finished selection. Full release should bring Accessories back as a deliberately more special category rather than another set of static decals.

### Product direction

Accessories may have trusted authored behaviors tied to the buddy, for example:

- a phone held in the buddy's hand that the buddy occasionally checks;
- an authored passive-income interaction associated with the phone;
- handheld toys/props with small idle animations;
- context-sensitive accessories that react to mood, Work Mode, room furniture, or other safe game state.

### Architecture boundary

An accessory definition may reference a **project-owned behavior capability ID** with bounded parameters. Character/custom content files cannot name arbitrary scripts or scenes.

The system should distinguish:

- static cosmetic accessories;
- authored interactive accessories;
- room/furniture interactions.

Do not turn the Accessories slot into a generic scripting/mod interface.

### Economy boundary

If an interactive accessory generates passive money, its rate and conditions belong to the normal economy/reward model and must be calibrated with Work Mode, Potion Shop and other passive sources. Equipping multiple accessories must not create uncontrolled income stacking.

---

## RELEASE-VOICE — player voice recordings for buddy reactions

Add optional local player-recorded voice clips that can be assigned to buddy reaction/action categories.

The inspiration is the playful voice-transformation idea seen in older handheld/toy software; implementation, UI, filters and assets must be original.

### Intended workflow

Players can:

- record one or more clips;
- assign clips to authored trigger groups such as damage reactions, happy reactions, or occasional random noises;
- store multiple recordings per trigger so playback can vary;
- preview recordings before assigning them;
- disable/remove recordings without affecting the buddy save.

### Voice filter

Provide an optional goofy voice transformation with a simple intensity control, for example:

```text
Off -> Light -> Heavy
```

or a continuous Light-to-Heavy slider if the audio implementation remains understandable and performant.

### Safety / privacy / storage

- microphone capture is opt-in and clearly signposted;
- recordings remain local by default;
- raw audio is never uploaded merely because Steam is running;
- define per-clip and aggregate storage caps;
- use safe whitelisted audio formats;
- normalize volume/peak levels to avoid painful playback;
- provide microphone/device failure recovery;
- recording data is separate from core character physics/progression data.

Steam sharing of voice recordings is **not currently approved** and should not be inferred from cosmetic/room sharing.

---

## RELEASE-TUTORIAL — in-world office helper

Add a lightweight contextual tutorial/help character/object with an office-computer flavor, but not a copy of Clippy.

Current concept: an original animated office item such as a **pen** that can appear when the player is new to a system or asks for help.

### Goals

- explain unusual Desktop Buddy interactions without permanent tutorial overlays;
- teach first-use flows such as grabbing, buying/equipping, Work Mode exit, painting, decorating and Potion Shop effects;
- provide short contextual tips rather than long modal tutorials;
- be dismissible and optionally disabled;
- remember completed teaches so it does not repeatedly interrupt experienced players.

### Clean-room rule

Do not copy Microsoft's Clippy character, wording, animation, art or presentation. Use an original office-themed helper and Desktop Buddy's own Win98 visual language.

---

## RELEASE-UGC — consolidated Steam sharing experience

After safe local formats exist for Scenes/rooms and custom cosmetics, unify the player-facing Steam sharing/install experience without creating an unrestricted mod loader.

Approved/considered shareable units must each pass their own owner gate and validator. Existing approved units remain room paintings and Buddy Studio configuration + declared paint. Full Scene packages, contraptions and customized-item presets are added only after their local declarative formats are stable and separately source-aligned.

Each format keeps its own validator and schema. Shared content must be declarative, bounded and recoverable when unavailable.

---

## RELEASE-POLISH — full release content and UX pass

After the expansion systems are real, run another deliberate polish phase rather than assuming the Steam demo polish scales automatically.

Include:

- multi-Buddy Scene performance, persistence and switching polish;
- Potion Shop lifecycle/economy/VFX/SFX polish;
- full Buddy Studio UX revamp verification;
- large cosmetic/Scene library performance;
- systemic-sandbox/contraption performance and safety;
- interactive accessory/furniture animation polish;
- voice recording UX/audio polish;
- tutorial helper timing/copy polish;
- final progression/economy recalibration across active tools, Work Mode, accessories, furniture, potions and other passive sources;
- final original item/cosmetic/environment art;
- VFX/SFX coverage;
- accessibility and reduced-motion/flashing options;
- Steam install/uninstall/offline/shared-content failure paths;
- clean-room/IP audit;
- four/eight-hour soak and Windows DPI/multi-monitor matrices.

---

## Still deferred beyond the currently approved full-release program

Unless promoted by a later owner decision:

- unrestricted scripting/mod loader;
- arbitrary user-authored meshes/shaders/scenes;
- Buddy-to-Buddy social AI/relationships/coordination;
- multiplayer;
- Linux/macOS ports;
- broad advanced painting suite such as unrestricted 3D orbit painting, tablet pressure, arbitrary custom brushes, blend-mode/layer systems and generalized material editing;
- Steam sharing of player microphone recordings.
