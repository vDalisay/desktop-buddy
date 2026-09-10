using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Sandbox;
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

        // A fresh graph is not clean merely because semantic revisions match their baselines:
        // there is no durable manifest yet. Commit that initial generation first.
        Assert.True(coordinator.IsDirty);
        await coordinator.FlushAsync(force: true);
        Assert.False(coordinator.IsDirty);
        Assert.Equal(0, coordinator.LastCommittedRevision);
        Assert.True(Store(files).HasCommittedGeneration);
        Assert.True(coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? buddy));

        coordinator.Player.Deposit(3_000);
        coordinator.Work.Record(WorkActivityKind.KeyboardPress, 25);
        buddy!.ApplyCareMood(10.0f);

        Assert.True(coordinator.IsDirty);
        await coordinator.FlushAsync();
        Assert.False(coordinator.IsDirty);
        Assert.Equal(1, coordinator.LastCommittedRevision);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(5_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(125, loaded.Work.Lifetime.KeyboardPresses);
        Assert.Equal(25.0f, loaded.BuddyIdentities[0].Mood);
    }

    [Fact]
    public async Task Built_parts_commit_with_the_Scene_and_come_back_on_load()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        await coordinator.FlushAsync(force: true);

        SandboxDocument sandbox = coordinator.ActiveSandbox;
        SandboxPartId beam = sandbox
            .Add(SandboxPartCatalogue.WoodBeam, new CanonicalRoomPosition(0.4f, 0.6f), 30.0f)
            .Part!.PartId;
        sandbox.Add(SandboxPartCatalogue.Wheel, new CanonicalRoomPosition(0.6f, 0.8f));
        sandbox.SetOverrides(beam, new SandboxPartOverrides(MassScale: 2.0f, Frozen: true));

        // Building marks the graph dirty exactly like a Buddy or wallet change does.
        Assert.True(coordinator.IsDirty);
        await coordinator.FlushAsync(force: true);
        Assert.False(coordinator.IsDirty);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        SceneProgressCoordinator reloaded = CoordinatorFromLoaded(files, loaded);
        SandboxDocument restored = reloaded.ActiveSandbox;

        Assert.Equal(2, restored.Count);
        Assert.True(restored.TryGet(beam, out PlacedSandboxPart? restoredBeam));
        Assert.Equal(SandboxPartCatalogue.WoodBeam, restoredBeam!.DefinitionId);
        Assert.Equal(30.0f, restoredBeam.RotationDegrees);
        Assert.Equal(2.0f, restoredBeam.Overrides.MassScale);
        Assert.True(restoredBeam.Overrides.Frozen);
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    public async Task Duplicating_a_Scene_copies_its_parts_and_deleting_one_drops_them()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        await coordinator.FlushAsync(force: true);

        SceneId sourceId = coordinator.ActiveSceneId;
        coordinator.ActiveSandbox.Add(SandboxPartCatalogue.MetalPlate, new CanonicalRoomPosition(0.5f, 0.9f));

        SceneLibraryResult duplicated = coordinator.DuplicateScene(sourceId);
        Assert.True(duplicated.Succeeded);
        SceneId copyId = duplicated.Scene!.SceneId;

        SandboxDocument copy = coordinator.SandboxFor(copyId);
        Assert.Single(copy.Parts);
        Assert.NotEqual(coordinator.SandboxFor(sourceId).Parts[0].PartId, copy.Parts[0].PartId);

        // Editing the copy leaves the original room alone.
        copy.Remove(copy.Parts[0].PartId);
        Assert.Single(coordinator.SandboxFor(sourceId).Parts);

        await coordinator.FlushAsync(force: true);
        Assert.True(coordinator.DeleteScene(copyId).Succeeded);
        await coordinator.FlushAsync(force: true);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.DoesNotContain(loaded.Scenes, scene => scene.SceneId == copyId);
        Assert.NotNull(loaded.Sandboxes);
        Assert.DoesNotContain(loaded.Sandboxes!, entry => entry.Key == copyId);
        Assert.True(loaded.Sandboxes!.ContainsKey(sourceId));
    }

    [Fact]
    public async Task Fresh_generation_failed_commit_stays_dirty_and_retries()
    {
        var files = new MemoryFiles { FailNextDurableWrite = true };
        SceneProgressCoordinator coordinator = Coordinator(files);

        await Assert.ThrowsAsync<IOException>(() => coordinator.FlushAsync(force: true));

        Assert.True(coordinator.IsDirty);
        Assert.Equal(-1, coordinator.LastCommittedRevision);
        Assert.NotNull(coordinator.LastFailure);
        Assert.False(Store(files).HasCommittedGeneration);

        await coordinator.FlushAsync(force: true);

        Assert.False(coordinator.IsDirty);
        Assert.Equal(0, coordinator.LastCommittedRevision);
        Assert.Null(coordinator.LastFailure);
        Assert.True(Store(files).HasCommittedGeneration);
    }

    [Fact]
    public async Task Loaded_unchanged_generation_does_not_create_redundant_commit()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator first = Coordinator(files);
        await first.FlushAsync(force: true);
        Assert.Equal(0, first.LastCommittedRevision);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        SceneProgressCoordinator restored = CoordinatorFromLoaded(files, loaded);
        Assert.False(restored.IsDirty);

        await restored.FlushAsync(force: true);

        Assert.False(restored.IsDirty);
        Assert.Equal(0, restored.LastCommittedRevision);
        SceneProgressLoadResult stillSame = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(0, stillSame.Revision);
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
    public async Task Reset_to_fresh_graph_preserves_live_object_identities_and_commits_one_home_scene()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        await coordinator.FlushAsync(force: true);
        Assert.True(coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary));

        PlayerProgressState playerReference = coordinator.Player;
        WorkProgressState workReference = coordinator.Work;
        BuddyIdentityState primaryReference = primary!;

        coordinator.Player.Deposit(50_000);
        coordinator.Work.Record(WorkActivityKind.KeyboardPress, 40);
        primaryReference.SetCharacter(Guid.Parse("8ac63245-a0fd-4316-b905-a8874343765f"));

        BuddyIdentitySnapshot secondSnapshot = primaryReference.Snapshot() with
        {
            BuddyIdentityId = BuddyIdentityId.From(Guid.Parse("ec143f42-a8f5-44c0-8997-5b4a11350450")),
            Revision = 0,
            CharacterId = null,
        };
        var second = new BuddyIdentityState(secondSnapshot);
        Assert.True(coordinator.RegisterBuddyIdentity(second));
        Assert.True(coordinator.AddBuddyToScene(
            coordinator.ActiveSceneId,
            second.BuddyIdentityId,
            new CanonicalRoomPosition(0.75f, 0.5f)).Succeeded);
        SceneLibraryResult lab = coordinator.CreateScene("Lab");
        Assert.True(lab.Succeeded);
        Assert.NotNull(lab.Scene);
        Assert.True(coordinator.SwitchScene(lab.Scene!.SceneId).Succeeded);
        await coordinator.FlushAsync(force: true);

        LegacyNextFestMigrationProjection fresh = FreshProjection();
        await coordinator.ResetToFreshGraphAsync(fresh);

        Assert.Same(playerReference, coordinator.Player);
        Assert.Same(workReference, coordinator.Work);
        Assert.True(coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? resetPrimary));
        Assert.Same(primaryReference, resetPrimary);
        Assert.Equal(fresh.Player.BalanceMilliCredits, coordinator.Player.BalanceMilliCredits);
        Assert.Equal(default, coordinator.Work.Lifetime);
        Assert.False(coordinator.Work.FirstEntryGlassesGranted);
        Assert.Null(resetPrimary!.CharacterId);
        Assert.Equal(1, coordinator.BuddyIdentityCount);
        Assert.Equal(1, coordinator.SceneCount);
        Assert.Equal(SceneId.LegacyHome, coordinator.ActiveSceneId);
        Assert.Empty(coordinator.ActiveScene.Environment.Decorations);
        Assert.Empty(coordinator.ActiveScene.OwnedUnplaced);
        Assert.False(coordinator.IsDirty);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Single(loaded.BuddyIdentities);
        Assert.Single(loaded.Scenes);
        Assert.Equal(SceneId.LegacyHome.Value, loaded.Index.ActiveSceneId);
        Assert.Null(loaded.BuddyIdentities[0].CharacterId);
        Assert.Empty(loaded.Scenes[0].Environment.Decorations);
    }

    [Fact]
    public async Task Reset_failed_manifest_write_restores_exact_pre_reset_graph()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        await coordinator.FlushAsync(force: true);
        Assert.True(coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary));

        coordinator.Player.Deposit(17_000);
        coordinator.Work.Record(WorkActivityKind.MouseClick, 11);
        primary!.SetCharacter(Guid.Parse("900c99ec-e68f-4ccd-a67c-8fbad4273e59"));
        SceneLibraryResult lab = coordinator.CreateScene("Rollback Lab");
        Assert.True(lab.Succeeded);
        Assert.NotNull(lab.Scene);
        Assert.True(coordinator.SwitchScene(lab.Scene!.SceneId).Succeeded);
        await coordinator.FlushAsync(force: true);

        string playerBefore = PlayerProgressSavePolicy.Serialize(coordinator.Player);
        string workBefore = WorkProgressSavePolicy.Serialize(coordinator.Work);
        string buddyBefore = BuddyIdentitySavePolicy.Serialize(primary);
        string sceneBefore = SceneSavePolicy.SerializeScene(coordinator.ActiveScene);
        SceneId activeBefore = coordinator.ActiveSceneId;
        int sceneCountBefore = coordinator.SceneCount;
        long committedBefore = coordinator.LastCommittedRevision;

        files.FailNextDurableWrite = true;
        await Assert.ThrowsAsync<IOException>(() => coordinator.ResetToFreshGraphAsync(FreshProjection()));

        Assert.Equal(playerBefore, PlayerProgressSavePolicy.Serialize(coordinator.Player));
        Assert.Equal(workBefore, WorkProgressSavePolicy.Serialize(coordinator.Work));
        Assert.True(coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? restoredPrimary));
        Assert.Same(primary, restoredPrimary);
        Assert.Equal(buddyBefore, BuddyIdentitySavePolicy.Serialize(restoredPrimary!));
        Assert.Equal(activeBefore, coordinator.ActiveSceneId);
        Assert.Equal(sceneCountBefore, coordinator.SceneCount);
        Assert.Equal(sceneBefore, SceneSavePolicy.SerializeScene(coordinator.ActiveScene));
        Assert.Equal(committedBefore, coordinator.LastCommittedRevision);
        Assert.False(coordinator.IsDirty);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(activeBefore.Value, loaded.Index.ActiveSceneId);
        Assert.Equal(sceneCountBefore, loaded.Scenes.Count);
        Assert.Equal(playerBefore, PlayerProgressSavePolicy.Serialize(loaded.Player));
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

    private static BuildScopePolicy NextFestScope() => BuildScopePolicy.Resolve(
        itchIo: false,
        steamDemo: true,
        nextFestDemo: true,
        fullRelease: false);

    private static LegacyNextFestMigrationProjection FreshProjection()
    {
        var fresh = new BuddyProgressState(cashPerPain: CashPerPain);
        return LegacyNextFestMigrationPolicy.Project(
            fresh.Snapshot(),
            activeCharacterId: null,
            new EnvironmentProgressSnapshot(0, new EnvironmentLayout(), []),
            new CanonicalRoomPosition(0.5f, 0.5f));
    }

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
        return new SceneProgressCoordinator(
            NextFestScope(),
            player,
            work,
            [buddy],
            [projection.Scene],
            projection.Scene.SceneId,
            Store(files));
    }

    private static SceneProgressCoordinator CoordinatorFromLoaded(
        MemoryFiles files,
        SceneProgressLoadResult loaded) => new(
            NextFestScope(),
            loaded.Player,
            loaded.Work,
            loaded.BuddyIdentities,
            loaded.Scenes,
            SceneId.From(loaded.Index.ActiveSceneId),
            Store(files),
            loaded.Revision,
            loaded.Sandboxes);

    private sealed class MemoryFiles : IAtomicSaveFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
        public bool FailNextDurableWrite { get; set; }

        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public void WriteDurable(string path, string contents)
        {
            if (FailNextDurableWrite)
            {
                FailNextDurableWrite = false;
                throw new IOException("Injected durable write failure.");
            }
            _files[path] = contents;
        }
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
