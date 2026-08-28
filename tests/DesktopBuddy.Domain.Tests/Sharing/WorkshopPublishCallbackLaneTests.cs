using System;
using System.Threading.Tasks;
using DesktopBuddy.Platform.Steam;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sharing;

public sealed class WorkshopPublishCallbackLaneTests
{
    [Fact]
    public async Task Create_lane_blocks_other_publish_until_callback_and_ignores_duplicate()
    {
        var lane = new WorkshopPublishCallbackLane();

        Assert.True(lane.TryBeginCreate(out Task<WorkshopCreateCallbackSignal> first));
        Assert.True(lane.HasPendingPublish);
        Assert.False(lane.TryBeginCreate(out _));
        Assert.False(lane.TryBeginUpdate(41, uploadProgress: null, out _));

        Assert.True(lane.CompleteCreate(nativeResult: 1, publishedFileId: 9001, needsLegalAgreement: true));
        WorkshopCreateCallbackSignal signal = await first;

        Assert.Equal(1, signal.NativeResult);
        Assert.Equal(9001UL, signal.PublishedFileId);
        Assert.True(signal.NeedsLegalAgreement);
        Assert.False(lane.HasPendingPublish);
        Assert.False(lane.CompleteCreate(nativeResult: 1, publishedFileId: 9999, needsLegalAgreement: false));

        Assert.True(lane.TryBeginUpdate(42, uploadProgress: null, out Task<WorkshopUpdateCallbackSignal> update));
        Assert.True(lane.CompleteUpdate(1, false, out _));
        Assert.Equal(1, (await update).NativeResult);
    }

    [Fact]
    public async Task Rejected_native_create_start_releases_lane_and_completes_waiter()
    {
        var lane = new WorkshopPublishCallbackLane();
        Assert.True(lane.TryBeginCreate(out Task<WorkshopCreateCallbackSignal> callback));

        lane.RejectCreateStart();

        WorkshopCreateCallbackSignal signal = await callback;
        Assert.Equal(-1, signal.NativeResult);
        Assert.Equal(0UL, signal.PublishedFileId);
        Assert.False(lane.HasPendingPublish);
        Assert.True(lane.TryBeginCreate(out _));
    }

    [Fact]
    public async Task Update_lane_keeps_callback_ownership_until_native_completion()
    {
        var lane = new WorkshopPublishCallbackLane();
        var progress = new RecordingProgress();

        Assert.True(lane.TryBeginUpdate(77, progress, out Task<WorkshopUpdateCallbackSignal> callback));
        Assert.True(lane.HasPendingPublish);
        Assert.False(lane.TryBeginCreate(out _));
        Assert.True(lane.TryGetUploadProgress(out long handle, out IProgress<WorkshopTransferProgress>? capturedProgress));
        Assert.Equal(77, handle);
        Assert.Same(progress, capturedProgress);

        Assert.True(lane.CompleteUpdate(nativeResult: 1, needsLegalAgreement: false, out IProgress<WorkshopTransferProgress>? completedProgress));
        WorkshopUpdateCallbackSignal signal = await callback;

        Assert.Equal(1, signal.NativeResult);
        Assert.False(signal.NeedsLegalAgreement);
        Assert.Same(progress, completedProgress);
        Assert.False(lane.HasPendingPublish);
        Assert.False(lane.TryGetUploadProgress(out _, out _));
        Assert.False(lane.CompleteUpdate(nativeResult: 1, needsLegalAgreement: false, out _));
    }

    [Fact]
    public async Task Rejected_native_update_start_releases_lane_and_clears_progress_owner()
    {
        var lane = new WorkshopPublishCallbackLane();
        var progress = new RecordingProgress();
        Assert.True(lane.TryBeginUpdate(88, progress, out Task<WorkshopUpdateCallbackSignal> callback));

        lane.RejectUpdateStart();

        WorkshopUpdateCallbackSignal signal = await callback;
        Assert.Equal(-1, signal.NativeResult);
        Assert.False(lane.HasPendingPublish);
        Assert.False(lane.TryGetUploadProgress(out _, out _));
        Assert.True(lane.TryBeginCreate(out _));
    }

    [Fact]
    public async Task Shutdown_completes_create_and_update_waiters_and_discards_late_callbacks()
    {
        var lane = new WorkshopPublishCallbackLane();
        Assert.True(lane.TryBeginCreate(out Task<WorkshopCreateCallbackSignal> create));

        lane.Shutdown();

        Assert.Equal(-1, (await create).NativeResult);
        Assert.False(lane.HasPendingPublish);
        Assert.False(lane.CompleteCreate(1, 1234, false));

        Assert.True(lane.TryBeginUpdate(99, uploadProgress: null, out Task<WorkshopUpdateCallbackSignal> update));
        lane.Shutdown();

        Assert.Equal(-1, (await update).NativeResult);
        Assert.False(lane.HasPendingPublish);
        Assert.False(lane.CompleteUpdate(1, false, out _));
    }

    private sealed class RecordingProgress : IProgress<WorkshopTransferProgress>
    {
        public WorkshopTransferProgress? Last { get; private set; }

        public void Report(WorkshopTransferProgress value) => Last = value;
    }
}
