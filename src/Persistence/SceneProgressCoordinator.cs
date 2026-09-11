using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Run-lifetime owner for the Scene-enabled persistence graph. It keeps exactly one account state
/// and Work state, a library of persistent Buddy identities, and one ordered Scene library. All
/// durable writes go through <see cref="SceneProgressTransactionStore"/> so autosave can never expose
/// player, Buddy, Work and Scene documents from different generations.
///
/// Initial Steam Demo/itch builds deliberately continue to use <see cref="SaveCoordinator"/> and the
/// legacy aggregate until their product surface is upgraded. This coordinator is the first-class
/// injected capability for Next Fest and Full Release code.
/// </summary>
public sealed partial class SceneProgressCoordinator
{
    public const double AutosaveSeconds = 30.0;

    private readonly object _sync = new();
    private readonly BuildScopePolicy _scope;
    private readonly SceneProgressTransactionStore _store;
    private readonly SceneLibraryState _scenes;
    private readonly Dictionary<BuddyIdentityId, BuddyIdentityState> _buddies = new();
    private readonly Dictionary<SceneId, SandboxDocument> _sandboxes = new();
    private readonly Dictionary<BuddyIdentityId, long> _savedBuddyRevisions = new();
    private Task _activeFlush = Task.CompletedTask;
    private long _savedPlayerRevision;
    private long _savedWorkRevision;
    private long _sceneRevision;
    private long _savedSceneRevision;
    private long _identityLibraryRevision;
    private long _savedIdentityLibraryRevision;
    private long _savedSandboxRevision;
    private double _dirtyRunningSeconds;

    public SceneProgressCoordinator(
        BuildScopePolicy scope,
        PlayerProgressState player,
        WorkProgressState work,
        IEnumerable<BuddyIdentityState> buddyIdentities,
        IEnumerable<SceneDocument> scenes,
        SceneId activeSceneId,
        SceneProgressTransactionStore store,
        long committedRevision = -1,
        IReadOnlyDictionary<SceneId, SandboxDocument>? sandboxes = null)
    {
        if (!scope.IncludesScenes)
            throw new ArgumentException("Scene progress can only be composed for a Scene-enabled build scope.", nameof(scope));
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Work = work ?? throw new ArgumentNullException(nameof(work));
        ArgumentNullException.ThrowIfNull(buddyIdentities);
        ArgumentNullException.ThrowIfNull(scenes);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scope = scope;

        foreach (BuddyIdentityState buddy in buddyIdentities)
        {
            ArgumentNullException.ThrowIfNull(buddy);
            if (!_buddies.TryAdd(buddy.BuddyIdentityId, buddy))
                throw new ArgumentException("Scene progress contains a duplicate Buddy identity.", nameof(buddyIdentities));
        }

        SceneDocument[] sceneDocuments = scenes.ToArray();
        if (sceneDocuments.Length == 0)
            throw new ArgumentException("Scene progress requires at least one Scene.", nameof(scenes));
        _scenes = new SceneLibraryState(scope, sceneDocuments, activeSceneId);
        ValidateSceneBuddyReferences();

        if (sandboxes is not null)
        {
            foreach ((SceneId sceneId, SandboxDocument document) in sandboxes)
            {
                if (_scenes.TryGet(sceneId, out _))
                    _sandboxes[sceneId] = document;
            }
        }

        _savedPlayerRevision = Player.Revision;
        _savedWorkRevision = Work.Revision;
        _savedSandboxRevision = SandboxRevision;
        foreach ((BuddyIdentityId id, BuddyIdentityState buddy) in _buddies)
            _savedBuddyRevisions[id] = buddy.Revision;
        LastCommittedRevision = committedRevision;
    }

    public PlayerProgressState Player { get; }
    public WorkProgressState Work { get; }
    public BuildScopePolicy Scope => _scope;
    public SceneId ActiveSceneId => _scenes.ActiveSceneId;
    public SceneDocument ActiveScene => _scenes.ActiveScene
        ?? throw new InvalidOperationException("Scene-enabled progress requires one active Scene.");
    public EnvironmentProgressSnapshot ActiveEnvironmentProgress => ActiveScene.EnvironmentProgress;
    public IReadOnlyList<SceneDocument> Scenes => _scenes.Scenes;
    public int SceneCount => _scenes.Count;
    public int BuddyIdentityCount => _buddies.Count;
    public bool CanCreateScene => _scenes.CanCreate;
    public long LastCommittedRevision { get; private set; }
    public bool LastCanonicalPromotionComplete { get; private set; } = true;
    public Exception? LastFailure { get; private set; }

    public bool IsDirty
    {
        get
        {
            // A fresh graph has no durable generation even when every semantic object's own
            // revision happens to match the in-memory saved-revision baselines. Keep it dirty until
            // the transaction store has produced the first manifest. Loaded/migrated coordinators
            // pass their committed revision and remain clean when otherwise unchanged.
            if (LastCommittedRevision < 0)
                return true;

            if (Player.Revision != Interlocked.Read(ref _savedPlayerRevision) ||
                Work.Revision != Interlocked.Read(ref _savedWorkRevision) ||
                _sceneRevision != Interlocked.Read(ref _savedSceneRevision) ||
                _identityLibraryRevision != Interlocked.Read(ref _savedIdentityLibraryRevision) ||
                SandboxRevision != Interlocked.Read(ref _savedSandboxRevision))
            {
                return true;
            }

            // Flush continuation may update the saved-revision map on a pool thread while the Godot
            // main thread asks IsDirty. Guard that map with the same lock that serializes flushes;
            // _buddies itself is run-lifetime composition state and mutates only on the owning thread.
            lock (_sync)
            {
                if (_savedBuddyRevisions.Count != _buddies.Count)
                    return true;
                foreach ((BuddyIdentityId id, BuddyIdentityState buddy) in _buddies)
                {
                    if (!_savedBuddyRevisions.TryGetValue(id, out long saved) || buddy.Revision != saved)
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Systemic construction for one Scene, created empty on first use. Parts live in their own
    /// per-Scene document, not in the Scene root, so construction can version itself separately.
    /// </summary>
    public SandboxDocument SandboxFor(SceneId sceneId)
    {
        if (!_scenes.TryGet(sceneId, out _))
            throw new KeyNotFoundException($"Scene library has no Scene {sceneId}.");
        if (!_sandboxes.TryGetValue(sceneId, out SandboxDocument? document))
        {
            document = new SandboxDocument();
            _sandboxes[sceneId] = document;
        }
        return document;
    }

    public SandboxDocument ActiveSandbox => SandboxFor(ActiveSceneId);

    /// <summary>Aggregate part-state revision, so any room's edit marks the graph dirty.</summary>
    private long SandboxRevision
    {
        get
        {
            long total = 0;
            foreach (SandboxDocument document in _sandboxes.Values)
                total += document.Revision;
            return total;
        }
    }

    public IReadOnlyList<BuddyIdentityState> BuddyIdentities() =>
        _buddies.Values.OrderBy(buddy => buddy.BuddyIdentityId).ToArray();

    public bool TryGetBuddy(BuddyIdentityId buddyIdentityId, out BuddyIdentityState? buddy) =>
        _buddies.TryGetValue(buddyIdentityId, out buddy);

    public bool RegisterBuddyIdentity(BuddyIdentityState buddy)
    {
        ArgumentNullException.ThrowIfNull(buddy);
        if (!_buddies.TryAdd(buddy.BuddyIdentityId, buddy))
            return false;
        Touch(ref _identityLibraryRevision);
        return true;
    }

    public bool RemoveBuddyIdentity(BuddyIdentityId buddyIdentityId)
    {
        if (!buddyIdentityId.IsValid || !_buddies.ContainsKey(buddyIdentityId))
            return false;
        if (_scenes.Scenes.Any(scene => scene.BuddyPlacements.Any(p => p.BuddyIdentityId == buddyIdentityId)))
            return false;
        if (!_buddies.Remove(buddyIdentityId))
            return false;
        Touch(ref _identityLibraryRevision);
        return true;
    }

    public SceneLibraryResult CreateScene(string name, EnvironmentLayout? environment = null) =>
        TrackSceneMutation(_scenes.Create(name, environment));

    public SceneLibraryResult CreateScene(string name, EnvironmentProgressSnapshot environment) =>
        TrackSceneMutation(_scenes.Create(name, environment));

    public SceneLibraryResult SwitchScene(SceneId sceneId) =>
        TrackSceneMutation(_scenes.Switch(sceneId));

    public SceneLibraryResult RenameScene(SceneId sceneId, string newName) =>
        TrackSceneMutation(_scenes.Rename(sceneId, newName));

    public SceneLibraryResult SetBuddiesCollide(SceneId sceneId, bool collide) =>
        TrackSceneMutation(_scenes.SetBuddiesCollide(sceneId, collide));

    public SceneLibraryResult DuplicateScene(SceneId sourceSceneId, string? newName = null)
    {
        SceneLibraryResult result = TrackSceneMutation(_scenes.Duplicate(sourceSceneId, newName));
        if (result.Succeeded && result.Scene is { } copy &&
            _sandboxes.TryGetValue(sourceSceneId, out SandboxDocument? source))
        {
            // The copy gets its own parts at the same places, under their own identities.
            _sandboxes[copy.SceneId] = source.CopyWithNewPartIds();
        }
        return result;
    }

    public SceneLibraryResult DeleteScene(SceneId sceneId)
    {
        SceneLibraryResult result = TrackSceneMutation(_scenes.Delete(sceneId));
        if (result.Succeeded)
            _sandboxes.Remove(sceneId);
        return result;
    }

    public SceneLibraryResult AddBuddyToScene(
        SceneId sceneId,
        BuddyIdentityId buddyIdentityId,
        CanonicalRoomPosition position)
    {
        if (!_buddies.ContainsKey(buddyIdentityId))
            return new SceneLibraryResult(SceneLibraryStatus.BuddyNotFound);
        return TrackSceneMutation(_scenes.AddBuddy(sceneId, buddyIdentityId, position));
    }

    public SceneLibraryResult RemoveBuddyFromScene(SceneId sceneId, BuddyIdentityId buddyIdentityId) =>
        TrackSceneMutation(_scenes.RemoveBuddy(sceneId, buddyIdentityId));

    public SceneLibraryResult MoveBuddy(
        SceneId sceneId,
        BuddyPlacementId placementId,
        CanonicalRoomPosition position) =>
        TrackSceneMutation(_scenes.MoveBuddy(sceneId, placementId, position));

    /// <summary>
    /// Applies one Room Decorator working copy and its wallet result as one Scene-progress
    /// generation. The editor must have opened from this exact Scene Environment snapshot. Any
    /// failed pre-manifest write restores the exact account and Scene snapshots in place; the
    /// transaction store never throws a post-manifest promotion failure, so a returned success is
    /// always the durable commit point even when canonical promotion remains recoverable work.
    /// </summary>
    public async Task CommitEnvironmentAsync(
        SceneId sceneId,
        EnvironmentEditSession session,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsDirty)
            return;

        // Serialize behind any existing autosave first. Using force here also handles the narrow
        // race where another flush starts between this await and the edit mutation: the second
        // forced flush below observes the edit as still dirty and commits another generation.
        await FlushAsync(force: true, token).ConfigureAwait(false);

        if (!_scenes.TryGet(sceneId, out SceneDocument? scene) || scene is null)
            throw new KeyNotFoundException($"Scene library has no Scene {sceneId}.");
        EnvironmentProgressSnapshot environmentBefore = scene.EnvironmentProgress;
        if (!session.MatchesBaseline(environmentBefore))
            throw new InvalidOperationException("The Scene environment changed while the room edit session was open.");

        PlayerProgressSnapshot playerBefore = Player.Snapshot();
        if (playerBefore.Revision == long.MaxValue || environmentBefore.Revision == long.MaxValue)
            throw new InvalidOperationException("The progress revision is exhausted.");
        if (!session.TryPrepareCommit(playerBefore.BalanceMilliCredits, out EnvironmentCommit commit))
            throw new InvalidOperationException("Current funds are insufficient for the staged room changes.");

        var playerAfter = playerBefore with
        {
            Revision = playerBefore.Revision + 1,
            BalanceMilliCredits = commit.BalanceMilliCredits,
        };
        var environmentAfter = new EnvironmentProgressSnapshot(
            environmentBefore.Revision + 1,
            commit.Layout,
            commit.OwnedUnplaced);
        long sceneRevisionBefore = _sceneRevision;

        Player.Adopt(playerAfter);
        SceneLibraryResult changed = _scenes.UpdateEnvironment(sceneId, environmentAfter);
        if (!changed.Succeeded)
        {
            Player.Adopt(playerBefore);
            throw new InvalidOperationException($"Scene environment update failed: {changed.Status}.");
        }
        Touch(ref _sceneRevision);

        try
        {
            await FlushAsync(force: true, token).ConfigureAwait(false);
        }
        catch
        {
            Player.Adopt(playerBefore);
            SceneLibraryResult restored = _scenes.UpdateEnvironment(sceneId, environmentBefore);
            _sceneRevision = sceneRevisionBefore;
            if (!restored.Succeeded)
                throw new InvalidOperationException("Scene environment rollback could not restore the prior document.");
            throw;
        }
    }

    public SceneProgressBindingRegistry CreateBindings(SceneId sceneId)
    {
        if (!_scenes.TryGet(sceneId, out SceneDocument? scene) || scene is null)
            throw new KeyNotFoundException($"Scene library has no Scene {sceneId}.");

        var referenced = new List<BuddyIdentityState>(scene.BuddyPlacements.Count);
        foreach (BuddyPlacement placement in scene.BuddyPlacements)
        {
            if (!_buddies.TryGetValue(placement.BuddyIdentityId, out BuddyIdentityState? buddy))
                throw new InvalidOperationException($"Scene references missing Buddy identity {placement.BuddyIdentityId}.");
            referenced.Add(buddy);
        }
        return new SceneProgressBindingRegistry(Player, scene, referenced);
    }

    public SceneProgressBindingRegistry CreateActiveBindings() => CreateBindings(ActiveSceneId);

    public Task TickAsync(double validRunningSeconds, CancellationToken token = default)
    {
        if (validRunningSeconds <= 0.0 || !IsDirty)
            return Task.CompletedTask;
        _dirtyRunningSeconds += validRunningSeconds;
        if (_dirtyRunningSeconds < AutosaveSeconds)
            return Task.CompletedTask;
        return RequestFlushAsync(token);
    }

    public Task FlushAsync(CancellationToken token = default) => RequestFlushAsync(token);

    public async Task FlushAsync(bool force, CancellationToken token = default)
    {
        await RequestFlushAsync(token).ConfigureAwait(false);
        if (force && IsDirty)
            await RequestFlushAsync(token).ConfigureAwait(false);
    }

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

    private async Task FlushLoopAsync(CancellationToken token)
    {
        try
        {
            if (!IsDirty)
                return;

            CapturedGeneration captured = CaptureGeneration();
            SceneProgressCommitResult committed = await _store.CommitAsync(
                captured.Player,
                captured.Work,
                captured.Buddies,
                captured.Scenes,
                captured.Sandboxes,
                captured.ActiveSceneId,
                token).ConfigureAwait(false);

            Interlocked.Exchange(ref _savedPlayerRevision, captured.PlayerRevision);
            Interlocked.Exchange(ref _savedWorkRevision, captured.WorkRevision);
            Interlocked.Exchange(ref _savedSceneRevision, captured.SceneRevision);
            Interlocked.Exchange(ref _savedIdentityLibraryRevision, captured.IdentityLibraryRevision);
            Interlocked.Exchange(ref _savedSandboxRevision, captured.SandboxRevision);
            lock (_sync)
            {
                _savedBuddyRevisions.Clear();
                foreach ((BuddyIdentityId id, long revision) in captured.BuddyRevisions)
                    _savedBuddyRevisions[id] = revision;
            }
            LastCommittedRevision = committed.Revision;
            LastCanonicalPromotionComplete = committed.CanonicalPromotionComplete;
            _dirtyRunningSeconds = 0.0;
            LastFailure = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastFailure = exception;
            throw;
        }
    }

    private CapturedGeneration CaptureGeneration()
    {
        PlayerProgressSnapshot playerSnapshot = Player.Snapshot();
        WorkProgressSnapshot workSnapshot = Work.Snapshot();
        BuddyIdentitySnapshot[] buddySnapshots = _buddies.Values
            .OrderBy(buddy => buddy.BuddyIdentityId)
            .Select(buddy => buddy.Snapshot())
            .ToArray();
        SceneDocument[] scenes = _scenes.Scenes.ToArray();
        SceneId activeSceneId = _scenes.ActiveSceneId;
        // Parts are value-copied for the same reason Buddy states are: the commit must not observe
        // a room the player keeps editing while the write is in flight.
        var sandboxCopies = new Dictionary<SceneId, SandboxDocument>(_sandboxes.Count);
        foreach ((SceneId sceneId, SandboxDocument document) in _sandboxes)
            sandboxCopies[sceneId] = new SandboxDocument(document.Parts, document.Revision);
        long sandboxRevision = SandboxRevision;

        var playerCopy = new PlayerProgressState(Player.CashPerPain, playerSnapshot);
        var workCopy = new WorkProgressState(
            workSnapshot.Lifetime,
            workSnapshot.ClaimedLifetimeMilestoneIds,
            workSnapshot.FirstEntryGlassesGranted,
            workSnapshot.Revision,
            workSnapshot.ActiveSession);
        BuddyIdentityState[] buddyCopies = buddySnapshots
            .Select(snapshot => new BuddyIdentityState(snapshot))
            .ToArray();
        var revisions = buddySnapshots.ToDictionary(
            snapshot => snapshot.BuddyIdentityId,
            snapshot => snapshot.Revision);

        return new CapturedGeneration(
            playerCopy,
            workCopy,
            buddyCopies,
            scenes,
            activeSceneId,
            playerSnapshot.Revision,
            workSnapshot.Revision,
            revisions,
            _sceneRevision,
            _identityLibraryRevision,
            sandboxCopies,
            sandboxRevision);
    }

    private SceneLibraryResult TrackSceneMutation(SceneLibraryResult result)
    {
        if (result.Status == SceneLibraryStatus.Succeeded)
            Touch(ref _sceneRevision);
        return result;
    }

    private void ValidateSceneBuddyReferences()
    {
        foreach (SceneDocument scene in _scenes.Scenes)
        {
            foreach (BuddyPlacement placement in scene.BuddyPlacements)
            {
                if (!_buddies.ContainsKey(placement.BuddyIdentityId))
                {
                    throw new ArgumentException(
                        $"Scene {scene.SceneId} references Buddy {placement.BuddyIdentityId}, but that identity was not loaded.");
                }
            }
        }
    }

    private static void Touch(ref long revision)
    {
        if (revision != long.MaxValue)
            revision++;
    }

    private sealed record CapturedGeneration(
        PlayerProgressState Player,
        WorkProgressState Work,
        IReadOnlyCollection<BuddyIdentityState> Buddies,
        IReadOnlyList<SceneDocument> Scenes,
        SceneId ActiveSceneId,
        long PlayerRevision,
        long WorkRevision,
        IReadOnlyDictionary<BuddyIdentityId, long> BuddyRevisions,
        long SceneRevision,
        long IdentityLibraryRevision,
        IReadOnlyDictionary<SceneId, SandboxDocument> Sandboxes,
        long SandboxRevision);
}
