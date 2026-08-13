using System.Linq;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Startup;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class AssetForgeGeneratedReplacementScenario : IScenario
{
    private const string TopFeatureId = "top.ci_pear_torso";
    private const string TopContentId = "cosmetic.top.ci_pear_torso";
    private const string ShoesFeatureId = "shoes.ci_soft_foot";
    private const string ShoesContentId = "cosmetic.shoes.ci_soft_foot";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, int seed, CancellationToken cancellationToken)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context = await CharacterEditorScenarioSupport.Create(tree, cancellationToken);
        try
        {
            BuddyGeneratedCosmeticRegistry registry = BuddyGeneratedCosmeticRegistry.Current;
            bool resourcesLoaded =
                registry.FeatureCatalog.Contains(CharacterFeatureSlot.Tops, TopFeatureId) &&
                registry.FeatureCatalog.Contains(CharacterFeatureSlot.Shoes, ShoesFeatureId) &&
                registry.TryGet(TopFeatureId, out GeneratedBuddyCosmeticResource topResource) &&
                registry.TryGet(ShoesFeatureId, out GeneratedBuddyCosmeticResource shoesResource) &&
                topResource.Slot == CharacterFeatureSlot.Tops &&
                shoesResource.Slot == CharacterFeatureSlot.Shoes;
            checks.Add(new StartupCheck(
                "af_generated_replacement_resources_loaded",
                resourcesLoaded,
                $"top={registry.FeatureCatalog.Contains(CharacterFeatureSlot.Tops, TopFeatureId)} shoes={registry.FeatureCatalog.Contains(CharacterFeatureSlot.Shoes, ShoesFeatureId)}"));

            ContentCatalogue catalogue = CatalogueLoader.Catalogue;
            bool commerceLoaded =
                catalogue.TryGet(TopContentId, out CatalogueEntry topSale) && topSale.PriceMilliCredits == 175_000 &&
                catalogue.TryGet(ShoesContentId, out CatalogueEntry shoesSale) && shoesSale.PriceMilliCredits == 160_000;
            checks.Add(new StartupCheck(
                "af_generated_replacement_commerce_loaded",
                commerceLoaded,
                $"topPrice={topSale.PriceMilliCredits} shoesPrice={shoesSale.PriceMilliCredits}"));

            var visualCatalog = new BuddyCosmeticVisualCatalog(registry.FeatureCatalog, registry);
            BuddyCosmeticVisualDefinition topVisual = visualCatalog.Resolve(CharacterFeatureSlot.Tops, TopFeatureId, out bool topFallback);
            BuddyCosmeticVisualDefinition shoesVisual = visualCatalog.Resolve(CharacterFeatureSlot.Shoes, ShoesFeatureId, out bool shoesFallback);
            bool applicationModes = !topFallback && !shoesFallback &&
                topVisual.ApplicationMode == BuddyCosmeticApplicationMode.PartReplacement &&
                shoesVisual.ApplicationMode == BuddyCosmeticApplicationMode.PairedPartReplacement;
            checks.Add(new StartupCheck(
                "af_generated_replacement_application_modes",
                applicationModes,
                $"top={topVisual.ApplicationMode} shoes={shoesVisual.ApplicationMode}"));

            BuddyVisualRigTrustSnapshot before = context.Preview.CaptureTrustSnapshot();
            CharacterDocument document = CharacterDocument.CreateDefault(
                Guid.Parse("af200000-0000-4000-8000-000000000001"),
                "Asset Forge Replacements");
            document = CharacterDocumentEditor.WriteFeatureId(document, CharacterFeatureSlot.Tops, TopFeatureId);
            document = CharacterDocumentEditor.WriteFeatureId(document, CharacterFeatureSlot.Shoes, ShoesFeatureId);
            CharacterCompileResult compiled = CharacterCompiler.Compile(document, registry.FeatureCatalog);
            checks.Add(new StartupCheck(
                "af_generated_replacements_compile",
                compiled.IsSuccess && compiled.Appearance is not null,
                $"status={compiled.Status} issues={compiled.Issues.Count}"));

            if (compiled.Appearance is not null)
            {
                context.Preview.ApplyAppearance(compiled.Appearance);
                context.Preview.RefreshCharacterCompositors();
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            Node3D? topRoot = context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Tops);
            Node3D? leftShoeRoot = context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Shoes);
            Node3D? rightShoeRoot = context.Preview.GetPairedCosmeticVisual(CharacterFeatureSlot.Shoes);
            bool replacementState =
                context.Preview.IsPartVisualReplaced(BuddyPartId.Torso) &&
                context.Preview.IsPartVisualReplaced(BuddyPartId.LeftFoot) &&
                context.Preview.IsPartVisualReplaced(BuddyPartId.RightFoot) &&
                !context.Preview.GetPartMesh(BuddyPartId.Torso).Visible &&
                !context.Preview.GetPartOutline(BuddyPartId.Torso).Visible &&
                !context.Preview.GetPartMesh(BuddyPartId.LeftFoot).Visible &&
                !context.Preview.GetPartMesh(BuddyPartId.RightFoot).Visible &&
                GodotObject.IsInstanceValid(topRoot) &&
                GodotObject.IsInstanceValid(leftShoeRoot) &&
                GodotObject.IsInstanceValid(rightShoeRoot);
            checks.Add(new StartupCheck(
                "af_generated_replacements_hide_base_visuals",
                replacementState,
                $"torso={context.Preview.IsPartVisualReplaced(BuddyPartId.Torso)} leftFoot={context.Preview.IsPartVisualReplaced(BuddyPartId.LeftFoot)} rightFoot={context.Preview.IsPartVisualReplaced(BuddyPartId.RightFoot)}"));

            int physicsNodes = (topRoot is null ? 0 : CountPhysics(topRoot)) +
                               (leftShoeRoot is null ? 0 : CountPhysics(leftShoeRoot)) +
                               (rightShoeRoot is null ? 0 : CountPhysics(rightShoeRoot));
            checks.Add(new StartupCheck(
                "af_generated_replacements_visual_only",
                physicsNodes == 0 && context.Preview.TrustedGeometryMatches(before),
                $"physics={physicsNodes} geometryMatches={context.Preview.TrustedGeometryMatches(before)}"));

            CharacterDocument defaults = CharacterDocument.CreateDefault(
                Guid.Parse("af200000-0000-4000-8000-000000000002"),
                "Asset Forge Defaults");
            CharacterCompileResult defaultCompile = CharacterCompiler.Compile(defaults, registry.FeatureCatalog);
            if (defaultCompile.Appearance is not null)
            {
                context.Preview.ApplyAppearance(defaultCompile.Appearance);
                context.Preview.RefreshCharacterCompositors();
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            bool restored =
                !context.Preview.IsPartVisualReplaced(BuddyPartId.Torso) &&
                !context.Preview.IsPartVisualReplaced(BuddyPartId.LeftFoot) &&
                !context.Preview.IsPartVisualReplaced(BuddyPartId.RightFoot) &&
                context.Preview.GetPartMesh(BuddyPartId.Torso).Visible &&
                context.Preview.GetPartMesh(BuddyPartId.LeftFoot).Visible &&
                context.Preview.GetPartMesh(BuddyPartId.RightFoot).Visible &&
                context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Tops) is null &&
                context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Shoes) is null &&
                context.Preview.TrustedGeometryMatches(before);
            checks.Add(new StartupCheck(
                "af_generated_replacements_restore_defaults",
                restored,
                $"torsoVisible={context.Preview.GetPartMesh(BuddyPartId.Torso).Visible} feetVisible={context.Preview.GetPartMesh(BuddyPartId.LeftFoot).Visible}/{context.Preview.GetPartMesh(BuddyPartId.RightFoot).Visible}"));
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
        foreach (Node child in node.GetChildren()) count += CountPhysics(child);
        return count;
    }
}
