# Desktop Buddy — Implementation Agent Instructions

## Source of Truth

Read these before changing code, in this order:

1. `docs/DECISIONS.md` — owner-confirmed decisions. The newer 2026-08-03 Phase B authorization recorded in item 2 supersedes only older Phase B deferral wording; historical decisions remain historical.
2. `docs/M5_5_PHASE_B_PAINTING_SOURCE_ALIGNMENT.md` — normative Milestone 5.6 supplement, completed B0 gate, locked painting behavior, architecture, budgets, task order, and verification.
3. `docs/M5_5_CHARACTER_EDITOR_SOURCE_ALIGNMENT.md` — normative Phase A supplement and historical A0 scope gate. Its statements that painting is deferred are superseded by item 2; its trusted visual/character architecture remains binding.
4. `docs/PRODUCT_REQUIREMENTS.md` — baseline observable behavior and acceptance criteria, as supplemented by items 2–3.
5. `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` — physics/gameplay contract.
6. `docs/ARCHITECTURE.md` — baseline ownership, interfaces, data flow, and failure behavior, as supplemented by items 2–3.
7. `docs/TEST_PLAN.md` and `docs/ROADMAP.md` — baseline verification and milestone order, as supplemented by item 2.
8. `docs/AGENT_VERIFICATION_AND_E2E.md` — baseline interactive verification workflow and end-to-end journey suite, as supplemented by item 2.
9. `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` — Phase A historical task contracts and the original short B1–B6 outline. The detailed Phase B contracts in item 2 supersede that short outline.
10. `docs/REFERENCE_RESEARCH.md` — clean-room reference evidence and technical sources.
11. `docs/OPEN_QUESTIONS.md` — decisions awaiting owner confirmation; do not implement behavior an open question affects until it is resolved by a higher-priority owner decision.

If documents conflict, apply the order above. Stop and ask the project owner only when the higher-priority documents do not resolve the conflict. If product behavior is not specified, do not invent it. Engineering coefficients explicitly assigned to a documented tuning or performance budget may be tuned through the documented acceptance process.

## Current State

- Milestones 0–5 are complete; Milestone 5 was owner-accepted on 2026-08-02.
- Milestone 5.5 Character Editor Phase A Tasks A0–A9 are complete and merged. The engine-free character schema/compiler, shared trusted visual rig, feature renderers/compositors, failure-safe local character store, schema-7 active selection, editor lifecycle, working-copy UI, and Phase A exit journey are present on `main`.
- The Work/Play and compact/full-screen redesign is merged. Painting must preserve its input ownership and editor window restoration behavior.
- Milestone 5.6 Character Painting Phase B is scheduled as of 2026-08-03. **Task B0 is complete. Task B1 — frontal hit mapping and paint-view camera — is the next executable task.**
- The Phase B UX target is a simplified, original, clean-room behavioral analogue of MECCHA CHAMELEON body painting: color wheel, direct body brush painting, mouse-wheel and visible-button brush sizing, eraser, Undo, undoable Erase All, and a zoomed locked-frontal view that can be panned.
- Phase C Steam Workshop remains deferred and requires Milestone 6 plus its own policy gate.
- The catalogue/economy and Work/Play UX research backlog remains non-blocking and must not be folded into Phase B implementation.
- Target exactly Godot 4.6.1 .NET/C# and Windows 10/11 x86_64 for the first Steam release.

## Non-Negotiable Architecture

- Use Godot `RigidBody2D` collision/physics with the approved six-circle custom active-puppet forces.
- Do not introduce a custom whole-world solver, `PinJoint2D` motor dependency, deep gameplay inheritance, global service locator, or all-purpose root script.
- Scene roots only compose and route. Put input, puppet constraints, locomotion, reactions, pain, mood, tools, economy, persistence, windowing, character editing, painting, and platform behavior in focused typed components/services.
- Inject scene dependencies through typed exported references or explicit constructor/factory wiring. Use local signals/events upward and explicit methods/commands downward.
- Store tunable/static content in typed Godot Resources. Store progress, character documents, and declared local paint PNGs in their separately versioned persistence boundaries only.
- Keep OS/Steam code behind interfaces with fully functional local/fallback implementations.
- Keep authoritative gameplay mutation on the physics/domain clock, not in drawing code.
- Character customization and painting are visual-only by construction. Character and paint data must never reach rig geometry, collision, mass, drives, forces, connectors, damage, mood, economy, or tool rules.
- `BuddyVisualProfile` remains trusted built-in geometry/tuning. Character compilation and paint loading never create, clone, replace, or mutate it.
- Runtime and editor preview share `BuddyVisualRigView`; the preview must not construct a fake/live `BuddyRoot` or any physics authority.
- Paint pixels are CPU-authoritative 512×512 RGBA8 surfaces. `BuddyVisualRigView.SetSurfaceUnderlay` is the only runtime binding seam, and face/accent decals remain above paint.
- PNG decode/encode, JSON, and file I/O never occur on the fixed physics tick. Godot texture creation/update remains on the main thread.
- Painting must remain inside the locked 64 MiB CPU editing budget and 8 MiB active GPU paint budget.

## Implementation Discipline

- Implement only the current numbered task and its tests; do not prebuild later Phase B tasks or Phase C.
- Follow B1–B6 and their prerequisites in `docs/M5_5_PHASE_B_PAINTING_SOURCE_ALIGNMENT.md`.
- B1 may build only frontal hit-to-UV mapping, the deterministic paint pose, camera framing, zoom, pan, clamps, Reset View, and their diagnostics/tests. Do not add stroke mutation, PNG persistence, or production paint controls during B1.
- Add tests with each behavior and run the relevant unit, headless, journey, and standalone checks before handoff.
- Verify changed behavior interactively in the running game through the configured Godot MCP server, then promote that interaction into committed automation. Interactive verification never substitutes for automated coverage.
- Never silently change confirmed resolution, controls, budgets, path whitelist, or scope. Record owner-approved changes before implementation.
- Keep debug visualizers and tuning panels behind development-build guards.
- Treat warnings, invalid UVs, seam jumps, non-finite coordinates, unexpected paint paths, dimension/byte-cap violations, save migration failures, failed image writes, main-thread violations, and lost editor/window recovery as actionable failures.
- Preserve user changes and keep generated `.godot/`, build output, Steam SDK binaries, development `steam_appid.txt`, character-library test fixtures, generated PNG test output, and artifacts out of source control unless a plan explicitly requires a committed fixture.
- Reset Progress clears active character selection to built-in but does not delete local character documents or their paint files.

## Clean-Room Rules

- Do not ship or commit original Interactive Buddy or MECCHA CHAMELEON files, decompiled source, extracted assets, shaders, art, audio, dialogue, skins, UI copy, layouts, names, branding, or likenesses.
- References may be used only to understand broad mechanics and interaction principles. All player-facing painting UI, brush rendering, icons, copy, layout, and presentation must be original.
- Do not trace screenshots or reproduce distinctive control arrangements. The required mouse-wheel plus visible-button sizing deliberately uses an independently specified, more discoverable control model.
- Store comparison notes and public URLs only in development documentation.
- Character packages, import/export, and user-generated Workshop distribution are not authorized in Phase B.

## Continuous Integration

CI runs on GitHub-hosted Linux runners (`ubuntu-latest`). The repository is public, so Actions minutes are free and jobs across branches run in parallel.

A self-hosted runner exists but is stopped and deliberately unused. Do not reintroduce `runs-on: self-hosted`: on a public repository a fork's pull request carries its own workflow file, so a self-hosted label would run untrusted code on the owner's machine.

- **Push, any branch** runs `CI / quick` only: compile, domain unit tests, and the Steam-binary guard.
- **Pull request to `main`** runs `CI / build-test` (the full scenario and journey sweep) and `Asset Forge CI / verify`.
- **Manual dispatch only** runs `CI / full-soak` and `Phase A Character Editor`.

- Never add slow steps to `CI / quick`. It is the per-commit feedback loop and must stay under roughly a minute. New scenarios, journeys, capture gates, and soaks belong in `CI / build-test` or `Asset Forge CI`, both of which are pull-request only.
- Do not add `push:` triggers to `Asset Forge CI` or `Phase A Character Editor`. They are pull-request and manual by design.
- The runner is Linux. Any test that feeds a path into code calling `Path.GetFullPath` must build that path per OS, for example `OperatingSystem.IsWindows() ? @"C:\save-test" : "/save-test"`. A hardcoded `C:\...` literal is not absolute on Linux: `GetFullPath` silently prefixes the working directory, so lookups miss instead of throwing. This is exactly what made `ProgressStoreTests` fail on Linux.
- Do not reintroduce `--filter "FullyQualifiedName!~ProgressStoreTests"`. That bug is fixed and the full domain suite passes on Linux.
- Allocation tests using `GC.GetAllocatedBytesForCurrentThread()` must retry until the measurement settles. Tiered-JIT promotion is asynchronous, allocates on the measuring thread, and flakes under full-suite load.
- The split between the push job and the pull-request jobs exists for fast feedback, not for cost. Keep it.

## Definition of Done

A task is done only when:

- Its behavior matches the approved requirements, the Phase B supplement, and the current numbered task.
- Automated tests cover normal, boundary, failure, memory, and threading paths.
- Required standalone Windows checks pass for the task's gate.
- Performance remains inside the locked CPU/GPU/upload budgets.
- Documentation and data schemas reflect the resulting implementation.
- No selectable UI element advertises an unimplemented or deferred feature.
- Paint remains visual-only and face/accent layer order remains authoritative.
- No Phase B implementation introduces Workshop, arbitrary packages, custom scripts/Resources, multiple buddies, physics customization, material painting, or cosmetic economy.

## Authorized and Deferred Features

**Authorized during Milestone 5.6 Phase B:** six local 512×512 RGBA8 part-paint surfaces; locked-frontal paint workspace; zoom/pan/reset view; color wheel; one circular brush; mouse-wheel and visible-button size controls; eraser; bounded stroke Undo; undoable confirmed Erase All; underlay preview/runtime binding; sequential schema migration; atomic whitelisted PNG persistence; and the required verification suite exactly as defined by the Phase B supplement.

**Still deferred and forbidden:** Steam Workshop; package import/export; arbitrary custom Resources/scenes/scripts/DLLs; multiple buddies; multiple gameplay profiles/save slots; cosmetic progression/economy; eyedropper, material sliders, patterns, gradients, spray/smudge/stamps, layers/blend modes, brush files, tablet pressure, 3D orbit/back-side painting; generalized mod APIs; multiplayer; Linux; and macOS.