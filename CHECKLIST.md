# Desktop Buddy — Where To Start (agent handoff checklist)

Fast orientation for the next agent. Authoritative specs live in `docs/`
(`DECISIONS.md` wins conflicts). This file is a *status snapshot*, not a spec —
when it disagrees with a green test run, trust the run and update this file.

Last updated: 2026-07-31, after M5 Task 5's gun-feel refinement Tasks A and B
(defect verification, projectile alignment, aim-gated trigger, aim v2 smoothed pursuit).
**Start here: `docs/M5_SHOP_AND_TOOL_CATALOGUE_PLAN.md`** for current M5 work;
`docs/M4_PERSONALITY_CARE_PERSISTENCE_PLAN.md` records the completed,
owner-accepted M4 implementation.

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
- **Milestone 4 (Personality, care, persistence): complete; owner accepted
  2026-07-27.** Post-acceptance persistence/lifecycle corrections landed
  2026-07-29.
- **Milestone 5 (Shop and full tool catalogue): in progress.** The Baseball
  slice is present; the remaining ordered work is in the M5 plan.

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
| Domain unit | `dotnet test` | 979/979 green |
| Build | `dotnet build DesktopBuddy.sln -c Debug` | 0 warn / 0 err |
| Scenarios (49) | `<godot> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<n>` | Current targeted lifecycle scenarios green; the latest full both-presentation catalogue result remains the recorded M4/M5 baseline |
| Journeys (14) | `<godot> --headless --fixed-fps 120 --path . -- --journey=<id> --seed=<n> --artifacts=<dir>` | `care_persistence` real-input two-process journey green; latest full matrix green |
| Quick suite | `tools\quick_validate.bat` | 26/26 |

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

**Milestone 5 catalogue work.** Follow
`docs/M5_SHOP_AND_TOOL_CATALOGUE_PLAN.md`. Task 0 (catalogue spine) is done: the
15 `data/catalogue/*.tres` definitions, the engine-free `ToolCatalogue`/
`CataloguePolicy` rules, and the authoritative `EconomyService.Purchase(contentId)`
boundary. Progression reset is deliberately **not** implemented — it waits on the
owner's erase/preserve matrix. Task 2 (FR-014 budget) extracted the cap rule to
`Domain/Interaction/LooseObjectAdmissionPolicy` and gated it with `object_budget`;
its projectile half waits for the Task 5 guns. Task 3 (Meal) is owner-ACCEPTED
(2026-07-30) and shop-visible. Task 4's Home-Run Bat refinement is also
owner-ACCEPTED (2026-07-30) and shop-visible. Task 5 (cursor-gun platform + Pistol) was
**rejected on feel** and then refined to acceptance: its plan
`docs/M5_TASK5_GUN_FEEL_AND_REAL_PISTOL_PLAN.md` is **complete and owner-ACCEPTED
(2026-07-31)**, with both `tool_nerf_blaster.tres` and `tool_pistol.tres` now shop-visible
at `12` and `30` credits, and the §4.1 aim constants accepted as authored rather than
co-tuned. Task 6 (Grenade) is **complete and owner-ACCEPTED (2026-07-31)** and shop-visible
at `40`. Task 5 also discharged Task 2's
deferred projectile assertion. Baseball is owner-ACCEPTED
(2026-07-29). The quick suite is now 26 steps — added `corner_scoop` (pickup against
a wall), `object_budget`, `meal_consume`, `bat_swing`, `pistol_fire`, and the `m5_meal`,
`m5_baseball_bat`, `m5_homerun_bat`, and `m5_pistol` real-input journeys.

The Boxing Glove mechanism is now the shared **cursor-tool** mechanism
(`CursorToolController` + authored `CursorToolProfile` array); a cursor-tethered tool
is a `.tres` plus a content ID. Elongated tools hold square to their swing through
the new engine-free `Domain/Physics/AlignmentTorque`. Lab key `K` selects the bat,
`B` the glove.

Cursor **guns** run the same shape: `CursorGunComponent` + an authored `GunProfile`
array, with the rules in the engine-free `Domain/Tools/CursorAimModel` and
`Domain/Tools/GunModel`, so the Shotgun is a `.tres` plus a content ID. Two guns ship on
it: the **Nerf Blaster** (`tool.nerf_blaster`, `ToolId 14`, lab key `N`) is the toy the
player owns first and the **Pistol** (`J`) is the real one, and nothing separates them but
their authored numbers — a point-blank dart scores measured zero pain where a bullet scores
`13`. The launch catalogue is therefore sixteen entries, with the blaster at progression
slot 7 ahead of the Pistol. Both are drawn at four times the old barrel's size by
`Presentation3D/GunMeshBuilder` + `CursorGunVisual3D` (procedural vertex-coloured boxes, no
imported art), with a flat version of the same silhouette in the legacy 2D view. The grip
sits at the cursor, so rounds are born 53–61 px ahead of the pointer — anything aiming a gun
in a test must add the barrel to its stand-off. Two traps are recorded in `DECISIONS.md` for anyone tidying the
projectile up: `RigidBody2D.ContinuousCd` is deliberately **off** because it destroys the
momentum the pain pipeline scores from (no-tunneling is guaranteed by a validated per-tick
travel bound instead), and `LockRotation` is deliberately **off** because locking it halves
the impulse a hit scores — a bullet's spin is fixed in the drawing, not in the body.

The aim is a **smoothed pursuit** (M5 Task 5 refinement, Tasks A/B): a smoothed pointer
velocity, a speed gate with hysteresis, and a bounded turn rate, all authored per gun. It
follows the direction the pointer has lately been travelling instead of the latest delta,
which is what stopped it snapping between 0/26/45 degrees. Consequence for tests: a gun
cannot be aimed by teleporting its cursor — use `M4ObjectScenarioSupport.AimGunOver` or
`JourneyRunner.AimAtPointAsync`. A press with no established aim no longer spends a round.
Task C pinned the feel in `pistol_fire` (17 checks now): sub-pixel travel steers the aim,
release jitter never flips it, and a reversal costs 39 ticks — bounded below by the authored
turn rate so it cannot snap, and above by that plus three smoothing half-lives.

**Home-Run Bat refinement** (`docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md`):

- [x] Task A — pure charged-swing domain model and trajectory servo.
- [x] Task B — composition, weak free-swing, handle grip, immutable impact
      admission plumbing, and the cumulative `homerun_bat_feel` scenario.
- [x] Task C — charge accrual, cursor-travel direction, render-only shake, and
      staged one-/three-/five-second tip glimmers in both presentation modes.
- [x] Task D — physical charged swing, measured impulse separation, pivot hold,
      and the single-hit epoch gate.
- [x] Task E — whole-game hit lag and victim shake.
- [x] Task E2 — procedural placeholder audio. This is deliberately provisional:
      four clean-room PCM cues are synthesized at startup and remain an explicit
      replacement seam; no sampled sound belongs in this implementation.
- [x] Task F — honest shaded 3D bat presentation: the clean-room lathed mesh
      stays inside the capsule collider and carries authored wood/black-grip
      colours through the existing shadowless per-pixel lighting rig.
- [x] Task G — interactive verification and trace capture. The live pass found
      and fixed the lathed mesh's reversed 2D→3D long axis; automation now pins
      the wooden barrel and glint to the physical tip and the black wrap to the
      handle.
- [x] Task H — promoted journey, documentation, complete regression, and owner
      feel gate.
  - [x] Engineering gate — `m5_homerun_bat` drives the real input queue through
        an exact `600` routed-tick charge, one attributed impact, a `60`-tick
        whole-game freeze, resumed launch, and recovery. The 24-step quick suite,
        the 940-test domain suite, both bat scenarios, presentation regressions,
        and both bat journeys pass in Mii3D and legacy.
  - [x] Owner-feedback implementation — the charged handle reaches the floor
        while the capsule remains collision-blocked; full tip speed is `6000`
        px/s; tip glints are `7/12/18` px at `1/3/5` seconds; an accepted
        home-run emits one small solver-point impact burst. Focused feel,
        presentation, tool-feedback, weak-bat, and both real-input journey
        regressions pass in Mii3D and legacy.
  - [x] Owner gate — revised feel accepted ("it's great"); acceptance is recorded
        in `docs/DECISIONS.md` and the catalogue entry is visible.

The buddy now has a hidden `200`-point hunger bar (owner decision 2026-07-29,
`DECISIONS.md`): it eats what fits, and refuses what would overfill. The refusal is
the performance the owner asked for — the item held in one hand, the buddy turned to
the player, a smooth damped left/right head yaw around the neck (four alternating
extremes maximum, no center pause, neutral finish), then the item put down below itself — and
it leaves that item alone until it has room. Food reuse cooldowns are gone; the save
schema is `5`.

M5 Task 6 (Grenade) is **complete and owner-accepted** (2026-07-31) against
`docs/M5_TASK6_GRENADE_PLAN.md`:

- [x] Task A — `GrenadeFuseModel` (Domain): `Pinned → PinPulled → Live → Detonated`, the
      one-way pin, held-forever safety, a `360`-tick fuse from either release path, and a
      re-grab that changes nothing. Domain suite `987 → 999`.
- [x] Task B — the grenade is a launchable profile on the existing pullback launcher (spawn
      key `7`), the pullback's first secondary press pulls the pin through the queued-input
      path, the dropped pin is a pooled cosmetic body on the ejected magazine's rules, and a
      live grenade is registry-protected until it goes off.
- [x] Task C — the blast applies an equivalent impulse per buddy part through the *shared*
      pain curve (a sibling entry on `InteractionDamageComponent`, not a parallel pipeline),
      shoves every dynamic body on `BuddyParts | LooseObjects`, and despawns. Measured
      point-blank `186`–`191` total pain across seeds `1/7/13`, which is about five solid
      aimed pistol bullets and knocks the buddy out.
- [x] Task D — lathed olive-drab mesh with cap, lever, and a pin ring that disappears with
      the pin; a matching flat silhouette in legacy mode; additive flash plus a ring sized to
      the real blast radius; the pistol's camera lane at the grenade's bigger `4.0 px`/`14`
      tick numbers; and two clean-room synthesized cues, the thud gated so a rolling grenade
      stays quiet.
- [x] Task E — `grenade_fuse` (14 checks, seeds `1/7/13`, both presentations) and the
      `m5_grenade` journey (8 assertions, seeds `1/7`, both presentations), both registered
      in `ScenarioCatalog`, `TEST_PLAN.md`, and the quick suite (now 28 steps).
- [x] Task G — the owner's first feel-gate pass, 2026-07-31: the grenade and its pin now
      read as models rather than as lumps (drawn at `1.75 x` the collider radius in both
      modes, grooved body, folded lever, wire-ring pin, and a real 3D mesh for the *dropped*
      pin, which had been flat canvas art in both modes); the explosion is four layers
      instead of two — white-hot core, swelling-and-cooling fireball, fourteen thrown embers,
      and the same shock ring; and `ShoveImpulseAtCenter` is doubled `900 → 1800`, which
      moves knockback alone and leaves the pain curve untouched.
- [x] Task F — **owner-accepted 2026-07-31** on real Windows after the Task G pass.
      `tool_grenade.tres` is `Visible = true` and the Grenade is on sale at its authored
      `40` credits; the `m5_grenade` journey's first leg became the real purchase it always
      owed, instead of asserting the invisible entry's refusal. The three flagged defaults
      in the plan's §3 (a live grenade stays live, "five bullets" is the solid reading,
      buy-once) were taken as written and are recorded in `DECISIONS.md`.

M5 Task 8 (Soccer Ball + Drink) is **implemented, pending the owner's feel gate**, against
`docs/M5_TASK8_SOCCER_BALL_AND_DRINK_PLAN.md`, whose §3 defaults were all accepted
pre-implementation on 2026-07-31:
- [x] Task A — the restitution seam. `LooseObjectProfile.Bounce` (`0..1`, default `0.0`,
      validated finite and in range) applied through a `PhysicsMaterial` at profile-apply
      time. A profile authoring no bounce is given no material at all, so every `.tres` that
      predates the field is bit-identical: `baseball_pullback`, `meal_consume`, `grenade_fuse`,
      `consume_care_cooldown`, and `object_budget` all green with unmoved readings, and the
      domain baseline is unmoved at `999/999` because this is authoring-layer, not domain.
- [x] Task B — two presets and their wiring. `data/objects/soccer_ball.tres` (radius `14`,
      mass `0.9`, bounce `0.65`, damp `0.3`/`0.8`) on spawn key `8` with its **own**
      `PullbackLauncherProfile` (`data/tools/pullback_launcher_soccer_ball.tres`,
      `VelocityPerPullPixel 11.5`, `MaxLaunchSpeed 1400`) through the new optional
      `LooseObjectProfile.Launch` override; `data/objects/drink.tres` (`+5` mood, `7200`-tick
      cooldown, `ConsumeHungerFill 0`) on key `9`. Both in the lab and sandbox launchable
      arrays and both granted at lab boot. Measured drop signature from `240 px`: Baseball
      `0` rebounds / `0.0 px` / `153` ticks, Soccer Ball `6` / `82.1 px` / `417` ticks;
      measured full-pullback launch `1035 px/s`.
- [x] Task C — the `soccer_and_drink` scenario, 8 checks, seeds `1/7/13`, both presentations.
- [x] Task D — the `m5_soccer_ball` (6 assertions) and `m5_drink` (7 assertions) real-input
      journeys, seeds `1/7`, both presentations; all three registered in `ScenarioCatalog` /
      `tests/journeys/`, `TEST_PLAN.md`, and the quick suite (now 31 steps).
- [x] Owner feedback pass, 2026-08-01 — three instructions, each data plus one pure model:
      the buddy now **plays football** with the ball (`Domain/Autonomy/SoccerPlayModel`, a
      priority-5 sibling of the catch lifecycle: a low ball rolling in is reserved from the
      pickup through the existing `Ignored` channel and given the anti-kick collision
      exception, trapped dead under the nearer foot, held one authored second, then kicked
      back the way it came at one of three seeded loft angles); the **Drink is raised once**
      and held two seconds instead of bitten five times (`Domain/Presentation/ConsumeGesture`
      now owns both schedules, with the Meal's arithmetic restated exactly so its measured
      signatures are bit-identical); and both items are **drawn as models**
      (`Presentation3D/LooseObjectVisual3D` + `LooseObjectMeshBuilder`, a panelled ball and a
      generic red-and-white can, degrading to the flat circle in legacy). Domain suite
      `999 → 1057`; quick suite 31 steps green.
- [x] Owner feedback pass 2, 2026-08-01 — the Soccer Ball became **foot-only** outside the
      later pass-6 corner-rescue exception. Player touch enables trapping, floor contact preserves it,
      side-wall/ceiling contact clears it, and one direct kick remains available without a
      trap. Domain `1059/1059`; `soccer_and_drink` seeds `1/7/13` and `m5_soccer_ball`
      seeds `1/7` pass in both presentations.
- [x] Owner feedback pass 3, 2026-08-01 — a Content/Delighted buddy chases and repeatedly
      kicks a free Soccer Ball, seeded between forward and arcing shots; its original near-wall
      choice was superseded by pass 6's deterministic rescue. A player-held ball instead makes
      the buddy watch and back into receive space while
      player ownership remains intact. Domain `1063/1063`; `soccer_and_drink` seeds `1/7/13`
      and `m5_soccer_ball` seeds `1/7` pass in both presentations; `baseball_pullback`,
      `object_catch_hold`, and `autonomous_motion` neighbors remain green.
- [x] Owner feedback pass 4, 2026-08-01 — receive no longer stops at a fixed distance: while
      held, the football remains the head/eye target through a `600`-tick retreat / `120`-tick
      pause cadence that repeats until release; release immediately restores chase. Football
      is filtered from both ambient obstacle-hop evidence paths while Baseball still trips the
      ray. Domain `1063/1063`; the same seed/presentation matrix and three neighbors remain green.
- [x] Owner feedback pass 5, 2026-08-01 — corrected the weak semantic-only gaze check. Item
      gaze now reaches the rendered head without an activity clip, Delighted opens its eyes
      while tracking, and pupil travel visibly crosses toward the target with correct vertical
      coordinates. Windowed frame inspected; `soccer_and_drink` full matrix, `m5_soccer_ball`
      full matrix, `lookat_priority_and_cone`, and `face_composition` are green.
- [x] Owner feedback pass 6, 2026-08-01 — receive travel continuously preserves semantic and
      rendered ball gaze; a cornered football alone may be picked up, carried/faced inward while
      watched, placed in front, and kicked away from the wall. Domain `1064/1064`;
      `soccer_and_drink` seeds `1/7/13` and `m5_soccer_ball` seeds `1/7` pass in both
      presentations.
- [x] Owner feedback pass 7, 2026-08-01 — the real rendered frame exposed an edge-on face and
      highlight-sized eyes despite correct gaze state. Item head yaw is now world-relative and
      item attention uses readable wide white eyes with dark moving pupils. Windowed capture
      inspected; `soccer_and_drink`, `face_composition`, and `lookat_priority_and_cone` pass.
- [x] Owner feedback pass 8, 2026-08-01 — replaced the Soccer Ball's random dark facet blocks
      with a smoother white sphere and twelve evenly distributed raised black pentagons. Held
      and unobstructed windowed frames were inspected; the mesh stays inside its envelope
      (`14.4 / 25.2 px`) and `soccer_and_drink` passes.
- [ ] Task E — owner feel gate. `tool_soccer_ball.tres` and `tool_drink.tres` are still
      `Visible = false` and the two entries may flip independently; both journeys assert the
      invisible entry's refusal until then, on the Grenade's precedent. The bounce value and
      the ball's launch tuning are the numbers the gate owns. `DECISIONS.md` records the
      three §3 defaults and the restitution seam.

**Next action:** start **M5 Task 7 (Burning + Fire Sprayer)** against
`docs/M5_TASK7_BURNING_AND_FIRE_SPRAYER_PLAN.md`, whose owner-gate defaults were accepted
pre-implementation on 2026-07-31 and which builds the FR-017.3 `EffectsSettings` seam that
Tasks 8–10 then ride.

Everything before it is closed: `docs/M5_TASK5_GUN_FEEL_AND_REAL_PISTOL_PLAN.md` is complete
and owner-accepted (both guns shop-visible; §4.1's aim constants accepted as authored rather
than co-tuned, so the Task F session was never needed), and M5 Task 6 (Grenade) is complete
and owner-accepted above. The Home-Run Bat Task H
refinement is complete, owner-accepted, and shop-visible. M4 is complete and owner-accepted.
Its post-acceptance hardening records
the exact fun boredom latch in schema 4, lossless lifecycle bucket transitions,
an ordered clean-exit final save, lowest-mood tracking on damage, and a real-input
two-process persistence journey.

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
