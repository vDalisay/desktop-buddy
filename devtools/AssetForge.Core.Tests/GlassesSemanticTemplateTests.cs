using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

/// <summary>Regression coverage for the exact opaque-canvas and dark-frame failures reported during visual validation.</summary>
public sealed class GlassesSemanticTemplateTests
{
    [Fact]
    public void Opaque_white_diamond_glasses_use_template_and_every_authored_mesh_uv_hits_visible_paint()
    {
        GeneratedAsset generated = GenerateDiamond();
        Assert.True(generated.UsedGlassesTemplate);
        Assert.Equal(ForegroundExtractionMode.UniformBackground, generated.Foreground.Mode);
        Assert.Equal(2, generated.Diagnostics.Holes);

        RgbaImage albedo = PngCodec.DecodeRgba8(generated.AlbedoPng);
        Assert.All(
            Enumerable.Range(0, albedo.Width * albedo.Height),
            pixel => Assert.Equal((byte)255, albedo.Pixels[pixel * 4 + 3]));

        int weakSamples = 0;
        int nonPinkSamples = 0;
        foreach (System.Numerics.Vector2 uv in generated.Mesh.Uvs)
        {
            int x = Math.Clamp((int)MathF.Floor(uv.X * albedo.Width), 0, albedo.Width - 1);
            int y = Math.Clamp((int)MathF.Floor(uv.Y * albedo.Height), 0, albedo.Height - 1);
            int index = ((y * albedo.Width) + x) * 4;
            if (albedo.Pixels[index + 3] < 250) weakSamples++;
            if (albedo.Pixels[index] < 180 || albedo.Pixels[index + 2] < 130) nonPinkSamples++;
        }

        Assert.Equal(0, weakSamples);
        Assert.Equal(0, nonPinkSamples);
    }

    [Fact]
    public void Rounded_glasses_front_and_back_surface_normals_face_outward()
    {
        GeneratedAsset generated = GenerateDiamond();
        float depth = (float)generated.Recipe.Geometry.Depth * 0.5f;
        float threshold = depth * 0.45f;
        int front = 0;
        int frontOutward = 0;
        int back = 0;
        int backOutward = 0;

        for (int i = 0; i < generated.Mesh.Positions.Count; i++)
        {
            System.Numerics.Vector3 position = generated.Mesh.Positions[i];
            System.Numerics.Vector3 normal = generated.Mesh.Normals[i];
            if (position.Z > threshold)
            {
                front++;
                if (normal.Z > 0.05f) frontOutward++;
            }
            else if (position.Z < -threshold)
            {
                back++;
                if (normal.Z < -0.05f) backOutward++;
            }
        }

        Assert.True(front > 0 && back > 0, "Expected rounded frame vertices on both depth faces.");
        Assert.True(frontOutward >= front * 0.85f,
            $"Expected front tube normals to face the camera side, got {frontOutward}/{front} outward.");
        Assert.True(backOutward >= back * 0.85f,
            $"Expected back tube normals to face away from the camera side, got {backOutward}/{back} outward.");
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
        AssetRecipe recipe = DiamondRecipe("equivalence");

        GeneratedAsset fromOpaque = AssetForgeGenerator.Generate(PngCodec.EncodeRgba8(opaque), recipe);
        GeneratedAsset fromTransparent = AssetForgeGenerator.Generate(PngCodec.EncodeRgba8(transparent), recipe);
        Assert.True(fromOpaque.UsedGlassesTemplate && fromTransparent.UsedGlassesTemplate);
        Assert.Equal(fromOpaque.GeometryHash, fromTransparent.GeometryHash);
    }

    private static GeneratedAsset GenerateDiamond() => AssetForgeGenerator.Generate(
        PngCodec.EncodeRgba8(CreateOpaqueDiamondGlasses()),
        DiamondRecipe("diamond"));

    private static AssetRecipe DiamondRecipe(string suffix) => AssetRecipe.GlassesDefaults() with
    {
        FeatureId = $"glasses.semantic_{suffix}",
        ContentId = $"cosmetic.glasses.semantic_{suffix}",
        DisplayName = "Semantic Diamond",
        Geometry = AssetRecipe.GlassesDefaults().Geometry with
        {
            GeometryResolution = 512,
            RuntimeTextureResolution = 512,
            ShapeMode = ShapeMode.RoundedExtrusion,
            SymmetryMode = SymmetryMode.Off,
        },
    };

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
