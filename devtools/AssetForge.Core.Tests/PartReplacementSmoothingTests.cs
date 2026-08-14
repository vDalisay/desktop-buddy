using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementSmoothingTests
{
    [Fact]
    public void Smoothed_replacement_is_deterministic_and_differs_from_legacy_depth_field()
    {
        byte[] png = PngCodec.EncodeRgba8(Ellipse(512, 500, 160, 220));
        AssetRecipe legacy = Fast(AssetRecipe.TorsoShapeDefaults() with
        {
            Geometry = AssetRecipe.TorsoShapeDefaults().Geometry with { SurfaceSmoothness = 0.0 },
        });
        AssetRecipe smooth = legacy with
        {
            Geometry = legacy.Geometry with { SurfaceSmoothness = 0.82 },
        };

        GeneratedAsset a = AssetForgeCompiler.Generate(png, smooth);
        GeneratedAsset b = AssetForgeCompiler.Generate(png, smooth);
        GeneratedAsset old = AssetForgeCompiler.Generate(png, legacy);

        Assert.Equal(a.GeometryHash, b.GeometryHash);
        Assert.Equal(a.GlbBytes, b.GlbBytes);
        Assert.NotEqual(old.GeometryHash, a.GeometryHash);
    }

    [Fact]
    public void Extended_smoothing_above_one_is_valid_deterministic_and_changes_depth_field()
    {
        byte[] png = PngCodec.EncodeRgba8(Ellipse(512, 500, 170, 225));
        AssetRecipe basis = Fast(AssetRecipe.TorsoShapeDefaults());
        AssetRecipe normal = basis with { Geometry = basis.Geometry with { SurfaceSmoothness = 1.0 } };
        AssetRecipe extra = basis with { Geometry = basis.Geometry with { SurfaceSmoothness = 2.5 } };

        Assert.Empty(extra.Validate());
        GeneratedAsset normalAsset = AssetForgeCompiler.Generate(png, normal);
        GeneratedAsset a = AssetForgeCompiler.Generate(png, extra);
        GeneratedAsset b = AssetForgeCompiler.Generate(png, extra);

        Assert.NotEqual(normalAsset.GeometryHash, a.GeometryHash);
        Assert.Equal(a.GeometryHash, b.GeometryHash);
        Assert.Equal(a.GlbBytes, b.GlbBytes);
    }

    [Fact]
    public void New_replacement_defaults_use_runtime_safe_128_resolution()
    {
        Assert.Equal(128, AssetRecipe.TorsoShapeDefaults().Geometry.GeometryResolution);
        Assert.Equal(128, AssetRecipe.FootShapeDefaults().Geometry.GeometryResolution);
        Assert.Equal(1.0, AssetRecipe.TorsoShapeDefaults().Geometry.SurfaceSmoothness);
        Assert.Equal(1.0, AssetRecipe.FootShapeDefaults().Geometry.SurfaceSmoothness);
    }

    [Fact]
    public void Replacement_profiles_produce_distinct_deterministic_geometry()
    {
        byte[] png = PngCodec.EncodeRgba8(Ellipse(512, 520, 190, 150));
        AssetRecipe basis = Fast(AssetRecipe.FootShapeDefaults());
        string rounded = AssetForgeCompiler.Generate(png, basis with
        {
            Geometry = basis.Geometry with { ShapeMode = ShapeMode.RoundedExtrusion },
        }).GeometryHash;
        string inflated = AssetForgeCompiler.Generate(png, basis with
        {
            Geometry = basis.Geometry with { ShapeMode = ShapeMode.InflatedSolid },
        }).GeometryHash;
        string relief = AssetForgeCompiler.Generate(png, basis with
        {
            Geometry = basis.Geometry with { ShapeMode = ShapeMode.Relief },
        }).GeometryHash;

        Assert.NotEqual(rounded, inflated);
        Assert.NotEqual(rounded, relief);
        Assert.NotEqual(inflated, relief);
    }

    [Fact]
    public void Replacement_depth_may_exceed_old_ui_limit_without_changing_recipe_validity()
    {
        AssetRecipe recipe = Fast(AssetRecipe.TorsoShapeDefaults()) with
        {
            Geometry = Fast(AssetRecipe.TorsoShapeDefaults()).Geometry with { Depth = 3.25 },
        };
        Assert.Empty(recipe.Validate());
        GeneratedAsset generated = AssetForgeCompiler.Generate(
            PngCodec.EncodeRgba8(Ellipse(512, 500, 150, 200)), recipe);
        Assert.True(generated.Mesh.Positions.Max(static p => p.Z) > 1.0f);
    }

    [Fact]
    public void Zero_smoothing_compatibility_value_is_omitted_from_canonical_recipe()
    {
        AssetRecipe recipe = Fast(AssetRecipe.TorsoShapeDefaults()) with
        {
            Geometry = Fast(AssetRecipe.TorsoShapeDefaults()).Geometry with { SurfaceSmoothness = 0.0 },
        };
        string json = RecipeCodec.WriteCanonical(recipe);
        Assert.DoesNotContain("surfaceSmoothness", json, StringComparison.Ordinal);
        AssetRecipe roundTripped = RecipeCodec.Read(json);
        Assert.Equal(0.0, roundTripped.Geometry.SurfaceSmoothness);
    }

    private static AssetRecipe Fast(AssetRecipe recipe) => recipe with
    {
        FeatureId = recipe.Category == AssetCategory.TorsoShape ? "top.smoothing_test" : "shoes.smoothing_test",
        ContentId = recipe.Category == AssetCategory.TorsoShape ? "cosmetic.top.smoothing_test" : "cosmetic.shoes.smoothing_test",
        DisplayName = "Smoothing Test",
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
            if (nx * nx + ny * ny > 1.0) continue;
            int i = ((y * 1024) + x) * 4;
            pixels[i] = 55;
            pixels[i + 1] = 162;
            pixels[i + 2] = 220;
            pixels[i + 3] = 255;
        }
        return new RgbaImage(1024, 1024, pixels);
    }
}
