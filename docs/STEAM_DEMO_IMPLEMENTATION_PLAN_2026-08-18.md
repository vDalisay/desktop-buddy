# Desktop Buddy — Steam Demo implementation plan

Status: **IMPLEMENTING**  
Branch: `agent/steam-demo-polish`  
Stacked on: `agent/steam-page-gameplay-polish` / PR #27 until capture polish lands  
Recorded: 2026-08-18

## Purpose

This is the post-capture Steam Demo polish pass. It deliberately does not own Steam store-page setup, trailer editing, capsule/logo production, pricing metadata, or Valve submission.

The implementation should keep advancing without owner interruption until a decision is genuinely subjective (visual acceptance, final economy pacing, final asset acceptance, or a scope choice the owner has not locked).

## Owner-locked demo contract

### Starting state and progression

- A fresh save starts with **Normal Grab only**.
- Pet, Tickle, Boxing Glove, Baseball, Baseball Bat, Meal, Nerf, Pistol, Soccer Ball, Grenade, Fire Sprayer, Power Grab, Repair Kit, Shotgun, and Drink are purchasable progression.
- Active grinding remains a valid strategy; catalogue items do not form a prerequisite chain, so the player may save for a desired item.
- The catalogue should occupy a few hours rather than being exhausted quickly. Main systems should be encountered within roughly 2–3 hours.
- Material final price changes are calibrated with the benchmark and remain an owner pacing gate; implementation may establish provisional values required by the new starting-inventory contract.
- Paint features remain free.
- Buddy Studio default items remain free; non-default items are paid.
- Room items are not free by default.
- The first **3 character slots are free**; additional slots are purchasable. Slot-count implementation must not impose a small artificial hard cap.

### First-session guidance

Add a lightweight first-session sequence that teaches, in context rather than as a blocking tutorial wall:

1. Grab and interact with Buddy.
2. Actions earn credits.
3. Open the shop and buy/equip tools/customization.
4. Paint Buddy is available as a free customization system.
5. Introduce Work Mode and explain that the companion counts actions locally while Work Mode is active.
6. Teach `double-click your buddy to return` when Work Mode is first entered.

Tutorial characters are demo scope, but should be layered on top of the guidance contract instead of making the basic onboarding depend on a final character asset.

### Tool-world interaction

Capture deliberately deferred this behavior; Demo implements it:

- compatible equipped/spawned physical tools may be dropped into the room;
- Grab can acquire those loose tools using the same loose-object eligibility boundary as other grabbable objects;
- thrown/dropped tools use real physics and remain ordinary loose objects while unequipped;
- double-clicking an eligible dropped tool re-equips that tool and removes/despawns the loose representation transactionally;
- re-equip never grants ownership and never bypasses catalogue ownership;
- invalid/unowned/stale objects do nothing rather than corrupt selection;
- destructive/consumable launchables that already have a distinct spawn lifecycle (for example live grenades/projectiles) are not silently converted into generic persistent tool pickups.

### Throw presentation

Improve the existing pullback/throw trajectory presentation for Baseball and Grenade without changing their gameplay authority:

- stable, readable sampled arc;
- predicted path uses the same authored launch inputs and gravity assumptions as the real launch path;
- no fake trajectory that diverges materially from the actual throw;
- obey Reduced Motion/Reduced Particles where applicable;
- trajectory is presentation-only and awards nothing.

### Work Mode

- Keep the current Work companion presentation and capture-polish reward readout.
- Add clearer **session** and **lifetime** action milestones that award credits through the existing ledger/settlement path.
- Milestones must be idempotent across restart/save reload.
- Add an explicit setting to mute Work Mode typing sounds without muting all SFX.
- Preserve the existing Work window position/geometry behavior.
- Steam achievement mapping is a later platform seam; domain milestone IDs/counters should be stable enough to map without rewriting reward logic.

### Buddy / demo personality polish

After core progression/onboarding/tool-world work is green, add bounded presentation/autonomy polish:

- clearer happiness/state read;
- room-awareness hooks so Buddy may look toward/walk toward newly placed creations;
- hidden favorite-color preference that can bias interest toward room objects matching that color;
- no requirement for voiced/sound reactions.

These behaviors must remain interruptions-safe and must not take authority away from player interaction.

### Paint Buddy

All existing Paint Buddy functionality remains in demo scope. Do not trim tools to reduce QA surface. Continue regression/performance work, including the expanded-limb/head-neck connector behavior already addressed in capture polish.

### Room Decorator

The owner is considering hiding Room Decorator from the public Demo. Do not hide or remove it until that scope decision is explicitly made. Until then, fix regressions only; do not expand it.

### Audio/assets/accessibility

- Replace weak final-facing tool/paint sounds when replacement assets exist; before then, make hooks, buses, overlap/polyphony, and mute behavior correct.
- Weak models called out for later replacement/polish: Meal, Drink, Tickle/Feather, Baseball, Boxing Glove, Nerf Blaster. Boxing Glove already received a capture-pass rebuild; treat the owner visual gate as authoritative.
- No music is required.
- Demo settings work includes Work typing mute and key-rebinding support where the current input architecture permits it without replacing Godot InputMap wholesale.
- Existing Reduced Motion, Screen Shake, Reduced Particles, and Photosensitivity Safe remain authoritative accessibility overrides.

## Implementation order

### DEMO-0 — branch/baseline and contract updates

- Stack this branch on capture PR #27 until #27 merges, then retarget/rebase to `main`.
- Update stale tests/docs that still encode the old four-tool starting inventory.
- Keep CI quick-on-push/full-on-PR contract intact.

### DEMO-1 — fresh-save ownership and catalogue contract

1. Change the domain fresh-save starting set to Grab only.
2. Convert Pet/Tickle/Boxing Glove from starting entries to purchasable entries with provisional whole-credit prices.
3. Preserve the established free-choice shop model; no prerequisite chain.
4. Add migration/new-save tests so existing saves keep ownership while new saves receive the new starting set.
5. Run catalogue/save/shop/domain regression gates.

### DEMO-2 — first-session guidance foundation

1. Add a persisted tutorial/onboarding progress record separate from volatile UI state.
2. Trigger guidance from real events: first grab, first credited action, first shop open/purchase, first Paint Buddy open, first Work Mode entry.
3. Provide skip/dismiss and never trap input behind a tutorial overlay.
4. Add a tutorial-character presenter seam, but keep text/flow functional without a final tutorial character asset.
5. Add restart/idempotency coverage.

### DEMO-3 — physical tool drop / pickup / double-click re-equip

1. Inventory-owned compatible tools get an authored world-drop profile.
2. Spawn through `LooseObjectRegistry`; registry remains live-object authority.
3. Grab uses existing loose-object acquisition.
4. Add double-click hit resolution on eligible dropped-tool bodies.
5. Verify ownership before re-equip; on success select tool then remove the loose body.
6. Prevent duplicate/desync states on rapid double-click, deletion, scene teardown, and save flush.
7. Add focused scenario coverage for drop -> grab -> throw -> double-click -> equipped.

### DEMO-4 — trajectory/throw-arc polish

- Replace the current weak guide with deterministic sampled ballistic prediction shared by Baseball/Grenade launch presentation.
- Cover minimum/maximum pull and changed gravity/profile values.

### DEMO-5 — Work milestones + Work typing mute

- Add session/lifetime milestone definitions and durable award IDs.
- Settle via existing reward ledger and persist awarded milestones.
- Surface progress/next milestone in Work UI without turning it into a large HUD.
- Add `MuteWorkTyping` to machine-local settings and route Work typing audio through it.

### DEMO-6 — character slots / customization ownership

- First 3 slots free.
- Additional slots use a deterministic price rule and persistent purchased-slot entitlement rather than preallocating a finite list.
- Defaults stay free; non-default Buddy Studio items remain paid.
- Character creation UI clearly distinguishes free remaining slots from paid expansion.

Final slot pricing remains part of the economy pacing gate.

### DEMO-7 — onboarding tutorial character + cross-system UX

- Bind the tutorial-character presenter seam to a small demo tutorial character set.
- Normalize Buy/Equip/Save/Done/Cancel/Discard/Reset language and disabled-state explanations.
- Ensure keyboard focus, Win98 motion policy, and status help are consistent.

### DEMO-8 — broader personality, asset, audio and accessibility polish

- bounded Buddy room-interest/favorite-color behavior;
- weak model replacements;
- final SFX asset hookup/audit;
- key rebinding/readability/accessibility cleanup;
- expensive path performance review.

### DEMO-9 — clean-save acceptance + owner gates

Automated first, then owner verification only for genuinely subjective items:

- delete saves / clean build;
- verify fresh save owns Grab only;
- earn and spend naturally;
- buy/equip tools and customization;
- exercise all Paint Buddy tools;
- enter/use/exit Work Mode;
- verify tutorial flow can be completed/skipped;
- verify dropped-tool round trip;
- verify no purchase/save/input regression.

Owner gates expected at this point:

1. final progression/price pacing;
2. final tutorial copy/character feel;
3. final replacement models/SFX;
4. Room Decorator included vs hidden in Demo;
5. final cross-system visual feel.

## Explicitly later

- Steamworks/platform implementation and achievement adapters;
- Workshop/UGC/shareable custom cosmetics/backgrounds;
- launch-only Drawn-to-Life custom cosmetic drawing templates;
- cosmetic stretching/deformation;
- broad release-candidate DPI/multi-monitor/soak matrix;
- Steam store-page administration and marketing-production tasks unless separately requested.
