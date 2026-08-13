# Buddy Studio — Current-Release Closure Status

Date: 2026-08-10  
Branch: `buddy-studio`

This document is the closure companion to `BUDDY_STUDIO_CUSTOMIZATION_PLAN.md`. It records the implemented current-release scope and separates it from the explicitly deferred full-release work in `BUDDY_STUDIO_FULL_RELEASE_PLAN.md`.

The branch has been synchronized with the accepted Environment Customization `main` merge, so Buddy Studio now sits on the same finalized Win98/customization/paint foundation that the Environment demo uses.

## Implemented current-release scope

### Twelve-category appearance model

The shipped character/cosmetic contract covers the locked order:

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

The existing character document, normalizer, validator and compiler own these categories. Buddy Studio does not introduce a second character schema or save stack.

Current definitions include real free/default content for every visible category and representative original alternate/paid content across the renderer families. Work glasses retain their existing Work Mode reward ownership route rather than becoming a duplicate shop purchase.

### Trusted rendering

Runtime appearance uses project-owned cosmetic definitions and a closed trusted visual catalogue. Character files store stable IDs and bounded appearance values only; they cannot name arbitrary Godot scenes, scripts, shaders, materials or meshes.

Implemented visual families include:

- semantic Eyes / Eyebrows / Mouth expression families;
- Hair;
- Nose;
- paired Ears;
- Glasses;
- Headwear with `HidesHair` without deleting the saved Hair choice;
- Tops over body paint;
- paired Shoes;
- existing Accessories/accent rendering;
- explicit bounded cosmetic render layers and visual-only anchors.

Cosmetics remain presentation-only. The established rig and live-swap scenarios assert that cosmetic/appearance swaps do not add physics authority, do not mutate trusted buddy geometry, and do not change the simulation physics contract.

### Ownership and purchase

Cosmetic ownership reuses the established catalogue, wallet and unlocked-content progression state.

```text
unowned selection -> preview only
Buy               -> permanent account ownership
owned preview      -> explicit Equip
Save               -> character document transaction
Cancel             -> appearance reverts, purchases remain owned
```

There is no Buddy-Studio-specific wallet or ownership database.

A cosmetic already worn by a saved character remains loadable/saveable after progression ownership is reset. Ownership gates choosing a new unowned item, not preserving an appearance that the character already wore.

### Studio working copy

`CharacterEditorSession` owns the Studio transaction. It distinguishes:

- saved baseline appearance;
- saveable working appearance;
- owned preview overrides awaiting Equip;
- unowned preview overrides that block Save.

The session also shares the existing Paint Buddy working copy. Accepted Environment paint changes are now merged into this branch, including cancellation of transient paint/curve preview state at character-session boundaries.

### Studio UI

The current Studio is an in-scene Win98 workspace with:

- the locked twelve-category strip;
- current-buddy preview;
- category-aware portrait framing and zoom/reset-view controls;
- catalogue grid with thumbnails and Owned/Preview state;
- price/balance feedback;
- Buy / Equip state;
- contextual Smaller / Larger / Move / Reset controls only for transformable definitions;
- one launch color channel UI on top of a named-channel-capable schema;
- deterministic Randomize;
- Save / Cancel and dirty-close handling;
- keyboard save, Move-mode Escape and keyboard nudging;
- no rotation control;
- normal `Customize > Buddy Studio` registration only after the real workspace is ready.

### Randomize

Randomize operates over all twelve categories and:

- uses only free/default plus permanently owned cosmetics;
- never purchases content;
- never spends credits;
- produces bounded valid transforms;
- preserves Paint Buddy data and unrelated extension state;
- keeps a randomized Hair choice stored even when selected Headwear hides it;
- is deterministic for the same seed and catalogue/ownership input.

## Authored launch commerce/content

The current production catalogue exposes paid Studio entries for the representative shipped cosmetics while keeping cosmetic entries out of the tool shop and out of the locked sixteen-entry selectable tool schedule.

The currently authored paid Studio catalogue includes Hair, Nose, Ears, Headwear, Tops and Shoes examples. Work glasses remain earned through Work Mode and are intentionally absent from the purchase catalogue.

All visible cosmetic definitions resolve an original cached Studio thumbnail. Missing/proprietary placeholder art is not part of the intended closure state.

## Focused automated closure gate

Run:

```bat
devtools\verification\validate_buddy_studio.bat
```

The focused validator performs:

1. solution build;
2. complete domain test project, including schema-2 migration, schema-3 compatibility migration, transform policy/bounds and compiler coverage;
3. Godot import;
4. `character_rig_view` — trusted anchors, render layers, cosmetic physics isolation, hair/headwear compatibility and Paint Buddy underlay preservation;
5. `expression_renderer_coverage` — alternative semantic Eyes, Eyebrows and Mouth families remain valid across required expression poses;
6. `character_swap_physics_invariant` — live appearance swaps preserve the gameplay physics invariant;
7. `buddy_studio_ownership_preview` — unowned preview, Save block, permanent Buy, explicit Equip, Cancel semantics, ownership-loss behavior and purchase/equip/restart;
8. `buddy_studio_randomize` — deterministic twelve-category owned/free-only Randomize and paint preservation;
9. `buddy_studio_ui_composition` — twelve-category UI, authored commerce/thumbnails, purchase/equip UX, transforms, focus/dirty-close and Studio interaction behavior;
10. `character_editor_create_use_and_react` — all-category character edit/save/use/restart plus the established Paint Buddy save/use/restart journey;
11. the normal-boot `--buddy-studio-startup-check` — verifies the production `Customize > Buddy Studio` route opens the real workspace through the actual bootstrap path.

These tests are committed but have **not** been executed by the GitHub connector. Local execution is the source of truth.

## Remaining owner closure gate

If the focused validator passes, the remaining current-release work is the final Windows visual/interaction acceptance that cannot be established by repository inspection alone.

Verify at 100%, 125%, 150% and 200% Windows scaling, and at minimum/default/maximized/full-interaction window sizes:

- all twelve categories remain reachable in the correct order;
- the preview clearly reads as the current buddy and category framing is useful;
- thumbnails are legible and no visible cosmetic looks like unfinished placeholder content;
- unowned preview, price, Buy, Owned, Equip and insufficient-funds states are immediately understandable;
- a purchased cosmetic remains owned across character switching/restart;
- Cancel reverts unsaved appearance changes without undoing purchases;
- Larger / Smaller / Move / Reset appear only where meaningful;
- Move by drag and keyboard nudge feels controlled and cannot escape authored bounds;
- no rotation control is present;
- the single launch color-channel workflow is clear;
- Randomize changes the complete look, including clothing/accessories where eligible, and never spends money;
- Save gives a clear reason when an unowned preview is active;
- Hair disappears/reappears predictably under `HidesHair` headwear without losing its saved selection;
- Tops/accessories/shoes layer correctly over the preserved Paint Buddy artwork;
- alternative Eyes/Eyebrows/Mouth continue responding to normal expressions;
- gameplay physics, grabbing and interaction feel are unchanged after saving/equipping cosmetics;
- focus/Tab navigation, Escape and Ctrl+S remain usable;
- closing Studio restores the ordinary Win98 shell/input state.

If this gate is accepted, the current Buddy Studio implementation can be merged into `main`.

## Explicitly deferred full-release work

Do **not** treat these as blockers for the current Buddy Studio merge:

```text
RELEASE-BS1..3  Player-made painted cosmetics using trusted category templates
RELEASE-BS4     Bounded anchored cosmetic stretching/deformation
RELEASE-BS5     Steam sharing/import of safe custom-cosmetic packages
RELEASE-BS6     Larger Browse / My Creations / Create-Edit / Shared UI revamp
RELEASE-BS7     Full-release migration/performance/Steam/UGC closure
```

Those requirements are detailed in `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md` and begin only after the current trusted shipped-cosmetic Studio is accepted and the required Steam/platform foundation exists.
