# User-Testing Polish — Closure Status

Status: **OWNER FOLLOW-UP 6 IMPLEMENTED; AUTOMATED VERIFICATION PASS; OWNER VISUAL PASS PENDING**
Authoritative input: `docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`  
Branch: `user-testing-polish`

This document maps the observed Section 1–7 user-testing findings to the implementation that now exists on the branch. The final owner feel/interaction pass remains required before the gate is fully closed.

All six 2026-08-11 owner follow-ups are implemented and locally verified. SFX remains unchanged as requested.

## 1. Paint Background

Implemented:

- Curved Line exposes visible circular control points throughout its staged baseline / first-bend / second-bend workflow.
- Curve guides clear on cancel, completion, tool switch, click-away/session close.
- Tool buttons are toggle/pressed state and Shapes displays the active shape name.
- `Save` is `Save and Exit` where it closes the editor.
- Fill is presented as `Bucket Fill`.
- Brush uses the horizontally projected background-canvas ellipse; Pen, Eraser, and Spray retain their round screen-space footprint.
- The Unsaved Background actions are anchored to the modal bottom.
- Existing Spray/Curve/Undo behavior remains the functional base.

Primary implementation: `src/Environment/EnvironmentBackgroundEditor.cs`.

## 2. Buddy Studio

Implemented:

- Shipped mouth families have visibly distinct neutral silhouettes: rounded/3-like, angular/caret, and flat line while preserving semantic expression poses.
- Accessories is hidden from the current demo category strip.
- Single-click previews without changing the equipped item; changing tabs cancels that preview.
- Double-click equips an owned item or buys and equips an affordable unowned item.
- The single item currently shown in preview keeps the thick active-title navy border in every state.
- Clean Cancel exits immediately. Dirty Cancel offers Save/Discard, and Save persists, applies, and exits.
- Catalogue thumbnails use trusted rendered appearance data rather than generic symbolic stand-ins.

Primary implementation: `src/Buddy/Presentation3D/Characters/CharacterFeatureRenderers.cs`, `src/CharacterEditor/BuddyStudio/BuddyStudioWorkspace.cs`, and `src/CharacterEditor/BuddyStudio/BuddyStudioThumbnailCache.cs`.

## 3. Paint Buddy

Implemented:

- Mirror modifier.
- `Paint backside too` modifier.
- Bucket Fill with mirror/backside-aware atomic Undo behavior.
- Complete modern tool rail with explicit active pressed state.
- Current-color block receives a stable bordered affordance; eyedropper changes the actual selected color and palette-state synchronization clears stale swatch selection.
- Turn and Zoom rows are reparented into a compact lower-left preview control cluster.
- Palette uses a blue title bar with pin and close controls; current color, swatches, add button, and full picker detach together into one unclipped 760×150 resizable native desktop window. Paint Background instead detaches its complete tool window, without a redundant nested palette window.
- Undo, Redo, and Erase All retain visible text beside their icons; Erase All uses an in-game Win98 confirmation rather than a native Godot dialog.
- `Show limbs` spreads hands/feet, reveals the arm/leg connectors, and makes both targets paintable. Each paired limb surface is split into disjoint end-part and connector UV lanes, so connector paint never appears on the hand/foot while the locked six-surface budget is preserved.
- Buddy Brush feedback is an unrotated vertical ellipse matching its visible stamp; Pen is available directly below Brush.
- Save, Use Character, Reset, and Exit use equal normal-height Win98 buttons in one row.
- Paint Buddy opens the character currently active in gameplay.

Primary implementation: `domain/DesktopBuddy.Domain/Painting/PaintWorkspace.cs`, `src/UI/Win98/Win98PaintUserTestingBootstrap.cs`, `src/UI/Win98/Win98PaintUserTestingLayoutBootstrap.cs`, `src/CharacterEditor/PaintCanvasControl.LimbPose.cs`, and `src/UI/Win98/Win98PaintLimbPoseBootstrap.cs`.

Focused coverage:

- `PaintBucketFillTests`
- `PaintSymmetryTests`
- `paint_limb_pose_mapping` scenario

## 4. General shell / catalogue

Implemented:

- The old Shop is now the player-facing `Inventory`; it owns Buy and Equip and the separate Tools command is hidden.
- Inventory has the same pin/detach/drag/dock treatment and resizable native-window behavior as the paint palettes.
- Outside-click closes the open command-bar flyout.
- Active gameplay tool is surfaced in the Win98 shell/status area.
- Floating desktop panels continue to use the draggable window path where safe.
- Ordinary VelocityAligned hand/foot visual rotation is restrained at walking/ambient speed, returns toward upright below the deadband, and smoothly restores full directional rotation for high-speed throws/impacts.

Primary implementation: `src/Shop/ShopPanel.cs`, `src/UI/Win98/Win98CommandBarBootstrap.CataloguePolish.cs`, `src/UI/Win98/Win98CommandBarBootstrap.OutsideClick.cs`, `src/UI/Win98/Win98WindowFrame.ToolStatus.cs`, `src/UI/Win98/Win98BuddyShellController.ToolStatus.cs`, `src/Buddy/Presentation3D/BuddyVisualPresenter.cs`, and `domain/DesktopBuddy.Domain/Presentation/VelocityRotationResponse.cs`.

Focused coverage:

- `shop_panel_purchase` scenario
- `VelocityRotationResponseTests`
- existing `presentation_3d` scenario in the closure runner

## 5. Work Mode

Implemented:

- Starting on any draggable Work surface, including the CRT, latches hold-LMB + wheel resizing until release; fine steps keep the original cursor anchor stable.
- A CRT click still toggles lifetime/session counters, while a held movement drags the Work companion.
- Resize, Pause, and Exit sit on a full-width active-title-blue bar with a thin raised grey outline.
- The normal Win98 frame is hidden while Work is active, preventing its right/bottom grey strips from showing while the native region is temporarily relaxed during resize.
- Exiting Work Mode selects normal Grab.
- Resize receives a restrained short feedback cue.
- First-entry reward receives a one-shot reward cue and the double-click Work exit receives a short completion cue.

Primary implementation: `src/Work/WorkCompanionView.WheelResize.cs`, `src/Work/WorkCompanionCoordinator.ExitTool.cs`, and `src/UI/UiFeedbackAudioBootstrap.cs`.

Focused coverage: `work_mode_resilience` scenario.

## 6. Decorate Room

Implemented:

- The bottom-anchored action row uses `Edit mode`, `Delete mode`, and `Reset Room` on the left, with equally sized `Buy` and owned-copy-only `Place` on the right. `Review Room` and Cancel are removed.
- Available/projected money values use deliberate green/red treatment.
- Snap/grid UI is hidden and free placement is forced for the current demo.
- Artificial floor/wall gating is removed; technical room bounds remain.
- Delete is a dedicated mode with Done/Cancel staging semantics.
- Close/Cancel owns the save-or-revert prompt for a dirty room session.
- Wallpaper changes participate in the same staged dirty/commit flow.
- Purchased decorations remain permanently owned and deleted copies return to storage.
- The decorator opens at the largest complete usable height (680×620 in the live pass), detaches into a resizable 760×620 native window, and exposes scrolling only when reduced.

Primary implementation: `src/Environment/EnvironmentDecorator.cs`, `src/Environment/EnvironmentDecorator.Preferences.cs`, and `src/Environment/EnvironmentPlacementController.cs`.

Focused coverage: `environment_decorator_room_build` plus the existing Environment closure scenarios.

## 7. Cross-cutting SFX priority

Immediate user-testing coverage implemented without making the desktop app noisy:

- one short synthesized feedback cue on standard button activation and popup selection;
- semantic purchase/confirm/caution tones derived from action state (`Buy`, `Equip`, `Save`, `Cancel`, `Delete`, etc.);
- no hover/focus loop sounds;
- explicit Work resize, first-entry reward, and direct-companion exit feedback;
- existing gameplay-specific reaction/swing/fire/grenade audio remains intact.

Primary implementation: `src/UI/UiFeedbackAudioBootstrap.cs`.

The broader content-complete audio consistency pass remains in the later Steam-demo polish phase exactly as allowed by the authoritative backlog; this immediate gate fills the obvious user-tested interaction gaps.

## Automated closure command

Run from the repository root:

```bat
tools\validate_user_testing_polish.bat
```

The runner performs:

1. solution build;
2. all pure domain tests;
3. Godot import/script composition;
4. Paint Buddy UV mapping;
5. Show-limbs mapping/restoration;
6. paint toolbar composition;
7. Buddy Studio user-testing composition;
8. unified Inventory buy/equip;
9. room decorator closure;
10. Work Mode resilience/resize/exit;
11. live 3D presentation regression.

**Recorded result (agent local run, 2026-08-11 follow-up 6): PASS.** Build completed with zero errors; all 1,328 domain tests passed; and the focused `environment_background_editor`, `work_mode_resilience`, and `paint_toolbar_icons` scenarios passed alongside the earlier closure set.

The running-game MCP pass also verified the top-level Buddy Studio route, active-character Paint Buddy opening, exact shared 1646×858 window bounds/position across main/Paint/Studio, unclipped 760×150 Paint Buddy palette detachment, Move title cursors, Win98 Erase All confirmation, no nested Paint Background palette window, full 680×620 Room Decorator opening, revised room actions, title-blue equipped border, and the hidden normal frame/blue hover treatment in Work.

## Final owner manual gate

Launch `tools\play_game.bat` and verify the interaction/feel items that automation cannot judge:

- Paint Background: Curve guidance reads naturally; control points appear/disappear at the right moments; Eraser footprint and active tool/shape are visually obvious.
- Paint Buddy: Mirror/backside/fill feel correct; the complete palette detaches/drags/resizes/docks; lower-left turn/zoom cluster is unobtrusive; and Show limbs makes hands, feet, arms, and legs intentionally paintable before restoring the normal preview.
- Buddy Studio: mouths read as clearly different; Accessories is absent; single preview/double equip is intuitive; unowned double-click buy/equip is clear; equipped borders are readable; and clean/dirty Cancel behaves naturally.
- Inventory: only Inventory is player-facing; Buy→Equip is clear; its window detaches/resizes/docks; outside-click closes the docked flyout; and bottom status reports the active tool.
- Gameplay buddy: ordinary walking/ambient hand/foot rotation is calmer, while throws/impacts still feel physical.
- Work: CRT click versus drag feels distinct; resize latching works from every draggable surface; button-resize no longer flickers; and Grab is active after exit.
- Decorate Room: Buy/storage/Place ownership is clear; the action positions read naturally; detached resizing and scrolling work; delete staging, free placement, funds, wallpaper dirty state, and close-time save/revert are understandable.
- Audio: repeated UI use is not fatiguing and there are no obviously doubled/missing cues in the changed flows.

If this manual pass is accepted and no validator failures remain, Sections 1–7 can be marked closed and the next implementation slice may move to Potion Shop.
