using System;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

public sealed class CharacterPaintManifestTests
{
    private const string Id = "12345678-1234-4567-89ab-1234567890ab";

    [Fact]
    public void Schema1_MigratesSequentiallyToEmptyCurrentSchemaPaintManifest()
    {
        CharacterDecodeResult result = CharacterDocumentPolicy.DecodeAndMigrate(
            $$"""{"schemaVersion":1,"id":"{{Id}}","displayName":"Buddy"}""");

        Assert.Equal(CharacterDecodeStatus.Valid, result.Status);
        CharacterDocument document = Assert.IsType<CharacterDocument>(result.Document);
        Assert.Equal(CharacterDocumentPolicy.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Empty(document.Paint.Declared());
    }

    [Fact]
    public void Schema2_WhitelistedPaintPathsRoundTrip()
    {
        CharacterDocument source = CharacterDocument.CreateDefault(Guid.Parse(Id), "Painted") with
        {
            Paint = CharacterPaintManifest.ForNonBlank(new[]
            {
                PaintPart.Head,
                PaintPart.RightFoot,
            }),
        };

        string json = CharacterDocumentPolicy.Serialize(source);
        CharacterDocument decoded = CharacterDocumentPolicy.DecodeAndMigrate(json).Document!;

        Assert.Equal("paint/head.png", decoded.Paint.Head);
        Assert.Equal("paint/right_foot.png", decoded.Paint.RightFoot);
        Assert.Null(decoded.Paint.Torso);
    }

    [Theory]
    [InlineData("../head.png")]
    [InlineData("paint/HEAD.png")]
    [InlineData("paint/torso.png")]
    [InlineData("C:/paint/head.png")]
    public void HeadReference_RejectsTraversalCaseAndCrossPartPaths(string path)
    {
        string json = $$"""
            {
              "schemaVersion": 2,
              "id": "{{Id}}",
              "displayName": "Buddy",
              "paint": { "head": "{{path}}" }
            }
            """;

        CharacterDecodeResult result = CharacterDocumentPolicy.DecodeAndMigrate(json);

        Assert.Equal(CharacterDecodeStatus.Malformed, result.Status);
        Assert.Null(result.Document);
    }
}
