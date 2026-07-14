# Milestone 3.5 — Frontal 3D Presentation Conversion

Authoritative scope: owner scope answers of 2026-07-14 (below) plus `docs/ROADMAP.md` and
`docs/ARCHITECTURE.md` §14/§20–§21 (`docs/DECISIONS.md` wins on conflict). Baseline:
committed M3 state `80fb22b` on `m3-sol`. This slice inserts between the M3 exit gate and
Milestone 4 and renumbers nothing; it amends the Milestone 7 "final original vector
visuals" wording on completion (Task 8).

**Prime invariant, every task:** the 2D physics simulation stays the sole authority. No
change to any physics profile, fixed-tick order, scenario expectation, or envelope bound
anywhere in this slice. 3D is rendering only; presenters read body state and never write
it, and never register `_PhysicsProcess` (ARCHITECTURE §23 single-entry rule).

## Owner-confirmed scope (2026-07-14)

- **Flat stage, 3D look.** The buddy keeps moving on the 2D plane (left/right + jump,
  facing the camera); no depth locomotion, no 3D physics.
- **Floating parts.** Head, torso, and detached hand/foot meshes tracking the six bodies,
  plus thin connector capsules — no skeleton, no IK, no skinned mesh.
- **Procedural in-engine assets.** Godot primitive meshes and unshaded materials; no
  external art pipeline. Original character only (clean-room: "Mii-inspired" means
  proportions and simplicity, never Nintendo trade dress, assets, or likeness).
- **One fixed character now; full character editor later.** The editor is deferred to its
  own milestone and must be enabled by the Task 3 seam: customization writes visual
  profiles only and can never touch rig/drive tuning, so it never re-opens the M1 gate.

## Design seams

| Worker | Home | Responsibility |
| --- | --- | --- |
| Plane mapping | `src/Presentation3D/WorldPlaneMapping.cs` | The single 2D↔3D mapping authority: `(x, y) → (x, −y, 0)`, `rot3dZ = −rot2d`. |
| World camera 3D | `src/Sandbox/BoundaryController.cs` | Orthographic `Camera3D` driven from `ApplyLayout` beside the existing `Camera2D`; boundary controller stays the only room/zoom authority. |
| Visual profile | `src/Buddy/Presentation3D/BuddyVisualProfile.cs` + `data/buddy/lab_buddy_visual.tres` | Typed per-part visual data, connector graph, rotation policy; the future editor seam. |
| Buddy presenter | `src/Buddy/Presentation3D/BuddyVisualPresenter.cs` | Builds and tracks six part meshes, connectors, and the face; read-only. |
| Generic tool visual | `src/Presentation3D/Body2DVisual3D.cs` | Reusable 2D-body→3D-mesh tracker (Boxing Glove now; M5 tool pattern later). |
| Presentation toggle | `BuddyLab.cs` / `SandboxRoot.cs` scene roots | `LegacyCircles` / `Mii3D` mode switch; runtime-flippable in the lab for owner A/B. |

## Global constraints (all tasks)

1. Zero per-frame managed allocation in tracking paths; cache mesh/label/material arrays
   at initialization (ARCHITECTURE §23 allocation policy applies to `_Process` here).
2. Every new visual constant lives in `BuddyVisualProfile`, never as a literal in logic.
   View-plumbing constants (camera Z distance, near/far) are code constants with an
   explanatory comment — they are provably invisible to the orthographic result.
3. No new player-facing text (faces remain the existing data strings), so no new
   translation keys. Lab toggle key follows the existing raw-key dev-guarded pattern in
   `LaboratoryControlComponent`, not `InputActions`.
4. Headless-neutral: all scenarios pass headless in both presentation modes.
5. Verify interactively through the Godot MCP tier per `AGENTS.md`, then keep the
   committed scenario/journey as the promotion target; interactive evidence never
   substitutes for automated coverage.

## Tasks

### Task 1 — Renderer spike (gate for everything below)
Extend `scenes/spike_transparent_window.tscn` / `TransparentWindowSpike.cs` (currently a
2D-only readout, `src/Laboratory/TransparentWindowSpike.cs`): add an orthographic
`Camera3D` (`Size = 360`, positioned `(240, −180, +500)` looking −Z per the Task 2
mapping) and three or four `MeshInstance3D` primitives (sphere + capsule) with
`StandardMaterial3D.ShadingMode = Unshaded` near `z = 0`. 3D nodes under a 2D root share
the viewport's `World3D`, so no scene restructure is needed. Validate on Windows 10/11:
desktop visible through empty alpha with 3D content composited; `Msaa3D` at Off/2×/4×/8×
(set beside the existing `Msaa2D`); V-sync both states; DPI 100–200%; the 480×360 default
and a resized window. Pass on `gl_compatibility` or rerun on `forward_plus` and record the
fallback. **Record the outcome in `DECISIONS.md` before starting Task 2.** The spike stays
development-only and export-excluded.

### Task 2 — Plane mapping and world camera (integration)
`WorldPlaneMapping` static class: `To3D(Vector2) => new Vector3(p.X, −p.Y, 0)`;
`To3DRotationZ(float rot2d) => −rot2d` (the Y flip inverts handedness). Document the
round-trip contract against the `Camera2D` pixel mapping in the class comment.
`BoundaryController` gains an optional `[Export] Camera3D WorldCamera3D`; `ApplyLayout`
(the method already setting `WorldCamera.Position = (W/2, H/2)` and
`Zoom = EffectiveZoom`, `src/Sandbox/BoundaryController.cs:109`) additionally sets, when
assigned: `Projection = Orthogonal`, `KeepAspect = Height`, `Size = (float)layout.RoomHeight`,
`Position = (W/2, −H/2, CameraDistance)`, identity look direction (−Z). Null-safe so
scenes without a 3D camera (e.g. `dual_profile_lab.tscn`) stay valid.
`DesktopWindowController.ApplyRenderSettings` (`src/Platform/DesktopWindowController.cs:137`)
adds `GetViewport().Msaa3D = msaa;` beside `Msaa2D`. Alignment assertions land with the
Task 7 scenario; this task requires a clean build and the existing suite green.

### Task 3 — `BuddyVisualProfile` and the physics/visual field split (data + integration)
New `BuddyVisualProfile : GameResource` with: six `PartVisualDefinition` sub-resources
(`PartId`, `Color`, `MeshRadiusScale`, `RotationPolicy ∈ {Physics, ScreenUpright,
VelocityAligned}`, velocity-smoothing constant), a connector list (part-pair, radius,
color; default graph torso→head plus torso→each hand/foot), and face text size/color.
`Validate()` mirrors `PuppetRigProfile.Validate()`: exactly six unique part IDs, valid
connector endpoints, positive scales. Seed `data/buddy/lab_buddy_visual.tres` by
transcribing the current circle colors from `data/buddy/lab_puppet_rig.tres` **before**
removing them there.
Migration: delete `FillColor` from `PuppetPartDefinition` (`src/Buddy/Physics/PuppetPartDefinition.cs:15`)
and from the six sub-resources in `lab_puppet_rig.tres`; change
`PuppetPartBody.Configure(definition, globalOrigin)` to
`Configure(definition, Color fill, Vector2 globalOrigin)`; `BuddyRoot` gains
`[Export] BuddyVisualProfile VisualProfile` (wired in `puppet.tscn`) and passes each
part's color from the visual profile at `Rig.Initialize` time, so the legacy 2D circles
read the same authoritative visual data as the 3D presenter. The rig resource is
physics-only afterward. Scene roots append the visual profile to the resources list they
pass to `StartupValidator.Validate` (the `GameResource` seam, `src/App/StartupValidator.cs:84`).

### Task 4 — `BuddyVisualPresenter` (integration/presentation)
`BuddyVisualPresenter : Node3D` with `[Export] BuddyRoot Buddy`, `[Export] BuddyVisualProfile
Profile`, and an explicit `Initialize()` called by the scene root after the buddy
initializes (composition rule — no tree searching). Build once: six `MeshInstance3D`
(SphereMesh for head/hands/feet, CapsuleMesh for torso) with mesh radius = physics
`PuppetPartDefinition.Radius × MeshRadiusScale` so the silhouette never lies about the
collision shape; connector capsules per the profile graph; one `Label3D` on the head.
All materials Unshaded with profile colors.
Per rendered frame (`_Process`): read each body's interpolation-aware transform, map
through `WorldPlaneMapping`, apply the per-part rotation policy — Torso `Physics`; Head
`Physics` with the face kept screen-upright by reproducing the existing
`FaceDrawRotation` counter-rotation (`src/Buddy/Physics/PuppetPartBody.cs:47`); hands/feet
`VelocityAligned` with smoothing, because the freely spinning circles are invisible in 2D
but a spinning shoe is not. Connectors: midpoint position, orient along the offset,
length = separation − end radii (clamped ≥ a profile minimum). Face: poll `Head.Face` and
update `Label3D.Text` only on change.
**Interpolation contract:** the 120 Hz smoothness must match the 2D build. Preferred
source is `GetGlobalTransformInterpolated()` — the symbol ships in the pinned 4.6.1
GodotSharp; verify at compile time that it is exposed on `CanvasItem`/`Node2D`. If it is
`Node3D`-only, implement the documented fallback: the presenter caches previous/current
fixed-tick transforms (fed from the scene root's existing fixed-tick route) and lerps by
`Engine.GetPhysicsInterpolationFraction()`. In the fallback case, snap both snapshots when
`PuppetRig.ResetToSafePose` fires (it already calls `ResetPhysicsInterpolation()` per body,
`src/Buddy/Physics/PuppetRig.cs:101`) so a fail-safe teleport cannot smear a ghost trail.

### Task 5 — Composition and toggle in lab and sandbox (integration)
`buddy_lab.tscn` and `sandbox.tscn` both add the presenter node and a `Camera3D` wired
into `RoomBounds.WorldCamera3D`. Scene roots (`BuddyLab.cs`, `SandboxRoot.cs`) gain
`[Export] PresentationMode Mode` (`LegacyCircles`, `Mii3D`), applied as visibility:
`Mii3D` hides the six `PuppetPartBody` canvas items and shows the presenter; visibility
does not affect `RigidBody2D` simulation, so the flip is a pure view change and is
runtime-safe. Lab-only raw key `V` in `LaboratoryControlComponent` flips the mode live for
the owner A/B (same dev-guarded pattern as `P`/`U`/`H`). Default stays `LegacyCircles` in
both scenes until Task 8. `puppet.tscn` changes only by the Task 3 `VisualProfile` export.

### Task 6 — Boxing Glove 3D counterpart (pattern-setter; may trail Task 7)
Generic `Body2DVisual3D : Node3D` tracker (`[Export]` target body + mesh parameters or a
small typed profile) reusing the Task 4 transform source and mapping; instance one for
`BoxingGloveBody` with `Mii3D` visibility handling symmetric to the buddy's. This
establishes the tool-visual pattern Milestone 5 will follow (documented in the Task 8
ARCHITECTURE amendment). A mixed 2D glove over a 3D buddy is acceptable only inside the
lab and only until this task lands; it must land before M5 starts.

### Task 7 — `presentation_3d` scenario and regression reruns (integration/testing)
New `src/Testing/Presentation3DScenario.cs`, id `presentation_3d`, registered in
`ScenarioCatalog` and listed in `TEST_PLAN.md`. Checks, in the `StartupCheck` style of
`M3PresentationScenario`:
- `visual_profile_valid` — the startup report contains a passing `resource:` check for
  the visual profile.
- `presenter_built` — six part visuals, connector count equals the profile graph, head
  `Label3D` present.
- `face_roundtrip` — `ScenarioSteps.StrikePart(head)` → `Reactions.CurrentFace == ">_<"`
  → `Label3D.Text` equals `CurrentFace`.
- `camera_alignment` — for every supported zoom at 480×360 plus one resized client
  (e.g. 700×520): expected screen pixel of the torso from the `ApplyLayout` math
  (`screen = (world − cameraCenter) × zoom + client/2`) versus
  `Camera3D.UnprojectPosition(WorldPlaneMapping.To3D(world))`; `|Δ| < 0.5 px`.
- `mode_toggle_physics_invariant` — flip modes; visibilities swap; strike again; the
  accepted pain is equal under both modes.
Reruns with `Mii3D` explicitly enabled (defaults unchanged until Task 8):
`room_resize_zoom`, `idle_soak_ci`, `repeat_envelope`, `m3_presentation`,
`tool_feel_reactions`; journeys `lab_spawn_settle`, `m3_glove_strike`. Verdicts must match
the legacy baselines. MCP interactive pass per `AGENTS.md`: launch the lab, `V`-toggle,
drive grab/strike/knockout/recovery through real input, capture screenshots as evidence.

### Task 8 — Acceptance flip, demotion, and documentation (owner-gated)
Only after the exit gate: flip the default `PresentationMode` to `Mii3D` in both scenes
and rerun `tools\quick_validate.bat` plus the full `idle_soak`; demote `LegacyCircles` to
a lab-only debug view or delete it (owner choice recorded at the gate); amend
`ARCHITECTURE.md` §14 (3D layer, mapping contract, visual/physics resource split, tool
pattern), `ROADMAP.md` M7 wording, and `README.md` status; record in `DECISIONS.md`:
(1) frontal 3D presentation replaces the 2D vector buddy, (2) customization is visual-only
forever — the future editor writes visual profiles and never rig/drive tuning,
(3) the Task 1 renderer outcome. The default flip is its own commit.

## Exit gate (owner-manual)

Side-by-side A/B via the lab `V` toggle at 100% and one clamped zoom, on real Windows with
the transparent shell: idle, walk, jump, grab/throw, glove hit including guard, knockout
collapse, and recovery. The owner explicitly accepts the look and confirms 120 Hz
smoothness parity with the 2D build. The automated green suite never substitutes for this
judgment (AGENTS.md Definition of Done).

## Verification commands

```sh
dotnet build DesktopBuddy.sln            # 0 warnings/errors
dotnet test                              # domain suite unchanged and green
<godot> --headless --path . --import
<godot> --headless --path . -- --scenario=presentation_3d --seed=1
# Task 7 rerun list, each with Mii3D enabled:
<godot> --headless --path . -- --scenario=room_resize_zoom --seed=1
<godot> --headless --path . -- --scenario=idle_soak_ci --seed=1 --fixed-fps 120
<godot> --headless --path . -- --scenario=repeat_envelope --seed=1
<godot> --headless --path . -- --scenario=m3_presentation --seed=1
<godot> --headless --path . -- --scenario=tool_feel_reactions --seed=1
<godot> --headless --path . -- --journey=lab_spawn_settle --seed=1
<godot> --headless --path . -- --journey=m3_glove_strike --seed=1
tools\play_buddy_lab.bat                 # owner A/B via the V toggle
tools\quick_validate.bat                 # before any handoff
```

## Deferred

- Full character editor, custom painting, and Steam Workshop — own milestone consuming
  the Task 3 seam; detailed pre-plan in `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`
  (nice-to-have, unscheduled).
- Preset visual variants, cosmetic fake-depth lanes, 3D VFX, lit or toon-shaded materials
  beyond Unshaded, 3D visuals for M5 tools beyond the glove pattern, and any skinned-mesh
  or IK connector work.

## Progress

Plan refined for agent handoff 2026-07-14 on the analysis worktree (baseline `m3-sol`
`80fb22b`). No tasks started. The M3 Task 12 owner feel/HUD gate is still open: Task 1
(spike) may proceed in parallel, but Tasks 2+ should follow the M3 gate so owner feel
judgments stay uncoupled.
