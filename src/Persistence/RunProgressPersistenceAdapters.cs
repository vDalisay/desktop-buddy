using System;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Persistence;

/// <summary>Legacy aggregate adapter for account-wide runtime save requests.</summary>
public sealed class LegacyRunProgressPersistence : IRunProgressPersistence
{
    private readonly SaveCoordinator _saves;

    public LegacyRunProgressPersistence(SaveCoordinator saves)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
    }

    public bool IsDirty => _saves.IsDirty;
    public Task TickAsync(double validRunningSeconds, CancellationToken token = default) =>
        _saves.TickAsync(validRunningSeconds, token);
    public Task FlushAsync(bool force, CancellationToken token = default) =>
        _saves.FlushProgressAsync(force, token);
}

/// <summary>Scene-enabled split graph adapter for account-wide runtime save requests.</summary>
public sealed class SceneRunProgressPersistence : IRunProgressPersistence
{
    private readonly SceneProgressCoordinator _saves;

    public SceneRunProgressPersistence(SceneProgressCoordinator saves)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
    }

    public bool IsDirty => _saves.IsDirty;
    public Task TickAsync(double validRunningSeconds, CancellationToken token = default) =>
        _saves.TickAsync(validRunningSeconds, token);
    public Task FlushAsync(bool force, CancellationToken token = default) =>
        _saves.FlushAsync(force, token);
}
