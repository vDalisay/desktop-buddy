# Desktop Buddy Asset Forge — Developer Workflow

Asset Forge is a developer-only Godot executable for turning standardized 2D source art into deterministic game-ready 3D content. The current vertical slice is `Buddy Studio > Glasses`.

## Launch

From the repository root on Windows:

```bat
tools\run_asset_forge.bat
```

The tool remains excluded from the normal Desktop Buddy assembly and game/Steam export.

## Glasses source contract

New glasses use the **glasses@2** preset:

```text
1024 × 1024
PNG
8-bit RGBA
front view
```

A transparent background is preferred. A fully opaque drawing on one flat canvas colour is also supported: Asset Forge samples and removes the uniform border colour deterministically.

### Coloring-page placement template

Choose **Save Glasses Template…** to create the canonical 1024×1024 Buddy-head guide. It contains a low-opacity render of the Buddy head plus center/eye guides. Treat it like a coloring-page reference layer:

1. put the template on a low/background layer in your drawing program;
2. draw the glasses on a separate layer over the head;
3. use the head size and eye line to choose the exact scale and placement you want;
4. hide/delete the Buddy template layer before exporting;
5. export only the clean glasses artwork at the original 1024×1024 canvas size.

**Do not crop, resize, recenter, or move the clean export after drawing it.** In glasses@2, source pixels map directly to Buddy-head-relative placement. Moving the drawing 50 pixels in the source therefore moves the resulting cosmetic by the corresponding amount on the head.

The guide is never included in generated game content.

## Rounded glasses@2 behavior

Rounded mode is a glasses-specific semantic generator rather than literal pixel extrusion. The source must contain two closed lens openings. Asset Forge then:

1. extracts foreground from alpha or a uniform opaque canvas;
2. detects and traces the two lens openings;
3. preserves their source-space placement and proportions using the 1024 Buddy-head template coordinate system;
4. sweeps a rounded 3D frame around those authored lens shapes;
5. traces the **authored bridge/nose** between the lenses from the drawing instead of replacing it with a fixed bridge;
6. creates trusted 3D temple arms from the outside frame roots and extends them backward around the head;
7. samples the authored colours into the generated opaque 3D material.

This means the bridge is user-customizable. It may be straight, raised, lowered, sloped, curved, thicker in the source, or omitted; Asset Forge follows the authored bridge centerline when it can trace a continuous connection between the two frames. The physical 3D cross-section is still controlled by **Frame thickness** so brush width does not accidentally dictate real mesh thickness.

Temples remain generated in the current preset because a single front-view image cannot reliably describe their side-view path. Their thickness, length, and drop remain editable preset controls.

If rounded mode cannot identify two closed lens openings it fails with an authoring error. It does not silently return unrelated geometry. **Flat silhouette extrusion** remains an explicit Advanced fallback for artwork intentionally meant to be extruded as drawn.

### Legacy glasses@1 recipes

Existing saved recipes with `presetVersion: 1` remain valid and retain the older auto-fit/fixed-bridge behavior so committed assets remain deterministic. New recipes default to `presetVersion: 2`. To use coloring-page placement and the authored bridge, create a new recipe or intentionally migrate an old recipe to v2 and regenerate it.

## Lighting and colour

Generated semantic cosmetics use the same opaque generated-asset material in Asset Forge and in the shipped game. The authored PNG remains the albedo, normal Buddy diffuse/specular lighting supplies the 3D form, and a restrained texture-coloured light floor prevents narrow rounded surfaces from collapsing toward black. The current generated-cosmetic floor is intentionally stronger than the original proof of concept while remaining lit rather than unshaded.

## Authoring flow

1. Run `tools\run_asset_forge.bat`.
2. Save the Glasses template if you need a placement reference.
3. Draw the complete front frame, including the bridge you want, over the template on a separate layer.
4. Hide the Buddy/template layer and export the clean 1024×1024 PNG without changing canvas coordinates.
5. Choose **Open Image…**.
6. Set display name, stable feature ID (`glasses.*`), ownership content ID (`cosmetic.glasses.*`), price, and sort order.
7. Tune frame thickness/depth/roundness and temple thickness/length/drop.
8. Choose **Generate** and inspect front and angled views against the trusted Buddy head.
9. Save the editable recipe when desired.
10. Choose **Export to Game**. Export writes source/recipe, canonical GLB, albedo, thumbnail, generated definitions, and generated catalogues transactionally, then verifies the new item.

Authoring source is stored under:

```text
authoring/asset-forge/glasses/<slug>/
  source.png
  recipe.json
```

Generated trusted content is stored under:

```text
assets/generated/cosmetics/<feature-id>/
data/cosmetics/generated/
data/catalogue/generated/
data/catalogue/generated_cosmetics.tres
```

## Verification / regeneration

Verify all saved authored assets without Godot generation state:

```bat
tools\verify_asset_forge.bat
```

Regenerate all authored assets and verify them:

```bat
tools\regenerate_asset_forge.bat
```

The standalone UI also exposes **Regenerate**, **Regenerate All**, **Verify**, and **Verify All**.

Canonical determinism covers generator/preset recipe, source bytes, geometry/normals/UVs, Core-written GLB bytes, and deterministic runtime albedo. Preview camera state and GPU thumbnail pixels are not part of the canonical asset hash.

## Local visual acceptance

Before merging a newly authored glasses asset, verify:

- lens shapes match the clean source;
- source placement/scale matches where you drew it on the Buddy template;
- the authored bridge/nose shape is represented rather than replaced;
- both lens openings remain open;
- frame surfaces read as the authored colour with visible 3D shading, not near-black;
- temples look plausible from roughly ±30°;
- export appears in Buddy Studio > Glasses and can be purchased/equipped/saved;
- no generated cosmetic changes 2D physics authority.
