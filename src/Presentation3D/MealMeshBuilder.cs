using System;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Clean-room Demo placeholder for the Meal loose object. The gameplay profile remains a single
/// circular physics body; this render-only mesh gives the item an immediately readable food
/// silhouette until final art replaces it.
/// </summary>
public static class MealMeshBuilder
{
    /// <summary>Maximum distance from origin in collider-radius units, with test headroom.</summary>
    public const float EnvelopeRadiusFactor = 1.50f;

    private static readonly Color PlateColor = new(0.90f, 0.91f, 0.88f, 1.0f);

    public static ArrayMesh PlatedSandwich(float radius, Color bread, Color filling)
    {
        if (!float.IsFinite(radius) || radius <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius));

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);

        // Plate: deliberately shallow so it reads as a support rather than changing the
        // apparent gameplay footprint. All dimensions derive from the existing collider radius.
        AddBox(
            tool,
            new Vector3(-1.18f * radius, -0.62f * radius, -0.48f * radius),
            new Vector3(1.18f * radius, -0.47f * radius, 0.48f * radius),
            PlateColor);

        // Three simple stacked layers read as a sandwich from the frontal game camera. No logo,
        // package, garnish, or branded food silhouette is authored here.
        AddBox(
            tool,
            new Vector3(-0.88f * radius, -0.40f * radius, -0.36f * radius),
            new Vector3(0.88f * radius, -0.10f * radius, 0.36f * radius),
            bread);
        AddBox(
            tool,
            new Vector3(-0.82f * radius, -0.08f * radius, -0.38f * radius),
            new Vector3(0.82f * radius, 0.12f * radius, 0.38f * radius),
            filling);
        AddBox(
            tool,
            new Vector3(-0.88f * radius, 0.14f * radius, -0.36f * radius),
            new Vector3(0.88f * radius, 0.44f * radius, 0.36f * radius),
            bread.Lightened(0.08f));

        tool.GenerateNormals();
        return tool.Commit();
    }

    private static void AddBox(SurfaceTool tool, Vector3 min, Vector3 max, Color color)
    {
        Vector3 p000 = new(min.X, min.Y, min.Z);
        Vector3 p001 = new(min.X, min.Y, max.Z);
        Vector3 p010 = new(min.X, max.Y, min.Z);
        Vector3 p011 = new(min.X, max.Y, max.Z);
        Vector3 p100 = new(max.X, min.Y, min.Z);
        Vector3 p101 = new(max.X, min.Y, max.Z);
        Vector3 p110 = new(max.X, max.Y, min.Z);
        Vector3 p111 = new(max.X, max.Y, max.Z);

        AddQuad(tool, p001, p101, p111, p011, color); // front
        AddQuad(tool, p100, p000, p010, p110, color); // back
        AddQuad(tool, p000, p001, p011, p010, color); // left
        AddQuad(tool, p101, p100, p110, p111, color); // right
        AddQuad(tool, p010, p011, p111, p110, color); // top
        AddQuad(tool, p000, p100, p101, p001, color); // bottom
    }

    private static void AddQuad(
        SurfaceTool tool,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color color)
    {
        AddTriangle(tool, a, b, c, color);
        AddTriangle(tool, a, c, d, color);
    }

    private static void AddTriangle(
        SurfaceTool tool,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color color)
    {
        tool.SetColor(color);
        tool.AddVertex(a);
        tool.SetColor(color);
        tool.AddVertex(b);
        tool.SetColor(color);
        tool.AddVertex(c);
    }
}
