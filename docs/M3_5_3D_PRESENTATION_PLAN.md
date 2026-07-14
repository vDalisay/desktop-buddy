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
- **Display-rate independence (owner answer, 2026-07-14).** The performance target is a
  comfortable 60 fps baseline, but presentation must look right on any monitor: the
  physics tick stays at the gate-protected 120 Hz, rendering runs V-synced at whatever
  rate the display provides, and the Task 4 interpolation contract makes motion smooth
  at every refresh rate. "60 fps" is a budget floor the app must comfortably exceed,
  never a render cap — capping at 60 on a 144 Hz panel would judder.
- **Expressive presentation is the next slice, not this one.** The owner direction of
  2026-07-14 (turning/facing, walk/eat/sit performances, head look-at, composed dynamic
  face — "Nintendo-like") is pre-planned in `docs/M3_6_EXPRESSIVE_PRESENTATION_PLAN.md`
  and lands after this slice's exit gate. This slice stays the frontal parity
  conversion, because its A/B gate compares against the 2D build — expressive motion
  has no 2D baseline and needs its own feel gate. Tasks 3–4 must build the seams that
  slice consumes (socket hierarchy, pose-source seam, replaceable face).

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
6. `physics/common/physics_interpolation` is on project-wide (`StartupValidator`
   asserts it stays on) and since Godot 4.4 it also covers `Node3D`. Presenter-driven
   3D nodes are positioned per rendered frame from already-interpolated data, so engine
   interpolation layered on top re-quantizes them to tick boundaries and adds a tick of
   latency — subtle stutter that would fail the exit gate with no visible cause. Every
   created 3D node tree (buddy presenter, tool visuals) and the `WorldCamera3D` sets
   `PhysicsInterpolationMode = Off` (set at the root; children inherit).
7. Frame-rate policy (owner scope above): keep `Engine.MaxFps = 0` (uncapped) under the
   existing V-sync-On default so every monitor renders at its native rate; when the
   user selects V-sync Off, apply a ceiling (`Engine.MaxFps = 240`, a code constant
   with comment) so an always-on desktop overlay never free-spins the GPU.
   `max_physics_steps_per_frame = 6` already bounds very slow displays: below ~20 fps
   the simulation goes briefly slow-motion rather than bursting (ARCHITECTURE §20) —
   accepted, no new handling.

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
and a resized window. Two traps the matrix must cover explicitly: (1) environment — the
3D pass clears per the camera/world `Environment`, and any sky or opaque background
paints over the desktop and kills the transparent shell, so the spike pins the exact
transparent-safe configuration (no `WorldEnvironment`; background respecting
`TransparentBg`) and that configuration is part of the recorded outcome; (2) color
parity — draw one reference color as a 2D rect and as a 3D unshaded quad side by side;
on `gl_compatibility` they must match (gamma pipeline), and if the `forward_plus`
fallback is exercised, record any tonemap/linear shift, because the exit-gate A/B
assumes 2D and 3D render the same profile colors identically. Pass on `gl_compatibility`
or rerun on `forward_plus` and record the fallback. **Record the outcome in
`DECISIONS.md` before starting Task 2.** The spike stays development-only and
export-excluded.

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
adds `GetViewport().Msaa3D = msaa;` beside `Msaa2D`. The camera sets
`PhysicsInterpolationMode = Off` (global constraint 6) so a queued layout change snaps
exactly like the 2D camera instead of easing over a tick. The class comment must state
that `To3DRotationZ` applies to *every* angle crossing the 2D→3D boundary — including
`PuppetPartBody.FaceDrawRotation`'s sideways-emoticon quarter turn (Task 4): copying a
2D angle verbatim renders a sideways face (the idle `":|"` is one) flipped by 180°.
Alignment assertions land with the Task 7 scenario; this task requires a clean build and
the existing suite green.

### Task 3 — `BuddyVisualProfile` and the physics/visual field split (data + integration)
New `BuddyVisualProfile : GameResource` with: six `PartVisualDefinition` sub-resources
(`PartId`, `Color`, `MeshRadiusScale`, `DepthOffset`, `RotationPolicy ∈ {Physics,
ScreenUpright, VelocityAligned}`, velocity-smoothing constant plus a velocity speed
deadband below which orientation holds), a torso `CapsuleHeightScale` (total capsule
height in mesh-radius units, validated `2.0`–`3.0`: `2.0` degenerates to the sphere,
the cap keeps the vertical overshoot ≤ half a radius per end — the physics torso is a
**circle**, so any elongation is silhouette the collider does not have), a connector
list (part-pair, radius, color, depth lane; default graph torso→head plus torso→each
hand/foot, mirroring the five physics links), and face text size/color.
`DepthOffset` exists because buddy parts never collide with each other and the spring
links have no minimum distance, so parts overlap routinely (a hand sits fully inside the
torso circle during guard and ragdoll). In 2D the painter's order hides this; in 3D at a
shared `z = 0` it produces hard intersection seams and fully swallowed parts. Constant
per-part Z lanes reproduce the 2D layering (feet behind torso behind hands behind head,
connectors between) and are provably invisible to the orthographic position/size math,
so the Task 7 alignment check is unaffected. These are draw-order lanes, not the
deferred cosmetic fake-depth feature.
`Validate()` mirrors `PuppetRigProfile.Validate()`: exactly six unique part IDs, valid
connector endpoints, positive scales, `CapsuleHeightScale` within its bound. Seed
`data/buddy/lab_buddy_visual.tres` by transcribing the current circle colors from
`data/buddy/lab_puppet_rig.tres` **before** removing them there.
Migration: delete `FillColor` from `PuppetPartDefinition` (`src/Buddy/Physics/PuppetPartDefinition.cs:15`)
and from the six sub-resources in `lab_puppet_rig.tres`; change
`PuppetPartBody.Configure(definition, globalOrigin)` to
`Configure(definition, Color fill, Vector2 globalOrigin)`; `BuddyRoot` gains
`[Export] BuddyVisualProfile VisualProfile` (wired in `puppet.tscn`), resolves each
part's color from it, and passes plain `Color` values into `Rig.Initialize` — the
physics layer (`src/Buddy/Physics`) must never reference the visual-profile type, or
the split this task creates is re-tangled at birth. The legacy 2D circles then read the
same authoritative visual data as the 3D presenter, and the rig resource is physics-only
afterward. Note: `DualProfileLab.ApplyProfile` loads rig `.tres` paths from the CLI; any
external rig file still carrying `FillColor` lines logs harmless unknown-property
warnings after the migration — sweep committed fixtures and expect the warning on stale
owner-local files.
Startup validation: no caller passes resources to `StartupValidator.Validate` today —
`Bootstrap.cs:88` and both scenario entry points call it bare, so the `resources` seam
(`src/App/StartupValidator.cs:84`) is currently unexercised. The Task 7 scenario
composes the lab and calls `Validate` with the buddy's `VisualProfile` explicitly; this
wiring is new scope, not an append to an existing list.

### Task 4 — `BuddyVisualPresenter` (integration/presentation)
`BuddyVisualPresenter : Node3D` with `[Export] BuddyRoot Buddy`, `[Export] BuddyVisualProfile
Profile`, and an explicit `Initialize()` called by the scene root after the buddy
initializes (composition rule — no tree searching). The presenter reads body state
through a small injectable transform-source seam (default implementation wraps the live
rig); the deferred character editor's preview later feeds this same seam fixed rest-pose
transforms with no physics, and the M3.6 expressive slice swaps in a posed provider —
so the seam is required now, not retrofitted. Build once, **as a socket hierarchy, not
six free-floating trackers**: `PresenterRoot → BodyYaw → {TorsoSocket, HeadSocket,
HandSocketL/R, FootSocketL/R}`, each socket owning its `MeshInstance3D` (SphereMesh for
head/hands/feet, CapsuleMesh for torso sized by the profile `CapsuleHeightScale`) with
mesh radius = the live configured `PuppetPartBody.Radius × MeshRadiusScale` (read from
the bodies, not re-read from a `.tres`, so CLI-swapped rig profiles stay consistent).
In this slice every socket's **global** transform is written straight from its mapped
body — visually identical to flat tracking — and `BodyYaw` stays identity; the
hierarchy exists because M3.6 poses these same sockets (facing turns, activity
performances, head look-at) and must never require a presenter rebuild. The silhouette
never lies *horizontally* about the collision shape; the torso capsule's bounded
vertical elongation over its circular collider is the single accepted exception,
recorded at Task 8. Parts and connectors sit at their profile `DepthOffset` Z lanes;
the `Label3D` face sits at the head lane + mesh radius + a small epsilon so it can
never z-fight or clip into the head sphere. The `Label3D` emoticon is the accepted
*parity* face for this slice and is explicitly replaceable: M3.6 swaps it for a
composed face texture on the head sphere, and `Reactions.CurrentFace` strings remain
the semantic contract either way. All materials Unshaded with profile colors.
Per rendered frame (`_Process`): read each body's interpolated transform (contract
below), map through `WorldPlaneMapping`, apply the per-part rotation policy — Torso
`Physics`; Head `Physics` with the face kept screen-upright by reproducing
`FaceDrawRotation` (`src/Buddy/Physics/PuppetPartBody.cs:48`) **through the rotation
mapping**: the sideways-ASCII quarter turn changes sign under the Y flip, and getting it
wrong shows every colon-style face (the idle `":|"` included) flipped 180° — an error
`face_roundtrip` cannot catch because it compares text only. Hands/feet
`VelocityAligned` with wrap-aware angle smoothing (lerp across ±π, never the long way
round on a walk-direction flip) and the profile speed deadband, below which orientation
holds — solver noise at rest otherwise visibly twitches idle shoes. Connectors: midpoint
position, orient along the offset, and axis-scale a unit-length `CapsuleMesh` built once
(cap distortion is invisible under unshaded flat color) to length = separation − end
radii (clamped ≥ a profile minimum) — never mutate `CapsuleMesh.Height` per frame, which
regenerates the mesh engine-side five times a frame; when separation ≈ 0 (overlapping
parts are routine, Task 3) hold the last orientation instead of normalizing a zero
vector into a NaN basis. Face: poll `Head.Face` and update `Label3D.Text` only on
change.
**Interpolation contract:** perceived smoothness must match the 2D build at any display
refresh rate — the physics tick stays 120 Hz and rendering floats with the monitor
(owner scope, 2026-07-14). Verified against the pinned 4.6.1 GodotSharp package: `GetGlobalTransformInterpolated()` exists
**only on `Node3D`** — there is no `CanvasItem`/`Node2D` equivalent, and a plain 2D
transform read always returns the raw last-tick value while the 2D circles render
engine-interpolated. The presenter therefore owns interpolation; the manual path is the
design, not a fallback. Pairing is the subtle part and must be exactly this:
- **Previous** sample: the scene root's `_PhysicsProcess` calls the presenter's
  `CaptureTickSnapshot()` **unconditionally, outside the lab pause gate** (beside
  `Pointer.ResolvePendingInput()`). Physics steps *after* `_PhysicsProcess`, so this
  reads end-of-previous-step transforms — one snapshot per engine tick into
  preallocated arrays. Capturing only on routed ticks is wrong: while paused, the
  interpolation fraction keeps sawing 0→1 each tick and a stale previous sample makes
  the buddy visibly shimmer between two poses (the 2D side avoids this the same way —
  `SetBodiesFrozen` calls `ResetPhysicsInterpolation()`).
- **Current** sample: read fresh in `_Process` (end of the latest completed step).
- Render transform = lerp(previous, current, `Engine.GetPhysicsInterpolationFraction()`).
  This reproduces the engine's own 2D pairs with zero added latency at every refresh
  rate: on 60 Hz displays two ticks pass per rendered frame (the pair is still the
  adjacent one), and on >120 Hz displays some frames see no new tick (previous/current
  hold, only the fraction advances). Pairing two root-fed samples instead would run one
  tick (~8.3 ms) behind the 2D impact VFX that stay visible in `Mii3D` mode.
Teleport seam: `PuppetRig.ResetToSafePose` exposes no event, and `RecoveryComponent` is
its sole runtime owner — subscribe `Recovery.HardRecovered` and snap both snapshots
there (the rig already calls `ResetPhysicsInterpolation()` per body for the 2D side,
`src/Buddy/Physics/PuppetRig.cs:101`) so a fail-safe teleport cannot smear a ghost
trail. The capture call is root-invoked, so the §23 no-presenter-`_PhysicsProcess` rule
holds.

### Task 5 — Composition and toggle in lab and sandbox (integration)
`buddy_lab.tscn` and `sandbox.tscn` both add the presenter node and a `Camera3D` wired
into `RoomBounds.WorldCamera3D`. Scene roots (`BuddyLab.cs`, `SandboxRoot.cs`) gain
`[Export] PresentationMode Mode` (`LegacyCircles`, `Mii3D`), applied as visibility:
`Mii3D` hides the six `PuppetPartBody` canvas items and shows the presenter; visibility
does not affect `RigidBody2D` simulation, so the flip is a pure view change and is
runtime-safe. Lab-only raw key `V` in `LaboratoryControlComponent` flips the mode live for
the owner A/B (same dev-guarded pattern as `P`/`U`/`H`). Headless enablement: extend
`RunnerArguments` (Domain, xUnit-tested like the existing `ProfileA`/`DriveA` overrides)
with a `--presentation=legacy|mii3d` argument that scene roots apply at compose time —
this is the mechanism the Task 7 reruns use while the committed default stays
`LegacyCircles` in both scenes until Task 8. `puppet.tscn` changes only by the Task 3
`VisualProfile` export.

### Task 6 — Boxing Glove 3D counterpart (pattern-setter; may trail Task 7)
Generic `Body2DVisual3D : Node3D` tracker (mesh parameters or a small typed profile)
reusing the Task 4 transform source and mapping. The glove body is **dynamic**:
`BoxingGloveController.Spawn()/Despawn()` creates and `QueueFree`s a fresh
`BoxingGloveBody` on every tool selection, so an `[Export]`-wired target cannot exist at
scene load — the tracker attaches/detaches on the controller's spawn/despawn seam (add a
spawn event or expose the active body; guard with `IsInstanceValid`; snap interpolation
snapshots on attach, since the body teleports to the cursor at spawn). Every M5 tool is
dynamic too, so this attach/detach lifecycle *is* the pattern this task exists to set.
Feel parity: `BoxingGloveBody._Draw` renders the owner-accepted impact pulse — squash/
stretch `(1 − 0.24p, 1 + 0.18p)` rotated to the impact normal — from private pulse
state; expose intensity/angle read-only and reproduce the pulse as a nonuniform mesh
scale through the mapping, or `Mii3D` gloves silently lose accepted M3 polish. `Mii3D`
visibility handling symmetric to the buddy's. This establishes the tool-visual pattern
Milestone 5 will follow (documented in the Task 8 ARCHITECTURE amendment). A mixed 2D
glove over a 3D buddy is acceptable only inside the lab and only until this task lands;
it must land before M5 starts.

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
  `Camera3D.UnprojectPosition(WorldPlaneMapping.To3D(world))`; `|Δ| < 0.5 px`. Both
  sides must share one frame of reference: `UnprojectPosition` uses the live viewport
  size while the formula uses the layout client size, and `BuddyLab` falls back to
  480×360 when a headless viewport reports zero — drive the real (headless) window size
  the way `room_resize_zoom` already does, or compute the expected pixel from
  `GetViewport().GetVisibleRect()`.
- `mode_toggle_physics_invariant` — flip modes; visibilities swap; strike again; the
  accepted pain is equal under both modes.
Reruns with `Mii3D` explicitly enabled (defaults unchanged until Task 8):
`room_resize_zoom`, `idle_soak_ci`, `repeat_envelope`, `m3_presentation`,
`tool_feel_reactions`; journeys `lab_spawn_settle`, `m3_glove_strike`. Verdicts must match
the legacy baselines. MCP interactive pass per `AGENTS.md`: launch the lab, `V`-toggle,
drive grab/strike/knockout/recovery through real input, capture screenshots as evidence —
including a named sideways-face orientation check (idle `":|"`, pet `":3"`, knockout
`"x_x"`): the Task 4 quarter-turn sign error renders text-identical but visually
flipped, so only this pass can catch it.

### Task 8 — Acceptance flip, demotion, and documentation (owner-gated)
Only after the exit gate: flip the default `PresentationMode` to `Mii3D` in both scenes
and rerun `tools\quick_validate.bat` plus the full `idle_soak`; demote `LegacyCircles` to
a lab-only debug view or delete it (owner choice recorded at the gate); amend
`ARCHITECTURE.md` §14 (3D layer, mapping contract, visual/physics resource split, tool
pattern), `ROADMAP.md` M7 wording, and `README.md` status; record in `DECISIONS.md`:
(1) frontal 3D presentation replaces the 2D vector buddy, (2) customization is visual-only
forever — the future editor writes visual profiles and never rig/drive tuning,
(3) the Task 1 renderer outcome, (4) the accepted torso-capsule silhouette exception
with the shipped `CapsuleHeightScale`, (5) the frame-rate policy (120 Hz tick, uncapped
V-synced rendering, V-sync-Off ceiling). The default flip is its own commit.

## Exit gate (owner-manual)

Side-by-side A/B via the lab `V` toggle at 100% and one clamped zoom, on real Windows with
the transparent shell: idle, walk, jump, grab/throw, glove hit including guard, knockout
collapse, and recovery. The owner explicitly accepts the look and confirms smoothness
parity with the 2D build at the display's native rate — judged on a 60 Hz monitor (the
target baseline) and spot-checked on one high-refresh monitor when available. Known
intentional difference to judge (not a bug):
the 2D circles carry 2 px dark outlines; unshaded 3D spheres have none and read flatter
against a busy desktop — if rejected, an inverted-hull outline shell is the
Unshaded-compatible fix. The automated green suite never substitutes for this judgment
(AGENTS.md Definition of Done).

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

- Orientation (turning/facing), performance animations (walk/eat/sit/jump dressing),
  head look-at, and the composed dynamic face — the expressive slice, pre-planned in
  `docs/M3_6_EXPRESSIVE_PRESENTATION_PLAN.md`, scheduled after this slice's exit gate
  and before Milestone 4 (whose exit criteria — visibly differentiated moods — it
  serves). This slice only builds its seams (socket hierarchy, pose source, replaceable
  face).
- Full character editor, custom painting, and Steam Workshop — own milestone consuming
  the Task 3 seam; detailed pre-plan in `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`
  (nice-to-have, unscheduled).
- Preset visual variants, cosmetic fake-depth lanes (distinct from the Task 3
  `DepthOffset` draw-order lanes, which are required parity infrastructure), 3D VFX,
  lit or toon-shaded materials beyond Unshaded, 3D visuals for M5 tools beyond the
  glove pattern, and any skinned-mesh or IK connector work.

## Progress

Plan refined for agent handoff 2026-07-14 on the analysis worktree (baseline `m3-sol`
`80fb22b`).

**2026-07-14 — M3 Task 12 owner gate ACCEPTED** (recorded in
`docs/M3_INTERACTION_DAMAGE_PLAN.md`), so Tasks 2+ are unblocked and owner feel judgments
stay uncoupled from M3's.

**Task 1 (renderer spike) DONE (2026-07-14).** `TransparentWindowSpike.cs` extended with
the orthographic `Camera3D`, unshaded primitives, the transparent-safe config, a 2D/3D
color-parity pair, and interactive MSAA/V-sync/resize controls. Build clean (0/0), MCP
launch produced no spike errors, 3D composites in the 2D viewport at mapping-predicted
pixels, and color parity passes on `gl_compatibility`. The owner ran the real-hardware
matrix (desktop-through-alpha, DPI 100–200%, `Msaa3D` Off/2×/4×/8×, V-sync both states,
resize) and **confirmed a full pass** — outcome recorded in `DECISIONS.md`. **Task 2 is
unblocked.**

Amended 2026-07-14 after a code-verified review. Substantive changes: the 2D
interpolated-transform API does not exist in the pinned 4.6.1 GodotSharp, so Task 4's
manual snapshot path is the primary design with an exact pairing contract
(unconditional per-tick root capture + fresh `_Process` read; reset snap via
`Recovery.HardRecovered`); global constraint 6 added (`PhysicsInterpolationMode = Off`
on presenter-driven 3D nodes and the camera — project interpolation is on and covers
3D since 4.4); Task 3 schema gained `DepthOffset` draw-order lanes (parts
interpenetrate by design), a bounded torso `CapsuleHeightScale` (physics torso is a
circle), and a velocity deadband; Task 6 now covers the glove's dynamic spawn
lifecycle and impact-pulse parity; the `StartupValidator` wiring description was
corrected (no caller passes resources today) and `camera_alignment` got a
frame-of-reference rule; the sideways-face rotation-sign trap is documented in
Tasks 2/4/7; `--presentation=` runner argument specified (Task 5); the Task 1 spike
matrix gained environment-transparency and 2D/3D color-parity checks.

Refined again 2026-07-14 on owner direction: display-rate independence added to the
owner scope (60 fps is a budget floor; physics stays 120 Hz; rendering is uncapped and
V-synced; constraint 7 and the reworded exit gate carry the policy), and the expressive
direction (turning, activity animations, look-at, composed face) was pre-planned as its
own slice in `docs/M3_6_EXPRESSIVE_PRESENTATION_PLAN.md` — Task 4 now builds the socket
hierarchy and replaceable face that slice requires, so nothing here is rebuilt later.
