using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Focused persistence capability for the Room Decorator. The UI owns an
/// <see cref="EnvironmentEditSession"/> and knows nothing about whether the active build persists
/// the room inside the legacy aggregate or inside a Scene transaction generation.
/// </summary>
public interface IEnvironmentProgressPersistence
{
    long BalanceMilliCredits { get; }
    EnvironmentProgressSnapshot Snapshot();
    Task CommitAsync(EnvironmentEditSession session, CancellationToken token = default);
}

/// <summary>Initial Demo/legacy adapter over the existing aggregate transaction.</summary>
public sealed class LegacyEnvironmentProgressPersistence : IEnvironmentProgressPersistence
{
    private readonly BuddyProgressState _progress;
    private readonly EnvironmentProgressState _environment;
    private readonly SaveCoordinator _saves;

    public LegacyEnvironmentProgressPersistence(
        BuddyProgressState progress,
        EnvironmentProgressState environment,
        SaveCoordinator saves)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
    }

    public long BalanceMilliCredits => _progress.BalanceMilliCredits;
    public EnvironmentProgressSnapshot Snapshot() => _environment.Snapshot();

    public Task CommitAsync(EnvironmentEditSession session, CancellationToken token = default) =>
        _saves.CommitEnvironmentAsync(session, token);
}

/// <summary>
/// Scene-enabled adapter. The mutable <paramref name="view"/> is a presentation bridge only; the
/// authoritative document remains in <see cref="SceneProgressCoordinator"/>. After a successful
/// manifest commit the bridge adopts the exact committed Scene snapshot so existing presentation
/// nodes can continue observing one stable <see cref="EnvironmentProgressState"/> instance.
/// </summary>
public sealed class SceneEnvironmentProgressPersistence : IEnvironmentProgressPersistence
{
    private readonly SceneProgressCoordinator _scenes;
    private readonly SceneId _sceneId;
    private readonly EnvironmentProgressState _view;

    public SceneEnvironmentProgressPersistence(
        SceneProgressCoordinator scenes,
        SceneId sceneId,
        EnvironmentProgressState view)
    {
        _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        if (!sceneId.IsValid)
            throw new ArgumentException("Environment persistence requires a stable Scene ID.", nameof(sceneId));
        _sceneId = sceneId;
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public long BalanceMilliCredits => _scenes.Player.BalanceMilliCredits;
    public EnvironmentProgressSnapshot Snapshot()
    {
        if (_scenes.ActiveSceneId != _sceneId)
        {
            throw new InvalidOperationException(
                "The active Scene changed; reopen the Room Decorator in the current Scene.");
        }
        return _scenes.ActiveEnvironmentProgress;
    }

    public async Task CommitAsync(EnvironmentEditSession session, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_scenes.ActiveSceneId != _sceneId)
        {
            throw new InvalidOperationException(
                "The active Scene changed while the Room Decorator was open; reopen it in the current Scene.");
        }

        await _scenes.CommitEnvironmentAsync(_sceneId, session, token).ConfigureAwait(false);
        _view.Adopt(_scenes.ActiveEnvironmentProgress);
    }
}
