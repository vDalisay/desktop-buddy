# Desktop Buddy — Parallel Customization Foundation & Agent Boundaries

Status: **Shared foundation ready for parallel Environment + Buddy Studio implementation**  
Prepared after Work Mode completion and merge into `main` on 2026-08-08.  
Feature plans remain authoritative for product behavior:

- `docs/ENVIRONMENT_DECORATOR_IMPLEMENTATION_PLAN.md`
- `docs/POST_WIN98_ENVIRONMENT_CUSTOMIZATION_PLAN.md`
- `docs/BUDDY_STUDIO_CUSTOMIZATION_PLAN.md`

This document is authoritative for **parallel-development ownership, shared seams, and what is already implemented**. If an older plan tells an agent to create infrastructure listed here as already landed, this document wins for implementation sequencing.

---

## 1. Why this foundation exists

Environment customization and Buddy Studio can proceed in parallel, but they touch several adjacent systems:

- the top-level Win98 command bar;
- visual catalogue/category UI;
- prices and balance presentation;
- economy/purchase boundaries;
- progress persistence;
- application composition/autoloads;
- editor input ownership;
- shared theme/dialog/focus behavior.

The two features intentionally have **different domain semantics**. Parallel work is safe only if they reuse presentation infrastructure while keeping their business models separate.

The key rule is:

> **Share UI primitives and shell routing. Do not share Environment and Buddy Studio transaction/domain models.**

---

## 2. Repository baseline discovered during the reuse audit

### 2.1 Work Mode is already integrated

Work Mode has been merged to `main`. It also landed part of the Buddy Studio foundation because the first-entry Work glasses reward needed real cosmetic ownership/equipment state.

Do not recreate that foundation in either feature branch.

### 2.2 Character schema 3 already exists

The current character implementation already has:

- `CharacterDocumentPolicy.CurrentSchemaVersion = 3`;
- sequential `2 -> 3` migration;
- all twelve locked feature slots in `CharacterFeatureSet`:
  - Face
  - Hair
  - Eyebrows/Brows
  - Eyes
  - Nose
  - Mouth
  - Ears
  - Accessories/TorsoAccent
  - Glasses
  - Headwear
  - Tops
  - Shoes;
- `CharacterFeatureDocument.Colors`, the named-color-channel seam;
- source-compatibility aliases for old Brows/TorsoAccent call sites;
- default `glasses.work_classic` support.

**Important:** the schema is ahead of the renderer. `CharacterCompiler` / `CompiledCharacterAppearance` still compile only Eyes, Brows, Mouth and TorsoAccent/Accessories. Full Buddy Studio must widen compilation/rendering; it must **not** bump the character schema merely to add the twelve categories again.

### 2.3 Cosmetic commerce foundation already exists

The general catalogue already supports:

- `CatalogueEntryKind.Cosmetic`;
- cosmetic IDs under the `cosmetic.*` namespace;
- cosmetics being purchasable but never tool-selectable;
- permanent ownership through the existing unlocked-content set;
- purchase/debit through the existing `EconomyService.Purchase` path.

`ContentIds.CosmeticWorkGlasses` is currently the first concrete cosmetic ownership ID.

Buddy Studio must extend this path. It must not add a second wallet, second cosmetic ownership collection, or second purchase service.

### 2.4 Existing shared Win98 pieces are already reusable

Keep using:

- `Win98ThemeFactory` — canonical visual tokens/styles;
- `Win98Dialog` — in-scene modal/subwindow frame and blocker;
- `ContentDisplayName.Credits` — current player-facing money formatting;
- existing status-bar/help conventions;
- existing editor focus conventions where applicable.

Do not clone these into Environment- or Buddy-specific equivalents.

### 2.5 Existing character editor lifecycle remains character-specific

`CharacterEditorModeCoordinator` captures/restores window state, isolates editor resize, and freezes gameplay for the character editor. `CharacterEditorSession` owns character working-copy/save/discard behavior.

Buddy Studio should extend/reuse those character-editor boundaries.

Environment Decorator has different behavior: the room remains visible and its own placement input must be routed while decorating. Do **not** force Environment Decorator through `CharacterEditorModeCoordinator` merely for code reuse.

---

## 3. Shared foundation landed before branching

### 3.1 Extensible `Customize` command

`Win98CommandBarBootstrap` now exposes a stable registration seam:

```csharp
IDisposable RegisterCustomizeCommand(
    CustomizeCommandDefinition definition,
    Action invoke,
    Func<bool>? isVisible = null,
    Func<bool>? isEnabled = null)
```

Stable IDs/order live in `CustomizeCommandIds`:

```text
customize.paint_buddy       order 100
customize.paint_background  order 200
customize.buddy_studio      order 300
```

The command bar itself owns **Paint Buddy**. Future commands appear only after their feature registers a real handler. This preserves the product rule that unfinished/nonfunctional commands are not exposed.

Feature branches must **not edit `Win98CommandBarBootstrap.cs`** to add their menu item.

Registration pattern:

```csharp
_registration = commandBar.RegisterCustomizeCommand(
    new CustomizeCommandDefinition(
        CustomizeCommandIds.BuddyStudio,
        "Buddy Studio",
        "Customize your buddy's appearance.",
        CustomizeCommandIds.BuddyStudioOrder),
    OpenBuddyStudio,
    isVisible: () => IsFeatureReady,
    isEnabled: () => CanOpen);
```

Dispose the returned token on teardown.

### 3.2 Reserved independent autoload roots

The shared `project.godot` baseline already contains both inert composition roots:

```text
EnvironmentCustomizationBootstrap
BuddyStudioBootstrap
```

Files:

```text
src/Environment/EnvironmentCustomizationBootstrap.cs
src/CharacterEditor/BuddyStudio/BuddyStudioBootstrap.cs
```

Each branch owns only its bootstrap file. Neither branch needs to edit `project.godot`, `scenes/bootstrap.tscn`, or the shared command-bar bootstrap merely to compose/register its feature.

### 3.3 Shared category strip

`Win98CategoryStrip` owns only presentation behavior:

- icon + text category buttons;
- selected state;
- deterministic Left/Right keyboard traversal;
- horizontal scrolling;
- Win98 theme integration.

It has no knowledge of character categories, decoration categories, ownership or purchases.

Both feature branches should supply their own category IDs and domain mapping.

### 3.4 Shared visual catalogue grid

`Win98CatalogGrid` owns:

- fixed visual tiles;
- preview texture slot;
- display name;
- caller-provided secondary text / badge;
- selected state;
- responsive columns;
- arrow-key navigation;
- scroll behavior;
- per-item presentation refresh.

Its data contract is `Win98CatalogItemPresentation`.

Crucially, `SecondaryText` is already-resolved presentation text. The grid never decides whether `$75` means a placement price, whether `Owned` means permanent ownership, or whether an item can be purchased. That distinction belongs to the feature controller.

### 3.5 Shared value/price presentation

`Win98ValuePanel` is a domain-neutral aligned key/value inspector. Callers may use it for:

Buddy Studio:

```text
Price        $100
Status       Owned
```

Environment:

```text
Available    $1,250
Item Cost    $75
Projected    $1,175
```

The caller computes every value. The shared panel performs no balance math and no transaction.

### 3.6 Shared popup skin

`Win98MenuStyle` is the canonical popup skin and is already used by the Paint menu. New popup menus should use it rather than copy/paste theme overrides.

---

## 4. Shared versus deliberately separate concepts

| Concern | Shared? | Owner / rule |
| --- | --- | --- |
| Win98 colors, borders, button states | Yes | `Win98ThemeFactory` |
| Popup styling | Yes | `Win98MenuStyle` |
| Modal frame/blocker | Yes | `Win98Dialog` |
| Category navigation UI | Yes | `Win98CategoryStrip` |
| Visual item grid | Yes | `Win98CatalogGrid` |
| Key/value price/budget display | Yes | `Win98ValuePanel` |
| Top-level Customize routing | Yes | `CustomizeCommandRegistry` via command bar |
| Money formatting | Yes | `ContentDisplayName.Credits` |
| Wallet/balance source | Yes | existing `BuddyProgressState` / `EconomyService` |
| Cosmetic permanent ownership | Buddy only | existing unlocked-content + `CatalogueEntryKind.Cosmetic` |
| Decoration ownership | **No permanent ownership model** | Environment pays per placed instance |
| Cosmetic purchase transaction | Buddy only | existing `EconomyService.Purchase` |
| Decoration placement transaction | Environment only | staged room edit + wallet delta |
| Character working copy | Buddy only | extend `CharacterEditorSession` |
| Environment working copy | Environment only | new `EnvironmentEditSession` |
| Character document schema | Buddy only | currently schema 3 |
| Progress save schema bump for room layout | Environment only | see §6 |
| Cosmetic renderer registry | Buddy only | new trusted character visual registry |
| Decoration renderer registry | Environment only | new trusted environment visual registry |
| Paint Background document/store | Environment only | separate from character paint |
| Randomize | Buddy only | extend existing character randomizer |
| Grid snap / placement mapping | Environment only | room-coordinate placement engine |

Do not create a generic `CustomizationItem`, `CustomizationTransaction`, or `CustomizationStore` that attempts to represent both cosmetics and furniture. Their lifecycle and economy invariants differ too much for that abstraction to remain honest.

---

## 5. Economy boundary — where reuse stops

### Buddy Studio

```text
preview cosmetic
    -> no spend
Buy
    -> EconomyService.Purchase(cosmeticContentId)
    -> permanent unlocked-content ownership
    -> immediate progression save path
Save character
    -> CharacterStore only
Cancel character changes
    -> purchases remain
```

### Environment

```text
select decoration definition
    -> no spend
place staged instance
    -> reserve cost in EnvironmentEditSession
move/rotate staged or saved instance
    -> no additional cost
sell staged/saved instance
    -> staged cancellation/refund delta
Save/Done
    -> commit environment state + resulting wallet delta atomically
Cancel editor
    -> restore baseline layout and wallet projection
```

Therefore:

- Environment must **not** add decorations to `ToolCatalogue` as cosmetics just to reuse `Purchase`;
- Buddy Studio must **not** use Environment's staged per-instance transaction;
- shared UI is passed prepared strings/states from these separate controllers.

---

## 6. Persistence boundary locked for parallel work

### 6.1 Buddy Studio

Buddy Studio keeps the existing split:

- permanent cosmetic ownership + credits: core `ProgressSave` through existing unlocked-content state;
- per-character appearance: character JSON through `CharacterStore`;
- paint files: existing character paint store;
- machine/UI preferences only where truly machine-local.

The Buddy Studio branch should not need to bump `ProgressSave.CurrentSchemaVersion` for cosmetic ownership. It may extend character schema **only if a genuinely new persisted shape beyond the already-landed schema 3 is required**; adding the twelve slots or named color map is not such a reason because both already exist.

### 6.2 Environment customization

Room layout and purchased placed instances are cloud-eligible semantic player state and have a financial transaction attached to them.

The Environment branch should put the durable environment snapshot into the **same `ProgressSave` aggregate** as the wallet instead of creating a separate room sidecar that requires a cross-file purchase journal.

Recommended shape follows the already-proven Work pattern:

```text
EnvironmentProgressState       engine-free mutable runtime state
EnvironmentProgressSnapshot    immutable snapshot
EnvironmentProgressSave        serialized field on ProgressSave
SaveCoordinator                receives/tracks EnvironmentProgressState
```

Then Save/Done can mutate the wallet and environment state as one explicit transaction and persist both through one `SaveProgressAsync` write.

This is intentionally assigned to the Environment branch so Buddy Studio does not modify the same save-schema files in parallel.

Large/background binary assets, if Paint Background eventually needs them, may use an environment asset store keyed from the semantic profile, but **decoration purchase/layout truth must remain in the atomic progress aggregate**.

### 6.3 Reset Progress

Existing product policy still applies:

- progression resets;
- machine/window/editor preferences survive;
- local character files survive;
- cosmetic ownership resets but already-worn character appearance follows the Buddy Studio plan's ownership-loss rule;
- environment progression/layout should reset to the default empty/default environment when the Environment state is introduced.

The Environment branch owns updating reset coverage for its new progress state. The Buddy branch should not edit `ProgressReset` merely for cosmetic ownership because unlocked-content reset already covers cosmetic ownership.

---

## 7. Strict file ownership during parallel implementation

This section is the primary merge-conflict contract.

### 7.1 Shared/frozen foundation files

Neither feature branch should modify these during normal feature work:

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

If a real shared-component bug is discovered, stop feature work on that specific issue and make a small isolated shared fix that can be cherry-picked/rebased into both branches. Do not independently evolve the same shared component in both branches.

### 7.2 Environment branch owns

Primary ownership:

```text
domain/DesktopBuddy.Domain/Environment/**
src/Environment/**
data/environment/**
assets/environment/**
```

Environment also owns any required changes to:

```text
domain/DesktopBuddy.Domain/Persistence/ProgressSave.cs
src/Persistence/SaveCoordinator.cs
src/App/ProgressReset.cs
src/App/RunContext.cs
src/App/Bootstrap.cs                  only if unavoidable after using its reserved autoload
```

Prefer extending through `EnvironmentCustomizationBootstrap` so `Bootstrap.cs` remains untouched.

Environment may create new tests under:

```text
tests/DesktopBuddy.Domain.Tests/Environment/**
src/Testing/Environment*
```

Environment must not edit:

```text
domain/DesktopBuddy.Domain/Characters/**
src/CharacterEditor/BuddyStudio/** (except its own environment bootstrap is elsewhere)
src/Buddy/Presentation3D/Characters/**
data/catalogue/launch_catalogue.tres
```

Decoration IDs belong to the environment catalogue/domain, not `ContentIds.Cosmetic*`.

### 7.3 Buddy Studio branch owns

Primary ownership:

```text
domain/DesktopBuddy.Domain/Characters/**
src/CharacterEditor/BuddyStudio/**
src/Buddy/Presentation3D/Characters/**
data/cosmetics/**
assets/cosmetics/**
```

Buddy Studio also owns feature-specific additions to:

```text
domain/DesktopBuddy.Domain/Content/ContentIds.cs
domain/DesktopBuddy.Domain/Content/CatalogueEntry.cs          only if genuinely needed
domain/DesktopBuddy.Domain/Content/ToolCatalogue.cs           only if genuinely needed
src/Content/**                                                 cosmetic authored resources
data/catalogue/launch_catalogue.tres                           cosmetic sale entries
src/CharacterEditor/CharacterEditorSession.cs
```

The commerce kinds and ownership path already exist, so changes to `CatalogueEntry` / `ToolCatalogue` should be minimal or unnecessary.

Buddy Studio must not edit:

```text
domain/DesktopBuddy.Domain/Environment/**
src/Environment/**
domain/DesktopBuddy.Domain/Persistence/ProgressSave.cs
src/Persistence/SaveCoordinator.cs
src/App/ProgressReset.cs
```

unless a concrete blocker is found and explicitly coordinated.

### 7.4 Files both branches may read but should not own

```text
src/Economy/EconomyService.cs
src/UI/ContentDisplayName.cs
src/App/CharacterEditorModeCoordinator.cs
src/Platform/**
src/UI/Win98/** shared files listed above
```

Consume these APIs. Avoid opportunistic refactors while the parallel branches are active.

---

## 8. Branch-specific composition contract

### Environment bootstrap

`EnvironmentCustomizationBootstrap` is the Environment branch's integration root.

Once `Paint Background` is functional, it registers:

```text
CustomizeCommandIds.PaintBackground
```

Environment Decorator is **not** a fourth Customize entry under the current product decision. It should be opened through the Environment/Decor shopping flow described by its plan.

The bootstrap may compose Environment services/controllers and find existing runtime nodes after they are ready. Keep the bootstrap thin; business state belongs in domain/runtime services.

### Buddy Studio bootstrap

`BuddyStudioBootstrap` is the Buddy branch's integration root.

Once the workspace is functional, it registers:

```text
CustomizeCommandIds.BuddyStudio
```

Buddy Studio should enter the established character-editor mode/session boundary instead of creating another top-level character persistence stack.

---

## 9. Reuse decisions from the audit

### Reuse directly

- `CharacterEditorSession` working-copy/save/discard semantics for Buddy Studio.
- `CharacterEditorModeCoordinator` for Buddy Studio's deliberate full editor mode.
- existing physics-free buddy preview/visual-rig route for Buddy Studio.
- existing catalogue + economy purchase path for permanent cosmetics.
- existing unlocked-content ownership set for cosmetics.
- `Win98Dialog` for dirty-close/confirmation UI.
- `Win98CategoryStrip`, `Win98CatalogGrid`, `Win98ValuePanel` for both feature families.
- `ContentDisplayName.Credits` for all visible credit amounts.

### Reuse conceptually, not by forcing the same class

- Environment needs a working-copy editor state like the character editor, but it gets `EnvironmentEditSession`, not `CharacterEditorSession`.
- Environment needs trusted visual definitions like cosmetics, but gets `DecorationDefinition` / environment registry, not `CosmeticDefinition`.
- Both features show catalogue tiles, but only the UI tile is shared.
- Both show prices, but only formatting/layout is shared.

### Do not reuse

- `ToolCatalogue` as the source of decoration definitions.
- `EconomyService.Purchase` as a fake per-instance room placement purchase.
- character JSON to store room state.
- environment state to store clothing/cosmetic equipment.
- character paint store for Paint Background.
- gameplay physics bodies for launch decorations.

---

## 10. Buddy Studio remaining work after the Work foundation

A Buddy Studio agent should treat these as **already done**:

- schema 3 migration;
- twelve `CharacterFeatureSet` slots;
- named `Colors` map seam;
- `CatalogueEntryKind.Cosmetic`;
- cosmetic namespace recognition;
- permanent unlocked-content ownership path;
- Work glasses ownership/equipment proof of concept.

The major remaining slices are:

1. real engine-free `CosmeticDefinition` metadata layered onto the existing feature catalogue;
2. expand `CharacterCompiler` / compiled appearance from four rendered categories to the twelve-category model;
3. trusted engine visual registry and anchors;
4. semantic renderer extension for face categories;
5. hair/glasses/headwear/tops/shoes/accessory rendering and compatibility;
6. ownership-aware Studio working preview semantics;
7. full Randomize extension with owned/free filtering;
8. Buddy Studio UI using the shared category/grid/value components;
9. cosmetic authored content and catalogue sale entries;
10. tests/owner closure.

Do not spend a first slice redoing schema 3.

---

## 11. Environment remaining work

The Environment branch starts essentially greenfield at the domain level, but the shell/UI foundation is now prepared.

Recommended sequence remains:

1. `EnvironmentProgressState` + definition/layout/edit-session domain;
2. same-progress-save atomic persistence/economy transaction;
3. trusted environment catalogue/visual presenter;
4. Paint Background data/editor vertical slice;
5. free placement + anchor/grid/rotation engine;
6. Environment Decorator using shared category/grid/value UI;
7. wallpaper slot and layering with Paint Background;
8. six launch categories/content;
9. tests/owner closure.

This order deliberately proves persistence/economy correctness before a large catalogue UI is built.

---

## 12. Shared-component change protocol while agents run

If either agent needs a shared feature that is missing:

1. verify it is genuinely generic to both systems rather than feature-specific;
2. do not silently modify the shared foundation file in that feature branch;
3. record the requested generic capability and why the existing API cannot express it;
4. implement the smallest possible shared patch in an isolated commit;
5. apply the same shared patch to both branches before continuing dependent work.

Good shared additions:

- generic tile badge alignment;
- generic category-strip scroll visibility fix;
- generic popup/focus bug;
- generic modal layout bug.

Bad shared additions:

- `IsOwnedCosmetic` on `Win98CatalogGrid`;
- `DecorationRefund` on `Win98ValuePanel`;
- `SelectedHair` on `Win98CategoryStrip`;
- room coordinate logic inside a UI tile.

---

## 13. Merge choreography

Both feature branches must originate from the same shared-foundation commit.

Either feature may finish first.

Recommended integration:

1. finish and owner-verify one branch;
2. merge it into `main`;
3. rebase/merge the second branch onto the updated `main`;
4. expected conflicts should be limited to genuinely shared integration/doc areas because file ownership above is disjoint;
5. run both feature regression suites plus the existing Work/Paint/Win98 suites;
6. verify the Customize dropdown contains exactly the functional entries:
   - Paint Buddy
   - Paint Background
   - Buddy Studio;
7. verify Environment Decorator remains reached through its own decor flow rather than becoming a fourth Customize item.

Do not merge the branches by choosing one branch's copy of shared files wholesale. Shared files should remain the common foundation plus explicitly coordinated shared fixes.

---

## 14. Agent start checklist

Before either subagent changes code, it should confirm:

- it is on the intended feature branch;
- its branch ancestry contains this foundation document;
- it has read its product implementation plan plus this boundary document;
- it does not plan to edit the other branch's owned paths;
- it will use the reserved bootstrap rather than adding another autoload;
- it will register its Customize route instead of editing the command bar;
- it will reuse shared visual catalogue/category/value components;
- it understands the separate economy semantics;
- it will make small, slice-oriented commits;
- it will stop and flag any requirement that appears to require a frozen shared-file change.

With those constraints, Environment customization and Buddy Studio are safe to implement concurrently.
