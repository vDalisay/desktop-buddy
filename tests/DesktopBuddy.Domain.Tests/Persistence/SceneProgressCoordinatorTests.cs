using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SceneProgressCoordinatorTests
{
    [Fact]
    public async Task Player_work_and_buddy_mutations_commit_as_one_generation()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        Assert.False(coordinator.IsDirty);
        Assert.True(coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? buddy));

        coordinator.Player.Deposit(3_000);
        coordinator.Work.Record(WorkActivityKind.KeyboardPress, 25);
        buddy!.ApplyCareMood(10.0f);

        Assert.True(coordinator.IsDirty);
        await coordinator.FlushAsync();
        Assert.False(coordinator.IsDirty);
        Assert.Equal(0, coordinator.LastCommittedRevision);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(5_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(125, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(25.0f, loaded.BuddyIdentities[0].Mood);
    }

    [Fact]
    public async Task Identity_and_scene_library_changes_build_two_buddy_bindings_and_persist_active_scene()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        Assert.True(coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary));
        BuddyIdentitySnapshot secondSnapshot = primary!.Snapshot() with
        {
            BuddyIdentityId = BuddyIdentityId.From(Guid.Parse("1530533b-f6cc-4a64-b142-2a228204db2d")),
            Revision = 0,
            Mood = -20.0f,
        };
        var second = new BuddyIdentityState(secondSnapshot);

        Assert.True(coordinator.RegisterBuddyIdentity(second));
        SceneLibraryResult added = coordinator.AddBuddyToScene(
            coordinator.ActiveSceneId,
            second.BuddyIdentityId,
            new CanonicalRoomPosition(0.7f, 0.5f));
        Assert.True(added.Succeeded);

        SceneProgressBindingRegistry bindings = coordinator.CreateActiveBindings();
        Assert.Equal(2, bindings.Count);
        Assert.Same(coordinator.Player, bindings.ForBuddy(BuddyIdentityId.LegacyPrimary).Coordinator.Player);
        Assert.Same(coordinator.Player, bindings.ForBuddy(second.BuddyIdentityId).Coordinator.Player);
        Assert.NotSame(
            bindings.ForBuddy(BuddyIdentityId.LegacyPrimary).Coordinator.Buddy,
            bindings.ForBuddy(second.BuddyIdentityId).Coordinator.Buddy);

        SceneLibraryResult created = coordinator.CreateScene("Lab");
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Scene);
        Assert.True(coordinator.SwitchScene(created.Scene!.SceneId).Succeeded);
        await coordinator.FlushAsync();

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(2, loaded.BuddyIdentities.Count);
        Assert.Equal(2, loaded.Scenes.Count);
        Assert.Equal(created.Scene.SceneId.Value, loaded.Index.ActiveSceneId);
    }

    [Fact]
    public async Task Autosave_waits_for_thirty_seconds_of_valid_running_time()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        coordinator.Work.Record(WorkActivityKind.MouseClick, 1);

        await coordinator.TickAsync(SceneProgressCoordinator.AutosaveSeconds - 1.0);
        Assert.False(Store(files).HasCommittedGeneration);
        Assert.True(coordinator.IsDirty);

        await coordinator.TickAsync(1.0);
        Assert.True(Store(files).HasCommittedGeneration);
        Assert.False(coordinator.IsDirty);
    }

    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\scene-progress-coordinator-test" : "/scene-progress-coordinator-test";

    private static SceneProgressTransactionStore Store(MemoryFiles files) => new(SaveRoot, files);

    private static SceneProgressCoordinator Coordinator(MemoryFiles files)
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
        var player = new PlayerProgressState(CashPerPain, projection.Player);
        var buddy = new BuddyIdentityState(projection.Buddy);
        var work = new WorkProgressState(
            lifetime: new WorkCounterSnapshot(100, 50),
            firstEntryGlassesGranted: true,
            revision: 2);
        BuildScopePolicy scope = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo: true,
            nextFestDemo: true,
            fullRelease: false);
        return new SceneProgressCoordinator(
            scope,
            player,
            work,
            [buddy],
            [projection.Scene],
            projection.Scene.SceneId,
            Store(files));
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
