using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Durable Initial Demo -> Scene progress migration coordinator. Binary Scene assets are committed
/// first; the split semantic graph is then committed through <see cref="SceneProgressTransactionStore"/>.
/// Until that manifest commit point the legacy aggregate progress.json remains authoritative and a
/// failed attempt is safely retryable because all migration IDs are deterministic.
/// </summary>
public sealed class NextFestMigrationStore
{
    private readonly SceneProgressTransactionStore _transactions;

    public NextFestMigrationStore(
        string progressPath,
        IAtomicSaveFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(progressPath))
            throw new ArgumentException("A resolved progress path is required.", nameof(progressPath));
        ArgumentNullException.ThrowIfNull(files);
        string fullProgressPath = Path.GetFullPath(progressPath);
        string saveRoot = Path.GetDirectoryName(fullProgressPath)
            ?? throw new ArgumentException("The progress path requires a parent directory.", nameof(progressPath));
        _transactions = new SceneProgressTransactionStore(saveRoot, files);
    }

    /// <summary>
    /// Commits one deterministic legacy migration. The asset callback runs before the semantic
    /// transaction and is intended for the legacy room-paint copy/move. If it fails there is no
    /// Scene progress manifest and the old aggregate remains authoritative. Once the manifest
    /// commits, promotion to canonical text paths is recoverable maintenance and does not roll back
    /// the migrated runtime graph.
    /// </summary>
    public async Task<CommittedSceneProgress> CommitLegacyMigrationAsync(
        LegacyNextFestMigrationProjection projection,
        double cashPerPain,
        WorkProgressState workProgress,
        Func<CancellationToken, Task>? commitAssetsAsync = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(workProgress);
        var player = new PlayerProgressState(cashPerPain, projection.Player);
        var buddy = new BuddyIdentityState(projection.Buddy);
        var registry = new SceneProgressBindingRegistry(player, projection.Scene, [buddy]);

        if (commitAssetsAsync is not null)
            await commitAssetsAsync(token).ConfigureAwait(false);

        SceneProgressCommitResult committed = await _transactions.CommitAsync(
            player,
            workProgress,
            [buddy],
            [projection.Scene],
            projection.Scene.SceneId,
            token).ConfigureAwait(false);

        return new CommittedSceneProgress(
            player,
            workProgress,
            buddy,
            projection.Scene,
            registry,
            committed.Revision,
            committed.CanonicalPromotionComplete);
    }
}

public sealed record CommittedSceneProgress(
    PlayerProgressState Player,
    WorkProgressState Work,
    BuddyIdentityState Buddy,
    SceneDocument Scene,
    SceneProgressBindingRegistry Registry,
    long CommitRevision,
    bool CanonicalPromotionComplete);
