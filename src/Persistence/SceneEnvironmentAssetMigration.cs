using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Retry-safe file half of Initial Demo -> Scene Environment migration. Semantic room state is
/// committed by <see cref="SceneProgressTransactionStore"/>; this type copies the legacy painted
/// background into the reserved Home Scene before that transaction publishes its manifest.
///
/// The source is intentionally retained. A failure before manifest commit must leave the Initial
/// Demo save fully retryable, and retaining the old local asset also avoids making a temporary
/// downgrade to the Initial Demo look like data loss. Re-running the copy is deterministic.
/// </summary>
public sealed class SceneEnvironmentAssetMigration
{
    private readonly EnvironmentPaintStore _legacy;
    private readonly EnvironmentPaintStore _scene;

    public SceneEnvironmentAssetMigration(
        ICharacterFileSystem fileSystem,
        string resolvedRoot,
        SceneId destinationSceneId)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (!destinationSceneId.IsValid)
            throw new ArgumentException("Environment asset migration requires a stable Scene ID.", nameof(destinationSceneId));
        _legacy = new EnvironmentPaintStore(fileSystem, resolvedRoot);
        _scene = EnvironmentPaintStore.ForScene(fileSystem, resolvedRoot, destinationSceneId);
    }

    public async Task CommitLegacyAssetsAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        byte[]? pixels = _legacy.Load();
        if (pixels is null)
            return;

        await _scene.SaveAsync(pixels, token).ConfigureAwait(false);
    }
}
