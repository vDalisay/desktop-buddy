# Desktop Buddy — Where To Start (agent handoff checklist)

Fast orientation for the next agent. Authoritative specs live in `docs/`
(`DECISIONS.md` wins conflicts). This file is a *status snapshot*, not a spec —
when it disagrees with a green test run, trust the run and update this file.

Last updated: 2026-07-12 (branch `opus`).

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
- [ ] **Bug: `repeat_envelope` seed-invariance.** It only measures the pre-autonomy
      settle, so it can't catch drift once autonomous motion starts. Extend it to
      sample across the autonomous phase. (Filed follow-up chip.)
- [x] **Bug: deep-rest foot-contact blind spot — FIXED this session.** Circular feet
      spin at idle; `PuppetPartBody._IntegrateForces` wrongly rotated the (already
      world-space) contact normal by the body rotation, so a spun foot fell out of
      the support cone → `supports=0` → 12 s recovery clock → hard-reset teleport,
      every soak seed. Fix: use the world normal directly. Guarded by new
      `idle_soak_no_hard_recovery` check (`SoakProbe` now tracks hard-recoveries).
      `autonomous_motion` jump check also hardened to sample the apex, not takeoff.

### Owner-in-the-loop (an agent can only prep/prompt, not sign off)
- [ ] **Transparent-window spike matrix** (`docs/M1_REVIEW_FIXES_PLAN.md` Task 8):
      run `scenes/spike_transparent_window.tscn` at 100% and 150% DPI, confirm
      transparency + pointer mapping, record in `docs/DECISIONS.md`, keep/delete.
- [ ] **Side-by-side reference review** (§8 bullet 4 + Task 6 verify): use the dual
      lab (`tools/play_buddy_lab.bat --dual`), Tab-swap grab between buddies, accept
      responsiveness / bounded stretch / whole-body impulse / sideways knockout /
      recovery feel against the v1.01 reference.
- [ ] **Windowed journey pass** for the three interactive journeys (review-fixes
      exit criterion).

### Gate close-out
- [ ] Lock the initial **accepted tuning Resource** once the reference review passes
      (ROADMAP M1 exit criterion; tighten `data/buddy/lab_envelope_bounds.tres` and
      drop the "provisional" marker).

## 5. Suggested next step

Take the `repeat_envelope` seed-invariance bug — it's the last agent-actionable
correctness hole in the §8 stability bullet and needs no owner. Then the remaining
gate items are all owner-in-the-loop; prep the spike matrix + dual-lab review so the
owner can run them in one sitting.

## 6. Ground rules (from the plans — still apply)

- Single routed gameplay tick; pure logic in `DesktopBuddy.Domain` with xUnit tests;
  no allocations on the 120 Hz path; integer tick counting; tolerance-band asserts;
  debug-only guards on dev code.
- A task is not done until the suite above runs green locally — **"done without
  running the suite" is the failure mode these plans exist to prevent.**
- Don't invent product behavior; pause and ask the owner per `AGENTS.md` / NFR-006.5.
