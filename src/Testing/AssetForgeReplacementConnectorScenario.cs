using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class AssetForgeReplacementConnectorScenario : IScenario
{
    private const string TopFeatureId = "top.ci_pear_torso";
    private const string ShoesFeatureId = "shoes.ci_soft_foot";

    public string Id => "asset_forge_replacement_connector_fit";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context = await CharacterEditorScenarioSupport.Create(tree, Id);
        ImageTexture? paintTexture = null;
        try
        {
            BuddyGeneratedCosmeticRegistry registry = BuddyGeneratedCosmeticRegistry.Current;
            CharacterDocument document = CharacterDocument.CreateDefault(
                Guid.Parse("af300000-0000-4000-8000-000000000001"),
                "Asset Forge Connector Fit");
            document = CharacterDocumentEditor.SetFeatureId(document, CharacterFeatureSlot.Tops, TopFeatureId);
            document = CharacterDocumentEditor.SetFeatureId(document, CharacterFeatureSlot.Shoes, ShoesFeatureId);
            CharacterCompileResult compile = CharacterCompiler.Compile(document, registry.FeatureCatalog);
            if (compile.Appearance is not null)
                context.Preview.ApplyAppearance(compile.Appearance);

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            bool fitted = context.Preview.ReplacementConnectorTrackingReadyForTest &&
                context.Preview.ReplacementConnectorCorrectionCountForTest > 0;
            checks.Add(new StartupCheck(
                "af_replacement_connectors_fit_generated_bounds",
                fitted,
                $"tracking={context.Preview.ReplacementConnectorTrackingReadyForTest} corrected={context.Preview.ReplacementConnectorCorrectionCountForTest}/{context.Preview.ConnectorVisualCount}"));

            checks.Add(new StartupCheck(
                "af_replacement_outline_keeps_buddy_world_thickness",
                context.Preview.GeneratedReplacementOutlineScaleIsCorrectForTest,
                $"outlineScaleCorrect={context.Preview.GeneratedReplacementOutlineScaleIsCorrectForTest}"));

            BuddyVisualRigTrustSnapshot studioTrust = context.Preview.CaptureTrustSnapshot();
            context.Preview.SetStudioPreviewMode(true);
            bool studioConnectorsHidden = context.Preview.StudioPreviewConnectorsHiddenForTest &&
                context.Preview.TrustedGeometryMatches(studioTrust);
            context.Preview.SetStudioPreviewMode(false);
            bool studioConnectorsRestored = Enumerable.Range(0, context.Preview.ConnectorVisualCount)
                .All(index => context.Preview.GetConnectorVisual(index).Visible) &&
                context.Preview.TrustedGeometryMatches(studioTrust);
            checks.Add(new StartupCheck(
                "af_buddy_studio_preview_hides_connectors_only",
                studioConnectorsHidden && studioConnectorsRestored,
                $"hidden={studioConnectorsHidden} restored={studioConnectorsRestored}"));

            byte[] rgba =
            [
                255, 32, 64, 255, 255, 32, 64, 255,
                255, 32, 64, 255, 255, 32, 64, 255,
            ];
            paintTexture = ImageTexture.CreateFromImage(Image.CreateFromData(2, 2, false, Image.Format.Rgba8, rgba));
            context.Preview.SetSurfaceUnderlay(BuddyPartId.Torso, paintTexture);
            context.Preview.SetSurfaceUnderlay(BuddyPartId.LeftFoot, paintTexture);
            context.Preview.SetSurfaceUnderlay(BuddyPartId.RightFoot, paintTexture);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            int paintShells = CountVisiblePaintShells(context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Tops)) +
                              CountVisiblePaintShells(context.Preview.GetCosmeticVisual(CharacterFeatureSlot.Shoes)) +
                              CountVisiblePaintShells(context.Preview.GetPairedCosmeticVisual(CharacterFeatureSlot.Shoes));
            checks.Add(new StartupCheck(
                "af_replacement_paint_binds_to_visible_generated_meshes",
                paintShells == 3 && context.Preview.GeneratedReplacementPaintShellCountForTest == 3,
                $"visiblePaintShells={paintShells} tracked={context.Preview.GeneratedReplacementPaintShellCountForTest}"));

            Vector2 torsoPoint = context.Preview.GeometrySource.ReadTransform(BuddyPartId.Torso).Position;
            bool mappedTorso = context.Preview.TryMapGeneratedReplacementPaintHit(torsoPoint, out PaintHit torsoHit) &&
                               torsoHit.Part == PaintPart.Torso && torsoHit.IsValid;
            checks.Add(new StartupCheck(
                "af_replacement_paint_hit_uses_generated_mesh_uv",
                mappedTorso,
                mappedTorso ? $"part={torsoHit.Part} uv=({torsoHit.Uv.X:F3},{torsoHit.Uv.Y:F3})" : "generated torso centre did not map"));

            BuddyVisualRigTrustSnapshot trust = context.Preview.CaptureTrustSnapshot();
            context.Preview.SetSurfaceUnderlay(BuddyPartId.Torso, null);
            context.Preview.SetSurfaceUnderlay(BuddyPartId.LeftFoot, null);
            context.Preview.SetSurfaceUnderlay(BuddyPartId.RightFoot, null);
            CharacterDocument defaults = CharacterDocument.CreateDefault(
                Guid.Parse("af300000-0000-4000-8000-000000000002"),
                "Asset Forge Connector Defaults");
            CharacterCompileResult defaultCompile = CharacterCompiler.Compile(defaults, registry.FeatureCatalog);
            if (defaultCompile.Appearance is not null)
                context.Preview.ApplyAppearance(defaultCompile.Appearance);

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool cleared = context.Preview.ReplacementConnectorCorrectionCountForTest == 0 &&
                context.Preview.TrustedGeometryMatches(trust);
            checks.Add(new StartupCheck(
                "af_replacement_connector_fit_clears_without_geometry_mutation",
                cleared,
                $"corrected={context.Preview.ReplacementConnectorCorrectionCountForTest} geometryMatches={context.Preview.TrustedGeometryMatches(trust)}"));
        }
        finally
        {
            paintTexture?.Dispose();
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return CharacterEditorScenarioSupport.Result(checks, seed);
    }

    private static int CountVisiblePaintShells(Node3D? root)
    {
        if (!GodotObject.IsInstanceValid(root)) return 0;
        return root!.FindChildren("GeneratedPaint", nameof(MeshInstance3D), true, false)
            .OfType<MeshInstance3D>()
            .Count(static mesh => mesh.Visible && mesh.MaterialOverride is StandardMaterial3D { AlbedoTexture: not null });
    }
}

internal static class AssetForgeReplacementConnectorScenarioRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        FieldInfo field = typeof(ScenarioCatalog).GetField(
            "Factories",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Scenario registry field was not found.");
        var factories = (Dictionary<string, Func<IScenario>>?)field.GetValue(null)
            ?? throw new InvalidOperationException("Scenario registry was not initialized.");
        factories["asset_forge_replacement_connector_fit"] = () => new AssetForgeReplacementConnectorScenario();
    }
}
