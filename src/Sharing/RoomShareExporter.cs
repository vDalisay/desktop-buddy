using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;

namespace DesktopBuddy.Sharing;

public sealed class RoomShareExporter
{
    private readonly WorkshopStagingStore _staging;
    private readonly ShareFolderReader _reader;
    private readonly string _appVersion;

    public RoomShareExporter(WorkshopStagingStore staging, string appVersion)
    {
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _reader = new ShareFolderReader();
        _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "development" : appVersion;
    }

    public Task<ShareExportResult> ExportAsync(
        ReadOnlyMemory<byte> pixels,
        Guid operationId,
        ReadOnlyMemory<byte>? previewPng = null,
        CancellationToken token = default) => Task.Run(
        () => Export(pixels.Span, operationId, previewPng, token),
        CancellationToken.None);

    public ShareExportResult Export(
        ReadOnlySpan<byte> pixels,
        Guid operationId,
        ReadOnlyMemory<byte>? previewPng = null,
        CancellationToken token = default)
    {
        if (pixels.Length != EnvironmentCanvasPolicy.Bytes)
            return new ShareExportResult(false, null, null, "Room canvas must be exactly 512x512 RGBA8.");
        WorkshopPublishStaging staging = _staging.CreatePublish(operationId);
        try
        {
            token.ThrowIfCancellationRequested();
            byte[] png = PaintPngCodec.Encode(pixels);
            var entry = new Sha256FileEntry
            {
                Path = ShareManifestPolicy.RoomBackgroundPath,
                Sha256 = Convert.ToHexString(SHA256.HashData(png)),
                EncodedBytes = png.LongLength,
            };
            ShareManifest manifest = ShareManifestPolicy.Create(
                ShareContentType.RoomPainting,
                "active-room",
                _appVersion,
                [entry]);
            byte[] manifestBytes = ShareManifestPolicy.Serialize(manifest);
            WorkshopStagingStore.WriteOwnedFile(staging.ContentRoot, entry.Path, png);
            WorkshopStagingStore.WriteOwnedFile(staging.ContentRoot, ShareManifestPolicy.ManifestFileName, manifestBytes);

            ReadOnlyMemory<byte> preview = previewPng is { Length: > 0 } supplied ? supplied : png;
            if (preview.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("Workshop preview exceeds the 2 MiB local preview budget.");
            File.WriteAllBytes(staging.PreviewPath, preview.ToArray());
            token.ThrowIfCancellationRequested();

            ShareFolderReadResult verified = _reader.Read(staging.ContentRoot, ShareContentType.RoomPainting);
            if (!verified.IsSuccess)
            {
                _staging.Cleanup(operationId);
                return new ShareExportResult(false, null, null, string.Join("; ", verified.Validation.Issues));
            }
            return new ShareExportResult(true, staging, manifest);
        }
        catch (OperationCanceledException)
        {
            _staging.Cleanup(operationId);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            _staging.Cleanup(operationId);
            return new ShareExportResult(false, null, null, exception.Message);
        }
    }
}
