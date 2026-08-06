# Desktop Buddy — Environment Decorator Implementation Plan

Status: **Owner UX decisions locked — detailed implementation plan**  
Branch baseline: `main`  
Depends on:

- `docs/POST_WIN98_ENVIRONMENT_CUSTOMIZATION_PLAN.md`
- `docs/WIN98_UI_UX_REVAMP_PLAN.md`
- the accepted Win98 shell / command-bar UI foundation
- existing economy ledger and progress persistence
- existing window/input ownership bridge

This plan refines the room-decoration portion of the broader environment roadmap. It does **not** replace the separate `Paint Background` editor. The Environment Decorator is specifically for buying, placing, rotating, selling, and persisting room decorations and wallpaper presets.

> Owner wording included `pants` as a category. This plan interprets that as **Plants**, because it is an environment-decoration category alongside lamps, sofas, paintings, wallpapers, and tables. If literal Pants was intended, correct the category before implementation.

---

## 1. Product goal

The Environment Decorator should feel like a compact late-1990s desktop room-design application embedded inside Desktop Buddy: a Win98-skinned catalogue on top of the live room, with direct mouse placement into the game area.

The mockup establishes the target interaction model:

- category tabs across the top;
- a paged/scrollable visual catalogue;
- selected item preview;
- explicit placement controls;
- optional grid snapping;
- visible budget / item cost / projected balance;
- free mouse placement into the room;
- selected placed items can be rotated or sold;
- the buddy remains visible while decorating;
- all ordinary UI remains in-scene rather than opening an inaccessible native child window.

The UI should borrow generic conventions from home-design, creative, and simulation applications while keeping the project's original Win98 visual language.

---

## 2. Locked owner decisions — 2026-08-06

### 2.1 Per-instance purchase model

Decorations are **not permanent unlocks**.

The player pays for every individual placed instance.

Examples:

- placing one Classic Floor Lamp costs the lamp price once;
- placing a second Classic Floor Lamp costs the same price again;
- placing two sofas means paying for two sofa instances;
- moving or rotating an already purchased placed instance does not charge again.

Selecting or previewing a catalogue entry never spends money.

The economy distinction is therefore:

```text
catalogue definition != owned unlock
placed instance = purchased room object
```

Do not reuse Buddy Studio's permanent cosmetic-unlock model for room decorations.

### 2.2 Physics / interaction scope

Launch decorations are visually persistent and **non-physical by default**.

They do not automatically:

- block buddy movement;
- change collision;
- add mass;
- receive damage;
- become throwable;
- participate in grab logic;
- alter economy or passive income;
- change mood or autonomy.

A later decoration may opt into specifically authored buddy interaction through a narrow trusted behavior contract. Generic decorations must never acquire gameplay behavior merely because they are placed.

### 2.3 Paint Background remains separate

`Customize > Paint Background` remains a dedicated background painting/color editor.

The Environment Decorator's **Wallpapers** category contains selectable project-owned wallpaper presets only. It does not expose freehand painting, the Paint Buddy brush system, custom background image import, or the background color editor.

The separation is:

```text
Customize
├─ Paint Buddy       -> direct buddy paint editor
├─ Paint Background  -> background paint/color editor
└─ Buddy Studio      -> buddy appearance/clothing

Room decoration flow
└─ Environment Decorator -> furniture, room objects, paintings, wallpaper presets
```

The Environment Decorator should be reachable from the room/decor shopping flow, for example `Shop > Decor` / `Decorate Room`, without adding a fourth entry to the already locked Customize menu unless the owner later requests that change.

### 2.4 Launch placement controls

Launch placement supports:

- free mouse position;
- rotation;
- optional grid snapping;
- a selectable snap-grid size.

Launch does **not** support:

- arbitrary resize;
- mirroring;
- manual forward/back layer reordering.

Those are explicitly recorded in the future nice-to-have section.

### 2.5 Launch categories

Launch category order:

1. Lamps
2. Sofas
3. Paintings
4. Wallpapers
5. Plants
6. Tables

Only categories with real shipped content appear. Do not expose empty placeholders.

---

## 3. Environment Decorator UX structure

## 3.1 Window composition

The editor is a draggable in-scene Win98 application panel layered over the room. It uses the shared UI theme/components rather than a new one-off theme.

Recommended structure:

```text
Environment Decorator window
├─ title bar
│  ├─ app icon
│  ├─ "Environment Decorator"
│  └─ close
├─ category tab row
├─ catalogue group
│  ├─ "Catalog - <Category>"
│  ├─ page / item-range status
│  ├─ previous / next page controls when required
│  └─ scrollable item grid
├─ lower inspector row
│  ├─ selected-item preview
│  ├─ placement controls
│  └─ budget panel
└─ contextual status/help strip
```

The panel should not consume the entire game viewport. The player must retain a useful visible room area for direct placement.

At narrow window sizes the panel may reduce catalogue columns or become vertically scrollable, but it must not spawn detached windows.

## 3.2 Category tabs

Each category uses an original small pixel-art icon and text label.

Categories behave as mutually exclusive tabs:

- selected category is visibly recessed/selected;
- switching category clears any uncommitted catalogue ghost preview;
- switching category does not alter already placed decorations;
- keyboard focus follows the shared Win98 focus rules;
- Left/Right arrow category traversal is recommended when the tab row has focus.

## 3.3 Catalogue grid

Each card shows:

- item preview;
- display name;
- price;
- selected state;
- affordability state.

Do not show internal content IDs in player-facing tooltips.

Cards should use fixed logical dimensions so the grid remains visually stable. The number of columns should respond to available panel width.

The catalogue may use either:

- page navigation; or
- one scrollable grid.

Recommended launch behavior is a **scrollable grid with a lightweight page/item-range label** only if the current Win98 layout genuinely benefits from paging. Do not force two navigation systems for the same list.

Items that are too expensive remain selectable for inspection but the placement action is disabled and the budget panel explains the shortfall.

## 3.4 Selection and preview

Selecting a catalogue card:

1. changes the selected catalogue definition;
2. updates the lower preview panel;
3. updates item cost;
4. updates projected funds;
5. enables `Place` only when placement can be afforded;
6. does not charge money;
7. does not alter the room until placement mode is entered.

Selecting an existing placed decoration instead changes the lower panel into **placed-instance mode**.

Placed-instance mode shows:

- definition preview/name;
- original/current value where useful;
- Rotate;
- Sell;
- Cancel selection.

The Place action is hidden or replaced appropriately because an existing object is already in the room.

---

## 4. Purchase and placement transaction

## 4.1 Placement state machine

Use an explicit state machine instead of inferring behavior from button visibility.

Suggested states:

```text
Browsing
CatalogueItemSelected
PlacingGhost
PlacedInstanceSelected
RotatingInstance
PendingSell
```

Transitions are deterministic and testable.

### Catalogue flow

```text
Browsing
  -> select catalogue card
CatalogueItemSelected
  -> Place
PlacingGhost
  -> move pointer over room
  -> left-click valid location
  -> reserve instance price
  -> create staged placed instance
  -> return to CatalogueItemSelected or PlacedInstanceSelected
```

Escape/right-click while `PlacingGhost` cancels the ghost without charge.

## 4.2 When money is charged

The room editor should operate as an **edit transaction** so experimentation is not punished by accidental clicks.

Recommended launch semantics:

- placing a new decoration immediately reserves its price inside the edit session;
- available/projected funds update immediately;
- the durable ledger is not mutated merely by hovering or ghost placement;
- `Save/Done` commits the environment changes and pending money delta together through a coordinated persistence boundary;
- cancelling the complete edit session restores the original room snapshot and releases all pending costs;
- a failed save keeps the edit session open and does not lose the staged layout.

If the current persistence architecture cannot guarantee a coordinated environment + economy commit, implement a recovery journal before shipping rather than allowing a crash to create a free object or deduct money without creating its object.

## 4.3 Affordability

For a candidate placement:

```text
projectedFunds = startingFunds
                 - totalPendingPlacementCosts
                 + totalPendingSellRefunds
```

A new placement is allowed only when `projectedFunds >= item.Price` after all currently staged changes.

The budget panel shows:

- Available Funds;
- Item Cost;
- After Purchase / Projected Funds.

Negative balances are never permitted.

## 4.4 Selling

Selling removes a purchased placed instance from the layout and credits a data-driven refund into the same edit transaction.

The refund rule must not be hard-coded into the UI.

Recommended policy seam:

```csharp
DecorationEconomyPolicy.SellRefundPermille
```

Recommended first tuning value: **100% refund** during the initial release so decorating encourages experimentation rather than punishing layout iteration. This is a tuning recommendation, not a locked owner economy decision, and can be changed before implementation begins.

A newly staged object that is sold before Save simply removes its pending purchase instead of generating extra money.

---

## 5. Domain architecture

Create the engine-independent model first.

Recommended types:

```text
DecorationCategory
DecorationDefinitionId
DecorationDefinition
PlacedDecorationId
PlacedDecoration
DecorationAnchorKind
DecorationInteractionKind
DecorationRotationPolicy
DecorationEconomyPolicy
EnvironmentLayout
EnvironmentEditSession
EnvironmentEditOperation
EnvironmentEditResult
```

## 5.1 DecorationDefinition

A trusted project-authored definition should contain domain-safe metadata such as:

```text
Id
DisplayNameKey
Category
PriceMilliCredits
AnchorKind
DefaultRotationDegrees
RotationStepDegrees
AllowsRotation
InteractionKind
RenderBand
Footprint / placement bounds
Visible
```

Godot resources hold engine-only references:

```text
preview texture
trusted scene / sprite resource
placement ghost visual
optional authored interaction adapter
```

Imported data may reference a known definition ID but never provide an arbitrary scene path, script, shader, or executable resource.

## 5.2 PlacedDecoration

Persist instance data, not live nodes:

```text
InstanceId
DefinitionId
CanonicalPositionX
CanonicalPositionY
RotationDegrees
RenderBand
Optional trusted visual state
```

Do not persist OS screen coordinates.

Do not persist scale, mirror, or arbitrary z-order in the launch schema because those controls are future nice-to-haves. Reserve schema evolution rather than writing unused fields.

## 5.3 Category enum

Launch values:

```text
Lamp
Sofa
Painting
Wallpaper
Plant
Table
```

Stable content IDs are persistence contracts. Names and display order may change without changing IDs.

---

## 6. Placement coordinates and anchors

## 6.1 Canonical room coordinate space

Persist room placement in resolution-independent canonical coordinates.

Recommended normalized room coordinates:

```text
x: 0.0 .. 1.0
y: 0.0 .. 1.0
```

or an explicit fixed logical room canvas mapped into the current viewport.

The mapping layer must exclude UI chrome such as title bar, command bar, status bar, and decorator panel itself.

Resizing the Desktop Buddy window must preserve relative room placement.

## 6.2 Free mouse placement

Default placement is free pointer positioning.

During ghost placement:

- ghost follows the pointer in room coordinates;
- invalid areas visibly reject placement;
- ghost is semi-transparent or otherwise visually distinguished;
- UI regions cannot receive placement;
- placement does not click through into buddy tools or grab behavior;
- the status bar explains why a location is invalid when applicable.

## 6.3 Optional grid snap

`Snap to grid` defaults to **off**.

When enabled:

- canonical position is quantized before validation;
- the preview immediately shows the snapped location;
- grid size is selected from a bounded list;
- the selected grid preference is a machine/editor preference, not part of the room layout.

Suggested logical options:

- Fine
- Medium
- Large

The UI may visually label these as familiar ratios or sizes, but persistence should not depend on translated labels.

## 6.4 Anchor kinds

Launch anchor kinds:

- `Floor`
- `Wall`
- `RoomSurface`

Mapping:

- Lamps: Floor
- Sofas: Floor
- Tables: Floor
- Plants: Floor
- Paintings: Wall
- Wallpapers: RoomSurface / wall skin

Invalid anchors are rejected before any purchase is reserved.

---

## 7. Rotation

Rotation is supported at launch, but it is definition-driven.

Each definition declares:

```text
AllowsRotation
RotationStepDegrees
```

Recommended defaults:

- freestanding floor furniture: 90-degree steps;
- paintings: no rotation unless specifically authored;
- wallpaper: no rotation;
- circular/symmetric objects may visually rotate without effect but should not expose a useless control.

The `Rotate` button advances by the definition's approved step and wraps at 360 degrees.

Rotation must never alter collision or physics in the first release because default decorations are visual-only.

---

## 8. Wallpaper category behavior

Wallpapers are deliberately different from `Paint Background`.

A wallpaper definition is a trusted project-owned room-surface visual preset.

Rules:

- exactly one wallpaper placement may occupy the wallpaper slot for a room/profile;
- selecting wallpaper previews the preset on the room wall;
- applying a new wallpaper is treated as one purchased room-surface instance;
- the item price is reserved once when the new wallpaper is staged;
- replacing a staged wallpaper removes its pending cost before applying the new one;
- replacing an already saved wallpaper follows the same sell/refund policy as other placed decor unless a later economy decision defines wallpaper-specific behavior;
- wallpaper does not modify the separate free-painted background data;
- if Paint Background data and wallpaper coexist, render ordering is explicit: base background -> painted background -> wallpaper overlay according to the environment profile's documented rule.

Recommended initial rule: wallpaper replaces/hides the visible wall treatment while selected but does not destroy the stored painted-background preset, so removing wallpaper restores the prior painted wall.

---

## 9. Rendering and runtime behavior

## 9.1 Visual-only default

Default placed decorations render through a dedicated environment visual layer, not as physics bodies.

Suitable engine representation may be:

- Sprite2D / Node2D;
- trusted scene containing only visual nodes;
- lightweight render presenter generated from `PlacedDecoration` data.

Do not instantiate generic RigidBody2D nodes for launch decorations.

## 9.2 Render bands

Use trusted bounded bands:

```text
Background
Wallpaper
WallDecoration
BehindBuddyFloor
BuddyPlane
FrontDecoration
UI
```

Launch definitions select their approved band. The user cannot arbitrarily reorder them in v1.

Examples:

- Wallpaper -> Wallpaper
- Painting -> WallDecoration
- Sofa/Table/Plant/Lamp -> BehindBuddyFloor by default

Specific project-authored definitions may use `FrontDecoration` when visually required.

## 9.3 Future interactivity seam

Do not make the base decoration class a universal gameplay object.

Instead define optional trusted capabilities, for example:

```text
IBuddySitTarget
IBuddyRestTarget
IBuddyWatchTarget
IDecorationToggleTarget
```

A sofa may later become a sit/rest target, and a lamp may later toggle, but this is authored per definition and remains absent from generic v1 content.

---

## 10. Input ownership

Opening Environment Decorator enters a deliberate room-edit input mode.

While active:

- decorator UI owns clicks inside its bounds;
- room-placement clicks are routed to the decorator placement controller;
- normal tool attacks, grabbing, shooting, and item use are suppressed;
- the buddy may remain visually active, but player gameplay input cannot leak through;
- Escape follows editor priority: cancel ghost -> clear selected placed object -> prompt/exit dirty decorator session;
- closing the decorator while dirty uses the shared Win98 Save / Discard / Continue Editing modal style.

The editor must remain recoverable in windowed/full interaction modes and must not break click-through restoration after closing.

---

## 11. Persistence

## 11.1 Environment profile

Persist decorations as part of the existing planned environment profile:

```text
EnvironmentProfile
├─ background reference/data
├─ wallpaper slot / placement
└─ placed decorations[]
```

Use a schema version from the first implementation.

## 11.2 Atomicity

Room edits must follow the project's existing atomic-save discipline:

1. validate complete staged layout;
2. validate pending economy delta;
3. write a recoverable pending transaction marker if two persistence roots are involved;
4. persist the environment snapshot;
5. persist the economy/progress mutation;
6. clear transaction marker only when both are durable;
7. recover deterministically on next boot if interrupted.

If environment layout is ultimately stored in the same durable progress aggregate, use that single atomic write instead of inventing a second journal.

## 11.3 Migration

Existing saves receive:

- default/empty environment layout;
- no placed decorations;
- no wallpaper placement;
- unchanged player funds.

Unknown future decoration definition IDs are preserved as unresolved records where safe but omitted from rendering until their definition returns.

---

## 12. UI behavior from the mockup

The following visible behaviors are required.

### Catalogue area

- title follows selected category, e.g. `Catalog - Lamps`;
- selected card gets a strong Win98 selection outline;
- price remains visible on every item card;
- scroll bar follows the shared Win98 skin;
- page arrows are shown only if paging is retained;
- unavailable/hidden content is not shown.

### Placement controls

The lower control group contains:

- selected item preview;
- concise pointer instruction (`Left-click to place` while ghosting);
- `Snap to grid` checkbox;
- grid-size selector visible only when snapping is enabled or kept disabled in-place if that avoids layout jump;
- Place;
- Rotate when valid for current selection;
- Sell only for an existing placed instance;
- Cancel.

### Budget panel

Show:

```text
Available Funds
Item Cost
After Purchase
```

When selecting an existing object, replace `After Purchase` with a meaningful sell/refund projection instead of showing irrelevant purchase copy.

Affordability must be communicated through text/state, not color alone.

---

## 13. Initial content slice

Do not attempt a large catalogue before placement is proven.

Recommended first implementation content:

### Lamps

- Classic Floor Lamp
- one alternate lamp

### Sofas

- one two-seat sofa
- one armchair or compact sofa

### Paintings

- one landscape
- one abstract/project-themed image

### Wallpapers

- one plain pattern
- one playful pattern

### Plants

- one floor plant
- one small potted plant

### Tables

- one side table
- one small desk/table

This gives at least two entries per category without requiring dozens of assets before the editor itself is validated.

Every asset must be original clean-room art.

---

## 14. Implementation slices

## ED0 — Domain model and economy contract

Deliver:

- `DecorationCategory`;
- definition IDs and immutable definition model;
- placed-instance model;
- anchor and rotation policies;
- per-instance cost calculations;
- staged placement/sell economy delta;
- validation and migration rules.

Tests:

- each placement charges exactly once;
- selecting/previewing charges zero;
- second identical instance charges again;
- moving/rotating charges zero;
- staged sell reverses the correct amount;
- unaffordable placement rejected;
- negative projected balance impossible.

## ED1 — Trusted catalogue and renderer

Deliver:

- project-authored definition resources;
- six launch categories;
- lightweight visual presenter;
- bounded render bands;
- unresolved-definition fallback behavior;
- first vertical-slice assets.

Tests:

- stable ID resolution;
- hidden content excluded;
- visual-only definitions instantiate no physics body;
- category filtering;
- render band enforcement.

## ED2 — Environment edit session and persistence

Deliver:

- working-copy room session;
- dirty tracking;
- staged purchase/refund delta;
- save/discard handling;
- atomic/recoverable environment + economy commit;
- restart restoration.

Tests:

- save/reload pixel-independent placement data round trip;
- cancel restores exact original layout and funds;
- interrupted transaction recovery;
- unknown IDs survive safely;
- existing save migration.

## ED3 — Placement engine

Deliver:

- canonical coordinate mapping;
- free mouse ghost placement;
- anchor validation;
- optional grid snap;
- rotation policy;
- selected placed-instance editing;
- sell flow.

Tests:

- resize mapping;
- invalid UI-area placement rejection;
- floor/wall anchor validation;
- grid quantization;
- rotation wrapping;
- repeated placements generate unique instance IDs.

## ED4 — Win98 Environment Decorator UI

Deliver:

- in-scene decorator window;
- category tabs;
- catalogue cards;
- selection preview;
- placement controls;
- budget projection;
- status help;
- keyboard focus/navigation;
- dirty-close modal integration.

Tests:

- focus traversal;
- selected/disabled states;
- input ownership;
- no gameplay click leakage;
- no detached native menu window.

## ED5 — Wallpaper slot

Deliver:

- wallpaper catalogue behavior;
- one active wallpaper slot per environment profile;
- preview/apply/replace flow;
- render ordering with stored Paint Background state;
- save/reload restoration.

Tests:

- wallpaper cannot duplicate into multiple room-surface slots;
- replacing staged wallpaper does not double-charge;
- removing wallpaper restores underlying painted background state;
- Paint Background document is not mutated by wallpaper operations.

## ED6 — Owner closure pass

Deliver:

- 100/125/150/200% DPI checks;
- minimum/default/maximized window checks;
- catalogue density tuning;
- pointer feel tuning;
- snap-grid feel tuning;
- item-price/readability pass;
- all six launch categories with real content;
- complete regression run.

---

## 15. Automated scenario plan

Recommended scenario IDs:

```text
environment_decor_catalogue
environment_decor_purchase_per_instance
environment_decor_cancel_transaction
environment_decor_free_placement
environment_decor_grid_snap
environment_decor_rotation
environment_decor_sell
environment_decor_resize_mapping
environment_decor_wallpaper_slot
environment_decor_input_ownership
environment_decor_restart_restore
```

Recommended end-to-end journey:

```text
environment_decorator_room_build
```

Journey outline:

1. start from known funds and default room;
2. open Environment Decorator;
3. place one lamp;
4. place a second identical lamp and verify a second charge;
5. place sofa, painting, plant, table;
6. apply wallpaper;
7. rotate a rotatable item;
8. enable snap and place another item;
9. sell one placed object;
10. save;
11. restart;
12. verify layout, rotation, wallpaper, and final funds;
13. reopen editor, stage changes, discard them, and verify saved state remains unchanged.

---

## 16. Manual owner test matrix

### Core placement

- selection does not spend money;
- Place enters clear ghost mode;
- free placement feels direct;
- UI does not steal room clicks incorrectly;
- invalid placement is understandable;
- two identical objects each cost money;
- moving/rotating existing objects is free.

### Snap

- off by default;
- toggling on visibly changes ghost position;
- different grid sizes are understandable;
- snap preference survives according to editor-preference policy;
- room coordinates remain correct after resize.

### Sell

- selected existing object clearly indicates Sell mode;
- refund projection is shown before commit;
- newly staged object cannot be sold for profit;
- cancel/discard restores original room and wallet.

### Wallpapers

- wallpaper is visually distinct from Paint Background;
- applying a wallpaper never destroys custom painted background data;
- removing/replacing wallpaper behaves predictably.

### Input and shell

- buddy/tool input does not trigger during placement;
- command bar remains recoverable;
- closing and reopening does not lose input mode;
- decorator works in windowed and deliberate full-interaction modes.

### DPI / sizes

Test at:

- 100%
- 125%
- 150%
- 200%

and at:

- minimum supported window;
- typical restored size;
- maximized/full interaction size.

---

## 17. Future nice-to-have plan — explicitly not launch scope

After v1 is stable, consider a second editing pass with:

### Resize

- definition-controlled min/max visual scale;
- optional aspect lock;
- no physics implications unless an interactive definition explicitly supports it;
- persistence schema migration for scale.

### Mirror

- horizontal mirror for approved asymmetric visuals;
- definition flag controls availability;
- mirrored state stored as a boolean.

### Layer forward/backward

- bounded user-controlled ordering only within a definition's approved render band;
- `Bring Forward` / `Send Backward` controls;
- never permit decor above UI;
- deterministic stable ordering after reload.

### Richer rotation

Launch already supports rotation. A future enhancement may add:

- more granular rotation steps;
- mouse-wheel or drag rotation affordance;
- per-definition angle bounds;
- angle snapping.

### Additional later possibilities

- duplicate placed instance;
- multi-select;
- box selection;
- copy/paste room objects;
- keyboard nudging;
- alignment/distribution helpers;
- saved room templates;
- authored furniture interactions;
- animated/toggleable decor;
- Steam/shared room packages after the local format is stable.

None of these should be partially exposed in the launch UI.

---

## 18. Definition of done

The Environment Decorator v1 is complete when:

- six real categories ship: Lamps, Sofas, Paintings, Wallpapers, Plants, Tables;
- each individual placed physical decoration costs its catalogue price once;
- duplicate instances require duplicate payment;
- previews never spend currency;
- launch decorations are visually persistent and non-physical by default;
- free mouse placement works across supported window sizes;
- optional grid snap works and is disabled by default;
- position and definition-approved rotation persist after restart;
- Sell is safe and cannot create money exploits;
- wallpaper presets remain separate from Paint Background data;
- staged room/economy changes save or discard coherently;
- the editor uses the shared Win98 application UI and remains in-scene;
- normal gameplay input cannot leak into placement mode;
- resize/DPI behavior passes the Windows matrix;
- automated domain, headless, and journey tests pass;
- the owner accepts catalogue density, placement feel, budget feedback, and final Win98 presentation.
