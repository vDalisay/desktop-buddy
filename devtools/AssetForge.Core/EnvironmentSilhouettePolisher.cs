using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Presentation cleanup for literal-template Environment silhouettes. It combines the same two
/// quality ideas used by Buddy replacements: full-resolution alpha-contour projection and bounded
/// rim/cap fairing. Legacy Lamp@1 is deliberately excluded so its accepted auto-fit bytes remain
/// reproducible.
/// </summary>
public static class EnvironmentSilhouettePolisher
{
    public static CanonicalMesh Apply(CanonicalMesh mesh, RgbaImage foreground, AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(recipe);

        if (!EnvironmentTemplateMapping.UsesLiteralTemplateSpace(recipe) ||
            recipe.Geometry.SurfaceSmoothness <= 0.000001)
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
