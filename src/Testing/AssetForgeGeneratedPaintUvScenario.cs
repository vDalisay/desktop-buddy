using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Regression gate for the paint-only topology used by generated Torso/Shoes replacements. The
/// authored GLB intentionally shares its physical Z=0 rim between front and back; paint must split
/// those coincident rim vertices into separate UV islands or front paint leaks onto the backside.
/// </summary>
public sealed class AssetForgeGeneratedPaintUvScenario : IScenario
{
    private const string TopFeatureId = "top.ci_pear_torso";
    private const string ShoesFeatureId = "shoes.ci_soft_foot";

    public string Id => "asset_forge_generated_paint_uvs";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context = await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            BuddyGeneratedCosmeticRegistry registry = BuddyGeneratedCosmeticRegistry.Current;
            CharacterDocument document = CharacterDocument.CreateDefault(
                Guid.Parse("af400000-0000-4000-8000-000000000001"),
                "Asset Forge Paint UVs");
            document = CharacterDocumentEditor.SetFeatureId(document, CharacterFeatureSlot.Tops, TopFeatureId);
            document = CharacterDocumentEditor.SetFeatureId(document, CharacterFeatureSlot.Shoes, ShoesFeatureId);
            CharacterCompileResult compiled = CharacterCompiler.Compile(document, registry.FeatureCatalog);

            bool compiledGenerated = compiled.IsSuccess && compiled.Appearance is not null &&
                                     compiled.Appearance.Tops.ResolvedFeatureId == TopFeatureId &&
                                     compiled.Appearance.Shoes.ResolvedFeatureId == ShoesFeatureId;
            checks.Add(new StartupCheck(
                "af_generated_paint_uvs_compile_generated_replacements",
                compiledGenerated,
                $"success={compiled.IsSuccess} top={compiled.Appearance?.Tops.ResolvedFeatureId} shoes={compiled.Appearance?.Shoes.ResolvedFeatureId}"));

            if (compiled.Appearance is not null)
            {
                context.Preview.ApplyAppearance(compiled.Appearance);
                context.Preview.RefreshCharacterCompositors();
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            // RefreshCharacterCompositors creates the paint-only shells. Their validation checks
            // that front triangles stay in U 0..0.5, back triangles in U 0.5..1, and that the
            // shared geometric rim actually exists twice with the expected half-atlas separation.
            bool split = context.Preview.GeneratedReplacementPaintUvSeamIsCorrectForTest;
            checks.Add(new StartupCheck(
                "af_generated_paint_uvs_split_shared_rim",
                split,
                $"frontBackSplit={split}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return CharacterEditorScenarioSupport.Result(checks, seed);
    }
}
