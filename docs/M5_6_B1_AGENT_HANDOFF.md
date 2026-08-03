# Milestone 5.6 Task B1 — Coding-Agent Implementation Handoff

Status: **ready for implementation; no B1 production code has been written on this branch.**

Requested execution profile from the owner:

- Model: **GPT-5.6 Sol**
- Reasoning: **medium**
- Working branch: `m5-6-b1-paint-mapping`
- Base commit: `cde2b1cf72f403d6fa618ce52c54d460aede9136`
- Target repository: `vDalisay/desktop-buddy`
- CI state: automatic push/PR triggers are temporarily paused; both workflows remain available through `workflow_dispatch`.

The ChatGPT session that created this handoff could not launch or configure a separate coding
agent. The model/profile above is therefore an execution request for the external agent, not
a claim that the agent is already running.

## Mandatory source order

Read and obey these before editing code:

1. `AGENTS.md`
2. `docs/DECISIONS.md`
3. `docs/M5_5_PHASE_B_PAINTING_SOURCE_ALIGNMENT.md`
4. `docs/M5_5_CHARACTER_EDITOR_SOURCE_ALIGNMENT.md`
5. `docs/PRODUCT_REQUIREMENTS.md`
6. `docs/ARCHITECTURE.md`
7. `docs/TEST_PLAN.md`
8. `docs/AGENT_VERIFICATION_AND_E2E.md`
9. `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`

The Phase B source-alignment document is normative for B1. Do not reinterpret or broaden the scope.

## Goal

Implement **Task B1 only**:

- analytic frontal pointer/ray-to-part and part-local UV mapping for the trusted M3.5 sphere and capsule body primitives;
- deterministic, physics-free frontal paint pose for all six buddy parts;
- orthographic paint-preview camera and canvas transform;
- zoom, pan, pan clamping, Reset View, and part-hover diagnostics;
- engine-free mapping tests plus a Godot headless scenario validating the real trusted meshes.

At the end of B1, a pointer position in the paint canvas must deterministically resolve to a typed hit result containing hit/miss, trusted part, normalized UV, local diagnostic data, and front-surface validity.

B1 must not mutate paint pixels. It creates the targeting and view substrate consumed by B2.

## Strict non-goals

Do not add CPU paint images, stroke mutation, brush, eraser, Undo, Erase All, color-wheel controls, character-schema changes, PNG persistence, runtime paint uploads, Workshop/package types, 3D orbit/back-side painting, gameplay/physics mutation, or catalogue/economy/Work-Play UX changes.

Do not prebuild B2–B6 APIs unless a minimal B1 interface is directly required by a compiled B1 caller or test.

## Architectural constraints

- Reuse the physics-free preview architecture and trusted `BuddyVisualRigView` geometry.
- Do not construct `BuddyRoot`, `RigidBody2D`, damage, economy, or gameplay services.
- Keep mapping math engine-free wherever practical; Godot-facing code owns projection and real-mesh verification only.
- Do not change trusted mesh dimensions, UV conventions, sockets, materials, or `BuddyVisualProfile` to simplify mapping.
- Do not use GPU readback, screenshot sampling, OCR, or rendered pixels as the correctness oracle.
- Coordinates must remain finite and deterministic across supported zoom, pan, resize, and DPI conditions.
- The camera remains locked dead-frontal; no yaw/orbit controls.
- The paint pose is presentation-only and cannot alter the live gameplay rig.
- Scene roots remain thin composition and routing owners.

## Required design work

### Repository inspection

Locate and document the trusted part definitions and primitive dimensions, `BuddyVisualRigView` mesh/socket construction, `StaticBuddyVisualTransformSource`, current preview composition, camera/projection utilities, canonical part identifiers/mirroring, and scenario registration pattern. Reuse existing contracts rather than adding duplicate part enums or geometry descriptions.

### Engine-free primitive mapping

Implement analytic sphere UV mapping, capsule cylindrical-side UV mapping, capsule-cap UV mapping, nearest visible trusted-surface selection for overlaps, and documented left/right orientation behavior matching the Godot 4.6.1 primitive meshes actually used by the project.

Define deterministic behavior for center, silhouette, sphere seam, capsule side/cap boundary, miss, overlap, non-finite input, extreme valid view transforms, and mirror symmetry. Never clamp a geometrical miss into a hit. UV clamping is allowed only for floating-point boundary noise after a valid hit.

### Frontal paint pose

Add a deterministic static, dead-frontal, physics-free pose with head, torso, hands, and feet separated enough to target all six front surfaces. Keep stable depth ordering, no animation/reactions/look-at/gameplay, and apply current character colors/features through the existing preview path. The presentation must remain original to Desktop Buddy.

### Camera and canvas state

Implement focused state for zoom, pan, viewport size, default framing, min/max zoom, reset, pan clamping, anchored zoom, resize, and canvas-to-paint ray/point conversion. Default framing fits the complete paint pose with margin. Pan must remain recoverable. Full production input and visible controls belong to B4.

### Hover diagnostics

Expose development/test observability for hit/miss, hovered part, UV, and useful mapping revision data. Optional markers remain development-only. Do not expose an incomplete user-facing Paint mode during B1.

## Required tests

Pure tests must cover sphere center/silhouette/seam; capsule side, caps, and side-cap seam; misses; non-finite rejection; mirrored limbs; overlap depth; default framing; zoom bounds and anchor stability; pan bounds/reset; minimum and ultrawide resize; and dependency isolation from gameplay physics/economy.

Add and register the headless scenario:

```text
paint_frontal_uv_mapping
```

It must construct the real physics-free trusted preview and verify six paintable parts, no gameplay authority, orthographic frontal camera, representative expected part/UV hits, real mesh convention parity, mirror cases, miss/overlap cases, and mapping stability through pan/zoom/reset. Do not assert screenshot colors.

## Verification

At minimum run:

```bash
dotnet build DesktopBuddy.sln -c Debug
dotnet test tests/DesktopBuddy.Domain.Tests/DesktopBuddy.Domain.Tests.csproj -c Debug
<godot-4.6.1-mono> --headless --path . --import
<godot-4.6.1-mono> --headless --path . -- --scenario=paint_frontal_uv_mapping --seed=1
```

Also run affected regressions including `character_rig_view`, `editor_preview_has_no_physics`, `character_appearance_invalidation`, `editor_mode_lifecycle_accounting`, and `editor_window_restore` using their exact registered IDs.

Automatic GitHub Actions triggers are paused. Run focused verification locally and/or invoke the preserved workflows manually with `workflow_dispatch`. Do not re-enable automatic CI without explicit owner approval.

## Interactive verification

Use the configured Godot MCP workflow with real pointer movement to verify all six parts target correctly, UV movement is continuous, selected visible seams do not jump, zoom/pan retain targeting, Reset View recovers framing, and no gameplay physics runs. Promote useful evidence into committed automation. Do not claim Windows acceptance from headless execution.

## Commit and PR discipline

- Work only on `m5-6-b1-paint-mapping`.
- Preserve existing user changes.
- Use coherent step commits: pure mapping/tests; pose/camera; Godot preview integration; scenario/regressions/docs.
- Do not merge to `main`.
- Open a draft PR targeting `main` after focused gates pass.
- State explicitly that B2–B6 are not implemented.
- Include exact test results and unverified real-Windows behavior honestly.

## Completion response expected

Return the branch and draft PR, commit list mapped to B1 steps, files/contracts changed, UV and seam decisions, test/scenario results, interactive findings, known limitations/Windows checks, and an explicit B2 handoff only after B1 review acceptance.
