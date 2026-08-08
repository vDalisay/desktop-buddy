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
            root.AddChild(preview);
            var workspace = new BuddyStudioWorkspace();
            workspace.Configure(session, economy, preview, () => { });
            root.AddChild(workspace);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            int categories = workspace.CategoryStrip.FindChildren("Category_*", "Button", true, false).Count;
            bool composed = categories == 12 &&
                workspace.FindChild("BuddyStudioPreviewPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioCatalogPane", true, false) is Control &&
                workspace.FindChild("BuddyStudioInspectorPane", true, false) is Control &&
                root.FindChild("BuddyStudioDirtyDialog", true, false) is PanelContainer;
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
                session.CanSave && !workspace.SaveAction.Disabled && workspace.BuyAction.Disabled;
            checks.Add(new StartupCheck(
                "bs6_buy_refreshes_ownership_and_save_eligibility",
                purchaseRefresh,
                $"owned={economy.IsUnlocked(ContentIds.CosmeticWorkGlasses)} saveDisabled={workspace.SaveAction.Disabled}"));

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
