using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;

namespace DesktopBuddy.Sharing;

public sealed class RoomShareImporter
{
    private readonly WorkshopStagingStore _staging;
    private readonly ShareFolderReader _reader;
    private readonly RoomPaintingLibraryStore _library;
    private readonly Func<DateTimeOffset> _utcNow;

    public RoomShareImporter(
        WorkshopStagingStore staging,
        RoomPaintingLibraryStore library,
        Func<DateTimeOffset>? utcNow = null)
    {
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _reader = new ShareFolderReader();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<RoomShareImportResult> ImportAsync(
        string steamInstallFolder,
        WorkshopImportSource source,
        CancellationToken token = default) => Task.Run(
        () => Import(steamInstallFolder, source, token),
        CancellationToken.None);

    public RoomShareImportResult Import(
        string steamInstallFolder,
        WorkshopImportSource source,
        CancellationToken token = default)
    {
        Guid operationId = Guid.NewGuid();
        WorkshopIncomingStaging incoming;
        try
        {
            token.ThrowIfCancellationRequested();
            incoming = _staging.SnapshotIncoming(steamInstallFolder, operationId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return new RoomShareImportResult(false, null, null, exception.Message);
        }

        try
        {
            ShareFolderReadResult folder = _reader.Read(incoming.ContentRoot, ShareContentType.RoomPainting);
            if (!folder.IsSuccess || folder.Manifest is null)
                return Quarantine(incoming, folder.Validation.Issues.Count == 0 ? "invalid" : folder.Validation.Issues[0].Code.ToString(), string.Join("; ", folder.Validation.Issues));

            if (!folder.Files.TryGetValue(ShareManifestPolicy.RoomBackgroundPath, out byte[]? encoded))
                return Quarantine(incoming, "missing-background", "Room share does not contain its declared background PNG.");
            byte[] pixels;
            try
            {
                pixels = PaintPngCodec.Decode(encoded);
                if (pixels.Length != EnvironmentCanvasPolicy.Bytes)
                    throw new InvalidDataException("Room PNG decoded to an invalid byte count.");
            }
            catch (InvalidDataException exception)
            {
                return Quarantine(incoming, "invalid-png", exception.Message);
            }

            byte[] manifestBytes = File.ReadAllBytes(Path.Combine(incoming.ContentRoot, ShareManifestPolicy.ManifestFileName));
            WorkshopProvenance provenance = WorkshopProvenanceStore.Create(
                source.PublishedFileId,
                source.SteamTimeUpdated,
                _utcNow(),
                manifestBytes,
                ShareContentTypes.RoomPainting);
            RoomPaintingImportResult imported = _library.Import(source.DisplayName, pixels, provenance, token);
            if (!imported.Success)
                return new RoomShareImportResult(false, null, null, imported.Detail);

            _staging.Cleanup(operationId);
            return new RoomShareImportResult(true, imported.Entry);
        }
        catch (OperationCanceledException)
        {
            _staging.Cleanup(operationId);
            return new RoomShareImportResult(false, null, null, "Room import cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return Quarantine(incoming, "io", exception.Message);
        }
    }

    private RoomShareImportResult Quarantine(WorkshopIncomingStaging incoming, string reason, string detail)
    {
        try
        {
            string path = _staging.Quarantine(incoming, reason);
            return new RoomShareImportResult(false, null, path, detail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RoomShareImportResult(false, null, null, $"{detail}; quarantine failed: {exception.Message}");
        }
    }
}
