# Desktop Buddy — Implementation Roadmap

Status: **Current owner roadmap / agent handoff sequence**  
Updated: 2026-08-11

This document is the current sequencing authority. Older milestone plans remain useful for architecture/history, but stale status text inside those documents does not override this roadmap.

## Priority rule

The owner-provided user-testing notes from 2026-08-11 are the highest-priority input for the next Steam-demo pass. Their concrete bug-fix/UX findings take precedence over earlier speculative polish plans when they conflict.

Authoritative extracted backlog:

`docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`

Do not defer an observed user-testing defect into generic later polish unless the owner explicitly changes or defers it.

The project is split into two product targets:

1. **Steam Demo** — stabilize and polish the already-implemented game, add the Potion Shop demo slice, integrate Steam/release systems, prepare store assets, perform the final content-complete polish, then cut the RC.
2. **Full Release** — expand Environment, Buddy Studio, accessories, tutorial/voice systems and Steam UGC after the demo ships.

Milestones are sequential release gates unless a task is explicitly marked safe to parallelize.

---

# Completed foundation and feature milestones

## Milestone 0 — Foundation ✅ COMPLETE

Godot/.NET project structure, domain/test split, typed resources, logging, scenario/journey infrastructure, Windows export scaffolding and established build/test tooling are in place.

## Milestone 1 — Physics Laboratory ✅ COMPLETE

Six-part ragdoll, spring/stretch controller, autonomous movement, grab/tether behavior, tuning resources, seeded physics scenarios and recovery/safety infrastructure are established.

## Milestone 2 — Windows Desktop Shell ✅ COMPLETE

Windows desktop-companion shell, transparency/window management, Work/Play input ownership, resize/fullscreen behavior, DPI/multi-monitor recovery seams and Win98 application chrome are established.

## Milestone 3 — Core Interaction and Damage ✅ COMPLETE

Core grabbing, pet/tickle/damage behavior, reactions, knockout/pain attribution and the interaction/economy bridge are established.

## Milestone 4 — Personality, Care and Persistence ✅ COMPLETE

Mood/trust/care behavior, passive progression, save/recovery, safe resume and long-lived progression state are established.

## Milestone 5 — Shop and Full Tool Catalogue ✅ COMPLETE / OWNER ACCEPTED

The confirmed tool catalogue and progression slice is implemented, including the purchasable tool progression, Grab/Power Grab behavior, economy calibration, reset service and automated progression coverage.

## Milestone 5.5 — Character Editor Phase A ✅ COMPLETE / MERGED

Trusted character schema/compiler, character library, parametric expression rendering, visual-only character swapping, editor working-copy flow and save/use/restart behavior are on `main`.

Reference: `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`.

## Milestone 5.6 — Paint Buddy / Character Painting Phase B ✅ COMPLETE / MERGED

Trusted body-surface painting, Undo/Erase behavior, persistence, runtime underlay rendering and save/use/restart coverage are complete. Later work added Spray/Airbrush, Curved Line and semantic paint-toolbar icons.

Reference: `docs/M5_6_PHASE_B_COMPLETION.md`.

## Milestone 5.7 — Work Mode Typing Companion ✅ COMPLETE FOR DEMO / OWNER ACCEPTED

Global privacy-safe activity counting, session/lifetime counters, compact Work presentation, milestone rewards, Work glasses reward, crash-safe session progress, return flow and low-cost gameplay suspension are implemented.

Remaining Work Mode items are demo polish/release verification, not missing foundation.

## Milestone 5.8 — Win98 / shared customization foundation ✅ COMPLETE / MERGED

Current in-scene Win98 shell, Customize routing, shared catalogue/category/value controls and reusable customization UI foundation are established.

## Milestone 5.9 — Environment Customization ✅ COMPLETE / OWNER ACCEPTED / MERGED

Current Steam-demo Environment baseline includes one local room/profile, Paint Background, wallpaper, Environment Decorator, permanent decoration ownership/storage, authored launch categories, free placement/editing, persistence, Spray/Airbrush, Curved Line and semantic paint icons.

Reference: `docs/ENVIRONMENT_DEMO_CLOSURE_STATUS.md`.

Full-release room profiles, Steam room sharing and furniture interactions remain deferred.

## Milestone 5.10 — Buddy Studio current-release implementation ✅ COMPLETE / OWNER ACCEPTED / MERGED

Current Buddy Studio includes trusted multi-category cosmetic definitions/rendering, permanent ownership, preview/purchase/equip/save boundaries, bounded fitting controls, named color-channel seam, deterministic Randomize, Work-glasses integration, save/restart behavior and clean-room launch content.

Reference: `docs/BUDDY_STUDIO_DEMO_CLOSURE_STATUS.md`.

Player-drawn cosmetics, deformation/stretching, Steam sharing and the larger Studio redesign are full-release work.

---

# Steam Demo completion program — locked owner order

The required order is:

1. **User-testing bug fixing + polish**
2. **Potion Shop temporary effects**
3. **Steam Demo platform/release foundation**
4. **Steam marketing assets**
5. **Steam Demo polish/content-complete pass**
6. **Steam Demo release candidate**
7. **Full Release expansions**

The order above supersedes the prior roadmap ordering.

---

## Milestone 5.11 — User-Testing Bug Fix + UX Polish 🔴 NEXT

This is the immediate implementation gate. It exists because the attached findings came from actual user testing and therefore take precedence over speculative polish sequencing.

Authoritative backlog:

`docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`

The pass includes, without reducing the authority of the detailed backlog:

### Environment paint / Paint Background

- clear Curved Line selection/edit/finalization state;
- visible curve control circles during editing;
- `Save and Exit` wording;
- Bucket Fill naming;
- Eraser footprint/cursor correction;
- pressed/indented active-tool state;
- active shape name/status feedback.

### Buddy Studio

- stronger Mouth variations;
- hide unfinished Accessories in the demo;
- single-click owned-item equip;
- unmistakable unowned preview/Buy state;
- clearer purchase state/action;
- actual trusted item/appearance renders for thumbnails.

### Paint Buddy

- detachable/floating color palette;
- Mirror option;
- optional simultaneous front/back painting;
- Bucket Fill;
- consistent color-picker behavior;
- move Turn/Zoom controls into the buddy frame;
- clear stale palette selection after eyedropper sampling;
- `Show limbs` painting pose/option.

### General shell/catalogue

- reduce excessive normal-animation limb rotation;
- merge Tools + Shop into one Buy/Equip flow;
- outside-click closes open top-bar menus/dropdowns;
- floatable panels can move outside the play window where safely supported;
- persistent active gameplay tool status.

### Work Mode

- resize via hold LMB + mouse wheel;
- auto-select normal Grab on exit.

### Decorate Room

- clearer button/mode hierarchy;
- deliberate money-value color treatment;
- remove Snap-to-grid from the demo UX;
- remove authored floor/wall placement restrictions while retaining safe recoverable bounds;
- dedicated Delete mode;
- simplify the room save/confirmation flow;
- consistent wallpaper/furniture dirty/save behavior.

### Cross-cutting

- **SFX for everything** is a priority observed from testing; start closing obvious missing feedback in this pass.

Exit gate:

- every user-testing item in Sections 1–7 of the authoritative backlog is fixed, explicitly changed by a new owner decision, or intentionally deferred by the owner;
- relevant validators/regression tests pass;
- no new save/input/purchase regression is introduced;
- owner verifies the changed flows before Potion Shop implementation starts.

---

## Milestone 5.12 — Potion Shop / Temporary Buddy Effects

After the user-testing gate, add a dedicated Potion Shop for a small set of short-lived, highly visible demo effects.

Target roughly **three polished demo showcase effects/items**, not a large catalogue.

Candidate pool from the owner/user-testing notes:

- temporary tail;
- glossy/shiny treatment;
- RGB/cycling-color treatment;
- glow-in-the-dark treatment, potentially interacting with a flashlight;
- metallic treatment with matching SFX and a possible gameplay modifier only if separately approved;
- poison/sickness effect;
- flashlight as a possible separate buyable toy/effect companion.

Exact entries, prices, durations, stacking rules and inventory/consume behavior are **not locked yet**.

Work Mode should contribute to the Potion Shop loop. Before implementation, choose explicitly between:

- normal credits earned through Work Mode;
- a dedicated Work/AFK token;
- or Work milestones/discounts/free samples without a second currency.

Do not add a second economy ledger before that design choice is approved.

Reference: `docs/POTION_SHOP_CONCEPT.md`.

Exit gate:

- final demo effect subset approved;
- lifecycle/economy rules locked;
- selected entries have real VFX/SFX and clear purchase/use feedback;
- restart/reset/mode changes cannot leave stuck effects;
- Paint Buddy, Buddy Studio, room, tool and save behavior remain intact.

---

## Milestone 6 — Steam Demo Platform and Release Foundation

Build the minimum robust Steam/release foundation needed to distribute and verify the demo while preserving the local/non-Steam path.

Deliver:

- local and Steam platform implementations behind one interface;
- Steamworks.NET lifecycle/bootstrap;
- graceful behavior when Steam is unavailable;
- cloud-safe progress payload boundaries;
- appropriate machine-local settings separation;
- queued/offline-safe stats and achievements;
- confirmed achievement set;
- launch-with-Windows and tray/recovery integration;
- release export/build automation;
- SteamPipe/depot documentation/tooling;
- installed-build and clean-install validation.

Do **not** start room/cosmetic Steam UGC here. M6 supplies the platform foundation those full-release systems later reuse.

Exit gate:

- installed Steam demo launches/saves correctly;
- offline/Steam-unavailable paths are safe;
- no proprietary Steam SDK files are committed improperly;
- direct local/non-Steam launch remains usable;
- achievement/stat/save behavior survives restart/connectivity transitions.

---

## Milestone 6.1 — Steam Marketing Asset Production

Marketing asset production starts after the platform foundation so captures can come from a representative Steam-demo build. This milestone intentionally precedes the final content-complete polish in the owner-approved sequence.

Prepare:

- Steam capsule/store/library art in every format required by the then-current Steamworks specification;
- main gameplay trailer;
- curated gameplay screenshots;
- short gameplay GIFs/loops;
- final logo/wordmark assets;
- store feature copy/demo messaging as required.

Capture/storyboard targets should include:

- desktop buddy/ragdoll interaction;
- memorable tools;
- Paint Buddy;
- Buddy Studio;
- Environment decoration/background painting;
- Work Mode;
- Potion Shop/effect showcase.

Exact Steam dimensions/submission requirements must be re-verified against official Steamworks guidance at production time rather than hard-coded now.

Reference: `docs/STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md`.

Exit gate:

- required Steam art/capture inventory exists at a usable production level;
- trailer/storyboard reflects the actual demo feature set;
- screenshots/GIFs contain no debug/programmer presentation;
- asset review identifies any remaining visual/content blockers for the subsequent content-complete polish pass.

---

## Milestone 6.2 — Steam Demo Polish / Content-Complete Pass

After marketing asset preparation, perform the final broad public-demo polish pass. This does **not** replace the earlier user-testing gate; it catches the broader quality bar revealed by progression review, final assets, Steam integration and marketing capture.

Primary goals:

- remaining bug fixing/regression closure;
- progression unlock clarity and pacing;
- Work Mode reward presentation/economy polish;
- Potion Shop balance/integration;
- final item/cosmetic/environment/potion assets;
- full-demo SFX consistency/completeness;
- UI/UX consistency across every system;
- accessibility/readability/DPI polish;
- clean first-session onboarding;
- replace remaining public-facing placeholders;
- incorporate capture/store-asset feedback where it exposes weak presentation.

Reference: `docs/STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md`.

Exit gate:

- no known data-loss, stuck-input, duplicated-purchase, off-screen-window or stuck-effect defect;
- progression/unlocks can be understood without debug knowledge;
- Work rewards and Potion Shop economy feel coherent with active/passive earning;
- no visible placeholder item/control remains in public demo scope;
- final demo systems pass cross-feature regression and owner UX review.

---

## Milestone 6.3 — Steam Demo Release Candidate

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

Full-release work begins after the Steam demo ships/stabilizes unless the owner explicitly promotes an item earlier.

Detailed cross-system roadmap:

`docs/FULL_RELEASE_EXPANSION_ROADMAP.md`

## Milestone 7.1 — Environment full-release expansion

- multiple named local room profiles;
- Steam sharing of complete room configurations;
- authored buddy/furniture interactions such as sitting, resting, watching or toggling objects.

## Milestone 7.2 — Buddy Studio full-release expansion

Reference: `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md`.

- trusted player-drawn cosmetic templates for Hair and other supported categories;
- local custom-cosmetic library;
- bounded anchored cosmetic stretching/deformation;
- Steam sharing/import of safe custom cosmetics;
- larger Browse / My Creations / Create-Edit / Shared Studio redesign.

## Milestone 7.3 — Interactive Accessories / authored gadgets

Bring Accessories back as a more distinctive full-release category:

- authored handheld/interactive props rather than only static decoration;
- e.g. a phone the buddy can hold/check;
- possible calibrated passive-income behavior;
- authored compatibility with mood/Work Mode/environment;
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

It may teach grabbing, Buy/Equip flow, Work Mode return, painting, decorating, Potion Shop use and other unusual interactions.

Must remain clean-room and must not copy Clippy character/art/animation/copy.

## Milestone 7.6 — Steam UGC consolidation

After safe local formats are stable:

- room sharing;
- custom-cosmetic sharing;
- install/uninstall/missing-content recovery;
- moderation/policy UX;
- large-library performance and deterministic browsing.

This remains bounded declarative UGC, not a general-purpose mod loader.

## Milestone 7.7 — Full Release polish and release candidate

Re-run the demo quality bar against the expanded game:

- progression/economy recalibration across active/passive sources;
- final art/VFX/SFX;
- large-library Buddy Studio/room UX;
- interactive furniture/accessory polish;
- voice/tutorial UX;
- Steam/cloud/UGC failure recovery;
- migration rehearsal from the Steam demo;
- Windows DPI/multi-monitor/accessibility/performance matrices;
- active/hidden/Work soaks;
- final clean-room/content audit.

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