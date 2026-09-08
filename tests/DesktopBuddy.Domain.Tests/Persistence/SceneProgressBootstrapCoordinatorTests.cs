using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SceneProgressBootstrapCoordinatorTests
{
    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\scene-bootstrap-test" : "/scene-bootstrap-test";
    private static readonly string ProgressPath = Path.Combine(SaveRoot, SteamCloudSavePolicy.ProgressFileName);

    [Fact]
    public void Non_scene_build_scope_cannot_construct_scene_bootstrap()
    {
        BuildScopePolicy initialDemo = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo: true,
            nextFestDemo: false,
            fullRelease: false);
        var files = new MemoryFiles();
        var transactions = new SceneProgressTransactionStore(SaveRoot, files);
        var migration = new NextFestMigrationStore(ProgressPath, files);

        Assert.Throws<ArgumentException>(() =>
            new SceneProgressBootstrapCoordinator(initialDemo, CashPerPain, transactions, migration));
    }

    [Fact]
    public async Task First_scene_boot_migrates_legacy_graph_and_preserves_work_progress()
    {
        var files = new MemoryFiles();
        SceneProgressBootstrapCoordinator bootstrap = Bootstrap(files);
        ProgressSave legacy = Legacy(balance: 123_000, keyboardPresses: 77);
        int assetCommits = 0;

        SceneProgressBootstrapResult result = await bootstrap.LoadOrMigrateAsync(
            legacy,
            new CanonicalRoomPosition(0.5f, 0.5f),
            _ =>
            {
                assetCommits++;
                return Task.CompletedTask;
            });

        Assert.Equal(SceneProgressBootstrapSource.LegacyMigration, result.Source);
        Assert.Equal(1, assetCommits);
        Assert.True(result.Coordinator.UsesExpectedLegacyPrimary());
        Assert.Equal(123_000, result.Coordinator.Player.BalanceMilliCredits);
        Assert.Equal(77, result.Coordinator.Work.Lifetime.KeyboardPresses);
        Assert.Single(result.Coordinator.Scenes);
        Assert.Equal(result.Coordinator.ActiveSceneId, result.Coordinator.Scenes[0].SceneId);
        Assert.True(new SceneProgressTransactionStore(SaveRoot, files).HasCommittedGeneration);
    }

    [Fact]
    public async Task Later_boot_loads_committed_generation_without_rerunning_legacy_migration_or_assets()
    {
        var files = new MemoryFiles();
        SceneProgressBootstrapCoordinator first = Bootstrap(files);
        SceneProgressBootstrapResult migrated = await first.LoadOrMigrateAsync(
            Legacy(balance: 45_000, keyboardPresses: 12),
            new CanonicalRoomPosition(0.4f, 0.6f));

        SceneProgressBootstrapCoordinator second = Bootstrap(files);
        int assetCommits = 0;
        SceneProgressBootstrapResult loaded = await second.LoadOrMigrateAsync(
            Legacy(balance: 999_000, keyboardPresses: 999),
            new CanonicalRoomPosition(0.1f, 0.1f),
            _ =>
            {
                assetCommits++;
                throw new InvalidOperationException("Committed generations must not rerun migration assets.");
            });

        Assert.Equal(SceneProgressBootstrapSource.CommittedGeneration, loaded.Source);
        Assert.Equal(0, assetCommits);
        Assert.Equal(45_000, loaded.Coordinator.Player.BalanceMilliCredits);
        Assert.Equal(12, loaded.Coordinator.Work.Lifetime.KeyboardPresses);
        Assert.Equal(migrated.Coordinator.LastCommittedRevision, loaded.Coordinator.LastCommittedRevision);
        Assert.Equal(migrated.Coordinator.ActiveSceneId, loaded.Coordinator.ActiveSceneId);
    }

    [Fact]
    public async Task Asset_failure_keeps_scene_manifest_absent_and_retryable()
    {
        var files = new MemoryFiles();
        SceneProgressBootstrapCoordinator bootstrap = Bootstrap(files);
        ProgressSave legacy = Legacy(balance: 88_000, keyboardPresses: 3);

        await Assert.ThrowsAsync<IOException>(() => bootstrap.LoadOrMigrateAsync(
            legacy,
            new CanonicalRoomPosition(0.5f, 0.5f),
            _ => throw new IOException("Injected room-paint copy failure.")));

        Assert.False(new SceneProgressTransactionStore(SaveRoot, files).HasCommittedGeneration);

        SceneProgressBootstrapResult retry = await Bootstrap(files).LoadOrMigrateAsync(
            legacy,
            new CanonicalRoomPosition(0.5f, 0.5f));
        Assert.Equal(SceneProgressBootstrapSource.LegacyMigration, retry.Source);
        Assert.Equal(88_000, retry.Coordinator.Player.BalanceMilliCredits);
    }

    private static SceneProgressBootstrapCoordinator Bootstrap(MemoryFiles files)
    {
        BuildScopePolicy nextFest = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo: true,
            nextFestDemo: true,
            fullRelease: false);
        var transactions = new SceneProgressTransactionStore(SaveRoot, files);
        var migration = new NextFestMigrationStore(ProgressPath, files);
        return new SceneProgressBootstrapCoordinator(nextFest, CashPerPain, transactions, migration);
    }

    private static ProgressSave Legacy(long balance, long keyboardPresses) => new()
    {
        Revision = 9,
        BalanceMilliCredits = balance,
        UnlockedToolIds = [ContentIds.ToolGrab],
        SelectedToolId = ContentIds.ToolGrab,
        Mood = 15.0f,
        Fullness = 60.0f,
        Work = new WorkProgressSave
        {
            Revision = 4,
            KeyboardPresses = keyboardPresses,
            MouseClicks = 5,
        },
        Environment = new EnvironmentProgressSave(),
    };

    private sealed class MemoryFiles : IAtomicSaveFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public void WriteDurable(string path, string contents) => _files[path] = contents;
        public void Replace(string temporary, string primary, string backup)
        {
            if (!_files.ContainsKey(primary))
                throw new FileNotFoundException("Injected filesystem replace requires a primary.", primary);
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

internal static class SceneProgressCoordinatorTestExtensions
{
    public static bool UsesExpectedLegacyPrimary(this SceneProgressCoordinator coordinator)
    {
        if (coordinator.BuddyIdentityCount != 1)
            return false;
        return coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? buddy) && buddy is not null;
    }
}
