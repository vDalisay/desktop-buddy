using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Fail-closed compatibility boundary for build surfaces that still use the schema-8 aggregate.
/// Once a committed Scene-progress manifest exists beside progress.json, the richer split graph is
/// authoritative. A legacy loader must not quarantine, reinterpret or overwrite that account-only
/// progress document merely because the current executable cannot activate Scenes.
///
/// Settings remain machine-local and independent, so their reads/writes continue through the inner
/// store even while semantic progress is protected.
/// </summary>
public sealed class LegacyProgressCompatibilityStore : IProgressStore
{
    private readonly IProgressStore _inner;
    private readonly IAtomicSaveFileSystem _files;
    private readonly string _sceneManifestPath;

    public LegacyProgressCompatibilityStore(
        IProgressStore inner,
        string resolvedSaveRoot,
        IAtomicSaveFileSystem files)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        if (string.IsNullOrWhiteSpace(resolvedSaveRoot))
            throw new ArgumentException("A resolved save root is required.", nameof(resolvedSaveRoot));

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedSaveRoot));
        _sceneManifestPath = Path.Combine(root, SceneProgressTransactionStore.ManifestFileName);
    }

    public bool SceneProgressIsAuthoritative => _files.Exists(_sceneManifestPath);

    public Task<LoadResult<ProgressSave>> LoadProgressAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (SceneProgressIsAuthoritative)
        {
            return Task.FromResult(new LoadResult<ProgressSave>(
                SaveLoadStatus.UnsupportedFutureVersion,
                null,
                "This save has been upgraded to Scene progress. The legacy aggregate is read-only to prevent destructive downgrade."));
        }

        return _inner.LoadProgressAsync(token);
    }

    public Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(CancellationToken token) =>
        _inner.LoadSettingsAsync(token);

    public Task SaveProgressAsync(ProgressSave data, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(data);
        token.ThrowIfCancellationRequested();
        if (SceneProgressIsAuthoritative)
        {
            throw new InvalidOperationException(
                "Legacy progress writes are blocked after Scene progress becomes authoritative.");
        }

        return _inner.SaveProgressAsync(data, token);
    }

    public Task SaveSettingsAsync(LocalSettingsSave data, CancellationToken token) =>
        _inner.SaveSettingsAsync(data, token);
}
