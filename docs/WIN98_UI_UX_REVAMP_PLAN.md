# Desktop Buddy — Windows 98 UI/UX Revamp

Status: **Owner approved — 2026-08-05**  
Implementation branch: `win98-feel`

## 1. Product direction

Desktop Buddy's player-facing windows, menus, and character editor will be rebuilt as one coherent desktop-application interface strongly inspired by the broad visual language of Windows 98 and classic MS Paint.

The implementation must be original and clean-room. It may reproduce generic period conventions—square controls, raised/recessed bevels, gray chrome, navy title bars, menu strips, status bars, classic scrollbars, compact icon toolbars, list boxes, and palette wells—but must not copy Microsoft artwork, icons, fonts, logos, exact branded text, or source assets.

The target is **near pixel-perfect late-1990s desktop UI proportions and interaction feedback**, combined with modern usability knowledge:

- consistent alignment and spacing;
- larger invisible hit targets where necessary;
- clear focus, hover, pressed, disabled, selected, and destructive states;
- keyboard navigation and visible focus;
- responsive resizing rather than a fixed-resolution mockup;
- no rounded modern cards, glass effects, or detached mobile-style popovers;
- no new native OS child windows for ordinary game menus.

## 2. Locked implementation order

1. **Windowed-mode game shell around the buddy**
2. **Persistent horizontal command bar for Shop, Tools, and Settings**
3. **Character editor and direct-paint workspace**

The shared Win98 control/theme foundation is built before the first phase and reused by every later phase.

## 3. Shared Win98 UI foundation

Create a focused reusable theme/component layer rather than styling every screen independently.

### 3.1 Theme tokens

Centralize at minimum:

- desktop gray;
- light face gray;
- raised highlight;
- raised midtone;
- dark shadow;
- darkest outline;
- active navy title color;
- inactive title color;
- title text color;
- selection navy and selection text;
- disabled text and embossed disabled highlight;
- warning/error colors;
- spacing units;
- canonical control heights;
- canonical title-bar height;
- canonical border widths;
- icon sizing tiers.

### 3.2 Required reusable controls

- `Win98WindowFrame`
- `Win98TitleBar`
- `Win98MenuBar`
- `Win98StatusBar`
- raised, pressed, toggle, default, destructive, and disabled buttons
- recessed panel / field
- raised group panel
- list box and selectable list row
- tab/category button
- toolbar icon button
- horizontal and vertical scrollbars
- splitter/resizer handle
- palette swatch and selected-color well
- tooltip
- modal confirmation panel
- keyboard focus ring

### 3.3 State rules

All interactive controls require deterministic visual states:

- normal;
- hover;
- pressed;
- toggled/selected;
- keyboard-focused;
- disabled;
- destructive confirmation.

Pressed controls invert their raised bevel into a recessed bevel and offset contents by one logical pixel. Selection must never rely on color alone.

### 3.4 Scaling and resize rules

- Use responsive containers and minimum sizes, not coordinate-only layouts.
- Preserve crisp integer-like borders at supported display scales.
- Sidebars keep bounded widths while the main viewport receives remaining space.
- At narrow widths, secondary panes collapse into tabs or drawers inside the same scene; they do not spawn separate OS windows.
- Text may reflow where appropriate, but toolbar controls retain stable dimensions.
- The buddy viewport must remain usable at the minimum supported window size.

## 4. Phase 1 — Windowed-mode buddy application shell

### 4.1 Goal

Windowed Play mode becomes a deliberate self-contained retro application window rather than an unframed buddy plus disconnected controls.

### 4.2 Required composition

The scene contains:

1. outer window border;
2. active title bar with original Desktop Buddy app icon;
3. title text reflecting the current buddy or mode;
4. window-command buttons mapped to game-safe actions;
5. optional application menu row where commands are useful;
6. recessed central buddy play area;
7. status bar for concise contextual information;
8. resize grip where resize is supported.

### 4.3 Window commands

The classic title-bar controls are visual analogues, not automatic promises of unsupported native-window behavior.

- **Minimize:** invoke the existing supported minimize/hide path.
- **Maximize/restore:** switch between the deliberate full interaction mode and the prior windowed bounds where supported.
- **Close:** use the game's existing safe close/exit behavior and confirmation policy.

The shell must preserve the existing window/input ownership model and may not regress click-through, buddy interaction, dock interaction, editor restoration, or tray behavior.

### 4.4 Dragging

- Dragging starts from the title bar and any explicitly designated drag handle only.
- Buttons, menu items, and embedded controls consume pointer input and never initiate window dragging.
- The title bar exposes clear hover/pressed feedback during drag initiation.
- Clamp/recovery behavior keeps the application retrievable after resolution or monitor changes.

### 4.5 Responsive layout

- The play area expands with the window.
- Title bar and status bar retain canonical heights.
- The buddy camera/framing adapts without stretching the buddy.
- Minimum dimensions prevent controls from overlapping or becoming unreachable.

### 4.6 Phase 1 acceptance criteria

- Windowed mode reads immediately as a cohesive Win98-style app.
- Buddy grab/tool interactions still work.
- Menu and title-bar hit regions do not leak clicks into gameplay.
- Drag, minimize, maximize/restore, close, resize, monitor clamp, and saved-bound restoration behave deterministically.
- 100%, 125%, 150%, and 200% Windows scaling are visually checked.
- No separate ordinary menu window is created.

## 5. Phase 2 — Persistent Shop / Tools / Settings command bar

### 5.1 Goal

Replace the current horizontal selection strip with a persistent, expandable command bar that is visually and behaviorally part of the same application.

### 5.2 Structure

The bar provides stable top-level entries:

- Shop
- Tools
- Settings

Additional future categories may be registered through the same typed model, but the implementation must not show nonfunctional controls.

### 5.3 Interaction model

- One top-level category may be expanded at a time.
- Selecting the active category collapses it.
- Expanded content opens as an in-scene raised/recessed panel attached to the bar.
- Escape closes the active panel.
- Focus returns to the invoking category button.
- Clicking gameplay outside the panel closes it only when doing so does not consume an intended tool interaction.
- The bar remains present unless the selected deliberate mode explicitly hides it.

### 5.4 Shop panel

- Classic list or icon-grid presentation with clear selected item state.
- Name, price, ownership state, availability, and action are readable without opening another OS window.
- Disabled purchases explain why through status text/tooltip.
- Permanent upgrades and selectable tools have distinct category labels and action wording.

### 5.5 Tools panel

- Current tool is visibly latched.
- Selecting a tool updates gameplay without closing and reopening external windows.
- Tool detail/help appears in the status area or attached information pane.
- Keyboard and pointer selection are both supported.

### 5.6 Settings panel

- Use period-appropriate checkboxes, radio buttons, list boxes, sliders, and dropdown-like selectors.
- Changes that can apply immediately do so.
- Destructive/reset actions require explicit confirmation.
- Settings remain within the game scene.

### 5.7 Phase 2 acceptance criteria

- Shop, Tools, and Settings are reachable from one persistent bar.
- No category creates an inaccessible separate window.
- Buddy interaction and bar interaction coexist in windowed mode.
- Layout remains usable during resize.
- Open/closed category state and keyboard focus are deterministic.

## 6. Phase 3 — Character editor and paint workspace

### 6.1 Goal

Recompose the character editor as an original classic-paint-program workspace, dynamically sized around a central rendered buddy.

Painting occurs directly on the trusted rendered buddy preview through the established visual-only painting architecture. UI changes must not allow paint or customization data to affect physics, collision, economy rules, damage, mass, or rig geometry.

### 6.2 High-level layout

- **Title bar:** character/editor title and game-safe window commands.
- **Menu row:** concise commands such as File/Edit/View where they map to real actions.
- **Left rail:** character library, customization items, tool picker, transform/view commands.
- **Center:** recessed buddy paint viewport.
- **Right rail or collapsible inspector:** layers and contextual tool controls.
- **Bottom:** palette strip, selected-color well, full color picker command, status bar.
- **Viewport edges:** classic horizontal and vertical scrollbars for pan/zoom context.

At narrow sizes, the right inspector may become an in-scene tabbed drawer. It must not become a detached native child window.

### 6.3 Character library

- Scrollable list of local characters.
- Clear selected character state.
- `+` creates a new character through the existing safe character creation workflow.
- Rename/duplicate/delete only appear when implemented and valid.
- Delete is destructive and confirmed.

### 6.4 Customization item list

- Scrollable list reserved for valid available cosmetic items such as hats/accessories.
- Empty state is explicit.
- Locked/deferred items are not shown as selectable fake functionality.
- Equipping remains visual-only.

### 6.5 Tool picker

Initial approved editor controls:

- brush;
- eraser;
- fill only if/when separately authorized by the painting architecture;
- eyedropper only if/when separately authorized;
- pan/hand tool where needed.

The visual mockup is not permission to bypass the current Phase B feature gates. Controls are added only when their corresponding behavior is implemented and authorized.

### 6.6 Direct painting viewport

- Paint directly on the rendered buddy in the central viewport.
- Preserve the trusted `BuddyVisualRigView` preview seam and visual-only paint surfaces.
- Use deterministic hit-to-surface mapping.
- Keep face/accent decals above paint.
- Cursor/brush preview communicates target and size before committing a stroke.
- Invalid/non-paintable surface feedback is explicit and does not mutate data.

### 6.7 Rotation

- Rotate left/right in 90-degree steps where the painting architecture supports those views.
- The UI must not imply paintable rear/side surfaces before mapping and persistence support exists.
- Rotation preserves zoom/pan sensibly or resets by a documented deterministic rule.

### 6.8 Zoom and scrollbars

- Visible zoom-in and zoom-out buttons.
- Mouse-wheel zoom where already authorized.
- Horizontal and vertical classic scrollbars pan the zoomed viewport.
- Status bar shows zoom percentage.
- Reset View restores deterministic framing.
- Scroll thumbs communicate viewport-to-content proportion rather than acting as decorative sliders.

### 6.9 History

- Undo and redo buttons with keyboard shortcuts.
- Disabled state when unavailable.
- History is bounded according to the painting memory budget.
- Reset/Erase All is undoable where required and requires confirmation where destructive.

### 6.10 Color controls

- Large selected-color well.
- Always-visible preset palette strip.
- Dedicated full color picker button.
- Foreground color remains obvious.
- Palette selection updates brush preview immediately.
- Full picker uses an original UI consistent with the theme; it must not invoke an uncontrolled platform dialog.

### 6.11 Brush size

- Visible decrease/increase controls.
- Numeric or stepped size readout.
- Mouse-wheel sizing where authorized.
- Min/max values are clamped and reflected in disabled button states.

### 6.12 Save and reset

- Save commits through the existing safe persistence workflow.
- Reset reverts to the documented last-saved/default boundary.
- Dirty state is visible in title/status text.
- Closing with unsaved edits follows an explicit Save / Discard / Cancel confirmation flow.

### 6.13 Layers

The owner wants layer controls in the final editor. This is a future architecture extension and does not override the current Phase B prohibition on generalized user layers/blend modes.

Implement in two stages:

1. **Presentation stage:** show only real fixed semantic surfaces/channels already supported by the paint architecture, with selection/visibility where technically safe.
2. **Generalized layer stage:** requires a separate owner-approved data model, persistence/migration plan, memory/GPU budget, compositor order, undo semantics, and clean failure behavior before editable/reorderable layers or blend modes are exposed.

No fake layer controls may be shipped.

### 6.14 Phase 3 acceptance criteria

- Editor visually reads as one classic paint application.
- Painting targets the rendered buddy, not a detached texture preview.
- Character/item lists, tool controls, viewport, palette, and status information remain reachable during resize.
- Undo/redo, brush size, save/reset, zoom/pan, selected color, palette, and full picker expose correct enabled states.
- Unsupported future functionality is absent rather than decorative.
- Painting remains within established CPU/GPU/history budgets and remains visual-only.

## 7. Accessibility and modern UX requirements

The period look must not reproduce period usability defects unnecessarily.

- Minimum practical pointer targets may exceed the visible classic button bounds through invisible padding.
- Every icon-only command has a tooltip and accessible label.
- Keyboard traversal order follows visual order.
- Enter/Space activates focused buttons; Escape closes transient panels/modals.
- Default and destructive actions are visually distinct.
- Critical state is not communicated through color alone.
- Text contrast remains readable.
- Focus is restored after closing panels and confirmations.
- Modal overlays trap focus only while active.

## 8. Engineering boundaries

- Keep scene roots as composition/routing only.
- Build focused typed UI presenters/controllers and reusable controls.
- Do not introduce a global UI service locator.
- Do not perform file I/O or image encoding on the physics tick.
- Preserve the current gameplay input mode bridge and click ownership.
- Keep character and paint customization visual-only.
- Preserve editor window restoration and mode transitions.
- Do not add branded Microsoft assets or copyrighted icon sheets.
- UI icons must be original, project-owned, or generated from simple generic primitives.

## 9. Verification strategy

Each phase requires:

- engine-free state/model tests where practical;
- Godot headless scene tests for layout/state wiring;
- standalone Windows interaction scenarios;
- explicit regression coverage for click-through and input ownership;
- resize matrix at minimum, default, and large window sizes;
- Windows display scaling checks at 100%, 125%, 150%, and 200%;
- keyboard-only navigation pass;
- saved window-bounds and off-screen recovery pass;
- screenshots/artifacts for owner visual review.

## 10. Delivery slices

### Slice W0 — shared theme and controls

- theme tokens;
- bevel primitives;
- buttons/panels/title bar/status bar;
- state and focus tests;
- development showcase scene.

### Slice W1 — windowed buddy shell

- shell composition;
- title-bar drag and commands;
- responsive play viewport;
- status bar;
- mode/input regression suite.

### Slice W2 — command bar

- persistent category strip;
- in-scene expandable host;
- Shop migration;
- Tools migration;
- Settings migration;
- focus and dismissal behavior.

### Slice W3 — editor chrome and responsive layout

- editor shell;
- character/customization rails;
- viewport chrome and scrollbars;
- palette/status structure;
- no new paint behavior yet.

### Slice W4 — editor behavior integration

- bind currently authorized painting/view/history/color/size/save/reset behavior;
- expose only implemented actions;
- preserve budgets and persistence.

### Slice W5 — owner visual calibration and hardening

- spacing and proportion calibration;
- resolution/DPI matrix;
- keyboard/accessibility pass;
- regression fixes;
- owner acceptance gate.

## 11. Definition of done

The revamp is complete only when:

- all three scopes use the same reusable Win98 theme system;
- no ordinary menu requires a separate native window;
- windowed mode, command bar, and editor resize cleanly;
- gameplay input and UI input never conflict;
- all visible controls perform real supported actions;
- modern accessibility/focus expectations are met without losing the retro presentation;
- automated and standalone verification pass;
- the owner accepts the final visual calibration in the running game.
