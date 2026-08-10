# Desktop Buddy — Environment Decorator Implementation Plan

Status: **Owner UX decisions locked — detailed implementation plan**  
Branch baseline: `main`  
Depends on:

- `docs/POST_WIN98_ENVIRONMENT_CUSTOMIZATION_PLAN.md`
- `docs/WIN98_UI_UX_REVAMP_PLAN.md`
- the accepted Win98 shell / command-bar UI foundation
- existing economy ledger and progress persistence
- existing window/input ownership bridge

This plan refines the room-decoration portion of the broader environment roadmap. It does **not** replace the separate `Paint Background` editor. The Environment Decorator is specifically for buying, placing, rotating, and persisting room decorations and wallpaper presets.

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
```

A new placement is allowed only when `projectedFunds >= item.Price` after all currently staged changes.

The budget panel shows:

- Available Funds;
- Item Cost;
- After Purchase / Projected Funds.

Negative balances are never permitted.

## 4.4 Purchase finality

Environment decorations have no Sell or refund flow. Placement remains staged until the player
confirms Save, so cancelling the uncommitted edit restores its reserved cost. After Save, the
purchase is final: players earn credits again for later items rather than reversing purchases.

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
- replacing an already saved wallpaper is a new final purchase and does not refund the previous wallpaper;
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
- Cancel.

### Budget panel

Show:

```text
Available Funds
Item Cost
After Purchase
```

When selecting an existing object, hide purchase projections that are no longer relevant.

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
- staged placement economy delta;
- validation and migration rules.

Tests:

- each placement charges exactly once;
- selecting/previewing charges zero;
- second identical instance charges again;
- moving/rotating charges zero;
- cancelling an uncommitted placement restores its reserved cost;
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
- staged purchase delta;
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
9. save;
10. restart;
11. verify layout, rotation, wallpaper, and final funds;
12. reopen editor, stage changes, discard them, and verify saved state remains unchanged.

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

### Purchase finality

- no Sell or refund action is exposed;
- cancelling before Save restores uncommitted placement costs;
- saved purchases remain final;
- cancel/discard restores the original room and wallet.

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
- no Sell/refund route exists and cancelling before Save restores only uncommitted costs;
- wallpaper presets remain separate from Paint Background data;
- staged room/economy changes save or discard coherently;
- the editor uses the shared Win98 application UI and remains in-scene;
- normal gameplay input cannot leak into placement mode;
- resize/DPI behavior passes the Windows matrix;
- automated domain, headless, and journey tests pass;
- the owner accepts catalogue density, placement feel, budget feedback, and final Win98 presentation.

---

## 19. Owner refinement — current demo paint parity and toolbar polish (2026-08-10)

This section is **authoritative for the current `environment-customization` pass**. Where it conflicts with an older deferred-scope statement in this document or the broader post-Win98 plan, this section wins. It records work promoted into the demo pass after the first Paint Background vertical slice was implemented.

The current branch already has two deliberately different 512×512 paint surfaces:

- Paint Buddy: `PaintSurface` / `PaintWorkspace`, whose U coordinate wraps around the buddy's UV seam and whose undo history stores bounded dirty patches;
- Paint Background: `EnvironmentCanvas`, which clamps at room edges and owns fill plus shape rasterisation with live preview.

Keep those surface models separate. Their edge semantics and gesture history are meaningfully different. Reuse only small **engine-free paint algorithms** whose contract is genuinely identical between the two editors.

### 19.1 Scope promoted into the current demo pass

The following are no longer deferred:

1. **Spray / Airbrush** in Paint Buddy and Paint Background.
2. **Curved Line** in Paint Buddy and Paint Background.
3. **Paint-toolbar icon refinement** near the end of the pass, using generated original placeholder icons until owner-provided final icons replace them.
4. A visible Paint Background brush-size control, because Spray and Curved Line must use the same authored brush diameter as the normal brush.

The current demo remains a **single local environment**. These items stay outside the demo and are reserved for the full release:

- multiple local room/environment profiles;
- sharing complete room configurations through Steam;
- authored furniture interactions with the buddy.

Do not expose dead profile/share/interaction UI in the demo. Keep persistence evolvable so the singleton environment can later migrate into the default room profile without changing the meaning of existing placements or the saved `environment/background.png` paint.

### 19.2 Clean-room reference rule

The behavior target is the familiar classic desktop Paint workflow, but implementation must be clean-room:

- do not copy Microsoft source code, binary resources, icons, exact pixel art, or other proprietary assets;
- reproduce only generic tool behavior and interaction conventions;
- all code and placeholder icons are original project work.

### 19.3 Spray / Airbrush behavior contract

Spray is a first-class paint mutation tool in both editors.

Required behavior:

- the tool distributes sparse selected-color pixels/dots throughout a circular spray envelope rather than painting a solid disk;
- the envelope diameter is **exactly the current Brush Size**. There is no separate Spray size, radius, pressure, flow, density, hardness, or opacity control in the demo;
- holding the pointer still continues spraying at a bounded time cadence;
- moving slowly naturally produces denser coverage because more spray pulses land along the path;
- moving quickly produces lighter coverage while still avoiding obvious large temporal gaps;
- each accepted spray dot uses the currently selected opaque paint color;
- one press/hold/release gesture is one Undo action;
- switching away from Spray or closing the editor must finish/cancel the active gesture through the same safe gesture boundary used by Brush;
- Spray never changes persistence schema: it only changes existing paint pixels.

#### Deterministic spray sampler

For testability, separate random point generation from canvas mutation. A small engine-free helper such as `SprayPattern` / `SpraySampler` may be shared by both paint domains.

Recommended deterministic sampling for one pulse:

```text
angle = 2π * random01()
radius = envelopeRadius * sqrt(random01())
point = center + (cos(angle), sin(angle)) * radius
```

The square-root radius produces an approximately uniform point density across the disk instead of clustering at the center.

Production may seed a gesture from a monotonic/random gesture seed; tests must be able to inject/fix the seed. Do not couple spray randomness to gameplay RNG or buddy autonomy.

Pulse count should scale with brush-envelope area so increasing Brush Size feels like a larger spray can rather than the same few dots spread over a huge disk. Pulse cadence and density constants are tuning data to be reviewed at the owner feel gate; they are not new player-facing sliders.

#### Paint Buddy spray integration

Primary files/seams:

```text
domain/DesktopBuddy.Domain/Painting/PaintTypes.cs
domain/DesktopBuddy.Domain/Painting/PaintSurface.cs
domain/DesktopBuddy.Domain/Painting/PaintWorkspace.cs
src/CharacterEditor/PaintCanvasControl.cs
src/CharacterEditor/CharacterEditorHost.Painting.cs
src/CharacterEditor/CharacterEditorHost.Win98PaintLayout.cs
src/UI/Win98/Win98PaintToolBootstrap.cs
```

Rules:

- add a real Spray paint-tool value; do not model Spray as a UI-only mode;
- sparse writes on `PaintSurface` preserve the existing buddy edge contract: U wraps, V clamps;
- dirty bounds must include every possible dot in the spray envelope so patch Undo can restore all modified pixels exactly;
- `PaintCanvasControl` already performs held-stroke `_Process` resampling. Spray should use a bounded time/pulse accumulator so a stationary pointer continues to spray without depending on mouse-motion events;
- surface misses and part changes must continue to use the existing anti-smear/bridge rules;
- the brush cursor may show the same size envelope as Brush, with a visibly different center/texture indicator only if useful.

Final placeholder tool-grid order:

```text
Brush  | Eraser
Spray  | Pick Color
Curve  | Hand/Pan
```

This makes **Spray directly below Brush** as required. The existing Brush Size row remains below the picker and controls Brush, Spray, and Curved Line width from one source of truth.

#### Paint Background spray integration

Primary files/seams:

```text
domain/DesktopBuddy.Domain/Environment/EnvironmentCanvas.cs
src/Environment/EnvironmentBackgroundEditor.cs
```

Rules:

- add `Spray` to `EnvironmentPaintTool`;
- use the environment canvas's existing `BrushDiameter`; X/Y continue to clamp rather than wrap;
- repeated pulses while held must work even if the pointer is stationary;
- add a visible `−  [size]  +` brush-size control to Paint Background and keep its value synchronized with `EnvironmentCanvas.BrushDiameter`;
- the same size value controls Brush, Spray, Straight Line and Curved Line thickness. Eraser may continue using that same diameter unless a later owner decision separates it;
- place the **Spray button directly below Brush** in the Paint Background tool column.

Recommended Paint Background tool grouping before icon conversion:

```text
Tools                    Brush/utility
Brush                    Eraser
Spray                    Pick Color
Fill Color               Size:  -  N  +
Shapes                   Undo
```

Exact column packing may adjust for DPI, but Brush → Spray vertical adjacency is locked.

### 19.4 Curved Line behavior contract

Curved Line is a separate shape/tool, not a mode of Straight Line.

The interaction follows the classic multi-stage curve convention:

1. first drag creates the straight baseline from endpoint A to endpoint B;
2. first subsequent drag bends the line at the first chosen location;
3. second subsequent drag applies the second bend and commits the final curve.

A second bend gesture with no meaningful displacement may finalize a one-bend result. Tool switch, Escape, right-click cancel, editor close, or invalid state must restore the pre-curve pixels if the curve has not committed.

The complete baseline + first bend + second bend is **one Undo action**.

Curved Line uses:

- selected paint color;
- the current shared Brush Size as stroke width;
- live preview at every stage;
- no new persistence fields.

#### Curve geometry helper

A small engine-free clean-room helper such as `ClassicCurveGeometry` may be shared by both editors. It should produce a sampled cubic Bézier/polyline; surface-specific rasterisation remains separate.

Recommended representation:

```text
P0 = baseline start
P3 = baseline end
C1 = initial point one-third along baseline
C2 = initial point two-thirds along baseline
```

For each bend gesture:

- resolve the closest safe parameter `t` on the currently previewed curve to the drag-start point;
- use the drag-release point as the requested bend target;
- after bend one, solve/adjust one control point while preserving the other baseline control;
- after bend two, solve the two control points from the two bend constraints when the system is well-conditioned;
- clamp `t` away from degenerate 0/1 endpoints and use a deterministic safe fallback for near-singular/zero-length baselines;
- sample the resulting curve densely enough relative to Brush Size that rasterisation cannot leave visible holes.

The helper returns geometry only. It must know nothing about Godot, buddy parts, UV wrapping, environment edges, history, or UI.

#### Paint Background curve transaction

`EnvironmentCanvas` already restores `_strokeBase` before redrawing drag-shape previews. Extend that concept into an explicit compound curve state rather than committing each bend as a separate edit.

Required state conceptually:

```text
Idle
BaselineDragging
AwaitFirstBend
FirstBendDragging
AwaitSecondBend
SecondBendDragging
```

Rules:

- capture the canvas baseline once at curve start;
- every preview restores that baseline, rerasterizes the current curve, then presents it;
- do not push multiple whole-image Undo snapshots for the baseline/bends;
- commit one history entry only when the curve finalizes;
- cancel restores the captured baseline exactly and produces no Undo entry;
- add `Curved Line` as a distinct entry in the existing Paint Background **Shapes** popup after Straight Line.

#### Paint Buddy curve transaction

Paint Buddy does not currently expose the environment shape menu, so Curved Line should be a dedicated **Curve** button in the Buddy paint tool grid rather than widening this scope to Square/Circle/Line parity.

The curve is authored in preview/canvas space, then sampled through the existing `PaintCanvasControl` mapping pipeline:

- each sampled screen point maps to a trusted `PaintHit?`;
- contiguous hits on the same part may stroke between samples;
- misses break continuity;
- a transition to another body part starts a new contiguous segment rather than drawing through empty space;
- existing UV seam wrapping and bridge-distance protection stay authoritative.

Because Buddy paint uses patch history, add a preview transaction seam to `PaintWorkspace` rather than repeatedly pushing normal gestures. It must be able to:

1. capture the clean pre-curve pixels for every affected part/dirty rectangle;
2. restore the previous preview before rerasterizing a changed curve;
3. expand captured clean bounds safely as a new preview touches additional areas/parts;
4. finalize all changed part patches into exactly one `PaintCommand`;
5. cancel back to the exact clean baseline with no command.

Do not snapshot all six 512×512 surfaces per mouse move; preserve the existing bounded paint-memory discipline.

### 19.5 Toolbar icon refinement — current pass, late slice

The existing textual labels (`Brush`, `Pick`, `Hand`, etc.) are temporary representation. Near the end of this pass, after tool behavior and ordering are stable, replace paint-tool toolbar words with **original generated placeholder icons**.

The owner will provide replacement artwork later, so the code must make icon replacement asset-only.

#### Icon architecture

Use a presentation-only mapping such as:

```text
semantic tool/action ID -> trusted placeholder Texture2D
```

Suggested asset root:

```text
assets/ui/paint_tools/placeholder/
```

Generate original small late-1990s/pixel-style placeholders for the controls actually shipped, including as applicable:

- Brush
- Spray/Airbrush
- Eraser
- Pick Color
- Hand/Pan
- Fill
- Shapes
- Straight Line
- Curved Line
- Square
- Circle
- Undo / Redo
- zoom / rotate actions where they are part of the compact tool rail

Do not recreate Microsoft's Paint icons. The placeholders need only communicate the tool clearly in the project's Win98 visual language.

Rules:

- behavior and automated tests identify controls by stable node/tool IDs, never visible button text or icon pixels;
- tooltips, status-bar help, accessible names and keyboard shortcuts remain textual;
- pressed/selected state comes from the Win98 button chrome, not from requiring alternate selected-icon art;
- keep a text fallback for missing icon resources in development builds;
- icon-only conversion applies to compact **tool/action controls**. Keep semantic actions such as Save, Cancel, Reset, confirmation buttons and descriptive popup entries textual unless separately approved;
- the Paint Background Shapes popup may remain text-based while its toolbar launcher becomes an icon, which preserves discoverability and avoids ambiguous tiny shape glyphs.

### 19.6 Implementation order added before ED6 closure

Execute these slices after the existing Paint Background/decorator functionality is stable and **before ED6 owner closure**:

#### PAINT-R0 — shared clean-room algorithm primitives

Deliver:

- deterministic uniform-disk spray sampler;
- cubic curve geometry/sampling helper;
- no Godot dependency;
- no shared surface/history abstraction.

Tests:

- deterministic result for fixed seed;
- every spray point stays within radius;
- area-scaled density behaves monotonically;
- curve baseline is exactly straight before bends;
- one/two bend constraints are stable;
- zero-length/near-end/near-singular inputs fail safely and deterministically.

#### PAINT-R1 — Spray in Paint Buddy

Deliver:

- `PaintTool.Spray`;
- sparse `PaintSurface` mutation with U wrap/V clamp;
- held/stationary pulse handling;
- one-gesture patch Undo;
- Spray button directly below Brush.

Tests:

- min/default/max Brush Size changes spray envelope;
- stationary hold accumulates density;
- slow traversal produces denser coverage than fast traversal for equal path length/time model;
- seam-crossing dots wrap correctly;
- no vertical wrap;
- miss/part transitions never smear;
- one Undo restores the exact pre-spray pixels.

#### PAINT-R2 — Spray + brush-size controls in Paint Background

Deliver:

- `EnvironmentPaintTool.Spray`;
- clamped spray rasterisation;
- held/stationary pulse handling;
- visible shared Brush Size controls;
- Spray directly below Brush.

Tests:

- environment edges clamp with no opposite-edge dots;
- Brush/Spray report and consume the same diameter;
- larger diameter expands the envelope;
- one Undo restores exact pixels.

#### PAINT-R3 — Curved Line in Paint Background

Deliver:

- Curved Line Shapes entry;
- baseline/first-bend/second-bend state machine;
- live preview from one captured baseline;
- selected color + shared Brush Size stroke;
- one final Undo action;
- Escape/right-click/tool-switch cancellation.

Tests:

- first-stage preview matches Straight Line rasterisation;
- first and second bends update preview deterministically;
- cancel restores byte-identical baseline;
- final curve is one Undo step;
- edge clipping never wraps.

#### PAINT-R4 — Curved Line in Paint Buddy

Deliver:

- Curve tool button;
- canvas-space curve authoring mapped through existing buddy surface hit testing;
- patch-based multi-stage preview transaction;
- one final Undo command;
- cancel with exact restore.

Tests:

- curve on one part remains continuous;
- crossing a silhouette miss breaks the stroke;
- crossing to another body part cannot bridge through space;
- wrapped U seam remains correct;
- one Undo restores every affected part exactly;
- memory remains inside the existing paint editing/undo budget.

#### PAINT-R5 — cross-editor UI parity and shortcuts/status

Deliver:

- locked tool ordering;
- coherent tooltips/status help;
- one explicit Spray shortcut and one Curve shortcut per editor that do not collide with existing shortcuts;
- brush-size status reflects the actual shared value;
- pending Curve state is visibly understandable and safely cancelled on mode changes.

Do not bind tests to the visible English labels, because PAINT-R6 replaces them with icons.

#### PAINT-R6 — generated placeholder icon toolbar

Deliver:

- original placeholder icon set;
- presentation mapping used by both paint editors where semantics overlap;
- icon-only compact tool buttons with text tooltips/accessibility/fallbacks;
- no behavioral changes in this slice.

Tests/headless checks:

- every visible tool resolves an icon or development fallback;
- icon conversion does not change tool IDs, ordering, focus traversal or shortcuts;
- no missing icon creates a nonfunctional button.

#### PAINT-R7 — owner/manual paint closure

Manual verification matrix:

- Spray on Paint Buddy and Paint Background at minimum/default/maximum Brush Size;
- stationary spray hold;
- slow versus fast spray traversal;
- Paint Buddy UV seam and top/bottom boundaries;
- Paint Background room edges;
- Curved Line with zero, one and two meaningful bends;
- curve cancellation at each intermediate stage;
- Undo immediately after Spray and Curved Line;
- Buddy curve crossing multiple visible body parts;
- toolbar icon readability and focus at 100%, 125%, 150% and 200% Windows DPI;
- minimum/default/maximized editor sizes;
- final generated placeholder icons are acceptable as temporary art until owner replacements arrive.

### 19.7 Demo/full-release gate after this refinement

After PAINT-R7 and the remaining ED6 demo closure work, **do not continue directly into room profiles/platform sharing/furniture behavior** on the assumption that they are part of this pass.

Full-release follow-up gates are explicitly:

```text
RELEASE-ENV1  Multiple local room/environment profiles
RELEASE-ENV2  Safe complete-room sharing format + Steam publishing/downloading integration
RELEASE-ENV3  Authored buddy/furniture interactions
```

Recommended dependency order:

1. stabilize the singleton environment save and demo UI first;
2. generalize that known-good singleton into multiple named local profiles without changing placed-instance semantics;
3. define/version/validate a complete-room package around the stable profile representation, then wrap it with Steam integration;
4. add narrow trusted furniture interaction capabilities (`IBuddySitTarget`, `IBuddyRestTarget`, `IBuddyWatchTarget`, `IDecorationToggleTarget`) only after the visual/persistence representation is stable.

The demo may leave narrow migration seams for these later steps, but no speculative profile manager, Steam adapter, or universal furniture behavior framework should be built during the current environment pass.
