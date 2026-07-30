# M5 Task 4 Refinement — Home-Run Bat Feel Plan

**Status:** Revised after architecture/feel audit, then after a second verification audit
against the codebase (2026-07-30) that corrected the swing arithmetic, the missing swing-phase
force cap, the free-swing caps, the victim-shake offset lane, and a stale test-count baseline.
**The §5 interaction-policy gate is resolved in full (2026-07-30); Tasks A–H are
complete, the revised feel is owner-accepted, and the catalogue entry is visible.** Four
of the nine answers changed the design — cursor-travel aiming, a whole-game freeze,
full-charge-only object freeze, and in-scope placeholder audio; see §5 and `docs/DECISIONS.md`
("Home-Run Bat Interaction Gate — Resolved in Full").
**Owner intent (2026-07-30):** The engineering-complete Baseball Bat (M5 Task 4) does not meet
the feel bar. Rebuild its handling so it plays like the Super Smash Bros. Home-Run Bat,
adapted to a mouse: grip it, charge it, and release a single devastating telegraphed swing.
**Owner clarification (2026-07-30):** The existing cursor-driven free-swing remains as a
separate **weak** physical attack. It must not rival or bypass the charged home-run attack.
**Baseline:** M5 Task 4 as recorded in `docs/DECISIONS.md` ("M5 Baseball Bat and the
Cursor-Tool Mechanism", 2026-07-30). Scenario `bat_swing` and journey `m5_baseball_bat` are
green and must stay green.

---

## 1. Owner specification (verbatim requirements)

1. The bat should **look 3D**.
2. **Holding left mouse** grabs the bat **by the handle** and holds it **upright**.
3. **Holding right mouse** (while gripped) **charges** the bat.
4. Longer charge → stronger swing and stronger impact, **capped at 5 seconds**.
5. While charging the bat **shakes**, ramping to maximum shake at 5 s. **At 5 s a small
   glimmer shines at the upper tip** of the bat.
6. **Releasing right mouse swings the bat.** On a hit, the **hit-stun freeze (hit lag) lasts
   longer the more charge** was stored, and the **hit object flies farther the more charge**
   was stored.

## 2. Reference research — the deliberate Smash hybrid

Facts gathered from SmashWiki (sources at the bottom), to be treated as the feel reference,
not as literal constants:

| Smash mechanic | Reference value | What it teaches us |
|---|---|---|
| Generic smash-attack charge | 60 frames (1 s), then holdable ~2 s more without extra power in Ultimate | Charge has a hard cap; holding past the cap is allowed and keeps max power |
| Generic charge feedback | Character moves/flashes while charging; later games briefly glint the fighter icon at full charge | Shake/flash communicates charge. The bat-tip glimmer in §1 is the owner's original adaptation, not a copied or source-claimed weapon effect |
| Full-charge damage | 1.4× | Charge multiplies outcome roughly 1.5×-ish in Smash; ours scales real swing speed instead |
| Home-Run Bat forward smash | One **single, delayed swing**; swing connects on frame **51 of 51** (Ultimate/SSB4), 60 in Brawl | Enormous anticipation, then one instant of contact. The windup **is** the weapon's identity |
| Windup animation (Brawl onward) | Characters "wind up like actual baseball players", pulling the bat **farther behind them** before the hit | Grip → lean back → snap behind the shoulder → sweep |
| Sweet spot | The **tip** ("tipper") sends the target farthest; the handle is the sourspot | Treat this as a laboratory hypothesis, not a free consequence: real tip speed rises with radius, but effective contact mass changes too |
| Launch | Up-and-away trajectory, extreme velocity, one-hit-KO identity | The arc must pass through contact **rising**, so the impulse points up-and-forward |
| Hit lag (Ultimate) | ≈15 frames at 15% damage; formula scales with damage; **hard cap 30 frames (0.5 s)**; attacker and victim freeze while unrelated actors continue; **the victim visibly shakes** | Scale duration with charge, cap at ~0.5 s, jitter the victim. **Owner overrode the scope**: our freeze stops *every* game element, not just attacker and victim (§4.7) — which makes the victim shake the only remaining on-screen motion, and therefore mandatory |
| Sound | The trophy-famous "KREEENG" ping on a connected smash | A connected smash wants an audible punctuation. **Owner overrode the original "defer audio" call**: this slice ships procedurally generated placeholder sounds (§4.8b). Reference only — never sample it |

Important reference correction: Smash's Home-Run Bat forward smash itself **cannot be
charged**; it always has the same delayed windup. This plan deliberately combines that
attack's one-swing anticipation, tipper aspiration, launch, and hit lag with the generic
smash-attack charge language because §1 explicitly asks for a charged bat. Our charge cap is
**5 s** and charge happens while holding the bat ready, so the post-release windup is shorter
than Smash's 51-frame Home-Run swing — the charge already sold the anticipation.

## 3. What exists today (do not rediscover)

- `src/Tools/CursorToolController.cs` — shared cursor-tool lifecycle: one collider at a time,
  center-anchored `GrabTether` follow, `AlignmentTorque` swing alignment for elongated tools,
  despawn/respawn on tool swap, playable-bounds clamp.
- `src/Tools/CursorToolBody.cs` — `RigidBody2D` capsule/circle, impact arming, squash pulse,
  2D debug draw, `IBody2DVisualPulseSource` (`VisualScale2D`/`VisualRotation2D`).
- `src/Tools/CursorToolProfile.cs` — authored data + validation; bat = `.tres`
  (`data/buddy/lab_cursor_tool_baseball_bat.tres`, length 90, radius 7, mass 6).
- `domain/DesktopBuddy.Domain/Physics/AlignmentTorque.cs` — pure bounded PD angular servo,
  `SwingAngleFor`, `SymmetricError` (half-turn fold) + unit tests.
- `src/Laboratory/LabPointerGrabComponent.cs` — the only pointer input path today. Primary =
  grab; Secondary = launcher aim, else cancel/drop; `K` selects the bat;
  `NotifyPointerExitedPlayArea` clears cursor tools.
- `src/Buddy/Presentation/ImpactFeedbackPresenter.cs` — ring + edge jolt on any cursor-tool
  impact; **fixed-length global slow-time** envelope (0.12 s, non-stacking), today only on
  max-pain or knockout. That accepted Boxing Glove effect remains unchanged; the charged bat
  uses the whole-game freeze in §4.7 instead.
- `src/Presentation3D/Body2DVisual3D.cs` — render-only 3D slot. **This file must change** for
  §4.8: `SetGeometry(radius, length, color, depthOffset)` today hard-codes both the mesh
  (`BuildMesh` → `CapsuleMesh`/`SphereMesh`) and `ShadingMode.Unshaded`, and takes only
  scalars, so there is no seam through which a lathed mesh or a `PerPixel` material can reach
  the slot. `BuddyLab.OnCursorToolSpawned` and `SandboxRoot.OnCursorToolSpawned` both call it
  with scalars. Task F adds the injection seam; the earlier draft listed this file only as
  pre-existing context and under-counted the work.
- `src/Buddy/Presentation3D/BuddyLookLightingRig.cs` — warm key + cool fill
  `DirectionalLight3D` with **shadows disabled**, already live;
  `BuddyLookMaterialLibrary` already uses `PerPixel` shading. A shaded bat will be lit
  correctly with no new lighting work.
- Tests: `src/Testing/BatSwingScenario.cs` (`bat_swing`), `tests/journeys/m5_baseball_bat.json`,
  **836-passing** domain suite baseline (verified 2026-07-30; `429` was the M3.6 figure and
  `648` the M4 one — do not check "baseline untouched" against either). **Task A landed
  2026-07-30 and moved the baseline to 940** (836 untouched + 104 new charged-swing/servo
  tests); check "baseline untouched" against 940 from here on.

**Sacred rule (DECISIONS.md):** pain comes only from the measured contact impulse through the
shared curve — **no per-tool or per-charge damage multiplier**. Charge must therefore make the
bat *really swing faster*, and both pain and knockback follow from true momentum. Whether the
tip naturally outperforms the barrel/handle is measured in the laboratory (§6); do not claim
or fake a tipper with a hidden damage factor.

## 4. Design

### 4.1 State machine (pure domain)

```
  (none) --select bat--> FOLLOW
  FOLLOW --LMB press--> GRIPPED
  GRIPPED --LMB release--> FOLLOW
  GRIPPED --RMB press--> CHARGING
  CHARGING --LMB release--> FOLLOW              (cancel, no swing)
  CHARGING --RMB release--> SWINGING            (latch epoch/charge/pivot)
  SWINGING --arc complete--> RECOVERY
  RECOVERY --settled, LMB held--> GRIPPED
  RECOVERY --settled, LMB released--> FOLLOW
  any state --pointer exit/tool swap/lost cursor--> despawn/reset
```

- **FOLLOW / weak free-swing** — the owner-confirmed secondary attack: center tether + swing
  alignment, with a rate-limited physical anchor and bounded drive so arbitrarily fast mouse
  input cannot rival the charged swing. Real contact may score positive pain through the
  shared curve, but the calibrated one-pass free-swing remains below maximum pain and below
  the charged attack's impulse/travel envelopes. This preserves the behavioral purpose of
  `bat_swing`/`m5_baseball_bat`; tests may tighten their strength assertions.
- **GRIPPED** — LMB held: tether anchor moves to the **handle**, an upright servo holds the
  barrel pointing up. Cursor motion drags the bat by the handle through a bounded physical
  anchor. Entering GRIPPED is non-damaging even if the re-anchor moves the body.
- **CHARGING** — LMB+RMB held: charge accrues in **routed physics ticks**
  (`600 ticks = 5 s @ 120 Hz`), clamps at max and stays there while held. The swing direction
  is latched when charging starts. Bat leans back, shake ramps, and the glint fires once at
  the cap. Releasing LMB cancels back to FOLLOW without a swing or damaging contact.
- **SWINGING** — RMB released: pointer input is ignored, the handle pivot stays at its
  release position, and the controller drives the scripted arc (§4.6). Any charge level
  swings; an RMB tap is a modest charged-mode swing, distinct from the weak free-swing.
- **RECOVERY** — short lockout, servo returns to upright; then GRIPPED if LMB still held,
  else FOLLOW. Recovery motion is non-damaging.
- Pointer exit, tool swap, or losing the cursor cancels everything (existing despawn paths).

Physical collisions remain enabled in every state so the bat cannot ghost through walls or
objects. **Pain admission is stateful:** FOLLOW admits the weak physical attack; SWINGING
admits at most one scored buddy impact per swing epoch; GRIPPED, CHARGING, and RECOVERY admit
no pain. This is an impact-admission rule, not a damage multiplier: every admitted event still
uses the solver impulse and shared pain curve unchanged.

### 4.2 New pure domain module — `domain/DesktopBuddy.Domain/Tools/ChargedSwing.cs`

Engine-free, allocation-free, mirroring the `AlignmentTorque`/`GrabTether` house style
(readonly record struct inputs/results, `Evaluate`-style statics, non-finite inputs reject):

- `ChargedSwingMachine.Tick(in ChargedSwingInput) → ChargedSwingResult` — the state machine
  above. Input: current state, grip held, charge held, elapsed ticks in state, profile
  constants. Result: next state, normalized charge `0..1`, one-shot event flags
  (`ChargeCompleted` for the cap/audio edge, `SwingReleased` carrying the released charge, latched
  direction/pivot, and a monotonically increasing `SwingEpoch`).
- `ChargeProgress(ticks, maxTicks) → float` — linear, clamped.
- `ShakeAmplitude(charge, maxAmplitude) → float` — ease-in `charge²·maxAmplitude`: subtle
  early, violent late, maximum exactly at full charge.
- `ShakeOffset(timeSeconds, amplitude, primaryHz, secondaryHz) → (x, y)` — deterministic
  two-frequency wobble (e.g. 33 Hz / 41 Hz, incommensurate so it never looks like a loop).
  Presentation-only; the physics body is never shaken.
- `SwingPlan(charge, rTip, profile) → (windupTicks, sweepTicks, followThroughTicks,
  sweepDegrees, targetAngularVelocity, targetTipSpeed)` — `targetTipSpeed` interpolates the
  authored uncharged/full endpoints, `targetAngularVelocity = targetTipSpeed / rTip`, and
  `sweepTicks` is **derived** from those (§4.6), never read from the profile. Duration shrinks
  and commanded angular velocity grows with charge because both fall out of one number.
- `SwingTrajectory(tick, plan, directionSign) → (barrelAngle,
  targetAngularVelocity)` — the position-and-velocity arc, continuous and monotonic through
  the contact zone. The velocity term is required feed-forward; the existing settling servo
  cannot guarantee a commanded swing speed from a moving angle target alone.
- `HitLagTicks(charge, minTicks, maxTicks) → int` — linear, rounded and clamped `6 → 60`
  routed ticks (`0.05 → 0.50 s` at 120 Hz).
- `SwingDirectionSign(cursorTravelX, travelThreshold, lastSign) → int` — §4.4 rule: returns
  `sign(cursorTravelX)` when `|cursorTravelX|` clears the threshold, otherwise `lastSign`,
  otherwise `+1`. No target argument; the owner's rule is pure cursor travel.
- `SwingImpactAdmission.Evaluate(mode, epoch, alreadyClaimed, scoredPain) → result` —
  allocation-free admission/claim policy: weak FOLLOW contacts use ordinary episode
  deduplication; home-run epochs claim only after their first positive-pain event and reject
  later buddy-part scoring for that epoch.

**Unit tests** (`tests/DesktopBuddy.Domain.Tests/Tools/ChargedSwingTests.cs`): every
transition in §4.1 including both cancel paths; exact `599/600/601` charge-tick boundaries;
charge clamps at max and holds; shake is 0 at 0 charge, monotonic, max exactly at cap;
`ChargeCompleted` fires exactly once per charge; trajectory angle is continuous and target
angular velocity finite/monotonic; direction truth table including the X dead zone; hit-lag
tick endpoints; swing-epoch claim/reject behavior; non-finite inputs rejected. Add two that
guard the §4.6 derivation specifically: `sweep_ticks_derive_from_tip_speed` pinning the
authored bat's endpoints at **24** and **8** ticks, and
`raising_tip_speed_shortens_the_sweep` proving the two move together — a future edit that
reintroduces an independently authored tick count fails both. Keep the current domain-test
baseline (**836**) green and add these on top.

### 4.3 Composition and input routing

Keep `CursorToolController` as the shared lifecycle/orchestrator, not an all-purpose bat
script. Add a focused `ChargedSwingComponent` worker (no `_PhysicsProcess`) for state,
grip/swing force targets, swing epochs, and damage-admission context. The controller injects
the active body/profile/cursor snapshot downward and applies the worker's bounded result;
the worker publishes semantic edge events upward for presentation. The worker never reads
hardware input or scene-tree groups. There is **no** target-resolver component: the
owner-confirmed direction rule is cursor travel alone (§4.4), so the slice needs no proximity
query against the rig or the loose-object registry.

`CursorToolController` exposes a scenario-drivable API that forwards to the swing worker
(input components translate; scenarios call directly, matching the `MoveCursor` seam):

```csharp
public void SetGrip(bool held);        // LMB while DrivesTool(selected) && profile.Swing != null
public void SetChargeHeld(bool held);  // RMB, same condition
```

`LabPointerGrabComponent.ResolvePendingInput` routing changes:

- Primary press/release → `CursorTools.SetGrip(...)` when the selected tool is a swing-capable
  cursor tool. (Today primary does nothing with the bat selected, so nothing is displaced.)
- Secondary press/release → `CursorTools.SetChargeHeld(...)` **before** the launcher/cancel
  else-branch, only when a swing-capable cursor tool is selected **and** neither
  `Grab.IsGrabbing` nor `LauncherTool.IsAiming` is live. That second condition is not
  redundant: `CanAimCurrentGrab` only inspects `Grab.CurrentGrab` and is **not** tied to
  `SelectedTool`, so grabbing a Baseball with `G`, beginning an aim with secondary, then
  pressing `K` leaves a live aim while a swing-capable tool is selected. Routing secondary to
  charge unconditionally would swallow the `RequestRelease()` that fires the launcher and
  strand the aim with no way to release it. Guarding on the two liveness flags keeps the
  launcher chord whole; the bat simply refuses to charge while a grab or aim is outstanding.
- `NotifyPointerExitedPlayArea` additionally clears grip/charge (despawn already happens).
- FOLLOW's rate-limited physical anchor introduces two candidate velocities. `ApplyAlignment`
  keeps consuming the **raw cursor velocity**, not the anchor's, so the barrel steers to where
  the player is swinging rather than to where the rate limiter has caught up to. Both exceed
  `MinimumAlignSpeed` (`60`) in every existing test, so this preserves today's alignment
  behaviour exactly; the anchor velocity is used only for the tether's relative-velocity term.

`LaboratoryControlComponent` gets no new keys; grip/charge are pointer-only, matching spec.

### 4.4 Grip, upright hold, and aim direction

- **Handle anchor:** the tether error is measured from the **handle point**, which is
  **derived from the collider, never authored** — local `(0, +Length/2 − Radius)`, the centre
  of the capsule's handle-end hemisphere (barrel is local −Y at rest). For the authored bat
  that is `(0, +38)`, giving a handle-to-tip lever `rTip = Length − Radius = 83 px` and a
  handle-to-centre-of-mass distance `rCom = Length/2 − Radius = 38 px`; both feed the derived
  sweep ticks in §4.6 and the force cap arithmetic. An earlier draft *also* authored a
  `HandleFraction = 0.92` knob, which resolves to `41.4 px` and silently contradicts the
  geometric rule — that field is **removed**, because a grip point that can disagree with the
  collider it grips is a data trap, not a tuning affordance.
- The tether force is applied at the corresponding **rotation-transformed world offset**
  (`ApplyForce(force, localOffset.Rotated(body.GlobalRotation))`), so the bat genuinely hangs
  from its handle instead of being center-pinned. Passing the unrotated local vector to
  `ApplyForce` is incorrect once the bat turns. Godot's `RigidBody2D.ApplyForce(force,
  position)` takes that offset from the **body origin** in global orientation — not from the
  centre of mass. They coincide for this body (the shape is centred on the node origin and the
  centre of mass is Auto), so the call is correct as written; the distinction matters only if
  anyone ever authors a centre-of-mass offset. `GrabTether` itself is unchanged.
- **Upright servo:** reuse `AlignmentTorque.Evaluate` with the **unfolded** error (do *not*
  use `SymmetricError` here — a real bat has a barrel end and a handle end; upside-down must
  be corrected). Target barrel angle = world up. New grip gains in the profile, stiffer than
  the swing-alignment gains so the upright hold reads intentional (start: stiffness 900 000,
  damping 120 000, same 500 000 torque cap).
- **Charge lean:** while CHARGING, the upright target leans **away from the swing direction**
  by `LeanDegrees` (default 35°) — the batter pulling back. This telegraphs the swing side.
- **Aim direction rule** (owner-confirmed 2026-07-30): the swing goes **the way the cursor is
  travelling** — nothing else. While GRIPPED or CHARGING, track the sign of horizontal cursor
  travel whenever it exceeds `DirectionTravelThreshold` (§4.9); that sign is the swing
  direction, so the bat always swings "in front of" where the player is dragging it. Below the
  threshold the last significant sign persists; with no significant travel since the bat
  spawned, default right. **Commit the sign at RMB release**, then hold it through
  SWINGING/RECOVERY.
- No target lookup is involved. An earlier draft resolved the nearest strikeable body within
  `2.5 × Length` and used `sign(target.x − cursor.x)` with a dead zone; the owner replaced that
  with pure cursor travel, which is both simpler and more predictable — the player aims by
  moving, not by the game guessing who they meant. This **deletes `SwingTargetResolver`
  entirely** (§4.3): it existed only to serve the target-based rule, and nothing else in the
  slice needs a buddy/loose-object proximity query.
- **Assumption to confirm if it feels wrong:** direction *tracks* through the charge and locks
  only at release, rather than locking when charging begins. That is the reading that makes
  "always in front of the cursor" true — you can wind up, change your mind, drag the other way,
  and the bat re-cocks. A visible consequence is that the charge lean (which tilts *away* from
  the swing side) flips sides mid-charge when you reverse. If the owner wants the lean to stay
  put once charging starts, latch at the CHARGING entry edge instead; everything else is
  unchanged.
- **Bounds clamp:** compute the pivot's allowed interval from the current/largest planned
  rotated barrel extent plus radius. A blanket full-length inset on both axes is safe but
  needlessly prevents wall-side aiming; the geometry helper must prove every planned angle
  remains inside the playable bounds.

### 4.5 Weak free-swing and impact admission

- A swing-capable profile gives FOLLOW its own `FreeSwingAnchorSpeedCap` and
  `FreeSwingForceCap`. The raw cursor remains the semantic pointer; a fixed-tick physical
  anchor advances toward it at the cap. This bounds high-DPI/teleport input so a flicked mouse
  cannot manufacture a home-run-grade impulse. The glove, whose `Swing` profile is `null`,
  keeps today's exact follow path.
- **The caps must not be set below what the two green bat tests already drive.**
  `BatSwingScenario` swings at `2 400 px/s` (`SwingSpeed`, `src/Testing/BatSwingScenario.cs`)
  and the `m5_baseball_bat` journey swings at `20 px` per physics tick — also `2 400 px/s`
  (`JourneyRunner.ExerciseM5BaseballBatAsync`). Both assert **lower** bounds:
  `fast_swing_scores_pain_attributed_to_the_bat` needs `Pain > 0` *and* `MilliCredits > 0`, and
  `bat_swing_hurts_the_buddy` needs `Pain > 0`. An earlier draft proposed
  `FreeSwingAnchorSpeedCap = 1 200` and `FreeSwingForceCap = 60 000` — exactly half of both
  today's swing speed and the profile's authored `MaximumForce`. That roughly halves the
  contact impulse and can drop it under the pain-curve floor, breaking the very baseline this
  plan promises to keep green. The defaults are therefore set at today's values by
  construction (`2 400` and `120 000`, §4.9), so FOLLOW behaviour is unchanged for the
  existing tests and the cap only bites on input *faster* than the current benchmark.
- **Lowering either cap is gated on measurement.** If Task D's envelopes overlap and the free
  swing must genuinely be weakened, first record the measured free-swing impulse at
  `2 400 px/s` against the curve floor, then lower the cap *and* raise the two tests' drive
  speed in the same change, so their lower-bound assertions keep proving something. Never
  lower a cap and leave a `Pain > 0` assertion sitting near the floor.
- Entering GRIPPED rate-limits the center-to-handle re-anchor. GRIPPED, CHARGING, and RECOVERY
  publish `ImpactMode.None`; their physical reposition/lean/settle contacts cannot enter the
  pain ledger or harmful history.
- FOLLOW publishes `ImpactMode.WeakFreeSwing`. Its contacts use the existing per-source/part
  episode deduplication and measured impulse curve with no multiplier.
- SWINGING publishes an immutable `SwingImpactContext(SwingEpoch, ReleasedCharge,
  ReleasedTick)`. The body retains that context through the architecture's one-tick contact
  observation delay and for no longer than `ContactObservationGraceTicks` (default `2`).
  `InteractionDamageComponent` copies the context into `AcceptedImpact`; it never asks
  `LastSwingCharge` from mutable controller state. `AcceptedImpact` is a 16-field positional
  `readonly record struct` consumed by `ImpactFeedbackPresenter`, several scenarios, and the
  journey runner, so widening it is a deliberate contract change — update every construction
  site rather than adding a parallel side-channel. The admission gate belongs immediately
  after `ApplyAcceptedImpact` computes a positive `pain` from `_curve.PainFor(...)`: that is
  the one point where "cannot score, pay, change mood, or trigger hit lag" is all still
  enforceable, and it sits after the existing zero-pain early return, so a graze naturally
  fails to consume the epoch with no extra branch.
- A pure `SwingImpactAdmission` scalar gate lets an epoch claim only its first
  **positive-pain** buddy impact. Once claimed, later parts remain physically collidable but
  cannot score, pay, change mood, or trigger another hit lag for that epoch. A zero-pain
  graze does not consume the attack. Tool swap, pointer exit, hard recovery, and despawn
  invalidate the context.

### 4.6 The swing itself — choreography (the heart of the feel)

Compass convention for a **rightward** swing (barrel direction; 0° = up, 90° = toward the
target, 180° = down; mirror all angles for leftward):

| Phase | Duration | Barrel angle | What it looks like |
|---|---|---|---|
| Charge lean | while charging | 0° → 325° | Bat tilts back over the rear shoulder, shaking harder and harder |
| Windup snap | 14 ticks (≈0.117 s) | 325° → 290° | The Brawl batter wind-up: one last pull **farther behind**, compressed because the charge already built the drama |
| Strike sweep | **derived**: 24 ticks (0 charge) → 7 ticks (full) | 290° → 180° → **90°** → 45° | Constant-ω plateau. The bat whips down-under-and-through like a rising baseball swing. Contact zone ≈ 140°–70°, where the tip is moving **up and toward** the target; the laboratory, not the drawing alone, must prove the resulting launch vector |
| Follow-through | `FollowThroughTicks` (10) | 45° → 20° | Ease-out tail, counted **separately** from `SweepDegrees`. Momentum wraps the bat over the front shoulder |
| Recovery | 42 ticks (0.35 s) | back to 0° | Servo settles to upright; charging locked out until GRIPPED again |

**Sweep ticks are derived, not authored.** `SweepDegrees` (245° = 290° → 45°) is the
constant-angular-velocity plateau, and the authored tip speed fixes how long it takes:

```
rTip        = Length - Radius                       // 83 px for the authored bat
omega       = tipSpeed / rTip                       // rad/s
sweepTicks  = round(radians(SweepDegrees) / omega * TicksPerSecond)
```

Uncharged: `1800/83 = 21.7 rad/s`, `4.276 rad / 21.7 = 0.197 s` → **24 ticks**.
Full charge: `6000/83 = 72.3 rad/s`, `4.276 / 72.3 = 0.0592 s` → **7 ticks**.

**7 ticks is not a typo, and it makes the contact window very thin.** The ≈140°–70° contact
zone is 70° of the 245° plateau, so at full charge the bat is inside it for roughly
`70/245 × 7 = 2.0` physics ticks. That is deliberate and matches the reference — Smash's
Home-Run Bat connects on a single frame — but it means the whole slice leans on CCD rather
than on having several ticks to notice a contact. Treat the point-blank and one-radius-offset
full-charge checks as load-bearing, not as edge cases, and if contacts are missed at full
charge, raise `SweepDegrees` or lower `TipSpeedFull` rather than quietly re-authoring the tick
count.

The earlier draft authored tip speeds *and* sweep ticks independently, which over-determined
the arc: with `SweepDegrees` fixed, tip speed is forced to scale as `1/sweepTicks`, so the
authored 24/11 tick pair demands a `24/11 = 2.18×` speed ratio while the current authored
speeds ask `6000/1800 = 3.33×` — a contradiction with no stated tie-breaker. Tip speed wins because it is the
quantity that produces the impulse, and the sacred rule is that charge must make the bat
really swing faster. `SweepTicksUncharged`/`SweepTicksFull` are therefore **removed** from the
profile; `SwingPlan` computes them, and validation asserts the derived values land within
`[MinimumSweepTicks, MaximumSweepTicks]` so an absurd authored tip speed cannot produce a
one-tick sweep.

Implementation mechanics:

- The body stays a **dynamic** `RigidBody2D` throughout. A dedicated bounded swing servo tracks
  both `SwingTrajectory.BarrelAngle` and its nonzero target angular velocity (PD plus bounded
  velocity feed-forward); reusing `AlignmentTorque` unchanged would damp the requested swing
  toward zero angular velocity. The handle tether holds the **latched release pivot**, not the
  moving cursor. **No kinematic teleporting and no authored collision impulse** — the solver
  measures the real momentum, so pain and knockback keep the sacred no-multiplier rule.
- **The swing phase needs its own tether force cap.** Holding the handle still while the bat
  rotates about it is a centripetal problem: the tether must supply `m · ω² · rCom`, where
  `rCom` is the handle-to-centre-of-mass distance (38 px for the authored bat). At full charge
  (`ω ≈ 72 rad/s`) that is `6 × 72.3² × 38 ≈ 1.19 × 10⁶` force units. The profile's authored
  `MaximumForce` is `120 000`, so the tether would saturate roughly 10× short, the "pivot" would
  be dragged bodily across the room, and the measured tip speed would never reach its target —
  silently invalidating every Task D envelope. `SwingAnchorForceCap` (§4.9, default
  `1 400 000`) governs the tether during SWINGING only; FOLLOW and GRIPPED keep the profile's
  ordinary `MaximumForce`. Note the uncharged swing already needs `6 × 21.7² × 38 ≈ 107 000`,
  which is inside today's `120 000` by under 12% — the existing cap was always marginal for a
  handle pivot, which is why this is a new authored value rather than a reuse.
- **Tip speed targets** (agent-tunable; Task 12 calibrates the economy side): uncharged
  ≈ 1 800 px/s, full charge ≈ 6 000 px/s, both measured at the barrel tip about the handle
  pivot. These are **not** comparable to the `2 400 px/s` cursor drag in today's `bat_swing`:
  that number is the translation speed of the bat's *centre* with no pivot, and the tip
  additionally carries whatever the alignment servo contributes, so today's effective tip
  speed is already above 2 400. Do not repeat the earlier draft's claim that uncharged "sits
  below the current benchmark swing" — it compares two different quantities. The only
  meaningful separation claim is the one Task D measures directly: non-overlapping
  pre-contact tip-speed envelopes between the weak free-swing and each charge band.
- **CCD:** set `ContinuousCd = CcdMode.CastShape` on the bat body during SWINGING (and back
  off afterwards). 6 000 px/s across a 120 Hz tick is 50 px — still more than the bat's
  width — so tunneling protection is mandatory, proven by point-blank and one-radius-offset
  full-charge scenario checks.
- A whiff is the same swing with nothing hit: same arc, same recovery, no consequences.
- Cursor motion during SWINGING does **not** move the pivot or change direction. This makes
  released charge the controlling variable and prevents a last-second pointer flick from
  overwhelming the charge curve.

### 4.7 Charged hit lag (the freeze) and victim shake

**Owner-confirmed 2026-07-30: the freeze stops the whole game, not just the bat and buddy.**
This overrides the SmashWiki reference in §2 (where unrelated actors keep moving) and it makes
the implementation *simpler*, not harder — see below.

Add a focused `SwingHitLagComponent`. It has no `_PhysicsProcess`; the composition root ticks
it in the established fixed order. `ImpactFeedbackPresenter` observes it but does not own
physics:

- **Suspend the routed physics tick wholesale.** When a qualifying impact lands, the root stops
  advancing the simulation for `HitLagTicks(releasedCharge)` (`6 → 60` routed ticks): no force
  routing, no constraint solving, no behavior or pain-timer advancement, for any body. The
  hit-lag counter, input collection, and presentation continue. Because nothing advances,
  nothing needs restoring — no per-body freeze-mode snapshot, no velocity capture, no
  transactional restore over a set that grows with every loose object. This replaces the
  earlier per-body freeze design outright, and deletes the trap it carried (a partially
  restored body).
- **Do not use `Engine.TimeScale`.** Suspending the root's own routed tick keeps the laboratory
  pause/step controls composable and avoids the `_resumeScale` mutation that
  `ImpactFeedbackPresenter` already performs for the glove. Two independent writers of
  `Engine.TimeScale` would fight.
- **What qualifies for the freeze** (owner-confirmed):
  - a scored **buddy** hit at *any* charge → freeze, duration scaled by released charge;
  - a **loose-object** hit at **full charge only** → freeze, at the full-charge duration;
  - a loose-object hit below full charge → no freeze, physics stays continuous.
  "Full charge" means the charge actually reached `MaxChargeTicks`, i.e. normalized charge
  `== 1.0` — the same condition that fires the tip glint, so what the player sees glimmer is
  exactly what earns the object freeze. **Assumption flagged:** if the owner wants a softer
  threshold (say ≥ 95%), that is a one-constant change, but the glint/freeze symmetry is worth
  keeping.
- Hit lag is non-stacking. A second contact cannot extend it, and the swing-epoch admission
  gate already prevents a second scored buddy hit. Pointer exit, hard recovery, tool swap, or
  tree exit cancels it and resumes the routed tick exactly once.
- **Interaction with the glove's existing hit-stop.** The Boxing Glove's 0.12 s
  maximum-pain/knockout `Engine.TimeScale` slow-time rule is unchanged, and bat *free*-swings
  still use it if they cross its normal threshold. But a full-charge home-run can trip both:
  it is a maximum-pain hit *and* a home-run epoch. The two must not compound — while a
  home-run freeze is active, `ImpactFeedbackPresenter` must not start its slow-time envelope,
  and the freeze wins. Assert this: `home_run_freeze_suppresses_the_global_slow_time`.
- **Victim shake:** during a swing hit-stop, the struck part's presentation jitters ±2 px at
  ~40 Hz, decaying over the freeze (SmashWiki: the victim shakes during hitlag). Presentation
  only — add a production `ImpactVisualOffsetComponent`. Do **not** use the scenario-only
  `SetDevelopmentOffset` seam; physics bodies stay untouched.
- **The shake needs its own offset lane (owner-confirmed 2026-07-30; see DECISIONS.md
  "Hit-Lag Shake Gets Its Own Offset Lane").** It must **not** join the existing composition
  in `BuddyVisualPresenter.ResolvePerformanceOffset`, which returns zero whenever
  `_performanceWeight <= 0` — and `PoseModeArbiter` forces Tracking (weight 0) for the whole
  post-impact window (`PostImpactCooldownTicks` = 60) and while the struck buddy is not
  stable standing. Routing the shake there would render nothing at exactly the moment it is
  needed. Instead add a second contributor lane that is **not gated by the performance
  weight**, still clamped through the same `BoundedOffset.Clamp` against
  `OffsetCapRadiusFraction * partRadius`, and summed into the part's final visual offset.
  This is safe precisely because the struck bodies are frozen during hit lag: there is no
  real motion for the jitter to misrepresent, and the stray-distance invariant is unchanged.
  The lane carries a comment at its definition naming that decision entry, so a later reader
  does not flag the missing weight gate as a defect. Only the hit-lag shake may use it; every
  other offset source keeps the weighted path.
- The shake is what keeps a whole-game freeze from reading as a hitch. With the owner's
  everything-stops rule there is no unrelated motion left on screen to prove the game is still
  alive, so the jitter is now load-bearing rather than decorative — do not defer it.

### 4.8 Looking 3D

- Replace the bat's capsule render body with a **lathed wooden bat mesh** built procedurally
  (`SurfaceTool` revolve, ~24 radial segments) in a new
  `src/Presentation3D/BatMeshBuilder.cs`: knob (r ≈ 5), wrapped handle (r ≈ 4), long taper,
  barrel (r ≤ collider `Radius`, currently 7), rounded tip; total height ≤ profile `Length`,
  aligned to the collider's long axis. Every mesh vertex must remain inside the capsule
  proxy, so a visible contact cannot precede the physical one.
- Material: `StandardMaterial3D`, **`PerPixel` shading**, wood albedo (warm tan ≈ the
  profile's existing `VisualColor`), roughness ≈ 0.7, and a **black handle** wrap from the knob
  up to the taper (owner-confirmed 2026-07-30) — authored as two profile colours,
  `VisualColor` for the wood and a new `GripColor` for the handle, so it stays data, not a
  hard-coded constant in the mesh builder; the existing
  shadowless `BuddyLookLightingRig` (key + fill) lights it — **no new lights**. Classic
  wooden bat look on purpose; do **not** copy Smash's black-and-gold trade dress.
- Plumbing: add a focused `CursorToolVisual3D` presenter around the existing dynamic slot.
  `CursorToolProfile` gains a `Visual3DKind` (`Capsule` default, `LathedBat`), and an internal
  `CursorToolVisualFactory` resolves mesh/material from that authored kind. `BuddyLab` and
  `SandboxRoot` pass the profile to the presenter and never branch on Bat or construct
  materials themselves. The glove keeps today's sphere path untouched.
- **`Body2DVisual3D` needs a real injection seam first.** Its `SetGeometry` constructs the
  mesh and forces `ShadingMode.Unshaded` internally, so the factory has nothing to hand it.
  Add an overload — `SetVisual(Mesh mesh, Material material, float depthOffset)` — that stores
  what it is given and leaves the existing scalar `SetGeometry` as the untouched default path
  for the glove and every other capsule/sphere consumer. Do **not** reach in through the
  public `Mesh` property from outside: the next `SetGeometry` call would silently clobber it,
  which is exactly the sort of order-dependent breakage the slot was written to avoid. Both
  root call sites move to the profile-passing form.
- The 2D debug `_Draw` capsule stays as-is (it depicts the collider, which is still a capsule).
- **Charge shake + glint** live on this visual: add `VisualOffset2D` to
  `IBody2DVisualPulseSource` (default zero) and apply it in `Body2DVisual3D._Process`;
  the controller feeds it `ShakeOffset(...)` while charging. The glint is a small additive
  unshaded star-quad (two crossed quads) parented at the **barrel tip**, scale-popping
  0 → 1 → 0 over ~0.35 s. The owner-feedback pass stages it at one, three, and five
  seconds (`7/12/18` px); only the five-second glint shares the `ChargeCompleted` edge.
  No particle systems, so nothing conflicts with the FR-017.3 reduced-effects settings that
  arrive with Task 7; when those settings land, the glint honors them.

- **Impact punctuation:** an accepted home-run epoch adds one compact `18` px, `0.20` s
  six-ray burst at the solver point. It is presentation-only and uses the existing focused
  impact presenter; the generic ring, whole-game freeze, and victim shake remain unchanged.

### 4.8b Placeholder audio (owner-confirmed 2026-07-30)

Audio is **in** this slice as a deliberate placeholder, not deferred:

- One `AudioStreamPlayer` owned by a focused `SwingAudioComponent`, fed by the same semantic
  edge events presentation already consumes: a charge-start tick, the `ChargeCompleted` glint
  edge, the swing release, and the scored home-run impact.
- **Generate the placeholder sounds procedurally** — a short synthesized crack/whoosh/ding
  written into an `AudioStreamWav` at startup, or an `AudioStreamGenerator` fill. Do **not**
  commit sampled audio pulled from anywhere, and specifically do not source anything
  resembling the reference game's "KREEENG": the point of a placeholder is to hold the seam
  open, and a borrowed sample creates a provenance problem that is far more expensive to undo
  than to avoid. Clean-room, same rule as the bat's visual.
- Mark it clearly as provisional in code and in `CHECKLIST.md` so the replacement pass has an
  obvious target. Volume authored on the profile; it must respect the existing audio bus
  layout rather than writing master volume.
- Not gated by the feel review — placeholder audio existing is the confirmed requirement;
  whether it *sounds* right is explicitly not a Task H acceptance criterion.

### 4.9 Authored data

New sub-resource `SwingToolProfile` (a `GameResource`, validated like everything else),
referenced from `CursorToolProfile` as `[Export] public SwingToolProfile? Swing` —
`null` means "not a charged-swing tool" (the glove authors none; no branch on tool names):

| Field | Default | Notes |
|---|---|---|
| `MaxChargeTicks` | 600 | exactly 5 s at the fixed 120 Hz tick — confirmed, not tunable |
| `LeanDegrees` | 35 | charge-pose lean away from the target |
| `WindupTicks` | 14 | post-release snap, ≈0.117 s at 120 Hz |
| `WindupDegrees` | 70 | barrel angle behind vertical at the top of the snap — where the strike sweep begins (290° in §4.6's compass). Added during Task A: §4.6's table implies it but the original table named no field for it, which would have left the sweep's start angle hard-coded. Must exceed `LeanDegrees`, or the snap would run the arc backwards |
| `SweepDegrees` | 245 | constant-ω plateau, windup end (290°) → 45°; **excludes** the follow-through tail |
| `FollowThroughDegrees` / `FollowThroughTicks` | 25 / 10 | the 45° → 20° ease-out wrap |
| `TipSpeedUncharged` / `TipSpeedFull` | 1 800 / 6 000 px/s | owner-feedback tuning; **sweep ticks derive from these** (§4.6) — 24 and 7 ticks for the authored bat |
| `MinimumSweepTicks` / `MaximumSweepTicks` | 5 / 60 | validation bound on the *derived* sweep so an absurd tip speed cannot produce a one-tick swing |
| `RecoveryTicks` | 42 | 0.35 s lockout + settle |
| `SwingAnchorForceCap` | 1 400 000 | tether authority during SWINGING only; ~1.17× the `m·ω²·rCom ≈ 1.19 × 10⁶` needed to hold the handle pivot at the owner-boosted full charge (§4.6). FOLLOW/GRIPPED keep the profile's `MaximumForce` |
| `FreeSwingAnchorSpeedCap` | 2 400 px/s | equals today's benchmark swing by construction, so the two green bat tests are unaffected; only *faster* input is bounded (§4.5) |
| `FreeSwingForceCap` | 120 000 | equals the bat profile's existing `MaximumForce` — FOLLOW behaviour is unchanged today (§4.5) |
| `ContactObservationGraceTicks` | 2 | covers the documented one-tick contact-report delay |
| `ShakeMaxAmplitudePx` | 3.5 | presentation shake at full charge |
| `ShakePrimaryHz` / `ShakeSecondaryHz` | 33 / 41 | incommensurate wobble |
| `GlintSeconds` / one-/three-/five-second sizes | 0.35 / 7 / 12 / 18 px | staged tip glimmers; only the cap emits `ChargeCompleted` |
| `DirectionTravelThreshold` | 6 px/tick | horizontal cursor travel that counts as "aiming that way" (§4.4); below it the previous sign persists |
| `GripColor` | near-black (`#141414`) | the owner-confirmed black handle wrap (§4.8) |
| `HitLagMinTicks` / `HitLagMaxTicks` | 6 / 60 | 0.05 / 0.50 s whole-game hit lag |
| `AudioVolumeDb` | −6 | placeholder swing/impact audio level (§4.8b); routed through the existing bus layout |
| `GripStiffness` / `GripDamping` | 900 000 / 120 000 | upright servo gains |
| `SwingAnchorStiffness` / `SwingAnchorDamping` | 240 000 / 1 000 | dedicated handle-pivot gains; the ordinary soft FOLLOW tether cannot reach the centripetal force demand inside the pivot-drift tolerance |
| `SwingServoStiffness` / `SwingServoDamping` | 50 000 / 120 000 | position-plus-velocity moving-trajectory gains; the modest position term corrects phase error while feed-forward velocity carries the fast plateau |
| `SwingTorqueCap` | 70 000 000 | strike-sweep authority includes cancellation of the pivot force's handle moment (up to `1 400 000 × 38 = 53 200 000`) plus net motor torque; a cap sized only for the motor cannot track the target once the off-centre tether saturates |

The grip anchor is **not** in this table: it derives from the collider as
`(0, +Length/2 − Radius)` (§4.4). There is no `HandleFraction`.

Validation: a `Swing` profile requires an elongated collider; every value finite/positive
range-checked in the established style; tick minima/maxima and uncharged/full relationships
must be ordered; `MaxChargeTicks` must equal the confirmed `600`; `TipSpeedFull` must exceed
`TipSpeedUncharged`; the **derived** sweep ticks for both charge endpoints must land inside
`[MinimumSweepTicks, MaximumSweepTicks]`; `SwingAnchorForceCap` must be at least the
`m · ω² · rCom` the full-charge tip speed implies, so a raised tip speed cannot silently
outrun its own tether; derived angular-velocity targets must remain finite.
`lab_cursor_tool_baseball_bat.tres` authors one with the defaults.
Except for the confirmed charge duration, values remain laboratory-tunable; economy-facing
consequences recalibrate in Task 12 as already planned.

### 4.10 Deliberately unchanged

One-collider-at-a-time; despawn/respawn on tool swap; attribution identity and
`AttributesContent`; **no pain/damage multiplier anywhere**; `ToolReactionComponent` stays
glove-only (recorded owner decision — do not extend to the bat in this slice); catalogue
entry stays `Visible = false` until the owner's feel gate. The glove path and non-bat cursor
tools remain byte-for-behavior unchanged. `bat_swing` and `m5_baseball_bat` retain their weak
free-swing purpose but gain upper-bound assertions so they cannot become a charged-mode
bypass; tool-feel reactions, look-at, pose-pipeline, and 3D-presentation regressions stay
green.

## 5. Owner gate and confirmed interaction policy

Confirmed:

1. LMB grips the handle; RMB while gripped charges; releasing RMB swings.
2. Charge strength and charge feedback cap at exactly 5 seconds (`600` routed ticks).
3. The existing cursor-driven free-swing remains as a separate weak physical attack.
4. Charged power changes real bat motion; no damage/payout multiplier is permitted.
5. The full-charge glimmer appears at the upper tip.
6. The hit-lag victim shake gets its own visual-offset lane, ungated by the pose pipeline's
   performance weight and still clamped by the existing per-part cap (2026-07-30; recorded in
   `docs/DECISIONS.md`). Not a weakening of the M3.6 rule — see §4.7.

The remaining interaction bundle was **resolved by the owner in full on 2026-07-30**. Four
answers changed the design rather than confirming it; those are marked ▲ and the affected
sections have been rewritten:

1. Releasing LMB mid-charge cancels to FOLLOW without swinging — a safe bail-out.
2. An RMB tap while gripped performs the minimum-charge home-run arc.
3. Holding RMB beyond 5 seconds retains full charge indefinitely until release.
4. ▲ **Direction comes from cursor travel alone, not from target proximity.** The bat swings
   the way the mouse is moving, so the swing is always "in front of" the cursor. The
   nearest-strikeable-target rule is withdrawn, and `SwingTargetResolver` is deleted from the
   design (§4.3, §4.4).
5. ▲ **The freeze stops every game element**, not just the bat and buddy. Implemented as a
   wholesale suspension of the root's routed physics tick, which is simpler than the per-body
   freeze it replaces (§4.7).
6. The post-release windup uses the provisional 14-tick snap.
7. ▲ **Clean-room classic wooden bat with a black handle wrap** (§4.8). Still no Smash
   black/gold trade dress.
8. ▲ **Placeholder audio ships in this slice**, procedurally generated, to be replaced later
   (§4.8b). The original "defer sound entirely" default is withdrawn.
9. ▲ **Loose-object freeze is full-charge-only.** A scored buddy hit freezes at any charge,
   duration scaled by charge. A loose object freezes only when the charge actually reached the
   cap — the same condition that fires the tip glint — and below that, object physics stays
   continuous (§4.7).

Two implementation readings were chosen where the answers did not fully specify; both are
flagged inline and are cheap to flip if they feel wrong:

- Swing direction **tracks through the charge and commits at RMB release**, rather than
  locking when charging starts (§4.4). Consequence: the charge lean flips sides if the player
  reverses mid-charge.
- "Full charge" for the object-freeze rule means normalized charge `== 1.0`, i.e. the charge
  reached `MaxChargeTicks` (§4.7).

Per the Planning Rule in `docs/DECISIONS.md`: anything beyond these defaults that turns out
to be product behavior requires a new owner decision rather than an implementation guess.

## 6. Implementation tasks (in order, each gated)

**Task A — Domain model.** §5 is fully confirmed (2026-07-30), so this starts now. Implement
`ChargedSwing.cs`, `SwingTrajectoryServo.cs`, and the full pure test suite (§4.2). No Godot
references.
*Accept:* all new tests green; current domain baseline untouched; exact `600`-tick cap and
swing-epoch admission proven.

**Task B — Composition, weak free-swing, grip, and admission plumbing.** Add
`ChargedSwingComponent`, `SwingToolProfile` validation/`.tres`, the
rate-limited FOLLOW anchor, handle anchor with rotated force offset, directed upright servo,
input routing, impact-context payload, and bounds helper. **Create the `homerun_bat_feel`
scenario and register it in `src/Testing/ScenarioCatalog.cs` here** — Tasks B through F all
name checks with no host scenario, and Task H runs `homerun_bat_feel` as if it already
existed; every check below and in Tasks C–E lands in this one scenario. Also add it to
`docs/TEST_PLAN.md` and, if it is frame-pacing sensitive, annotate the `--fixed-fps 120`
requirement. *Accept:*
`weak_free_swing_scores_positive_but_bounded_pain`,
`gripping_in_contact_scores_no_pain`,
`gripping_the_bat_holds_it_upright_by_the_handle` (barrel-up within 8° after settle and
handle-to-cursor error inside tolerance), `letting_go_returns_to_weak_follow`, and unchanged
glove-response envelopes.

**Task C — Charge, direction, shake, and glint.** Add CHARGING tick accrual/lean, cursor-travel
direction tracking with commit-at-release, `VisualOffset2D`, and the glint edge. *Accept:*
`charge_caps_on_tick_600_not_300` (`599/600/601` boundary),
`charge_shake_amplitude_ramps_and_caps_at_five_seconds`,
`charge_shows_small_medium_and_large_tip_glimmers`,
`dragging_right_then_left_swings_left` (direction follows cursor travel, and the *last*
significant travel before release is the one that counts),
`sub_threshold_jitter_does_not_flip_the_direction`,
`pointer_motion_after_release_cannot_change_pivot_direction_or_charge`,
`mirrored_drags_produce_mirrored_swings`, and
`releasing_the_grip_cancels_without_a_swing_or_pain`.

**Completed 2026-07-30, then revised by Task H owner feedback.** The cumulative
`homerun_bat_feel` scenario pins the checks above in both presentation modes. Charge wobble
is a render-only offset sourced from monotonic presentation time; exact `120/360/600`
milestones start strictly increasing timed stars at the geometric barrel tip in both the
legacy draw path and dynamic 3D slot. The physics body and collider remain untouched.

**Task D — Physical home-run swing and single-hit gate.** Add SWINGING/RECOVERY,
position-plus-velocity servo drive, latched pivot, charge-scaled trajectory, observation
grace, epoch admission, and CCD toggle. *Accept:*

- measured pre-contact barrel-tip speed falls inside non-overlapping low/mid/full envelopes,
  **and** reaches the §4.9 tip-speed targets within tolerance — a swing that lands inside its
  envelope only because every band came in slow proves separation while hiding a saturated
  tether, so assert the absolute target as well as the ordering;
- `the_handle_pivot_holds_through_a_full_charge_swing`: handle drift from the latched release
  pivot stays inside an authored tolerance for the whole sweep. This is the check that catches
  an under-sized `SwingAnchorForceCap`;
- low/mid/full measured impulses and post-hit whole-buddy travel increase by laboratory-set
  minimum ratios, not merely strict `>` comparisons;
- `weak_free_swing_cannot_match_full_charge_impulse` compares the retained Task B weak
  FOLLOW benchmark against the production full-charge swing now driven in this task;
- the controlled contact vector launches up-and-away inside an authored angle envelope;
- a controlled loose object travels farther at higher charge;
- one home-run epoch records exactly one positive-pain impact even when the bat crosses
  several buddy parts;
- a zero-pain graze does not consume the epoch;
- pointer motion after RMB release cannot alter pivot, direction, or the charge comparison;
- uncharged RMB tap stays modest; point-blank and one-radius-offset full charge never tunnel;
- a charged whiff followed by resting/recovery contact cannot reuse stale charge or score.

The tip/barrel/handle comparison is recorded as evidence. If the real solver does not make
the tip strongest, stop for an owner feel decision; do not add a hidden tip multiplier.

**Completed 2026-07-30.** The production position-plus-velocity servo compensates the
off-centre handle force before applying motor torque, holds the full-charge pivot inside
`12.8 px`, and after the owner-feedback speed increase reaches measured low/mid/full tip
speeds of approximately `1 697 / 3 530 / 5 065 px/s` against the
`1 800 / 3 914 / 6 000` targets. The controlled buddy probe records
`1 834 / 2 617 / 8 564` impulse and `79.4 / 89.2 / 125.1 px`
24-tick post-hit centre-of-mass travel. The full hit launches at `53.6°` up-and-away,
scores once across four distinct buddy parts, and survives both point-blank and one-radius
offset CCD probes. A physical zero-pain graze leaves its epoch available, and a full whiff
cannot reuse stale charge on later GRIPPED contact. The one-contact passive-object probe
travels `1 067 / 2 200 / 3 089 px` across charge bands. Its no-multiplier
tip/barrel/handle evidence is `3 089 / 2 808 / 1 335 px`, with “tip” defined as the
distal barrel sweet spot `70 px` from the handle on the collider-derived `83 px` lever;
the geometric end-cap point is a tangential graze, not the striking face.

**Task E — Whole-game hit lag and victim shake.** Add `SwingHitLagComponent` and
`ImpactVisualOffsetComponent` (§4.7). While hit lag is active the root does not advance the
routed physics tick at all — no force, constraint, behavior, or pain-timer routing for any
body — while the hit-lag counter, input collection, and presentation continue. *Accept:*
`charge_scales_hit_lag_ticks` proves exact profile endpoints and non-stacking;
`launch_velocity_resumes_after_hit_lag`; `every_loose_object_stops_during_hit_lag` (the
inverse of the earlier draft's check — the owner's rule is that nothing keeps moving);
`knockout_and_recovery_timers_do_not_burn_during_the_freeze`;
`full_charge_object_hit_freezes_but_partial_charge_does_not` (§4.7's charge rule, asserted at
both the cap and one tick under it);
`home_run_freeze_suppresses_the_global_slow_time` (the glove's `Engine.TimeScale` envelope
must not compound with the freeze); `cancel_resumes_the_tick_exactly_once`; and
`struck_part_shakes_during_freeze_only` through the production ungated offset lane — the
check must run while the pose pipeline holds Tracking (which it always will after an impact),
so a shake wired to the performance-weighted path fails it rather than passing vacuously.

**Completed 2026-07-30.** The focused root gate now withholds every routed gameplay tick
and disables the 2D physics server for exactly `6 → 60` engine physics frames, preserving
all body transforms and velocities without per-body snapshots. Buddy hits freeze at every
charge; loose objects freeze only at the exact full-charge endpoint, with the `599/600`
boundary pinned. The production impact-offset lane renders victim-only shake at a measured
`1.99 px` while the pose pipeline remains in Tracking, then returns to zero. The cumulative
scenario also proves non-stacking, timer hold, launch resume, unrelated-object hold,
single cancel/resume, and suppression of the existing global slow-time envelope. A live MCP
input pass reached `600` charge ticks through primary/secondary input and observed one
`60`-frame full-charge loose-object freeze.

**Task E2 — Placeholder audio.** Add `SwingAudioComponent` with procedurally generated
charge/release/impact/glint sounds (§4.8b). *Accept:* sounds fire on the semantic edges and
nowhere else; nothing sampled enters the repo; the component writes no master volume and
routes through the existing bus layout; a headless run emits no audio-device warnings.

**Completed 2026-07-30.** One focused component now synthesizes four deterministic mono
PCM clips once at startup and plays them through its single scene-authored
`AudioStreamPlayer`. The cumulative physical scenario observes exact semantic counts:
full charge plus impact is `(start, complete, release, impact) = (1,1,1,1)`, an RMB tap
omits only completion, and a charged whiff never emits the impact cue. It also pins four
generated `AudioStreamWav` resources, one owned player, the existing valid bus, the
profile-authored `−6 dB` level, and unchanged Master volume. The focused headless run
emitted no audio-device warning. These sounds remain explicitly provisional and sampled
audio remains out of the repository.

**Task F — Honest 3D bat.** Add `BatMeshBuilder`, `CursorToolVisual3D`, factory plumbing, and
PerPixel materials. *Accept:* extend `Presentation3DScenario`/look checks: every lathed vertex
lies inside the length/radius capsule envelope; bat uses the shadowless accepted rig; glove
still uses its sphere path; roots contain no Bat-specific branch; 2D collider draw and all
accepted physical outcomes are unchanged by presentation mode.

**Completed 2026-07-30.** The dynamic cursor-tool slot now has the explicit
`SetVisual(mesh, material, depth)` injection seam and a focused profile-driven presenter/factory.
`LathedBat` builds 24 radial segments with authored wood and black-wrap vertex colours under
one rough `PerPixel` material; the default glove route still calls the original scalar
sphere/capsule path. `presentation_3d` proves all `1,728` face vertices remain inside the
authoritative capsule, both packed colours survive, the accepted two-light rig stays
shadowless, and the root contains only one generically named dynamic slot. Both presentation
modes retain identical charged-bat physical telemetry. A live real-`K` preview confirmed the
rounded barrel, taper, black handle, and clean runtime log.

**Task G — Interactive verification and trace capture.** Launch through the configured Godot
MCP server, use real `K`/pointer/button input, and perform weak free-swing plus left/right
low/mid/full charged hits and a whiff. Inspect semantic state, physics telemetry, errors, and
scene wiring; capture screenshots and an input trace. Fix discrepancies before treating
automation as final. *Accept:* no warnings/NaNs/missing Resources; visible 5-second
shake/glint, whole-game freeze, recovery, and up-and-away launch match the scenario telemetry.

**Completed 2026-07-30.** Fresh live rigs covered weak free-swing and both directions at
released charge `0.0067 / 0.4983 / 1.0`. The charged hits latched the requested `+1/-1`
direction, created one epoch, emitted one impact cue, and measured `6 / 33 / 60` frozen
frames; full hits resumed into recovery, while the full charged whiff scored zero, requested
no freeze, and emitted `(start, complete, release, impact) = (1,1,1,0)`. The clean full-charge
capture observed tick `600`, one glint start, active source and Mii3D counterpart glints, and
the black-wrap/wood visual under the real `K`, pointer, primary, and secondary path. Fresh
runtime logs contained no errors, NaNs, or missing Resources. Raw screenshots and the
first-party input trace remain throwaway artifacts under `.artifacts/task-g/`, ready for
Task H hardening.

The visual pass found one real discrepancy: the asymmetric lathe had treated 3D local Y as
2D local Y, so the black handle appeared on the physical barrel/glint end. The builder now
applies the established 2D-Y-down → 3D-Y-up mapping. `presentation_3d` pins wood at the
mapped barrel extreme and grip at the mapped handle extreme, and `homerun_bat_feel` now
requires the Mii3D slot's glint as well as the source edge instead of the earlier permissive
OR. Both focused scenarios pass in Mii3D and legacy after the correction.

**Task H — Promote automation, documentation, and owner feel gate.** Harden the captured trace
into `tests/journeys/m5_homerun_bat.json` using semantic targets and real input (select `K`,
hold primary, hold secondary for exactly `600` routed ticks, release, assert one attributed
impact, whole-game freeze, resumed launch, and recovery). Keep `m5_baseball_bat` as the weak
free-swing journey with its new upper bound. Update `docs/DECISIONS.md`, `CHECKLIST.md`, and
`docs/TEST_PLAN.md`; run `tools/quick_validate.bat`, the domain suite, `homerun_bat_feel`,
presentation regressions, and both bat journeys at `--fixed-fps 120`. Then conduct the
side-by-side owner feel review. *Accept:* every automated gate is green and the owner accepts
the feel before the catalogue entry becomes visible.

**Task H completed and owner-accepted 2026-07-30.** The promoted
`m5_homerun_bat` journey uses the queued real input path and records
`charge=600/600`, `routed=600`, `epoch=1`, `impacts=1`, `freeze=(1,60)`, resumed
launch, and completed recovery. The 24-step quick suite (including the promoted
journey), all 940 domain tests, `homerun_bat_feel`, `bat_swing`, `presentation_3d`,
`presentation_look`, and both bat journeys pass at fixed 120 Hz; the focused
scenario and journey matrix passes in both Mii3D and legacy. After the focused
low-placement, stronger-launch, staged-glint, and contact-burst feedback pass, the
owner accepted the revised feel ("it's great"). The catalogue entry is now visible;
the decision is recorded in `docs/DECISIONS.md`.

## 7. Risks and traps for the implementing agent

- **Godot capsule axis:** the collider's long axis is local **Y**; the barrel is −Y when the
  bat is upright with the handle anchor at +Y. Keep the quarter-turn correction from
  `CursorToolController.ApplyAlignment` in mind everywhere.
- **Force-position coordinates:** `RigidBody2D.ApplyForce` expects a world-oriented offset
  from the **body origin** (not the centre of mass — they coincide here, but the docs say
  origin). Rotate the local handle point by the current body rotation.
- **Never author a duration and a speed for the same arc.** Sweep ticks are derived from tip
  speed (§4.6). If a future tuning pass wants a slower-looking swing, change `SweepDegrees` or
  the tip speed — reintroducing an authored `SweepTicks` recreates the exact contradiction
  this plan had to resolve, and nothing in the type system will catch it.
- **A handle pivot is a centripetal load, not just a rotation.** Holding the handle while the
  bat spins about it costs `m · ω² · rCom`, which at full charge is ~8× the profile's ordinary
  `MaximumForce`. If the measured tip speed comes in far under target and the bat visibly
  slides across the room during a swing, suspect a saturated tether before touching the torque
  servo — `SwingAnchorForceCap` is the knob.
- **Tip speed and cursor speed are different quantities.** Today's `bat_swing` drives the bat
  *centre* at 2 400 px/s with no pivot; the charged swing quotes the *tip* about a handle
  pivot. Never compare the two numbers directly to argue that one swing is stronger — measure
  both at the tip, which is what the Task D envelopes do.
- **Do not use `SymmetricError` for grip/swing targets** — the fold that stops the bat
  spinning 180° in FOLLOW would let it hang upside-down in GRIPPED and swing backwards in
  SWINGING. Unfolded error for directed states, folded only in FOLLOW.
- **Never move the physics body for presentation** (shake, glint, victim jitter are all
  visual-layer offsets). The hover-scores-no-pain check exists precisely to catch a shaking
  collider grinding pain out of contact jitter.
- **Charge/swing/hit-lag in routed 120 Hz ticks; presentation in usec** — matches the codebase split
  (`InteractionDamageComponent` ticks vs presenter `Time.GetTicksUsec`), keeps pause/step
  lab controls honest.
- **Scenario drives the API, not synthetic clicks** (except the journey, which uses the real
  input queue) — same convention the launcher and care tools established.
- The swing servo fighting the room walls: full-charge sweeps near a wall will clamp against
  `MaximumForce`/torque caps; that is fine (the bat thuds against the wall), but assert no
  NaN/instability in the point-blank scenario.
- **No mutable `LastSwingCharge` attribution.** Contact observation trails the solver by one
  120 Hz tick; carry the immutable epoch/charge context through the bounded grace window and
  copy it into the accepted event.
- **Local freeze restore is transactional.** Snapshot each body's prior freeze mode and
  velocities once, suppress the buddy/bat's routed simulation island, and restore once on
  expiry, cancellation, hard recovery, tool swap, or tree exit. Never mutate
  `Engine.TimeScale` for the home-run hit.
- **Weak means physically weak.** Do not enforce the free-swing distinction with a pain cap
  or multiplier. Tune the anchor/force envelope and prove the resulting impulse upper bound.
- **A cap that bites on an existing test is a regression, not a tightening.** Both bat tests
  assert `Pain > 0` at 2 400 px/s. Any free-swing cap below that silently pushes them toward
  the pain-curve floor, and they will fail intermittently before they fail honestly. §4.5
  fixes the defaults at today's values for this reason; changing them is a measured change.
- **The victim shake must not use the performance-weighted offset path.** It renders zero for
  the entire post-impact window. Use the ungated lane (§4.7, DECISIONS 2026-07-30), and write
  the check so it runs *while* the pipeline holds Tracking, or it will pass vacuously.

## 8. Sources

- [Home-Run Bat — SmashWiki](https://www.ssbwiki.com/Home-Run_Bat) — windup animation
  ("wind up like actual baseball players", "bring the bat slightly farther behind them just
  before hitting"), swing frame table (51/51 Ultimate), tipper/sourspot, up-and-away OHKO
  launch, KREEENG, item design history.
- [Smash attack — SmashWiki](https://www.ssbwiki.com/Smash_attack) — 60-frame charge, 1.4×,
  shake/flash while charging, extended hold in Ultimate.
- [Charge — SmashWiki](https://www.ssbwiki.com/Charge) — generic charge categories,
  Home-Run Bat fixed-windup exception, full-charge fighter-icon glint, and charge sounds.
- [Hitlag — SmashWiki](https://www.ssbwiki.com/Hitlag) — damage-scaled freeze, 30-frame cap,
  attacker/victim frozen while unrelated elements continue, victim shake, design purpose.
- [Home-Run Contest — SmashWiki](https://www.ssbwiki.com/Home-Run_Contest) — tipper
  distance, ten-second format.
