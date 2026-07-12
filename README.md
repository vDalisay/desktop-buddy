# Desktop Buddy

Desktop Buddy is a Godot 4.6.1 C# desktop-idler and physics sandbox for Windows/Steam. A small transparent, bordered box contains an original six-circle robot buddy that stands, walks, jumps, reacts, catches objects, resists fearful grabs, gets knocked unconscious, and recovers through an active physics puppet.

The project is a clean-room spiritual successor to the interaction loop of *Interactive Buddy*. It recreates behavioral principles and physics feel with original code, art, audio, UI, character identity, tools, and progression.

## Project Status

Milestone 0 foundation is complete and Milestone 1 physics-laboratory work is in progress. The current lab contains the typed provisional six-body rig, passive spring/damper/max-stretch solver, physical standing/recovery, seeded autonomous walk/jump drive with explicit conscious/unconscious profiles, elastic part/object grabbing with fearful resistance, and physics-boundary room resize/zoom handling. Its coefficients remain laboratory data until the full physics gate accepts a tuning profile. Economy, shop, and content work have not started.

Target stack:

- Godot 4.6.1 .NET
- C# / .NET 8
- Godot `RigidBody2D` with custom six-body active-puppet forces
- Windows 10/11 x86_64
- Steam release with local fallback, Cloud progress, stats, and achievements

## Handoff Documents

- [Agent instructions](AGENTS.md)
- [Confirmed decisions](docs/DECISIONS.md)
- [Product requirements](docs/PRODUCT_REQUIREMENTS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Ragdoll and gameplay specification](docs/RAGDOLL_AND_GAMEPLAY_SPEC.md)
- [Test plan](docs/TEST_PLAN.md)
- [Agent verification and end-to-end journeys](docs/AGENT_VERIFICATION_AND_E2E.md)
- [Implementation roadmap](docs/ROADMAP.md)
- [Reference research](docs/REFERENCE_RESEARCH.md)
- [Open questions](docs/OPEN_QUESTIONS.md)

## First Implementation Gate

Before economy or shop work begins, the project must prove:

- Stable six-body spring behavior at a fixed 120 Hz.
- Upright idle, sideways walking, jumping, drag/throw, fearful resistance, knockout, and natural recovery.
- Long-run stability and tolerance-based headless regression tests.
- A standalone Windows transparent overlay with correct pointer mapping.
- Side-by-side acceptance against the approved v1.01/v1.02 reference policy.

See [the roadmap](docs/ROADMAP.md) and [test plan](docs/TEST_PLAN.md) for the complete gate.

## Scope Boundary

The current launch plan contains one buddy, one save, fourteen interactions/tools, a two-hour unlock curve, mood-scaled run-time passive income, and non-graphic slapstick feedback. Bleeding, painting, cosmetics, Workshop/custom buddies, multiple buddies, profiles, multiplayer, and non-Windows platforms are deferred.

## Toolchain Versions

These are pinned; a mismatch (especially export templates vs. editor) breaks C# builds or transparency.

| Component | Version |
| --- | --- |
| Godot editor | `4.6.1.stable.mono` (Windows build: `Godot_v4.6.1-stable_mono_win64`) |
| Godot export templates | `4.6.1.stable.mono` — must match the editor exactly |
| .NET SDK | Pinned by [`global.json`](global.json) (`8.0.204`); targets .NET 8 |
| Target runtime | Windows 10/11 x86_64 |

## Solution Layout

Four-project solution (`DesktopBuddy.sln`), per [ARCHITECTURE.md](docs/ARCHITECTURE.md) Section 22:

- `DesktopBuddy.csproj` — the Godot game assembly (`Godot.NET.Sdk/4.6.1`); source under `src/`, scenes under `scenes/`, typed data under `data/`.
- `domain/DesktopBuddy.Domain` — Godot-free .NET class library (rules, formulas, timers, save DTOs, runner-argument contract).
- `tests/DesktopBuddy.Domain.Tests` — xUnit tests for the domain library.
- `DesktopBuddy.Steam` — optional Steam adapter, added in Milestone 6.

## Testing the Physics Lab in Godot

Do not run `scenes/buddy/puppet.tscn` by itself: it is the reusable six-body actor and intentionally has no room boundaries or fixed-tick lab router, so gravity makes it fall forever.

From the Godot editor:

1. Open `scenes/buddy_lab.tscn` in the FileSystem dock.
2. Press **F6** (Run Current Scene), not F5.
3. Use left-drag to grab/throw a body part, right-click to drop, `P` to pause, `.` to advance one physics tick, `U` to toggle limp/unconscious mode, `Shift+U` to reseed autonomy, and `1`/`2`/`3`/`4` for `0.25x`/`0.5x`/`1x`/`2x` simulation speed.
4. Press `H` to hide or restore the development telemetry panel.

For one-click launch outside the editor, run [`tools/play_buddy_lab.bat`](tools/play_buddy_lab.bat). It uses `GODOT_PATH` when set and otherwise checks the pinned Godot 4.6.1 .NET editor in the current user's Downloads folder.

For a fast automated check, run [`tools/quick_validate.bat`](tools/quick_validate.bat). It builds the solution, runs domain tests, imports the project, and exercises representative Milestone 1 scenarios and journeys.

Telemetry-enabled scenarios write `telemetry_<id>.jsonl` and `envelope_<id>.json` when `--artifacts` is supplied. Run `idle_soak_ci` for the three-minute push check and `idle_soak` for the full 216,000-tick/30-minute gate. Add `--fixed-fps 120` before `--path` for headless soak runs so simulation time free-runs while preserving the exact tick count. `repeat_envelope` compares five identical-seed and five varied-seed runs against [`lab_envelope_bounds.tres`](data/buddy/lab_envelope_bounds.tres).

The Milestone 1 journeys are `lab_spawn_settle`, `lab_grab_throw`, `lab_walk_jump`, and `lab_idle_soak`. The latter is the full soak and is intended for nightly/manual gate validation.

To compare tuning profiles side by side, run `tools\play_buddy_lab.bat --dual -- --profile-a=res://data/buddy/lab_puppet_rig.tres --profile-b=res://data/buddy/lab_puppet_rig.tres --drive-a=res://data/buddy/lab_active_drive.tres --drive-b=res://data/buddy/lab_active_drive.tres`. Both buddies receive the same seed and fixed-tick routing; `Tab` switches which buddy can be grabbed, and picks ignore the inactive buddy. Review response delay, bounded stretch, whole-body impulse propagation, sideways collapse, and physics-driven recovery, then run `dual_profile_smoke` and the complete regression suite before accepting Resource changes.

To record and promote input, launch a debug build with `--automation --trace-out=.artifacts/traces/session.json`, play through real mouse/keyboard input, then run with `--promote-trace=.artifacts/traces/session.json --journey-out=tests/journeys/draft.json`. Harden the draft per [`AGENT_VERIFICATION_AND_E2E.md`](docs/AGENT_VERIFICATION_AND_E2E.md): replace residual sandbox coordinates, set the fixture/seed, add semantic assertions, and commit only the hardened journey.

The standalone pointer/transparency spike is `res://scenes/spike_transparent_window.tscn`. Run it on each target DPI scale and compare the on-window client readout with the OS pointer; it is development-only and export-excluded.

## Building and Testing

The project exposes one command per test layer (see [TEST_PLAN.md](docs/TEST_PLAN.md)). Replace `<godot>` with the pinned editor binary.

```sh
# Build everything (also places the game assembly where Godot loads it).
dotnet build DesktopBuddy.sln

# Layer 1 — pure C# unit tests (no Godot runtime).
dotnet test

# One-time / after adding assets — headless import.
<godot> --headless --path . --import

# Layer 2 — headless seeded Godot scenarios (JSON verdict, exit 0 pass / 1 fail).
<godot> --headless --path . -- --scenario=<id> --seed=<n> [--artifacts=<dir>]

# Layer 3 — end-to-end journeys through the real input path.
<godot> --headless --path . -- --journey=<id> --seed=<n> [--artifacts=<dir>]
```

Milestone 0 ships the `boot_smoke` scenario and journey. CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs the build, domain tests, headless import, and both boot smoke runs on every push — with no Steam SDK required.

Milestone 1 additionally runs the `lab_spawn_settle` journey and the `passive_rig`, `standing_recovery`, `autonomous_motion`, `laboratory_controls`, `grab_release`, `grab_resistance`, `grab_hard_recovery`, and `room_resize_zoom` scenarios. Together they cover six-body composition, collision-layer isolation, required rigid-body runtime settings, finite force telemetry, bounded strain, physical standing measurements, exact recovery timing, force-driven self-righting, immediate escaped/invalid-state recovery, seeded bidirectional walking, whole-body jumping, passive unconscious physics, the lab input surface, elastic acquisition/release, fearful resistance, hard-recovery cleanup, physics-boundary wall rebuilds, zoom clamping, representative aspect ratios, and safe containment correction.

The development-only laboratory controls are keyboard-accessible in `buddy_lab.tscn`: `P` pauses/resumes, `.` advances one fixed physics tick while paused, `U` toggles consciousness, `Shift+U` advances to the next autonomy seed, and `1`/`2`/`3`/`4` select `0.25x`/`0.5x`/`1x`/`2x` time scale. The `laboratory_controls` scenario exercises the same input path.

## Interactive Verification (Godot MCP)

Tier 1 interactive verification uses the runtime-enabled [godot-mcp-runtime](https://github.com/Erodenn/godot-mcp-runtime) server. The committed [`.mcp.json`](.mcp.json) points at the git-ignored checkout under `Mcp/godot-mcp-runtime/` (its own `.git`, `node_modules`, and `dist` are not tracked). Set it up once per machine:

```sh
git clone https://github.com/Erodenn/godot-mcp-runtime.git Mcp/godot-mcp-runtime
npm --prefix Mcp/godot-mcp-runtime install
npm --prefix Mcp/godot-mcp-runtime run build   # produces Mcp/godot-mcp-runtime/dist/index.js
```

The only environment variable needed is `GODOT_PATH`, pointing at the pinned Godot 4.6.1 mono binary. The MCP tier is development-only, bound to localhost, never gating, and excluded from release exports; it never runs in CI.
