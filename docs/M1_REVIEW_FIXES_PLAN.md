# Milestone 1 Review Fixes — Agent Handoff

Remediates the defects found in the 2026-07-12 implementation review of
`docs/M1_REMEDIATION_PLAN.md`. That plan's six tasks are all present in the tree, but the
review **executed the full suite** and found two failing scenarios, one journey that fails
windowed, a release-export breakage, and several plan requirements that were silently
dropped. This plan fixes them. Read `docs/M1_REMEDIATION_PLAN.md` (the parent plan),
`docs/STATE_AUDIT_2026-07-12.md`, `docs/AGENT_VERIFICATION_AND_E2E.md` §2–§5, and
`docs/ARCHITECTURE.md` §21–§23 before starting.

Review evidence (reproduce any of these before touching code if you doubt the diagnosis):

| Symptom | Command | Observed |
| --- | --- | --- |
| `grab_resistance` FAILS | `godot --headless --path . -- --scenario=grab_resistance --seed=1` | `fearful_grab_produces_opposing_force` fails, `minForceX=0`; extensions 276–322 px where the scenario pulls 70 px |
| `idle_soak_ci` FAILS | `godot --headless --path . -- --scenario=idle_soak_ci --seed=1 --artifacts=.artifacts/x` | `idle_soak_standing_capable` fails, `supports=0`; envelope shows 1,939/21,599 standing frames |
| `lab_grab_throw` fails windowed | `godot --path . -- --journey=lab_grab_throw --seed=1` (no `--headless`) | `assert:all_parts_grabbed` false |
| Everything else | full scenario/journey sweep | green (13/14 scenarios, 3/3 headless journeys) |

## Ground rules (apply to every task)

- All rules from `docs/M1_REMEDIATION_PLAN.md` still apply: single routed gameplay tick,
  pure logic in `DesktopBuddy.Domain` with xUnit tests, no allocations on the 120 Hz tick
  path, integer tick counting, tolerance-band assertions, debug-only guards on dev code.
- **The failure mode this plan exists to fix is "done without running the suite."** A task
  is not done until you have run, locally and green:
  1. `dotnet build DesktopBuddy.sln -c Debug`
  2. `dotnet test tests/DesktopBuddy.Domain.Tests/DesktopBuddy.Domain.Tests.csproj`
  3. Every scenario in `ScenarioCatalog` except full `idle_soak` (run `idle_soak_ci`
     instead), each `--headless --seed=1`, plus `--seed=7` for the scenario(s) your task
     touched.
  4. All four lab journeys headless, **and** the three interactive-path lab journeys
     (`lab_spawn_settle`, `lab_grab_throw`, `lab_walk_jump`) windowed (omit `--headless`).
     Windowed runs open a real window on this machine; that is expected. Do not move the
     physical mouse while one runs.
- Resolve the pinned Godot with `tools\resolve_godot.bat` (or set `GODOT_PATH`).
  Repository-adjacent, Downloads, and `PATH` installs are supported; scenario/journey invocation shapes are in `README.md`.
- Where this plan says "measure and record," put the numbers in the task's commit message
  body so the next reviewer can check your work without rerunning it.

---

## Task 1 — Pointer input integrity (fixes the two grab regressions)

**Root cause, established by the review:** to let `lab_grab_throw` drive the real input
queue headless, `src/Laboratory/LabPointerGrabComponent.cs` was changed to be active in
headless builds, to read raw mouse buttons in `_Input`, and to track a `_cursor` from raw
events. Three defects followed:

- **(a) Cursor stomping.** `ResolvePendingInput()` calls `Grab.MoveCursor(cursor)` on
  *every* routed tick whenever *any* grab is active — including grabs that scenarios
  acquired directly via `lab.Grab.TryGrab(...)`. In headless scenarios no mouse events
  arrive, so `_cursor` stays `(0,0)` and the tether cursor is dragged to the window origin
  every tick, overriding the scenario's `MoveCursor` intent. This flips the resistance
  direction in `GrabResistanceScenario` (red CI) and silently corrupts the semantics of
  `grab_release` / `grab_hard_recovery` (they still pass, but exercise a violent corner
  drag instead of a 70 px pull).
- **(b) Two cursor sources.** Headless resolves picks/drags from event-tracked `_cursor`;
  windowed resolves from `GetGlobalMousePosition()` and **discards synthesized event
  positions**, so the same journey grabs different world points windowed vs headless.
  This is why `lab_grab_throw` fails windowed (violating parent-plan Task 3's done-when).
- **(c) `_UnhandledInput` → `_Input` regression.** The component now sees events before
  GUI handling, so clicking a lab UI control can also start a grab; and raw
  `MouseButton.Left/Right` matching abandoned the `InputActions.Primary/Secondary` action
  map (`buddy_primary` = mouse 1, `buddy_secondary` = mouse 2 in `project.godot`, so
  action matching works for synthesized events too).

**Fix spec — all in `src/Laboratory/LabPointerGrabComponent.cs`:**

1. **Grab ownership.** Add `private bool _ownsGrab;`
   - Set `true` immediately after a successful `Grab.TryGrab(...)` inside
     `ResolvePendingInput`.
   - Set `false` in `ReleaseIfGrabbing()` after release, on cancel, and whenever
     `Grab.IsGrabbing` is observed false.
   - Gate the per-tick drag: `if (_ownsGrab && Grab.IsGrabbing) Grab.MoveCursor(cursor);`
   - Scenarios that call `lab.Grab.TryGrab` directly are then never touched by the
     pointer. No scenario changes needed.
2. **One cursor source for both modes.** Delete the
   `DisplayServer.GetName() == "headless"` branches. Track the viewport-space cursor from
   events in every mode (`_cursor = mouse.Position` already does this — real hardware
   motion also produces `InputEventMouseMotion`, so interactive use keeps working), and
   convert to world space at resolve time:
   `Vector2 cursor = GetViewport().GetCanvasTransform().AffineInverse() * _cursor;`
   This is zoom-correct (the lab canvas transform is what `room_resize_zoom` manipulates)
   and identical windowed vs headless. Delete the headless nearest-part special case in
   `TryPick` — with a world-correct cursor the existing `PhysicsDirectSpaceState2D`
   intersection query works headless too (physics space exists headless; the previous
   special case existed only because the cursor was in the wrong space). If the space
   query proves empty headless (verify before assuming), keep the group-based fallback but
   make it conditional on the query returning nothing, not on the display server name.
3. **Restore `_UnhandledInput` and action matching.** Move the handler back to
   `_UnhandledInput`, match `@event.IsActionPressed(InputActions.Primary)` /
   `IsActionReleased(InputActions.Primary)` / `IsActionPressed(InputActions.Secondary)`,
   and keep a plain `@event is InputEventMouse mouse → _cursor = mouse.Position` line for
   cursor tracking (also move it to `_UnhandledInput`; if cursor tracking then misses
   events consumed by GUI, track position in `_Input` but *act* only in
   `_UnhandledInput`). Synthesized events reach `_UnhandledInput` normally; if the
   headless journey stops receiving them, fall back to `_Input` for everything and record
   why in a comment — but try `_UnhandledInput` first.
4. **Truthful state.** `_active = BuildInfo.IsDebugBuild;` stays (headless activation is
   required by the journey). Rewrite the stale comment above it — it still says "never
   headless".
5. Keep `ReceivedInputCount` / `LastPickedPart` / `SuccessfulPickCount` observability;
   re-check the journey's `pointer_input_received >= 18` expectation still holds with the
   final event-handler choice (3 events per part × 6 parts).

**Verify:**

- `grab_resistance --seed=1` passes, and its verdict shows `minForceX` strongly negative
  again (it was −1000-scale before the regression).
- `grab_release --seed=1` passes **and** its verdict extension values return to the
  ~40–80 px scale (record them) — this proves the stomp is gone, not just masked.
- `grab_hard_recovery`, `laboratory_controls`, `room_resize_zoom` pass (pointer interacts
  with zoom via the canvas transform now).
- `lab_grab_throw --seed=1` passes headless **and windowed**.
- Windowed manual smoke: run `devtools/play_buddy_lab.bat`, drag a part with the mouse,
  confirm grab/drag/release and that clicking lab UI buttons does not grab.

**Done when:** all of the above green; no `DisplayServer.GetName()` checks remain in the
pointer component.

## Task 2 — Idle soak: real acceleration, sound end-state assertion, CI wiring

Three defects: the soak runs at wall clock despite the parent plan explicitly requiring
fast-forward (its header comment falsely claims "engine time scaling"); the end-state
check samples one instantaneous tick (`SupportContactCount > 0`) and fails whenever the
autonomous buddy is mid-step/mid-jump at the final tick (measured: standing only 1,939 of
21,599 frames — the check is a coin flip weighted against you); and `idle_soak_ci` is in
the catalog but never wired into push CI (parent plan Task 2.4 required it; README even
calls it "the three-minute push check").

**Fix spec:**

1. **Acceleration via `--fixed-fps`.** Invoke soak runs as:
   `godot --headless --fixed-fps 120 --path . -- --scenario=idle_soak_ci --seed=1 --artifacts=...`
   With `--fixed-fps 120` each main-loop iteration advances exactly one 1/120 s step
   regardless of wall time, and headless iterations are uncapped, so the loop free-runs.
   No scenario code changes — `await PhysicsFrame` per tick still counts exact ticks.
   - First, measure: run `idle_soak_ci` with and without the flag and record both wall
     times. Expect a large speedup (the review measured ~180 s of physics at wall clock).
     If `--fixed-fps` does not free-run under the .NET/headless combination (measure, don't
     assume), fall back to the parent plan's alternative: accept wall clock, and say so
     honestly in the scenario header, CI comments, and README.
   - Apply the flag to: the new push-CI step (below), both `full-soak` job steps in
     `.github/workflows/ci.yml`, and the README soak commands. Do **not** apply it to the
     interactive/windowed instructions.
2. **Fix the lying header comment** on `src/Testing/IdleSoakScenario.cs` — document the
   actual mechanism and why (`--fixed-fps` decouples sim time from wall clock; lab
   `Engine.TimeScale` control caps at 4× and is not used), per parent plan Task 2.1's
   "document the choice in the scenario header comment."
3. **End-state settle window.** Replace the instantaneous
   `idle_soak_standing_capable` check with a recovery window: after the soak loop, wait up
   to 720 ticks for `lab.Buddy.Standing.Snapshot.IsStable` (same pattern as
   `StandingRecoveryScenario.WaitForStanding`). Pass detail should report how many ticks
   it took. This asserts "standing-capable at soak end" (TEST_PLAN §3's actual wording)
   instead of "standing at one arbitrary tick."
4. **Strain bound from the profile, not a literal.** `idle_soak_connected` currently
   hardcodes `1.1f`. Load `res://data/buddy/lab_envelope_bounds.tres`
   (`EnvelopeBoundsProfile`) and use `MaximumLinkStrain`. Same replacement in
   `JourneyRunner.ExerciseIdleSoakAsync` (`soak_connected`) and
   `ExerciseGrabThrowAsync` (`rig_connected_after_throws`).
5. **Frame accounting.** Add a check that the recorded envelope's `FrameCount` is within
   16 frames of the tick count when telemetry was enabled (the recorder's pool drops
   frames silently on writer-thread starvation; today nothing would notice a cascade).
   Read the envelope back with `TelemetrySerializer.ReadEnvelope` — the passive-rig
   scenarios already show the pattern.
6. **CI step.** Add to the `build-test` job after "Repeated-run envelope":
   ```yaml
   - name: Idle soak (three-minute variant)
     run: xvfb-run -a "$GODOT_BIN" --headless --fixed-fps 120 --path . -- --scenario=idle_soak_ci --seed=1 --artifacts=.artifacts/idle_soak_ci
   ```
   Keep the full 216,000-tick `idle_soak` + `lab_idle_soak` journey in the
   `workflow_dispatch` job (now fast with the flag). Update the job's comment to describe
   the actual split.
7. **One soak implementation.** `JourneyRunner.ExerciseIdleSoakAsync` duplicates the
   scenario loop. Extract the loop (tick budget → per-tick finite/awake/strain/containment
   accumulation → result struct) into one shared helper — a static method on
   `IdleSoakScenario` or a small `SoakProbe` class under `src/Testing/` — and call it from
   both. Parent plan Task 3.3 required exactly this ("one implementation, two entry
   points").

**Verify:** `idle_soak_ci` passes 3× consecutively with `--seed=1` and once with
`--seed=7`, with `--fixed-fps 120` wall time recorded; `lab_idle_soak` journey passes
headless with the flag; CI YAML parses (`gh workflow view` or push to a branch).

**Done when:** soak variant green 3×, push CI contains the step, comment/README describe
the real mechanism, shared soak helper used by both entry points.

## Task 3 — `repeat_envelope` can never fail its settle bound

`src/Testing/RepeatEnvelopeScenario.cs` initializes `int settled = bounds.MaximumSettleTicks`
and later checks `maxSettle <= bounds.MaximumSettleTicks` — a run that **never** settles
produces exactly the bound, so the check passes vacuously. If all ten runs fail to settle,
every check passes (spread = 0).

**Fix spec:**

1. Track settling explicitly: `bool allRunsSettled = true;` set
   `allRunsSettled &= runSettled;` per run (a run settles only if the stable break fired).
   Add check `repeat_all_runs_settled` with the failing run index in the detail.
2. Keep `settled = tick + 1` for spread math but only fold it into min/max when the run
   actually settled; report unsettled runs in the messages list.
3. **Tighten the provisional bounds with data.** Review-measured values (seed 1): settle
   53–59 ticks, spread 6, pose spread 5.2 px, max strain 0.76 — against bounds of 720 /
   240 / 440 px / 1.1. Bounds that loose only catch NaN-grade catastrophes. Run
   `repeat_envelope` with seeds 1, 7, 42; take the worst observed value across runs and
   set each bound to roughly 3× that worst case (keep `MaximumLinkStrain` at 1.1 — it is
   a physical connectivity bound, not a statistical one). Update
   `data/buddy/lab_envelope_bounds.tres`, keep `resource_name` marked provisional, and
   record measured-vs-chosen values in the commit message.

**Verify:** scenario passes 3× with the tightened bounds across seeds 1/7/42; then
temporarily set `MaximumSettleTicks = 1` and confirm the scenario **fails** with the new
check naming the run (restore afterwards) — proof the assertion can fire.

**Done when:** vacuous-pass hole closed and demonstrated, bounds tightened from
measurements, all seeds green.

## Task 4 — Export filter swallows shipping tuning resources

`export_presets.cfg` gained `data/buddy/lab_*` in `exclude_filter`. Every buddy tuning
profile is named `lab_*` (`lab_puppet_rig.tres`, `lab_active_drive.tres`,
`lab_autonomous_motion.tres`, `lab_conscious_drive.tres`, `lab_unconscious_drive.tres`,
`lab_grab_tether.tres`, `lab_boundary.tres`) and `scenes/buddy/puppet.tscn` — which is
**not** excluded — references five of them. A release export ships a scene whose resource
dependencies are stripped. The intent was to exclude only the new
`lab_envelope_bounds.tres`.

**Fix spec:** replace `data/buddy/lab_*` with `data/buddy/lab_envelope_bounds.tres` in the
`exclude_filter`. Do not rename resources (ROADMAP's exit criterion locks accepted tuning
as `.tres` under `data/buddy/`, so the profiles must ship).

**Verify:** if export templates for 4.6.1 are installed
(`%APPDATA%\Godot\export_templates\4.6.1.stable.mono\`), run
`godot --headless --path . --export-release "Windows Desktop" build/windows/DesktopBuddy.exe`
and confirm no missing-dependency errors and that `lab_envelope_bounds.tres` is absent
from the PCK while `lab_puppet_rig.tres` is present
(`godotpcktool` or re-import check — otherwise inspect the export log's file list). If
templates are not installed, state that in the commit message and verify by filter-string
inspection plus `grep` that no other `data/buddy` file matches the new pattern.

**Done when:** filter excludes exactly the envelope-bounds resource; export (or documented
fallback verification) clean.

## Task 5 — Journey step vocabulary, so record-and-promote produces runnable journeys

Parent plan Task 3's prereq was a data-driven step vocabulary
(`pointer_press/drag/release`, `advance_time`, `wait_predicate`) with semantic anchors.
What landed instead: three hardcoded C# "exercise" methods selected by `setup.exercise`,
and `steps` arrays in the journey JSONs that **the runner never reads** (decorative).
Consequence: `TracePromoter` (parent Task 4) emits step-based journey drafts that
`JourneyRunner` cannot execute — the promoted draft runs the default settle path, ignores
every step, and fails on its TODO predicate. The record → promote → harden loop is
stillborn, and the JSONs misrepresent the contract.

Decision (make it, don't re-litigate): **implement a minimal step interpreter additively.**
Keep the three existing exercise journeys exactly as they are (they are green; migrating
them is not in scope). The interpreter exists so promoted journeys can run.

**Fix spec:**

1. In `JourneyRunner.ComputeBuddyLabStateAsync`, when setup has **no** `exercise` key and
   the journey has a non-empty `steps` array, execute the steps sequentially before the
   generic settle/assert block. Support exactly:
   - `pointer_press` — resolve `target` to a world position (see 2), convert world →
     viewport coords via `GetViewport().GetCanvasTransform() * world`, synthesize a left
     press there via `Input.ParseInputEvent`, then await one process + one physics frame
     (mirror `ExerciseGrabThrowAsync`'s cadence).
   - `drag` — same resolution, synthesize `InputEventMouseMotion` (Relative/Velocity from
     the delta), await one process + one physics frame.
   - `pointer_release` — synthesize left release at the last drag/press position.
   - `press_key` — synthesize `InputEventKey` press+release from `key` (physical keycode
     int, matching what the recorder captured).
   - `advance_time` — `ticks` physics frames in a loop (no wall-clock sleeps).
   - Unknown step → fail the journey with `step_known=false` naming the step (fail loud,
     per the runner's existing unknown-predicate style).
   `wait_predicate` is **not** required (the promoter never emits it); leave it
   unimplemented and have the unknown-step failure name it clearly if encountered.
2. Anchor resolution for `target`: `buddy:<PartId>` → that part's `GlobalPosition` from
   `lab.Buddy.Rig` (parse the enum name after the colon); `sandbox` → use the step's
   literal `x`/`y` as world coords; anything else → journey fails with
   `anchor_known=false`. This is the same anchor grammar `AutomationDriver.ResolveAnchor`
   writes.
3. Promoted journeys need at least one real assertion to be runnable. Add one generic
   always-computable predicate for step journeys: reuse the existing
   `lab_composed` / `lab_six_body` / `lab_finite` / `lab_settled` set (already computed
   after the exercise block). Update `TracePromoter` to emit
   `{"predicate": "lab_finite"}` plus the existing TODO marker assertion **commented via
   a `"_todo"` key** rather than a fake predicate — i.e. draft asserts `lab_finite` and
   carries `"_todo": "add semantic assertions"` so it runs green immediately after
   promotion and still tells the hardener what to do. (Runner must ignore assertion
   entries lacking a `predicate` key — add that guard.)
4. **Promoter collapse + idle-strip** (parent Task 4.3, currently absent): in
   `TracePromoter.Promote`:
   - Collapse each unbroken run of `pointer_motion` samples between a press and release
     into at most one `drag` step per anchor change, targeting the run's final sample
     (semantic target if the final sample resolved to one, else sandbox x/y).
   - Strip idle time: emit no `advance_time` for event gaps — inter-step waiting is the
     runner's per-step frame cadence. (Recording timestamps stay in the trace file for
     future use; promotion just doesn't emit them.)
   - Extend `TracePromoterTests` beyond `Contains`: assert exact step count and order for
     a synthetic 240-motion-sample drag (expect press, one drag, release), and assert the
     draft asserts `lab_finite`.
5. End-to-end check of the loop without a human: record a trace by running the
   grab-throw journey with tracing enabled —
   `godot --headless --path . -- --journey=lab_grab_throw --seed=1 --trace-out=.artifacts/traces/session.json`
   (the driver records synthesized input too; that is the point of going through the real
   queue). Then
   `godot --headless --path . -- --promote-trace=.artifacts/traces/session.json --journey-out=tests/journeys/draft_probe.json`,
   then run the draft: `--journey=draft_probe --seed=1`. It must pass. Delete
   `tests/journeys/draft_probe.json` afterwards (drafts are throwaway; only hardened
   journeys are committed).
6. Update the `steps` arrays in the three existing journey JSONs to be honest: either
   delete them or rename the key to `"_documentation"` — they must not look like
   executable steps while the runner ignores them (they have `exercise` set, so the
   interpreter will skip them by design).

**Verify:** step 5's record→promote→run loop green headless and windowed; all existing
journeys still green; new promoter unit tests green.

**Done when:** a promoted draft runs unmodified; the collapse test pins step count; no
journey JSON carries dead `steps` data.

## Task 6 — Dual-profile lab: make the review workflow actually usable

`src/Laboratory/DualProfileLab.cs` deviations from parent Task 5: `Tab` toggles
`InteractiveBuddyIndex` but **nothing consumes it** (README documents a swap that does not
exist); there is no pointer/grab path in the dual scene at all, so the side-by-side feel
review (responsiveness, impulse propagation, knockout, recovery) cannot be performed;
`--seed` is ignored (both buddies hardcoded to seed 1); only the rig profile is swappable
(plan named the `ActiveDriveProfile` pair too); and the root has its own
`_PhysicsProcess`.

**Fix spec:**

1. **Grab path + toggle.** Add to `scenes/dual_profile_lab.tscn` a single
   `GrabTetherController` (reuse `res://data/buddy/lab_grab_tether.tres`) and a
   `LabPointerGrabComponent`, initialized from `DualProfileLab._Ready` exactly as
   `BuddyLab` does. Route them in the dual tick in the same order BuddyLab uses
   (pointer resolve → grab tick → buddy ticks). Consume `InteractiveBuddyIndex`: the
   pointer's pick must consider only the active buddy's parts. Cleanest hook given Task 1's
   pointer rework: give `LabPointerGrabComponent` an optional
   `Func<RigidBody2D, bool> PickFilter` (default null = allow all) and have
   `DualProfileLab` set `body => body is PuppetPartBody p && OwnedBy(p, ActiveBuddy)`
   (ownership via `IsAncestorOf`). Also feed `SetGrabContext` to the active buddy's
   `GrabResistance` each tick like BuddyLab does, and clear it on the inactive one.
2. **Seed from args.** In `_Ready`, `ulong seed = args.Seed ?? 1;` and reseed both buddies
   with it. `DualProfileSmokeScenario` already reseeds explicitly, so it stays valid.
3. **Drive profile pair.** Add `--drive-a=` / `--drive-b=` to `RunnerArguments` (same
   pattern as `profile-a/b`, add unit tests per Task 7) and apply to
   `buddy.ActiveDrive` the way `ApplyProfile` applies the rig profile. Check what profile
   property `ActiveDriveComponent` exposes; if it is not assignable pre-`_Ready`, apply in
   `_Ready` before reseed and document the ordering constraint in a comment.
4. **Tick ownership.** Keep `DualProfileLab` as its own composition root (restructuring it
   into `BuddyLab` is not worth the churn for a dev tool), but: mirror BuddyLab's routing
   order exactly, and add a paragraph to the STATE_AUDIT watch item's answer in
   `docs/DECISIONS.md` (one bullet under the spike section) recording the deliberate
   deviation: two dev-only roots exist (`BuddyLab`, `DualProfileLab`); the shared-routing
   factoring is deferred to M2 when `SandboxRoot` gets its tick, per the audit watch item.
5. **README truth.** Update the dual-profile paragraph: Tab now genuinely swaps grab
   targeting (after 1); document `--drive-a/--drive-b`; keep the acceptance-review
   criteria list as is.

**Verify:** `dual_profile_smoke --seed=1` and `--seed=7` pass;
`devtools\play_buddy_lab.bat --dual` manual check — grab buddy A, press Tab, confirm grabs
now hit buddy B only; both metrics labels update; telemetry export with `--artifacts`
produces `telemetry_dual_profile_a/_b.jsonl` + envelopes.

**Done when:** the review workflow is performable end-to-end on this machine; README
matches behavior; seeds/profiles all injectable.

## Task 7 — Small fixes batch (one commit, no behavior beyond the listed)

1. **Tick-path allocation** (`src/Laboratory/TelemetryRecorder.cs:57`):
   `link.LinkId.ToString()` marshals a new string per link per tick (~600/s). In
   `Initialize`, snapshot `string[] _linkIds` once from `buddy.Constraints.Telemetry`
   (LinkIds are fixed after rig build) and index into it in `Capture`.
2. **RunnerArguments tests**: `tests/.../Automation/RunnerArgumentsTests.cs` has zero
   coverage for `trace-out`, `promote-trace`, `journey-out`, `profile-a/b` (and Task 6's
   `drive-a/b`), including: values parse, `--trace-out`/`--promote-trace` set
   `AutomationRequested`, and `--promote-trace` without `--journey-out` (and vice versa)
   throws. Add them.
3. **Walk/jump budget derived, not guessed** (parent Task 3.2 said derive from the
   planner): in `JourneyRunner.ExerciseWalkJumpAsync`, compute the budget from the loaded
   autonomy profile instead of trusting the JSON:
   `budget = 8 * (profile.MaximumIdleTicks + profile.MaximumWalkTicks) + 2 * profile.MaximumJumpIntervalTicks`
   (≈ 3,840 with current tuning; generous multiple of the worst goal cycle), clamped to at
   most the JSON `timeout_physics_ticks` if that is larger. Expose the profile via
   whatever `AutonomousMotionComponent` already holds (`lab.Buddy.AutonomousMotion` — add
   a read-only profile getter if absent). Keep `timeout_physics_ticks` in the JSON as a
   hard cap; document the relationship in the JSON `description`.
4. **Enum readability in artifacts**: add `JsonStringEnumConverter` to
   `TelemetrySerializer.Options` so `consciousness` serializes as a name. Adjust the
   round-trip unit test.
5. **DECISIONS.md placement**: move the "Development Spike Observations" section below the
   "This file records only decisions explicitly confirmed by the project owner" preamble
   (it currently sits above it, reading as if exempt).
6. **Comment hygiene**: `IdleSoakScenario` header (done in Task 2), pointer "never
   headless" comment (done in Task 1) — sweep for any other comments contradicted by this
   plan's changes (`grep -rn "time scaling\|never headless" src/`).

**Verify:** build + domain tests green; `passive_rig --artifacts` envelope shows
`"consciousness":"conscious"`-style names in jsonl; re-run one grab scenario to confirm
no recorder regression.

## Task 8 — Transparent-window spike: perform the manual matrix (owner-in-the-loop)

The spike launches, but the observations the TEST_PLAN §8 gate actually needs —
transparency renders correctly, pointer mapping accurate — were **not performed**
(`docs/DECISIONS.md` says so itself). This is a manual, eyes-on task; an agent can only
prepare and prompt.

**Procedure to hand the owner (also append condensed form to the DECISIONS entry):**

1. `godot --path . res://scenes/spike_transparent_window.tscn` at 100% Windows display
   scale: confirm (a) desktop visible through the window background, (b) the three shapes
   opaque, (c) window stays on top, (d) the on-window `client x, y` readout tracks the
   pointer with no visible offset at all four window corners and center.
2. Switch Windows display scale to 150% (Settings → Display), repeat (d), note the
   reported `DPI scale` value and any offset/blurriness/Compatibility-renderer artifact.
3. Record in `docs/DECISIONS.md` under the existing spike section: transparency y/n,
   pointer accuracy per scale, artifacts observed, and whether the spike is kept or
   deleted (parent plan: throwaway by design — delete the scene+script after recording
   unless findings argue for keeping it as an M2 reference).

**Done when:** DECISIONS.md records actual observations for both scales and the
keep/delete decision; the M1 exit-checklist bullet can be checked honestly.

---

## Execution order and rationale

1. **Task 1** first — it un-breaks push CI (`grab_resistance`) and windowed parity;
   Task 6 builds on the reworked pointer.
2. **Task 2** next — second red scenario + CI wiring; Task 5's promoted-draft check runs
   journeys and benefits from the shared soak helper being settled.
3. **Tasks 3, 4** — small, independent, close correctness holes.
4. **Task 7** — mechanical; do before Task 5/6 commits pile up (it touches shared files
   lightly).
5. **Task 5** — the architectural one; largest diff, do it when the suite is already
   green so its regressions are unmistakable.
6. **Task 6** — dev-tool completion, depends on Task 1's pointer shape.
7. **Task 8** — anytime; requires the owner at the machine.

## Exit checklist

- [ ] Full headless suite green: every catalog scenario (soak via `idle_soak_ci`,
      full `idle_soak` once via `--fixed-fps` to prove the mechanism) and all four lab
      journeys, seeds 1 and 7.
- [ ] `lab_spawn_settle`, `lab_grab_throw`, `lab_walk_jump` green **windowed**.
- [ ] Push CI contains `idle_soak_ci` step; `full-soak` job uses the acceleration flag;
      CI green on the branch.
- [ ] Record→promote→run loop demonstrated (Task 5.5) and the probe draft deleted.
- [ ] Release export filter verified to ship tuning profiles and exclude lab-only data.
- [ ] `repeat_envelope` failure path demonstrated once (bound pinch test) and bounds
      tightened from measurements.
- [ ] Dual lab: Tab-swap grab works interactively; seeds/profiles injectable; README
      matches.
- [ ] DECISIONS.md: spike observations recorded (owner), dual-root deviation noted,
      section placement fixed.
- [ ] No comment in `src/` contradicts runtime behavior introduced by this plan.
