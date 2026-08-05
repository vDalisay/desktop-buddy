# Desktop Buddy — Post-Win98 Environment Customization Plan

Status: **Future implementation — begins only after the Win98 UI/UX revamp is accepted**  
Depends on: `docs/WIN98_UI_UX_REVAMP_PLAN.md`  
Planning branch: `win98-feel`

## 1. Purpose

After the Windows 98 UI/UX upgrade is complete, Desktop Buddy should expand customization beyond the buddy itself and let the player build a persistent personal play space.

This future phase adds two connected systems:

1. **customizable backgrounds** that players can color, save, export, import, and share;
2. **persistent decorative items** such as chairs, lamps, televisions, tables, shelves, rugs, plants, and similar room objects.

The result should feel like decorating a small room or desktop diorama around the buddy, not spawning temporary gameplay props. The room composition must survive restarts and remain visually consistent across supported window sizes.

## 2. Product boundaries

### Included

- background color customization;
- optional background zones or layers when technically justified;
- named background presets;
- save, duplicate, rename, delete, export, import, and share workflows;
- permanent decorative-item ownership and placement;
- move, rotate, mirror, layer, store, and remove operations where supported;
- persistent room layouts;
- decorative-item catalogue integration with the Shop;
- buddy interactions with selected furniture when separately implemented;
- safe migration of existing saves.

### Excluded from the first release

- unrestricted arbitrary script/mod execution;
- importing executable or scene content from shared packages;
- user-authored shaders;
- multiplayer room synchronization;
- cloud-hosted public galleries;
- physics-heavy construction systems;
- furniture that changes economy, damage, mass, collision, or progression without a separate approved design;
- Steam Workshop dependency for the initial implementation.

Shared content must be treated as untrusted data.

## 3. Locked implementation order

1. **Environment domain model and persistence**
2. **Background color editor and local presets**
3. **Background export/import/share package**
4. **Decorative-item catalogue and ownership**
5. **Placement/edit mode and persistent room layouts**
6. **Optional buddy/furniture interactions**
7. **Platform sharing integrations, including Steam, only after the local format is stable**

Do not begin item placement by serializing live Godot nodes. Define stable domain data first.

## 4. Environment data architecture

Create an engine-independent environment model in the domain layer.

Suggested concepts:

- `EnvironmentProfileId`
- `EnvironmentProfile`
- `BackgroundDefinition`
- `BackgroundZoneDefinition`
- `BackgroundPresetId`
- `DecorationDefinitionId`
- `OwnedDecoration`
- `PlacedDecorationId`
- `PlacedDecoration`
- `EnvironmentLayout`
- `EnvironmentPackageManifest`
- `EnvironmentSchemaVersion`

### 4.1 Environment profile

An environment profile represents one saved room composition and contains at minimum:

- stable profile ID;
- player-facing name;
- background definition or background preset reference;
- ordered placed-decoration records;
- last-edited timestamp if already supported by persistence conventions;
- schema version;
- optional preview-image reference;
- optional source/import metadata that does not affect identity.

The active environment profile should be referenced from player preferences or the existing save root. Reset Progress behavior must follow the already approved reset policy: environment ownership/layout is progression data only if deliberately classified that way; window and application preferences remain separate.

### 4.2 Placement record

Each placed decoration stores data, not a node tree:

- stable placed-instance ID;
- catalogue definition ID;
- normalized or canonical room position;
- rotation from an approved discrete or clamped range;
- mirror state when supported;
- visual scale only when explicitly allowed by the item definition;
- render layer/depth band;
- anchor type, such as floor, wall, tabletop, or free background plane;
- optional item-specific visual state, such as lamp on/off or TV channel skin;
- no arbitrary resource path supplied by imported data.

### 4.3 Definition catalogue

Decorative item behavior and asset references belong to trusted project-owned definitions. Imported layouts may reference known definition IDs but may not provide executable scenes, scripts, shaders, or arbitrary filesystem paths.

Each definition should declare:

- ID and display name;
- category;
- owned/unlocked requirements;
- trusted scene or visual resource;
- preview icon;
- supported anchors;
- placement footprint;
- rotation/mirroring rules;
- scale policy;
- render-depth policy;
- whether the buddy can interact with it;
- whether it blocks buddy movement;
- optional state schema;
- compatibility/version information.

Unknown definition IDs must fail safely and remain visible in diagnostics without breaking the whole layout.

## 5. Customizable backgrounds

### 5.1 Initial background model

The first release should support reliable color customization before adding complex painting.

Minimum background options:

- one solid background color;
- optional second color for a simple vertical or horizontal split;
- optional floor color separate from wall/background color;
- a small set of trusted project-owned patterns or textures with color tinting;
- reset to default;
- live preview;
- accessible color values and preset swatches.

Use explicit background zones where the scene visually contains distinct wall, floor, trim, or panel regions. Do not infer regions from pixels at runtime.

### 5.2 Color editor UX

Expose the system through the completed Win98 UI framework, likely as a `Background` or `Room` category in the persistent command bar.

The editor should provide:

- background preset list;
- selected-zone list;
- large selected-color well;
- existing project palette and full color picker;
- optional RGB/HSV fields when already supported by the shared color picker;
- Apply, Save As, Duplicate, Rename, Delete, Reset, Export, and Import actions;
- dirty-state indication;
- Save / Discard / Cancel handling on close or profile switch;
- status text for validation and import results.

Only real background zones should be shown. Do not display disabled placeholder layers.

### 5.3 Persistence

Background colors must persist independently from the character paint system. Character paint belongs to a character; background presets and environment profiles belong to the room/environment system.

Saving should be atomic:

1. validate the complete model;
2. write to a temporary file through the existing persistence abstraction;
3. replace the prior file only after success;
4. retain or recover the last known-good state after failure.

### 5.4 Background preset management

Players can:

- create a named preset from the current background;
- duplicate an existing preset;
- rename it;
- delete it with confirmation;
- apply it without replacing the entire room layout;
- export it as a shareable package;
- import a compatible package.

Names require trimming, length limits, invalid-character handling, and deterministic duplicate-name behavior.

## 6. Saving and sharing backgrounds

### 6.1 Share format

Define a versioned, deterministic package format before platform integration.

Recommended initial extension:

- `.dbbackground` for background-only packages;
- reserve `.dbenvironment` for future complete room layouts.

A background package should be a ZIP-compatible container or another simple documented container containing:

- `manifest.json`;
- background data JSON;
- optional preview PNG;
- no executable content;
- no external absolute paths;
- no user-selected arbitrary files in the initial version.

### 6.2 Manifest

The manifest should include:

- package type;
- schema version;
- package ID;
- display name;
- creator-entered author name, optional and untrusted display text;
- created-with game version;
- minimum compatible version;
- included file list with hashes;
- feature flags;
- optional description;
- optional original package ID for derivatives.

Do not treat author fields as authenticated identity.

### 6.3 Import safety

Imports must:

- enforce compressed and uncompressed size limits;
- reject path traversal and absolute paths;
- reject symlinks and unexpected file types;
- limit file count;
- validate JSON depth, length, enums, and numeric ranges;
- verify declared hashes;
- ignore or reject unknown executable-capable content;
- generate a new local ID on collision unless the user explicitly replaces a matching local package;
- show a preview and summary before committing;
- never overwrite an existing preset silently;
- remain off the physics tick.

Malformed packages must produce a readable error and leave current data unchanged.

### 6.4 Sharing workflow

Initial sharing is local and platform-independent:

1. player selects a background preset;
2. Export opens an in-game Win98-style destination workflow or a controlled native save path only where already approved;
3. the game writes the versioned package and optional preview;
4. another player imports it through the Background editor;
5. validation runs before the preset is added;
6. imported content receives a visible imported/source marker.

Steam Workshop or other platform publishing may wrap this same package later. Platform code must not define a second incompatible serialization format.

### 6.5 Preview generation

Preview images are derived artifacts and may be regenerated. They should:

- use bounded dimensions;
- exclude unrelated private filesystem information;
- render only the environment/background intended for sharing;
- be written asynchronously outside physics processing;
- fail without invalidating the underlying preset.

## 7. Permanent decorative items

### 7.1 Item categories

Initial catalogue candidates:

- chairs and stools;
- floor and desk lamps;
- televisions/monitors;
- small tables and desks;
- shelves and cabinets;
- rugs and mats;
- plants;
- wall art and posters;
- clocks;
- speakers/radios;
- cushions, beds, or resting spots;
- small non-interactive ornaments.

The first vertical slice should use three items with different placement rules:

1. chair — floor anchored;
2. lamp — floor anchored with an on/off visual state;
3. wall-mounted TV or picture — wall anchored.

This tests placement, anchoring, persistence, state, depth, and resizing without requiring the full catalogue.

### 7.2 Ownership versus placement

Keep these concepts separate:

- **owned decoration:** the player has unlocked or purchased the catalogue item;
- **placed decoration:** one instance of an owned item currently exists in an environment layout.

A single owned item may support either one placed instance or unlimited placed instances according to its definition. This rule must be explicit and deterministic.

### 7.3 Shop integration

Permanent decorative items belong in a dedicated Shop category such as `Decor` or `Room`.

The Shop must show:

- item preview;
- price;
- ownership/quantity;
- anchor type;
- whether it is interactive;
- purchase action;
- Place action for owned items;
- clear distinction from temporary gameplay tools.

Purchasing does not immediately spawn the item into an arbitrary location. It unlocks ownership and may optionally enter placement mode with an explicit preview.

## 8. Decoration placement/edit mode

### 8.1 Entry and exit

Placement is a deliberate editing mode, not normal gameplay drag behavior.

Enter through:

- the Room/Decor command-bar category;
- Place from an owned decoration;
- Edit Room from the environment profile menu.

Exit through:

- Done/Save;
- Cancel, restoring the pre-edit snapshot;
- Escape with confirmation when dirty;
- mode transition handling that follows the shared UI ownership bridge.

### 8.2 Placement interactions

Required operations:

- select an owned item;
- show a ghost preview before placement;
- snap to valid anchor regions;
- communicate invalid placement before committing;
- place;
- select an existing item;
- move;
- rotate;
- mirror where supported;
- move forward/back within a bounded depth band where supported;
- toggle item-specific visual state;
- return to storage/remove from layout;
- undo and redo within the edit session;
- save or cancel the complete edit session.

### 8.3 Coordinate strategy

Persist placement in a resolution-independent coordinate space tied to a canonical environment canvas or explicit anchor surfaces.

Do not store raw OS screen coordinates. At runtime:

- map canonical coordinates into the current buddy viewport;
- preserve item relationships during resize;
- clamp or recover items when the viewport becomes too small;
- keep title bars, command bars, status bars, and other UI chrome outside the room coordinate space;
- maintain deterministic z-order.

### 8.4 Collision and buddy movement

Decorations are visual-only by default.

Items affect buddy navigation or physical interaction only when their trusted definition explicitly opts in and a separate testable behavior exists. A chair may eventually allow sitting, but merely placing a chair must not silently change physics.

Interactive furniture should use narrow interfaces such as:

- `IBuddyRestTarget`
- `IBuddySitTarget`
- `IBuddyWatchTarget`
- `IDecorationToggleTarget`

Avoid a single universal decoration script with unrelated responsibilities.

### 8.5 Render ordering

Use explicit bands rather than arbitrary unbounded z values:

- far background;
- wall decoration;
- behind-buddy floor decoration;
- buddy/gameplay plane;
- front-of-buddy decoration;
- UI overlay.

Each definition declares allowed bands. Imported layout data cannot elevate decorations above UI or outside permitted bands.

## 9. Environment profiles and complete layouts

After background presets and decoration placement are stable, allow complete environment profiles.

Players can:

- save the current background plus all placed items as a named room;
- create multiple rooms;
- duplicate and rename rooms;
- switch active room;
- delete a room with confirmation;
- export/import complete layouts through `.dbenvironment` packages;
- optionally apply only the background or only the decoration arrangement during import.

A complete environment package references trusted catalogue definition IDs. Missing items should be listed before import. The importer may either skip missing items or import the layout as partially unresolved; it must not grant paid/unowned items unless the product design explicitly permits shared decorative layouts to do so.

## 10. Save compatibility and migration

Introduce a schema version from the first implementation.

Migration rules:

- existing saves receive a default environment profile;
- missing background data resolves to the current default background;
- missing decoration collections resolve to empty;
- unknown fields are ignored only when forward-compatible behavior is safe;
- unsupported newer schema versions are rejected with a clear message rather than partially corrupted;
- unknown decoration IDs are preserved as unresolved records where practical so they can return after content restoration;
- migrations are engine-independent and covered by tests.

## 11. UI integration after the Win98 upgrade

The completed shared Win98 UI foundation must be reused.

Recommended command-bar structure:

- Shop
  - Tools
  - Clothing
  - Decor
- Room
  - Background
  - Decorations
  - Rooms
  - Import / Export
- Settings

The exact hierarchy may be adjusted after usability review, but environment customization must remain in-scene and must not reintroduce inaccessible detached game windows.

The room editor should use:

- a classic catalogue/list pane;
- a central recessed environment viewport;
- a compact property pane or attached drawer;
- status-bar placement guidance;
- visible Save, Cancel, Undo, and Redo states;
- keyboard focus and shortcuts consistent with the Win98 revamp.

## 12. Verification requirements

### 12.1 Engine-independent tests

- background color validation;
- preset naming and identity;
- environment profile serialization round trips;
- placement normalization and clamping;
- schema migration;
- ownership versus placed-instance rules;
- package manifest validation;
- hash validation;
- archive path traversal rejection;
- size/file-count limits;
- unknown definition handling;
- import collision behavior;
- cancel restoring the original edit snapshot.

### 12.2 Godot/headless tests

- catalogue definitions resolve trusted scenes;
- anchor validation;
- canonical-to-viewport coordinate mapping;
- resize preservation;
- deterministic render bands;
- placement preview validity;
- room switching;
- background live preview;
- lamp/TV visual-state restoration;
- missing-resource fallback rendering;
- no imported data creates arbitrary nodes or loads arbitrary paths.

### 12.3 Standalone Windows scenarios

- create and save a background preset;
- restart and verify restoration;
- export and re-import a background;
- import malformed and oversized packages safely;
- purchase/place/move/rotate/store chair, lamp, and TV;
- resize at minimum/default/large window sizes;
- test 100%, 125%, 150%, and 200% display scaling;
- switch windowed/full interaction modes without losing room state;
- verify click ownership between room editor, buddy, and command bar;
- recover layouts after monitor/resolution changes;
- verify no file work occurs on the physics tick.

## 13. Delivery slices

### Slice E0 — environment domain and persistence

- versioned domain model;
- default environment migration;
- repository/persistence abstraction;
- engine-independent serialization and migration tests.

### Slice E1 — background colors and presets

- background zones;
- live color editor;
- preset CRUD;
- active-background persistence;
- dirty-state and reset behavior.

### Slice E2 — safe background sharing

- `.dbbackground` manifest and package writer;
- validator/importer;
- bounded preview generation;
- import/export UI;
- malformed-package test corpus.

### Slice E3 — decoration catalogue and ownership

- trusted definitions;
- Decor shop category;
- purchase/ownership persistence;
- chair, lamp, and TV vertical-slice assets.

### Slice E4 — placement mode

- canonical room coordinates;
- ghost preview and anchor validation;
- move/rotate/mirror/store;
- undo/redo and save/cancel transaction;
- resize and input-ownership tests.

### Slice E5 — persistent room profiles

- background plus decoration layout;
- room CRUD and switching;
- `.dbenvironment` local share format;
- missing-item resolution rules.

### Slice E6 — furniture interactions

- opt-in interaction interfaces;
- chair sitting/resting;
- lamp toggle;
- TV watching/idle behavior;
- gameplay and physics regression tests.

### Slice E7 — platform sharing integration

- Steam or other platform wrapper around the stable local package;
- publish/update/download workflow;
- platform metadata mapping;
- moderation/reporting considerations;
- offline behavior remains fully functional.

## 14. Acceptance criteria

This future phase is complete when:

- players can recolor the supported background zones and save named presets;
- saved backgrounds restore correctly after restart;
- players can export and import safe versioned background packages;
- malformed shared files cannot mutate the active save or load executable content;
- permanent decorative items are distinct from temporary tools;
- owned decorations can be placed, edited, stored, and restored;
- room layouts remain stable during resize, DPI changes, and mode transitions;
- chair, lamp, and TV prove floor anchoring, wall anchoring, persistent state, and render ordering;
- environment customization uses the shared Win98 UI and does not spawn ordinary detached windows;
- existing saves migrate to a valid default environment;
- automated, headless, and standalone verification passes;
- the owner accepts the running background editor, sharing workflow, and room-decoration UX.

## 15. Definition of future scope boundary

This document records the intended work after the Win98 UI upgrade. It does not authorize implementation before the active UI revamp reaches its owner acceptance gate. During the Win98 work, only seams that reduce later rework may be introduced, such as typed command-category registration or a neutral environment viewport host. No visible unfinished Background, Room, or Decor controls should ship before their behavior is real.