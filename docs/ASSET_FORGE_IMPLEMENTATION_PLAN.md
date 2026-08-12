# Desktop Buddy Asset Forge — Implementation Plan

Status: **Owner-approved implementation plan**  
Target: **Developer-only deterministic asset authoring pipeline**  
Repository: `desktop-buddy`  
Primary target platform: Windows 10/11 x86_64, Godot 4.6.1 .NET / C#

---

## 1. Product goal

Desktop Buddy Asset Forge is a **separate developer executable** that lives in the repository but is not compiled into, packaged with, or exposed by the shipped game.

Its purpose is to let the developer turn a standardized 2D source image into a **deterministic stylized 3D asset** that can be exported into Desktop Buddy as trusted authored content.

The same tool serves two product areas:

1. **Buddy Studio cosmetics / visual replacements**
   - Glasses
   - Hair
   - Headwear
   - Accessories
   - Torso-shape replacements (`Tops`)
   - Foot-shape replacements (`Shoes`)
   - Future Buddy visual categories

2. **Environment Decorator assets**
   - Lamps
   - Sofas
   - Tables
   - Plants
   - Paintings
   - Future room-decoration categories

The core design requirement is **repeatability**:

> The same generator version + source image + recipe must always produce the same canonical geometry, UVs, normals, attachment metadata, and catalogue metadata.

The pipeline is deliberately **not AI image-to-3D generation**. The authoritative generation path is procedural and deterministic.

---

## 2. Locked owner decisions

The following decisions are authoritative for this plan.

### 2.1 Developer-only standalone tool

Asset Forge is a separate executable.

It may live inside the repository, but:

- it is not part of `DesktopBuddy.csproj`;
- it is not included in Steam/game exports;
- players never run it;
- it never executes inside Buddy Studio or Environment Decorator at runtime.

### 2.2 Standard source format

Initial source standard:

```text
1024 × 1024
PNG
RGBA
transparent background
single front-view image
```

Every asset category uses the same source-canvas resolution. Category-specific authoring templates define different safe areas, anchors, guides, and expected proportions.

### 2.3 Presets + Advanced settings

Each category has a preset with sensible defaults and a small number of category-specific controls.

Examples:

- Glasses: frame depth, frame-thickness bias, temple thickness, temple length, roundness.
- Lamp: depth, roundness, emission, light brightness, light range.
- Sofa: depth, inflation/roundness, simplification.

An **Advanced** panel exposes the lower-level deterministic generator values so the developer can manually tune them.

All generation-relevant values must be persisted in the asset recipe. No important output parameter may exist only as transient UI state.

### 2.4 Single-front-view v1

Version 1 uses only the front source image.

The tool does not pretend to semantically infer unseen geometry. Hidden-side geometry is generated using deterministic category rules.

Example:

- Glasses temples are generated automatically from the front-frame geometry and category settings.
- A sofa generated from one front image is a stylized front-derived 2.5D/rounded result, not a reconstructed real sofa.

Future versions may add front + side source profiles for categories that benefit from them.

### 2.5 Actual 3D visual output

Generated Buddy and Environment assets should be actual 3D geometry rather than pre-rendered flat sprites.

The output should have enough depth to remain visually meaningful as the Buddy or preview camera turns.

### 2.6 Source colours matter

Colours in the input PNG become part of the default generated asset.

If the developer draws pink glasses, the exported result must remain recognizably that authored shade of pink. Shared Desktop Buddy lighting/material treatment may affect final rendered brightness and shading, but Asset Forge must not arbitrarily reinterpret the source colour.

### 2.7 One material region for v1

Version 1 keeps material-channel semantics minimal.

The source image may contain many painted colours, but the asset has one primary authored texture/material region. Named multi-channel recolouring such as `Frame`, `Lens`, `Accent`, `Sole`, etc. is deferred.

The generated mesh must nevertheless receive stable UVs so future player paintability can be added without discarding the generated geometry format.

### 2.8 Holes and disconnected components

The generator must support:

- internal holes;
- multiple disconnected visible islands/components.

This is required immediately for glasses and is a general-purpose geometry feature.

### 2.9 Symmetry

Symmetry is a shared pipeline feature.

Supported modes should include at least:

```text
Off
Mirror Left -> Right
Mirror Right -> Left
Average Both Sides
```

Category presets may enable one by default.

### 2.10 Automatic attachment / placement metadata

Asset Forge should automatically choose sane attachment/placement defaults from the selected category and allow the developer to adjust them.

Examples:

- Glasses -> Buddy `EyeGroup` anchor.
- Tops -> torso visual replacement.
- Shoes -> paired left/right foot visual replacement.
- Lamp/Sofa/Table/Plant -> floor anchor with bottom-center pivot.
- Painting -> wall anchor with centered pivot.

### 2.11 Generated thumbnails

Asset Forge automatically generates catalogue thumbnails from the final generated asset.

The thumbnail is presentation output. It is not part of the canonical geometry determinism hash because GPU rasterization may vary slightly across hardware/drivers.

### 2.12 Export includes game metadata

The tool authors game-facing metadata such as:

- stable content ID;
- display name;
- category;
- price;
- sort order;
- attachment or placement policy;
- visual source metadata;
- generated mesh/texture/thumbnail paths.

### 2.13 Buddy cosmetics retain Buddy economy semantics

Buddy Studio cosmetics remain permanent unlocks using the existing cosmetic ownership/economy path.

Selecting an unowned cosmetic may preview it for free. Buying it spends credits once and permanently unlocks it through the existing unlocked-content model.

### 2.14 Environment decorations retain Environment economy semantics

Environment decoration definitions are not permanent cosmetic unlocks.

Each placed instance is purchased separately according to the existing Environment Decorator edit-session transaction model.

Asset Forge must not merge the Buddy and Environment economy models.

### 2.15 Environment assets are visual-only in v1

Generated Environment assets are cosmetic/non-physical by default.

They must not automatically add:

- collision;
- rigid bodies;
- mass;
- damage interaction;
- grab behavior;
- arbitrary scripts;
- gameplay logic.

### 2.16 Tops and Shoes are visual replacements

Owner decision, 2026-08-12. This supersedes the overlay interpretation everywhere it appears, including the current shipped renderer and the `trusted torso/foot overlay` wording in `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md`.

```text
Tops
!= clothing mesh layered on top of the torso
= purchasable torso visual shape replacement

Shoes
!= shoe meshes layered on top of default feet
= purchasable foot visual shape replacements
```

A Top is a new torso shape — triangular, pear-shaped, whatever the source art draws — in exactly the sense the default torso is a shape today. It is a whole new mesh, not a decoration in front of one.

Underlying 2D physics remains unchanged.

There are no existing players, so the two shipped items `cosmetic.top.utility_bib` and `cosmetic.shoes.soft_steps` are re-authored under the replacement model. No ownership or save migration is required.

### 2.17 Part replacement and body paint

Owner decision, 2026-08-12.

```text
equip a replacement  -> the default part visual is replaced by the new mesh
the replacement      -> always starts empty, every time it is equipped
```

The default part visual and its painted surface are hidden, not deleted, and return unchanged when the replacement is removed. A replacement is never painted in v1 and paint input over a replaced part does nothing; the analytic frontal brush mapper owns the primitive parts only.

Replacement painting is future work and is governed by the UV contract in Section 9.8.

### 2.18 First proof-of-concept category

The first complete vertical slice is **Glasses**.

Do not expand into several categories before one generated pair of glasses works end-to-end from source PNG to purchasable/equippable Buddy Studio content.

---

## 3. Target developer workflow

The intended workflow is:

```text
Desktop Buddy Asset Forge.exe
        |
        v
1. New Asset
        |
        +-- Buddy Studio
        |     +-- Glasses
        |     +-- Hair
        |     +-- Headwear
        |     +-- Torso Shape / Tops
        |     +-- Foot Shape / Shoes
        |     +-- ...
        |
        +-- Environment
              +-- Lamp
              +-- Sofa
              +-- Table
              +-- Plant
              +-- Painting
              +-- ...
        |
        v
2. Import 1024x1024 transparent PNG
        |
        v
3. Select category preset
        |
        +-- Simple controls
        |
        +-- Advanced deterministic settings
        |
        v
4. Set game metadata
        |
        +-- Name
        +-- Stable ID
        +-- Category
        +-- Price
        +-- Sort order
        +-- attachment/placement metadata
        |
        v
5. Generate
        |
        v
6. Interactive 3D preview
        |
        +-- orbit
        +-- pan
        +-- zoom
        +-- reset camera
        +-- Buddy reference where relevant
        +-- room/floor reference where relevant
        |
        v
7. Tweak settings -> Generate again
        |
        v
8. Save source recipe
        |
        v
9. Export to Game
        |
        +-- generated mesh
        +-- texture
        +-- thumbnail
        +-- trusted definition
        +-- catalogue/economy registration
        |
        v
10. Normal Desktop Buddy build/import
        |
        v
Generated item appears in the selected game category
```

`Export to Game` means exporting into trusted repository source-content locations. Asset Forge does **not** patch an already-compiled game executable.

---

## 4. Repository architecture

Recommended structure:

```text
desktop-buddy/
|
+-- src/
+-- domain/
+-- assets/
+-- data/
+-- docs/
|
+-- authoring/
|   +-- .gdignore
|   +-- asset-forge/
|       +-- glasses/
|       +-- torso/
|       +-- feet/
|       +-- lamps/
|       +-- sofas/
|       +-- ...
|
+-- src/
|   +-- Buddy/
|       +-- Presentation3D/   (shared trusted rig — see 4.2.1)
|
+-- devtools/
    +-- .gdignore
    |
    +-- AssetForge.Core/
    |   +-- DesktopBuddy.AssetForge.Core.csproj
    |   +-- Geometry/
    |   +-- Images/
    |   +-- Recipes/
    |   +-- Presets/
    |   +-- Validation/
    |   +-- Export/
    |
    +-- AssetForge.Core.Tests/
    |
    +-- AssetForge/
        +-- project.godot
        +-- DesktopBuddy.AssetForge.csproj
        +-- scenes/
        +-- src/
        +-- export_presets.cfg
```

### 4.1 Core library

`AssetForge.Core` is a deterministic .NET library containing:

- source-image validation;
- mask/contour extraction;
- geometry generation;
- recipe model + canonical serialization;
- hashing;
- presets;
- export validation;
- generated-catalogue authoring helpers.

It must not depend on the Asset Forge UI.

Where practical, deterministic geometry logic should also avoid depending on live Godot scene state so it can be tested independently.

### 4.2 UI/preview application

`AssetForge` is a separate Godot 4.6.1 .NET app responsible for:

- developer UI;
- image preview;
- interactive 3D preview;
- Buddy/head reference scenes;
- Environment floor/wall reference scenes;
- gizmos;
- thumbnail rendering;
- invoking the deterministic core;
- invoking validated export.

### 4.2.1 Sharing the trusted Buddy rig across two Godot projects

Asset Forge is its own Godot project, so its `res://` root is `devtools/AssetForge/`. Godot resources do not cross project roots, and the Section 14.5 preview gate requires the *real* Buddy head, not a lookalike.

The rig is buildable without the game's scene tree: `BuddyVisualRigView` constructs every part mesh, outline, paint layer, connector, and material in code from `BuddyVisualProfile` and `BuddyLookMaterialLibrary`.

Therefore:

```text
extract the trusted visual rig + look material library
    into a shared Godot-SDK class library
        referenced by DesktopBuddy and by AssetForge

copy the trusted profile resources
    data/buddy/*.tres
        into the Asset Forge project as a build step
```

Rules:

- the shared library is the single definition of the rig; Asset Forge never forks a second copy of the Buddy look;
- the copy step is generated output and stays out of source control;
- Asset Forge previews the reference Buddy but never constructs a `BuddyRoot`, physics authority, or live save state.

### 4.3 Game project build isolation

`DesktopBuddy.csproj` currently uses the Godot SDK broad C# source glob and explicitly excludes only existing standalone projects.

The implementation must add the Asset Forge standalone paths to `DefaultItemExcludes`, otherwise C# files under `devtools/` could accidentally enter the game assembly.

Four boundaries have to move together:

```text
DesktopBuddy.csproj    DefaultItemExcludes gains devtools/**/* and authoring/**/*
export_presets.cfg     exclude_filter gains devtools/*, authoring/*
DesktopBuddy.sln       decide deliberately whether Asset Forge joins the solution
.github/workflows      CI builds the whole solution, so joining it joins CI
```

`.gdignore` under `devtools/` and `authoring/` keeps both trees out of the game's import scan; the export filter is belt-and-braces for non-resource files.

Required verification:

```text
Desktop Buddy builds without Asset Forge
Asset Forge builds without Desktop Buddy
Desktop Buddy Steam/game export contains no Asset Forge assemblies/scenes/content
Adding Asset Forge does not slow or break the existing CI game/domain/journey jobs
```

---

## 5. Standard authoring templates

All category templates use a 1024x1024 transparent PNG canvas.

Templates should be stored as developer-only reference assets and never appear in shipped game content.

### 5.1 Glasses template

Guides should include:

- Buddy head silhouette reference;
- horizontal eye line;
- left/right eye centers;
- face center line;
- recommended glasses bounds;
- optional temple-root guide regions.

### 5.2 Torso-shape template

Guides should include:

- default torso visual silhouette;
- center line;
- top/bottom recommended envelope;
- connector/reference locations;
- translucent physics envelope.

### 5.3 Foot-shape template

Guides should include:

- one default foot outline;
- center;
- connector/ankle location;
- forward direction;
- translucent physics envelope.

Default workflow: author one foot and mirror/generate the paired counterpart.

### 5.4 Environment template

Guides should include:

- floor line;
- object center;
- safe bounds;
- default pivot location;
- Buddy-scale reference option;
- wall/floor anchor reference according to category.

---

## 6. Source versus runtime resolution

Keep authoring source at 1024x1024 even when runtime textures are smaller.

Suggested initial runtime ranges:

| Category | Authoring | Typical runtime texture |
| --- | ---: | ---: |
| Glasses | 1024x1024 | 256-512 |
| Foot replacement | 1024x1024 | 512 |
| Torso replacement | 1024x1024 | 512 |
| Hair/headwear | 1024x1024 | 512 |
| Lamp | 1024x1024 | 512 |
| Sofa | 1024x1024 | 512-1024 |
| Painting | 1024x1024 | 512-1024 |

The exact runtime texture resolution is preset-controlled and may be changed in Advanced settings.

---

## 7. Asset recipe — developer source of truth

Every authored asset retains both:

1. original source image;
2. editable generation recipe.

Example:

```text
authoring/asset-forge/glasses/pink_round/
+-- source.png
+-- recipe.json
```

A recipe should contain, at minimum:

```text
generatorVersion
presetId
presetVersion
assetId
assetFamily
category
displayName
inputImageHash

commerce:
    price
    free
    sortOrder

geometry:
    shapeMode
    alphaThreshold
    contourTolerance
    smoothingIterations
    thicknessBias
    depth
    bevelWidth
    bevelSegments
    symmetryMode

material:
    runtimeTextureResolution
    sideColorMode
    roughnessProfile

attachment:
    anchor
    secondaryAnchor
    applicationMode
    pivot
    scale

environment:
    anchorKind
    rotationPolicy
    renderBand
    optionalLightProfile

thumbnail:
    yaw
    pitch
    padding
```

The exact schema may use nested strongly typed records/resources, but all output-affecting values must be persisted.

### 7.1 Recipe versioning

Both generator and preset versions must be explicit.

Example:

```text
generatorVersion = 1
preset = glasses@1
```

If `glasses@2` is introduced later, existing `glasses@1` recipes must not silently change behavior.

Migration must be explicit and developer-controlled.

---

## 8. Determinism contract

For:

```text
generator version X
+ source PNG bytes/hash Y
+ canonical recipe Z
```

the canonical generated result must reproduce the same:

- vertex positions;
- indices;
- UVs;
- normals/tangents where authored;
- material-slot structure;
- attachment metadata;
- generated definition metadata;
- canonical hashes.

### 8.1 Rules

1. No random generators in authoritative generation.
2. No timestamps inside canonical generated data.
3. No random GUIDs.
4. Sort disconnected components deterministically.
5. Sort contour loops deterministically.
6. Triangulate in deterministic order.
7. Quantize generated positions/UVs before canonical hashing/serialization.
8. Use fixed iteration counts for smoothing/inflation.
9. Store generator and preset versions.
10. SHA-256 source bytes and canonical recipe.
11. Hash canonical generated geometry/metadata.
12. Camera/preview movement must never mutate generation state.

### 8.2 Developer diagnostics

The tool should display values such as:

```text
Input hash:       ...
Recipe hash:      ...
Geometry hash:    ...
Generator:        1
Preset:           glasses@1

Deterministic output verified
```

### 8.3 Thumbnail exception

Do not use GPU-rendered thumbnail pixels as the canonical determinism gate.

A thumbnail is generated/stored output but slight per-driver raster differences must not invalidate canonical geometry determinism.

---

## 9. Geometry generation pipeline

### 9.1 Stage A — source validation

Validate:

- PNG decode succeeds;
- expected RGBA format;
- 1024x1024 dimensions;
- finite/valid metadata;
- meaningful alpha data;
- source byte hash.

Reject corrupt/unsupported files with actionable errors.

### 9.2 Stage B — alpha mask

Convert alpha into a deterministic binary mask using preset/recipe alpha threshold.

Transparent pixels are outside geometry. Visible pixels are inside geometry.

### 9.3 Stage C — component and contour extraction

Extract:

- connected components;
- outer contours;
- inner contours/holes;
- deterministic component order;
- deterministic contour orientation/order.

Holes must remain actual holes in generated front/back topology.

### 9.4 Stage D — contour simplification

Apply bounded deterministic simplification before bevel/inflation.

Goal:

- preserve the visible silhouette;
- eliminate pixel-noise topology;
- keep meshes simple enough for repeated real-time use.

### 9.5 Stage E — symmetry

Apply category/default symmetry according to recipe before final mesh generation.

### 9.6 Stage F — 3D surface generation

Support several shared deterministic generation modes.

#### Flat Extrusion

Best for:

- paintings;
- flat accessories;
- some glasses.

#### Rounded Extrusion

Best for:

- glasses;
- tables;
- furniture;
- mechanical/decorative assets.

#### Inflated Solid

Generate increased depth toward silhouette interiors to create a soft rounded volume.

Best for:

- torso replacements;
- foot replacements;
- soft hair chunks;
- soft furniture;
- plants.

#### Relief

Front detail has depth while the back remains comparatively simple.

Best for:

- some wall props;
- framed art;
- decorative assets.

Category presets choose the default mode. Advanced may override it where valid.

### 9.7 Stage G — bevel / smoothing / normals

Shared advanced parameters should include:

```text
Depth
Bevel width
Bevel segments
Contour smoothing
Contour simplification
Surface smoothing
Normal smoothing
```

Simple presets should expose only the subset useful to the current category.

### 9.8 Stage H — UV generation

The mesh must get stable UVs from the first implementation.

Initial layout contract:

```text
front surface -> source image UVs
side surface  -> deterministic edge-bleed mapping
back surface  -> deterministic generated mapping/region
```

Persist:

```text
PaintUvLayoutVersion = 1
```

Player painting of generated replacements is deferred, but the UV contract must not block that future work.

---

## 10. Source colour and material handling

The input image remains the default albedo source.

### 10.1 Front face

Front geometry samples the source image directly through generated UVs.

### 10.2 Side faces

Since the front PNG contains no explicit side texture, use a deterministic **edge bleed** rule by default.

Nearest relevant boundary colour propagates through the side wall.

Example:

```text
pink frame -> pink side walls
```

rather than grey autogenerated sides.

### 10.3 Runtime look

Generated Buddy cosmetics must use the same shared Desktop Buddy soft-toon lighting/material language as the existing Buddy 3D renderer.

Do not invent a second independent shading style for generated content.

The runtime material library should gain an appropriate textured-lit material seam, conceptually:

```text
CreateLitTexturedMaterial(Texture2D albedo, Color modulation)
```

rather than using unshaded quads/sprites for final Buddy-generated assets.

### 10.4 Environment look

Generated environment assets should use a consistent environment-compatible lit material path that visually belongs beside the Buddy and current 3D room presentation.

Category-specific lighting metadata may add emission/local lights, but the base albedo pipeline remains consistent.

---

## 11. Generated geometry container

Baked mesh format: **GLB**.

Benefits:

- Godot-native import support;
- mesh + UV + normals stay together in one inspectable file;
- suitable for inspection and source control;
- no Blender dependency required in the authoritative pipeline.

### 11.1 The Core writes the GLB bytes

`AssetForge.Core` serializes the GLB itself. It does not call Godot's `GltfDocument`.

Reason: Section 33 AF-15 requires `Verify All` to re-derive committed geometry from the command line without launching the UI. If baking needs the engine, determinism stops being testable in the pure test project and CI has to boot Godot to check a hash.

```text
canonical mesh -> Core GLB writer -> bytes -> SHA-256
```

The writer is deterministic output only: fixed accessor order, fixed buffer layout, no generator timestamp, no random node names.

### 11.2 Geometry only

The exported GLB carries positions, indices, UVs, and normals.

It does not carry the authored look. Runtime materials come from the shared soft-toon library described in Section 10.3, applied over the separately exported `albedo.png`, so a generated asset can never smuggle a second shading style into the game through an embedded glTF material.

### 11.3 Mesh, not scene

Godot imports a `.glb` as a **scene**, not as a `Mesh`. The generated-asset loader must resolve the single canonical mesh deterministically — through import configuration or an explicit extraction step chosen once in AF-4 and applied to every generated asset thereafter.

A generated package containing more than one mesh node is an export validation failure, not a thing the loader guesses about.

The preview camera, reference Buddy, editor gizmos, and floor/wall guides must never be included in the exported GLB.

---

## 12. Asset Forge UI

Recommended main layout:

```text
+---------------------------------------------------------------+
| File | Presets | View                                         |
+---------------+----------------------------+------------------+
| SOURCE        |                            | SETTINGS         |
|               |                            |                  |
| PNG preview   |        3D PREVIEW          | Category         |
|               |                            | Simple controls  |
| Metadata      |                            |                  |
| Name          |                            | > Advanced       |
| ID            |                            |                  |
| Category      |                            | [ Generate ]     |
| Price         |                            |                  |
+---------------+----------------------------+------------------+
| components | holes | tris | hashes                 [Export]   |
+---------------------------------------------------------------+
```

### 12.1 Preview controls

```text
Left drag       orbit
Middle drag     pan
Mouse wheel     zoom
Double click    reset camera
```

### 12.2 Buddy preview options

For Buddy categories:

```text
Show Buddy reference
Show attachment guides
Show physics envelope
Front view
30 deg left
30 deg right
```

### 12.3 Environment preview options

```text
Show floor
Show wall
Show Buddy scale reference
Show placement footprint
Show pivot
```

Preview-camera state is developer convenience state and must not modify generated geometry.

---

## 13. Preset architecture

Preset behavior should be data/typed-definition driven, not a large UI switch statement.

Conceptual type:

```text
PresetDefinition
+-- id + version
+-- asset family/category
+-- geometry mode
+-- default settings
+-- allowed setting ranges
+-- visible Simple controls
+-- attachment/placement policy
+-- category-specific generation adapter
```

Initial presets:

```text
Buddy
+-- Glasses
+-- Torso Shape
+-- Foot Shape

Environment
+-- Lamp
+-- Sofa
+-- Table
+-- Plant
+-- Painting
```

Later presets may add Hair, Headwear, Face shape, Ear shape, additional accessories, wall decor, etc.

---

## 14. Glasses — first complete vertical slice

Glasses are the first required proof of concept.

### 14.1 Input

```text
1024x1024 transparent PNG
```

### 14.2 Required automatic behavior

```text
alpha -> frame silhouette
internal lens holes retained
symmetry available/defaulted
front frame geometry generated
frame edges bevelled/rounded
side depth generated
temples auto-generated
source colours textured onto front
edge colours propagated to sides
EyeGroup attachment assigned
thumbnail rendered
```

### 14.3 Simple controls

Expose at least:

```text
Frame Thickness Bias
Frame Depth
Temple Thickness
Temple Length
Temple Drop
Roundness
```

`Frame Thickness Bias = 0` means preserve the authored drawing's visible frame thickness as the baseline.

Positive values expand/thicken the visible frame. Negative values thin it within safe topology bounds.

### 14.4 Automatic temple generation

The front frame does not contain meaningful backward-going geometry.

The glasses preset therefore owns a deterministic temple-generation rule using outer frame attachment points and parameters such as:

```text
Temple length
Temple thickness
Temple vertical drop
Temple curvature
```

This is category logic, not AI inference.

### 14.5 Preview gate

A generated pair of glasses must be inspectable:

```text
asset only
on Buddy head — front
on Buddy head — 30 deg left
on Buddy head — 30 deg right
```

The owner should be able to see that the temples and side depth behave correctly before export.

---

## 15. Buddy runtime integration

### 15.1 Preserve trusted rendering boundary

Character files/player state may select trusted IDs and bounded values but may never name arbitrary scenes, scripts, shaders, DLLs, or external mesh paths.

Generated assets remain project-owned trusted content registered through project-authored definitions.

### 15.2 Current architecture constraint

The current Buddy implementation has:

- `CharacterFeatureCatalog.Shipped` with explicitly authored cosmetic definitions;
- `BuddyCosmeticVisualCatalog` mapping cosmetic IDs to trusted anchors/render kinds;
- `BuddyVisualRigView` building several current cosmetics procedurally from primitive meshes.

Asset Forge requires a data-driven generated-asset path alongside that existing legacy path.

### 15.3 Hybrid visual source

Add a trusted visual-source model conceptually like:

```text
BuddyCosmeticVisualSource
+-- LegacyProcedural
+-- GeneratedAsset
```

Do not delete the current procedural renderer immediately.

Existing shipped content continues to work while generated assets use a new trusted mesh/texture path.

### 15.4 Generated cosmetic resource

Introduce a trusted generated cosmetic resource/definition containing engine-facing authored data such as:

```text
CosmeticId
Slot
ApplicationMode
PrimaryAnchor
SecondaryAnchor
Mesh
AlbedoTexture
Thumbnail
DefaultScale
RenderLayer
PaintUvLayoutVersion
GeneratorVersion
CanonicalAssetHash
```

Domain/economy metadata remains in the appropriate existing engine-free/catalogue boundaries.

### 15.5 Attachment modes

Introduce explicit visual application modes:

```text
Attachment
PartReplacement
PairedPartReplacement
```

Examples:

| Category | Application mode |
| --- | --- |
| Glasses | Attachment |
| Hair | Attachment |
| Headwear | Attachment |
| Accessories | Attachment |
| Tops | PartReplacement -> Torso |
| Shoes | PairedPartReplacement -> LeftFoot + RightFoot |

---

## 16. Tops and Shoes — replacement architecture

The current implementation builds `TopUtilityBib` as geometry attached in front of the torso and `ShoesSoftSteps` as geometry added at foot anchors.

That current interpretation is superseded by Section 2.16. Both shipped items are re-authored as shape replacements; no ownership or save migration is required.

### 16.1 Runtime seam

The visual rig needs a visual-only base-part override boundary:

```text
physics body
    |
    +-- default 3D body visual
    +-- selected replacement visual
```

Selecting a replacement:

- hides/replaces only the default 3D mesh for that part;
- keeps the original 2D RigidBody2D/collider/forces/mass/drive untouched;
- keeps the part socket transform and presentation tracking intact.

### 16.2 Torso

```text
Torso physics circle
        |
        +-- default torso visual [hidden when replaced]
        +-- selected generated torso visual [visible]
```

### 16.3 Feet

```text
LeftFoot physics -> generated left foot visual
RightFoot physics -> generated mirrored/right foot visual
```

### 16.4 Body paint under a replacement

Painting shipped before Asset Forge, so the replacement seam lands on a live feature. Two facts decide the behavior:

```text
paint is a grown shell mesh cloned from the part mesh
brush targeting is analytic ray -> UV against the sphere/capsule primitives
```

A replacement mesh has neither, so Section 2.17 applies:

- replacing the part visual also hides that part's paint shell;
- the default part visual and its painted pixels are hidden, never deleted or rewritten;
- removing the replacement restores the default visual with its paint unchanged;
- the replacement is always unpainted and paint input over it does nothing.

The paint document, its PNG persistence, and the analytic mapper are untouched by this milestone. Painting a replacement needs a mesh-based hit/UV path and is future work under Section 17.

### 16.5 Physics envelope preview

Asset Forge should show the underlying physics envelope in translucent form for part-replacement categories.

If the replacement visual significantly exceeds it, show a warning rather than silently altering physics.

Example:

```text
WARNING: Visual extends substantially beyond gameplay collision envelope.
```

Export may remain allowed for developer-authored content unless geometry is otherwise invalid.

---

## 17. Future paintability seam

Full player painting of generated replacement shapes is **not part of this implementation**.

However generated geometry must preserve future support by shipping with stable UVs and a versioned paint-layout contract.

Important v1 behavior:

- generated replacement uses source albedo;
- the replaced part's paint shell is hidden with its default visual, per Section 16.4;
- the replacement system should not force a new physics, character, or paint schema solely for paint;
- future painting can bind a paint texture/shell to the generated replacement mesh using the stable UV definition.

The same UV contract serves the trusted drawing templates in `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md`. There is one paint-layout version across generated meshes and player-drawn templates, not two.

Do not build the full replacement-paint UI in this Asset Forge milestone.

---

## 18. Buddy Studio generated catalogue path

### 18.1 Problem

Adding one cosmetic today means editing five hard-coded places, three of which are engine-free domain code that cannot load a `.tres` at all:

```text
domain  ContentIds                              stable content ID constant + IsCosmetic prefix rule
domain  CharacterFeatureIds                     stable feature ID constant
domain  CharacterFeatureCatalog.Shipped         static CreateShippedDefinitions() list
domain  CataloguePolicy.LaunchContentIds        launch set, count-asserted by tests
data    data/catalogue/cosmetic_*.tres          priced catalogue entry
engine  BuddyCosmeticVisualCatalog              cosmetic ID -> anchor/layer/visual kind
engine  BuddyVisualRigView.Cosmetics            a render method per visual kind
```

That conflicts with the target workflow:

> Generate -> Export to Game -> item appears in the correct Buddy Studio category without writing a new renderer method for each asset.

Merging catalogues alone is not enough. A generated cosmetic that reaches only the priced catalogue is a shop entry with no valid feature ID and no editor slot: `CharacterDocumentValidator` rejects the selection and the editor never lists it.

### 18.2 Required seams

The generated path needs all of:

```text
feature catalogue     CharacterFeatureCatalog gains a generated-definition source
                      alongside Shipped, so generated feature IDs validate and
                      appear in their slot. Shipped stays the launch set.

content IDs           generated IDs keep the existing "cosmetic." prefix so
                      IsCosmetic/IsKnown/IsCatalogueEntry stay true without
                      widening the trust rules.

launch policy         CataloguePolicy.LaunchContentIds and its count assertions
                      describe the launch set only. Generated entries are
                      validated as generated content, never folded into the
                      launch count.

visual catalogue      BuddyCosmeticVisualCatalog gains the generated visual
                      source of Section 15.3 instead of a new enum member per
                      exported item.
```

### 18.3 Display names

Catalogue resources carry `NameKey`/`DescriptionKey`, but there is no string table yet and player-facing text is currently derived from the content ID slug.

Asset Forge therefore authors both: the stable ID slug stays the fallback name and the recipe's display name is exported as the authored key value, so generated content needs no per-item change when localization lands.

### 18.4 Feature colour channels

Every character feature carries a colour and a named colour-channel map. A generated cosmetic's colour is authored in the source PNG.

v1 rule:

```text
generated cosmetic -> no colour picker in Buddy Studio
                   -> albedo texture is the authored appearance
                   -> the feature colour channel is not applied
```

This matches the one-material-region decision in Section 2.7. Named channels return with Section 32.3.

### 18.5 Generated cosmetics catalogue

Add a generated content catalogue/resource separate from manually maintained launch content.

Recommended split:

```text
data/cosmetics/
+-- ... existing/authored content
+-- generated/
    +-- catalogue.tres
    +-- glasses.pink_round.tres
    +-- ...

data/catalogue/
+-- launch_catalogue.tres
+-- generated_cosmetics.tres
```

### 18.6 Runtime merge

`CatalogueLoader` should merge the manually authored launch catalogue and generated cosmetic-sale catalogue into one validated domain catalogue.

Asset Forge must modify only the generated content boundary, not repeatedly rewrite the hand-authored launch catalogue.

### 18.7 Permanent ownership semantics

Generated Buddy Studio entries use the existing cosmetic purchase path:

```text
select unowned -> preview only
Buy            -> existing EconomyService.Purchase
ownership      -> existing unlocked-content set
Save character -> character appearance save path
```

No new wallet or cosmetic ownership set.

---

## 19. Environment runtime integration

The current Environment Decorator already has the correct higher-level architecture:

- trusted decoration definitions;
- category, price, anchor, rotation, render-band metadata;
- environment edit session;
- per-instance purchase semantics;
- placement/move/rotation flow;
- persistent layout;
- trusted visual factory/registry.

Asset Forge should extend the visual/content production path without replacing that domain/transaction model.

### 19.1 Environment visual source

Add conceptually:

```text
EnvironmentDecorationVisualSource
+-- LegacyProcedural
+-- GeneratedMesh
```

Current hard-coded primitive/quads remain valid legacy content.

Generated definitions reference trusted generated mesh/texture/thumbnail resources.

### 19.2 Environment resource extension

Extend `EnvironmentDecorationResource` with generated-visual fields such as:

```text
VisualSource
GeneratedMesh
AlbedoTexture
Thumbnail
DefaultScale
Pivot
GeneratorVersion
CanonicalAssetHash
OptionalLightProfile
```

Do not allow arbitrary scripts or player-provided resource paths.

### 19.3 Generated environment catalogue

Recommended split:

```text
data/environment/
+-- launch_decorations.tres
+-- generated_decorations.tres
+-- generated/
    +-- lamp.bubble.tres
    +-- sofa.blob.tres
    +-- ...
```

`EnvironmentDecorationRegistry` merges launch + generated definitions into one trusted catalogue.

Asset Forge modifies only the generated boundary.

### 19.4 Preserve edit-session/economy semantics

Do not change:

```text
select definition -> free preview
place staged item -> reserve per-instance cost in edit session
Save/Done -> commit layout + wallet delta
Cancel -> restore baseline/release pending costs
```

Generated environment assets behave exactly like existing decoration definitions from the Environment Decorator's point of view.

---

## 20. Environment placement metadata

Asset Forge should derive sensible defaults from category and visible silhouette.

### 20.1 Floor assets

Categories such as:

```text
Lamp
Sofa
Table
Plant
```

Default:

```text
AnchorKind = Floor
Pivot = visible silhouette bottom-center
```

### 20.2 Wall assets

Paintings default to:

```text
AnchorKind = Wall
Pivot = visible silhouette center
```

### 20.3 Proportional sizing

Preserve visible source aspect ratio.

The developer sets a default logical width or height and Asset Forge derives the other dimension proportionally.

---

## 21. Lamp preset and visual lighting metadata

Lamp generation should support a visual light profile.

Conceptual data:

```text
DecorationLightProfile
+-- Enabled
+-- EmissionStrength
+-- LightEnabled
+-- Brightness
+-- Range
+-- Color
+-- EmitterPosition
```

Keep two concepts separate:

```text
bulb looks bright = emissive material
bulb lights room  = actual local light
```

The simple Lamp preset may expose:

```text
Depth
Roundness
Brightness
Range
Light colour
```

Advanced mode exposes emitter position and exact values.

The preview should allow dragging/adjusting a visible developer gizmo for the light emitter.

This remains visual-only and does not create gameplay interaction.

---

## 22. Sofa preset

Initial Sofa preset uses a deep rounded/inflated front silhouette.

The tool should label this model honestly as front-derived/stylized, for example:

```text
Front-only 2.5D
```

Do not claim that v1 reconstructed unseen sofa geometry.

Future:

```text
SofaPresetV2
front.png + side.png
profile loft/intersection
```

may create more accurate furniture depth.

---

## 23. Generated thumbnails

Generate a standardized catalogue thumbnail after successful asset generation.

Recommended source thumbnail:

```text
256x256 RGBA PNG
transparent background
orthographic camera
fixed lighting
fixed framing/padding
```

Buddy cosmetics generally show the asset itself rather than a full Buddy so small assets remain legible in the catalogue.

Thumbnail settings are recipe data:

```text
Yaw
Pitch
Padding
Scale/framing
```

Generated thumbnails should be consumed by Buddy Studio / Environment catalogue presentation instead of requiring per-item preview drawing code.

---

## 24. Exported file layout

### 24.1 Example generated Buddy asset

```text
authoring/
+-- asset-forge/
    +-- glasses/
        +-- pink_round/
            +-- source.png
            +-- recipe.json

assets/
+-- generated/
    +-- cosmetics/
        +-- glasses.pink_round/
            +-- mesh.glb
            +-- albedo.png
            +-- thumbnail.png

data/
+-- cosmetics/
    +-- generated/
        +-- glasses.pink_round.tres
        +-- catalogue.tres

+-- catalogue/
    +-- generated_cosmetics.tres
```

### 24.2 Example generated Environment asset

```text
authoring/
+-- asset-forge/
    +-- lamps/
        +-- bubble_lamp/
            +-- source.png
            +-- recipe.json

assets/
+-- generated/
    +-- environment/
        +-- lamp.bubble/
            +-- mesh.glb
            +-- albedo.png
            +-- thumbnail.png

data/
+-- environment/
    +-- generated/
        +-- lamp.bubble.tres
    +-- generated_decorations.tres
```

Exact paths may be refined during implementation, but keep the separation between:

- developer source/recipes;
- generated binary/textured assets;
- trusted game definitions/catalogues.

---

## 25. Transactional export

`Export to Game` must be transactional.

Correct flow:

```text
Generate
  -> validate source/recipe
  -> validate canonical geometry
  -> build export in temporary staging location
  -> write mesh/texture/thumbnail/definitions there
  -> reload/validate baked GLB
  -> validate stable IDs/catalogue uniqueness
  -> validate economy metadata
  -> validate target paths
  -> only then atomically replace generated asset + generated catalogue entries
```

If any step fails:

```text
Export failed.
Game content unchanged.
```

Never leave a half-exported state such as:

```text
mesh exists
catalogue missing
price entry exists
definition missing
```

---

## 26. Export validation

At minimum validate:

### Identity / paths

```text
ID follows allowed stable-ID syntax
ID is not owned by another recipe
no path traversal
all output paths remain under whitelisted generated roots
```

### Geometry

```text
mesh contains vertices
mesh contains triangles
positions finite
UVs finite
normals finite
no invalid/degenerate unbounded values
```

### Content

```text
texture exists
thumbnail exists
recipe source hash matches source
canonical asset hash matches current generation
category is valid
sort order is valid/unique where required
price is valid
```

### Buddy-specific

```text
valid feature slot
valid attachment/application mode
valid primary/secondary anchors
valid part-replacement target
valid transform policy
```

### Environment-specific

```text
valid DecorationCategory
valid AnchorKind
valid RenderBand
valid rotation policy
valid placement size/pivot
no collision/gameplay nodes
```

### Security/trust

Generated packages must never contain arbitrary:

- scripts;
- DLLs;
- user shaders;
- arbitrary external paths;
- physics bodies/collision for default environment assets.

---

## 27. Tool warnings

Warnings should be visible and actionable rather than silently rewriting suspicious art.

Examples:

```text
WARNING: 17 disconnected shapes detected.
WARNING: Component #12 is only 2 pixels wide.
WARNING: Torso visual extends far beyond physics envelope.
WARNING: No meaningful transparent background detected.
WARNING: Glasses contain no lens holes.
WARNING: Temple attachment points are unusually high.
WARNING: Sofa depth exceeds its front height.
```

Safe warnings may still allow developer export. Invalid geometry/path/trust failures must block export.

---

## 28. Advanced inspector

Advanced mode should expose literal numerical values.

Example:

```text
Geometry
  Alpha Threshold            0.500
  Contour Simplification     0.750 px
  Smoothing Iterations       2
  Thickness Bias             0.000
  Extrusion Depth            0.120
  Bevel Width                0.025
  Bevel Segments             3
  Smooth Normals             Yes

Symmetry
  Mode                       Left -> Right
  Center X                   512

Texture
  Runtime Resolution         512
  Edge Bleed                 8 px

Attachment
  Anchor X                   0.000
  Anchor Y                   0.000
  Anchor Z                   0.000
  Scale                      1.000

Thumbnail
  Yaw                        12 deg
  Pitch                      -8 deg
  Padding                    12%
```

All output-affecting values must be serialized into the recipe.

---

## 29. Complexity / performance diagnostics

Asset Forge should show generated complexity such as:

```text
Vertices
Triangles
Surfaces
Texture resolution
Approximate texture memory
```

Presets should warn when generated geometry is unexpectedly complex.

The game target is simple stylized meshes, not high-poly generated models.

Repeated Environment instances must reuse shared imported mesh/texture resources rather than duplicating unique GPU resources per placed copy.

---

## 30. Runtime failure behavior

### 30.1 Buddy

If a character references a known cosmetic ID but the generated visual resource is missing/corrupt:

```text
log actionable error
fall back to trusted category fallback visual
continue loading character
```

One missing generated cosmetic must not invalidate the entire character document.

### 30.2 Environment

A missing/invalid generated decoration visual must follow the established trusted environment recovery/failure policy.

Never interpret a missing definition as permission to load an arbitrary path from persisted player state.

---

## 31. Explicit v1 non-goals

Do not fold the following into this implementation:

```text
AI-generated geometry
semantic object recognition
automatic sofa-part recognition
multiple material colour channels
player-facing Asset Forge
Workshop importing through Asset Forge
collision generation
gameplay scripting
rig/physics mutation
arbitrary player GLB import
front + side reconstruction
full replacement-paint editor
general mod API
```

---

## 32. Future seams

The architecture should leave room for:

### 32.1 Front + side authoring

```text
front.png
side.png
    -> deterministic profile loft / more accurate depth
```

### 32.2 Player paintability

```text
stable generated UVs
    -> replacement paint adapter
    -> Buddy Studio player painting
```

### 32.3 Named material/colour regions

```text
Primary
Secondary
Accent
...
```

### 32.4 Cosmetic stretch/custom shaping

Future bounded stretch/deformation can operate on generated visual meshes/transforms while remaining independent from physics.

### 32.5 Player-drawn cosmetics

The later Drawn-to-Life-style player feature must **not execute Asset Forge**.

Asset Forge remains trusted developer tooling. Player-drawn content may reuse safe UV/attachment templates without gaining arbitrary mesh/script generation privileges.

---

# 33. Implementation sequence

## AF-0 — Record architecture and source-of-truth decisions

This document is the source-of-truth plan for Asset Forge.

### Position in the programme

Asset Forge is a developer-facing side tool, not shipped game content. It is deliberately **outside** the locked Steam demo order in `docs/ROADMAP.md` and is not gated by Milestone 5.11 or any later milestone. It may start at any time and does not consume demo scope.

The moment it exports content into the game, however, the receiving seams in Sections 15–19 are ordinary game work and follow the normal architecture, test, and review discipline.

### Decisions already resolved

Owner, 2026-08-12, recorded in `docs/DECISIONS.md`:

```text
Tops/Shoes are shape replacements     supersedes the overlay model everywhere
no migration                          there are no existing players
paint under a replacement             Sections 2.17 and 16.4
Asset Forge is not roadmap-gated      developer-facing side tool
```

Gate:

- the `docs/DECISIONS.md` entry exists before the first receiving-seam change lands;
- no conflicting higher-priority owner decision;
- no ambiguous category/economy/physics rule remains unresolved.

---

## AF-1 — Standalone executable shell

Create:

```text
AssetForge.Core
AssetForge.Core.Tests
AssetForge
```

Extract the shared trusted rig library described in Section 4.2.1 and reference it from both projects.

Apply the four build boundaries in Section 4.3: `DefaultItemExcludes`, `export_presets.cfg` exclusions, the deliberate solution decision, and its CI consequence.

Add development scripts such as:

```text
tools/build_asset_forge.bat
tools/run_asset_forge.bat
```

Build empty functional UI with:

```text
New Asset
Open Recipe
Save Recipe
Generate
Export to Game
```

Build an empty 3D preview with orbit/pan/zoom/reset.

Gate:

```text
Desktop Buddy builds normally
Asset Forge builds independently
Asset Forge launches independently
Steam/game export excludes Asset Forge
existing domain/journey/quick suites stay green after the rig extraction
```

---

## AF-2 — Recipe and deterministic foundation

Implement strongly typed recipe model and canonical serialization.

Implement:

- generator version;
- preset ID/version;
- source byte hashing;
- canonical recipe hashing;
- generated canonical hash.

Implement deterministic PNG validation and image loading.

Gate:

- recipe save/load is canonical;
- identical source + recipe reproduces identical recipe/input hashes;
- UI state that is not recipe data cannot change generation hashes.

---

## AF-3 — Mask, contour, holes, triangulation

Implement:

```text
alpha mask
connected components
outer contours
inner holes
deterministic ordering
contour simplification
front-face triangulation
```

Add pure fixtures for:

- convex silhouette;
- concave silhouette;
- one hole;
- multiple holes;
- multiple components;
- thin islands.

Gate:

- same fixtures generate identical canonical topology hashes repeatedly.

---

## AF-4 — Basic 3D extrusion + preview

Implement:

```text
front face
back face
side walls
UVs
normals
source texture
```

Wire canonical generated mesh into preview.

Implement the Core GLB writer of Section 11.1 and choose the mesh-resolution rule of Section 11.3 once, here, for every later category.

Gate:

- generated mesh can be orbited/inspected;
- preview-camera changes never alter canonical mesh hash;
- source colours remain visible;
- the GLB is written and re-read without the Godot editor, and identical input reproduces identical bytes.

---

## AF-5 — Rounded/inflated geometry + symmetry

Implement:

```text
bevel
rounded extrusion
inflated solid
relief (if required by first presets)
normal smoothing
thickness bias
symmetry modes
```

Gate:

- deterministic fixtures cover every implemented geometry mode;
- no NaN/non-finite topology on boundary cases.

---

## AF-6 — Glasses preset

Implement `GlassesPresetV1`.

Required features:

```text
head/eye authoring template
lens-hole preservation
symmetry
frame-thickness bias
frame depth
bevel/roundness
automatic temple generation
EyeGroup anchor
Buddy head preview
front / +/-30 degree inspection
```

Gate:

- owner-authored pink glasses source produces correct pink 3D frames;
- holes remain open;
- temples are visible and plausible from the side;
- generation is deterministic.

---

## AF-7 — Generated Buddy cosmetic runtime seam

Add trusted generated-asset resource support.

Extend Buddy rendering with hybrid:

```text
LegacyProcedural
GeneratedAsset
```

Generated attachment path first supports `Glasses` on existing `EyeGroup` anchor.

Do not remove existing procedural cosmetics.

Gate:

- manually authored generated glasses definition renders in live Buddy;
- same definition renders in physics-free Buddy Studio preview;
- missing asset falls back safely.

---

## AF-8 — Buddy generated catalogue + economy export

Add generated cosmetic catalogue/resource path.

Open the four seams of Section 18.2 — feature catalogue, content IDs, launch policy, visual catalogue — before wiring export. Merging priced catalogues alone produces a shop entry the character validator rejects.

Refactor runtime catalogue loading to merge:

```text
launch catalogue
+
generated cosmetics catalogue
```

Asset Forge export writes:

```text
stable ID
display name
Glasses slot
price
sort order
thumbnail
mesh/texture definition
ownership content ID
```

Gate — complete first vertical slice:

```text
Import pink glasses PNG
-> generate
-> preview on Buddy
-> set price
-> Export to Game
-> build/run Desktop Buddy
-> Buddy Studio > Glasses
-> item visible
-> preview unowned without spending
-> Buy
-> credits deducted once
-> item becomes permanently owned
-> equip
-> save/reload
-> side temples visible when Buddy turns
```

Do not proceed to several more categories until this gate passes.

---

## AF-9 — Part replacement runtime seam

Add:

```text
PartReplacement
PairedPartReplacement
```

Implement visual override of default torso/foot meshes while preserving original physics.

Implement the paint rule of Section 16.4: the replaced part's paint shell hides with its default visual, painted pixels are never rewritten, and brush input over a replaced part does nothing.

Define the outline behavior for replacement meshes. Default parts carry an inverted-hull outline shell; a generated non-convex mesh needs a stated rule rather than an inherited grow amount that closes its own concavities.

Add Asset Forge physics-envelope preview/warnings.

Gate:

- replacing torso/feet changes only presentation;
- existing physics/ragdoll tests remain unchanged;
- no collision/mass/force/profile mutations;
- paint a torso, equip a replacement, remove it: the original paint returns byte-identical;
- an equipped replacement is unpainted and stays unpainted under brush input.

---

## AF-10 — Torso Shape + Foot Shape presets

Implement:

```text
TorsoShapePresetV1
FootShapePresetV1
```

Use inflated/rounded generation by default.

Foot preset can author one side and generate/mirror the paired visual.

Gate:

- generated Tops replace torso visual instead of layering on top;
- generated Shoes replace foot visuals instead of layering on top;
- correct Buddy Studio purchase/equip semantics remain intact.

---

## AF-11 — Generated Environment runtime seam

Extend Environment trusted definitions and visual factory/presenter with:

```text
LegacyProcedural
GeneratedMesh
```

Add merged generated environment catalogue.

Do not modify the established Environment edit-session transaction model.

Gate:

- manually registered generated visual can be selected, staged, placed, rotated, saved, reloaded, and cancelled through the existing Environment Decorator flow;
- generated visual remains non-physical.

---

## AF-12 — Lamp preset

Implement `LampPresetV1`.

Include:

```text
floor pivot
rounded geometry
source colour texture
emission
optional local light
brightness
range
emitter position gizmo
```

Gate:

```text
Asset Forge lamp
-> Environment Decorator > Lamps
-> correct price
-> per-instance placement purchase
-> save/reload
-> visual lighting works
-> no collision/gameplay behavior
```

---

## AF-13 — Sofa preset

Implement `SofaPresetV1` using front-derived rounded/inflated depth.

Label workflow as front-only/stylized rather than reconstructed full 3D.

Gate:

- generated sofa reads correctly frontally and from accepted room-view angles;
- placement metadata/pivot are correct;
- repeated placed instances reuse shared mesh/texture resources.

---

## AF-14 — Thumbnail pipeline

Finalize standardized thumbnail rendering/caching.

Cache key should derive from canonical asset + thumbnail settings, for example:

```text
geometry hash
+ texture hash
+ thumbnail recipe hash
```

Gate:

- generated Buddy/Environment catalogue entries require no per-item thumbnail drawing code.

---

## AF-15 — Regeneration and verification tooling

Add:

```text
Regenerate
Regenerate All
Verify
Verify All
```

Example diagnostics:

```text
OK glasses.pink_round
  input unchanged
  recipe unchanged
  geometry unchanged

FAIL lamp.bubble
  committed generated geometry differs from current recipe
```

`Verify All` should run from command line without launching the Asset Forge UI so CI can eventually validate committed generated content.

This is only possible because the Core owns the GLB writer (Section 11.1). Verification re-derives geometry and bytes from source + recipe in the pure library and compares hashes; it never boots Godot.

---

# 34. Automated test matrix

## Source validation

```text
valid PNG
wrong dimensions
corrupt PNG
fully transparent
fully opaque
semi-transparent edges
single-pixel islands
```

## Geometry

```text
convex silhouette
concave silhouette
single hole
multiple holes
multiple islands
very thin geometry
symmetry
extrusion
rounded extrusion
inflation
bevel
```

## Determinism

```text
same input x100
same recipe x100
recipe save/load round-trip
different geometry value changes canonical hash
preview-camera changes do not change canonical hash
```

## Glasses

```text
two lens holes
bridge
symmetry
temples
frame-thickness bias
safe negative thickness limits
head scale/anchor
front and side preview
```

## Export

```text
duplicate ID
invalid ID
invalid price
invalid category
invalid output path
missing texture
missing thumbnail
missing/corrupt GLB
catalogue conflict
interrupted/staged export failure
```

## Buddy Studio

```text
generated item visible
correct category
correct price
unowned preview
purchase
ownership persistence
equip
character save/load
safe fallback when generated visual missing
```

## Part replacements

```text
torso default visual replaced
left/right feet replaced
physics unchanged
connectors unchanged
old base visual hidden correctly
paint shell hides with the default visual
paint returns unchanged when the replacement is removed
brush input over a replaced part is a no-op
```

## Environment

```text
generated lamp visible
generated sofa visible
correct category
per-instance cost
placement
rotation
save
cancel
reload
no collision
no arbitrary gameplay node
```

---

# 35. Definition of Done

Asset Forge v1 is not considered complete merely because the standalone tool can make a GLB.

The first release-quality gate requires an end-to-end content pipeline.

At minimum:

1. Asset Forge builds as a separate executable and is excluded from the game build/export.
2. 1024x1024 transparent PNG input is validated.
3. Recipes are versioned, saved, reloadable, and deterministic.
4. Holes and disconnected shapes are supported.
5. Glasses generation supports real depth, bevel/rounding, and automatic temples.
6. Generated asset can be orbited/zoomed/panned in preview and preview state cannot alter generated output.
7. Source colours remain materially meaningful in the generated result.
8. Thumbnail is generated automatically.
9. Export is validated and transactional.
10. Generated cosmetic definition/catalogue content is data-driven rather than requiring a new renderer switch case per exported item.
11. Pink-glasses vertical slice works end-to-end in Buddy Studio: preview -> buy -> own -> equip -> save/load.
12. Generated glasses remain visually valid as the Buddy turns and temples/depth are visible.
13. Part-replacement seam exists and changes presentation only, and body paint under a replacement follows Section 16.4 without ever being rewritten.
14. Tops are torso-shape replacements and Shoes are foot-shape replacements, and the two shipped items are re-authored under that model.
15. Generated Environment visual seam exists without altering the Environment transaction model.
16. At least one generated Lamp works end-to-end through Environment Decorator.
17. Generated Environment assets remain non-physical by default.
18. Determinism/regeneration tests are automated.
19. Existing Buddy physics/gameplay tests remain green.
20. Existing Buddy Studio and Environment authored content remains compatible through the legacy visual path during migration.

---

# 36. Primary architecture boundary

The system should remain divided into three clearly different trust/lifecycle layers:

```text
SOURCE ART
Developer-controlled
1024 PNG + recipe
        |
        v
ASSET FORGE
Developer-only deterministic compiler
        |
        v
TRUSTED GAME CONTENT
Mesh + texture + thumbnail + trusted metadata
        |
        v
PLAYER CUSTOMIZATION
Buy / equip / place / eventually paint
```

The player never operates the compiler.

The compiler never mutates gameplay physics.

This boundary is mandatory for keeping generation deterministic, game content trusted, and future player customization safe.

---

# 37. Recommended first implementation delivery

Do not start by building every category.

The first implementation delivery should stop at this complete proof:

> Import one 1024x1024 pink glasses PNG -> choose the Glasses preset -> generate true 3D frames and automatic temples -> inspect them around the Buddy head -> set a name and price -> Export to Game -> build/run Desktop Buddy -> see them as a buyable item under Buddy Studio > Glasses -> purchase/equip them -> see the side temples when the Buddy turns.

Only after that vertical slice is accepted should the shared generator expand in this order:

```text
Torso Shape
-> Foot Shape
-> Lamp
-> Sofa
-> additional categories
```

This sequence validates the most important architectural seams before multiplying content types.
