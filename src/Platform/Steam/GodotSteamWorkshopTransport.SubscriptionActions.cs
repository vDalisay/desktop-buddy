using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport
{
    private const int UnsubscribeConfirmationFrames = 300;

    public async Task<WorkshopSubscriptionChangeResult> UnsubscribeAsync(
        ulong publishedFileId,
        CancellationToken token)
    {
        if (!IsAvailable)
        {
            return new WorkshopSubscriptionChangeResult(
                WorkshopRemoteStatus.Unavailable,
                publishedFileId,
                Detail: UnavailableReason);
        }
        if (!IsOnMainThread)
        {
            return new WorkshopSubscriptionChangeResult(
                WorkshopRemoteStatus.Failed,
                publishedFileId,
                Detail: "Steam Workshop unsubscribe must start on the Godot main thread.");
        }
        if (publishedFileId == 0)
        {
            return new WorkshopSubscriptionChangeResult(
                WorkshopRemoteStatus.Failed,
                0,
                Detail: "Published file ID is required.");
        }
        if (token.IsCancellationRequested)
        {
            return new WorkshopSubscriptionChangeResult(
                WorkshopRemoteStatus.Cancelled,
                publishedFileId,
                Detail: "Workshop unsubscribe cancelled before it started.");
        }

        long rawId = checked((long)publishedFileId);
        WorkshopItemState initial = (WorkshopItemState)checked((uint)Math.Max(0, CallInt64("get_item_state", rawId)));
        if ((initial & WorkshopItemState.Subscribed) == 0)
            return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Success, publishedFileId);

        if (!CallBool("unsubscribe_item", rawId))
        {
            return new WorkshopSubscriptionChangeResult(
                WorkshopRemoteStatus.Failed,
                publishedFileId,
                Detail: "Steam could not start the Workshop unsubscribe operation.");
        }

        // Valve documents UnsubscribeItem as asynchronous. Avoid pretending dispatch means success:
        // keep pumping normal Steam callbacks and wait until GetItemState drops the Subscribed bit.
        // The local installed copy may remain until the game exits; that does not mean the user is
        // still subscribed.
        for (int frame = 0; frame < UnsubscribeConfirmationFrames; frame++)
        {
            if (token.IsCancellationRequested)
            {
                return new WorkshopSubscriptionChangeResult(
                    WorkshopRemoteStatus.Cancelled,
                    publishedFileId,
                    Detail: "Stopped waiting for Steam to confirm the unsubscribe. Steam may still complete it.");
            }

            WorkshopItemState state = (WorkshopItemState)checked((uint)Math.Max(0, CallInt64("get_item_state", rawId)));
            if ((state & WorkshopItemState.Subscribed) == 0)
                return new WorkshopSubscriptionChangeResult(WorkshopRemoteStatus.Success, publishedFileId);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return new WorkshopSubscriptionChangeResult(
            WorkshopRemoteStatus.Failed,
            publishedFileId,
            Detail: "Steam did not confirm the Workshop unsubscribe in time. Refresh subscriptions to check its final state.");
    }
}
