# Desktop Buddy — Implementation Agent Instructions

## Source of Truth

Read these before changing code, in this order:

1. `docs/DECISIONS.md` — owner-confirmed decisions.
2. `docs/M5_5_CHARACTER_EDITOR_SOURCE_ALIGNMENT.md` — normative Milestone 5.5 supplement and A0 scope gate. Its historical “may begin A1” completion sentence is superseded by the Current State below.
3. `docs/PRODUCT_REQUIREMENTS.md` — baseline observable behavior and acceptance criteria, as supplemented for Milestone 5.5.
4. `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` — physics/gameplay contract.
5. `docs/ARCHITECTURE.md` — baseline ownership, interfaces, data flow, and failure behavior, as supplemented for Milestone 5.5.
6. `docs/TEST_PLAN.md` and `docs/ROADMAP.md` — baseline verification and milestone order, as supplemented for Milestone 5.5.
7. `docs/AGENT_VERIFICATION_AND_E2E.md` — baseline interactive verification workflow and end-to-end journey suite, as supplemented for Milestone 5.5.
8. `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` — detailed A1–A9 handoff contracts; Phase B and Phase C remain deferred.
9. `docs/REFERENCE_RESEARCH.md` — clean-room reference evidence and technical sources.
10. `docs/OPEN_QUESTIONS.md` — decisions awaiting owner confirmation; do not implement behavior an open question affects until it is resolved into `DECISIONS.md`.

If documents conflict, apply the order above. Stop and ask the project owner only when the higher-priority documents do not resolve the conflict. If product behavior is not specified, do not invent it. Engineering coefficients explicitly assigned to the physics/economy laboratory may be tuned through the documented acceptance process.

## Current State

- Milestones 0–5 are complete; Milestone 5 was owner-accepted on 2026-08-02.
- Progress persistence is schema 6 at the M5 exit baseline. Reset Progress rewrites the existing `BuddyProgressState` in place through `Adopt`; Milestone 5.5 must extend that architecture rather than replace it.
- Milestone 5.5 Character Editor Phase A is scheduled now, before Milestone 6.
- Phase A Tasks A0–A2 are complete. A1 established the engine-free character schema/compiler. A2 extracted `BuddyVisualRigView`, retained gameplay sampling in `BuddyVisualPresenter`, added the physics-free static preview source, locked trusted geometry against appearance mutation, and moved scorch over active custom base colors. **Task A3 is the next executable task.**
- The retractable dock/settings surface remains separate scheduled work. Character Editor A8 must integrate with the approved production settings/panel surface and must not create a competing settings architecture.
- The Linux PR workflow currently has six pre-existing `ProgressStoreTests` failures caused by Windows-style in-memory test paths. Do not attribute those failures to A1/A2 or modify persistence behavior as part of A3; verify the assigned task’s focused tests independently and report the baseline red honestly.
- Target exactly Godot 4.6.1 .NET/C# and Windows 10/11 x86_64 for the first Steam release.

## Non-Negotiable Architecture

- Use Godot `RigidBody2D` collision/physics with the approved six-circle custom active-puppet forces.
- Do not introduce a custom whole-world solver, `PinJoint2D` motor dependency, deep gameplay inheritance, global service locator, or all-purpose root script.
- Scene roots only compose and route. Put input, puppet constraints, locomotion, reactions, pain, mood, tools, economy, persistence, windowing, character editing, and platform behavior in focused typed components/services.
- Inject scene dependencies through typed exported references or explicit constructor/factory wiring. Use local signals/events upward and explicit methods/commands downward.
- Store tunable/static content in typed Godot Resources. Store user progress and character documents in their separately versioned JSON boundaries only.
- Keep OS/Steam code behind interfaces with fully functional local/fallback implementations.
- Keep authoritative gameplay mutation on the physics/domain clock, not in drawing code.
- Character customization is visual-only by construction. Character data must never reach rig geometry, collision, mass, drives, forces, connectors, damage, mood, economy, or tool rules.
- `BuddyVisualProfile` remains trusted built-in geometry/tuning. Character compilation returns the narrow engine-free `CompiledCharacterAppearance`; it never creates, clones, replaces, or mutates `BuddyVisualProfile`.
- Runtime and editor preview share `BuddyVisualRigView`; the preview must not construct a fake or live `BuddyRoot` or any physics authority.

## Implementation Discipline

- Implement only the current task and its tests; do not prebuild later Phase A tasks or deferred phases.
- Follow A1–A9 and their prerequisites in `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`.
- Add tests with each behavior and run the relevant unit, headless, journey, and standalone checks before handoff.
- Verify changed behavior interactively in the running game through the configured Godot MCP server (launch, drive the behavior through real input, inspect semantic state, capture evidence), then promote that interaction into a committed journey or scenario test per `docs/AGENT_VERIFICATION_AND_E2E.md` and the Milestone 5.5 supplement. Interactive verification never substitutes for automated coverage.
- Never silently change confirmed numbers or scope. Update `DECISIONS.md` only after owner confirmation.
- Keep debug visualizers and tuning panels behind development-build guards.
- Treat warnings, missing Resource references, invalid catalog/feature IDs, save migration failures, NaN physics, failed character writes, and lost input/window recovery as actionable failures.
- Preserve user changes and keep generated `.godot/`, build output, Steam SDK binaries, development `steam_appid.txt`, character-library test fixtures, and generated artifacts out of source control unless a plan explicitly requires a committed fixture.
- A6 must inspect the schema version present when it starts, add active-character selection through every `ProgressSave`/snapshot/`Apply`/`Adopt` path, and preserve M5's all-or-nothing Reset Progress behavior.
- Reset Progress clears active character selection to built-in but does not delete local character documents.

## Clean-Room Rules

- Do not ship or commit original Interactive Buddy files, decompiled source, art, audio, dialogue, skins, UI copy, names, or likenesses.
- Use the reference only to understand mechanics and feel. All player-facing expression and all shipped feature art must be original.
- Store behavior comparison notes only in development documentation.
- Character packages and user-generated content are not authorized in Phase A.

## Definition of Done

A task is done only when:

- Its behavior matches the approved requirements, the Milestone 5.5 supplement, and the task plan.
- Automated tests cover normal, boundary, and failure paths.
- Required standalone Windows checks pass for the task's gate.
- Performance remains inside the current milestone budget.
- Documentation and data schemas reflect the resulting implementation.
- No selectable UI element advertises an unimplemented or deferred feature.
- No Phase A implementation introduces a paint, Workshop, arbitrary-package, multi-buddy, physics-customization, or cosmetic-economy surface.

## Deferred and Authorized Features

**Authorized during Milestone 5.5 Phase A only:** bounded local parametric visual characters, six part colors, shipped eye/brow/mouth/accent variants, local library, one active appearance, physics-free preview, editor mode/window transition, and active-selection persistence exactly as defined by the source-alignment supplement and task plan.

**Still deferred and forbidden:** bleeding; freehand painting/color strokes and paint files; Steam Workshop; package import/export; arbitrary custom Resources/scenes/scripts/DLLs; multiple buddies; multiple gameplay profiles/save slots; cosmetic progression/economy; generalized mod APIs; multiplayer; Linux; and macOS. Preserve only the explicitly named Phase B surface-underlay seam and do not build speculative compatibility machinery.
