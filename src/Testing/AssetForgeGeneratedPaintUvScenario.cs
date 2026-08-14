using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Physics;
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
    public string Id => "asset_forge_generated_paint_uvs";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context = await CharacterEditorScenarioSupport.Create(tree, Id);
        ImageTexture? paintTexture = null;
        try
        {
            BuddyGeneratedCosmeticRegistry registry = BuddyGeneratedCosmeticRegistry.Current;
            GeneratedBuddyCosmeticResource? top = registry.Entries.FirstOrDefault(
                static entry => entry.Slot == CharacterFeatureSlot.Tops);
            CharacterDocument document = CharacterDocument.CreateDefault(
                Guid.Parse("af400000-0000-4000-8000-000000000001"),
                "Asset Forge Paint UVs");
            if (top is not null)
                document = CharacterDocumentEditor.SetFeatureId(document, CharacterFeatureSlot.Tops, top.FeatureId);
            CharacterCompileResult compiled = CharacterCompiler.Compile(document, registry.FeatureCatalog);

            bool compiledGenerated = top is not null && compiled.IsSuccess && compiled.Appearance is not null &&
                                     compiled.Appearance.Tops.ResolvedFeatureId == top.FeatureId;
            checks.Add(new StartupCheck(
                "af_generated_paint_uvs_compile_generated_replacements",
                compiledGenerated,
                $"success={compiled.IsSuccess} top={compiled.Appearance?.Tops.ResolvedFeatureId}"));

            if (compiled.Appearance is not null)
                context.Preview.ApplyAppearance(compiled.Appearance);
            context.Preview.RefreshCharacterCompositors();
            context.Preview.RefreshGeneratedReplacementVisualsForTest();

            paintTexture = ImageTexture.CreateFromImage(Image.CreateFromData(
                2, 2, false, Image.Format.Rgba8,
                [255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255]));
            context.Preview.SetSurfaceUnderlay(BuddyPartId.Torso, paintTexture);

            bool bindsToReplacement = !context.Preview.GetPartMesh(BuddyPartId.Torso).Visible &&
                                      context.Preview.GeneratedReplacementPaintSurfaceCountForTest == 1 &&
                                      context.Preview.GeneratedReplacementPaintUsesSurfaceDetailForTest;
            checks.Add(new StartupCheck(
                "af_generated_paint_binds_without_restoring_legacy_torso",
                bindsToReplacement,
                $"legacyVisible={context.Preview.GetPartMesh(BuddyPartId.Torso).Visible} surfaces={context.Preview.GeneratedReplacementPaintSurfaceCountForTest}"));

            bool split = context.Preview.GeneratedReplacementPaintUvSeamIsCorrectForTest;
            checks.Add(new StartupCheck(
                "af_generated_paint_uvs_split_shared_rim",
                split,
                $"frontBackSplit={split}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
            paintTexture?.Dispose();
        }

        return CharacterEditorScenarioSupport.Result(checks, seed);
    }
}
