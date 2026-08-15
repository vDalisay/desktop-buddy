using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentSilhouettePolisherTests
{
    [Fact]
    public void New_lamps_default_to_versioned_inflated_solid_with_full_surface_smoothing()
    {
        AssetRecipe recipe = AssetRecipe.LampDefaults();
        Assert.Equal(ShapeMode.InflatedSolid, recipe.Geometry.ShapeMode);
        Assert.Equal(1.0, recipe.Geometry.SurfaceSmoothness, 8);
        Assert.Equal(3, recipe.PresetVersion);
        Assert.True(EnvironmentTemplateMapping.UsesLiteralTemplateSpace(recipe));
        Assert.True(EnvironmentSilhouettePolisher.UsesSmoothedLiteralContract(recipe));
    }

    [Fact]
    public void Lamp_v3_refines_rim_beyond_the_coarse_mask_lattice()
    {
        AssetRecipe defaults = AssetRecipe.LampDefaults();
        AssetRecipe recipe = defaults with
        {
            AssetId = "decoration.lamp.smoothing_test",
            Geometry = defaults.Geometry with
            {
                GeometryResolution = 64,
                RuntimeTextureResolution = 64,
                SurfaceSmoothness = 1.0,
                ShapeMode = ShapeMode.InflatedSolid,
            },
        };

        GeneratedAsset generated = AssetForgeCompiler.Generate(RoundedLampSource(), recipe);
        bool hasSubGridUv = generated.Mesh.Uvs.Any(uv =>
        {
            float gx = uv.X * recipe.Geometry.GeometryResolution;
            float gy = uv.Y * recipe.Geometry.GeometryResolution;
            return MathF.Abs(gx - MathF.Round(gx)) > .01f || MathF.Abs(gy - MathF.Round(gy)) > .01f;
        });

        Assert.True(hasSubGridUv);
    }

    [Fact]
    public void Low_opacity_template_guides_beneath_art_do_not_enter_the_lamp_mask_or_smoothed_contour()
    {
        AssetRecipe defaults = AssetRecipe.LampDefaults();
        AssetRecipe recipe = defaults with
        {
            AssetId = "decoration.lamp.guide_alpha_test",
            Geometry = defaults.Geometry with
            {
                GeometryResolution = 64,
                RuntimeTextureResolution = 64,
                AlphaThreshold = .5,
            },
        };
        byte[] clean = RoundedLampSource();
        RgbaImage decoded = PngCodec.DecodeRgba8(clean);
        byte[] guidedPixels = (byte[])decoded.Pixels.Clone();
        FillTransparentOnly(guidedPixels, 180, 100, 184, 880, 84, 112, 128, 58);
        FillTransparentOnly(guidedPixels, 840, 100, 844, 880, 84, 112, 128, 58);
        FillTransparentOnly(guidedPixels, 100, 878, 924, 882, 66, 116, 72, 120);
        byte[] guided = PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, guidedPixels));

        GeneratedAsset withoutGuide = AssetForgeCompiler.Generate(clean, recipe);
        GeneratedAsset withGuide = AssetForgeCompiler.Generate(guided, recipe);

        Assert.Equal(withoutGuide.GeometryHash, withGuide.GeometryHash);
        Assert.Equal(withoutGuide.GlbBytes, withGuide.GlbBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Pre_v3_lamps_keep_the_pre_polisher_geometry_path(int presetVersion)
    {
        AssetRecipe defaults = AssetRecipe.LampDefaults();
        AssetRecipe recipe = defaults with
        {
            PresetVersion = presetVersion,
            AssetId = $"decoration.lamp.compat_v{presetVersion}",
            Geometry = defaults.Geometry with
            {
                GeometryResolution = 64,
                RuntimeTextureResolution = 64,
                SurfaceSmoothness = .8,
            },
        };
        byte[] source = RoundedLampSource();
        RgbaImage foreground = ForegroundExtractor.Extract(PngCodec.DecodeRgba8(source)).Image;
        MaskGrid mask = MaskGrid.FromImage(foreground, recipe.Geometry);
        CanonicalMesh legacy = EnvironmentSilhouetteGenerator.Generate(mask, recipe);
        GeneratedAsset compiled = AssetForgeCompiler.Generate(source, recipe);

        Assert.False(EnvironmentSilhouettePolisher.UsesSmoothedLiteralContract(recipe));
        Assert.Equal(legacy.CanonicalHash(), compiled.GeometryHash);
        Assert.Equal(GlbWriter.Write(legacy), compiled.GlbBytes);
    }

    private static byte[] RoundedLampSource()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 492, 410, 532, EnvironmentTemplateSpace.FloorY, 186, 112, 72);
        Fill(pixels, 430, EnvironmentTemplateSpace.FloorY - 36, 594, EnvironmentTemplateSpace.FloorY, 154, 86, 58);
        for (int y = 170; y < 430; y++)
        for (int x = 300; x < 724; x++)
        {
            float dx = MathF.Max(MathF.Abs(x - 512) - 184f, 0f);
            float dy = MathF.Max(MathF.Abs(y - 300) - 92f, 0f);
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > 24f) continue;
            byte alpha = distance <= 22f ? (byte)255 : (byte)Math.Clamp((24f - distance) * 127.5f, 0f, 255f);
            Pixel(pixels, x, y, 246, 184, 65, alpha);
        }
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a = 255)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++) Pixel(pixels, x, y, r, g, b, a);
    }

    private static void FillTransparentOnly(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = (y * 1024 + x) * 4;
            if (pixels[i + 3] != 0) continue;
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }
    }

    private static void Pixel(byte[] pixels, int x, int y, byte r, byte g, byte b, byte a)
    {
        int i = (y * 1024 + x) * 4;
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
        pixels[i + 3] = a;
    }
}
