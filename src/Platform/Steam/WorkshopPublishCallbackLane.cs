using System;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

internal readonly record struct WorkshopCreateCallbackSignal(
    int NativeResult,
    ulong PublishedFileId,
    bool NeedsLegalAgreement);

internal readonly record struct WorkshopUpdateCallbackSignal(
    int NativeResult,
    bool NeedsLegalAgreement);

/// <summary>
/// Pure managed state machine for Steam's single in-flight CreateItem/SubmitItemUpdate callback
/// lane. Steam callbacks are not correlated by request token, so the next operation must never
/// begin until the callback that owns the current lane has arrived. Duplicate/late callbacks are
/// ignored after ownership has been released.
/// </summary>
internal sealed class WorkshopPublishCallbackLane
{
    private readonly object _gate = new();
    private TaskCompletionSource<WorkshopCreateCallbackSignal>? _create;
    private TaskCompletionSource<WorkshopUpdateCallbackSignal>? _update;
    private long _updateHandle;
    private ulong _updatePublishedFileId;
    private IProgress<WorkshopTransferProgress>? _uploadProgress;

    public bool HasPendingPublish
    {
        get
        {
            lock (_gate) return _create is not null || _update is not null;
        }
    }

    public bool TryBeginCreate(out Task<WorkshopCreateCallbackSignal> callback)
    {
        lock (_gate)
        {
            if (_create is not null || _update is not null)
            {
                callback = Task.FromResult(default(WorkshopCreateCallbackSignal));
                return false;
            }

            _create = NewCompletion<WorkshopCreateCallbackSignal>();
            callback = _create.Task;
            return true;
        }
    }

    /// <summary>Releases a create lane only when the native create call itself was rejected.</summary>
    public void RejectCreateStart()
    {
        TaskCompletionSource<WorkshopCreateCallbackSignal>? pending;
        lock (_gate)
        {
            pending = _create;
            _create = null;
        }
        pending?.TrySetResult(new WorkshopCreateCallbackSignal(-1, 0, false));
    }

    public bool CompleteCreate(int nativeResult, ulong publishedFileId, bool needsLegalAgreement)
    {
        TaskCompletionSource<WorkshopCreateCallbackSignal>? pending;
        lock (_gate)
        {
            pending = _create;
            if (pending is null) return false;
            _create = null;
        }
        pending.TrySetResult(new WorkshopCreateCallbackSignal(nativeResult, publishedFileId, needsLegalAgreement));
        return true;
    }

    public bool TryBeginUpdate(
        long updateHandle,
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? uploadProgress,
        out Task<WorkshopUpdateCallbackSignal> callback)
    {
        if (updateHandle == -1) throw new ArgumentOutOfRangeException(nameof(updateHandle));
        if (publishedFileId == 0) throw new ArgumentOutOfRangeException(nameof(publishedFileId));
        lock (_gate)
        {
            if (_create is not null || _update is not null)
            {
                callback = Task.FromResult(default(WorkshopUpdateCallbackSignal));
                return false;
            }

            _update = NewCompletion<WorkshopUpdateCallbackSignal>();
            _updateHandle = updateHandle;
            _updatePublishedFileId = publishedFileId;
            _uploadProgress = uploadProgress;
            callback = _update.Task;
            return true;
        }
    }

    /// <summary>Releases an update lane only when SubmitItemUpdate itself was rejected.</summary>
    public void RejectUpdateStart()
    {
        TaskCompletionSource<WorkshopUpdateCallbackSignal>? pending;
        lock (_gate)
        {
            pending = _update;
            _update = null;
            _updateHandle = 0;
            _updatePublishedFileId = 0;
            _uploadProgress = null;
        }
        pending?.TrySetResult(new WorkshopUpdateCallbackSignal(-1, false));
    }

    public bool TryGetUploadProgress(
        out long updateHandle,
        out IProgress<WorkshopTransferProgress>? progress)
    {
        lock (_gate)
        {
            updateHandle = _updateHandle;
            progress = _uploadProgress;
            return _update is not null && updateHandle != -1 && progress is not null;
        }
    }

    public bool CompleteUpdate(
        int nativeResult,
        ulong publishedFileId,
        bool needsLegalAgreement,
        out IProgress<WorkshopTransferProgress>? progress)
    {
        TaskCompletionSource<WorkshopUpdateCallbackSignal>? pending;
        lock (_gate)
        {
            pending = _update;
            if (pending is null || publishedFileId != _updatePublishedFileId)
            {
                progress = null;
                return false;
            }

            progress = _uploadProgress;
            _update = null;
            _updateHandle = 0;
            _updatePublishedFileId = 0;
            _uploadProgress = null;
        }
        pending.TrySetResult(new WorkshopUpdateCallbackSignal(nativeResult, needsLegalAgreement));
        return true;
    }

    /// <summary>
    /// Completes every waiter when the native transport leaves the tree. No caller may remain
    /// blocked waiting for a Steam signal from a bridge that no longer exists.
    /// </summary>
    public void Shutdown()
    {
        TaskCompletionSource<WorkshopCreateCallbackSignal>? create;
        TaskCompletionSource<WorkshopUpdateCallbackSignal>? update;
        lock (_gate)
        {
            create = _create;
            update = _update;
            _create = null;
            _update = null;
            _updateHandle = 0;
            _updatePublishedFileId = 0;
            _uploadProgress = null;
        }

        create?.TrySetResult(new WorkshopCreateCallbackSignal(-1, 0, false));
        update?.TrySetResult(new WorkshopUpdateCallbackSignal(-1, false));
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
