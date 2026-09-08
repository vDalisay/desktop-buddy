using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class LegacyProgressCompatibilityStoreTests
{
    private const string SaveRoot = "/save";
    private const string ManifestPath = "/save/scene-progress.commit.json";

    [Fact]
    public async Task Legacy_progress_load_is_blocked_without_touching_inner_store_after_scene_commit()
    {
        var inner = new RecordingStore();
        var files = new MemoryFiles();
        files.Set(ManifestPath, "committed");
        var guarded = new LegacyProgressCompatibilityStore(inner, SaveRoot, files);

        LoadResult<ProgressSave> result = await guarded.LoadProgressAsync(default);

        Assert.Equal(SaveLoadStatus.UnsupportedFutureVersion, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("Scene progress", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, inner.ProgressLoads);
    }

    [Fact]
    public async Task Legacy_progress_write_is_blocked_after_scene_commit_but_settings_still_flow()
    {
        var inner = new RecordingStore();
        var files = new MemoryFiles();
        files.Set(ManifestPath, "committed");
        var guarded = new LegacyProgressCompatibilityStore(inner, SaveRoot, files);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guarded.SaveProgressAsync(new ProgressSave(), default));
        await guarded.SaveSettingsAsync(new LocalSettingsSave(), default);
        LoadResult<LocalSettingsSave> loaded = await guarded.LoadSettingsAsync(default);

        Assert.Equal(0, inner.ProgressWrites);
        Assert.Equal(1, inner.SettingsWrites);
        Assert.Equal(1, inner.SettingsLoads);
        Assert.Equal(SaveLoadStatus.Loaded, loaded.Status);
    }

    [Fact]
    public async Task Legacy_store_behavior_is_unchanged_before_scene_manifest_exists()
    {
        var inner = new RecordingStore();
        var guarded = new LegacyProgressCompatibilityStore(inner, SaveRoot, new MemoryFiles());

        LoadResult<ProgressSave> loaded = await guarded.LoadProgressAsync(default);
        await guarded.SaveProgressAsync(new ProgressSave { Revision = 4 }, default);

        Assert.Equal(SaveLoadStatus.Loaded, loaded.Status);
        Assert.Equal(1, inner.ProgressLoads);
        Assert.Equal(1, inner.ProgressWrites);
    }

    private sealed class RecordingStore : IProgressStore
    {
        public int ProgressLoads { get; private set; }
        public int ProgressWrites { get; private set; }
        public int SettingsLoads { get; private set; }
        public int SettingsWrites { get; private set; }

        public Task<LoadResult<ProgressSave>> LoadProgressAsync(CancellationToken token)
        {
            ProgressLoads++;
            return Task.FromResult(new LoadResult<ProgressSave>(
                SaveLoadStatus.Loaded,
                new ProgressSave()));
        }

        public Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(CancellationToken token)
        {
            SettingsLoads++;
            return Task.FromResult(new LoadResult<LocalSettingsSave>(
                SaveLoadStatus.Loaded,
                new LocalSettingsSave()));
        }

        public Task SaveProgressAsync(ProgressSave data, CancellationToken token)
        {
            ProgressWrites++;
            return Task.CompletedTask;
        }

        public Task SaveSettingsAsync(LocalSettingsSave data, CancellationToken token)
        {
            SettingsWrites++;
            return Task.CompletedTask;
        }
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
        public void Set(string path, string contents) => _files[path] = contents;
    }
}
