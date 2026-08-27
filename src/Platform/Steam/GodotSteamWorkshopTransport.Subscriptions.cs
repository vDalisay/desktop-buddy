using System;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport
{
    async Task<WorkshopSubscriptionQueryResult> ISteamWorkshopTransport.GetSubscribedItemsAsync(
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return new WorkshopSubscriptionQueryResult(
                WorkshopRemoteStatus.Cancelled,
                Array.Empty<PublishedWorkshopItem>(),
                "Workshop subscription query cancelled.");
        }
        if (!IsAvailable)
        {
            return new WorkshopSubscriptionQueryResult(
                WorkshopRemoteStatus.Unavailable,
                Array.Empty<PublishedWorkshopItem>(),
                UnavailableReason ?? "Steam Workshop is unavailable.");
        }
        if (!IsOnMainThread)
        {
            return new WorkshopSubscriptionQueryResult(
                WorkshopRemoteStatus.Failed,
                Array.Empty<PublishedWorkshopItem>(),
                "Steam Workshop subscriptions must be queried on the Godot main thread.");
        }

        try
        {
            var items = await GetSubscribedItemsAsync(token);
            return WorkshopSubscriptionQueryResult.Success(items);
        }
        catch (OperationCanceledException)
        {
            return new WorkshopSubscriptionQueryResult(
                WorkshopRemoteStatus.Cancelled,
                Array.Empty<PublishedWorkshopItem>(),
                "Workshop subscription query cancelled.");
        }
    }
}
