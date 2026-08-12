using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

/// <summary>Regression coverage for the exact opaque-canvas failure reported during visual validation.</summary>
public sealed class GlassesSemanticTemplateTests
{
    [Fact]
    public void Opaque_white_diamond_glasses_use_template_and_every_authored_mesh_uv_hits_visible_paint()
    {
        byte[] source = PngCodec.EncodeRgba8(CreateOpaqueDiamondGlasses());
        AssetRecipe recipe = AssetRecipe.GlassesDefaults() with
        {
            FeatureId = "glasses.semantic_diamond",
            ContentId = "cosmetic.glasses.semantic_diamond",
            DisplayName = "Semantic Diamond",
            Geometry = AssetRecipe.GlassesDefaults().Geometry with
            {
                GeometryResolution = 512,
                RuntimeTextureResolution = 512,
                ShapeMode = ShapeMode.RoundedExtrusion,
                SymmetryMode = SymmetryMode.Off,
            },
        };

        GeneratedAsset generated = AssetForgeGenerator.Generate(source, recipe);
        Assert.True(generated.UsedGlassesTemplate);
        Assert.Equal(ForegroundExtractionMode.UniformBackground, generated.Foreground.Mode);
        Assert.Equal(2, generated.Diagnostics.Holes);

        RgbaImage albedo = PngCodec.DecodeRgba8(generated.AlbedoPng);
        int weakSamples = 0;
        int nonPinkSamples = 0;
        foreach (System.Numerics.Vector2 uv in generated.Mesh.Uvs)
        {
            int x = Math.Clamp((int)MathF.Floor(uv.X * albedo.Width), 0, albedo.Width - 1);
            int y = Math.Clamp((int)MathF.Floor(uv.Y * albedo.Height), 0, albedo.Height - 1);
            int index = ((y * albedo.Width) + x) * 4;
            if (albedo.Pixels[index + 3] < 128) weakSamples++;
            if (albedo.Pixels[index] < 180 || albedo.Pixels[index + 2] < 130) nonPinkSamples++;
        }

        Assert.Equal(0, weakSamples);
        Assert.Equal(0, nonPinkSamples);
    }

    [Fact]
    public void Opaque_white_canvas_and_equivalent_transparent_canvas_generate_same_semantic_geometry()
    {
        RgbaImage opaque = CreateOpaqueDiamondGlasses();
        byte[] transparentPixels = opaque.Pixels.ToArray();
        for (int i = 0; i < transparentPixels.Length; i += 4)
        {
            bool white = transparentPixels[i] == 255 && transparentPixels[i + 1] == 255 && transparentPixels[i + 2] == 255;
            if (white) transparentPixels[i + 3] = 0;
        }
        var transparent = new RgbaImage(1024, 1024, transparentPixels);
        AssetRecipe recipe = AssetRecipe.GlassesDefaults() with
        {
            FeatureId = "glasses.semantic_equivalence",
            ContentId = "cosmetic.glasses.semantic_equivalence",
            DisplayName = "Semantic Equivalence",
        };

        GeneratedAsset fromOpaque = AssetForgeGenerator.Generate(PngCodec.EncodeRgba8(opaque), recipe);
        GeneratedAsset fromTransparent = AssetForgeGenerator.Generate(PngCodec.EncodeRgba8(transparent), recipe);
        Assert.True(fromOpaque.UsedGlassesTemplate && fromTransparent.UsedGlassesTemplate);
        Assert.Equal(fromOpaque.GeometryHash, fromTransparent.GeometryHash);
    }

    private static RgbaImage CreateOpaqueDiamondGlasses()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        const int radius = 17;
        DrawLine(pixels, 300, 395, 425, 265, radius);
        DrawLine(pixels, 425, 265, 490, 395, radius);
        DrawLine(pixels, 490, 395, 375, 535, radius);
        DrawLine(pixels, 375, 535, 300, 395, radius);
        DrawLine(pixels, 640, 400, 735, 275, radius);
        DrawLine(pixels, 735, 275, 805, 415, radius);
        DrawLine(pixels, 805, 415, 710, 545, radius);
        DrawLine(pixels, 710, 545, 640, 400, radius);
        DrawLine(pixels, 485, 395, 645, 400, radius);
        return new RgbaImage(size, size, pixels);
    }

    private static void DrawLine(byte[] pixels, int x0, int y0, int x1, int y1, int radius)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int step = 0; step <= steps; step++)
        {
            double t = steps == 0 ? 0 : (double)step / steps;
            int cx = (int)Math.Round(x0 + (x1 - x0) * t);
            int cy = (int)Math.Round(y0 + (y1 - y0) * t);
            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= 1024 || y >= 1024) continue;
                int index = ((y * 1024) + x) * 4;
                pixels[index] = 255;
                pixels[index + 1] = 174;
                pixels[index + 2] = 201;
                pixels[index + 3] = 255;
            }
        }
    }
}
