using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// Main-thread adapter from the project-owned dynamic GDScript bridge to typed C# operations.
/// GodotSteam objects and Variants terminate here; sharing/persistence code only sees the narrow
/// transport contracts in <see cref="ISteamWorkshopTransport"/>.
/// </summary>
public partial class GodotSteamWorkshopTransport : Node, ISteamWorkshopTransport, ISteamAvailability
{
    private const int SteamResultOk = 1;
    private readonly object _callbackGate = new();
    private readonly Dictionary<ulong, PendingDownload> _downloads = new();
    private Node? _bridge;
    private uint _appId;
    private int _mainThreadId;
    private TaskCompletionSource<CreateCallback>? _pendingCreate;
    private TaskCompletionSource<UpdateCallback>? _pendingUpdate;
    private long _activeUpdateHandle;
    private IProgress<WorkshopTransferProgress>? _activeUploadProgress;
    private bool _signalsConnected;

    private readonly record struct CreateCallback(int Result, ulong FileId, bool NeedsAgreement);
    private readonly record struct UpdateCallback(int Result, bool NeedsAgreement);

    private sealed class PendingDownload
    {
        public required ulong PublishedFileId { get; init; }
        public required TaskCompletionSource<WorkshopInstalledItemResult> Completion { get; init; }
        public IProgress<WorkshopTransferProgress>? Progress { get; init; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }

    public bool IsAvailable => IsInitialized && GodotObject.IsInstanceValid(_bridge);
    public bool IsInstalled => GodotObject.IsInstanceValid(_bridge);
    public bool IsInitialized { get; private set; }
    public string? UnavailableReason { get; private set; } = "Steam Workshop has not been initialized.";
    public uint AppId => _appId;

    public override void _Ready()
    {
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!IsAvailable) return;

        if (_activeUpdateHandle > 0 && _activeUploadProgress is not null)
        {
            Godot.Collections.Dictionary info = CallDictionary("get_item_update_progress", _activeUpdateHandle);
            ulong processed = ReadUInt64(info, "processed", "bytes_processed", "current");
            ulong total = ReadUInt64(info, "total", "bytes_total");
            if (total > 0)
                _activeUploadProgress.Report(new WorkshopTransferProgress(processed, total, "Uploading"));
        }

        PendingDownload[] downloads;
        lock (_callbackGate) downloads = _downloads.Values.ToArray();
        foreach (PendingDownload pending in downloads)
        {
            Godot.Collections.Dictionary info = CallDictionary("get_item_download_info", checked((long)pending.PublishedFileId));
            ulong current = ReadUInt64(info, "downloaded", "bytes_downloaded", "current");
            ulong total = ReadUInt64(info, "total", "bytes_total");
            if (total > 0)
                pending.Progress?.Report(new WorkshopTransferProgress(current, total, "Downloading"));
        }
    }

    public bool Initialize(Node bridge, uint appId)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        if (appId == 0)
        {
            SetUnavailable("No Steam AppID is configured.");
            return false;
        }

        _bridge = bridge;
        _appId = appId;
        ConnectSignals();
        Godot.Collections.Dictionary result = CallDictionary("initialize", (long)appId);
        int status = ReadInt32(result, "status", fallback: -1);
        if (status != 0)
        {
            SetUnavailable(ReadString(result, "verbal") ?? "Steam initialization failed.");
            return false;
        }

        IsInitialized = true;
        UnavailableReason = null;
        return true;
    }

    public Task<WorkshopCreateRemoteResult> CreateItemAsync(CancellationToken token)
    {
        if (!IsAvailable)
            return Task.FromResult(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Unavailable, 0, false, Detail: UnavailableReason));
        if (!IsOnMainThread)
            return Task.FromResult(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "Steam Workshop create must start on the Godot main thread."));
        if (token.IsCancellationRequested)
            return Task.FromResult(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Cancelled, 0, false));

        TaskCompletionSource<CreateCallback> completion = NewCompletion<CreateCallback>();
        lock (_callbackGate)
        {
            if (_pendingCreate is not null || _pendingUpdate is not null)
                return Task.FromResult(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "Another Workshop publish operation is pending."));
            _pendingCreate = completion;
        }

        if (!CallBool("create_item", (long)_appId))
        {
            lock (_callbackGate)
                if (ReferenceEquals(_pendingCreate, completion)) _pendingCreate = null;
            return Task.FromResult(new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "GodotSteam rejected CreateItem."));
        }

        return AwaitCreateAsync(completion, token);
    }

    public Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        if (!IsAvailable)
            return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Unavailable, update.PublishedFileId, false, Detail: UnavailableReason));
        if (!IsOnMainThread)
            return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "Steam Workshop update must start on the Godot main thread."));
        if (update.PublishedFileId == 0)
            return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "Published file ID is required."));
        if (token.IsCancellationRequested)
            return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Cancelled, update.PublishedFileId, false));

        lock (_callbackGate)
        {
            if (_pendingCreate is not null || _pendingUpdate is not null)
                return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "Another Workshop publish operation is pending."));
        }

        long handle = CallInt64("start_item_update", (long)_appId, checked((long)update.PublishedFileId));
        if (handle <= 0)
            return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "Steam returned an invalid UGC update handle."));

        if (!CallBool("set_item_title", handle, update.Title) ||
            !CallBool("set_item_description", handle, update.Description) ||
            !CallBool("set_item_visibility", handle, (int)update.Visibility) ||
            !CallBool("set_item_tags", handle, update.Tags.ToArray()) ||
            !CallBool("set_item_metadata", handle, update.Metadata) ||
            !CallBool("set_item_content", handle, update.ContentFolder) ||
            (!string.IsNullOrWhiteSpace(update.PreviewFile) && !CallBool("set_item_preview", handle, update.PreviewFile!)))
        {
            return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "Steam rejected one or more Workshop update fields."));
        }

        TaskCompletionSource<UpdateCallback> completion = NewCompletion<UpdateCallback>();
        lock (_callbackGate)
        {
            _pendingUpdate = completion;
            _activeUpdateHandle = handle;
            _activeUploadProgress = progress;
        }

        if (!CallBool("submit_item_update", handle, update.ChangeNote))
        {
            lock (_callbackGate)
            {
                if (ReferenceEquals(_pendingUpdate, completion)) _pendingUpdate = null;
                _activeUpdateHandle = 0;
                _activeUploadProgress = null;
            }
            return Task.FromResult(new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "GodotSteam rejected SubmitItemUpdate."));
        }

        return AwaitUpdateAsync(completion, update.PublishedFileId, token);
    }

    public Task<IReadOnlyList<PublishedWorkshopItem>> GetSubscribedItemsAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<PublishedWorkshopItem>>(token);
        if (!IsAvailable || !IsOnMainThread)
            return Task.FromResult<IReadOnlyList<PublishedWorkshopItem>>(Array.Empty<PublishedWorkshopItem>());

        Variant raw = _bridge!.Call("get_subscribed_items");
        long[] ids = raw.VariantType == Variant.Type.PackedInt64Array ? raw.AsInt64Array() : [];
        var items = new List<PublishedWorkshopItem>(ids.Length);
        foreach (long rawId in ids)
        {
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

    public Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        if (!IsAvailable)
            return Task.FromResult(new WorkshopInstalledItemResult(WorkshopRemoteStatus.Unavailable, publishedFileId, null, 0, Detail: UnavailableReason));
        if (!IsOnMainThread)
            return Task.FromResult(new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, publishedFileId, null, 0, Detail: "Steam Workshop download must start on the Godot main thread."));
        if (publishedFileId == 0)
            return Task.FromResult(new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, 0, null, 0, Detail: "Published file ID is required."));
        if (token.IsCancellationRequested)
            return Task.FromResult(new WorkshopInstalledItemResult(WorkshopRemoteStatus.Cancelled, publishedFileId, null, 0));

        long rawId = checked((long)publishedFileId);
        WorkshopItemState state = (WorkshopItemState)checked((uint)Math.Max(0, CallInt64("get_item_state", rawId)));
        if ((state & WorkshopItemState.Installed) != 0 && (state & WorkshopItemState.NeedsUpdate) == 0)
            return Task.FromResult(InstalledInfo(publishedFileId));

        var pending = new PendingDownload
        {
            PublishedFileId = publishedFileId,
            Completion = NewCompletion<WorkshopInstalledItemResult>(),
            Progress = progress,
        };
        lock (_callbackGate)
        {
            if (_downloads.ContainsKey(publishedFileId))
                return Task.FromResult(new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, publishedFileId, null, 0, Detail: "A download for this Workshop item is already pending."));
            _downloads.Add(publishedFileId, pending);
        }

        if (token.CanBeCanceled)
        {
            pending.CancellationRegistration = token.Register(() => CancelDownload(pending));
            if (pending.Completion.Task.IsCompleted)
                return pending.Completion.Task;
        }

        if (!CallBool("download_item", rawId, false))
        {
            RemoveDownload(pending);
            pending.Completion.TrySetResult(new WorkshopInstalledItemResult(
                WorkshopRemoteStatus.Failed,
                publishedFileId,
                null,
                0,
                Detail: "Steam could not start the Workshop download."));
        }
        return pending.Completion.Task;
    }

    public void OpenWorkshopBrowser()
    {
        if (IsAvailable && IsOnMainThread) _bridge!.Call("open_workshop_browser", (long)_appId);
    }

    public void OpenWorkshopItem(ulong publishedFileId)
    {
        if (IsAvailable && IsOnMainThread && publishedFileId != 0)
            _bridge!.Call("open_workshop_item", checked((long)publishedFileId));
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_bridge) && IsOnMainThread) _bridge!.Call("shutdown");
        IsInitialized = false;
        base._ExitTree();
    }

    private async Task<WorkshopCreateRemoteResult> AwaitCreateAsync(
        TaskCompletionSource<CreateCallback> completion,
        CancellationToken token)
    {
        try
        {
            CreateCallback callback = await WaitAsync(completion.Task, token);
            return callback.Result == SteamResultOk
                ? new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Success, callback.FileId, callback.NeedsAgreement, callback.Result)
                : new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Failed, callback.FileId, callback.NeedsAgreement, callback.Result, $"Steam CreateItem failed with EResult {callback.Result}.");
        }
        catch (OperationCanceledException)
        {
            // Steam has no cancellation for CreateItem. The callback remains registered and will
            // release the publish lane when Steam eventually answers; a later create cannot race it.
            return new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Cancelled, 0, false, Detail: "Stopped waiting for Workshop item creation; Steam may still complete it.");
        }
    }

    private async Task<WorkshopSubmitRemoteResult> AwaitUpdateAsync(
        TaskCompletionSource<UpdateCallback> completion,
        ulong publishedFileId,
        CancellationToken token)
    {
        try
        {
            UpdateCallback callback = await WaitAsync(completion.Task, token);
            return callback.Result == SteamResultOk
                ? new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Success, publishedFileId, callback.NeedsAgreement, callback.Result)
                : new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, publishedFileId, callback.NeedsAgreement, callback.Result, $"Steam SubmitItemUpdate failed with EResult {callback.Result}.");
        }
        catch (OperationCanceledException)
        {
            // SubmitItemUpdate cannot be cancelled remotely. Do not clear _pendingUpdate here;
            // the Steam callback owns cleanup so another update cannot consume the wrong callback.
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Cancelled, publishedFileId, false, Detail: "Stopped waiting for Workshop upload; Steam may still finish it.");
        }
    }

    private WorkshopInstalledItemResult InstalledInfo(ulong publishedFileId)
    {
        Godot.Collections.Dictionary info = CallDictionary("get_item_install_info", checked((long)publishedFileId));
        bool ret = ReadBool(info, "ret", "success", fallback: info.Count > 0);
        string? folder = ReadString(info, "folder", "path");
        long timestamp = checked((long)Math.Min(long.MaxValue, ReadUInt64(info, "timestamp", "time_stamp")));
        if (!ret || string.IsNullOrWhiteSpace(folder))
            return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, publishedFileId, null, timestamp, Detail: "Steam reports the item is not installed.");
        return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Success, publishedFileId, folder, timestamp);
    }

    private void ConnectSignals()
    {
        if (_signalsConnected || !GodotObject.IsInstanceValid(_bridge)) return;
        _bridge!.Connect("workshop_item_created", Callable.From<long, long, bool>(OnItemCreated));
        _bridge.Connect("workshop_item_updated", Callable.From<long, bool>(OnItemUpdated));
        _bridge.Connect("workshop_item_downloaded", Callable.From<long, long, long>(OnItemDownloaded));
        _signalsConnected = true;
    }

    private void OnItemCreated(long result, long fileId, bool needsAgreement)
    {
        TaskCompletionSource<CreateCallback>? pending;
        lock (_callbackGate)
        {
            pending = _pendingCreate;
            _pendingCreate = null;
        }
        pending?.TrySetResult(new CreateCallback(
            checked((int)result),
            fileId <= 0 ? 0UL : checked((ulong)fileId),
            needsAgreement));
    }

    private void OnItemUpdated(long result, bool needsAgreement)
    {
        TaskCompletionSource<UpdateCallback>? pending;
        IProgress<WorkshopTransferProgress>? progress;
        lock (_callbackGate)
        {
            pending = _pendingUpdate;
            progress = _activeUploadProgress;
            _pendingUpdate = null;
            _activeUpdateHandle = 0;
            _activeUploadProgress = null;
        }
        if (result == SteamResultOk)
            progress?.Report(new WorkshopTransferProgress(1, 1, "Complete"));
        pending?.TrySetResult(new UpdateCallback(checked((int)result), needsAgreement));
    }

    private void OnItemDownloaded(long result, long appId, long fileId)
    {
        if (fileId <= 0) return;
        ulong publishedFileId = checked((ulong)fileId);
        PendingDownload? pending;
        lock (_callbackGate)
        {
            if (!_downloads.TryGetValue(publishedFileId, out pending)) return;
            _downloads.Remove(publishedFileId);
        }
        pending.CancellationRegistration.Dispose();

        WorkshopInstalledItemResult final;
        if (appId < 0 || checked((uint)appId) != _appId)
        {
            final = new WorkshopInstalledItemResult(
                WorkshopRemoteStatus.Failed,
                publishedFileId,
                null,
                0,
                checked((int)result),
                "Steam returned a mismatched AppID for the Workshop download.");
        }
        else if (result != SteamResultOk)
        {
            final = new WorkshopInstalledItemResult(
                WorkshopRemoteStatus.Failed,
                publishedFileId,
                null,
                0,
                checked((int)result),
                $"Steam Workshop download failed with EResult {result}.");
        }
        else
        {
            final = InstalledInfo(publishedFileId);
            if (final.IsSuccess)
                pending.Progress?.Report(new WorkshopTransferProgress(1, 1, "Installed"));
        }
        pending.Completion.TrySetResult(final);
    }

    private void CancelDownload(PendingDownload pending)
    {
        bool removed;
        lock (_callbackGate)
        {
            removed = _downloads.TryGetValue(pending.PublishedFileId, out PendingDownload? current) &&
                ReferenceEquals(current, pending);
            if (removed) _downloads.Remove(pending.PublishedFileId);
        }
        if (!removed) return;
        pending.Completion.TrySetResult(new WorkshopInstalledItemResult(
            WorkshopRemoteStatus.Cancelled,
            pending.PublishedFileId,
            null,
            0,
            Detail: "Stopped waiting for Workshop download; Steam may continue caching it."));
    }

    private void RemoveDownload(PendingDownload pending)
    {
        lock (_callbackGate)
        {
            if (_downloads.TryGetValue(pending.PublishedFileId, out PendingDownload? current) && ReferenceEquals(current, pending))
                _downloads.Remove(pending.PublishedFileId);
        }
        pending.CancellationRegistration.Dispose();
    }

    private Godot.Collections.Dictionary CallDictionary(string method, params Variant[] args)
    {
        if (!GodotObject.IsInstanceValid(_bridge)) return new Godot.Collections.Dictionary();
        Variant result = _bridge!.Call(method, args);
        return result.VariantType == Variant.Type.Dictionary
            ? result.AsGodotDictionary()
            : new Godot.Collections.Dictionary();
    }

    private bool CallBool(string method, params Variant[] args) =>
        GodotObject.IsInstanceValid(_bridge) && _bridge!.Call(method, args).AsBool();

    private long CallInt64(string method, params Variant[] args) =>
        GodotObject.IsInstanceValid(_bridge) ? _bridge!.Call(method, args).AsInt64() : 0;

    private static int ReadInt32(Godot.Collections.Dictionary dictionary, string key, int fallback = 0) =>
        dictionary.TryGetValue(key, out Variant value) ? checked((int)value.AsInt64()) : fallback;

    private static ulong ReadUInt64(Godot.Collections.Dictionary dictionary, params string[] keys)
    {
        foreach (string key in keys)
            if (dictionary.TryGetValue(key, out Variant value))
            {
                long signed = value.AsInt64();
                return signed <= 0 ? 0 : checked((ulong)signed);
            }
        return 0;
    }

    private static string? ReadString(Godot.Collections.Dictionary dictionary, params string[] keys)
    {
        foreach (string key in keys)
            if (dictionary.TryGetValue(key, out Variant value))
            {
                string text = value.AsString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        return null;
    }

    private static bool ReadBool(Godot.Collections.Dictionary dictionary, string key1, string key2, bool fallback)
    {
        if (dictionary.TryGetValue(key1, out Variant first)) return first.AsBool();
        if (dictionary.TryGetValue(key2, out Variant second)) return second.AsBool();
        return fallback;
    }

    private void SetUnavailable(string reason)
    {
        IsInitialized = false;
        UnavailableReason = reason;
    }

    private bool IsOnMainThread => _mainThreadId == 0 || System.Environment.CurrentManagedThreadId == _mainThreadId;

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken token)
    {
        if (!token.CanBeCanceled) return await task;
        TaskCompletionSource<bool> cancellation = NewCompletion<bool>();
        using CancellationTokenRegistration registration = token.Register(() => cancellation.TrySetResult(true));
        Task winner = await Task.WhenAny(task, cancellation.Task);
        if (winner != task) throw new OperationCanceledException(token);
        return await task;
    }
}