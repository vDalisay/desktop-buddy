# Milestone 4 — Review Fixes Plan

Status: **COMPLETE 2026-07-26** (see Progress) — raised by the pre-acceptance
implementation/code review of all six M4 tasks against
`docs/M4_PERSONALITY_CARE_PERSISTENCE_PLAN.md`, 2026-07-26.

M4's automated gates are green and were re-verified independently at the start of
this review: build clean with zero warnings, `dotnet test` **611/611**,
`tools\quick_validate.bat` **15/15**, catalog **41 scenarios / 11 journeys**
matching `CHECKLIST.md:58-59`. The architecture holds: domain stays Godot-free,
stable string content IDs cross every seam, physics is forces-only with one-shot
release impulses and no transform/velocity writes, there is one gameplay
`_PhysicsProcess`, and mood drift left the physics path for `LifecycleCoordinator`.

This plan closes what the review found that does **not** match the M4 plan. It
invents no product behavior; every change either restores a plan deliverable, fixes
a defect, or records an explicit scope boundary that already exists in the M4 plan.

## Prime invariants — unchanged and still binding

The M4 plan's nine prime invariants continue to apply to every task here. In
particular: no new `_PhysicsProcess` registration, zero managed allocation on the
120 Hz path, two clocks never mixed, stable string IDs on every domain/save seam,
seeded randomness only, presentation parity, ARCH §12 persistence, and
owner-accepted behavior regression-locked.

## Findings and tasks

### Task A — Obstacle probe cannot see floor objects (blocking)

**Finding.** The obstacle probe is a horizontal ray at torso centre:
`LeftObstacleCast.GlobalPosition = torso`, `TargetPosition = (±ObstacleProbeDistance, 0)`
(`src/Buddy/Behavior/AutonomousMotionComponent.cs:108-126`).

Rig geometry (`data/buddy/lab_puppet_rig.tres`): torso centre `y=0`; feet centre
`y=+55`, radius `17`, so the floor line sits at `y≈+72`. A lab loose object
(radius `12`, `data/objects/lab_loose_object.tres`) resting on that floor has its
centre at `y≈+60` and its **top at `y≈+48`** — 48 px below the ray. Floor-resting
objects never register as obstacles.

`jump_trait_gate` passes only because it spawns the obstacle **at torso height and
freezes it** (`src/Testing/JumpTraitGateScenario.cs:35-41`). The scenario proves the
gate chain, not the gameplay. Persisted propensity, hysteresis, committed-path and
stable-support gating all work correctly — there is simply nothing to hop over, so
the whole Task 3 traits deliverable is inert in real play.

**Fix.**
- Add `ObstacleProbeHeightOffset` to `AutonomousMotionProfile` (validated finite and
  `>= 0`), default `64.0` — 8 px above the `y≈+72` floor line, so any object resting
  on the floor with radius `>= 6` is crossed. The probe mask stays layer 3
  (`LooseObjects`), so the room floor (layer 1) and the buddy's own feet (layer 2)
  cannot self-trigger.
- Offset both ray origins by that value in `UpdateObstacleSensing`.
- Rewrite `jump_trait_gate` to spawn a **real, unfrozen, floor-resting** object in
  the committed walk path and let it settle, so the scenario asserts the shipped
  gameplay path rather than a synthetic torso-height prop.

**Gate.** `jump_trait_gate` on seeds 1 and 7 in both presentations; the autonomy,
wall, and idle-soak regressions stay green.

### Task B — Task 5 tray surface has no caller (blocking)

**Finding.** The M4 plan Task 5 says *"Implement the minimal M4 tray surface:
Show/Hide and Save & Quit."* `SandboxRoot.RequestSaveAndQuit()`
(`src/App/SandboxRoot.cs:269`) has **zero callers**; `SetHiddenToTray` is called only
by `SandboxRoot` itself and the two lifecycle scenarios. There is no tray icon, menu,
input action, or hotkey. `docs/DECISIONS.md:595` nevertheless records the tray scope
as a delivered default, and `docs/M4_OWNER_GATE.md` step 4 asks the owner to hide to
tray with no user-reachable path.

**Fix.**
- Add two shell input actions in `project.godot`: `toggle_hide_to_tray`
  (`Ctrl+Shift+H`) and `save_and_quit` (`Ctrl+Shift+Q`), and matching
  `InputActions` constants.
- Add a focused `TrayCommandComponent` (`src/Platform/`) with `ProcessMode.Always`,
  composed in code by `SandboxRoot` next to `LifecycleCoordinator` (no scene edit,
  no autoload, no second physics root). It reads only those two actions and raises
  `HideShowToggled` / `SaveAndQuitRequested`; `SandboxRoot` routes them to
  `SetHiddenToTray` and `RequestSaveAndQuit`.
- **Record the honest boundary.** Godot delivers no input to an invisible,
  unfocused window, so *restoring* from hidden needs the native tray icon or global
  hotkey, which the M4 plan already scopes to M6 ("Full tray menu (M6). M4 ships
  Show/Hide + Save & Quit only."). M4 therefore ships the command surface and the
  hidden-mode state machine; the restore path is an M6 dependency. Update
  `docs/DECISIONS.md` and `docs/M4_OWNER_GATE.md` step 4 to state this plainly and
  to take the frozen-ragdoll/accrual proof from `hidden_clock_accrual` plus a
  windowed hide-then-relaunch CPU observation.

**Gate.** Build clean; `hidden_clock_accrual` green; docs carry no claim the code
does not implement.

### Task C — Hidden mode never throttles rendering (blocking)

**Finding.** ARCH §24 and the M4 plan require hidden mode to *"hide the window,
pause the tree, **disable the render loop, throttle `Engine.MaxFps` near 10**"*, and
on show to *"reset interpolation for buddy and objects, clear the physics
accumulator (FR-015.10)"*. `LifecycleCoordinator.SetHiddenToTray`
(`src/App/LifecycleCoordinator.cs:67-77`) does window-hide plus `GetTree().Paused`
only. No `Engine.MaxFps` or render-loop change exists anywhere in the M4 diff, and
no interpolation reset runs on show. The M4 plan's Progress entry claims hidden mode
*"pauses the gameplay tree and rendering"*; rendering is not paused.

`hidden_clock_accrual`'s `show_resumes_without_physics_burst` check asserts only
`!tree.Paused && AllBodiesFinite()` (`src/Testing/HiddenClockAccrualScenario.cs:61`)
— it does not test for a burst.

Owner gate step 4 asks for hidden CPU `<0.5%` on the reference machine, which is not
plausible with the render loop live.

**Fix.**
- Add `HiddenMaxFps` (default `10`) to `MoodEconomyProfile` beside the existing
  cadences, validated `> 0`.
- On hide: capture the current `Engine.MaxFps`, set it to `HiddenMaxFps`, and set
  `RenderingServer.RenderLoopEnabled = false`. Skip both under the headless display
  server, exactly as the existing window-visibility call does.
- On show: restore the captured `Engine.MaxFps`, re-enable the render loop, then
  reset physics interpolation across the buddy rig and every registered loose object
  so no pose jump is visible (FR-015.10's observable requirement). The step
  accumulator itself stays bounded by the existing
  `physics/common/max_physics_steps_per_frame=6` project setting; record that as the
  accumulator answer rather than inventing an engine API that does not exist.
- Give `LooseObjectRegistry` a `ResetInterpolation()` sweep and reuse the rig's
  existing reset seam.
- Strengthen `hidden_clock_accrual` to record part positions immediately before show
  and assert they are unchanged one frame after show, plus assert the render loop and
  FPS cap were actually toggled.

**Gate.** `hidden_clock_accrual` and `suspend_no_catchup` on seeds 1 and 7 in both
presentations.

### Task D — Suspend / resume / session lock have no runtime path (blocking)

**Finding.** The M4 plan Task 5 requires extending *"the desktop service/adapter
seams for hide/show, suspend/resume, discontinuity, and session lock"* with the
emulated adapter exposing deterministic stimuli. `IWindowsDesktopAdapter` and
`EmulatedWindowsDesktopAdapter` are unchanged — no suspend, resume, or session
members exist. `LifecycleCoordinator.NotifySuspended`/`NotifyResumed` are called
**only** from `SuspendNoCatchupScenario`. FR-016.8 (session lock counts as hidden
running time) has no runtime wiring at all.

**Fix.**
- Extend `IWindowsDesktopAdapter` with `SystemSuspending`, `SystemResumed`, and
  `SessionLockChanged(bool locked)` events.
- `EmulatedWindowsDesktopAdapter` gains `RaiseSuspending()`, `RaiseResumed()`, and
  `RaiseSessionLockChanged(bool)` so headless scenarios drive the seam
  deterministically instead of poking the coordinator directly.
- `WindowsDesktopAdapter` declares the events and does not raise them; power and
  session notifications join the M2 owner-manual Windows matrix, exactly as the M4
  plan allows ("rather than blocking this milestone").
- `DesktopWindowController` exposes its configured adapter; `SandboxRoot` subscribes
  and forwards to the coordinator.
- Add `LifecycleCoordinator.NotifySessionLock(bool)`: a locked session keeps
  accruing as **hidden running time** with no clock reset and no discontinuity
  exclusion (FR-016.8), and unlocking restores the prior presentation state without
  replaying anything.
- Extend `suspend_no_catchup` to drive suspend/resume **through the emulated
  adapter** and to assert session-lock accounting.

**Gate.** `suspend_no_catchup` on seeds 1 and 7 in both presentations; the M2
desktop-shell journey stays green.

### Task E — Malformed v1 save crashes the load instead of quarantining

**Finding.** `ProgressSavePolicy.MigrateV1`
(`domain/DesktopBuddy.Domain/Persistence/ProgressSavePolicy.cs:171-233`) uses
`GetInt32()`, `GetSingle()`, and `GetInt64()`. On a type mismatch these throw
`InvalidOperationException`/`FormatException`, which `Decode` does not catch — it
catches only `JsonException` and `ArgumentException` (`:67-74`). The exception
escapes to `Bootstrap`, which logs and `QuitSafely(3)`: the app exits rather than
quarantining and recovering. Tests cover malformed **v2** only
(`tests/.../ProgressSavePolicyTests.cs:177`).

**Fix.** Use `TryGetInt32`/`TryGetSingle`/`TryGetInt64` throughout the migration,
treat a wrong-typed legacy field as malformed, and add a defensive
`InvalidOperationException`/`FormatException`/`OverflowException` catch in `Decode`
that maps to `SaveDecodeStatus.Malformed`. Add unit coverage for wrong-typed v1
fields and for a v1 array holding non-integers.

**Gate.** New `ProgressSavePolicy`/`ProgressStore` tests; the corrupt-primary →
quarantine → backup → defaults chain still green.

### Task F — A held object can never be physically lost

**Finding.** `ObjectInteractionComponent.PhysicsTick:146` computes
`holdConfirmed = IsHolding ? GodotObject.IsInstanceValid(_heldBody) : CatchHandsReady(...)`.
Once holding, that is unconditionally true, so the model's grip-loss branches
(`Hold`/`Inspect`/`Consume` → `Drop`,
`domain/.../ObjectInteractionModel.cs:258-306`) are dead at runtime. Combined with
collision exceptions against all six rig parts, the object is glued until the model
chooses to release. Knocking food out of the buddy's hands — the natural FR-008.10
"interrupted mid-meal" case — is unreachable except through an activity interrupt.

**Fix.** Add `HoldReleaseDistance` to `ObjectInteractionProfile` (validated finite,
`> CatchConfirmDistance`) and confirm a live hold by measuring the held body against
the current hold centre, so a hard enough disturbance separates it and the model
takes its existing `Drop` path. Cancelling the consume token on that drop already
works and starts no cooldown.

**Gate.** `object_catch_hold`, `consume_care_cooldown`, and `activity_clips` on
seeds 1 and 7 in both presentations; a new domain test that a grip loss during
`Consume` starts no cooldown.

### Task G — Save & Quit can return before the newest state is written

**Finding.** `SaveCoordinator.FlushProgressAsync` (`src/Persistence/SaveCoordinator.cs:51-60`)
returns the **in-flight** flush when one is running. That is deliberately correct for
autosave — the comment says so — but on the Save & Quit path
(`src/App/SandboxRoot.cs:280`) it means the process can exit with the newest mutation
unwritten and no retry left.

**Fix.** Add `FlushProgressAsync(bool force)`. When forced, await the active flush
and then, if the state is still dirty, run exactly one more flush. Bounded to one
extra pass so a continuously advancing running-time revision can never starve the
quit. `OnCloseRequested` and `_ExitTree` use the forced form; autosave and focus-loss
keep the coalescing form.

**Gate.** New `SaveCoordinatorTests` for "mutation during flush is written by a
forced flush" and "forced flush cannot loop more than once".

### Task H — A content buddy freezes near the cursor

**Finding.** `UpdateSocialStance` sets `Greet` whenever
`distance <= ApproachDistance` and the band has a greet interval
(`domain/.../BehaviorArbiterModel.cs:430-434`). `IsEligible(Social)` is true for any
non-`None` stance, so priority 6 owns actuation **permanently** inside 170 px
(Content) / 110 px (Delighted) with `DriveActive: false`, suppressing ambient walking
the entire time while `GreetRequested` fires only every 900/360 ticks.
`mood_band_behavior` tests Content/Delighted at 400 px only
(`src/Testing/MoodBandBehaviorScenario.cs:31-32`), so the near-cursor case is
untested.

**Fix.** Make the greet own actuation only on the tick it actually fires: compute
cadence readiness inside `UpdateSocialStance` and return `Greet` only when a greet is
due, `None` otherwise. Ambient then keeps the buddy alive between waves, and the wave
itself still preempts as priority 6. Extend `mood_band_behavior` with the near-cursor
Content case asserting ambient ownership between greets and Social ownership on the
greet tick.

**Gate.** `mood_band_behavior` and `behavior_priority_ladder` on seeds 1 and 7 in
both presentations; new arbiter unit coverage for greet cadence ownership.

### Task I — The priority ladder is computed in two places

**Finding.** `BehaviorArbiter.PhysicsTick:113-118` hand-rolls `suppressObject` from
priorities 0–4 and passes it to `ObjectInteraction.PhysicsTick` **before**
`_model.Resolve` runs. It currently agrees with `BehaviorArbiterModel.IsEligible`,
but the two must be kept in sync by hand; a future priority change in the model
silently desynchronises object suppression.

**Fix.** Build the non-object fields of the snapshot first, derive suppression from a
new `BehaviorArbiterModel.SuppressesVoluntaryAction(in BehaviorSnapshot)` that walks
the same `IsEligible` ladder over priorities 0–4, tick the object component, then
complete the snapshot with a `with` expression (a stack copy on a
`readonly record struct` — no allocation). One ladder, one source of truth.

**Gate.** `behavior_priority_ladder`, `object_catch_hold`, `object_toss_discard`,
`consume_care_cooldown`; the 10,000-tick zero-allocation arbiter check stays at zero.

### Task J — Minor correctness and hygiene

1. **`ObjectCandidate.AtRest` is documented but never read**
   (`domain/.../ObjectInteractionModel.cs:58-66`); candidate scoring is distance-only
   plus the harmful filter and band gate, where the M4 plan specified "distance,
   safety, memory, mood band". Score airborne (thrown) candidates ahead of resting
   ones so a real throw wins over a nearer idle prop, matching the documented meaning.
2. **`BlockedDirection` is discarded on the ambient path** —
   `BehaviorArbiter.cs:240` uses the raw `ambient.WalkDirection`, so the arbiter's
   wall filter only ever affects Social and ObjectAction. Use `Intent.WalkDirection`.
3. **`Transition()` leaks a stale `LastAbort`** into a normal intent's `Abort` field
   (`ObjectInteractionModel.cs:412-424`), which `HandleIntent`
   (`ObjectInteractionComponent.cs:294`) then treats as a reason to cancel a live
   consume token. Emit `ObjectAbortReason.None` on transitions.
4. **`OnBodyEntered`'s duplicate guard is ineffective**
   (`ObjectInteractionComponent.cs:516-531`): the free-slot branch fires at a lower
   index before the equality check can reach an existing entry. Scan for a duplicate
   first, then insert. Also recover the slot and `SensedCount` when a sensed body was
   freed without an exit signal.
5. **A known-but-locked selected tool is dropped silently** —
   `ProgressSavePolicy.CreateState:117-123` falls back to Grab without retaining the
   value, because `unknownSelected` is null on that branch. Retain it in the
   extension bucket like any other non-activatable ID.
6. **`NotifySuspended`/`NotifyResumed` do not guard `IsInitialized`**
   (`LifecycleCoordinator.cs:79-92`) and dereference a `null!` clock if called before
   `Configure`.
7. **`MoneyHudPresenter._ExitTree` returns early** when `Pipeline` is invalid
   (`src/UI/MoneyHudPresenter.cs:46`), skipping the `EconomyService.BalanceChanged`
   unsubscribe.

### Task K — Documentation and gate refresh

- Record every behavioural change above in `docs/DECISIONS.md` under a new
  "M4 review fixes" heading, including the corrected obstacle probe height, the
  hidden-mode FPS/render-loop values, the M6 restore-from-hidden boundary, and the
  hold-release distance.
- Update `docs/M4_OWNER_GATE.md` step 4 for the honest hide/restore story and step 1
  for the now-live obstacle hop.
- Update this plan's Progress section and the M4 plan's Progress entry for Tasks 3
  and 5 so neither claims behaviour the code does not implement.
- Re-run the full gate: build, `dotnet test`, `quick_validate`, the affected
  scenarios on seeds 1 and 7 in both presentations, and the `care_persistence`
  journey.

## Verification

```bat
dotnet build DesktopBuddy.sln -c Debug
dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug
tools\quick_validate.bat
```

Affected scenarios on seeds 1 and 7 under `--presentation=mii3d` and
`--presentation=legacy`: `jump_trait_gate`, `mood_band_behavior`,
`behavior_priority_ladder`, `object_catch_hold`, `object_toss_discard`,
`consume_care_cooldown`, `hidden_clock_accrual`, `suspend_no_catchup`,
`autonomous_motion`, `autonomy_respects_walls`, `activity_clips`, plus the
`care_persistence` journey.

## Progress

Status: **COMPLETE — 2026-07-26.** Final gates: build clean with zero warnings;
`dotnet test` **638/638** (611 before this pass, +27 new); `quick_validate` **15/15**;
scenario matrix **78/78** (39 runnable scenarios in both presentations, `idle_soak`
run as `idle_soak_ci`, window-only `owner_feedback_visual` excluded); journeys
**21/21** (both presentations except the documented Mii3D-only presentation-toggle
journey). The affected scenarios additionally passed on seeds 1 and 7 in both modes.

- [x] **Task A — obstacle probe height and a real floor-obstacle gate.**
      `ObstacleProbeHeightOffset` defaults to `64 px` below the torso. `jump_trait_gate`
      now spawns unfrozen floor-resting objects on both sides and asserts the probe sees
      one (`probe=True offset=64`) with propensity `0` never hopping and `100` hopping.
      `autonomous_motion` used the same torso-height prop and regressed on the first
      matrix run; its obstacle moved to the floor line too, which is exactly the
      evidence that the old probe height was never exercising gameplay.
- [x] **Task B — tray command surface and honest M6 boundary.** `toggle_hide_to_tray`
      (`Ctrl+Shift+H`) and `save_and_quit` (`Ctrl+Shift+Q`) added to the input map and
      `InputActions.All` (startup validation now reports 22 checks), driven by
      `TrayCommandComponent` at `ProcessMode.Always` and composed in code by
      `SandboxRoot`. Restore-from-hidden documented as an M6 native dependency in
      `DECISIONS.md` and `M4_OWNER_GATE.md` rather than claimed as shipped.
- [x] **Task C — hidden-mode render/FPS throttle and show-time interpolation reset.**
      `HiddenMaxFps = 10` plus `RenderingServer.RenderLoopEnabled = false` on hide,
      both restored on show followed by `PuppetRig.ResetInterpolation()` and
      `LooseObjectRegistry.ResetInterpolation()`. `hidden_clock_accrual` asserts the
      throttle state and that the largest part displacement across the show frame is
      bounded (`largest_jump=0.068`), replacing a check that only tested finiteness.
- [x] **Task D — adapter suspend/resume/session-lock seams.**
      `IWindowsDesktopAdapter` gained the three events, the emulated adapter raises
      them, and `suspend_no_catchup` now drives suspend/resume **through the adapter**
      and asserts a locked session accrues `2.0 s` of hidden running time with income
      and zero excluded spans.
- [x] **Task E — malformed legacy save hardening.** `Try*` accessors throughout
      `MigrateV1` plus a defensive
      `InvalidOperationException`/`FormatException`/`OverflowException` catch in
      `Decode`. Eight new theory cases cover wrong-typed scalars, non-integer legacy
      arrays, and an out-of-range integer.
- [x] **Task F — physical grip loss.** `HoldReleaseDistance = 72 px` confirms a live
      hold against the hold centre, reaching the model's previously dead drop branches;
      the direct laboratory consume path checks it too. New domain test asserts an
      interrupted consume drops and requests nothing.
- [x] **Task G — forced Save & Quit flush.** `FlushProgressAsync(force)` joins an
      in-flight write then runs at most one more pass. Three new tests: a mutation
      during the write is captured, the loop is bounded against a store that dirties on
      every write, and a clean state writes nothing extra.
- [x] **Task H — greet cadence ownership.** Cadence readiness moved into
      `UpdateSocialStance`, so `Greet` is produced only on the tick it fires. New unit
      test walks a whole interval asserting `Ambient` ownership and
      `AmbientSuppressed == false` between waves; `mood_band_behavior` asserts the same
      at the near-cursor Content distance the old test never covered.
- [x] **Task I — one priority ladder.** `BehaviorArbiterModel.SuppressesVoluntaryAction`
      is the single source; the runtime builds the snapshot, asks the model, ticks the
      object worker, then completes the snapshot with a `with` expression. Two new tests
      pin it to `Resolve`'s own ownership for every priority.
- [x] **Task J — minor correctness and hygiene.** All seven: airborne-over-resting
      candidate scoring, ambient wall filtering, no stale abort reason on transitions,
      duplicate-safe sensor admission with slot/count recovery, retained
      known-but-locked selection, `IsInitialized` guards on the suspend/resume
      notifications, and an unconditional economy unsubscribe in the money HUD.
- [x] **Task L — the laboratory could not spawn a loose object** (found while writing
      the owner-gate instructions, 2026-07-26). `SpawnLooseObject` had exactly two
      callers: scenarios, and the Eat key, which puts food straight into the hand. No
      key put an object into the room, so *every* object-interaction behaviour —
      approach, catch, hold, inspect, toss, discard, and the newly live obstacle hop —
      was unreachable by a human, and gate steps 1–3 were not performable as written.
      Added `O` (drop a safe object at the cursor, clamped inside the room and never
      below the floor line) and `Shift+O` (clear all), wired through
      `LaboratoryControlComponent` events so the lab root keeps owning the factory.
      `laboratory_controls` asserts spawn placement and clearing
      (`before=0 spawned=2 inside=True cleared=0`).
- [x] **Task K — documentation and gate refresh.** `DECISIONS.md` "M4 Review Fixes",
      a rewritten `M4_OWNER_GATE.md` (new obstacle-hop step, honest hide/restore step,
      native power/session listed as owner-manual), corrected Task 3 and Task 5
      Progress entries in the M4 plan, and `CHECKLIST.md` counts refreshed to 638 tests
      and 78 scenario runs. `README.md` documents the complete laboratory key surface
      and the three sandbox shell hotkeys, and the owner gate lists the keys each step
      needs instead of assuming them.

### Not changed, and why

- **`care_persistence` phase one still drives care and damage through the domain
  models rather than the live Eat choreography.** Its subject is the cross-process
  save round trip, and freezing the routed simulation is what makes the semantic
  checkpoint exact. The real bite-five path is covered by `consume_care_cooldown` in
  both presentations, so the coverage exists; merging the two would make the journey
  a duplicate of the scenario and reintroduce a race against cumulative-time
  accounting.
- **The native adapter still does not raise the §24 events.** `WM_POWERBROADCAST` and
  `WTSRegisterSessionNotification` on the subclassed window procedure are real native
  work that the M4 plan explicitly routes to the M2 owner-manual Windows matrix. The
  seam, the emulated stimuli, and the headless coverage are in place so that work is
  additive.
- **Hidden-mode CPU `<0.5%`** stays owner-manual on reference hardware, as the M4
  plan specifies. The throttle it depends on is now implemented and asserted; the
  measurement is not something a headless gate can make.
