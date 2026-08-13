# M5 Task 8 — Soccer Ball + Drink Plan

**Status: IMPLEMENTED 2026-07-31 through Task D; Task E (owner feel gate) outstanding.**
Both catalogue entries remain `Visible = false`. Measurements taken during implementation are
recorded inline below where the plan asked for them (§2.2) and collected in §6.

**Originally: PLAN — written 2026-07-31, not yet implemented.** Refines the master plan's
Task 8 stub to handoff fidelity. This is the mildest slice in M5 — **two data-driven
reuses** — and the plan's job is mostly to prove that claim and to name the one real
code addition (restitution authoring, §2.1). Authoritative contracts: RAGDOLL §9.2
Soccer Ball and Drink rows, §8.2 care table (+1 clean catch, +5 Drink), FR-008.4/.10,
FR-013.2, FR-014.

**Prerequisites:** none beyond current `main` + the grenade branch. Independent of
Tasks 7/9/10 — may run in parallel with any of them.

---

## 1. What exists today (do not rediscover)

- **Identity minted (M5 Task 0):** `tool.soccer_ball` (65 credits provisional, order
  11) and `tool.drink` (80 credits provisional, order 12, Kind = Care), both
  `Visible = false`. No enum or catalogue work.
- **`PullbackLauncherComponent`** — multi-launchable by construction: spawn key →
  content id → `LooseObjectProfile`. Baseball is key `5`, Meal `6`, Grenade `7`. Each
  launchable is a `.tres`, not code (this is the cursor-tool lesson repeated for
  launchables).
- **`LooseObjectProfile`** — already carries everything both items need
  (`Consumable`, `ConsumeMoodGain`, `ConsumeCooldownTicks`, `ConsumeHungerFill`,
  physical tuning, colors) **except restitution**: nothing authors bounce, and a
  soccer ball that doesn't bounce is a red baseball. §2.1 adds the field.
- **`CareConsumableModel`** — cooldown slots are **per content id** (preallocated
  array keyed by id). Meal/Drink cooldown independence is true by construction; the
  scenario's job is to *prove* it, not to build it.
- **Consume machinery** — `ObjectInteractionComponent` already routes any
  `Consumable` profile through appetite (`WouldEat(ConsumeHungerFill)`), the
  two-phase begin/complete token, refusal choreography, and `ApplyCareMood`. The
  code comment literally promises "the catalogue's Meal, Drink, and Repair Kit are
  the same machinery with their own profiles."
- **Clean-catch rule (superseded for Soccer Ball on 2026-08-01)** — the generic `+1`
  rule remains unchanged, but the Soccer Ball is now foot-only and never completes a catch.
- **Fun/novelty** — `FunInterestModel` meters fire on catches (`fun.catch`); a new
  ball is automatically novel. Nothing to add.
- **FR-014 budget** — both items are ordinary registry citizens: `SafeToEvict = true`,
  no protection states beyond the registry's own held/grabbed/committed rules.

## 2. Design

### 2.1 The one code addition: authored restitution

`LooseObjectProfile` gains `[Export] Bounce` (`0..1`, default `0.0` so **every
existing `.tres` is bit-identical in behavior**), validated finite and in range;
`LooseObjectBody` applies it through a `PhysicsMaterial` at profile-apply time.
Baseball/Meal/Grenade author nothing and stay at `0.0` — their measured scenario
signatures (`baseball_pullback`, `grenade_fuse` thud gating, meal flight) must not
move, and the accept gate runs them to prove it.

### 2.2 Soccer Ball — `data/objects/soccer_ball.tres`, spawn key `8`

| Field | Provisional | Why |
|---|---|---|
| `ContentId` | `tool.soccer_ball` | |
| `Radius` / `Mass` | 14 / 0.9 | visibly bigger than Baseball (9/1.0), slightly lighter |
| `Bounce` | 0.65 | the point of the ball |
| `LinearDamp` / `AngularDamp` | 0.3 / 0.8 | rolls long (Baseball 0.8/1.2) |
| `RestSpeedThreshold` / `RestTicksRequired` | 5.0 / 60 | unchanged defaults |
| Colors | white fill, black outline | classic placeholder; final art is M7 |

Its own `PullbackLauncherProfile` preset tuned so a full pullback flies a touch
slower and loopier than Baseball (empirical, lab-measured at Task B, recorded here).

**Implemented as `LooseObjectProfile.Launch`**, an optional per-launchable
`PullbackLauncherProfile` reference; `null` — every launchable authored before the Soccer
Ball — means the launcher's shared preset, so nothing that did not author one moved. The ball
authors `data/tools/pullback_launcher_soccer_ball.tres`: `VelocityPerPullPixel 11.5` and
`MaxLaunchSpeed 1400` against the shared `15.0`/`1800`. **Measured** full pullback:
`1035 px/s` (the Baseball's measured full pullback in `baseball_pullback` is `1575 px/s`).
The original catch/hold/toss/discard and +1 clean-catch behavior was superseded by the
2026-08-01 owner feedback in §8: the Soccer Ball is foot-only.

**Distinctness is measured, not asserted:** the scenario drops both balls from the
same authored height on the same seed and records bounce count to rest, peak rebound
height, and ticks-to-rest. The check requires the signatures to differ beyond a
stated tolerance *and* pins the soccer values as the regression band.

**Measured** (`soccer_and_drink`, both balls dropped `240 px` above their own resting height,
identical on seeds `1/7/13` and in both presentation modes, because a drop is deterministic
physics):

| | rebounds | peak rebound | routed ticks to registry rest |
|---|---|---|---|
| Baseball (`Bounce 0.0`) | 0 | `0.0 px` | 153 |
| Soccer Ball (`Bounce 0.65`) | 6 | `82.1 px` | 417 |

Bands asserted: soccer rebounds `>=` baseball `+ 2`, soccer peak `>= 60 px` **and** `>=`
baseball `+ 40 px`, soccer ticks-to-rest `>` baseball's. The Baseball row is separately pinned
as `bounce_zero_objects_did_not_change` (`<= 1` rebound, `<= 8 px` peak).

### 2.3 Drink — `data/objects/drink.tres`, spawn key `9`

| Field | Provisional | Why |
|---|---|---|
| `ContentId` | `tool.drink` | |
| `Consumable` | true | |
| `ConsumeMoodGain` | 5.0 | RAGDOLL §8.2 |
| `ConsumeCooldownTicks` | 7200 | 60 s at 120 Hz, RAGDOLL §9.2 |
| `ConsumeHungerFill` | 0.0 | §3 default 1 — a drink is timer-gated, not appetite-gated |
| `Radius` / `Mass` | 8 / 0.6 | small can |
| `LinearDamp` / `AngularDamp` | 1.6 / 2.6 | Meal-like, doesn't roll away |
| Colors | authored, `#4aa3df`-class | |

The coherent gating story, stated so nobody re-derives it: the **Meal** has
`ConsumeCooldownTicks = 0` because *appetite* rations food (owner 2026-07-29, hunger
bar); the **Drink** has a 60 s timer and `ConsumeHungerFill = 0` because it is not
food — the timer rations it and a full buddy still accepts one. Independence
(consume Meal → immediately consume Drink → both succeed) follows from per-id
cooldown slots and hunger-fill 0; the scenario proves both directions.

FR-008.10 holds by machinery: cancel/drop/refusal never starts the Drink's cooldown.

### 2.4 Presentation

Both items use the standard loose-object presentation seams (flat body drawing +
`Body2DVisual3D` ball visual) with authored colors. No new mesh builders: a
white/black ball and a small blue can read fine at placeholder fidelity, and final
art is explicitly M7. If the soccer ball's plain white sphere reads as an egg in 3D,
a simple two-tone banding on the existing ball visual is the permitted extent of
polish — anything more is out of scope.

## 3. Owner gate — **ACCEPTED in full (owner, 2026-07-31)**, pre-implementation

All three defaults below are owner decisions. Record them in `DECISIONS.md` at
Task E's bookkeeping; the feel gate still owns the tuning numbers (default 2's bounce
value stays empirical), but the rules are settled.

1. **The Drink never gets refused for a full stomach** (`ConsumeHungerFill = 0`).
   Alternative: a small fill (~10) so chain-feeding drinks eventually meets the
   refusal performance.
2. **Soccer bounce 0.65 and long roll** — tuned to "playground ball", measured and
   recorded at Task B; the owner's feel gate owns the final number.
3. **Both are buy-once, spawn-forever** like Baseball/Grenade (spawn keys `8`/`9`).

## 4. Implementation tasks (in order, each gated)

**Task A — Restitution seam.** §2.1: `Bounce` export + body wiring + validation.
*Accept:* new profile-validation unit rows; `baseball_pullback`, `meal_consume`,
`grenade_fuse` byte-for-byte green (no signature movement at `Bounce = 0`); domain
baseline unmoved (this is authoring-layer, not domain).

**Task B — Two presets + launcher wiring.** §2.2 + §2.3 `.tres` files, spawn keys
`8`/`9`, launcher presets, lab boot grants for both ids. Measure the soccer flight
and bounce signature; record numbers **here**.
*Accept:* scenario checks — `soccer_spawns_launches_and_rests` (key 8, pullback arc,
registry admission, rest), `drink_spawns_like_a_meal` (key 9),
`soccer_signature_differs_from_baseball` (§2.2 measured bands),
`bounce_zero_objects_did_not_change` (Baseball drop signature pinned before/after).

**Task C — `soccer_and_drink` scenario.** The master plan's original clean-catch criterion is
superseded by §8: the Soccer Ball never enters the pickup lifecycle; Meal→Drink
immediate-succession both succeed; Drink→Drink inside 60 s
refused `OnCooldown` and the cooldown was started only by success (a cancelled drink
leaves it consumable now); a full buddy accepts a Drink (`TooFull` never fires at
fill 0); presets verifiably distinct (§2.2 bands re-asserted on the composition).
*Accept:* seeds 1/7/13, both presentation modes.

**Task D — Journeys + registration.** Two lean real-input journeys per
`AGENT_VERIFICATION_AND_E2E.md` (per-tool, happy + cancel/secondary path):
`m5_soccer_ball` (catalogue leg per current visibility — grenade precedent — spawn,
player grab, pullback-cancel via the chord's cancel, then real launch, buddy never
hand-attaches it) and `m5_drink` (spawn, buddy drinks, +5 mood, immediate second drink
refused on cooldown, refusal-not-punished leg). Register both + the scenario in
`ScenarioCatalog`, `TEST_PLAN.md`, quick suite (+3 steps).
*Accept:* journeys green seeds 1/7, both presentations.

**Task E — Feel gate + bookkeeping.** Owner plays both. DECISIONS entry (the three
§3 defaults + the restitution seam), `CHECKLIST.md`, TEST_PLAN suite docs, full
sweep recorded here. `Visible = true` per item only on the owner's word — the two
entries can flip independently.

## 5. Validation commands

The standard three: build + domain suite, quick scenario suite, targeted runs
(`soccer_and_drink`, `m5_soccer_ball`, `m5_drink`, plus `baseball_pullback`,
`meal_consume`, `consume_care_cooldown`, `object_budget` as neighbours) across seeds
1/7/13 and both presentation modes. Any baseline movement stated in the commit
message, never silently absorbed.

## 6. Implementation record (2026-07-31)

**Sweep, all green, no baseline movement:**

- `dotnet test` — `999/999`, unchanged. The plan predicted this: the whole slice is
  authoring-layer plus two `.tres` files, and there is no new domain logic to unit-test.
- `soccer_and_drink` — 8 checks, seeds `1`, `7`, `13`, plus `--presentation=legacy` on seed
  `1`. All pass.
- `m5_soccer_ball` — 6 assertions, seeds `1` and `7`, plus legacy on seed `1`.
- `m5_drink` — 7 assertions, seeds `1` and `7`, plus legacy on seed `1`.
- Neighbours: `baseball_pullback`, `meal_consume`, `grenade_fuse`, `consume_care_cooldown`,
  and `object_budget` all green with unmoved readings — `baseball_pullback` still measures
  launch `1575 px/s`, impact impulse `426.8`, pain `4.4`.
- `devtools\verification\quick_validate.bat` — 31 steps, green.

**Deviations from the plan, both additive and both stated:**

1. §2.1 calls restitution "the one code addition", but §2.2 also asks the Soccer Ball for its
   own `PullbackLauncherProfile`, which the launcher had no way to honour — it read one shared
   preset. That is the second (small) code addition: `LooseObjectProfile.Launch`, plus
   `PullbackLauncherComponent.AimTuning`, which resolves the aimed body's own preset and falls
   back to the shared one. Null default, so no existing launchable changed.
2. §2.4's optional two-tone banding on the ball visual was **not** done. Loose objects have no
   3D mesh presenter today — they draw flat in both modes, as the Baseball, Meal, and Grenade
   body all do — so banding would have meant building a presenter, which §2.4 explicitly puts
   out of scope ("anything more is out of scope"; final art is M7). The ball is authored white
   with a black outline as the table asks.

## 7. Owner feedback pass (2026-08-01)

Three instructions arrived after the slice above landed; all three are implemented on this
branch and recorded in `DECISIONS.md` under "Soccer Ball Trap and Kick, the Drink's Single
Raise, and Both 3D Models". In short:

1. **The soccer loop** — roll → foot trap → one-second dwell → kick back at a seeded straight
   or slightly lofted angle. The decision lives in `Domain/Autonomy/SoccerPlayModel` (40 unit
   tests); `src/` only reads the ball, marks it reserved, and turns the intent into one bounded
   `ObjectDriveCommand`. Tuning is `data/objects/soccer_play.tres`, referenced by the ball's
   profile alone.
2. **The Drink's single raise** — `Domain/Presentation/ConsumeGesture` now owns both consume
   schedules (18 unit tests). The Meal's is the M4 arithmetic restated exactly; the Drink
   authors `SingleRaise` with a `60`-tick raise and a `240`-tick hold.
3. **Both 3D models** — `Presentation3D/LooseObjectVisual3D` and `LooseObjectMeshBuilder`,
   opted into by `LooseObjectProfile.Visual3D`. Clean-room placeholder art until M7.

**Measured:** trap at `234 px/s` approach, `119`/`120` dwell ticks with the ball at `0.00 px/s`
throughout, kick at exactly `520 px/s` with the gap going `52 → 171 px`, loft `12°` on seed `1`
and `24°` on seeds `7`/`13`; drink raised once and held `244` ticks against the authored `240`.

**Sweep:** domain suite `1057/1057` (was `999`, plus 40 soccer and 18 gesture rows);
`soccer_and_drink` 15 checks on seeds `1/7/13` and both presentation modes; both journeys on
seeds `1/7` and both modes; `meal_consume`, `consume_care_cooldown`, `activity_clips`,
`baseball_pullback`, `grenade_fuse`, and `object_budget` unmoved; `quick_validate.bat` 31 steps
green.

**Note for the merge:** Task 7's `EffectsSettings` seam (FR-017.3) is not on this branch, so
none of the above reads it. The new visuals are plain meshes with no particles, shake, or
motion effects, so there is nothing here that a reduced-motion setting would need to gate —
but if the seam later grows a "reduced detail" axis, `LooseObjectVisual3D.SetPresentationActive`
is the one place that would consume it.

**Left for the owner (Task E):** the feel gate itself. Bounce `0.65`, the roll damping, and
the ball's launch tuning are the numbers the gate owns; the rules behind them are settled per
§3 and recorded in `DECISIONS.md`. Flipping either `Visible` flag is the owner's word alone,
and the two entries can flip independently.

## 8. Owner feedback pass 2 (2026-08-01)

- The buddy never ordinarily picks up, catches, or hand-attaches the Soccer Ball; the former
  clean-catch reward checks are removed for this content id. Pass 6 adds the sole corner-rescue
  exception.
- Player Grab/launch contact enables trapping. Ground contact preserves eligibility; either
  side wall or the ceiling clears it until the next player touch.
- A low ball that is in foot range may still be kicked directly when trapping is unavailable.
  The existing soccer kick command is reused; no second lifecycle is introduced.

**Verification:** domain `1059/1059`; `soccer_and_drink` seeds `1/7/13` and
`m5_soccer_ball` seeds `1/7`, both presentation modes; neighboring `baseball_pullback` and
`object_catch_hold` remain green.

## 9. Owner feedback pass 3 (2026-08-01)

Content/Delighted buddies actively chase and watch a free Soccer Ball. Autonomous shot
selection is seeded between straight and non-zero arc. The original near-wall underfoot turn
was superseded by pass 6's deterministic rescue. A player-held ball instead requests a continuing
receive cadence: watch it, retreat for `600` routed ticks, pause for `120`, and repeat without
taking ownership; release immediately restores chase/play. The Soccer Ball is excluded from
ambient obstacle-hop evidence without changing any other object's hop behavior. These values
are provisional Resource data owned by Task E's feel gate.

The visible-gaze correction applies item look-at at full rendered head weight, opens
Delighted's otherwise pupil-less happy arcs while tracking, and fixes the shared pupil painter's
travel/vertical coordinate conversion. Verification asserts applied head yaw plus composed
pupil direction and includes a windowed post-draw capture rather than relying on target state.

## 10. Owner feedback pass 6 (2026-08-01)

Receive travel now carries a continuous rendered-gaze assertion rather than an eventual one.
The former random near-wall underfoot turn is replaced by the requested deterministic corner
rescue: the football-only exception lifts the stranded ball, carries/turns inward while watching
it, places it in front, waits one authored turn beat for separation, and kicks inward. Ordinary
football play remains hand-free, and player takeover aborts the rescue.

**Verification:** domain `1064/1064`; `soccer_and_drink` seeds `1/7/13` and
`m5_soccer_ball` seeds `1/7`, both presentation modes. The receive oracle samples `640`
travelling ticks with no semantic/head/open-eye pupil dropout; the corner oracle measures
`60` carried ticks before placement and the inward kick.
