using System;
using DesktopBuddy.Domain.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

/// <summary>
/// The buddy's favourite colour is decided when the character is created and never moves after
/// that. Room personality reads it, so a colour that tracked the live torso would silently
/// retarget a buddy every time the player repainted it (owner feedback 2026-08-19).
/// </summary>
public sealed class CharacterFavoriteColorTests
{
    private static readonly Guid CharacterId = Guid.Parse("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff");

    [Fact]
    public void LegacyDocumentWithoutOne_FreezesTheTorsoColourItWasCreatedWith()
    {
        var legacy = new CharacterDocument
        {
            Id = CharacterId,
            DisplayName = "Buddy",
            PartColors = new CharacterPartColors { Torso = Rgba32.Parse("#FF69B4") },
        };
        Assert.Null(legacy.FavoriteColor);

        CharacterDocument normalized = CharacterDocumentNormalizer.Normalize(legacy).Document;

        Assert.Equal(Rgba32.Parse("#FF69B4"), normalized.FavoriteColor);
    }

    [Fact]
    public void RepaintingTheTorso_LeavesTheFavouriteColourWhereItWas()
    {
        CharacterDocument created = CharacterDocumentNormalizer.Normalize(new CharacterDocument
        {
            Id = CharacterId,
            DisplayName = "Buddy",
            PartColors = new CharacterPartColors { Torso = Rgba32.Parse("#FF69B4") },
        }).Document;

        CharacterDocument repainted = CharacterDocumentNormalizer.Normalize(created with
        {
            PartColors = created.PartColors with { Torso = Rgba32.Parse("#204020") },
        }).Document;

        Assert.Equal(Rgba32.Parse("#FF69B4"), repainted.FavoriteColor);
    }

    [Fact]
    public void CompiledAppearance_CarriesTheFrozenColourNotTheCurrentTorso()
    {
        CharacterDocument document = CharacterDocumentNormalizer.Normalize(new CharacterDocument
        {
            Id = CharacterId,
            DisplayName = "Buddy",
            PartColors = new CharacterPartColors { Torso = Rgba32.Parse("#FF69B4") },
        }).Document;
        document = CharacterDocumentNormalizer.Normalize(document with
        {
            PartColors = document.PartColors with { Torso = Rgba32.Parse("#204020") },
        }).Document;

        CharacterCompileResult result = CharacterCompiler.Compile(
            document,
            CharacterFeatureCatalog.Shipped);

        Assert.True(result.IsSuccess);
        Assert.Equal(Rgba32.Parse("#FF69B4"), result.Appearance!.FavoriteColor);
        Assert.Equal(Rgba32.Parse("#204020"), result.Appearance.PartColors.Torso);
    }

    [Fact]
    public void RoundTrip_KeepsTheFavouriteColourAcrossSerialization()
    {
        CharacterDocument document = CharacterDocumentNormalizer.Normalize(new CharacterDocument
        {
            Id = CharacterId,
            DisplayName = "Buddy",
            PartColors = new CharacterPartColors { Torso = Rgba32.Parse("#FF69B4") },
        }).Document;

        string json = CharacterDocumentPolicy.Serialize(document);
        CharacterDecodeResult reloaded = CharacterDocumentPolicy.DecodeAndMigrate(json);

        Assert.True(reloaded.IsSuccess);
        Assert.Equal(Rgba32.Parse("#FF69B4"), reloaded.Document!.FavoriteColor);
    }
}
