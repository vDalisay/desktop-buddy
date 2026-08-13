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
        float straightBridgeY = CenterBridgeAverageY(straight);
        float raisedBridgeY = CenterBridgeAverageY(raised);
        Assert.True(
            raisedBridgeY > straightBridgeY + 0.01f,
            $"Expected raised authored bridge to move upward: straight={straightBridgeY:0.000}, raised={raisedBridgeY:0.000}.");
        Assert.NotEqual(straight.GeometryHash, raised.GeometryHash);
    }

    [Fact]
    public void Glasses2_preserves_complex_closed_bridge_artwork_as_a_full_silhouette()
    {
        GeneratedAsset generated = Generate(CreateArrowBridgeGlasses(), 2);
        Assert.True(generated.Diagnostics.Holes >= 4, $"Expected at least four interior holes, got {generated.Diagnostics.Holes}.");

        float halfDepth = (float)generated.Recipe.Geometry.Depth * 0.5f;
        float[] bridgeY = generated.Mesh.Positions
            .Where(p => MathF.Abs(p.X) < 0.38f && MathF.Abs(p.Z - halfDepth) < 0.002f)
            .Select(static p => p.Y)
            .ToArray();
        Assert.NotEmpty(bridgeY);
        Assert.True(
            bridgeY.Max() - bridgeY.Min() > 0.20f,
            $"Complex bridge was collapsed toward a center-line; front silhouette Y span was {bridgeY.Max() - bridgeY.Min():0.000}.");
    }

    private static float CenterBridgeAverageY(GeneratedAsset generated)
    {
        float halfDepth = (float)generated.Recipe.Geometry.Depth * 0.5f;
        float[] candidates = generated.Mesh.Positions
            .Where(p => MathF.Abs(p.X) < 0.10f && MathF.Abs(p.Z - halfDepth) < 0.002f)
            .Select(static p => p.Y)
            .ToArray();
        Assert.NotEmpty(candidates);
        return candidates.Average();
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

    private static RgbaImage CreateArrowBridgeGlasses()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        DrawFrame(pixels, 245, 320, 440, 575, 14);
        DrawFrame(pixels, 584, 320, 779, 575, 14);
        DrawPolyline(pixels,
            [(425, 440), (480, 440), (480, 414), (540, 474), (480, 534), (480, 508), (425, 508), (425, 440)],
            9);
        DrawPolyline(pixels,
            [(599, 440), (544, 440), (544, 414), (484, 474), (544, 534), (544, 508), (599, 508), (599, 440)],
            9);
        return new RgbaImage(size, size, pixels);
    }

    private static void DrawFrame(byte[] pixels, int x0, int y0, int x1, int y1, int radius)
    {
        DrawLine(pixels, x0, y0, x1, y0, radius);
        DrawLine(pixels, x1, y0, x1, y1, radius);
        DrawLine(pixels, x1, y1, x0, y1, radius);
        DrawLine(pixels, x0, y1, x0, y0, radius);
    }

    private static void DrawPolyline(byte[] pixels, IReadOnlyList<(int X, int Y)> points, int radius)
    {
        for (int i = 0; i < points.Count - 1; i++)
            DrawLine(pixels, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, radius);
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
