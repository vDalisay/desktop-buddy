# Desktop Buddy — Buddy Studio Customization & Clothing Plan

Status: **Owner UX decisions locked — implementation planning ready, scheduling not yet committed**  
Revised 2026-08-06 after an architecture review: reuse pass over the existing character and catalogue types (§6–§8, §10), the §8.4 ownership-loss rule, and a three-category vertical slice shape for §16. No locked owner decision changed.  
Planning branch: `win98-feel`  
Depends on:

- completed Character Editor Phase A architecture in `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`;
- Character Painting / paint persistence already landed in the current character schema;
- accepted Win98 application shell and shared UI foundation in `docs/WIN98_UI_UX_REVAMP_PLAN.md`;
- existing credits, purchase ledger, progression persistence, and immediate-save purchase behavior;
- the shared `Customize` command-bar direction recorded in `docs/POST_WIN98_ENVIRONMENT_CUSTOMIZATION_PLAN.md`.

Recommended schedule: **after the Win98 closure pass and before environment customization / Milestone 6**, unless the owner explicitly changes the roadmap order.

---

## 1. Purpose

Buddy Studio is the dedicated appearance and clothing workspace for a buddy. It expands the existing character editor from the current limited facial-feature model into a full category-based creator while preserving the existing paint editor as a separate tool.

The broad interaction model is inspired by generic console-avatar creator conventions: a persistent buddy preview, category tabs, selectable appearance tiles, contextual transform controls, a color inspector, and explicit Save / Cancel actions. The visual treatment remains the project's original Win98 skin.

The implementation must be clean-room. It may use generic avatar-creator conventions, but it must not copy Nintendo/Mii artwork, icons, exact layouts, wording, proportions, silhouettes, sounds, fonts, or source assets. All category icons, cosmetic art, meshes, decals, and UI chrome are original project assets.

Buddy Studio is opened from the persistent horizontal command bar:

```text
Customize
├─ Paint Buddy
├─ Paint Background
└─ Buddy Studio
```

- **Paint Buddy** routes to the existing direct-paint editor.
- **Paint Background** routes to the future environment/background editor.
- **Buddy Studio** routes to this appearance/clothing workspace.

No nonfunctional command should be exposed. `Paint Background` remains hidden/disabled according to the environment-plan release gate until that editor is real.

---

## 2. Locked owner decisions — 2026-08-06

These are product requirements, not implementation suggestions.

### 2.1 Ownership and purchase model

- Every cosmetic may be previewed in Buddy Studio whether owned or not.
- Unowned cosmetics show their price and an explicit **Buy** action.
- Buying permanently unlocks that cosmetic **account/save-wide**, not only for the current buddy.
- Once owned, the cosmetic may be used on any local buddy.
- A baseline set of cosmetics is free/owned from the start.
- Previewing an item never purchases it.
- Saving a character with an unowned preview selected is not allowed.
- Purchases never happen implicitly through Save, Randomize, character switching, or import.

### 2.2 Launch categories

The top category bar contains exactly these launch categories in this order:

1. Face
2. Hair
3. Eyebrows
4. Eyes
5. Nose
6. Mouth
7. Ears
8. Accessories
9. Glasses
10. Headwear
11. Tops
12. Shoes

Do not add a generic `Body`, `Color`, `Clothing`, or `More` category in the launch workspace.

### 2.3 Transform controls

Contextual transform actions are:

- **Larger**
- **Smaller**
- **Move**
- **Reset**

There is **no rotation control**.

Transform support is category/definition driven:

| Category | Launch transform policy |
| --- | --- |
| Face | none |
| Hair | none |
| Eyebrows | move + uniform scale |
| Eyes | move + uniform scale |
| Nose | move + uniform scale |
| Mouth | move + uniform scale |
| Ears | move + uniform scale |
| Accessories | per-definition; default move + uniform scale when safe |
| Glasses | move + uniform scale |
| Headwear | none |
| Tops | none; predefined fitting |
| Shoes | none; predefined fitting |

Controls that do not apply to the selected definition are hidden rather than shown as meaningless disabled chrome, except where hiding would cause severe layout jitter; in that case preserve the footprint and disable them with explanatory status text.

### 2.4 Color channels

The data model supports **multiple stable named color channels per cosmetic** from the first schema version of Buddy Studio.

Examples:

- `primary`
- `secondary`
- `trim`
- `lens`

Launch content uses **one channel only**, normally `primary`.

The UI must therefore not hard-code "the cosmetic has exactly one color" into persistence or rendering. At launch, the channel selector stays hidden whenever the selected definition exposes only one channel. It appears only when real multi-channel content exists later.

### 2.5 Randomize

Randomize affects **everything** in Buddy Studio:

- all twelve category selections;
- supported colors;
- supported offsets;
- supported scales;
- clothing and accessories as well as facial appearance.

Randomize may select only:

- free/default cosmetics; and
- already-owned cosmetics.

Randomize must never:

- spend credits;
- select an unowned cosmetic;
- make a purchase;
- unlock content;
- alter character paint;
- alter the active background or room;
- alter gameplay tuning, physics, economy, mood, or progression outside appearance selection.

Randomization remains deterministic when given a test seed.

---

## 3. Prime invariants

The existing character-editor invariants remain authoritative.

1. **Visual-only customization.** Buddy Studio data cannot modify rigid bodies, collision, mass, spring tuning, locomotion, damage, payouts, mood rules, tool behavior, or any physics geometry.
2. **One simulation buddy.** Changing appearance reuses the existing buddy and visual rig.
3. **Paint survives Studio edits.** Buddy Studio never clears, resamples, bakes, resizes, or rewrites paint PNGs.
4. **Paint remains below cosmetic overlays.** Clothing, face elements, glasses, hair, headwear, and other explicit overlays may visually cover paint but do not destroy it.
5. **Semantic expressions remain authoritative.** Selected eye/brow/mouth styles must still render the existing semantic expression poses.
6. **Purchases are progression transactions; appearance edits are character-document transactions.** They must not share one Cancel boundary.
7. **Cancel never refunds purchases.** If an item was purchased while Studio was open, ownership remains even if the player cancels the character appearance edits.
8. **No hidden purchase through randomization or save.** Ownership checks are explicit.
9. **Stable IDs are persistence contracts.** Released cosmetic IDs and color-channel IDs are never reused for different content.
10. **Unknown IDs remain recoverable.** Unknown future cosmetic IDs are preserved in character data and compile to safe category defaults rather than corrupting the whole character.
11. **Trusted renderer mapping.** Character files store IDs and bounded values, never arbitrary resource paths, scenes, scripts, meshes, shaders, or executable data.
12. **Editor remains in-scene.** Buddy Studio does not create an ordinary detached OS window.

---

## 4. UX composition

## 4.1 Overall window

Buddy Studio reuses the completed Win98 application/editor frame and should read as the sibling of Paint Buddy, not a separate application.

Recommended composition:

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ Desktop Buddy - Buddy Studio                                               │
├─────────────────────────────────────────────────────────────────────────────┤
│ Face | Hair | Eyebrows | Eyes | Nose | Mouth | Ears | ... | Tops | Shoes │
├───────────────────┬──────────────────────────────────┬──────────────────────┤
│                   │                                  │                      │
│ Buddy preview     │  category item grid              │  Color               │
│                   │                                  │  wheel / presets      │
│                   │  page controls when needed       │                      │
│                   │                                  │  Price / Owned        │
│ Name: Bumba       │  Larger Smaller Move Reset       │  Buy                  │
│ Randomize         │                                  │                      │
├───────────────────┴──────────────────────────────────┴──────────────────────┤
│                                                        Save      Cancel     │
└─────────────────────────────────────────────────────────────────────────────┘
```

This is a structural guide, not an instruction to reproduce the supplied mockup pixel-for-pixel.

### Left preview pane

- Reuse the physics-free `BuddyVisualRigView` preview path.
- Show the current buddy in a stable neutral/rest pose.
- Show the character's display name as read-only text.
- Character naming remains handled by the existing new-character / rename flow, not duplicated as an editable Studio field.
- Put **Randomize** in the preview/global-action region, visually separated from per-item purchase controls.
- Preview updates immediately for working-copy edits and unowned previews.
- Studio preview contains no gameplay simulation, damage components, economy components, or live buddy AI.

### Top category strip

- Twelve stable category tabs in the locked order.
- Each uses an original icon plus short label.
- Selected category is recessed/pressed and is not communicated by color alone.
- At narrow widths, use an in-scene horizontal scroller/arrow affordance; do not compress labels until unreadable and do not wrap into an uncontrolled multi-row layout.
- Mouse wheel over the category strip may scroll horizontally where appropriate.
- Left/right keyboard navigation changes category after the strip has keyboard focus.

### Item grid

- Selected category shows only real definitions belonging to that category.
- Tiles use original preview art generated from the trusted cosmetic definition.
- Owned/free/unowned state is visible without obscuring the cosmetic preview.
- Selection is distinct from ownership: an unowned tile may be selected for preview.
- Grid order is deterministic and stable by authored catalogue order, never filesystem enumeration order.
- Overflow scrolls in the existing Win98 scroll container. Do not build pagination until a real category exceeds what a scroll pane handles comfortably; `Page X/Y`, Previous/Next and their bounds logic are deferred until then.

### Contextual transform panel

- The existing offset/scale controls in `CharacterEditorHost` are the starting implementation; `Larger` / `Smaller` / `Move` re-skin that bound value path rather than introducing a second transform pipeline.
- `Larger` / `Smaller` change uniform scale in bounded steps.
- `Move` enters direct manipulation for the selected transformable feature.
- While Move is active:
  - dragging inside the preview moves the selected feature in normalized local coordinates;
  - arrow keys nudge it by a small deterministic step;
  - Shift+arrow uses a larger step;
  - Escape exits Move mode without closing Studio.
- `Reset` resets only the current cosmetic's transform to the definition defaults; it does not reset the category choice or color.
- Transform values remain normalized and resolution-independent.

### Color inspector

- Reuse the shared project color-wheel/preset behavior where possible.
- Show the selected cosmetic's current channel color.
- Launch content exposes one visible channel and therefore no channel selector.
- Future definitions with multiple channels reveal a compact channel selector above the wheel.
- Preset swatches and custom palette behavior should reuse the Paint Buddy palette implementation where technically appropriate, but Buddy Studio stores selected cosmetic colors in the character document rather than paint preferences.
- Definitions may opt out of tinting entirely; for a non-tintable definition the color section hides.

### Purchase area

For the currently selected cosmetic:

**Free/default item**

- show `Free` or `Owned`;
- no Buy button.

**Owned paid item**

- show `Owned`;
- no Buy button.

**Unowned item**

- show its price;
- show `Buy`;
- disable Buy when funds are insufficient and explain why through the status bar / tooltip.

On successful purchase:

- ownership updates immediately account-wide;
- the selected preview becomes save-eligible;
- the tile ownership state updates without leaving the category;
- progression save is requested immediately through the existing purchase-save path.

### Bottom actions

- **Save** writes the character working copy atomically through the existing character store.
- **Cancel** restores the character baseline but does not undo purchases.
- Save is disabled when the working copy currently references any unowned cosmetic.
- The status bar explains the exact blocking item rather than silently refusing.
- Standard dirty-close Save / Discard / Continue Editing behavior remains in force.

---

## 5. Category contracts

Every category requires a trusted definition type and a safe `none`/default strategy where visually optional.

## 5.1 Face

Purpose: the buddy's front face plate/frame style, not physics head geometry.

- Face options may change a trusted visual front-plate/frame treatment while preserving the canonical head paint surface and collision sphere.
- A face option must not alter the physical head radius or ray/UV paint mapping.
- Launch Face definitions are non-transformable.
- If a face definition is tintable, its single launch channel is `primary`.
- Any future render-only shell that changes visible silhouette must still satisfy the paint-UV and physics invariants before it can ship.

Open risk: constrained by the invariants above (no silhouette change, no head radius change, no paint-UV change), Face has the thinnest definition of the twelve categories and could ship as an empty tab. **One concrete, renderable Face definition must exist and be reviewed before the Face tab becomes visible.** If no such definition survives the invariants, the tab is hidden rather than shipped with a single no-op entry — this is an authoring outcome, not a change to the locked category list.

## 5.2 Hair

- Trusted render-only head attachment.
- Non-transformable at launch.
- Tintable through `primary` when authored.
- May be hidden by a selected headwear definition through an explicit compatibility/display rule; the hair selection remains saved and returns when the conflicting headwear is removed.

## 5.3 Eyebrows

- Reuses/extends the semantic brow renderer contract.
- Existing stable IDs (`brows.soft_arc`, `brows.straight`, `brows.segmented`) remain valid.
- Move + uniform scale.
- Selected renderer must support every existing `FaceBrowPose`.

## 5.4 Eyes

- Reuses/extends the semantic eye renderer contract.
- Existing stable IDs remain valid.
- Move + uniform scale; both eyes remain one symmetrical group unless a future separately approved design changes that rule.
- Selected renderer must support every existing `FaceEyePose`.

## 5.5 Nose

- New trusted face attachment/decal category.
- Move + uniform scale.
- No gameplay collision.
- Optional `none` only if the art direction allows noseless buddies; otherwise every definition catalog must supply a free default.

## 5.6 Mouth

- Reuses/extends the semantic mouth renderer contract.
- Existing stable IDs remain valid.
- Move + uniform scale.
- Selected renderer must support every existing `FaceMouthPose`, including chewing/reaction states used by the current game.

## 5.7 Ears

- Paired trusted visual attachments anchored to the head.
- Move + uniform scale as one paired selection.
- No collision and no effect on head paint targeting.

## 5.8 Accessories

- General cosmetic category for trusted decorative attachments that do not belong to Hair, Glasses, Headwear, Tops, or Shoes.
- Definition declares its anchor and transform policy.
- Default launch behavior permits move + uniform scale only where the authoring definition marks it safe.
- Existing torso-accent IDs (`accent.none`, `accent.panel`, `accent.chevron`, `accent.bolts`) migrate into this category without changing those stable IDs.
- Accessories may use explicit render-layer bands but may never render above UI.

## 5.9 Glasses

- Trusted face attachment anchored relative to the eye group.
- Move + uniform scale.
- Visually renders above eye features.
- Tint channel applies to frame/lens according to the definition; launch definitions still expose only one named channel.

## 5.10 Headwear

- Trusted head-top attachment.
- Predefined fitting; no player transform controls at launch.
- Definition explicitly declares whether it hides hair while equipped.
- It does not delete or replace the saved Hair selection.

## 5.11 Tops

- Trusted torso visual overlay/attachment.
- Predefined fitting; no player transform controls.
- Renders above torso paint while equipped; removing the top reveals unchanged underlying paint.
- Tops never change torso collision, mass, spring attachment points, or gameplay silhouette used for physics.

## 5.12 Shoes

- One selection applies to both feet as a paired cosmetic.
- Predefined fitting; no player transform controls.
- Renders above foot paint while equipped; underlying paint is preserved.
- Shoes have no collision and do not alter foot rigid-body geometry.

---

## 6. Character schema evolution

Current character schema is version 2 and contains:

- `partColors`;
- legacy `features` (`eyes`, `brows`, `mouth`, `torsoAccent`);
- `paint` manifest.

Buddy Studio introduces the next sequential schema version. It **widens the existing `features` node to twelve keys** rather than adding a parallel `studio` node beside it: the four legacy slots already store exactly the per-category shape Buddy Studio needs (`featureId`, `offsetX`, `offsetY`, `scale`, colour), so schema 3 is additive plus one rename.

Canonical schema 3 shape:

```json
{
  "schemaVersion": 3,
  "id": "...",
  "displayName": "...",
  "partColors": { ... },
  "features": {
    "face": { ... },
    "hair": { ... },
    "eyebrows": { ... },
    "eyes": { ... },
    "nose": { ... },
    "mouth": { ... },
    "ears": { ... },
    "accessories": { ... },
    "glasses": { ... },
    "headwear": { ... },
    "tops": { ... },
    "shoes": { ... }
  },
  "paint": { ... }
}
```

`eyes`, `mouth` and `paint` keep their existing key names and values; `brows` is renamed to `eyebrows` and `torsoAccent` to `accessories`; the remaining eight keys are new. There is no second appearance node and no move of surviving data between nodes.

Each category selection uses:

```text
cosmeticId: stable string
transform: optional object
    offsetX
    offsetY
    scale
colors: map<string channelId, #RRGGBB>
```

Rules:

- `transform` is absent for definitions whose transform policy is `None`.
- Offsets remain normalized and bounded.
- Scale remains uniform and definition-bounded.
- Rotation is not serialized.
- Colors are opaque `#RRGGBB` unless a separately approved feature requires alpha later.
- Color-channel keys are stable definition-owned IDs.
- Unknown channel IDs are preserved but ignored by the current renderer.
- Missing known channels compile to definition defaults.
- No resource path, renderer type name, scene path, material path, or script name appears in the document.

### 6.1 Migration 2 -> 3

Migration is additive plus two key renames. It must preserve existing characters exactly as closely as the new renderer permits:

- `features.eyes` and `features.mouth` are untouched — same key, same ID, transform and color;
- `features.brows` -> `features.eyebrows`, retaining the existing brow ID, transform and color;
- `features.torsoAccent` -> `features.accessories`, retaining the existing `accent.*` stable ID, transform and color;
- Face, Hair, Nose, Ears, Glasses, Headwear, Tops and Shoes receive shipped free defaults;
- paint manifest is byte-for-byte semantically unchanged;
- character GUID and display name are unchanged;
- unknown extension data remains preserved.

`features` remains the one appearance source of truth; the renamed keys do not survive alongside their old names after migration.

---

## 7. Engine-free cosmetic catalogue

**Widen the existing types; do not stand up a parallel vocabulary next to them.** `CharacterFeatureCatalog` already owns ids-per-slot, per-slot defaults and duplicate-ID rejection, and `CharacterFeatureDocument` already is the per-category selection record. The general catalogue is those types generalized, not new ones beside them:

```text
CharacterFeatureSlot        widened from 4 values to the 12 locked categories
CharacterFeatureCatalog     ids/defaults per category, plus the definition data below
CharacterFeatureDocument    the per-category selection (id + transform + colors)
CharacterCompiler           compiles selections; unchanged responsibility
```

Genuinely new, because nothing in the current model expresses it:

```text
CosmeticDefinition          the per-ID rules the catalogue currently lacks
CosmeticTransformPolicy     None | MoveAndUniformScale, plus bounds
CosmeticColorChannel        stable channel ID + default
```

Compatibility is a `HidesHair` flag on the definition (§12), not a policy type. Do not introduce `BuddyStudio*` mirrors of types that already exist under `Characters/`.

A `CosmeticDefinition` contains only engine-free rules/data:

- stable cosmetic ID;
- category;
- display/localization key;
- authored sort order;
- free/default flag;
- transform policy and bounds;
- default transform;
- named color channels + defaults;
- compatibility tags/rules;
- safe fallback ID for category resolution where appropriate;
- flags such as `HidesHair`;
- no Godot resource references.

Engine-side visual assets live in a separate trusted registry keyed by the same stable cosmetic ID.

### 7.1 Stable IDs

Use namespaced strings, for example:

```text
face.classic_plate
hair.short_sweep
eyes.soft_oval
brows.soft_arc
nose.button
mouth.rounded
ears.round
accessory.panel
accessory.star
glasses.round
headwear.cap
top.basic
shoes.basic
```

Existing released IDs are retained rather than renamed only for cosmetic neatness.

---

## 8. Ownership and economy architecture

**Cosmetics ride the existing catalogue and ownership set.** `CatalogueEntry` already carries exactly the commerce fields a cosmetic needs — `ContentId`, `PriceMilliCredits`, `ProgressionOrder`, `Visible`, `NameKey`, `DescriptionKey` — with the same "no unfinished shop entry is shown" gate this plan requires, and ownership is already a set of content-ID strings (`unlockedToolIds` in the progress snapshot). A second commerce stack would duplicate the ledger integration, the idempotency rules, the reset contract and their tests for no new behavior.

What actually changes:

- add `CatalogueEntryKind.Cosmetic`;
- `IsSelectable => Kind is not (PassiveUpgrade or Cosmetic)` — cosmetics never enter tool selection;
- cosmetic content IDs are namespaced (`cosmetic.hair.short_sweep`, …) and are never parsed as tool IDs;
- the tool shop filters to `Kind is not Cosmetic`, and Buddy Studio filters to `Kind is Cosmetic`, so neither surface lists the other's content;
- cosmetic entries carry `ProgressionOrder` only for authored display order — **they are not part of, and must not renumber, the locked sixteen-entry tool progression schedule.** Any catalogue test that asserts the tool schedule is updated to assert it over `Kind is not Cosmetic`.

Free/default cosmetics are not catalogue entries at all: they are `IsFreeDefault` in the definition (§7) and are never sold, matching how `StartingTool` works today.

### 8.1 Progress persistence

No new ownership collection and no progression schema migration: cosmetic IDs are added to the existing unlocked-content set, which is already a `string` set keyed by content ID.

Ownership resolution:

```text
owned = definition.IsFreeDefault || progress.IsUnlocked(cosmeticId)
```

Rules:

- all current migrated defaults must be free so existing saves never lose their appearance;
- purchasing adds the ID exactly once; the existing `PurchaseResult.AlreadyOwned` covers duplicates;
- unknown/future cosmetic IDs in a save survive a load/save cycle through the existing extension-data path, exactly as unknown tool IDs do today;
- reset-progress behavior follows the already approved reset contract: cosmetic purchases are progression and are erased by Reset Progress, while application/window preferences remain preserved — subject to §8.4, which governs what that does to already-saved characters;
- platform achievements remain governed by the existing reset policy and are unrelated to cosmetics.

### 8.2 Purchase transaction

Purchase flow:

1. resolve selected cosmetic definition + commerce entry;
2. verify it is visible/released;
3. verify not already owned/free;
4. verify sufficient credits;
5. debit through the existing ledger semantics;
6. add the cosmetic ID to progression ownership;
7. emit one progression change;
8. request immediate durable save through the existing coordinator;
9. refresh Buddy Studio ownership and Save eligibility.

A progression save failure follows the existing dirty-retry policy; it must not corrupt the character file or silently refund/re-spend through a repeated UI click.

### 8.3 Price authoring

This plan does not lock individual cosmetic prices. Pricing is a content/economy authoring pass after representative cosmetics exist.

Price constraints:

- never modify the locked M5 tool progression schedule to make room for cosmetics;
- basic/default options remain free;
- price is shown in the same displayed credit unit as the existing shop;
- no loot boxes, random paid rolls, or implicit purchase through Randomize.

### 8.4 Worn cosmetics survive ownership loss

A character document outlives the progression that paid for its cosmetics: Reset Progress erases cosmetic ownership (§8.1), and a character may be copied in from another save. Without an explicit rule, every such character loads referencing unowned content and becomes permanently unsaveable under §9.

The rule: **the ownership check gates selecting new cosmetics, never persisting cosmetics a character already wears.**

- Loading never rewrites a character's appearance because of ownership; a worn cosmetic keeps rendering after Reset Progress.
- A character that already wears an unowned cosmetic remains saveable with that cosmetic in place. Save is blocked only when the *current Studio session* selected an unowned item (§9) — the unowned-preview flag is session state, not a property of loaded data.
- Deselecting an unowned worn cosmetic is one-way: it cannot be reselected until it is owned again.
- Randomize (§10) still refuses unowned content, so randomizing a reset save legitimately strips its previously purchased look.

This keeps §3.9–3.11 intact — no purchase is implied, no character file is silently rewritten, and no player loses an appearance they already had.

---

## 9. Working-copy and preview semantics

Buddy Studio extends the existing `CharacterEditorSession`; it does not create a second character-edit persistence stack.

Maintain two concepts while Studio is open:

1. **working character appearance** — saveable character data;
2. **preview override** — may temporarily reference an unowned cosmetic.

Recommended behavior:

- selecting an owned/free item writes directly to the working copy;
- selecting an unowned item updates the preview and marks that category as an unowned preview;
- Buy converts the preview into a normal saveable working-copy selection after ownership succeeds;
- navigating to another category does not accidentally purchase or discard the unowned preview;
- Save is blocked while any category has an unowned preview *selected in this session*; a cosmetic the loaded character already wore is not a preview and does not block Save (§8.4);
- Cancel drops all Studio working-copy / preview appearance changes and restores the saved baseline;
- purchases survive Cancel because they belong to progression, not the character transaction.

This separation prevents a character file from ever durably referencing content the save does not own while still allowing full-store preview behavior.

---

## 10. Randomization architecture

Extend the existing seeded `CharacterRandomizer` to twelve categories and ownership filtering. It is already the engine-free, seed-driven randomizer this section describes; a `BuddyStudioRandomizer` beside it would be the same code with a different name.

The signature gains an owned-set parameter:

```text
CharacterRandomizer.Randomize(
    CharacterDocument baseline,
    CharacterFeatureCatalog catalogue,
    IReadOnlySet<string> ownedCosmeticIds,
    ulong seed)
```

For every category:

1. build eligible definitions = free defaults + owned definitions;
2. apply compatibility filtering based on already selected/randomized items;
3. choose deterministically from the stable authored list;
4. choose each supported launch color from an authored safe randomization palette/range;
5. choose bounded transform values only for transformable categories;
6. normalize and validate the final complete result;
7. return one replacement Studio appearance object.

Randomize changes all twelve categories in one operation.

Compatibility resolution must be deterministic. Example: if a headwear item hides hair, hair is still randomized and saved; it is merely not rendered while that headwear remains selected.

Randomize never considers the currently previewed unowned store item as eligible unless it has actually been purchased.

---

## 11. Rendering architecture

## 11.1 Trusted visual registry

Create an engine-side `BuddyCosmeticVisualCatalog` keyed by cosmetic ID. It may reference trusted project-owned:

- meshes;
- materials;
- decal renderers;
- compositor renderer kinds;
- anchor definitions;
- preview icons.

Character data cannot supply these references.

Unknown/missing visual registrations produce a safe category fallback plus a diagnostic warning; they do not quarantine an otherwise valid character.

## 11.2 Visual anchors

Extend `BuddyVisualRigView` with explicit cosmetic anchors that follow the visual rig only:

- head front / face plane;
- head crown;
- left/right ear;
- eye/glasses group;
- torso front;
- torso attachment;
- left/right foot;
- other narrowly justified visual-only anchors.

These anchors contain no collision shapes and do not participate in physics.

## 11.3 Layer order

Use an explicit bounded render contract. Recommended conceptual order:

1. trusted base buddy materials;
2. per-part paint underlay/surface texture;
3. torso/front body overlays and Tops;
4. semantic face compositor (eyes/brows/mouth) + nose/face details;
5. ears / hair;
6. glasses;
7. general accessories according to approved bounded bands;
8. headwear;
9. presentation effects that already intentionally sit above appearance;
10. UI.

Specific engine draw priorities may differ, but the contract must guarantee:

- paint remains intact beneath clothing;
- expressions remain visible according to their intended layer;
- headwear can hide hair without deleting it;
- cosmetics can never render above UI through imported/user data.

## 11.4 Expressions

Eyes, eyebrows, and mouth remain semantic renderer families rather than static stickers.

Every shipped definition in these categories must support all currently required semantic poses. Adding a cosmetic may not create a missing-expression crash path.

## 11.5 Paint fidelity

Studio must not change the canonical paint UV mapping.

Any cosmetic that visually alters silhouette is an attachment/overlay outside the paintable physics/paint surface unless a separate future plan explicitly extends the paint system.

---

## 12. Compatibility rules

Keep compatibility small and data-driven.

Required first-release rule:

- Headwear may declare `HidesHair = true`.

Recommended extension seam:

- definitions may expose simple tags and explicit hide rules;
- no arbitrary scripting or expression language;
- a compatibility rule affects rendering/eligibility only, never ownership.

Do not create a combinatorial exclusion matrix unless real content requires it.

---

## 13. Responsive Win98 behavior

Buddy Studio must pass the same UI quality bar as Paint Buddy.

- minimum window size prevents the preview, item grid, and purchase controls from becoming unreachable;
- left preview pane retains a bounded width;
- center item grid receives flexible space;
- right color/purchase pane retains a bounded width;
- top category strip scrolls horizontally on narrow windows;
- Save / Cancel remain reachable at the bottom;
- no ordinary child OS windows;
- modal purchase confirmation, if later required, uses the shared in-scene Win98 modal style;
- 100%, 125%, 150%, and 200% Windows scaling are part of owner acceptance.

At very narrow supported widths, collapse the color/purchase pane into an in-scene attached drawer/tab rather than overlapping the item grid. Do not spawn a separate window.

---

## 14. Input and accessibility

Required keyboard behavior:

- Tab / Shift+Tab follows deterministic visual order;
- arrow keys navigate tiles when the item grid is focused;
- Enter selects/activates a focused tile or button;
- Escape exits Move mode first, then follows normal dirty-close behavior;
- Ctrl+S saves when saveable;
- category strip supports left/right keyboard navigation;
- selected/owned/unowned/disabled states are not communicated by color alone;
- tooltips/status bar explain price, ownership, unavailable Save, transform limits, and compatibility behavior.

The Win98 skin may look period-authentic but must keep modern usable hit targets and focus visibility.

---

## 15. Initial content authoring rules

The implementation slice should not ship empty placeholder categories.

For every visible category:

- at least one free/default real cosmetic exists;
- every item has an original preview icon/thumbnail;
- every paid item has a real render implementation before it becomes visible;
- stable IDs are assigned before save-format acceptance;
- no copied Mii/Nintendo asset or distinctive silhouette is used.

Content count is deliberately not locked by this plan. Architecture and UX must handle small and large category counts without relying on exactly 6, 9, or 12 tiles.

Representative implementation content should cover the renderer types, not attempt the final cosmetic catalogue in the first technical slice.

---

## 16. Implementation slices

**Slice shape: BS0–BS5 carry the full twelve-category schema but only three shipped categories.** The schedule risk here is authoring original art for eight new categories, not the domain code — so BS0–BS5 prove the whole pipeline against **Hair, Headwear and Tops**, which between them exercise every mechanism the other nine need: a head attachment, the hides-hair compatibility rule, a torso overlay above paint, a paid purchase, and a tintable channel. The schema, catalogue and category strip are twelve-wide from BS0; the nine unproven categories simply have no definitions yet and their tabs stay hidden under the existing "no nonfunctional command" rule.

BS6 then fills content against a proven pipeline instead of discovering pipeline problems while twelve categories of art are in flight.

### BS0 — authority alignment and schema contract

Files/doc areas:

- `docs/BUDDY_STUDIO_CUSTOMIZATION_PLAN.md`
- `docs/ROADMAP.md` when the owner schedules the work
- `domain/DesktopBuddy.Domain/Characters/*`

Deliver:

- schema 3 design implemented as the widened `features` node;
- twelve stable category contract (`CharacterFeatureSlot` widened);
- `CosmeticDefinition` / transform policy / color channels on the existing `CharacterFeatureCatalog`;
- 2 -> 3 migration (additive plus the `brows` and `torsoAccent` renames);
- normalizer/validator/compiler coverage;
- existing eye/brow/mouth/accent IDs retained;
- paint manifest unchanged.

Gate:

- old schema-2 characters migrate without losing paint, identity, current facial choices, transforms, or colors.

### BS1 — trusted visual catalogue and render anchors

Deliver:

- engine-side visual registry;
- visual-only anchor nodes;
- category fallback behavior;
- head-crown and torso-front attachment seams, implemented for Hair, Headwear and Tops; the remaining anchors are declared by the contract but not built until their content exists;
- semantic feature renderer generalization;
- explicit render-layer policy;
- headwear-hides-hair rule.

Gate:

- appearance swapping changes no physics values or nodes;
- paint remains visually/persistently intact.

### BS2 — cosmetic ownership and purchase domain

Deliver:

- `CatalogueEntryKind.Cosmetic` plus the `IsSelectable` and shop/Studio filters;
- cosmetic IDs riding the existing unlocked-content set (no new collection, no progression schema migration);
- free/default ownership policy;
- purchase through the existing catalogue purchase path and ledger;
- immediate durable save request;
- §8.4 worn-cosmetic-survives-ownership-loss behavior;
- representative free + paid cosmetics in the three BS slice categories.

Gate:

- purchase is account-wide, idempotent, and survives switching buddies/restart;
- tool economy ordering and the locked sixteen-entry tool schedule are unchanged, asserted over `Kind is not Cosmetic`;
- no cosmetic appears in the tool shop and no tool appears in Buddy Studio.

### BS3 — Buddy Studio Win98 shell

Deliver:

- `Customize > Buddy Studio` command route;
- preview pane;
- twelve-category strip;
- scrolling item grid;
- right color/purchase pane;
- bottom Save / Cancel;
- shared status bar;
- responsive layout and focus graph.

Gate:

- no detached ordinary window;
- all controls remain reachable at supported minimum size/DPI.

### BS4 — selection, transforms, colors, and preview

Deliver:

- owned/free working-copy selection;
- unowned preview override;
- contextual transform controls;
- preview drag/nudge move mode;
- one-channel launch UI on a multi-channel schema;
- shared palette/color wheel integration;
- Save eligibility rules.

Gate:

- Save can never persist an unowned cosmetic;
- transform bounds and no-rotation contract are enforced in domain validation, not only UI.

### BS5 — Randomize and transaction closure

Deliver:

- deterministic all-category randomizer;
- only owned/free eligibility;
- compatibility resolution;
- Save/Cancel semantics;
- dirty-close integration;
- purchase-survives-Cancel behavior.

Gate:

- 50 deterministic randomization seeds produce only valid, owned/free complete appearances and never touch paint or credits, plus one fixed-seed golden output.

### BS6 — content, verification, and owner acceptance

Deliver:

- real original launch content for the remaining nine categories, on the pipeline BS0–BS5 proved;
- one concrete Face definition, or a hidden Face tab (§5.1);
- final thumbnails/icons;
- pricing pass for paid cosmetics;
- Windows DPI matrix;
- performance check;
- regression suite and real-input journey;
- owner visual/UX review.

Gate:

- no visible placeholder cosmetic;
- all current gameplay/paint/character journeys remain green;
- owner accepts category navigation, preview/purchase UX, transforms, color selection, Randomize, and runtime fidelity.

---

## 17. Automated verification

## 17.1 Domain tests

Add coverage for:

- schema 2 -> 3 migration;
- unknown cosmetic ID preservation;
- unknown color-channel preservation;
- transform-policy validation;
- transform bounds;
- rotation cannot enter the schema;
- category mismatch rejection;
- default fallback resolution;
- multi-channel definition validation while launch entries use one channel;
- owned/free resolution;
- cosmetic entries are never selectable as tools and never appear in the tool shop;
- the locked tool progression schedule is unchanged once cosmetic entries exist;
- reset-progress clears cosmetic ownership;
- a character wearing a cosmetic still loads, renders and saves after that cosmetic's ownership is reset (§8.4);
- randomize never chooses unowned content;
- randomize changes all twelve categories over representative seeds;
- deterministic randomize output for a fixed seed;
- compatibility/hides-hair behavior does not delete Hair selection;
- Cancel snapshot restores character appearance but not purchased ownership.

## 17.2 Headless / Godot scenarios

Recommended scenario IDs:

```text
buddy_studio_schema_migration
buddy_studio_visual_only
buddy_studio_render_layers
buddy_studio_purchase
buddy_studio_unowned_preview
buddy_studio_transforms
buddy_studio_randomize
buddy_studio_paint_preservation
buddy_studio_ui_composition
```

Assertions include:

- same rigid-body count and physics profiles before/after Studio change;
- no cosmetic renderer has collision layers/masks;
- paint textures survive every category change;
- headwear hide/unhide restores hair;
- glasses stay aligned with transformed eyes according to the authored anchor contract;
- semantic face expressions still render with alternative eyes/brows/mouth;
- unowned preview is visible but cannot save;
- purchasing removes the save block without changing selection;
- category grid order is stable.

## 17.3 Real-input journey

Add a journey such as:

`buddy_studio_buy_customize_save_restart`

Journey:

1. start with a known existing character and credits;
2. open `Customize` -> `Buddy Studio`;
3. preview an unowned cosmetic;
4. verify Save is blocked;
5. purchase it;
6. change face features;
7. move/scale a supported feature;
8. change its color;
9. equip headwear/top/shoes;
10. Randomize and verify every resulting item is owned/free;
11. make a final deterministic selection;
12. Save;
13. return to gameplay and verify runtime appearance;
14. restart;
15. verify character appearance and cosmetic ownership restore;
16. verify paint remains unchanged.

A second cancellation leg should prove that a purchase remains owned while unsaved appearance edits are discarded.

---

## 18. Performance budgets

Buddy Studio is not a physics-heavy feature, but it must avoid avoidable churn.

- Do not rebuild the simulation buddy.
- Reuse attachment/render nodes where practical rather than freeing/instantiating all twelve categories on every tile hover.
- Preview thumbnails should be pre-authored or cached; do not render hundreds every frame.
- Compilation remains engine-free and bounded by twelve category selections.
- Color changes update only affected materials/compositor state.
- Previewing a grid item must not trigger character-file I/O.
- Purchase/save I/O remains outside physics processing.
- Randomize is one bounded operation over the catalogue, not a frame-by-frame animation of hundreds of intermediate states.

---

## 19. Failure behavior

- Missing trusted visual: compile/render safe category default and log warning; preserve original ID.
- Invalid transform/color data: validator rejects save/load according to existing character corruption policy.
- Unsupported future character schema: reject as future-version, do not partially rewrite.
- Purchase save failure: ownership remains in dirty progression state for retry under existing persistence semantics; UI surfaces failure.
- Character save failure: working Studio edits remain open and dirty; purchases are unaffected.
- Missing thumbnail: use an original generic missing-preview tile, but do not make an unfinished paid cosmetic visible in production.
- Insufficient credits: no partial debit and no ownership mutation.

---

## 20. Owner acceptance checklist

Buddy Studio is accepted when the owner can verify on a real Windows build that:

- `Customize` exposes `Paint Buddy`, `Paint Background`, and `Buddy Studio` according to feature availability;
- the twelve Studio categories are in the locked order;
- the preview reads as the current buddy, not a separate avatar system;
- unowned cosmetics can be previewed without being purchased;
- price/Buy/Owned states are obvious;
- bought cosmetics work on every buddy;
- Duplicate/character switching does not duplicate or lose ownership;
- Larger/Smaller/Move/Reset appear only where meaningful;
- there is no rotation control;
- move/scale feels controlled and stays in bounds;
- the color UX supports one launch channel cleanly without painting the architecture into a one-channel corner;
- Randomize visibly changes the complete look, including clothing, but uses only free/owned content;
- Randomize never spends money;
- Save refuses an unowned preview with a clear reason;
- Cancel reverts appearance edits but does not refund a purchase;
- existing buddy paint remains unchanged underneath clothing and accessories;
- expressions still work with alternative face features;
- gameplay physics/interaction feel is identical before and after appearance changes;
- the Studio remains usable at 100%, 125%, 150%, and 200% scaling;
- no visible content is a nonfunctional placeholder;
- all automated scenarios and the restart journey pass.

---

## 21. Scope boundary

This plan authorizes implementation planning for Buddy Studio only after scheduling is confirmed. It does not automatically authorize:

- Steam Workshop character packages;
- arbitrary user-imported cosmetic meshes/textures/scripts;
- body/physics shape modification;
- cosmetic stat bonuses;
- paid random rolls;
- rotation controls;
- more clothing categories beyond Headwear, Tops, and Shoes;
- multi-channel launch content merely because the schema supports it;
- a second character persistence system;
- changes to Paint Background / room-decoration implementation beyond the shared `Customize` menu integration.

The goal is one coherent system: **Paint Buddy edits surface paint, Buddy Studio edits owned appearance/clothing, and Paint Background edits the environment**, all reached through the same Win98 `Customize` command surface.
