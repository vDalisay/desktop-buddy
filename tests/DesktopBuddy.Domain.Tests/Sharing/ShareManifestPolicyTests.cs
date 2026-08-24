using System;
using DesktopBuddy.Domain.Sharing;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sharing;

public sealed class ShareManifestPolicyTests
{
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void Room_manifest_roundtrips()
    {
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.RoomPainting,
            "active-room",
            "test",
            [new Sha256FileEntry { Path = ShareManifestPolicy.RoomBackgroundPath, Sha256 = Hash, EncodedBytes = 128 }]);

        byte[] encoded = ShareManifestPolicy.Serialize(manifest);
        ShareManifestDecodeResult decoded = ShareManifestPolicy.Decode(encoded, ShareContentType.RoomPainting);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(ShareContentTypes.RoomPainting, decoded.Manifest!.ContentType);
        Assert.Single(decoded.Manifest.Files);
    }

    [Theory]
    [InlineData("../environment/background.png")]
    [InlineData("environment/../background.png")]
    [InlineData("/environment/background.png")]
    [InlineData("environment\\background.png")]
    [InlineData("environment//background.png")]
    [InlineData("C:/background.png")]
    public void Unsafe_paths_are_rejected(string path)
    {
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.RoomPainting,
            "active-room",
            "test",
            [new Sha256FileEntry { Path = path, Sha256 = Hash, EncodedBytes = 128 }]);

        ShareValidationResult validation = ShareManifestPolicy.Validate(manifest, ShareContentType.RoomPainting);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Code is ShareValidationCode.InvalidPath or ShareValidationCode.MissingFile);
    }

    [Fact]
    public void Duplicate_paths_are_rejected()
    {
        var file = new Sha256FileEntry { Path = ShareManifestPolicy.CharacterFileName, Sha256 = Hash, EncodedBytes = 128 };
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.BuddyCharacter,
            Guid.NewGuid().ToString("D"),
            "test",
            [file, file]);

        ShareValidationResult validation = ShareManifestPolicy.Validate(manifest, ShareContentType.BuddyCharacter);

        Assert.Contains(validation.Issues, issue => issue.Code == ShareValidationCode.DuplicatePath);
    }

    [Fact]
    public void Content_type_mismatch_is_rejected()
    {
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.RoomPainting,
            "active-room",
            "test",
            [new Sha256FileEntry { Path = ShareManifestPolicy.RoomBackgroundPath, Sha256 = Hash, EncodedBytes = 128 }]);

        ShareValidationResult validation = ShareManifestPolicy.Validate(manifest, ShareContentType.BuddyCharacter);

        Assert.Contains(validation.Issues, issue => issue.Code == ShareValidationCode.WrongContentType);
    }

    [Fact]
    public void Future_schema_is_refused_without_migration()
    {
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.RoomPainting,
            "active-room",
            "test",
            [new Sha256FileEntry { Path = ShareManifestPolicy.RoomBackgroundPath, Sha256 = Hash, EncodedBytes = 128 }]) with
        {
            SchemaVersion = ShareManifestPolicy.CurrentSchemaVersion + 1,
        };

        ShareValidationResult validation = ShareManifestPolicy.Validate(manifest, ShareContentType.RoomPainting);

        Assert.Contains(validation.Issues, issue => issue.Code == ShareValidationCode.UnsupportedSchema);
    }

    [Fact]
    public void Unexpected_buddy_payload_is_rejected()
    {
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.BuddyCharacter,
            Guid.NewGuid().ToString("D"),
            "test",
            [
                new Sha256FileEntry { Path = ShareManifestPolicy.CharacterFileName, Sha256 = Hash, EncodedBytes = 128 },
                new Sha256FileEntry { Path = "scripts/evil.gd", Sha256 = Hash, EncodedBytes = 128 },
            ]);

        ShareValidationResult validation = ShareManifestPolicy.Validate(manifest, ShareContentType.BuddyCharacter);

        Assert.Contains(validation.Issues, issue => issue.Code == ShareValidationCode.UnexpectedFile);
    }

    [Fact]
    public void Invalid_hash_is_rejected()
    {
        ShareManifest manifest = ShareManifestPolicy.Create(
            ShareContentType.RoomPainting,
            "active-room",
            "test",
            [new Sha256FileEntry { Path = ShareManifestPolicy.RoomBackgroundPath, Sha256 = "not-a-hash", EncodedBytes = 128 }]);

        ShareValidationResult validation = ShareManifestPolicy.Validate(manifest, ShareContentType.RoomPainting);

        Assert.Contains(validation.Issues, issue => issue.Code == ShareValidationCode.InvalidHash);
    }
}
