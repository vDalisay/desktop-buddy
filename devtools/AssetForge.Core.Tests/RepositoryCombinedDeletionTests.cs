using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class RepositoryCombinedDeletionTests
{
    [Fact]
    public void Combined_delete_removes_one_environment_asset_and_rebuilds_aggregate_without_touching_peer()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe lamp = Fast(AssetRecipe.LampDefaults(), "decoration.lamp.delete_test");
            AssetRecipe table = Fast(AssetRecipe.TableDefaults(), "decoration.table.delete_peer");
            Export(root, lamp, LampSource());
            Export(root, table, TableSource());

            IReadOnlyList<ExportedAssetForgeAsset> before = RepositoryAssetForgeDeletion.ListExported(root);
            Assert.Contains(before, asset => asset.StableId == lamp.AssetId);
            Assert.Contains(before, asset => asset.StableId == table.AssetId);

            AssetForgeRepositoryVerificationResult result = RepositoryAssetForgeDeletion.Delete(root, lamp.AssetId);

            Assert.True(result.Passed, Diagnostics(result));
            Assert.False(Directory.Exists(Path.Combine(root, "authoring", "asset-forge", "lamps", "delete_test")));
            Assert.False(Directory.Exists(Path.Combine(root, "assets", "generated", "environment", lamp.AssetId)));
            Assert.False(File.Exists(Path.Combine(root, "data", "environment", "generated", lamp.AssetId + ".tres")));
            Assert.True(Directory.Exists(Path.Combine(root, "assets", "generated", "environment", table.AssetId)));
            string aggregate = File.ReadAllText(Path.Combine(root, "data", "environment", "generated_decorations.tres"));
            Assert.DoesNotContain(lamp.AssetId + ".tres", aggregate, StringComparison.Ordinal);
            Assert.Contains(table.AssetId + ".tres", aggregate, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", aggregate, StringComparison.Ordinal);

            IReadOnlyList<ExportedAssetForgeAsset> after = RepositoryAssetForgeDeletion.ListExported(root);
            Assert.DoesNotContain(after, asset => asset.StableId == lamp.AssetId);
            Assert.Contains(after, asset => asset.StableId == table.AssetId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AssetRecipe Fast(AssetRecipe recipe, string id) => recipe with
    {
        AssetId = id,
        Geometry = recipe.Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
        },
    };

    private static void Export(string root, AssetRecipe recipe, byte[] source)
    {
        GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
        RepositoryEnvironmentExporter.Export(root, source, generated, EnvironmentThumbnailGenerator.Create(generated.AlbedoPng));
    }

    private static byte[] LampSource()
    {
        byte[] p = new byte[1024 * 1024 * 4];
        Fill(p, 480, 360, 544, EnvironmentTemplateSpace.FloorY, 146, 90, 58);
        Fill(p, 360, 220, 664, 440, 232, 174, 66);
        Fill(p, 410, EnvironmentTemplateSpace.FloorY - 50, 614, EnvironmentTemplateSpace.FloorY, 116, 68, 46);
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, p));
    }

    private static byte[] TableSource()
    {
        byte[] p = new byte[1024 * 1024 * 4];
        Fill(p, 280, 480, 744, 555, 165, 104, 65);
        Fill(p, 330, 555, 380, EnvironmentTemplateSpace.FloorY, 110, 68, 45);
        Fill(p, 644, 555, 694, EnvironmentTemplateSpace.FloorY, 110, 68, 45);
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, p));
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = (y * 1024 + x) * 4;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
        }
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-combined-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />\n");
        Directory.CreateDirectory(Path.Combine(root, "data", "environment"));
        File.WriteAllText(Path.Combine(root, "data", "environment", "generated_decorations.tres"),
            "[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationCatalogueResource\" load_steps=2 format=3]\n\n" +
            "[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationCatalogueResource.cs\" id=\"1\"]\n\n" +
            "[resource]\nscript = ExtResource(\"1\")\nEntries = Array[Resource]([])\n");
        return root;
    }

    private static string Diagnostics(AssetForgeRepositoryVerificationResult result) => string.Join(Environment.NewLine,
        result.BuddyStudio.RepositoryDiagnostics
            .Concat(result.Environment.RepositoryDiagnostics)
            .Concat(result.BuddyStudio.Assets.SelectMany(static asset => asset.Diagnostics))
            .Concat(result.Environment.Assets.SelectMany(static asset => asset.Diagnostics)));
}
