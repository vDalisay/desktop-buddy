# Desktop Buddy — Agent Verification and End-to-End Journey Testing

Status: Supplemental workflow specification. `TEST_PLAN.md` remains authoritative for acceptance gates. This document defines how implementation agents verify running builds interactively, and how that verification is converted into an autonomous end-to-end suite that runs and plays the game without any agent present.

## 1. Two Tiers

| Tier | Name | Runs | Gate status |
| --- | --- | --- | --- |
| 1 | Interactive agent verification | An agent drives the running game through a Godot MCP server during development | Never gating; evidence for handoff only |
| 2 | End-to-end journey tests | The game plays itself from committed journey scripts, headless or windowed, in CI and locally | Gate-eligible; each milestone's journeys join its exit criteria |

The binding rule between tiers: **every behavior an agent verifies interactively, and every bug an agent finds interactively, must be promoted into a committed journey or scenario test before the task is done.** MCP interaction is never a substitute for automated coverage.

## 2. First-Party Automation Layer

The game owns its automation surface; MCP servers and journey scripts are clients of it. This keeps Tier 2 independent of any third-party tool.

- `AutomationDriver` is a development-only service composed at bootstrap when the build is a debug build **and** `--automation` is present on the command line. Release exports contain no automation code paths, no journey runner scenes, and no MCP addon (feature guards plus export filters; the clean-depot check in `TEST_PLAN.md` Section 6 verifies absence).
- Input synthesis goes through `Input.ParseInputEvent` so synthesized pointer/key events enter the same Godot input queue, the same `InputCollector`, and the same immutable `ToolInputFrame` path as real input. Journey steps never call gameplay components directly; a journey that cannot be expressed through input plus public commands indicates a testability gap to fix in the game, not in the test.
- Semantic targeting: steps name stable anchors — buddy part IDs, tool IDs, UI control names, sandbox-relative points — and the driver resolves them to coordinates at runtime. No hardcoded pixels, so journeys survive window size, zoom, and layout changes.
- State queries are read-only: the published view snapshot plus development telemetry (money, mood band, consciousness, selected tool, ammo/cooldown state, loose-object count, pain-window sum, save status). Journeys assert on these, within the same tolerance-envelope discipline as headless scenarios.
- Determinism: every journey declares a seed for the injected RNG service and starts from a declared save fixture (fresh save or a committed fixture file). Time acceleration for long journeys uses engine time scaling or fixed-step fast-forward in headless runs; the 120 Hz fixed tick and tick-counted timers make accelerated runs semantically identical. Release behavior is unaffected — acceleration exists only behind `--automation`.
- Artifacts: each run writes a machine-readable verdict JSON, telemetry series, scene-tree dumps on failure, the input trace, and (windowed runs only) screenshots to an artifacts directory passed on the command line. CI uploads artifacts on failure; agents read the same artifacts instead of re-running blindly.

## 3. Journey Tests (Tier 2)

- Journeys are versioned JSON files under `tests/journeys/`, executed by the same automation entrypoint as headless scenarios:

  ```text
  godot --headless -- --journey=<id> --seed=<n> --artifacts=<dir>
  ```

  A windowed profile (omit `--headless`) runs the identical journey visibly for local observation or MCP-attached watching.
- A journey has three parts: **setup** (seed, save fixture, window size, zoom), **steps**, and **assertions/teardown**. Step vocabulary starts small and grows only with need: `select_tool`, `pointer_press/drag/release`, `stroke_over_part`, `pullback_launch`, `click_ui`, `press_key`, `wait_signal`, `wait_predicate`, `assert_state`, `advance_time`. Multi-phase journeys (for example save → relaunch → resume) are expressed as ordered phases; the runner relaunches the process between phases.
- Waiting is always signal- or predicate-based with an explicit timeout. Fixed sleeps are forbidden. A flaky journey is a defect in the game or the journey — there are no automatic retries.
- Screenshots are artifacts for humans and agents, never assertions. All assertions are semantic and tolerance-based.
- Rendering-dependent steps (screenshot capture) are marked optional and skipped headless; everything else must pass headless with the emulated platform adapter, which is what CI runs.
- Steam-dependent journeys (offline queue, reconnect idempotency) run against a scriptable fake `IPlatformService`, never the real Steam client.

## 4. Interactive Agent Verification (Tier 1)

- The Godot MCP server configuration is committed in `.mcp.json` so every agent session gets the same tooling. Required capabilities: launch/stop the project, read logs and errors, capture screenshots, query the scene tree, synthesize pointer/key input, and await signals. The reference baseline is [Coding-Solo/godot-mcp](https://github.com/Coding-Solo/godot-mcp); an extended runtime-control server is acceptable when it meets the policy below. The server currently configured in this workspace already exposes runtime control (run/stop, screenshots, input, scene-tree and signal queries).
- Third-party MCP addons are development dependencies: pinned to a reviewed commit, dev-only autoload, excluded from release exports, bound to localhost. Any `eval`-style capability is debug-build-only and must never be used to mutate gameplay state to fake a passing outcome — it is for inspection.
- Standard verification loop for every implementation task:
  1. Build and run the relevant unit/scenario tests.
  2. Launch the game through MCP and drive the changed behavior through real input — the same way a player would reach it.
  3. Inspect semantic state and telemetry; capture a screenshot or log excerpt as handoff evidence.
  4. Promote the interaction into a journey (new or extended) or a headless scenario before marking the task done.
- MCP never runs in CI, and no gameplay code path may exist solely to serve MCP.

## 5. Record and Promote

The automation layer can record live input (MCP-driven or human play) into an input trace with timestamps and resolved semantic anchors. A promotion step converts a trace into a journey draft. The agent then hardens the draft: replace residual coordinates with semantic targets, add setup (seed, fixture), add assertions, and delete incidental input. Raw traces are throwaway; only hardened journeys are committed.

## 6. Scope Boundaries

- Native Windows overlay behavior — real pointer passthrough, focus stealing, tray, global hotkey, DPI, monitor topology — cannot be exercised by in-process input synthesis. Tier 1 on a real Windows session assists development there, but the manual matrix in `TEST_PLAN.md` Section 5 remains the gate.
- Journeys cover gameplay, UI, economy, persistence, and lifecycle behavior reachable through the game's own input path with the emulated platform adapter.
- The economy pacing benchmark stays in `TEST_PLAN.md` Section 4 as a deterministic simulation; journeys spot-check real purchases, not the full two-hour curve.

## 7. Milestone Journey Map

Journeys land with the milestone that introduces the behavior and join that milestone's exit criteria:

| Milestone | Journeys |
| --- | --- |
| 0 | Boot smoke: launch to sandbox composition, assert startup validation passed, clean exit code |
| 1 | Lab: spawn/settle within envelopes; grab-throw each part; walk/jump observation; time-accelerated 30-minute idle soak |
| 2 | Windowed mode-transition journeys where in-process input suffices; agent-assisted native matrix workflow documented |
| 3 | Damage/tool-feel slice: glove strike pays through HUD; activation alone does not pay; Pet rubbing and Tickle escalation/cooldown use real input; learned glove defense raises; knockout at threshold; dedup under continuous contact; grab-payout neutrality |
| 4 | Care and persistence: meal consumption grants mood and starts cooldown; failed use starts none; save → relaunch → safe standing resume with semantic state intact |
| 5 | Shop: earn → purchase → permanent unlock across relaunch; one journey per tool's happy path plus its cancel/secondary path |
| 6 | Platform fake: offline queue accrual, reconnect idempotency, local fallback boot |
| 7 | Full journey regression in CI; four/eight-hour soaks reuse journey infrastructure with acceleration disabled |
