# Environment Customization — Demo Closure Status

Date: 2026-08-10  
Branch: `environment-customization`

This is the closure companion to `ENVIRONMENT_DECORATOR_IMPLEMENTATION_PLAN.md`. It records the behavior that is now authoritative after implementation and owner bug-fix passes, and separates automated closure from the remaining subjective/manual ED6 + PAINT-R7 gate.

## Implemented demo scope

### Environment Decorator (ED0–ED5)

Implemented:

- six launch categories: Lamps, Sofas, Paintings, Wallpapers, Plants, Tables;
- at least two authored clean-room entries per category;
- canonical room placement and anchor validation;
- optional persisted grid snapping;
- definition-controlled rotation;
- visual-only trusted render bands;
- in-scene Win98 catalogue/editor UI;
- placement preview and selected-item editing;
- atomic wallet + environment progress commit through `ProgressSave`;
- dirty Save / Discard handling;
- one wallpaper slot;
- wallpaper and Paint Background remain separate data;
- wallpaper removal through the synthetic `None` tile;
- room `Reset All`;
- unresolved definition records remain safe/non-rendered;
- environment presentation is hidden while the dedicated Work companion is active.

### Permanent decoration ownership

The current implementation deliberately distinguishes **purchase** from **placement**.

A purchased copy stays owned permanently:

- a newly purchased copy costs its catalogue price once;
- a duplicate physical copy is another purchase and costs again;
- moving or rotating an owned copy costs nothing;
- deleting a copy that existed before the current editor session does **not** refund currency;
- that deleted copy moves to `OwnedUnplaced` storage;
- placing a stored copy again is free;
- deleting a copy that was bought only in the still-open edit session cancels that staged purchase and restores its pending cost;
- Discard restores the exact pre-editor room, storage and wallet state;
- `Reset All` removes room placements but keeps all previously purchased copies owned.

This storage behavior is now the authoritative interpretation of the plan's "saved purchases are final / no Sell or refund" rule.

### Environment render order

The current room composition is:

```text
opaque room base      z = -100
wallpaper              z = -90
Paint Background      z = -70 (transparent where unpainted)
wall decorations       z = -50
floor decor behind buddy
buddy plane
approved front decor
UI
```

Painting therefore appears on top of wallpaper. Erasing Paint Background reveals the wallpaper, or the default room base when no wallpaper exists. Older opaque base-grey paint files are migrated so their formerly blank pixels become transparent without deleting authored strokes.

### Paint refinement (PAINT-R0–R6)

Implemented in both relevant editors where applicable:

- deterministic clean-room Spray sampler;
- area-scaled spray density;
- stationary held Spray pulses;
- shared Brush Size behavior;
- Curved Line geometry helper;
- compound curve preview/finalization transaction;
- one Undo action for one Spray gesture / one completed curve;
- Buddy curve mapping through existing surface hit testing rather than a second UV mapper;
- environment curve clipping rather than wrapping;
- shortcuts/status text for Spray and Curve;
- semantic paint-icon provider;
- generated original placeholder icons with asset replacement seam;
- icon-only compact paint controls while tooltips, popup entries and semantic actions remain textual.

## Automated closure gates

The scenario registry now exposes the plan's named Environment gates:

```text
environment_decor_catalogue
environment_decor_purchase_per_instance
environment_decor_cancel_transaction
environment_decor_free_placement
environment_decor_grid_snap
environment_decor_rotation
environment_decor_resize_mapping
environment_decor_wallpaper_slot
environment_decor_input_ownership
environment_decor_restart_restore
environment_decorator_room_build
```

The established Environment scenario IDs are also retained and routed to the current ED6 expectations rather than the obsolete six-item/separate-Buy-button vertical slice.

Paint icon closure additionally exposes:

```text
paint_toolbar_icons
```

The new gates cover catalogue/category integrity, semantic previews, non-physical rendering, trusted bands, canonical resize mapping, anchor/snap behavior, per-instance purchases, permanent ownership/storage, cancellation, wallpaper single-slot behavior, save/restart round-trip, editor input isolation, and the paint icon contract.

These scenarios are committed but have **not** been executed by the GitHub connector. Local build/scenario execution remains the verification source of truth.

## Remaining owner gate — ED6 + PAINT-R7

No additional feature implementation should be started before this gate. What remains is primarily presentation/feel and Windows-specific verification that cannot be established from repository inspection alone.

### Build and automated regression

Verify the branch builds and the domain/scenario suite runs without failures. Any compiler/runtime failure is implementation work and should be fixed before subjective review.

### Environment Decorator manual closure

Verify at 100%, 125%, 150% and 200% Windows DPI, and at minimum/default/maximized/full-interaction sizes:

- catalogue density and readability;
- category/title/selection clarity;
- item price and owned-storage feedback;
- free-placement pointer feel;
- Fine/Medium/Large snap feel;
- moving, selecting, rotating and deleting furniture;
- duplicate placement/payment clarity;
- saved deletion banks the copy without refund;
- banked copy can be placed again free;
- `Reset All` retains ownership;
- `None` wallpaper behavior;
- wallpaper -> paint -> furniture visual layering;
- Paint Background data survives wallpaper replacement/removal;
- normal gameplay input never leaks through the editor;
- shell/click-through/input state restores after closing.

### Paint manual closure

Verify Paint Buddy and Paint Background at minimum/default/maximum Brush Size:

- stationary Spray hold;
- slow vs fast Spray traversal;
- Buddy Spray around the UV seam and top/bottom limits;
- Background Spray at room edges;
- Curved Line baseline plus one and two meaningful bends;
- cancel a pending curve at each intermediate phase;
- Undo immediately after Spray and after Curved Line;
- Buddy curve crossing silhouette gaps and multiple body parts;
- placeholder icon readability, focus and tooltips at 100/125/150/200% DPI;
- minimum/default/maximized editor layouts.

If this gate passes, the current Environment demo pass is complete.

## Explicitly after the demo gate

Do not implement these as part of current Environment closure:

```text
RELEASE-ENV1  Multiple local room/environment profiles
RELEASE-ENV2  Complete-room package format + Steam sharing
RELEASE-ENV3  Authored buddy/furniture interactions
```

Those follow only after the singleton room representation and owner closure are accepted.
