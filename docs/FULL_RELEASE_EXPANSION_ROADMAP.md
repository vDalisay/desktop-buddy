# Desktop Buddy — Full Release Expansion Roadmap

Status: **Owner-approved direction; begins after the Steam demo ships**  
Recorded: 2026-08-11

This roadmap collects the currently approved post-demo expansion directions into one sequence. Detailed implementation plans remain authoritative for the systems that already have them, especially Potion Shop, Environment Customization and Buddy Studio.

The Steam demo is intentionally narrower: one room/profile, authored non-physical room items, the current Buddy Studio, Work Mode, Paint Buddy/Background and core tools. **Potion Shop temporary effects are now Full Release scope and must not be pulled back into the Steam Demo unless the owner explicitly reverses that decision.**

---

## RELEASE-0 — platform / data prerequisites

Before user-generated or shareable content:

- complete the Milestone 6 local/Steam platform abstraction during the Steam Demo program;
- establish stable cloud/local data boundaries;
- finish safe versioned package/import primitives before UGC work;
- define Steam UGC/Workshop policy, moderation and failure behavior;
- rehearse migrations from the Steam demo data formats;
- ensure missing/unsubscribed shared content degrades safely without corrupting saves.

No arbitrary script/mod loader is introduced as a prerequisite for these features.

---

## RELEASE-POTION — Potion Shop / temporary buddy effects

Reference: `docs/POTION_SHOP_CONCEPT.md`.

Potion Shop is the first newly promoted Full Release feature after the Steam Demo ships/stabilizes. It provides temporary, highly visible buddy effects without mutating permanent Paint Buddy or Buddy Studio data.

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

## RELEASE-ENV — Environment expansion

Existing approved Environment full-release scope remains:

### RELEASE-ENV1 — multiple local room profiles

- multiple named local rooms/environments;
- create, rename, duplicate, delete and switch rooms;
- active-room selection persists safely;
- room assets remain isolated and atomic;
- migration from the demo's single-room save is deterministic.

### RELEASE-ENV2 — complete-room sharing through Steam

- safe versioned room package;
- share wallpaper/background paint plus placed decoration configuration and compatible authored content references;
- validate imported package paths, dimensions, IDs and size caps;
- downloaded rooms receive safe local identities;
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

After safe local formats exist for rooms and custom cosmetics, unify the player-facing Steam sharing/install experience without creating an unrestricted mod loader.

Approved shareable units currently are:

- complete room configurations;
- player-created Buddy Studio cosmetics.

Each format keeps its own validator and schema. Shared content must be declarative, bounded and recoverable when unavailable.

Potential full Workshop/custom-buddy packages remain a separate future policy gate rather than an automatic consequence of these two sharing systems.

---

## RELEASE-POLISH — full release content and UX pass

After the expansion systems are real, run another deliberate polish phase rather than assuming the Steam demo polish scales automatically.

Include:

- Potion Shop lifecycle/economy/VFX/SFX polish;
- full Buddy Studio UX revamp verification;
- large cosmetic/room library performance;
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
- multiple simultaneous buddies;
- multiplayer;
- Linux/macOS ports;
- broad advanced painting suite such as unrestricted 3D orbit painting, tablet pressure, arbitrary custom brushes, blend-mode/layer systems and generalized material editing;
- Steam sharing of player microphone recordings.
