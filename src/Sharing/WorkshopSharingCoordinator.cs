using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Sharing;
using DesktopBuddy.Platform.Steam;

namespace DesktopBuddy.Sharing;

public enum WorkshopPublishStatus
{
    Published,
    NeedsLegalAgreement,
    Unavailable,
    Failed,
    Cancelled,
}

public sealed record WorkshopPublishResult(
    WorkshopPublishStatus Status,
    ulong PublishedFileId,
    string? Detail = null)
{
    public bool IsSuccess => Status is WorkshopPublishStatus.Published or WorkshopPublishStatus.NeedsLegalAgreement;
}

public enum WorkshopImportStatus
{
    ImportedRoom,
    ImportedBuddy,
    Unavailable,
    UnsupportedContent,
    Failed,
    Cancelled,
}

public sealed record WorkshopImportResult(
    WorkshopImportStatus Status,
    ulong PublishedFileId,
    Guid? LocalId,
    string? QuarantinePath = null,
    string? Detail = null)
{
    public bool IsSuccess => Status is WorkshopImportStatus.ImportedRoom or WorkshopImportStatus.ImportedBuddy;
}

/// <summary>
/// Application service for asynchronous social sharing. It owns workflow/state transitions only;
/// Steam transports do not know room/character types and importers do not know Steam callbacks.
/// </summary>
public sealed class WorkshopSharingCoordinator
{
    private readonly ISteamWorkshopTransport _transport;
    private readonly WorkshopStagingStore _staging;
    private readonly RoomShareExporter _roomExporter;
    private readonly RoomShareImporter _roomImporter;
    private readonly CharacterShareExporter _characterExporter;
    private readonly CharacterShareImporter _characterImporter;

    public WorkshopSharingCoordinator(
        ISteamWorkshopTransport transport,
        WorkshopStagingStore staging,
        RoomShareExporter roomExporter,
        RoomShareImporter roomImporter,
        CharacterShareExporter characterExporter,
        CharacterShareImporter characterImporter)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _roomExporter = roomExporter ?? throw new ArgumentNullException(nameof(roomExporter));
        _roomImporter = roomImporter ?? throw new ArgumentNullException(nameof(roomImporter));
        _characterExporter = characterExporter ?? throw new ArgumentNullException(nameof(characterExporter));
        _characterImporter = characterImporter ?? throw new ArgumentNullException(nameof(characterImporter));
    }

    public bool IsAvailable => _transport.IsAvailable;

    public async Task<WorkshopPublishResult> PublishRoomAsync(
        ReadOnlyMemory<byte> pixels,
        string title,
        string description,
        IProgress<WorkshopTransferProgress>? progress = null,
        CancellationToken token = default)
    {
        if (!_transport.IsAvailable)
            return new WorkshopPublishResult(WorkshopPublishStatus.Unavailable, 0, "Steam Workshop is unavailable; the local room is unchanged.");
        Guid operationId = Guid.NewGuid();
        ShareExportResult exported = await _roomExporter.ExportAsync(pixels, operationId, token);
        if (!exported.Success || exported.Staging is null)
            return new WorkshopPublishResult(WorkshopPublishStatus.Failed, 0, exported.Detail);
        return await PublishStagedAsync(
            exported.Staging.Value,
            title,
            description,
            ["DesktopBuddy.RoomPainting", "FormatVersion.1"],
            "desktop-buddy:room:1",
            progress,
            token);
    }

    public async Task<WorkshopPublishResult> PublishCharacterAsync(
        Guid characterId,
        string title,
        string description,
        ReadOnlyMemory<byte>? previewPng = null,
        IProgress<WorkshopTransferProgress>? progress = null,
        CancellationToken token = default)
    {
        if (!_transport.IsAvailable)
            return new WorkshopPublishResult(WorkshopPublishStatus.Unavailable, 0, "Steam Workshop is unavailable; the local buddy is unchanged.");
        Guid operationId = Guid.NewGuid();
        ShareExportResult exported = await _characterExporter.ExportAsync(characterId, operationId, previewPng, token);
        if (!exported.Success || exported.Staging is null)
            return new WorkshopPublishResult(WorkshopPublishStatus.Failed, 0, exported.Detail);
        return await PublishStagedAsync(
            exported.Staging.Value,
            title,
            description,
            ["DesktopBuddy.BuddyCharacter", "FormatVersion.1"],
            "desktop-buddy:buddy:1",
            progress,
            token);
    }

    public Task<IReadOnlyList<PublishedWorkshopItem>> GetSubscriptionsAsync(CancellationToken token = default) =>
        _transport.GetSubscribedItemsAsync(token);

    public async Task<WorkshopImportResult> ImportSubscribedAsync(
        PublishedWorkshopItem item,
        IProgress<WorkshopTransferProgress>? progress = null,
        CancellationToken token = default)
    {
        if (!_transport.IsAvailable)
            return new WorkshopImportResult(WorkshopImportStatus.Unavailable, item.PublishedFileId, null, Detail: "Steam Workshop is unavailable.");

        WorkshopInstalledItemResult installed = await _transport.EnsureInstalledAsync(item.PublishedFileId, progress, token);
        if (!installed.IsSuccess || installed.InstallFolder is null)
        {
            WorkshopImportStatus status = installed.Status == WorkshopRemoteStatus.Cancelled
                ? WorkshopImportStatus.Cancelled
                : WorkshopImportStatus.Failed;
            return new WorkshopImportResult(status, item.PublishedFileId, null, Detail: installed.Detail);
        }

        string? contentType = item.ContentType;
        if (contentType is null)
            contentType = DetectContentType(installed.InstallFolder);
        var source = new WorkshopImportSource(item.PublishedFileId, installed.TimeUpdated, item.DisplayName);

        if (string.Equals(contentType, ShareContentTypes.RoomPainting, StringComparison.Ordinal))
        {
            RoomShareImportResult imported = await _roomImporter.ImportAsync(installed.InstallFolder, source, token);
            return imported.Success && imported.Entry is not null
                ? new WorkshopImportResult(WorkshopImportStatus.ImportedRoom, item.PublishedFileId, imported.Entry.Id, Detail: imported.Detail)
                : new WorkshopImportResult(WorkshopImportStatus.Failed, item.PublishedFileId, null, imported.QuarantinePath, imported.Detail);
        }

        if (string.Equals(contentType, ShareContentTypes.BuddyCharacter, StringComparison.Ordinal))
        {
            CharacterShareImportResult imported = await _characterImporter.ImportAsync(installed.InstallFolder, source, token);
            return imported.Success && imported.LocalCharacterId.HasValue
                ? new WorkshopImportResult(WorkshopImportStatus.ImportedBuddy, item.PublishedFileId, imported.LocalCharacterId, Detail: imported.Detail)
                : new WorkshopImportResult(WorkshopImportStatus.Failed, item.PublishedFileId, null, imported.QuarantinePath, imported.Detail);
        }

        return new WorkshopImportResult(
            WorkshopImportStatus.UnsupportedContent,
            item.PublishedFileId,
            null,
            Detail: "Subscribed item is not a supported Desktop Buddy room or buddy share.");
    }

    public void OpenWorkshopBrowser() => _transport.OpenWorkshopBrowser();
    public void OpenWorkshopItem(ulong publishedFileId) => _transport.OpenWorkshopItem(publishedFileId);

    private async Task<WorkshopPublishResult> PublishStagedAsync(
        WorkshopPublishStaging staging,
        string title,
        string description,
        IReadOnlyList<string> tags,
        string metadata,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        string safeTitle = NormalizeTitle(title);
        string safeDescription = NormalizeDescription(description);
        WorkshopCreateRemoteResult created = await _transport.CreateItemAsync(token);
        if (!created.IsSuccess)
        {
            _staging.Cleanup(staging.OperationId);
            return FromRemote(created.Status, created.PublishedFileId, created.Detail);
        }

        bool hasPreview = File.Exists(staging.PreviewPath);
        var update = new WorkshopRemoteUpdate(
            created.PublishedFileId,
            safeTitle,
            safeDescription,
            staging.ContentRoot,
            hasPreview ? staging.PreviewPath : null,
            tags,
            metadata,
            WorkshopVisibility.Public);
        WorkshopSubmitRemoteResult submitted = await _transport.SubmitUpdateAsync(update, progress, token);

        // SubmitItemUpdate cannot be cancelled remotely. Preserve an operation snapshot if the
        // caller stops waiting so startup recovery/debugging can reconcile it instead of deleting
        // bytes Steam may still be consuming.
        if (submitted.Status != WorkshopRemoteStatus.Cancelled)
            _staging.Cleanup(staging.OperationId);

        if (!submitted.IsSuccess)
            return FromRemote(submitted.Status, created.PublishedFileId, submitted.Detail);

        bool needsAgreement = created.NeedsLegalAgreement || submitted.NeedsLegalAgreement;
        if (needsAgreement)
        {
            _transport.OpenWorkshopItem(created.PublishedFileId);
            return new WorkshopPublishResult(
                WorkshopPublishStatus.NeedsLegalAgreement,
                created.PublishedFileId,
                "Steam requires acceptance of the Workshop Legal Agreement before the item is fully published.");
        }

        return new WorkshopPublishResult(WorkshopPublishStatus.Published, created.PublishedFileId);
    }

    private static WorkshopPublishResult FromRemote(WorkshopRemoteStatus status, ulong id, string? detail) => new(
        status switch
        {
            WorkshopRemoteStatus.Unavailable => WorkshopPublishStatus.Unavailable,
            WorkshopRemoteStatus.Cancelled => WorkshopPublishStatus.Cancelled,
            _ => WorkshopPublishStatus.Failed,
        },
        id,
        detail);

    private static string? DetectContentType(string folder)
    {
        try
        {
            ShareFolderReadResult room = new ShareFolderReader().Read(folder, ShareContentType.RoomPainting);
            if (room.IsSuccess) return ShareContentTypes.RoomPainting;
            ShareFolderReadResult buddy = new ShareFolderReader().Read(folder, ShareContentType.BuddyCharacter);
            return buddy.IsSuccess ? ShareContentTypes.BuddyCharacter : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string NormalizeTitle(string value)
    {
        string result = Sanitize(value, 128);
        return result.Length == 0 ? "Desktop Buddy Creation" : result;
    }

    private static string NormalizeDescription(string value) => Sanitize(value, 8000);

    private static string Sanitize(string? value, int maxLength)
    {
        string result = string.Concat((value ?? string.Empty).Where(c => !char.IsControl(c) || c is '\n' or '\t')).Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }
}