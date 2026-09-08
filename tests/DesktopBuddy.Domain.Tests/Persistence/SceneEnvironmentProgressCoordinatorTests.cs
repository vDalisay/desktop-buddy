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

public sealed class SceneEnvironmentProgressCoordinatorTests
{
    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot = OperatingSystem.IsWindows()
        ? @"C:\scene-environment-progress-test"
        : "/scene-environment-progress-test";

    private static readonly DecorationDefinition Lamp = new(
        new DecorationDefinitionId("decoration.lamp.scene_test"),
        "environment.lamp.scene_test",
        DecorationCategory.Lamp,
        75_000,
        DecorationAnchorKind.Floor,
        new DecorationRotationPolicy(true, 90),
        DecorationRenderBand.BehindBuddyFloor);

    private static readonly DecorationDefinition Plant = new(
        new DecorationDefinitionId("decoration.plant.scene_test"),
        "environment.plant.scene_test",
        DecorationCategory.Plant,
        40_000,
        DecorationAnchorKind.Floor,
        new DecorationRotationPolicy(true, 90),
        DecorationRenderBand.BehindBuddyFloor);

    [Fact]
    public async Task Scene_environment_commit_updates_wallet_layout_and_storage_in_one_generation()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        await coordinator.FlushAsync(force: true);
        Assert.Equal(0, coordinator.LastCommittedRevision);

        EnvironmentProgressSnapshot baseline = coordinator.ActiveEnvironmentProgress;
        var session = new EnvironmentEditSession(
            baseline,
            coordinator.Player.BalanceMilliCredits,
            new DecorationCatalogue([Lamp, Plant]),
            () => new PlacedDecorationId(Id(50)));

        Assert.True(session.Buy(Lamp.Id, coordinator.Player.BalanceMilliCredits).Succeeded);
        Assert.True(session.Place(Plant.Id, new CanonicalRoomPosition(0.6f, 0.8f)).Succeeded);

        await coordinator.CommitEnvironmentAsync(coordinator.ActiveSceneId, session);

        Assert.Equal(85_000, coordinator.Player.BalanceMilliCredits);
        Assert.Equal(baseline.Revision + 1, coordinator.ActiveEnvironmentProgress.Revision);
        Assert.Single(coordinator.ActiveScene.Environment.Decorations);
        Assert.Equal(Plant.Id, coordinator.ActiveScene.Environment.Decorations[0].DefinitionId);
        Assert.Equal(new[] { Lamp.Id }, coordinator.ActiveScene.OwnedUnplaced);
        Assert.Equal(1, coordinator.LastCommittedRevision);
        Assert.False(coordinator.IsDirty);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(85_000, loaded.Player.BalanceMilliCredits);
        Assert.Equal(baseline.Revision + 1, loaded.Scenes[0].EnvironmentRevision);
        Assert.Single(loaded.Scenes[0].Environment.Decorations);
        Assert.Equal(Plant.Id, loaded.Scenes[0].Environment.Decorations[0].DefinitionId);
        Assert.Equal(new[] { Lamp.Id }, loaded.Scenes[0].OwnedUnplaced);
    }

    [Fact]
    public async Task Failed_scene_environment_commit_restores_account_and_scene_exactly()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        await coordinator.FlushAsync(force: true);
        PlayerProgressSnapshot playerBefore = coordinator.Player.Snapshot();
        EnvironmentProgressSnapshot environmentBefore = coordinator.ActiveEnvironmentProgress;
        long committedBefore = coordinator.LastCommittedRevision;

        var session = new EnvironmentEditSession(
            environmentBefore,
            coordinator.Player.BalanceMilliCredits,
            new DecorationCatalogue([Lamp]),
            () => new PlacedDecorationId(Id(51)));
        Assert.True(session.Buy(Lamp.Id, coordinator.Player.BalanceMilliCredits).Succeeded);
        files.FailNextDurableWrite = true;

        await Assert.ThrowsAsync<IOException>(() =>
            coordinator.CommitEnvironmentAsync(coordinator.ActiveSceneId, session));

        PlayerProgressSnapshot playerAfter = coordinator.Player.Snapshot();
        Assert.Equal(playerBefore.Revision, playerAfter.Revision);
        Assert.Equal(playerBefore.BalanceMilliCredits, playerAfter.BalanceMilliCredits);
        Assert.Equal(playerBefore.SelectedToolId, playerAfter.SelectedToolId);
        Assert.Equal(environmentBefore.Revision, coordinator.ActiveEnvironmentProgress.Revision);
        Assert.Equal(environmentBefore.Layout.Decorations, coordinator.ActiveScene.Environment.Decorations);
        Assert.Equal(environmentBefore.OwnedUnplaced, coordinator.ActiveScene.OwnedUnplaced);
        Assert.Equal(committedBefore, coordinator.LastCommittedRevision);
        Assert.False(coordinator.IsDirty);
        Assert.NotNull(coordinator.LastFailure);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(playerBefore.BalanceMilliCredits, loaded.Player.BalanceMilliCredits);
        Assert.Equal(environmentBefore.Revision, loaded.Scenes[0].EnvironmentRevision);
        Assert.Equal(environmentBefore.OwnedUnplaced, loaded.Scenes[0].OwnedUnplaced);
    }

    [Fact]
    public async Task Stale_environment_session_is_rejected_without_spending_again()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator coordinator = Coordinator(files);
        await coordinator.FlushAsync(force: true);
        EnvironmentProgressSnapshot original = coordinator.ActiveEnvironmentProgress;

        var first = new EnvironmentEditSession(
            original,
            coordinator.Player.BalanceMilliCredits,
            new DecorationCatalogue([Lamp]),
            () => new PlacedDecorationId(Id(52)));
        var stale = new EnvironmentEditSession(
            original,
            coordinator.Player.BalanceMilliCredits,
            new DecorationCatalogue([Lamp]),
            () => new PlacedDecorationId(Id(53)));
        Assert.True(first.Buy(Lamp.Id, coordinator.Player.BalanceMilliCredits).Succeeded);
        Assert.True(stale.Buy(Lamp.Id, coordinator.Player.BalanceMilliCredits).Succeeded);

        await coordinator.CommitEnvironmentAsync(coordinator.ActiveSceneId, first);
        long balanceAfterFirst = coordinator.Player.BalanceMilliCredits;
        long revisionAfterFirst = coordinator.ActiveEnvironmentProgress.Revision;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.CommitEnvironmentAsync(coordinator.ActiveSceneId, stale));

        Assert.Equal(balanceAfterFirst, coordinator.Player.BalanceMilliCredits);
        Assert.Equal(revisionAfterFirst, coordinator.ActiveEnvironmentProgress.Revision);
        Assert.Single(coordinator.ActiveScene.OwnedUnplaced);
    }

    private static SceneProgressCoordinator Coordinator(MemoryFiles files)
    {
        var legacy = new BuddyProgressState(
            cashPerPain: CashPerPain,
            initialBalanceMilliCredits: 200_000);
        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            legacy.Snapshot(),
            activeCharacterId: null,
            new EnvironmentProgressSnapshot(4, new EnvironmentLayout(), []),
            new CanonicalRoomPosition(0.5f, 0.5f));
        var player = new PlayerProgressState(CashPerPain, projection.Player);
        var buddy = new BuddyIdentityState(projection.Buddy);
        return new SceneProgressCoordinator(
            NextFest(),
            player,
            new WorkProgressState(),
            [buddy],
            [projection.Scene],
            projection.Scene.SceneId,
            Store(files));
    }

    private static SceneProgressTransactionStore Store(MemoryFiles files) => new(SaveRoot, files);

    private static BuildScopePolicy NextFest() => BuildScopePolicy.Resolve(
        itchIo: false,
        steamDemo: true,
        nextFestDemo: true,
        fullRelease: false);

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

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
