# Desktop Buddy — Implementation Agent Instructions

## Source of Truth

Read these before changing code, in this order:

1. `docs/M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md` — normative owner-authorized Steam Workshop supplement. For Workshop/package/platform scope it supersedes older Phase C deferral/forbidden wording in this file, `docs/DECISIONS.md`, `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`, `docs/ROADMAP.md`, and historical milestone notes. It does not authorize real-time multiplayer or the deferred Damage Sprint leaderboard.
2. `docs/NEXT_FEST_THREE_BUILD_SCOPE_SOURCE_ALIGNMENT_2026-09-07.md` — owner-authorized correction defining the three cumulative Steam build surfaces: Normal Demo, Next Fest Demo Update, and Full Release. It supersedes any wording that removes existing/pre-approved Normal Demo content for Next Fest curation or treats the Normal Demo and Next Fest Demo as the same build.
3. `docs/NEXT_FEST_DEMO_SYSTEMIC_SANDBOX_VERTICAL_SLICE_2026-09-07.md` — detailed Next Fest vertical-slice plan. Read through item 2: curation applies only to newly introduced systemic systems/content, never by removing existing Normal Demo functionality.
4. `docs/FULL_RELEASE_MULTI_BUDDY_SCENES_SOURCE_ALIGNMENT_2026-09-07.md` — owner-authorized Scene/multi-Buddy architecture supplement. It defines the account/Buddy state split, Scene documents and production multi-Buddy runtime. Where its distribution wording conflicts with items 2–3, those newer Demo scope decisions control. Buddy-to-Buddy social AI/interactions remain deferred.
5. `docs/DECISIONS.md` — owner-confirmed decisions. Historical Phase B/Phase C deferrals remain historical where superseded by newer owner-authorized supplements.
6. `docs/M5_5_PHASE_B_PAINTING_SOURCE_ALIGNMENT.md` — normative Milestone 5.6 painting supplement, locked painting behavior, architecture, budgets, task order, and verification.
7. `docs/M5_5_CHARACTER_EDITOR_SOURCE_ALIGNMENT.md` — normative Phase A supplement and historical A0 scope gate. Its statements that painting is deferred are superseded by item 6; its trusted visual/character architecture remains binding.
8. `docs/PRODUCT_REQUIREMENTS.md` — baseline observable behavior and acceptance criteria, as supplemented by items 1–4, 6, and 7.
9. `docs/RAGDOLL_AND_GAMEPLAY_SPEC.md` — physics/gameplay contract.
10. `docs/ARCHITECTURE.md` — baseline ownership, interfaces, data flow, and failure behavior, as supplemented by items 1–4, 6, and 7.
11. `docs/TEST_PLAN.md` and `docs/ROADMAP.md` — baseline verification and milestone order, as supplemented by items 1–4 and 6.
12. `docs/AGENT_VERIFICATION_AND_E2E.md` — baseline interactive verification workflow and end-to-end journey suite.
13. `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` — Phase A historical task contracts and original Workshop planning. Its Steamworks.NET/Phase C deferral wording is superseded by item 1; trusted character/package validation concepts remain historical design evidence where they do not conflict.
14. `docs/GODOTSTEAM_WORKSHOP_AND_SOCIAL_FEATURES_IMPLEMENTATION_PLAN_2026-08-25.md` — detailed Workshop research and task contracts, as activated and corrected by items 1–3.
15. `docs/REFERENCE_RESEARCH.md` — clean-room reference evidence and technical sources.
16. `docs/OPEN_QUESTIONS.md` — decisions awaiting owner confirmation; do not implement behavior an open question affects until it is resolved by a higher-priority owner decision.

If documents conflict, apply the order above. Stop and ask the project owner only when the higher-priority documents do not resolve the conflict. If product behavior is not specified, do not invent it. Engineering coefficients explicitly assigned to a documented tuning or performance budget may be tuned through the documented acceptance process.

## Current State

- Milestones 0–5 are complete; Milestone 5 was owner-accepted on 2026-08-02.
- Milestone 5.5 Character Editor Phase A Tasks A0–A9 are complete and merged. The engine-free character schema/compiler, shared trusted visual rig, feature renderers/compositors, failure-safe local character store, schema-7 active selection, editor lifecycle, working-copy UI, and Phase A exit journey are present on `main`.
- The Work/Play and compact/full-screen redesign is merged. Painting and Workshop surfaces must preserve its input ownership and window restoration behavior.
- Milestone 5.6 Character Painting Phase B source alignment remains binding for painting behavior, visual-only guarantees, persistence boundaries, and budgets.
- **Steam Workshop v1 is owner-authorized as of 2026-08-25.** The source-controlled path for room paintings and Buddy Studio configuration + declared buddy paint uses optional GodotSteam 4.22. Workshop is enabled in the Steam Demo and full Steam release through the `steam` build feature, and excluded from the itch.io build. Live Steamworks/two-account validation remains an external gate.
- **Three cumulative Steam build surfaces are owner-authorized as of 2026-09-07.** The Normal Demo preserves the current/pre-approved Demo content baseline. The Next Fest Demo Update contains the complete Normal Demo plus a curated vertical slice of newly planned Scene/multi-Buddy/systemic-sandbox systems. The Full Release contains those surfaces plus the remaining systemic breadth, higher caps and other approved Full Release content. Next Fest work must never remove an existing Normal Demo tool or feature merely to make the vertical slice smaller.
- **Multi-Buddy Scenes are owner-authorized as of 2026-09-07.** The production architecture may host several player-created Buddies in one active Scene and several named Scene documents switchable through Win98-style tabs. Only one Scene simulates at a time. Buddy-to-Buddy social AI/relationships/interactions remain outside the initial scope. The Normal Demo keeps its existing one-Buddy/one-room behavior; the Next Fest Demo receives the limited Scene/multi-Buddy slice; the Full Release receives the broader version.
- The Workshop is asynchronous social functionality only. There are no lobbies, P2P sessions, replicated players, RPCs, `MultiplayerPeer`, or shared live rooms.
- The future 30-second friends Damage Sprint leaderboard remains deferred.
- Target exactly Godot 4.6.1 .NET/C# and Windows 10/11 x86_64 for the first Steam release.

## Non-Negotiable Architecture

- Use Godot `RigidBody2D` collision/physics with the approved six-circle custom active-puppet forces.
- Do not introduce a custom whole-world solver, `PinJoint2D` motor dependency, deep gameplay inheritance, global service locator, or all-purpose root script.
- Scene roots only compose and route. Put input, puppet constraints, locomotion, reactions, pain, mood, tools, economy, persistence, windowing, character editing, painting, sharing, and platform behavior in focused typed components/services.
- Inject scene dependencies through typed exported references or explicit constructor/factory wiring. Use local signals/events upward and explicit methods/commands downward.
- Store tunable/static content in typed Godot Resources. Store progress, Buddy identity documents, Scene documents/assets, character documents, declared local paint PNGs, imported Workshop copies, and Workshop provenance only in their separately owned/versioned persistence boundaries.
- Keep OS/Steam code behind interfaces with fully functional local/fallback implementations. Steam/GodotSteam absence or initialization failure must never block normal single-player boot, local saves, Paint Room, or Buddy Studio.
- Keep authoritative gameplay mutation on the physics/domain clock, not in drawing or platform code.
- Character customization, painting, and Workshop-imported character/room content are visual-only by construction. They must never reach rig geometry, collision, mass, drives, forces, connectors, damage, mood, economy, or tool rules.
- `BuddyVisualProfile` remains trusted built-in geometry/tuning. Character compilation and paint loading never create, clone, replace, or mutate it.
- Runtime and editor preview share `BuddyVisualRigView`; the preview must not construct a fake/live `BuddyRoot` or any physics authority.
- Paint pixels are CPU-authoritative 512×512 RGBA8 surfaces. `BuddyVisualRigView.SetSurfaceUnderlay` is the only runtime binding seam, and face/accent decals remain above paint.
- PNG decode/encode, JSON, file I/O, Workshop staging, and hashing never occur on the fixed physics tick. Godot texture creation/update and GodotSteam calls remain on the main thread where required.
- Painting must remain inside the locked 64 MiB CPU editing budget and 8 MiB active GPU paint budget until a separately measured multi-Buddy budget supersedes the one-active-Buddy runtime assumption. Do not silently lower paint resolution.
- Scene switching must never create multiple hidden live physics worlds. Only the active Scene simulates; inactive Scenes are persisted/suspended documents.
- Multi-Buddy runtime composition must preserve the single routed gameplay `_PhysicsProcess`. Several Buddy actors are ticked in deterministic stable order from the owning route rather than registering independent gameplay clocks.
- Player/account progression (wallet, unlocks, selected tool, aggregate statistics) remains shared. Buddy-specific mood, hunger/fullness, harmful memory, traits/preferences, and fun/novelty must not be shared between distinct Buddy identities once the multi-Buddy state split lands.
- Shared Normal Demo / Next Fest / Full systems use one implementation and schema. Distribution differences belong in trusted availability metadata, caps and validators rather than forked gameplay code.
- Build-scope checks must preserve the cumulative invariant: Normal Demo baseline ⊂ Next Fest Demo ⊂ Full Release. A scope filter for new systemic content must never hide an already-authorized Normal Demo feature in the Next Fest or Full Release builds.
- Workshop content is hostile data. V1 and later specifically authorized package types accept only explicitly versioned declarative formats and exact path/ID/property whitelists. Never `GD.Load`/`ResourceLoader` Workshop content and never accept Workshop scenes, Resources, scripts, shaders, DLLs, native libraries, meshes, or generalized mods.
- Imported Workshop characters always receive a fresh local GUID; remote/source identity is provenance only. Downloaded/imported content never auto-activates or silently replaces active local content.

## Implementation Discipline

- Follow the numbered task/source-alignment document for the feature being changed. Three-build scope follows `docs/NEXT_FEST_THREE_BUILD_SCOPE_SOURCE_ALIGNMENT_2026-09-07.md`; Next Fest systemic/Scene work follows that correction plus `docs/NEXT_FEST_DEMO_SYSTEMIC_SANDBOX_VERTICAL_SLICE_2026-09-07.md` and the multi-Buddy architecture supplement; painting work follows `docs/M5_5_PHASE_B_PAINTING_SOURCE_ALIGNMENT.md`; existing Workshop v1 work follows `docs/M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md` and the GodotSteam implementation plan.
- Preserve all three independently buildable Steam surfaces. `steam,steam_demo` remains the Normal Demo; the Next Fest Demo should use an additional explicit feature such as `next_fest_demo`; `steam,full_release` remains the Full Release. Do not mutate the Normal Demo preset into the Next Fest preset.
- The Next Fest widening is limited to the exact **new-system** slice in its source-alignment documents. It is additive over the Normal Demo. Do not remove or reclassify existing Normal Demo tools, Work Mode, Paint surfaces, Buddy Studio content, tutorial, Gore policy or Workshop v1 merely to reduce the Next Fest catalogue.
- Do not infer Buddy-to-Buddy conversations, relationships, collision/avoidance, fighting, object passing, coordinated activities, or multiplayer merely because several Buddies can coexist in one Scene.
- Contraption Workshop sharing is authorized only after the local Blueprint schema/validator is stable and only as the declarative Demo-compatible/Full-required package model in the Next Fest supplement. Do not widen this into generalized mods or arbitrary package payloads.
- Add tests with each behavior and run the relevant unit, headless, journey, native-addon, and standalone checks before handoff.
- Verify changed player-visible behavior interactively in the running game through the configured Godot MCP server when the task requires it, then promote that interaction into committed automation. Interactive verification never substitutes for automated coverage.
- Never silently change confirmed resolution, controls, budgets, path whitelist, AppID ownership, package schema, or scope. Record owner-approved changes before implementation.
- Keep debug visualizers and tuning panels behind development-build guards.
- Treat warnings, invalid UVs, seam jumps, non-finite coordinates, unexpected paint/package paths, dimension/byte-cap violations, hash mismatches, save migration failures, failed image writes, main-thread violations, Steam callback mismatches, lost Scene switching/editor recovery, and lost window recovery as actionable failures.
- Preserve user changes and keep generated `.godot/`, build output, Steam SDK/GodotSteam runtime binaries, development `steam_appid.txt`, character-library test fixtures, generated PNG test output, and artifacts out of source control unless an approved plan explicitly requires a committed fixture.
- Reset Progress behavior for Scene/Buddy identity data must be explicitly implemented by the Scene source-alignment tasks; do not assume the old single `ActiveCharacterId` reset rule is sufficient. Local character documents and imported Workshop library content remain separate from gameplay reset unless an explicit requirement says otherwise.

## Clean-Room Rules

- Do not ship or commit original Interactive Buddy or MECCHA CHAMELEON files, decompiled source, extracted assets, shaders, art, audio, dialogue, skins, UI copy, layouts, names, branding, or likenesses.
- References may be used only to understand broad mechanics and interaction principles. All player-facing painting and Workshop UI, brush rendering, icons, copy, layout, and presentation must be original.
- Do not trace screenshots or reproduce distinctive control arrangements. The required mouse-wheel plus visible-button sizing deliberately uses an independently specified, more discoverable control model.
- Store comparison notes and public URLs only in development documentation.
- Workshop package import/export is authorized only for the specifically approved declarative content types. It must not become a route for arbitrary Resources, scripts, executable content, or generalized mods.

## Continuous Integration

CI runs on GitHub-hosted Linux runners (`ubuntu-latest`). The repository is public, so Actions minutes are free and jobs across branches run in parallel.

A self-hosted runner exists but is stopped and deliberately unused. Do not reintroduce `runs-on: self-hosted`: on a public repository a fork's pull request carries its own workflow file, so a self-hosted label would run untrusted code on the owner's machine.

- **Push, any branch** runs `CI / quick` only: compile, domain unit tests, and the Steam-binary guard.
- **Pull request to `main`** runs `CI / build-test` (the full scenario and journey sweep), `Asset Forge CI / verify`, and the focused `GodotSteam Native Smoke` when its path filters match.
- **Manual dispatch only** runs `CI / full-soak` and `Phase A Character Editor`.

- Never add slow steps to `CI / quick`. It is the per-commit feedback loop and must stay under roughly a minute. New scenarios, journeys, capture gates, native addon gates, and soaks belong in pull-request workflows.
- Do not add `push:` triggers to `Asset Forge CI` or `Phase A Character Editor`. They are pull-request and manual by design.
- Keep the native GodotSteam dependency materialized from the pinned/hash-verified installer; do not commit the addon/runtime binaries simply to make CI easier.
- The runner is Linux. Any test that feeds a path into code calling `Path.GetFullPath` must build that path per OS, for example `OperatingSystem.IsWindows() ? @"C:\save-test" : "/save-test"`. A hardcoded `C:\...` literal is not absolute on Linux: `GetFullPath` silently prefixes the working directory, so lookups miss instead of throwing. This is exactly what made `ProgressStoreTests` fail on Linux.
- Do not reintroduce `--filter "FullyQualifiedName!~ProgressStoreTests"`. That bug is fixed and the full domain suite passes on Linux.
- Allocation tests using `GC.GetAllocatedBytesForCurrentThread()` must retry until the measurement settles. Tiered-JIT promotion is asynchronous, allocates on the measuring thread, and flakes under full-suite load.
- The split between the push job and the pull-request jobs exists for fast feedback, not for cost. Keep it.

## Definition of Done

A task is done only when:

- Its behavior matches the approved requirements and the applicable source-alignment supplement/current numbered task.
- Automated tests cover normal, boundary, failure, memory, threading, and hostile-input paths appropriate to the feature.
- Required standalone Windows/live-Steam checks pass for the task's gate, or are explicitly recorded as external release gates when they cannot run in source-controlled CI.
- Performance remains inside the locked or explicitly superseding measured CPU/GPU/upload budgets.
- Documentation and data schemas reflect the resulting implementation.
- No selectable UI element advertises an unimplemented or deferred feature.
- Paint and imported UGC remain visual-only and face/accent layer order remains authoritative.
- Workshop changes remain inside their specifically authorized declarative package boundary and preserve offline-first single-player behavior.
- Scope-sensitive work proves all three Steam build matrices when relevant: Normal Demo baseline unchanged; Next Fest equals baseline plus its new slice; Full Release equals those plus Full-only breadth.

## Authorized and Deferred Features

**Authorized painting scope:** six local 512×512 RGBA8 part-paint surfaces; locked-frontal paint workspace; zoom/pan/reset view; color wheel; circular brush tooling and approved sizing/eraser/Undo/Erase All behavior; underlay preview/runtime binding; sequential schema migration; atomic whitelisted PNG persistence; and the verification defined by the painting supplement.

**Authorized Steam Workshop v1 scope:** optional GodotSteam 4.22 integration; base-game Workshop owner AppID `5114950`; room-painting publish/download/import; Buddy Studio configuration + declared buddy-paint publish/download/import; explicit apply/select only; local offline copies; strict hostile-data validation; directory-backed emulator; Steam overlay browsing; public tags `Room Painting` and `Buddy`; and null/offline fallback.

**Authorized Normal Demo baseline:** everything already present in `main` or explicitly approved/planned for the Normal Steam Demo under the existing scope remains part of that build unless a separate owner decision changes it. Next Fest and Full Release inherit this baseline; Next Fest curation cannot remove it.

**Authorized multi-Buddy Scene direction:** several player-created Buddy identities may coexist in one active Scene; several named Scene documents may hold different environment/background state and Buddy rosters; Win98-style Scene tabs may switch between them without restarting; only one Scene simulates at a time; account economy/tool ownership stays shared while Buddy-specific emotional/personality state becomes per identity. The Normal Demo does not expose the new Scene/multi-Buddy system; the Next Fest Demo is capped to the approved vertical slice; Full Release starts from the higher profiling target in the Scene source alignment.

**Authorized Next Fest systemic-sandbox slice:** the Next Fest Demo adds the curated construction, Rope/Weld/Hinge constraints, bounded Properties, Button/Timer/Piston/Weapon Trigger automation, Wood/Metal damage slice, Buddy structural integrity/repair, gravity control, local Blueprints, selected new Room/Scene functionality and—after local validation is stable—Demo-compatible contraption Workshop sharing defined in the Next Fest documents. This is **in addition to**, never instead of, the complete Normal Demo baseline. Recommendations in the earlier vertical-slice draft to hide existing Normal Demo tools are superseded.

**Authorized Full Release systemic direction:** Full Release contains the complete Normal Demo baseline and complete Next Fest slice plus Full-only definitions, properties, devices, higher caps, deeper construction/material/destruction/Room Physics content and the other approved Full Release expansion programs.

**Still deferred/forbidden without a new owner decision:** arbitrary custom Workshop Resources/scenes/scripts/DLLs/meshes/native code; generalized mod APIs; Buddy-to-Buddy social AI/relationships/coordination/collision design; multiple independent player economy save slots; unapproved cosmetic progression/economy changes; unapproved painting tools such as material sliders/patterns/gradients/smudge/stamps/layers/blend modes/brush files/tablet pressure/3D orbit/back-side painting; Steam lobbies; matchmaking; P2P/network sockets/SDR; RPCs; `MultiplayerPeer`; shared live rooms; the 30-second friends Damage Sprint leaderboard; Linux release support; and macOS release support.
