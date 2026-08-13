# Milestone 1 Feel & Gait — Agent Handoff Plan

Remediates the owner's first hands-on feel review (2026-07-12, windowed lab session),
which **rejected** the current tuning against the `TEST_PLAN.md` §8 bullet
"side-by-side reference review accepts responsiveness, bounded stretch, whole-body
impulse propagation, sideways knockout, and recovery feel."

Owner verdict, verbatim intent:

> "Everything works but doesn't feel right yet. I'm not able to pick up the buddy as
> easily, I should be able to hang him up in the air but it feels too heavy. This is
> why I also cannot really fling him. I can make him fall and I can see the buddy
> recover, but it looks visually sluggish and slow. The buddy should feel more alive.
> … Right now the buddy is just sliding around, while it needs to actually use the
> feet to move around."

References named by the owner: *Interactive Buddy* v1.01 (already the canonical
reference, `docs/REFERENCE_RESEARCH.md` §1) and *People Playground* (new secondary
"aliveness" reference — observation only, clean-room rules apply).

Read before starting: `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` §1, §3, §5, §6, §11–12;
`docs/REFERENCE_RESEARCH.md` §2, §7; `docs/M1_REVIEW_FIXES_PLAN.md` ground rules;
`CHECKLIST.md`.

## 0. Why "IK" is not the mechanism (read this first)

The owner asked for inverse kinematics for walking/throwing. Applying the constraint
docs to that request:

- The rig is **exactly six circles**; limbs are presentation connectors, not bodies
  (`RAGDOLL_AND_GAMEPLAY_SPEC.md` §3.1). Articulated IK chains need joint hierarchies
  we deliberately do not have, and adding hidden bodies is forbidden without owner
  approval. The spec also bans becoming "a realistic multi-bone ragdoll" (§1).
- The verified v1.01 mechanism (`REFERENCE_RESEARCH.md` §2) is: satellites
  spring-driven toward **rotating offsets** around the torso; walking = "horizontal
  whole-body impulses plus **oscillating leg impulses**"; jumping = upward impulses
  across the body. The original's lively walk is an *animated-target* effect, not IK.
- Spec §3.3 explicitly allows the active drive to apply "**limb-target** or
  grip-support forces."

Therefore this plan implements the owner's requested *look* with the reference's
actual *mechanism*: a phase-driven gait that moves per-foot **step targets** (lift,
swing, plant, traction) and drives the feet toward them with bounded forces. That is
the 1-link degenerate case of IK — target-driven end-effector placement — with zero
new bodies, zero joints, and full physics authority. If the owner later wants
articulated limbs (People Playground-style segmented arms/legs), that is a **rig
change requiring an explicit owner decision** and is out of scope here.

## 1. Diagnosis — measured, not guessed

Numbers from the tree at plan time (`data/buddy/*.tres`, gravity = Godot default
980 px/s²; total rig mass = 1.4+2.5+0.7+0.7+1.0+1.0 = **7.3**):

| Symptom | Root cause | Evidence |
| --- | --- | --- |
| "Too heavy, can't hang him up" | Tether cannot exceed body weight | Rig weight = 7.3 × 980 ≈ **7,154**. `lab_grab_tether.tres` `MaximumForce = 6000`. Grabbing any part hangs the whole rig through the links (link force caps 14k–20k are fine), so the tether saturates below weight and the buddy sags to the floor. |
| "Cannot really fling him" | Tether too soft to whip the mass; global damping eats what speed it does gain | `Stiffness = 220` closes only ~27 px of gap at force cap; `Damping = 20` opposes fast relative cursor motion; every part carries `LinearDamp = 2.0` (Godot default 0.1), so gained speed halves in fractions of a second. `ThrowSpeedCap = 900` is likely fine — the body just never reaches it. |
| "Visually sluggish and slow… should feel more alive" | Honey-damped world + weak early recovery assistance | `LinearDamp 2.0` / `AngularDamp 4.0` on all six parts damp every fall, bounce, and reaction. Recovery assistance ramps 0→1 over 5 s (`RecoveryClock.AssistanceRampTicks = 600`) multiplying `SelfRightForce 2400`, so the first ~2 s of assisted recovery are feeble. |
| "Just sliding around… needs to actually use the feet" | Locomotion is a distributed whole-body push; gait term is invisible | `ApplyLocomotion` spreads `WalkForce 600` across all parts by mass and adds only a ±`GaitForce 100` **vertical** alternation on the feet — no forward swing, no stance traction, no lift height. 100 force on a 1.0-mass foot under 980 gravity and a 190-stiffness link is imperceptible. |
| (secondary) jump reads as a "pop" | Single whole-body impulse, no anticipation | `JumpImpulse 1800` → Δv ≈ 246 px/s → ~27–31 px rise (scenario-measured 26.76), applied in one tick with no crouch. |

All of these are provisional lab values the spec *requires* us to establish
empirically (§1 list, §11.2) — nothing here changes an approved constant. The
recovery **timings** (2 s assist delay / ≤5 s ramp / 10 s hard floor) are
spec-locked; the ramp may be made *faster* ("over no more than 5 seconds") but the
delays may not move.

## 2. Feel targets (owner-derived, encode as assertions)

Translate the review into testable statements. Each becomes a scenario assertion in
Task 5 with a tolerance band measured during tuning:

1. **Hold-aloft:** grab the head, move the cursor to the room's upper third, hold
   2 s → every part's Y above the floor by ≥ its radius; tether force not saturated
   in steady state.
2. **Fling:** scripted 0.3 s swipe (≥ 1,500 px/s cursor speed) then release →
   release speed ≥ 60% of `ThrowSpeedCap`, and the buddy crosses ≥ half the room
   before first floor contact.
3. **Responsive fall:** knocked sideways from standing, torso peak speed reaches
   ≥ 300 px/s (no honey), and the rig visibly tumbles (angular motion, not a slide).
4. **Alive recovery:** from a limp side pose, buddy returns to stable standing in
   ≤ 3 s once assistance starts (assistance start stays spec-locked at 2 s).
5. **Stepping walk:** during autonomous walk, feet alternate support (left/right
   support contacts alternate), the swing foot clears the floor by a visible lift
   each cycle, and torso bobs vertically with the cycle. No perceptible sliding
   while both feet are planted.
6. **Determinism preserved:** all existing seeded scenarios stay green with
   re-measured envelopes; same-seed repeat spread stays within its bound.

Numbers 1–5 are initial proposals — the owner accepts/adjusts them during the Task 6
review, then they freeze into the locked tuning resource + regression fixtures.

## 3. Tasks

Order follows the spec's tuning ladder (§11.2): body/damping first, then links,
then drive/gait, then grab — so later layers never compensate for unstable earlier
layers. Ground rules from `M1_REVIEW_FIXES_PLAN.md` apply verbatim (single routed
tick, domain-pure logic + xUnit, no tick-path allocations, tolerance bands,
suite-green-before-done, measured numbers in commit messages).

### Task 1 — De-honey the world (damping & responsiveness pass)

**Change:** in `lab_puppet_rig.tres`, drop per-part `LinearDamp` 2.0 → start at
**0.3** and `AngularDamp` 4.0 → start at **1.5**. Compensate stability at the link
level, not the world level: if the rig oscillates after settling, raise per-link
`Damping` (currently 15–20) until the passive-settle scenario is critically damped —
energy control belongs in the springs (spec §3.2 "damping … opposing motion rather
than adding energy"), not in global drag.

**Expect:** every downstream number changes — settle ticks, walk speed at the same
force, jump rise, grab extensions, envelope bounds. That is why this task is first.

**Verify:** `passive_rig`, `standing_recovery`, `repeat_envelope` (bounds will need
re-measurement — record old→new in the commit), `idle_soak_ci` seeds 1/7. Record
before/after wall observations in the dual lab (Task 6 workflow) for the owner.

### Task 2 — Grab authority (hold, dangle, fling)

**Change** `lab_grab_tether.tres`:

- `MaximumForce` 6000 → start at **18,000** (≈2.5× rig weight; enough to hold aloft
  with margin and to accelerate the mass during a swipe).
- `Stiffness` 220 → start at **700** (gap-closing authority; at 18k cap the
  saturation extension becomes ~26 px, similar to today's, so bounded-stretch
  behavior remains familiar).
- `Damping` 20 → retune toward critical for the new stiffness (start ~35; the domain
  PD is unit-tested, so add a domain test pinning "cursor step response overshoot
  ≤ X%" once values settle).
- `ThrowSpeedCap` 900: leave until fling-feel review; raise only if the owner wants
  faster throws after the body can actually reach the cap.
- Re-proportion `GrabResistanceForce` (3500, `lab_active_drive.tres`) after the new
  tether max so full-fear resistance still *visibly stretches but never dominates*
  (spec §6). Re-measure `grab_resistance` bounds.

**Note:** `FearLevel` defaults to 0, so resistance is *not* part of the heaviness
bug — do not "fix" it as such; only re-proportion it.

**Verify:** `grab_release`, `grab_resistance`, `grab_hard_recovery` with re-measured
bands; new hold-aloft + fling assertions (Task 5) pass; manual dangle/fling in the
lab feels right to the owner (Task 6).

### Task 3 — Stepping gait (the "use the feet" ask)

**New domain type `GaitCycle`** (pure, `DesktopBuddy.Domain`, unit-tested): given
cycle phase [0,1), walk direction, and a gait profile, return per-foot data —

- swing-foot **target offset** relative to its rest offset (forward `StepLength`,
  up `StepLift`, following a half-sine swing arc),
- stance-foot **traction force scale** (backward push against the ground, which
  propels the body through friction instead of an abstract whole-body shove),
- torso bob offset and forward lean scale.

Feet alternate roles each half cycle. Pure math: no Godot types, fully covered by
xUnit tests (phase → target continuity, alternation, zero at idle).

**`ActiveDriveComponent.ApplyLocomotion` rework:** replace the ±100 vertical wiggle
with: (a) drive each foot toward its `GaitCycle` target with a bounded
spring-toward-target force (this is the spec-allowed "limb-target force"); (b) apply
stance traction at the planted foot only while it has a support contact; (c) shrink
the direct whole-body `WalkForce` component to a small assist (start ~30% of
today's) so most motion visibly comes from the feet; (d) apply the torso bob/lean as
a small force, never a transform. All new coefficients live in
`ActiveDriveProfile` (`StepLength`, `StepLift`, `StepDriveStiffness`,
`StanceTraction`, `GaitCycleTicks`, `TorsoBobForce`, `TorsoLeanScale`) with
validation; no literals in the component (spec §1).

**Jump anticipation (small, same family):** split the jump into a short crouch
(feet driven under torso, torso dips — reuse the step-target machinery) for
`JumpCrouchTicks` (~12–18), then the existing whole-body impulse. Retune
`JumpImpulse` upward after Task 1's damping change; target a rise the owner accepts
(reference: v1.01 jumps read as ~0.5–1 torso height).

**Autonomy unchanged:** `AutonomousMotionPlanner` still decides idle/walk/jump and
directions from the seed; the gait only changes how intent becomes force. Planner
tests untouched.

**Verify:** `autonomous_motion` extended with step assertions — feet alternate
support contacts during walk, swing-foot peak clearance ≥ `StepLift × 0.5`, walk
covers ≥ the current distance band; all existing checks stay green on seeds 1/7.
Watch for a regression I hit this session: measurement windows must span a full gait
cycle, not sample a single tick.

### Task 4 — Recovery snap

With Task 1's damping fix, falls and knockdown reactions speed up for free. For the
recovery itself:

- `RecoveryClock.AssistanceRampTicks` 600 → **240** (full assistance in 2 s;
  spec-legal — §5 says "over no more than 5 seconds"; the 2 s start delay and 10 s
  hard floor are untouched). Domain constant + its unit tests update together.
- Raise `SelfRightForce` (2400 → retune, start ~3600) and re-verify the assisted
  path rights the buddy without launching it (`standing_recovery` bands).
- Re-check `UprightStiffness`/`UprightDamping` (900/140) after damping changes; the
  torso should track upright snappily without ringing.

**Verify:** `standing_recovery` + feel target 4 (stand within ≤3 s of assistance
start, measured from a seeded side-pose fixture); `grab_hard_recovery` unchanged
semantics; soak still hard-recovery-free.

### Task 5 — Feel targets become regression fixtures

Add/extend scenarios so the accepted feel is pinned (spec §11.2 "each accepted
profile becomes a versioned resource plus a seeded regression fixture"):

- `grab_hold_aloft` (new): scripted grab + raise + 2 s hold → all parts off floor,
  steady-state force below cap (target 1).
- `grab_release`: add the scripted-swipe fling check (target 2) alongside the
  existing extension/cap checks.
- `standing_recovery`: add the ≤3 s-from-assistance-start bound (target 4).
- `autonomous_motion`: step-alternation + foot-clearance checks (target 5).
- Re-measure and tighten `lab_envelope_bounds.tres` for the new dynamics
  (same-seed spread must stay tight; cross-seed bound re-measured per the
  repeat_envelope procedure — worst observed × ~1.75, documented in the commit).

Every changed band gets the bound-pinch non-vacuity test treatment (set bound below
observed once, watch it fail, restore).

### Task 6 — Owner A/B loop, then lock the tuning

The dual-profile lab exists for exactly this (`--profile-a/b`, `--drive-a/b`,
Tab-swap grab):

1. Keep the current provisional profiles as **A** (baseline); build candidates as
   **B** copies (`data/buddy/candidate_*.tres`, export-excluded).
2. Owner sessions: `devtools\play_buddy_lab.bat --dual` with A vs B; owner exercises
   dangle, fling, knockdown, recovery, walk; agent records verdicts per behavior
   (`REFERENCE_RESEARCH.md` §7 workflow, side-by-side with the v1.01 reference where
   useful).
3. Iterate T1–T4 values until the owner accepts each behavior.
4. On acceptance: candidate values replace the `lab_*.tres` provisional profiles,
   `resource_name` drops "Provisional", `CHECKLIST.md` §8 review bullet flips, and
   the ROADMAP "lock an initial accepted tuning Resource" exit criterion is met.

**This task is owner-in-the-loop by definition** — the agent prepares builds and
records outcomes; the owner's judgment is the gate.

## 4. Out of scope / explicitly deferred

- Articulated limbs, extra rigid bodies, skeletal IK chains, joint motors — all
  forbidden without a separate owner decision (spec §1, §3.1; motor exclusion
  rationale in `REFERENCE_RESEARCH.md` §5).
- Throw *animations* (wind-up poses for buddy-thrown objects): the catch/hold/toss
  object pipeline is later M1/M3 work; the gait's step-target machinery is designed
  to be reused for it (limb-target forces are generic), but do not build it now.
- Mood-driven fear values, face/expression juice — M3/M4 per roadmap.
- Idle micro-motion (breathing bob, cursor-following head): cheap and high-"alive"
  value, but it is new product behavior not in the decision log — **ask the owner**
  before adding even to the lab.

## 5. Known interactions & risks

- **Every tuning change invalidates measured bands.** Tasks are ordered so bands are
  re-measured once per layer, not repeatedly. Expect `grab_release` extension
  values, `repeat_envelope` bounds, and settle-tick windows to change in Tasks 1–4;
  re-record deliberately, never loosen a bound just to go green without recording
  the measured basis.
- **Windowed journey runs currently hang** (discovered this session: windowed
  `--journey` runs compose the lab but never write a verdict or quit; headless is
  fine). Filed as a separate follow-up — do not block feel work on it, but manual
  windowed verification is the owner path until fixed.
- **Determinism:** gait adds no RNG (phase from tick count, direction from the
  seeded planner), so seeded scenarios stay deterministic. The same-seed
  repeat-envelope check is the guard.
- **Zoom/room floor:** step targets are in rig-local units and scale with the
  existing zoom policy; `room_resize_zoom` must stay green (it manipulates the
  canvas transform the pointer also uses).

## 6. Exit checklist

- [ ] Damping pass done; world reads snappy; all scenarios green with re-measured
      bounds recorded old→new.
- [ ] Grab: hold-aloft and fling fixtures green; owner confirms dangle/fling feel.
- [ ] Gait: `GaitCycle` domain type + tests; feet visibly step (alternation +
      clearance fixtures green); sliding gone to the owner's eye.
- [ ] Recovery: ≤3 s assisted stand fixture green; ramp constants updated within
      spec limits.
- [ ] Jump anticipation in; rise accepted by owner.
- [ ] `repeat_envelope`/`idle_soak_ci` re-baselined, seeds 1/7/42 green, non-vacuity
      pinch demonstrated for every new/changed bound.
- [ ] Owner A/B review accepts all five behaviors → provisional profiles promoted to
      accepted (rename, lock), `CHECKLIST.md` + memory updated.
- [ ] `TEST_PLAN.md` §8 side-by-side bullet marked satisfied by the owner, not the
      agent.
