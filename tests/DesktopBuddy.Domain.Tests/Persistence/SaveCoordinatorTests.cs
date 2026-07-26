using System;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SaveCoordinatorTests
{
    [Fact]
    public async Task DirtyProgress_CoalescesAtThirtyValidRunningSeconds()
    {
        var state = new BuddyProgressState(0.5);
        var store = new InMemoryProgressStore();
        var coordinator = new SaveCoordinator(state, store);
        state.ApplyCareMood(1);

        await coordinator.TickAsync(29.9);
        Assert.Equal(0, store.ProgressWriteCount);

        await coordinator.TickAsync(0.1);
        Assert.Equal(1, store.ProgressWriteCount);
        Assert.False(coordinator.IsDirty);
    }

    [Fact]
    public async Task ConcurrentImmediateRequests_UseOneSerializedWriter()
    {
        var state = new BuddyProgressState(0.5);
        var store = new InMemoryProgressStore();
        var coordinator = new SaveCoordinator(state, store);
        state.ApplyCareMood(2);

        await Task.WhenAll(
            coordinator.FlushProgressAsync(),
            coordinator.FlushProgressAsync(),
            coordinator.FlushProgressAsync());

        Assert.Equal(1, store.ProgressWriteCount);
        Assert.False(coordinator.IsDirty);
    }

    [Fact]
    public async Task MutationDuringWrite_DoesNotStarveFlushAndRemainsDirty()
    {
        var state = new BuddyProgressState(0.5);
        var store = new BlockingProgressStore();
        var coordinator = new SaveCoordinator(state, store);
        state.ApplyCareMood(2);

        Task first = coordinator.FlushProgressAsync();
        await store.WriteStarted.Task;
        state.AccrueTime(1, 1, 0);
        store.AllowWrite.TrySetResult();
        await first;

        Assert.True(coordinator.IsDirty);
        Assert.Equal(1, store.ProgressWriteCount);

        await coordinator.FlushProgressAsync();
        Assert.False(coordinator.IsDirty);
        Assert.Equal(2, store.ProgressWriteCount);
    }

    [Fact]
    public async Task FailedWrite_RetainsDirtyStateForRetry()
    {
        var state = new BuddyProgressState(0.5);
        var store = new InMemoryProgressStore
        {
            NextProgressFailure = new InvalidOperationException("injected"),
        };
        var coordinator = new SaveCoordinator(state, store);
        state.ApplyCareMood(3);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.FlushProgressAsync());
        Assert.True(coordinator.IsDirty);
        Assert.NotNull(coordinator.LastFailure);

        await coordinator.FlushProgressAsync();
        Assert.False(coordinator.IsDirty);
        Assert.Equal(1, store.ProgressWriteCount);
    }

    [Fact]
    public async Task HiddenTimeMutation_IsDirtyAndAutosavesNormally()
    {
        var state = new BuddyProgressState(0.5);
        var store = new InMemoryProgressStore();
        var coordinator = new SaveCoordinator(state, store);

        state.AccrueTime(30, 0, 30);
        await coordinator.TickAsync(30);

        Assert.Equal(30, store.Progress!.Times.HiddenSeconds);
        Assert.Equal(1, store.ProgressWriteCount);
    }

    [Fact]
    public async Task SettingsUseTheirOwnWriteChannel()
    {
        var state = new BuddyProgressState(0.5);
        var store = new InMemoryProgressStore();
        var coordinator = new SaveCoordinator(state, store);

        await coordinator.SaveSettingsAsync(new LocalSettingsSave { Revision = 4 });

        Assert.Equal(0, store.ProgressWriteCount);
        Assert.Equal(1, store.SettingsWriteCount);
        Assert.Equal(4, store.Settings!.Revision);
    }

    private sealed class BlockingProgressStore : IProgressStore
    {
        public TaskCompletionSource WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ProgressWriteCount { get; private set; }

        public Task<LoadResult<ProgressSave>> LoadProgressAsync(
            System.Threading.CancellationToken token) =>
            throw new NotSupportedException();

        public Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(
            System.Threading.CancellationToken token) =>
            throw new NotSupportedException();

        public async Task SaveProgressAsync(
            ProgressSave data,
            System.Threading.CancellationToken token)
        {
            WriteStarted.TrySetResult();
            if (ProgressWriteCount == 0)
                await AllowWrite.Task.WaitAsync(token);
            ProgressWriteCount++;
        }

        public Task SaveSettingsAsync(
            LocalSettingsSave data,
            System.Threading.CancellationToken token) =>
            Task.CompletedTask;
    }
}
