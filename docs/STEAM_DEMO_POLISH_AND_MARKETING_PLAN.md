# Desktop Buddy — Steam Demo Polish and Marketing Plan

Status: **Approved roadmap program; implementation sequencing corrected by owner**  
Recorded: 2026-08-11

This plan supports the Steam-demo sequence in `docs/ROADMAP.md`.

## Source precedence

The owner-provided user-testing notes are the authoritative source for the immediate bug-fix/polish gate. They take precedence over earlier speculative polish wording when the two conflict.

Authoritative extracted backlog:

`docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`

The important distinction is:

- **Immediate user-testing pass:** fix observed bugs/usability problems before Potion Shop.
- **Later content-complete polish:** after Potion Shop, Steam foundation and marketing assets, perform the broader release-quality pass for progression, rewards, assets, SFX, onboarding and cross-system consistency.

Do not postpone an observed user-testing issue into the later generic polish phase unless the owner explicitly approves that deferral.

---

# Phase order

## DEMO-U0 — User-testing bug fixing + UX polish — FIRST

Work through `USER_TESTING_POLISH_BACKLOG_2026-08-11.md` Sections 1–7 before new Potion Shop feature implementation.

Required process:

1. reproduce/inspect each observation against the current build;
2. implement the requested behavior or record a new owner decision;
3. add focused automated regression coverage where practical;
4. run affected Paint/Environment/Buddy Studio/Work/tool validators;
5. perform a manual owner-facing verification pass over the changed interactions.

This pass owns the specific observed issues around Curved Line clarity, Buddy Studio buying/equipping/thumbnails/mouths, Paint Buddy controls, Tools+Shop consolidation, menu dismissal/floating behavior, active-tool feedback, Work Mode resize/Grab restore, Decorate Room interaction/save consistency, and the first high-priority SFX cleanup.

Exit gate:

- every testing item is fixed, explicitly changed, or explicitly deferred by the owner;
- no new save/input/purchase regressions;
- changed flows are owner-verified;
- Potion Shop may start only after this gate.

---

## DEMO-F1 — Potion Shop / temporary-effect feature slice

Detailed concept work lives in `docs/POTION_SHOP_CONCEPT.md`.

Keep the demo slice small and polished, targeting roughly three showcase effects/items. Candidate ideas from the same testing notes include temporary tail, shiny/RGB/glow/metal/poison treatments and a flashlight interaction.

Work Mode may feed the Potion Shop loop, but the currency/reward model must be explicitly decided before implementation. Do not create a second economy ledger by assumption.

Exit gate:

- effect set/lifecycle/economy approved;
- VFX/SFX/purchase/use feedback finished;
- no stuck effect through restart/reset/mode transitions;
- cross-feature safety with paint/cosmetics/room/tools/save.

---

## DEMO-S2 — Steam Demo platform/release foundation

Implement the local/Steam platform abstraction, Steamworks.NET lifecycle, cloud-safe save boundary, offline-safe stats/achievements, release/export/depot tooling and installed-build checks described in `ROADMAP.md` Milestone 6.

Exit gate:

- installed demo behaves correctly online/offline;
- non-Steam/local path remains usable;
- Steam/save/stat/achievement lifecycle is restart-safe.

---

## DEMO-M3 — Steam marketing assets

This phase intentionally occurs before the final content-complete polish per the owner-approved sequence.

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
- Work Mode;
- Potion Shop effects.

### Trailer target

Create a storyboard before capture. Candidate order:

1. immediate desktop-buddy/ragdoll hook;
2. memorable tool interaction;
3. Paint Buddy;
4. Buddy Studio;
5. environment decoration;
6. Work Mode earning;
7. Potion Shop effect showcase;
8. fast closing montage/demo CTA.

### Screenshot/GIF target

Capture intentional compositions with production UI and no debug overlays. Use GIFs/short loops where motion communicates a feature better than a still, especially ragdoll reactions, painting, room placement, Work Mode and potion effects.

This milestone may expose weak presentation. Record those findings as inputs to the following content-complete polish pass rather than silently changing the user-testing backlog.

Exit gate:

- store/capture inventory exists at production-usable quality;
- no debug/programmer presentation in intended final captures;
- asset review produces a concrete list of remaining visual/content blockers.

---

## DEMO-P4 — Steam Demo polish / content-complete pass

This is the broad final public-demo polish phase **after** marketing asset preparation. It is separate from and later than the user-testing bug-fix gate.

Primary goals:

- remaining bug/regression closure;
- progression/unlock clarity and pacing;
- Work Mode reward presentation and economy integration;
- Potion Shop affordability/reward balance;
- final item/cosmetic/environment/potion assets;
- complete demo SFX consistency;
- UI/UX consistency across all systems;
- accessibility/readability/DPI polish;
- clean first-session onboarding/tutorial copy;
- replace every public-facing placeholder;
- address visual weaknesses revealed by marketing capture.

### Progression and reward review

Evaluate the demo as one economy:

- tool unlock order/prices;
- cosmetic/environment/Potion Shop visibility and acquisition clarity;
- first-session pacing;
- Work Mode session/lifetime milestones;
- Work first-entry reward clarity;
- active vs passive income balance;
- ownership wording;
- reset route/behavior;
- no dead/locked item with no understandable acquisition path.

Do not silently change established economy numbers. Record target pacing and get owner approval for material progression changes.

### Work Mode release polish

Beyond the earlier observed resize/Grab fixes, finish:

- session/lifetime reward clarity;
- payout summaries;
- correct active Buddy Studio appearance/cosmetic rendering in Work Mode;
- first-entry privacy sentence;
- `double-click your buddy to return` teach;
- release DPI/monitor/soak verification.

### Final asset/VFX/SFX pass

Replace temporary/programmer-facing presentation with approved demo assets:

- tool/item art;
- Buddy Studio thumbnails;
- Environment thumbnails/art;
- Potion Shop icons/VFX;
- remaining toolbar/cursor icons;
- final SFX.

**SFX for everything** remains a high-priority quality target. Audit purchasing/equipping, tool use, paint actions, room editing, Studio actions, Work rewards, potion lifecycle and useful error/confirmation states. Keep feedback non-fatiguing.

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

- no known data-loss, purchase duplication, input-lock, off-screen-window, invisible-buddy, stuck-effect or unrecoverable-shell defect;
- progression/unlocks can be understood without debug knowledge;
- Work/Potion economy feels coherent;
- no visible placeholder item/control remains;
- owner accepts final cross-system UX.

---

## DEMO-R5 — Steam Demo release candidate

Freeze features and run:

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
- final clean-room/IP audit.

The Steam demo is ready to ship only when no selectable system/item is represented by placeholder content or a nonfunctional control.

---

# User-testing detail reference

For the exact observed items, use `docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md` rather than treating this file as a substitute summary. The testing backlog preserves the requested system-by-system findings and the distinction between immediate fixes, Potion Shop ideas and full-release ideas.