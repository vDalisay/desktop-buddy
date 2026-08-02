using System;
using System.Linq;
using System.Text.Json;
using DesktopBuddy.Domain.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

public sealed class CharacterDocumentPolicyTests
{
    private static readonly Guid CharacterId = Guid.Parse("12345678-1234-4567-89ab-1234567890ab");

    [Fact]
    public void CurrentDocument_RoundTripsEveryFieldAndTopLevelExtension()
    {
        var source = new CharacterDocument
        {
            Id = CharacterId,
            DisplayName = "Nova 🤖",
            PartColors = new CharacterPartColors
            {
                Head = Rgba32.Parse("#010203"),
                Torso = Rgba32.Parse("#111213"),
                LeftHand = Rgba32.Parse("#212223"),
                RightHand = Rgba32.Parse("#313233"),
                LeftFoot = Rgba32.Parse("#414243"),
                RightFoot = Rgba32.Parse("#515253"),
            },
            Features = new CharacterFeatureSet
            {
                Eyes = Feature(CharacterFeatureIds.EyesRoundDot, -0.25, 0.5, 0.8, "#616263"),
                Brows = Feature(CharacterFeatureIds.BrowsStraight, 0.1, -0.2, 1.1, "#717273"),
                Mouth = Feature(CharacterFeatureIds.MouthLine, 0.3, 0.4, 1.2, "#818283"),
                TorsoAccent = Feature(CharacterFeatureIds.AccentChevron, -0.5, -0.6, 0.9, "#919293"),
            },
            ExtensionData =
            {
                ["futureFlag"] = JsonDocument.Parse("true").RootElement.Clone(),
            },
        };

        CharacterDecodeResult decoded = CharacterDocumentPolicy.DecodeAndMigrate(
            CharacterDocumentPolicy.Serialize(source));

        Assert.Equal(CharacterDecodeStatus.Valid, decoded.Status);
        CharacterDocument document = Assert.IsType<CharacterDocument>(decoded.Document);
        Assert.Equal(CharacterId, document.Id);
        Assert.Equal("Nova 🤖", document.DisplayName);
        Assert.Equal(Rgba32.Parse("#515253"), document.PartColors.RightFoot);
        Assert.Equal(CharacterFeatureIds.AccentChevron, document.Features.TorsoAccent.FeatureId);
        Assert.Equal(-0.6, document.Features.TorsoAccent.OffsetY);
        Assert.Equal(JsonValueKind.True, document.ExtensionData["futureFlag"].ValueKind);
    }

    [Fact]
    public void Serialize_UsesCanonicalGuidAndUppercaseColorStrings()
    {
        CharacterDocument document = CharacterDocument.CreateDefault(CharacterId, "Buddy") with
        {
            PartColors = CharacterPartColors.BuiltIn with
            {
                Head = new Rgba32(0x0a, 0xbc, 0xef),
            },
        };

        string json = CharacterDocumentPolicy.Serialize(document);

        Assert.Contains("\"id\": \"12345678-1234-4567-89ab-1234567890ab\"", json);
        Assert.Contains("\"head\": \"#0ABCEF\"", json);
        Assert.False(json.Contains("#0abcef", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingOptionalNestedFields_DefaultDeterministically()
    {
        string json = $$"""
            {
              "schemaVersion": 1,
              "id": "{{CharacterId:D}}",
              "displayName": "Buddy",
              "partColors": { "head": "#010203" },
              "features": { "eyes": { "featureId": "eyes.round_dot" } }
            }
            """;

        CharacterDocument document = CharacterDocumentPolicy.DecodeAndMigrate(json).Document!;

        Assert.Equal(Rgba32.Parse("#010203"), document.PartColors.Head);
        Assert.Equal(CharacterPartColors.BuiltIn.Torso, document.PartColors.Torso);
        Assert.Equal(CharacterFeatureIds.EyesRoundDot, document.Features.Eyes.FeatureId);
        Assert.Equal(1.0, document.Features.Eyes.Scale);
        Assert.Equal(CharacterFeatureIds.BrowsSoftArc, document.Features.Brows.FeatureId);
        Assert.Equal(CharacterFeatureIds.AccentNone, document.Features.TorsoAccent.FeatureId);
    }

    [Fact]
    public void UnknownFeatureId_SurvivesDecodeNormalizeAndSerialize()
    {
        string json = $$"""
            {
              "schemaVersion": 1,
              "id": "{{CharacterId:D}}",
              "displayName": " Future Eyes ",
              "features": {
                "eyes": { "featureId": "eyes.from_future" }
              },
              "futureTopLevel": { "answer": 42 }
            }
            """;

        CharacterDocument decoded = CharacterDocumentPolicy.DecodeAndMigrate(json).Document!;
        CharacterDocument normalized = CharacterDocumentNormalizer.Normalize(decoded).Document;
        string serialized = CharacterDocumentPolicy.Serialize(normalized);
        CharacterDocument roundTrip = CharacterDocumentPolicy.DecodeAndMigrate(serialized).Document!;

        Assert.Equal("eyes.from_future", roundTrip.Features.Eyes.FeatureId);
        Assert.Equal("Future Eyes", roundTrip.DisplayName);
        Assert.Equal(42, roundTrip.ExtensionData["futureTopLevel"].GetProperty("answer").GetInt32());
    }

    [Fact]
    public void FutureSchema_IsNotClassifiedAsMalformed()
    {
        CharacterDecodeResult result = CharacterDocumentPolicy.DecodeAndMigrate(
            """{"schemaVersion":999,"id":"12345678-1234-4567-89ab-1234567890ab","displayName":"Buddy"}""");

        Assert.Equal(CharacterDecodeStatus.UnsupportedFutureVersion, result.Status);
        Assert.Null(result.Document);
    }

    [Fact]
    public void SequentialMigrationHarness_RefusesMissingStep()
    {
        CharacterDecodeResult result = CharacterDocumentPolicy.DecodeAndMigrate(
            """{"schemaVersion":0,"id":"12345678-1234-4567-89ab-1234567890ab","displayName":"Buddy"}""");

        Assert.Equal(CharacterDecodeStatus.MissingMigrationStep, result.Status);
        Assert.Contains("schema 0", result.Detail ?? string.Empty);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"not-a-guid\",\"displayName\":\"Buddy\"}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"12345678-1234-4567-89ab-1234567890ab\",\"displayName\":\"Buddy\",\"partColors\":{\"head\":\"red\"}}")]
    public void MalformedRequiredValues_AreClassifiedWithoutThrowing(string json)
    {
        CharacterDecodeResult result = CharacterDocumentPolicy.DecodeAndMigrate(json);

        Assert.Equal(CharacterDecodeStatus.Malformed, result.Status);
        Assert.Null(result.Document);
    }

    private static CharacterFeatureDocument Feature(
        string id,
        double x,
        double y,
        double scale,
        string color) => new()
    {
        FeatureId = id,
        OffsetX = x,
        OffsetY = y,
        Scale = scale,
        Color = Rgba32.Parse(color),
    };
}
