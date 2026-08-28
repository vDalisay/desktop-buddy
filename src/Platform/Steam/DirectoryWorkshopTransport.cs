using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// Deterministic Workshop emulator for CI and local development. It models remote item creation,
/// immutable submitted snapshots, subscription enumeration and installed-content lookup without a
/// Steam client. It is never selected automatically in release builds.
/// </summary>
public sealed class DirectoryWorkshopTransport : ISteamWorkshopTransport
{
    private const string MetadataFileName = "item.json";
    private readonly string _root;
    private readonly object _gate = new();
    private ulong _nextId;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record ItemMetadata
    {
        public ulong PublishedFileId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Metadata { get; init; } = string.Empty;
        public string[] Tags { get; init; } = [];
        public WorkshopVisibility Visibility { get; init; }
        public long TimeUpdated { get; init; }
        public bool Subscribed { get; init; } = true;
    }

    public DirectoryWorkshopTransport(string resolvedRoot, ulong firstId = 1000)
    {
        if (string.IsNullOrWhiteSpace(resolvedRoot))
            throw new ArgumentException("A directory Workshop root is required.", nameof(resolvedRoot));
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedRoot));
        Directory.CreateDirectory(_root);
        _nextId = Math.Max(firstId, DiscoverNextId());
    }

    public bool IsAvailable => true;
    public bool IsInstalled => true;
    public bool IsInitialized => true;
    public string? UnavailableReason => null;
    public string Root => _root;

    public Task<WorkshopCreateRemoteResult> CreateItemAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return Task.FromResult(new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Cancelled,
                0,
                false,
                Detail: "Directory Workshop create cancelled."));

        ulong id;
        lock (_gate)
        {
            id = _nextId++;
            Directory.CreateDirectory(ItemRoot(id));
        }
        return Task.FromResult(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Success, id, false));
    }

    public Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token) => Task.Run(() =>
    {
        if (update.PublishedFileId == 0)
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "Published file ID is required.");

        string item = ItemRoot(update.PublishedFileId);
        string staging = item + ".staging";
        string previous = item + ".previous";
        try
        {
            token.ThrowIfCancellationRequested();
            ValidateSource(update.ContentFolder, update.PreviewFile);
            SafeDelete(staging);
            SafeDelete(previous);
            Directory.CreateDirectory(staging);
            string stagedContent = Path.Combine(staging, "content");
            Directory.CreateDirectory(stagedContent);

            long total = CountBytes(update.ContentFolder) +
                (string.IsNullOrWhiteSpace(update.PreviewFile) ? 0 : new FileInfo(update.PreviewFile!).Length);
            long copied = 0;
            CopyTree(update.ContentFolder, stagedContent, token, bytes =>
            {
                copied += bytes;
                progress?.Report(new WorkshopTransferProgress((ulong)Math.Max(0, copied), (ulong)Math.Max(0, total), "Uploading"));
            });
            if (!string.IsNullOrWhiteSpace(update.PreviewFile))
            {
                string previewTarget = Path.Combine(staging, "preview.png");
                File.Copy(update.PreviewFile!, previewTarget, overwrite: false);
                copied += new FileInfo(previewTarget).Length;
                progress?.Report(new WorkshopTransferProgress((ulong)Math.Max(0, copied), (ulong)Math.Max(0, total), "Uploading"));
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var metadata = new ItemMetadata
            {
                PublishedFileId = update.PublishedFileId,
                Title = Normalize(update.Title, 128),
                Description = Normalize(update.Description, 8000),
                Metadata = Normalize(update.Metadata, 5000),
                Tags = update.Tags.Take(32).Select(tag => Normalize(tag, 255)).Where(tag => tag.Length > 0).ToArray(),
                Visibility = update.Visibility,
                TimeUpdated = now,
                Subscribed = true,
            };
            File.WriteAllText(Path.Combine(staging, MetadataFileName), JsonSerializer.Serialize(metadata, JsonOptions));
            token.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (Directory.Exists(item)) Directory.Move(item, previous);
                try
                {
                    Directory.Move(staging, item);
                    SafeDelete(previous);
                }
                catch
                {
                    if (!Directory.Exists(item) && Directory.Exists(previous)) Directory.Move(previous, item);
                    throw;
                }
            }
            progress?.Report(new WorkshopTransferProgress((ulong)Math.Max(0, total), (ulong)Math.Max(0, total), "Complete"));
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Success, update.PublishedFileId, false);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(staging);
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Cancelled, update.PublishedFileId, false, Detail: "Directory Workshop update cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            SafeDelete(staging);
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: exception.Message);
        }
    }, CancellationToken.None);

    public Task<WorkshopSubscriptionQueryResult> GetSubscribedItemsAsync(CancellationToken token) => Task.Run(() =>
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var items = new List<PublishedWorkshopItem>();
            foreach (string directory in Directory.EnumerateDirectories(_root))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(directory);
                if (!ulong.TryParse(name, out ulong id) || IsLinked(directory)) continue;
                ItemMetadata? metadata = TryReadMetadata(directory);
                if (metadata is null || !metadata.Subscribed) continue;
                string? contentType = ParseContentType(metadata.Metadata, metadata.Tags);
                items.Add(new PublishedWorkshopItem(
                    id,
                    WorkshopItemState.Subscribed | WorkshopItemState.Installed,
                    metadata.Title.Length == 0 ? $"Workshop Item {id}" : metadata.Title,
                    metadata.TimeUpdated,
                    contentType));
            }
            return WorkshopSubscriptionQueryResult.Success(items.OrderBy(item => item.PublishedFileId).ToArray());
        }
        catch (OperationCanceledException)
        {
            return new WorkshopSubscriptionQueryResult(
                WorkshopRemoteStatus.Cancelled,
                Array.Empty<PublishedWorkshopItem>(),
                "Directory Workshop subscription query cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new WorkshopSubscriptionQueryResult(
                WorkshopRemoteStatus.Failed,
                Array.Empty<PublishedWorkshopItem>(),
                exception.Message);
        }
    }, CancellationToken.None);

    public Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return Task.FromResult(new WorkshopInstalledItemResult(
                WorkshopRemoteStatus.Cancelled,
                publishedFileId,
                null,
                0,
                Detail: "Directory Workshop install lookup cancelled."));

        string item = ItemRoot(publishedFileId);
        ItemMetadata? metadata = TryReadMetadata(item);
        string content = Path.Combine(item, "content");
        if (metadata is null || !Directory.Exists(content) || IsLinked(content))
        {
            return Task.FromResult(new WorkshopInstalledItemResult(
                WorkshopRemoteStatus.Failed,
                publishedFileId,
                null,
                0,
                Detail: "Workshop item is not installed."));
        }
        ulong bytes = (ulong)Math.Max(0, CountBytes(content));
        progress?.Report(new WorkshopTransferProgress(bytes, bytes, "Installed"));
        return Task.FromResult(new WorkshopInstalledItemResult(
            WorkshopRemoteStatus.Success,
            publishedFileId,
            content,
            metadata.TimeUpdated));
    }

    public void OpenWorkshopBrowser() { }
    public void OpenWorkshopItem(ulong publishedFileId) { }

    public bool SetSubscribed(ulong publishedFileId, bool subscribed)
    {
        string item = ItemRoot(publishedFileId);
        ItemMetadata? metadata = TryReadMetadata(item);
        if (metadata is null) return false;
        File.WriteAllText(Path.Combine(item, MetadataFileName), JsonSerializer.Serialize(metadata with { Subscribed = subscribed }, JsonOptions));
        return true;
    }

    private ulong DiscoverNextId()
    {
        ulong max = 0;
        foreach (string directory in Directory.EnumerateDirectories(_root))
            if (ulong.TryParse(Path.GetFileName(directory), out ulong id)) max = Math.Max(max, id);
        return max == ulong.MaxValue ? ulong.MaxValue : max + 1;
    }

    private string ItemRoot(ulong id)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        string candidate = Path.GetFullPath(Path.Combine(_root, id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, comparison)) throw new InvalidDataException("Workshop item escaped emulator root.");
        return candidate;
    }

    private static ItemMetadata? TryReadMetadata(string item)
    {
        try
        {
            if (!Directory.Exists(item) || IsLinked(item)) return null;
            string path = Path.Combine(item, MetadataFileName);
            if (!File.Exists(path) || IsLinked(path) || new FileInfo(path).Length > 64 * 1024) return null;
            return JsonSerializer.Deserialize<ItemMetadata>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void ValidateSource(string contentFolder, string? previewFile)
    {
        if (!Directory.Exists(contentFolder) || IsLinked(contentFolder)) throw new InvalidDataException("Workshop content folder is missing or linked.");
        if (!string.IsNullOrWhiteSpace(previewFile) && (!File.Exists(previewFile) || IsLinked(previewFile)))
            throw new InvalidDataException("Workshop preview file is missing or linked.");
    }

    private static void CopyTree(string source, string destination, CancellationToken token, Action<long> copied)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (IsLinked(directory)) throw new InvalidDataException("Linked content directories are not allowed.");
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (IsLinked(file)) throw new InvalidDataException("Linked content files are not allowed.");
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
            copied(new FileInfo(target).Length);
        }
    }

    private static long CountBytes(string root)
    {
        long total = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            total = checked(total + new FileInfo(file).Length);
        return total;
    }

    private static bool IsLinked(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private static string Normalize(string? value, int max)
    {
        string normalized = string.Concat((value ?? string.Empty).Where(c => !char.IsControl(c) || c is '\n' or '\t')).Trim();
        return normalized.Length <= max ? normalized : normalized[..max];
    }

    private static string? ParseContentType(string metadata, IReadOnlyCollection<string> tags)
    {
        if (metadata.Contains("desktop-buddy:room:1", StringComparison.Ordinal) || tags.Contains("Room Painting")) return "room-painting";
        if (metadata.Contains("desktop-buddy:buddy:1", StringComparison.Ordinal) || tags.Contains("Buddy")) return "buddy-character";
        return null;
    }

    private static void SafeDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
