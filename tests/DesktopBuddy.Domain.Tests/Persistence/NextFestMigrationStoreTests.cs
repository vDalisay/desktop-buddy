using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class NextFestMigrationStoreTests
{
    [Fact]
    public async Task Successful_migration_commits_work_and_scene_graph_under_one_manifest_and_keeps_legacy_backup()
    {
        var files = new MemoryFiles();
        BuddyProgressState legacy = LegacyProgress();
        WorkProgressState work = LegacyWork();
        files.Set(
            ProgressPath,
            ProgressSavePolicy.Serialize(ProgressSave.FromSnapshot(legacy.Snapshot(), work: work.Snapshot())));
        LegacyNextFestMigrationProjection projection = Projection(legacy);
        var store = new NextFestMigrationStore(ProgressPath, files);

        CommittedSceneProgress committed = await store.CommitLegacyMigrationAsync(
            projection,
            cashPerPain: CashPerPain,
            workProgress: work);

        Assert.True(committed.CanonicalPromotionComplete);
        Assert.Equal(0, committed.CommitRevision);
        Assert.True(files.Exists(ManifestPath));
        Assert.Equal(SaveDecodeStatus.Valid, PlayerProgressSavePolicy.Decode(files.Text(ProgressPath)).Status);
        Assert.Equal(SaveDecodeStatus.Valid, ProgressSavePolicy.Decode(files.Text(ProgressPath + ".bak")).Status);
        Assert.Equal(SaveDecodeStatus.Valid, WorkProgressSavePolicy.Decode(files.Text(WorkPath)).Status);
        Assert.Equal(SaveDecodeStatus.Valid, BuddyIdentitySavePolicy.Decode(files.Text(BuddyPath(projection))).Status);
        Assert.Equal(SaveDecodeStatus.Valid, SceneSavePolicy.DecodeScene(files.Text(ScenePath(projection))).Status);
        Assert.Equal(SaveDecodeStatus.Valid, SceneSavePolicy.DecodeIndex(files.Text(IndexPath)).Status);
        Assert.Equal(1, committed.Registry.Count);
        Assert.Same(committed.Player, committed.Registry.Player);
        Assert.Equal(1_234, committed.Player.BalanceMilliCredits);
        Assert.Equal(12.0f, committed.Buddy.Mood);
        Assert.Equal(44.0f, committed.Buddy.Fullness);
        Assert.Equal(4_321, committed.Work.Lifetime.KeyboardPresses);

        SceneProgressLoadResult loaded = await new SceneProgressTransactionStore(SaveDir, files)
            .LoadCommittedAsync(CashPerPain);
        Assert.Equal(4_321, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(876, loaded.Work.Lifetime.MouseClicks);
        Assert.True(loaded.Work.FirstEntryGlassesGranted);
    }

    [Fact]
    public async Task Pre_manifest_write_failure_leaves_legacy_save_authoritative_and_retryable()
    {
        var files = new MemoryFiles();
        BuddyProgressState legacy = LegacyProgress();
        WorkProgressState work = LegacyWork();
        string legacyJson = ProgressSavePolicy.Serialize(
            ProgressSave.FromSnapshot(legacy.Snapshot(), work: work.Snapshot()));
        files.Set(ProgressPath, legacyJson);
        files.FailWriteDestination = WorkPath + ".next";
        LegacyNextFestMigrationProjection projection = Projection(legacy);
        var store = new NextFestMigrationStore(ProgressPath, files);

        await Assert.ThrowsAsync<IOException>(() => store.CommitLegacyMigrationAsync(
            projection,
            cashPerPain: CashPerPain,
            workProgress: work));

        Assert.Equal(legacyJson, files.Text(ProgressPath));
        Assert.Equal(SaveDecodeStatus.Valid, ProgressSavePolicy.Decode(files.Text(ProgressPath)).Status);
        Assert.False(files.Exists(ManifestPath));

        files.FailWriteDestination = null;
        await store.CommitLegacyMigrationAsync(projection, CashPerPain, work);
        SceneProgressLoadResult loaded = await new SceneProgressTransactionStore(SaveDir, files)
            .LoadCommittedAsync(CashPerPain);
        Assert.Equal(4_321, loaded.Work.Lifetime.KeyboardPresses);
    }

    [Fact]
    public async Task Asset_failure_occurs_before_manifest_commit_and_preserves_legacy_progress()
    {
        var files = new MemoryFiles();
        BuddyProgressState legacy = LegacyProgress();
        WorkProgressState work = LegacyWork();
        string legacyJson = ProgressSavePolicy.Serialize(
            ProgressSave.FromSnapshot(legacy.Snapshot(), work: work.Snapshot()));
        files.Set(ProgressPath, legacyJson);
        LegacyNextFestMigrationProjection projection = Projection(legacy);
        var store = new NextFestMigrationStore(ProgressPath, files);
        bool assetAttempted = false;

        await Assert.ThrowsAsync<IOException>(() => store.CommitLegacyMigrationAsync(
            projection,
            cashPerPain: CashPerPain,
            workProgress: work,
            commitAssetsAsync: _ =>
            {
                assetAttempted = true;
                throw new IOException("Injected room-paint migration failure.");
            }));

        Assert.True(assetAttempted);
        Assert.Equal(legacyJson, files.Text(ProgressPath));
        Assert.Equal(SaveDecodeStatus.Valid, ProgressSavePolicy.Decode(files.Text(ProgressPath)).Status);
        Assert.False(files.Exists(ManifestPath));
        Assert.False(files.Exists(ProgressPath + ".bak"));
    }

    [Fact]
    public async Task Promotion_failure_after_manifest_is_committed_and_recovers_from_staged_split_files()
    {
        var files = new MemoryFiles();
        BuddyProgressState legacy = LegacyProgress();
        WorkProgressState work = LegacyWork();
        string legacyJson = ProgressSavePolicy.Serialize(
            ProgressSave.FromSnapshot(legacy.Snapshot(), work: work.Snapshot()));
        files.Set(ProgressPath, legacyJson);
        files.FailReplaceDestination = ProgressPath;
        LegacyNextFestMigrationProjection projection = Projection(legacy);
        var store = new NextFestMigrationStore(ProgressPath, files);

        CommittedSceneProgress committed = await store.CommitLegacyMigrationAsync(
            projection,
            CashPerPain,
            work);

        Assert.False(committed.CanonicalPromotionComplete);
        Assert.True(files.Exists(ManifestPath));
        Assert.Equal(legacyJson, files.Text(ProgressPath));
        Assert.True(files.Exists(ProgressPath + ".next"));
        Assert.True(files.Exists(WorkPath + ".next"));

        SceneProgressLoadResult loaded = await new SceneProgressTransactionStore(SaveDir, files)
            .LoadCommittedAsync(CashPerPain);
        Assert.Equal(1_234, loaded.Player.BalanceMilliCredits);
        Assert.Equal(4_321, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(12.0f, loaded.BuddyIdentities[0].Mood);
    }

    private const double CashPerPain = 0.01;
    private static readonly string SaveDir =
        OperatingSystem.IsWindows() ? @"C:\nextfest-migration-test" : "/nextfest-migration-test";
    private static readonly string ProgressPath = Path.Combine(SaveDir, "progress.json");
    private static readonly string ManifestPath = Path.Combine(SaveDir, SceneProgressTransactionStore.ManifestFileName);
    private static readonly string WorkPath = Resolve(SceneStoragePaths.WorkProgress);
    private static readonly string IndexPath = Resolve(SceneStoragePaths.SceneIndex);

    private static BuddyProgressState LegacyProgress() => new(
        cashPerPain: CashPerPain,
        initialMood: 12.0f,
        unlockedToolIds: [ContentIds.ToolGrab],
        initialBalanceMilliCredits: 1_234,
        initialFullness: 44.0f);

    private static WorkProgressState LegacyWork() => new(
        lifetime: new WorkCounterSnapshot(4_321, 876),
        claimedLifetimeMilestoneIds: ["employee.day"],
        firstEntryGlassesGranted: true,
        revision: 6,
        activeSession: new WorkSessionSnapshot(
            Guid.Parse("189a6640-7dc0-4219-b93c-bd221d250e3d"),
            new WorkCounterSnapshot(44, 12),
            ["session.test"]));

    private static LegacyNextFestMigrationProjection Projection(BuddyProgressState legacy) =>
        LegacyNextFestMigrationPolicy.Project(
            legacy.Snapshot(),
            activeCharacterId: null,
            legacyEnvironment: new EnvironmentLayout([]),
            buddyPosition: new CanonicalRoomPosition(0.5f, 0.5f));

    private static string BuddyPath(in LegacyNextFestMigrationProjection projection) =>
        Resolve(SceneStoragePaths.BuddyIdentity(projection.Buddy.BuddyIdentityId));

    private static string ScenePath(in LegacyNextFestMigrationProjection projection) =>
        Resolve(SceneStoragePaths.SceneDocument(projection.Scene.SceneId));

    private static string Resolve(string relative) => Path.GetFullPath(Path.Combine(
        SaveDir,
        relative.Replace('/', Path.DirectorySeparatorChar)));

    private sealed class MemoryFiles : IAtomicSaveFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public string? FailWriteDestination { get; set; }
        public string? FailReplaceDestination { get; set; }
        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public void Set(string path, string contents) => _files[path] = contents;
        public string Text(string path) => _files[path];

        public void WriteDurable(string path, string contents)
        {
            if (string.Equals(path, FailWriteDestination, StringComparison.Ordinal))
                throw new IOException("Injected durable-write failure.");
            _files[path] = contents;
        }

        public void Replace(string temporary, string primary, string backup)
        {
            if (string.Equals(primary, FailReplaceDestination, StringComparison.Ordinal))
                throw new IOException("Injected replace failure.");
            _files[backup] = _files[primary];
            _files[primary] = _files[temporary];
            _files.Remove(temporary);
        }

        public void Move(string source, string destination)
        {
            _files[destination] = _files[source];
            _files.Remove(source);
        }
    }
}
