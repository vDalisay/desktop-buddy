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

Rounded mode is a glasses-specific semantic generator rather than literal pixel extrusion. The source must contain at least two closed frame/lens openings. Asset Forge then:

1. extracts foreground from alpha or a uniform opaque canvas;
2. detects all enclosed interior holes and uses the **two largest** as the left/right lens-frame guides;
3. preserves those frames' source-space placement and proportions using the 1024 Buddy-head template coordinate system;
4. sweeps a rounded 3D frame around those authored lens shapes;
5. preserves complex authored bridge/nose artwork between the lenses as its **full filled silhouette**, including extra enclosed holes/cut-outs;
6. falls back to the older authored center-line tube only for very thin/open bridges that do not form a useful silhouette;
7. creates trusted 3D temple arms from the outside frame roots and extends them backward around the head;
8. samples the authored colours into the generated opaque 3D material.

The bridge is therefore user-customizable. It can be straight, raised, lowered, sloped, curved, thick, hollow, arrow-shaped, or contain additional cut-outs. Extra enclosed bridge holes are valid and are reported as **interior holes**, not additional lenses. For example, two hollow lens frames plus two hollow inward-pointing bridge arrows legitimately produce four interior holes.

For complex bridge art, the 2D source defines the bridge silhouette/thickness. The lens-frame tubes still use **Frame thickness** for their rounded physical cross-section. Temples remain generated in the current preset because a single front-view image cannot reliably describe their side-view path; their thickness, length, and drop remain editable preset controls.

If rounded mode cannot identify at least two closed lens/frame openings it fails with an authoring error. It does not silently return unrelated geometry. **Flat silhouette extrusion** remains an explicit Advanced fallback for artwork intentionally meant to be extruded as drawn.

### Legacy glasses@1 recipes

Existing saved recipes with `presetVersion: 1` remain valid and retain the older auto-fit/fixed-bridge behavior so committed assets remain deterministic. New recipes default to `presetVersion: 2`. To use coloring-page placement and the authored bridge, create a new recipe or intentionally migrate an old recipe to v2 and regenerate it.

## Lighting and colour

Generated semantic cosmetics use the same opaque generated-asset material in Asset Forge and in the shipped game. The authored PNG remains the albedo and normal Buddy diffuse/specular lighting supplies the 3D form.

Asset Forge exposes **Lighting level** in the glasses controls:

- range: `0.00`–`1.00`;
- default: **`0.36`**, which is the currently approved brightness;
- `0.00` means no authored colour floor beyond scene lighting;
- higher values add more texture-coloured emission while the asset remains otherwise normally lit.

The control updates the preview live. After changing it, click **Generate** again before Export: the value is part of the canonical recipe and is persisted into the generated cosmetic resource, so the equipped runtime cosmetic uses the same lighting level as the preview. Existing/default generated assets without explicit authored lighting continue to resolve to `0.36`.

## Authoring flow

1. Run `tools\run_asset_forge.bat`.
2. Save the Glasses template if you need a placement reference.
3. Draw the complete front frame, including the full bridge shape you want, over the template on a separate layer.
4. Hide the Buddy/template layer and export the clean 1024×1024 PNG without changing canvas coordinates.
5. Choose **Open Image…**.
6. Set display name, stable feature ID (`glasses.*`), ownership content ID (`cosmetic.glasses.*`), price, and sort order.
7. Tune frame thickness/depth/roundness, lighting level, and temple thickness/length/drop.
8. Choose **Generate** and inspect front and angled views against the trusted Buddy head.
9. Save the editable recipe when desired.
10. Choose **Export to Game**. Export writes source/recipe, canonical GLB, albedo, thumbnail, generated definitions, and generated catalogues, then persists the authored lighting metadata and verifies the new item.

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

The standalone UI also exposes **Regenerate**, **Regenerate All**, **Verify**, and **Verify All**. Regeneration reapplies a recipe's authored lighting metadata after rebuilding the deterministic geometry/albedo package.

Canonical determinism covers generator/preset recipe (including Lighting level), source bytes, geometry/normals/UVs, Core-written GLB bytes, and deterministic runtime albedo. Preview camera state and GPU thumbnail pixels are not part of the canonical asset hash.

## Local visual acceptance

Before merging a newly authored glasses asset, verify:

- lens shapes match the clean source;
- source placement/scale matches where you drew it on the Buddy template;
- complex authored bridge/nose shapes are fully represented rather than skeletonized or clipped;
- all intended interior cut-outs remain open;
- frame surfaces read as the authored colour with visible 3D shading;
- Lighting level previews the expected brightness and the exported/equipped item matches it;
- temples look plausible from roughly ±30°;
- export appears in Buddy Studio > Glasses and can be purchased/equipped/saved;
- no generated cosmetic changes 2D physics authority.
