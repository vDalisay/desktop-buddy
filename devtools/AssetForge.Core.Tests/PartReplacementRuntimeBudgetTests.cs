using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementRuntimeBudgetTests
{
    [Theory]
    [InlineData(AssetCategory.TorsoShape)]
    [InlineData(AssetCategory.FootShape)]
    public void Default_replacement_runtime_mesh_stays_well_below_old_256_density(AssetCategory category)
    {
        AssetRecipe recipe = category == AssetCategory.TorsoShape
            ? AssetRecipe.TorsoShapeDefaults()
            : AssetRecipe.FootShapeDefaults();
        recipe = recipe with
        {
            FeatureId = category == AssetCategory.TorsoShape ? "top.runtime_budget" : "shoes.runtime_budget",
            ContentId = category == AssetCategory.TorsoShape ? "cosmetic.top.runtime_budget" : "cosmetic.shoes.runtime_budget",
            DisplayName = "Runtime Budget",
            Geometry = recipe.Geometry with { RuntimeTextureResolution = 128 },
        };

        byte[] source = PngCodec.EncodeRgba8(Ellipse(512, 520, 180, category == AssetCategory.TorsoShape ? 230 : 150));
        GeneratedAsset runtime = AssetForgeCompiler.Generate(source, recipe);
        GeneratedAsset oldDensity = AssetForgeCompiler.Generate(source, recipe with
        {
            Geometry = recipe.Geometry with { GeometryResolution = 256 },
        });

        Assert.True(runtime.TriangleCount < 20_000,
            $"Default {category} replacement produced {runtime.TriangleCount:N0} triangles; the runtime budget is <20k.");
        Assert.True(oldDensity.TriangleCount > runtime.TriangleCount * 3,
            $"Expected the 128 runtime mesh to be substantially lighter than 256 ({runtime.TriangleCount:N0} vs {oldDensity.TriangleCount:N0}).");
    }

    private static RgbaImage Ellipse(int cx, int cy, int rx, int ry)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            double nx = (x - cx) / (double)rx;
            double ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny > 1.0) continue;
            int i = ((y * 1024) + x) * 4;
            pixels[i] = 68;
            pixels[i + 1] = 168;
            pixels[i + 2] = 226;
            pixels[i + 3] = 255;
        }
        return new RgbaImage(1024, 1024, pixels);
    }
}
