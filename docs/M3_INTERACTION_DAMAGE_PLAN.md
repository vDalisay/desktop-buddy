# Milestone 3 — Core Interaction and Damage Slice

Authoritative scope: `docs/ROADMAP.md` M3 and `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` §7–§8
(`docs/DECISIONS.md` wins on conflict). This plan splits **pure-Domain,
headless-testable** logic (Tasks 1–8) from **Godot integration** (Tasks 9–11) and the
**owner-manual** feel/HUD gate (Task 12), mirroring the M2 plan structure. All approved
constants are transcribed from the spec; every value marked empirical stays in a tuning
`Resource`, never a literal in logic.

## Design seams (from `ARCHITECTURE.md` §6 worker roles)

| Worker | Home | Responsibility |
| --- | --- | --- |
| Impact router | `Domain/Interaction` | Contacts → deduplicated, attributed impact samples (§7.1–7.2). |
| Pain/knockout | `Domain/Damage` | Pain-conversion curve, rolling 5 s pain window, fixed 4 s knockout (§7.2–7.3). |
| Reward ledger | `Domain/Economy` | Payout formula, milli-credits, 0.25 s coalescing (§7.4). |
| Mood/memory | `Domain/Mood` | Persistent-mood clamp + drift, per-harm reduction, mood-60 trust reset, per-tool harmful history, transient channels (§8.1, §7.2). |
| Care | `Domain/Mood` | Pet/Tickle valid-contact cadence, +1 mood per 3 s (§8, tool table). |
| Passive income | `Domain/Economy` | Piecewise mood multiplier 0.25/1.0/2.0 (§8). |

Simulation time everywhere is monotonic runtime seconds (spec §2 / `TelemetryFrame`
tick), never wall clock.

## Tasks

### Task 1 — Impact router: contact-episode dedup (Domain, headless-testable)
`ImpactRouter` + `ContactEpisode` in `Domain/Interaction`. Input: raw contact samples
`(sourceInteractionId, targetPartId, impulse, relativeVelocity, timeSeconds)`. Rules
(§7.1–7.2): episode key = `(sourceInteractionId, targetPartId)`; the first valid contact
in an episode yields exactly one accepted sample; resting/sliding repeat callbacks are
suppressed; a new episode for the same key requires ≥ `0.15 s` of separation/inactivity.
Emits accepted `ImpactSample`s carrying part ID → derived payout region. xUnit: first-hit
accept, repeat-suppress, re-arm after 0.15 s gap, distinct keys independent, sub-0.15 s
re-contact rejected.

### Task 2 — Pain conversion curve (Domain, headless-testable)
`PainCurve` reads an empirical impulse→pain mapping from a tuning `Resource`
(`PainConversionData`); logic holds no magic numbers. Non-negative, monotonic-non-
decreasing pain for accepted impulse. `PayoutRegion` enum (Head/Torso/Arms/Legs) with the
part-ID → region map (§6 body-part mapping). xUnit: zero/low/high impulse, clamp at zero,
region mapping per part ID.

### Task 3 — Pain/knockout window (Domain, headless-testable)
`PainKnockoutModel` in `Domain/Damage`. Timestamped accepted-pain events over a rolling
`5 s` window; sum ≥ `100` while conscious → enter Unconscious once, start exact `4 s`
monotonic timer, clear the rolling window, ignore retrigger until the timer completes
(later hits never restart/extend), wake at completion (§7.3). Hits during unconsciousness
stay valid pain/reward/mood but never enter a future window. Repair Kit clears
pain/rolling state but does not shorten an active knockout (§7.3 last para). xUnit:
sub-threshold no-KO, threshold KO once, window slides out old events, retrigger ignored,
wake at 4 s, unconscious hits excluded, repair-kit-does-not-shorten.

### Task 4 — Reward ledger + milli-credits (Domain, headless-testable)
`RewardLedger` in `Domain/Economy`. `money = pain × regionMult × unconsciousMult ×
cashPerPain`; region 1.2/1.0/0.8/0.8, consciousness 1.0/0.5 (§7.4); grab adds no modifier.
Balance in signed 64-bit **milli-credits** (1000/credit); HUD reads whole credits.
Accepted rewards within `0.25 s` coalesce into one `+$N.N` feedback event; raw pain stays
hidden. `cashPerPain` from tuning `Resource`. xUnit: per-region/consciousness products,
milli-credit accumulation with no float drift, 0.25 s coalescing boundary, whole-credit
display floor.

### Task 5 — Mood/memory model (Domain, headless-testable)
`MoodModel` in `Domain/Mood`. Persistent mood clamped `[-100,+100]`; each accepted harmful
event reduces mood by `min(10, pain × 0.1)` (Burning ticks use the same; entering knockout
adds no separate penalty, §7.2). Drift toward `0` at `0.5`/min while running (incl.
hidden-to-tray), no catch-up across close/sleep/clock gaps (§8.1). Mood-band lookup
(Fearful/Wary/Neutral/Content/Delighted). Mood-`60` upward-crossing trust reset (§4.1):
fire once on cross from `<60` to `≥60`, re-arm only after mood later drops below `60`.
Per-tool harmful history record. xUnit: clamp, reduction formula incl. cap at 10, drift
rate + no-catch-up, band boundaries, trust-reset fire-once + re-arm, per-tool history.
(Transient acute channels — fear/pain/delight/curiosity/unconscious — have no approved
durations, so they land with the presentation tuning in Task 10, not here.)

### Task 6 — Care model: Pet/Tickle cadence (Domain, headless-testable)
`CareModel` in `Domain/Mood`. Valid Pet/Tickle contact grants `+1` mood at most once per
`3` valid-contact seconds each (§8 care table); cadence counts valid contact only, not
held input over empty space; no immediate money. xUnit: award once per 3 s, empty-space
holds never award, Pet and Tickle track independent cadences.

### Task 7 — Passive income service (Domain, headless-testable)
`PassiveIncome` in `Domain/Economy`. Piecewise-linear mood multiplier through anchors
`0.25×` at `-100`, `1.0×` at `0`, `2.0×` at `+100` (§8), applied to a base rate from
tuning `Resource`; accrues in milli-credits on the monotonic timer; suspended-physics /
hidden-to-tray accrual uses the same low-cost clock with no catch-up. xUnit: anchor
values, interpolation midpoints, accrual over elapsed time, no catch-up across a gap.

### Task 8 — Tool catalogue + selection (Domain + Resources, headless-testable)
`ToolId`/`ToolDefinition` resources for the M3 subset: **Grab** (exists), **Pet**,
**Tickle**, **Boxing Glove**. Pet/Tickle = held stroke over valid buddy contact; Boxing
Glove = cursor-tethered physical collider whose real swing impulse drives pain (no hidden
tool multiplier — differences arise from physical contact + shared curve, §7.2/§9). New
save starts with Grab selected (§ shop rules). Selection never changes on Work/Play
transition (M2 invariant). xUnit for selection/persistence-shape; physical behavior is
Task 9.

### Task 9 — Godot wiring: contacts → router → damage → ledger/mood (integration)
In the Godot assembly: feed `PuppetPartBody` authoritative contacts into `ImpactRouter`
with real part IDs/impulses, run the Task 2–5 pipeline on the gameplay fixed tick, and
apply mood/ledger results. Add the Boxing Glove tethered collider (reuses the M1 grab
tether pattern) and Pet/Tickle stroke detection over valid buddy contact. Headless
scenarios: `impact_dedup`, `knockout_window`, `payout_by_region`, `pet_tickle_mood` —
driven through the runner, asserting semantic events, not pixels.

Wiring notes (2026-07-13 domain review):
- The ledger multiplier uses `PainAcceptance.ConsciousnessAtAcceptance`, never the
  post-transition state — the KO-triggering hit landed conscious and pays `1.0×`
  (§7.1 "at acceptance time"). `KnockoutTriggered` drives the §7.3 semantic event.
- Call `MoodModel.RegisterHarm` only for accepted events with `pain > 0`, or a
  below-curve-floor contact marks a tool feared with zero mood loss.
- Passive income credits the balance via `RewardLedger.Deposit` (silent — no `+$N.N`
  burst). Per ROADMAP its wiring is M4 scope; the M3 exit gate does not need it.
- Calibrate `ImpactRouter.MinimumImpulse` together with the §7.1 attribution-expiry
  rule: below-threshold contacts do not keep an episode alive, so a resting object's
  occasional above-threshold jitter (> 0.15 s apart) would re-score. The
  `impact_dedup` scenario should include a resting-object case.
- The `PainConversionData` and `ToolDefinition` tuning `Resource` assets promised in
  Tasks 2/8 are Godot-side and land here (Domain classes take plain constructor data).

### Task 10 — Reaction, expression, sound, fear resistance (integration/presentation)
Face emoticons on the head circle per consciousness/acute-state/mood-band priority (§ face
rules), transient reaction state, nonverbal robot sounds, and fear-based grab resistance
wired to `MoodModel` fear + tool history (physical resistance already lands in M1;
strength mapping is empirical tuning). No approved constants — emoticon set and durations
are presentation tuning.

### Task 11 — Minimal money HUD + debug tuning panels (integration)
Whole-credit balance readout + coalesced `+$N.N` reward feedback; debug-only panels to
inspect mood/pain-window/ledger and nudge tuning. HUD is renderer-dependent — unblocked by
the closed M2 Task 0 gate (`gl_compatibility` accepted).

### Task 12 — M3 exit gate (owner-manual)
Owner verifies on real Windows: interactions stable and satisfying, payouts arise from
physical pain and never from merely pressing a tool button (ROADMAP M3 exit criteria);
`TEST_PLAN.md` §2 damage/economy assertions hold. Feel A/B as in M1 if tuning needs a pass.

## Progress

Plan written 2026-07-13 on branch `m3-sol` (M2 Task 0 renderer gate closed same day, so
HUD work in Tasks 10–11 is unblocked).

**Tasks 1–8 DONE (2026-07-13)** — the entire pure-Domain, headless-testable damage/mood/
economy core, suite green at **190/190** (was 102 pre-M3):
- Task 1 `Interaction/ImpactRouter` — contact-episode dedup.
- Task 2 `Damage/PainCurve` + `Damage/PayoutRegion` — impulse→pain + part→region.
- Task 3 `Damage/PainKnockoutModel` — rolling 5 s window + fixed 4 s knockout.
- Task 4 `Economy/RewardLedger` — payout formula, milli-credits, 0.25 s coalescing.
- Task 5 `Mood/MoodModel` — persistent mood, harm reduction, drift, trust reset, history.
- Task 6 `Mood/CareModel` — Pet/Tickle valid-contact cadence.
- Task 7 `Economy/PassiveIncome` — mood multiplier + drift-free accrual.
- Task 8 `Tools/ToolSelection` + `ToolCatalog` — M3 tool subset, Grab default, category map.

**Review pass (2026-07-13):** Tasks 1–8 verified against RAGDOLL §7–§8 boundary by
boundary. Three pre-Task-9 API seams fixed with tests (suite 195/195):
`RegisterPain` now returns `PainAcceptance` (consciousness *at acceptance* for the
payout multiplier + `KnockoutTriggered` edge for the semantic event);
`RewardLedger.Deposit` added for passive income; an unpolled elapsed feedback burst is
queued instead of overwritten. Wiring hazards recorded under Task 9.

**Tasks 9–11 DONE (2026-07-13):** authoritative `RigidBody2D` contacts now flow through
the shared router/pain/ledger/mood pipeline in both `BuddyLab` and the normal sandbox.
The physical Boxing Glove, Pet/Tickle stroke detector, reaction priority, original PCM
robot chirps, mood/tool-history fear resistance, compact money HUD, and development-only
telemetry are composed through focused typed components. The committed Godot coverage is
`impact_dedup`, `knockout_window`, `payout_by_region`, `pet_tickle_mood`, and
`m3_presentation`; `m3_glove_strike` exercises tool selection and the strike through the
real input queue and proves selection alone does not pay. Final verification: solution
build 0 warnings/errors, domain suite 195/195, all listed scenarios, M1
`grab_resistance`, normal `boot_smoke`, and the M3 journey pass on pinned Godot 4.6.1.

**Remaining:** Task 12 owner-manual exit gate on real Windows. The owner must accept
interaction feel and the compact HUD; this tuning/visual judgment is not replaced by the
automated green suite.
