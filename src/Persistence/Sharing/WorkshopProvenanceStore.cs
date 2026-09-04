using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopBuddy.Persistence.Sharing;

public sealed record WorkshopProvenance(
    ulong PublishedFileId,
    long SteamTimeUpdated,
    DateTimeOffset ImportedUtc,
    string ManifestSha256,
    string ContentType);

public static class WorkshopProvenanceStore
{
    public const string FileName = "workshop-provenance.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static WorkshopProvenance Create(
        ulong publishedFileId,
        long steamTimeUpdated,
        DateTimeOffset importedUtc,
        ReadOnlySpan<byte> manifestBytes,
        string contentType) => new(
            publishedFileId,
            steamTimeUpdated,
            importedUtc,
            Convert.ToHexString(SHA256.HashData(manifestBytes)),
            contentType);

    public static void Write(string destinationDirectory, WorkshopProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        Directory.CreateDirectory(destinationDirectory);
        string path = Path.Combine(destinationDirectory, FileName);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(provenance, Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static WorkshopProvenance? TryRead(string destinationDirectory)
    {
        try
        {
            string path = Path.Combine(destinationDirectory, FileName);
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 16 * 1024) return null;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null) return null;
            return JsonSerializer.Deserialize<WorkshopProvenance>(File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
