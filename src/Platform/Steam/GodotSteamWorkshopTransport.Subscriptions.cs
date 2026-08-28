using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

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
            IReadOnlyList<PublishedWorkshopItem> items = await ReadSubscribedItemsOnMainThreadAsync(token);
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

    /// <summary>
    /// Raw GodotSteam enumeration exists only behind the typed interface result above. Keeping it
    /// private prevents an unavailable Steam client from being accidentally interpreted as a valid
    /// empty subscription set by application/UI callers.
    /// </summary>
    private Task<IReadOnlyList<PublishedWorkshopItem>> ReadSubscribedItemsOnMainThreadAsync(
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Variant raw = _bridge!.Call("get_subscribed_items");
        long[] ids = raw.VariantType == Variant.Type.PackedInt64Array ? raw.AsInt64Array() : [];
        var items = new List<PublishedWorkshopItem>(ids.Length);
        foreach (long rawId in ids)
        {
            token.ThrowIfCancellationRequested();
            if (rawId <= 0) continue;
            ulong id = checked((ulong)rawId);
            uint state = checked((uint)Math.Max(0, CallInt64("get_item_state", rawId)));
            long timestamp = 0;
            if (((WorkshopItemState)state & WorkshopItemState.Installed) != 0)
            {
                Godot.Collections.Dictionary install = CallDictionary("get_item_install_info", rawId);
                timestamp = checked((long)Math.Min(long.MaxValue, ReadUInt64(install, "timestamp", "time_stamp")));
            }
            items.Add(new PublishedWorkshopItem(id, (WorkshopItemState)state, $"Workshop Item {id}", timestamp));
        }
        return Task.FromResult<IReadOnlyList<PublishedWorkshopItem>>(items);
    }
}
