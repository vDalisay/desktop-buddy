using System;
using System.Collections.Generic;
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

    private const int RadialSegments = 20;

    /// <summary>
    /// A plain hamburger: domed top bun, patty, flat-bottomed lower bun. Built as a stack of
    /// rings lathed around Y, so the silhouette reads as a burger from any angle the loose
    /// object tumbles to. <paramref name="bun"/> is the bread, <paramref name="patty"/> the
    /// filling; no seeds, wrapper, branding or garnish.
    /// </summary>
    public static ArrayMesh Burger(float radius, Color bun, Color patty)
    {
        if (!float.IsFinite(radius) || radius <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius));

        Color topBun = bun.Lightened(0.16f);
        // Height, ring radius, and the colour of the band running up from it.
        var rings = new List<(float Y, float Radius, Color Tint)>
        {
            (-0.62f, 0.00f, bun),  // Underside, closed at the pole.
            (-0.60f, 0.60f, bun),
            (-0.50f, 0.88f, bun),  // Bottom bun, sitting flat.
            (-0.30f, 0.96f, patty),
            (-0.26f, 1.00f, patty), // Patty, deliberately proud of the bread.
            (-0.04f, 1.00f, topBun),
            (0.00f, 0.96f, topBun),
            (0.26f, 0.88f, topBun),
            (0.46f, 0.70f, topBun), // Dome.
            (0.60f, 0.42f, topBun),
            (0.68f, 0.00f, topBun),
        };

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int index = 0; index < rings.Count - 1; index++)
        {
            (float lowerY, float lowerRadius, Color tint) = rings[index];
            (float upperY, float upperRadius, _) = rings[index + 1];
            for (int segment = 0; segment < RadialSegments; segment++)
            {
                float start = Mathf.Tau * segment / RadialSegments;
                float end = Mathf.Tau * (segment + 1) / RadialSegments;
                Vector3 a = Ring(lowerRadius * radius, lowerY * radius, start);
                Vector3 b = Ring(lowerRadius * radius, lowerY * radius, end);
                Vector3 c = Ring(upperRadius * radius, upperY * radius, end);
                Vector3 d = Ring(upperRadius * radius, upperY * radius, start);

                // Degenerate where a ring has collapsed to the pole.
                if (lowerRadius > 0.0f)
                    AddTriangle(tool, a, b, c, tint);
                if (upperRadius > 0.0f)
                    AddTriangle(tool, a, c, d, tint);
            }
        }

        tool.GenerateNormals();
        return tool.Commit();
    }

    private static Vector3 Ring(float radius, float y, float angle) =>
        new(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

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
