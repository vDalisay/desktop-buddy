# Milestone 1 Remediation Plan — Agent Handoff

Fixes the gaps recorded in `docs/STATE_AUDIT_2026-07-12.md` (P2–P6; P1 already reverted).
Execute tasks in order — later tasks consume earlier outputs. Read `docs/ROADMAP.md` (Milestone 1),
`docs/ARCHITECTURE.md` §7/§21–§23, `docs/TEST_PLAN.md` §3/§8, and
`docs/AGENT_VERIFICATION_AND_E2E.md` before starting.

## Ground rules (apply to every task)

- All mutation goes through the existing composition: `BuddyLab._PhysicsProcess` is the only
  gameplay tick; new components get explicit `Initialize()` + routed tick calls, never their own
  `_PhysicsProcess`.
- Pure logic (formats, extraction, policies) goes in `DesktopBuddy.Domain` with xUnit tests in
  `tests/DesktopBuddy.Domain.Tests`; Godot adapters stay thin in `src/`.
- No allocations on the 120 Hz tick path: preallocated buffers, `readonly record struct` payloads,
  no LINQ/closures in tick code.
- Exact durations count integer ticks at 120 Hz, never accumulated floats.
- Scenarios/journeys assert tolerance ranges, not bit-exact values; every one takes `--seed`.
- Each task ends with: `dotnet build DesktopBuddy.sln -c Debug` clean,
  `dotnet test tests/DesktopBuddy.Domain.Tests/DesktopBuddy.Domain.Tests.csproj` green, new
  scenario/journey passing headless locally, and a CI step added to `.github/workflows/ci.yml`
  mirroring the existing step style. Verify commands are in `README.md` and
  `devtools/verification/quick_validate.bat`.
- Development-only code (telemetry export, tracing) must stay out of release exports: guard with
  debug-build checks like the existing lab code and keep new lab-only scenes/data inside the
  existing `exclude_filter` paths or add to it.

---

## Task 1 — Telemetry time-series export (audit P4)

**Goal:** each scenario/journey run can write a per-tick telemetry series to the artifacts
directory so tolerance envelopes can be extracted (ROADMAP M1 "telemetry export for
tolerance-envelope extraction").

1. Domain: add `TelemetryFrame` (`readonly record struct`) under
   `domain/DesktopBuddy.Domain/Telemetry/` capturing per tick: tick index, per-part position /
   speed / angular speed, per-link separation + strain (from `PassiveSpringResult`), support/standing
   state, drive intent + applied force magnitude, tether strain + active flag, consciousness.
   Add a `TelemetryEnvelope` reducer (min/max/mean per metric over a run) — pure, unit-tested.
2. Domain: add a serializer producing one JSON-lines (`.jsonl`) file (one frame per line) plus one
   `envelope.json` summary. JSON-lines keeps memory flat for long soaks. Unit-test both.
3. `src/`: add `TelemetryRecorder` component (lab/testing-only). `BuddyLab._PhysicsProcess` feeds it
   after `Buddy.PhysicsTick()` when recording is enabled. Sources: `PuppetRig.Parts`,
   `PuppetConstraintComponent` link telemetry (`LinkTelemetry` already exists), `StandingDetector`
   snapshot, `ActiveDriveComponent`, `GrabTetherController.CurrentGrab`. Ring/streaming write —
   flush to disk off the tick (buffered writer, flush per N ticks is fine; no per-tick file I/O).
4. Wire `--artifacts=<dir>` (already parsed in `RunnerArguments`) so scenarios/journeys that opt in
   write `telemetry_<id>.jsonl` + `envelope_<id>.json` beside the verdict file.
5. Enable it in `passive_rig` and `standing_recovery` scenarios as first consumers; assert the files
   exist and the envelope parses in the scenario checks.

**Done when:** running any opted-in scenario with `--artifacts` produces verdict + jsonl + envelope;
domain tests cover frame serialization and envelope reduction; zero managed allocation added to the
steady-state tick (preallocate frame buffer; reuse).

## Task 2 — Idle-soak + repeated-run envelope scenario (audit P2, gate bullets)

**Goal:** TEST_PLAN §3 "30-minute accelerated idle soak" and "repeated runs stay inside approved
envelopes" get automated coverage.

1. Add `idle_soak` scenario to `ScenarioCatalog`: seeded spawn, no input, run 30 simulated minutes
   = 216,000 ticks. Accelerate by driving ticks directly headless (call the routed tick in a loop —
   fast-forward per E2E §2), **not** via `Engine.TimeScale` (lab control caps at 4x; wall-clock too
   slow for CI). If direct loop-driving fights the engine's fixed-step pump, use
   `Physics2DServer`-stepping or `--fixed-fps` + `Engine.TimeScale`; document the choice in the
   scenario header comment.
2. Assertions per TEST_PLAN §3: all bodies finite, connected (strain ≤ configured max), contained in
   room bounds, no part ever sleeping, at soak end buddy recognizable/standing-capable. Record
   telemetry (Task 1) and write the envelope artifact.
3. Add `repeat_envelope` scenario (or a `--repeat=N` runner flag): run the settle scenario N=5 times
   with the same seed and with N different seeds; assert outcome metrics (settle time, final pose
   spread, max strain) stay inside tolerance bands defined as exported tuning values, not literals —
   put the bands in a typed Resource under `data/buddy/` (e.g. `lab_envelope_bounds.tres`).
4. CI: add both scenario steps. If the 30-minute soak exceeds a few minutes of CI wall time, keep a
   3-minute variant on every push and the full 30-minute run behind a nightly/`workflow_dispatch`
   job — note whichever split you choose in the CI comments and README.

**Done when:** both scenarios pass repeatedly locally (run each 3x), CI has the steps, envelope
bounds live in a `.tres`, and failures print which metric left its band.

## Task 3 — Missing Milestone 1 journeys (audit P2)

**Goal:** E2E §7 M1 row complete: grab-throw each part, walk/jump observation, accelerated idle
soak journey.

Prereq: journey step vocabulary in `JourneyRunner` may need new steps — extend minimally per E2E §3
(`pointer_press/drag/release`, `wait_predicate`, `advance_time`, `assert_state`). Steps target
semantic anchors (buddy part IDs), resolved to coordinates at runtime — no hardcoded pixels.

1. `tests/journeys/lab_grab_throw.json`: for each of the six parts — pointer-press on the part's
   resolved position, drag across the room, release with velocity; assert grab acquired
   (tether active state), release velocity ≤ cap, buddy returns to standing within a tolerance
   window, rig stays connected. Reuse `GrabReleaseScenario` tolerances.
2. `tests/journeys/lab_walk_jump.json`: seeded autonomy enabled; wait-predicate for at least one
   walk segment in each direction and one jump-and-land within a generous simulated-time budget
   (autonomy is seeded, so the expected plan is deterministic — derive the budget from
   `AutonomousMotionPlanner` with that seed, don't guess); assert containment and standing recovery
   after landing.
3. `tests/journeys/lab_idle_soak.json`: journey wrapper over the Task 2 soak path (setup: seed,
   window size, zoom; steps: `advance_time` 30 simulated minutes; assertions from the envelope).
   Share the acceleration mechanism with Task 2 — one implementation, two entry points.
4. CI steps + README journey list update.

**Done when:** all three journeys pass headless with the documented command, and each also runs
windowed (omit `--headless`) without behavioral difference.

## Task 4 — Record-and-promote input tracing (audit P3)

**Goal:** ROADMAP M1 "input-trace recording for the record-and-promote workflow"; E2E §5.

1. Extend `AutomationDriver` (composed only when debug build + `--automation`): a recording mode
   that captures live pointer/key input with tick timestamps and resolves each sample against
   semantic anchors (nearest buddy part within grab radius, UI control name, else sandbox-relative
   point). Input synthesis/observation goes through the Godot input queue (`Input.ParseInputEvent`
   path) per E2E §2 — never call gameplay components directly.
2. Write traces as JSON to a `--trace-out=<path>` file. Raw traces are throwaway artifacts — do not
   commit them; add the trace output default directory to `.gitignore` if needed.
3. Promotion: a converter (Domain logic, unit-tested) that turns a trace into a journey draft —
   collapse pointer samples into `pointer_press/drag/release` steps with semantic targets, strip
   idle time, emit TODO markers where assertions must be authored. CLI entry:
   `--promote-trace=<in> --journey-out=<out>`.
4. Hardening remains manual per E2E §5 (replace residual coordinates, add seed/fixture/assertions).
   Document the loop in `AGENT_VERIFICATION_AND_E2E.md`-referencing form inside `README.md`.

**Done when:** record a manual lab session → promote → hand-harden into a runnable journey; the
converter has unit tests; release export contains none of it (verify export filter / debug guards).

## Task 5 — Side-by-side reference tuning workflow (audit P5)

**Goal:** ROADMAP M1 "side-by-side reference tuning workflow"; feeds TEST_PLAN §8 reference review.

Keep it cheap — this is a lab workflow, not a product feature:

1. Add a lab mode that instantiates two `BuddyRoot` compositions in one lab scene, offset
   horizontally, each with its own `PuppetRigProfile`/`ActiveDriveProfile` `.tres` (candidate vs
   reference), same seed, mirrored autonomous plans. Both tick from the single
   `BuddyLab._PhysicsProcess` routing. They must not collide with each other — either separate
   collision layers within `BuddyParts`' lab configuration or spatial separation with per-buddy
   room bounds; prefer spatial separation (two rooms) to avoid touching the layer table.
2. Profile pair selected by lab CLI args (`--profile-a=res://... --profile-b=res://...`) with
   sensible defaults; toggle key to swap which buddy receives pointer grabs.
3. Telemetry panel shows both buddies' key metrics side by side (reuse Task 1 recorder for export —
   two files, suffixed `_a`/`_b`).
4. Document the review procedure (what "responsiveness, bounded stretch, whole-body impulse
   propagation, sideways knockout, recovery feel" acceptance looks like) in a short section appended
   to this file or `docs/DECISIONS.md` when the review happens.

**Done when:** `devtools/play_buddy_lab.bat` variant (or flag) launches the dual view; both rigs run
seeded and stable; a scenario smoke-checks that the dual composition initializes and ticks 10
seconds without divergence-to-NaN.

## Task 6 — Transparent-window pointer-mapping spike (audit P6)

**Goal:** TEST_PLAN §8 bullet "standalone transparent window and pointer mapping work at default
size on Windows 10/11". Throwaway by design (ROADMAP M1) — production shell is Milestone 2.

1. Create `scenes/spike_transparent_window.tscn` + one script under `src/Laboratory/` (or
   `src/Spike/`): borderless transparent always-on-top window at 480x360, a few opaque shapes,
   pointer position readout proving client→sandbox coordinate mapping at 100% and one high-DPI
   scale. Godot APIs only — no Win32 subclassing (that is M2 work).
2. Add the scene to the export `exclude_filter`. No CI step — this is a manual Windows check.
3. Record results (transparency works y/n, pointer mapping accurate at tested DPI scales, any
   Compatibility-renderer artifacts) in `docs/DECISIONS.md` — these observations feed the M2
   renderer validation spike.
4. Delete or keep per its findings; the spike itself is not maintained code.

**Done when:** spike runs standalone on this Windows 11 machine, findings written to DECISIONS.md.

---

## Exit checklist (maps to TEST_PLAN §8 gate)

- [ ] Task 1–6 done in order; CI green on every push.
- [ ] Spawn, idle, walk, jump, drag, throw, fear-resistance, recovery scenarios pass (existing +
      new); knockout scenario is Milestone 3 — pain pipeline not in M1 scope, only the manual
      consciousness toggle.
- [ ] 30-minute stability + repeated-run envelopes pass with bounds stored in a typed Resource.
- [ ] Standalone transparent window + pointer mapping verified on this machine.
- [ ] Side-by-side reference review performed and accepted; outcome recorded.
- [ ] Accepted tuning locked as `.tres` under `data/buddy/` with regression coverage
      (ROADMAP M1 exit criterion) — final step, after the reference review accepts a profile.
