# Milestone 4 — Personality, Care, and Persistence

Status: **READY FOR IMPLEMENTATION — all owner decisions resolved 2026-07-24**
(see `docs/DECISIONS.md`, "Milestone 4 pre-plan — owner decisions resolved").
M3.6 is complete and owner-accepted (2026-07-21); the owner-feedback rework and
the 2026-07-24 runtime fixes are closed with no known red. Implementation begins
at Task 0.

Authoritative sources this plan composes (it invents no product behavior):

- `docs/ROADMAP.md` — Milestone 4 deliverables and exit criteria.
- `docs/PRODUCT_REQUIREMENTS.md` — FR-005.3–FR-005.6 (autonomy/object behavior),
  FR-007 (mood/trust/memory), FR-008 (care), FR-012 (passive income),
  FR-015 (save/load/resume), FR-016.2/16.3/16.8 (hidden/lifecycle clock).
- `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` §4 — the behavior-arbitration priority ladder.
- `docs/ARCHITECTURE.md` §5 (stable string IDs), §7 (fixed-tick order), §8 (time and
  lifecycle), §11 (mood/economy boundaries), §12 (save architecture), §23 (physics
  integration rules), §24 (hidden-to-tray mechanics).
- `docs/TEST_PLAN.md` §2 — the "Mood, Trust, and Passive Income" and
  "Persistence" unit suites this milestone must turn green.
- `docs/AGENT_VERIFICATION_AND_E2E.md` §7 — the Milestone 4 journey row:
  care/persistence journeys including save → relaunch → safe resume.
- `docs/DECISIONS.md` — ambient timer jumping OFF (2026-07-20); deferred jump
  personality (2026-07-14); learned-threat guard/flee behavior (2026-07-24).

## Scope

From `ROADMAP.md` Milestone 4, restated as five work streams:

1. **Autonomy** — approach/flee/catch/hold/consume/toss decisions behind the
   RAGDOLL §4 priority ladder (a real `BehaviorArbiter` and `ObjectInteraction`
   pipeline; today only ambient walk and tool-threat reactions exist).
2. **Memory and trust** — persistent per-tool harmful history and the mood-60
   trust reset (domain rules exist in `MoodModel`; persistence and string-ID
   alignment do not).
3. **Traits** — per-save ambient jump propensity, sampled only at new-save
   creation, combined with obstacle/situation evidence (DECISIONS 2026-07-14;
   ambient pure-timer jumping stays OFF per DECISIONS 2026-07-20).
4. **Economy clock** — passive-income service, mood decay, care gains/cooldowns,
   and the hidden-to-tray low-cost clock with the no-catch-up rule.
5. **Persistence** — versioned atomic saves, backup/quarantine recovery, one save
   slot, safe-pose resume, no catch-up across close/sleep.

Exit criteria (ROADMAP): the mood/trust, suspend/hidden timing, and save-failure
suites pass, and the buddy visibly differentiates fearful, wary, neutral, content,
and delighted behavior without a mood meter.

Out of scope: shop/prices/purchasable tools (M5 — Meal, Drink, Repair Kit are M5
catalogue items; M4 builds the consume/cooldown *machinery* against the existing
laboratory food item), Steam adapter and the platform operation queue (M6),
economy calibration against the 3–120-minute schedule (M5, needs the catalogue).

**Prime invariants, every task:**

1. Physics stays authoritative and untouched: no new `_PhysicsProcess`
   registrations (ARCHITECTURE §23), no change to accepted feel profiles, scenario
   expectations, or envelope bounds. Behavior work adds *intent producers*; only
   `ActiveDrive`-family components translate intent into bounded forces.
2. Zero managed allocation on the 120 Hz path; intents are `readonly record
   struct` payloads over plain delegates; no LINQ/closures/boxing on tick paths.
3. Exact durations count integer routed ticks; clock-driven rules (drift, passive
   income) consume monotonic elapsed seconds *handed in by the caller* — a
   closed/slept/discontinuity span is simply never handed in (no catch-up by
   construction, FR-012.4/FR-015.9).
4. Stable content IDs cross every domain seam as plain `string` (ARCHITECTURE §5).
   No `StringName`/`Rid`/`GodotObject` in domain records or saves.
5. Seeded randomness only: behavior decisions draw from the behavior stream, trait
   sampling from a dedicated save-creation stream, both isolated from
   presentation streams (ARCHITECTURE §23). Headless scenarios inject fixed seeds.
6. Every scenario/journey passes identically under `--presentation=mii3d` and
   `--presentation=legacy`.
7. Persistence writes follow ARCHITECTURE §12 exactly: single writer, off-thread
   serialize without Godot objects, temp file + `FileStream.Flush(true)` +
   `File.Replace` atomic swap with one rolling backup, `.corrupt-<timestamp>`
   quarantine, sequential N→N+1 migrations. Never serialize live pose, objects,
   pain, knockout, or statuses (FR-015.2).
8. Owner-accepted behavior is regression-locked: the learned Boxing Glove
   guard/flee, Work/Play routing, Eat five-bite sequence, grab-hang pendulum, and
   recovery timings must not change observably. The existing scenario suite is the
   contract; it stays green after every task.
9. Don't invent product behavior; pause for the owner per NFR-006.5.

## Where the code stands today (verified 2026-07-24)

Agents: trust this inventory over assumptions, and re-verify with a quick grep
before building on it.

**Exists and is owner-accepted (build on it, don't rewrite):**

- `domain/DesktopBuddy.Domain/Mood/MoodModel.cs` — mood clamp/bands/drift, harm
  formula `min(10, pain × 0.1)`, trust-reset crossing semantics. **Gap:** harmful
  memory is keyed by `int` tool id, violating the §5 string-ID rule (Task 0).
- `domain/.../Mood/CareModel.cs` — Pet/Tickle tuning, satisfaction/cadence
  results (wired into `src/Tools/CareStrokeComponent.cs`).
- `domain/.../Economy/PassiveIncome.cs` — mood multiplier anchors
  (0.25×/1.0×/2.0×), milli-credit accrual with fractional carry. **Not yet wired
  to any runtime clock or currency owner.**
- `domain/.../Economy/RewardLedger.cs`, `Damage/PainKnockoutModel.cs`,
  `Interaction/ImpactRouter.cs` — damage→pain→payout pipeline, runtime-owned by
  `src/Interaction/InteractionDamageComponent.cs`.
- `domain/.../Autonomy/AutonomousMotionPlanner.cs` +
  `src/Buddy/Behavior/AutonomousMotionComponent.cs` — ambient walk with the
  wall-block rule; ambient *timer* jump gated OFF in shipped `.tres`.
- `src/Buddy/Behavior/ToolReactionComponent.cs` — learned-threat flee + hand
  guard (owner-accepted 2026-07-24). This *is* priority-6-shaped behavior; the
  arbiter must subsume it without observable change.
- `src/Buddy/Behavior/BehaviorActivityComponent.cs` — fixed-tick Eat activity
  (five authoritative bites, hand targets, item socket), triggered today by the
  laboratory `E` key with a throwaway food item.
- `src/Buddy/Behavior/GrabResistanceComponent.cs`, recovery, grab tether — done.
- `src/UI/MoneyHudPresenter.cs` — HUD; reads damage rewards only.

**Missing entirely (this milestone creates it):**

- `BehaviorArbiter` (the §4 priority ladder exists only as a document).
- Object interaction: candidate sensing (`InteractionSense` layer 6 is named in
  §20 but unused), catch/hold/inspect/toss/discard lifecycle.
- Consume-as-care pipeline: consume success → mood gain → cooldown start
  (FR-008.10); today Eat is presentation + hand choreography with no care effect.
- Per-save traits (jump propensity) and any notion of "a save".
- Persistence: no `src/Persistence/`, no DTOs, no store, no coordinator; grep for
  `IProgressStore` matches only docs.
- Clocks/lifecycle: no `GameClock`, no hidden-to-tray low-cost mode, no
  discontinuity handling; `PassiveIncome`/mood drift never accrue at runtime.
- `EconomyService` as the single currency/unlock owner; today
  `InteractionDamageComponent` privately constructs `RewardLedger` and
  `MoodModel` (`InteractionDamageComponent.cs:144-145`) — state dies with the node.

## Architecture

### New and changed seams

| Worker | Home | Responsibility |
| --- | --- | --- |
| `BehaviorArbiterModel` | `domain/.../Autonomy/` | Pure §4 ladder: resolve priorities 0–7 from an immutable snapshot into one actuation intent + one object intent per tick; hysteresis/commitment so goals cannot flip-flop at 120 Hz (§23). |
| `ObjectInteractionModel` | `domain/.../Autonomy/` | Candidate scoring (distance, safety, memory, mood band) and the catch→hold→inspect→consume/toss/discard lifecycle state machine with abort rules. |
| `BuddyTraits` | `domain/.../Autonomy/` | Per-save sampled traits (jump propensity now; the seam for later ones). Sampled once from the save-creation RNG stream; persisted; regenerated only on new game. |
| `BuddyProgressState` | `domain/.../Persistence/` | The single runtime owner of persistent semantic state: mood model, harmful memory, currency, unlocks, selected tool, traits, statistics counters, cumulative time. Constructed from a loaded save; snapshotted into a save DTO. |
| `ProgressSaveV1` / `LocalSettingsSaveV1` + `SaveMigrations` | `domain/.../Persistence/` | Versioned DTOs (FR-015.1/15.2 field lists), validation, unknown-ID extension bucket, sequential migrations. Pure, `dotnet test`-covered. |
| `BehaviorArbiter` | `src/Buddy/Behavior/` | Thin node: builds the snapshot at §7 step 3, calls the domain model, routes intents to `ActiveDriveComponent` / `BehaviorActivityComponent` / `ObjectInteractionComponent`. Applies no forces. |
| `ObjectInteractionComponent` | `src/Buddy/Behavior/` | `Area2D` sensing on layer 6 (scans 3), candidate reporting, and driving committed object actions through bounded drive/limb-target requests. |
| `EconomyService` | `src/Economy/` | Sole currency/unlock mutator (§11): consumes `RewardEvent`s and passive accrual, exposes read-only balance snapshots to the HUD. |
| `JsonProgressStore : IProgressStore` | `src/Persistence/` | §12 atomic write/load/quarantine against `user://`; plus an in-memory store for scenarios. |
| `SaveCoordinator` | `src/App/` | Single writer: main-thread snapshot, off-thread serialize, 30 s dirty coalesce (FR-015.6), immediate flush on purchase/unlock/focus-loss/clean-exit (FR-015.7). |
| `GameClock` + `LifecycleCoordinator` | `src/App/` | Monotonic elapsed-span source with discontinuity exclusion (§8); hidden-to-tray low-cost mode (§24) feeding mood drift + passive income only. |

### Fixed-tick and clock flow

The §7 order gains real step 3 content: `SandboxRoot`'s single gameplay tick calls
`BuddyRoot`, which now routes *snapshot → arbiter → drives* where today it routes
straight into autonomy/drives. `AutonomousMotionComponent`'s decision-making moves
behind the arbiter as the priority-7 producer (the planner class is reused as its
engine); `ToolReactionComponent`'s flee/guard becomes the priority-6/3 producer.
Neither may change observable behavior — the existing tool-feel, wall-block, and
recovery scenarios are the regression oracle.

Two clocks, never mixed (§8): routed ticks for durations (cooldowns, knockout,
activity phases); `GameClock` monotonic spans for mood drift and passive income.
`LifecycleCoordinator` hands spans to `MoodModel.Drift` and `PassiveIncome.Accrue`
— foreground at low cadence (once per second is ample), hidden mode at ~10 Hz
via `ProcessMode.Always` (§24). Close/suspend/discontinuity spans are discarded,
which is the entire no-catch-up implementation; session lock counts as running
time (FR-016.8).

### Save policy for tests

Scenarios and the laboratory never write `user://`: the composition root injects
the in-memory store unless the runner receives an explicit save-fixture argument.
Journeys use committed fixtures plus the multi-phase relaunch mechanism
(`AGENT_VERIFICATION_AND_E2E.md` §3) for save → relaunch → resume coverage.

## Tasks

### Task 0 — String-ID alignment and state lift (prep; no owner decision needed)

Convert `MoodModel` harmful memory from `int` to `string` tool ids (§5) and update
its clients (`InteractionDamageComponent`, `ToolReactionComponent`, tests).
Introduce `BuddyProgressState` as the constructor-injected owner of `MoodModel` +
`RewardLedger`, replacing the private `new` in
`InteractionDamageComponent._Ready`; composition roots build one per run. Pure
refactor: zero behavior change, full suite green. This unblocks persistence and
is deliberately small.

**Done when:** `dotnet test` green; full scenario/journey suite green both modes;
grep shows no `int toolId` on mood/memory seams.

### Task 1 — Domain: arbiter, object lifecycle, traits (pure C#)

`BehaviorArbiterModel` implementing the §4 ladder over an immutable
`BehaviorSnapshot` (consciousness, recovery, hazard/burning flags, grab state,
mood band, memory queries, candidate list, support/wall state). Emits
`ActuationIntent` + `ObjectIntent` record structs. Commitment rule: a selected
goal persists a profile tick count unless preempted by a higher priority.

`ObjectInteractionModel`: candidate scoring and lifecycle
(`Approach → Catch → Hold → Inspect → {Consume | Toss | Discard | Drop}`) with
abort-on-higher-priority, hazard-memory gating (FR-005.6, FR-010.4/10.5 shape),
and completed-catch detection for the +1 mood rule (FR-008.3).

`BuddyTraits.Sample(IRandomSource)` for jump propensity within the owner-approved
range; `CareConsumableModel` for consume-grants-mood-and-starts-cooldown with
FR-008.10 semantics (cancel/miss/drop never starts cooldown).

**Done when:** xUnit suites cover ladder preemption at every adjacent priority
pair, commitment/hysteresis, lifecycle aborts, memory gating, trait sampling
determinism per seed, and cooldown-start rules. `dotnet test` green.

### Task 2 — Object interaction integration: sense, catch, hold, consume, toss

`ObjectInteractionComponent` with an `InteractionSense` `Area2D` (layer 6 scans 3;
startup validation asserts the layer wiring). Candidates come from
`LooseObjectRegistry`-tracked bodies. Committed actions drive through existing
seams: catch/hold reuse the Eat-style shared hand-target machinery in
`ActiveDriveComponent`; consume routes into `BehaviorActivityComponent`'s Eat
activity and, on the fifth authoritative bite, fires the care event into
`BuddyProgressState` (mood gain + cooldown start — the consume pipeline becomes
real care). Toss is a bounded capped impulse through the drive, never a direct
velocity write; discard is a low-energy release plus a flee bias flag for the
arbiter.

New scenarios: `object_catch_hold` (seeded throw → catch → hold; +1 mood exactly
once per throw), `object_toss_discard` (mood-banded toss vs. hazard-memory
discard), `consume_care_cooldown` (consume grants mood once, cooldown counts
routed ticks, cancelled consume starts no cooldown).

**Done when:** new scenarios green on seeds 1 and 7 both modes; Eat regression
(`activity_clips`, owner-feedback checks) green; no allocation regression.

### Task 3 — Arbiter integration, mood-band personality, jump trait

Insert `BehaviorArbiter` into the `BuddyRoot` routed order; migrate
`AutonomousMotionComponent` decisions to priority 7 and `ToolReactionComponent`
threat response to priorities 3/6 with regression lock. Add the emotional/social
layer (priority 6): band-differentiated approach/keep-distance/flee toward the
cursor and objects per the owner-resolved vocabulary (decision 1 below), using
tool memory (FR-005.6) and transient reactions.

Jump personality: jumps may now be requested only by the arbiter when the trait
propensity and situation evidence agree (obstacle in the committed walk path per
decision 2); the pure ambient timer stays OFF.

New scenarios: `behavior_priority_ladder` (script each preemption: knockout cuts
object action; hazard cuts social; grab constraint coexists), `mood_band_behavior`
(pin mood per band via test seam → assert distance/approach/flee envelopes
differ per band), `jump_trait_gate` (zero-propensity seed never ambient-jumps;
high-propensity seed hops only at obstacle evidence; reload keeps the trait).

**Done when:** new + full regression suite green both modes; tool-feel and
wall-block scenarios unchanged; arbiter tick cost inside the existing physics
step budget (telemetry check, no new allocation).

### Task 4 — Persistence: DTOs, store, coordinator, resume

Domain: `ProgressSaveV1` exactly per FR-015.1 (money milli-credits, unlocks,
selected tool, mood, harmful/per-tool memory, traits, statistics, cumulative
run/active/hidden time, schema version + monotonic revision, extension bucket),
`LocalSettingsSaveV1` per §12, validation, and the migration scaffold with a
pinch test proving unknown-version rejection.

Game: `JsonProgressStore` (§12 write path exactly — temp, durable flush,
`File.Replace`, one backup, quarantine-then-fallback load order),
`SaveCoordinator` (dirty tracking, 30 s coalesce, event flushes), and boot
integration: load → construct `BuddyProgressState` → spawn safe standing buddy
(FR-015.3); new save seeds `BuddyTraits` and FR-013.1 defaults (money 0, Grab
selected, starter tools available).

Unit suites: the entire TEST_PLAN §2 Persistence list except the two Steam rows
(M6): round-trip, migration, no-live-state-in-save, write-failure backup
preservation (fault-injecting store double), corrupt quarantine, settings/progress
separation.

Journey: `care_persistence` — phase 1 plays (consume care, take damage, earn),
asserts semantic state; phase 2 relaunches, asserts safe standing pose and intact
money/mood/memory/trait/selected tool (the §7 journey-map row).

**Done when:** unit suites + journey green; scenario runs provably write nothing
to `user://`; full regression green.

### Task 5 — Clocks, passive income, hidden-to-tray

`GameClock` (monotonic spans, discontinuity exclusion threshold per §8),
`LifecycleCoordinator` (`ProcessMode.Always`) accruing mood drift + passive income
through `BuddyProgressState`/`EconomyService`; `EconomyService` becomes the sole
currency mutator with `MoneyHudPresenter` consuming its snapshot. Hidden-to-tray
implements §24 mechanics behind `IDesktopWindowService` so the emulated adapter
can drive it headless; suspend/resume clears the physics accumulator
(FR-015.10). Session-lock semantics land in the adapter seam; native
verification joins the M2 owner-manual Windows matrix rather than blocking this
milestone.

Unit: drift/no-drift spans, multiplier anchors and interpolation, no-catch-up
across simulated close/sleep/discontinuity, hidden-time-accrues vs.
closed-time-does-not (TEST_PLAN §2 mood/trust/passive rows not already covered).
Scenarios: `hidden_clock_accrual` (enter hidden mode headless → mood drifts and
income accrues at low cadence, physics frozen, no burst on show),
`suspend_no_catchup` (inject discontinuity → zero income/drift, no physics burst).

**Done when:** suites green; hidden-mode CPU sanity via existing telemetry
(<0.5% target is owner-manual on real hardware, recorded not gated here).

### Task 6 — Composition, regression, docs, owner gate

Full-suite pass (unit, 31+ scenarios, journeys, quick_validate) both presentation
modes; allocation check on an active scene with arbiter + persistence live;
update `TEST_PLAN.md` (new scenarios/journeys join §2/§3), `CHECKLIST.md`,
`DECISIONS.md` (resolved decisions + any delegated defaults chosen), and this
plan's Progress section. Prepare the owner gate script: hands-on session
demonstrating the five bands visibly differentiated without a mood meter, care →
passive-income effect, save/relaunch resume, and hidden-to-tray behavior.

**Done when:** everything green; owner acceptance recorded in `DECISIONS.md`.

## New test surface (summary)

| Layer | Additions |
| --- | --- |
| Unit (`dotnet test`) | Arbiter ladder/commitment; object lifecycle; traits; care consumable cooldowns; mood/trust/passive rows of TEST_PLAN §2; save round-trip/migration/quarantine/atomicity; clock no-catch-up. |
| Scenarios | `object_catch_hold`, `object_toss_discard`, `consume_care_cooldown`, `behavior_priority_ladder`, `mood_band_behavior`, `jump_trait_gate`, `hidden_clock_accrual`, `suspend_no_catchup` — all seeded, both presentation modes, registered in `ScenarioCatalog`. |
| Journeys | `care_persistence` (multi-phase relaunch). |
| Owner-manual | Five-band differentiation review; hidden-mode CPU on reference hardware; native session-lock check (joins M2 matrix). |

## Owner decisions required before Tasks 1+

**Status 2026-07-24: ALL SIX decisions RESOLVED into `docs/DECISIONS.md`
("Milestone 4 pre-plan — owner decisions resolved"). Every M4 task is unblocked;
implementation may begin at Task 0.** Decision 5 was delegated: engineering picks
a provisional base passive rate (~1 credit/minute at neutral mood order), marked
provisional, replaced during M5 calibration.

Original list, kept for context; proposals were starting points, not decisions.

1. **Band-visible behavior vocabulary.** Proposal: fearful — max cursor distance,
   flees approach, guards; wary — keeps moderate distance, never approaches, no
   catches of thrown objects; neutral — current ambient behavior; content —
   occasional cursor approach, catches willingly, occasional wave; delighted —
   eager approach, eager catch, frequent wave/glances. Exact distances/cadences
   delegated as tuning.
2. **Jump personality shape.** Proposal: propensity sampled uniformly in an
   approved range mapping to obstacle-hop eagerness only; pure-timer ambient
   jumps remain OFF. Confirm this matches the 2026-07-20 "too random" intent.
3. **Approach target semantics.** Does the friendly buddy approach the cursor,
   thrown/idle objects, or both (priority)? Reference v1.01 behavior.
4. **M4 consumable scope.** Confirm: consume/cooldown machinery ships against the
   laboratory food item; Meal/Drink/Repair Kit arrive as M5 catalogue entries on
   this machinery. The §7 journey-map "meal consumption" row is satisfied by the
   M4 food item, re-verified in M5 with the real Meal.
5. **Provisional base passive rate.** A placeholder credits/second is needed
   before M5 calibration; propose shipping it clearly marked provisional in a
   `MoodEconomyProfile` resource.
6. **Laboratory save policy.** Confirm labs/scenarios stay saveless (in-memory
   store) and only the sandbox/standalone boot touches `user://`.

## Progress

Authoritative per-task status. Update after each task lands with its suite run.

- [ ] Task 0 — String-ID alignment and state lift
- [ ] Task 1 — Domain arbiter/object/traits models
- [ ] Task 2 — Object interaction integration
- [ ] Task 3 — Arbiter integration + personality + jump trait
- [ ] Task 4 — Persistence stack + resume journey
- [ ] Task 5 — Clocks, passive income, hidden mode
- [ ] Task 6 — Composition, regression, docs, owner gate
