using System;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room boxing-glove cursor-tool visual. The authoritative gameplay shape stays
/// the existing circular <see cref="CollisionShape2D"/>; this mesh only replaces the old red-ball
/// presentation with a readable padded fist, thumb and cuff silhouette.
/// </summary>
public static class BoxingGloveMeshBuilder
{
    private const int LongitudeSegments = 20;
    private const int LatitudeSegments = 10;
    private const float CaptureVisualScale = 1.8f;

    public static ArrayMesh Build(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile) || profile.IsElongated || profile.Radius <= 0f)
            throw new ArgumentException("A boxing-glove visual requires a live circular cursor-tool profile.", nameof(profile));

        // Visual-only oversizing: keep the existing circular collider and impact reach unchanged.
        // The old first-pass mesh fit entirely inside that radius and therefore read much smaller
        // than the other cursor tools on the three-quarter camera.
        float r = profile.Radius * CaptureVisualScale;
        Color main = profile.VisualColor;
        Color dark = profile.OutlineColor.Lerp(main, 0.28f);
        Color cuff = profile.OutlineColor.Lerp(main, 0.48f);

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        AddEllipsoid(surface, new Vector3(0f, -r * 0.10f, 0f),
            new Vector3(r * 0.72f, r * 0.77f, r * 0.62f), main);

        // A smaller forward lobe breaks the ball silhouette into the familiar curled-knuckle shape.
        AddEllipsoid(surface, new Vector3(r * 0.22f, -r * 0.38f, r * 0.02f),
            new Vector3(r * 0.54f, r * 0.42f, r * 0.56f), main.Lightened(0.035f));

        // Thumb pad: offset down/forward so it reads from the frontal gameplay camera.
        AddEllipsoid(surface, new Vector3(r * 0.46f, r * 0.18f, r * 0.10f),
            new Vector3(r * 0.28f, r * 0.43f, r * 0.34f), dark);

        // Short wrist cuff. It deliberately remains visual-only; its larger presentation does not
        // enlarge the authoritative 2D strike circle.
        AddEllipsoid(surface, new Vector3(-r * 0.04f, r * 0.62f, 0f),
            new Vector3(r * 0.50f, r * 0.28f, r * 0.48f), cuff);

        // The capture-polish material is lit. Without vertex normals the mesh falls back to a
        // nearly flat read even though it is volumetric, which was the exact problem with the
        // first pass.
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
                float u0 = longitude / (float)LongitudeSegments;
                float u1 = (longitude + 1) / (float)LongitudeSegments;
                float theta0 = Mathf.Tau * u0;
                float theta1 = Mathf.Tau * u1;

                Vector3 p00 = EllipsoidPoint(center, radii, phi0, theta0);
                Vector3 p01 = EllipsoidPoint(center, radii, phi0, theta1);
                Vector3 p10 = EllipsoidPoint(center, radii, phi1, theta0);
                Vector3 p11 = EllipsoidPoint(center, radii, phi1, theta1);

                AddVertex(surface, p00, color);
                AddVertex(surface, p10, color);
                AddVertex(surface, p11, color);
                AddVertex(surface, p00, color);
                AddVertex(surface, p11, color);
                AddVertex(surface, p01, color);
            }
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

    private static void AddVertex(SurfaceTool surface, Vector3 point, Color color)
    {
        surface.SetColor(color);
        surface.AddVertex(point);
    }
}
