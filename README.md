# Desktop Buddy

Desktop Buddy is a Godot 4.6.1 C# desktop-idler and physics sandbox for Windows/Steam. A small transparent, bordered box contains an original six-circle robot buddy that stands, walks, jumps, reacts, catches objects, resists fearful grabs, gets knocked unconscious, and recovers through an active physics puppet.

The project is a clean-room spiritual successor to the interaction loop of *Interactive Buddy*. It recreates behavioral principles and physics feel with original code, art, audio, UI, character identity, tools, and progression.

## Project Status

Requirements and architecture are specified; runtime implementation has not started on the checked-out baseline. The first engineering target is the physics laboratory, not the shop or content catalogue.

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
