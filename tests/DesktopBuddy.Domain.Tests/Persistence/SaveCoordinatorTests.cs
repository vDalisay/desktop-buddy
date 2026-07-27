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

    /// <summary>
    /// Save &amp; Quit and clean exit have no "next request" to retry on, so a mutation that
    /// landed during the durable write must still be captured before the process dies.
    /// </summary>
    [Fact]
    public async Task ForcedFlush_WritesAMutationThatArrivedDuringTheWrite()
    {
        var state = new BuddyProgressState(0.5);
        var store = new BlockingProgressStore();
        var coordinator = new SaveCoordinator(state, store);
        state.ApplyCareMood(2);

        Task forced = coordinator.FlushProgressAsync(force: true);
        await store.WriteStarted.Task;
        state.AccrueTime(1, 1, 0);
        store.AllowWrite.TrySetResult();
        await forced;

        Assert.False(coordinator.IsDirty);
        Assert.Equal(2, store.ProgressWriteCount);
    }

    /// <summary>
    /// Bounded to one extra pass: running-time revisions advance continuously, so an
    /// unbounded catch-up loop would trap the quit forever.
    /// </summary>
    [Fact]
    public async Task ForcedFlush_RunsAtMostOneExtraPass()
    {
        var state = new BuddyProgressState(0.5);
        var store = new MutatingProgressStore(state);
        var coordinator = new SaveCoordinator(state, store);
        state.ApplyCareMood(2);

        await coordinator.FlushProgressAsync(force: true);

        Assert.Equal(2, store.ProgressWriteCount);
        Assert.True(coordinator.IsDirty);
    }

    [Fact]
    public async Task ForcedFlush_OnCleanStateWritesNothingExtra()
    {
        var state = new BuddyProgressState(0.5);
        var store = new InMemoryProgressStore();
        var coordinator = new SaveCoordinator(state, store);
        state.ApplyCareMood(1);

        await coordinator.FlushProgressAsync(force: true);

        Assert.Equal(1, store.ProgressWriteCount);
        Assert.False(coordinator.IsDirty);
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

    /// <summary>Dirties the state on every write, so a forced flush can never converge.</summary>
    private sealed class MutatingProgressStore(BuddyProgressState state) : IProgressStore
    {
        public int ProgressWriteCount { get; private set; }

        public Task<LoadResult<ProgressSave>> LoadProgressAsync(
            System.Threading.CancellationToken token) =>
            throw new NotSupportedException();

        public Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(
            System.Threading.CancellationToken token) =>
            throw new NotSupportedException();

        public Task SaveProgressAsync(ProgressSave data, System.Threading.CancellationToken token)
        {
            ProgressWriteCount++;
            state.AccrueTime(1, 0, 0);
            return Task.CompletedTask;
        }

        public Task SaveSettingsAsync(
            LocalSettingsSave data,
            System.Threading.CancellationToken token) =>
            Task.CompletedTask;
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
