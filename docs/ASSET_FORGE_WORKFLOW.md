# Desktop Buddy Asset Forge — Developer Workflow

Asset Forge is a developer-only Godot executable for turning a standardized 2D source image into deterministic game-ready 3D cosmetic content. Version 1 completes the `Buddy Studio > Glasses` vertical slice.

## Launch

From the repository root on Windows:

```bat
devtools\AssetForge\run_asset_forge.bat
```

The launcher synchronizes the trusted Buddy visual profiles, runs the pure deterministic Core tests, builds the standalone Godot project, and launches Asset Forge. The tool is excluded from normal Desktop Buddy compilation and game/Steam export.

## Source art contract

Glasses v1 expects:

```text
1024 × 1024
PNG
8-bit RGBA
front view
```

A transparent background is preferred. A fully opaque drawing on a **single flat canvas colour** (for example pink glasses on white) is also supported: Asset Forge deterministically samples the outer border, removes that uniform background, and creates the same canonical transparent foreground before shape analysis. If an opaque border is not uniform enough to identify safely, generation fails instead of treating the whole canvas as geometry.

Use **Save Glasses Template…** in Asset Forge to create the 1024×1024 reference guide. The guide contains the Buddy head outline, center/eye lines, eye centers, recommended frame envelope, and temple-root zones.

The guide itself is a reference layer. Do not leave guide artwork mixed into the final frame drawing.

### Rounded glasses template contract

The default **Rounded glasses template** mode is semantic rather than a literal pixel extrusion. The source drawing must contain **two closed lens openings**. Asset Forge then:

1. extracts the foreground from transparency or a uniform opaque canvas;
2. detects the two enclosed lens openings;
3. traces and simplifies each lens contour deterministically;
4. uniformly fits those contours to the trusted Buddy-head glasses envelope while preserving their proportions;
5. sweeps a rounded 3D frame tube around the detected lens shapes;
6. creates the bridge from the nearest inner lens points;
7. creates 3D temple arms from the outer lens points and extends them backward around the head;
8. samples colour from the nearest opaque authored frame pixels rather than from the transparent lens boundary.

The source stroke tells Asset Forge **what shape the glasses are**. The physical 3D frame thickness is controlled separately by **Frame thickness**. This is intentional: a thick brush stroke should not force the shipped glasses mesh to have an equally thick physical cross-section.

If Rounded glasses template cannot find two closed lens openings, it fails with an authoring error. It does **not** silently fall back to an unrelated mesh. **Flat silhouette extrusion** remains available explicitly under Advanced for unusual artwork that is intentionally meant to be extruded as drawn.

## Authoring flow

1. Start `devtools\AssetForge\run_asset_forge.bat`.
2. Optionally save the Glasses template and draw the frame against it in the art program of choice.
3. Draw a closed left and right lens/frame shape plus bridge. Export a 1024×1024 RGBA PNG; transparency is preferred but a flat opaque canvas is accepted.
4. Choose **Open Image…**.
5. Set display name, stable feature ID (`glasses.*`), ownership content ID (`cosmetic.glasses.*`), price, and sort order.
6. Tune **Frame thickness**, frame depth, roundness, and temple dimensions. Advanced exposes alpha threshold, source-mask bias, geometry/runtime resolution, semantic rounded-template versus flat extrusion, and symmetry.
7. Choose **Generate**.
8. Check the status line. Normal glasses should report `rounded glasses template fit`, two lens holes, and either `source alpha` or `auto background rgb(...)`. A slab-like input is rejected.
9. Inspect the actual 3D result against the trusted Buddy head. Left-drag orbits, middle-drag pans, mouse wheel zooms, and **Reset View** restores the camera.
10. Save the editable recipe when desired.
11. Choose **Export to Game**. Asset Forge writes the source/recipe, canonical GLB, albedo, thumbnail, generated cosmetic definition, generated sale definition, and aggregate generated catalogues transactionally.
12. Export automatically runs the pure verifier for the new item. Do not commit generated content while verification reports drift.

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
devtools\AssetForge\verify_asset_forge.bat
```

Regenerate every saved authored asset, preserving an existing thumbnail when available, then verify the repository:

```bat
devtools\AssetForge\regenerate_asset_forge.bat
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

Automated CI covers build/import, deterministic generation, transactional export, generated catalogue loading, Buddy Studio purchase/equip, persistence, GLB rendering, and the visual-only/no-physics boundary. Core regression fixtures also include fully opaque white-canvas pink glasses so the original full-canvas/slab failure cannot return unnoticed.

Create one real pair of glasses from your own 2D art and confirm:

- front lens/frame shapes match the source art rather than the 1024 canvas bounds;
- the two lens openings remain open;
- frame cross-sections read as rounded 3D material rather than flat cards;
- **Frame thickness** changes physical frame thickness independently of source brush width;
- temples look plausible from roughly +30° and -30° views;
- authored colours remain recognizable under the Buddy lighting;
- the scale/position on the reference head looks correct;
- the generated thumbnail is usable;
- after **Export to Game**, the item appears under Buddy Studio > Glasses;
- previewing an unowned item is free, buying charges once, Equip applies it, and Save/restart preserves it.

If any of the visual bullets fail, keep the branch unmerged and adjust the Glasses preset/generator rather than hand-editing the generated GLB.
