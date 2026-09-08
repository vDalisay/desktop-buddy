using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SceneProgressBootstrapEnvironmentTests
{
    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot = OperatingSystem.IsWindows()
        ? @"C:\scene-bootstrap-environment-test"
        : "/scene-bootstrap-environment-test";
    private static readonly string ProgressPath = Path.Combine(SaveRoot, SteamCloudSavePolicy.ProgressFileName);

    [Fact]
    public async Task Legacy_environment_revision_and_storage_migrate_once_and_survive_restart()
    {
        var files = new MemoryFiles();
        var stored = new DecorationDefinitionId("decoration.lamp.migrated");
        var legacy = new ProgressSave
        {
            Revision = 3,
            BalanceMilliCredits = 125_000,
            UnlockedToolIds = [ContentIds.ToolGrab],
            SelectedToolId = ContentIds.ToolGrab,
            Environment = new EnvironmentProgressSave
            {
                Revision = 11,
                OwnedUnplaced = [stored.Value, stored.Value],
            },
        };

        SceneProgressBootstrapResult migrated = await Bootstrap(files).LoadOrMigrateAsync(
            legacy,
            new CanonicalRoomPosition(0.5f, 0.5f));

        Assert.Equal(SceneProgressBootstrapSource.LegacyMigration, migrated.Source);
        Assert.Equal(11, migrated.Coordinator.ActiveScene.EnvironmentRevision);
        Assert.Equal(new[] { stored, stored }, migrated.Coordinator.ActiveScene.OwnedUnplaced);

        int legacyLoads = 0;
        SceneProgressBootstrapResult restarted = await Bootstrap(files).LoadOrMigrateAsync(
            _ =>
            {
                legacyLoads++;
                throw new InvalidOperationException("Committed Scene data must bypass legacy decode.");
            },
            new CanonicalRoomPosition(0.5f, 0.5f));

        Assert.Equal(0, legacyLoads);
        Assert.Equal(SceneProgressBootstrapSource.CommittedGeneration, restarted.Source);
        Assert.Equal(11, restarted.Coordinator.ActiveScene.EnvironmentRevision);
        Assert.Equal(new[] { stored, stored }, restarted.Coordinator.ActiveScene.OwnedUnplaced);
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
