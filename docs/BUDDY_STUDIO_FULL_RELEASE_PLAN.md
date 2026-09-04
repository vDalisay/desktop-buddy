# Desktop Buddy — Buddy Studio Full-Release Expansion Plan

Status: **Owner-approved future scope — full release, not demo/current Buddy Studio closure**  
Recorded: 2026-08-10  
Builds on: `docs/BUDDY_STUDIO_CUSTOMIZATION_PLAN.md`

This plan records the post-launch Buddy Studio expansion. It does not change the current Buddy Studio completion gate. The current implementation should finish with trusted project-authored cosmetics first; the features below begin only after that representation, persistence, rendering and Steam foundation are stable.

---

## 1. Full-release goals

The full release expands Buddy Studio in three directions:

1. **Player-drawn cosmetics** — players can create their own visual cosmetic for any supported Buddy Studio category by painting into a category-specific template, then wear it on the buddy.
2. **Bounded cosmetic stretching** — supported cosmetics can be gently deformed after selection while remaining attached to their authored anchor, enabling substantially more individual fitting than uniform scale alone.
3. **Buddy Studio UI revamp** — the Studio gets a second UX/layout pass designed around a much larger catalogue plus creation/editing workflows rather than only the launch selection/purchase flow.

Player-created cosmetics can also be shared through Steam once the Steam sharing/moderation layer exists.

These remain visual-only systems. They never change buddy physics, collision, damage, movement, mood, economy tuning or paint UV geometry.

---

## 2. Player-drawn cosmetic system

### 2.1 Product behavior

A player may create a custom cosmetic for **every Buddy Studio category for which a safe drawable template exists**, including Hair, Eyebrows, Eyes, Nose, Mouth, Ears, Accessories, Glasses, Headwear, Tops, Shoes and future drawable Face treatments.

The intended workflow is similar in spirit to classic template-based drawing games: the player is given a bounded 2D drawing region that clearly communicates where the cosmetic will appear, paints inside that region, and sees the result mapped onto the buddy at the category's trusted attachment/overlay location.

The implementation is clean-room. Do not copy Nintendo/Drawn to Life art, UI, templates, assets or source behavior. The project only adopts the generic idea of **paint a constrained template -> map the authored image onto a predefined character region**.

### 2.2 Template contract

Player-created cosmetics are not arbitrary meshes, scenes or scripts. Every drawable category uses a trusted project-authored `CosmeticDrawingTemplate`.

A template should define engine-free metadata such as:

```text
TemplateId
Category
CanvasWidth / CanvasHeight
PaintableMask
Optional symmetry rule
Default anchor
Trusted mapping kind
Trusted render band
Safe visible bounds
Default transform
Allowed transform/deformation capabilities
Thumbnail framing
```

Engine-side registration maps that template ID to trusted rendering/mapping resources. User data stores only template IDs, bounded paint data and bounded authoring parameters.

Templates may use different mappings depending on category:

- flat front-facing decal/overlay;
- paired mirrored decal;
- trusted head-crown attachment plane;
- trusted torso/foot shape-replacement surface;
- a predefined wrapped cosmetic surface when a category genuinely needs it.

The Tops/Shoes mapping is a **shape replacement**, not an overlay, per the 2026-08-12 owner decision in `docs/DECISIONS.md`. A drawn Top authors the torso's own surface; it is never a layer in front of it. The generated-mesh UV contract in `docs/ASSET_FORGE_IMPLEMENTATION_PLAN.md` Section 9.8 is the same contract these templates use — one paint-layout version, not two.

No custom cosmetic file may supply an arbitrary resource path, shader, material, script, mesh or executable behavior.

### 2.3 Painting workspace

Buddy Studio gains a dedicated **Create / Edit Custom Cosmetic** workflow rather than overloading the normal catalogue tile selector.

Recommended flow:

```text
Buddy Studio
  -> choose category
  -> Create Custom
  -> choose available template for that category
  -> custom cosmetic paint workspace
       - template silhouette / safe-area guide
       - paint canvas
       - color palette / picker
       - Brush
       - Spray
       - Eraser
       - Undo / Redo
       - optional Fill where the template semantics support it
       - zoom / pan
       - live buddy preview
  -> name cosmetic
  -> Save Locally
  -> cosmetic appears in My Creations
```

Reuse the proven paint algorithms and UI semantics from Paint Buddy / Paint Background where their contracts actually match. Do not create another unrelated brush engine merely because this editor lives inside Buddy Studio.

Template guides are editor-only. They are not baked into the player's final artwork.

### 2.4 Mapping and runtime rendering

A custom cosmetic compiles through the same trusted Buddy Studio category/anchor pipeline as authored cosmetics.

Conceptually:

```text
CustomCosmeticDocument
  -> resolve trusted template
  -> validate image dimensions / payload / parameters
  -> upload trusted texture
  -> instantiate trusted category renderer
  -> apply authored transform/deformation
  -> attach to normal cosmetic anchor
```

The renderer never infers gameplay geometry from painted alpha. A huge painted hat does not enlarge head collision; painted shoes do not change foot bodies.

Custom cosmetics should obey normal compatibility rules such as `HidesHair` only when the selected trusted template declares that behavior. User artwork cannot create new compatibility behavior.

### 2.5 Local custom cosmetic document

Recommended durable shape:

```text
CustomCosmeticDocument
  SchemaVersion
  LocalId
  DisplayName
  TemplateId
  Category
  CreatedUtc / ModifiedUtc
  PaintAssetReference
  Transform
  OptionalDeformation
  ThumbnailReference
  ExtensionData
```

The image payload should live in a whitelisted per-cosmetic asset directory, while metadata stays small and versioned.

Stable local IDs must survive rename. Renaming a custom cosmetic changes only display metadata.

Use the project's existing atomic staged-write/backup/quarantine discipline. Unsupported future versions are preserved/rejected safely rather than partially rewritten.

### 2.6 Buddies referencing custom cosmetics

A character selection may reference either:

- a trusted shipped cosmetic ID; or
- a stable local custom-cosmetic ID.

Unknown/missing custom IDs remain recoverable. The character document keeps the reference, the renderer falls back safely, and the Studio explains that the custom item is unavailable rather than deleting the selection silently.

Duplicating a buddy must not duplicate the underlying custom cosmetic asset; both characters can reference the same local cosmetic ID.

Deleting a custom cosmetic that is currently worn requires an explicit dependency warning. Recommended behavior is to block deletion until the player confirms that affected characters will fall back visually while preserving their unresolved reference for recovery/import.

---

## 3. Steam sharing for custom cosmetics

### 3.1 Scope

Sharing the custom-cosmetic format described in this section is **full release only** and depends on the Milestone 6 Steam/platform layer. This does not defer Workshop v1 sharing of the existing Buddy Studio configuration plus declared buddy paint, which is included in the Steam Demo. The local custom-cosmetic format must be stable before its publishing/downloading is implemented.

The share unit is one safe custom cosmetic package, not an arbitrary game mod.

Recommended package contents:

```text
manifest.json
paint.png
thumbnail.png
```

The manifest contains only bounded declarative metadata such as schema version, template ID, category, display name and optional author/share metadata. It contains no code or arbitrary file references.

### 3.2 Import validation

Downloaded packages are untrusted input.

Validate before installing:

- supported package schema;
- known trusted TemplateId;
- category/template agreement;
- exact allowed image dimensions and formats;
- decoded pixel and encoded byte limits;
- no path traversal;
- no extra executable/script/shader/scene files;
- bounded transform/deformation data;
- safe UTF-8 metadata lengths;
- thumbnail limits;
- package aggregate size cap.

Imports receive a new local installation identity separate from the remote Steam item ID so local persistence is not coupled to Workshop availability.

Removing/unsubscribing a Steam item follows the same unresolved-reference behavior as a missing local cosmetic; do not mutate character files destructively.

### 3.3 Publishing lifecycle

Recommended UX:

```text
My Creations
  -> select custom cosmetic
  -> Share
  -> preview publish metadata
  -> validate package locally
  -> Steam publish/update
  -> show publishing state / Steam item identity
```

Updating a published cosmetic should preserve its Steam item identity when the player explicitly updates that creation. "Save As New" creates a different share item.

Moderation, legal/UGC notices, visibility controls and report flows use the final Steam policy pass rather than being guessed inside Buddy Studio.

---

## 4. Bounded cosmetic stretching / deformation

### 4.1 Product behavior

A later full-release customization layer allows supported cosmetics to be **slightly stretched and squashed locally**, conceptually similar to a gentle face-warp/deformation editor rather than free arbitrary mesh editing.

This is separate from launch uniform scale. The cosmetic should remain attached to its normal anchor while portions of its visual shape can be pulled within safe bounds.

Examples:

- make one hairstyle wider near the top but not taller everywhere;
- slightly pull the side of glasses outward;
- make a mouth cosmetic broader on one side within a symmetry policy;
- adjust a hat crown/profile without detaching it from the head anchor.

The implementation must not copy Mario 64 face assets, interaction visuals or code. Only the generic notion of bounded interactive deformation is relevant.

### 4.2 Representation

Prefer a small normalized deformation lattice/control cage rather than persisting arbitrary geometry.

Example conceptual representation:

```text
CosmeticDeformation
  Version
  ControlPoints[]
    NormalizedX
    NormalizedY
    DeltaX
    DeltaY
```

A trusted template/definition declares:

- whether deformation is allowed;
- cage topology/control-point count;
- per-point movement bounds;
- symmetry constraints, if any;
- maximum aggregate deformation;
- protected anchor regions that cannot be dragged away from their attachment.

The user never adds/removes control points or changes topology.

### 4.3 Stay-in-place invariant

The deformation is evaluated in cosmetic-local coordinates **after anchor placement but before final rendering**.

The attachment origin and protected anchor points remain fixed. Stretching cannot translate the whole cosmetic off the buddy or make it detach during animations.

For paired cosmetics, definitions choose one of:

- mirrored deformation;
- synchronized paired deformation;
- independently editable pair, only when intentionally supported.

### 4.4 Visual-only safety

Deformation affects only cosmetic presentation. It must not:

- change physics/collision;
- alter Paint Buddy UV mapping;
- move buddy body parts;
- change semantic expression state;
- create a generalized runtime mesh editor.

Bounds are validated in the domain/document layer, not only clamped visually in the UI.

---

## 5. Full-release Buddy Studio UI revamp

The launch/current Studio is allowed to optimize for selecting, previewing, buying and fitting shipped cosmetics. The full release needs another UX pass because it must also support local creations, Steam content and deformation editing without turning one screen into an overloaded control panel.

### 5.1 UX goals

The revamp should make these top-level activities deliberate:

```text
Customize
  Browse / Equip
  My Creations
  Create / Edit
  Shared / Steam
```

These may be tabs, modes, or another compact Win98-native navigation structure after a dedicated mockup/research pass. Do not bolt all four into the current right inspector as more buttons.

The visual identity remains Desktop Buddy's Win98 application language, but layout is not frozen to the launch Studio arrangement.

### 5.2 Browse / Equip

Needs to handle a much larger combined collection:

- shipped free/paid cosmetics;
- owned cosmetics;
- local custom cosmetics;
- installed Steam cosmetics.

Recommended capabilities for the revamp:

- category filter remains primary;
- source filter (`Game`, `Mine`, `Shared`) when useful;
- search for large libraries;
- deterministic sorting;
- robust grid virtualization/lazy thumbnails;
- selected-item inspector that clearly distinguishes source/ownership/editability;
- no internal IDs in normal player-facing copy.

### 5.3 Create / Edit

Creation should be a focused authoring workspace with a larger live preview and paint/deformation controls. Avoid trying to paint a template inside a tiny catalogue tile panel.

Creation mode should make three stages visually clear:

1. choose template;
2. paint / fit / deform;
3. save/name/share.

### 5.4 My Creations management

Provide explicit management for:

- rename;
- duplicate creation;
- edit;
- delete with dependency warning;
- publish/update through Steam when available;
- identify whether a creation is local-only or published.

### 5.5 Shared content

Steam browsing itself may use Steam's platform surfaces where appropriate, but installed shared cosmetics must integrate naturally into Buddy Studio after download. Do not make users manage installed cosmetics through a disconnected debug/import window.

### 5.6 Responsive/focus requirements

The revamp retains:

- in-scene UI ownership;
- keyboard focus/navigation;
- tooltip/status help;
- no detached ordinary child windows;
- 100/125/150/200% DPI verification;
- minimum/default/maximized layouts;
- modern usable hit targets under period styling.

---

## 6. Architecture boundaries

### 6.1 Reuse, do not fork

Full-release features should extend the current systems:

- `CharacterFeatureSlot` / Buddy Studio category contracts;
- trusted cosmetic anchor/render pipeline;
- character working-copy semantics;
- paint algorithms where applicable;
- shared palette/color controls;
- platform abstraction for Steam;
- atomic file-store patterns.

Do not create a second character renderer or a general mod loader.

### 6.2 Trusted templates are the security boundary

The key full-release extension is **user-authored pixels and bounded parameters inside project-authored templates**, not user-authored executable rendering definitions.

That distinction must remain explicit in schema, imports and Steam packaging.

### 6.3 Paint and custom cosmetics remain separate assets

Paint Buddy edits the buddy's canonical body paint surfaces. A player-created Hair/Tops/etc. cosmetic is its own asset selected through Buddy Studio. Creating or editing one must never rewrite the character's body-paint PNGs, and equipping a shape replacement hides the underlying painted surface rather than editing it.

---

## 7. Suggested implementation slices

These begin only after current Buddy Studio closure and the required platform prerequisites.

### RELEASE-BS1 — custom cosmetic document + trusted template contract

Deliver:

- engine-free custom cosmetic schema;
- trusted drawable template definitions;
- local ID/reference rules;
- atomic store + migration/quarantine behavior;
- character-reference resolution;
- payload and path validation.

Gate:

- custom items can round-trip locally with no arbitrary engine-resource reference capability.

### RELEASE-BS2 — custom cosmetic painting vertical slice

Deliver Hair first as the vertical slice:

- one trusted Hair drawing template;
- paint workspace;
- live mapped preview;
- save/rename/duplicate/delete lifecycle;
- runtime equip/save/restart.

Gate:

- painted Hair maps predictably to its trusted region, survives restart, stays visual-only and does not alter body paint.

### RELEASE-BS3 — drawable templates across categories

Deliver safe templates for the remaining categories where meaningful.

Gate:

- every visible `Create Custom` category has a real template and runtime renderer; categories without a safe template hide creation rather than expose a dead button.

### RELEASE-BS4 — bounded deformation/stretching

Deliver:

- definition/template deformation policies;
- fixed control lattice;
- direct manipulation UI;
- protected anchor constraints;
- persistence + migration;
- paired/symmetry policy.

Gate:

- maximum deformation remains visually attached and never changes physics or body paint mapping.

### RELEASE-BS5 — Steam custom-cosmetic sharing

Depends on Milestone 6 Steam/platform APIs.

Deliver:

- safe package builder/parser;
- local validation before publish;
- install/import validation;
- Steam publish/update/download integration;
- local-vs-remote identity handling;
- missing/unsubscribed recovery semantics;
- policy/moderation UX required by the final platform integration.

Gate:

- hostile/malformed package corpus cannot escape whitelisted data formats or create executable/resource injection paths.

### RELEASE-BS6 — Studio UX revamp

Run a dedicated mockup/interaction pass against the now-real feature set, then implement the Browse / My Creations / Create-Edit / Shared workflows with a responsive Win98-native structure.

Gate:

- large local/shared libraries remain navigable and creation does not overload the normal equip flow.

### RELEASE-BS7 — full-release closure

Deliver:

- migration rehearsal from current-release characters/Studio data;
- large-library performance test;
- Steam install/uninstall/restart tests;
- custom cosmetic corruption/recovery tests;
- deformation stress tests;
- full Windows DPI/input matrix;
- owner visual and usability review.

---

## 8. Verification plan

Automated coverage should include at minimum:

- custom cosmetic schema round-trip and migration;
- exact template/category validation;
- path traversal and unexpected-file rejection;
- image decompression/pixel/byte caps;
- unknown template/custom ID preservation;
- character wearing custom item survives restart;
- deleting/uninstalling a referenced item uses safe fallback without erasing the reference;
- custom item never creates collision/physics nodes;
- body paint hashes unchanged after custom cosmetic create/edit/equip;
- deterministic paint Undo/Redo where shared paint infrastructure is reused;
- deformation control points remain bounded;
- protected anchors remain fixed after extreme permitted deformation;
- paired/symmetry deformation policy;
- save/reload deformation fidelity;
- published package round-trip;
- malformed Steam package corpus rejection;
- local duplicate and Steam identity do not alias accidentally;
- library virtualization/search does not change deterministic ordering;
- no current launch cosmetic or character becomes invalid after schema migration.

Recommended journeys:

```text
buddy_studio_create_custom_hair_save_restart
buddy_studio_custom_cosmetic_deform_restart
buddy_studio_publish_install_custom_cosmetic
buddy_studio_missing_shared_cosmetic_recovery
```

---

## 9. Explicit current-release stop line

Do not implement these features merely to finish the current Buddy Studio branch:

- player-authored cosmetic painting templates;
- custom cosmetic Steam sharing;
- deformation/stretch cages;
- the large-library/creation-oriented Studio UI revamp.

The current release should finish and validate the trusted shipped-cosmetic system first. Architecture may leave narrow extension seams for this plan, but must not add speculative Workshop services, generalized user mesh import, scripting, arbitrary shader support or a second cosmetic persistence stack before the full-release gates above are active.
