using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementGeometryTests
{
    [Fact]
    public void Torso_literal_template_translation_moves_generated_geometry()
    {
        AssetRecipe recipe = Fast(AssetRecipe.TorsoShapeDefaults());
        GeneratedAsset a = AssetForgeCompiler.Generate(PngCodec.EncodeRgba8(Ellipse(512, 500, 140, 190)), recipe);
        GeneratedAsset b = AssetForgeCompiler.Generate(PngCodec.EncodeRgba8(Ellipse(572, 500, 140, 190)), recipe);
        float delta = b.Mesh.Positions.Average(static p => p.X) - a.Mesh.Positions.Average(static p => p.X);
        Assert.InRange(delta, 0.16f, 0.24f);
        Assert.NotEqual(a.GeometryHash, b.GeometryHash);
    }

    [Fact]
    public void Foot_replacement_preserves_authored_hole()
    {
        AssetRecipe recipe = Fast(AssetRecipe.FootShapeDefaults());
        GeneratedAsset generated = AssetForgeCompiler.Generate(PngCodec.EncodeRgba8(Ring(512, 520, 170, 70)), recipe);
        Assert.True(generated.Diagnostics.Holes >= 1);
        Assert.True(generated.TriangleCount > 0);
        GlbWriter.ValidateSingleMesh(generated.GlbBytes);
    }

    [Fact]
    public void Inflated_and_rounded_replacements_have_different_geometry()
    {
        AssetRecipe inflated = Fast(AssetRecipe.TorsoShapeDefaults());
        AssetRecipe rounded = inflated with { Geometry = inflated.Geometry with { ShapeMode = ShapeMode.RoundedExtrusion } };
        byte[] png = PngCodec.EncodeRgba8(Ellipse(512, 500, 150, 210));
        Assert.NotEqual(
            AssetForgeCompiler.Generate(png, inflated).GeometryHash,
            AssetForgeCompiler.Generate(png, rounded).GeometryHash);
    }

    private static AssetRecipe Fast(AssetRecipe recipe) => recipe with
    {
        FeatureId = recipe.Category == AssetCategory.TorsoShape ? "top.core_test" : "shoes.core_test",
        ContentId = recipe.Category == AssetCategory.TorsoShape ? "cosmetic.top.core_test" : "cosmetic.shoes.core_test",
        DisplayName = "Core Test",
        Geometry = recipe.Geometry with { GeometryResolution = 64, RuntimeTextureResolution = 128 },
    };

    private static RgbaImage Ellipse(int cx, int cy, int rx, int ry)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            double nx = (x - cx) / (double)rx;
            double ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny <= 1.0) Set(pixels, x, y);
        }
        return new RgbaImage(1024, 1024, pixels);
    }

    private static RgbaImage Ring(int cx, int cy, int outer, int inner)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        int outer2 = outer * outer;
        int inner2 = inner * inner;
        for (int y = cy - outer; y <= cy + outer; y++)
        for (int x = cx - outer; x <= cx + outer; x++)
        {
            int dx = x - cx;
            int dy = y - cy;
            int d2 = dx * dx + dy * dy;
            if (d2 <= outer2 && d2 >= inner2) Set(pixels, x, y);
        }
        return new RgbaImage(1024, 1024, pixels);
    }

    private static void Set(byte[] pixels, int x, int y)
    {
        int i = ((y * 1024) + x) * 4;
        pixels[i] = 225;
        pixels[i + 1] = 118;
        pixels[i + 2] = 178;
        pixels[i + 3] = 255;
    }
}
