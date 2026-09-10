# Multi-Buddy and build-surface bug investigation

Status: the reported runtime, build-surface traversal, edge-placement, grab-isolation, and Build layout fixes were implemented and verified on 2026-09-11. Broader tool-by-tool multi-buddy parity remains outside this focused repair.

Implemented on branch `feature/master-release-plan-2026-09-08`, starting from HEAD `fbcfe5dbbb5868632d9484be9cb538a935a15a3b`, while preserving its existing uncommitted changes.

## Requested outcome

The owner's 2026-09-11 request is to debug and plan fixes, prioritizing added Buddies and traversal of player-built structures, then moving the Build preview to the right of the list. Every added Buddy should have the first Buddy's behavior and interactions. The master plan section 3.3 supports the owned roster without an artificial two-Buddy cap. Older single-Buddy deferrals are historical context, not a reason to reject this request. This report is a repair plan, not a new competing product specification.

## Evidence and confidence

The supplied 17.3-second recording was inspected through sampled frames. The second Buddy appears collapsed and stretched, but responds to dragging; the first Buddy is initially on the beam pile and is later on the room floor. The Build preview is below the list. Frames do not establish the exact recovery reason or prove missing physics ticks.

The fixes passed the domain suite and the tagged Windows production journey. That journey exercises a fully initialized second Buddy, safe edge placement, support on a placed build beam, and the right-side Build preview. The original investigation evidence below is retained to explain the repair decisions.

### 1. Build materials never count as support — confirmed source defect

- `src/Sandbox/SandboxPartBody.cs:Configure` places construction bodies on `CollisionLayers.LooseObjects`; the collision masks already permit Buddy collisions.
- `src/Buddy/Physics/PuppetPartBody.cs:_IntegrateForces` accepts support only from `CollisionLayers.RoomBounds`.
- `StandingDetector.PhysicsTick` requires foot support; `RecoveryComponent.PhysicsTick` advances the inability clock when stable standing is absent. `RecoveryClock` reaches its timeout after 2 seconds plus 10 seconds of failed assistance, then resets the pose.
- Therefore a Buddy on a built floor can collide correctly but still fail standing, balance, and recovery classification. Increasing the timer or disabling recovery would hide the defect.
- The support normal check uses `Abs(Y)`, which also accepts ceiling-facing contacts. Direction must be verified against actual Godot 4.6.1 contacts before changing the predicate.

### 2. Added actors have incomplete composition — confirmed; full physics cause unresolved

- The committed `SandboxRoot.ComposeAdditionalSceneActor` creates a visual presenter without the first actor's pose, facing, activity, head-look, face, or impact-offset components.
- `BuddyVisualPresenter.UpdateVisuals` falls back to zero performance weight when its pose pipeline is absent. This explains missing expressive presentation; it does not by itself prove that active physics is disabled.
- Existing uncommitted `ComposeSceneActorExpression` code supplies these six components. Review and verify that work instead of duplicating it or treating its comment as proof of a complete fix.
- Physics routing already iterates the roster through `SceneRuntimeHost.PhysicsTick`, `BuddyActorRuntime.PhysicsTick`, and `BuddyRoot.PhysicsTick`, including active drive and passive constraints. Measure those paths before changing their architecture.

### 3. Actor identity is lost in some interaction paths — confirmed source defects

- `BuddyPosePipeline.Evaluate` tests whether any `PuppetPartBody` is grabbed, not whether that body belongs to its Buddy. Grabbing one Buddy can suppress another Buddy's presentation.
- Both accepted-contact and blast paths in `InteractionDamageComponent` make the same global grab test when populating `AcceptedImpact.IsBuddyGrabbed`.
- Fire contact handling in `FireSprayerComponent` carries a part enum into one burning-state array and one `Pipeline`; `FindPart` resolves that enum against `Pipeline.Buddy`. This cannot represent independently burning Buddies. Inspect the droplet-to-owner handoff and repair it as part of interaction parity.
- Additional first-actor-bound audio, feedback, scorch, knockout, recovery, and care-item consumers exist in `SandboxRoot`/`sandbox.tscn`. Audit their subscriptions and lifecycle; do not assume a successful physical hit proves full parity.

### 4. Placement and traversal need more than collision masks

- Cast placement checks only that the pointer is inside room bounds, then converts it to a rig origin. It does not check whether the whole standing pose overlaps a beam or ceiling.
- Autonomy has horizontal loose-object probes and outer-room wall avoidance. That is not evidence of correct movement through interior walls, doorways, or raised platforms.
- Existing production bootstrap checks establish roster count, binding independence, positions, and Build placement/removal. They do not establish sustained standing/walking on build materials for every actor.

## Implementation order

### P0 — capture a reproducible baseline

1. Use a disposable save and the actual Next Fest/Full production composition. Record the working-tree revision/diff and build timestamp so committed and pending fixes cannot be confused.
2. Through Godot MCP, add Buddies B and C using the cast UI, drag/release each on the ordinary floor, and repeat after Build exit and Scene switching.
3. Capture per-placement routed-tick deltas, consciousness, frozen/sleeping state, support count, standing state, drive intent/forces, constraint strain, presentation weight, and recovery reason/count. Determine whether the remaining collapse is physical, visual, or both.
4. Reproduce on a wide frozen raised beam, then a stable house with adequate headroom and a doorway. Observe beyond the 12-second recovery deadline. Record upward support contacts and the reason for any reset.
5. Turn these reproductions into semantic scenarios/journeys; keep screenshots as evidence, not assertions.

### P1 — restore actor parity

1. Complete and validate the existing additional-actor composition patch. Match the authored actor's required initialization, presentation, recovery subscriptions, and teardown. Reuse current components; extract shared wiring only where it prevents demonstrated divergence.
2. Use actual rig-part ownership for grab-sensitive pose and impact paths. Reuse the existing ownership pattern; do not route gameplay by selected UI focus.
3. Preserve actor identity through fire contacts and maintain burning/scorch/damage state per actor, with one shared emitter and shared account wallet. Audit remaining tools and feedback bindings, repairing actual first-actor-only behavior.
4. Verify newly spawned actors resume after editor/Scene/Work transitions and are removed cleanly. Fix the measured physics cause if it remains after presentation repair; do not tune forces speculatively.

Acceptance: every actor independently stands, walks, recovers, reacts, and can be grabbed, cared for, struck, shot, burned, and affected by explosions. Grabbing A must not change B's grab flag or pose mode. A targeted hit/care event affects the correct Buddy; area effects may affect several, with shared wallet accounting unchanged. Repeat for first, middle, and last roster entries, including after removal and re-addition.

### P1 — recognize and traverse construction surfaces

1. Extend support classification at the shared contact reader to include actual construction bodies. Do not relabel all loose objects as room boundaries. Keep support based on a verified upward contact normal, so walls and ceilings do not count as floors. Cover frozen and dynamic construction bodies.
2. Recheck standing, balance, gait, and recovery on flat and rotated surfaces. Retain genuine invalid-state/out-of-bounds and failed-recovery safeguards.
3. Make placement validate the complete trusted six-circle pose against construction geometry. Use the same placement calculation for preview and final placement; choose a nearby clear pose or visibly reject an impossible location. Revalidate restored/recovery anchors after construction changes.
4. Test the existing local obstacle behavior on interior walls, doorways, steps within the current movement capability, and platform edges. Extend local sensing only where the reproduction fails. A supporting floor should not be mistaken for a frontal obstruction. A blocked wall should lead to a valid local movement decision, not endless pushing followed by a false respawn.
5. Keep normal physical falling and structure collapse. Do not make unfrozen piles immovable or promise automatic route planning through arbitrary houses as part of this bug fix.

Acceptance: each Buddy can be placed on a clear built floor, remain supported beyond several recovery windows, move along it, and pass through a sufficiently wide/tall doorway. Removing support causes physical falling and recovery, not floating. Ceiling/wall contacts do not qualify as standing; obstructed placement does not spawn intersecting bodies. Save/restart and Scene switching preserve usable placement.

### P2 — move Build preview right of the list

In `BuildModeController.BuildUi`, put the list and preview/details column in an `HBoxContainer`. Keep actions/status below. Adjust palette and detached-window sizes together, preserving list selection, scrolling, dragging, pinning, and minimum-window usability. Reuse `SandboxPartPreview`.

Acceptance: preview remains to the right of the list in both pinned and detached modes, updates with selection, and does not overlap or clip controls at supported sizes/DPI.

## Verification and completion

- Run focused domain tests for any new classification/placement policy and existing recovery tests.
- Add actual Godot-contact scenarios for built floors, angled surfaces, walls, ceilings, dynamic support, and support removal. Pure mock support flags are insufficient.
- Extend `production_bootstrap_persistence` or add focused production journeys for roster locomotion/interaction, Build transitions, Scene switches, and restart. Include ordinary floor baselines to distinguish actor failures from platform failures.
- Test 1, 2, 4, and 8 actors as engineering samples, not product caps. Measure physics/presentation with representative structures and painted actors; preserve documented budgets.
- Verify the changed player behavior interactively through Godot MCP, then run the relevant headless/journey and standalone Windows checks. Keep single-Buddy Initial Demo behavior intact.
- Put new integration scenarios in PR checks; do not add slow work to `CI / quick`.
- Mark the fixes complete only when the reproduced failures pass these checks. This investigation alone is not release evidence.
