using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;

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

    /// <summary>
    /// Compatibility overload for tests/callers that already hold a proven legacy aggregate.
    /// Production bootstrap should prefer the lazy-loader overload so a committed Scene manifest
    /// is inspected before <c>progress.json</c> is ever decoded as the legacy schema.
    /// </summary>
    public Task<SceneProgressBootstrapResult> LoadOrMigrateAsync(
        ProgressSave legacy,
        CanonicalRoomPosition legacyBuddyPosition,
        Func<CancellationToken, Task>? commitLegacyAssetsAsync = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        return LoadOrMigrateAsync(
            _ => Task.FromResult(legacy),
            legacyBuddyPosition,
            commitLegacyAssetsAsync,
            token);
    }

    /// <summary>
    /// Chooses the persistence format before invoking <paramref name="loadLegacyAsync"/>. If a
    /// Scene manifest exists, its generation is authoritative even when malformed or dependent on
    /// staged recovery bytes: load failure is surfaced and never falls through to legacy decode.
    /// </summary>
    public async Task<SceneProgressBootstrapResult> LoadOrMigrateAsync(
        Func<CancellationToken, Task<ProgressSave>> loadLegacyAsync,
        CanonicalRoomPosition legacyBuddyPosition,
        Func<CancellationToken, Task>? commitLegacyAssetsAsync = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(loadLegacyAsync);

        if (_transactions.HasCommittedGeneration)
        {
            SceneProgressLoadResult loaded = await _transactions
                .LoadCommittedAsync(_cashPerPain, token)
                .ConfigureAwait(false);
            return new SceneProgressBootstrapResult(
                CreateCoordinator(loaded),
                SceneProgressBootstrapSource.CommittedGeneration,
                // LoadCommittedAsync may have resolved one or more documents from .next after an
                // interrupted post-manifest promotion. Until the transaction store reports exact
                // canonical completeness, never claim that a successful load proves promotion.
                CanonicalPromotionComplete: false);
        }

        ProgressSave legacy = await loadLegacyAsync(token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Legacy progress loader returned no aggregate.");

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
            environment.Snapshot(),
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
