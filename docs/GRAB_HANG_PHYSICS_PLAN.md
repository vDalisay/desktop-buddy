# Grab Hang Physics — Fix Plan (owner feedback 2026-07-21)

Status: **V2 IMPLEMENTED AND OWNER-ACCEPTED (2026-07-22). See "V2 feel pass"
at the end for the accepted implementation.** The v1 sections below
are kept for context: the root-cause analysis and plumbing remain correct and
stay in place; only the torque model and airborne spring tuning change.

## Owner report

> "When I grab the buddy by its feet, I would expect the buddy to hang from its
> feet because that's how realistic physics work. Instead, the buddy stands
> upright while I lift him off the ground."

Expected: grabbing any part and lifting the buddy off support should make the
rest of the body **hang below the grab point** (grab a foot → buddy dangles
upside-down; grab a hand → body hangs sideways-then-below; grab the head →
body hangs below the head, which already looks correct today by coincidence).

## Root cause (verified in code, three interacting facts)

1. **Star topology with central anchors → zero torque path.** All five
   structural links in `data/buddy/lab_puppet_rig.tres` connect the torso
   (PartA) to a limb/head (PartB) with `LocalAnchorA`/`LocalAnchorB` left at
   their default `(0,0)` (body centers). `PuppetConstraintComponent.PhysicsTick`
   therefore applies every spring force through each body's center of mass —
   **no spring can ever produce torque on any body.** Gravity acting on the
   unheld parts has no physical way to rotate the assembly around the grab
   point.

2. **The rest-offset frame is the torso's rotation, and nothing rotates the
   torso while dangled.** `PuppetConstraintComponent.cs:67` rotates each
   `RestOffset` by `link.A.GlobalRotation` (torso). While dangled,
   `ActiveDriveComponent` correctly disables the upright torque
   (`ActiveDriveComponent.cs:111-120`), and per fact 1 the springs can't rotate
   the torso either. The torso's rotation stays ≈ 0, so the rest-offset frame
   stays frozen at world-upright.

3. **Airborne grab makes the springs 5× stiffer.** `BuddyRoot.cs:118` passes
   `airborneGrab: dangled` into the constraints, which applies
   `AirborneGrabStiffnessMultiplier = 5` / `AirborneGrabDampingMultiplier = 2`
   (deliberate, owner-accepted 2026-07-20: it lets the tether translate the
   whole puppet instead of stretching one part). Combined with facts 1–2, the
   rig is held **rigidly in the upright standing formation** in whatever frame
   the torso last had — so lifting by a foot produces a buddy standing at
   attention in mid-air under the cursor.

In short: the dangle branch already goes passive (correct intent, landed in
`OWNER_FEEDBACK_2026_07_20_FIX_PLAN.md` B3), but the passive structure has no
rotational degree of freedom, so "passive" collapses to "frozen upright."

History note: a pure limit-only mode (springs off, distance caps only) was
already tried and **rejected** ("let every unheld part slide to the lowest
permitted point"). Do not resurrect it. The fix must keep the springs and add
the missing rotation, not remove structure.

## Fix design — hang-alignment torque (recommended: Option 2)

Two viable designs were considered:

- **Option 1 — frame override:** while `airborneGrab`, rotate rest offsets in
  `PuppetConstraintComponent` by a computed hang angle instead of the torso
  rotation. Rejected as primary: it forks the rest-offset frame away from
  `torso.GlobalRotation`, which the link-error metric in `GrabDangleScenario`,
  the head face counter-rotation, and the 3D visual rotation all read. Every
  consumer would need frame awareness.

- **Option 2 (recommended) — bounded torso hang-alignment torque:** while
  dangled, apply a bounded torque that steers `torso.GlobalRotation` toward
  the hang angle implied by the grab geometry. The existing
  rest-offset-rotates-with-torso machinery then does everything else: the 5×
  springs carry the formation into the rotated frame, so the buddy swings and
  settles with its mass below the grab point. Every downstream consumer
  (link-error metric, face counter-rotation, visual rotation policy) follows
  automatically, and on release the existing upright torque + recovery rights
  the buddy with no new code. This torque is passive-structure *emulation* —
  it stands in for the torque the center-anchored springs physically cannot
  produce — not a new behavior.

### Hang angle definition (pure domain function — unit-testable)

Add a Godot-free function in `domain/DesktopBuddy.Domain/Physics/` (suggested
file `HangFrame.cs`), following the `PassiveSpring`/`GrabTether` pattern
(readonly record struct input/result, static `Evaluate`):

- Inputs: grabbed part id's **rest-frame** offset from the rig's rest-frame
  center of mass (`restDirection = restCOM - restPositionOfGrabbedPart`,
  computable from `PuppetRigProfile` parts' `RestPosition` + `Mass`), and the
  **actual** world offset (`actualDirection = worldCOM - grabAnchorWorld`).
- Output: `theta = Atan2(actualDirection) - Atan2(restDirection)`, wrapped to
  `[-π, π]`, plus a validity flag.
- Using COM (not the torso) makes the same formula work for **all six parts
  including the torso itself** (torso grab: restDirection ≈ small but nonzero
  since COM ≠ torso center; add an epsilon guard — if either vector length is
  below ~1 px, report invalid and the caller holds the previous angle / applies
  no alignment torque that tick).

### Runtime wiring

1. **Plumb grab info down.** `BuddyRoot.PhysicsTick(bool buddyPartGrabbed,
   bool headGrabbed)` gains the grabbed part identity and the grab world
   anchor (e.g. `BuddyPartId? grabbedPart, Vector2 grabWorldPoint`). Callers
   that route grabs (buddy lab / sandbox shell — find via callers of
   `BuddyRoot.PhysicsTick` and `GrabTetherController.CurrentGrab`) resolve the
   grabbed `PuppetPartBody.PartId` from `GrabTetherController.CurrentGrab`
   (`GrabWorld` is already in `GrabState`). Keep the old signature semantics:
   `buddyPartGrabbed == grabbedPart is not null`.

2. **Apply the torque in the dangled branch.** In
   `ActiveDriveComponent.PhysicsTick`, inside the existing
   `if (dangled)` early-return branch (`ActiveDriveComponent.cs:113-120`),
   before returning: compute `theta` via the domain function, then
   `torque = wrap(theta - torso.GlobalRotation) * HangAlignStiffness -
   torso.AngularVelocity * HangAlignDamping`, clamp to
   `±MaximumHangAlignTorque`, `torso.ApplyTorque(...)`. Expose a
   `LastHangAlignTorque` telemetry property like the existing `Last*` fields.
   Active outputs stay off; this is the only force the branch emits.

3. **Typed tuning.** Add `HangAlignStiffness`, `HangAlignDamping`,
   `MaximumHangAlignTorque` to `ActiveDriveProfile` with `Validate()` checks
   (finite, non-negative, max > 0) and values in `data/buddy/lab_active_drive.tres`.
   Starting guess: same order as the existing upright torque constants; tune in
   the lab until a foot-grab settles inverted in ~0.5–1.5 s with a small
   natural sway (per-part `LinearDamp 0.4` + 2× airborne damping already
   provide sway decay).

4. **Head-righting interaction.** While dangled the head upright torque is
   already off (whole branch returns early) — no conflict. On release,
   existing recovery/upright drives take over from whatever rotation the hang
   left; `RecoveryComponent` hard-reset remains the fail-safe.

### Visual layer checks (no code expected, verify only)

- Head face: `PuppetPartBody.FaceDrawRotation` already counter-rotates by
  `GlobalRotation` — an inverted head keeps its face upright. Confirm this is
  the desired look in the windowed owner pass (an upside-down buddy with a
  screen-upright face is a deliberate cartoon choice; the owner may instead
  want the face to rotate with the body — ask at the gate).
- 3D presentation: verify `VisualRotationPolicy` / `BuddyVisualPresenter`
  handle a torso rotation near ±π without lane/sort glitches (Mii3D mode).

## Test plan

- **Domain unit tests** (`tests/DesktopBuddy.Domain.Tests/Physics/HangFrameTests.cs`):
  head grab from standing → θ ≈ 0; foot grab with mass already inverted →
  θ ≈ ±π sign-consistent; torso grab valid; epsilon/degenerate inputs →
  invalid flag; wrap behavior at the ±π seam.
- **`grab_dangle` scenario:** the topology metric
  (`RestOffset.Rotated(partA.GlobalRotation)`) remains valid because the frame
  follows the torso — with the torso rotated ~π on a foot grab, the expected
  head offset lands below the torso, which is exactly the hang pose. Expect the
  24 px bound to still hold after settle (measurement starts at tick 90);
  re-tune bound or settle window only if the swing hasn't decayed.
- **New scenario `grab_hang_orientation`** (add to `ScenarioCatalog`): grab
  each foot (and one hand) from standing, raise cursor, wait for settle on
  routed ticks, assert semantic vertical ordering: grabbed part is the highest
  part; head `GlobalPosition.Y` > torso `Y` for a foot grab (screen-down
  positive = head below torso); torso rotation within tolerance of the domain
  θ; all bodies finite; release → `ActiveDrive.ActiveOutputsEnabled` resumes
  and standing recovers within the recovery budget. Headless-neutral, semantic
  asserts only, seeds via the salted-stream convention.
- **Regressions:** `grab_hold_aloft` (head grab — body already hangs below the
  head, should pass unchanged), `grab_release`, `grab_hard_recovery`,
  `standing_recovery` (known pre-existing red at 228 ticks — must not worsen),
  `knockout_window`, `autonomous_motion`, `idle_soak`. Seeds 1 and 7, both
  Mii3D and LegacyCircles where the convention applies.
- **Windowed owner gate:** interactive foot-grab in the lab on the real
  renderer — the buddy must *feel* like it flops and hangs. Record the face
  counter-rotation question (above) as an owner decision in `DECISIONS.md`.

## Verification commands

- Unit: `dotnet test`
- Scenario: `<godot_console> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<n>`
  (`<godot_console>` = `C:\Users\Home\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64_console.exe`)
- Quick suite: `devtools\verification\quick_validate.bat`
- Gotchas: close any `--editor` Godot instance first (headless deadlocks);
  `--fixed-fps 120` is mandatory for pacing-sensitive scenarios; wrap headless
  runs in a hard timeout.

## Invariants (carried from M3.6 / owner-feedback pass — binding)

1. 2D physics stays the sole authority; presentation never writes state. The
   hang is a bounded physics torque on the authoritative clock, never a visual
   rotation pretending to be one.
2. All new tuning is typed, exported, validated profile data — no magic
   numbers in components.
3. Scenario checks are semantic and measure in `RoutedTicks`, never rendered
   frames.
4. The passive-spring topology stays on during dangle; the rejected
   limit-only mode must not return.

---

# V2 feel pass — pendulum swing + loose limbs (owner feedback 2026-07-22)

**IMPLEMENTED — automated gates green; windowed owner feel gate accepted.** V1 landed (`HangFrame` domain function, grab
plumbing through `BuddyRoot.PhysicsTick(BuddyPartId?, Vector2)`,
`ApplyHangAlignment` in `ActiveDriveComponent`, `grab_hang_orientation`
scenario) and functions correctly, but the owner rejected the feel:

> "I should be able to swing the buddy like a pendulum but it feels too
> non-realistic. When I grab it by the feet, it seems to just slowly rotate
> the rest of the body around the grabbing point until it's down. Swinging it
> doesn't feel like the body is loose. It should be somewhat on par to when
> the buddy is unconscious where the physics mostly take over — but the body
> parts should be somewhat 'anchored' to their respective places instead of
> just sliding down as before."

## Why v1 feels wrong (diagnosis of the shipped code)

Two compounding causes, both in the shipped tuning/model — the architecture is
fine:

1. **The hang torque is an overdamped position servo, not gravity.**
   `ApplyHangAlignment` applies `torque = angleError * 8000 − ω * 800`
   (`HangAlignStiffness`/`HangAlignDamping` in `lab_active_drive.tres`).
   A linear PD with that damping converges monotonically to the target angle
   at controller speed — no momentum, no overshoot, no swing. That IS the
   observed "slowly rotates the body around the grab point until it's down."
   A real hanging body is driven by gravity torque `−m·g·L·sin(θ)` with only
   light air damping: it overshoots, oscillates, and responds to being swung.

2. **The 5× airborne spring multiplier welds the formation rigid.**
   `AirborneGrabStiffnessMultiplier = 5` / damping `2` (in
   `lab_puppet_rig.tres`, applied by `PuppetConstraintComponent`) locks every
   part to its frame slot. Consequences: (a) limbs cannot lag or flop — the
   buddy translates as one rigid statue when the cursor swings, which is the
   "body is not loose" complaint; (b) the COM is rigidly attached to the
   frame, so the servo's own target (`HangFrame` angle from COM) barely moves
   independently — there is no pendulum *state* left to swing.

The owner's reference point is exactly the unconscious floor ragdoll — which
runs these same springs at **1×**. The 5× boost was added (2026-07-20) so the
tether would translate the whole puppet instead of stretching one part; with
v1's rotation frame now in place, the distance caps (`MaximumDistance` +
`LimitStiffness`) can carry that job instead, which is also more realistic
(the held limb goes taut first, then the body follows).

## V2 design — three coordinated changes

### Change 1 — gravity pendulum torque replaces the PD servo

Replace the linear PD in `ApplyHangAlignment` with the physical pendulum
restoring torque:

- `angleError = WrapAngle(hangAngle − torso.GlobalRotation)` (unchanged
  computation, reuse `HangFrame`).
- `torque = HangGravityGain * totalMass * |actualDirection| * sin(angleError)
  − torso.AngularVelocity * HangSwingDamping`, clamped to
  `±MaximumHangAlignTorque` as today. `|actualDirection|` = pixel distance
  COM↔grab anchor (the pendulum arm length, already computed in
  `ApplyHangAlignment`).
- The `sin(angleError)` (not linear error) and a **small** damping are the
  point: target several visible overshoots decaying over ~2–4 s (≈ 5–15 % of
  critical damping). The torso body's own `AngularDamp = 1.5` already
  contributes background decay — tune `HangSwingDamping` down accordingly,
  possibly to 0.
- Put the pendulum torque math in the domain (extend
  `domain/DesktopBuddy.Domain/Physics/HangFrame.cs` or a sibling
  `PendulumTorque.cs`, same readonly-record pattern) so it unit-tests without
  Godot: zero torque at equilibrium, maximum near 90°, restoring sign both
  sides, wrap seam at ±π, clamp behavior, degenerate-input guards.
- Profile: replace `HangAlignStiffness`/`HangAlignDamping` with
  `HangGravityGain`/`HangSwingDamping` (typed, validated, in
  `ActiveDriveProfile` + `lab_active_drive.tres`). Keep
  `MaximumHangAlignTorque`. Starting guess for gain: Godot default gravity is
  980 px/s²; effective torque must move the whole assembly through the
  springs, so expect the tuned value above the naive `g` — tune in the lab
  until a foot-grab swings with a visibly decaying pendulum.

### Change 2 — unconscious-par springs while dangled

Drop the airborne structural multipliers toward the unconscious baseline:
`AirborneGrabStiffnessMultiplier 5 → ~1.25` and
`AirborneGrabDampingMultiplier 2 → ~1` (tune; typed values in
`lab_puppet_rig.tres`, validation floor is 1.0). Effects:

- Limbs genuinely lag and flop under gravity and momentum — "loose."
- The COM can move relative to the grabbed part, so the pendulum has real
  state and cursor swings pump it.
- "Anchored, not sliding": the springs stay ON at ~1× (unconscious-par, the
  owner's stated reference) and the per-link `MaximumDistance` +
  `LimitStiffness` caps remain the hard backstop. This is NOT the rejected
  limit-only mode — that mode turned the springs off entirely.
- Watch for the regression the 5× originally fixed: hoisting by one part
  stretching that link ugly before the body follows. The caps make the link go
  taut and drag the body — acceptable and realistic. If the stretch still
  reads badly in the windowed pass, split the difference (≤ 2×) rather than
  returning to 5.

### Change 3 — keep everything else from v1

Plumbing (`BuddyRoot` signature, grabbed-part resolution in the shells),
passive gating (all other active outputs off while dangled), telemetry
(`LastHangAlignTorque`), head-disturb notify, and release→recovery flow are
correct and unchanged. No visual-layer code; face counter-rotation question
from v1 still goes to the owner gate.

## Test plan (v2)

- **Domain unit tests:** pendulum torque function as listed in Change 1.
  Existing `HangFrameTests` stay.
- **`grab_hang_orientation` (update):** an underdamped hang settles later and
  crosses the target — relax `LatestSettleTick` (180 → ~420) and
  `StableSettleTicks` as needed, and ADD an overshoot assert: torso angle
  error must change sign at least once before settling (pins underdamped
  behavior so a future retune back to a servo fails the scenario). Keep all
  existing semantic asserts (grabbed part highest, mass-frame alignment,
  passivity, finiteness, recovery).
- **New scenario `grab_swing_pendulum`** (add to `ScenarioCatalog`): grab a
  foot, lift clear of support, let it part-settle, then oscillate the cursor
  horizontally (sine, ~1 Hz, ~60 px amplitude) for a few seconds on routed
  ticks. Semantic asserts:
  1. COM horizontal swing amplitude ≥ a fraction of cursor amplitude (body
     follows the swing);
  2. COM motion lags the cursor (peak cross-correlation at a nonzero tick
     lag) — proves loose coupling, not a rigid statue;
  3. every link separation stays below `MaximumDistance` plus a small margin
     the whole time (anchored — the old slide bug cannot return);
  4. link separations *do* transiently exceed a small epsilon (proves the
     formation actually flexes);
  5. all bodies finite, drive stays passive, release recovers standing.
- **`grab_dangle` (recalibrate):** the 24 px `MaximumAcceptedLinkError` was
  measured against 5× springs; 1.25× sags more. Re-measure the settled error
  and set the bound to measured + margin. The bound's job is catching
  limit-slide, not pinning stiffness.
- **Regressions:** `grab_hold_aloft` (looser springs stretch more when
  hoisting by the head — clearance assert must still pass), `grab_release`,
  `grab_hard_recovery`, `standing_recovery` (pre-existing red must not
  worsen), `knockout_window`, `autonomous_motion`, `idle_soak`. Seeds 1 and
  7, both Mii3D and LegacyCircles where the convention applies.
- **Windowed owner gate (the actual acceptance):** grab the feet, whip the
  cursor side to side — expect a floppy pendulum with limb lag, on par with
  the unconscious floor ragdoll but held together at the sockets. Also
  re-ask the v1 face-orientation question. Record accepted tuning values and
  both owner decisions in `DECISIONS.md`.

Verification commands and gotchas: same as the v1 section above.
