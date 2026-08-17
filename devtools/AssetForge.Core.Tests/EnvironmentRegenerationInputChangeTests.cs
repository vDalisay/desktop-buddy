using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentRegenerationInputChangeTests
{
    [Fact]
    public void Verify_detects_changed_source_and_regenerate_rebuilds_to_new_canonical_output()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = AssetRecipe.SofaDefaults() with
            {
                AssetId = "decoration.sofa.input_change",
                Geometry = AssetRecipe.SofaDefaults().Geometry with
                {
                    GeometryResolution = 64,
                    RuntimeTextureResolution = 128,
                },
            };
            byte[] originalSource = Source(270, 350, 175, 122, 190);
            GeneratedAsset original = AssetForgeCompiler.Generate(originalSource, recipe);
            RepositoryEnvironmentExporter.Export(
                root,
                originalSource,
                original,
                EnvironmentThumbnailGenerator.Create(original));
            Assert.True(RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId).Passed);

            string authoredSource = Path.Combine(
                root,
                "authoring",
                "asset-forge",
                "sofas",
                "input_change",
                "source.png");
            byte[] changedSource = Source(330, 390, 92, 165, 202);
            File.WriteAllBytes(authoredSource, changedSource);
            GeneratedAsset expectedChanged = AssetForgeCompiler.Generate(changedSource, recipe);
            Assert.NotEqual(original.GeometryHash, expectedChanged.GeometryHash);

            EnvironmentAssetVerificationResult stale = RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId);
            Assert.False(stale.Passed);
            Assert.Contains(stale.Diagnostics, line =>
                line.Contains("differs from source + recipe", StringComparison.Ordinal) ||
                line.Contains("albedo.png differs", StringComparison.Ordinal));

            EnvironmentRepositoryRegenerationResult regenerated =
                RepositoryEnvironmentRegenerator.Regenerate(root, recipe.AssetId);
            Assert.True(regenerated.Verification.Passed, Diagnostics(regenerated.Verification));
            Assert.Equal(
                expectedChanged.GlbBytes,
                File.ReadAllBytes(Path.Combine(root, "assets", "generated", "environment", recipe.AssetId, AssetFileNaming.MeshFileName(recipe))));
            Assert.Equal(
                expectedChanged.AlbedoPng,
                File.ReadAllBytes(Path.Combine(root, "assets", "generated", "environment", recipe.AssetId, "albedo.png")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Source(int x, int y, byte r, byte g, byte b)
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

    private static void Fill(
        byte[] pixels,
        int x0,
        int y0,
        int x1,
        int y1,
        byte r,
        byte g,
        byte b)
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
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-input-change-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(root, "data", "environment"));
        File.WriteAllText(Path.Combine(root, "data", "environment", "generated_decorations.tres"),
            "[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationCatalogueResource\" load_steps=2 format=3]\n\n" +
            "[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationCatalogueResource.cs\" id=\"1\"]\n\n" +
            "[resource]\nscript = ExtResource(\"1\")\nEntries = Array[Resource]([])\n");
        return root;
    }

    private static string Diagnostics(EnvironmentRepositoryVerificationResult result) =>
        string.Join("; ", result.Assets.SelectMany(static asset => asset.Diagnostics)
            .Concat(result.RepositoryDiagnostics));
}
