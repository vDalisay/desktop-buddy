# Buddy Studio — Parallel Agent Handoff

Branch: `buddy-studio`  
Exact shared base: `3a789e1b2ef6c31be562d6aeb89e725649789ae9`  
Parallel sibling: `environment-customization`  

Read before implementation:

1. `docs/CUSTOMIZATION_PARALLEL_IMPLEMENTATION_FOUNDATION.md` — authoritative shared/frozen boundaries.
2. `docs/BUDDY_STUDIO_CUSTOMIZATION_PLAN.md` — product/architecture requirements.
3. `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` — established character editor/session architecture.

Do not implement Environment/Paint Background work on this branch.

---

## 1. Foundation that is already implemented

The Work Mode project already landed more Buddy Studio foundation than the older plan assumes. Do **not** redo it.

Already present:

- character schema version **4**;
- sequential schema `2 -> 3` and `3 -> 4` migrations;
- all twelve feature slots in `CharacterFeatureSet`;
- named `CharacterFeatureDocument.Colors` map seam;
- legacy Brows/TorsoAccent source aliases;
- `CatalogueEntryKind.Cosmetic`;
- `cosmetic.*` namespace recognition;
- permanent cosmetic ownership through the existing unlocked-content set;
- generic purchase path through `EconomyService.Purchase`;
- first real proof-of-concept cosmetic: Work glasses;
- `BuddyStudioBootstrap` reserved autoload composition root;
- shared Customize dropdown registration seam;
- reusable category strip, catalogue grid, value panel, popup style and Win98 dialog/theme.

The critical gap is that **persistence is ahead of rendering**: `CharacterCompiler` / `CompiledCharacterAppearance` still compile only Eyes, Brows, Mouth and TorsoAccent/Accessories. The full Studio work starts by widening definition/compiler/render paths, not by bumping the schema again.

---

## 2. Branch ownership

Primary branch-owned paths:

```text
domain/DesktopBuddy.Domain/Characters/**
src/CharacterEditor/BuddyStudio/**
src/Buddy/Presentation3D/Characters/**
data/cosmetics/**
assets/cosmetics/**
tests/DesktopBuddy.Domain.Tests/Characters/**
src/Testing/BuddyStudio*
```

Feature-specific commerce/content additions may touch:

```text
domain/DesktopBuddy.Domain/Content/ContentIds.cs
data/catalogue/launch_catalogue.tres
src/Content/**
src/CharacterEditor/CharacterEditorSession.cs
```

`CatalogueEntryKind.Cosmetic` and generic cosmetic validation already exist, so avoid needless edits to `CatalogueEntry.cs` / `ToolCatalogue.cs` unless a concrete missing rule requires them.

Do not modify:

```text
domain/DesktopBuddy.Domain/Environment/**
src/Environment/**
data/environment/**
assets/environment/**
domain/DesktopBuddy.Domain/Persistence/ProgressSave.cs
src/Persistence/SaveCoordinator.cs
src/App/ProgressReset.cs
```

Cosmetic ownership is already part of unlocked-content progress, so Buddy Studio should not require a new ProgressSave ownership model.

---

## 3. Frozen shared files

Do not independently change these while the Environment sibling branch is active:

```text
project.godot
src/UI/Win98/Win98CommandBarBootstrap.cs
src/UI/Win98/CustomizeCommandRegistry.cs
src/UI/Win98/Win98CategoryStrip.cs
src/UI/Win98/Win98CatalogGrid.cs
src/UI/Win98/Win98ValuePanel.cs
src/UI/Win98/Win98MenuStyle.cs
src/UI/Win98/Win98ThemeFactory.cs
src/UI/Win98/Win98Dialog.cs
```

If a truly generic capability is missing, coordinate a small shared patch that can be applied to both branches rather than evolving the component only here.

---

## 4. Commerce rule

Buddy cosmetics are permanent unlocks:

```text
select unowned cosmetic -> preview only, no spend
Buy                     -> EconomyService.Purchase(cosmetic content ID)
ownership               -> existing unlocked-content set
Save character          -> character document transaction
Cancel Studio changes   -> appearance working copy rolls back; purchase remains
```

Free/default definitions are usable without purchase.

Do not reuse Environment's per-instance staged purchase model.

Loaded/worn cosmetics after ownership loss follow the existing plan rule: ownership gates choosing a *new* unowned item, not loading/saving an appearance the character already wore.

---

## 5. Character schema rule

Do not perform another schema bump just to implement the planned categories.

Schema 4 settles the vertical convention rather than adding shape: a positive `offsetY` moves a
feature **up** everywhere, and the empty `*.none` variants are non-transformable. The migration
flips the stored `offsetY` of the rig-placed slots (nose, ears, glasses), which used to mean the
opposite, and clears any transform parked on an empty variant.

The schema already serializes:

```text
Face
Hair
Eyebrows
Eyes
Nose
Mouth
Ears
Accessories
Glasses
Headwear
Tops
Shoes
```

and each `CharacterFeatureDocument` already has the legacy primary `Color` plus a named `Colors` dictionary seam.

Only introduce schema 4 if later implementation uncovers a genuinely new persisted concept that cannot be represented safely by schema 3. Such a change should be called out explicitly before landing.

---

## 6. Recommended implementation sequence

### BS-0 — Real cosmetic definition model

Extend the existing character feature catalogue rather than creating a parallel BuddyStudio catalogue vocabulary.

Add engine-free metadata such as:

- stable feature/cosmetic ID;
- `CharacterFeatureSlot` category;
- authored display/sort metadata;
- free/default flag;
- transform policy and bounds;
- default transform;
- one or more stable named color channel definitions;
- tintability;
- compatibility flags such as `HidesHair`;
- trusted fallback ID.

Preserve existing feature IDs and `CharacterFeatureCatalog` responsibility.

### BS-1 — Widen compiler and compiled appearance

This is a major current gap.

Expand `CharacterCompiler` / compiled appearance so every schema-3 category is resolved through trusted definitions, including fallback/warnings.

Keep existing semantic Eyes/Brows/Mouth behavior intact. Unknown optional definitions must fall back safely without invalidating otherwise usable characters.

Tests should prove all 12 categories compile, old schema-2 migrations look equivalent, unknown IDs fall back deterministically, and paint is untouched.

### BS-2 — Trusted engine visual registry / anchors

Add a project-owned visual registry keyed by stable feature ID and explicit visual-only anchors for:

- face/head front;
- crown/hair/headwear;
- eye/glasses plane;
- ear pair;
- torso/top/accessory;
- paired feet/shoes.

No character JSON may name a scene, shader, mesh, material or script path.

Keep physics/collision unchanged.

### BS-3 — Rendering families

Implement launch rendering support:

- Face visual treatment;
- Hair;
- Nose;
- Ears;
- Glasses;
- Headwear, including `HidesHair` without deleting the saved Hair choice;
- Tops over torso paint;
- Shoes over foot paint;
- Accessories through bounded trusted bands;
- existing Eyes/Eyebrows/Mouth remain semantic expression-capable.

### BS-4 — Studio working-copy / ownership preview model

Extend `CharacterEditorSession` rather than adding a second character save stack.

Studio needs to distinguish:

```text
saved baseline
working saveable appearance
unowned preview override(s)
```

Selecting owned/free items edits the working copy. Selecting an unowned item previews it without purchasing and marks only that session/category unsaveable. Buy converts the selected preview into an owned/saveable selection. Cancel restores baseline appearance but never refunds purchases.

### BS-5 — Randomize

Extend the existing deterministic character randomizer to all twelve categories with the owner's locked rule:

- free + owned only;
- every category including clothing/accessories;
- named launch colors;
- valid transforms;
- no purchases;
- no paint/background changes;
- deterministic seed behavior.

### BS-6 — Buddy Studio UI

Use shared:

- `Win98CategoryStrip` for the 12 locked categories;
- `Win98CatalogGrid` for definition tiles;
- `Win98ValuePanel` for Price / Owned status where useful;
- `Win98Dialog` for confirmation/dirty-close flows;
- `ContentDisplayName.Credits` for prices.

Build feature-specific panes/controllers for:

- physics-free character preview;
- contextual Larger / Smaller / Move / Reset;
- color wheel/presets and future named-channel selector seam;
- Buy;
- Randomize;
- Save / Cancel.

Once the workspace is genuinely functional, register `CustomizeCommandIds.BuddyStudio` from `BuddyStudioBootstrap` through `Win98CommandBarBootstrap.RegisterCustomizeCommand`. Do not expose a dead entry earlier.

### BS-7 — Authored content / closure

Add real original cosmetics, cosmetic sale entries to the existing authored general commerce catalogue, pricing, keyboard/focus/DPI validation and the automated scenarios described in the product plan.

---

## 7. First concrete coding target

Start with **BS-0 + BS-1**, not the large UI.

A good first slice should prove in pure/domain tests that a schema-3 character containing one definition for each of the twelve categories compiles into an appearance representation that retains/resolves all twelve slots, while the current four semantic renderer families still behave exactly as before.

Then add one visible vertical slice such as:

```text
Hair
Glasses
Headwear
```

before scaling the renderer/UI across all twelve categories. This validates attachment anchors, color, permanent ownership and `HidesHair` with the smallest representative set.

---

## 8. Stop-and-coordinate triggers

Stop and raise a shared-foundation question instead of editing frozen files if you believe you need to:

- modify the top-level command bar directly;
- add another autoload to `project.godot`;
- put ownership logic into `Win98CatalogGrid`;
- add cosmetic-specific fields to `Win98ValuePanel`;
- change generic Win98 theme/dialog behavior;
- modify Environment persistence/domain files;
- create a second cosmetic ownership set or wallet;
- create `BuddyStudioCharacterDocument` / `BuddyStudioCatalogue` mirrors of established character types.

Prefer a Buddy-specific implementation inside the branch-owned paths unless the missing capability is demonstrably generic.
