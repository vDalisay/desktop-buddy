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
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly object _callbackGate = new();
    private readonly Dictionary<ulong, TaskCompletionSource<DownloadCallback>> _downloads = new();
    private Node? _bridge;
    private uint _appId;
    private TaskCompletionSource<CreateCallback>? _pendingCreate;
    private TaskCompletionSource<UpdateCallback>? _pendingUpdate;
    private long _activeUpdateHandle;
    private IProgress<WorkshopTransferProgress>? _activeUploadProgress;
    private bool _signalsConnected;

    private readonly record struct CreateCallback(int Result, ulong FileId, bool NeedsAgreement);
    private readonly record struct UpdateCallback(int Result, bool NeedsAgreement);
    private readonly record struct DownloadCallback(int Result, uint AppId, ulong FileId);

    public bool IsAvailable => IsInitialized && GodotObject.IsInstanceValid(_bridge);
    public bool IsInstalled => GodotObject.IsInstanceValid(_bridge);
    public bool IsInitialized { get; private set; }
    public string? UnavailableReason { get; private set; } = "Steam Workshop has not been initialized.";
    public uint AppId => _appId;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!IsAvailable || _activeUpdateHandle <= 0 || _activeUploadProgress is null)
            return;
        Godot.Collections.Dictionary info = CallDictionary("get_item_update_progress", _activeUpdateHandle);
        ulong processed = ReadUInt64(info, "processed", "bytes_processed", "current");
        ulong total = ReadUInt64(info, "total", "bytes_total");
        if (total > 0)
            _activeUploadProgress.Report(new WorkshopTransferProgress(processed, total, "Uploading"));
    }

    public bool Initialize(Node bridge, uint appId)
    {
        ArgumentNullException.ThrowIfNull(bridge);
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

    public Task<WorkshopCreateRemoteResult> CreateItemAsync(CancellationToken token) =>
        SerializePublishAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            TaskCompletionSource<CreateCallback> completion = NewCompletion<CreateCallback>();
            lock (_callbackGate)
            {
                if (_pendingCreate is not null)
                    return new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "Another create operation is already pending.");
                _pendingCreate = completion;
            }

            try
            {
                if (!CallBool("create_item", (long)_appId))
                    return new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "GodotSteam rejected CreateItem.");
                CreateCallback callback = await WaitAsync(completion.Task, token).ConfigureAwait(false);
                return callback.Result == SteamResultOk
                    ? new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Success, callback.FileId, callback.NeedsAgreement, callback.Result)
                    : new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Failed, callback.FileId, callback.NeedsAgreement, callback.Result, $"Steam CreateItem failed with EResult {callback.Result}.");
            }
            catch (OperationCanceledException)
            {
                return new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Cancelled, 0, false, Detail: "Workshop create cancelled while waiting for Steam.");
            }
            finally
            {
                lock (_callbackGate)
                    if (ReferenceEquals(_pendingCreate, completion)) _pendingCreate = null;
            }
        }, token);

    public Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token) => SerializePublishAsync(async () =>
    {
        if (!IsAvailable)
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Unavailable, update.PublishedFileId, false, Detail: UnavailableReason);
        if (update.PublishedFileId == 0)
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, 0, false, Detail: "Published file ID is required.");
        token.ThrowIfCancellationRequested();

        long handle = CallInt64("start_item_update", (long)_appId, checked((long)update.PublishedFileId));
        if (handle <= 0)
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "Steam returned an invalid UGC update handle.");

        if (!CallBool("set_item_title", handle, update.Title) ||
            !CallBool("set_item_description", handle, update.Description) ||
            !CallBool("set_item_visibility", handle, (int)update.Visibility) ||
            !CallBool("set_item_tags", handle, update.Tags.ToArray()) ||
            !CallBool("set_item_metadata", handle, update.Metadata) ||
            !CallBool("set_item_content", handle, update.ContentFolder) ||
            (!string.IsNullOrWhiteSpace(update.PreviewFile) && !CallBool("set_item_preview", handle, update.PreviewFile!)))
        {
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "Steam rejected one or more Workshop update fields.");
        }

        TaskCompletionSource<UpdateCallback> completion = NewCompletion<UpdateCallback>();
        lock (_callbackGate)
        {
            if (_pendingUpdate is not null)
                return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "Another Workshop update is already pending.");
            _pendingUpdate = completion;
            _activeUpdateHandle = handle;
            _activeUploadProgress = progress;
        }

        try
        {
            if (!CallBool("submit_item_update", handle, update.ChangeNote))
                return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, false, Detail: "GodotSteam rejected SubmitItemUpdate.");

            // Steam does not expose cancellation for an upload once submitted. Cancellation only
            // stops this caller waiting; the bridge continues pumping and consumes the callback.
            UpdateCallback callback = await WaitAsync(completion.Task, token).ConfigureAwait(false);
            if (callback.Result == SteamResultOk)
            {
                progress?.Report(new WorkshopTransferProgress(1, 1, "Complete"));
                return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Success, update.PublishedFileId, callback.NeedsAgreement, callback.Result);
            }
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Failed, update.PublishedFileId, callback.NeedsAgreement, callback.Result, $"Steam SubmitItemUpdate failed with EResult {callback.Result}.");
        }
        catch (OperationCanceledException)
        {
            return new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Cancelled, update.PublishedFileId, false, Detail: "Stopped waiting for Workshop upload; Steam may still finish it.");
        }
        finally
        {
            lock (_callbackGate)
            {
                if (ReferenceEquals(_pendingUpdate, completion)) _pendingUpdate = null;
                _activeUpdateHandle = 0;
                _activeUploadProgress = null;
            }
        }
    }, token);

    public Task<IReadOnlyList<PublishedWorkshopItem>> GetSubscribedItemsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAvailable)
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

    public async Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        if (!IsAvailable)
            return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Unavailable, publishedFileId, null, 0, Detail: UnavailableReason);
        if (publishedFileId == 0)
            return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, 0, null, 0, Detail: "Published file ID is required.");
        token.ThrowIfCancellationRequested();

        long rawId = checked((long)publishedFileId);
        WorkshopItemState state = (WorkshopItemState)checked((uint)Math.Max(0, CallInt64("get_item_state", rawId)));
        if ((state & WorkshopItemState.Installed) != 0 && (state & WorkshopItemState.NeedsUpdate) == 0)
            return InstalledInfo(publishedFileId);

        TaskCompletionSource<DownloadCallback> completion = NewCompletion<DownloadCallback>();
        lock (_callbackGate)
        {
            if (_downloads.ContainsKey(publishedFileId))
                return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, publishedFileId, null, 0, Detail: "A download for this Workshop item is already pending.");
            _downloads.Add(publishedFileId, completion);
        }
        try
        {
            if (!CallBool("download_item", rawId, false))
                return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, publishedFileId, null, 0, Detail: "Steam could not start the Workshop download.");

            while (!completion.Task.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                Godot.Collections.Dictionary info = CallDictionary("get_item_download_info", rawId);
                ulong current = ReadUInt64(info, "downloaded", "bytes_downloaded", "current");
                ulong total = ReadUInt64(info, "total", "bytes_total");
                if (total > 0) progress?.Report(new WorkshopTransferProgress(current, total, "Downloading"));
                Task delay = Task.Delay(100, token);
                Task winner = await Task.WhenAny(completion.Task, delay).ConfigureAwait(false);
                if (winner == completion.Task) break;
            }

            DownloadCallback callback = await completion.Task.ConfigureAwait(false);
            if (callback.AppId != _appId || callback.FileId != publishedFileId)
                return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, publishedFileId, null, 0, callback.Result, "Steam returned a mismatched download callback.");
            if (callback.Result != SteamResultOk)
                return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Failed, publishedFileId, null, 0, callback.Result, $"Steam Workshop download failed with EResult {callback.Result}.");
            return InstalledInfo(publishedFileId);
        }
        catch (OperationCanceledException)
        {
            return new WorkshopInstalledItemResult(WorkshopRemoteStatus.Cancelled, publishedFileId, null, 0, Detail: "Workshop download wait cancelled.");
        }
        finally
        {
            lock (_callbackGate) _downloads.Remove(publishedFileId);
        }
    }

    public void OpenWorkshopBrowser()
    {
        if (IsAvailable) _bridge!.Call("open_workshop_browser", (long)_appId);
    }

    public void OpenWorkshopItem(ulong publishedFileId)
    {
        if (IsAvailable && publishedFileId != 0)
            _bridge!.Call("open_workshop_item", checked((long)publishedFileId));
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_bridge)) _bridge!.Call("shutdown");
        IsInitialized = false;
        base._ExitTree();
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
        lock (_callbackGate) pending = _pendingCreate;
        if (pending is null || fileId <= 0) return;
        pending.TrySetResult(new CreateCallback(checked((int)result), checked((ulong)fileId), needsAgreement));
    }

    private void OnItemUpdated(long result, bool needsAgreement)
    {
        TaskCompletionSource<UpdateCallback>? pending;
        lock (_callbackGate) pending = _pendingUpdate;
        pending?.TrySetResult(new UpdateCallback(checked((int)result), needsAgreement));
    }

    private void OnItemDownloaded(long result, long appId, long fileId)
    {
        if (appId < 0 || fileId <= 0) return;
        TaskCompletionSource<DownloadCallback>? pending;
        lock (_callbackGate) _downloads.TryGetValue(checked((ulong)fileId), out pending);
        pending?.TrySetResult(new DownloadCallback(checked((int)result), checked((uint)appId), checked((ulong)fileId)));
    }

    private async Task<T> SerializePublishAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        if (!IsAvailable)
        {
            if (typeof(T) == typeof(WorkshopCreateRemoteResult))
                return (T)(object)new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Unavailable, 0, false, Detail: UnavailableReason);
            if (typeof(T) == typeof(WorkshopSubmitRemoteResult))
                return (T)(object)new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Unavailable, 0, false, Detail: UnavailableReason);
        }
        try
        {
            await _publishGate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (typeof(T) == typeof(WorkshopCreateRemoteResult))
                return (T)(object)new WorkshopCreateRemoteResult(WorkshopRemoteStatus.Cancelled, 0, false);
            if (typeof(T) == typeof(WorkshopSubmitRemoteResult))
                return (T)(object)new WorkshopSubmitRemoteResult(WorkshopRemoteStatus.Cancelled, 0, false);
            throw;
        }
        try { return await action().ConfigureAwait(false); }
        finally { _publishGate.Release(); }
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

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken token)
    {
        if (!token.CanBeCanceled) return await task.ConfigureAwait(false);
        TaskCompletionSource<bool> cancellation = NewCompletion<bool>();
        using CancellationTokenRegistration registration = token.Register(() => cancellation.TrySetResult(true));
        Task winner = await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false);
        if (winner != task) throw new OperationCanceledException(token);
        return await task.ConfigureAwait(false);
    }
}
