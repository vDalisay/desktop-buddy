using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class AssetForgeGenerationWarningsTests
{
    [Fact]
    public void Replacement_envelope_warning_is_exposed_without_blocking_generation()
    {
        AssetRecipe defaults = AssetRecipe.TorsoShapeDefaults();
        AssetRecipe recipe = defaults with
        {
            FeatureId = "top.warning_test",
            ContentId = "cosmetic.top.warning_test",
            Geometry = defaults.Geometry with { GeometryResolution = 64, RuntimeTextureResolution = 64 },
        };
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 50, 50, 970, 970);
        GeneratedAsset generated = AssetForgeCompiler.Generate(PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels)), recipe);

        IReadOnlyList<string> warnings = AssetForgeGenerationWarnings.Analyze(generated);
        Assert.Contains(warnings, static warning => warning.Contains("Physics remains unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void Many_disconnected_islands_receive_actionable_warning()
    {
        AssetRecipe defaults = AssetRecipe.PlantDefaults();
        AssetRecipe recipe = defaults with
        {
            AssetId = "decoration.plant.warning_test",
            Geometry = defaults.Geometry with { GeometryResolution = 64, RuntimeTextureResolution = 64 },
        };
        byte[] pixels = new byte[1024 * 1024 * 4];
        for (int i = 0; i < 9; i++)
        {
            int x = 120 + (i % 3) * 260;
            int y = 160 + (i / 3) * 230;
            Fill(pixels, x, y, x + 55, y + 55);
        }
        GeneratedAsset generated = AssetForgeCompiler.Generate(PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels)), recipe);

        IReadOnlyList<string> warnings = AssetForgeGenerationWarnings.Analyze(generated);
        Assert.Contains(warnings, static warning => warning.StartsWith("WARNING: 9 disconnected", StringComparison.Ordinal));
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = (y * 1024 + x) * 4;
            pixels[i] = 180;
            pixels[i + 1] = 120;
            pixels[i + 2] = 90;
            pixels[i + 3] = 255;
        }
    }
}
