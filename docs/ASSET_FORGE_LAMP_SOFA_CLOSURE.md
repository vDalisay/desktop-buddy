# Asset Forge — Lamp/Sofa v0.1 baseline closure

Status: **accepted and merged to `main` as Asset Forge v0.1**.  
Merge commit: `618df8f41379abafd4e39f97112c99eb04cdec5e`.

This document is retained as the historical acceptance record for the first Environment presets and
cross-category maintenance/thumbnail work. `ASSET_FORGE_V1_CLOSURE.md` is authoritative for the
subsequent v1 implementation and its versioned Lamp/Sofa smoothing contracts.

## Accepted v0.1 implementation

### Lamp

- `lamp@2` uses literal 1024×1024 authoring-template placement.
- The template floor centre is the generated local origin.
- Moving/scaling clean source art inside the fixed template changes final room placement literally.
- `lamp@1` remains reproducible through its original visible-bounds auto-fit path.
- Lamp light metadata stays presentation-only.
- `lamp@2` bakes the emitter to generated local coordinates at export so runtime light placement does
  not depend on visible bounds.
- The Asset Forge preview emitter can be edited numerically or dragged in the frontal preview.
- Runtime verification covers generated mesh, emitter, local light, per-instance purchase, cancel,
  stable-ID restart payload and absence of physics/collision nodes.

`lamp@2` is intentionally frozen as this accepted v0.1 mesh contract. v1 adds the separately versioned
`lamp@3` preset for Inflated Solid defaults and full-resolution rim smoothing.

### Sofa

- `sofa@1` is the accepted second Environment prototype.
- It uses a fixed floor/seat/back authoring template and literal template-space placement.
- Geometry is intentionally a deterministic front-derived stylized 2.5D volume; side-view/AI
  reconstruction remains outside the v1 contract.
- Sofa export reuses the trusted generated Environment definition/catalogue seam.
- No Lamp light metadata is emitted for Sofa.
- Runtime verification covers two paid instances, restart-by-ID, one trusted authored mesh per
  presenter, no physics/light nodes, and sharing of imported mesh/texture resources between copies.

`sofa@1` is likewise frozen as the accepted v0.1 mesh contract. v1 adds `sofa@2` for the shared
full-resolution Environment silhouette polisher.

### Shared thumbnails and maintenance

- Environment thumbnails are deterministic item-only 256×256 RGBA images with alpha-bounds crop and
  breathing room.
- Arbitrary crop dimensions use a deterministic bilinear resize rather than the integer-only runtime
  texture downsampler.
- Buddy and Environment exports share the canonical thumbnail cache key:
  `geometry hash + albedo hash + thumbnail recipe hash`.
- Existing canonical Buddy thumbnails are preserved during headless regeneration; missing/corrupt or
  wrong-sized thumbnails are repaired deterministically.
- Verification rejects non-canonical generated thumbnails.
- Combined verify/regenerate covers Buddy and Environment authoring trees.
- Environment regeneration is tested after source mutation and after generated-file corruption.
- Duplicate stable-ID and malformed authoring-recipe diagnostics are covered by the repository
  authoring identity audit.
- Export safety tests cover invalid thumbnails, traversal-shaped IDs and aggregate preservation.

### Diagnostics / compatibility

- Category generation diagnostics record elapsed milliseconds, vertex/triangle counts and generated
  GLB/albedo byte sizes.
- Thumbnail cache hit/miss counters are reported in developer logs.
- Glasses v1/v2 are byte-compared through the category dispatch against the accepted Glasses
  generator to guard compatibility.
- Opened recipe preset versions and hidden metadata are preserved by the modern category UI instead
  of being rebuilt from today's defaults.

## v0.1 automated gate

The accepted PR workflow covered:

1. game solution build;
2. domain unit tests;
3. deterministic Asset Forge Core tests;
4. standalone Asset Forge build;
5. developer-only source/export exclusion checks;
6. generated CI fixture export and `--verify-all` re-derivation;
7. Godot import and boot smoke;
8. generated Glasses, Torso/Foot, paint UV and connector scenarios;
9. Buddy Studio owned-preview activation scenario;
10. generated Lamp runtime scenario;
11. generated Sofa runtime scenario;
12. standalone Asset Forge import and headless boot.

The v0.1 branch/PR was accepted by the owner and merged. The remaining v1 visual/input acceptance is
tracked only in `ASSET_FORGE_V1_CLOSURE.md`.
