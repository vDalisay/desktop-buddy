using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Persistence;

public interface IAtomicSaveFileSystem
{
    bool Exists(string path);
    string ReadAllText(string path);
    void CreateDirectory(string path);
    void WriteDurable(string path, string contents);
    void Replace(string temporary, string primary, string backup);
    void Move(string source, string destination);
}

public sealed class AtomicSaveFileSystem : IAtomicSaveFileSystem
{
    public bool Exists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteDurable(string path, string contents)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
        using var stream = new FileStream(
            path,
            FileMode.Create,
            System.IO.FileAccess.Write,
            FileShare.None,
            16_384,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public void Replace(string temporary, string primary, string backup) =>
        File.Replace(temporary, primary, backup, ignoreMetadataErrors: true);

    public void Move(string source, string destination) =>
        File.Move(source, destination);
}

/// <summary>
/// Durable versioned JSON store. Paths must already be resolved on Godot's main
/// thread; this class contains no native Godot object and serializes off-thread on native builds.
/// Single-threaded browser-WASM executes the same work inline through <see cref="PersistenceWork"/>.
/// A browser build injects its Godot-backed filesystem adapter from the composition root.
/// </summary>
public sealed class JsonProgressStore : IProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _progressPath;
    private readonly string _settingsPath;
    private readonly IAtomicSaveFileSystem _files;
    private readonly Func<DateTimeOffset> _utcNow;

    public JsonProgressStore(
        string progressPath,
        string settingsPath,
        IAtomicSaveFileSystem? files = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(progressPath) || string.IsNullOrWhiteSpace(settingsPath))
            throw new ArgumentException("Resolved progress and settings paths are required.");
        _progressPath = Path.GetFullPath(progressPath);
        _settingsPath = Path.GetFullPath(settingsPath);
        _files = files ?? new AtomicSaveFileSystem();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<LoadResult<ProgressSave>> LoadProgressAsync(CancellationToken token) =>
        PersistenceWork.Run(() => LoadProgress(token), token);

    public Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(CancellationToken token) =>
        PersistenceWork.Run(() => LoadSettings(token), token);

    public async Task SaveProgressAsync(ProgressSave data, CancellationToken token)
    {
        string json = await PersistenceWork.Run(() => ProgressSavePolicy.Serialize(data), token)
            .ConfigureAwait(false);
        await PersistenceWork.Run(() => SaveAtomic(_progressPath, json, token), token)
            .ConfigureAwait(false);
    }

    public async Task SaveSettingsAsync(LocalSettingsSave data, CancellationToken token)
    {
        ValidateSettings(data);
        string json = await PersistenceWork.Run(
            () => JsonSerializer.Serialize(data, JsonOptions), token).ConfigureAwait(false);
        await PersistenceWork.Run(() => SaveAtomic(_settingsPath, json, token), token)
            .ConfigureAwait(false);
    }

    private LoadResult<ProgressSave> LoadProgress(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string backup = BackupPath(_progressPath);
        if (!_files.Exists(_progressPath))
        {
            return LoadProgressBackupOrDefault(backup, null, token);
        }

        SaveDecodeResult primary = ProgressSavePolicy.Decode(_files.ReadAllText(_progressPath));
        if (primary.Status == SaveDecodeStatus.Valid)
            return new LoadResult<ProgressSave>(SaveLoadStatus.Loaded, primary.Save);
        if (primary.Status == SaveDecodeStatus.UnsupportedFutureVersion)
        {
            return new LoadResult<ProgressSave>(
                SaveLoadStatus.UnsupportedFutureVersion, null, primary.Detail);
        }

        string quarantined = Quarantine(_progressPath);
        return LoadProgressBackupOrDefault(backup, quarantined, token);
    }

    private LoadResult<ProgressSave> LoadProgressBackupOrDefault(
        string backup,
        string? quarantined,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_files.Exists(backup))
        {
            SaveDecodeResult decoded = ProgressSavePolicy.Decode(_files.ReadAllText(backup));
            if (decoded.Status == SaveDecodeStatus.Valid)
            {
                return new LoadResult<ProgressSave>(
                    SaveLoadStatus.BackupRecovered,
                    decoded.Save,
                    "Recovered from rolling backup.",
                    quarantined);
            }
            if (decoded.Status == SaveDecodeStatus.UnsupportedFutureVersion)
            {
                return new LoadResult<ProgressSave>(
                    SaveLoadStatus.UnsupportedFutureVersion, null, decoded.Detail, quarantined);
            }
            Quarantine(backup);
        }

        return new LoadResult<ProgressSave>(
            quarantined is null ? SaveLoadStatus.NewSave : SaveLoadStatus.DefaultsRecovered,
            new ProgressSave(),
            quarantined is null ? "No progress file." : "Corrupt progress recovered to defaults.",
            quarantined);
    }

    private LoadResult<LocalSettingsSave> LoadSettings(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string backup = BackupPath(_settingsPath);
        if (TryReadSettings(_settingsPath, out LocalSettingsSave? settings))
            return new LoadResult<LocalSettingsSave>(SaveLoadStatus.Loaded, settings);

        string? quarantined = _files.Exists(_settingsPath) ? Quarantine(_settingsPath) : null;
        if (TryReadSettings(backup, out settings))
        {
            return new LoadResult<LocalSettingsSave>(
                SaveLoadStatus.BackupRecovered, settings, "Recovered settings backup.", quarantined);
        }
        if (_files.Exists(backup))
            Quarantine(backup);
        return new LoadResult<LocalSettingsSave>(
            quarantined is null ? SaveLoadStatus.NewSave : SaveLoadStatus.DefaultsRecovered,
            new LocalSettingsSave(),
            null,
            quarantined);
    }

    private bool TryReadSettings(string path, out LocalSettingsSave? settings)
    {
        settings = null;
        if (!_files.Exists(path))
            return false;
        try
        {
            settings = JsonSerializer.Deserialize<LocalSettingsSave>(
                _files.ReadAllText(path), JsonOptions);
            if (settings is null)
                return false;
            ValidateSettings(settings);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void SaveAtomic(string primary, string json, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(primary)
            ?? throw new InvalidOperationException("Save path has no parent directory.");
        _files.CreateDirectory(directory);
        string temporary = primary + ".tmp";
        _files.WriteDurable(temporary, json);
        token.ThrowIfCancellationRequested();
        if (_files.Exists(primary))
            _files.Replace(temporary, primary, BackupPath(primary));
        else
            _files.Move(temporary, primary);
    }

    private string Quarantine(string path)
    {
        string quarantined = path + $".corrupt-{_utcNow():yyyyMMddHHmmssfff}";
        _files.Move(path, quarantined);
        return quarantined;
    }

    private static string BackupPath(string primary) => primary + ".bak";

    private static void ValidateSettings(LocalSettingsSave settings)
    {
        if (settings.SchemaVersion != LocalSettingsSave.CurrentSchemaVersion ||
            settings.Revision < 0 ||
            settings.WindowWidth < 360 || settings.WindowHeight < 270 ||
            settings.WorkWindowWidth < 0 || settings.WorkWindowHeight < 0 ||
            settings.Dpi <= 0 ||
            settings.ZoomPercent is not (75 or 100 or 125 or 150 or 175 or 200) ||
            settings.UiScalePercent is not (100 or 125 or 150 or 175 or 200) ||
            !float.IsFinite(settings.MasterVolume) ||
            !float.IsFinite(settings.SfxVolume) ||
            !float.IsFinite(settings.UiVolume) ||
            settings.MasterVolume is < 0.0f or > 1.0f ||
            settings.SfxVolume is < 0.0f or > 1.0f ||
            settings.UiVolume is < 0.0f or > 1.0f ||
            settings.MaxFps is < 0 or > 480 ||
            settings.BackgroundMaxFps is < 0 or > 480 ||
            settings.StartupInputMode is not ("remember" or "work" or "play"))
        {
            throw new ArgumentException("Local settings payload is invalid.", nameof(settings));
        }
    }
}
