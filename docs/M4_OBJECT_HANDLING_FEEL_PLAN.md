# M4 — Object Handling Feel Plan

Status: **COMPLETE 2026-07-26** (see Progress) — owner feel corrections from hands-on play.
Follows `docs/M4_REVIEW_FIXES_PLAN.md`. Owner instructions, not engineering choices.

## Owner findings

1. **Obstacle detection still fails intermittently.** The buddy walks into a ball and
   keeps pushing instead of hopping.
2. **The pickup is nightmare fuel.** Screenshot evidence: both arms stretched most of
   the room's width toward a distant ball.
3. **Required feel instead:**
   - the ball must be *within reach* before the buddy reaches for it;
   - hands may extend only **minimally**;
   - when the ball **touches a hand it sticks** — that is the catch;
   - then a **slight forward hand motion throws it back toward the player's cursor**;
   - to pick something off the ground: walk to it, do a **slight scoop** (body and hands
     lower a little), then the object **relocates directly to the hand and sticks**.

## Root causes

**The stretch.** `ObjectInteractionComponent.BuildCatchCommand` targets each hand at the
*object's own position* ± clearance, and `ActiveDriveComponent.ApplyObjectHandReach`
springs the hands there with `MaximumHandForce = 18000` through elastic arm links. Nothing
bounds the target to arm's length.

**Why it fires at absurd range.** `BuildCandidates` sets `ObjectCandidate.Distance` to
`Mathf.Abs(offsetX)` — a *horizontal* distance. `CatchDistance = 46` therefore admits an
object 46 px sideways and arbitrarily far above or below, which is exactly the diagonal
reach in the screenshot.

**The spring capture.** `ApplyObjectBodyCommand` also springs the *object* toward the hold
centre, so the object flies to the buddy rather than the buddy catching the object. That is
the "floaty" half of the nightmare.

**Detection.** `RayCast2D.HitFromInside` defaults to `false`. Once the buddy is touching the
ball, the probe origin (`torso + 64 px`) can sit *inside* the ball's circle, so the ray
reports nothing and the hop gate never opens — intermittent by construction, depending on
exactly how close the walk cycle stops.

## Design

### Task 1 — Real reach, minimal extension

- `ObjectCandidate.Distance` becomes a true 2D distance from the buddy's **reach origin**
  (torso + `ReachOriginOffset`). `Direction` stays `sign(dx)`. `CatchDistance` becomes a
  genuine reach radius; `ApproachDistance` a genuine approach radius.
- New `ReachRadius` (default `44`, just past the `38 px` natural hand rest offset) and
  `MaximumReachExtension` (default `6`). Every hand target is **clamped** into a circle of
  `ReachRadius + MaximumReachExtension` around the reach origin, so no command can ever ask
  for more than a minimal extension however far away the object is.
- `CatchDistance` is validated `<= ReachRadius + MaximumReachExtension`, so the machine can
  never commit to a catch it cannot physically reach.

### Task 2 — Contact catch and sticky hold

- Delete the object spring entirely (`ObjectStiffness`/`ObjectDamping`/`MaximumObjectForce`
  and the `ObjectTarget` field). Objects are never pulled toward the buddy.
- `Catch` extends the hands minimally toward the clamped target and waits. The catch
  confirms when the object physically **touches** a hand: centre distance
  `<= handRadius + objectRadius + CatchContactTolerance`.
- On confirmation the object **attaches**: `Freeze = true` with `FreezeMode.Kinematic`, and
  each routed tick its transform is set to the live hand socket. Attachment is a hard
  relocation by design — the owner asked for exactly this ("directly relocate/teleport to
  the buddy's hand and stick to it"). This does not violate ARCH §23: the invariant governs
  the **buddy rig**, whose bodies are still only ever driven by bounded forces. A carried
  loose object is cargo, not a simulated participant, while it is held.
- Release un-freezes, restores the pre-hold damping, and applies one impulse.

### Task 3 — Scoop pickup for resting objects

- The runtime, which knows `AtRest` from the registry, picks the flavour; the domain
  lifecycle is unchanged (it already delegates confirmation to `holdConfirmed`).
  - **Airborne + thrown** → contact catch (Task 2).
  - **At rest** → scoop: for `ScoopTicks` the hand targets lower toward the object and a
    bounded downward force dips the torso and head, then the object attaches.
- The dip is a bounded `ApplyCentralForce`, never a transform write.

### Task 4 — Throw back toward the cursor

- Reverses the previous "cursor-safe" toss direction, which threw *away* from the cursor.
  The owner wants the ball returned **to** the player. Recorded as an owner override of
  that delegated default.
- `Toss` becomes a two-beat gesture: `ThrowWindupTicks` pulling the hand target slightly
  back, then release on the forward beat with an impulse along `(cursor - hand)` scaled by
  `TossImpulse` plus `TossLiftImpulse`.
- `Discard` keeps its low-energy away-from-cursor release and its flee bias.

### Task 5 — Reliable obstacle detection

- `HitFromInside = true` on both obstacle probes, which alone fixes the touching case.
- Add a registry-backed fallback so detection never depends on ray edge cases: any sensed
  object at rest, within `ObstacleForwardWindow` ahead in the committed direction and below
  the torso, counts as obstacle evidence. Exposed from `ObjectInteractionComponent` and
  OR'd into the arbiter snapshot alongside the ray result.

### Task 6 — Tests, scenarios, docs

- Domain: reach-gated commitment, contact confirmation, scoop confirmation, throw-toward
  policy.
- Scenarios: `object_catch_hold` asserts hands never exceed the reach envelope and the
  object ends attached to a hand; `object_toss_discard` asserts the toss travels toward the
  cursor and the discard away from it; `jump_trait_gate` gains a touching-ball case.
- Record the reach envelope, the sticky-hold decision, and the throw-direction reversal in
  `DECISIONS.md`; refresh the owner gate.

## Verification

Build, `dotnet test`, `quick_validate`, the full scenario matrix in both presentations,
seed 7 for the behaviour set, and all journeys.

## Progress

Status: **COMPLETE — 2026-07-26.** Build clean with zero warnings; `dotnet test`
**646/646**; `quick_validate` **15/15**; scenario matrix **78/78** in both presentations on
seed 1 with the eight behaviour scenarios also green on seed 7; journeys **21/21**.

- [x] **Task 1 — real reach, minimal extension.** `ObjectCandidate.Distance` is now a true
      2D distance from `ReachOriginOffset`. Every hand target passes through one
      `ClampToReach` chokepoint bounded by `ReachRadius + MaximumReachExtension` (`44 + 6`),
      `MaximumHandForce` dropped `18000 → 6000`, and `CatchDistance <= MaximumReach` is a
      validation error so the machine cannot commit to an unreachable catch.
      `object_catch_hold` measures the high-water mark: `commanded=36.89 limit=50.00`.
- [x] **Task 2 — contact catch and sticky hold.** The object spring is deleted outright —
      `ObjectStiffness`, `ObjectDamping`, `MaximumObjectForce`, and the `ObjectTarget` field
      are gone, so nothing pulls an object toward the buddy. A catch confirms on physical
      contact with a hand, then the object attaches: frozen kinematic and placed on the hand
      socket every routed tick. Release restores damping and hands the object the carrying
      hand's own velocity so the gesture continues. Asserted:
      `attached=True hand_gap=23.53`.
- [x] **Task 3 — scoop pickup.** A resting object is scooped instead of caught: hands lower
      onto it, `ScoopDipForce` dips torso and head as a bounded force for `ScoopTicks`, then
      the object relocates into the hand. The runtime picks the flavour from the registry's
      rest state, so the domain lifecycle needed no new phase.
- [x] **Task 4 — throw back toward the cursor.** `ThrowWindupTicks` draws the hands back,
      then the release impulse follows `(cursor - carry pose)`. Verified
      `aimed_at_cursor=True impulse_x=-192.8 toward=-1`. Discard keeps its low-energy
      away-from-cursor release and flee bias.
- [x] **Task 5 — reliable obstacle detection.** `HitFromInside = true` on both probes, plus
      a registry-backed second source: any sensed resting object within
      `ObstacleForwardWindow` ahead and below the torso. The arbiter ORs the two, so hop
      evidence no longer depends on ray edge cases.
- [x] **Task 6 — tests, scenarios, docs.** Four new domain tests, two new scenario checks,
      and the toss diagnostics now report phase, attachment, and aim.

### Bug this work exposed

Making the toss a multi-tick gesture surfaced a latent defect. The candidate scanner
deliberately hides a buddy-held object, and the model's candidate-lost guard treated any
phase outside `Hold`/`Inspect`/`Consume` as "not holding" — so on the toss's *second* tick
the object was reported lost and the gesture aborted, leaving the ball stuck in the hand
forever with `phase=Idle attached=True`. It was invisible while `Toss` lasted a single tick.
The guard now applies only to `Approach` and `Catch`, the phases that genuinely need a
visible candidate, and a regression test drives an entire toss with no candidates at all.
