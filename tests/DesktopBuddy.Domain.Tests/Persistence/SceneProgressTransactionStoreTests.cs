using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SceneProgressTransactionStoreTests
{
    [Fact]
    public async Task Commit_and_load_round_trip_one_coherent_generation()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        TestGraph graph = Graph();

        SceneProgressCommitResult committed = await store.CommitAsync(
            graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);

        Assert.Equal(0, committed.Revision);
        Assert.True(committed.CanonicalPromotionComplete);
        Assert.Equal(0, loaded.Revision);
        Assert.Equal(2_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(100, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(50, loaded.Work.Lifetime.MouseClicks);
        Assert.True(loaded.Work.FirstEntryGlassesGranted);
        Assert.Single(loaded.BuddyIdentities);
        Assert.Equal(15.0f, loaded.BuddyIdentities[0].Mood);
        Assert.Single(loaded.Scenes);
        Assert.Equal(graph.Scene.SceneId, loaded.Scenes[0].SceneId);
        Assert.Equal(graph.Scene.SceneId.Value, loaded.Index.ActiveSceneId);
    }

    [Fact]
    public async Task Prepare_failure_leaves_previous_manifest_generation_authoritative()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        TestGraph graph = Graph();
        await store.CommitAsync(graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);

        graph.Player.Deposit(5_000);
        graph.Work.Record(WorkActivityKind.KeyboardPress, 10);
        graph.Buddy.ApplyCareMood(20.0f);
        files.FailWriteDestination = ScenePath(graph.Scene) + ".next";

        await Assert.ThrowsAsync<IOException>(() => store.CommitAsync(
            graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId));

        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);
        Assert.Equal(0, loaded.Revision);
        Assert.Equal(2_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(100, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(15.0f, loaded.BuddyIdentities[0].Mood);
    }

    [Fact]
    public async Task Promotion_failure_after_manifest_still_loads_new_committed_generation_from_staging()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        TestGraph graph = Graph();
        await store.CommitAsync(graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);

        graph.Player.Deposit(5_000);
        graph.Work.Record(WorkActivityKind.KeyboardPress, 10);
        graph.Buddy.ApplyCareMood(20.0f);
        files.FailReplaceDestination = ProgressPath;

        SceneProgressCommitResult committed = await store.CommitAsync(
            graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);

        Assert.Equal(1, committed.Revision);
        Assert.False(committed.CanonicalPromotionComplete);
        Assert.Equal(1, loaded.Revision);
        Assert.Equal(7_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(110, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(35.0f, loaded.BuddyIdentities[0].Mood);
        Assert.True(files.Exists(ProgressPath + ".next"));
        Assert.True(files.Exists(WorkPath + ".next"));
    }

    [Theory]
    [InlineData("progress")]
    [InlineData("work")]
    public async Task Cloud_copy_of_committed_incomplete_generation_is_self_contained(string failurePoint)
    {
        var sourceFiles = new MemoryFiles();
        var sourceStore = Store(sourceFiles);
        TestGraph graph = Graph();
        await sourceStore.CommitAsync(
            graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);

        graph.Player.Deposit(9_000);
        graph.Work.Record(WorkActivityKind.KeyboardPress, 33);
        graph.Buddy.ApplyCareMood(25.0f);
        sourceFiles.FailReplaceDestination = failurePoint == "progress" ? ProgressPath : WorkPath;

        SceneProgressCommitResult partial = await sourceStore.CommitAsync(
            graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        Assert.False(partial.CanonicalPromotionComplete);

        // Seed files that must never cross the Auto-Cloud boundary. This makes the copy model
        // exercise the same allowlist Steamworks is configured from instead of merely copying
        // every in-memory file and accidentally hiding an over-broad policy.
        sourceFiles.WriteDurable(Path.Combine(SaveRoot, SteamCloudSavePolicy.SettingsFileName), "{}");
        sourceFiles.WriteDurable(ProgressPath + ".bak", "old-backup");
        sourceFiles.WriteDurable(ProgressPath + ".tmp", "uncommitted-temp");
        sourceFiles.WriteDurable(ProgressPath + ".invalid-20260908", "quarantine");

        var cloudFiles = sourceFiles.CopyCloudEligible(SaveRoot, CloudDestinationRoot);
        var cloudStore = new SceneProgressTransactionStore(CloudDestinationRoot, cloudFiles);
        SceneProgressLoadResult loaded = await cloudStore.LoadCommittedAsync(CashPerPain);

        Assert.Equal(1, loaded.Revision);
        Assert.Equal(11_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(133, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(40.0f, loaded.BuddyIdentities[0].Mood);
        Assert.Equal(graph.Scene.SceneId, loaded.Scenes[0].SceneId);

        string cloudProgress = Path.Combine(CloudDestinationRoot, SteamCloudSavePolicy.ProgressFileName);
        Assert.True(cloudFiles.Exists(Path.Combine(
            CloudDestinationRoot,
            SteamCloudSavePolicy.SceneProgressManifestFileName)));
        Assert.True(
            cloudFiles.Exists(cloudProgress) ||
            cloudFiles.Exists(cloudProgress + SteamCloudSavePolicy.SceneRecoverySuffix));
        Assert.False(cloudFiles.Exists(Path.Combine(
            CloudDestinationRoot,
            SteamCloudSavePolicy.SettingsFileName)));
        Assert.False(cloudFiles.Exists(cloudProgress + ".bak"));
        Assert.False(cloudFiles.Exists(cloudProgress + ".tmp"));
        Assert.False(cloudFiles.Exists(cloudProgress + ".invalid-20260908"));
    }

    [Fact]
    public async Task Next_commit_first_recovers_prior_incomplete_promotion_before_reusing_staging_paths()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        TestGraph graph = Graph();
        await store.CommitAsync(graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);

        graph.Player.Deposit(1_000);
        graph.Work.Record(WorkActivityKind.MouseClick, 5);
        files.FailReplaceDestination = ProgressPath;
        SceneProgressCommitResult partial = await store.CommitAsync(
            graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        Assert.False(partial.CanonicalPromotionComplete);

        files.FailReplaceDestination = null;
        graph.Player.Deposit(2_000);
        graph.Work.Record(WorkActivityKind.MouseClick, 7);
        SceneProgressCommitResult next = await store.CommitAsync(
            graph.Player, graph.Work, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);

        Assert.Equal(2, next.Revision);
        Assert.True(next.CanonicalPromotionComplete);
        Assert.Equal(5_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(62, loaded.Work.Lifetime.MouseClicks);
        Assert.False(files.Exists(ProgressPath + ".next"));
        Assert.False(files.Exists(WorkPath + ".next"));
    }

    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\scene-progress-transaction-test" : "/scene-progress-transaction-test";
    private static readonly string CloudDestinationRoot = SaveRoot + "-cloud-copy";
    private static readonly string ProgressPath = Path.Combine(SaveRoot, SteamCloudSavePolicy.ProgressFileName);
    private static readonly string WorkPath = Resolve(SceneStoragePaths.WorkProgress);

    private static SceneProgressTransactionStore Store(MemoryFiles files) => new(SaveRoot, files);

    private static TestGraph Graph()
    {
        var legacy = new BuddyProgressState(
            cashPerPain: CashPerPain,
            initialMood: 15.0f,
            initialBalanceMilliCredits: 2_000);
        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            legacy.Snapshot(),
            activeCharacterId: null,
            legacyEnvironment: new EnvironmentLayout([]),
            buddyPosition: new CanonicalRoomPosition(0.5f, 0.5f));
        var work = new WorkProgressState(
            lifetime: new WorkCounterSnapshot(100, 50),
            claimedLifetimeMilestoneIds: ["work.test.lifetime"],
            firstEntryGlassesGranted: true,
            revision: 3);
        return new TestGraph(
            new PlayerProgressState(CashPerPain, projection.Player),
            work,
            new BuddyIdentityState(projection.Buddy),
            projection.Scene);
    }

    private static string ScenePath(SceneDocument scene) => Resolve(SceneStoragePaths.SceneDocument(scene.SceneId));

    private static string Resolve(string relative) => Path.GetFullPath(Path.Combine(
        SaveRoot,
        relative.Replace('/', Path.DirectorySeparatorChar)));

    private sealed record TestGraph(
        PlayerProgressState Player,
        WorkProgressState Work,
        BuddyIdentityState Buddy,
        SceneDocument Scene);

    private sealed class MemoryFiles : IAtomicSaveFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public string? FailWriteDestination { get; set; }
        public string? FailReplaceDestination { get; set; }

        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }

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

        public MemoryFiles CopyCloudEligible(string sourceRoot, string destinationRoot)
        {
            string canonicalSourceRoot = Path.GetFullPath(sourceRoot);
            string canonicalDestinationRoot = Path.GetFullPath(destinationRoot);
            var copy = new MemoryFiles();

            foreach ((string path, string contents) in _files)
            {
                string fullPath = Path.GetFullPath(path);
                string relative = Path.GetRelativePath(canonicalSourceRoot, fullPath);
                if (relative == ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    !SteamCloudSavePolicy.IsCloudEligibleRelativePath(relative))
                {
                    continue;
                }

                string destination = Path.GetFullPath(Path.Combine(canonicalDestinationRoot, relative));
                copy.WriteDurable(destination, contents);
            }

            return copy;
        }
    }
}
