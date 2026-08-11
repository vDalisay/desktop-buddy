# User-Testing Polish — Closure Status

Status: **IMPLEMENTATION COMPLETE; AUTOMATED + OWNER MANUAL GATES NOT YET RUN**  
Authoritative input: `docs/USER_TESTING_POLISH_BACKLOG_2026-08-11.md`  
Branch: `user-testing-polish`

This document maps the observed Section 1–7 user-testing findings to the implementation that now exists on the branch. It does **not** declare the gate green: the connector environment cannot run the Windows/Godot build, so `tools\validate_user_testing_polish.bat` and the final owner feel pass remain required.

## 1. Paint Background

Implemented:

- Curved Line exposes visible circular control points throughout its staged baseline / first-bend / second-bend workflow.
- Curve guides clear on cancel, completion, tool switch, click-away/session close.
- Tool buttons are toggle/pressed state and Shapes displays the active shape name.
- `Save` is `Save and Exit` where it closes the editor.
- Fill is presented as `Bucket Fill`.
- Eraser uses the same circular brush-footprint cursor ring as the paint brush rather than an ellipse-style marker.
- Existing Spray/Curve/Undo behavior remains the functional base.

Primary implementation: `src/Environment/EnvironmentBackgroundEditor.cs`.

## 2. Buddy Studio

Implemented:

- Shipped mouth families have visibly distinct neutral silhouettes: rounded/3-like, angular/caret, and flat line while preserving semantic expression poses.
- Accessories is hidden from the current demo category strip.
- Clicking an owned cosmetic auto-equips it.
- Unowned preview and unaffordable states are explicit; save remains gated until ownership is resolved.
- Buy/equip state is explicit in the action control.
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
- Palette can detach into a draggable Win98 `Color Palette` window and dock back into the paint footer.
- `Show limbs` spreads hands/feet, reveals connectors, and moves the trusted paint mapper by the same offsets so the visible and paintable targets remain aligned. The pose is editor-only and restores on disable/exit.

Primary implementation: `domain/DesktopBuddy.Domain/Painting/PaintWorkspace.cs`, `src/UI/Win98/Win98PaintUserTestingBootstrap.cs`, `src/UI/Win98/Win98PaintUserTestingLayoutBootstrap.cs`, `src/CharacterEditor/PaintCanvasControl.LimbPose.cs`, and `src/UI/Win98/Win98PaintLimbPoseBootstrap.cs`.

Focused coverage:

- `PaintBucketFillTests`
- `PaintSymmetryTests`
- `paint_limb_pose_mapping` scenario

## 4. General shell / catalogue

Implemented:

- The old Shop is now the player-facing `Catalogue`; it owns Buy and Equip and the separate Tools command is hidden.
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

- Hold LMB + wheel resizes the companion through the existing safe native-window resize policy.
- Exiting Work Mode selects normal Grab.
- Resize receives a restrained short feedback cue.
- First-entry reward receives a one-shot reward cue and the double-click Work exit receives a short completion cue.

Primary implementation: `src/Work/WorkCompanionView.WheelResize.cs`, `src/Work/WorkCompanionCoordinator.ExitTool.cs`, and `src/UI/UiFeedbackAudioBootstrap.cs`.

Focused coverage: `work_mode_resilience` scenario.

## 6. Decorate Room

Implemented:

- Clearer Place / Edit Items / Delete Items mode separation and focus chrome.
- Available/projected money values use deliberate green/red treatment.
- Snap/grid UI is hidden and free placement is forced for the current demo.
- Artificial floor/wall gating is removed; technical room bounds remain.
- Delete is a dedicated mode with Done/Cancel staging semantics.
- Room completion is a single `Review Room` → `Satisfied with your room?` flow that saves the whole room or reverts the session.
- Wallpaper changes participate in the same staged dirty/commit flow.
- Purchased decorations remain permanently owned and deleted copies return to storage.

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
8. unified Catalogue buy/equip;
9. room decorator closure;
10. Work Mode resilience/resize/exit;
11. live 3D presentation regression.

No automated result is recorded here until that command has actually been run locally.

## Final owner manual gate

After the automated command passes, launch `tools\play_game.bat` and verify the interaction/feel items that automation cannot judge:

- Paint Background: Curve guidance reads naturally; control points appear/disappear at the right moments; Eraser footprint and active tool/shape are visually obvious.
- Paint Buddy: Mirror/backside/fill feel correct; palette detaches/drags/docks; lower-left turn/zoom cluster is unobtrusive; eyedropper clears stale swatch selection; Show limbs makes hands/feet intentionally paintable and restores the normal preview afterward.
- Buddy Studio: mouths read as clearly different; Accessories is absent; owned click equips; unowned/Buy state is immediately understandable; thumbnails look like the actual item.
- Catalogue: only Catalogue is player-facing; Buy→Equip flow is clear; outside-click closes the flyout; bottom status reports the active tool.
- Gameplay buddy: ordinary walking/ambient hand/foot rotation is calmer, while throws/impacts still feel physical.
- Work: LMB+wheel resize feels controlled; first reward/resize/exit SFX are audible but quiet; Grab is active after exit.
- Decorate Room: mode changes, delete staging, free placement, funds, wallpaper dirty state, and final room save/revert flow are understandable.
- Audio: repeated UI use is not fatiguing and there are no obviously doubled/missing cues in the changed flows.

If this manual pass is accepted and no validator failures remain, Sections 1–7 can be marked closed and the next implementation slice may move to Potion Shop.
