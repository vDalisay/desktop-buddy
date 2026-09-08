using System;
using System.Collections.Generic;
using System.IO;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class RunContextProgressRoutingTests
{
    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\run-context-routing-test" : "/run-context-routing-test";

    [Fact]
    public void Scene_run_routes_account_buddy_and_persistence_to_split_graph()
    {
        var legacy = new BuddyProgressState(
            CashPerPain,
            initialMood: 12.0f,
            initialBalanceMilliCredits: 5_000);
        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            legacy.Snapshot(),
            activeCharacterId: null,
            legacyEnvironment: new EnvironmentLayout([]),
            buddyPosition: new CanonicalRoomPosition(0.5f, 0.5f));
        var player = new PlayerProgressState(CashPerPain, projection.Player);
        var buddy = new BuddyIdentityState(projection.Buddy);
        var work = new WorkProgressState();
        BuildScopePolicy scope = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo: true,
            nextFestDemo: true,
            fullRelease: false);
        var scenes = new SceneProgressCoordinator(
            scope,
            player,
            work,
            [buddy],
            [projection.Scene],
            projection.Scene.SceneId,
            new SceneProgressTransactionStore(SaveRoot, new MemoryFiles()),
            committedRevision: 0);

        var context = new RunContext(
            legacy,
            Economy: null!,
            ProgressStore: null!,
            Saves: null!,
            Settings: new LocalSettingsSave(),
            LoadStatus: SaveLoadStatus.Loaded,
            WorkProgress: work,
            SceneProgress: scenes);

        Assert.True(context.UsesSplitSceneProgress);
        Assert.True(context.PlayerProgress.IsSplit);
        Assert.Same(player, context.PlayerProgress.PlayerProgress);
        Assert.True(context.ActiveBuddyProgress.IsSplit);
        Assert.Same(buddy, context.ActiveBuddyProgress.BuddyProgress);
        Assert.IsType<SceneRunProgressPersistence>(context.RunProgressPersistence);
    }

    [Fact]
    public void Initial_demo_run_keeps_legacy_account_buddy_and_persistence_backend()
    {
        var legacy = new BuddyProgressState(CashPerPain, initialBalanceMilliCredits: 2_000);
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(legacy, store);
        var context = new RunContext(
            legacy,
            Economy: null!,
            ProgressStore: store,
            Saves: saves,
            Settings: new LocalSettingsSave(),
            LoadStatus: SaveLoadStatus.Loaded);

        Assert.False(context.UsesSplitSceneProgress);
        Assert.False(context.PlayerProgress.IsSplit);
        Assert.Same(legacy, context.PlayerProgress.LegacyProgress);
        Assert.False(context.ActiveBuddyProgress.IsSplit);
        Assert.Same(legacy, context.ActiveBuddyProgress.LegacyProgress);
        Assert.IsType<LegacyRunProgressPersistence>(context.RunProgressPersistence);
    }

    private sealed class MemoryFiles : IAtomicSaveFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public void WriteDurable(string path, string contents) => _files[path] = contents;

        public void Replace(string temporary, string primary, string backup)
        {
            if (_files.TryGetValue(primary, out string? prior))
                _files[backup] = prior;
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
