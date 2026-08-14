using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
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

            bool topCommerce = CatalogueLoader.Catalogue.TryGet(TopContentId, out CatalogueEntry topSale) &&
                topSale.PriceMilliCredits == 175_000;
            bool shoesCommerce = CatalogueLoader.Catalogue.TryGet(ShoesContentId, out CatalogueEntry shoesSale) &&
                shoesSale.PriceMilliCredits == 160_000;
            checks.Add(new StartupCheck(
                "af_generated_replacement_commerce_loaded",
                topCommerce && shoesCommerce,
                $"top={topCommerce} shoes={shoesCommerce}"));

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
                $"success={compiled.IsSuccess} errors={compiled.Errors.Count} warnings={compiled.Warnings.Count}"));

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

            Node3D? leftGenerated = leftShoeRoot?.GetNodeOrNull<Node3D>("GeneratedMesh");
            Node3D? rightGenerated = rightShoeRoot?.GetNodeOrNull<Node3D>("GeneratedMesh");
            bool pairedMirror =
                GodotObject.IsInstanceValid(leftGenerated) && GodotObject.IsInstanceValid(rightGenerated) &&
                Mathf.Abs(Mathf.Wrap(leftGenerated!.RotationDegrees.Y, 0f, 360f) - 180f) < 0.1f &&
                Mathf.Abs(Mathf.Wrap(rightGenerated!.RotationDegrees.Y, 0f, 360f)) < 0.1f &&
                leftGenerated.Scale.X > 0f && rightGenerated.Scale.X > 0f;
            checks.Add(new StartupCheck(
                "af_generated_shoes_are_outward_mirrored_pair",
                pairedMirror,
                $"leftY={leftGenerated?.RotationDegrees.Y:0.0} rightY={rightGenerated?.RotationDegrees.Y:0.0} leftScale={leftGenerated?.Scale.X:0.00} rightScale={rightGenerated?.Scale.X:0.00}"));

            bool buddyOutline = HasGeneratedOutline(topRoot) &&
                HasGeneratedOutline(leftShoeRoot) &&
                HasGeneratedOutline(rightShoeRoot);
            checks.Add(new StartupCheck(
                "af_generated_replacements_use_buddy_outline",
                buddyOutline,
                $"top={HasGeneratedOutline(topRoot)} left={HasGeneratedOutline(leftShoeRoot)} right={HasGeneratedOutline(rightShoeRoot)}"));

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
                context.Preview.GetPairedCosmeticVisual(CharacterFeatureSlot.Shoes) is null &&
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

    private static bool HasGeneratedOutline(Node3D? root) =>
        GodotObject.IsInstanceValid(root) &&
        root!.FindChildren("GeneratedOutline", nameof(MeshInstance3D), true, false)
            .OfType<MeshInstance3D>()
            .Any();

    private static int CountPhysics(Node node)
    {
        int count = node is CollisionObject2D or CollisionObject3D or Joint2D or Joint3D ? 1 : 0;
        foreach (Node child in node.GetChildren()) count += CountPhysics(child);
        return count;
    }
}
