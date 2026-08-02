# M5 Tasks 11–13 — Strength, Economy, and Milestone Closeout Handoff

**Status: PLAN — written 2026-08-01, not yet implemented.** This document refines
Tasks 11–13 of `docs/M5_SHOP_AND_TOOL_CATALOGUE_PLAN.md` to implementation-handoff
fidelity. It does not authorize unresolved product choices. A smaller implementation
agent should be able to follow the ordered work packets below without rediscovering the
architecture or inventing behavior.

Authoritative contracts, in conflict order: `docs/DECISIONS.md`,
`docs/PRODUCT_REQUIREMENTS.md` (FR-006.2/.4/.6/.8, FR-011.8–10/.15,
FR-012, FR-013, FR-014, FR-015.7, FR-019, NFR-002, NFR-006),
`docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` (§6–§9), `docs/ARCHITECTURE.md`,
`docs/TEST_PLAN.md` (§4 and §7), `docs/ROADMAP.md`, and
`docs/AGENT_VERIFICATION_AND_E2E.md` (§2–§7). If those documents still conflict at
implementation time, stop and ask the owner; this plan never breaks the tie.

---

## 1. Dependency graph and completion language

Work in this order:

1. Resolve the blocking owner/source-of-truth questions in §2.
2. Finish and owner-accept Task 10 (Repair Kit).
3. Implement Task 11, then pass its automated, real-input, and owner-feel gates.
4. Freeze the complete accepted catalogue and implement Task 12 against that exact set.
5. Finish the dock catalogue binding/reset work, then perform Task 13.

Task 12 may build its engine-free measuring instrument while Task 11 is being tuned, but
it may not publish final prices or a green verdict before the Strength slot and catalogue
membership are confirmed. Task 13 is blocked until every catalogue slice has
`Visible = true`, the dock is bound to the real catalogue, and Task 12 is accepted.

Use these terms literally in commits and handoffs:

- **implemented:** code and focused automated tests exist;
- **engineering-complete:** focused tests, scenario, and real-input journey pass;
- **shop-visible:** engineering-complete plus owner feel acceptance recorded in
  `DECISIONS.md`, then and only then `Visible = true`;
- **milestone-closed:** Task 13's full automated, Windows, performance, docs, and owner
  gates all pass. An agent cannot self-certify owner or reference-hardware gates.

No task may hide an unrun gate behind “not available.” Record it as `BLOCKED`, name the
required owner/machine, and leave the task open.

## 2. Mandatory preflight — resolve before behavior or calibration

### 2.1 Catalogue count conflict — blocks Tasks 12 and 13

The repository currently disagrees with itself:

- `DECISIONS.md` records the Nerf Blaster owner gate as accepted and on sale;
- `CataloguePolicy.LaunchContentIds` and `data/catalogue/launch_catalogue.tres` contain
  fifteen selectable interactions **plus** `upgrade.strength` (`16` entries total);
- current FR-013.2 says `15` entries total and omits the Nerf Blaster;
- several older M5 plan sentences alternate between “15 entries” and “fifteen
  interactions plus the upgrade.”

Do not delete the accepted Nerf, change the requirement, or calibrate one interpretation
silently. Ask the owner which launch catalogue is authoritative. Record the answer in
`DECISIONS.md`, then amend FR-013.2/.5, `CataloguePolicy` comments/tests,
`launch_catalogue.tres`, ROADMAP, and M5 wording in one bookkeeping commit so they all
name the same exact ordered set and count. Task 12 takes that resulting ordered
`ToolCatalogue` snapshot as input; it never owns a second hard-coded catalogue list.

### 2.2 Strength decision packet — blocks Task 11 behavior

Ask the owner for one complete answer containing every row below. Put the resolved values
in `DECISIONS.md` before changing grab behavior; keep the unanswered packet in
`OPEN_QUESTIONS.md` meanwhile.

| Decision | Required answer | Implementation consequence |
| --- | --- | --- |
| Product name | final player-facing name and translation-key wording | catalogue strings; do not leave “Strength Upgrade” as guessed launch copy |
| Tiers | exactly one purchase, or a specified tier model | FR-019 and persistence/catalogue schema; current code supports one owned ID only |
| Force factor | finite factor for `Stiffness` | effective tether input |
| Force-ceiling factor | finite factor for `MaximumForce` | effective tether input |
| Stretch factor | finite factor for `StretchLimitHandWidths` | effective limiter tuning |
| Release factor | finite factor applied before capping | release calculation |
| Upgraded safe cap | finite px/s maximum distinct from the base `ThrowSpeedCap` | final release cap |
| Strain/snap rule | absolute immunity, or exact longer-window rule | `GrabStretchLimiter` policy; no hybrid guess |
| Fear response | confirm existing resistance remains generated/visible unchanged | scenario observation, never a resistance bypass |
| Activation boundary | effect begins on next grab or may change a grab already in progress | how the runtime source is sampled |
| Progression slot | exact position among the confirmed purchasable order | Resource order and simulation targets |
| Price handling | fixed owner price, or permission for Task 12 to derive/tune it | `upgrade_strength.tres` and economy acceptance |

If tiers are not exactly one, stop and revise FR-019, the catalogue/persistence design,
this Task 11 packet, and Task 12 before implementation. Do not stretch the single boolean
ownership seam into an implicit tier system.

### 2.3 Economy benchmark decision packet — blocks Task 12 acceptance

The owner must approve the representative mixed-session benchmark, not merely its final
numbers. Record:

- the exact ordered active actions and idle/care intervals represented by one session;
- the distribution or fixed counts used by each seed, including body regions,
  consciousness, misses, legitimate repeat episodes, and duplicate callbacks;
- whether purchases happen immediately when affordable and whether post-purchase balance
  carries forward (recommended model: yes/yes, but owner must confirm);
- the seed set and median rule;
- tolerance around each cumulative target minute, the `~120 min` full-catalogue target,
  and the `~25%` peak-passive ratio;
- what qualifies as one “ordinary event” for the no-multi-skip obligation;
- the Strength Upgrade target minute after §2.2 is resolved.

Store the approved trace definition as readable test data/code next to the simulation;
do not generate a trace from the prices being tuned. Prices are outputs, never inputs to
the player-behavior generator.

### 2.4 Task 13 external gates — identify operators before starting

Record who will perform the real-Windows owner playthrough and who has access to the
i5-8400/UHD 630-class reference machine. The implementation agent prepares commands,
artifacts, and result templates. Only observed runs may populate measurements. Headless CI
cannot be relabeled as the NFR-002 reference-hardware result.

### 2.5 Baseline capture

Before Task 11 code, run and record in this document's implementation progress entry:

```text
dotnet test
dotnet build DesktopBuddy.sln -c Debug
tools\quick_validate.bat
```

Also record: commit SHA, domain test count, quick-suite count, full scenario IDs, full
journey IDs, and every existing red/skip with its documented reason. Do not absorb a
baseline movement silently.

---

## 3. Task 11 — Strength Upgrade (FR-019)

### 3.1 Existing seams — use these, do not create substitutes

- Ownership already persists under stable content ID `upgrade.strength` in
  `BuddyProgressState`; it is a `PassiveUpgrade`, not a `ToolId`.
- `CataloguePolicy.SelectableEntries` already excludes passive upgrades and the purchase
  boundary already performs atomic spend/unlock/save flushing.
- `GrabTether.Evaluate` owns stiffness/damping/maximum-force math;
  `GrabTether.CapReleaseVelocity` owns the direction-preserving release cap.
- `GrabStretchLimiter` owns stretch clamp, strain buzz, ease-off hysteresis, and snap;
  `GrabTetherController` is the Godot adapter that samples Resources and applies forces.
- Base authored values live in `GrabTetherProfile`; preserve them as the unowned baseline.
- Fear resistance is produced outside the tether solver. The upgrade must not short-circuit
  that producer or change its visible reaction.

Do not add a second Grab controller, duplicate the base profile, put the upgrade in
`ToolId`, add a damage multiplier, or branch on a player-facing string.

### 3.2 Target design

Add an immutable, engine-free resolved snapshot, provisionally named
`GrabStrengthSettings`, containing only the confirmed mechanical quantities and the
confirmed strain mode. Add `IGrabStrengthSource` with one allocation-free read method or
property that returns that snapshot.

Implement exactly two source states for the current one-tier contract:

1. **identity/unowned:** effective values equal the existing `GrabTetherProfile` values;
2. **owned:** factors and upgraded cap come from one validated typed Resource and ownership
   comes from `BuddyProgressState`/`ContentIds.UpgradeStrength`.

Resolve all effective inputs in one place. The controller consumes the resolved stiffness,
maximum force, stretch limit, release scale/cap, and strain mode; no downstream class asks
the progress state again. Damping, buzz presentation, hysteresis, and snap impulse values
remain base values unless the owner packet explicitly changes them.

Suggested file ownership (names may change only if an existing convention requires it):

- `domain/DesktopBuddy.Domain/Physics/GrabStrengthSettings.cs` — immutable snapshot,
  strain-mode enum, validation, identity composition;
- `src/Grab/IGrabStrengthSource.cs` — read contract;
- `src/Grab/ProgressGrabStrengthSource.cs` — progress ownership + Resource adapter;
- `src/Grab/GrabStrengthUpgradeProfile.cs` and
  `data/buddy/grab_strength_upgrade.tres` — confirmed factors/cap/mode;
- `src/Grab/GrabTetherController.cs` — consume one resolved snapshot;
- `tests/DesktopBuddy.Domain.Tests/Physics/GrabStrengthSettingsTests.cs` plus focused
  additions to `GrabTetherTests`/`GrabStretchLimiterTests`;
- `src/Testing/StrengthUpgradeScenario.cs`, `tests/journeys/m5_strength_upgrade.json`
  or the repository's current code-backed journey registration equivalent.

The runtime sampling boundary must match the §2.2 owner answer. Whichever boundary is
chosen, it must be deterministic, documented, and tested. Never reconstruct Resources or
allocate on the 120 Hz path.

### 3.3 Implementation packets — land in order

**Task 11A — Decision and data shell.** Resolve §2.2; amend FR-019 if the owner changes
its snap/tier wording; add the typed profile/snapshot/source contract and validation. Keep
`upgrade_strength.tres` at `Visible = false` and price `0` until its product and economy
decisions are real.

Accept:

- non-finite, zero/negative where invalid, sub-identity authority factors (unless explicitly
  approved), and a safe cap inconsistent with the confirmed rule are rejected at startup;
- identity composition returns the exact existing floats, not approximately equivalent
  re-authored values;
- the Resource owns every confirmed tuning number; domain code contains no launch
  magnitudes.

**Task 11B — Tether authority.** Inject the source into `GrabTetherController` through the
composition root. Feed effective stiffness and maximum force into the existing
`GrabTether.Evaluate`; do not fork its solver.

Accept (pure tests first):

- a golden table of representative anchor error/relative velocity inputs compares old
  direct `GrabTether.Evaluate` results with identity-source results using exact equality;
- owned results apply both confirmed force quantities and remain bounded by the upgraded
  ceiling;
- damping and force direction are unchanged;
- source reads allocate zero bytes in a warmed loop.

**Task 11C — Stretch and strain/snap policy.** Parameterize the existing limiter with the
resolved stretch limit and confirmed strain mode. Preserve the complete unowned state
machine and all existing tests byte-for-byte in outcome. Absolute immunity means no
`Snapped` state or forced release for any held duration; a longer-window decision means the
exact confirmed duration and eventual outcome. In either case keep the bounded clamp and
visible strain response specified by the owner.

Accept:

- unowned `Slack → Straining → Snapped`, hysteresis cancellation, peak overpull, and snap
  impulse tests remain green;
- owned limit equals the confirmed factor times the authored base limit;
- an owned hold beyond the old 360-tick boundary follows the confirmed outcome;
- hard reposition, release, and re-grab reset both modes;
- loose objects and torso remain exempt from limb stretch exactly as today.

**Task 11D — Stronger release.** On intentional release, scale the sampled velocity by the
confirmed factor, then call the existing direction-preserving cap with the upgraded safe
maximum. Cancellation, hard reposition, and snap-driven release must not be converted into
an intentional upgraded yank. Keep `Released(... countsAsThrow)` semantics intact.

Accept:

- below-cap velocity scales in the same direction;
- above-cap velocity lands exactly at the upgraded safe maximum;
- zero velocity stays zero and finite;
- unowned release is exactly today's result;
- no money, pain, mood, statistics, or harmful-memory mutation occurs merely from owning
  or activating the modifier. Any later collision earns only through the shared impact
  pipeline.

**Task 11E — Scenario and real-input journey.** Register `strength_upgrade` and
`m5_strength_upgrade`.

The scenario must create two otherwise identical runs:

1. unowned: prove current FR-006.8 stretch/snap behavior and baseline release cap;
2. owned: prove the exact confirmed force authority, larger measured reach, release yank
   and cap, and strain/snap outcome.

Both runs must also prove fear resistance stays non-zero and visibly routed, the upgrade
never appears in selectable entries, selecting `upgrade.strength` is rejected, ownership
survives save round-trip, and no direct reward/mood/stat change comes from the modifier.
Use quantitative telemetry with tolerances, not “looked stronger.”

The journey starts from a declared fresh-save fixture, exercises the real purchase seam at
the authored price (or uses the established exact-price saveless buyer before the dock is
available), relaunches to prove ownership, then drives Grab through real pointer input. It
covers a normal stronger yank and the applicable strain/snap secondary/error path. No fixed
sleeps.

Accept: scenario seeds `1`, `7`, and `13`; journey seeds `1` and `7`; both presentations;
`--fixed-fps 120`; existing `grab_release`, `grab_resistance`, `grab_dangle`,
`grab_hard_recovery`, and `payout_by_region` remain green.

**Task 11F — Owner feel gate and promotion.** Owner compares unowned/owned on real Windows
and explicitly accepts the product name, control authority, reach, yank, cap, and strain
response. Record the decision and measured values. Only then assign the Task 12-approved
price/slot and set `Visible = true`; if Task 12 has not finalized the price, Task 11 may be
engineering-complete but cannot be shop-visible.

### 3.4 Task 11 definition of done

All 11A–F accepts pass; the identity path is regression-proven; there is one Resource-backed
modifier seam; FR-019.5/.6 negative proofs exist; the upgrade is purchase-only and never
selectable; real-input evidence is promoted; the owner gate is recorded. If any owner row
is unresolved, report Task 11 as blocked, not partially done.

---

## 4. Task 12 — Economy simulation and calibration

### 4.1 Ownership and non-goals

`EconomySimulation` is an engine-free measuring instrument, not a new economy. It must call
the real domain rules:

```text
raw ContactSample
  -> ImpactRouter (real 0.15 s source/body dedup)
  -> PainCurve (real authored anchors supplied as an immutable snapshot)
  -> RewardLedger (real region/consciousness/cash-per-pain formula)
  -> sequential Purchase/TrySpend against the real ToolCatalogue snapshot

elapsed running interval + mood trace
  -> PassiveIncome (real piecewise mood multiplier and fractional carry)
  -> the same balance
```

Do not add per-tool payout coefficients, random catch-up income, price-dependent player
behavior, a second purchase rule, or a closed-form spreadsheet that bypasses these types.
The simulation may report results but may not mutate runtime saves.

### 4.2 Exact new surfaces

Suggested domain files:

- `domain/DesktopBuddy.Domain/Economy/EconomyBenchmark.cs` — immutable trace/session
  records, approved targets/tolerances supplied by caller;
- `domain/DesktopBuddy.Domain/Economy/EconomySimulation.cs` — replay and result;
- `domain/DesktopBuddy.Domain/Economy/EconomySimulationResult.cs` — per-seed active,
  passive, purchase-time, award, and proof-obligation metrics;
- `tests/DesktopBuddy.Domain.Tests/Economy/EconomySimulationTests.cs` — model/unit and
  deterministic golden tests.

Suggested Godot test adapter:

- `src/Testing/EconomyCalibrationScenario.cs` loads
  `data/catalogue/launch_catalogue.tres`, `data/buddy/lab_pain_conversion.tres`, and
  `data/buddy/m4_mood_economy.tres`, converts them once to immutable inputs, runs the
  simulation, fails on the accepted tolerances, and writes one JSON plus one Markdown
  report under `--artifacts`;
- register stable scenario ID `economy_calibration` in `ScenarioCatalog` and TEST_PLAN;
- optionally add `tools/run_economy_calibration.bat` as a thin command wrapper only. The
  scenario remains the authority.

Do not duplicate final catalogue prices in the unit-test project. Pure unit tests use
small synthetic catalogues to prove replay rules; the Godot scenario is the integration
gate that consumes the actual `.tres` prices and coefficients.

### 4.3 Trace and result contract

Each active trace event contains at least: routed tick/time, source interaction ID,
content ID, target part, impulse, relative velocity, and consciousness at acceptance.
Duplicate callbacks appear as separate raw events with the same source/body episode key so
the real router suppresses them. A legitimate later reuse either has the approved inactivity
gap or a new interaction ID. Each passive interval contains duration and mood. Seed variation
changes only the owner-approved player/session inputs.

For each seed, replay the ordered purchasable catalogue. When the approved policy says an
item is bought, subtract its real Resource price immediately and carry the remaining balance.
Record cumulative affordability/purchase time for every entry, including Strength at its
confirmed slot. Report at least:

- active, passive, and total earned milli-credits;
- accepted/suppressed contacts and per-event awards;
- purchase time, target, signed error, and pass/fail per entry;
- maximum single ordinary-event award and maximum milestones skipped;
- active credits/minute, maximum-mood passive credits/minute, and their ratio;
- final purchase time and remaining balance;
- per-metric median across the approved seed set.

All arithmetic that reaches balances/prices remains integer milli-credits. Report formatting
may use decimals; acceptance never depends on locale-formatted strings or binary-float display.

### 4.4 Implementation packets — instrument first, values last

**Task 12A — Preflight reconciliation.** Resolve §2.1 and §2.3. Freeze the exact ordered
catalogue, target table, seed set, tolerances, purchase policy, and ordinary-event definition
in DECISIONS/requirements. Inventory every current `PriceCredits`, `CashPerPain`,
`NeutralCreditsPerMinute`, Baseball physical preset, and catch ceiling as a baseline report.

**Task 12B — Pure replay engine.** Implement trace replay through the real domain types and
sequential spend. Unit tests use tiny constructed inputs and prove deterministic repeat,
integer carry, purchase carry-forward, an unaffordable item staying pending, and stable median
calculation for odd/even seed counts per the owner-approved convention.

**Task 12C — Four proof obligations as executable metrics.** Implement TEST_PLAN §4 exactly:

1. **Active dominance:** compare separately accumulated active and passive income over the
   same approved session window; active must exceed passive and satisfy the approved band.
2. **Peak passive ≈25%:** calculate max-mood passive rate from the real
   `NeutralCreditsPerMinute` and `PassiveIncome.MoodMultiplier(+100)`, divided by benchmark
   active rate; compare to the approved tolerance around `0.25`.
3. **No ordinary multi-skip:** after each sequential purchase state, apply each approved
   ordinary event and count newly affordable consecutive milestones; the maximum must be
   the owner-approved limit (normally one, but do not infer).
4. **Reuse pays, duplicates do not:** feed a first callback, a duplicate inside the real
   contact episode, and a legitimate reuse after the approved re-arm/new-interaction
   boundary. Assert awards `positive, zero, positive` through `ImpactRouter` and the ledger,
   not with a simulation-only boolean.

**Task 12D — Real-Resource integration report.** Add `economy_calibration`. It validates and
loads actual Resources, prints the full per-seed/median table, and fails non-zero when any
target, proof obligation, catalogue membership/order, or two-hour result is outside the
approved band. Two identical invocations at the same commit/seed set must produce byte-identical
JSON after excluding explicitly non-deterministic metadata such as wall-clock generation time;
prefer omitting such metadata entirely.

**Task 12E — Calibration pass.** Change values only in their existing typed Resources:

- `PainConversionProfile.CashPerPain` in `data/buddy/lab_pain_conversion.tres`;
- `MoodEconomyProfile.NeutralCreditsPerMinute` in
  `data/buddy/m4_mood_economy.tres`;
- each non-starting catalogue `PriceCredits` and confirmed `ProgressionOrder`;
- Baseball preset/catch ceiling only through their existing typed profile Resources.

Do not tune the impulse-to-pain anchors merely to make prices fit; they remain gameplay feel
unless a separate owner-approved physics recalibration is opened. Iterate by committing the
generated before/after report with the final decision entry, not generated transient noise.
Accepted slices retain their current visibility; calibration never promotes an unaccepted
slice.

**Task 12F — Regression and live pacing gate.** Run unit/build, quick suite, targeted
`economy_calibration` twice, then the complete scenario and journey catalogues on seeds `1`
and `7` in both presentations at 120 Hz. Owner plays the approved mixed session on real
Windows and accepts pacing. Record final coefficients, prices, target errors, ratio, full
catalogue time, commit SHA, and artifacts in `DECISIONS.md`/TEST_PLAN. Remove “provisional”
wording only from values actually accepted.

### 4.5 Task 12 definition of done

The actual Resource catalogue and coefficients pass every approved target using the approved
median/tolerances; TEST_PLAN §4's four obligations are directly exercised; the output is
deterministic and reviewable; all prices are authoritative whole credits; owner pacing is
accepted; no tool-specific money multiplier exists; Task 12 did not change visibility.

---

## 5. Task 13 — Composition, regression, docs, and M5 exit

### 5.1 Hard prerequisites

Stop before implementation if any row is false:

- Tasks 0–12 are engineering-complete;
- every launch entry passed its owner feel gate and every non-starting entry is visible;
- the §2.1 catalogue count/order conflict is resolved everywhere;
- dock plan Tasks 1–6 and the real catalogue binding are complete;
- reset erase/preserve matrix and confirmation UI are owner-confirmed and complete;
- `economy_calibration` passes final Resource data;
- every M5 slice already has a registered real-input journey.

Task 13 composes and closes; it does not finish missing tool behavior under a generic
“integration fix” commit. Send a missing slice back to its task and rerun its gate.

### 5.2 Preflight inventory (machine-readable, no hand-maintained subset)

Add one test/helper that derives the required inventory from authoritative registries:

- launch entries from the validated `ToolCatalogue`;
- scenarios from `ScenarioCatalog.Ids`;
- journeys from the Journey runner's registry/files;
- visible shop/selectable rows from `CataloguePolicy`.

Assert exact set relationships:

- every non-starting catalogue entry is visible and offered in the shop;
- every selectable entry has one M5 behavior journey; `upgrade.strength` is shop-only and
  has its upgrade journey;
- no invisible or unknown entry appears in dock tools/shop;
- each expected M5 scenario/journey ID exists exactly once;
- the performance and economy scenarios are registered but are not mistaken for per-tool
  journeys.

Generate the full sweep from these registries. Do not paste a curated list into a script that
will miss the next registered scenario (the M3.6 regression failure this gate exists to prevent).

### 5.3 `m5_shop_progression` — exact journey contract

Add a multi-phase journey through the **real dock UI and purchase boundary**:

1. Phase A starts from the committed fresh-save fixture: `0` balance, Grab selected, exact
   starting ownership, no unknown IDs.
2. Earn enough for the first purchase through real gameplay input (Boxing Glove is available)
   and/or approved time acceleration of the real passive path. No direct balance mutation in
   journey steps.
3. Open the dock through real input, verify the ordered visible shop, purchase the first
   item, verify exact authored charge, `OWNED`, immediate availability, and an immediate-save
   flush signal.
4. Continue through the exact owner-confirmed purchasable order. Long pacing is already
   proven by Task 12, so the journey may use a committed **progress fixture between phases**
   containing legitimately earned balance if runtime would otherwise be excessive; that
   fixture must pass normal save loading and may set only persistent preconditions. It may
   not call `Deposit`, `Unlock`, or `Purchase` behind the UI during an assertion phase.
5. Attempt one insufficient-funds purchase and verify feedback plus zero mutation. Attempt an
   already-owned purchase and verify zero double charge. Confirm Strength never enters tool
   selection.
6. Relaunch at least after the first, middle, and final purchases. Each phase verifies balance,
   ownership, selection validity, and catalogue order survived through the real save path.
7. End with every confirmed purchasable entry owned, final balance correct, all selectable
   tools usable, Strength effect owned, no unknown IDs, and no unfinished entry visible.

Use `wait_signal`/`wait_predicate` with explicit timeouts. No fixed sleeps, direct component
calls, or lab-wide “grant every tool” bootstrap in this journey. Keep per-tool behavior in the
existing `m5_<tool>` journeys; progression proves shop/persistence composition and does not
retest every mechanic.

### 5.4 Full regression runner

Add or extend a thin script to:

1. discover every scenario/journey ID from the application's list command or committed
   registry export;
2. run every ID with seeds `1` and `7`, presentations `mii3d` and `legacy`, and
   `--fixed-fps 120`;
3. give each run a unique artifacts directory and preserve first-failure logs;
4. continue after failures to produce a complete matrix, then exit non-zero if any cell is
   red or unexpectedly skipped;
5. write JSON/Markdown summary containing commit SHA, command, ID, seed, presentation,
   duration, verdict, and artifact path.

Window-only tests remain separate and explicitly named; they are not silently skipped by a
headless green summary. `tools\quick_validate.bat` remains the fast suite, not the M5 exit
suite.

### 5.5 Performance gate — two layers, never conflated

**Layer A: deterministic in-game stress/allocation scenario.** Add stable ID
`m5_performance` (or extend an existing explicit performance scenario) that:

- warms all relevant code/pools before measuring;
- holds exactly `24` registered loose objects, including protected/unsafe cases;
- drives representative peak pistol/shotgun projectile pools, grenade/fire-spray particles,
  status/presentation components, and both presentation modes;
- proves bullets/pellets/VFX do not alter the loose-object count;
- samples the **whole routed gameplay tick**, not only the existing object-registry and
  arbiter sub-probes, using allocation deltas outside the measured body;
- asserts zero managed bytes after warm-up and bounded live/pooled counts and lifetimes;
- records tick-time distribution and GC collection counts as diagnostic evidence without
  pretending headless timing equals the reference-hardware FPS gate.

Do not leave allocation instrumentation enabled in release behavior. Avoid telemetry/log
formatting inside the measured tick.

**Layer B: real reference-hardware benchmark.** Provide one documented Windows command and
result template for `480x360` on i5-8400/UHD 630-class hardware. Measure:

- fixed 120 Hz physics and at least 60 rendered FPS;
- total process CPU target `<5%` and resident memory `<300 MB` in the representative active
  scene;
- hidden-to-tray CPU target `<0.5%`;
- four-hour visible and eight-hour hidden soak growth;
- GC collections/frame-time spikes and bounded pool/object/save-queue counts.

Record OS, CPU/GPU, build configuration, commit SHA, sample/warm-up durations, monitor/tool
used, raw logs, and pass/fail. If reference hardware is unavailable, Layer A may be green but
Task 13 stays externally blocked.

### 5.6 Documentation and clean-room audit

Update in the same closeout wave:

- `docs/ARCHITECTURE.md`: actual catalogue/purchase, Strength source, gun/projectile,
  Burning/status, object-budget, simulation, full-sweep, and allocation ownership;
- `docs/TEST_PLAN.md`: final per-tool scenario/journey map, `economy_calibration`,
  `m5_shop_progression`, full-sweep command, performance/soak procedure and observed result;
- `docs/DECISIONS.md`: every owner packet answer, final coefficients/prices, feel gates, and
  external acceptance results;
- `docs/PRODUCT_REQUIREMENTS.md`, `docs/ROADMAP.md`, and
  `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md`: reconcile accepted supersessions and exact catalogue
  count/order; remove stale provisional claims only where decided;
- `CHECKLIST.md`: rewrite against observed current state; no unchecked item may be described
  as done;
- this master M5 plan: progress entries with final counts/commands and M5 status.

Search the shipped project for copied reference branding/assets and for debug-only unfinished
content exposure. The lab may retain its documented dev-only routes; exported shop/tool UI
may not. Verify release export filters exclude automation/MCP artifacts as already required.

### 5.7 Owner exit matrix

Task 13 closes only when one evidence row exists for each:

| Gate | Required evidence | Who may accept |
| --- | --- | --- |
| Full automated matrix | generated all-ID × seed × presentation report, zero unexpected red/skip | implementation agent/CI |
| Shop progression | `m5_shop_progression` artifacts across relaunch | implementation agent/CI |
| Per-tool coverage | inventory proof: behavior/error scenario + real-input journey per entry | implementation agent/CI |
| Economy | Task 12 final report and live pacing decision | owner |
| Performance Layer A | warmed allocation/pool scenario artifacts | implementation agent/CI |
| Performance Layer B | reference-hardware active/hidden/soak report | owner or named hardware operator |
| Windows feel | every interaction plus Strength and dock exercised on real Windows | owner |
| Clean-room presentation | explicit audit with no copied expressive content | owner |
| Documentation | source-of-truth consistency check and final checklist | implementation agent + owner for decisions |

### 5.8 Task 13 definition of done

The real dock sells the exact approved catalogue from a fresh save through final ownership;
all ownership persists; the generated full matrix is green in both presentations; every M5
entry has behavior/error and real-input coverage; deterministic and hardware performance
gates pass; docs describe the implemented system without stale conflicts; the owner records
Windows feel, pacing, and clean-room acceptance. Otherwise M5 remains open with the exact
blocking row named.

---

## 6. Required validation commands

Every packet uses the repository-standard commands:

```text
dotnet test
dotnet build DesktopBuddy.sln -c Debug
tools\quick_validate.bat
<godot> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<n> --presentation=<mode> --artifacts=<dir>
<godot> --headless --fixed-fps 120 --path . -- --journey=<id> --seed=<n> --presentation=<mode> --artifacts=<dir>
```

Use `mii3d` and `legacy`; Task 11 targeted seeds are stated in §3.3; Tasks 12/13 use the
approved simulation seeds plus full catalogue seeds `1` and `7`. Close any Godot editor
before headless runs. Revert the known MCP blank-line change to `project.godot` before any
commit. Never claim a command was run without preserving its verdict.

## 7. Handoff response template

Each implementing agent ends with this exact information:

```text
Task/subtask:
Commit SHA:
Status: implemented | engineering-complete | shop-visible | BLOCKED
Changed files:
Requirements/decisions used:
Automated commands and exact counts:
Interactive real-input behavior driven:
Artifacts:
Owner gate still required:
Known reds/skips:
Next unblocked packet:
```

This format is part of the handoff contract: it prevents an implementation-only result from
being mistaken for an accepted product slice.
