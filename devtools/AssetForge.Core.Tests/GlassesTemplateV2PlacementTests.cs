using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class GlassesTemplateV2PlacementTests
{
    [Fact]
    public void New_glasses_default_to_coloring_template_preset()
    {
        AssetRecipe recipe = AssetRecipe.GlassesDefaults();
        Assert.Equal("glasses", recipe.PresetId);
        Assert.Equal(2, recipe.PresetVersion);
        Assert.Empty(recipe.Validate());
    }

    [Fact]
    public void Glasses2_preserves_source_translation_in_buddy_head_space()
    {
        GeneratedAsset original = Generate(CreateGlasses(0, 470), 2);
        GeneratedAsset shifted = Generate(CreateGlasses(60, 470), 2);
        float delta = shifted.Mesh.Positions.Average(static p => p.X) - original.Mesh.Positions.Average(static p => p.X);
        Assert.InRange(delta, 0.14f, 0.20f);
    }

    [Fact]
    public void Glasses1_retains_legacy_autofit_when_source_is_translated()
    {
        GeneratedAsset original = Generate(CreateGlasses(0, 470), 1);
        GeneratedAsset shifted = Generate(CreateGlasses(60, 470), 1);
        float delta = shifted.Mesh.Positions.Average(static p => p.X) - original.Mesh.Positions.Average(static p => p.X);
        Assert.InRange(MathF.Abs(delta), 0f, 0.02f);
    }

    [Fact]
    public void Glasses2_uses_the_authored_bridge_curve_instead_of_a_fixed_bridge()
    {
        GeneratedAsset straight = Generate(CreateGlasses(0, 470), 2);
        GeneratedAsset raised = Generate(CreateGlasses(0, 410), 2);
        float straightBridgeY = CenterBridgeTop(straight);
        float raisedBridgeY = CenterBridgeTop(raised);
        Assert.True(raisedBridgeY > straightBridgeY + 0.10f);
        Assert.NotEqual(straight.GeometryHash, raised.GeometryHash);
    }

    private static float CenterBridgeTop(GeneratedAsset generated)
    {
        float[] candidates = generated.Mesh.Positions
            .Where(static p => MathF.Abs(p.X) < 0.10f && p.Z > -0.10f)
            .Select(static p => p.Y)
            .ToArray();
        Assert.NotEmpty(candidates);
        return candidates.Max();
    }

    private static GeneratedAsset Generate(RgbaImage image, int presetVersion)
    {
        AssetRecipe recipe = AssetRecipe.GlassesDefaults() with
        {
            PresetVersion = presetVersion,
            FeatureId = $"glasses.template_v{presetVersion}_placement_test",
            ContentId = $"cosmetic.glasses.template_v{presetVersion}_placement_test",
            DisplayName = "Template Placement Test",
            Geometry = AssetRecipe.GlassesDefaults().Geometry with
            {
                GeometryResolution = 256,
                RuntimeTextureResolution = 256,
                ShapeMode = ShapeMode.RoundedExtrusion,
                SymmetryMode = SymmetryMode.Off,
            },
        };
        return AssetForgeGenerator.Generate(PngCodec.EncodeRgba8(image), recipe);
    }

    private static RgbaImage CreateGlasses(int shiftX, int bridgePeakY)
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        DrawFrame(pixels, 270 + shiftX, 360, 465 + shiftX, 555, 16);
        DrawFrame(pixels, 560 + shiftX, 360, 755 + shiftX, 555, 16);
        DrawLine(pixels, 455 + shiftX, 470, 512 + shiftX, bridgePeakY, 14);
        DrawLine(pixels, 512 + shiftX, bridgePeakY, 570 + shiftX, 470, 14);
        return new RgbaImage(size, size, pixels);
    }

    private static void DrawFrame(byte[] pixels, int x0, int y0, int x1, int y1, int radius)
    {
        DrawLine(pixels, x0, y0, x1, y0, radius);
        DrawLine(pixels, x1, y0, x1, y1, radius);
        DrawLine(pixels, x1, y1, x0, y1, radius);
        DrawLine(pixels, x0, y1, x0, y0, radius);
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
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > radius * radius) continue;
                int index = ((y * 1024) + x) * 4;
                pixels[index] = 239;
                pixels[index + 1] = 123;
                pixels[index + 2] = 175;
                pixels[index + 3] = 255;
            }
        }
    }
}
