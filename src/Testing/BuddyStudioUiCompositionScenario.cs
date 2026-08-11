using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class BuddyStudioUiCompositionScenario : IScenario
{
    public string Id => "buddy_studio_ui_composition";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        var root = new Control { Name = "BuddyStudioScenarioRoot" };
        try
        {
            string[] releasedCosmetics =
            [
                ContentIds.CosmeticHairShortSweep, ContentIds.CosmeticNoseButton,
                ContentIds.CosmeticEarsRoundTabs, ContentIds.CosmeticHeadwearSoftCap,
                ContentIds.CosmeticTopUtilityBib, ContentIds.CosmeticShoesSoftSteps,
            ];
            ToolCatalogue production = CatalogueLoader.Catalogue;
            bool catalogueClosed = releasedCosmetics.All(id =>
                    production.TryGet(id, out CatalogueEntry entry) &&
                    entry.Kind == CatalogueEntryKind.Cosmetic && entry.Visible && entry.HasValidPrice) &&
                CataloguePolicy.CosmeticEntries(production).Select(entry => entry.ContentId).SequenceEqual(releasedCosmetics) &&
                !production.Contains(ContentIds.CosmeticWorkGlasses) &&
                CataloguePolicy.ShopEntries(production).Count == 12 &&
                CataloguePolicy.SelectableEntries(production).Count == 16;
            checks.Add(new StartupCheck(
                "bs7_authored_sales_are_studio_only_and_keep_sixteen_tool_schedule",
                catalogueClosed,
                $"cosmetics={CataloguePolicy.CosmeticEntries(production).Count} shop={CataloguePolicy.ShopEntries(production).Count} tools={CataloguePolicy.SelectableEntries(production).Count}"));

            int definitions = 0;
            bool thumbnails = true;
            foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>().Distinct())
            foreach (CosmeticDefinition definition in CharacterFeatureCatalog.Shipped.GetDefinitions(slot))
            {
                definitions++;
                thumbnails &= GodotObject.IsInstanceValid(BuddyStudioThumbnailCache.For(definition));
            }
            checks.Add(new StartupCheck(
                "bs7_trusted_thumbnails_are_cached_for_every_definition",
                thumbnails && BuddyStudioThumbnailCache.Count == definitions,
                $"cached={BuddyStudioThumbnailCache.Count} definitions={definitions}"));

            var progress = new BuddyProgressState(0.01, initialBalanceMilliCredits: 1000);
            var rewardStore = new InMemoryProgressStore();
            var rewardSaves = new SaveCoordinator(progress, rewardStore);
            var workProgress = new WorkProgressState();
            var workRewardService = new WorkFirstEntryRewardService(progress, workProgress, rewardSaves);
            var catalogue = new ToolCatalogue([
                new CatalogueEntry(
                    ContentIds.CosmeticHairShortSweep,
                    CatalogueEntryKind.Cosmetic,
                    2000,
                    0,
                    true,
                    "cosmetic.hair.short_sweep.name",
                    "cosmetic.hair.short_sweep.description"),
            ]);
            var economy = new EconomyService(progress, catalogue);
            var library = new CharacterLibraryIndex(new CharacterFileSystem(), context.Root);
            Guid id = Guid.Parse("8b600000-0000-4000-8000-000000000001");
            await context.Store.SaveAsync(CharacterDocument.CreateDefault(id, "UI Buddy"), CancellationToken.None);
            var session = new CharacterEditorSession(
                context.Store,
                library,
                context.Coordinator,
                context.Preview,
                economy: economy);
            await session.SelectAsync(id);

            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            tree.Root.AddChild(root);
            var preview = new Control { Name = "ScenarioPreview" };
            var camera = new Camera3D
            {
                Name = "ScenarioPreviewCamera",
                Position = new Vector3(0, 0, 600),
                Size = 400,
            };
            var paintCanvas = new Control { Name = "CharacterPaintCanvas", Visible = true };
            preview.AddChild(paintCanvas);
            preview.AddChild(camera);
            root.AddChild(preview);
            var status = new Label { Name = "CharacterEditorStatus" };
            root.AddChild(status);
            int durableSaves = 0;
            bool closed = false;
            var workspace = new BuddyStudioWorkspace();
            workspace.Configure(session, economy, preview, camera, status, () => closed = true, () =>
            {
                durableSaves++;
                return Task.CompletedTask;
            });
            root.AddChild(workspace);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            workspace.AttachPreview();

            bool headFrame = workspace.ViewZoom == 1.0f &&
                workspace.PreviewFocus.IsEqualApprox(new Vector2(0, 50)) &&
                Mathf.IsEqualApprox(workspace.PreviewCameraSize, 105) &&
                workspace.FindChild("BuddyStudioZoomOut", true, false) is Button &&
                workspace.FindChild("BuddyStudioZoomIn", true, false) is Button &&
                workspace.FindChild("BuddyStudioResetView", true, false) is Button;
            checks.Add(new StartupCheck(
                "bs7_studio_opens_with_zoomed_head_frame_and_visible_view_controls",
                headFrame,
                $"focus={workspace.PreviewFocus} size={workspace.PreviewCameraSize} zoom={workspace.ViewZoom}"));

            ((Button)workspace.FindChild("BuddyStudioZoomIn", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            bool zoomed = workspace.ViewZoom > 1.0f && workspace.PreviewCameraSize < 105;
            workspace.SelectCategory(CharacterFeatureSlot.Tops);
            bool torsoFrame = workspace.PreviewFocus.IsEqualApprox(Vector2.Zero) &&
                Mathf.IsEqualApprox(workspace.PreviewCameraSize, 135);
            workspace.SelectCategory(CharacterFeatureSlot.Shoes);
            bool feetFrame = workspace.PreviewFocus.IsEqualApprox(new Vector2(0, -55)) &&
                Mathf.IsEqualApprox(workspace.PreviewCameraSize, 105);
            ((Button)workspace.FindChild("BuddyStudioResetView", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            bool resetFeet = workspace.ViewZoom == 1.0f && Mathf.IsEqualApprox(workspace.PreviewCameraSize, 105);
            checks.Add(new StartupCheck(
                "bs7_view_zoom_reset_and_category_focus_cover_head_torso_feet",
                zoomed && torsoFrame && feetFrame && resetFeet,
                $"zoomed={zoomed} torso={torsoFrame} feet={feetFrame} reset={resetFeet}"));

            int categories = workspace.CategoryStrip.FindChildren("Category_*", "Button", true, false).Count;
            bool accessoriesHidden = workspace.CategoryStrip.FindChild("Category_accessories", true, false) is null;
            bool composed = categories == 11 && accessoriesHidden &&
                workspace.FindChild("BuddyStudioPreviewPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioCatalogPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioInspectorPane", true, false) is Control &&
                root.FindChild("BuddyStudioDirtyDialog", true, false) is PanelContainer &&
                status.GetParent() == root && !paintCanvas.Visible;
            checks.Add(new StartupCheck(
                "user_test_studio_hides_accessories_but_keeps_complete_demo_workspace",
                composed,
                $"categories={categories} accessoriesHidden={accessoriesHidden} statusParent={status.GetParent()?.Name}"));

            workspace.SelectCategory(CharacterFeatureSlot.Face);
            ((Button)workspace.FindChild("CategoryNext", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            var presets = (GridContainer)workspace.FindChild("BuddyStudioColorPresets", true, false);
            Button saveAction = (Button)workspace.FindChild("BuddyStudioSave", true, false);
            Button exitAction = (Button)workspace.FindChild("BuddyStudioCancel", true, false);
            bool studioLayoutFollowup = workspace.SelectedSlot == CharacterFeatureSlot.Hair &&
                presets.SizeFlagsHorizontal == Control.SizeFlags.ExpandFill &&
                presets.GetChildren().OfType<Button>().All(button =>
                    button.SizeFlagsHorizontal == Control.SizeFlags.ExpandFill) &&
                saveAction.GetParent()?.GetParent()?.GetParent()?.GetParent()?.Name == "BuddyStudioInspectorPane" &&
                exitAction.Text == "Exit";
            checks.Add(new StartupCheck(
                "user_test_studio_arrows_switch_tabs_and_full_width_swatches_actions_stay_inside_inspector",
                studioLayoutFollowup,
                $"slot={workspace.SelectedSlot} swatches={presets.SizeFlagsHorizontal} exit={exitAction.Text}"));

            workspace.SelectCategory(CharacterFeatureSlot.Glasses);
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesWorkClassic);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool lockedRewardCopy = session.HasUnownedPreviews && workspace.BuyAction.Disabled &&
                workspace.BuyAction.Text == "Earn in Work Mode";

            WorkFirstEntryRewardResult workReward = await workRewardService.EnsureAsync();
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesNone);
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesWorkClassic);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool earnedPreviewed = workReward.WasFirstEntry && workReward.OwnershipGranted &&
                lockedRewardCopy && progress.IsToolUnlocked(ContentIds.CosmeticWorkGlasses) &&
                !production.Contains(ContentIds.CosmeticWorkGlasses) &&
                session.HasOwnedPreviews && !session.HasUnownedPreviews &&
                workspace.BuyAction.Text == "Equip" && !workspace.BuyAction.Disabled &&
                CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Glasses) ==
                    CharacterFeatureIds.GlassesNone;
            workspace.CatalogGrid.Activate(CharacterFeatureIds.GlassesWorkClassic);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool doubleClickEquipped = !session.HasOwnedPreviews && !session.HasUnownedPreviews &&
                workspace.BuyAction.Text == "Equipped" && workspace.BuyAction.Disabled &&
                CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Glasses) ==
                    CharacterFeatureIds.GlassesWorkClassic;
            checks.Add(new StartupCheck(
                "user_test_owned_catalogue_single_click_previews_and_double_click_equips",
                earnedPreviewed && doubleClickEquipped,
                $"previewed={earnedPreviewed} equipped={doubleClickEquipped} reward={workReward}"));
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesNone);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool previewTileAccented = workspace.CatalogGrid.IsAccented(CharacterFeatureIds.GlassesNone);
            bool equippedTileNotAccented = !workspace.CatalogGrid.IsAccented(CharacterFeatureIds.GlassesWorkClassic);
            checks.Add(new StartupCheck(
                "user_test_preview_tile_owns_the_single_thick_title_blue_border",
                previewTileAccented && equippedTileNotAccented &&
                    workspace.CatalogGrid.SelectedId == CharacterFeatureIds.GlassesNone,
                $"preview={previewTileAccented} equipped={equippedTileNotAccented} selected={workspace.CatalogGrid.SelectedId}"));
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesWorkClassic);

            Button glassesLarger = (Button)workspace.FindChild("BuddyStudioLarger", true, false);
            double workingGlassesScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.WorkingDocument!, CharacterFeatureSlot.Glasses).Scale;
            glassesLarger.EmitSignal(BaseButton.SignalName.Pressed);
            double scaledWorkingGlasses = CharacterDocumentEditor.ReadFeatureTransform(
                session.WorkingDocument!, CharacterFeatureSlot.Glasses).Scale;
            checks.Add(new StartupCheck(
                "user_test_auto_equipped_cosmetic_remains_directly_editable",
                scaledWorkingGlasses > workingGlassesScale,
                $"scale={workingGlassesScale}->{scaledWorkingGlasses}"));

            workspace.SelectCategory(CharacterFeatureSlot.Hair);
            workspace.CatalogGrid.Select(CharacterFeatureIds.HairShortSweep);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool insufficientGated = session.HasUnownedPreviews && session.CanSave &&
                !workspace.SaveAction.Disabled && workspace.BuyAction.Disabled &&
                workspace.BuyAction.Text.StartsWith("Buy", StringComparison.Ordinal) &&
                workspace.CatalogGrid.SelectedId == CharacterFeatureIds.HairShortSweep;
            checks.Add(new StartupCheck(
                "user_test_unowned_preview_keeps_buy_state_clear_when_unaffordable",
                insufficientGated,
                $"preview={session.HasUnownedPreviews} saveDisabled={workspace.SaveAction.Disabled} buy={workspace.BuyAction.Text} disabled={workspace.BuyAction.Disabled}"));

            workspace.SelectCategory(CharacterFeatureSlot.Eyes);
            bool tabRestoredEquipped = !session.HasOwnedPreviews && !session.HasUnownedPreviews &&
                CharacterDocumentEditor.ReadFeatureId(session.PreviewDocument!, CharacterFeatureSlot.Hair) ==
                    CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Hair);
            checks.Add(new StartupCheck(
                "user_test_changing_tabs_clears_transient_cosmetic_preview",
                tabRestoredEquipped,
                $"ownedPreview={session.HasOwnedPreviews} unownedPreview={session.HasUnownedPreviews}"));

            workspace.SelectCategory(CharacterFeatureSlot.Hair);
            workspace.CatalogGrid.Select(CharacterFeatureIds.HairShortSweep);

            economy.DepositPassive(4000);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            checks.Add(new StartupCheck(
                "user_test_unowned_preview_enables_explicit_buy_when_funded",
                !workspace.BuyAction.Disabled && session.CanSave &&
                    workspace.BuyAction.Text.StartsWith("Buy", StringComparison.Ordinal),
                $"balance={economy.BalanceMilliCredits} action={workspace.BuyAction.Text}"));

            workspace.CatalogGrid.Activate(CharacterFeatureIds.HairShortSweep);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool purchasedAndEquipped = economy.IsUnlocked(ContentIds.CosmeticHairShortSweep) &&
                durableSaves == 1 && session.CanSave &&
                !session.HasOwnedPreviews && !session.HasUnownedPreviews &&
                workspace.BuyAction.Disabled && workspace.BuyAction.Text == "Equipped" &&
                CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Hair) ==
                    CharacterFeatureIds.HairShortSweep;
            checks.Add(new StartupCheck(
                "user_test_buy_is_one_clear_purchase_and_equip_action",
                purchasedAndEquipped,
                $"owned={economy.IsUnlocked(ContentIds.CosmeticHairShortSweep)} saves={durableSaves} action={workspace.BuyAction.Text}"));

            workspace.SaveAction.EmitSignal(BaseButton.SignalName.Pressed);
            for (int frame = 0; frame < 120; frame++)
            {
                context.Coordinator.PhysicsTick();
                if (!session.IsDirty && context.Selection.ActiveCharacterId == id)
                    break;
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            context.Coordinator.PhysicsTick();
            checks.Add(new StartupCheck(
                "bs7_save_immediately_persists_and_applies_the_opened_buddy",
                !session.IsDirty && context.Store.SaveCount > 0 && context.Selection.ActiveCharacterId == id,
                $"dirty={session.IsDirty} saves={context.Store.SaveCount} active={context.Selection.ActiveCharacterId}"));
            CharacterEditorActionResult cleanClose = session.RequestClose();
            checks.Add(new StartupCheck(
                "user_test_clean_cancel_requires_no_unsaved_prompt",
                cleanClose.Completed && !cleanClose.NeedsUnsavedDecision,
                $"completed={cleanClose.Completed} prompt={cleanClose.NeedsUnsavedDecision}"));

            workspace.SelectCategory(CharacterFeatureSlot.Eyes);
            Button move = (Button)workspace.FindChild("BuddyStudioMove", true, false);
            Button larger = (Button)workspace.FindChild("BuddyStudioLarger", true, false);
            Button smaller = (Button)workspace.FindChild("BuddyStudioSmaller", true, false);
            Button reset = (Button)workspace.FindChild("BuddyStudioReset", true, false);
            double beforeScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes).Scale;
            larger.EmitSignal(BaseButton.SignalName.Pressed);
            double largerScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes).Scale;
            smaller.EmitSignal(BaseButton.SignalName.Pressed);
            double smallerScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes).Scale;
            for (int press = 0; press < 20; press++)
                larger.EmitSignal(BaseButton.SignalName.Pressed);
            double boundedScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes).Scale;
            CosmeticDefinition eyes = CharacterFeatureCatalog.Shipped.ResolveDefinition(
                CharacterFeatureSlot.Eyes,
                CharacterDocumentEditor.ReadFeatureId(session.PreviewDocument!, CharacterFeatureSlot.Eyes),
                out _);
            bool transforms = !larger.Disabled && !smaller.Disabled &&
                largerScale > beforeScale && smallerScale < largerScale &&
                boundedScale <= eyes.TransformBounds.MaximumScale;
            checks.Add(new StartupCheck(
                "bs7_supported_smaller_and_larger_mutate_bounded_scale",
                transforms,
                $"scale={beforeScale}->{largerScale}->{smallerScale}->{boundedScale} max={eyes.TransformBounds.MaximumScale}"));

            int previewZ = preview.ZIndex;
            Control.CursorShape previewCursor = preview.MouseDefaultCursorShape;
            NormalizedFeatureTransform beforeMove = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes);
            move.EmitSignal(BaseButton.SignalName.Pressed);
            workspace._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            bool escapeExited = !workspace.MoveMode;
            move.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Control moveBlocker = (Control)root.FindChild("BuddyStudioMoveBlocker", true, false);
            Rect2 previewRect = preview.GetGlobalRect();
            Vector2 inside = previewRect.GetCenter();
            Vector2 outside = previewRect.End + new Vector2(20, 20);
            moveBlocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = inside,
                GlobalPosition = inside,
            });
            moveBlocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion
            {
                ButtonMask = MouseButtonMask.Left,
                Relative = new Vector2(8, -4),
                Position = inside + new Vector2(8, -4),
                GlobalPosition = inside + new Vector2(8, -4),
            });
            moveBlocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = inside + new Vector2(8, -4),
                GlobalPosition = inside + new Vector2(8, -4),
            });
            NormalizedFeatureTransform afterMove = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes);
            bool focusedMove = escapeExited && workspace.MoveMode && moveBlocker.Visible &&
                preview.MouseDefaultCursorShape == Control.CursorShape.Move && afterMove != beforeMove;
            moveBlocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = outside,
                GlobalPosition = outside,
            });
            bool moveRestored = !workspace.MoveMode && !moveBlocker.Visible &&
                preview.MouseDefaultCursorShape == previewCursor && preview.ZIndex == previewZ;
            checks.Add(new StartupCheck(
                "bs7_move_mode_drags_inside_the_preview_and_restores_on_outside_click",
                focusedMove && moveRestored,
                $"focused={focusedMove} restored={moveRestored} transform={beforeMove}->{afterMove}"));

            Control transformActions = (Control)workspace.FindChild("BuddyStudioTransformActions", true, false);
            bool fillsWidth = transformActions.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                smaller.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                larger.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                move.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                reset.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill);
            checks.Add(new StartupCheck(
                "bs6_transform_actions_evenly_fill_section_width",
                fillsWidth,
                $"grid={transformActions.SizeFlagsHorizontal} buttons={smaller.SizeFlagsHorizontal}"));

            workspace.SelectCategory(CharacterFeatureSlot.Hair);
            bool forbiddenTransformDisabled = smaller.Disabled && larger.Disabled && move.Disabled && reset.Disabled;
            checks.Add(new StartupCheck(
                "bs7_transform_controls_disable_only_for_forbidden_policy",
                forbiddenTransformDisabled,
                $"smaller={smaller.Disabled} larger={larger.Disabled} move={move.Disabled} reset={reset.Disabled}"));

            workspace.SelectCategory(CharacterFeatureSlot.Eyes);
            smaller.EmitSignal(BaseButton.SignalName.Pressed);
            long savesBeforeClose = context.Store.SaveCount;
            ((Button)workspace.FindChild("BuddyStudioCancel", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            HBoxContainer unsavedActions = (HBoxContainer)root.FindChild("BuddyStudioUnsavedActions", true, false);
            Control unsavedSpacer = (Control)root.FindChild("BuddyStudioUnsavedSpacer", true, false);
            bool centeredBottom = unsavedActions.Alignment == BoxContainer.AlignmentMode.Center &&
                unsavedSpacer.SizeFlagsVertical.HasFlag(Control.SizeFlags.ExpandFill);
            ((Button)root.FindChild("BuddyStudioUnsavedSave", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            for (int frame = 0; frame < 120 && !closed; frame++)
            {
                context.Coordinator.PhysicsTick();
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            context.Coordinator.PhysicsTick();
            checks.Add(new StartupCheck(
                "bs7_cancel_unsaved_save_persists_applies_and_closes",
                centeredBottom && closed && !session.IsDirty &&
                    context.Store.SaveCount > savesBeforeClose && context.Selection.ActiveCharacterId == id,
                $"centered={centeredBottom} closed={closed} dirty={session.IsDirty} saves={savesBeforeClose}->{context.Store.SaveCount}"));

            workspace.DetachPreview();
            checks.Add(new StartupCheck(
                "bs6_studio_hides_paint_but_restores_preview_paint_state",
                paintCanvas.Visible && preview.GetParent() == root && status.GetParent() == root &&
                    camera.Position.IsEqualApprox(new Vector3(0, 0, 600)) && Mathf.IsEqualApprox(camera.Size, 400),
                $"paint={paintCanvas.Visible} previewParent={preview.GetParent()?.Name} camera={camera.Position}/{camera.Size}"));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }
        return CharacterEditorScenarioSupport.Result(checks, seed);
    }
}
