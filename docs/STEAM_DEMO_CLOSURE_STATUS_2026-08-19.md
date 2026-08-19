# Desktop Buddy — Steam Demo implementation closure

Status: **OBJECTIVE IMPLEMENTATION COMPLETE — FINAL CI / OWNER GATES REMAIN**  
Branch: `agent/steam-demo-closure`  
Base accepted by owner: `main` at `919ccc941efcb2ec9f7f82380ff8e19623a2d7b0`  
Recorded: 2026-08-19

## Scope

This document closes the engineering work defined by `STEAM_DEMO_IMPLEMENTATION_PLAN_2026-08-18.md` after the owner locally accepted and merged the Demo polish and objective audit into `main`.

It does not move the owner gates into engineering guesses. Final economy pacing, tutorial feel, replacement-asset taste, Room Decorator Demo visibility, and final cross-system visual feel remain explicit owner decisions.

## Phase status

### DEMO-0 through DEMO-7 — complete on accepted `main`

The preceding Demo polish/audit work already delivered and was locally accepted for:

- Grab-only fresh-save ownership and purchasable progression;
- persisted first-session guidance with skip/dismiss and tutorial-character presentation seam;
- compatible physical tool drop / Grab / throw / double-click re-equip;
- shared Baseball/Grenade ballistic prediction;
- Work session/lifetime milestones and independent Work typing mute;
- three free character slots plus persistent paid expansion;
- free Buddy Studio defaults / paid alternatives;
- cross-system Inventory/Buy/Equip/Save/Done/Cancel/Discard/Reset polish;
- keyboard focus, disabled-state explanations, status help, and objective UI/performance audit.

### DEMO-8 — objective engineering complete

#### Personality / room interest

Bounded favorite-color room interest is present and yields to higher-priority behavior/player interaction. The existing five mood bands remain the persistent Buddy state read; stronger mood presentation is a final visual decision rather than an engineering requirement.

#### Weak model/presentation audit

The plan called out Meal, Drink, Tickle/Feather, Baseball, Boxing Glove, and Nerf Blaster.

Current objective state:

- **Meal:** now has a distinct clean-room plated-sandwich 3D placeholder through `LooseObjectVisualKind.Meal`; collider, mass, hunger/mood effects and consumption authority are unchanged. `demo_meal_visual` verifies the profile, non-empty bounded mesh, and distinct visual layers.
- **Drink:** already has the dedicated can presentation on accepted `main`.
- **Tickle/Feather:** Tickle previously displayed another hand. `ToolCursorPresenter` now keeps the Pet hand but draws a distinct feather for Tickle; the presentation remains entirely outside care/contact authority. `demo_care_tool_presentation` verifies Pet/Feather/release routing while existing `pet_tickle_mood` remains the mechanical cadence gate.
- **Baseball:** already has the stitched sphere presentation.
- **Boxing Glove:** already has the dedicated rebuilt glove mesh; final owner acceptance remains authoritative.
- **Nerf Blaster:** already has its authored gun presentation.

These changes close missing/ambiguous presentation hooks; they do not claim final art acceptance.

#### SFX / audio

The owner-authored gameplay SFX expansion is already merged into `main` and routes through the existing gameplay audio components. Existing synthesized cues remain valid fallbacks where no replacement asset exists.

Closure adds `devtools/verify_sfx_imports.sh`, run in both quick and full CI, so every tracked authored `.mp3`/`.wav` under `assets/sfx` must have its Godot `.import` sidecar and orphaned audio sidecars fail the build. This prevents an asset working locally but silently disappearing from an export.

Work typing remains independently mutable through `MuteWorkTyping`; broad SFX mute and the existing accessibility/motion policies remain authoritative.

#### Accessibility / input

Accepted audit work already covers Drop Tool rebinding, robust Settings hotkey capture/cancellation, status/tooltip reasons for unavailable actions, reduced motion/particle/screen-shake authority, and keyboard/focus cleanup. No Godot InputMap replacement is introduced.

#### Expensive-path review

The accepted audit removed or bounded the material safe hot paths it found, including catalogue tree churn, Asset Forge Studio reconciliation, filesystem slot scans, dropped-tool idle physics processing, command sorting, and several Paint composition loops.

Three deeper candidates were reviewed and deliberately not rewritten without measurement because they touch accepted central lifecycle/layout code or are low-value allocations:

1. central `Win98CommandBarBootstrap` frame refresh;
2. large owner-tested `Win98PaintUxPolishBootstrap` layout glue;
3. procedural Environment catalogue preview texture regeneration.

These are profiling candidates, not blockers for the Demo implementation contract.

### DEMO-9 — automated acceptance closed

The full `main`-target PR CI now explicitly exercises the Demo-critical paths that were previously spread across local checks or narrower milestone suites:

- Inventory Buy/Equip behavior;
- Settings hotkey capture;
- catalogue in-place update behavior;
- shop progression journey;
- clean character Paint save/use/restart;
- Work window lifecycle and Work resilience;
- multi-grenade lifecycle;
- economy calibration;
- Meal presentation;
- normal boot/physics/tool suites;
- dropped-tool round trip;
- shared ballistic prediction;
- Pet/Tickle cadence and presentation;
- long-enough idle/dual-profile stability smoke.

`DemoCleanSaveAcceptanceTests` now covers:

- Grab-only fresh state;
- the complete tutorial sequence including Paint and Work enter/exit;
- purchased+selected tool persistence;
- paid character-slot entitlement persistence;
- complete/skipped tutorial idempotency;
- repeated purchase/completion protection;
- a real durable round trip through production `JsonProgressStore`, not only an in-memory reconstructed snapshot.

The existing reset/persistence suite separately covers Reset Progress atomicity, fresh-state restoration and preservation of local settings boundaries.

## Intentionally unchanged

### Room Decorator

Per the locked plan, no engineering pass hides/removes or expands Room Decorator. Its inclusion in the public Demo remains an owner scope decision.

### Final economy numbers

The automated economy calibration verifies the authored progression machinery; it does not turn provisional slot/tool/cosmetic prices into an owner-approved pacing decision.

### Final tutorial character/copy

The functional guidance and presenter contract are implemented. Character style, exact copy and feel remain a visual/editorial owner gate.

### Final model/SFX taste

Engineering now provides distinct presentations and functioning/packaged audio hooks. Whether individual final assets are good enough is intentionally not inferred from automated tests.

## Remaining owner gates

Once PR CI is green, the implementation plan has no remaining non-owner engineering phase. The remaining gates are exactly:

1. final progression and price pacing;
2. final tutorial copy / tutorial-character feel;
3. final replacement model and SFX acceptance;
4. Room Decorator included vs hidden in the public Demo;
5. final cross-system visual feel.

Any concrete regression found during those checks returns to engineering; the subjective decision itself does not.
