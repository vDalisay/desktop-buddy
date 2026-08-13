using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class LightingLevelTests
{
    [Fact]
    public void Lighting_defaults_to_approved_level_and_round_trips_canonically()
    {
        AssetRecipe recipe = AssetRecipe.GlassesDefaults();
        Assert.Equal(0.36, recipe.LightingLevel, 3);

        string json = RecipeCodec.WriteCanonical(recipe);
        Assert.Contains("\"lightingLevel\": 0.36", json, StringComparison.Ordinal);

        AssetRecipe roundTrip = RecipeCodec.Read(json);
        Assert.Equal(recipe.LightingLevel, roundTrip.LightingLevel, 6);
    }

    [Fact]
    public void Lighting_level_participates_in_recipe_identity_without_changing_geometry_settings()
    {
        AssetRecipe baseline = AssetRecipe.GlassesDefaults();
        AssetRecipe dimmer = baseline with { LightingLevel = 0.18 };

        Assert.NotEqual(RecipeCodec.Hash(baseline), RecipeCodec.Hash(dimmer));
        Assert.Equal(baseline.Geometry, dimmer.Geometry);
        Assert.Empty(baseline.Validate());
        Assert.Empty(dimmer.Validate());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Lighting_rejects_values_outside_the_authoring_range(double value)
    {
        AssetRecipe recipe = AssetRecipe.GlassesDefaults() with { LightingLevel = value };
        Assert.Contains(recipe.Validate(), error => error.Contains("LightingLevel", StringComparison.Ordinal));
    }
}
