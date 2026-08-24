using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

public sealed class NullSteamWorkshopTransport : ISteamWorkshopTransport, ISteamAvailability
{
    public NullSteamWorkshopTransport(string? reason = null) =>
        UnavailableReason = string.IsNullOrWhiteSpace(reason) ? "Steam Workshop is unavailable." : reason;

    public bool IsAvailable => false;
    public bool IsInstalled => false;
    public bool IsInitialized => false;
    public string? UnavailableReason { get; }

    public Task<WorkshopCreateRemoteResult> CreateItemAsync(CancellationToken token) =>
        Task.FromResult(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Unavailable, 0, false, Detail: UnavailableReason));

    public Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token) =>
        Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Unavailable, update.PublishedFileId, false, Detail: UnavailableReason));

    public Task<IReadOnlyList<PublishedWorkshopItem>> GetSubscribedItemsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<PublishedWorkshopItem>>(Array.Empty<PublishedWorkshopItem>());

    public Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token) =>
        Task.FromResult(new WorkshopInstalledItemResult(
            WorkshopRemoteStatus.Unavailable,
            publishedFileId,
            null,
            0,
            Detail: UnavailableReason));

    public void OpenWorkshopBrowser() { }
    public void OpenWorkshopItem(ulong publishedFileId) { }
}
