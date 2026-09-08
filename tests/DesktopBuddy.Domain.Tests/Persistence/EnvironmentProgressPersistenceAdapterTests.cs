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

public sealed class EnvironmentProgressPersistenceAdapterTests
{
    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\environment-adapter-test" : "/environment-adapter-test";
    private static readonly DecorationDefinition Lamp = new(
        new DecorationDefinitionId("decoration.lamp.adapter_test"),
        "environment.lamp.adapter_test",
        DecorationCategory.Lamp,
        25_000,
        DecorationAnchorKind.Floor,
        new DecorationRotationPolicy(true, 90),
        DecorationRenderBand.BehindBuddyFloor);

    [Fact]
    public async Task Scene_adapter_commits_wallet_and_room_then_updates_stable_presentation_view()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator scenes = Coordinator(files);
        await scenes.FlushAsync(force: true);
        EnvironmentProgressSnapshot initial = scenes.ActiveEnvironmentProgress;
        var view = new EnvironmentProgressState(initial.Layout, initial.Revision, initial.OwnedUnplaced);
        var adapter = new SceneEnvironmentProgressPersistence(scenes, scenes.ActiveSceneId, view);
        var session = new EnvironmentEditSession(
            initial,
            adapter.BalanceMilliCredits,
            new DecorationCatalogue([Lamp]),
            () => new PlacedDecorationId(Guid.Parse("00000000-0000-0000-0000-000000000777")));

        Assert.True(session.Buy(Lamp.Id, adapter.BalanceMilliCredits).Succeeded);
        await adapter.CommitAsync(session);

        Assert.Equal(75_000, scenes.Player.BalanceMilliCredits);
        Assert.Equal(initial.Revision + 1, view.Revision);
        Assert.Equal([Lamp.Id], view.OwnedUnplaced);
        Assert.Empty(view.Layout.Decorations);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(75_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal([Lamp.Id], loaded.Scenes[0].EnvironmentProgress.OwnedUnplaced);
        Assert.Equal(view.Revision, loaded.Scenes[0].EnvironmentProgress.Revision);
    }

    [Fact]
    public async Task Scene_adapter_rejects_reads_and_commits_after_active_scene_changes()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator scenes = Coordinator(files);
        await scenes.FlushAsync(force: true);
        SceneId original = scenes.ActiveSceneId;
        EnvironmentProgressSnapshot initial = scenes.ActiveEnvironmentProgress;
        var view = new EnvironmentProgressState(initial.Layout, initial.Revision, initial.OwnedUnplaced);
        var adapter = new SceneEnvironmentProgressPersistence(scenes, original, view);
        var session = new EnvironmentEditSession(
            initial,
            adapter.BalanceMilliCredits,
            new DecorationCatalogue([Lamp]));
        Assert.True(session.Buy(Lamp.Id, adapter.BalanceMilliCredits).Succeeded);

        SceneLibraryResult created = scenes.CreateScene("Lab");
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Scene);
        Assert.True(scenes.SwitchScene(created.Scene!.SceneId).Succeeded);

        Assert.Throws<InvalidOperationException>(() => adapter.Snapshot());
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.CommitAsync(session));
        Assert.Equal(100_000, scenes.Player.BalanceMilliCredits);
        Assert.Equal(initial.Revision, view.Revision);
        Assert.Empty(view.OwnedUnplaced);
    }

    private static SceneProgressCoordinator Coordinator(MemoryFiles files)
    {
        var legacy = new BuddyProgressState(
            cashPerPain: CashPerPain,
            initialBalanceMilliCredits: 100_000);
        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            legacy.Snapshot(),
            activeCharacterId: null,
            new EnvironmentProgressSnapshot(3, new EnvironmentLayout(), []),
            new CanonicalRoomPosition(0.5f, 0.5f));
        return new SceneProgressCoordinator(
            BuildScopePolicy.Resolve(false, true, true, false),
            new PlayerProgressState(CashPerPain, projection.Player),
            new WorkProgressState(),
            [new BuddyIdentityState(projection.Buddy)],
            [projection.Scene],
            projection.Scene.SceneId,
            Store(files));
    }

    private static SceneProgressTransactionStore Store(MemoryFiles files) => new(SaveRoot, files);

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
