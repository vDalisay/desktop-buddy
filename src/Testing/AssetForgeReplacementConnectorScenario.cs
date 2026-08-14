using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
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

            BuddyVisualRigTrustSnapshot trust = context.Preview.CaptureTrustSnapshot();
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
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return CharacterEditorScenarioSupport.Result(checks, seed);
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
