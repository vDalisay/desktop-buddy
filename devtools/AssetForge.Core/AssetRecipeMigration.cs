namespace DesktopBuddy.AssetForge.Core;

public sealed record AssetRecipeMigrationPlan(
    int TargetPresetVersion,
    string ButtonText,
    string Summary,
    bool RequiresSourceRealignment);

/// <summary>
/// Explicit opt-in migration policy for authoring contracts whose newer preset changes placement or
/// generated presentation. Old recipes remain reproducible until the developer chooses this path.
/// </summary>
public static class AssetRecipeMigration
{
    public static AssetRecipeMigrationPlan? Plan(AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.Category switch
        {
            AssetCategory.Glasses when recipe.PresetVersion < 2 => new(
                2,
                "Migrate to glasses@2",
                "glasses@2 switches legacy auto-fit to literal Buddy-head template placement.",
                RequiresSourceRealignment: true),
            AssetCategory.Lamp when recipe.PresetVersion < 3 => new(
                3,
                "Migrate to lamp@3 smoothing",
                recipe.PresetVersion == 1
                    ? "lamp@3 switches legacy auto-fit to literal floor-template placement and enables the smoothed Inflated Solid contract."
                    : "lamp@3 keeps literal floor-template placement but enables full-resolution rim smoothing and the Inflated Solid default.",
                RequiresSourceRealignment: recipe.PresetVersion == 1),
            AssetCategory.Sofa when recipe.PresetVersion < 2 => new(
                2,
                "Migrate to sofa@2 smoothing",
                "sofa@2 keeps literal floor-template placement and adds the shared full-resolution Environment silhouette polisher.",
                RequiresSourceRealignment: false),
            _ => null,
        };
    }

    public static AssetRecipe MigrateToLatest(AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        AssetRecipeMigrationPlan plan = Plan(recipe)
            ?? throw new InvalidOperationException($"{recipe.PresetId}@{recipe.PresetVersion} has no newer supported migration target.");

        AssetRecipe migrated = recipe.Category switch
        {
            AssetCategory.Glasses => recipe with { PresetVersion = plan.TargetPresetVersion },
            AssetCategory.Lamp => recipe with
            {
                PresetVersion = plan.TargetPresetVersion,
                Geometry = recipe.Geometry with
                {
                    ShapeMode = ShapeMode.InflatedSolid,
                    SurfaceSmoothness = 1.0,
                },
            },
            AssetCategory.Sofa => recipe with { PresetVersion = plan.TargetPresetVersion },
            _ => throw new InvalidOperationException($"No migration is registered for {recipe.Category}."),
        };

        IReadOnlyList<string> errors = migrated.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException("Migrated recipe is invalid: " + string.Join("; ", errors));
        return migrated;
    }
}
