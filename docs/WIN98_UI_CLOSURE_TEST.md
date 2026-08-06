# Win98 UI/UX Closure Test

This is the final owner test pass for the `win98-feel` branch. Agent-side implementation and automated scenario coverage are complete; this pass verifies behavior that depends on the real Windows desktop, display scaling, native window input, and visual judgement.

## 1. Build and startup

1. Run the normal build command.
2. Launch with `tools/play_game.bat`.
3. Confirm there is no C# build failure, exception spam, missing autoload error, or immediately closing terminal.
4. Confirm the main shell accepts buddy input and command-bar input.

## 2. Restored, maximized, and scaled layout

Test the paint editor in these configurations:

- restored window near its minimum usable size;
- a normal medium window;
- maximized;
- Windows display scaling at the currently configured scale;
- a second scale if readily available, such as 100% and 125% or 150%.

Confirm:

- Characters, Layers, tools, viewport, scrollbars, palette, and actions remain reachable;
- no controls overlap or disappear behind another panel;
- the viewport receives extra room as the window grows;
- the tool column can scroll vertically when height is constrained;
- text remains legible and button borders remain intact.

## 3. Painting workflow

1. Select Brush, Eraser, Pick, and Hand by pointer and by `B`, `E`, `I`, and `H`.
2. Paint the front, both sides, and back after rotating with `R` and `T`.
3. Draw slowly, quickly, across small body parts, and across the texture seam.
4. Change brush size by clicking, holding the buttons, holding `[` or `]`, and using the wheel.
5. Confirm size buttons disable at their limits and re-enable after moving away.
6. Pan by Hand, middle mouse, and Space + left mouse.
7. Zoom by buttons, shortcuts, and Ctrl + wheel; confirm bounded buttons and Reset View state.
8. Pick an existing painted color and confirm the current-color well, full picker, and matching preset update.
9. Pick an unpainted transparent area and confirm the current color does not change.

## 4. Layers and preview isolation

1. Select each semantic body-part layer.
2. Confirm strokes cannot touch overlapping non-selected parts.
3. Hide the selected layer and confirm it is absent from the preview and cannot receive paint or eyedropper input.
4. Show the layer again, then use Show All Parts.
5. Close and reopen the editor and confirm every body part is visible.
6. Confirm stretchy connector meshes remain hidden only inside the editor and return in gameplay.

## 5. History, destructive actions, and document state

1. Paint multiple strokes across multiple parts.
2. Undo and redo with buttons, menu items, `Ctrl+Z`, `Ctrl+Y`, and `Ctrl+Shift+Z`.
3. Confirm disabled states match across buttons and menus.
4. Use Erase All and confirm its warning appears and cancellation preserves work.
5. Confirm accepting Erase All is undoable.
6. Confirm the title shows `*` while modified and clears after Save.
7. Close with unsaved work through the button, File menu, and `Ctrl+W`; verify the existing unsaved-changes flow each time.
8. Save with `Ctrl+S`, close, reopen, and confirm paint persistence.
9. Use Character with `Ctrl+Enter`, return to gameplay, and confirm visual fidelity.

## 6. Character management

Test New, Duplicate, Delete, Randomize, character switching, Save, Use Character, Reset, and Close.

Confirm:

- destructive operations require the intended confirmation;
- cancellation leaves the working character unchanged;
- switching with unsaved paint follows the intended save/discard/cancel behavior;
- selection remains valid after duplicate or delete;
- a failed operation does not destroy the current working copy.

## 7. Keyboard and accessibility

1. Traverse the complete editor with Tab and Shift+Tab.
2. Confirm hidden and disabled controls are skipped.
3. Confirm traversal wraps and never enters invisible legacy controls.
4. Open menus with `Alt+F`, `Alt+E`, `Alt+V`, and focus the menu row with `F10`.
5. Close menus with Escape and confirm focus returns to the invoking menu.
6. Confirm shortcuts do not fire while typing in a text field or spin box.
7. Confirm focused-control help appears in the status bar.
8. Confirm selected tools, layers, visibility state, and preset color are not communicated by color alone.

## 8. Window/input ownership

Test restored, maximized, and deliberate fullscreen modes.

Confirm:

- buddy interaction and UI interaction do not block one another incorrectly;
- no editor command opens an unusable separate OS window;
- clicking editor controls never falls through to the buddy;
- closing the editor restores gameplay input;
- click-through behavior outside the active play area remains correct;
- title bar dragging, resizing, minimize, maximize, and restore remain functional.

## 9. Automated scenarios

Run the project scenario mechanism for the complete painting group, including:

- `paint_semantic_layer_filtering`
- `paint_eyedropper_sampling`
- `paint_document_state`
- all existing `paint_*` scenarios
- `character_paint_save_use_restart`

Record any failing scenario ID and its detailed check output.

## Closure rule

The branch is ready to merge when:

- the project builds cleanly;
- all automated paint scenarios pass;
- no blocker appears in sections 1–8;
- remaining visual differences are accepted by the owner or recorded as explicit follow-up work outside this revamp.
