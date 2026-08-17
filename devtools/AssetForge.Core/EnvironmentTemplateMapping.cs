using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Canonical mapping for Environment presets whose 1024x1024 authoring-template coordinates are
/// part of the preset contract. Floor categories use the floor/contact line as local Y=0; wall
/// categories use their authored wall anchor centre as local origin. Moving/scaling clean source art
/// inside the fixed template therefore produces the same deterministic placement change in-game.
/// </summary>
public static class EnvironmentTemplateMapping
{
    public static int ReferenceHeightPixels =>
        EnvironmentTemplateSpace.FloorY - EnvironmentTemplateSpace.SafeTop;

    public static int PaintingReferenceHeightPixels =>
        EnvironmentTemplateSpace.PaintingArtBottom - EnvironmentTemplateSpace.PaintingArtTop;

    public static float UnitsPerPixel(AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        int referencePixels = recipe.Environment.Anchor == EnvironmentAnchorMode.Wall
            ? PaintingReferenceHeightPixels
            : ReferenceHeightPixels;
        if (referencePixels <= 0)
            throw new InvalidOperationException("Environment template reference height must be positive.");
        return (float)(recipe.Environment.LogicalHeight / referencePixels);
    }

    public static Vector2 SourcePixelToWorld(double sourceX, double sourceY, AssetRecipe recipe)
    {
        if (!double.IsFinite(sourceX) || !double.IsFinite(sourceY))
            throw new ArgumentOutOfRangeException(nameof(sourceX), "Template coordinates must be finite.");

        float units = UnitsPerPixel(recipe);
        float originY = recipe.Environment.Anchor == EnvironmentAnchorMode.Wall
            ? EnvironmentTemplateSpace.PaintingAnchorY
            : EnvironmentTemplateSpace.FloorY;
        return new Vector2(
            ((float)sourceX - EnvironmentTemplateSpace.CenterX) * units,
            (originY - (float)sourceY) * units);
    }

    /// <summary>
    /// Inverse of SourcePixelToWorld for literal-template editor tools such as the Lamp emitter
    /// gizmo. Returned coordinates are source-template pixels and are intentionally not clamped so
    /// callers can choose whether out-of-canvas motion should clamp, reject or remain visible.
    /// </summary>
    public static Vector2 WorldToSourcePixel(Vector2 world, AssetRecipe recipe)
    {
        if (!float.IsFinite(world.X) || !float.IsFinite(world.Y))
            throw new ArgumentOutOfRangeException(nameof(world), "World coordinates must be finite.");
        float units = UnitsPerPixel(recipe);
        if (!float.IsFinite(units) || units <= 0f)
            throw new InvalidOperationException("Environment template world scale must be positive and finite.");
        float originY = recipe.Environment.Anchor == EnvironmentAnchorMode.Wall
            ? EnvironmentTemplateSpace.PaintingAnchorY
            : EnvironmentTemplateSpace.FloorY;
        return new Vector2(
            (world.X / units) + EnvironmentTemplateSpace.CenterX,
            originY - (world.Y / units));
    }

    public static Vector2 WorldToNormalizedSource(Vector2 world, AssetRecipe recipe)
    {
        Vector2 pixels = WorldToSourcePixel(world, recipe);
        return pixels / EnvironmentTemplateSpace.CanvasSize;
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
        AssetCategory.Table => recipe.PresetId == "table" && recipe.PresetVersion >= 1,
        AssetCategory.Plant => recipe.PresetId == "plant" && recipe.PresetVersion >= 1,
        AssetCategory.Painting => recipe.PresetId == "painting" && recipe.PresetVersion >= 1,
        _ => false,
    };
}
