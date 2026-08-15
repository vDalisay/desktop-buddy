using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class AssetRecipeMigrationTests
{
    [Fact]
    public void Lamp_v2_migrates_to_v3_without_changing_identity_economy_light_or_thumbnail_metadata()
    {
        AssetRecipe current = AssetRecipe.LampDefaults() with
        {
            PresetVersion = 2,
            AssetId = "decoration.lamp.migration_test",
            DisplayName = "Migration Lamp",
            PriceCredits = 321,
            SortOrder = 77,
            Geometry = AssetRecipe.LampDefaults().Geometry with
            {
                ShapeMode = ShapeMode.RoundedExtrusion,
                SurfaceSmoothness = .82,
                Depth = .23,
                Roundness = .73,
            },
            Light = AssetRecipe.LampDefaults().Light with
            {
                EmitterX = .42,
                EmitterY = .31,
                Brightness = 1.7,
            },
            Thumbnail = new ThumbnailSettings { YawDegrees = 19, PitchDegrees = -11, Padding = .17 },
        };

        AssetRecipeMigrationPlan plan = Assert.IsType<AssetRecipeMigrationPlan>(AssetRecipeMigration.Plan(current));
        AssetRecipe migrated = AssetRecipeMigration.MigrateToLatest(current);

        Assert.Equal(3, plan.TargetPresetVersion);
        Assert.False(plan.RequiresSourceRealignment);
        Assert.Equal(3, migrated.PresetVersion);
        Assert.Equal(ShapeMode.InflatedSolid, migrated.Geometry.ShapeMode);
        Assert.Equal(1.0, migrated.Geometry.SurfaceSmoothness, 8);
        Assert.Equal(current.Geometry.Depth, migrated.Geometry.Depth);
        Assert.Equal(current.Geometry.Roundness, migrated.Geometry.Roundness);
        Assert.Equal(current.AssetId, migrated.AssetId);
        Assert.Equal(current.DisplayName, migrated.DisplayName);
        Assert.Equal(current.PriceCredits, migrated.PriceCredits);
        Assert.Equal(current.SortOrder, migrated.SortOrder);
        Assert.Equal(current.Light, migrated.Light);
        Assert.Equal(current.Thumbnail, migrated.Thumbnail);
        Assert.Empty(migrated.Validate());
    }

    [Fact]
    public void Lamp_v1_migration_warns_that_literal_template_realignment_is_required()
    {
        AssetRecipe current = AssetRecipe.LampDefaults() with
        {
            PresetVersion = 1,
            AssetId = "decoration.lamp.legacy_migration_test",
            Geometry = AssetRecipe.LampDefaults().Geometry with
            {
                ShapeMode = ShapeMode.RoundedExtrusion,
                SurfaceSmoothness = .82,
            },
        };

        AssetRecipeMigrationPlan plan = Assert.IsType<AssetRecipeMigrationPlan>(AssetRecipeMigration.Plan(current));
        Assert.True(plan.RequiresSourceRealignment);
        Assert.Equal(3, AssetRecipeMigration.MigrateToLatest(current).PresetVersion);
    }

    [Fact]
    public void Sofa_v1_migration_preserves_authored_geometry_and_placement_metadata()
    {
        AssetRecipe current = AssetRecipe.SofaDefaults() with
        {
            PresetVersion = 1,
            AssetId = "decoration.sofa.migration_test",
            Geometry = AssetRecipe.SofaDefaults().Geometry with
            {
                Depth = .41,
                Roundness = .66,
                SurfaceSmoothness = .72,
            },
            Environment = AssetRecipe.SofaDefaults().Environment with { LogicalHeight = 117 },
        };

        AssetRecipe migrated = AssetRecipeMigration.MigrateToLatest(current);
        Assert.Equal(2, migrated.PresetVersion);
        Assert.Equal(current.Geometry, migrated.Geometry);
        Assert.Equal(current.Environment, migrated.Environment);
        Assert.False(AssetRecipeMigration.Plan(current)!.RequiresSourceRealignment);
        Assert.Empty(migrated.Validate());
    }

    [Fact]
    public void Glasses_v1_migration_is_explicit_literal_template_upgrade()
    {
        AssetRecipe current = AssetRecipe.GlassesDefaults() with
        {
            PresetVersion = 1,
            FeatureId = "glasses.migration_test",
            ContentId = "cosmetic.glasses.migration_test",
        };

        AssetRecipeMigrationPlan plan = Assert.IsType<AssetRecipeMigrationPlan>(AssetRecipeMigration.Plan(current));
        AssetRecipe migrated = AssetRecipeMigration.MigrateToLatest(current);
        Assert.True(plan.RequiresSourceRealignment);
        Assert.Equal(2, migrated.PresetVersion);
        Assert.Equal(current.Geometry, migrated.Geometry);
    }

    [Theory]
    [MemberData(nameof(CurrentRecipes))]
    public void Current_presets_have_no_migration_plan(AssetRecipe recipe)
    {
        Assert.Null(AssetRecipeMigration.Plan(recipe));
    }

    public static IEnumerable<object[]> CurrentRecipes()
    {
        yield return [AssetRecipe.GlassesDefaults()];
        yield return [AssetRecipe.LampDefaults()];
        yield return [AssetRecipe.SofaDefaults()];
        yield return [AssetRecipe.TableDefaults()];
        yield return [AssetRecipe.PlantDefaults()];
        yield return [AssetRecipe.PaintingDefaults()];
    }
}
