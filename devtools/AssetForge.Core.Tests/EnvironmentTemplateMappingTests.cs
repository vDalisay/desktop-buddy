using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentTemplateMappingTests
{
    [Fact]
    public void Template_floor_center_is_the_world_origin()
    {
        AssetRecipe recipe = AssetRecipe.LampDefaults();
        System.Numerics.Vector2 point = EnvironmentTemplateMapping.SourcePixelToWorld(
            EnvironmentTemplateSpace.CenterX,
            EnvironmentTemplateSpace.FloorY,
            recipe);

        Assert.Equal(0f, point.X, 5);
        Assert.Equal(0f, point.Y, 5);
    }

    [Fact]
    public void Template_safe_top_maps_to_negative_logical_height()
    {
        AssetRecipe recipe = AssetRecipe.LampDefaults() with
        {
            Environment = AssetRecipe.LampDefaults().Environment with { LogicalHeight = 156 },
        };
        System.Numerics.Vector2 point = EnvironmentTemplateMapping.SourcePixelToWorld(
            EnvironmentTemplateSpace.CenterX,
            EnvironmentTemplateSpace.SafeTop,
            recipe);

        Assert.Equal(0f, point.X, 5);
        Assert.Equal(-156f, point.Y, 4);
    }

    [Fact]
    public void Moving_source_pixels_has_a_stable_world_delta()
    {
        AssetRecipe recipe = AssetRecipe.LampDefaults();
        System.Numerics.Vector2 a = EnvironmentTemplateMapping.SourcePixelToWorld(400, 700, recipe);
        System.Numerics.Vector2 b = EnvironmentTemplateMapping.SourcePixelToWorld(500, 650, recipe);
        float units = EnvironmentTemplateMapping.UnitsPerPixel(recipe);

        Assert.Equal(100f * units, b.X - a.X, 4);
        Assert.Equal(-50f * units, b.Y - a.Y, 4);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(512, 880)]
    [InlineData(356.25, 278.75)]
    [InlineData(1024, 1024)]
    public void Literal_template_mapping_round_trips_source_pixels(double x, double y)
    {
        AssetRecipe recipe = AssetRecipe.LampDefaults() with
        {
            Environment = AssetRecipe.LampDefaults().Environment with { LogicalHeight = 173 },
        };
        System.Numerics.Vector2 world = EnvironmentTemplateMapping.SourcePixelToWorld(x, y, recipe);
        System.Numerics.Vector2 pixels = EnvironmentTemplateMapping.WorldToSourcePixel(world, recipe);

        Assert.Equal((float)x, pixels.X, 3);
        Assert.Equal((float)y, pixels.Y, 3);
    }

    [Fact]
    public void World_to_normalized_source_is_the_inverse_used_by_editor_gizmos()
    {
        AssetRecipe recipe = AssetRecipe.LampDefaults();
        System.Numerics.Vector2 world = EnvironmentTemplateMapping.SourcePixelToWorld(768, 256, recipe);
        System.Numerics.Vector2 normalized = EnvironmentTemplateMapping.WorldToNormalizedSource(world, recipe);

        Assert.Equal(.75f, normalized.X, 4);
        Assert.Equal(.25f, normalized.Y, 4);
    }

    [Fact]
    public void Lamp_v1_stays_legacy_while_v2_is_literal()
    {
        AssetRecipe v1 = AssetRecipe.LampDefaults() with { PresetVersion = 1 };
        AssetRecipe v2 = AssetRecipe.LampDefaults() with { PresetVersion = 2 };

        Assert.False(EnvironmentTemplateMapping.UsesLiteralTemplateSpace(v1));
        Assert.True(EnvironmentTemplateMapping.UsesLiteralTemplateSpace(v2));
    }
}
