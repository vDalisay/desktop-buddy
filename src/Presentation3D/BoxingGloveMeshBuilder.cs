using System;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room boxing-glove cursor visual as one continuous padded shell. The cuff,
/// wrist, palm, curled knuckle mass and thumb are deformations of the same surface rather than
/// intersecting primitive blobs, so the silhouette holds together from gameplay angles. Gameplay
/// remains the original circular collider; this is presentation only. Forward is +X, matching the
/// shared cursor-aim/pistol convention.
/// </summary>
public static class BoxingGloveMeshBuilder
{
    private const int RingSegments = 36;
    private const float CaptureVisualScale = 1.8f;

    // X progresses from the open cuff toward the striking face. CenterY bends the padded shell
    // slightly upward through the knuckle roll while RadiusY/RadiusZ keep the fist broad and soft.
    private static readonly ShellRing[] Profile =
    [
        new(-1.08f,  0.14f, 0.55f, 0.50f), // open flared cuff
        new(-0.88f,  0.12f, 0.50f, 0.47f),
        new(-0.62f,  0.09f, 0.43f, 0.43f), // wrist pinch
        new(-0.38f,  0.06f, 0.54f, 0.52f),
        new(-0.12f,  0.02f, 0.70f, 0.65f), // palm
        new( 0.14f, -0.08f, 0.86f, 0.76f),
        new( 0.38f, -0.16f, 0.93f, 0.82f), // knuckle crown
        new( 0.58f, -0.08f, 0.82f, 0.75f),
        new( 0.72f,  0.05f, 0.58f, 0.58f), // curled striking front
        new( 0.79f,  0.12f, 0.25f, 0.29f),
        new( 0.81f,  0.13f, 0.05f, 0.07f), // rounded cap
    ];

    public static ArrayMesh Build(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile) || profile.IsElongated || profile.Radius <= 0f)
            throw new ArgumentException("A boxing-glove visual requires a live circular cursor-tool profile.", nameof(profile));

        float r = profile.Radius * CaptureVisualScale;
        Color red = profile.VisualColor;
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        for (int ring = 0; ring < Profile.Length - 1; ring++)
        {
            for (int segment = 0; segment < RingSegments; segment++)
            {
                float theta0 = Mathf.Tau * segment / RingSegments;
                float theta1 = Mathf.Tau * (segment + 1) / RingSegments;
                Vector3 p00 = ShellPoint(Profile[ring], theta0, r);
                Vector3 p01 = ShellPoint(Profile[ring], theta1, r);
                Vector3 p10 = ShellPoint(Profile[ring + 1], theta0, r);
                Vector3 p11 = ShellPoint(Profile[ring + 1], theta1, r);
                AddQuad(surface, p00, p10, p11, p01, red);
            }
        }

        // Intentionally leave the first cuff ring open. The final tiny ring closes naturally into
        // a rounded front rather than adding a flat primitive cap that would reveal another shape.
        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the boxing-glove shell.");
    }

    private static Vector3 ShellPoint(ShellRing ring, float theta, float scale)
    {
        float cos = Mathf.Cos(theta);
        float sin = Mathf.Sin(theta);

        // The thumb is a smooth local bulge of this SAME shell on the lower palm. It peaks around
        // X~=0.05 and screen-down (+Y), then blends to zero before the cuff and striking front.
        // Circular angular distance keeps the bulge seamless around the ring boundary.
        float xWeight = Gaussian(ring.X, 0.02f, 0.36f);
        float angular = WrappedAngle(theta);
        float thumbWeight = xWeight * Gaussian(angular, 0.0f, 0.50f);
        float radialBulge = 1.0f + (0.34f * thumbWeight);
        float thumbDrop = 0.17f * thumbWeight;
        float thumbForward = 0.08f * thumbWeight;

        return new Vector3(
            (ring.X + thumbForward) * scale,
            (ring.CenterY + thumbDrop + (cos * ring.RadiusY * radialBulge)) * scale,
            (sin * ring.RadiusZ * (1.0f + (0.16f * thumbWeight))) * scale);
    }

    private static float Gaussian(float value, float center, float width)
    {
        float normalized = (value - center) / Math.Max(0.0001f, width);
        return Mathf.Exp(-(normalized * normalized));
    }

    private static float WrappedAngle(float theta)
    {
        float value = theta % Mathf.Tau;
        if (value > Mathf.Pi)
            value -= Mathf.Tau;
        return value;
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
        surface.SetColor(color);
        surface.AddVertex(a);
        surface.SetColor(color);
        surface.AddVertex(b);
        surface.SetColor(color);
        surface.AddVertex(c);
    }

    private readonly record struct ShellRing(
        float X,
        float CenterY,
        float RadiusY,
        float RadiusZ);
}
