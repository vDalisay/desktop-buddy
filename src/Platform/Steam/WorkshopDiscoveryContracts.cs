using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

/// <summary>Sort modes intentionally kept small so the in-game browser mirrors the useful Steam Workshop views.</summary>
public enum WorkshopBrowseSort
{
    MostPopular,
    MostRecent,
}

/// <summary>One public Workshop result plus the web-facing metadata used by the in-game browser.</summary>
public sealed record WorkshopBrowseItem(
    PublishedWorkshopItem Item,
    string PreviewUrl = "",
    ulong OwnerSteamId = 0,
    uint VotesUp = 0,
    uint VotesDown = 0,
    float Score = 0.0f);

public readonly record struct WorkshopBrowsePage(
    WorkshopRemoteStatus Status,
    IReadOnlyList<WorkshopBrowseItem> Items,
    uint Page,
    uint TotalMatching,
    string? Detail = null)
{
    public bool IsSuccess => Status == WorkshopRemoteStatus.Success;

    public static WorkshopBrowsePage Success(
        IReadOnlyList<WorkshopBrowseItem> items,
        uint page,
        uint totalMatching) =>
        new(WorkshopRemoteStatus.Success, items ?? Array.Empty<WorkshopBrowseItem>(), page, totalMatching);
}

/// <summary>
/// Optional live-Steam discovery surface. Publishing/importing remains on ISteamWorkshopTransport;
/// this capability is only required by the in-game community browser.
/// </summary>
public interface IWorkshopDiscoveryTransport
{
    bool IsDiscoveryAvailable { get; }
    string? DiscoveryUnavailableReason { get; }

    Task<WorkshopBrowsePage> BrowseAsync(
        WorkshopBrowseSort sort,
        uint page,
        CancellationToken token);

    Task<WorkshopSubscriptionChangeResult> SubscribeAsync(
        ulong publishedFileId,
        CancellationToken token);
}
