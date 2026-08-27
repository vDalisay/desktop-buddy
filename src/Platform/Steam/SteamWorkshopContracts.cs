using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

public interface ISteamAvailability
{
    bool IsInstalled { get; }
    bool IsInitialized { get; }
    string? UnavailableReason { get; }
}

public enum WorkshopRemoteStatus
{
    Success,
    Unavailable,
    Failed,
    Cancelled,
    Unsupported,
}

public readonly record struct WorkshopCreateRemoteResult(
    WorkshopRemoteStatus Status,
    ulong PublishedFileId,
    bool NeedsLegalAgreement,
    int NativeResult = 0,
    string? Detail = null)
{
    public bool IsSuccess => Status == WorkshopRemoteStatus.Success && PublishedFileId != 0;
}

public readonly record struct WorkshopSubmitRemoteResult(
    WorkshopRemoteStatus Status,
    ulong PublishedFileId,
    bool NeedsLegalAgreement,
    int NativeResult = 0,
    string? Detail = null)
{
    public bool IsSuccess => Status == WorkshopRemoteStatus.Success;
}

public readonly record struct WorkshopTransferProgress(
    ulong BytesProcessed,
    ulong BytesTotal,
    string Stage)
{
    public double Fraction => BytesTotal == 0 ? 0.0 : Math.Clamp((double)BytesProcessed / BytesTotal, 0.0, 1.0);
}

public sealed record WorkshopRemoteUpdate(
    ulong PublishedFileId,
    string Title,
    string Description,
    string ContentFolder,
    string? PreviewFile,
    IReadOnlyList<string> Tags,
    string Metadata,
    WorkshopVisibility Visibility = WorkshopVisibility.Public,
    string ChangeNote = "Published from Desktop Buddy");

public enum WorkshopVisibility
{
    Public = 0,
    FriendsOnly = 1,
    Private = 2,
    Unlisted = 3,
}

[Flags]
public enum WorkshopItemState : uint
{
    None = 0,
    Subscribed = 1,
    LegacyItem = 2,
    Installed = 4,
    NeedsUpdate = 8,
    Downloading = 16,
    DownloadPending = 32,
}

public sealed record PublishedWorkshopItem(
    ulong PublishedFileId,
    WorkshopItemState State,
    string DisplayName,
    long TimeUpdated = 0,
    string? ContentType = null);

public readonly record struct WorkshopSubscriptionQueryResult(
    WorkshopRemoteStatus Status,
    IReadOnlyList<PublishedWorkshopItem> Items,
    string? Detail = null)
{
    public bool IsSuccess => Status == WorkshopRemoteStatus.Success;

    public static WorkshopSubscriptionQueryResult Success(IReadOnlyList<PublishedWorkshopItem> items) =>
        new(WorkshopRemoteStatus.Success, items ?? Array.Empty<PublishedWorkshopItem>());
}

public readonly record struct WorkshopInstalledItemResult(
    WorkshopRemoteStatus Status,
    ulong PublishedFileId,
    string? InstallFolder,
    long TimeUpdated,
    int NativeResult = 0,
    string? Detail = null)
{
    public bool IsSuccess => Status == WorkshopRemoteStatus.Success && !string.IsNullOrWhiteSpace(InstallFolder);
}

public interface ISteamWorkshopTransport : ISteamAvailability
{
    bool IsAvailable { get; }

    Task<WorkshopCreateRemoteResult> CreateItemAsync(CancellationToken token);

    Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token);

    Task<WorkshopSubscriptionQueryResult> GetSubscribedItemsAsync(CancellationToken token);

    Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token);

    void OpenWorkshopBrowser();
    void OpenWorkshopItem(ulong publishedFileId);
}
