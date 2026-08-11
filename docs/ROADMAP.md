# Desktop Buddy — Implementation Roadmap

Status: **Current owner roadmap / agent handoff sequence**  
Updated: 2026-08-11

This document is the current sequencing authority. Older milestone plans remain useful for architecture/history, but stale status text inside those older documents does not override this roadmap.

The project is now split into two product targets:

1. **Steam Demo** — finish a compact, polished, marketable build from the systems already on `main`, plus the approved Potion Shop/demo-effect slice.
2. **Full Release** — expand Environment, Buddy Studio and the buddy's personality/content systems after the demo ships.

Milestones are sequential release gates unless a task is explicitly marked safe to parallelize.

---

# Completed foundation and feature milestones

## Milestone 0 — Foundation ✅ COMPLETE

Current baseline includes the Godot/.NET project structure, domain/test split, typed resources, logging, scenario/journey infrastructure, Windows export scaffolding and the established build/test tooling.

## Milestone 1 — Physics Laboratory ✅ COMPLETE

The six-part ragdoll, spring/stretch controller, autonomous movement, grab/tether behavior, tuning resources, seeded physics scenarios and recovery/safety infrastructure are established.

## Milestone 2 — Windows Desktop Shell ✅ COMPLETE

The Windows desktop-companion shell, transparency/window management, Work/Play input ownership, resize/fullscreen behavior, DPI/multi-monitor recovery seams and Win98 application chrome are established.

## Milestone 3 — Core Interaction and Damage ✅ COMPLETE

Core grabbing, pet/tickle/damage behavior, reactions, knockout/pain attribution and the interaction/economy bridge are established.

## Milestone 4 — Personality, Care and Persistence ✅ COMPLETE

Mood/trust/care behavior, passive progression, save/recovery, safe resume and long-lived progression state are established.

## Milestone 5 — Shop and Full Tool Catalogue ✅ COMPLETE / OWNER ACCEPTED

The confirmed tool catalogue and progression slice is implemented, including the twelve purchasable progression items, Grab/Power Grab behavior, economy calibration, reset service and automated progression coverage.

The current selectable tool/content architecture remains the base for the Steam-demo progression polish pass.

## Milestone 5.5 — Character Editor Phase A ✅ COMPLETE / MERGED

The trusted character schema/compiler, character library, parametric expression rendering, visual-only character swapping, editor working-copy flow and save/use/restart behavior are on `main`.

Reference: `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`.

## Milestone 5.6 — Paint Buddy / Character Painting Phase B ✅ COMPLETE / MERGED

The character-painting implementation is complete in the current product baseline, including trusted body-surface mapping, CPU paint surfaces, Undo/Erase behavior, persistence, runtime underlay rendering and save/use/restart coverage.

Later demo work also added Spray/Airbrush, Curved Line and semantic paint-toolbar icons through the Environment refinement pass.

Reference: `docs/M5_6_PHASE_B_COMPLETION.md`.

## Milestone 5.7 — Work Mode Typing Companion ✅ COMPLETE FOR DEMO / OWNER ACCEPTED

Implemented:

- global privacy-safe activity counting;
- session/lifetime counters;
- compact Work companion presentation;
- milestone rewards;
- Work glasses first-entry reward;
- crash-safe session progress;
- double-click return flow;
- low-cost gameplay suspension.

Remaining Work Mode work is now **demo polish/release verification**, not unfinished foundation. It is tracked in `docs/STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md`.

## Milestone 5.8 — Win98 / shared customization foundation ✅ COMPLETE / MERGED

The current in-scene Win98 shell, Customize routing, shared catalogue/category/value controls and reusable customization UI foundation are established.

## Milestone 5.9 — Environment Customization ✅ COMPLETE / OWNER ACCEPTED / MERGED

The Steam-demo Environment baseline is complete:

- one local room/profile;
- Paint Background;
- wallpaper layer;
- Environment Decorator;
- permanent decoration ownership/storage;
- six launch categories with authored entries;
- free placement/editing;
- room persistence;
- Spray/Airbrush;
- Curved Line;
- placeholder semantic paint icons.

Reference: `docs/ENVIRONMENT_DEMO_CLOSURE_STATUS.md`.

Full-release room profiles, Steam room sharing and authored furniture interactions remain intentionally deferred.

## Milestone 5.10 — Buddy Studio current-release implementation ✅ COMPLETE / OWNER ACCEPTED / MERGED

The current Buddy Studio is on `main` with:

- trusted multi-category cosmetic definitions/rendering;
- permanent cosmetic ownership;
- Buy/preview/equip/save boundaries;
- bounded fitting controls;
- named color-channel seam;
- deterministic Randomize;
- Work glasses integration;
- save/restart behavior;
- clean-room authored launch content.

Reference: `docs/BUDDY_STUDIO_DEMO_CLOSURE_STATUS.md`.

Player-drawn cosmetics, cosmetic deformation/stretching, Steam sharing and the larger Studio redesign are full-release work.

---

# Steam Demo completion program

## Milestone 5.11 — Potion Shop / temporary buddy effects 🟡 NEXT FEATURE SLICE

Add a dedicated Potion Shop for short-lived, highly visible buddy effects before the final demo polish pass.

Target roughly **three polished demo showcase effects/items** rather than a large catalogue.

Current candidate pool includes:

- temporary tail;
- glossy/shiny treatment;
- RGB/cycling-color treatment;
- glow-in-the-dark treatment, potentially interacting with a flashlight;
- metallic treatment with matching SFX and a possible authored gameplay modifier only if separately approved;
- poison/sickness effect;
- flashlight as a possible separate buyable toy/effect companion.

Exact demo entries, prices, durations, stacking rules and inventory/consume behavior are **not locked yet**.

Work Mode should contribute to the Potion Shop loop. Before implementation, decide whether this means:

- normal credits earned through Work Mode;
- a dedicated Work/AFK token;
- or Work milestones/discounts/free samples without a second currency.

Do not add a second economy ledger until that design choice is explicitly approved.

Detailed concept boundary: `docs/POTION_SHOP_CONCEPT.md`.

Exit gate:

- final demo effect subset approved;
- effect lifecycle/economy rules locked;
- all selected entries have real VFX/SFX and clear purchase/use feedback;
- mode changes/restart/reset cannot leave the buddy in a stuck effect state;
- existing Paint Buddy, Buddy Studio, room, tool and save behavior remains intact.

---

## Milestone 6 — Steam Demo Platform and Release Systems

Build the minimum robust Steam/release foundation required to distribute and verify the demo while preserving a fully functional local/non-Steam path.

Deliver:

- local and Steam platform implementations behind the same interface;
- Steamworks.NET lifecycle/bootstrap;
- graceful behavior when Steam is unavailable;
- cloud-safe progress payload boundaries;
- machine-local settings kept out of cloud progression where appropriate;
- queued/offline-safe stats and achievements;
- the confirmed achievement set;
- launch-with-Windows option and final tray/recovery integration;
- release export preset and build automation;
- SteamPipe/depot documentation/tooling;
- installed-build and clean-install validation.

Do **not** start room/cosmetic Steam UGC here. M6 should provide the safe platform foundation those full-release systems will later build on.

Exit gate:

- installed Steam demo build launches and saves correctly;
- offline/Steam-unavailable paths are safe;
- no proprietary Steam SDK files are committed improperly;
- direct local/non-Steam development launch remains usable;
- achievement/stat/save behavior survives restart and connectivity transitions.

---

## Milestone 6.5 — Steam Demo Polish and Content-Complete Pass

This is the major pre-public polish phase.

Primary goals:

- bug fixing and regression closure;
- progression/unlock clarity and pacing;
- Work Mode reward polish;
- Potion Shop integration/economy balance;
- final item/cosmetic/environment/potion assets;
- **SFX coverage across the entire demo**;
- consistent UI/UX across every editor/menu/system;
- accessibility/readability/DPI polish;
- clean first-session onboarding.

The owner polish backlog is now captured in:

`docs/STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md`

That plan currently includes the following approved demo-polish directions.

### Paint Background / Environment paint

- clearer Curved Line control-point/state feedback;
- `Save and Exit` wording;
- Bucket Fill naming;
- Eraser footprint/cursor correction;
- pressed/indented active-tool state;
- active shape name/status feedback.

### Buddy Studio

- stronger Mouth variations;
- hide weak/unfinished Accessories in the demo;
- single-click owned-item equip;
- much clearer unowned preview/Buy/Owned/Equipped states;
- actual item/appearance renders for thumbnails.

### Paint Buddy

- detachable/floating color palette;
- Mirror option;
- optional front+back simultaneous painting;
- Bucket Fill;
- one consistent color-picker component/interaction;
- move Turn/Zoom controls into the buddy preview frame;
- clear stale palette selection after eyedropper sampling;
- `Show limbs` painting pose/option.

### General shell/catalogue

- reduce excessive limb/ball rotation during normal animation;
- merge Tools + Shop into one Buy/Equip catalogue flow;
- outside click closes top-bar menus/dropdowns;
- appropriate panels can be dragged outside the play area/window;
- persistent active-tool status feedback;
- consistent action terminology across systems.

### Work Mode

- resize via LMB + mouse wheel interaction;
- auto-select normal Grab when returning to Play;
- reward/onboarding polish;
- correct active Buddy Studio appearance/cosmetic rendering;
- release soak/DPI verification.

### Decorate Room

- clearer button/mode hierarchy;
- deliberate money-value color treatment;
- remove Snap-to-grid from the demo UX unless later restored;
- remove floor/wall placement restrictions while retaining safe room bounds;
- dedicated Delete mode;
- simplify the nested placement/save flow;
- consistent wallpaper/furniture dirty/save behavior.

Exit gate:

- no known data-loss, stuck-input, duplicated-purchase, off-screen-window or stuck-effect defect;
- progression can be understood without debug knowledge;
- no visible placeholder item/control remains in public demo scope;
- final demo systems pass cross-feature regression and owner UX review.

---

## Milestone 6.6 — Steam Demo Marketing / Store Asset Production

Begin only after the demo is content-locked enough that final captures will not immediately become obsolete.

Prepare:

- Steam capsule/store/library art in every format required by the then-current Steamworks specification;
- main gameplay trailer;
- curated gameplay screenshots;
- short gameplay GIFs/loops;
- final logo/wordmark assets needed by the Steam page;
- store feature copy and demo messaging as required.

Marketing capture should deliberately show:

- desktop buddy/ragdoll interaction;
- memorable tools;
- Paint Buddy;
- Buddy Studio;
- Environment decoration/background painting;
- Work Mode;
- Potion Shop/effect showcase.

Exact Steam image dimensions/submission rules must be re-verified against official Steamworks guidance at asset-production time rather than hard-coded into this roadmap.

Reference: `docs/STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md`.

Exit gate:

- all required Steam art exists at production quality;
- trailer/storyboard/captures represent the actual demo build;
- screenshots/GIFs contain final UI and no debug/programmer assets;
- marketing package is internally reviewed before upload.

---

## Milestone 6.7 — Steam Demo Release Candidate

Freeze demo features and run the release matrix:

- full automated regression;
- installed depot;
- clean install/uninstall/reinstall;
- fresh-save progression run;
- supported save migration rehearsal;
- corruption/recovery paths;
- Steam online/offline transitions;
- 100/125/150/200% DPI;
- multi-monitor/window recovery;
- minimum/default/maximized/fullscreen layouts;
- four-hour active soak;
- four-hour Work Mode soak;
- hidden/tray soak;
- performance/memory review;
- accessibility/audio/readability review;
- final clean-room/IP audit.

**Steam Demo ships after this gate.**

---

# Full Release expansion program

Full-release work begins after the Steam demo is shipped/stable unless the owner explicitly promotes an item earlier.

Detailed cross-system roadmap:

`docs/FULL_RELEASE_EXPANSION_ROADMAP.md`

## Milestone 7.1 — Environment full-release expansion

- multiple named local room profiles;
- Steam sharing of complete room configurations;
- authored buddy/furniture interactions such as sitting, resting, watching or toggling objects.

## Milestone 7.2 — Buddy Studio full-release expansion

Existing detailed plan: `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md`.

- trusted player-drawn cosmetic templates for Hair and other supported categories;
- local custom-cosmetic library;
- bounded anchored cosmetic stretching/deformation;
- Steam sharing/import of safe custom cosmetics;
- larger Browse / My Creations / Create-Edit / Shared Studio redesign.

## Milestone 7.3 — Interactive Accessories / authored gadgets

Bring Accessories back as a more distinctive full-release category.

Direction:

- authored handheld/interactive props rather than only static decoration;
- e.g. a phone the buddy can hold/check;
- possible calibrated passive-income behavior;
- compatibility with mood/Work Mode/environment where explicitly authored;
- trusted capability IDs only — no arbitrary scripts from character/shared content.

## Milestone 7.4 — Player voice recordings

Add optional player-recorded voice reactions:

- multiple recordings;
- assignment to authored triggers such as damage, happiness and occasional random noises;
- optional original goofy voice filter with Light/Heavy intensity;
- local/private by default;
- bounded safe audio storage and playback normalization.

Steam sharing of microphone recordings is **not currently approved**.

## Milestone 7.5 — Original office-helper tutorial system

Add an opt-in contextual tutorial helper with an original office-item identity, such as an animated pen.

It may teach grabbing, Shop/Equip flow, Work Mode return, painting, decorating, Potion Shop use and other unusual interactions.

This must be clean-room and must not copy Clippy's character/art/animation/copy.

## Milestone 7.6 — Steam UGC consolidation

After safe local formats are stable:

- room sharing;
- custom-cosmetic sharing;
- install/uninstall/missing-content recovery;
- moderation/policy UX;
- large-library performance and deterministic browsing.

This remains bounded declarative UGC, not a general-purpose mod loader.

## Milestone 7.7 — Full Release polish and release candidate

Re-run the demo-quality bar against the expanded game:

- progression/economy recalibration across active and passive sources;
- final art/VFX/SFX;
- large-library Buddy Studio/room UX;
- interactive furniture/accessory polish;
- voice/tutorial UX;
- Steam/cloud/UGC failure recovery;
- migration rehearsal from the Steam demo;
- Windows DPI/multi-monitor/accessibility/performance matrices;
- active/hidden/Work soaks;
- final clean-room and content audit.

---

# Deferred beyond the currently approved full-release program

Do not implement without a later owner promotion:

- unrestricted scripting/mod loader;
- arbitrary user-authored Godot scenes/scripts/shaders/meshes;
- optional blood/bleeding;
- broad advanced painting suite such as unrestricted 3D orbit painting, tablet pressure, arbitrary brushes and generalized material/layer editing;
- multiple simultaneous buddies;
- multiplayer;
- Linux/macOS ports;
- Steam sharing of player microphone recordings.
