# Asset Forge Runtime Mesh Quality Contract

This document is normative for generated Buddy body-part replacements and should be reused by future generated Buddy parts where practical.

## Why this exists

Owner validation of early Torso/Foot exports exposed meshes around 25k-35k vertices and 50k-69k triangles for very small stylized Buddy parts. Those meshes rendered, but they were unnecessarily expensive for Buddy Studio and made generated-mesh paint hit-testing stall badly.

Asset Forge therefore separates **source quality** from **runtime mesh density**:

- source artwork remains a fixed 1024x1024 template-space PNG;
- the procedural runtime mesh is generated at an explicit mesh resolution;
- new Torso/Foot recipes default to **128** runtime mesh resolution;
- **64** is the lighter option for simple silhouettes;
- **256** remains available for deliberately high-detail authoring, but is not the default runtime choice;
- a generated Torso/Foot above **20,000 triangles** must show an authoring warning before export.

The current deterministic grid generator benefits more from generating directly at the intended runtime resolution than from adding a native post-process dependency solely to decimate a mesh that Asset Forge can generate more cheaply in the first place. If future contour/organic generators need general-purpose decimation, prefer an attribute-aware simplifier that preserves UVs/normals and remains deterministic across supported CI platforms.

## Runtime rendering

Generated replacements must:

- keep all 2D physics/collision/joint data unchanged;
- use the shared Buddy generated-asset material and authored lighting level;
- use a Buddy outline shell that cannot self-intersect through pixel-derived sidewalls;
- avoid normal-based outline Grow on replacement sidewalls; use the approved uniformly enlarged back-face shell instead;
- preserve the generated UV layout used by character paint surfaces.

## Paint performance

Painting arbitrary generated silhouettes must never linearly scan the complete mesh for every mouse sample.

The current runtime path therefore:

1. extracts immutable triangle positions and UVs once per imported generated `ArrayMesh`;
2. builds a deterministic local-space BVH once;
3. transforms the paint cursor ray into mesh-local space once per sample;
4. traverses only intersected BVH nodes;
5. performs barycentric UV interpolation only for candidate triangles.

This is required even with the 128 default because it also keeps older authored 256-resolution replacements usable.

## Authoring quality controls

Torso/Foot replacement authors receive independent controls for:

- Depth (visual only; never physics),
- Edge roundness,
- Surface smoothness (`0..3`),
- shape profile (Rounded / Inflated / Soft relief),
- Runtime mesh resolution (Advanced).

`Surface smoothness = 0` preserves the legacy v1 depth-field behavior. `0..1` is the normal range. Values above `1` apply additional deterministic relaxation passes for intentionally soft/plush shapes without moving the authored XY silhouette.

## Buddy Studio inspection

Buddy Studio appearance preview intentionally hides limb connector cylinders while it owns the preview. Those connectors are gameplay presentation helpers and obscure replacement fit during authoring.

Preview navigation must support:

- unrestricted practical zoom in/out (no arbitrary framing clamp),
- mouse-wheel zoom,
- middle-button drag pan,
- Reset View.

Gameplay connector visibility and physics are restored when Studio releases the preview.

## Regression gates

Automated coverage must keep checking:

- new Torso/Foot defaults use 128 runtime mesh resolution;
- representative default replacements remain below 20k triangles;
- 256 remains substantially denser than the runtime default;
- extended smoothing is deterministic;
- legacy zero-smoothing canonical recipe behavior is stable;
- replacement outline effective thickness is stable without material Grow;
- generated paint shells bind to generated meshes;
- generated UV hit mapping works through the accelerated path;
- Studio preview connector hiding restores cleanly;
- trusted Buddy geometry/physics never changes.
