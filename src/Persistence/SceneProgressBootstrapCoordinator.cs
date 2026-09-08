using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;

namespace DesktopBuddy.Persistence;

public enum SceneProgressBootstrapSource
{
    CommittedGeneration = 0,
    LegacyMigration = 1,
}

/// <summary>
/// Result of choosing the authoritative persistence model for one Scene-enabled run. The returned
/// coordinator is always backed by the same transaction store that supplied or committed the graph,
/// so later autosaves continue the exact generation chain selected at boot.
/// </summary>
public sealed record SceneProgressBootstrapResult(
    SceneProgressCoordinator Coordinator,
    SceneProgressBootstrapSource Source,
    bool CanonicalPromotionComplete);

/// <summary>
/// Managed-only format router used by Bootstrap before any Godot runtime composition occurs.
/// A committed Scene manifest always wins. Without one, exactly one deterministic projection of the
/// legacy aggregate is committed, including Work progress, before split state becomes authoritative.
///
/// The legacy aggregate is never rewritten by this type. Failure before the Scene transaction's
/// manifest commit therefore leaves it authoritative and retryable; failure after the manifest commit
/// is recovered by <see cref="SceneProgressTransactionStore"/> on the next call.
/// </summary>
public sealed class SceneProgressBootstrapCoordinator
{
    private readonly BuildScopePolicy _scope;
    private readonly double _cashPerPain;
    private readonly SceneProgressTransactionStore _transactions;
    private readonly NextFestMigrationStore _migration;

    public SceneProgressBootstrapCoordinator(
        BuildScopePolicy scope,
        double cashPerPain,
        SceneProgressTransactionStore transactions,
        NextFestMigrationStore migration)
    {
        if (!scope.IncludesScenes)
            throw new ArgumentException("Scene bootstrap requires a Scene-enabled build scope.", nameof(scope));
        if (!double.IsFinite(cashPerPain) || cashPerPain < 0.0)
            throw new ArgumentOutOfRangeException(nameof(cashPerPain));
        _scope = scope;
        _cashPerPain = cashPerPain;
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _migration = migration ?? throw new ArgumentNullException(nameof(migration));
    }

    public async Task<SceneProgressBootstrapResult> LoadOrMigrateAsync(
        ProgressSave legacy,
        CanonicalRoomPosition legacyBuddyPosition,
        Func<CancellationToken, Task>? commitLegacyAssetsAsync = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        if (_transactions.HasCommittedGeneration)
        {
            SceneProgressLoadResult loaded = await _transactions
                .LoadCommittedAsync(_cashPerPain, token)
                .ConfigureAwait(false);
            return new SceneProgressBootstrapResult(
                CreateCoordinator(loaded),
                SceneProgressBootstrapSource.CommittedGeneration,
                CanonicalPromotionComplete: true);
        }

        // Reconstruct through the existing legacy policy before partitioning. This applies all
        // schema/default/unknown-ID normalization in one place instead of teaching migration a
        // second interpretation of the aggregate DTO.
        BuddyProgressState legacyProgress = ProgressSavePolicy.CreateState(legacy, _cashPerPain);
        WorkProgressState work = legacy.Work?.CreateState() ?? new WorkProgressState();
        EnvironmentProgressState environment = legacy.Environment?.CreateState()
            ?? new EnvironmentProgressState();
        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            legacyProgress.Snapshot(),
            legacy.ActiveCharacterId,
            environment.Layout,
            legacyBuddyPosition);

        CommittedSceneProgress migrated = await _migration.CommitLegacyMigrationAsync(
            projection,
            _cashPerPain,
            work,
            commitLegacyAssetsAsync,
            token).ConfigureAwait(false);

        var coordinator = new SceneProgressCoordinator(
            _scope,
            migrated.Player,
            migrated.Work,
            [migrated.Buddy],
            [migrated.Scene],
            migrated.Scene.SceneId,
            _transactions,
            migrated.CommitRevision);
        return new SceneProgressBootstrapResult(
            coordinator,
            SceneProgressBootstrapSource.LegacyMigration,
            migrated.CanonicalPromotionComplete);
    }

    private SceneProgressCoordinator CreateCoordinator(SceneProgressLoadResult loaded) =>
        new(
            _scope,
            loaded.Player,
            loaded.Work,
            loaded.BuddyIdentities,
            loaded.Scenes,
            SceneId.From(loaded.Index.ActiveSceneId),
            _transactions,
            loaded.Revision);
}
