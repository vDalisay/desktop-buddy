# Win98 Paint UX Correction and Custom Palette Plan

## Status

- Repository: `vDalisay/desktop-buddy`
- Branch: `win98-feel`
- Baseline commit reviewed: `0e143c832e359edee5389a8c560923dcf5aec6da`
- Scope: correct the latest character-editor UX regressions, finish the requested modal layouts, simplify layers, and add an extensible custom-color palette.
- This document is an implementation plan only. No behavior in this plan is considered complete until implemented, compiled, and owner-tested.

## Product intent

The paint editor should retain the visual language of Windows 98 while following the layout clarity of modern creative applications. The interface should be compact, predictable, and task-oriented:

- character management remains in the upper-left column;
- painting tools remain in a narrow dedicated tool column;
- the buddy preview remains the dominant work area;
- layers expose one clear selection mechanism;
- palette actions stay adjacent to the palette;
- modal actions are consistently aligned and padded;
- controls should not be duplicated when one control already expresses the same state.

The implementation must preserve the existing paint-domain behavior, persistence model, input ownership, semantic body-part filtering, undo/redo behavior, and editor-only connector hiding.

## Confirmed requested changes

### Character column

1. Move `+ New Character` back into the original location directly below the character list.
2. Remove the `Previous` and `Next` pager buttons from the visible paint-editor layout.
3. Keep pagination internals available only if the session still needs them, but do not expose those controls in this editor.
4. Make `Duplicate` and `Delete` share the entire available row equally.
5. Keep `Randomize` absent from the paint editor.
6. Keep `Local Characters` absent.
7. Keep character-list tooltips free of internal IDs, directory names, GUIDs, or persistence details.

### New-character modal

1. Keep the randomized phrase and name input.
2. Anchor the action row to the bottom-right.
3. Add consistent interior padding around all edges.
4. Keep `Cancel` before `Create`.
5. Keep Enter as Create and Escape as Cancel.
6. Prevent accidental interaction with the editor behind the modal.
7. Ensure focus initially lands in the name field and returns to `+ New Character` after closing.

### Unsaved-changes modal

1. Change the blue title-bar text to `Are you sure?`.
2. Keep the explanatory body text `Save changes before continuing?`.
3. Center the action buttons horizontally.
4. Anchor the action row to the bottom.
5. Add consistent bottom and side padding.
6. Keep button order `Save`, `Discard`, `Cancel` unless owner feedback changes it.
7. Preserve the current session state-machine behavior. Styling must not bypass `ResolveUnsavedAsync`.

### Layers

1. Remove the body-part `OptionButton` dropdown.
2. Use the list below it as the only body-part selection control.
3. Add a compact `?` help control beside the `Layers` header.
4. Move the full explanatory sentence into that control's tooltip:

   `Hidden layers cannot receive paint and return when the editor closes.`

5. Remove the permanently visible help paragraph.
6. Keep an explicit `All body parts` entry in the list as the first row.
7. Keep selection state synchronized with `PaintCanvasControl.ActivePartFilter`.
8. Keep the visibility checkbox disabled while `All body parts` is selected.
9. Hidden parts must remain excluded from paint and eyedropper hit-testing and must restore when the editor closes.

### Tool layout

The current screenshot still shows the old 2x2 Brush/Eraser/Pick/Hand arrangement. Replace bootstrap-time visual mutation with deterministic layout composition so the requested design always appears.

Target layout:

- group heading: `Paint`
- full-width `Brush` tool button with shortcut hint `B`
- full-width `Eraser` tool button with shortcut hint `E`
- separator
- group heading: `Inspect & move`
- full-width `Pick Color` tool button with shortcut hint `I`
- full-width `Pan View` tool button with shortcut hint `H`

Each tool button must:

- have a minimum height of approximately 30–32 px;
- fill the tool-column width;
- use left-aligned action text;
- expose the shortcut without making it look like part of the action name;
- retain mutually exclusive pressed state;
- retain current keyboard shortcuts and cursor behavior;
- remain usable through Tab traversal.

Recommended implementation: use an `HBoxContainer` inside each button only if Godot button text cannot produce reliable left/right alignment. Otherwise use a short label and put shortcut text in tooltip/status help to avoid brittle whitespace-based alignment.

### Color-picker icon

1. Replace the missing text/emoji presentation with an actual project-owned pixel-art icon.
2. The icon should be heavily inspired by the supplied reference: a small paint bucket containing several colored brushes.
3. It must be an original clean-room asset, not a copied or traced image.
4. Match the existing low-resolution pixel aesthetic and Win98 toolbar scale.
5. Create at least one source PNG suitable for Godot import, preferably 24x24 or 32x32 pixels with transparency.
6. The color-picker button should show the icon without relying on emoji font support.
7. Keep the tooltip `Open the full color picker.` and preserve keyboard focus behavior.

Suggested asset path:

- `assets/ui/win98/paint_bucket_brushes.png`

Suggested source/editable companion only if the project already keeps source art:

- `assets/ui/win98/source/paint_bucket_brushes.xcf` or equivalent should not be introduced unless source-art storage is already established.

### Custom palette slot

1. The palette must always end with a visible empty-looking `+` color slot.
2. Clicking it appends one custom color swatch immediately before the `+` slot.
3. The new swatch defaults to the current brush color.
4. The newly added swatch becomes selected.
5. Clicking a custom swatch sets the current brush color exactly like built-in presets.
6. Custom swatches participate in the existing selected-state styling.
7. The `+` slot itself is an action button, not a selectable color.
8. Additions should not disturb built-in palette ordering.
9. The palette layout must wrap or expand predictably at narrow widths.
10. Decide persistence explicitly rather than accidentally:

   - Phase 1 required behavior: custom colors live for the current application session.
   - Phase 2 recommended behavior: persist custom palette colors in user preferences, not in a character document, because they are editor tooling preferences rather than character data.

11. Set a practical limit, recommended 24 custom colors, to prevent unbounded layout growth.
12. At the limit, disable the `+` slot and explain the limit through its tooltip/status help.
13. Avoid duplicate insertion where practical. Recommended behavior: if the current brush color already exists as a custom swatch, select that swatch instead of adding another identical entry.

## Root-cause corrections

The latest UX bootstrap attempted to rearrange controls after multiple other deferred bootstraps had already composed the editor. The screenshot indicates that some mutations did not occur or occurred before their target nodes existed. The next implementation should reduce timing-sensitive patching.

### Architectural direction

Move stable layout responsibilities into the component that owns the layout:

- character-column placement and sizing: `CharacterEditorHost.Win98PaintLayout.cs`
- layer panel structure: `Win98PaintLayersBootstrap.cs`, or preferably a dedicated reusable layer panel if this code is further refactored
- tool-button creation and state: `Win98PaintToolBootstrap.cs` plus deterministic host container structure
- modal layout: `CharacterEditorHost.cs` or a dedicated partial such as `CharacterEditorHost.Win98Dialogs.cs`
- palette structure and custom colors: a dedicated presenter/bootstrap with one clear ownership boundary

`Win98PaintUxPolishBootstrap.cs` should be reduced or removed once its changes are moved into stable owners. It should not remain a broad catch-all that repeatedly searches and mutates unrelated controls every frame.

Every autoload or bootstrap that relies on `_Process`, deferred composition, or input while the editor is open must explicitly set:

```csharp
ProcessMode = ProcessModeEnum.Always;
```

## Detailed implementation phases

## Phase 1 — Character-column correction

### Files

- `src/CharacterEditor/CharacterEditorHost.Win98PaintLayout.cs`
- `src/CharacterEditor/CharacterEditorHost.cs`
- `src/UI/Win98/Win98PaintUxPolishBootstrap.cs`

### Work

1. Restore the new-character control to `Win98CharacterColumnBody` directly after the character list.
2. Reuse the real `NewButton` rather than creating a second replacement button where possible.
3. Change its text to `+ New Character` and preserve the modal-opening handler.
4. Hide or remove the pager row in the Win98 paint layout.
5. Keep `_previousPage` and `_nextPage` initialized if `RefreshAll` still references them; set them hidden and exclude them from focus traversal.
6. Replace the management `HBoxContainer` with a two-column `GridContainer` or equal-expand `HBoxContainer`.
7. Set both `DuplicateButton` and `DeleteButton` to `SizeFlagsHorizontal = ExpandFill`.
8. Ensure the row has exactly two visible children.
9. Remove Randomize from the row entirely for this layout rather than only setting it invisible after composition.
10. Remove old replacement-new-button code from the polish bootstrap to avoid duplicate controls.

### Acceptance criteria

- `+ New Character` appears directly below the list.
- No pager buttons are visible or reachable through Tab.
- Duplicate and Delete have equal widths.
- No empty gap remains where Randomize used to be.
- Creating a character adds/selects the unsaved working entry without duplicating list rows.

## Phase 2 — Deterministic modal layouts

### Files

- `src/CharacterEditor/CharacterEditorHost.cs`
- new recommended partial: `src/CharacterEditor/CharacterEditorHost.Win98Dialogs.cs`
- `src/UI/Win98/Win98PaintModalThemeBootstrap.cs`
- `src/UI/Win98/Win98PaintUxPolishBootstrap.cs`

### Work

1. Extract modal creation from the broad UX bootstrap into host-owned dialog methods.
2. Add an outer `MarginContainer` inside each modal panel with 12–16 px equivalent margins.
3. Use a `VBoxContainer` with:
   - title bar at top;
   - content area expanding vertically;
   - spacer using `SizeFlagsVertical = ExpandFill`;
   - action row at bottom.
4. New-character action row:
   - `Alignment = End`;
   - right-aligned;
   - bottom anchored through the expanding spacer;
   - 8 px button separation;
   - Create receives default focus behavior after text submission, but initial focus stays in the name input.
5. Unsaved prompt action row:
   - `Alignment = Center`;
   - bottom anchored through the expanding spacer;
   - equal button sizes;
   - title-bar label changed to `Are you sure?`.
6. Ensure modal blockers are layered immediately below their corresponding modal and above editor controls.
7. Ensure hidden modals and blockers use `MouseFilter = Ignore` or are invisible.
8. Ensure modal controls are excluded from the normal paint focus graph while hidden.
9. Preserve the existing unsaved-decision state machine and action routing.

### Acceptance criteria

- New-character buttons remain bottom-right at all supported window sizes.
- Unsaved buttons remain bottom-center.
- Both dialogs maintain visible interior padding at 100%, 125%, and 150% Windows scaling.
- The title of the unsaved dialog reads `Are you sure?`.
- No click passes through a visible modal.

## Phase 3 — Layer panel simplification

### Files

- `src/UI/Win98/Win98PaintLayersBootstrap.cs`
- `src/UI/Win98/Win98PaintFocusBootstrap.cs`

### Work

1. Delete `_selector` and all `OptionButton` creation and synchronization code.
2. Add `All body parts` as item index 0 in `PaintLayerList`.
3. Map list indices as follows:
   - 0: `ActivePartFilter = null`
   - 1..6: corresponding `PaintPart`
4. Retain one source of truth for selection and visibility state.
5. Build a centered header row containing:
   - `Layers` label;
   - 22–24 px `?` button.
6. Put the full help sentence in `TooltipText` and `AccessibilityDescription`.
7. Remove the visible help label completely.
8. Update status text after list selection and visibility toggles.
9. Keep `Win98ItemListCheck.Attach(list)` only if its checkmark behavior remains appropriate with the new `All body parts` row. Otherwise replace it with standard selected-row styling.
10. Ensure restore-on-close selects index 0 and restores all preview body sockets.

### Acceptance criteria

- No layer dropdown is visible.
- The list contains `All body parts` followed by the six body parts.
- Clicking each row updates paint targeting immediately.
- The `?` tooltip shows the full requested sentence.
- The old explanatory paragraph is absent.

## Phase 4 — Tool-column rebuild

### Files

- `src/CharacterEditor/CharacterEditorHost.Win98PaintLayout.cs`
- `src/UI/Win98/Win98PaintToolBootstrap.cs`
- `src/UI/Win98/Win98PaintShortcutBootstrap.cs`
- `src/UI/Win98/Win98PaintFocusBootstrap.cs`

### Work

1. Change `Win98ToolPicker` from a 2-column grid to a vertical container or one-column grid at creation time.
2. Create all four tool buttons within a single deterministic pass.
3. Do not depend on a later bootstrap finding the grid after an unknown frame count.
4. Insert semantic headings and separator as described above.
5. Preserve existing node names:
   - `PaintBrushButton`
   - `PaintEraserButton`
   - `PaintEyedropperButton`
   - `PaintPanButton`
6. Preserve toggle state and selection routing in `Win98PaintToolBootstrap`.
7. Replace whitespace-aligned shortcut text with either:
   - a child row with action and shortcut labels, or
   - action-only button text plus shortcut in tooltip/status bar.
8. Confirm the status bar still reports focused control help.
9. Confirm keyboard shortcuts trigger the same source buttons.

### Acceptance criteria

- The old 2x2 layout no longer appears, including on first open.
- Tools are full-width and grouped.
- Only one tool is pressed at a time.
- B/E/I/H shortcuts still work.
- Tab order follows the visual order.

## Phase 5 — Pixel-art color-picker icon

### Files

- new asset: `assets/ui/win98/paint_bucket_brushes.png`
- `src/CharacterEditor/CharacterEditorHost.Win98PaintLayout.cs`

### Work

1. Create an original transparent pixel-art icon depicting:
   - a light gray paint bucket;
   - dark pixel outline;
   - several brushes rising from it;
   - small cyan, red, yellow, green, blue, and magenta accents;
   - readable silhouette at 24–32 px.
2. Avoid antialiased scaling. Import and render using nearest-neighbor behavior consistent with project pixel assets.
3. Load the texture through `GD.Load<Texture2D>()` or a preloaded resource.
4. Set `_paintColorPicker.Icon` to the texture.
5. Clear emoji-only text; use no text or a concise fallback only if the texture fails.
6. Preserve button size and tooltip.

### Acceptance criteria

- The button shows the bucket-and-brush icon on systems without emoji fonts.
- The icon is crisp at 100%, 125%, and 150% scaling.
- Clicking it still opens the full Godot color picker.

## Phase 6 — Extensible custom palette

### Recommended ownership

Create a dedicated component:

- `src/UI/Win98/Win98PaintCustomPaletteBootstrap.cs`

Do not overload `Win98PaintPaletteStateBootstrap` with creation, persistence, and selection responsibilities unless it is deliberately renamed/refactored into one palette presenter.

### Data model

Introduce a small editor-preferences abstraction if persistence is implemented:

```csharp
public interface IPaintPalettePreferences
{
    IReadOnlyList<PaintColor> CustomColors { get; }
    void Add(PaintColor color);
    void RemoveAt(int index);
}
```

For the first implementation pass, an in-memory list in the palette presenter is acceptable. It must be clearly isolated so persistence can be added without changing button behavior.

### Work

1. Resolve the preset palette container and current `PaintCanvasControl`.
2. Ensure a terminal action button named `PaintAddCustomColorButton` always exists.
3. Style it as an empty swatch with centered `+`.
4. On press:
   - read `Workspace.SelectedColor`;
   - check for an existing matching custom swatch;
   - if found, select it;
   - otherwise append a custom swatch immediately before the `+` button;
   - set the swatch color and tooltip;
   - keep the current brush color unchanged;
   - update selected-state styling.
5. Use stable metadata rather than parsing tooltip strings to identify colors. Recommended metadata fields:
   - color packed as RGB integer;
   - source type `preset`, `custom`, or `add-action`.
6. Refactor `Win98PaintPaletteStateBootstrap.TryReadPreset` away from tooltip parsing.
7. Give each swatch an accessibility description such as `Custom color #RRGGBB`.
8. Apply the same pressed/selected border to preset and custom swatches.
9. Keep the `+` button unpressed and excluded from color-match iteration.
10. Add maximum-count handling.
11. Rebuild custom swatches when the editor is reopened.
12. Optional follow-up after owner approval: right-click or Delete-key removal of custom colors. Do not implement removal in the first pass unless requested.

### Acceptance criteria

- A `+` swatch is always visible after the final color.
- Clicking it creates one swatch using the current brush color.
- The custom swatch immediately works as a color selector.
- Selected-state highlighting works for custom colors.
- Built-in colors remain in their original order.
- Duplicate colors do not produce repeated custom swatches.
- The palette remains usable at narrow window widths.

## Phase 7 — Remove obsolete bootstrap behavior

### Files

- `src/UI/Win98/Win98PaintUxPolishBootstrap.cs`
- `src/UI/Win98/Win98PaintModalThemeBootstrap.cs`
- `project.godot`

### Work

1. After responsibilities move to stable owners, remove redundant tree searches and visual mutations.
2. Delete the broad UX bootstrap if it no longer owns any behavior.
3. Delete the modal-theme bootstrap if dialogs inherit the correct theme directly.
4. Remove corresponding autoload entries from `project.godot`.
5. Preserve all unrelated autoload entries and the entire input map.
6. Reconfirm every remaining editor bootstrap has `ProcessModeEnum.Always` where required.

### Acceptance criteria

- No duplicate new-character button exists.
- No old layer dropdown can reappear because of bootstrap order.
- No old 2x2 tool grid flashes before being replaced.
- No runtime tree-search loop exists solely to keep correcting stable layout.

## Phase 8 — Automated regression coverage

### Files

- `src/Testing/Win98PaintInteractionScenarios.cs`
- new recommended: `src/Testing/Win98PaintUiCompositionScenarios.cs`
- `src/Testing/PaintingPhaseBScenarioRegistration.cs`

### Scenarios

1. `paint_character_column_composition`
   - new button is under the list;
   - pager is hidden;
   - Duplicate/Delete are the only management controls;
   - equal expand flags are set.
2. `paint_layer_list_composition`
   - no `PaintLayerSelector` exists;
   - seven list entries exist;
   - row-to-filter mapping is correct;
   - help tooltip has the full sentence.
3. `paint_tool_column_composition`
   - all four named tool buttons exist;
   - visual order is Brush, Eraser, Pick Color, Pan View;
   - no two-column layout remains.
4. `paint_custom_palette_add`
   - `+` action exists last;
   - current brush color creates a custom swatch;
   - duplicate add reselects rather than duplicates;
   - custom swatch selects exact RGB.
5. `paint_modal_layout_contract`
   - new-character row alignment is End;
   - unsaved row alignment is Center;
   - title text is `Are you sure?`;
   - modal blockers stop input.

UI scenarios should verify composition contracts without relying on pixel coordinates where possible. Manual testing remains required for visual padding and DPI behavior.

## Manual test matrix

### Character creation

- Open the new-character modal five times and confirm the phrase can change each time.
- Confirm buttons remain bottom-right at minimum, restored, and maximized sizes.
- Confirm blank names cannot be created.
- Confirm Enter creates and Escape cancels.
- Confirm creating while another character is dirty routes through the unsaved prompt and preserves the entered name after Save or Discard.
- Confirm Cancel in the unsaved prompt does not create a character.

### Character column

- Confirm `+ New Character` sits directly below the list.
- Confirm Previous/Next are absent.
- Confirm Duplicate and Delete are equal width.
- Confirm no ID appears on hover.

### Unsaved prompt

- Confirm blue title reads `Are you sure?`.
- Confirm action buttons are bottom-centered.
- Confirm Save, Discard, and Cancel still perform their existing state-machine actions.

### Layers

- Confirm there is no dropdown.
- Confirm `All body parts` and six body parts appear in the list.
- Confirm the `?` tooltip contains the exact full sentence.
- Confirm hidden layers cannot receive paint or eyedropper sampling.
- Confirm all layers restore when closing the editor.

### Tools

- Confirm vertical grouped layout appears immediately on first open.
- Confirm tool buttons do not overflow the narrow column.
- Confirm B/E/I/H and mouse clicks remain synchronized.

### Palette

- Confirm the new pixel icon is crisp.
- Confirm the `+` palette slot remains last after multiple additions.
- Confirm each custom color can be selected.
- Confirm selected-state border works.
- Confirm palette behavior after closing and reopening according to the chosen session/persistence scope.

### Scaling

Run at Windows scaling:

- 100%
- 125%
- 150%

Check modal padding, button alignment, icon sharpness, palette wrapping, layer help tooltip, and character-column button widths.

## Implementation order and commit structure

Recommended small commits:

1. `fix(paint): restore character column action layout`
2. `fix(paint): rebuild editor dialogs with stable Win98 layout`
3. `fix(paint): simplify semantic layer selection`
4. `refactor(paint): compose grouped creative tool rail`
5. `feat(paint): add pixel bucket color-picker icon`
6. `feat(paint): add extensible custom palette colors`
7. `refactor(paint): remove obsolete UX mutation bootstraps`
8. `test(paint): cover final Win98 UX composition`

Before every commit, fetch the latest branch head because local owner changes may have been pushed.

## Definition of done

This correction pass is complete when:

- all confirmed requested layout changes are present;
- the old pager, layer dropdown, help paragraph, Randomize button, and 2x2 tool arrangement are absent;
- character creation and unsaved dialogs have stable requested alignment and padding;
- the unsaved title reads `Are you sure?`;
- the bucket-and-brush icon is visible and crisp;
- the terminal `+` palette slot creates usable custom colors;
- no internal character identifier is exposed through tooltips;
- all affected workflows preserve existing persistence and unsaved-decision semantics;
- the project builds without C# errors;
- automated scenarios pass;
- the owner completes the manual DPI and interaction test matrix.
