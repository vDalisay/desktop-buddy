using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport
{
    private PendingSubscriptionQuery? _pendingSubscriptionQuery;

    private sealed record PendingSubscriptionQuery(
        long Handle,
        IReadOnlyList<PublishedWorkshopItem> Items,
        TaskCompletionSource<IReadOnlyList<PublishedWorkshopItem>> Completion);

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

    async Task<WorkshopSubscriptionQueryResult> ISteamWorkshopTransport.GetItemDetailsAsync(
        IReadOnlyList<ulong> publishedFileIds,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return new WorkshopSubscriptionQueryResult(WorkshopRemoteStatus.Cancelled, [], "Workshop item query cancelled.");
        if (!IsAvailable)
            return new WorkshopSubscriptionQueryResult(WorkshopRemoteStatus.Unavailable, [], UnavailableReason ?? "Steam Workshop is unavailable.");
        if (!IsOnMainThread)
            return new WorkshopSubscriptionQueryResult(WorkshopRemoteStatus.Failed, [], "Steam Workshop items must be queried on the Godot main thread.");

        try
        {
            long[] ids = publishedFileIds
                .Where(id => id is > 0 and <= long.MaxValue)
                .Distinct()
                .Select(id => checked((long)id))
                .ToArray();
            return WorkshopSubscriptionQueryResult.Success(await ReadItemsOnMainThreadAsync(ids, token));
        }
        catch (OperationCanceledException)
        {
            return new WorkshopSubscriptionQueryResult(WorkshopRemoteStatus.Cancelled, [], "Workshop item query cancelled.");
        }
    }

    /// <summary>
    /// Raw GodotSteam enumeration exists only behind the typed interface result above. Keeping it
    /// private prevents an unavailable Steam client from being accidentally interpreted as a valid
    /// empty subscription set by application/UI callers.
    /// </summary>
    private async Task<IReadOnlyList<PublishedWorkshopItem>> ReadSubscribedItemsOnMainThreadAsync(
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Variant raw = _bridge!.Call("get_subscribed_items");
        long[] ids = raw.VariantType == Variant.Type.PackedInt64Array ? raw.AsInt64Array() : [];
        return await ReadItemsOnMainThreadAsync(ids, token);
    }

    private async Task<IReadOnlyList<PublishedWorkshopItem>> ReadItemsOnMainThreadAsync(
        long[] ids,
        CancellationToken token)
    {
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
        if (items.Count == 0)
            return items;
        if (_pendingSubscriptionQuery is not null)
            return await WaitAsync(_pendingSubscriptionQuery.Completion.Task, token);

        long handle = CallInt64("query_item_details", ids);
        if (handle < 0)
            return items;

        var pending = new PendingSubscriptionQuery(
            handle,
            items,
            NewCompletion<IReadOnlyList<PublishedWorkshopItem>>());
        _pendingSubscriptionQuery = pending;
        return await WaitAsync(pending.Completion.Task, token);
    }

    private void OnQueryCompleted(long handle, long result, long resultsReturned)
    {
        PendingSubscriptionQuery? pending = _pendingSubscriptionQuery;
        if (pending is null || pending.Handle != handle)
            return;
        _pendingSubscriptionQuery = null;

        try
        {
            if (result != SteamResultOk)
            {
                pending.Completion.TrySetResult(pending.Items);
                return;
            }

            var byId = new Dictionary<ulong, PublishedWorkshopItem>();
            foreach (PublishedWorkshopItem item in pending.Items)
                byId[item.PublishedFileId] = item;

            for (int index = 0; index < resultsReturned; index++)
            {
                Godot.Collections.Dictionary details = CallDictionary("get_query_item_result", handle, index);
                ulong id = ReadUInt64(details, "file_id", "published_file_id");
                if (id == 0 || !byId.TryGetValue(id, out PublishedWorkshopItem? item))
                    continue;
                string title = NormalizeRemoteText(ReadString(details, "title"), 128, allowLines: false);
                string description = NormalizeRemoteText(ReadString(details, "description"), 8000, allowLines: true);
                byId[id] = item with
                {
                    DisplayName = title.Length == 0 ? item.DisplayName : title,
                    Description = description,
                };
            }

            pending.Completion.TrySetResult(pending.Items.Select(item => byId[item.PublishedFileId]).ToArray());
        }
        catch (Exception exception)
        {
            Log.Warn("Workshop", $"Steam subscription metadata could not be read: {exception.Message}");
            pending.Completion.TrySetResult(pending.Items);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(_bridge))
                _bridge!.Call("release_query", handle);
        }
    }

    private void ShutdownSubscriptionQuery()
    {
        PendingSubscriptionQuery? pending = _pendingSubscriptionQuery;
        if (pending is null)
            return;
        _pendingSubscriptionQuery = null;
        if (GodotObject.IsInstanceValid(_bridge))
            _bridge!.Call("release_query", pending.Handle);
        pending.Completion.TrySetResult(pending.Items);
    }

    private static string NormalizeRemoteText(string? value, int maxLength, bool allowLines)
    {
        string safe = string.Concat((value ?? string.Empty)
            .Where(c => !char.IsControl(c) || (allowLines && c is '\n' or '\t'))).Trim();
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }
}
