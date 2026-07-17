# M3.5 Production Materials and Look — Variant C Adoption

Status: **planned, unbuilt; owner direction accepted 2026-07-15**. This task inserts
after M3.5 Task 7 and before the still-paused Task 8 default flip. It productionizes
the accepted Variant C result from `docs/M3_5_LOOKDEV_SPIKE_PLAN.md` without changing
the authoritative 2D physics, adding expressive animation early, or shipping the
lookdev scene.

## Accepted target

- Soft matte toon shading from built-in `StandardMaterial3D`: Lambert diffuse,
  toon specular, specular `0.08`, roughness `1.0`.
- Transparent-safe two-light rig with no `WorldEnvironment`: warm key at energy `0.75`
  and cool camera-axis fill at energy `0.70`; shadows off.
- Ink-colored inverted-hull outlines on the six buddy part meshes: unshaded, front-face
  culled, grow amount `1.5`. Connectors remain unoutlined.
- Existing visual-profile colors remain the base albedos. Shading intentionally changes
  rendered pixels; exact 2D/3D pixel-color parity is no longer an acceptance criterion.
- The expressive target is a roughly 60-degree three-quarter read, represented by about
  30 degrees of yaw from dead-frontal. Dynamic facing is M3.6 scope. This task fixes the
  depth-lane transform order and proves it at 30 degrees, but keeps normal M3.5 tracking
  at identity yaw.
- The spike's primitive dot face was a decision aid only. M3.5 retains the replaceable
  semantic `Label3D` face; M3.6 builds the accepted composed procedural face.

## Invariants and scope boundary

1. `RigidBody2D` and the six-body rig remain the only gameplay/interaction truth. No
   physics profile, body shape, drive force, tool response, pain, mood, or economy value
   changes.
2. No `WorldEnvironment`, sky, perspective camera, shadow map, external texture, model,
   or Nintendo-derived asset enters the shipping presentation.
3. All look constants live in typed Resources. Scene roots compose and route only;
   presenters and the lighting component own rendering details.
4. Materials, meshes, and outline shells are built once and cached. The render path has
   zero per-frame managed allocation and never mutates a mesh Resource per frame.
5. Normal M3.5 output remains front-tracking until M3.6. A development/scenario-only yaw
   drive may exercise the accepted 30-degree pose; no player-facing facing control is
   introduced here.
6. The Boxing Glove retains its accepted red visual and gameplay behavior in this task.
   It remains compatible with the light rig; extending the Variant C outline language
   to tools requires a separate owner-reviewed tool look because their ink colors and
   silhouettes were not part of the spike decision.

## Ownership and data

| Worker | Home | Responsibility |
| --- | --- | --- |
| Look profile | `src/Buddy/Presentation3D/BuddyLookProfile.cs` + `data/buddy/lab_buddy_look.tres` | Typed shading, light, and outline data with validation. |
| Material library | `src/Buddy/Presentation3D/BuddyLookMaterialLibrary.cs` | Builds/caches lit per-color materials and the shared unshaded outline material. No node ownership. |
| Lighting rig | `src/Buddy/Presentation3D/BuddyLookLightingRig.cs` | Owns/configures exactly two `DirectionalLight3D` children from the injected look profile. |
| Presenter | `src/Buddy/Presentation3D/BuddyVisualPresenter.cs` | Consumes the look profile/library, builds six outline shells, and applies depth lanes in camera space after pose/yaw resolution. |
| Composition | `buddy_lab.tscn`, `sandbox.tscn`, and their focused roots | Inject the same look profile into presenter and lighting rig; no material construction. |

`BuddyVisualProfile` gains one required typed `BuddyLookProfile Look` reference. Its
validation delegates into the nested look profile so missing or invalid look data fails
startup. The future character editor may choose base colors and bounded visual options,
but can never write physics/drive data; the accepted default look remains a Resource,
not code literals.

Required look fields and accepted defaults:

| Field | Accepted default |
| --- | --- |
| `DiffuseMode` | Lambert (serialized value `1`) |
| `SpecularMode` | Toon |
| `Specular` | `0.08` |
| `Roughness` | `1.0` |
| `KeyColor` / `KeyEnergy` | `(1.0, 0.98, 0.94)` / `0.75` |
| `KeyEulerDegrees` | pitch `-35`, yaw `-30`, roll `0` |
| `FillColor` / `FillEnergy` | `(0.85, 0.90, 1.0)` / `0.70` |
| `FillEulerDegrees` | camera-axis identity |
| `ShadowsEnabled` | `false` (validation rejects `true` for the accepted profile) |
| `OutlineColor` | `#183042` |
| `OutlineGrowAmount` | `1.5` |

## Transform contract — yaw before camera-space lanes

The lookdev spike proved that a local Z painter lane becomes a visible X displacement
when its parent is yawed. The production transform order is therefore:

1. Read/interpolate the mapped 2D part pose with Z = 0.
2. Resolve the presentation pose and `BodyYaw` with no painter-lane component.
3. Apply the part's `DepthOffset` as a **global camera-axis Z addition** to the resolved
   result.
4. Apply the identical final transform/depth to the visible mesh and its outline shell.

At identity yaw this must reproduce the current M3.5 projection. At the accepted
30-degree scenario yaw, changing a part's `DepthOffset` may change only projected draw
order/depth; it must not add a screen-X displacement. M3.6 consumes this ordering when
it adds dynamic three-quarter facing.

## Tasks

### L1 — Typed look profile and validation

Add `BuddyLookProfile`, the accepted lab Resource, the required reference from
`BuddyVisualProfile`, validation for finite/ranged energies/specular/roughness/grow and
finite colors/angles, plus failure-path tests. Do not expose a shader-choice catalog:
the accepted production style is one typed configuration, while enum fields mirror the
Godot material settings for validation and inspection.

### L2 — Cached soft-toon materials

Add `BuddyLookMaterialLibrary`. Replace the presenter's unshaded part/connector
materials with cached Standard materials using the accepted Lambert/toon settings;
base `AlbedoColor` continues to come from each part/connector visual definition. Build
one shared unshaded ink material for outline shells. Assert no material or mesh Resource
is created or mutated from `_Process`.

### L3 — Transparent-safe two-light rig

Add `BuddyLookLightingRig`, creating exactly one key and one fill light during explicit
initialization. Compose it in lab and sandbox from the same look Resource. Both lights
have shadows disabled and inherit `PhysicsInterpolationMode = Off`; no environment is
created. Startup fails on missing profile/references rather than silently reverting to
flat output.

### L4 — Outline shells and camera-space depth lanes

For head, torso, both hands, and both feet, build one cached duplicate shell from the
same mesh with the shared outline material. Do not outline connectors or the parity
face. Refactor final transform application to the yaw-before-lane contract above.
Outline and primary mesh always share pose, scale, and final lane; only grow/culling
differ. Keep `BodyYaw` identity in normal M3.5 composition.

### L5 — Automated parity and regression coverage

Extend `presentation_3d` or add a focused `presentation_look` scenario with:

- `look_profile_valid` — accepted profile passes; missing/non-finite/negative fields
  fail with actionable names.
- `soft_toon_material_contract` — six parts and five connectors have cached Lambert
  materials with exact base albedos and accepted specular/roughness settings.
- `transparent_safe_light_contract` — exactly two configured directional lights, no
  environment, shadows off.
- `outline_contract` — exactly six primary shells, shared ink material, grow `1.5`,
  front-face culling; no connector outline.
- `camera_space_depth_lane` — identity projection is unchanged; at ±30-degree yaw,
  changing only lane Z adds less than `0.5 px` screen-X error.
- `look_toggle_physics_invariant` — mode/yaw/look changes do not change accepted pain,
  body transforms, collision, payout, or mood mutation.
- `look_idle_soak` — all transforms/material references remain finite and object counts
  remain constant through the existing 21,600-tick soak.

Rerun the full M3.5 Task 7 list explicitly under `--presentation=mii3d`, then
`tools\quick_validate.bat`. The normal legacy baseline verdicts must remain unchanged.

### L6 — Interactive acceptance and handoff to Task 8

Through Godot MCP and then the owner on real Windows, exercise the real lab/sandbox
input paths: idle, walk, jump, grab/throw, glove strike/guard, knockout, and recovery at
100% plus a clamped zoom. Capture a calm and busy-desktop example. Inspect a temporary
30-degree yaw through the development tier to confirm the lane fix, but do not ship a
static yaw behavior.

Acceptance asks whether the production result preserves the adopted Variant C shading,
matte response, outline weight, and busy-background readability while motion remains as
smooth as the legacy view at 60 Hz and a high-refresh display when available. Automated
green cannot substitute for this judgment.

Only after this gate may M3.5 Task 8 flip the default to `Mii3D`, apply the owner's
legacy-view disposition, and update final architecture/roadmap/readme status. The
default flip remains its own commit.

## Definition of done

- Accepted look data is typed, validated, and shared by lab/sandbox.
- Shipping 3D uses the accepted built-in soft-toon materials and shadowless two-light
  rig with no environment.
- The six buddy parts have stable inverted-hull outlines; connectors do not.
- Depth lanes are camera-space after pose/yaw and pass the 30-degree projection test.
- No physics/gameplay behavior or accepted M3 feel value changes.
- Focused scenarios, M3.5 rerun matrix, soak, quick suite, and interactive evidence are
  green.
- Owner accepts the production look; Task 8 remains the separate default-flip gate.

## Progress

Plan authored after the 2026-07-15 Variant C acceptance.

**L1 (typed look profile + validation) DONE (2026-07-17, `db2b685`).** Added the pure-logic
`BuddyLookData` (`domain/DesktopBuddy.Domain/Presentation/`) with finite/ranged validation and
25 failure-path xUnit tests, the `BuddyLookProfile` Godot resource delegating its `Validate`
into `BuddyLookData` (Lambert diffuse / toon specular / metallic-specular `0.08` / roughness
`1.0`, warm key `(1,0.98,0.94)`/`0.75` at pitch `-35` yaw `-30`, cool camera-axis fill
`(0.85,0.90,1.0)`/`0.70`, shadows off with validation rejecting `true`, outline `#183042` grow
`1.5`), the accepted `data/buddy/lab_buddy_look.tres`, and a required `Look` reference on
`BuddyVisualProfile` whose validation delegates into it. Build 0/0, domain 205→230, the
`presentation_3d` `visual_profile_valid` check exercises the delegated look validation.

**L2 (cached soft-toon materials) DONE (2026-07-17, `7348b0f`).** Added `BuddyLookMaterialLibrary`
(no node ownership): soft-toon `StandardMaterial3D` cached per part/connector albedo and one
shared unshaded ink outline material (front-face cull, grow `1.5`). The presenter builds the
library once in `Initialize` and reads cached references on the render path; its previous
unshaded part/connector materials are replaced with the cached lit materials.

**L3 (transparent-safe two-light rig) DONE (2026-07-17, `9a4a231`).** Added `BuddyLookLightingRig`:
exactly one warm key and one cool camera-axis fill `DirectionalLight3D`, shadows disabled,
`PhysicsInterpolationMode = Off`, no `WorldEnvironment`, all from the injected look profile;
startup fails loudly on a missing/invalid profile. Composed in `buddy_lab.tscn` and
`sandbox.tscn` from the same `lab_buddy_look.tres`; `BuddyLab`/`SandboxRoot` validate and
initialize it. `boot_smoke` (which instantiates `sandbox.tscn`) confirms the sandbox wiring.

**L4 (outlines + camera-space depth lanes) DONE (2026-07-17, `93b3b8a`).** Built one cached
inverted-hull outline shell per buddy part (head, torso, both hands, both feet) as a socket
child sharing the same mesh Resource and the shared front-face-culled grow-`1.5` ink material;
connectors and the face get no shell. Refactored final transform application to the
yaw-before-lane contract: mapped 2D pose (Z=0) → BodyYaw about the torso pivot with no lane →
`DepthOffset` added as a global camera-axis Z → identical transform to mesh and its shell.
Identity yaw reproduces the current projection exactly (`m3_presentation` still asserts head
socket global Z == `DepthOffset`). Added a development-only `SetDevelopmentYawDegrees` drive;
BodyYaw stays identity in normal composition.

**L5 (automated coverage + regression reruns) DONE (2026-07-17).** Added the `presentation_look`
scenario (registered in `ScenarioCatalog`, documented in `TEST_PLAN.md`) with all seven checks
green: `look_profile_valid`, `soft_toon_material_contract`, `transparent_safe_light_contract`,
`outline_contract`, `camera_space_depth_lane` (identity `0.0000`, global-Z lane `0.0000`, screen-X
`0.0000 px` at `±30°`), `look_toggle_physics_invariant`, and `look_idle_soak` through the
21,600-tick soak. Full M3.5 Task 7 matrix rerun under `--presentation=mii3d`
(`presentation_3d`, `room_resize_zoom`, `repeat_envelope`, `m3_presentation`,
`tool_feel_reactions`, `idle_soak_ci`; journeys `lab_spawn_settle`, `m3_glove_strike`) all pass,
and the legacy-default baseline of the same matrix is unchanged. `tools\quick_validate.bat`
passes (9/9). Build 0/0, domain 230/230. **L6 (interactive owner acceptance) and Task 8
(default flip) remain owner-gated and out of scope here.**
