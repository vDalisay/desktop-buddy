# Desktop Buddy — Steam Demo Polish and Marketing Plan

Status: **Approved owner sequence; Potion Shop removed from demo scope**  
Recorded: 2026-08-11

This plan supports the Steam-demo sequence in `docs/ROADMAP.md`.

## Source precedence

The owner-provided user-testing notes remain authoritative for the current bug-fix gate. They take precedence over earlier speculative polish wording when the two conflict.

Authoritative extracted backlog:

`docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`

Potion Shop temporary effects are now a **Full Release** feature. `docs/POTION_SHOP_CONCEPT.md` remains the concept source, but none of its effects, economy, UI or assets are required for the Steam Demo.

---

# Locked Steam Demo phase order

1. **DEMO-U0 — close current user-testing bug/performance gate**
2. **DEMO-P1 — Steam Demo polish / content-complete pass**
3. **DEMO-S2 — Steam Demo platform/release foundation**
4. **DEMO-M3 — Steam marketing assets**
5. **DEMO-R4 — Steam Demo release candidate (RC)**
6. **Ship/stabilize the Steam Demo**

This supersedes the older order that put Potion Shop before Steam integration and put the broad content-complete polish pass after marketing production.

---

## DEMO-U0 — User-testing bug fixing + UX polish

Finish the owner-observed issues in `USER_TESTING_POLISH_BACKLOG_2026-08-11.md` before moving into broad demo polish.

At the time of this sequence update, the remaining owner recheck is Paint Buddy maximum-brush performance/continuity: large Brush strokes must remain responsive and continuous instead of lagging and appearing striped.

Required process:

1. reproduce/inspect the observation against the current build;
2. implement the requested behavior or record a new owner decision;
3. add focused regression coverage where practical;
4. run affected Paint/Environment/Buddy Studio/Work/tool validators;
5. perform the owner-facing manual verification pass.

Exit gate:

- every testing item is fixed, superseded by a later owner decision, or explicitly deferred;
- no new save/input/purchase regressions;
- max-size Paint Buddy Brush is owner-verified in the Windows build;
- DEMO-P1 may start only after this gate.

---

## DEMO-P1 — Steam Demo polish / content-complete pass

This is the broad public-demo quality pass over the **existing demo feature set**. It occurs before Steam integration and marketing capture so those phases work from a coherent, presentable game rather than becoming the place where gameplay polish is finished.

Primary goals:

- remaining bug/regression closure;
- progression/unlock clarity and pacing;
- Work Mode reward presentation and economy integration;
- final demo item/cosmetic/environment assets;
- complete demo SFX consistency;
- UI/UX consistency across all current systems;
- accessibility/readability/DPI polish;
- clean first-session onboarding/tutorial copy;
- replace every public-facing placeholder;
- performance/memory review of expensive interactive paths.

### Progression and reward review

Evaluate the demo as one economy:

- tool unlock order/prices;
- cosmetic/environment acquisition clarity;
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
- preliminary DPI/monitor/performance review.

### Final asset/VFX/SFX pass

Replace temporary/programmer-facing presentation with approved demo assets:

- tool/item art;
- Buddy Studio thumbnails;
- Environment thumbnails/art;
- remaining toolbar/cursor icons;
- final demo SFX.

Potion Shop icons/VFX/SFX are excluded from this milestone.

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
- owner accepts final cross-system UX before Steam integration begins.

---

## DEMO-S2 — Steam Demo platform/release foundation

This phase adds distribution/platform infrastructure, not another gameplay system.

Implement:

- one platform abstraction with local and Steam implementations;
- Steamworks.NET lifecycle/bootstrap and clean shutdown;
- graceful Steam-unavailable/offline/direct-launch behavior;
- explicit cloud-safe progress boundary and machine-local settings separation;
- queued/offline-safe achievements and stats;
- confirmed demo achievement/stat set;
- launch-with-Windows and tray/recovery integration where appropriate;
- deterministic Windows release export/package automation;
- SteamPipe/depot build configuration documentation and repeatable upload tooling;
- installed-build, clean-install, restart and connectivity-transition checks;
- enough diagnostics to distinguish platform failures from gameplay/save failures.

Do **not** add Steam Workshop/UGC, room sharing or custom-cosmetic sharing here.

Exit gate:

- installed demo behaves correctly online and offline;
- direct local/non-Steam path remains usable where supported;
- cloud-eligible progress and machine-local settings remain correctly separated;
- Steam/save/stat/achievement lifecycle is restart-safe;
- depot/build steps are repeatable.

---

## DEMO-M3 — Steam marketing assets

Produce the store/capture package from the polished, platform-integrated demo build.

Prepare:

- Steam capsule/store/library art in every then-current required format;
- main gameplay trailer;
- curated gameplay screenshots;
- short gameplay GIFs/loops;
- logo/wordmark assets;
- store feature/demo copy where needed.

Exact Steam dimensions and submission requirements must be checked against current official Steamworks guidance at production time.

### Capture targets

Deliberately represent:

- desktop buddy/ragdoll interaction;
- memorable tools/reactions;
- Paint Buddy;
- Buddy Studio;
- Environment decorating/background painting;
- Work Mode.

Potion Shop effects are excluded because they are Full Release content.

### Trailer target

Create a storyboard before capture. Candidate order:

1. immediate desktop-buddy/ragdoll hook;
2. memorable tool interaction;
3. Paint Buddy;
4. Buddy Studio;
5. environment decoration;
6. Work Mode earning;
7. fast closing montage/demo CTA.

### Screenshot/GIF target

Capture intentional compositions with production UI and no debug overlays. Use GIFs/short loops where motion communicates a feature better than a still, especially ragdoll reactions, painting, room placement and Work Mode.

Marketing capture may reveal a blocker. Record it for targeted correction before RC acceptance; do not use capture review as a reason to reopen broad feature scope.

Exit gate:

- store/capture inventory exists at production-usable quality;
- no debug/programmer presentation in intended final captures;
- trailer/storyboard matches the actual demo feature set;
- all capture-discovered release blockers are identified for resolution before RC acceptance.

---

## DEMO-R4 — Steam Demo release candidate

**RC = Release Candidate.** This is the feature-frozen build believed ready to become the public Steam Demo if the release matrix passes. No planned feature/content work belongs here; only release-blocking fixes are allowed. A blocker fix creates a new candidate and the affected validation is repeated.

Run:

- full automated regression;
- installed-depot test;
- direct non-Steam launch test where supported;
- clean install/uninstall/reinstall;
- fresh-save progression sample;
- supported save-migration rehearsal;
- save corruption/recovery;
- Steam online/offline transitions;
- 100/125/150/200% DPI and multi-monitor checks;
- minimum/default/maximized/fullscreen layouts;
- four-hour active soak;
- four-hour Work Mode soak;
- hidden/tray soak;
- performance/memory review;
- final accessibility/readability/audio review;
- final clean-room/IP audit;
- final store/build version sanity check.

The Steam Demo is ready to ship only when the RC gate passes and no selectable public-demo system/item is represented by placeholder content or a nonfunctional control.

---

# User-testing detail reference

For the exact observed items, use `docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md` rather than treating this file as a substitute summary. Potion Shop concept work remains preserved in `docs/POTION_SHOP_CONCEPT.md`, but it is now Full Release scope.
