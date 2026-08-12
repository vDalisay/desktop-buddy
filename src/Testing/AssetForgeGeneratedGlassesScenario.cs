using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// CI generates glasses.ci_pink_round immediately before Godot import, then this scenario proves
/// that the generated trust/catalogue/economy/editor/render/persistence seams agree on that asset.
/// The fixture itself is intentionally not committed.
/// </summary>
public sealed class AssetForgeGeneratedGlassesScenario : IScenario
{
    private const string FeatureId = "glasses.ci_pink_round";
    private const string ContentId = "cosmetic.glasses.ci_pink_round";

    public string Id => "asset_forge_generated_glasses";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            BuddyGeneratedCosmeticRegistry generated = BuddyGeneratedCosmeticRegistry.Current;
            bool resourceLoaded = generated.TryGet(FeatureId, out GeneratedBuddyCosmeticResource resource) &&
                resource.Slot == CharacterFeatureSlot.Glasses &&
                string.Equals(resource.ContentId, ContentId, StringComparison.Ordinal) &&
                GodotObject.IsInstanceValid(resource.MeshScene) &&
                GodotObject.IsInstanceValid(resource.AlbedoTexture) &&
                GodotObject.IsInstanceValid(resource.Thumbnail);
            checks.Add(new StartupCheck(
                "af_generated_resource_imported",
                resourceLoaded,
                resourceLoaded ? $"hash={resource.CanonicalAssetHash}" : "CI generated glasses resource was not loaded."));
            if (!resourceLoaded)
                return CharacterEditorScenarioSupport.Result(checks, seed);

            CharacterFeatureCatalog features = generated.FeatureCatalog;
            CosmeticDefinition cosmetic = features.ResolveDefinition(
                CharacterFeatureSlot.Glasses,
                FeatureId,
                out bool knownFeature);
            bool featureComposed = knownFeature &&
                string.Equals(cosmetic.OwnershipContentId, ContentId, StringComparison.Ordinal) &&
                !cosmetic.IsFreeDefault;
            checks.Add(new StartupCheck(
                "af_generated_feature_composed",
                featureComposed,
                $"known={knownFeature} ownership={cosmetic.OwnershipContentId}"));

            bool commerceComposed = CatalogueLoader.Catalogue.TryGet(ContentId, out CatalogueEntry sale) &&
                sale.Kind == CatalogueEntryKind.Cosmetic && sale.Visible && sale.PriceMilliCredits == 125_000;
            checks.Add(new StartupCheck(
                "af_generated_commerce_composed",
                commerceComposed,
                commerceComposed ? $"price={sale.PriceMilliCredits}" : "Generated sale was not in merged catalogue."));
            if (!featureComposed || !commerceComposed)
                return CharacterEditorScenarioSupport.Result(checks, seed);

            var store = new CharacterStore(
                new CharacterFileSystem(),
                context.Root,
                featureCatalog: features);
            var selection = new CharacterSelectionState();
            var progressStore = new InMemoryProgressStore();
            var progress = new BuddyProgressState(
                cashPerPain: 0.01,
                initialBalanceMilliCredits: 500_000);
            var saves = new SaveCoordinator(progress, progressStore, selection: selection);
            var economy = new EconomyService(progress, CatalogueLoader.Catalogue);
            var coordinator = new CharacterSelectionCoordinator(
                store,
                selection,
                context.Preview,
                saves,
                features);
            var library = new CharacterLibraryIndex(new CharacterFileSystem(), context.Root);
            var session = new CharacterEditorSession(
                store,
                library,
                coordinator,
                context.Preview,
                economy: economy,
                featureCatalog: features);

            Guid id = Guid.Parse("af100000-0000-4000-8000-000000000001");
            CharacterSaveResult baselineSaved = await store.SaveAsync(
                CharacterDocument.CreateDefault(id, "Asset Forge Fixture"),
                CancellationToken.None);
            CharacterEditorActionResult opened = await session.OpenActiveAsync(id);
            CharacterEditorActionResult previewed = session.PreviewCosmetic(
                CharacterFeatureSlot.Glasses,
                FeatureId);
            bool previewOnly = baselineSaved.IsSuccess && opened.Completed && previewed.Completed &&
                session.HasUnownedPreview(CharacterFeatureSlot.Glasses) &&
                !session.CanSave &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesNone &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.PreviewDocument!, CharacterFeatureSlot.Glasses) == FeatureId;
            checks.Add(new StartupCheck(
                "af_generated_unowned_preview",
                previewOnly,
                $"opened={opened.Completed} preview={previewed.Completed} canSave={session.CanSave}"));

            CharacterEditorActionResult bought = session.BuyPreviewedCosmetic(CharacterFeatureSlot.Glasses);
            CharacterEditorActionResult equipped = session.EquipPreviewedCosmetic(CharacterFeatureSlot.Glasses);
            CharacterEditorActionResult saved = await session.SaveAsync();
            await saves.FlushProgressAsync(force: true);
            bool purchaseEquipSave = bought.Completed && equipped.Completed && saved.Completed &&
                economy.IsUnlocked(ContentId) && session.CanSave &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses) == FeatureId;
            checks.Add(new StartupCheck(
                "af_generated_purchase_equip_save",
                purchaseEquipSave,
                $"buy={bought.Completed} equip={equipped.Completed} save={saved.Completed} owned={economy.IsUnlocked(ContentId)}"));

            CharacterLoadResult reloaded = await store.LoadAsync(id, CancellationToken.None);
            CompiledCharacterAppearance? compiledAppearance = null;
            bool compileSucceeded = false;
            if (reloaded.Document is not null)
            {
                CharacterCompileResult compileResult = CharacterCompiler.Compile(reloaded.Document, features);
                compileSucceeded = compileResult.IsSuccess;
                compiledAppearance = compileResult.Appearance;
            }
            bool survivesRestart = reloaded.IsSuccess && compileSucceeded && compiledAppearance is not null &&
                CharacterDocumentEditor.ReadFeatureId(
                    reloaded.Document!, CharacterFeatureSlot.Glasses) == FeatureId &&
                compiledAppearance.Glasses.ResolvedFeatureId == FeatureId;
            checks.Add(new StartupCheck(
                "af_generated_id_survives_reload_and_compile",
                survivesRestart,
                $"load={reloaded.Status} compile={compileSucceeded} glasses={compiledAppearance?.Glasses.ResolvedFeatureId}"));

            if (compiledAppearance is not null)
            {
                context.Preview.ApplyAppearance(compiledAppearance);
                context.Preview.RefreshCharacterCompositors();
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Node3D? visual = context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Glasses);
            int meshCount = visual is null
                ? 0
                : (visual is MeshInstance3D ? 1 : 0) +
                  visual.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Count();
            int physicsNodes = visual is null ? 0 : CountPhysics(visual);
            bool generatedRendered = GodotObject.IsInstanceValid(visual) && meshCount == 1 && physicsNodes == 0;
            checks.Add(new StartupCheck(
                "af_generated_glb_renders_visual_only",
                generatedRendered,
                $"visual={GodotObject.IsInstanceValid(visual)} meshes={meshCount} physics={physicsNodes}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return CharacterEditorScenarioSupport.Result(checks, seed);
    }

    private static int CountPhysics(Node node)
    {
        int count = node is CollisionObject2D or CollisionObject3D or Joint2D or Joint3D ? 1 : 0;
        foreach (Node child in node.GetChildren())
            count += CountPhysics(child);
        return count;
    }
}
