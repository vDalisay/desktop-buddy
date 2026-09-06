using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Sharing;
using Godot;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport : IWorkshopDiscoveryTransport
{
    private const string DiscoveryScriptPath = "res://src/Platform/Steam/GodotSteamWorkshopDiscovery.gd";
    private const int SubscribeConfirmationFrames = 300;

    private Node? _discoveryBridge;
    private string? _discoveryUnavailableReason;
    private PendingBrowseQuery? _pendingBrowseQuery;

    private sealed record PendingBrowseQuery(
        long Handle,
        uint Page,
        TaskCompletionSource<WorkshopBrowsePage> Completion);

    public bool IsDiscoveryAvailable => IsAvailable && EnsureDiscoveryBridge() is not null;
    public string? DiscoveryUnavailableReason => _discoveryUnavailableReason ?? UnavailableReason;

    public async Task<WorkshopBrowsePage> BrowseAsync(
        WorkshopBrowseSort sort,
        uint page,
        CancellationToken token)
    {
        uint safePage = Math.Max(1u, page);
        if (token.IsCancellationRequested)
            return new WorkshopBrowsePage(WorkshopRemoteStatus.Cancelled, [], safePage, 0, "Workshop browse cancelled.");
        if (!IsAvailable)
            return new WorkshopBrowsePage(WorkshopRemoteStatus.Unavailable, [], safePage, 0, UnavailableReason);
        if (!IsOnMainThread)
            return new WorkshopBrowsePage(WorkshopRemoteStatus.Failed, [], safePage, 0, "Workshop discovery must start on the Godot main thread.");

        Node? discovery = EnsureDiscoveryBridge();
        if (discovery is null)
            return new WorkshopBrowsePage(WorkshopRemoteStatus.Unsupported, [], safePage, 0, DiscoveryUnavailableReason);
        if (_pendingBrowseQuery is not null)
            return new WorkshopBrowsePage(WorkshopRemoteStatus.Failed, [], safePage, 0, "Another Workshop browse query is already pending.");

        long handle = discovery.Call("query_page", (int)sort, checked((int)safePage)).AsInt64();
        if (handle < 0)
            return new WorkshopBrowsePage(WorkshopRemoteStatus.Failed, [], safePage, 0, "Steam could not start the Workshop browse query.");

        var pending = new PendingBrowseQuery(handle, safePage, NewCompletion<WorkshopBrowsePage>());
        _pendingBrowseQuery = pending;
        try
        {
            return await WaitAsync(pending.Completion.Task, token);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_pendingBrowseQuery, pending))
            {
                _pendingBrowseQuery = null;
                discovery.Call("release_query", handle);
            }
            return new WorkshopBrowsePage(WorkshopRemoteStatus.Cancelled, [], safePage, 0, "Workshop browse cancelled.");
        }
    }

    public async Task<WorkshopSubscriptionChangeResult> SubscribeAsync(
        ulong publishedFileId,
        CancellationToken token)
    {
        if (!IsAvailable)
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Unavailable, publishedFileId, Detail: UnavailableReason);
        if (!IsOnMainThread)
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Failed, publishedFileId, Detail: "Steam Workshop subscribe must start on the Godot main thread.");
        if (publishedFileId == 0)
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Failed, 0, Detail: "Published file ID is required.");
        if (token.IsCancellationRequested)
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Cancelled, publishedFileId, Detail: "Workshop subscribe cancelled before it started.");

        Node? discovery = EnsureDiscoveryBridge();
        if (discovery is null)
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Unsupported, publishedFileId, Detail: DiscoveryUnavailableReason);

        long rawId = checked((long)publishedFileId);
        WorkshopItemState initial = ReadDiscoveryState(discovery, rawId);
        if ((initial & WorkshopItemState.Subscribed) != 0)
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Success, publishedFileId);

        if (!discovery.Call("subscribe_item", rawId).AsBool())
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Failed, publishedFileId, Detail: "Steam could not start the Workshop subscribe operation.");

        for (int frame = 0; frame < SubscribeConfirmationFrames; frame++)
        {
            if (token.IsCancellationRequested)
                return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Cancelled, publishedFileId, Detail: "Stopped waiting for Steam to confirm the subscription. Steam may still complete it.");

            WorkshopItemState state = ReadDiscoveryState(discovery, rawId);
            if ((state & WorkshopItemState.Subscribed) != 0)
                return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Success, publishedFileId);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return new WorkshopSubscriptionChangeResult(
            WorkshopRemoteStatus.Failed,
            publishedFileId,
            Detail: "Steam did not confirm the Workshop subscription in time. Refresh Browse Content to check its final state.");
    }

    private Node? EnsureDiscoveryBridge()
    {
        if (GodotObject.IsInstanceValid(_discoveryBridge))
            return _discoveryBridge;
        if (!IsAvailable || _runtimeAppId == 0)
            return null;

        try
        {
            GDScript? script = GD.Load<GDScript>(DiscoveryScriptPath);
            if (script is null)
            {
                _discoveryUnavailableReason = "The Workshop discovery bridge could not be loaded.";
                return null;
            }

            GodotObject instance = (GodotObject)script.New();
            if (instance is not Node node)
            {
                _discoveryUnavailableReason = "The Workshop discovery bridge did not instantiate as a Node.";
                return null;
            }

            node.Name = "GodotSteamWorkshopDiscovery";
            AddChild(node);
            Variant configured = node.Call("configure", (long)_runtimeAppId);
            if (configured.VariantType != Variant.Type.Dictionary ||
                !configured.AsGodotDictionary().TryGetValue("ok", out Variant ok) ||
                !ok.AsBool())
            {
                _discoveryUnavailableReason = node.Call("unavailable_reason").AsString();
                node.QueueFree();
                return null;
            }

            node.Connect(
                "browse_query_completed",
                Callable.From<long, long, long, long>(OnBrowseQueryCompleted));
            _discoveryBridge = node;
            _discoveryUnavailableReason = null;
            return node;
        }
        catch (Exception exception)
        {
            _discoveryUnavailableReason = $"Workshop discovery could not initialize: {exception.Message}";
            Log.Warn("Workshop", _discoveryUnavailableReason);
            return null;
        }
    }

    private void OnBrowseQueryCompleted(long handle, long result, long resultsReturned, long totalMatching)
    {
        PendingBrowseQuery? pending = _pendingBrowseQuery;
        Node? discovery = _discoveryBridge;
        if (pending is null || pending.Handle != handle || !GodotObject.IsInstanceValid(discovery))
            return;
        _pendingBrowseQuery = null;

        try
        {
            if (result != SteamResultOk)
            {
                pending.Completion.TrySetResult(new WorkshopBrowsePage(
                    WorkshopRemoteStatus.Failed,
                    [],
                    pending.Page,
                    0,
                    $"Steam Workshop browse query failed with EResult {result}."));
                return;
            }

            var items = new List<WorkshopBrowseItem>(checked((int)Math.Max(0, resultsReturned)));
            for (int index = 0; index < resultsReturned; index++)
            {
                Godot.Collections.Dictionary details = DiscoveryDictionary(discovery!, "get_query_item_result", handle, index);
                WorkshopBrowseItem? parsed = ParseBrowseItem(discovery!, details);
                if (parsed is not null)
                    items.Add(parsed);
            }

            pending.Completion.TrySetResult(WorkshopBrowsePage.Success(
                items,
                pending.Page,
                checked((uint)Math.Clamp(totalMatching, 0L, uint.MaxValue))));
        }
        catch (Exception exception)
        {
            Log.Warn("Workshop", $"Steam browse results could not be read: {exception.Message}");
            pending.Completion.TrySetResult(new WorkshopBrowsePage(
                WorkshopRemoteStatus.Failed,
                [],
                pending.Page,
                0,
                "Steam returned Workshop results that Desktop Buddy could not read."));
        }
        finally
        {
            discovery!.Call("release_query", handle);
        }
    }

    private WorkshopBrowseItem? ParseBrowseItem(Node discovery, Godot.Collections.Dictionary details)
    {
        ulong id = ReadUInt64(details, "file_id", "published_file_id");
        if (id == 0)
            return null;

        ulong rawConsumerAppId = ReadUInt64(details, "consumer_app_id", "consumer_appid", "consumer_id");
        uint consumerAppId = rawConsumerAppId is > 0 and <= uint.MaxValue
            ? checked((uint)rawConsumerAppId)
            : 0;

        // This is the same defense as the subscription/download path. A Demo browser must never
        // surface a full-game-only item even if Steam returns one because the account touched the
        // full game's web Workshop previously.
        if (consumerAppId != 0 && consumerAppId != _runtimeAppId)
            return null;

        string title = NormalizeRemoteText(ReadString(details, "title"), 128, allowLines: false);
        string description = NormalizeRemoteText(ReadString(details, "description"), 8000, allowLines: true);
        string previewUrl = NormalizePreviewUrl(ReadString(details, "preview_url"));
        string? contentType = DetectContentType(details);
        long rawUpdated = checked((long)Math.Min(long.MaxValue, ReadUInt64(details, "time_updated", "updated", "time_created")));
        WorkshopItemState state = ReadDiscoveryState(discovery, checked((long)id));

        var item = new PublishedWorkshopItem(
            id,
            state,
            title.Length == 0 ? $"Workshop Item {id}" : title,
            rawUpdated,
            contentType,
            description,
            consumerAppId == 0 ? _runtimeAppId : consumerAppId);

        ulong owner = ReadUInt64(details, "owner_id", "steam_id_owner", "owner");
        uint votesUp = checked((uint)Math.Min(uint.MaxValue, ReadUInt64(details, "votes_up", "votesup")));
        uint votesDown = checked((uint)Math.Min(uint.MaxValue, ReadUInt64(details, "votes_down", "votesdown")));
        float score = ReadFloat(details, "score", "star_rating");
        return new WorkshopBrowseItem(item, previewUrl, owner, votesUp, votesDown, score);
    }

    private static WorkshopItemState ReadDiscoveryState(Node discovery, long rawId) =>
        (WorkshopItemState)checked((uint)Math.Max(0, discovery.Call("get_item_state", rawId).AsInt64()));

    private static Godot.Collections.Dictionary DiscoveryDictionary(Node discovery, string method, params Variant[] args)
    {
        Variant result = discovery.Call(method, args);
        return result.VariantType == Variant.Type.Dictionary
            ? result.AsGodotDictionary()
            : new Godot.Collections.Dictionary();
    }

    private static float ReadFloat(Godot.Collections.Dictionary dictionary, params string[] keys)
    {
        foreach (string key in keys)
            if (dictionary.TryGetValue(key, out Variant value))
                return value.VariantType is Variant.Type.Float or Variant.Type.Int
                    ? (float)value.AsDouble()
                    : 0.0f;
        return 0.0f;
    }

    private static string NormalizePreviewUrl(string? value)
    {
        string candidate = (value ?? string.Empty).Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
            return string.Empty;
        return uri.Scheme is "https" or "http" ? uri.AbsoluteUri : string.Empty;
    }

    private static string? DetectContentType(Godot.Collections.Dictionary details)
    {
        if (!details.TryGetValue("tags", out Variant raw))
            return null;

        IEnumerable<string> tags = raw.VariantType switch
        {
            Variant.Type.PackedStringArray => raw.AsStringArray(),
            Variant.Type.Array => raw.AsGodotArray().Select(value => value.AsString()),
            Variant.Type.String => raw.AsString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => Array.Empty<string>(),
        };

        foreach (string tag in tags)
        {
            if (string.Equals(tag, "Buddy", StringComparison.OrdinalIgnoreCase))
                return ShareContentTypes.BuddyCharacter;
            if (string.Equals(tag, "Room Painting", StringComparison.OrdinalIgnoreCase))
                return ShareContentTypes.RoomPainting;
        }
        return null;
    }
}
