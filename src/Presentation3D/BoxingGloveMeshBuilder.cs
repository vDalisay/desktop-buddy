using System;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room boxing-glove cursor-tool visual. Gameplay remains the original circular
/// collider; this is a presentation-only padded glove built from a palm volume, curled knuckle
/// roll, thumb and flared cuff. Its forward axis is +X, matching the pistol's right-facing aim.
/// </summary>
public static class BoxingGloveMeshBuilder
{
    private const int LongitudeSegments = 30;
    private const int LatitudeSegments = 16;
    private const int TubeSegments = 22;
    private const float CaptureVisualScale = 1.8f;

    public static ArrayMesh Build(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile) || profile.IsElongated || profile.Radius <= 0f)
            throw new ArgumentException("A boxing-glove visual requires a live circular cursor-tool profile.", nameof(profile));

        // Visual only: the larger padded silhouette never changes strike radius or damage reach.
        float r = profile.Radius * CaptureVisualScale;
        Color red = profile.VisualColor;
        Color litRed = red.Lightened(0.045f);
        Color foldRed = profile.OutlineColor.Lerp(red, 0.62f);
        Color cuffRed = profile.OutlineColor.Lerp(red, 0.78f);

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        // The palm is deliberately longer than it is tall. The old implementation stacked four
        // almost-round ellipsoids at one point, which collapsed to a tiny red blob in gameplay.
        AddEllipsoid(surface,
            new Vector3(0.05f * r, 0.06f * r, 0.0f),
            new Vector3(0.76f * r, 0.63f * r, 0.61f * r),
            red);

        // Curled padded fingers/knuckles: a C-shaped roll rather than another sphere. This creates
        // the large round striking face plus the deep upper fold visible in the reference glove.
        AddTube(surface,
            new[]
            {
                new Vector3(-0.16f * r, -0.43f * r, 0.00f),
                new Vector3( 0.02f * r, -0.58f * r, 0.00f),
                new Vector3( 0.28f * r, -0.59f * r, 0.00f),
                new Vector3( 0.52f * r, -0.45f * r, 0.00f),
                new Vector3( 0.67f * r, -0.20f * r, 0.00f),
                new Vector3( 0.66f * r,  0.05f * r, 0.00f),
            },
            new[] { 0.36f * r, 0.42f * r, 0.48f * r, 0.51f * r, 0.48f * r, 0.42f * r },
            litRed);

        // Palm-side folded pad gives the glove the compressed inner notch instead of a ball-like
        // silhouette. It sits slightly toward the camera/depth side so the fold remains readable.
        AddEllipsoid(surface,
            new Vector3(0.16f * r, 0.08f * r, 0.25f * r),
            new Vector3(0.46f * r, 0.46f * r, 0.31f * r),
            foldRed);

        // Thumb wraps forward and back toward the fist, like the reference rather than hanging as
        // an isolated side bubble.
        AddTube(surface,
            new[]
            {
                new Vector3(-0.10f * r, 0.24f * r, 0.18f * r),
                new Vector3( 0.10f * r, 0.38f * r, 0.25f * r),
                new Vector3( 0.34f * r, 0.37f * r, 0.25f * r),
                new Vector3( 0.50f * r, 0.24f * r, 0.20f * r),
            },
            new[] { 0.23f * r, 0.28f * r, 0.30f * r, 0.25f * r },
            red.Lightened(0.02f));

        // A real cuff reads as a wrist opening: wide at the back, narrower where it joins the hand.
        AddCuff(surface,
            -0.98f * r,
            -0.46f * r,
            0.55f * r,
            0.42f * r,
            cuffRed);

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the boxing-glove mesh.");
    }

    private static void AddEllipsoid(SurfaceTool surface, Vector3 center, Vector3 radii, Color color)
    {
        for (int latitude = 0; latitude < LatitudeSegments; latitude++)
        {
            float v0 = latitude / (float)LatitudeSegments;
            float v1 = (latitude + 1) / (float)LatitudeSegments;
            float phi0 = Mathf.Pi * (v0 - 0.5f);
            float phi1 = Mathf.Pi * (v1 - 0.5f);

            for (int longitude = 0; longitude < LongitudeSegments; longitude++)
            {
                float theta0 = Mathf.Tau * longitude / LongitudeSegments;
                float theta1 = Mathf.Tau * (longitude + 1) / LongitudeSegments;
                Vector3 p00 = EllipsoidPoint(center, radii, phi0, theta0);
                Vector3 p01 = EllipsoidPoint(center, radii, phi0, theta1);
                Vector3 p10 = EllipsoidPoint(center, radii, phi1, theta0);
                Vector3 p11 = EllipsoidPoint(center, radii, phi1, theta1);
                AddQuad(surface, p00, p10, p11, p01, color);
            }
        }
    }

    private static void AddTube(SurfaceTool surface, Vector3[] centers, float[] radii, Color color)
    {
        if (centers.Length < 2 || centers.Length != radii.Length)
            throw new ArgumentException("A glove tube requires matching centre/radius samples.");

        for (int ring = 0; ring < centers.Length - 1; ring++)
        {
            Vector3 tangent0 = TubeTangent(centers, ring);
            Vector3 tangent1 = TubeTangent(centers, ring + 1);
            Vector3 side0 = new Vector3(-tangent0.Y, tangent0.X, 0.0f).Normalized();
            Vector3 side1 = new Vector3(-tangent1.Y, tangent1.X, 0.0f).Normalized();
            Vector3 depth = Vector3.Back;

            for (int segment = 0; segment < TubeSegments; segment++)
            {
                float a0 = Mathf.Tau * segment / TubeSegments;
                float a1 = Mathf.Tau * (segment + 1) / TubeSegments;
                Vector3 p00 = centers[ring] + ((side0 * Mathf.Cos(a0) + depth * Mathf.Sin(a0)) * radii[ring]);
                Vector3 p01 = centers[ring] + ((side0 * Mathf.Cos(a1) + depth * Mathf.Sin(a1)) * radii[ring]);
                Vector3 p10 = centers[ring + 1] + ((side1 * Mathf.Cos(a0) + depth * Mathf.Sin(a0)) * radii[ring + 1]);
                Vector3 p11 = centers[ring + 1] + ((side1 * Mathf.Cos(a1) + depth * Mathf.Sin(a1)) * radii[ring + 1]);
                AddQuad(surface, p00, p10, p11, p01, color);
            }
        }

        AddTubeCap(surface, centers[0], -TubeTangent(centers, 0), radii[0], color);
        AddTubeCap(surface, centers[^1], TubeTangent(centers, centers.Length - 1), radii[^1], color);
    }

    private static Vector3 TubeTangent(Vector3[] centers, int index)
    {
        Vector3 tangent = index <= 0
            ? centers[1] - centers[0]
            : index >= centers.Length - 1
                ? centers[^1] - centers[^2]
                : centers[index + 1] - centers[index - 1];
        return tangent.LengthSquared() <= 0.000001f ? Vector3.Right : tangent.Normalized();
    }

    private static void AddTubeCap(SurfaceTool surface, Vector3 center, Vector3 tangent, float radius, Color color)
    {
        Vector3 side = new Vector3(-tangent.Y, tangent.X, 0.0f).Normalized();
        Vector3 depth = Vector3.Back;
        for (int segment = 0; segment < TubeSegments; segment++)
        {
            float a0 = Mathf.Tau * segment / TubeSegments;
            float a1 = Mathf.Tau * (segment + 1) / TubeSegments;
            AddTriangle(
                surface,
                center,
                center + ((side * Mathf.Cos(a1) + depth * Mathf.Sin(a1)) * radius),
                center + ((side * Mathf.Cos(a0) + depth * Mathf.Sin(a0)) * radius),
                color);
        }
    }

    private static void AddCuff(
        SurfaceTool surface,
        float backX,
        float frontX,
        float backRadius,
        float frontRadius,
        Color color)
    {
        const int rings = 3;
        for (int ring = 0; ring < rings - 1; ring++)
        {
            float t0 = ring / (float)(rings - 1);
            float t1 = (ring + 1) / (float)(rings - 1);
            float x0 = Mathf.Lerp(backX, frontX, t0);
            float x1 = Mathf.Lerp(backX, frontX, t1);
            float r0 = Mathf.Lerp(backRadius, frontRadius, t0);
            float r1 = Mathf.Lerp(backRadius, frontRadius, t1);
            for (int segment = 0; segment < TubeSegments; segment++)
            {
                float a0 = Mathf.Tau * segment / TubeSegments;
                float a1 = Mathf.Tau * (segment + 1) / TubeSegments;
                Vector3 p00 = new(x0, Mathf.Cos(a0) * r0, Mathf.Sin(a0) * r0);
                Vector3 p01 = new(x0, Mathf.Cos(a1) * r0, Mathf.Sin(a1) * r0);
                Vector3 p10 = new(x1, Mathf.Cos(a0) * r1, Mathf.Sin(a0) * r1);
                Vector3 p11 = new(x1, Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1);
                AddQuad(surface, p00, p10, p11, p01, color);
            }
        }

        // Back rim has a shallow inner ring, leaving a visible wrist opening instead of capping
        // the cuff with a flat red disk.
        float inner = backRadius * 0.72f;
        float insetX = backX + ((frontX - backX) * 0.08f);
        for (int segment = 0; segment < TubeSegments; segment++)
        {
            float a0 = Mathf.Tau * segment / TubeSegments;
            float a1 = Mathf.Tau * (segment + 1) / TubeSegments;
            Vector3 outer0 = new(backX, Mathf.Cos(a0) * backRadius, Mathf.Sin(a0) * backRadius);
            Vector3 outer1 = new(backX, Mathf.Cos(a1) * backRadius, Mathf.Sin(a1) * backRadius);
            Vector3 inner0 = new(insetX, Mathf.Cos(a0) * inner, Mathf.Sin(a0) * inner);
            Vector3 inner1 = new(insetX, Mathf.Cos(a1) * inner, Mathf.Sin(a1) * inner);
            AddQuad(surface, outer0, outer1, inner1, inner0, color.Darkened(0.12f));
        }
    }

    private static Vector3 EllipsoidPoint(Vector3 center, Vector3 radii, float phi, float theta)
    {
        float cosPhi = Mathf.Cos(phi);
        return center + new Vector3(
            radii.X * cosPhi * Mathf.Cos(theta),
            radii.Y * Mathf.Sin(phi),
            radii.Z * cosPhi * Mathf.Sin(theta));
    }

    private static void AddQuad(
        SurfaceTool surface,
        Vector3 p00,
        Vector3 p10,
        Vector3 p11,
        Vector3 p01,
        Color color)
    {
        AddTriangle(surface, p00, p10, p11, color);
        AddTriangle(surface, p00, p11, p01, color);
    }

    private static void AddTriangle(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        AddVertex(surface, a, color);
        AddVertex(surface, b, color);
        AddVertex(surface, c, color);
    }

    private static void AddVertex(SurfaceTool surface, Vector3 point, Color color)
    {
        surface.SetColor(color);
        surface.AddVertex(point);
    }
}
