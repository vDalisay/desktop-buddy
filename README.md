# Desktop Buddy

Desktop Buddy is a Godot 4.6.1 C# desktop-idler and physics sandbox for Windows/Steam. A small transparent, bordered box contains an original six-circle robot buddy that stands, walks, jumps, reacts, catches objects, resists fearful grabs, gets knocked unconscious, and recovers through an active physics puppet.

The project is a clean-room spiritual successor to the interaction loop of *Interactive Buddy*. It recreates behavioral principles and physics feel with original code, art, audio, UI, character identity, tools, and progression.

## Project Status

Milestone 0 foundation is complete and Milestone 1 physics-laboratory work is in progress. The current lab contains the typed provisional six-body rig and passive spring/damper/max-stretch solver; its coefficients remain laboratory data until the full physics gate accepts a tuning profile. Economy, shop, and content work have not started.

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

Milestone 1 additionally runs `passive_rig` and `standing_recovery`, covering six-body composition, collision-layer isolation, required rigid-body runtime settings, finite force telemetry, bounded strain, physical standing measurements, exact recovery timing, force-driven self-righting, and immediate escaped/invalid-state recovery.

## Interactive Verification (Godot MCP)

Tier 1 interactive verification uses the runtime-enabled [godot-mcp-runtime](https://github.com/Erodenn/godot-mcp-runtime) server. The committed [`.mcp.json`](.mcp.json) points at the git-ignored checkout under `Mcp/godot-mcp-runtime/` (its own `.git`, `node_modules`, and `dist` are not tracked). Set it up once per machine:

```sh
git clone https://github.com/Erodenn/godot-mcp-runtime.git Mcp/godot-mcp-runtime
npm --prefix Mcp/godot-mcp-runtime install
npm --prefix Mcp/godot-mcp-runtime run build   # produces Mcp/godot-mcp-runtime/dist/index.js
```

The only environment variable needed is `GODOT_PATH`, pointing at the pinned Godot 4.6.1 mono binary. The MCP tier is development-only, bound to localhost, never gating, and excluded from release exports; it never runs in CI.
