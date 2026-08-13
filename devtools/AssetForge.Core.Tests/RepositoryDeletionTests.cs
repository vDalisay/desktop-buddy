using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class RepositoryDeletionTests
{
    [Fact]
    public void Delete_removes_every_exported_file_and_empties_the_catalogues()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-asset-forge-delete-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />\n");
            byte[] png = PngCodec.EncodeRgba8(TestGlassesImage());
            AssetRecipe recipe = Recipe();
            RepositoryExporter.ExportGlasses(root, png, AssetForgeGenerator.Generate(png, recipe), png);
            Assert.Single(RepositoryExporter.ListExported(root));

            RepositoryExporter.Delete(root, recipe.FeatureId);

            Assert.Empty(RepositoryExporter.ListExported(root));
            Assert.False(Directory.Exists(Path.Combine(root, "assets", "generated", "cosmetics", recipe.FeatureId)));
            Assert.False(File.Exists(Path.Combine(root, "data", "cosmetics", "generated", recipe.FeatureId + ".tres")));
            Assert.False(File.Exists(Path.Combine(root, "data", "catalogue", "generated", recipe.ContentId.Replace('.', '_') + ".tres")));
            string cosmeticCatalogue = File.ReadAllText(Path.Combine(root, "data", "cosmetics", "generated", "catalogue.tres"));
            string saleCatalogue = File.ReadAllText(Path.Combine(root, "data", "catalogue", "generated_cosmetics.tres"));
            Assert.Contains("Entries = Array[Resource]([])", cosmeticCatalogue, StringComparison.Ordinal);
            Assert.Contains("Entries = Array[Resource]([])", saleCatalogue, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", cosmeticCatalogue, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", saleCatalogue, StringComparison.Ordinal);
            Assert.True(RepositoryAssetVerifier.VerifyAll(root).Passed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AssetRecipe Recipe() => AssetRecipe.GlassesDefaults() with
    {
        FeatureId = "glasses.delete_test",
        ContentId = "cosmetic.glasses.delete_test",
        DisplayName = "Delete Test",
        Geometry = AssetRecipe.GlassesDefaults().Geometry with
        {
            GeometryResolution = 128,
            RuntimeTextureResolution = 256,
            SymmetryMode = SymmetryMode.Off,
        },
    };

    private static RgbaImage TestGlassesImage()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        DrawFrame(pixels, 215, 390, 475, 620, 38);
        DrawFrame(pixels, 549, 390, 809, 620, 38);
        FillPink(pixels, 470, 485, 554, 525);
        return new RgbaImage(size, size, pixels);
    }

    private static void DrawFrame(byte[] pixels, int x0, int y0, int x1, int y1, int thickness)
    {
        FillPink(pixels, x0, y0, x1, y0 + thickness);
        FillPink(pixels, x0, y1 - thickness, x1, y1);
        FillPink(pixels, x0, y0, x0 + thickness, y1);
        FillPink(pixels, x1 - thickness, y0, x1, y1);
    }

    private static void FillPink(byte[] pixels, int x0, int y0, int x1, int y1)
    {
        const int size = 1024;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = (y * size + x) * 4;
            pixels[i] = 239;
            pixels[i + 1] = 123;
            pixels[i + 2] = 175;
            pixels[i + 3] = 255;
        }
    }
}
