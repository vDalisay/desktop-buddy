# Desktop Buddy Asset Forge — Developer Workflow

Asset Forge is a developer-only Godot executable for turning a standardized 2D source image into deterministic game-ready 3D cosmetic content. Version 1 completes the `Buddy Studio > Glasses` vertical slice.

## Launch

From the repository root on Windows:

```bat
tools\run_asset_forge.bat
```

The launcher synchronizes the trusted Buddy visual profiles, runs the pure deterministic Core tests, builds the standalone Godot project, and launches Asset Forge. The tool is excluded from normal Desktop Buddy compilation and game/Steam export.

## Source art contract

Glasses v1 expects:

```text
1024 × 1024
PNG
8-bit RGBA
transparent background
front view
```

Use **Save Glasses Template…** in Asset Forge to create the 1024×1024 reference guide. The guide contains the Buddy head outline, center/eye lines, eye centers, recommended frame envelope, and temple-root zones.

The template is a **reference layer only**. Hide/delete its guides before exporting the final `source.png`; guide pixels must not remain in the source alpha mask.

## Authoring flow

1. Start `tools\run_asset_forge.bat`.
2. Optionally save the Glasses template and draw the frame against it in the art program of choice.
3. Export the art-only image as a 1024×1024 transparent RGBA PNG.
4. Choose **Open Image…**.
5. Set display name, stable feature ID (`glasses.*`), ownership content ID (`cosmetic.glasses.*`), price, and sort order.
6. Tune frame depth, roundness, thickness bias, and temple dimensions. Advanced exposes alpha threshold, geometry/runtime resolution, flat/rounded extrusion, and symmetry.
7. Choose **Generate**.
8. Inspect the actual 3D result against the trusted Buddy head. Left-drag orbits, middle-drag pans, mouse wheel zooms, and **Reset View** restores the camera.
9. Save the editable recipe when desired.
10. Choose **Export to Game**. Asset Forge writes the source/recipe, canonical GLB, albedo, thumbnail, generated cosmetic definition, generated sale definition, and aggregate generated catalogues transactionally.
11. Export automatically runs the pure verifier for the new item. Do not commit generated content while verification reports drift.

Authoring source is stored under:

```text
authoring/asset-forge/glasses/<slug>/
  source.png
  recipe.json
```

Generated trusted game content is stored under:

```text
assets/generated/cosmetics/<feature-id>/
data/cosmetics/generated/
data/catalogue/generated/
data/catalogue/generated_cosmetics.tres
```

## Determinism / regeneration commands

Asset Forge Core owns the GLB writer, so committed generated content can be re-derived without starting Godot.

Verify every saved authored asset:

```bat
tools\verify_asset_forge.bat
```

Regenerate every saved authored asset, preserving an existing thumbnail when available, then verify the repository:

```bat
tools\regenerate_asset_forge.bat
```

The standalone UI also exposes **Regenerate**, **Regenerate All**, **Verify**, and **Verify All** for saved authoring content.

`Verify All` re-derives the canonical recipe, input-derived geometry, GLB bytes, and albedo bytes and checks the committed trusted metadata/catalogue references. Thumbnail pixels are only validated as a PNG because GPU thumbnail rasterization is intentionally not part of the canonical determinism hash.

## What is deterministic

The canonical asset hash is derived from:

- generator version;
- source PNG byte hash;
- canonical recipe hash;
- canonical geometry/normal/UV hash;
- Core-written GLB byte hash;
- deterministic runtime albedo PNG hash.

Preview camera state, Buddy-reference visibility, UI layout, and generated thumbnail pixels do not affect canonical output.

## First local visual acceptance gate

Automated CI covers build/import, deterministic generation, transactional export, generated catalogue loading, Buddy Studio purchase/equip, persistence, GLB rendering, and the visual-only/no-physics boundary. The remaining acceptance gate is deliberately visual.

Create one real pair of glasses from your own 2D art and confirm:

- front silhouette matches the source art;
- the two lens openings remain open;
- frame edges read as rounded/bevelled rather than a flat card;
- temples look plausible from roughly +30° and -30° views;
- authored colours remain recognizable under the Buddy lighting;
- the scale/position on the reference head looks correct;
- the generated thumbnail is usable;
- after **Export to Game**, the item appears under Buddy Studio > Glasses;
- previewing an unowned item is free, buying charges once, Equip applies it, and Save/restart preserves it.

If any of the visual bullets fail, keep the branch unmerged and adjust the Glasses preset/generator rather than hand-editing the generated GLB.
