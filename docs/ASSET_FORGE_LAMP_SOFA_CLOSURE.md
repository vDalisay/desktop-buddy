# Asset Forge — Lamp/Sofa continuation closure

This branch continues `ASSET_FORGE_IMPLEMENTATION_PLAN.md` from a fresh `main` base. It closes the
first Environment presets and the cross-category maintenance/thumbnail work without changing the
runtime authority model: generated content is still trusted visual/resource data only.

## Implemented

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

### Sofa

- `sofa@1` is enabled as the second Environment prototype.
- It uses a fixed floor/seat/back authoring template and literal template-space placement.
- Geometry is intentionally a deterministic front-derived stylized 2.5D volume; side-view/AI
  reconstruction remains outside the v1 contract.
- Sofa export reuses the trusted generated Environment definition/catalogue seam.
- No Lamp light metadata is emitted for Sofa.
- Runtime verification covers two paid instances, restart-by-ID, one trusted authored mesh per
  presenter, no physics/light nodes, and sharing of imported mesh/texture resources between copies.

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
- Glasses v1/v2 are byte-compared through the new category dispatch against the accepted Glasses
  generator to guard compatibility.
- Opened recipe preset versions and hidden metadata are preserved by the modern category UI instead
  of being rebuilt from today's defaults.

## Automated gates before local verification

The Asset Forge PR workflow targets both `asset-forge` and `main` and runs:

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

## Remaining local-only acceptance

Do not treat these as implementation gaps. They are intentionally visual/input judgements that the
headless gates cannot meaningfully decide:

- Lamp floor scale/contact reads correctly beside the Buddy reference.
- The draggable Lamp emitter feels correctly attached to the frontal preview and its numeric values
  track the intended point.
- Lamp glow and optional room light look sensible in the actual Room Decorator.
- Sofa front-derived depth/rounding looks acceptable for the game's stylized art direction.
- Sofa floor scale/contact and two-copy room composition read naturally.
- Generated catalogue thumbnails have aesthetically acceptable crop/padding in the live UI.

No merge should occur until these local visual/input gates are confirmed.
