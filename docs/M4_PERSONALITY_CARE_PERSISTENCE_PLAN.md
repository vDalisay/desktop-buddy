# Milestone 4 — Personality, Care, and Persistence

Status: **COMPLETE — OWNER ACCEPTED 2026-07-27**
(see `docs/DECISIONS.md`, "Milestone 4 Owner Gate — Accepted").
Post-acceptance audit hardening landed 2026-07-29 without changing the accepted
personality or feel contract.

Plan revision: merged V1 + V2 review pass, 2026-07-25. Task bodies are the
condensed V2 form; traceability, invariants, code inventory, scope boundaries,
and the progress ledger are retained from V1. `docs/M4_PERSONALITY_CARE_PERSISTENCE_PLAN_V2.md`
is superseded by this file and may be deleted.

M4 adds persistent personality, care, object behavior, passive economy, and
lifecycle-safe save/resume. Existing M3/M3.6 physics feel, tool reactions, Eat
choreography, grab behavior, recovery, Work/Play routing, and presentation parity
are regression contracts.

## Authoritative sources

This plan composes existing product decisions; it invents no product behavior.

- `docs/ROADMAP.md` — Milestone 4 deliverables and exit criteria.
- `docs/PRODUCT_REQUIREMENTS.md` — FR-005.3–FR-005.6 (autonomy/object behavior),
  FR-007 (mood/trust/memory), FR-008 (care), FR-012 (passive income),
  FR-013.1 (starting state), FR-015 (save/load/resume), FR-016.2/16.3/16.8
  (hidden/lifecycle clock).
- `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` §4 / §4.1 — behavior-arbitration priority
  ladder; object memory and the mood-60 trust reset.
- `docs/ARCHITECTURE.md` §5 (stable string IDs, `IProgressStore`), §7 (fixed-tick
  order), §8 (time and lifecycle), §11 (mood/economy boundaries), §12 (save
  architecture), §23 (physics integration rules), §24 (hidden-to-tray mechanics).
- `docs/TEST_PLAN.md` §2 — "Mood, Trust, and Passive Income" and "Persistence"
  unit suites this milestone turns green.
- `docs/AGENT_VERIFICATION_AND_E2E.md` §3 (multi-phase journeys), §7 row 4
  (care/persistence journey including save → relaunch → safe resume).
- `docs/DECISIONS.md` — ambient timer jumping OFF (2026-07-20); deferred jump
  personality (2026-07-14); learned-threat guard/flee behavior (2026-07-24);
  M4 pre-plan owner decisions (2026-07-24).

## Scope

Five work streams from `ROADMAP.md` Milestone 4:

1. **Autonomy** — approach/flee/catch/hold/consume/toss behind the RAGDOLL §4
   ladder (real `BehaviorArbiter` + object-interaction pipeline; today only
   ambient walk and tool-threat reactions exist).
2. **Memory and trust** — persistent per-tool harmful history and the mood-60
   trust reset (domain rules exist in `MoodModel`; persistence and string-ID
   alignment do not).
3. **Traits** — per-save obstacle-hop propensity, sampled only at new-save
   creation, combined with situation evidence. Pure-timer ambient jumping stays
   OFF (DECISIONS 2026-07-20).
4. **Economy clock** — passive income, mood drift, care gains/cooldowns, and the
   hidden-to-tray low-cost clock with the no-catch-up rule.
5. **Persistence** — versioned atomic saves, backup/quarantine recovery, one save
   slot, safe-pose resume, no catch-up across close/sleep.

**Exit criteria (ROADMAP):** the mood/trust, suspend/hidden timing, and
save-failure suites pass, and the buddy visibly differentiates fearful, wary,
neutral, content, and delighted behavior **without a mood meter**.

**Out of scope — do not build these here:**

- Shop, prices, purchasable tools (M5). Meal, Drink, and Repair Kit are M5
  catalogue items; M4 builds the consume/cooldown *machinery* against the
  existing laboratory food item (owner decision 4).
- Steam adapter and the platform operation queue (M6).
- Economy calibration against the FR-012.3 / FR-013 3–120-minute schedule (M5 —
  it needs the catalogue). M4 ships a provisional rate only.
- Full tray menu (M6). M4 ships Show/Hide + Save & Quit only.

## Prime invariants — every task

1. **Physics untouched.** No new `_PhysicsProcess` registrations (ARCH §23), no
   change to accepted feel profiles, scenario expectations, or envelope bounds.
   Behavior work adds *intent producers*; only `ActiveDrive`-family components
   translate intent into bounded forces. Never set transform or velocity directly.
2. **Zero managed allocation on the 120 Hz path.** Intents are `readonly record
   struct` payloads; no LINQ/closures/boxing on tick paths; spans and
   preallocated buffers instead of per-tick collections.
3. **Two clocks, never mixed (ARCH §8).** Exact durations count integer routed
   ticks (cooldowns, knockout, activity phases). Clock-driven rules (mood drift,
   passive income) consume monotonic elapsed seconds *handed in by the caller* —
   a closed/slept/discontinuity span is simply never handed in. That is the
   entire no-catch-up implementation (FR-012.4 / FR-015.9).
4. **Stable content IDs cross every domain seam as plain `string`** (ARCH §5). No
   `StringName`/`Rid`/`GodotObject` in domain records or saves.
5. **Seeded randomness only.** Behavior decisions draw from the behavior stream,
   trait sampling from a dedicated save-creation stream, both isolated from
   presentation streams. Headless scenarios inject fixed seeds.
6. **Presentation parity.** Every scenario/journey passes identically under
   `--presentation=mii3d` and `--presentation=legacy`.
7. **Persistence follows ARCH §12 exactly.** Single writer; off-thread serialize
   with no Godot objects; temp file + `FileStream.Flush(true)` + `File.Replace`
   atomic swap with one rolling backup; `.corrupt-<timestamp>` quarantine;
   sequential N→N+1 migrations. Never serialize live pose, loose objects,
   projectiles, pain window, knockout, or transient statuses (FR-015.2).
8. **Owner-accepted behavior is regression-locked.** Learned Boxing Glove
   guard/flee, Work/Play routing, Eat five-bite sequence, grab-hang pendulum, and
   recovery timings must not change observably. The existing scenario suite is
   the contract and stays green after every task.
9. **Do not invent product behavior.** Where this plan states a number not backed
   by a requirement, it is listed under "Delegated defaults" below and must be
   recorded in `DECISIONS.md` at Task 6. Anything outside that list that needs a
   product answer stops for the owner per NFR-006.5.

## Where the code stands today (verified 2026-07-25)

Agents: trust this inventory over assumptions; re-verify with a quick grep before
building on it.

**Exists and is owner-accepted — build on it, do not rewrite:**

| Seam | Note |
| --- | --- |
| `domain/.../Mood/MoodModel.cs` | mood clamp/bands/drift (`0.5` pts/min), harm formula `min(10, pain × 0.1)`, trust-reset crossing. **Gap:** harmful memory keyed by `int` (`MoodModel.cs:35-54`), violating ARCH §5 — Task 0. |
| `domain/.../Mood/CareModel.cs` | Pet/Tickle tuning, satisfaction/cadence, wired to `src/Tools/CareStrokeComponent.cs`. |
| `domain/.../Economy/PassiveIncome.cs` | mood multiplier anchors `0.25×/1.0×/2.0×`, milli-credit fractional carry. **Not wired to any runtime clock or currency owner.** |
| `domain/.../Economy/RewardLedger.cs`, `Damage/PainKnockoutModel.cs`, `Interaction/ImpactRouter.cs` | damage→pain→payout, runtime-owned by `src/Interaction/InteractionDamageComponent.cs`. |
| `domain/.../Autonomy/AutonomousMotionPlanner.cs`, `IRandomSource.cs`, `SeededRandomSource.cs` | ambient walk + wall-block rule; ambient *timer* jump gated OFF in shipped `.tres`. |
| `domain/.../Physics/RecoveryClock.cs` | the exact-routed-tick convention to copy for all new duration logic. |
| `domain/.../Tools/ToolSelection.cs` | existing `ToolId` enum — keep it for selection; Task 0 adds a total enum↔string mapping. |
| `src/Buddy/Behavior/ToolReactionComponent.cs` | learned-threat flee + hand guard (accepted 2026-07-24). Priority-3-shaped; the arbiter must subsume it with no observable change. |
| `src/Buddy/Behavior/BehaviorActivityComponent.cs` | fixed-tick Eat activity, five authoritative bites, `EatBiteCompleted(completed, total)` event; triggered today by laboratory `E` with a throwaway food item. |
| `src/Buddy/Behavior/GrabResistanceComponent.cs`, recovery, grab tether, `PuppetRig.ResetToSafePose` | done; `ResetToSafePose` is the resume seam. |
| `src/Objects/LooseObjectBody.cs` | exists; has no registry, profile, or throw-token concept. |
| `src/UI/MoneyHudPresenter.cs` | HUD; reads damage rewards only. |
| `project.godot` layer names | `layer_3="LooseObjects"`, `layer_6="InteractionSense"` already declared; layer 6 is unused. |

**Missing entirely — this milestone creates it:**

- `BehaviorArbiter` (the §4 ladder exists only as a document).
- Object interaction: candidate sensing on layer 6, catch/hold/inspect/toss/discard.
- Consume-as-care: consume success → mood gain → cooldown start (FR-008.10).
  Today Eat is presentation + hand choreography with no care effect.
- Per-save traits and any notion of "a save".
- Persistence: no `src/Persistence/`, no DTOs, no store, no coordinator. `grep
  IProgressStore` matches docs only.
- Clocks/lifecycle: no `GameClock`, no hidden-to-tray mode, no discontinuity
  handling. `PassiveIncome` and mood drift never accrue correctly at runtime —
  drift currently runs on the physics path at `InteractionDamageComponent.cs:231`.
- `EconomyService`: today `InteractionDamageComponent` privately constructs
  `RewardLedger` and `MoodModel` (`InteractionDamageComponent.cs:144-145`), so
  state dies with the node.
- **Multi-phase journeys.** `AGENT_VERIFICATION_AND_E2E.md` §3 specifies phased
  relaunch, but `src/Testing/JourneyRunner.cs` has no phase support. Task 4 adds it.

**Current counts (for the Task 6 stale-count fix):** 33 scenarios registered in
`src/Testing/ScenarioCatalog.cs`; 10 journeys in `tests/journeys/`. `CHECKLIST.md:58`
says 31 scenarios and `:59` says 10 journeys; `docs/M3_6_...PLAN.md:556` says 26
scenarios. M4 lands 41 scenarios and 11 journeys.

## Delegated defaults — record in `DECISIONS.md` at Task 6

These are engineering choices this plan makes because no requirement covers them.
They are provisional and must be listed explicitly, not buried in prose.

1. **Laboratory food tuning.** The M4 lab-food item ships with `+10` mood and a
   `7200`-routed-tick (`60 s`) cooldown, borrowed from FR-008.4 (Meal). Owner
   decision 4 confirmed the *machinery* target, not the tuning. M5 replaces this
   with the real catalogue Meal.
2. **Reuse cooldowns are not persisted.** FR-015.2 excludes transient statuses, so
   an in-flight care cooldown is lost on quit. Consequence to state plainly: a
   relaunch clears the `60 s` window. Acceptable in M4 (lab-only item); flag it
   for owner review at M5 when Meal becomes purchasable.
3. **Discontinuity threshold `5 s`.** ARCH §8 names the threshold, not a value.
4. **Provisional passive rate ~`1` credit/minute at neutral mood**, in a
   `MoodEconomyProfile` resource marked provisional (owner decision 5 delegates this).
5. **Trait propensity is a deterministic `0–100` bucket**, sampled uniformly, so
   the persisted value is exactly reproducible across save round-trips.
6. **Band distances and cadences** — delegated tuning per owner decision 1,
   judged at the M4 owner exit gate.

## Architecture — new and changed seams

| Worker | Home | Responsibility |
| --- | --- | --- |
| `BehaviorArbiterModel` | `domain/.../Autonomy/` | Pure §4 ladder: immutable `BehaviorSnapshot` → one `ActuationIntent` + one `ObjectIntent` per tick; commitment/hysteresis so goals cannot flip-flop at 120 Hz; immediate higher-priority preemption; diagnostics. |
| `ObjectInteractionModel` | `domain/.../Autonomy/` | Candidate scoring (distance, safety, memory, mood band) and the `Idle → Approach → Catch → Hold → Inspect → {Consume \| Toss \| Discard \| Drop}` machine with abort rules, harmful-memory gating, safe-catch reward-token dedup, cursor-safe toss direction. |
| `BuddyTraits` | `domain/.../Autonomy/` | Per-save traits (obstacle-hop propensity now; seam for later ones). Sampled once from the save-creation RNG stream; persisted; regenerated only on new save. |
| `CareConsumableModel` | `domain/.../Mood/` | Consume success → mood gain → cooldown start; miss/cancel/drop/interruption start no cooldown (FR-008.10). |
| `BuddyProgressState` | `domain/.../Persistence/` | Sole per-run owner of persistent semantic state: mood model, harmful memory, balance, unlocks, selected tool, traits, statistics, cumulative times, save revision. Command methods, immutable snapshots, low-frequency semantic events. |
| `ProgressSave` / `LocalSettingsSave` + `SaveMigrations` | `domain/.../Persistence/` | Versioned DTOs per FR-015.1/15.2 field lists, validation, unknown-ID extension bucket, sequential migrations. **Name the types as ARCH §5 declares them** (`IProgressStore` is typed over `ProgressSave`/`LocalSettingsSave`); the schema version is a field, not a type suffix. If a suffix is preferred, ARCH §5 must be updated in the same task. |
| `BehaviorArbiter` | `src/Buddy/Behavior/` | Thin node: builds the snapshot at §7 step 3, calls the domain model, routes intents to `ActiveDriveComponent` / `BehaviorActivityComponent` / `ObjectInteractionComponent`. Applies no forces. |
| `ObjectInteractionComponent` + `LooseObjectRegistry` | `src/Buddy/Behavior/`, `src/Objects/` | `Area2D` on layer 6 scanning layer 3; fixed-capacity registry tracking stable content ID, runtime ID, throw token, safety/consumable metadata, held/protected/rest state, deterministic eviction. |
| `EconomyService` | `src/Economy/` | Sole currency/unlock mutator (ARCH §11) over `BuddyProgressState`. Introduced in **Task 0** so no slice ships with a second mutator; passive-accrual wiring lands in Task 5. |
| `JsonProgressStore`, `InMemoryProgressStore` | `src/Persistence/` | ARCH §12 atomic write/load/quarantine against `user://`; in-memory store for labs/scenarios. |
| `SaveCoordinator` | `src/App/` | Single writer: main-thread snapshot, off-thread serialize, `30 s` dirty coalesce (FR-015.6), immediate flush on purchase/unlock/focus-loss/Save & Quit/clean exit (FR-015.7). Progress and settings files stay separate. |
| `GameClock` + `LifecycleCoordinator` | `src/App/` | Monotonic span source with discontinuity exclusion (§8); `ProcessMode.Always` hidden-to-tray low-cost mode (§24) feeding mood drift + passive income only. |

### Fixed-tick and clock flow

ARCH §7 step 3 gains real content: `SandboxRoot`'s single gameplay tick calls
`BuddyRoot`, which routes *snapshot → arbiter → selected producers →
`ActiveDriveComponent` → passive constraints* where today it routes straight into
autonomy/drives. `AutonomousMotionComponent` becomes the priority-7 producer (the
planner class is reused as its engine); `ToolReactionComponent`'s learned-threat
response becomes the priority-3 producer, its social/emotional shading priority 6.
Neither may change observable behavior — the existing tool-feel, wall-block, and
recovery scenarios are the regression oracle. Autonomy decision/RNG progression
pauses while suppressed.

`LifecycleCoordinator` is the **only** runtime caller of `MoodModel.Drift`,
`PassiveIncome.Accrue`, and cumulative-time counters: foreground at low cadence
(once per second is ample), hidden mode at ~10 Hz. No mood or economy work belongs
on the routed physics path. Session lock counts as running hidden time (FR-016.8).

### Save policy for tests

Scenarios and the laboratory never write `user://`: the composition root injects
the in-memory store unless the runner receives an explicit save-fixture argument.
The persistence journey uses an artifact-local fixture plus the Task 4 phased
relaunch mechanism.

## Tasks

### Task 0 — String-ID alignment, state lift, economy owner

- Add canonical stable IDs in Domain (`tool.grab`, `tool.pet`, `tool.tickle`,
  `tool.boxing_glove`, generic object/boundary IDs, `care.lab_food`) with a total
  enum↔string mapping.
- Convert content attribution and harmful-memory seams from `int` to ordinal
  strings: `domain/.../Mood/MoodModel.cs`, Domain interaction records/interfaces,
  `src/Interaction/InteractionDamageComponent.cs`, impact sources,
  `src/Buddy/Behavior/ToolReactionComponent.cs`,
  `src/Buddy/Presentation/BuddyReactionComponent.cs`, and every test/scenario
  caller — including `src/Testing/JourneyRunner.cs:835`,
  `M3PresentationScenario.cs:127`, and `ToolFeelReactionScenario.cs:237` (these
  are journey/scenario *verdict* paths; missing one silently changes a gate).
- Add `domain/.../Persistence/BuddyProgressState.cs` as the sole per-run owner of
  mood/history, reward balance, selected tool, starter unlocks, traits,
  statistics, cumulative times, and save revision. Expose command methods,
  immutable snapshots, and low-frequency semantic events.
- Add `EconomyService` as the sole currency/unlock mutator over that state, and
  route existing damage rewards through it. Passive accrual joins in Task 5.
- Refactor `InteractionDamageComponent.Initialize(...)` to receive the shared
  progress/economy state, replacing the private `new` at lines 144-145. Keep the
  impact router, pain, knockout, care cadence, and feedback workers transient.
  Preserve compatibility telemetry accessors while callers migrate.
- Compose one state per run in `SandboxRoot`/`BuddyLab`; labs and tests use fresh
  in-memory state.

Pure refactor: zero behavior change. Deliberately small — it unblocks persistence.

**Gate:** build; Domain tests; full current scenario/journey matrix in both
presentation modes; learned glove behavior, damage payout, Pet/Tickle, trust
reset, and selection unchanged; `grep` shows no integer content IDs on any
Domain or save seam.

### Task 1 — Domain: arbiter, object lifecycle, traits, consumable

Add Godot-free models under `domain/.../Autonomy/` (and `Mood/` for the
consumable) with matching xUnit suites:

- `BehaviorArbiterModel` — immutable `BehaviorSnapshot` (consciousness, recovery,
  hazard/burning flags, grab state, mood band, memory queries, candidate list,
  support/wall state), priority 0–7 selection, `ActuationIntent` + `ObjectIntent`,
  commitment/hysteresis, immediate higher-priority preemption, diagnostics.
- `ObjectInteractionModel` — fixed semantic lifecycle Idle → Approach → Catch →
  Hold → Inspect → Consume/Toss/Discard/Drop, abort rules, harmful-memory gating
  (FR-005.6, FR-010.4/10.5 shape), safe-catch reward-token dedup for the FR-008.3
  `+1`, cursor-safe toss direction policy.
- `BuddyTraits.Sample(IRandomSource)` — obstacle-hop propensity as a deterministic
  `0–100` bucket, sampled once from the save-creation RNG stream, persisted.
- `CareConsumableModel` — lab-food consume success, `+10` mood, `7200` routed-tick
  cooldown, no cooldown on miss/cancel/drop/interruption (FR-008.10).

Reuse `AutonomousMotionPlanner`, `IRandomSource`/`SeededRandomSource`, `CareModel`,
`MoodModel`, `PassiveIncome`, and the exact-tick conventions from `RecoveryClock`.
Spans and preallocated buffers, not per-tick collections.

**Gate:** xUnit coverage for every adjacent priority pair, commitment
expiry/invalidation, object transitions and aborts, harmful-memory gating,
once-per-throw catch care, trait determinism per seed, zero/high-propensity
traits, and consume cooldown semantics. `dotnet test` green.

### Task 2 — Object interaction integration: sense, catch, hold, consume, toss

- Add fixed-capacity `LooseObjectRegistry` and typed `LooseObjectProfile`. Extend
  `LooseObjectBody` **without** adding `_PhysicsProcess`. Protect held objects
  from eviction.
- Add `ObjectInteractionComponent` plus profile: `Area2D` layer 6 scanning layer
  3; maintain candidates from enter/exit signals in fixed buffers; extend startup
  validation to assert exact layer/mask wiring.
- Extend `DriveIntent`/runtime object command and `ActiveDriveComponent` for
  bounded two-hand catch/hold forces and one-shot toss/discard impulses. Add and
  remove buddy collision exceptions while held. Discard is a low-energy release
  plus a flee-bias flag for the arbiter.
- Reuse the existing Eat choreography. Only an active lab-food consume token may
  turn the fifth authoritative `EatBiteCompleted` into care success. On success:
  apply mood once, start the cooldown once, consume/unregister the object, clear
  hold state, then let the existing final hand-lowering finish. Accepted impacts
  and cancelled Eat grant nothing and start no cooldown.
- Keep laboratory `E` visually compatible while backing it with real lab-food
  semantics.
- Register scenarios `object_catch_hold`, `object_toss_discard`,
  `consume_care_cooldown`.

**Gate:** new scenarios on seeds 1 and 7 under both presentations;
`activity_clips`, owner visual checks, grab, `impact_dedup`, and autonomy
regressions green; zero active-tick allocation.

### Task 3 — Arbiter integration, mood-band personality, jump trait

- Add thin `src/Buddy/Behavior/BehaviorArbiter.cs` and typed profile; wire into
  `scenes/buddy/puppet.tscn` and `BuddyRoot`.
- Remove `BuddyRoot.BuildDriveIntent()`. `BuddyRoot` stays a router:
  standing/recovery/activity → snapshot → arbiter → selected producers →
  `ActiveDriveComponent` → passive constraints.
- Keep existing workers as producers: recovery/safety 0 and 2; unconscious 1;
  learned glove hazard 3; supported fearful grab resistance 4; committed
  object/Eat 5; social/tool emotion 6; existing autonomy planner 7. Pause
  autonomy decision/RNG progression while suppressed. Preserve accepted glove
  guard/flee, tickle reactions, wall stop, dangling grab, and recovery behavior.
- Add typed five-band distance/cadence tuning resources per owner decision 1:
  fearful — maximum distance, flee/guard, no voluntary catch; wary — moderate
  standoff, no approach or catch; neutral — current ambient baseline; content —
  occasional cursor/object approach, willing catches, occasional wave; delighted —
  eager approach/catch, frequent wave/glance. Hysteresis on every distance
  envelope. Presentation RNG stays separate.
- Gate hop requests on persisted propensity + committed path + obstacle evidence +
  stable support + no higher priority. Timer-driven ambient jumping stays disabled.
- Add scenarios `behavior_priority_ladder`, `mood_band_behavior`, `jump_trait_gate`.
  `jump_trait_gate` covers zero-propensity never hopping and high-propensity
  hopping only on obstacle evidence. **Trait-reload assertion belongs to Task 4**
  (`care_persistence`), since persistence does not exist yet at this task.

**Gate:** all priority preemptions, band envelopes, obstacle-only jump, existing
tool/autonomy/wall/grab/recovery scenarios, arbiter tick cost inside the current
physics step budget via telemetry, and the allocation check.

### Task 4 — Persistence: DTOs, store, coordinator, resume, phased journeys

- Add pure contracts/DTOs under `domain/.../Persistence/`: `ProgressSave`,
  `LocalSettingsSave`, immutable progress snapshot/statistics, `IProgressStore`
  per ARCH §5, load results, validation, migrations, unknown-ID extension bucket.
- Persist only semantic state (FR-015.1): schema version + monotonic revision,
  milli-credits, unlock IDs, selected string tool ID, mood, harmful IDs, traits,
  statistics, cumulative run/active/hidden time, extension data. Exclude
  transforms, velocities, loose objects, pain/knockout, status/activity/grab
  state, cooldowns, and all Godot/native objects (FR-015.2).
- Add one real sequential legacy migration for the pre-Task-0 integer tool/harm
  IDs. Reject unsupported future schemas without quarantining or overwriting them.
  Unknown selected tools fall back to Grab while retaining the unknown data.
- Add `JsonProgressStore` and `InMemoryProgressStore` under `src/Persistence/`.
  Resolve `user://` on the main thread, serialize pure data off-thread, write temp,
  `Flush(true)`, atomically replace primary with one rolling backup, quarantine a
  malformed primary before backup/default fallback, and preserve dirty state after
  a failed write.
- Add `SaveCoordinator`: one serialized writer, dirty-generation tracking, `30`
  valid-running-second coalescing (FR-015.6), immediate flush on purchase/unlock,
  focus loss, Save & Quit, and clean exit (FR-015.7). Progress and settings files
  stay separate (FR-015.8). State explicitly whether hidden-mode accrual marks
  state dirty — it does, so tray sessions autosave on the same `30 s` cadence.
- Refactor `Bootstrap` to load/migrate/validate before sandbox composition, sample
  traits only for a new save, seed FR-013.1 defaults (money 0, Grab selected,
  starter tools available), inject runtime context, then spawn the buddy in an
  ordinary safe standing pose (FR-015.3). Add a session-resume reset seam using
  `RecoveryComponent`/`PuppetRig.ResetToSafePose` that clears all transient
  simulation state without counting a hard recovery.
- Store policy: normal sandbox uses JSON `user://`; laboratory, scenarios, and
  journeys use the in-memory store; the persistence journey uses an explicit
  artifact-local fixture (owner decision 6).
- **Add phased journeys to `JourneyRunner`** — runner arguments plus process
  phases/relaunch per `AGENT_VERIFICATION_AND_E2E.md` §3, retaining single-phase
  compatibility for the existing 10 journeys. Add `tests/journeys/care_persistence.json`:
  phase 1 consumes care, takes damage, earns, and saves; phase 2 launches a fresh
  process and verifies safe standing pose plus restored balance, mood, memory,
  selected tool, and **trait**, with transient state absent.

**Gate:** round-trip, migration, validation, unknown-version, unknown-ID,
no-live-state, revision, atomic-failure (fault-injecting store double),
quarantine/backup/default, settings separation, and concurrent/coalesced save
tests; `care_persistence` green in both modes; proof that labs and scenarios do
not touch real `user://`; full regression green.

### Task 5 — Game clock, economy accrual, hidden lifecycle

- Add a pure monotonic-span filter plus runtime `GameClock`; inject a production
  `Stopwatch` source and a manual test source. Reject nonpositive spans, the first
  span after resume, and spans above the typed discontinuity threshold (`5 s`).
- Route `PassiveIncome` through `EconomyService`. Add a `MoodEconomyProfile`
  resource with the clearly provisional neutral rate of `1` credit/minute.
- Remove `_mood.Drift(_fixedDelta)` from `InteractionDamageComponent.PhysicsTick`
  (`:231`) in the same slice that `LifecycleCoordinator` starts driving drift —
  never leave both live. `LifecycleCoordinator` becomes the sole runtime caller
  for mood drift, passive income, and cumulative running counters.
- Update `MoneyHudPresenter` to subscribe to balance changes so passive deposits
  refresh the HUD; retain damage-only `+$` feedback behavior.
- Add `LifecycleCoordinator` with `ProcessMode.Always` and low-cadence
  foreground/hidden clock handoff.
- Extend the desktop service/adapter seams for hide/show, suspend/resume,
  discontinuity, and session lock. The emulated adapter exposes deterministic
  stimuli so this is drivable headless; the native Windows adapter handles power
  and session notifications, joining the M2 owner-manual Windows matrix rather
  than blocking this milestone.
- Hidden mode (ARCH §24): apply the final foreground span, hide the window, pause
  the tree, disable the render loop, throttle `Engine.MaxFps` near 10, keep
  lifecycle/save services active, accrue only clock-driven mood/income/time.
  Show/resume reverses the settings, resets interpolation for buddy and objects,
  clears the physics accumulator (FR-015.10), and never replays hidden or
  suspended physics. Session lock counts as running hidden time (FR-016.8).
- Implement the minimal M4 tray surface: Show/Hide and Save & Quit.
- Add scenarios `hidden_clock_accrual`, `suspend_no_catchup` using manual time.

**Gate:** exact drift/no-drift span tests, multiplier anchors and interpolation
with fractional carry, no-catch-up across simulated close/sleep/discontinuity,
hidden accrual with frozen physics and rendering, no burst on show, session-lock
accounting, HUD passive updates, and a recorded native manual-matrix entry
(hidden-mode CPU `<0.5%` is owner-manual on real hardware, recorded not gated here).

### Task 6 — Composition, regression, docs, owner gate

- Update scene references and typed profiles in `scenes/bootstrap.tscn`,
  `scenes/sandbox.tscn`, `scenes/buddy_lab.tscn`, `scenes/buddy/puppet.tscn`. Add
  no autoload and no second physics root.
- Register the eight M4 scenarios in `src/Testing/ScenarioCatalog.cs`: expected
  total becomes **41**. Add `care_persistence`: expected journey total becomes **11**.
- Expand `devtools/verification/quick_validate.bat` with focused M4 gates; keep full matrices and
  soaks outside the quick path.
- Run the allocation regression with arbiter, object sensor/registry, persistence,
  and lifecycle live after warm-up.
- Update `docs/TEST_PLAN.md` (§2/§3), `CHECKLIST.md` (fix the stale `31`
  scenarios at `:58` and `10` journeys at `:59`), `README.md`,
  `docs/ARCHITECTURE.md` (only if the persistence DTO names diverge from §5),
  `docs/DECISIONS.md`, and this plan's Progress section. Record every entry from
  "Delegated defaults" above, plus chosen stable IDs, band tuning, object
  durations, trait distribution, discontinuity threshold, provisional income rate,
  and the minimal tray scope.
- Prepare the owner gate script: five-band readability without a meter,
  care → economy effect, save/relaunch semantic retention with safe pose, hidden
  accrual with frozen ragdoll, and no show/resume burst.

**Gate:** everything green in both presentation modes; docs updated with no stale
counts; owner acceptance recorded in `DECISIONS.md` **only after the owner
performs the gate**. Report skipped manual owner acceptance as pending; do not
mark M4 accepted without owner action.

## New test surface (summary)

| Layer | Additions |
| --- | --- |
| Unit (`dotnet test`) | Arbiter ladder/commitment; object lifecycle; traits; care consumable cooldowns; TEST_PLAN §2 mood/trust/passive rows; save round-trip/migration/quarantine/atomicity; clock no-catch-up. |
| Scenarios (33 → 41) | `object_catch_hold`, `object_toss_discard`, `consume_care_cooldown`, `behavior_priority_ladder`, `mood_band_behavior`, `jump_trait_gate`, `hidden_clock_accrual`, `suspend_no_catchup` — all seeded, both presentation modes, registered in `ScenarioCatalog`. |
| Journeys (10 → 11) | `care_persistence`, the first multi-phase journey. |
| Owner-manual | Five-band differentiation review; hidden-mode CPU on reference hardware; native session-lock check (joins the M2 matrix). |

## Owner decisions

**All six resolved 2026-07-24** in `docs/DECISIONS.md`, "Milestone 4 pre-plan —
owner decisions resolved": band vocabulary accepted as proposed; jump personality
confirmed (obstacle-hop only, timer jumps OFF); approach targets both cursor and
objects with priority 5 outranking priority 6 naturally; consumable scope confirmed
against the lab food item; provisional passive rate delegated to engineering;
laboratories and scenarios stay saveless. No product clarification blocks work.

## Verification

Run incrementally after each task, then the full gate:

```bat
dotnet build DesktopBuddy.sln -c Debug
dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug
<godot> --headless --path . --import
```

For each new scenario, seeds 1 and 7, both presentations:

```bat
<godot> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<seed> --presentation=mii3d --artifacts=.artifacts\<id>-mii3d-s<seed>
<godot> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<seed> --presentation=legacy --artifacts=.artifacts\<id>-legacy-s<seed>
```

Run all journeys in both presentations, including cross-process `care_persistence`, then:

```bat
devtools\verification\quick_validate.bat
```

Finally use the project `verify` skill to drive real runtime flows, run windowed
owner-gate preparation, and record manual Windows hidden-CPU and session-lock
results.

## Progress

Authoritative per-task status. Update after each task lands with its suite run.

- [x] **Task 0 — String-ID alignment, state lift, economy owner** (2026-07-25).
      `ContentIds` (total `ToolId`↔string mapping), `MoodModel` harmful history on
      `string`, `ContentId` added to `ContactSample`/`ImpactSample`, `IImpactSource.ContentId`
      → `string` (`ImpactContent` deleted), `BuddyProgressState` + `EconomyService`
      composed once per run in `SandboxRoot`/`BuddyLab` and injected into
      `InteractionDamageComponent.Initialize(progress, economy)`.
      Gates: build clean; `dotnet test` **453/453**; `quick_validate` **11/11**;
      32 scenarios × 2 presentations **60/64**; 10 journeys × 2 presentations **19/20**;
      `grep` clean of integer content IDs. See "Known pre-existing red" below for the
      four non-passes — all reproduced on an unmodified baseline or invalid invocations.
- [x] **Task 1 — Domain arbiter/object/traits/consumable models** (2026-07-25).
      `BehaviorArbiterModel` (§4 ladder 0–7, commitment/hysteresis, immediate
      higher-priority preemption, `ArbiterDiagnostics`), `ObjectInteractionModel`
      (Idle→Approach→Catch→Hold→Inspect→Consume/Toss/Discard/Drop, abort rules,
      harmful gating, once-per-throw catch care), `BuddyTraits.Sample` (dedicated
      save-creation stream, 0–100 bucket), `CareConsumableModel` (+10 / 7200 ticks,
      no cooldown on cancel/miss/drop/interrupt), `SocialBandTuning` five-band vocabulary.
      Gate: `dotnet test` **540/540** (429 at M3.6 baseline).
- [x] **Task 2 — Object interaction integration** (2026-07-26).
      Added the fixed-capacity `LooseObjectRegistry`, typed loose-object and
      interaction profiles, exact layer-6→layer-3 sensing, bounded two-hand
      catch/hold drive, one-shot toss/discard commands, held collision exceptions,
      and real player-release throw attribution. Lab `E` now consumes a registered
      food object through the existing Eat choreography; only authoritative bite
      five applies care and starts the exact cooldown.
      Gates: build clean with zero warnings; `dotnet test` **571/571**;
      `quick_validate` **11/11**; `object_catch_hold`, `object_toss_discard`, and
      `consume_care_cooldown` **12/12** across seeds 1/7 and both presentations;
      targeted activity/grab/impact/autonomy regressions green. The catalog is now
      **36 scenarios** and **10 journeys**.
- [x] **Task 3 — Arbiter integration + personality + jump trait** (2026-07-26).
      Added the thin runtime `BehaviorArbiter`, removed drive-intent construction
      from `BuddyRoot`, and routed the complete §4 priority ladder through the
      existing focused producers into `ActiveDriveComponent`. One typed
      five-resource social vocabulary now drives both social intent and voluntary
      object catching with stateful hysteresis; suppressed ambient planning pauses
      its RNG stream. Two layer-3 ray probes provide real obstacle evidence, and
      hopping now requires the per-run persisted propensity, a committed walk,
      stable support, and no higher-priority owner while timer jumping remains off.
      **Corrected 2026-07-26 (review fixes):** those probes fired at torso height,
      which is clear above any floor-resting object, so the trait could not fire in
      real play. The probe height is now a profile value defaulting to `64 px` below
      the torso and both affected scenarios use real floor-resting objects. See
      `docs/M4_REVIEW_FIXES_PLAN.md` Task A.
      Gates: build clean with zero warnings; `dotnet test` **576/576**, including
      a 10,000-tick zero-allocation arbiter check; `quick_validate` **11/11**;
      `behavior_priority_ladder`, `mood_band_behavior`, and `jump_trait_gate`
      **12/12** across seeds 1/7 and both presentations; focused object, Eat,
      tool-feel, grab/dangle, wall, and autonomy regressions green. The catalog is
      now **39 scenarios** and **10 journeys**. Trait reload remains assigned to
      Task 4's `care_persistence` gate.
- [x] **Task 4 — Persistence stack + phased journeys + resume** (2026-07-26).
      Added versioned semantic progress/settings DTOs, the real integer-ID v1→v2
      migration, validation and forward-compatible unknown-ID retention, durable
      JSON temp/flush/atomic-replace with rolling backup and corrupt quarantine,
      the hermetic in-memory store, and a revision-based single save coordinator.
      Normal boot loads before sandbox composition, samples traits only for a new
      save, injects one run context, and resumes through a non-recovery safe-pose
      reset; labs and scenarios remain in-memory. Focus loss, unlock/purchase, clean
      close, and Save & Quit have immediate save seams, while later mutations remain
      dirty instead of starving a flush. Journey arguments and `JourneyRunner` now
      support ordered hard-timeout child-process phases with artifact-local fixtures.
      Gates: build clean with zero warnings; `dotnet test` **605/605**;
      `quick_validate` **11/11**; `care_persistence` passed fresh-process write→resume
      in both Mii3D and legacy presentations, restoring balance, mood, harmful
      memory, selection, and trait while proving safe pose and transient-state
      absence. The suite is now **39 scenarios** and **11 journeys**.
- [x] **Task 5 — Clocks, passive income, hidden mode** (2026-07-26).
      Added the pure monotonic span filter and injected `GameClock`; first,
      non-forward, suspended, and over-`5 s` discontinuity spans award nothing.
      `LifecycleCoordinator` is now the sole runtime owner of mood drift,
      mood-scaled passive income, cumulative run/active/hidden time, and its
      autosave cadence. Hidden-to-tray pauses the gameplay tree while the
      always-running low-frequency lifecycle path continues; show and
      resume reset the clock baseline so skipped physics is never replayed.
      **Corrected 2026-07-26 (review fixes):** as first landed this task paused only
      the tree, not rendering, shipped no caller for the tray commands, and left the
      suspend/resume/session-lock adapter seams unbuilt. Hidden mode now disables the
      render loop and caps frames at `10`, show re-anchors interpolation, the
      `Ctrl+Shift+H` / `Ctrl+Shift+Q` command surface exists (restore-from-hidden is
      an explicit M6 dependency), and §24 stimuli travel through
      `IWindowsDesktopAdapter`. See `docs/M4_REVIEW_FIXES_PLAN.md` Tasks B–D.
      Added the provisional typed `MoodEconomyProfile` at `1` neutral
      credit/minute, routed passive deposits through `EconomyService`, and made
      the HUD observe all balance changes. Removed physics-tick mood drift.
      Gates: build clean with zero warnings; `dotnet test` **611/611**;
      `quick_validate` **11/11**; `hidden_clock_accrual` and
      `suspend_no_catchup` **8/8** across seeds 1/7 and both presentations.
      The catalog is now **41 scenarios** and **11 journeys**.
- [x] **Task 6 — Composition, regression, docs, owner gate** (owner accepted
      2026-07-27).
      Scene composition, the 41-scenario/11-journey catalogs, the 15-step quick
      suite, delegated-default decisions, and the owner script are complete.
      The full valid headless matrix passed **80/80 scenario runs** (40 runnable
      scenarios in Mii3D and legacy; the window-only visual gate excluded) and
      **21/21 journey runs** (both presentations except the documented
      Mii3D-only presentation-toggle journey). Full `idle_soak` scenarios and
      full `lab_idle_soak` journeys passed in both presentations. Final gates:
      build clean with zero warnings; `dotnet test` **611/611**;
      `quick_validate` **15/15**. The warmed 240-tick live registry/object/arbiter
      allocation probe reports zero managed bytes; its first run exposed and
      removed an interface-enumerator allocation in wall sensing. A Godot 4.6
      Windows shutdown race was also
      hardened by draining pending managed interop finalizers before every
      application/test-runner quit; the two formerly affected Mii3D journeys
      now exit cleanly after passing. The owner subsequently performed
      `docs/M4_OWNER_GATE.md` and accepted Milestone 4 in full on 2026-07-27.

      **Pre-acceptance review, 2026-07-26.** An implementation/code review of all six
      tasks against this plan found eleven items, four of them plan deliverables that
      had not landed. They are tracked and closed in
      `docs/M4_REVIEW_FIXES_PLAN.md`; after that pass `dotnet test` is **638/638**,
      `quick_validate` **15/15**, the scenario matrix **78/78**, and journeys
      **21/21**.

      **Post-acceptance audit hardening, 2026-07-29.** The persistence journey now
      drives care, damage, selection, and transient state through real Buddy Lab
      keyboard/pointer input before its disk-backed relaunch. Fun persistence schema
      4 stores the boredom hysteresis latch exactly. Lifecycle mode/suspend/lock
      transitions settle the accepted pre-transition tail into the previous bucket;
      clean exit settles and stops lifecycle mutation before the forced final save,
      with a blocking dirty-save fallback during tree exit. Damage now updates the
      persisted lowest-mood statistic. The owner explicitly approved the personality
      system and these corrections.

### Known pre-existing red (found during the Task 0 gate, 2026-07-25)

The plan header's "no known red" claim was wrong. Recorded here so later tasks are not
blamed for it:

1. **RESOLVED 2026-07-26 — `grab_resistance` /
   `fearful_resists_more_than_calm`.** The assertion itself was not relaxed.
   The owner-approved resistance feel pass raised the force to `17000` and made
   resistance walk-driven; the full M4 matrix now produces
   `calm=13.4`, `fearful=29.8` and passes identically in both presentations.
2. **`owner_feedback_visual` cannot run headless.** It calls
   `tree.Root.GetTexture().GetImage()`, which NREs without a window. `TEST_PLAN.md:138`
   already describes it as the *windowed* screenshot scenario, so headless matrix runs
   must exclude it. Not a regression.
3. **`m35_presentation_toggle` must not be run with `--presentation=legacy`.** Its whole
   subject is the mode, and `TEST_PLAN.md:143` names it as the single documented
   exception to the both-modes rule. A legacy invocation is an invalid test, not a failure.
