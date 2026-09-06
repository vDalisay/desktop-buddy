using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// Cross-app publishing surface used by the Steam demo. Steam itself is initialized under the
/// running AppID; before each create/update/browser operation the bridge is pointed at either the
/// runtime Workshop or the canonical full-game Workshop. The only allowed targets are the two
/// identities that were validated at initialization time.
/// </summary>
public partial class GodotSteamWorkshopTransport : ITargetedSteamWorkshopTransport
{
    private uint _crossAppPublishTargetAppId;

    // Ordinary Workshop consumption remains scoped to the app that is actually running. The
    // targeted interface separately exposes the authorized full-game mirror target to the demo
    // mirroring layer without changing download/subscription identity.
    uint ITargetedSteamWorkshopTransport.WorkshopOwnerAppId =>
        _crossAppPublishTargetAppId == 0 ? _runtimeAppId : _crossAppPublishTargetAppId;

    private void ConfigureCrossAppPublishTarget(uint runtimeAppId, uint workshopOwnerAppId)
    {
        _crossAppPublishTargetAppId = workshopOwnerAppId == runtimeAppId ? 0 : workshopOwnerAppId;
    }

    public Task<WorkshopCreateRemoteResult> CreateItemAsync(
        uint consumerAppId,
        CancellationToken token)
    {
        if (!IsAvailable)
            return Task.FromResult(new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Unavailable,
                0,
                false,
                Detail: UnavailableReason));
        if (!IsOnMainThread)
            return Task.FromResult(new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Failed,
                0,
                false,
                Detail: "Steam Workshop create must start on the Godot main thread."));
        if (token.IsCancellationRequested)
            return Task.FromResult(new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Cancelled,
                0,
                false));
        if (!TrySelectPublishTarget(consumerAppId, out string? targetError))
            return Task.FromResult(new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Failed,
                0,
                false,
                Detail: targetError));

        if (!_publishCallbacks.TryBeginCreate(out Task<WorkshopCreateCallbackSignal> callback))
            return Task.FromResult(new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Failed,
                0,
                false,
                Detail: "Another Workshop publish operation is pending."));

        if (!CallBool("create_item", (long)consumerAppId))
        {
            _publishCallbacks.RejectCreateStart();
            return Task.FromResult(new WorkshopCreateRemoteResult(
                WorkshopRemoteStatus.Failed,
                0,
                false,
                Detail: $"GodotSteam rejected CreateItem for consumer AppID {consumerAppId}."));
        }

        // Steam has no cancellation primitive for CreateItem. Once dispatched, keep ownership of
        // the callback lane until the real result arrives so the caller receives the file ID.
        return AwaitCreateAsync(callback);
    }

    public Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        uint consumerAppId,
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token)
    {
        if (!IsAvailable)
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Unavailable,
                update.PublishedFileId,
                false,
                Detail: UnavailableReason));
        if (!IsOnMainThread)
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                update.PublishedFileId,
                false,
                Detail: "Steam Workshop update must start on the Godot main thread."));
        if (update.PublishedFileId == 0)
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                0,
                false,
                Detail: "Published file ID is required."));
        if (token.IsCancellationRequested)
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Cancelled,
                update.PublishedFileId,
                false));
        if (!TrySelectPublishTarget(consumerAppId, out string? targetError))
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                update.PublishedFileId,
                false,
                Detail: targetError));
        if (_publishCallbacks.HasPendingPublish)
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                update.PublishedFileId,
                false,
                Detail: "Another Workshop publish operation is pending."));

        long handle = CallInt64("start_item_update", (long)consumerAppId, checked((long)update.PublishedFileId));
        if (handle == InvalidUgcUpdateHandle)
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                update.PublishedFileId,
                false,
                Detail: $"Steam returned an invalid UGC update handle for consumer AppID {consumerAppId}."));

        if (!CallBool("set_item_title", handle, update.Title) ||
            !CallBool("set_item_description", handle, update.Description) ||
            !CallBool("set_item_visibility", handle, (int)update.Visibility) ||
            !CallBool("set_item_tags", handle, update.Tags.ToArray()) ||
            !CallBool("set_item_metadata", handle, update.Metadata) ||
            !CallBool("set_item_content", handle, update.ContentFolder) ||
            (!string.IsNullOrWhiteSpace(update.PreviewFile) && !CallBool("set_item_preview", handle, update.PreviewFile!)))
        {
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                update.PublishedFileId,
                false,
                Detail: $"Steam rejected one or more Workshop update fields for consumer AppID {consumerAppId}."));
        }

        if (!_publishCallbacks.TryBeginUpdate(
                handle,
                update.PublishedFileId,
                progress,
                out Task<WorkshopUpdateCallbackSignal> callback))
        {
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                update.PublishedFileId,
                false,
                Detail: "Another Workshop publish operation became pending."));
        }

        if (!CallBool("submit_item_update", handle, update.ChangeNote))
        {
            _publishCallbacks.RejectUpdateStart();
            return Task.FromResult(new WorkshopSubmitRemoteResult(
                WorkshopRemoteStatus.Failed,
                update.PublishedFileId,
                false,
                Detail: $"GodotSteam rejected SubmitItemUpdate for consumer AppID {consumerAppId}."));
        }

        return AwaitUpdateAsync(callback, update.PublishedFileId, token);
    }

    public void OpenWorkshopBrowser(uint consumerAppId)
    {
        if (!IsAvailable || !IsOnMainThread) return;
        if (!TrySelectPublishTarget(consumerAppId, out _)) return;
        _bridge!.Call("open_workshop_browser", (long)consumerAppId);
    }

    private bool TrySelectPublishTarget(uint consumerAppId, out string? error)
    {
        error = null;
        uint mirrorAppId = _crossAppPublishTargetAppId;
        if (consumerAppId == 0 ||
            (consumerAppId != _runtimeAppId && consumerAppId != mirrorAppId))
        {
            error = $"Consumer AppID {consumerAppId} is not an allowed Desktop Buddy Workshop target.";
            return false;
        }

        if (!Godot.GodotObject.IsInstanceValid(_bridge) ||
            !_bridge!.Call("configure_workshop_app_id", (long)consumerAppId).AsBool())
        {
            error = $"GodotSteam bridge rejected consumer AppID {consumerAppId}.";
            return false;
        }

        return true;
    }
}
