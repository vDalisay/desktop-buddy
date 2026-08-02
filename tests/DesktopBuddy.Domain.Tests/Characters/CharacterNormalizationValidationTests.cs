using System;
using System.Linq;
using DesktopBuddy.Domain.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

public sealed class CharacterNormalizationValidationTests
{
    private static readonly Guid CharacterId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

    [Theory]
    [InlineData("A", true)]
    [InlineData("1234567890123456789012345678901234567890", true)]
    [InlineData("12345678901234567890123456789012345678901", false)]
    [InlineData("", false)]
    [InlineData("bad/name", false)]
    [InlineData("bad\nname", false)]
    [InlineData("bad\u0001name", false)]
    public void NameBoundaries_AreMeasuredAsUnicodeScalars(string name, bool expectedValid)
    {
        CharacterValidationResult result = CharacterDocumentValidator.Validate(
            CharacterDocument.CreateDefault(CharacterId, name));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void FortyEmojiAreValidButFortyOneAreNot()
    {
        string forty = string.Concat(Enumerable.Repeat("🤖", 40));
        string fortyOne = forty + "🤖";

        Assert.True(CharacterDocumentValidator.Validate(
            CharacterDocument.CreateDefault(CharacterId, forty)).IsValid);
        Assert.False(CharacterDocumentValidator.Validate(
            CharacterDocument.CreateDefault(CharacterId, fortyOne)).IsValid);
    }

    [Fact]
    public void Normalize_ClampsOnlyOutOfBoundsValuesAndReturnsANewDocument()
    {
        CharacterDocument source = CharacterDocument.CreateDefault(CharacterId, " Buddy ") with
        {
            Features = CharacterFeatureSet.BuiltIn with
            {
                Eyes = CharacterFeatureSet.BuiltIn.Eyes with
                {
                    OffsetX = -2.0,
                    OffsetY = 0.25,
                    Scale = 2.0,
                },
                Mouth = CharacterFeatureSet.BuiltIn.Mouth with
                {
                    OffsetX = 1.0,
                    OffsetY = -1.0,
                    Scale = 0.75,
                },
            },
        };

        CharacterNormalizationResult result = CharacterDocumentNormalizer.Normalize(source);

        Assert.NotSame(source, result.Document);
        Assert.Equal(" Buddy ", source.DisplayName);
        Assert.Equal("Buddy", result.Document.DisplayName);
        Assert.Equal(-1.0, result.Document.Features.Eyes.OffsetX);
        Assert.Equal(0.25, result.Document.Features.Eyes.OffsetY);
        Assert.Equal(1.25, result.Document.Features.Eyes.Scale);
        Assert.Equal(1.0, result.Document.Features.Mouth.OffsetX);
        Assert.Equal(0.75, result.Document.Features.Mouth.Scale);
        Assert.Equal(
            new[] { "displayName", "features.eyes.offsetX", "features.eyes.scale" },
            result.ChangedFields);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteValues_AreNotSilentlyNormalized(double value)
    {
        CharacterDocument source = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            Features = CharacterFeatureSet.BuiltIn with
            {
                Eyes = CharacterFeatureSet.BuiltIn.Eyes with { OffsetX = value },
            },
        };

        CharacterDocument normalized = CharacterDocumentNormalizer.Normalize(source).Document;
        CharacterValidationResult validation = CharacterDocumentValidator.Validate(normalized);

        Assert.Equal(value, normalized.Features.Eyes.OffsetX);
        Assert.Contains(validation.Errors, error =>
            error.Path == "features.eyes.offsetX" && error.Message.Contains("finite", StringComparison.Ordinal));
    }

    [Fact]
    public void Serialize_RejectsAnUnnormalizedDocument()
    {
        CharacterDocument source = CharacterDocument.CreateDefault(CharacterId, " Buddy ");

        Assert.Throws<ArgumentException>(() => CharacterDocumentPolicy.Serialize(source));
    }
}
