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
(Fearful/Wary/Neutral/Content/Delighted). Mood-`60` upward-crossing trust reset (§ mood
rules): fire once on cross from `<60` to `≥60`, re-arm only after mood later drops below
`60`. Per-tool harmful history record. Transient channels (fear/pain/delight/curiosity/
unconscious) decay independently. xUnit: clamp, reduction formula incl. cap at 10, drift
rate + no-catch-up, band boundaries, trust-reset fire-once + re-arm, per-tool history.

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
`TEST_PLAN.md` §4 damage/economy assertions hold. Feel A/B as in M1 if tuning needs a pass.

## Progress

Plan written 2026-07-13 on branch `m3-sol` (M2 Task 0 renderer gate closed same day, so
HUD work in Tasks 10–11 is unblocked). Implementation begins at Task 1.
