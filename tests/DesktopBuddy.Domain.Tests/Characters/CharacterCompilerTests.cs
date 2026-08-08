using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

public sealed class CharacterCompilerTests
{
    private static readonly Guid CharacterId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab");

    [Fact]
    public void DefaultDocument_CompilesToEveryCatalogDefault()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy");

        CharacterCompileResult result = CharacterCompiler.Compile(
            document,
            CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Warnings);
        CompiledCharacterAppearance appearance = Assert.IsType<CompiledCharacterAppearance>(
            result.Appearance);
        Assert.Equal(CharacterFeatureIds.FaceClassicPlate, appearance.Face.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.HairNone, appearance.Hair.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.EyesSoftOval, appearance.Eyes.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.BrowsSoftArc, appearance.Brows.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.NoseNone, appearance.Nose.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.MouthRounded, appearance.Mouth.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.EarsNone, appearance.Ears.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.AccentNone, appearance.TorsoAccent.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.GlassesNone, appearance.Glasses.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.HeadwearNone, appearance.Headwear.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.TopNone, appearance.Tops.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.ShoesNone, appearance.Shoes.ResolvedFeatureId);
        Assert.Equal(CharacterPartColors.BuiltIn.Head, appearance.PartColors.Head);
    }

    [Fact]
    public void UnknownFeature_CompilesToSlotDefaultWithOneWarningAndDoesNotMutateDocument()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with
            {
                Eyes = CharacterFeatureSet.BuiltIn.Eyes with
                {
                    FeatureId = "eyes.future",
                    OffsetX = 0.5,
                },
            },
        };

        CharacterCompileResult result = CharacterCompiler.Compile(
            document,
            CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        CharacterCompileWarning warning = Assert.Single(result.Warnings);
        Assert.Equal("eyes.future", warning.OriginalFeatureId);
        Assert.Equal(CharacterFeatureIds.EyesSoftOval, warning.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.EyesSoftOval, result.Appearance!.Eyes.ResolvedFeatureId);
        Assert.Equal(0.5, result.Appearance.Eyes.Transform.OffsetX);
        Assert.Equal("eyes.future", document.Features.Eyes.FeatureId);
    }

    [Fact]
    public void Compile_RejectsAnUnnormalizedDocument()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, " Buddy ");

        CharacterCompileResult result = CharacterCompiler.Compile(
            document,
            CharacterFeatureCatalog.Shipped);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Message.Contains("normalized", StringComparison.Ordinal));
    }

    [Fact]
    public void ShippedFeatureIds_AreGloballyUniqueAndBelongToExactlyOneSlot()
    {
        CharacterFeatureCatalog catalog = CharacterFeatureCatalog.Shipped;
        string[] all = catalog.AllIds.ToArray();

        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        foreach (string id in all)
        {
            Assert.True(catalog.TryGetSlot(id, out CharacterFeatureSlot slot));
            Assert.Contains(id, catalog.GetIds(slot));
            // Distinct(): TorsoAccent is a source-compat alias of Accessories, so GetValues
            // yields that slot value twice.
            Assert.Single(
                Enum.GetValues<CharacterFeatureSlot>().Distinct(),
                candidate => catalog.Contains(candidate, id));
        }
    }

    [Fact]
    public void CatalogDefaults_ExistAndBelongToTheirDeclaredSlot()
    {
        CharacterFeatureCatalog catalog = CharacterFeatureCatalog.Shipped;

        foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>())
        {
            string defaultId = catalog.GetDefaultId(slot);
            Assert.Contains(defaultId, catalog.GetIds(slot));
            Assert.True(catalog.Contains(slot, defaultId));
        }
    }

    [Fact]
    public void CatalogConstructor_RejectsDuplicateIdsAcrossSlots()
    {
        Assert.Throws<ArgumentException>(() => new CharacterFeatureCatalog(
            ["shared"], "shared",
            ["shared"], "shared",
            ["mouth"], "mouth",
            ["accent"], "accent"));
    }

    [Fact]
    public void ShippedDefinitions_ExposeStableStudioMetadataAndOnlyWorkGlassesArePaid()
    {
        CharacterFeatureCatalog catalog = CharacterFeatureCatalog.Shipped;

        Assert.Equal(12, Enum.GetValues<CharacterFeatureSlot>().Distinct().Count());
        foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>().Distinct())
        {
            IReadOnlyList<CosmeticDefinition> definitions = catalog.GetDefinitions(slot);
            Assert.NotEmpty(definitions);
            Assert.Equal(catalog.GetDefaultId(slot), definitions.Single(definition => definition.Id == definition.FallbackId).Id);
            Assert.All(definitions, definition => Assert.StartsWith("buddy_studio.cosmetic.", definition.DisplayNameKey));
        }

        Assert.True(catalog.TryGetDefinition(CharacterFeatureIds.GlassesWorkClassic, out CosmeticDefinition workGlasses));
        Assert.False(workGlasses.IsFreeDefault);
        Assert.All(
            catalog.AllIds.Where(id => id != CharacterFeatureIds.GlassesWorkClassic),
            id => Assert.True(catalog.TryGetDefinition(id, out CosmeticDefinition definition) && definition.IsFreeDefault));
    }

    [Fact]
    public void Compiler_ResolvesNamedChannelsAndIgnoresUnknownChannels()
    {
        CharacterFeatureDocument eyes = CharacterFeatureSet.BuiltIn.Eyes with
        {
            Color = Rgba32.Parse("#010203"),
            Colors = new Dictionary<string, Rgba32>(StringComparer.Ordinal)
            {
                [CosmeticDefinition.PrimaryColorChannel] = Rgba32.Parse("#AABBCC"),
                ["future"] = Rgba32.Parse("#FFFFFF"),
            },
        };
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with { Eyes = eyes },
        };

        CharacterCompileResult result = CharacterCompiler.Compile(document, CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        Assert.Equal(Rgba32.Parse("#AABBCC"), result.Appearance!.Eyes.Color);
        Assert.Equal(1, result.Appearance.Eyes.ColorChannels.Count);
        Assert.False(result.Appearance.Eyes.ColorChannels.TryGetValue("future", out _));
        Assert.Equal(Rgba32.Parse("#FFFFFF"), document.Features.Eyes.Colors["future"]);
    }

    [Fact]
    public void Compiler_RejectsKnownCosmeticInWrongCategory()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with
            {
                Hair = CharacterFeatureSet.BuiltIn.Hair with { FeatureId = CharacterFeatureIds.TopNone },
            },
        };

        CharacterCompileResult result = CharacterCompiler.Compile(document, CharacterFeatureCatalog.Shipped);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Path == "features.hair.featureId");
    }

    [Fact]
    public void Compiler_RejectsTransformOnNonTransformableDefinition()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with
            {
                Headwear = CharacterFeatureSet.BuiltIn.Headwear with { Scale = 1.1 },
            },
        };

        CharacterCompileResult result = CharacterCompiler.Compile(document, CharacterFeatureCatalog.Shipped);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Path == "features.headwear.transform");
    }
}
