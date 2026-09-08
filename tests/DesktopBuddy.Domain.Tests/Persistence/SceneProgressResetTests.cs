using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SceneProgressResetTests
{
    private const double CashPerPain = 0.01;
    private static readonly string SaveRoot =
        OperatingSystem.IsWindows() ? @"C:\scene-progress-reset-test" : "/scene-progress-reset-test";

    [Fact]
    public async Task Public_scene_reset_deletes_characters_only_after_manifest_commit()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator scenes = Coordinator(files);
        await scenes.FlushAsync(force: true);
        scenes.Player.Deposit(50_000);
        scenes.Work.Record(WorkActivityKind.KeyboardPress, 42);

        bool deleteCalled = false;
        bool reset = await ProgressReset.ResetSceneAsync(
            scenes,
            deleteCharacters: _ =>
            {
                Assert.False(scenes.IsDirty);
                Assert.Equal(SceneId.LegacyHome, scenes.ActiveSceneId);
                Assert.Single(scenes.Scenes);
                deleteCalled = true;
                return Task.FromResult(4);
            });

        Assert.True(reset);
        Assert.True(deleteCalled);
        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Single(loaded.Scenes);
        Assert.Single(loaded.BuddyIdentities);
        Assert.Equal(SceneId.LegacyHome.Value, loaded.Index.ActiveSceneId);
    }

    [Fact]
    public async Task Failed_scene_reset_never_deletes_characters_and_keeps_previous_generation()
    {
        var files = new MemoryFiles();
        SceneProgressCoordinator scenes = Coordinator(files);
        await scenes.FlushAsync(force: true);
        scenes.Player.Deposit(17_000);
        await scenes.FlushAsync(force: true);
        string before = PlayerProgressSavePolicy.Serialize(scenes.Player);
        long committedBefore = scenes.LastCommittedRevision;

        bool deleteCalled = false;
        files.FailNextDurableWrite = true;
        bool reset = await ProgressReset.ResetSceneAsync(
            scenes,
            deleteCharacters: _ =>
            {
                deleteCalled = true;
                return Task.FromResult(1);
            });

        Assert.False(reset);
        Assert.False(deleteCalled);
        Assert.Equal(before, PlayerProgressSavePolicy.Serialize(scenes.Player));
        Assert.Equal(committedBefore, scenes.LastCommittedRevision);
        Assert.False(scenes.IsDirty);

        SceneProgressLoadResult loaded = await Store(files).LoadCommittedAsync(CashPerPain);
        Assert.Equal(before, PlayerProgressSavePolicy.Serialize(loaded.Player));
    }

    private static SceneProgressCoordinator Coordinator(MemoryFiles files)
    {
        var legacy = new BuddyProgressState(
            cashPerPain: CashPerPain,
            initialBalanceMilliCredits: 2_000);
        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            legacy.Snapshot(),
            activeCharacterId: null,
            new EnvironmentProgressSnapshot(0, new EnvironmentLayout(), []),
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
