# Asset Forge v1 — Implementation Closure

Status: **implementation complete; final owner visual/input pass pending**  
Branch: `codex/asset-forge-v1-completion`  
Baseline: Asset Forge v0.1 was accepted and merged to `main` as `618df8f41379abafd4e39f97112c99eb04cdec5e`.

This document records the implemented boundary for `docs/ASSET_FORGE_IMPLEMENTATION_PLAN.md` and the owner-approved category/template addendum. It intentionally distinguishes automated architectural closure from the final subjective visual pass.

## v1 category set

The developer-facing v1 tool has complete authoring slices for:

- Glasses (`glasses@1` legacy + `glasses@2` literal Buddy-head template)
- Top / Torso replacement (`torso_shape@1`)
- Shoes / Foot replacement (`foot_shape@1`, deterministic paired presentation)
- Lamp (`lamp@1` legacy auto-fit + `lamp@2` accepted v0.1 literal mesh + `lamp@3` smoothed literal floor template)
- Sofa (`sofa@1` accepted v0.1 literal mesh + `sofa@2` smoothed literal floor template)
- Table (`table@1`)
- Plant (`plant@1`)
- Painting / wall decoration (`painting@1`)

Hair, Headwear and generic face/head Accessories remain visible as **Planned** template seams. They are not v1 requirements in the source plan; they are later category expansions and remain disabled rather than exposing a non-functional editor.

## Lamp v1 quality closure

New Lamp authoring now defaults to **lamp@3 / Inflated Solid** with full deterministic surface smoothing.

Literal Environment silhouettes no longer have to expose the occupancy-grid staircase as their final outline. The generator now:

1. keeps the bounded occupancy grid as the topology/runtime-budget authority;
2. reconstructs the original 1024×1024 alpha boundary using deterministic marching-squares segments;
3. projects generated rim vertices back onto that full-resolution authored contour;
4. reuses the proven Buddy replacement rim/cap fairing for bounded smoothing;
5. re-pins authored floor-contact vertices after smoothing;
6. recalculates normals deterministically.

The smoothing change is explicitly versioned. `lamp@1` keeps legacy visible-bounds auto-fit, `lamp@2` keeps the accepted v0.1 literal-template/pre-polisher geometry path, and `lamp@3` opts into full-resolution rim projection/fairing. Existing `lamp@1` and `lamp@2` recipes therefore remain reproducible from their saved source + recipe rather than silently changing when the tool is upgraded.

Low-opacity coloring-guide pixels remain below the normal alpha threshold and are not treated as generated geometry. Automated coverage models guide pixels beneath authored opaque art and proves they do not alter the Lamp geometry/GLB.

## Explicit preset migration

Backward compatibility and adoption of the improved contracts are separate actions. Opening an older recipe keeps its saved preset version and therefore keeps its historical output path. Asset Forge now exposes an explicit migration action when a newer supported contract exists:

- `glasses@1 -> glasses@2`: switches auto-fit to literal head-template placement and requires source realignment on the current guide;
- `lamp@1 -> lamp@3`: switches auto-fit to literal floor-template placement, adopts Inflated Solid + full smoothing and requires source realignment;
- `lamp@2 -> lamp@3`: preserves literal placement while opting into Inflated Solid + full-resolution smoothing;
- `sofa@1 -> sofa@2`: preserves literal placement while opting into the shared Environment silhouette polisher.

Migration preserves stable identity, economy, light/placement and thumbnail metadata unless the target contract explicitly changes a generation default. The generated preview/export is invalidated after migration and must be regenerated before export. This prevents silent reinterpretation while avoiding JSON hand-editing for developers who do want the newer preset.

## Initial Environment category closure

### Sofa

- dedicated 1024×1024 floor template;
- seat/back/floor/Buddy-scale guides;
- front-derived stylized 2.5D volume;
- floor pivot;
- data-driven catalogue/export/runtime path;
- per-instance economy and stable-ID persistence coverage;
- `sofa@1` remains the accepted v0.1 pre-polisher geometry contract;
- new authoring defaults to `sofa@2`, which adds the shared full-resolution rim projection/fairing without rewriting `sofa@1` recipes.

### Table

- dedicated 1024×1024 floor template;
- base/leg contact and tabletop height/envelope guides;
- literal floor placement;
- rounded front-derived volume;
- shared generated Environment export/runtime path.

### Plant

- dedicated 1024×1024 floor template;
- pot/base contact and foliage-safe guides;
- literal floor placement;
- inflated smoothed volume;
- shared generated Environment export/runtime path.

### Painting

- dedicated 1024×1024 wall template;
- centre wall anchor, artwork bounds and frame margin;
- literal wall-centre mapping rather than floor mapping;
- thin flat/rounded/relief-capable volume;
- no local light and no rotation by default;
- shared generated Environment export/runtime path.

All non-Lamp initial Environment categories are explicitly non-emissive and remain non-physical.

## AF-0 through AF-15 closure matrix

| Phase | v1 status | Primary closure |
|---|---|---|
| AF-0 | Complete | Architecture/source-of-truth and owner decisions recorded. |
| AF-1 | Complete | Standalone Godot/.NET tool, independent build/run scripts, game/export exclusions, shared trusted visual profiles. |
| AF-2 | Complete | Typed/versioned recipes, canonical JSON, deterministic source/recipe/asset hashes, PNG validation. |
| AF-3 | Complete | Alpha masks, disconnected components, holes, deterministic contour/topology fixtures. |
| AF-4 | Complete | Canonical textured mesh, normals/UVs, deterministic Core-owned GLB writer, orbit/pan/zoom preview. |
| AF-5 | Complete | Rounded/inflated/relief generation, normal/surface smoothing, thickness bias and symmetry coverage. |
| AF-6 | Complete | Glasses template, literal v2 placement, holes/bridge/temples, head preview, legacy v1 compatibility. |
| AF-7 | Complete | Trusted generated Buddy cosmetic seam with safe fallback and legacy procedural compatibility. |
| AF-8 | Complete | Generated Buddy catalogue/economy export; preview/buy/own/equip/persistence runtime scenarios. |
| AF-9 | Complete | Torso/foot presentation replacement seam; trusted physics unchanged; paint hide/restore/no-op rules; outline/connector gates. |
| AF-10 | Complete | Torso and Foot authoring presets/templates and deterministic replacement generation. |
| AF-11 | Complete | Data-driven generated Environment catalogue/visual seam without changing edit-session transactions; non-physical runtime path. |
| AF-12 | Complete | Lamp authoring, floor pivot, versioned smoothed volume, emission/local light, emitter gizmo, per-instance economy and persistence. |
| AF-13 | Complete | Sofa authoring, dedicated template, versioned smoothed 2.5D generation, per-instance economy and persistence. |
| AF-14 | Complete | Shared deterministic 256×256 thumbnail contract/cache and catalogue integration. |
| AF-15 | Complete | Verify/Verify All/Regenerate/Regenerate All from pure Core, CI fixture regeneration, drift/corruption/identity coverage and combined Buddy+Environment maintenance. |

## Maintenance/destructive tooling

Repository maintenance is unified across Buddy Studio and Environment assets:

- Verify
- Verify All
- Regenerate
- Regenerate All
- Delete Asset…

Delete now lists both asset families. Environment deletion removes the owned authoring source/recipe, generated mesh/texture/thumbnail and trusted definition, then rebuilds the generated Environment aggregate without disturbing peer assets. Git remains the recovery/history mechanism for deliberate destructive authoring changes.

## Warning and performance behavior

Suspicious but valid generation remains exportable and receives non-canonical authoring warnings instead of silent art rewriting. Current warnings cover:

- many/disconnected visible shapes;
- unusual Glasses lens-opening results;
- Buddy replacement visuals substantially outside the trusted physics envelope;
- excessive Environment depth;
- category triangle-budget overruns.

Warnings never enter hashes or generated bytes. Trust/path/recipe/geometry failures still block generation/export.

Generation diagnostics expose component/hole counts, vertices, triangles, lighting, source/recipe/geometry/asset hashes and category runtime-budget guidance. Thumbnail cache and generation timing/output-size diagnostics remain developer-only.

## Trust boundary retained

The v1 compiler remains developer-only and deterministic:

```text
1024 PNG + canonical recipe
          |
          v
Asset Forge Core
          |
          v
trusted mesh + texture + thumbnail + metadata
          |
          v
existing Buddy Studio / Environment runtime transactions
```

Generated packages cannot introduce player scripts, DLLs, arbitrary shaders, arbitrary external paths, collision bodies or gameplay behavior. Persisted player state continues to store trusted stable IDs rather than arbitrary asset paths.

## Automated closure gate

The Asset Forge CI workflow now covers:

- game solution build;
- domain tests;
- deterministic Core tests;
- standalone Asset Forge build;
- source/export exclusion rules;
- transactional generation/export fixtures;
- pure-Core Verify All;
- explicit pre-upgrade `lamp@1`, `lamp@2` and `sofa@1` geometry compatibility gates;
- explicit migration-policy tests for Glasses/Lamp/Sofa;
- game import/boot;
- generated Glasses commerce/render/persistence;
- generated Torso/Foot replacement + paint/outline/connector rules;
- generated Lamp runtime/economy/light/persistence;
- generated Sofa runtime/economy/persistence;
- generated Table/Plant/Painting trusted runtime/transaction/persistence seams;
- standalone Asset Forge import and boot.

The PR should not be considered ready for owner visual acceptance until the latest run is green.

## Final owner pass

Once CI is green, the remaining work is deliberately subjective/local rather than missing architecture:

1. Generate the provided Lamp-like art with the Lamp defaults (`lamp@3`, Inflated Solid). Confirm rounded shade/stem/base boundaries no longer show the previous grid staircase and that the intended silhouette is not over-smoothed.
2. Confirm Lamp emitter dragging in the front view, preview orbit/reset, export, in-room local light origin, per-instance purchase and restart persistence.
3. Generate one Sofa with the new `sofa@2` default and confirm the front-derived volume, scale and floor contact fit the game art direction; export/place/restart it.
4. Save/import the Table, Plant and Painting templates once each; confirm literal placement and scale. Painting should use a wall anchor; Table/Plant should use the floor.
5. Inspect generated catalogue thumbnails for Glasses/replacements/Environment at normal UI size.
6. Open an existing `lamp@2` and `sofa@1` recipe, regenerate/verify them, then use the explicit migration action on disposable copies and confirm the old output stays reproducible until migration is chosen.
7. Exercise Delete Asset… with a disposable generated Environment item and confirm its peer generated items remain in the catalogue.

Any failure in this final pass should be treated as v1 polish on this branch rather than expanding scope into the explicit non-goals/future categories.
