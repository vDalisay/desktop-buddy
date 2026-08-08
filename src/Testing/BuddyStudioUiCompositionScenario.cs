using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence.Characters;
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
            var progress = new BuddyProgressState(0.01, initialBalanceMilliCredits: 5000);
            var catalogue = new ToolCatalogue([
                new CatalogueEntry(
                    ContentIds.CosmeticWorkGlasses,
                    CatalogueEntryKind.Cosmetic,
                    2000,
                    0,
                    true,
                    "cosmetic.work_glasses.name",
                    "cosmetic.work_glasses.description"),
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
            var paintCanvas = new Control { Name = "CharacterPaintCanvas", Visible = true };
            preview.AddChild(paintCanvas);
            root.AddChild(preview);
            var workspace = new BuddyStudioWorkspace();
            workspace.Configure(session, economy, preview, () => { });
            root.AddChild(workspace);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            workspace.AttachPreview();

            int categories = workspace.CategoryStrip.FindChildren("Category_*", "Button", true, false).Count;
            bool composed = categories == 12 &&
                workspace.FindChild("BuddyStudioPreviewPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioCatalogPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioInspectorPane", true, false) is Control &&
                root.FindChild("BuddyStudioDirtyDialog", true, false) is PanelContainer &&
                !paintCanvas.Visible;
            checks.Add(new StartupCheck(
                "bs6_shared_controls_compose_twelve_accessible_categories",
                composed,
                $"categories={categories}"));

            workspace.SelectCategory(CharacterFeatureSlot.Glasses);
            workspace.CatalogGrid.Select(CharacterFeatureIds.GlassesWorkClassic);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool previewGated = session.HasUnownedPreviews && !session.CanSave &&
                workspace.SaveAction.Disabled && !workspace.BuyAction.Disabled &&
                workspace.CatalogGrid.SelectedId == CharacterFeatureIds.GlassesWorkClassic;
            checks.Add(new StartupCheck(
                "bs6_unowned_preview_labels_and_gates_actions",
                previewGated,
                $"preview={session.HasUnownedPreviews} saveDisabled={workspace.SaveAction.Disabled} buyDisabled={workspace.BuyAction.Disabled}"));

            workspace.BuyAction.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool purchaseRefresh = economy.IsUnlocked(ContentIds.CosmeticWorkGlasses) &&
                session.CanSave && session.HasOwnedPreviews && workspace.SaveAction.Disabled &&
                !workspace.BuyAction.Disabled && workspace.BuyAction.Text == "Equip" &&
                CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Glasses) ==
                    CharacterFeatureIds.GlassesNone;
            checks.Add(new StartupCheck(
                "bs6_buy_refreshes_to_real_equip_action",
                purchaseRefresh,
                $"owned={economy.IsUnlocked(ContentIds.CosmeticWorkGlasses)} action={workspace.BuyAction.Text}"));

            workspace.BuyAction.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool equipped = !session.HasOwnedPreviews && !workspace.SaveAction.Disabled &&
                workspace.BuyAction.Disabled && workspace.BuyAction.Text == "Equipped" &&
                CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Glasses) ==
                    CharacterFeatureIds.GlassesWorkClassic;
            checks.Add(new StartupCheck(
                "bs6_equip_action_mutates_working_copy",
                equipped,
                $"action={workspace.BuyAction.Text} saveDisabled={workspace.SaveAction.Disabled}"));

            workspace.SelectCategory(CharacterFeatureSlot.Eyes);
            Button move = (Button)workspace.FindChild("BuddyStudioMove", true, false);
            Button larger = (Button)workspace.FindChild("BuddyStudioLarger", true, false);
            double beforeScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes).Scale;
            move.EmitSignal(BaseButton.SignalName.Pressed);
            larger.EmitSignal(BaseButton.SignalName.Pressed);
            double afterScale = CharacterDocumentEditor.ReadFeatureTransform(
                session.PreviewDocument!, CharacterFeatureSlot.Eyes).Scale;
            bool transforms = workspace.MoveMode && afterScale > beforeScale;
            checks.Add(new StartupCheck(
                "bs6_contextual_move_and_scale_controls_mutate_working_copy",
                transforms,
                $"move={workspace.MoveMode} scale={beforeScale}->{afterScale}"));

            Control transformActions = (Control)workspace.FindChild("BuddyStudioTransformActions", true, false);
            Button smaller = (Button)workspace.FindChild("BuddyStudioSmaller", true, false);
            Button reset = (Button)workspace.FindChild("BuddyStudioReset", true, false);
            bool fillsWidth = transformActions.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                smaller.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                larger.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                move.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill) &&
                reset.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.ExpandFill);
            checks.Add(new StartupCheck(
                "bs6_transform_actions_evenly_fill_section_width",
                fillsWidth,
                $"grid={transformActions.SizeFlagsHorizontal} buttons={smaller.SizeFlagsHorizontal}"));

            workspace.DetachPreview();
            checks.Add(new StartupCheck(
                "bs6_studio_hides_paint_but_restores_preview_paint_state",
                paintCanvas.Visible && preview.GetParent() == root,
                $"paint={paintCanvas.Visible} previewParent={preview.GetParent()?.Name}"));
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
