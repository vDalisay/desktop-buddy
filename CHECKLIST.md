# Desktop Buddy — Where To Start (agent handoff checklist)

Fast orientation for the next agent. Authoritative specs live in `docs/`
(`DECISIONS.md` wins conflicts). This file is a *status snapshot*, not a spec —
when it disagrees with a green test run, trust the run and update this file.

Last updated: 2026-07-24, after owner runtime verification of recovery,
Boxing Glove pointer-exit reactions, and Work/Play routing, plus closure of the
loose-ball settling regression.
**Start here: `docs/M3_6_EXPRESSIVE_PRESENTATION_PLAN.md`** — read its
Progress section, which is the authoritative per-task status.

## 1. Current position

- **Milestone 0 (Foundation): complete.**
- **Milestone 1 (Physics Laboratory): complete**, feel tuning owner-ACCEPTED
  2026-07-12 (`AcceptedM1*` profiles). Sections 3–4 below are M1 history.
- **Milestone 2 (Windows desktop shell): code complete; remaining work is
  owner-manual** — native adapter verification and the `TEST_PLAN.md` §5 standalone
  matrix.
- **Milestone 3 (Interaction and damage): complete, owner gate ACCEPTED.**
- **Milestone 3.5 (3D presentation): complete.** Mii3D is the shipping default
  since the Task 8 flip (`52b42b5`); `LegacyCircles` survives as a dev view behind
  `V` / `--presentation=legacy`.
- **Milestone 3.6 (expressive presentation): complete; owner accepted 2026-07-21.**
  Pose pipeline, facing, activities + item socket, head look-at, composed face,
  composition/regression/docs, and the owner feedback rework are complete.
- Milestone 4 behavior is next; economy/shop work remains Milestone 5 scope.

### Known red

Current targeted verification leaves no known red.

Resolved 2026-07-24:

- `impact_dedup`: the loose-object prototype now receives explicit replacement
  linear/angular damping (`1.5` / `2.0`) from its scenario configuration rather
  than inheriting insufficient project damping. The strict `<5 px/s` for 60
  consecutive ticks oracle is unchanged; seeds 1 and 7 in both presentation
  modes settle in 225 ticks at 2.5 px/s, while first-hit, resting-contact, reward,
  and re-arm checks remain green.
- `standing_recovery`: owner accepted the measured 228-tick assisted stand-up;
  the documented regression ceiling is now 240 ticks with no tuning change.
- `desktop_shell_modes`: live six-body Work Mode hit regions, early shell input
  observation, Play capture, drag following, Escape recovery, and transparent
  passthrough are covered by the now-green journey (seeds 1 and 7).

## 2. Green baseline (verify before you build on it)

Godot: use `tools\resolve_godot.bat` (or set `GODOT_PATH` explicitly); the resolver
supports repository-adjacent, Downloads, and `PATH` installs. **Close any `--editor` Godot on this project first** — it
deadlocks headless runs. Wrap each headless run in a hard timeout.

| Layer | Command | Status |
| --- | --- | --- |
| Domain unit | `dotnet test` | 638/638 green |
| Build | `dotnet build DesktopBuddy.sln -c Debug` | 0 warn / 0 err |
| Scenarios (41) | `<godot> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<n>` | 78/78 valid headless presentation runs green (`idle_soak` run as `idle_soak_ci`); window-only visual gate pending owner |
| Journeys (11) | `<godot> --headless --fixed-fps 120 --path . -- --journey=<id> --seed=<n> --artifacts=<dir>` | 21/21 valid presentation runs green |
| Quick suite | `tools\quick_validate.bat` | 15/15 |

Scenario ids live in `src/Testing/ScenarioCatalog.cs`; journey ids are the
filenames in `tests/journeys/`. Every scenario and journey is also rerun under
`--presentation=mii3d` and `--presentation=legacy`; the verdicts must match.

Gotchas that WILL fail a run if you forget them:
- **`--fixed-fps 120` is mandatory** for the presentation scenarios. Without it
  `activity_clips` fails at a rock-steady walk ratio 0.828 on every seed and every
  commit, which reads exactly like a deterministic code regression.
- **Soak** (`idle_soak`, `idle_soak_ci`, `lab_idle_soak`) needs `--fixed-fps 120`
  to free-run, else it takes wall-clock minutes.
- **`lab_idle_soak` journey needs `--artifacts=<dir>`** — it asserts
  `soak_envelope_written`, which is only produced when telemetry is enabled.
- Journeys `lab_spawn_settle`, `lab_grab_throw`, `lab_walk_jump` also have a
  **windowed** pass in the review-fixes exit criteria — that opens a real window
  and is owner-in-the-loop (don't drive it while the user needs the mouse).

## 3. Done in M1

- Six-body passive rig, spring/damper/max-stretch, upright + autonomous walk/jump,
  conscious/unconscious/passive drive, self-righting + safe recovery.
- Elastic grab tether (all six parts + one loose object), capped release, fearful
  resistance, hard-recovery grab release.
- Lab controls (pause/step/slow-mo/reseed/consciousness), seeded RNG service,
  boundaries + zoom (360×270 floor), telemetry time-series + envelope export.
- Record→promote→run trace loop, step interpreter, dual-profile lab with Tab-swap
  grab, transparent-window spike scene.

## 4. Remaining before the §8 gate

### Code work (agent-actionable)
- [x] **Bug: `repeat_envelope` seed-invariance — FIXED this session.** It broke each
      run at the first settle tick, sampling the pre-autonomy pose (near seed-invariant),
      so the 5-same/5-different-seed design tested nothing. Now each run drives a
      600-tick seeded autonomy window (`AutonomyObservationTicks`) after settle, then
      splits **same-seed** repeatability (runs 0–4, must cluster — measured 0.05–1.7px,
      bound 24) from **cross-seed** spread (measured 90–229px, bound 400) and adds a
      per-run finite+contained guard. Non-vacuous proven by a bound-pinch test. Bounds
      recorded in `data/buddy/lab_envelope_bounds.tres` (still provisional).
- [x] **Bug: deep-rest foot-contact blind spot — FIXED this session.** Circular feet
      spin at idle; `PuppetPartBody._IntegrateForces` wrongly rotated the (already
      world-space) contact normal by the body rotation, so a spun foot fell out of
      the support cone → `supports=0` → 12 s recovery clock → hard-reset teleport,
      every soak seed. Fix: use the world normal directly. Guarded by new
      `idle_soak_no_hard_recovery` check (`SoakProbe` now tracks hard-recoveries).
      `autonomous_motion` jump check also hardened to sample the apex, not takeoff.

### Owner-in-the-loop (an agent can only prep/prompt, not sign off)
- [x] **Transparent-window spike matrix** (`docs/M1_REVIEW_FIXES_PLAN.md` Task 8):
      owner confirmed 2026-07-12 "transparent window looks good" at 100% scale;
      **150% DPI pass ACCEPTED 2026-07-13** (standalone GUI, `gl_compatibility`, owner
      visual confirm). Recorded in `docs/DECISIONS.md`; keep/delete of the spike scene
      is now discretionary.
- [x] **Side-by-side reference review** (§8 bullet 4): first review 2026-07-12
      REJECTED; `docs/M1_FEEL_AND_GAIT_PLAN.md` Tasks 1–6 done; re-review 2026-07-12
      **ACCEPTED** by the owner ("feels way better, I approve"). Tuning locked:
      `lab_*.tres` profiles renamed `Provisional*` → `AcceptedM1*`; recorded in
      `docs/DECISIONS.md`. This closes the last engineering item on the §8 gate.
- [ ] **Windowed journey pass** for the three interactive journeys (review-fixes
      exit criterion). **Blocked by a new bug found 2026-07-12:** windowed
      `--journey` runs compose the lab but never execute/complete (no verdict, no
      quit; buddy just idles). Headless is unaffected. Needs its own fix session;
      note the memory gotcha — an open Godot editor may interact with second
      instances, so rule that out first when debugging.

### Gate close-out
- [x] **Accepted tuning Resource locked** (2026-07-12): `lab_*.tres` profiles renamed
      `AcceptedM1*` after the owner's feel acceptance. ROADMAP M1 exit criterion met.
      (`lab_envelope_bounds.tres` stays provisional — regression tolerances, not a
      feel profile.)

### §8 gate remaining (owner-manual, not code)
- [x] Transparent-window 150% DPI pass + corner pointer checks — **ACCEPTED
      2026-07-13**, recorded in `docs/DECISIONS.md`. Renderer decision gate closed;
      `gl_compatibility` accepted, HUD work unblocked.
- [ ] Windowed automated journey pass — blocked by the windowed-journey-hang bug
      (chip `task_6f8d585a`); manual windowed play already exercised the grab path.

## 5. Suggested next step

**Milestone 4 behavior.** Detailed architecture and task breakdown now live in
`docs/M4_PERSONALITY_CARE_PERSISTENCE_PLAN.md` (2026-07-24): all six owner
decisions are resolved in `docs/DECISIONS.md`; implementation begins at Task 0.
M3.6 Task 6 and the owner-feedback rework are complete and
owner-accepted. The final pass includes the real `E` Eat interaction, two-hand five-bite
motion, frontal food facing, final hand lowering, passive airborne grabs, full-body wall
detection, immediate grounded stopping, and sub-0.5-second head recovery after the calm
delay. See `docs/OWNER_FEEDBACK_2026_07_20_FIX_PLAN.md` for the checked-off acceptance list.

### Older milestone history (kept for context)

Milestone 2 (Windows desktop shell) — see `docs/M2_DESKTOP_SHELL_PLAN.md`
for the ordered task breakdown. Landed so far (headless-testable foundation, suite
green):
- Task 1 `WindowPlacementPolicy` (Domain): first-launch lower-right, off-screen
  recovery, monitor clamping. xUnit-covered.
- Task 2 `InputModeStateMachine` (Domain): Work/Play transition rules. xUnit-covered.
- Task 3 window-service seam (`IDesktopWindowService`, `WindowSettings`,
  `IWindowsDesktopAdapter`, `EmulatedWindowsDesktopAdapter`, `DesktopWindowController`):
  builds green; native adapter deferred to Task 4.

Task 5 (compose the shell into the sandbox boot: `SandboxRoot` gained its gameplay
tick + a real box boundary + `DesktopShellController`; `sandbox.tscn` rebuilt), Task 6
(`tests/journeys/desktop_shell_modes.json`, 8 predicates green), and the Task 4 native
adapter **skeleton** (`WindowsDesktopAdapter` + factory; WndProc `HTTRANSPARENT`
hit-testing, monitor topology, per-monitor DPI; selected only on a real Windows run,
emulated everywhere else) are done. Task 0 renderer decision is **closed** (150% DPI
pass accepted 2026-07-13, `gl_compatibility` accepted, HUD work unblocked). Remaining
owner-manual on real Windows: Task 4 verification + next cut (sandbox→client hit-region
mapping, tray/hotkey/launch-at-login, §24 lifecycle messages), Task 7 the
`TEST_PLAN.md` §5 standalone matrix. To verify Task 4: run the standalone build and look
for `[WinAdapter] Native adapter attached …` +
`DesktopWindowController ready (native=True …)`.

## 6. Ground rules (from the plans — still apply)

- Single routed gameplay tick; pure logic in `DesktopBuddy.Domain` with xUnit tests;
  no allocations on the 120 Hz path; integer tick counting; tolerance-band asserts;
  debug-only guards on dev code.
- A task is not done until the suite above runs green locally — **"done without
  running the suite" is the failure mode these plans exist to prevent.**
- Don't invent product behavior; pause and ask the owner per `AGENTS.md` / NFR-006.5.
