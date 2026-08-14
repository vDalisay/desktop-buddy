using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class AssetForgeGeneratedReplacementScenario : IScenario
{
    private const string TopFeatureId = "top.ci_pear_torso";
    private const string TopContentId = "cosmetic.top.ci_pear_torso";
    private const string ShoesFeatureId = "shoes.ci_soft_foot";
    private const string ShoesContentId = "cosmetic.shoes.ci_soft_foot";

    public string Id => "asset_forge_generated_replacements";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context = await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            BuddyGeneratedCosmeticRegistry registry = BuddyGeneratedCosmeticRegistry.Current;
            bool resourcesLoaded = registry.TryGet(TopFeatureId, out GeneratedBuddyCosmeticResource topResource) &&
                                   registry.TryGet(ShoesFeatureId, out GeneratedBuddyCosmeticResource shoesResource) &&
                                   topResource.Slot == CharacterFeatureSlot.Tops &&
                                   shoesResource.Slot == CharacterFeatureSlot.Shoes;
            checks.Add(new StartupCheck("af_generated_replacement_resources_loaded", resourcesLoaded,
                $"top={registry.FeatureCatalog.Contains(CharacterFeatureSlot.Tops, TopFeatureId)} shoes={registry.FeatureCatalog.Contains(CharacterFeatureSlot.Shoes, ShoesFeatureId)}"));

            bool commerce = CatalogueLoader.Catalogue.TryGet(TopContentId, out CatalogueEntry topSale) &&
                            CatalogueLoader.Catalogue.TryGet(ShoesContentId, out CatalogueEntry shoesSale) &&
                            topSale.PriceMilliCredits == 175_000 && shoesSale.PriceMilliCredits == 160_000;
            checks.Add(new StartupCheck("af_generated_replacement_commerce_loaded", commerce,
                $"top={topSale.PriceMilliCredits} shoes={shoesSale.PriceMilliCredits}"));

            var visualCatalog = new BuddyCosmeticVisualCatalog(registry.FeatureCatalog, registry);
            BuddyCosmeticVisualDefinition topVisual = visualCatalog.Resolve(CharacterFeatureSlot.Tops, TopFeatureId, out bool topFallback);
            BuddyCosmeticVisualDefinition shoesVisual = visualCatalog.Resolve(CharacterFeatureSlot.Shoes, ShoesFeatureId, out bool shoesFallback);
            bool modes = !topFallback && !shoesFallback &&
                         topVisual.ApplicationMode == BuddyCosmeticApplicationMode.PartReplacement &&
                         shoesVisual.ApplicationMode == BuddyCosmeticApplicationMode.PairedPartReplacement;
            checks.Add(new StartupCheck("af_generated_replacement_application_modes", modes,
                $"top={topVisual.ApplicationMode} shoes={shoesVisual.ApplicationMode}"));

            CosmeticDefinition topDefinition = registry.FeatureCatalog.ResolveDefinition(CharacterFeatureSlot.Tops, TopFeatureId, out bool knownTop);
            CosmeticDefinition shoesDefinition = registry.FeatureCatalog.ResolveDefinition(CharacterFeatureSlot.Shoes, ShoesFeatureId, out bool knownShoes);
            bool colors = knownTop && knownShoes && topDefinition.ColorChannels.Count > 0 && shoesDefinition.ColorChannels.Count > 0;
            checks.Add(new StartupCheck("af_generated_replacements_expose_color_channel", colors,
                $"top={knownTop} shoes={knownShoes}"));

            CharacterDocument document = CharacterDocument.CreateDefault(Guid.Parse("af200000-0000-4000-8000-000000000001"), "Asset Forge Replacements");
            document = CharacterDocumentEditor.SetFeatureId(document, CharacterFeatureSlot.Tops, TopFeatureId);
            document = CharacterDocumentEditor.SetFeatureId(document, CharacterFeatureSlot.Shoes, ShoesFeatureId);
            CharacterCompileResult compiled = CharacterCompiler.Compile(document, registry.FeatureCatalog);
            checks.Add(new StartupCheck("af_generated_replacements_compile", compiled.IsSuccess && compiled.Appearance is not null,
                $"success={compiled.IsSuccess} errors={compiled.Errors.Count}"));

            var store = new CharacterStore(new CharacterFileSystem(), context.Root, featureCatalog: registry.FeatureCatalog);
            CharacterSaveResult saved = await store.SaveAsync(document, CancellationToken.None);
            CharacterLoadResult loaded = await store.LoadAsync(document.Id, CancellationToken.None);
            CharacterCompileResult loadedCompile = loaded.Document is null
                ? new CharacterCompileResult(null, Array.Empty<CharacterCompileWarning>(), [new CharacterValidationIssue("character", "Reload returned no document.")])
                : CharacterCompiler.Compile(loaded.Document, registry.FeatureCatalog);
            bool survivesRestart = saved.IsSuccess && loaded.IsSuccess && loaded.Document is not null && loadedCompile.IsSuccess && loadedCompile.Appearance is not null &&
                                   loadedCompile.Appearance.Tops.ResolvedFeatureId == TopFeatureId && loadedCompile.Appearance.Shoes.ResolvedFeatureId == ShoesFeatureId;
            checks.Add(new StartupCheck("af_generated_replacements_survive_reload_and_compile", survivesRestart,
                $"save={saved.Status} load={loaded.Status} top={loadedCompile.Appearance?.Tops.ResolvedFeatureId} shoes={loadedCompile.Appearance?.Shoes.ResolvedFeatureId}"));

            BuddyVisualRigTrustSnapshot before = context.Preview.CaptureTrustSnapshot();
            if (compiled.Appearance is not null)
                context.Preview.ApplyAppearance(compiled.Appearance);
            context.Preview.RefreshCharacterCompositors();
            context.Preview.RefreshGeneratedReplacementVisualsForTest();

            Node3D? topRoot = context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Tops);
            Node3D? leftShoeRoot = context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Shoes);
            Node3D? rightShoeRoot = context.Preview.GetPairedCosmeticVisual(CharacterFeatureSlot.Shoes);
            bool replacementState = context.Preview.IsPartVisualReplaced(BuddyPartId.Torso) &&
                                    context.Preview.IsPartVisualReplaced(BuddyPartId.LeftFoot) &&
                                    context.Preview.IsPartVisualReplaced(BuddyPartId.RightFoot) &&
                                    !context.Preview.GetPartMesh(BuddyPartId.Torso).Visible &&
                                    !context.Preview.GetPartMesh(BuddyPartId.LeftFoot).Visible &&
                                    !context.Preview.GetPartMesh(BuddyPartId.RightFoot).Visible &&
                                    GodotObject.IsInstanceValid(topRoot) && GodotObject.IsInstanceValid(leftShoeRoot) && GodotObject.IsInstanceValid(rightShoeRoot);
            checks.Add(new StartupCheck("af_generated_replacements_hide_base_visuals", replacementState,
                $"torso={context.Preview.IsPartVisualReplaced(BuddyPartId.Torso)} feet={context.Preview.IsPartVisualReplaced(BuddyPartId.LeftFoot)}/{context.Preview.IsPartVisualReplaced(BuddyPartId.RightFoot)}"));

            bool outlines = HasGeneratedOutline(topRoot) && HasGeneratedOutline(leftShoeRoot) && HasGeneratedOutline(rightShoeRoot);
            checks.Add(new StartupCheck("af_generated_replacements_use_buddy_outline", outlines,
                $"top={HasGeneratedOutline(topRoot)} left={HasGeneratedOutline(leftShoeRoot)} right={HasGeneratedOutline(rightShoeRoot)}"));

            bool paintShell = context.Preview.GeneratedReplacementPaintShellCountForTest == 3 && context.Preview.GeneratedReplacementPaintScaleIsCorrectForTest;
            checks.Add(new StartupCheck("af_generated_replacements_use_uniform_paint_shell", paintShell,
                $"shells={context.Preview.GeneratedReplacementPaintShellCountForTest} scaleOk={context.Preview.GeneratedReplacementPaintScaleIsCorrectForTest}"));

            bool splitUv = context.Preview.GeneratedReplacementPaintUvSeamIsCorrectForTest;
            checks.Add(new StartupCheck("af_generated_replacements_split_front_back_paint_uvs", splitUv, $"split={splitUv}"));

            int physicsNodes = CountPhysics(topRoot) + CountPhysics(leftShoeRoot) + CountPhysics(rightShoeRoot);
            bool visualOnly = physicsNodes == 0 && context.Preview.TrustedGeometryMatches(before);
            checks.Add(new StartupCheck("af_generated_replacements_visual_only", visualOnly,
                $"physics={physicsNodes} geometryMatches={context.Preview.TrustedGeometryMatches(before)}"));

            CharacterCompileResult defaults = CharacterCompiler.Compile(
                CharacterDocument.CreateDefault(Guid.Parse("af200000-0000-4000-8000-000000000002"), "Asset Forge Defaults"),
                registry.FeatureCatalog);
            if (defaults.Appearance is not null)
                context.Preview.ApplyAppearance(defaults.Appearance);
            context.Preview.RefreshCharacterCompositors();
            bool restored = !context.Preview.IsPartVisualReplaced(BuddyPartId.Torso) &&
                            !context.Preview.IsPartVisualReplaced(BuddyPartId.LeftFoot) &&
                            !context.Preview.IsPartVisualReplaced(BuddyPartId.RightFoot) &&
                            context.Preview.TrustedGeometryMatches(before);
            checks.Add(new StartupCheck("af_generated_replacements_restore_defaults", restored,
                $"torso={context.Preview.IsPartVisualReplaced(BuddyPartId.Torso)} feet={context.Preview.IsPartVisualReplaced(BuddyPartId.LeftFoot)}/{context.Preview.IsPartVisualReplaced(BuddyPartId.RightFoot)}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return CharacterEditorScenarioSupport.Result(checks, seed);
    }

    private static bool HasGeneratedOutline(Node3D? root) =>
        GodotObject.IsInstanceValid(root) && root!.FindChildren("GeneratedOutline", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Any();

    private static int CountPhysics(Node3D? root)
    {
        if (!GodotObject.IsInstanceValid(root)) return 0;
        return Count(root!);
        static int Count(Node node)
        {
            int result = node is CollisionObject2D or CollisionObject3D or Joint2D or Joint3D ? 1 : 0;
            foreach (Node child in node.GetChildren()) result += Count(child);
            return result;
        }
    }
}
