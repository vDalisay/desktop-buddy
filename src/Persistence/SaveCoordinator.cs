using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Single serialized progress writer with revision-based dirty tracking and
/// valid-running-time autosave coalescing. Character selection and Work lifetime progress
/// are captured in the same durable progress write as the gameplay snapshot.
/// </summary>
public sealed class SaveCoordinator
{
    public const double AutosaveSeconds = 30.0;

    private readonly object _sync = new();
    private readonly BuddyProgressState _progress;
    private readonly CharacterSelectionState? _selection;
    private readonly WorkProgressState? _work;
    private readonly IProgressStore _store;
    private Task _activeFlush = Task.CompletedTask;
    private long _savedRevision;
    private long _savedSelectionRevision;
    private long _savedWorkRevision;
    private double _dirtyRunningSeconds;
    private LocalSettingsSave? _registeredSettings;

    public SaveCoordinator(
        BuddyProgressState progress,
        IProgressStore store,
        long? savedRevision = null,
        CharacterSelectionState? selection = null,
        long? savedSelectionRevision = null,
        WorkProgressState? work = null,
        long? savedWorkRevision = null)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _selection = selection;
        _work = work;
        _savedRevision = savedRevision ?? progress.Revision;
        _savedSelectionRevision = savedSelectionRevision ?? selection?.Revision ?? 0;
        _savedWorkRevision = savedWorkRevision ?? work?.Revision ?? 0;
        _progress.Changed += OnProgressChanged;
        if (_selection is not null)
            _selection.Changed += OnSelectionChanged;
    }

    public CharacterSelectionState? CharacterSelection => _selection;
    public WorkProgressState? WorkProgress => _work;

    public bool IsDirty =>
        _progress.Revision != Interlocked.Read(ref _savedRevision) ||
        (_selection is not null &&
            _selection.Revision != Interlocked.Read(ref _savedSelectionRevision)) ||
        (_work is not null &&
            _work.Revision != Interlocked.Read(ref _savedWorkRevision));

    public Exception? LastFailure { get; private set; }

    public Task TickAsync(double validRunningSeconds, CancellationToken token = default)
    {
        if (validRunningSeconds <= 0.0 || !IsDirty)
            return Task.CompletedTask;
        _dirtyRunningSeconds += validRunningSeconds;
        if (_dirtyRunningSeconds < AutosaveSeconds)
            return Task.CompletedTask;
        return RequestFlushAsync(token);
    }

    public async Task FlushProgressAsync(bool force, CancellationToken token = default)
    {
        await RequestFlushAsync(token).ConfigureAwait(false);
        if (!force || !IsDirty)
            return;
        await RequestFlushAsync(token).ConfigureAwait(false);
    }

    public Task FlushProgressAsync(CancellationToken token = default) =>
        RequestFlushAsync(token);

    public Task FlushSelectionImmediatelyAsync(CancellationToken token = default) =>
        RequestFlushAsync(token);

    private Task RequestFlushAsync(CancellationToken token)
    {
        lock (_sync)
        {
            if (!_activeFlush.IsCompleted)
                return _activeFlush;
            _activeFlush = FlushLoopAsync(token);
            return _activeFlush;
        }
    }

    public void RegisterSettings(LocalSettingsSave settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
            _registeredSettings = settings;
    }

    public LocalSettingsSave? RegisteredSettings
    {
        get
        {
            lock (_sync)
                return _registeredSettings;
        }
    }

    public Task SaveSettingsAsync(
        LocalSettingsSave settings,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LocalSettingsSave selected;
        lock (_sync)
        {
            selected = _registeredSettings ?? settings;
        }
        return _store.SaveSettingsAsync(selected, token);
    }

    public Task SaveRegisteredSettingsAsync(CancellationToken token = default)
    {
        LocalSettingsSave settings;
        lock (_sync)
        {
            settings = _registeredSettings
                ?? throw new InvalidOperationException("No local settings snapshot is registered.");
        }
        return _store.SaveSettingsAsync(settings, token);
    }

    private async Task FlushLoopAsync(CancellationToken token)
    {
        try
        {
            if (!IsDirty)
                return;

            ProgressSnapshot snapshot = _progress.Snapshot();
            long selectionRevision = _selection?.Revision ?? 0;
            Guid? activeCharacterId = _selection?.ActiveCharacterId;
            WorkProgressSnapshot? workSnapshot = _work?.Snapshot();
            long workRevision = workSnapshot?.Revision ?? 0;
            ProgressSave save = ProgressSave.FromSnapshot(snapshot, activeCharacterId, workSnapshot);
            await _store.SaveProgressAsync(save, token).ConfigureAwait(false);
            Interlocked.Exchange(ref _savedRevision, snapshot.Revision);
            Interlocked.Exchange(ref _savedSelectionRevision, selectionRevision);
            Interlocked.Exchange(ref _savedWorkRevision, workRevision);
            _dirtyRunningSeconds = 0.0;
            LastFailure = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastFailure = exception;
            throw;
        }
    }

    private void OnProgressChanged(ProgressChange change)
    {
        if (change is ProgressChange.ToolUnlocked or ProgressChange.ContentPurchased)
            _ = FlushImmediateEventAsync();
    }

    private void OnSelectionChanged(Guid? activeCharacterId) =>
        _ = FlushImmediateEventAsync();

    private async Task FlushImmediateEventAsync()
    {
        try
        {
            await FlushProgressAsync().ConfigureAwait(false);
        }
        catch
        {
            // Failure remains visible through LastFailure and revisions remain dirty.
        }
    }
}