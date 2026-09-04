# Desktop Buddy — Implementation Roadmap

Status: **Current owner roadmap / agent handoff sequence**  
Updated: 2026-08-11

This document is the current sequencing authority. Older milestone plans remain useful for architecture/history, but stale status text inside those documents does not override this roadmap.

## Priority rule

The owner-provided user-testing notes from 2026-08-11 remain the highest-priority input until their final observed defect is owner-verified. Concrete bug-fix/UX findings take precedence over speculative roadmap work when they conflict.

Authoritative extracted backlog:

`docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`

Do not defer an observed user-testing defect into generic later polish unless the owner explicitly changes or defers it.

The project is split into two product targets:

1. **Steam Demo** — close the current user-testing gate, perform the broad content-complete demo polish pass, integrate the Steam/release foundation and authorized Workshop v1, produce Steam marketing assets, then cut and validate the release candidate.
2. **Full Release** — retain Workshop v1 and add Potion Shop temporary effects plus expanded Environment, Buddy Studio, accessories, tutorial/voice systems and separately approved sharing features after the demo ships.

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

Trusted body-surface painting, Undo/Erase behavior, persistence, runtime underlay rendering and save/use/restart coverage are complete. Later work added Spray/Airbrush, Curved Line, Pen, semantic paint-toolbar icons, mirror/backside options and expanded-limb painting support.

Reference: `docs/M5_6_PHASE_B_COMPLETION.md`.

## Milestone 5.7 — Work Mode Typing Companion ✅ COMPLETE FOR DEMO / OWNER ACCEPTED

Global privacy-safe activity counting, session/lifetime counters, compact Work presentation, milestone rewards, Work glasses reward, crash-safe session progress, return flow and low-cost gameplay suspension are implemented.

Remaining Work Mode items are demo polish/release verification, not missing foundation.

## Milestone 5.8 — Win98 / shared customization foundation ✅ COMPLETE / MERGED

Current in-scene Win98 shell, customization routing, shared catalogue/category/value controls and reusable customization UI foundation are established.

## Milestone 5.9 — Environment Customization ✅ COMPLETE / OWNER ACCEPTED / MERGED

Current Steam-demo Environment baseline includes one local room/profile, Paint Background, wallpaper, Room Decorator, permanent decoration ownership/storage, authored launch categories, free placement/editing, persistence, Spray/Airbrush, Curved Line and semantic paint icons.

Reference: `docs/ENVIRONMENT_DEMO_CLOSURE_STATUS.md`.

Full-release room profiles and furniture interactions remain deferred. Data-only Workshop sharing for the current room painting is authorized for the Steam Demo under Milestone 6.

## Milestone 5.10 — Buddy Studio current-release implementation ✅ COMPLETE / OWNER ACCEPTED / MERGED

Current Buddy Studio includes trusted multi-category cosmetic definitions/rendering, permanent ownership, preview/purchase/equip/save boundaries, bounded fitting controls, named color-channel seam, deterministic Randomize, Work-glasses integration, save/restart behavior and clean-room launch content.

Reference: `docs/BUDDY_STUDIO_DEMO_CLOSURE_STATUS.md`.

Player-drawn cosmetics, deformation/stretching and the larger Studio redesign are full-release work. Data-only Workshop sharing for the current Buddy Studio configuration and declared buddy paint is authorized for the Steam Demo under Milestone 6.

---

# Steam Demo completion program — locked owner order

The required order is now:

1. **Finish the current user-testing bug-fix/performance gate**
2. **Steam Demo polish / content-complete pass**
3. **Steam Demo platform and release foundation**
4. **Steam marketing assets**
5. **Steam Demo release candidate (RC)**
6. **Ship/stabilize the Steam Demo**
7. **Full Release expansions, including Potion Shop temporary effects**

This order supersedes every earlier roadmap that placed Potion Shop in the demo or placed the broad demo polish pass after Steam marketing production.

---

## Milestone 5.11 — User-Testing Bug Fix + UX Polish 🟡 FINAL OWNER RECHECK

The broad user-testing pass has been implemented and repeatedly refined. The remaining observed gate at the time of this roadmap update is the Paint Buddy maximum-brush performance/continuity defect: large Brush strokes must remain responsive and continuous rather than lagging and appearing striped.

Authoritative backlog:

`docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`

Exit gate:

- every owner-observed user-testing item is fixed, explicitly changed by a later owner decision, or intentionally deferred by the owner;
- max-size Paint Buddy Brush interaction is responsive and visually continuous in the real Windows build;
- relevant validators/regression tests pass;
- no new save/input/purchase regression is introduced;
- owner accepts the final changed flow before the content-complete Demo polish milestone starts.

---

## Milestone 5.12 — Steam Demo Polish / Content-Complete Pass 🔴 NEXT AFTER 5.11

Perform the broad public-demo quality pass before Steam platform integration and marketing capture. This is separate from the targeted user-testing gate: it evaluates the already-implemented demo as one complete product rather than adding another major gameplay system.

Primary goals:

- remaining bug and regression closure;
- progression/unlock clarity and pacing;
- Work Mode reward presentation and economy polish;
- final demo tool, cosmetic and environment assets;
- complete demo SFX consistency/completeness;
- UI/UX consistency across every current demo system;
- accessibility/readability/DPI polish;
- clean first-session onboarding;
- replace remaining public-facing placeholders;
- performance/memory review of expensive interactive paths, especially painting and desktop/window modes.

Reference: `docs/STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md`.

### Progression and reward review

Evaluate the demo as one economy:

- tool unlock order/prices;
- cosmetic and environment acquisition clarity;
- first-session pacing;
- Work Mode session/lifetime milestones;
- Work first-entry reward clarity;
- active vs passive income balance;
- ownership wording;
- reset route/behavior;
- no dead/locked item with no understandable acquisition path.

Do not silently change established economy numbers. Record target pacing and get owner approval for material progression changes.

### Work Mode release polish

Finish:

- session/lifetime reward clarity;
- payout summaries;
- correct active Buddy Studio appearance/cosmetic rendering in Work Mode;
- first-entry privacy sentence;
- `double-click your buddy to return` teaching;
- preliminary DPI/monitor/performance review before the final RC matrix.

### Final asset/VFX/SFX pass

Replace temporary/programmer-facing presentation with approved demo assets:

- tool/item art;
- Buddy Studio thumbnails;
- Environment thumbnails/art;
- remaining toolbar/cursor icons;
- final demo SFX.

Potion Shop assets/effects are **not** part of the Steam Demo scope anymore.

### Cross-system consistency

Normalize:

- `Buy`, `Equip`, `Save`, `Save and Exit`, `Done`, `Cancel`, `Discard`, `Reset` terminology;
- button state hierarchy;
- tooltips/status-bar help;
- keyboard focus;
- menu/window behavior;
- active-tool/status feedback;
- DPI/readability.

Exit gate:

- no known data-loss, purchase duplication, input-lock, off-screen-window, invisible-buddy or unrecoverable-shell defect;
- progression/unlocks can be understood without debug knowledge;
- Work rewards feel coherent with active/passive earning;
- no visible placeholder item/control remains in public demo scope;
- owner accepts final cross-system demo UX before Steam integration begins.

---

## Milestone 6 — Steam Demo Platform and Release Foundation

This milestone adds release plumbing and authorized asynchronous Workshop sharing, not a new gameplay system. Build the minimum robust Steam/release layer required to distribute, install, save, share, verify and support the demo while preserving the direct local/non-Steam path.

Deliver:

- one platform-facing abstraction used by gameplay, with local and Steam implementations behind it;
- optional GodotSteam 4.22 bootstrap/lifecycle and clean shutdown;
- graceful behavior when Steam is unavailable, offline, not initialized or launched outside Steam;
- explicit save/cloud boundary: cloud-eligible player progress separated from machine-local window/settings state;
- queued/offline-safe stats and achievements with retry/reconciliation behavior;
- confirmed demo achievement/stat set;
- launch-with-Windows and tray/recovery integration where appropriate for the desktop-companion product;
- deterministic Windows release export/package automation;
- SteamPipe build/depot configuration documentation and repeatable upload tooling;
- installed-build, clean-install and restart validation;
- logging/diagnostics sufficient to distinguish platform failure from gameplay/save failure.
- data-only Workshop v1 publish/download/import for room paintings and Buddy Studio configuration plus declared buddy paint;
- explicit apply/use only, local offline copies, strict hostile-data validation, and a directory-backed CI/development emulator;
- Workshop enabled in Steam Demo and full Steam builds, and excluded from itch.io builds.

Do **not** extend Workshop v1 into arbitrary mods, custom Resources/scenes/scripts/native code, real-time multiplayer, complete room-profile sharing, or unapproved custom-cosmetic formats.

Exit gate:

- installed Steam demo launches and saves correctly;
- offline/Steam-unavailable paths are safe and understandable;
- direct local/non-Steam launch remains usable where supported;
- machine-local window/display preferences are not accidentally cloud-roamed as player progress;
- achievement/stat/save behavior survives restart and connectivity transitions;
- room-painting and buddy Workshop round trips pass online and offline without auto-activation;
- the Steam Demo exposes Workshop while the itch.io build does not;
- release/depot tooling can reproduce an installable build without manual mystery steps;
- no proprietary Steam SDK files are committed improperly.

---

## Milestone 6.1 — Steam Marketing Asset Production

Produce the store/capture package from a representative, platform-integrated and already-polished Steam-demo build.

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
- Work Mode.

Potion Shop/effect footage is excluded because Potion Shop is now a Full Release feature.

Exact Steam dimensions/submission requirements must be re-verified against official Steamworks guidance at production time rather than hard-coded now.

Reference: `docs/STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md`.

Marketing capture may still reveal a release blocker. Record such findings as RC blockers or targeted fixes; do not reopen a new broad feature/polish milestone or expand demo scope.

Exit gate:

- required Steam art/capture inventory exists at production-usable quality;
- trailer/storyboard reflects the actual demo feature set;
- screenshots/GIFs contain no debug/programmer presentation;
- any capture-discovered blocker is explicitly tracked for resolution before RC acceptance.

---

## Milestone 6.2 — Steam Demo Release Candidate (RC)

**RC means Release Candidate:** a feature-frozen build believed to be ready to ship if the validation matrix passes. During RC, do not add planned features or broaden scope. Only release-blocking fixes should change the candidate; a blocking fix produces a new candidate that repeats the relevant validation.

Freeze demo features and run the release matrix:

- full automated regression;
- installed Steam depot;
- direct non-Steam launch test where supported;
- clean install/uninstall/reinstall;
- fresh-save progression run;
- supported save migration rehearsal;
- save corruption/recovery paths;
- Steam online/offline transitions;
- 100/125/150/200% DPI;
- multi-monitor/window recovery;
- minimum/default/maximized/fullscreen layouts;
- four-hour active soak;
- four-hour Work Mode soak;
- hidden/tray soak;
- performance/memory review;
- accessibility/audio/readability review;
- final clean-room/IP audit;
- final store/build version sanity check.

**Steam Demo ships after this gate passes.**

---

# Full Release expansion program

Full-release work begins after the Steam demo ships/stabilizes unless the owner explicitly promotes an item earlier.

Detailed cross-system roadmap:

`docs/FULL_RELEASE_EXPANSION_ROADMAP.md`

## Milestone 7.1 — Potion Shop / Temporary Buddy Effects

Potion Shop has been deliberately removed from Steam Demo scope and promoted to Full Release scope.

Reference: `docs/POTION_SHOP_CONCEPT.md`.

Target a small, polished initial effect set before expanding the catalogue. Candidate ideas include:

- temporary tail;
- glossy/shiny treatment;
- RGB/cycling-color treatment;
- glow-in-the-dark treatment, potentially interacting with a flashlight;
- metallic treatment with matching SFX and a possible gameplay modifier only if separately approved;
- poison/sickness effect;
- flashlight as a possible separate buyable toy/effect companion.

Before implementation, explicitly lock purchase/consumption model, duration, stacking/compatibility, restart/reset behavior, accessibility treatment and Work Mode economy integration. Do not add a second economy ledger by assumption.

Exit gate:

- initial effect set and lifecycle/economy rules approved;
- selected entries have production-quality VFX/SFX and clear purchase/use/expiry feedback;
- restart/reset/mode changes cannot leave stuck effects;
- temporary effects never mutate Paint Buddy or Buddy Studio documents;
- Paint Buddy, Buddy Studio, room, tools, physics and save behavior remain intact.

## Milestone 7.2 — Environment full-release expansion

- multiple named local room profiles;
- Steam sharing of complete room configurations;
- authored buddy/furniture interactions such as sitting, resting, watching or toggling objects.

## Milestone 7.3 — Buddy Studio full-release expansion

Reference: `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md`.

- trusted player-drawn cosmetic templates for Hair and other supported categories;
- local custom-cosmetic library;
- bounded anchored cosmetic stretching/deformation;
- Steam sharing/import of safe custom cosmetics;
- larger Browse / My Creations / Create-Edit / Shared Studio redesign.

## Milestone 7.4 — Interactive Accessories / authored gadgets

Bring Accessories back as a more distinctive full-release category:

- authored handheld/interactive props rather than only static decoration;
- e.g. a phone the buddy can hold/check;
- possible calibrated passive-income behavior;
- authored compatibility with mood/Work Mode/environment;
- trusted capability IDs only — no arbitrary scripts from character/shared content.

## Milestone 7.5 — Player voice recordings

Add optional player-recorded voice reactions:

- multiple recordings;
- assignment to authored triggers such as damage, happiness and occasional random noises;
- optional original goofy voice filter with Light/Heavy intensity;
- local/private by default;
- bounded safe audio storage and playback normalization.

Steam sharing of microphone recordings is **not currently approved**.

## Milestone 7.6 — Original office-helper tutorial system

Add an opt-in contextual tutorial helper with an original office-item identity, such as an animated pen.

It may teach grabbing, Buy/Equip flow, Work Mode return, painting, decorating, Potion Shop use and other unusual interactions.

Must remain clean-room and must not copy Clippy character/art/animation/copy.

## Milestone 7.7 — Steam UGC consolidation

After safe local formats are stable:

- room sharing;
- custom-cosmetic sharing;
- install/uninstall/missing-content recovery;
- moderation/policy UX;
- large-library performance and deterministic browsing.

This remains bounded declarative UGC, not a general-purpose mod loader.

## Milestone 7.8 — Full Release polish and release candidate

Re-run the demo quality bar against the expanded game:

- progression/economy recalibration across active/passive sources;
- Potion Shop/effect balance and lifecycle polish;
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
