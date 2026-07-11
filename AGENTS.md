# Desktop Buddy — Implementation Agent Instructions

## Source of Truth

Read these before changing code, in this order:

1. `docs/DECISIONS.md` — owner-confirmed decisions.
2. `docs/PRODUCT_REQUIREMENTS.md` — observable behavior and acceptance criteria.
3. `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` — physics/gameplay contract.
4. `docs/ARCHITECTURE.md` — ownership, interfaces, data flow, and failure behavior.
5. `docs/TEST_PLAN.md` and `docs/ROADMAP.md` — verification and milestone order.
6. `docs/REFERENCE_RESEARCH.md` — clean-room reference evidence and technical sources.
7. `docs/OPEN_QUESTIONS.md` — decisions awaiting owner confirmation; do not implement behavior an open question affects until it is resolved into `DECISIONS.md`.

If documents conflict, stop and ask the project owner. If product behavior is not specified, do not invent it. Engineering coefficients explicitly assigned to the physics/economy laboratory may be tuned through the documented acceptance process.

## Current State

- The checked-out baseline is the minimal Godot initialization commit.
- Existing `main`, `chat`, `codex`, and `threejs` branches are non-authoritative experiments. Do not merge or copy one wholesale.
- Implementation starts at Roadmap Milestone 0, then must pass the Milestone 1 physics gate before economy/shop work.
- Target exactly Godot 4.6.1 .NET/C# and Windows 10/11 x86_64 for the first Steam release.

## Non-Negotiable Architecture

- Use Godot `RigidBody2D` collision/physics with the approved six-circle custom active-puppet forces.
- Do not introduce a custom whole-world solver, `PinJoint2D` motor dependency, deep gameplay inheritance, global service locator, or all-purpose root script.
- Scene roots only compose and route. Put input, puppet constraints, locomotion, reactions, pain, mood, tools, economy, persistence, windowing, and platform behavior in focused typed components/services.
- Inject scene dependencies through typed exported references or explicit constructor/factory wiring. Use local signals/events upward and explicit methods/commands downward.
- Store tunable/static content in typed Godot Resources. Store user progress in versioned JSON only.
- Keep OS/Steam code behind interfaces with fully functional local/fallback implementations.
- Keep authoritative gameplay mutation on the physics/domain clock, not in drawing code.

## Implementation Discipline

- Implement only the current milestone and its tests; do not prebuild deferred features.
- Add tests with each behavior and run the relevant unit, headless, and standalone checks before handoff.
- Never silently change confirmed numbers or scope. Update `DECISIONS.md` only after owner confirmation.
- Keep debug visualizers and tuning panels behind development-build guards.
- Treat warnings, missing Resource references, invalid catalog IDs, save migration failures, NaN physics, and lost input recovery as actionable failures.
- Preserve user changes and keep generated `.godot/`, build output, Steam SDK binaries, and development `steam_appid.txt` out of source control.

## Clean-Room Rules

- Do not ship or commit original Interactive Buddy files, decompiled source, art, audio, dialogue, skins, UI copy, names, or likenesses.
- Use the reference only to understand mechanics and feel. All player-facing expression must be original.
- Store behavior comparison notes only in development documentation.

## Definition of Done

A task is done only when:

- Its behavior matches the approved requirements and architecture.
- Automated tests cover normal, boundary, and failure paths.
- Required standalone Windows checks pass.
- Performance remains inside the current milestone budget.
- Documentation and data schemas reflect the resulting implementation.
- No selectable UI element advertises an unimplemented feature.

## Deferred Features

Do not implement bleeding, painting/coloring, cosmetics, Workshop/custom buddies, multiple buddies, profiles, multiplayer, Linux, or macOS. Preserve the documented future custom-buddy seam without building a speculative mod API.
