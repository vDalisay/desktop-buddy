using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Durable Initial Demo -> Scene progress migration writer. The legacy progress document remains
/// the commit marker until every dependent split document (and any caller-owned binary assets) has
/// committed. Replacing progress.json with the account-only document is deliberately the final write,
/// so a failure before that point leaves the old save authoritative and safely retryable.
/// </summary>
public sealed class NextFestMigrationStore
{
    private readonly string _progressPath;
    private readonly string _saveRoot;
    private readonly IAtomicSaveFileSystem _files;

    public NextFestMigrationStore(
        string progressPath,
        IAtomicSaveFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(progressPath))
            throw new ArgumentException("A resolved progress path is required.", nameof(progressPath));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _progressPath = Path.GetFullPath(progressPath);
        _saveRoot = Path.GetDirectoryName(_progressPath)
            ?? throw new ArgumentException("The progress path requires a parent directory.", nameof(progressPath));
    }

    /// <summary>
    /// Commits one deterministic legacy migration. The optional asset callback is invoked after the
    /// JSON dependents are durable but before account progress becomes the authoritative root. It is
    /// intended for the legacy room-paint copy/move, whose binary filesystem contract lives outside
    /// this text store.
    /// </summary>
    public async Task<CommittedSceneProgress> CommitLegacyMigrationAsync(
        LegacyNextFestMigrationProjection projection,
        double cashPerPain,
        Func<CancellationToken, Task>? commitAssetsAsync = null,
        CancellationToken token = default)
    {
        var player = new PlayerProgressState(cashPerPain, projection.Player);
        var buddy = new BuddyIdentityState(projection.Buddy);
        var registry = new SceneProgressBindingRegistry(player, projection.Scene, [buddy]);

        string buddyJson = await PersistenceWork.Run(
            () => BuddyIdentitySavePolicy.Serialize(buddy), token).ConfigureAwait(false);
        string sceneJson = await PersistenceWork.Run(
            () => SceneSavePolicy.SerializeScene(projection.Scene), token).ConfigureAwait(false);
        var index = new SceneIndexSave
        {
            Revision = 0,
            ActiveSceneId = projection.Scene.SceneId.Value,
            OrderedSceneIds = [projection.Scene.SceneId.Value],
        };
        string indexJson = await PersistenceWork.Run(
            () => SceneSavePolicy.SerializeIndex(index), token).ConfigureAwait(false);
        string playerJson = await PersistenceWork.Run(
            () => PlayerProgressSavePolicy.Serialize(player), token).ConfigureAwait(false);

        // Dependency-first commit order. Reserved legacy IDs make every destination deterministic,
        // so overwriting a partial prior attempt is safe.
        await SaveAtomicAsync(
            ResolveRelative(SceneStoragePaths.BuddyIdentity(projection.Buddy.BuddyIdentityId)),
            buddyJson,
            token).ConfigureAwait(false);
        await SaveAtomicAsync(
            ResolveRelative(SceneStoragePaths.SceneDocument(projection.Scene.SceneId)),
            sceneJson,
            token).ConfigureAwait(false);
        await SaveAtomicAsync(
            ResolveRelative(SceneStoragePaths.SceneIndex),
            indexJson,
            token).ConfigureAwait(false);

        if (commitAssetsAsync is not null)
            await commitAssetsAsync(token).ConfigureAwait(false);

        // Commit point. If this fails, progress.json is still the valid legacy document (or its
        // previous committed account document on an idempotent retry) and the partial dependents are
        // harmless deterministic projections.
        await SaveAtomicAsync(_progressPath, playerJson, token).ConfigureAwait(false);

        return new CommittedSceneProgress(player, buddy, projection.Scene, registry);
    }

    private string ResolveRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new ArgumentException("Scene storage keys must be non-empty relative paths.", nameof(relative));

        string platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(_saveRoot, platformRelative));
        string back = Path.GetRelativePath(_saveRoot, full);
        if (back == ".." || back.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene storage key escaped the trusted save root.");
        return full;
    }

    private Task SaveAtomicAsync(string primary, string json, CancellationToken token) =>
        PersistenceWork.Run(() => SaveAtomic(primary, json, token), token);

    private void SaveAtomic(string primary, string json, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(primary)
            ?? throw new InvalidOperationException("Save path has no parent directory.");
        _files.CreateDirectory(directory);
        string temporary = primary + ".tmp";
        _files.WriteDurable(temporary, json);
        token.ThrowIfCancellationRequested();
        if (_files.Exists(primary))
            _files.Replace(temporary, primary, primary + ".bak");
        else
            _files.Move(temporary, primary);
    }
}

public sealed record CommittedSceneProgress(
    PlayerProgressState Player,
    BuddyIdentityState Buddy,
    SceneDocument Scene,
    SceneProgressBindingRegistry Registry);
