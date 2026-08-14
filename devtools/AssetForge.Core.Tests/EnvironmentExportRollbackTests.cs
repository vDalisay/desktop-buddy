using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentExportRollbackTests
{
    [Fact]
    public void Mid_commit_destination_failure_rolls_back_files_already_replaced()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastSofa();
            byte[] originalSource = SofaSource(255, 340, 176, 122, 192);
            GeneratedAsset original = AssetForgeCompiler.Generate(originalSource, recipe);
            ExportResult first = RepositoryEnvironmentExporter.Export(
                root,
                originalSource,
                original,
                EnvironmentThumbnailGenerator.Create(original));

            string meshPath = Path.Combine(first.AssetDirectory, "mesh.glb");
            string albedoPath = Path.Combine(first.AssetDirectory, "albedo.png");
            string thumbnailPath = Path.Combine(first.AssetDirectory, "thumbnail.png");
            string sourcePath = Path.Combine(first.AuthoringDirectory, "source.png");
            string recipePath = Path.Combine(first.AuthoringDirectory, "recipe.json");
            byte[] meshBefore = File.ReadAllBytes(meshPath);
            byte[] albedoBefore = File.ReadAllBytes(albedoPath);
            byte[] thumbnailBefore = File.ReadAllBytes(thumbnailPath);
            byte[] sourceBefore = File.ReadAllBytes(sourcePath);
            string recipeBefore = File.ReadAllText(recipePath);

            // Force failure late in Commit(): assets/* and authoring/* sort before data/*, so those
            // files are replaced first. A directory occupying the definition file path makes the
            // later File.Copy fail and exercises the actual rollback path rather than preflight.
            string definitionPath = first.CosmeticDefinitionPath;
            File.Delete(definitionPath);
            Directory.CreateDirectory(definitionPath);

            byte[] changedSource = SofaSource(320, 385, 88, 160, 206);
            GeneratedAsset changed = AssetForgeCompiler.Generate(changedSource, recipe);
            Assert.NotEqual(original.CanonicalAssetHash, changed.CanonicalAssetHash);

            Assert.ThrowsAny<Exception>(() => RepositoryEnvironmentExporter.Export(
                root,
                changedSource,
                changed,
                EnvironmentThumbnailGenerator.Create(changed)));

            Assert.Equal(meshBefore, File.ReadAllBytes(meshPath));
            Assert.Equal(albedoBefore, File.ReadAllBytes(albedoPath));
            Assert.Equal(thumbnailBefore, File.ReadAllBytes(thumbnailPath));
            Assert.Equal(sourceBefore, File.ReadAllBytes(sourcePath));
            Assert.Equal(recipeBefore, File.ReadAllText(recipePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AssetRecipe FastSofa() => AssetRecipe.SofaDefaults() with
    {
        AssetId = "decoration.sofa.rollback",
        DisplayName = "Rollback Sofa",
        Geometry = AssetRecipe.SofaDefaults().Geometry with
        {
            GeometryResolution = 32,
            RuntimeTextureResolution = 64,
        },
    };

    private static byte[] SofaSource(int x, int y, byte r, byte g, byte b)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, x, y + 185, x + 500, y + 410, r, g, b);
        Fill(pixels, x + 45, y, x + 455, y + 225,
            (byte)Math.Min(255, r + 18),
            (byte)Math.Min(255, g + 18),
            (byte)Math.Min(255, b + 18));
        Fill(pixels, x - 20, y + 115, x + 75, y + 370,
            (byte)Math.Max(0, r - 35),
            (byte)Math.Max(0, g - 35),
            (byte)Math.Max(0, b - 35));
        Fill(pixels, x + 425, y + 115, x + 520, y + 370,
            (byte)Math.Max(0, r - 35),
            (byte)Math.Max(0, g - 35),
            (byte)Math.Max(0, b - 35));
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int yy = Math.Max(0, y0); yy < Math.Min(1024, y1); yy++)
        for (int xx = Math.Max(0, x0); xx < Math.Min(1024, x1); xx++)
        {
            int i = (yy * 1024 + xx) * 4;
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-env-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(root, "data", "environment"));
        File.WriteAllText(Path.Combine(root, "data", "environment", "generated_decorations.tres"),
            "[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationCatalogueResource\" load_steps=2 format=3]\n\n" +
            "[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationCatalogueResource.cs\" id=\"1\"]\n\n" +
            "[resource]\nscript = ExtResource(\"1\")\nEntries = Array[Resource]([])\n");
        return root;
    }
}
