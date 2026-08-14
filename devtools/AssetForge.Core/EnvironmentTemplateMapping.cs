using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Canonical mapping for Environment presets whose 1024x1024 authoring-template coordinates are
/// part of the preset contract. The floor/contact line is the local origin and the template centre
/// is X=0. LogicalHeight describes the world-space distance from SafeTop to the floor, so moving or
/// scaling clean source art inside the fixed template produces the same deterministic placement
/// change in generated geometry instead of being silently re-centred or auto-fitted.
/// </summary>
public static class EnvironmentTemplateMapping
{
    public static int ReferenceHeightPixels =>
        EnvironmentTemplateSpace.FloorY - EnvironmentTemplateSpace.SafeTop;

    public static float UnitsPerPixel(AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (ReferenceHeightPixels <= 0)
            throw new InvalidOperationException("Environment template reference height must be positive.");
        return (float)(recipe.Environment.LogicalHeight / ReferenceHeightPixels);
    }

    public static Vector2 SourcePixelToWorld(double sourceX, double sourceY, AssetRecipe recipe)
    {
        if (!double.IsFinite(sourceX) || !double.IsFinite(sourceY))
            throw new ArgumentOutOfRangeException(nameof(sourceX), "Template coordinates must be finite.");

        float units = UnitsPerPixel(recipe);
        return new Vector2(
            ((float)sourceX - EnvironmentTemplateSpace.CenterX) * units,
            -((float)EnvironmentTemplateSpace.FloorY - (float)sourceY) * units);
    }

    public static Vector2 GridVertexToWorld(
        int vertexX,
        int vertexY,
        int gridWidth,
        int gridHeight,
        AssetRecipe recipe)
    {
        if (gridWidth <= 0 || gridHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(gridWidth), "Grid dimensions must be positive.");
        if (vertexX < 0 || vertexX > gridWidth || vertexY < 0 || vertexY > gridHeight)
            throw new ArgumentOutOfRangeException(nameof(vertexX), "Grid vertex must be inside the inclusive grid boundary.");

        double sourceX = vertexX * (EnvironmentTemplateSpace.CanvasSize / (double)gridWidth);
        double sourceY = vertexY * (EnvironmentTemplateSpace.CanvasSize / (double)gridHeight);
        return SourcePixelToWorld(sourceX, sourceY, recipe);
    }

    public static bool UsesLiteralTemplateSpace(AssetRecipe recipe) => recipe.Category switch
    {
        AssetCategory.Lamp => recipe.PresetId == "lamp" && recipe.PresetVersion >= 2,
        AssetCategory.Sofa => recipe.PresetId == "sofa" && recipe.PresetVersion >= 1,
        _ => false,
    };
}
