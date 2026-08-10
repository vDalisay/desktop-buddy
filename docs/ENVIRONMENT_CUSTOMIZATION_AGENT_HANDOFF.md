# Environment Customization — Parallel Agent Handoff

Branch: `environment-customization`  
Exact shared base: `3a789e1b2ef6c31be562d6aeb89e725649789ae9`  
Parallel sibling: `buddy-studio`  

Read before implementation:

1. `docs/CUSTOMIZATION_PARALLEL_IMPLEMENTATION_FOUNDATION.md` — authoritative shared/frozen boundaries.
2. `docs/ENVIRONMENT_DECORATOR_IMPLEMENTATION_PLAN.md` — Environment Decorator product/architecture plan.
3. `docs/POST_WIN98_ENVIRONMENT_CUSTOMIZATION_PLAN.md` — broader Paint Background + room roadmap.

Do not implement Buddy Studio work on this branch.

---

## 1. Already provided by the shared base

Do **not** recreate or locally fork these systems:

- extensible top-level `Customize` dropdown;
- stable `CustomizeCommandIds.PaintBackground` route ID;
- `EnvironmentCustomizationBootstrap` reserved autoload composition root;
- `Win98CategoryStrip`;
- `Win98CatalogGrid`;
- `Win98ValuePanel`;
- `Win98MenuStyle`;
- `Win98ThemeFactory`;
- `Win98Dialog`;
- existing credits formatting through `ContentDisplayName.Credits`;
- existing authoritative wallet/economy state.

`Paint Background` should only register into Customize after a functional editor exists. Environment Decorator remains reached through its decor/shop flow under the locked product decision; do not add it as a fourth Customize command.

---

## 2. Branch ownership

Primary branch-owned paths:

```text
domain/DesktopBuddy.Domain/Environment/**
src/Environment/**
data/environment/**
assets/environment/**
tests/DesktopBuddy.Domain.Tests/Environment/**
src/Testing/Environment*
```

This branch also owns the Environment-specific persistence integration when required:

```text
domain/DesktopBuddy.Domain/Persistence/ProgressSave.cs
src/Persistence/SaveCoordinator.cs
src/App/ProgressReset.cs
src/App/RunContext.cs
```

Prefer composing through `src/Environment/EnvironmentCustomizationBootstrap.cs`; avoid editing `project.godot` or `src/App/Bootstrap.cs` because the shared base already reserved the autoload.

Do not modify normal feature work in:

```text
domain/DesktopBuddy.Domain/Characters/**
src/CharacterEditor/BuddyStudio/**
src/Buddy/Presentation3D/Characters/**
data/cosmetics/**
assets/cosmetics/**
data/catalogue/launch_catalogue.tres
```

---

## 3. Frozen shared files

Do not independently change these while the sibling branch is active:

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

If a genuinely generic capability is missing, isolate it as a tiny shared-foundation patch for both branches rather than evolving the shared component only here.

---

## 4. Economy rule — do not blur this boundary

Decorations are **per-instance purchases**. They are not permanent catalogue unlocks.

Correct model:

```text
select/preview definition -> $0
place staged instance     -> reserve that instance's price
place second same item    -> reserve the price again
move/rotate instance      -> $0 additional
sell                       -> staged cancellation/refund delta
Save/Done                  -> commit layout + wallet delta
Cancel                     -> restore baseline layout / discard staged delta
```

Do not model furniture as `CatalogueEntryKind.Cosmetic`. Do not call `EconomyService.Purchase` once and then allow unlimited copies.

The UI may reuse shared price/card/value presentation, but the transaction model is Environment-owned.

---

## 5. Persistence rule

Persist the durable room/environment layout in the **same core progress aggregate as the wallet** so a layout purchase can become one atomic `SaveProgressAsync` write rather than a cross-file journal.

Recommended pattern follows the existing Work progress architecture:

```text
EnvironmentProgressState
EnvironmentProgressSnapshot
EnvironmentProgressSave
```

Then extend:

- `ProgressSave` with Environment data;
- `SaveCoordinator` revision/dirty capture;
- run composition with one Environment state instance;
- `ProgressReset` so environment progression/layout returns to the default environment while local UI/window preferences survive.

Keep large binary background assets out of the core JSON if needed later, but the semantic environment/layout and any financially purchased placed-instance truth must remain in the atomic progress aggregate.

---

## 6. Recommended implementation sequence

### ENV-0 — Domain and transaction core

Create engine-free:

- `DecorationCategory` (Lamp, Sofa, Painting, Wallpaper, Plant, Table);
- `DecorationDefinition` and stable ID type/rules;
- `PlacedDecoration` with unique instance ID;
- anchor/rotation/render-band policies;
- `EnvironmentLayout`;
- `EnvironmentProgressState/Snapshot`;
- `EnvironmentEditSession` with baseline + working copy + staged cost/refund calculation;
- validation and saturation/negative-balance protections.

First gate: pure C# tests prove two identical placed objects each cost money, moving/rotating costs nothing, cancel restores baseline, and unaffordable placement is rejected.

### ENV-1 — Atomic progress persistence

Integrate Environment state into one ProgressSave write and reset behavior. Prove save/reload and failed-write rollback before building substantial UI.

### ENV-2 — Trusted authored definitions + visual presenter

Add environment-only authored catalogue/registry under `data/environment` / `src/Environment`. Definitions contain domain-safe metadata; Godot resources map stable IDs to project-owned visuals. Generic launch decorations remain non-physical.

### ENV-3 — Paint Background vertical slice

Build the separate `Paint Background` editor/data flow. Once it has a real usable vertical slice, register `CustomizeCommandIds.PaintBackground` from `EnvironmentCustomizationBootstrap` using `Win98CommandBarBootstrap.RegisterCustomizeCommand`.

Do not expose a dead menu entry before this point.

### ENV-4 — Placement engine

Implement canonical room coordinate mapping, free pointer ghost placement, Floor/Wall/RoomSurface anchors, optional grid snap and definition-controlled rotation. Suppress normal gameplay tool input while placing.

### ENV-5 — Environment Decorator UI

Use shared:

- `Win98CategoryStrip` for Lamps/Sofas/Paintings/Wallpapers/Plants/Tables;
- `Win98CatalogGrid` for visual cards;
- `Win98ValuePanel` for Available Funds / Item Cost / Projected Funds;
- `Win98Dialog` for dirty-close/save-discard flows.

Feature code computes price, affordability, refund and placement state. Do not put those rules into the shared UI components.

### ENV-6 — Wallpaper + content + closure

Implement one wallpaper slot, layering against Paint Background, at least the agreed first real content slice, DPI/resizing/manual placement validation and regression scenarios from the detailed plan.

---

## 7. First concrete coding target

Start with **ENV-0**, not the visual editor.

The first PR/slice should compile and test an engine-independent edit transaction with no Godot scene dependency. A useful acceptance scenario is:

```text
starting balance = $250
empty layout
place lamp $75
place same lamp $75
move first lamp
rotate second lamp
stage plant $40
cancel session
=> committed balance/layout unchanged

repeat, Save/commit
=> 3 unique instances persisted
=> wallet delta = -$190 exactly once
```

After that invariant is solid, wire it into `ProgressSave`/`SaveCoordinator`.

---

## 8. Stop-and-coordinate triggers

Stop and raise a shared-foundation question instead of editing frozen files if you believe you need to:

- modify the Customize command bar directly;
- add another autoload in `project.godot`;
- add ownership/economy semantics to `Win98CatalogGrid`;
- add environment-specific fields to `Win98ValuePanel`;
- change generic Win98 theme/dialog behavior;
- modify Buddy Studio/character rendering code;
- represent decorations through the permanent cosmetic catalogue.

A feature-specific solution inside `src/Environment/**` is preferred unless the need is demonstrably generic.

---

## 9. Owner refinement added 2026-08-10 — execute before ENV-6 closure

The detailed implementation contract is now `docs/ENVIRONMENT_DECORATOR_IMPLEMENTATION_PLAN.md` **Section 19**. It promotes the following work into this active pass:

```text
PAINT-R0  shared engine-free spray + curve geometry helpers
PAINT-R1  Spray/Airbrush in Paint Buddy
PAINT-R2  Spray + shared brush-size controls in Paint Background
PAINT-R3  Curved Line in Paint Background
PAINT-R4  Curved Line in Paint Buddy
PAINT-R5  cross-editor tool ordering, status and shortcut parity
PAINT-R6  original generated placeholder toolbar icons
PAINT-R7  owner/manual paint closure
```

Treat these as current requirements, not future nice-to-haves. They run before the final ENV-6 closure gate.

### 9.1 Cross-domain edit exception for this pass

The owner explicitly requires Spray and Curved Line in **both** Paint Background and Paint Buddy. That necessarily touches the established Buddy painting subsystem even though normal Environment work avoids unrelated character code.

This exception is narrowly scoped to:

```text
domain/DesktopBuddy.Domain/Painting/**
src/CharacterEditor/PaintCanvasControl.cs
src/CharacterEditor/CharacterEditorHost.Painting.cs
src/CharacterEditor/CharacterEditorHost.Win98PaintLayout.cs
src/UI/Win98/Win98PaintToolBootstrap.cs
paint-specific tests/scenarios
```

Do not use this exception to modify Buddy Studio cosmetic/character rendering work. If the parallel `buddy-studio` branch has changed one of these exact paint files, coordinate/rebase before editing rather than overwriting sibling changes.

### 9.2 Architecture constraint

Do **not** merge `EnvironmentCanvas` and `PaintSurface` into a generalized canvas abstraction. The former clamps room edges and owns environment shapes; the latter wraps the buddy U seam and uses bounded patch history.

Only small pure helpers with identical semantics may be shared, specifically the deterministic spray point sampler and curve geometry sampler. Each surface remains responsible for rasterisation, edge behavior and Undo.

### 9.3 Locked UI placement

Paint Buddy placeholder ordering before icon conversion:

```text
Brush  | Eraser
Spray  | Pick Color
Curve  | Hand/Pan
```

Paint Background must place Spray directly below Brush and add a visible shared Brush Size control. The existing Shapes popup gains a distinct `Curved Line` entry. Brush Size is one source of truth for Brush, Spray and line/curve width; do not add a separate Spray-size control.

PAINT-R6 then replaces compact tool words with generated original placeholder icons while keeping text in tooltips/status/accessibility. The owner will provide final replacement icons later, so no behavior may depend on the placeholder artwork.

### 9.4 Explicit demo stop line

Do **not** continue into these after the current pass:

- multiple local room/environment profiles;
- Steam sharing/publishing/downloading of complete room configurations;
- buddy interactions with furniture.

Those three are full-release work. The demo ships the known-good singleton local environment and visual/non-physical decorations. Leave migration seams only; do not expose inactive UI or build speculative services for the release-only features.
