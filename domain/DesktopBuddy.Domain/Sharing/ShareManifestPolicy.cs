using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Painting;

namespace DesktopBuddy.Domain.Sharing;

public static class ShareManifestPolicy
{
    public const int CurrentSchemaVersion = 1;
    public const int SupportedAppContentVersion = 1;
    public const int MaximumManifestBytes = 64 * 1024;
    public const int MaximumCharacterJsonBytes = 512 * 1024;
    public const string FormatId = "desktop-buddy-share";
    public const string ManifestFileName = "manifest.json";
    public const string CharacterFileName = "character.json";
    public const string RoomBackgroundPath = "environment/background.png";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly HashSet<string> BuddyAllowedPaths = new(
        new[] { CharacterFileName }.Concat(PaintPolicy.WhitelistedPaths.Values),
        StringComparer.Ordinal);

    public static ShareManifest Create(
        ShareContentType type,
        string sourceId,
        string createdWithAppVersion,
        IEnumerable<Sha256FileEntry> files) => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        ContentType = ShareContentTypes.ToWire(type),
        FormatId = FormatId,
        MinimumAppContentVersion = SupportedAppContentVersion,
        CreatedWithAppVersion = createdWithAppVersion,
        SourceId = sourceId,
        Files = files?.ToList() ?? throw new ArgumentNullException(nameof(files)),
    };

    public static byte[] Serialize(ShareManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ShareValidationResult validation = Validate(manifest, expectedType: null);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => issue.Message)), nameof(manifest));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (bytes.Length > MaximumManifestBytes)
            throw new ArgumentException("Share manifest exceeds the 64 KiB limit.", nameof(manifest));
        return bytes;
    }

    public static ShareManifestDecodeResult Decode(ReadOnlySpan<byte> utf8, ShareContentType expectedType)
    {
        if (utf8.Length == 0)
            return Failure(ShareValidationCode.MalformedManifest, ManifestFileName, "Share manifest is empty.");
        if (utf8.Length > MaximumManifestBytes)
            return Failure(ShareValidationCode.ManifestTooLarge, ManifestFileName, "Share manifest exceeds the 64 KiB limit.");
        try
        {
            ShareManifest? manifest = JsonSerializer.Deserialize<ShareManifest>(utf8, JsonOptions);
            if (manifest is null)
                return Failure(ShareValidationCode.MalformedManifest, ManifestFileName, "Share manifest is empty.");
            ShareValidationResult validation = Validate(manifest, expectedType);
            return new ShareManifestDecodeResult(validation.IsValid ? manifest : null, validation);
        }
        catch (JsonException exception)
        {
            return Failure(ShareValidationCode.MalformedManifest, ManifestFileName, exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return Failure(ShareValidationCode.MalformedManifest, ManifestFileName, exception.Message);
        }
    }

    public static ShareValidationResult Validate(ShareManifest manifest, ShareContentType? expectedType)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<ShareValidationIssue>();

        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new ShareValidationIssue(
                ShareValidationCode.UnsupportedSchema,
                "schemaVersion",
                manifest.SchemaVersion > CurrentSchemaVersion
                    ? $"Share schema {manifest.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}."
                    : $"Share schema {manifest.SchemaVersion} is not supported."));
        }
        if (!string.Equals(manifest.FormatId, FormatId, StringComparison.Ordinal))
            issues.Add(new ShareValidationIssue(ShareValidationCode.WrongFormat, "formatId", "Unknown share format."));
        if (manifest.MinimumAppContentVersion <= 0 || manifest.MinimumAppContentVersion > SupportedAppContentVersion)
        {
            issues.Add(new ShareValidationIssue(
                ShareValidationCode.UnsupportedContentVersion,
                "minimumAppContentVersion",
                $"Content requires app content version {manifest.MinimumAppContentVersion}; supported is {SupportedAppContentVersion}."));
        }

        if (!ShareContentTypes.TryParse(manifest.ContentType, out ShareContentType parsedType))
        {
            issues.Add(new ShareValidationIssue(ShareValidationCode.WrongContentType, "contentType", "Unknown share content type."));
        }
        else if (expectedType.HasValue && parsedType != expectedType.Value)
        {
            issues.Add(new ShareValidationIssue(
                ShareValidationCode.WrongContentType,
                "contentType",
                $"Expected {ShareContentTypes.ToWire(expectedType.Value)} content."));
        }

        if (!IsSafeScalar(manifest.SourceId, 1, 128))
            issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidSourceId, "sourceId", "Source ID is missing or invalid."));
        if (!IsSafeScalar(manifest.CreatedWithAppVersion, 1, 64))
            issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidAppVersion, "createdWithAppVersion", "App version is missing or invalid."));

        if (manifest.Files is null || manifest.Files.Count == 0)
        {
            issues.Add(new ShareValidationIssue(ShareValidationCode.MissingFile, "files", "Manifest declares no content files."));
            return new ShareValidationResult(issues);
        }
        if (manifest.Files.Count > 8)
            issues.Add(new ShareValidationIssue(ShareValidationCode.UnexpectedFile, "files", "Manifest declares too many files."));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        long aggregate = 0;
        foreach (Sha256FileEntry? file in manifest.Files)
        {
            if (file is null)
            {
                issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidPath, "files", "Manifest contains a null file entry."));
                continue;
            }

            string path = file.Path ?? string.Empty;
            if (!IsCanonicalRelativePath(path))
            {
                issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidPath, path, "File path is not a canonical safe relative path."));
                continue;
            }
            if (!seen.Add(path))
            {
                issues.Add(new ShareValidationIssue(ShareValidationCode.DuplicatePath, path, "File path is declared more than once."));
                continue;
            }

            if (ShareContentTypes.TryParse(manifest.ContentType, out ShareContentType type) && !IsAllowedPath(type, path))
                issues.Add(new ShareValidationIssue(ShareValidationCode.UnexpectedFile, path, "File path is not allowed for this content type."));

            long cap = path == CharacterFileName ? MaximumCharacterJsonBytes : PaintPolicy.MaximumEncodedPngBytes;
            if (file.EncodedBytes <= 0 || file.EncodedBytes > cap)
                issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidEncodedSize, path, $"Declared encoded size is outside the allowed range (max {cap} bytes)."));
            else
                aggregate += file.EncodedBytes;

            if (!IsSha256(file.Sha256))
                issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidHash, path, "SHA-256 must contain exactly 64 hexadecimal characters."));
        }

        if (ShareContentTypes.TryParse(manifest.ContentType, out ShareContentType contentType))
        {
            if (contentType == ShareContentType.RoomPainting)
            {
                if (manifest.Files.Count != 1 || !seen.Contains(RoomBackgroundPath))
                    issues.Add(new ShareValidationIssue(ShareValidationCode.MissingFile, RoomBackgroundPath, "Room share must contain exactly one background PNG."));
                if (aggregate > PaintPolicy.MaximumEncodedPngBytes)
                    issues.Add(new ShareValidationIssue(ShareValidationCode.AggregateTooLarge, "files", "Room share exceeds the encoded payload budget."));
            }
            else
            {
                if (!seen.Contains(CharacterFileName))
                    issues.Add(new ShareValidationIssue(ShareValidationCode.MissingFile, CharacterFileName, "Buddy share is missing character.json."));
                long paintBytes = manifest.Files
                    .Where(file => file is not null && !string.Equals(file.Path, CharacterFileName, StringComparison.Ordinal) && file.EncodedBytes > 0)
                    .Sum(file => file!.EncodedBytes);
                if (paintBytes > PaintPolicy.MaximumAggregateEncodedBytes)
                    issues.Add(new ShareValidationIssue(ShareValidationCode.AggregateTooLarge, "files", "Buddy paint exceeds the 12 MiB aggregate budget."));
            }
        }

        return new ShareValidationResult(issues);
    }

    public static bool IsCanonicalRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 160 || path[0] is '/' or '\\' || path.Contains('\\'))
            return false;
        if (path.Contains("//", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal))
            return false;
        string[] segments = path.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.IndexOf('\0') >= 0 || segment.IndexOf(':') >= 0)
                return false;
            foreach (char c in segment)
                if (char.IsControl(c)) return false;
        }
        return true;
    }

    public static bool IsAllowedPath(ShareContentType type, string path) => type switch
    {
        ShareContentType.RoomPainting => string.Equals(path, RoomBackgroundPath, StringComparison.Ordinal),
        ShareContentType.BuddyCharacter => BuddyAllowedPaths.Contains(path),
        _ => false,
    };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsSafeScalar(string? value, int minLength, int maxLength)
    {
        if (value is null || value.Length < minLength || value.Length > maxLength)
            return false;
        foreach (char c in value)
            if (char.IsControl(c)) return false;
        return true;
    }

    private static ShareManifestDecodeResult Failure(ShareValidationCode code, string path, string message) =>
        new(null, new ShareValidationResult([new ShareValidationIssue(code, path, message)]));
}
