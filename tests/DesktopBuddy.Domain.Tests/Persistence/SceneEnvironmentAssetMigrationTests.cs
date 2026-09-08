using System;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SceneEnvironmentAssetMigrationTests
{
    [Fact]
    public async Task Legacy_background_is_copied_to_reserved_scene_without_deleting_source()
    {
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-scene-asset-migration-{Guid.NewGuid():N}");
        var files = new CharacterFileSystem();
        var legacy = new EnvironmentPaintStore(files, root);
        var scene = EnvironmentPaintStore.ForScene(files, root, SceneId.LegacyHome);
        var migration = new SceneEnvironmentAssetMigration(files, root, SceneId.LegacyHome);

        try
        {
            var canvas = new EnvironmentCanvas
            {
                Color = new EnvironmentColor(80, 30, 150),
                Tool = EnvironmentPaintTool.Fill,
            };
            canvas.Begin(.5, .5);
            canvas.End(.5, .5);
            await legacy.SaveAsync(canvas.Pixels);

            await migration.CommitLegacyAssetsAsync();
            Assert.Equal(canvas.ClonePixels(), legacy.Load());
            Assert.Equal(canvas.ClonePixels(), scene.Load());

            // A retry after a failed semantic transaction is intentionally idempotent.
            await migration.CommitLegacyAssetsAsync();
            Assert.Equal(canvas.ClonePixels(), legacy.Load());
            Assert.Equal(canvas.ClonePixels(), scene.Load());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_legacy_background_is_a_noop()
    {
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-scene-asset-empty-{Guid.NewGuid():N}");
        var files = new CharacterFileSystem();
        var migration = new SceneEnvironmentAssetMigration(files, root, SceneId.LegacyHome);
        var scene = EnvironmentPaintStore.ForScene(files, root, SceneId.LegacyHome);

        try
        {
            await migration.CommitLegacyAssetsAsync();
            Assert.Null(scene.Load());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
