# M5 Task 7 — Burning + Fire Sprayer Plan

**Status: PLAN — written 2026-07-31, not yet implemented.** Refines the master plan's
Task 7 stub (`M5_SHOP_AND_TOOL_CATALOGUE_PLAN.md`) to handoff fidelity, the same way
`M5_TASK5_GUN_FEEL_AND_REAL_PISTOL_PLAN.md` and `M5_TASK6_GRENADE_PLAN.md` did for their
slices. Authoritative contracts: RAGDOLL §9.1 (shared cursor-weapon aim), §9.2 Fire
Sprayer row, **§9.3 Burning**, FR-010.10, FR-017.3, and `DECISIONS.md` "Owner Feel Pass
and Shop Addition (2026-07-25)" (sprayer controls, Burning behavior, mood-loss rule).

**Prerequisites:** the grenade branch (`m5-gun-wrapup-and-grenade`) is merged. Gun
Tasks F/H (aim co-tuning, promotion) are **not** blockers — the sprayer reads
`CursorAimModel` through the same authored constants, so whatever values the co-tuning
session accepts flow into the sprayer's profile the same day. Start at Task A any time.

**Every §9.3 quantity left open by the spec** — burn pain per tick, tick cadence, mood
loss, visuals, particles, audio — **is authored data in this slice's profile**, tuned in
the lab at Task C and provisional until the owner's feel gate.

---

## 1. What exists today (do not rediscover)

- **Identity minted (M5 Task 0):** `ToolId.FireSprayer`, `tool.fire_sprayer`, category
  Damage, `data/catalogue/tool_fire_sprayer.tres` (50 credits provisional, progression
  order 10, `Visible = false`). No enum or catalogue work.
- **`Domain/Tools/CursorAimModel.cs`** — the shared aim lifecycle (smoothed motion
  vector, wheel offset, offset-clearing rule). The sprayer consumes it exactly as the
  guns do; RAGDOLL §9.1 says Pistol, Shotgun, **and Fire Sprayer** share this model.
- **`src/Tools/CursorGunComponent.cs`** — the thin-driver precedent: routed input frame
  in, domain models decide, pooled physical projectiles out. The sprayer is a sibling
  component on the same shape, **not** a `GunProfile`: `GunMachine` is a press-edge
  cadence/magazine machine and the sprayer is hold-to-stream with no magazine, no
  reload, and no press edge. Forcing it through `GunMachine` would mean authoring fake
  capacity — don't.
- **`src/Tools/ProjectileBody.cs`** — pooled physical body idiom (own pool, never a
  loose object, `InteractionId` per launch). Spray droplets copy the pooling and
  layer/mask discipline but **not** the damage path (§2.3).
- **`InteractionDamageComponent.ApplyBlastImpulse`** (grenade Task C) — the sanctioned
  contact-free damage entry: equivalent impulse → shared curve → knockout window →
  payout → harmful memory → `ImpactAccepted` with a world point. **Burn ticks reuse this
  exact entry.** No new pain machinery exists anywhere in this slice.
- **`BehaviorPriority.Hazard = 3`** (`BehaviorArbiterModel.cs`) — already reserved with
  the comment "Burning or a recognized nearby hazard: drop held hazards and flee", and
  already plumbed: `HazardPresent` / `HazardFleeDirection` snapshot fields exist and
  resolve above `ObjectAction` (5), so a committed catch/eat aborts by ladder
  construction (`ObjectInteractionModel` aborts when a higher priority owns actuation).
  **Burning's panic is setting one snapshot bool**, not a new behavior system.
- **Hard reposition** — the centralized fail-safe cleanup (DECISIONS "Fail-safe
  cleanup") already *promises* to clear Burning: "releases the active grab …, clears
  unstable velocities, rolling pain, knockout, **Burning**, and other temporary
  statuses". This slice makes that sentence true by adding one `Clear()` call to the
  existing operation.
- **Settings fields with no consumer:** `ProgressSave` carries `ReducedMotion`,
  `ScreenShake`, `ReducedParticles`, `PhotosensitivitySafe` — and **nothing in `src/`
  reads them yet**. FR-017.3 says this slice's effects honor them from day one, so the
  read seam is built here (§2.5), not deferred to the M7 accessibility pass.
- **Audio idiom** — `SwingAudioComponent`/`GrenadeAudioComponent`: clean-room
  synthesized PCM at 22 050 Hz, per-cue counters as scenario oracles, headless-safe.
- **Presentation idioms** — `CameraKickComponent` (not used by the sprayer — no kick),
  muzzle-flash additive lane, `ImpactFeedbackPresenter` rings, `GrenadeVisual3D`'s
  four-layer explosion (fireball/ember color language to reuse for flame, including the
  index-based golden-angle fan — **no randomness in presentation**).
- **Mood rule** — DECISIONS 2026-07-25: "Each accepted harmful event reduces mood by
  `min(10, pain × 0.1)`. **Burning pain ticks use the same rule** and knockout adds no
  separate mood penalty." Nothing to build: mood loss falls out of the shared economy
  path the moment burn ticks are accepted events.

## 2. Design

### 2.1 `BurningStatusModel` — pure domain, `Domain/Damage/BurningStatusModel.cs`

Engine-free, allocation-free, immutable-in/out on the `GunMachine`/`GrenadeFuseMachine`
idiom. Time is routed ticks only.

Constants (`BurningConstants`, validated well-formed):

| Constant | Provisional | Meaning |
|---|---|---|
| `ApplyTicks` | 480 | 4 s granted per fresh contact (§9.3) |
| `CapTicks` | 960 | remaining never exceeds 8 s (§9.3) |
| `PainIntervalTicks` | 60 | one attributed pain event each 0.5 s while burning |

Semantics:

- `Apply()`: `remaining = min(remaining + ApplyTicks, CapTicks)`. Sustained per-tick
  contact therefore pins remaining at the cap — which is exactly the master-plan check
  "sustained spray caps at 8 s remaining". A fresh apply on a non-burning buddy starts
  at 480.
- `Tick()`: decrements; every `PainIntervalTicks` of continuous burning it flags
  `PainEventDue` (first event one full interval after ignition — the spray contact
  itself scores nothing, §2.3). Expiry at 0 is silent; no exit event beyond the flag
  going quiet.
- `Clear()`: immediate, idempotent — the entry point for hard reposition (this slice)
  and the Repair Kit (Task 10 calls it; ships here unused by any tool).
- The model owns **timing only**. Which part burns, pain size, and mood are the
  component's and the shared pipeline's business.

Unit table: apply/refresh/cap arithmetic (fresh, mid-burn refresh, at-cap refresh),
tick cadence including the first-interval delay, expiry exactness, clear-mid-interval,
survives-many-cap-cycles determinism, ill-formed constants → inert.

### 2.2 Spray — `FireSprayerComponent` + `FireSprayerProfile` (`data/tools/fire_sprayer.tres`)

- **Aim:** `CursorAimModel`, same authored constants block as the guns (copied into the
  sprayer's profile like `GunProfile` carries them; Task F of the gun plan may move the
  accepted values — author whatever is current, they're data).
- **Emission:** while primary is held and a cursor exists, emit one droplet every
  `EmitIntervalTicks = 4` (30/s). No press edge, no magazine, no reload, no dry-fire.
  Releasing primary stops emission the same tick — that is the tool's "cancel path" for
  the journey. Secondary is free (nothing to cancel), matching §9.1's exception table.
- **Droplets:** pooled physical bodies (own pool, `PoolCapacity = 48`), spawned at the
  authored muzzle offset along `AimForward`, speed `SprayDropletSpeed = 700 px/s`,
  gravity scale `0.4` (a sagging stream, not a bullet), lifetime `45` ticks and max
  travel `260 px` — the sprayer is deliberately a **close-range** weapon. Lateral fan
  by droplet **index** (deterministic triangle wave across `SprayHalfAngleDegrees = 7`),
  never a random source: replayed seeds must reproduce the stream exactly, and the
  fan pattern is gameplay, not presentation.
- Droplets collide with `BuddyParts | RoomBounds`; a room-bounds hit or expiry re-pools
  them. They are never loose objects, never evictable, and keep flying while the tool
  is deselected (a stream in the air belongs to the room — gun-pool precedent).

### 2.3 Burning is the only harm lane — droplets never score impacts

A droplet's buddy contact does exactly two things: `BurningStatusModel.Apply()` and
record the **ignition part** (the most recently sprayed `BuddyPart` — where the burn
"is"). Droplets are authored below the dedup impulse threshold *and* explicitly routed
around the contact pipeline, so a stream can never double-dip as both impact pain and
burn pain. The scenario asserts zero accepted impacts attributed to `tool.fire_sprayer`
during pure spraying.

Each `PainEventDue` tick, the component calls
`Pipeline.ApplyBlastImpulse(burnInteractionId, ContentIds.ToolFireSprayer,
ignitionPart, BurnEquivalentImpulse, partWorldPoint)`:

- **One burn = one interaction id** (minted at ignition, re-minted when a lapsed burn
  reignites) so rolling-pain bookkeeping and any future episode logic see a continuous
  burn as one source, and each `PainIntervalTicks` event is a fresh accepted sample.
- Everything downstream is untouched machinery: shared curve (impulse→pain — the
  no-per-tool-multiplier rule holds because the burn is an impulse source, grenade
  precedent), zero-pain floor, knockout window, payout, **harmful memory**
  (`tool.fire_sprayer` becomes feared, which is what makes the buddy flee the tool
  later), and the `min(10, pain × 0.1)` mood loss.
- `BurnEquivalentImpulse` provisional **200** — tune at Task C to **3–6 pain per
  event**, giving a full 4 s burn ≈ 25–45 total pain and a sustained 8 s cap burn
  ≈ 55–90: painful, profitable, and **never a knockout by itself** (§3 default 1).
- Burning **survives knockout** (master-plan check): the model keeps ticking and events
  keep applying under the existing unconscious-buddy rules — the same "a blast cannot
  retrigger a running knockout" behavior the grenade documented.

### 2.4 Panic, dropping, fleeing — one snapshot bool

While `BurningStatusModel.IsBurning`, the buddy's arbiter snapshot sets
`HazardPresent = true` with `HazardFleeDirection` pointing away from the player's
cursor while spray is active, else away from the nearest wall (read how
`GrabHardRecoveryScenario`/ladder tests author flee direction and match — resolve the
exact source at implementation, it is one expression). Priority 3 then:

- outranks `ObjectAction` (5): a committed catch/hold/eat aborts through the existing
  `ObjectInteractionModel` higher-priority abort, which **is** "drops held items";
- outranks `Social`/`Ambient`: the buddy runs, which is the panic;
- stays below `Unconscious` (1): a KO'd burning buddy lies there and burns, correctly.

Zero new behavior systems. The scenario proves drop + panic through the real ladder.

### 2.5 Presentation + the FR-017.3 seam (all presentation-only)

**`EffectsSettings` seam (new, small):** the composition root snapshots the four
`ProgressSave` fields into an `EffectsSettings` readonly struct and hands it to
presentation components at initialize (and on settings change, when a settings UI
exists — today the lab exposes toggles on its panel). **Gameplay never reads it**: a
dedicated scenario check runs the same seed with all four settings flipped and asserts
identical pain/mood/tick outcomes — determinism must not vary with accessibility.

- **Spray visual:** droplets draw as flame-colored streaks (3D: small additive quads
  through the standard visual seam; legacy 2D: the droplet body's own canvas). Under
  `ReducedParticles`, visual droplet draw thins to every third droplet — the *physics*
  stream is unchanged.
- **Burning buddy overlay:** flame flicker on the ignition part (both modes), flicker
  modulation capped at **3 Hz when `PhotosensitivitySafe`** (default true — so the safe
  cap is the shipped look; the unsafe faster flicker is the opt-out), ember motes under
  the reduced-particles rule, everything on the `GrenadeVisual3D` index-fan idiom.
- **No screen flash, no camera kick** from this tool. While the seam is being built,
  wire the two lines that make `ScreenShake = false` silence `CameraKickComponent`
  globally (pistol/grenade kicks) — the seam makes it trivial and shipping the seam
  while leaving the one existing shake setting dead would be absurd. Flagged here, not
  silent.
- **Audio:** `FireAudioComponent` on the established idiom — a looped-by-chunks spray
  hiss while emitting, a soft ignition *whumpf* on a fresh apply. Counters as oracles.

### 2.6 Selection, lab, budget

- Selection key: assign at implementation from the free map (`G/B/K/J/F/T/N` taken;
  suggest `H`). Wheel/aim HUD behavior identical to guns.
- Lab boot grant: add `ContentIds.ToolFireSprayer` to the development-laboratory
  unlock list in `BuddyLab` (grenade precedent).
- Loose-object budget: droplets are pooled, never registered — FR-014 untouched; state
  it in the scenario with a registry-count probe during a long spray.

### 2.7 Authored data (`FireSprayerProfile`, validated finite/positive)

| Field | Provisional |
|---|---|
| `EmitIntervalTicks` | 4 |
| `SprayDropletSpeed` / gravity scale | 700 px/s / 0.4 |
| `DropletLifetimeTicks` / `MaxTravelPx` | 45 / 260 |
| `SprayHalfAngleDegrees` (index fan) | 7 |
| `PoolCapacity` / droplet radius / mass | 48 / 1.5 / 0.05 |
| `BurningConstants` (Apply/Cap/PainInterval) | 480 / 960 / 60 |
| `BurnEquivalentImpulse` | 200 → tuned to 3–6 pain/event |
| Muzzle offset / visual length / `Visual3DKind` | ~48 px / 52 px / next free kind |
| Flame colors (stream, flicker, ember) | authored, `#ff9a3c`-class |
| Aim constants block | copy of current accepted gun values |

## 3. Owner gate — **ACCEPTED in full (owner, 2026-07-31)**, pre-implementation

All four defaults below are owner decisions, not provisional guesses. Record them in
`DECISIONS.md` at Task F's bookkeeping as accepted 2026-07-31; the feel gate still
owns the *tuning* (numbers), but these *rules* are settled.

1. **A full burn never KOs by itself.** Even a sustained 8 s cap burn peaks below the
   100-pain rolling window. (Alternative: hotter — a max burn alone can KO.)
2. **The sprayer has no ammunition, heat, or duration limit** — hold primary forever.
   Guns author "unlimited reserve"; the spec is silent for the sprayer, and a fuel
   gauge would be new UI for no requested reason.
3. **Fire does not spread.** Only the buddy burns; objects, walls, and the room are
   not flammable, and a burning buddy ignites nothing it touches.
4. **The stream pushes nothing.** Droplet mass is cosmetic-tiny; the sprayer harms
   through Burning only, with zero knockback lane.

## 4. Implementation tasks (in order, each gated)

**Task A — Burning domain model.** §2.1 complete with the full unit table.
*Accept:* all timing/refresh/clear/determinism tests green; domain baseline moves by
exactly the new test count; no other suite touched.

**Task B — Sprayer, droplets, ignition.** §2.2 + §2.3's apply path (no pain events
yet), selection key, lab grant, pool discipline.
*Accept:* scenario checks — `spray_streams_only_while_primary_held` (emission counts,
release stops same tick), `droplets_never_register_as_loose_objects` (registry probe
over a 5 s spray), `spray_contact_ignites_and_refreshes_to_cap` (fresh 480, sustained
pins 960), `droplets_score_zero_impacts` (no `tool.fire_sprayer` accepted impact from
pure spraying); Baseball/gun scenarios untouched.

**Task C — Burn pain, panic, drop.** §2.3 events + §2.4 snapshot wiring + hard
reposition clear. Measure and tune `BurnEquivalentImpulse` to the stated band; record
the numbers **in this section** (grenade-plan style).
*Accept:* `burn_ticks_pay_and_hurt_on_the_shared_formula` (pain per event in band,
mood delta = `min(10, pain × 0.1)` per event, payout attributed `tool.fire_sprayer`,
harmful memory entered), `a_burning_buddy_drops_its_ball_and_panics` (real ladder:
held object released via priority-3 abort, hazard layer active),
`burning_survives_knockout_but_not_hard_reposition`, `a_full_cap_burn_never_knocks_out`
(§3 default 1 proven, not assumed). Seeds 1/7/13.

**Task D — Presentation, FR-017.3 seam, audio.** §2.5 complete, both presentation
modes.
*Accept:* `settings_change_visuals_and_never_gameplay` (same seed, all four toggles
flipped, identical pain/mood/ticks; visual draw counts differ),
`flicker_respects_the_photosensitivity_cap` (measured modulation ≤ 3 Hz when safe),
`screen_shake_setting_silences_the_kick_lane` (pistol fire under `ScreenShake=false`),
`spray_and_ignition_cues_fire_with_counters`; existing presentation regressions green.

**Task E — Scenario, journey, registration.** `burning_status` (the master plan's
accept list is the floor: spray → 4 s of attributed ticks, cap at 8 s, drop + panic,
per-tick formula match, survives KO, cleared by hard reposition) + `m5_fire_sprayer`
journey (catalogue leg per the current visibility state — grenade-journey precedent —
select by key, real pointer spray, burn, panic, release-stops-spray cancel leg).
Register in `ScenarioCatalog`, `TEST_PLAN.md`, quick suite.
*Accept:* journey green seeds 1/7, both presentations; quick suite grows by exactly 2.

**Task F — Feel gate + bookkeeping.** Owner plays it on real Windows. Then: DECISIONS
entry (the four §3 defaults as accepted/vetoed + the settings-seam scope), TEST_PLAN
suite documentation, `CHECKLIST.md`, full validation sweep recorded here.
`Visible = true` only on the owner's word. RAGDOLL §9.3 needs **no** amendment — every
number this slice authors is a §9.3 "tuning value" by construction.

## 5. Validation commands

The standard three (toolchain notes): build + domain suite, quick scenario suite, and
targeted runs (`burning_status`, `m5_fire_sprayer`, plus `pistol_fire`,
`nerf_versus_pistol`, `object_toss_discard`, `behavior_priority_ladder` as neighbours)
across seeds 1/7/13 and both presentation modes. Any baseline movement stated in the
commit message, never silently absorbed.
