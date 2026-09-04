using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.Persistence.Sharing;

public sealed record RoomPaintingLibraryEntry(
    Guid Id,
    string DisplayName,
    DateTimeOffset ImportedUtc,
    ulong? WorkshopItemId,
    string Description = "");

public sealed record RoomPaintingImportResult(
    bool Success,
    RoomPaintingLibraryEntry? Entry,
    string? Detail = null,
    bool IsCancelled = false);

/// <summary>
/// Imported Workshop rooms are independent local presets. Import never changes the active room;
/// applying a preset is a separate explicit call through the existing EnvironmentPaintStore.
/// </summary>
public sealed class RoomPaintingLibraryStore
{
    private const string MetadataFileName = "room.json";
    private const string BackgroundFileName = "background.png";
    private readonly string _root;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public RoomPaintingLibraryStore(string resolvedRoot)
    {
        if (string.IsNullOrWhiteSpace(resolvedRoot)) throw new ArgumentException("A room library root is required.", nameof(resolvedRoot));
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedRoot));
    }

    public string Root => _root;

    public Task<RoomPaintingImportResult> ImportAsync(
        string displayName,
        ReadOnlyMemory<byte> pixels,
        WorkshopProvenance? provenance,
        CancellationToken token = default,
        string description = "") => Task.Run(
        () => Import(displayName, pixels.Span, provenance, token, description),
        CancellationToken.None);

    public RoomPaintingImportResult Import(
        string displayName,
        ReadOnlySpan<byte> pixels,
        WorkshopProvenance? provenance = null,
        CancellationToken token = default,
        string description = "")
    {
        if (pixels.Length != EnvironmentCanvasPolicy.Bytes)
            return new RoomPaintingImportResult(false, null, "Room painting must be exactly 512x512 RGBA8.");
        string name = NormalizeName(displayName);
        Guid id = Guid.NewGuid();
        string destination = DirectoryFor(id);
        string staging = destination + ".staging";
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_root);
            SafeDelete(staging);
            Directory.CreateDirectory(staging);
            byte[] png = PaintPngCodec.Encode(pixels);
            File.WriteAllBytes(Path.Combine(staging, BackgroundFileName), png);
            var entry = new RoomPaintingLibraryEntry(
                id,
                name,
                DateTimeOffset.UtcNow,
                provenance?.PublishedFileId,
                NormalizeDescription(description));
            File.WriteAllText(Path.Combine(staging, MetadataFileName), JsonSerializer.Serialize(entry, Options));
            if (provenance is not null) WorkshopProvenanceStore.Write(staging, provenance);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return new RoomPaintingImportResult(true, entry);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(staging);
            return new RoomPaintingImportResult(false, null, "Import cancelled.", IsCancelled: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SafeDelete(staging);
            return new RoomPaintingImportResult(false, null, exception.Message);
        }
    }

    public byte[]? LoadPixels(Guid id)
    {
        try
        {
            string directory = DirectoryFor(id);
            if (!Directory.Exists(directory) || IsLinked(directory)) return null;
            string path = Path.Combine(directory, BackgroundFileName);
            if (!File.Exists(path) || IsLinked(path)) return null;
            byte[] png = ReadBounded(path, DesktopBuddy.Domain.Painting.PaintPolicy.MaximumEncodedPngBytes);
            byte[] pixels = PaintPngCodec.Decode(png);
            return pixels.Length == EnvironmentCanvasPolicy.Bytes ? pixels : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    public async Task<bool> ApplyAsync(Guid id, EnvironmentPaintStore activeStore, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(activeStore);
        byte[]? pixels = LoadPixels(id);
        if (pixels is null) return false;
        await activeStore.SaveAsync(pixels, token);
        return true;
    }

    public IReadOnlyList<RoomPaintingLibraryEntry> List()
    {
        if (!Directory.Exists(_root)) return [];
        var entries = new List<RoomPaintingLibraryEntry>();
        foreach (string directory in Directory.EnumerateDirectories(_root))
        {
            try
            {
                if (IsLinked(directory) || !Guid.TryParse(Path.GetFileName(directory), out _)) continue;
                string metadata = Path.Combine(directory, MetadataFileName);
                if (!File.Exists(metadata) || IsLinked(metadata) || new FileInfo(metadata).Length > 16 * 1024) continue;
                RoomPaintingLibraryEntry? entry = JsonSerializer.Deserialize<RoomPaintingLibraryEntry>(File.ReadAllText(metadata), Options);
                if (entry is not null) entries.Add(entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return entries
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id)
            .ToArray();
    }

    private string DirectoryFor(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Room preset ID cannot be empty.", nameof(id));
        string path = Path.GetFullPath(Path.Combine(_root, id.ToString("D")));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, comparison)) throw new InvalidDataException("Room preset escaped the library root.");
        return path;
    }

    private static string NormalizeName(string value)
    {
        string name = (value ?? string.Empty).Trim();
        if (name.Length == 0) return "Imported Room Painting";
        if (name.Length > 80) name = name[..80];
        return string.Concat(name.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
    }

    private static string NormalizeDescription(string value)
    {
        string description = (value ?? string.Empty).Trim();
        if (description.Length > 8000) description = description[..8000];
        return string.Concat(description.Select(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t' ? ' ' : c));
    }

    private static byte[] ReadBounded(string path, int max)
    {
        if (new FileInfo(path).Length > max) throw new InvalidDataException("Room painting PNG exceeds its size limit.");
        return File.ReadAllBytes(path);
    }

    private static bool IsLinked(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private static void SafeDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
