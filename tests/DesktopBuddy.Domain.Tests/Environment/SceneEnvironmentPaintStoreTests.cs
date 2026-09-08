using System;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Environment;

public sealed class SceneEnvironmentPaintStoreTests
{
    [Fact]
    public async Task Scene_paint_stores_use_stable_scene_paths_and_do_not_share_bytes()
    {
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-scene-paint-{Guid.NewGuid():N}");
        SceneId home = SceneId.From(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        SceneId lab = SceneId.From(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var files = new CharacterFileSystem();
        var homeStore = EnvironmentPaintStore.ForScene(files, root, home);
        var labStore = EnvironmentPaintStore.ForScene(files, root, lab);

        try
        {
            Assert.EndsWith(
                SceneStoragePaths.BackgroundPaint(home).Replace('/', Path.DirectorySeparatorChar),
                homeStore.PaintPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(
                SceneStoragePaths.BackgroundPaint(lab).Replace('/', Path.DirectorySeparatorChar),
                labStore.PaintPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(homeStore.PaintPath, labStore.PaintPath);

            var homeCanvas = new EnvironmentCanvas
            {
                Color = new EnvironmentColor(10, 20, 30),
                Tool = EnvironmentPaintTool.Fill,
            };
            homeCanvas.Begin(.5, .5);
            homeCanvas.End(.5, .5);
            await homeStore.SaveAsync(homeCanvas.Pixels);

            Assert.Equal(homeCanvas.ClonePixels(), homeStore.Load());
            Assert.Null(labStore.Load());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Legacy_store_keeps_the_initial_demo_global_path()
    {
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-legacy-paint-{Guid.NewGuid():N}");
        var store = new EnvironmentPaintStore(new CharacterFileSystem(), root);

        Assert.EndsWith(
            EnvironmentCanvasPolicy.RelativePath.Replace('/', Path.DirectorySeparatorChar),
            store.PaintPath,
            StringComparison.OrdinalIgnoreCase);
    }
}
