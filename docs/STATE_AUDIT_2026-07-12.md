# State Audit — 2026-07-12 (branch `opus`)

Verification of the working tree against `ROADMAP.md`, `ARCHITECTURE.md`, `TEST_PLAN.md`, and
`AGENT_VERIFICATION_AND_E2E.md`. Milestone 0 is complete; Milestone 1 is in progress.

## 1. Conformant — verified in code

- **Engine config (ARCHITECTURE §20).** `project.godot` has 120 Hz tick, `max_physics_steps_per_frame=6`,
  physics interpolation, transparency flags including `per_pixel_transparency/allowed`, borderless /
  always-on-top / 480x360 defaults, `gl_compatibility`, custom user dir `DesktopBuddy`, named collision
  layers exactly per the §20 table, and no Jolt/D3D12 template leftovers. Stretch settings absent =
  engine default `disabled`, as required.
- **Assembly split (§22).** `DesktopBuddy.Domain` (no Godot SDK), root `DesktopBuddy`,
  `DesktopBuddy.Domain.Tests` (xUnit), `Directory.Build.props`, `global.json`. Domain physics
  (`PassiveSpring`, `GrabTether`, `RecoveryClock`, `RoomLayoutPolicy`, `AutonomousMotionPlanner`) is
  pure `System.Numerics` code with unit tests.
- **Single fixed-tick entry point (§23).** Only `BuddyLab._PhysicsProcess` exists as a gameplay tick;
  it routes Boundaries → Grab → Buddy explicitly. No component registers its own `_PhysicsProcess`.
  `LaboratoryTelemetryPanel._Process` is presentation-only — allowed.
- **Body configuration (§23).** `PuppetPartBody`: `CanSleep=false`, `ContactMonitor=true`,
  `MaxContactsReported>=8`; `LooseObjectBody`: `CanSleep=true`. `PassiveRigScenario` asserts the
  contract at startup, as §23 demands.
- **Seeded RNG (§23).** `IRandomSource`/`SeededRandomSource` injected; no `GD.Rand*`/`new Random()`
  usage in `src/`. Lab reseed control present.
- **Grab tether (M1).** All six parts + one loose object acquire through one contract
  (`GrabReleaseScenario`); release-velocity cap in domain code; fear resistance scenario; hard
  recovery releases the grab (lab bridges tether↔recovery per DECISIONS fail-safe cleanup).
- **Lab controls (M1).** Pause, single-step (with freeze discipline + interpolation reset),
  time scale 0.05–4.0, consciousness toggle, reseed. Covered by `laboratory_controls` scenario.
- **Boundaries/zoom (§21).** `RoomLayoutPolicy` owns derived room size and the 360x270 floor;
  `room_resize_zoom` scenario in CI; boundary rebuild goes through one controller path.
- **Test protocol + CI (M0).** `--scenario=<id> --seed=<n>` and `--journey=<id>` runners with JSON
  verdicts/artifacts; CI runs build, domain tests, headless import, all nine scenarios, two journeys,
  Steam-binary guard; README documents the three commands.
- **Export hygiene.** `export_presets.cfg` excludes `buddy_lab.tscn`, `test_runner.tscn`, `tests/*`,
  docs. No Steam binaries tracked.

## 2. Problem — needs action

### P1. Uncommitted `project.godot` points main scene at an export-excluded scene
Working tree changes `run/main_scene` from `scenes/bootstrap.tscn` to `scenes/buddy_lab.tscn`
(editor convenience after the "editor friendly" commit). `buddy_lab.tscn` is in the export
`exclude_filter`, so an export from this tree ships a build whose main scene does not exist, and the
boot-smoke contract (bootstrap composition) no longer matches normal boot.
**Steer:** revert the working-tree change; keep bootstrap as main scene permanently. For editor
convenience use `tools/play_buddy_lab.bat`, the editor's "Run Specific Scene" (F6/Ctrl+R on the lab
scene), or a `--scene` launch config — never the project main scene.

### P2. Milestone 1 journey coverage is one of four
`AGENT_VERIFICATION_AND_E2E.md` §7 maps M1 to: spawn/settle ✅ (`lab_spawn_settle`), grab-throw each
part ❌, walk/jump observation ❌, time-accelerated 30-minute idle soak ❌. The soak also backs two
TEST_PLAN §3/§8 gate bullets (30-minute stability, repeated-run envelopes) that currently have no
automated coverage at all.
**Steer:** next M1 work should land `lab_grab_throw`, `lab_walk_jump`, and `lab_idle_soak` journeys.
For the soak, prefer headless fixed-step fast-forward per E2E §2 rather than `Engine.TimeScale`
(the lab control caps at 4.0x — a real-time 30-minute soak at 4x is still 7.5 minutes of CI wall
clock; fast-forward makes it tractable).

### P3. Record-and-promote input tracing not implemented
`AutomationDriver` still carries the Milestone 0 skeleton comment "semantic anchor resolution and
record-and-promote tracing land in Milestone 1". ROADMAP M1 explicitly lists input-trace recording.
Currently no trace capture, no anchor resolution, no promotion path.

### P4. Telemetry export for tolerance-envelope extraction missing
ROADMAP M1 requires "telemetry export for tolerance-envelope extraction". Today only per-run
`*.verdict.json` files exist (pass/fail + failure list); the lab telemetry panel renders live values
but nothing writes a time-series (strain, body speed, support state, drive force per tick) to an
artifacts file that envelope extraction could consume.

### P5. Side-by-side reference tuning workflow absent
ROADMAP M1 and the TEST_PLAN §8 gate ("side-by-side reference review accepts responsiveness…")
require a comparison workflow (two profiles side by side, or recorded reference vs candidate).
Nothing in the tree implements or documents it.

### P6. Transparent-window spike not started
ROADMAP M1 includes a minimal throwaway standalone transparent-window spike for the TEST_PLAN §8
"standalone transparent window and pointer mapping at default size" bullet. No spike scene/script
exists. Low urgency inside M1 but it is a §8 gate bullet, so economy work cannot start without it.

## 3. Watch items — not violations yet

- **Two composition roots with `_PhysicsProcess` potential.** `SandboxRoot` doc claims ownership of
  the single gameplay `_PhysicsProcess`; today it has none and `BuddyLab` owns the lab tick. Fine
  while the scenes never coexist, but when M2/M3 give `SandboxRoot` its tick, factor the shared
  routing (controls gate → boundaries → grab → buddy) so the two roots cannot drift apart.
- **`GrabResistanceComponent` lives under `src/Buddy/Behavior/`** while ARCHITECTURE §3 places it as
  a buddy component — placement matches; just confirm its future interaction with `BehaviorArbiter`
  (M3+) keeps "choosing intent" out of the drive path.
- **Exit criterion "lock an initial accepted tuning Resource"** still open — expected, since the
  envelope/soak/reference-review work (P2, P4, P5) is exactly what produces it.
- **`Consciousness` enum lives in Domain** (`domain/.../Buddy/Consciousness.cs`) and is consumed by
  Godot components — correct direction of dependency; keep future enums (MoodBand, InputMode…) in
  Domain the same way.

## 4. Suggested order of remaining M1 work

1. Revert the main-scene change (P1 — one line, do now).
2. Telemetry time-series export (P4) — prerequisite for envelopes.
3. Soak + repeated-run envelope scenario, then the three missing journeys (P2).
4. Record-and-promote tracing (P3) — unblocks journey authoring for grab-throw/walk-jump.
5. Side-by-side reference workflow (P5), then reference review.
6. Transparent-window spike (P6) — independent; any time before gate review.
