using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
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
            graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);

        Assert.Equal(0, committed.Revision);
        Assert.True(committed.CanonicalPromotionComplete);
        Assert.Equal(0, loaded.Revision);
        Assert.Equal(2_000, loaded.Player.BalanceMilliCredits);
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
        await store.CommitAsync(graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);

        graph.Player.Deposit(5_000);
        graph.Buddy.ApplyCareMood(20.0f);
        files.FailWriteDestination = ScenePath(graph.Scene) + ".next";

        await Assert.ThrowsAsync<IOException>(() => store.CommitAsync(
            graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId));

        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);
        Assert.Equal(0, loaded.Revision);
        Assert.Equal(2_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(15.0f, loaded.BuddyIdentities[0].Mood);
    }

    [Fact]
    public async Task Promotion_failure_after_manifest_still_loads_new_committed_generation_from_staging()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        TestGraph graph = Graph();
        await store.CommitAsync(graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);

        graph.Player.Deposit(5_000);
        graph.Buddy.ApplyCareMood(20.0f);
        files.FailReplaceDestination = ProgressPath;

        SceneProgressCommitResult committed = await store.CommitAsync(
            graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);

        Assert.Equal(1, committed.Revision);
        Assert.False(committed.CanonicalPromotionComplete);
        Assert.Equal(1, loaded.Revision);
        Assert.Equal(7_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(35.0f, loaded.BuddyIdentities[0].Mood);
        Assert.True(files.Exists(ProgressPath + ".next"));
    }

    [Fact]
    public async Task Next_commit_first_recovers_prior_incomplete_promotion_before_reusing_staging_paths()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        TestGraph graph = Graph();
        await store.CommitAsync(graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);

        graph.Player.Deposit(1_000);
        files.FailReplaceDestination = ProgressPath;
        SceneProgressCommitResult partial = await store.CommitAsync(
            graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        Assert.False(partial.CanonicalPromotionComplete);

        files.FailReplaceDestination = null;
        graph.Player.Deposit(2_000);
        SceneProgressCommitResult next = await store.CommitAsync(
            graph.Player, [graph.Buddy], [graph.Scene], graph.Scene.SceneId);
        SceneProgressLoadResult loaded = await store.LoadCommittedAsync(CashPerPain);

        Assert.Equal(2, next.Revision);
        Assert.True(next.CanonicalPromotionComplete);
        Assert.Equal(5_000, loaded.Player.BalanceMilliCredits);
        Assert.False(files.Exists(ProgressPath + ".next"));
    }

    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\scene-progress-transaction-test" : "/scene-progress-transaction-test";
    private static readonly string ProgressPath = Path.Combine(SaveRoot, SteamCloudSavePolicy.ProgressFileName);

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
        return new TestGraph(
            new PlayerProgressState(CashPerPain, projection.Player),
            new BuddyIdentityState(projection.Buddy),
            projection.Scene);
    }

    private static string ScenePath(SceneDocument scene) => Resolve(SceneStoragePaths.SceneDocument(scene.SceneId));

    private static string Resolve(string relative) => Path.GetFullPath(Path.Combine(
        SaveRoot,
        relative.Replace('/', Path.DirectorySeparatorChar)));

    private sealed record TestGraph(
        PlayerProgressState Player,
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
    }
}
