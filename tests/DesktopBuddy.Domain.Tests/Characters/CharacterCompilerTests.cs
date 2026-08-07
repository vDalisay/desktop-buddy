using System;
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
        Assert.Equal(CharacterFeatureIds.EyesSoftOval, appearance.Eyes.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.BrowsSoftArc, appearance.Brows.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.MouthRounded, appearance.Mouth.ResolvedFeatureId);
        Assert.Equal(CharacterFeatureIds.AccentNone, appearance.TorsoAccent.ResolvedFeatureId);
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
}
