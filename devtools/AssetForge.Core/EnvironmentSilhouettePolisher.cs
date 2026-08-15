using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Presentation cleanup for the versioned literal-template Environment presets that opt into the
/// v1 full-resolution smoothing contract. Older accepted preset versions deliberately bypass this
/// stage so source + recipe can regenerate their original geometry bytes.
/// </summary>
public static class EnvironmentSilhouettePolisher
{
    public static CanonicalMesh Apply(CanonicalMesh mesh, RgbaImage foreground, AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(recipe);

        if (!UsesSmoothedLiteralContract(recipe) || recipe.Geometry.SurfaceSmoothness <= 0.000001)
            return mesh;

        mesh = SilhouetteSubpixelProjector.Apply(
            mesh,
            foreground,
            recipe.Geometry,
            pixel => EnvironmentTemplateMapping.SourcePixelToWorld(pixel.X, pixel.Y, recipe));

        // Reuse the proven deterministic plush/furniture rim fairing instead of solving the same
        // low-poly silhouette problem twice. This operates only on generated presentation geometry.
        mesh = PartReplacementMeshPostprocessor.Apply(mesh, recipe.Geometry);

        if (recipe.Environment.Anchor == EnvironmentAnchorMode.Floor)
            PinAuthoredFloorContact(mesh, recipe);

        mesh.RecalculateNormals();
        return mesh;
    }

    public static bool UsesSmoothedLiteralContract(AssetRecipe recipe) => recipe.Category switch
    {
        // lamp@2 and sofa@1 shipped in the accepted v0.1 baseline and must remain reproducible.
        AssetCategory.Lamp => recipe.PresetId == "lamp" && recipe.PresetVersion >= 3,
        AssetCategory.Sofa => recipe.PresetId == "sofa" && recipe.PresetVersion >= 2,
        // These categories enter the tool for the first time with the smoothed v1 contract.
        AssetCategory.Table => recipe.PresetId == "table" && recipe.PresetVersion >= 1,
        AssetCategory.Plant => recipe.PresetId == "plant" && recipe.PresetVersion >= 1,
        AssetCategory.Painting => recipe.PresetId == "painting" && recipe.PresetVersion >= 1,
        _ => false,
    };

    private static void PinAuthoredFloorContact(CanonicalMesh mesh, AssetRecipe recipe)
    {
        float gridCellPixels = AssetForgeGenerator.SourceSize / (float)recipe.Geometry.GeometryResolution;
        float tolerancePixels = MathF.Max(2f, gridCellPixels * 1.25f);
        float toleranceWorld = tolerancePixels * EnvironmentTemplateMapping.UnitsPerPixel(recipe) * 1.75f;

        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            Vector2 uv = mesh.Uvs[i];
            float sourceY = uv.Y * AssetForgeGenerator.SourceSize;
            Vector3 position = mesh.Positions[i];
            if (MathF.Abs(sourceY - EnvironmentTemplateSpace.FloorY) > tolerancePixels ||
                MathF.Abs(position.Y) > toleranceWorld)
                continue;
            mesh.Positions[i] = new Vector3(position.X, 0f, position.Z);
        }
    }
}
