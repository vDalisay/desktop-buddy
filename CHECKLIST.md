# Desktop Buddy — Where To Start (agent handoff checklist)

Fast orientation for the next agent. Authoritative specs live in `docs/`
(`DECISIONS.md` wins conflicts). This file is a *status snapshot*, not a spec —
when it disagrees with a green test run, trust the run and update this file.

Last updated: 2026-07-12 (branch `opus`), after the M1 feel tuning was accepted
and Milestone 2 kicked off. **Start here: `docs/M2_DESKTOP_SHELL_PLAN.md`** — the
active work is the Windows desktop shell; its Tasks 1–3 (headless-testable
foundation) have landed.

## 1. Current position

- **Milestone 0 (Foundation): complete.**
- **Milestone 1 (Physics Laboratory): in progress**, closing on the `TEST_PLAN.md`
  §8 exit gate. The remediation + review-fixes plans
  (`docs/M1_REMEDIATION_PLAN.md`, `docs/M1_REVIEW_FIXES_PLAN.md`) are **implemented
  and committed** (`8065632`, `d261fa3`, `356bc8e`).
- Economy/shop work (M5) is **blocked** until every §8 gate bullet is true and an
  initial accepted tuning Resource is locked. Do not start it early.

## 2. Green baseline (verify before you build on it)

Godot: `%USERPROFILE%\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64_console.exe`
(or `$GODOT_PATH`). **Close any `--editor` Godot on this project first** — it
deadlocks headless runs. Wrap each headless run in a hard timeout.

| Layer | Command | Status |
| --- | --- | --- |
| Domain unit | `dotnet test tests/DesktopBuddy.Domain.Tests/DesktopBuddy.Domain.Tests.csproj` | 58/58 green |
| Build | `dotnet build DesktopBuddy.sln -c Debug` | 0 warn / 0 err |
| Scenarios (13) | `<godot> --headless --path . -- --scenario=<id> --seed=<n>` | all green, seeds 1 (+ 7 on soak & autonomous_motion) |
| Journeys (4) | `<godot> --headless --path . -- --journey=<id> --seed=<n>` | all green headless |

Scenario ids: `boot_smoke, passive_rig, standing_recovery, autonomous_motion,
laboratory_controls, grab_release, grab_resistance, grab_hard_recovery,
room_resize_zoom, idle_soak, idle_soak_ci, repeat_envelope, dual_profile_smoke`.

Gotchas that WILL fail a run if you forget them:
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
- [~] **Transparent-window spike matrix** (`docs/M1_REVIEW_FIXES_PLAN.md` Task 8):
      owner confirmed 2026-07-12 "transparent window looks good" at current display
      scale. Still open: 150% DPI pass, corner-readout pointer checks, recording
      both in `docs/DECISIONS.md`, keep/delete decision.
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
- [ ] Transparent-window 150% DPI pass + corner pointer checks (100% already
      confirmed good) — record in `docs/DECISIONS.md`.
- [ ] Windowed automated journey pass — blocked by the windowed-journey-hang bug
      (chip `task_6f8d585a`); manual windowed play already exercised the grab path.

## 5. Suggested next step

Milestone 2 (Windows desktop shell) is underway — see `docs/M2_DESKTOP_SHELL_PLAN.md`
for the ordered task breakdown. Landed so far (headless-testable foundation, suite
green):
- Task 1 `WindowPlacementPolicy` (Domain): first-launch lower-right, off-screen
  recovery, monitor clamping. xUnit-covered.
- Task 2 `InputModeStateMachine` (Domain): Work/Play transition rules. xUnit-covered.
- Task 3 window-service seam (`IDesktopWindowService`, `WindowSettings`,
  `IWindowsDesktopAdapter`, `EmulatedWindowsDesktopAdapter`, `DesktopWindowController`):
  builds green; native adapter deferred to Task 4.

Task 5 (compose the shell into the sandbox boot: `SandboxRoot` gained its gameplay
tick + a real box boundary + `DesktopShellController`; `sandbox.tscn` rebuilt) and
Task 6 (`tests/journeys/desktop_shell_modes.json`, 8 predicates green) are also done.
Remaining are owner-manual gates only: Task 0 renderer visual matrix (150% DPI pass
still open), Task 4 native Windows adapter verification (real P/Invoke), Task 7 the
`TEST_PLAN.md` §5 standalone matrix. The renderer decision still blocks HUD work and
dovetails with the pending 150% DPI spike check.

## 6. Ground rules (from the plans — still apply)

- Single routed gameplay tick; pure logic in `DesktopBuddy.Domain` with xUnit tests;
  no allocations on the 120 Hz path; integer tick counting; tolerance-band asserts;
  debug-only guards on dev code.
- A task is not done until the suite above runs green locally — **"done without
  running the suite" is the failure mode these plans exist to prevent.**
- Don't invent product behavior; pause and ask the owner per `AGENTS.md` / NFR-006.5.
