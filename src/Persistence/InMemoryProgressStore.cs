using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Persistence;

/// <summary>Scenario/laboratory store; never resolves or touches user://.</summary>
public sealed class InMemoryProgressStore : IProgressStore
{
    public ProgressSave? Progress { get; private set; }
    public LocalSettingsSave? Settings { get; private set; }
    public int ProgressWriteCount { get; private set; }
    public int SettingsWriteCount { get; private set; }
    public Exception? NextProgressFailure { get; set; }
    public Exception? NextSettingsFailure { get; set; }

    public Task<LoadResult<ProgressSave>> LoadProgressAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Progress is null
            ? new LoadResult<ProgressSave>(SaveLoadStatus.NewSave, new ProgressSave())
            : new LoadResult<ProgressSave>(SaveLoadStatus.Loaded, Progress));
    }

    public Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Settings is null
            ? new LoadResult<LocalSettingsSave>(SaveLoadStatus.NewSave, new LocalSettingsSave())
            : new LoadResult<LocalSettingsSave>(SaveLoadStatus.Loaded, Settings));
    }

    public Task SaveProgressAsync(ProgressSave data, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (NextProgressFailure is Exception failure)
        {
            NextProgressFailure = null;
            return Task.FromException(failure);
        }
        ProgressSavePolicy.Validate(data);
        Progress = data;
        ProgressWriteCount++;
        return Task.CompletedTask;
    }

    public Task SaveSettingsAsync(LocalSettingsSave data, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Settings = data;
        SettingsWriteCount++;
        return Task.CompletedTask;
    }
}
