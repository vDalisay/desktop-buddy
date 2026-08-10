using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class ProgressStoreTests
{
    [Fact]
    public async Task AtomicSave_RotatesExactlyOneBackup()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        var first = new ProgressSave
        {
            Revision = 1,
            UnlockedToolIds = [ContentIds.ToolGrab],
        };
        var second = first with { Revision = 2, BalanceMilliCredits = 1000 };

        await store.SaveProgressAsync(first, default);
        await store.SaveProgressAsync(second, default);

        Assert.Contains("\"revision\": 2", files.Text(ProgressPath));
        Assert.Contains("\"revision\": 1", files.Text(ProgressPath + ".bak"));
        Assert.Equal(1, files.ReplaceCount);
        Assert.Equal(2, files.DurableWriteCount);
    }

    [Fact]
    public async Task AtomicFailure_PreservesPrimaryAndThrows()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        await store.SaveProgressAsync(new ProgressSave
        {
            Revision = 1,
            UnlockedToolIds = [ContentIds.ToolGrab],
        }, default);
        files.FailReplace = true;

        await Assert.ThrowsAsync<IOException>(() => store.SaveProgressAsync(
            new ProgressSave
            {
                Revision = 2,
                UnlockedToolIds = [ContentIds.ToolGrab],
            },
            default));

        Assert.Contains("\"revision\": 1", files.Text(ProgressPath));
    }

    [Fact]
    public async Task CorruptPrimary_IsQuarantinedBeforeBackupRecovery()
    {
        var files = new MemoryFiles();
        files.Set(ProgressPath, "{broken");
        files.Set(ProgressPath + ".bak", ProgressSavePolicy.Serialize(new ProgressSave
        {
            Revision = 7,
            UnlockedToolIds = [ContentIds.ToolGrab],
        }));
        var store = Store(files);

        LoadResult<ProgressSave> result = await store.LoadProgressAsync(default);

        Assert.Equal(SaveLoadStatus.BackupRecovered, result.Status);
        Assert.Equal(7, result.Value!.Revision);
        Assert.Equal(ProgressPath + ".corrupt-20260726120000000", result.QuarantinedPath);
        Assert.True(files.Exists(result.QuarantinedPath!));
    }

    [Fact]
    public async Task CorruptPrimaryAndBackup_RecoverDefaults()
    {
        var files = new MemoryFiles();
        files.Set(ProgressPath, "bad");
        files.Set(ProgressPath + ".bak", "also bad");

        LoadResult<ProgressSave> result = await Store(files).LoadProgressAsync(default);

        Assert.Equal(SaveLoadStatus.DefaultsRecovered, result.Status);
        Assert.Equal(0, result.Value!.BalanceMilliCredits);
        Assert.True(files.Paths.Count >= 2);
    }

    [Fact]
    public async Task FuturePrimary_IsNotQuarantinedOrOverwritten()
    {
        var files = new MemoryFiles();
        files.Set(ProgressPath, """{"schemaVersion":999}""");

        LoadResult<ProgressSave> result = await Store(files).LoadProgressAsync(default);

        Assert.Equal(SaveLoadStatus.UnsupportedFutureVersion, result.Status);
        Assert.True(files.Exists(ProgressPath));
        Assert.DoesNotContain(files.Paths, path => path.Contains(".corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsWrite_NeverTouchesProgressPath()
    {
        var files = new MemoryFiles();
        var store = Store(files);

        await store.SaveSettingsAsync(new LocalSettingsSave(), default);

        Assert.True(files.Exists(SettingsPath));
        Assert.False(files.Exists(ProgressPath));
    }

    [Fact]
    public async Task EnvironmentDecoratorPreferences_RoundTripOnlyThroughLocalSettings()
    {
        var files = new MemoryFiles();
        var store = Store(files);
        var wanted = new LocalSettingsSave
        {
            Revision = 9,
            EnvironmentSnapToGrid = true,
            EnvironmentGridSize = EnvironmentGridSize.Fine,
        };

        await store.SaveSettingsAsync(wanted, default);
        LoadResult<LocalSettingsSave> loaded = await store.LoadSettingsAsync(default);

        Assert.Equal(SaveLoadStatus.Loaded, loaded.Status);
        Assert.NotNull(loaded.Value);
        Assert.True(loaded.Value!.EnvironmentSnapToGrid);
        Assert.Equal(EnvironmentGridSize.Fine, loaded.Value.EnvironmentGridSize);
        Assert.False(files.Exists(ProgressPath));
    }

    private const string ProgressPath = "C:\\save-test\\progress.json";
    private const string SettingsPath = "C:\\save-test\\settings.json";

    private static JsonProgressStore Store(MemoryFiles files) =>
        new(
            ProgressPath,
            SettingsPath,
            files,
            () => new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero));

    private sealed class MemoryFiles : IAtomicSaveFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
        public bool FailReplace { get; set; }
        public int ReplaceCount { get; private set; }
        public int DurableWriteCount { get; private set; }
        public IReadOnlyCollection<string> Paths => _files.Keys;
        public bool Exists(string path) => _files.ContainsKey(path);
        public string ReadAllText(string path) => _files[path];
        public void CreateDirectory(string path) { }
        public void Set(string path, string contents) => _files[path] = contents;
        public string Text(string path) => _files[path];
        public void WriteDurable(string path, string contents)
        {
            DurableWriteCount++;
            _files[path] = contents;
        }
        public void Replace(string temporary, string primary, string backup)
        {
            if (FailReplace) throw new IOException("Injected replace failure.");
            ReplaceCount++;
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
