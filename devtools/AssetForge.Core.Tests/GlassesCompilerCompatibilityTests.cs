using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class GlassesCompilerCompatibilityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Category_dispatch_preserves_existing_glasses_generator_bytes(int presetVersion)
    {
        AssetRecipe recipe = AssetRecipe.GlassesDefaults() with
        {
            PresetVersion = presetVersion,
            FeatureId = $"glasses.compat_v{presetVersion}",
            ContentId = $"cosmetic.glasses.compat_v{presetVersion}",
            Geometry = AssetRecipe.GlassesDefaults().Geometry with
            {
                GeometryResolution = 64,
                RuntimeTextureResolution = 128,
                SymmetryMode = SymmetryMode.Off,
            },
        };
        byte[] source = Source();

        GeneratedAsset accepted = AssetForgeGenerator.Generate(source, recipe);
        GeneratedAsset dispatched = AssetForgeCompiler.Generate(source, recipe);

        Assert.Equal(accepted.GlbBytes, dispatched.GlbBytes);
        Assert.Equal(accepted.AlbedoPng, dispatched.AlbedoPng);
        Assert.Equal(accepted.GeometryHash, dispatched.GeometryHash);
        Assert.Equal(accepted.CanonicalAssetHash, dispatched.CanonicalAssetHash);
    }

    private static byte[] Source()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Frame(pixels, 230, 400, 470, 610, 34);
        Frame(pixels, 554, 400, 794, 610, 34);
        Fill(pixels, 466, 490, 558, 524, 225, 105, 166);
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static void Frame(byte[] pixels, int x0, int y0, int x1, int y1, int thickness)
    {
        Fill(pixels, x0, y0, x1, y0 + thickness, 225, 105, 166);
        Fill(pixels, x0, y1 - thickness, x1, y1, 225, 105, 166);
        Fill(pixels, x0, y0, x0 + thickness, y1, 225, 105, 166);
        Fill(pixels, x1 - thickness, y0, x1, y1, 225, 105, 166);
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = (y * 1024 + x) * 4;
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }
    }
}
