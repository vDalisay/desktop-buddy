# Asset Forge — Category UI & Authoring Template Standard

Status: **Owner-approved normative addendum to `docs/ASSET_FORGE_IMPLEMENTATION_PLAN.md`**  
Date: 2026-08-13  
Scope: Asset Forge UI architecture + mandatory authoring templates for every category

This document extends the Asset Forge implementation plan. Where older text implies a single long settings panel or one generic Environment template, this document is authoritative.

---

## 1. Owner decisions

1. Asset Forge must not become a giant global property sheet as more asset types are added.
2. Settings are grouped by **asset category**. Only controls relevant to the selected category are shown in the primary Inspector.
3. Common metadata is separate from category generation controls.
4. Low-level deterministic generator settings remain available through progressive disclosure under **Advanced / Generator**.
5. Repository verification/regeneration/destructive maintenance is secondary tooling and must not compete with the normal Import -> Generate -> Export workflow.
6. Every implemented asset category must ship with a **1024x1024 authoring template** before that category is considered complete.
7. Templates behave like low-opacity coloring-page guides. The developer draws over the guide, then exports a clean source file with the guide hidden/removed without cropping, moving or resizing the canvas.
8. Template coordinates are meaningful. Category generators must define how source pixels map to the relevant Buddy/body/environment coordinate system instead of silently recentring/re-scaling authored art unless that preset explicitly documents legacy auto-fit behavior.
9. A category's template and generator version are one contract. If placement semantics change, bump the preset/template version rather than silently changing old recipes.

---

## 2. Modern Asset Forge workspace

The primary workspace is a three-pane editor:

```text
+--------------------------------------------------------------------------------+
| Asset Forge                         Open Recipe  Save Recipe  Generate  Export  |
+----------------------+--------------------------------------+------------------+
| ASSET / SOURCE       | 3D PREVIEW                           | INSPECTOR        |
|                      |                                      |                  |
| Category             | orbit / pan / zoom                   | category-only    |
| Source PNG           | reference Buddy / floor / wall       | settings         |
| Save template        |                                      |                  |
| Display name         |                                      | Appearance       |
|                      |                                      | Advanced         |
| Publishing details  |                                      |                  |
|   (collapsed)        |                                      |                  |
+----------------------+--------------------------------------+------------------+
| status                                                 Technical details (>)   |
+--------------------------------------------------------------------------------+
```

### 2.1 Left pane — Asset and source

Primary tasks only:

- Asset family/category selector;
- category/template status;
- source PNG;
- Import PNG;
- Save template;
- display name;
- optional migration notice/action for legacy recipe versions.

Publishing metadata is collapsed by default:

- feature/content ID;
- price;
- sort order;
- future placement/catalogue metadata not needed during ordinary shape iteration.

### 2.2 Centre — Preview-first workflow

The preview receives the largest share of the window.

Preview toolbar contains only context-relevant controls such as:

- Show Buddy reference;
- Show floor/wall reference;
- Reset view;
- future template/anchor gizmo toggles.

No generation settings should be layered over the preview itself.

### 2.3 Right pane — Category Inspector

The Inspector is assembled from the selected category preset.

Example — Glasses:

```text
Shape
  Frame thickness
  Bridge thickness
  Depth
  Roundness

Side arms
  Temple thickness
  Temple length
  Temple drop

Appearance
  Lighting level

Generator
  Show advanced settings
```

Example — Lamp:

```text
Shape
  Depth
  Roundness
  Base profile

Light
  Emitter position
  Brightness
  Range
  Emission level

Placement
  Floor pivot / base contact

Appearance
  Lighting/material controls

Generator
  Show advanced settings
```

A control belonging only to Glasses must never be visible for Lamp/Sofa/Table/etc.

### 2.4 Header and secondary tooling

Primary header actions:

```text
Open Recipe
Save Recipe
Generate
Export to Game
```

Repository operations are secondary and hidden/collapsed by default:

```text
Verify
Verify All
Regenerate
Regenerate All
Delete exported asset
```

Technical hashes/diagnostics are collapsed by default but remain one click away.

### 2.5 UX rules

- Use clear section headings and short help text instead of a wall of labels.
- Prefer progressive disclosure over showing every setting simultaneously.
- Keep common authoring actions visible; keep maintenance/destructive actions secondary.
- Use consistent control heights, padding and panel spacing.
- Keep readable contrast and do not encode state by colour alone.
- Disable planned categories rather than letting the developer enter a non-functional editor.
- Show a short explanation for a disabled/planned category.
- Changing a generation-relevant value after Generate invalidates Export until Generate runs again.
- Preview-only camera/orbit changes never invalidate deterministic output.
- UI-only layout state never enters the canonical recipe hash.

---

## 3. Mandatory category-template contract

Every category implementation must define an `AuthoringTemplateSpec` before its generator is considered ready.

Each spec defines:

```text
stable category/template ID
asset family
human display name
template filename
1024x1024 canvas contract
reference scene/object
authoring guides
anchor/contact regions
safe drawing bounds
scale reference
implemented/version status
```

Template PNGs are developer-only and never ship as game content.

### 3.1 General template rules

All templates:

- are exactly 1024x1024;
- use transparent background;
- render reference geometry at low opacity;
- use stronger but still non-destructive guide lines for axes/anchors;
- leave the authored drawing readable on top;
- contain no pixels that should remain in the final clean source;
- document which reference/guide layer must be hidden before export;
- define literal or explicitly documented mapping into category coordinate space;
- include category-specific scale/placement cues rather than only a generic bounding box.

---

## 4. Buddy Studio template specifications

### 4.1 Glasses

Reference: trusted Buddy head.

Guides:

- low-opacity Buddy head silhouette/render;
- face centre line;
- eye line;
- left/right eye centres;
- recommended frame envelope;
- temple-root regions.

Placement semantics:

- literal head-template coordinates for `glasses@2+`;
- authored front frame and bridge are preserved;
- generated temples extend into unseen depth according to recipe settings.

### 4.2 Hair

Reference: trusted Buddy head.

Guides:

- low-opacity head silhouette;
- scalp envelope;
- hairline guide;
- face keep-out area;
- centre line;
- attachment/coverage envelope.

The template communicates where hair may extend beyond the head while keeping the face readable.

### 4.3 Headwear

Reference: trusted Buddy head.

Guides:

- head silhouette;
- crown contact band;
- centre line;
- recommended headwear bounds;
- face keep-out region.

The crown contact region defines the default attachment/pivot expectation.

### 4.4 Accessories

Reference: trusted Buddy head/face.

Guides:

- centre line;
- eye line;
- ear-side regions;
- common accessory anchor zones;
- safe face/head bounds.

The eventual preset may expose a selected anchor subtype; the template must change or clearly mark the chosen anchor region.

### 4.5 Top / Torso replacement

Reference: trusted default torso visual and connector positions.

Guides:

- default torso silhouette;
- torso centre line;
- neck/shoulder connector area;
- lower connector area;
- recommended replacement envelope;
- translucent underlying physics envelope.

The physics envelope is reference-only. Generated visual shape never changes gameplay collision/rig geometry.

### 4.6 Shoes / Foot replacement

Reference: one trusted default foot.

Guides:

- default foot silhouette;
- ankle connector;
- forward direction;
- ground/contact line;
- recommended replacement envelope;
- translucent physics envelope.

Default workflow authors one foot; paired/mirrored generation is deterministic.

---

## 5. Environment template specifications

Environment categories share room-scale conventions, but **do not share one vague generic template**. Each category gets a dedicated guide.

### 5.1 Lamp

Reference: floor plane + Buddy scale silhouette.

Required guides:

- floor line;
- bottom-centre **base contact zone** that must meet the floor;
- vertical centre line;
- overall safe drawing bounds;
- recommended shade/top envelope;
- explicit **light-source / emitter region** inside or below the shade;
- optional stem/body corridor;
- Buddy height/scale reference.

The preset uses these authored regions to initialize:

```text
floor pivot
base contact
emitter position
local light origin
shade/body proportions
```

The emitter remains adjustable in the Inspector with a gizmo, but the template provides the canonical starting region.

### 5.2 Sofa

Reference: floor plane + seated/standing Buddy scale cues.

Guides:

- floor/base contact zone;
- horizontal centre;
- seat-height guide;
- seat envelope;
- back-rest envelope;
- overall safe bounds;
- Buddy scale reference.

### 5.3 Table

Reference: floor plane + Buddy scale silhouette.

Guides:

- floor line;
- leg/base contact region;
- centre line;
- tabletop height;
- tabletop envelope;
- safe bounds;
- Buddy scale reference.

### 5.4 Plant

Reference: floor plane + Buddy scale silhouette.

Guides:

- floor line;
- pot/base contact zone;
- pot envelope;
- vertical centre line;
- foliage safe region;
- overall safe bounds;
- Buddy scale reference.

### 5.5 Painting / wall decoration

Reference: wall plane + optional Buddy scale silhouette.

Guides:

- wall anchor centre;
- horizontal/vertical centre lines;
- recommended artwork bounds;
- frame-safe margin;
- wall plane orientation;
- scale reference.

No floor pivot is used for wall categories.

---

## 6. Category implementation gate

A future category is not complete until all of the following exist together:

```text
AuthoringTemplateSpec
+ generated 1024x1024 template PNG
+ category preset / recipe schema
+ category-specific Inspector controls
+ category-specific preview reference
+ deterministic generator
+ validation tests for template-coordinate mapping
+ export/runtime seam
+ end-to-end visual gate
```

Do not enable a category in the Asset Forge selector before this complete slice exists. A category may appear disabled as `Planned` beforehand.

For every category, automated tests must prove at least:

- template generator produces 1024x1024 RGBA output;
- template generation is deterministic;
- moving/scaling authored content in literal template space produces the documented placement change;
- category-only controls do not alter unrelated categories;
- template/reference pixels are never required in the clean source image;
- old recipe/template versions remain reproducible after a later version is introduced.

---

## 7. Implementation sequence amendments

Amend the main sequence as follows:

### AF-6 / Glasses

Keep the accepted glasses vertical slice, including the Buddy-head coloring template, literal `glasses@2` placement, authored bridge, bridge thickness tuning and lighting level.

### AF-9 / Part replacement seam

Before Torso/Foot presets are enabled, implement their template-space/reference contracts alongside the runtime replacement seam.

### AF-10 / Torso + Foot presets

Gate now additionally requires:

```text
Torso template generated and validated
Foot template generated and validated
Inspector switches to torso-only / foot-only controls
preview uses relevant trusted Buddy reference parts
literal template placement tests pass
```

### AF-12 / Lamp preset

Gate now additionally requires:

```text
Lamp template generated
base contact zone maps to floor pivot
emitter region initializes light-source position
Buddy scale reference visible in authoring template/preview
Lamp-only Inspector controls shown
```

### AF-13 / Sofa preset

Gate now additionally requires a dedicated Sofa template with floor contact, seat/back envelopes and Buddy scale reference.

### Every later category

Add a template specification and generator before enabling the category. The category-template requirement is not optional cleanup after the generator; it is part of the category's first vertical slice.
