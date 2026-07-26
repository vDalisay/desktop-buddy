using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Single serialized progress writer with revision-based dirty tracking and
/// valid-running-time autosave coalescing.
/// </summary>
public sealed class SaveCoordinator
{
    public const double AutosaveSeconds = 30.0;

    private readonly object _sync = new();
    private readonly BuddyProgressState _progress;
    private readonly IProgressStore _store;
    private Task _activeFlush = Task.CompletedTask;
    private long _savedRevision;
    private double _dirtyRunningSeconds;

    public SaveCoordinator(
        BuddyProgressState progress,
        IProgressStore store,
        long? savedRevision = null)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _savedRevision = savedRevision ?? progress.Revision;
        _progress.Changed += OnProgressChanged;
    }

    public bool IsDirty => _progress.Revision != Interlocked.Read(ref _savedRevision);
    public Exception? LastFailure { get; private set; }

    /// <summary>
    /// Counts only accepted running spans. Hidden accrual mutates Revision and
    /// therefore marks dirty exactly like foreground accrual.
    /// </summary>
    public Task TickAsync(double validRunningSeconds, CancellationToken token = default)
    {
        if (validRunningSeconds <= 0.0 || !IsDirty)
            return Task.CompletedTask;
        _dirtyRunningSeconds += validRunningSeconds;
        if (_dirtyRunningSeconds < AutosaveSeconds)
            return Task.CompletedTask;
        return FlushProgressAsync(token);
    }

    public Task FlushProgressAsync(CancellationToken token = default)
    {
        lock (_sync)
        {
            if (!_activeFlush.IsCompleted)
                return _activeFlush;
            _activeFlush = FlushLoopAsync(token);
            return _activeFlush;
        }
    }

    public Task SaveSettingsAsync(
        LocalSettingsSave settings,
        CancellationToken token = default) =>
        _store.SaveSettingsAsync(settings, token);

    private async Task FlushLoopAsync(CancellationToken token)
    {
        try
        {
            if (!IsDirty)
                return;

            // One flush owns one coherent main-thread snapshot. A mutation that
            // arrives during the durable write remains dirty for the next request;
            // continuously advancing running-time revisions must never starve an
            // explicit save/quit behind an endless catch-up loop.
            ProgressSnapshot snapshot = _progress.Snapshot();
            ProgressSave save = ProgressSave.FromSnapshot(snapshot);
            await _store.SaveProgressAsync(save, token).ConfigureAwait(false);
            Interlocked.Exchange(ref _savedRevision, snapshot.Revision);
            _dirtyRunningSeconds = 0.0;
            LastFailure = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastFailure = exception;
            // Do not advance _savedRevision: failure intentionally retains dirty state.
            throw;
        }
    }

    private void OnProgressChanged(ProgressChange change)
    {
        if (change == ProgressChange.ToolUnlocked)
            _ = FlushImmediateEventAsync();
    }

    private async Task FlushImmediateEventAsync()
    {
        try
        {
            await FlushProgressAsync().ConfigureAwait(false);
        }
        catch
        {
            // FlushLoopAsync records LastFailure and deliberately leaves the
            // revision dirty. The next autosave/event/clean exit retries it.
        }
    }
}
