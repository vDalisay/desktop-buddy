using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

public sealed class CharacterCompilerTests
{
    private static readonly Guid CharacterId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void Compile_DefaultDocument_ProducesBuiltInAppearance()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy");

        CharacterCompileResult result = CharacterCompiler.Compile(document, CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Appearance);
        Assert.Equal(CharacterId, result.Appearance!.CharacterId);
        Assert.Equal(CharacterFeatureIds.FaceClassicPlate, result.Appearance.Face.Id);
        Assert.Equal(CharacterFeatureIds.HairNone, result.Appearance.Hair.Id);
        Assert.Equal(CharacterFeatureIds.EyesSoftOval, result.Appearance.Eyes.Id);
        Assert.Equal(CharacterFeatureIds.BrowsSoftArc, result.Appearance.Brows.Id);
        Assert.Equal(CharacterFeatureIds.NoseNone, result.Appearance.Nose.Id);
        Assert.Equal(CharacterFeatureIds.MouthRounded, result.Appearance.Mouth.Id);
        Assert.Equal(CharacterFeatureIds.EarsNone, result.Appearance.Ears.Id);
        Assert.Equal(CharacterFeatureIds.AccentNone, result.Appearance.Accessories.Id);
        Assert.Equal(CharacterFeatureIds.GlassesNone, result.Appearance.Glasses.Id);
        Assert.Equal(CharacterFeatureIds.HeadwearNone, result.Appearance.Headwear.Id);
        Assert.Equal(CharacterFeatureIds.TopNone, result.Appearance.Tops.Id);
        Assert.Equal(CharacterFeatureIds.ShoesNone, result.Appearance.Shoes.Id);
    }

    [Fact]
    public void Compile_UnknownFeatureFallsBackAndReportsRepair()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with
            {
                Eyes = CharacterFeatureSet.BuiltIn.Eyes with { Id = "eyes.future" },
            },
        };

        CharacterCompileResult result = CharacterCompiler.Compile(document, CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        Assert.Equal(CharacterFeatureIds.EyesSoftOval, result.Appearance!.Eyes.Id);
        Assert.Contains(result.Repairs, repair => repair.Contains("eyes.future", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_AppliesStoredTransformWithinAuthoredBounds()
    {
        CharacterFeatureDocument transformedEyes = CharacterFeatureSet.BuiltIn.Eyes with
        {
            Transform = new NormalizedFeatureTransform(0.15f, -0.12f, 1.25f),
        };
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with { Eyes = transformedEyes },
        };

        CharacterCompileResult result = CharacterCompiler.Compile(document, CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        Assert.Equal(transformedEyes.Transform, result.Appearance!.Eyes.Transform);
    }

    [Fact]
    public void Compile_ClampsOutOfBoundsTransformAndReportsRepair()
    {
        CharacterFeatureDocument transformedEyes = CharacterFeatureSet.BuiltIn.Eyes with
        {
            Transform = new NormalizedFeatureTransform(8.0f, -8.0f, 9.0f),
        };
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with { Eyes = transformedEyes },
        };

        CharacterCompileResult result = CharacterCompiler.Compile(document, CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(transformedEyes.Transform, result.Appearance!.Eyes.Transform);
        Assert.Contains(result.Repairs, repair => repair.Contains("transform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Catalogue_RejectsDuplicateDefinitionsAndInvalidFallbacks()
    {
        CosmeticDefinition sample = CharacterFeatureCatalog.Shipped.GetDefinitions(CharacterFeatureSlot.Eyes)[0];
        Assert.Throws<ArgumentException>(() => new CharacterFeatureCatalog([sample, sample]));

        CosmeticDefinition badFallback = sample with { FallbackId = "eyes.missing" };
        IEnumerable<CosmeticDefinition> all = CharacterFeatureCatalog.Shipped.AllIds
            .Select(id =>
            {
                Assert.True(CharacterFeatureCatalog.Shipped.TryGetDefinition(id, out CosmeticDefinition definition));
                return definition.Id == sample.Id ? badFallback : definition;
            });
        Assert.Throws<ArgumentException>(() => new CharacterFeatureCatalog(all));
    }

    [Fact]
    public void LegacyConstructor_StillSupportsPhaseAFeatureSets()
    {
        var catalog = new CharacterFeatureCatalog(
            ["eye"], "eye",
            ["brow"], "brow",
            ["mouth"], "mouth",
            ["accent"], "accent");

        Assert.True(catalog.Contains(CharacterFeatureSlot.Eyes, "eye"));
        Assert.True(catalog.Contains(CharacterFeatureSlot.Brows, "brow"));
        Assert.True(catalog.Contains(CharacterFeatureSlot.Mouth, "mouth"));
        Assert.True(catalog.Contains(CharacterFeatureSlot.Accessories, "accent"));
    }

    [Fact]
    public void LegacyConstructor_RejectsSharedFeatureIdAcrossSlots()
    {
        Assert.Throws<ArgumentException>(() => new CharacterFeatureCatalog(
            ["shared"], "shared",
            ["shared"], "shared",
            ["mouth"], "mouth",
            ["accent"], "accent"));
    }

    [Fact]
    public void ShippedDefinitions_ExposeStableStudioMetadataAndAuthoredOwnershipIds()
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

        string[] paidIds =
        [
            CharacterFeatureIds.HairShortSweep,
            CharacterFeatureIds.BrowsStraight, CharacterFeatureIds.BrowsSegmented,
            CharacterFeatureIds.EyesRoundDot, CharacterFeatureIds.EyesHorizontalLed,
            CharacterFeatureIds.NoseButton,
            CharacterFeatureIds.MouthPixel, CharacterFeatureIds.MouthLine,
            CharacterFeatureIds.EarsRoundTabs,
            CharacterFeatureIds.AccentPanel, CharacterFeatureIds.AccentChevron, CharacterFeatureIds.AccentBolts,
            CharacterFeatureIds.GlassesWorkClassic,
            CharacterFeatureIds.HeadwearSoftCap, CharacterFeatureIds.TopUtilityBib,
            CharacterFeatureIds.ShoesSoftSteps,
        ];
        Assert.All(paidIds, id =>
        {
            Assert.True(catalog.TryGetDefinition(id, out CosmeticDefinition definition));
            Assert.False(definition.IsFreeDefault);
            Assert.NotNull(definition.OwnershipContentId);
        });
        Assert.All(
            catalog.AllIds.Except(paidIds),
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
}
