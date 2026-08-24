using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Sharing;

public enum ShareContentType
{
    RoomPainting,
    BuddyCharacter,
}

public static class ShareContentTypes
{
    public const string RoomPainting = "room-painting";
    public const string BuddyCharacter = "buddy-character";

    public static string ToWire(ShareContentType type) => type switch
    {
        ShareContentType.RoomPainting => RoomPainting,
        ShareContentType.BuddyCharacter => BuddyCharacter,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static bool TryParse(string? value, out ShareContentType type)
    {
        if (string.Equals(value, RoomPainting, StringComparison.Ordinal))
        {
            type = ShareContentType.RoomPainting;
            return true;
        }
        if (string.Equals(value, BuddyCharacter, StringComparison.Ordinal))
        {
            type = ShareContentType.BuddyCharacter;
            return true;
        }
        type = default;
        return false;
    }
}

public sealed record Sha256FileEntry
{
    public string Path { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long EncodedBytes { get; init; }
}

public sealed record ShareManifest
{
    public int SchemaVersion { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string FormatId { get; init; } = string.Empty;
    public int MinimumAppContentVersion { get; init; }
    public string CreatedWithAppVersion { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public List<Sha256FileEntry> Files { get; init; } = [];
}

public enum ShareValidationCode
{
    MalformedManifest,
    ManifestTooLarge,
    UnsupportedSchema,
    UnsupportedContentVersion,
    WrongFormat,
    WrongContentType,
    InvalidSourceId,
    InvalidAppVersion,
    MissingFile,
    UnexpectedFile,
    InvalidPath,
    DuplicatePath,
    InvalidHash,
    InvalidEncodedSize,
    AggregateTooLarge,
    LinkedPath,
    HashMismatch,
    InvalidPayload,
    IoFailure,
}

public readonly record struct ShareValidationIssue(
    ShareValidationCode Code,
    string Path,
    string Message);

public sealed record ShareValidationResult(IReadOnlyList<ShareValidationIssue> Issues)
{
    public static ShareValidationResult Valid { get; } = new(Array.Empty<ShareValidationIssue>());
    public bool IsValid => Issues.Count == 0;
}

public readonly record struct ShareManifestDecodeResult(
    ShareManifest? Manifest,
    ShareValidationResult Validation)
{
    public bool IsSuccess => Manifest is not null && Validation.IsValid;
}
