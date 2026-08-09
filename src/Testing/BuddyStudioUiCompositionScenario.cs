using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
                "bs7_original_thumbnails_are_cached_for_every_visible_definition",
                thumbnails && BuddyStudioThumbnailCache.Count == definitions,
                $"cached={BuddyStudioThumbnailCache.Count} definitions={definitions}"));

            var progress = new BuddyProgressState(0.01, initialBalanceMilliCredits: 1000);
            var rewardStore = new InMemoryProgressStore();
            var rewardSaves = new SaveCoordinator(progress, rewardStore);
            var workProgress = new WorkProgressState();
            var workRewardService =
                new WorkFirstEntryRewardService(progress, workProgress, rewardSaves);
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
            ((Button)workspace.FindChild("BuddyStudioZoomIn", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            ((Button)workspace.FindChild("BuddyStudioResetView", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            bool resetFeet = workspace.ViewZoom == 1.0f && Mathf.IsEqualApprox(workspace.PreviewCameraSize, 105);
            checks.Add(new StartupCheck(
                "bs7_view_zoom_reset_and_category_focus_cover_head_torso_feet",
                zoomed && torsoFrame && feetFrame && resetFeet,
                $"zoomed={zoomed} torso={torsoFrame} feet={feetFrame} reset={resetFeet}"));

            int categories = workspace.CategoryStrip.FindChildren("Category_*", "Button", true, false).Count;
            bool composed = categories == 12 &&
                workspace.FindChild("BuddyStudioPreviewPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioCatalogPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioInspectorPane", true, false) is Control &&
                root.FindChild("BuddyStudioDirtyDialog", true, false) is PanelContainer &&
                workspace.FindChild("BuddyStudioStatus", true, false) is null &&
                workspace.FindChild("CharacterEditorStatus", true, false) is null &&
                status.GetParent() == root &&
                !paintCanvas.Visible;
            checks.Add(new StartupCheck(
                "bs6_shared_controls_compose_twelve_accessible_categories",
                composed,
                $"categories={categories} statusParent={status.GetParent()?.Name}"));

            workspace.SelectCategory(CharacterFeatureSlot.Glasses);
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesWorkClassic);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool lockedRewardCopy = session.HasUnownedPreviews && workspace.BuyAction.Disabled &&
                workspace.BuyAction.Text == "Earn in Work Mode";
            WorkFirstEntryRewardResult workReward = await workRewardService.EnsureAsync();
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesNone);
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesWorkClassic);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool earnedCanEquip = workReward.WasFirstEntry && workReward.OwnershipGranted &&
                lockedRewardCopy &&
                progress.IsToolUnlocked(ContentIds.CosmeticWorkGlasses) &&
                !production.Contains(ContentIds.CosmeticWorkGlasses) &&
                session.HasOwnedPreviews && workspace.BuyAction.Text == "Equip" &&
                !workspace.BuyAction.Disabled;
            checks.Add(new StartupCheck(
                "bs7_work_reward_unlock_is_equip_only_and_never_purchasable",
                earnedCanEquip,
                $"locked={lockedRewardCopy} reward={workReward} owned={progress.IsToolUnlocked(ContentIds.CosmeticWorkGlasses)} action={workspace.BuyAction.Text}"));

            Button glassesLarger = (Button)workspace.FindChild("BuddyStudioLarger", true, false);
            double workingGlassesScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.WorkingDocument!, CharacterFeatureSlot.Glasses).Scale;
            double previewGlassesScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Glasses).Scale;
            glassesLarger.EmitSignal(BaseButton.SignalName.Pressed);
            double scaledPreviewGlasses = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Glasses).Scale;
            bool previewScaleChanged = scaledPreviewGlasses > previewGlassesScale &&
                CharacterDocumentEditor.ReadFeatureTransform(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses).Scale == workingGlassesScale;
            checks.Add(new StartupCheck(
                "bs7_supported_preview_scale_changes_without_early_equip",
                previewScaleChanged,
                $"working={workingGlassesScale} preview={previewGlassesScale}->{scaledPreviewGlasses}"));

            workspace.BuyAction.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool earnedEquipped = !session.HasOwnedPreviews &&
                workspace.BuyAction.Text == "Equipped" && workspace.BuyAction.Disabled &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses) ==
                    CharacterFeatureIds.GlassesWorkClassic;
            checks.Add(new StartupCheck(
                "bs7_earned_work_glasses_equip_the_working_character",
                earnedEquipped,
                $"action={workspace.BuyAction.Text} equipped={CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Glasses)}"));

            workspace.SelectCategory(CharacterFeatureSlot.Hair);
            workspace.CatalogGrid.Select(CharacterFeatureIds.HairShortSweep);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool insufficientGated = session.HasUnownedPreviews && !session.CanSave &&
                workspace.SaveAction.Disabled && workspace.BuyAction.Disabled &&
                workspace.CatalogGrid.SelectedId == CharacterFeatureIds.HairShortSweep;
            checks.Add(new StartupCheck(
                "bs7_insufficient_funds_disable_buy_without_losing_preview",
                insufficientGated,
                $"preview={session.HasUnownedPreviews} saveDisabled={workspace.SaveAction.Disabled} buyDisabled={workspace.BuyAction.Disabled}"));

            ((Button)workspace.FindChild("BuddyStudioCancel", true, false))
                .EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            ((Button)root.FindChild("BuddyStudioUnsavedSave", true, false))
                .EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool failedCloseStaysOpen = !closed && workspace.Visible && session.HasUnownedPreviews &&
                session.PendingAction == CharacterEditorPendingAction.None &&
                status.Text.Contains("Buy or deselect", StringComparison.Ordinal);
            checks.Add(new StartupCheck(
                "bs7_cancel_unsaved_save_failure_keeps_studio_open",
                failedCloseStaysOpen,
                $"closed={closed} pending={session.PendingAction} status={status.Text}"));

            economy.DepositPassive(4000);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            checks.Add(new StartupCheck(
                "bs6_unowned_preview_enables_buy_when_funded",
                !workspace.BuyAction.Disabled && !session.CanSave,
                $"balance={economy.BalanceMilliCredits} buyDisabled={workspace.BuyAction.Disabled}"));

            workspace.BuyAction.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool purchaseRefresh = economy.IsUnlocked(ContentIds.CosmeticHairShortSweep) && durableSaves == 1 &&
                session.CanSave && session.HasOwnedPreviews &&
                !workspace.BuyAction.Disabled && workspace.BuyAction.Text == "Equip" &&
                CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Hair) ==
                    CharacterFeatureIds.HairNone;
            checks.Add(new StartupCheck(
                "bs6_buy_refreshes_to_real_equip_action",
                purchaseRefresh,
                $"owned={economy.IsUnlocked(ContentIds.CosmeticHairShortSweep)} saves={durableSaves} action={workspace.BuyAction.Text}"));

            workspace.BuyAction.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool equipped = !session.HasOwnedPreviews && !workspace.SaveAction.Disabled &&
                workspace.BuyAction.Disabled && workspace.BuyAction.Text == "Equipped" &&
                CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Hair) ==
                    CharacterFeatureIds.HairShortSweep;
            checks.Add(new StartupCheck(
                "bs6_equip_action_mutates_working_copy",
                equipped,
                $"action={workspace.BuyAction.Text} saveDisabled={workspace.SaveAction.Disabled}"));

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
            workspace._UnhandledKeyInput(new InputEventKey
            {
                Pressed = true,
                Keycode = Key.Escape,
            });
            bool escapeExited = !workspace.MoveMode;
            move.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            // The blocker is hit first by real GUI picking, so drive move mode through it.
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
            bool insidePressKeptMoveMode = workspace.MoveMode && moveBlocker.Visible;
            moveBlocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion
            {
                ButtonMask = MouseButtonMask.Left,
                Relative = new Vector2(12, -8),
                Position = inside + new Vector2(12, -8),
                GlobalPosition = inside + new Vector2(12, -8),
            });
            bool moveCursor = moveBlocker.MouseDefaultCursorShape == Control.CursorShape.Move;
            moveBlocker.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = inside + new Vector2(12, -8),
                GlobalPosition = inside + new Vector2(12, -8),
            });
            NormalizedFeatureTransform afterMove = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes);
            bool focusedMove = escapeExited && insidePressKeptMoveMode && moveCursor &&
                workspace.MoveMode && moveBlocker.Visible &&
                moveBlocker.MouseFilter == Control.MouseFilterEnum.Stop &&
                preview.MouseDefaultCursorShape == Control.CursorShape.Move &&
                workspace.CatalogGrid.Visible && afterMove != beforeMove;
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
                $"focused={focusedMove} insideKept={insidePressKeptMoveMode} cursor={moveCursor} " +
                $"restored={moveRestored} rect={previewRect} transform={beforeMove}->{afterMove}"));

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
            bool forbiddenTransformDisabled =
                smaller.Disabled && larger.Disabled && move.Disabled && reset.Disabled;
            checks.Add(new StartupCheck(
                "bs7_transform_controls_disable_only_for_forbidden_policy",
                forbiddenTransformDisabled,
                $"smaller={smaller.Disabled} larger={larger.Disabled} move={move.Disabled} reset={reset.Disabled}"));

            workspace.SelectCategory(CharacterFeatureSlot.Eyes);
            smaller.EmitSignal(BaseButton.SignalName.Pressed);
            long savesBeforeClose = context.Store.SaveCount;
            ((Button)workspace.FindChild("BuddyStudioCancel", true, false))
                .EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            HBoxContainer unsavedActions =
                (HBoxContainer)root.FindChild("BuddyStudioUnsavedActions", true, false);
            Control unsavedSpacer = (Control)root.FindChild("BuddyStudioUnsavedSpacer", true, false);
            bool centeredBottom = unsavedActions.Alignment == BoxContainer.AlignmentMode.Center &&
                unsavedSpacer.SizeFlagsVertical.HasFlag(Control.SizeFlags.ExpandFill);
            ((Button)root.FindChild("BuddyStudioUnsavedSave", true, false))
                .EmitSignal(BaseButton.SignalName.Pressed);
            for (int frame = 0; frame < 120 && !closed; frame++)
            {
                context.Coordinator.PhysicsTick();
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            context.Coordinator.PhysicsTick();
            checks.Add(new StartupCheck(
                "bs7_cancel_unsaved_save_persists_applies_and_closes",
                centeredBottom && closed && !session.IsDirty &&
                    context.Store.SaveCount > savesBeforeClose &&
                    context.Selection.ActiveCharacterId == id,
                $"centered={centeredBottom} closed={closed} dirty={session.IsDirty} saves={savesBeforeClose}->{context.Store.SaveCount} active={context.Selection.ActiveCharacterId}"));

            workspace.DetachPreview();
            checks.Add(new StartupCheck(
                "bs6_studio_hides_paint_but_restores_preview_paint_state",
                paintCanvas.Visible && preview.GetParent() == root &&
                    status.GetParent() == root &&
                    camera.Position.IsEqualApprox(new Vector3(0, 0, 600)) &&
                    Mathf.IsEqualApprox(camera.Size, 400),
                $"paint={paintCanvas.Visible} previewParent={preview.GetParent()?.Name} statusParent={status.GetParent()?.Name} camera={camera.Position}/{camera.Size}"));
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
