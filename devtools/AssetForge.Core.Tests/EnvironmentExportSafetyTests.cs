using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentExportSafetyTests
{
    [Fact]
    public void Invalid_thumbnail_fails_before_existing_environment_export_is_modified()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastSofa("decoration.sofa.transaction_safe");
            byte[] source = SofaSource(260, 340, 178, 126, 194);
            GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
            byte[] thumbnail = EnvironmentThumbnailGenerator.Create(generated);
            ExportResult first = RepositoryEnvironmentExporter.Export(root, source, generated, thumbnail);
            string meshFileName = AssetFileNaming.MeshFileName(recipe);

            byte[] meshBefore = File.ReadAllBytes(Path.Combine(first.AssetDirectory, meshFileName));
            byte[] albedoBefore = File.ReadAllBytes(Path.Combine(first.AssetDirectory, "albedo.png"));
            byte[] thumbnailBefore = File.ReadAllBytes(Path.Combine(first.AssetDirectory, "thumbnail.png"));
            string definitionBefore = File.ReadAllText(first.CosmeticDefinitionPath);
            string aggregateBefore = File.ReadAllText(first.CataloguePath);

            byte[] changedSource = SofaSource(315, 375, 84, 158, 203);
            GeneratedAsset changed = AssetForgeCompiler.Generate(changedSource, recipe);
            Assert.NotEqual(generated.CanonicalAssetHash, changed.CanonicalAssetHash);

            Assert.ThrowsAny<Exception>(() => RepositoryEnvironmentExporter.Export(
                root,
                changedSource,
                changed,
                [1, 2, 3, 4, 5]));

            Assert.Equal(meshBefore, File.ReadAllBytes(Path.Combine(first.AssetDirectory, meshFileName)));
            Assert.Equal(albedoBefore, File.ReadAllBytes(Path.Combine(first.AssetDirectory, "albedo.png")));
            Assert.Equal(thumbnailBefore, File.ReadAllBytes(Path.Combine(first.AssetDirectory, "thumbnail.png")));
            Assert.Equal(definitionBefore, File.ReadAllText(first.CosmeticDefinitionPath));
            Assert.Equal(aggregateBefore, File.ReadAllText(first.CataloguePath));
            Assert.True(RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId).Passed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("decoration.sofa../escape")]
    [InlineData("decoration.sofa./../escape")]
    [InlineData("decoration.sofa.bad/../../escape")]
    public void Environment_export_rejects_path_traversal_asset_ids(string assetId)
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastSofa(assetId);
            byte[] source = SofaSource(270, 350, 170, 120, 190);

            Exception exception = Assert.ThrowsAny<Exception>(() =>
            {
                GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
                RepositoryEnvironmentExporter.Export(
                    root,
                    source,
                    generated,
                    EnvironmentThumbnailGenerator.Create(generated));
            });

            Assert.True(
                exception.Message.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("AssetId", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase),
                exception.Message);
            Assert.False(Directory.Exists(Path.Combine(root, "escape")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Exporting_second_environment_asset_preserves_first_and_aggregate_contains_both_once()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe lampRecipe = AssetRecipe.LampDefaults() with
            {
                AssetId = "decoration.lamp.aggregate_safe",
                Geometry = AssetRecipe.LampDefaults().Geometry with
                {
                    GeometryResolution = 32,
                    RuntimeTextureResolution = 64,
                },
            };
            byte[] lampSource = LampSource();
            GeneratedAsset lamp = AssetForgeCompiler.Generate(lampSource, lampRecipe);
            ExportResult lampExport = RepositoryEnvironmentExporter.Export(
                root,
                lampSource,
                lamp,
                EnvironmentThumbnailGenerator.Create(lamp));
            string lampMeshFileName = AssetFileNaming.MeshFileName(lampRecipe);
            byte[] lampMeshBefore = File.ReadAllBytes(Path.Combine(lampExport.AssetDirectory, lampMeshFileName));

            AssetRecipe sofaRecipe = FastSofa("decoration.sofa.aggregate_safe");
            byte[] sofaSource = SofaSource(250, 345, 176, 121, 193);
            GeneratedAsset sofa = AssetForgeCompiler.Generate(sofaSource, sofaRecipe);
            ExportResult sofaExport = RepositoryEnvironmentExporter.Export(
                root,
                sofaSource,
                sofa,
                EnvironmentThumbnailGenerator.Create(sofa));

            string aggregate = File.ReadAllText(sofaExport.CataloguePath);
            Assert.Equal(1, Count(aggregate, "res://data/environment/generated/decoration.lamp.aggregate_safe.tres"));
            Assert.Equal(1, Count(aggregate, "res://data/environment/generated/decoration.sofa.aggregate_safe.tres"));
            Assert.Equal(lampMeshBefore, File.ReadAllBytes(Path.Combine(lampExport.AssetDirectory, lampMeshFileName)));
            Assert.True(RepositoryEnvironmentVerifier.VerifyAll(root).Passed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AssetRecipe FastSofa(string id) => AssetRecipe.SofaDefaults() with
    {
        AssetId = id,
        Geometry = AssetRecipe.SofaDefaults().Geometry with
        {
            GeometryResolution = 32,
            RuntimeTextureResolution = 64,
        },
    };

    private static byte[] SofaSource(int x, int y, byte r, byte g, byte b)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, x, y + 190, x + 500, y + 410, r, g, b);
        Fill(pixels, x + 40, y, x + 460, y + 230,
            (byte)Math.Min(255, r + 18),
            (byte)Math.Min(255, g + 18),
            (byte)Math.Min(255, b + 18));
        Fill(pixels, x - 20, y + 120, x + 70, y + 370,
            (byte)Math.Max(0, r - 35),
            (byte)Math.Max(0, g - 35),
            (byte)Math.Max(0, b - 35));
        Fill(pixels, x + 430, y + 120, x + 520, y + 370,
            (byte)Math.Max(0, r - 35),
            (byte)Math.Max(0, g - 35),
            (byte)Math.Max(0, b - 35));
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static byte[] LampSource()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 472, 330, 552, 830, 66, 72, 82);
        Fill(pixels, 382, 800, 642, 880, 45, 50, 58);
        Fill(pixels, 335, 190, 689, 410, 244, 183, 74);
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

    private static int Count(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-env-safety-" + Guid.NewGuid().ToString("N"));
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
