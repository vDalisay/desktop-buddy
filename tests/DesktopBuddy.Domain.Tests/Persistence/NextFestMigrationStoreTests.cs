using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class NextFestMigrationStoreTests
{
    [Fact]
    public async Task Successful_migration_commits_dependencies_before_player_progress_and_keeps_legacy_backup()
    {
        var files = new MemoryFiles();
        BuddyProgressState legacy = LegacyProgress();
        files.Set(ProgressPath, ProgressSavePolicy.Serialize(ProgressSave.FromSnapshot(legacy.Snapshot())));
        LegacyNextFestMigrationProjection projection = Projection(legacy);
        var store = new NextFestMigrationStore(ProgressPath, files);

        CommittedSceneProgress committed = await store.CommitLegacyMigrationAsync(
            projection,
            cashPerPain: CashPerPain);

        Assert.Equal(SaveDecodeStatus.Valid, PlayerProgressSavePolicy.Decode(files.Text(ProgressPath)).Status);
        Assert.Equal(SaveDecodeStatus.Valid, ProgressSavePolicy.Decode(files.Text(ProgressPath + ".bak")).Status);
        Assert.Equal(SaveDecodeStatus.Valid, BuddyIdentitySavePolicy.Decode(files.Text(BuddyPath(projection))).Status);
        Assert.Equal(SaveDecodeStatus.Valid, SceneSavePolicy.DecodeScene(files.Text(ScenePath(projection))).Status);
        Assert.Equal(SaveDecodeStatus.Valid, SceneSavePolicy.DecodeIndex(files.Text(IndexPath)).Status);
        Assert.Equal(1, committed.Registry.Count);
        Assert.Same(committed.Player, committed.Registry.Player);
        Assert.Equal(1_234, committed.Player.BalanceMilliCredits);
        Assert.Equal(12.0f, committed.Buddy.Mood);
        Assert.Equal(44.0f, committed.Buddy.Fullness);
    }

    [Fact]
    public async Task Final_progress_replace_failure_leaves_legacy_save_authoritative_and_retryable()
    {
        var files = new MemoryFiles();
        BuddyProgressState legacy = LegacyProgress();
        string legacyJson = ProgressSavePolicy.Serialize(ProgressSave.FromSnapshot(legacy.Snapshot()));
        files.Set(ProgressPath, legacyJson);
        files.FailReplaceDestination = ProgressPath;
        LegacyNextFestMigrationProjection projection = Projection(legacy);
        var store = new NextFestMigrationStore(ProgressPath, files);

        await Assert.ThrowsAsync<IOException>(() => store.CommitLegacyMigrationAsync(
            projection,
            cashPerPain: CashPerPain));

        Assert.Equal(legacyJson, files.Text(ProgressPath));
        Assert.Equal(SaveDecodeStatus.Valid, ProgressSavePolicy.Decode(files.Text(ProgressPath)).Status);
        Assert.True(files.Exists(BuddyPath(projection)));
        Assert.True(files.Exists(ScenePath(projection)));
        Assert.True(files.Exists(IndexPath));

        files.FailReplaceDestination = null;
        await store.CommitLegacyMigrationAsync(projection, cashPerPain: CashPerPain);
        Assert.Equal(SaveDecodeStatus.Valid, PlayerProgressSavePolicy.Decode(files.Text(ProgressPath)).Status);
        Assert.Equal(SaveDecodeStatus.Valid, ProgressSavePolicy.Decode(files.Text(ProgressPath + ".bak")).Status);
    }

    [Fact]
    public async Task Asset_failure_occurs_before_commit_point_and_preserves_legacy_progress()
    {
        var files = new MemoryFiles();
        BuddyProgressState legacy = LegacyProgress();
        string legacyJson = ProgressSavePolicy.Serialize(ProgressSave.FromSnapshot(legacy.Snapshot()));
        files.Set(ProgressPath, legacyJson);
        LegacyNextFestMigrationProjection projection = Projection(legacy);
        var store = new NextFestMigrationStore(ProgressPath, files);
        bool assetAttempted = false;

        await Assert.ThrowsAsync<IOException>(() => store.CommitLegacyMigrationAsync(
            projection,
            cashPerPain: CashPerPain,
            commitAssetsAsync: _ =>
            {
                assetAttempted = true;
                throw new IOException("Injected room-paint migration failure.");
            }));

        Assert.True(assetAttempted);
        Assert.Equal(legacyJson, files.Text(ProgressPath));
        Assert.Equal(SaveDecodeStatus.Valid, ProgressSavePolicy.Decode(files.Text(ProgressPath)).Status);
        Assert.False(files.Exists(ProgressPath + ".bak"));
    }

    private const double CashPerPain = 0.01;
    private static readonly string SaveDir =
        OperatingSystem.IsWindows() ? @"C:\nextfest-migration-test" : "/nextfest-migration-test";
    private static readonly string ProgressPath = Path.Combine(SaveDir, "progress.json");
    private static readonly string IndexPath = Resolve(SceneStoragePaths.SceneIndex);

    private static BuddyProgressState LegacyProgress() => new(
        cashPerPain: CashPerPain,
        initialMood: 12.0f,
        unlockedToolIds: [ContentIds.ToolGrab],
        initialBalanceMilliCredits: 1_234,
        initialFullness: 44.0f);

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

        public string? FailReplaceDestination { get; set; }
        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public void Set(string path, string contents) => _files[path] = contents;
        public string Text(string path) => _files[path];

        public void WriteDurable(string path, string contents) => _files[path] = contents;

        public void Replace(string temporary, string primary, string backup)
        {
            if (string.Equals(primary, FailReplaceDestination, StringComparison.Ordinal))
                throw new IOException("Injected final replace failure.");
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
