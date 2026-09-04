# Milestone 5 — Shop and Full Tool Catalogue

> **Historical scope note:** The Workshop deferral in this Milestone 5 plan is superseded by `docs/M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md`; Workshop v1 is included in the Steam Demo and full Steam release, and excluded from itch.io.

Status: **ACTIVE PLAN — written 2026-07-29, Tasks 11–13 resolved/refined 2026-08-02.**
M4 is owner-accepted. Tasks 11–13 now have a complete architecture handoff in
`docs/M5_TASK11_TO_13_HANDOFF_PLAN.md`. Power Grab replaces the former passive
Strength Upgrade, the launch catalogue contains 16 selectable interactions, and the
official completionist economy horizon is 209 minutes.

Initial baseline at plan-writing time: domain 648/648, 41 scenarios / 11 journeys
green across seeds 1 and 7 in both presentations, quick suite 15/15. Current
post-audit baseline: domain 715/715, 43 registered scenarios / 11 journeys, build
clean with zero warnings, and quick suite 17/17. The additional scenarios are valid
catalogue growth, not an M4 gate regression.

## Authoritative sources

- `docs/PRODUCT_REQUIREMENTS.md` — FR-003.2 (retractable panel), FR-011.15
  (milli-credits, whole-credit display), FR-012.5 (shared cash-per-pain), **FR-013**
  (16 interactions, ownership, 209-minute schedule, unrestricted skipping, full
  Reset Progress), **FR-014** (24-object budget), FR-015.7 (purchase → immediate
  flush), FR-017.3 (effects honor reduced-motion/particles/photosensitivity),
  **FR-019** (selectable Power Grab).
- `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` — §8 (passive multiplier anchors, 25 % passive
  benchmark), **§9 launch tool contracts** (shared controls, per-tool table, §9.3
  Burning), §10 (loose objects and cleanup).
- `docs/ROADMAP.md` — Milestone 5 deliverables and the confirmed progression order.
- `docs/DECISIONS.md` — “Power Grab, 16-Tool Catalogue, 209-Minute Economy, and
  Full Progress Reset” (2026-08-02) is the owner authority for Tasks 11–13;
  earlier Strength notes are superseded history. “M5 Baseball Input — Revised”
  (2026-07-28) remains the launch-control authority.
- `docs/UI_FLOATING_DOCK_PLAN.md` — a **draft** proposal for the FR-003.2 panel, not an
  approved implementation contract. Before any dock task begins, the owner must approve
  an original clean-room direction, record it in `DECISIONS.md`, replace the untracked
  session-scratchpad mockup with a repository artifact if it remains normative, and revise
  the dock plan to include the required Settings surface. Its catalogue binding may then
  consume the catalogue this plan builds.
- `docs/TEST_PLAN.md` — §2 damage/economy assertions; **§4 Economy Simulation** (the
  deterministic benchmark Task 12 implements, including its four extra proof
  obligations); per-tool behavior/error tests are an M5 exit criterion.
- `docs/AGENT_VERIFICATION_AND_E2E.md` — every M5 tool needs a real-input journey covering
  its happy path and its applicable cancel/secondary path; `m5_shop_progression` is an
  additional purchase/persistence journey, not a substitute for those tool journeys.

## Scope

Deliver:

1. Catalogue spine and dock binding for exactly 16 selectable interactions: four starting
   tools plus twelve permanent purchasable tools. Reset Progress uses the confirmed
   destructive confirmation and exact erase/preserve matrix.
2. Existing loose-object-budget integration for every M5 spawn; no second budget owner.
3. The locked purchase order:
   **Baseball → Baseball Bat → Meal → Nerf → Pistol → Soccer Ball → Grenade →
   Fire Sprayer → Power Grab → Repair Kit → Shotgun → Drink**.
4. Power Grab as a selectable, permanent stronger-grab tool while Normal Grab remains
   available.
5. Production-path economy calibration to cumulative targets
   **3, 7, 13, 21, 41, 52, 76, 104, 120, 138, 184, and 209 minutes**, each ±15% median,
   plus unrestricted save/skip strategy regressions.
6. Composition, reset, full regression, performance, documentation, and owner exit gates.

Out of scope (do not build):

- Steam stats/achievements sync, tray icon, launch-with-login — Milestone 6.
- Final art/icons/SFX, accessibility pass, tutorial copy — Milestone 7.
- Cosmetic progression, including retained face-style variants A/C — Deferred Roadmap.
- Blood, painting, multiple buddies, Workshop — Deferred Roadmap.

## Prime invariants — every task

- **Single routed gameplay tick.** All new tool logic ticks from the composition
  root's routed tick; presentation clocks count `RoutedTicks` and honor the
  presentation hold. No `Engine.GetPhysicsFrames()` in gameplay or expressive code.
- **Pure logic in `DesktopBuddy.Domain` with xUnit first.** Gun cadence/reload,
  extracted object-admission policy (if extraction is useful), Burning timers, catalogue
  rules, and the economy simulation are all engine-free domain types. Godot components
  stay thin drivers.
- **Resources author static data (ARCHITECTURE §6, NFR-006.2).** Catalogue metadata,
  prices, ordering, translation keys, icon/scene references, tool presets, gun data, and
  status tuning live in typed `Resource` classes and `.tres` assets. Domain code receives
  validated immutable snapshots and owns rules; it does not become a second static-data
  author.
- **Stable string IDs (ARCHITECTURE §5).** Every catalogue entry gets a `ContentIds`
  constant; `ForTool` stays a total mapping; `ToolId` appends only. Persisted IDs are
  never repurposed. Power Grab appends `ToolId.PowerGrab = 15` and uses
  `tool.power_grab`. Deprecated `upgrade.strength` is read only by the schema-5
  migration and is never emitted by current saves.
- **No allocations on the 120 Hz path.** Pellets, particles, and trajectory previews
  use pooled/preallocated storage.
- **Attribution through the shared pipeline.** Every new tool's pain flows through
  `ImpactRouter` → `PainCurve` → `PainKnockoutModel` → `RewardLedger` with the tool's
  content ID as attribution source. No tool grants money for button presses; payouts
  arise from physical pain only (M3 gate rule still binds).
- **Statistics.** Per-tool uses and pain-caused counters update for every new tool
  through the existing seam: `ProgressStatistics.ToolUses` / `ToolPainMilli` on
  `BuddyProgressState`, keyed by content ID. Do not add a parallel counter store.
- **The purchase boundary is authoritative.** The UI requests
  `EconomyService.Purchase(contentId)` only. The service resolves the validated catalogue
  entry and authoritative price, rejects unknown, starting, unfinished/invisible, or
  otherwise non-purchasable entries, and then performs the atomic spend/unlock.
  Success → immediate dirty flush (FR-015.7, already wired — do not bypass it with a
  second purchase path).
- **No unfinished shop entry is shown** (owner rule, 2026-07-28). A catalogue entry is
  invisible to the dock until its slice's automated gates pass, the behavior is driven
  interactively through real input, and the owner accepts the slice's feel. The
  development laboratory may expose an explicit debug-only route to unfinished content;
  shipped tools/shop views may not.
- **One loose-object authority.** The existing `LooseObjectRegistry` remains the sole
  owner of the FR-014 cap. Grenades and launched toy/care objects register there.
  Bullets, pellets, and VFX use separate bounded pools and never consume one of the
  `24` loose-toy slots (RAGDOLL §10, ARCHITECTURE §15).
- **Verification gotchas.** `--fixed-fps 120` on every scenario run; rerun scenarios
  and journeys under both `--presentation=mii3d` and `legacy` with identical verdicts;
  labs stay saveless; close any `--editor` instance before headless runs; MCP
  `run_project` appends a blank line to `project.godot` — revert before committing.
- **Planning rule.** Anything not covered here, in the FRs, or in `DECISIONS.md` —
  stop and ask the owner. Magnitudes marked *provisional* are agent-tunable; anything
  marked *owner* is not.

## Where the code stands today (verified 2026-07-29)

- **A provisional purchase boundary exists.**
  `EconomyService.Purchase(contentId, priceMilliCredits)` → `PurchaseResult`;
  `BuddyProgressState` holds `UnlockedToolIds`, `SelectedToolId`, balance; unknown
  persisted tool IDs survive round-trip via `Extensions.UnknownSelectedToolId`. New
  saves start Grab-selected with the four starting tools (FR-013.1). M5 must remove the
  caller-supplied price and make the service resolve purchasability and price from the
  validated catalogue.
- **Locked selection exists for Baseball.** Real new saves keep Baseball locked; the
  selection rule must generalize, not be rebuilt.
- **Pullback launcher exists and is shared.** `src/Tools/PullbackLauncherComponent` +
  `PullbackLauncherProfile` (max pull 120, min pull 8, 15 px/s per pixel, cap
  1800 px/s, preview = launch math). Baseball chord: key `5` spawns/replaces one ball
  at the cursor, Grab acquires, hold-secondary + back-drag previews, release launches.
  `baseball_pullback` scenario drives it with real input.
- **Consumable machinery exists** against `care.lab_food` (M4): approach/catch/hold/
  consume/toss arbiter actions, eat activity, cooldown gating. Meal/Drink/Repair Kit
  reuse it with their own IDs, mood amounts, and cooldowns.
- **Behavior arbiter exists** (M4) including the priority-3 immediate-hazard branch
  the spec reserves for Burning/held hazards — Burning wires into it, it is not new
  arbiter surface.
- **The FR-014 object budget already exists.** `src/Objects/LooseObjectRegistry.cs`
  owns capacity `24`, monotonic spawn order, oldest-safe eviction, held/protected flags,
  and clean refusal. M5 audits every new loose-object spawn path into that owner and may
  extract its pure decision rule without creating a parallel registry.
- **Cursor guns, Burning, catalogue Resources, and dock UI do not exist.**
  `src/UI/` holds only `MoneyHudPresenter`; the dock plan is still a draft and is gated
  by the owner/clean-room decision above.
- **ToolId today:** Grab 0, Pet 1, Tickle 2, BoxingGlove 3, Baseball 4.

## Architecture — new and changed seams

- **`src/Content/ToolDefinition.cs` + `data/catalogue/*.tres`** — the authoritative
  16-interaction static catalogue. Every entry is selectable; the four starters are free
  and the twelve shop entries are permanent purchases. Definitions carry stable content
  ID, kind, authoritative milli-credit price, order, visibility/completeness, translation
  keys, icon, ToolId/use mode, and required scene/profile references. Startup validation
  rejects duplicates, invalid prices/order, missing assets, incomplete visible entries,
  and any entry without a total ToolId mapping.
- **`Domain/Content/CataloguePolicy.cs`** — engine-free filtering and purchase/selection
  rules over immutable snapshots produced from validated Resources. Starting entries are
  never purchasable; invisible, unknown, invalid, or unowned entries cannot be selected.
  Catalogue order is not a prerequisite chain. This type owns no authored prices, display
  metadata, or Godot references.
- **Stable identity:** retain every existing `ToolId` ordinal through
  `NerfBlaster = 14`; append `PowerGrab = 15`. Add
  `ContentIds.ToolPowerGrab = "tool.power_grab"` and extend every total mapping in the
  same commit. Never repurpose `upgrade.strength`; it is a deprecated schema-5 migration
  alias only. Schema 6 maps legacy ownership to Power Grab and new writes never emit the
  legacy ID.
- **`Domain/Tools/CursorAimModel.cs`** — shared cursor-weapon aim: forward = latest
  non-trivial mouse-motion vector; wheel offsets aim up/down; next non-trivial motion
  clears the offset (spec §9.1). Pure, seeded-input testable.
- **`Domain/Tools/GunModel.cs`** — magazine, minimum shot interval, reload duration,
  fire-on-press, `R` reload, auto-reload on empty fire, unlimited reserve. One model,
  two validated Resource-backed profiles (Pistol 8 / 0.25 s / 1.2 s; Shotgun 5 /
  0.9 s / 2 s / 6 pellets). Tick-counted in routed ticks, no wall clock.
- **`src/Tools/CursorGunComponent.cs`** — thin driver: reads the `ToolInputFrame`,
  feeds `CursorAimModel` + `GunModel`, spawns pooled CCD `RigidBody2D` projectiles
  attributed to the owning tool ID.
- **`Domain/Damage/StatusEffects.cs`** — `BurningStatus`: apply 4 s, contact refresh
  capped at 8 s remaining, periodic attributed pain ticks through the normal accepted-
  event path (mood −min(10, pain×0.1) per tick, same as any harm), cleared by Repair
  Kit, cleared by hard reposition. Emits the hazard signal the arbiter's priority-3
  branch consumes (panic, drop held items). Duration/tick policy and presentation
  references come from a validated `StatusDefinition` Resource.
- **Existing `src/Objects/LooseObjectRegistry.cs`** — remains the sole FR-014 owner. If
  its oldest-safe decision is extracted to
  `Domain/Interaction/LooseObjectAdmissionPolicy.cs` for unit testing, the registry
  delegates to that policy and retains runtime identity, flags, cleanup, and capacity;
  there is never a second `ObjectBudget`.
- **Power Grab resolver.** Add a Godot-free `GrabVariant` and immutable
  `GrabResolvedSettings`. A Resource adapter derives Power values from the Normal
  `GrabTetherProfile`, samples once at acquisition, and stores them on the active grab.
  Normal and Power share the controller, target query, tether solver, stretch maximum,
  hysteresis, strain feedback, cancellation, and hard safety recovery. Power disables
  only sustained-stretch escape and applies its stronger release only for an intentional
  release. Tool changes cancel the live grab; they never mutate it.
- **Economy lab.** A deterministic domain `EconomySimulation` replays benchmark contacts
  through the real `ImpactRouter`/`PainCurve`/`RewardLedger`, passive intervals through
  `PassiveIncome`, and purchase intents through the real catalogue purchase boundary.
  The Godot adapter loads actual Resources and emits fingerprinted JSON/Markdown. Traces
  describe behavior independently of prices; only typed Resources are calibrated.
- **Reset transaction.** A typed confirmed-reset service builds first-run gameplay state,
  copies preserved preference data, validates and atomically saves the candidate, then
  swaps the in-memory state. Cancel/dismiss/failure cannot mutate memory or disk; platform
  achievements are not revoked.

## Delegated defaults — record in `DECISIONS.md` at the gate

Agent-tunable, owner reviews results rather than choosing raw values:

- Burning cadence/pain, spray presentation, grenade radius/impulse/falloff, pellet spread.
- Pullback profiles and gun projectile speeds/masses.
- Safe/evictable classification inside FR-014; eviction stays oldest eligible safe object.
- Power Grab pull-force/damping/release multipliers and higher safe cap. Values live in
  typed Resources, preserve the shared Normal stretch limit, and remain provisional until
  the Normal-versus-Power owner feel gate.
- Final prices, cash-per-pain, and passive rate, calibrated to the locked 209-minute table.

Resolved owner decisions (do not reopen):

- Power Grab is one selectable permanent purchase between Fire Sprayer and Repair Kit;
  Normal Grab remains selectable; Power cannot be escaped through sustained strain.
- The exact 16-interaction catalogue and Nerf position are locked.
- The representative benchmark is about 120 active plus 89 background minutes; official
  targets are completionist medians ±15%, with separate skip/save strategies.
- Reset clears all gameplay progression/counters but preserves preferences and already
  awarded platform achievements.

Still external/owner-gated:

- clean-room dock direction and complete Settings surface as described in the dock plan;
- per-slice control/feel confirmation where an earlier task still calls for it;
- final Windows/reference-hardware and owner feel/pacing acceptance.

## Tasks

Order is dependency-driven and matches the confirmed progression: guns need the
platform (Task 5 before 9), Fire Sprayer needs Burning (Task 7), Repair Kit clears
Burning so it lands after it (Task 10), the upgrade and calibration close.

The dock's Tasks 1–5 (`docs/UI_FLOATING_DOCK_PLAN.md`) are independent of Tasks 2–10
here only **after** the dock owner gate above is resolved and the draft is revised. They
may then run as a parallel track; dock Task 6 depends on Task 0/1 below.

Each tool slice has two explicit completion states:

1. **Engineering-complete:** unit/scenario coverage passes, MCP verification drives the
   behavior through real input, and that interaction is promoted into the slice's M5
   journey.
2. **Shop-visible:** the owner accepts the slice's feel on real Windows and the
   acceptance is recorded in `DECISIONS.md`; only then may its Resource set
   `Visible = true`.

### Task 0 — Catalogue spine

Create the typed `ToolDefinition` Resource schema and `15` `.tres` definitions with
clearly marked provisional prices and `Visible = false` for unfinished content; create
the engine-free `CataloguePolicy`; extend `ToolId`/`ContentIds`; generalize locked
selection so selecting any unowned selectable entry is rejected at the progress/tool
selection boundary rather than per tool. Replace
`EconomyService.Purchase(contentId, priceMilliCredits)` with the authoritative
`Purchase(contentId)` lookup described above. Add the FR-013.1 starting-set assertion.

After the owner confirms the reset erase/preserve matrix, add a `ResetProgress`
operation that requires a non-forgeable explicit confirmation result from the UI and
mutates exactly that matrix. Until then, catalogue and purchase work may proceed, but
the reset operation must not be implemented.

**Accept:** unit — FR-019 filter (upgrade never selectable, never in tools list);
locked selection rejected for every unowned entry and allowed after purchase; purchase
rejects unknown, starting, invisible, invalid, already-owned, and insufficient-funds
entries without mutation; no caller-controlled price parameter exists and the charged
amount equals the authoritative Resource price; visible Resource validation rejects
missing translation keys/assets and duplicate IDs; save round-trip with a future
unknown catalogue ID preserved. Once reset is unblocked,
unit tests cover every field in the owner-confirmed erase/preserve matrix and prove a
declined/missing confirmation changes nothing. Existing Baseball behavior remains
unchanged (`baseball_pullback` still green).

### Task 1 — Dock catalogue binding (dock plan Task 6 + reset UI)

Prereq: the owner-approved clean-room dock direction is recorded, the dock plan is
revised, its Tasks 1–5 are complete, and the reset matrix is confirmed. Bind
`DockToolsSection` / `DockShopSection` to the real catalogue and authoritative
`EconomyService.Purchase(contentId)`; owned rows show `OWNED`; upgrade shows in shop
only; invisible entries are absent entirely. Add a real Settings section for the
currently supported settings (not a paint stub or a partial System substitute), with
final art/accessibility polish still deferred to M7. Put progression reset behind the
explicit confirmation dialog.

**Accept:** dock plan Task 6 criteria against the real catalogue, plus: unfinished
entries absent from both grid and shop; purchase → relaunch → still owned (flush
path); authoritative Resource price displayed and charged; unknown/starting/invisible
purchase intents rejected with feedback and no state change; every currently supported
setting is reachable in the retractable panel and persists through its existing seam;
reset flow applies exactly the owner-confirmed matrix and a declined dialog changes
nothing.

### Task 2 — Loose-object budget (FR-014)

Audit and extend the existing `LooseObjectRegistry` integration at every **loose-object**
spawn point (laboratory toys, Baseball, Meal/Drink/Repair Kit, Soccer Ball, Grenade).
Protected flags flow from real state (player Grab, buddy hold, committed consume/launch,
live fuse, hazardous/burning state). If the eviction choice is extracted for pure unit
coverage, `LooseObjectRegistry` remains the only runtime owner.

Bullets/pellets use a separate bounded pool with maximum lifetime/distance and never
register as loose objects. VFX particles remain non-gameplay and separately pooled.

**Accept:** preserve the existing FR-014 tests and add pure policy tests if extraction
occurs — cap 24, oldest-safe eviction, protected never evicted, refusal when all are
protected. Scenario `object_budget`: admit 30 independently registered balls through
the real registry → count never exceeds 24 and the held ball survives. Gun scenario:
active bullets/pellets do not change `LooseObjectRegistry.Count`, expire/return to their
pool, and cannot accumulate without bound.

### Task 3 — Meal

First care consumable through the M4 machinery with catalogue identity: pullback-
launched care object, buddy approach/catch/eat via existing arbiter actions,
`+10` mood on successful consumption, `60` s cooldown starting only on success
(cancel/drop/miss does not start it), replaces `care.lab_food` as the shipped food
(lab food remains a lab-only spawn or is retired — owner preference at the slice
review). Confirm the launch chord with the owner at slice start.

**Accept:** scenario `meal_consume`: launch → catch → five-bite eat → +10 mood exactly
once → second Meal within 60 s not consumable → after cooldown consumable again; drop
mid-eat starts no cooldown. Fun/interest `fun.treat` still fires.

### Task 4 — Baseball Bat

Cursor-tethered physical collider on the Boxing Glove mechanism (tether follow, real
swing speed → contact impulse → shared pain pipeline, no tool multiplier), distinct
shape/mass/tether data and distinct content ID for attribution/memory/stats.

**Accept:** scenario `bat_swing`: stationary hover produces no pain (dedup/graze
rules); a fast swing produces pain attributed to `tool.baseball_bat`; harmful-history
records the bat, not the glove. Glove regression scenarios untouched.

**Feel refinement (owner, 2026-07-30):** the engineering-complete bat is superseded by the
Home-Run-Bat treatment — grip/charge/release with charged hit lag and a 3D look. Full
handoff plan: `docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md`. The feel gate for this slice is
that plan's Task G, not the original accept line alone.

### Task 5 — Cursor-gun platform + Pistol

`CursorAimModel` + `GunModel` + `CursorGunComponent` + pooled CCD projectile. Pistol
data: magazine 8, 0.25 s minimum interval, 1.2 s reload, `R` reload, auto-reload when
fired empty, fire once per primary press. Wheel aim offset + clear-on-motion. Reload
binds the **existing** `InputActions.Reload` (`buddy_reload`) action — do not define a
new input action — and reaches the gun through the `ToolInputFrame` path, not a direct
key read.

**Accept:** unit — full cadence/reload/auto-reload state table in ticks; aim vector
and wheel-offset lifecycle. Scenario `pistol_fire`: eight shots empty the magazine
without starting reload; the ninth eligible primary press attempts an empty shot and
starts auto-reload; projectile hit produces attributed pain; CCD proven by a
point-blank high-speed shot never tunneling; mid-reload primary presses ignored.

### Task 6 — Grenade

Pullback-launched explosive: `2.5` s fuse **starting at launch, not press**; blast =
radial impulse + attributed pain with falloff (provisional); an inexperienced buddy
may investigate/catch it (existing curiosity/approach path); once grenade harm is in
harmful history the buddy flees/discards instead (existing memory + priority-3 drop
path). Budget-protected while fuse is live.

**Accept:** scenario `grenade_fuse`: fuse timer starts only on launch (held preview
does not tick it); blast damages by distance; a buddy holding it at detonation takes
the close-range result and drops nothing afterward (nothing left to hold); after a
harmful blast the next grenade triggers flee/discard, seeds 1 and 7.

**Owner refinement (2026-07-31):** the fuse rule above is **superseded** — the owner
replaced it with a pin mechanic (pin drops on the first RMB press; safe while
player-held; explodes 3 s after release with the pin out), set damage at ≈ 5 pistol
bullets, and specified the presentation (small explosion, medium camera kick,
placeholder boom/landing sounds, simple 3D model, heavier-than-Baseball heft on the
same arc). Full handoff plan: `docs/M5_TASK6_GRENADE_PLAN.md` — the slice's spec and
accept criteria are that plan's, not this section's; RAGDOLL §9.2's fuse sentence is
amended at its bookkeeping task.

### Task 7 — Burning + Fire Sprayer

`BurningStatus` per §9.3 (4 s apply, refresh capped 8 s, periodic attributed pain,
panic via arbiter priority 3, drop held items, cleared by hard reposition) + Fire
Sprayer: cursor weapon on the Task 5 aim model, hold-primary continuous spray,
contact applies/refreshes Burning. Effects honor reduced-particles/motion/
photosensitivity settings (FR-017.3) from day one, not as polish.

**Accept:** unit — status timers/refresh-cap/clear table. Scenario `burning_status`:
spray → burn 4 s of attributed ticks; sustained spray caps at 8 s remaining; buddy
drops a held ball and panics; pain/mood per tick match the shared formula; status
survives KO but hard reposition clears it.

**Refined (2026-07-31):** handoff plan at `docs/M5_TASK7_BURNING_AND_FIRE_SPRAYER_PLAN.md`;
the slice's authoritative spec and accept criteria are that plan's. It also builds the
FR-017.3 `EffectsSettings` read seam (no runtime consumer of the saved settings exists yet).

### Task 8 — Soccer Ball + Drink

Two data-driven reuses: Soccer Ball = second pullback loose object with its own
empirical preset and foot-only play behavior (never catch/hold/toss); Drink = second care consumable (+5 mood, 60 s own
cooldown, independent of Meal's cooldown).

**Accept:** scenario `soccer_and_drink`: the Soccer Ball is never picked up, player touch
enables trapping, floor contact preserves it, wall/ceiling contact disables it without
disabling a direct kick; Meal and Drink cooldowns proven independent (consume Meal, immediately
consume Drink → both succeed); presets verifiably distinct (different measured
bounce/settle signature than Baseball).

**Refined (2026-07-31):** handoff plan at `docs/M5_TASK8_SOCCER_BALL_AND_DRINK_PLAN.md`;
the slice's authoritative spec and accept criteria are that plan's. One code addition:
authored restitution (`Bounce`) on `LooseObjectProfile` — nothing authors bounce today.

### Task 9 — Shotgun

`GunModel` second profile: capacity 5, 0.9 s interval, 2 s reload, one press fires
`6` CCD pellets with provisional spread; per-pellet attribution through the shared
pipeline (dedup rules mean simultaneous pellets on one part form one contact episode —
assert the actual accepted-event count, don't assume 6).

**Accept:** unit — profile table. Scenario `shotgun_spread`: 6 pellets spawn per
press; cadence/reload honored; multi-part hits attribute per part; point-blank no
tunneling.

**Refined (2026-07-31):** handoff plan at `docs/M5_TASK9_SHOTGUN_PLAN.md`; the slice's
authoritative spec and accept criteria are that plan's. It resolves the dedup reading:
pellets of one shot share one interaction id, so one shot into one part scores once.

### Task 10 — Repair Kit

Third care consumable: pullback-launched, successful application grants `+20` mood,
clears rolling/transient pain and harmful statuses **including Burning**, and never
shortens an active 4 s knockout (`ClearRollingPain` already honors this — wire, don't
re-implement). **No cooldown and no appetite gate** (owner, 2026-07-29): it is not food, so
nothing rations it. Its profile therefore authors `ConsumeCooldownTicks = 0` and
`ConsumeHungerFill = 0`, and a full buddy must still accept one.

**Accept:** scenario `repair_kit`: burning buddy → apply → Burning cleared + 20 mood;
KO'd buddy → apply → KO end time unchanged while rolling pain clears; a failed/dropped
application applies nothing; a buddy with a full hunger bar still accepts one (the appetite
rule is for food, and the Repair Kit is not).

**Refined (2026-07-31):** handoff plan at `docs/M5_TASK10_REPAIR_KIT_PLAN.md`; the slice's
authoritative spec and accept criteria are that plan's. It resolves how FR-008.7/FR-010.10
are reachable at all (a KO'd or burning buddy can never eat): player contact-application on
a thrown kit, flagged as an owner-gate default; RAGDOLL's stale 120 s cooldown rows are
amended at its bookkeeping task.

### Task 11 — Power Grab (FR-019)

Implement the architecture and ordered packets in
`docs/M5_TASK11_TO_13_HANDOFF_PLAN.md` §3:

1. append `ToolId.PowerGrab = 15`, add `tool.power_grab`, and migrate schema 5
   `upgrade.strength` ownership to schema 6 without repurposing the old ID;
2. add immutable per-acquisition Normal/Power resolved settings and typed release reasons;
3. extend the one stretch limiter/controller so Power shares the Normal stretch maximum
   but cannot force-snap from sustained strain;
4. route both grab selections through the same target query/controller, with a safe cancel
   on tool change;
5. replace the hidden passive catalogue entry with a selectable Power Grab entry;
6. prove buddy/loose-object behavior, release caps, failure releases, persistence,
   migration, long-hold safety, and downstream damage/economy invariance.

**Accept:** unit/migration/catalogue tests, `power_grab` scenario and
`m5_power_grab` journey in both presentations on committed seeds, then owner accepts the
side-by-side “dramatic but controllable” feel.

**Status:** implemented 2026-08-02 (commit `6d77837`). Only the owner feel gate remains.

### Task 12 — Economy calibration and simulation

Follow `docs/M5_TASK11_TO_13_HANDOFF_PLAN.md` §4. The runner replays production
economy paths and loads actual Resources; no duplicate launch prices or simplified payout
formula are allowed.

Official completionist cumulative targets are:

`3, 7, 13, 21, 41, 52, 76, 104, 120, 138, 184, 209` minutes for the exact order in
Scope, each within ±15% of the median. Pistol, Grenade, Fire Sprayer, and Shotgun are the
only high-value items. Also run unrestricted save/skip strategies, including saving for
each high-value item and preferring Power Grab.

**Accept:** deterministic fingerprinted JSON/Markdown for all seeds/strategies; every
completionist row in band; active dominance; peak passive approximately 25% of active;
ordinary events do not skip multiple milestones; real dedup proves
positive/zero/positive; owner accepts the final pacing report.

**Status:** complete 2026-08-02 (commit `7de2e81`); the whole catalogue was re-priced.

### Task 13 — Reset Progress, composition, regression, docs, and owner exit

Follow `docs/M5_TASK11_TO_13_HANDOFF_PLAN.md` §5. Add a transactional confirmed reset
that atomically commits a first-run gameplay payload while preserving preferences and never
revoking platform achievements. Add full before/after and failure-path tests.

Preferences are preserved because reset never writes the settings payload — it is a
separate file written by a separate call — not by copy-forward code. Platform achievements
are untouched because no achievements adapter exists to call.

Generate the launch inventory and regression matrix from authoritative registries. Update
`m5_shop_progression` to purchase/use/reload all twelve items in order, exercise a skip
strategy, switch Normal/Power Grab, confirm and cancel reset, and verify reload at
checkpoints. Keep the accelerated journey separate from Task 12's 209-minute calibration.

**Accept:** exact 16-item inventory, schema/reset/progression journeys, complete registered
scenario/journey sweep, allocation/stress/Windows evidence, reconciled docs, owner
Power/economy/catalogue gates, and the dock clean-room gate.

## New test surface (summary)

Unit coverage includes Resource-to-snapshot catalogue validation, authoritative purchase
and free-skip rules, locked selection, schema-5 Strength-alias migration, immutable
Normal/Power grab resolution, shared stretch/escape policy, typed release reasons,
transactional Reset Progress and complete failure-path equality, and production-path
`EconomySimulation`.

New/updated scenarios include `object_budget`, `meal_consume`, `bat_swing`,
`pistol_fire`, `grenade_fuse`, `burning_status`, `soccer_and_drink`,
`shotgun_spread`, `repair_kit`, `power_grab`, and `economy_calibration`, plus
existing `baseball_pullback`. Journeys include one real-input `m5_<tool>` journey per
slice, `m5_power_grab`, and the registry-derived `m5_shop_progression` covering
purchase order, free skipping, Normal/Power switching, reload checkpoints, confirmed
reset, and cancelled reset.

Use declared seeds and fixtures, no fixed sleeps. Run seeds 1 and 7 at minimum, both
`mii3d` and `legacy`, with `--fixed-fps 120`. Task 12's economy seed set contains
at least five committed seeds and reports medians.

## Verification

The standard three, before calling any task done:

```
dotnet test
dotnet build DesktopBuddy.sln -c Debug
<godot> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<n>
```

Plus `devtools\verification\quick_validate.bat`; the slice's real-input MCP verification and promoted
journey; at Tasks 1, 12, and 13 the full catalogue sweep in both presentation modes;
and at Task 13 the reference-hardware/allocation/pool performance gate. "Done without
running the suite" remains the failure mode this plan exists to prevent.

## Progress

- 2026-08-02 — **Tasks 11–13 product decisions resolved and architecture handoff
  replaced.** Locked the 16 selectable interactions and purchase order, replaced the
  passive Strength concept with selectable Power Grab, specified schema-6 legacy
  migration, adopted the 209-minute completionist schedule and casual 120-active/
  89-background benchmark, defined skip-strategy coverage, and fixed the Reset Progress
  erase/preserve matrix. `docs/M5_TASK11_TO_13_HANDOFF_PLAN.md` now contains ordered
  file-level packets, runtime contracts, transaction boundaries, simulation/report
  contracts, failure paths, and acceptance tests suitable for a smaller implementation
  agent.

- 2026-08-01 — **Tasks 11–13 refined to agent-handoff fidelity.** Added
  `docs/M5_TASK11_TO_13_HANDOFF_PLAN.md` with owner decision packets, exact existing
  seams/file targets, ordered implementation packets, executable acceptance checks,
  registry-derived full regression, a two-layer performance gate, documentation/owner
  evidence ownership, and a fixed handoff template. The audit also found a blocking
  source-of-truth conflict: accepted Nerf/code currently describe `16` catalogue entries
  including Strength, while current FR-013.2 says `15` and omits Nerf. No implementation
  or owner decision was inferred; Tasks 11–13 remain gated as stated.
- 2026-07-29 — Plan written. Baseball slice pre-existing (see status header).
  Nothing else started.
- 2026-07-29 — Audit corrections integrated: Resource-authored catalogue,
  authoritative purchase lookup, existing loose-object registry ownership and
  projectile separation, explicit reset/dock/Strength owner gates, per-tool journeys,
  visibility sequencing, firearm empty-trigger reload boundary, and M5 performance
  gate.
- 2026-07-29 — **Task 0 catalogue spine landed** (reset operation excluded, still owner-
  blocked). `ToolId` appended through `RepairKit = 13`; `ContentIds` gained the nine new
  `tool.*` constants plus `upgrade.strength` and the `IsCatalogueEntry` predicate;
  `ToolCatalog.CategoryOf` extended. New engine-free `Domain/Content/CatalogueEntry`,
  `ToolCatalogue` (structural validation), and `CataloguePolicy` (shop/tool filtering,
  purchase eligibility, FR-013.1 starting set, FR-013.2 launch completeness). New
  `src/Content/ToolDefinition` + `CatalogueDefinition` + `CatalogueLoader` with the 15
  `data/catalogue/*.tres` definitions; `boot_smoke` now validates the shipped catalogue.
  `EconomyService.Purchase(contentId)` and `BuddyProgressState.Purchase(contentId,
  catalogue)` are authoritative — the caller-supplied price parameter is gone.
  `PurchaseStatus` gained `NotAvailable` (unfinished) and `NotPurchasable` (starting).
  Fixed in passing: save load filtered unlocks with `IsTool`, which would have discarded
  ownership of `upgrade.strength`; it now filters with `IsCatalogueEntry`.
  Provisional data: prices in credits equal to the FR-013.4 target minutes (Baseball 3 …
  Repair Kit 120); the Strength Upgrade is unpriced and invisible pending its owner
  decisions. Only the four starting tools and Baseball are `Visible = true`.
  Verified: domain 772/772, quick suite 17/17, `baseball_pullback` green on seeds 1 and 7
  in both presentations, `boot_smoke` green.
- 2026-07-29 — **Owner gates resolved** (see `DECISIONS.md`, same date): Baseball feel
  ACCEPTED, so it stays `Visible = true`; the Meal reuses the Baseball launch chord, which
  unblocks Task 3's input work. The acceptance came with a defect report — a ball resting
  completely in a corner was never picked up — fixed with the new `corner_scoop` scenario
  (both corners, in the quick suite): a committed object approach now spends the ambient
  wall-avoid margin and stops on torso contact, and the ground-scoop gate measures the
  object's near surface instead of its centre. Verified: domain 772/772, quick suite 18/18,
  the object/behaviour scenario sweep green on seeds 1 and 7 (plus legacy presentation).
- 2026-07-29 — **Task 2 loose-object budget DONE** (except the projectile half, which needs
  Task 5's guns to exist). Audited every loose-object spawn path: only two register today —
  `BuddyLab.SpawnLooseObject` (lab toys, food) and `PullbackLauncherComponent` (Baseball) —
  and both go through the one registry, with protection flowing from real state (player Grab
  each tick, buddy hold, launcher `SetProtected` across aim/launch/cancel, authored
  hazardous/safe flags). The oldest-safe decision is extracted to the engine-free
  `Domain/Interaction/LooseObjectAdmissionPolicy` (cap `24` now declared once, there); the
  registry delegates over a `stackalloc` span and remains the sole runtime owner of identity,
  flags, and cleanup. Added a debug-only audit: a profile-configured `LooseObjectBody` that
  reaches the tree unregistered logs an error, so a future spawn path cannot quietly escape
  the budget (the M1/M3 legacy radius/mass props are exempt by design and verified silent).
  New scenario `object_budget` — 30 spawns against the cap through the real registry, count
  peaks at 24, the buddy's held ball survives 7 evictions, oldest-first order proven — plus 7
  policy unit tests. Deferred to Task 5: the assertion that bullets/pellets never change
  `LooseObjectRegistry.Count`. Verified: domain 783/783, quick suite 19/19, `object_budget`
  green seeds 1 and 7 in both presentations.
- 2026-07-29 — **Task 3 Meal engineering-complete** (shop-visible still pending the owner's
  feel gate: `tool_meal.tres` stays `Visible = false`). What edible means is now authored
  data — `LooseObjectProfile` carries `ConsumeMoodGain` and `ConsumeCooldownTicks`, validated,
  and the consume path reads the held item's own tuning instead of testing for
  `care.lab_food`. `data/objects/meal.tres` is the first catalogue consumable (`+10` mood,
  `7200`-tick cooldown, FR-008.4). The launcher is generalised from "the Baseball" to an
  authored `LaunchableProfiles` array with per-object attribution and per-object ownership
  checks, so the Soccer Ball, Grenade, Drink, and Repair Kit are `.tres` references rather
  than new input code; `HasBall`/`CurrentBall` became `HasLaunchable`/`CurrentLaunchable`.
  Lab key `6` places a Meal on the confirmed chord (key `5` stays the Baseball). New scenario
  `meal_consume` (abandoned meal charges nothing, finished meal pays `+10` once and starts the
  exact `7200`-tick cooldown, a second meal inside the window is refused, and after the
  cooldown elapses the next one is eaten) and the real-input journey `m5_meal` (spawn key →
  Grab carry → aim cancel → pullback launch → fetch and eat). Lab food is retained as the `E`
  key's dev spawn; retiring it is an owner call at the slice review. Verified: domain
  783/783, quick suite 21/21, `meal_consume` + `m5_meal` + `baseball_pullback` green on seeds
  1 and 7 in both presentations.
- 2026-07-29 — **Owner review of the Meal slice: two defects fixed, one behaviour added**
  (see `DECISIONS.md`, "Hunger Replaces the Food Cooldown"). (1) The eaten item rode the
  carrying hand's socket while both hands lifted to the mouth; it now rides the midpoint
  between the hands whenever the eat reach is active, guarded by a `meal_consume` check on
  the sideways offset and resting height. (2) A full buddy fetched, dropped, and re-fetched
  food forever. Appetite now replaces the food reuse cooldown: new engine-free
  `Domain/Mood/HungerModel` (`200`-point bar, accept iff `fullness + fill <= 200`, three
  drain rates) plus `HungerActivityPolicy`; fullness is persisted (save schema 4 → 5, legacy
  saves resume empty); `LooseObjectProfile` authors `ConsumeHungerFill`; the Meal fills `50`
  and its cooldown is `0`. Refusal is a performance: pick up once, head-shake through the new
  `ActivityId.Refuse` clip, put it down, then ignore that specific item until there is room
  for it — other food is still judged on its own size. FR-008.4/.5/.10 amended; FR-008.16–19
  added; FR-008.6 followed the same day — the Repair Kit has **no** cooldown and no appetite
  gate (owner), so nothing rations it. Verified: domain 805/805, quick suite 21/21, `meal_consume` (now including
  the refusal loop and the recovery) + `m5_meal` + `consume_care_cooldown` + `activity_clips`
  + `m36_expressive` + `care_persistence` green on seeds 1 and 7 in both presentations.
- 2026-07-29 — Owner correction on the refusal performance (FR-008.19 amended). Three defects
  in the first cut: the refusal shared `EatReachActive`, so the food rode the midpoint between
  both hands instead of the one hand that picked it up; the `ActivityId.Refuse` clip was never
  requested from the selector, so no head-shake ever played; and the resolve threw the item
  aside on the discard impulse, which read as the food glitching away. Now: the refusal keeps
  the ordinary one-handed carry (only `IsStationary` is shared), the animator requests the clip
  and **seeks** it by the new `BehaviorActivityComponent.RefuseProgress` so the gesture
  fills the `96`-tick window, facing and the head look-at are both forced frontal for the
  duration (the "no" is aimed at the player), the shake amplitude is authored as
  a bounded profile value, and the item is dropped at
  rest below the buddy. `meal_consume` gains three checks — one hand, the two-way shake at a
  frontal buddy, and the at-rest drop below the buddy with no discard. Verified: domain
  808/808, quick suite 21/21, `meal_consume` seeds 1/7/13, `activity_clips`,
  `consume_care_cooldown`, `lookat_priority_and_cone`, `facing_follows_walk`,
  `object_toss_discard`, `face_composition`, and the `m36_expressive` + `m5_meal` journeys on
  seeds 1 and 7.
- 2026-07-30 — Owner clarified that “shake” means **rotation around the neck’s vertical
  axis**, not lateral head translation. The refusal now starts from a frontal visual body,
  clears residual look-at pitch/yaw, and plays four smooth damped yaw lobes
  (left `30°` → right `24.9°` → left `20.1°` → right `12°` → neutral). It crosses the
  middle continuously with no hold, never exceeds four alternating extremes, leaves pitch
  and roll untouched, and resets the rotation before any following clip. The typed tuning is
  renamed from pixel `ActivityRefuseAmplitude` to degree-valued
  `ActivityRefuseYawDegrees`; selection is also pinned to authoritative
  `BehaviorActivityComponent.IsRefusing`, so a long render frame cannot expire the visual
  before the routed-tick refusal window. `meal_consume` now rejects sideways translation,
  excess/undamped lobes, non-frontal composition, center pauses, residual rotation, and
  pitch/roll activity.
- 2026-07-29 — Second audit pass: all first-audit factual claims re-verified against
  the repository (`LooseObjectRegistry` cap/eviction/protection, arbiter `Hazard = 3`
  branch, `ToolUses`/`ToolPainMilli` statistics, AGENT_VERIFICATION §7 per-tool
  journey rule, NFR-002 budgets, ARCHITECTURE §6/§15, RAGDOLL §10 projectile
  exclusion). Added: TEST_PLAN §4 as Task 12's contract with its four proof
  obligations and median-across-seeds wording; statistics seam named explicitly;
  reload bound to existing `buddy_reload` action through `ToolInputFrame`; journey
  naming/fixture convention (`m5_<tool>`).
- 2026-07-30 — **Task 3 Meal CLOSED — owner feel gate accepted.** `tool_meal.tres` is now
  `Visible = true`, making the Meal the second shop-offered entry after the Baseball; the
  price stays the provisional FR-013.4 placeholder until Task 12. Recorded in `DECISIONS.md`
  ("M5 Meal Slice Accepted"). The plan's open retire-or-keep question for `care.lab_food`
  was not answered, so it stays a dev-only `E`-key spawn, provisionally, to be revisited at
  the Task 13 gate. Verified: `boot_smoke` green (catalogue validation covers visibility).
- 2026-07-30 — Owner settled the open question: lab food is **kept** as a dev-only `E`-key
  spawn, not retired, and the M3.6/M4 scenarios keep using it.
- 2026-07-30 — **Task 4 Baseball Bat engineering-complete** (shop-visible still pending the
  owner's feel gate: `tool_baseball_bat.tres` stays `Visible = false`). The Boxing Glove
  mechanism is generalised rather than copied: `CursorToolController` holds an authored
  `CursorToolProfile` array and activates the one matching the selected tool, so the bat is
  a `.tres` plus a content ID and the next cursor-tethered tool will be too. The collider
  takes its shape, mass, tether, colours, and attribution identity from its own profile;
  facing, head look-at, and the pointer path now ask `DrivesTool` instead of naming the
  glove, and impact feedback asks `AttributesContent`. New engine-free
  `Domain/Physics/AlignmentTorque` — a bounded damped angular servo, the rotational
  counterpart of `GrabTether` — holds the elongated barrel square to the cursor's travel and
  folds out the bat's half-turn symmetry; a zero stiffness disables it for round tools.
  `Body2DVisual3D` re-shapes per spawn (capsule or sphere) because the collider now depends
  on the selected tool. Lab key `K` selects the bat in both the pointer component and the
  laboratory controls; the dev catalogue unlocks it. New scenario `bat_swing` (own elongated
  collider and ID, a parked bat scoring nothing across `120` ticks with the surface gap
  measured so the check cannot pass vacuously, a real swing scoring pain attributed to
  `tool.baseball_bat` with a best alignment error of `0.00°`, harmful history and pain
  statistics naming the bat and not the glove, and a tool swap replacing collider and
  identity together) plus the real-input journey `m5_baseball_bat`; both are in the quick
  suite, now 23/23. Two deliberate deferrals are recorded in `DECISIONS.md`: the buddy's
  learned defense stays glove-only pending an owner feel call, and `ProgressStatistics.ToolUses`
  is left alone because it has no runtime writer and what counts as one "use" is an owner
  decision. Verified: build 0/0, domain 836/836, quick suite 23/23, `bat_swing` seeds 1/7/13
  plus legacy presentation, the `m5_baseball_bat` / `m3_glove_strike` / `m3_tool_feel` /
  `m36_expressive` journeys on seeds 1 and 7 in both presentations, and a full sweep of the
  scenario catalogue and journey catalogue on seed 1 (the only two reds were the documented
  window-only `owner_feedback_visual` and `lab_idle_soak` run without `--artifacts`, which
  passes when given one).
- 2026-07-30 — **Task 4 Home-Run Bat refinement CLOSED — owner feel gate accepted.**
  The owner accepted the revised low-to-floor charge placement, stronger full-charge
  launch, staged `1/3/5`-second glints, and compact contact burst. Task H's promoted
  journey and full regression are green, and `tool_baseball_bat.tres` is now
  `Visible = true`. Its `20`-credit price remains provisional until Task 12.
  **Next: Task 5 — cursor-gun platform + Pistol.**
- 2026-07-31 — **Task 5 cursor-gun platform + Pistol engineering-complete** (shop-visible
  still pending the owner's feel gate: `tool_pistol.tres` stays `Visible = false`). Three new
  engine-free domain types carry the rules: `Domain/Tools/CursorAimModel` (forward follows the
  latest non-trivial pointer motion, the wheel pitches that aim up or down, the next motion
  clears the offset — pitched about the aim's own horizontal side so "up" is up whichever way
  the weapon points) and `Domain/Tools/GunModel` (magazine, shot interval, reload, fire on the
  press edge, auto-reload on an empty pull, unlimited reserve), both stated in routed ticks.
  `src/Tools/CursorGunComponent` is the thin driver, `src/Tools/GunProfile` the authored data,
  and `src/Tools/ProjectileBody` a pooled physical projectile that never enters
  `LooseObjectRegistry`; the Shotgun is therefore a `.tres` plus a content ID. Reload binds the
  existing `buddy_reload` action and reaches the gun through the pointer's queued-input path,
  never a direct key read. Lab key `J` selects the Pistol; the dev catalogue unlocks it.
  New scenario `pistol_fire` (11 checks) and real-input journey `m5_pistol` (10 assertions),
  both in the quick suite, now 26/26. Deferred Task 2 assertion discharged here: bullets never
  change `LooseObjectRegistry.Count`, peak inside their pool, and return to it.
  **Two findings recorded in `DECISIONS.md`:** Godot's `RigidBody2D.ContinuousCd` destroys a
  shot's momentum instead of transferring it (measured pain `85` disabled vs `0` with
  `CastRay`, and `CastShape` let a shot pass clean through a head), so no-tunneling is
  guaranteed geometrically by a validated `24` px per-tick travel bound rather than by that
  setting; and muzzle speed, not projectile mass, is the lever on how much a gun hurts.
  Two deliberate deferrals: the buddy's facing/head look-at still ignores a drawn gun (a feel
  call, and `CursorGunComponent.DrivesTool` is the seam ready for it), and the gun's cursor
  visual is a minimal 2D barrel — full presentation is M7's art pass.
  Verified: build 0/0, domain 971/971, quick suite 26/26, `pistol_fire` seeds 1/7/13 plus
  legacy presentation, `m5_pistol` seeds 1/7 in both presentations, and a 32-scenario /
  6-journey regression sweep on seed 1 all green.
- 2026-08-02 — **Task 13 complete (reset, composition audit, regression, docs).**
  `src/App/ProgressReset.cs` owns the confirmed reset: it builds a first-run state with the
  shared factory (`CreateNewProgress`, moved out of `Bootstrap` so a new player and a reset
  player are made the same way) and installs it over the live `BuddyProgressState` through
  the new `Adopt`, which routes to the same private `Apply` the constructor uses. Rewriting
  in place rather than swapping the reference means none of the seven holders of that
  reference has to be re-bound, and a failed write is rolled back to the exact prior
  snapshot — all-or-nothing in memory and on disk, with the save file never deleted.
  Settings are preserved by not being written; there is no copy-forward code.
  The trigger is the existing tray seam: `TrayCommandComponent.RequestResetProgress()` arms
  and raises `ResetProgressRequested`, `ConfirmResetProgress()` inside a 30 s window raises
  `ResetProgressConfirmed`, and a cancel, a lapse, or any other tray command disarms it —
  so "Cancel is the default, two affirmative actions" is assertable with no dialog in the
  build. The modal stays with `docs/UI_FLOATING_DOCK_PLAN.md` Task 7.
  Composition audit: `ValidateLaunchCatalogue` now rejects a non-ownable or non-selectable
  entry, two entries selling one `ToolId`, and any `upgrade.strength`; `BuddyLab`'s
  hand-listed dev unlocks now derive from `CataloguePolicy.SelectableEntries` (the only
  hand-maintained tool list found); `boot_smoke` asserts all three composition roots share
  `power_grab_profile.tres` and that the sandbox sells from the validated catalogue.
  New journey `m5_shop_progression` (13 assertions, quick-suite step 41) walks one save from
  a first run to all sixteen owned — purchases through `EconomyService`, earnings through
  the ledger, reload checkpoints after Nerf and Power Grab, the no-prerequisite skip proof,
  and the reset and cancel branches. `power_grab` gained the per-tick allocation probe
  (Power allocates 0 B, same as Normal) and an orphaned-body check.
  Verified: build 0/0, domain 1150/1150, quick suite 41/41, `m5_shop_progression` seeds 1/7
  in both presentations, and a full scenario/journey sweep in `legacy`.
  **Remaining for M5 exit: the Repair Kit and Power Grab owner feel gates, the Windows 10/11
  standalone run, and the dock (which carries the reset confirmation modal).**
