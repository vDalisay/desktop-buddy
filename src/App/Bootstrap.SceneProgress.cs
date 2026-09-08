using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.App;

public partial class Bootstrap
{
    /// <summary>
    /// Scene-enabled boot result plus the read-only aggregate projection still needed by staged
    /// legacy consumers. The Scene coordinator is authoritative before this compatibility DTO is
    /// constructed; the DTO must never be used as a writer after a Scene manifest exists.
    /// </summary>
    private sealed record SceneBootCompatibility(
        SceneProgressCoordinator SceneProgress,
        LoadResult<ProgressSave> CompatibilityProgressLoad);

    private async Task<SceneBootCompatibility> LoadSceneProgressAsync(
        IProgressStore legacyStore,
        IAtomicSaveFileSystem files,
        string progressPath,
        string saveRoot,
        double cashPerPain,
        bool browser,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(legacyStore);
        ArgumentNullException.ThrowIfNull(files);

        var transactions = new SceneProgressTransactionStore(saveRoot, files);
        var migration = new NextFestMigrationStore(progressPath, files);
        var bootstrap = new SceneProgressBootstrapCoordinator(
            DemoScope.ActiveBuildScope,
            cashPerPain,
            transactions,
            migration);
        ICharacterFileSystem assetFiles = browser
            ? new GodotBrowserCharacterFileSystem()
            : new CharacterFileSystem();
        var environmentAssets = new SceneEnvironmentAssetMigration(
            assetFiles,
            saveRoot,
            SceneId.LegacyHome);

        LoadResult<ProgressSave>? legacyLoad = null;
        async Task<ProgressSave> LoadLegacyAsync(CancellationToken loadToken)
        {
            LoadResult<ProgressSave> loaded = await legacyStore
                .LoadProgressAsync(loadToken)
                .ConfigureAwait(false);
            legacyLoad = loaded;

            if (loaded.Status == SaveLoadStatus.UnsupportedFutureVersion)
            {
                throw new InvalidDataException(
                    $"Legacy progress is from a newer build: {loaded.Detail}");
            }

            if (loaded.Status is SaveLoadStatus.NewSave or SaveLoadStatus.DefaultsRecovered)
            {
                BuddyProgressState fresh = ProgressReset.CreateNewProgress(cashPerPain);
                var work = new WorkProgressState();
                var environment = new EnvironmentProgressState();
                return ProgressSave.FromSnapshot(
                    fresh.Snapshot(),
                    activeCharacterId: null,
                    work: work.Snapshot(),
                    environment: environment.Snapshot());
            }

            return loaded.Value
                ?? throw new InvalidDataException(
                    $"Legacy progress could not be loaded for Scene migration: {loaded.Detail}");
        }

        SceneProgressBootstrapResult result = await bootstrap.LoadOrMigrateAsync(
            LoadLegacyAsync,
            // The schema-8 aggregate never persisted a Buddy room anchor. Use the deterministic
            // center anchor for its one-time migration; later Scene saves own placement explicitly.
            new CanonicalRoomPosition(0.5f, 0.5f),
            environmentAssets.CommitLegacyAssetsAsync,
            token).ConfigureAwait(false);

        if (!result.Coordinator.TryGetBuddy(
                BuddyIdentityId.LegacyPrimary,
                out BuddyIdentityState? primaryBuddy) ||
            primaryBuddy is null)
        {
            throw new InvalidDataException(
                "The current single-actor runtime requires the migrated primary Buddy identity.");
        }

        LegacyProgressAggregateSnapshot compatibility =
            LegacyProgressPartitionPolicy.RecombineForLegacy(
                new LegacyProgressPartition(
                    result.Coordinator.Player.Snapshot(),
                    primaryBuddy.Snapshot()));
        ProgressSave compatibilitySave = ProgressSave.FromSnapshot(
            compatibility.Progress,
            compatibility.ActiveCharacterId,
            result.Coordinator.Work.Snapshot(),
            result.Coordinator.ActiveEnvironmentProgress);

        // A committed generation deliberately never invokes the legacy loader. For migration,
        // retain the legacy load status so first-session guidance still distinguishes a true new
        // save from an existing player being upgraded.
        SaveLoadStatus compatibilityStatus = result.Source == SceneProgressBootstrapSource.CommittedGeneration
            ? SaveLoadStatus.Loaded
            : legacyLoad?.Status ?? SaveLoadStatus.Loaded;
        string? detail = result.Source == SceneProgressBootstrapSource.CommittedGeneration
            ? "Scene progress loaded from committed generation."
            : legacyLoad?.Detail;
        string? quarantinedPath = result.Source == SceneProgressBootstrapSource.LegacyMigration
            ? legacyLoad?.QuarantinedPath
            : null;

        return new SceneBootCompatibility(
            result.Coordinator,
            new LoadResult<ProgressSave>(
                compatibilityStatus,
                compatibilitySave,
                detail,
                quarantinedPath));
    }
}
