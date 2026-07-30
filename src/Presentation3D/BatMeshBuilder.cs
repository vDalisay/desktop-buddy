using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room classic wooden bat visual as a lathed vertex-coloured
/// mesh. The cross-section is deliberately inset from the authoritative capsule
/// collider: presentation can never advertise an earlier contact than physics.
/// </summary>
public static class BatMeshBuilder
{
    public const int RadialSegments = 24;
    public const float GripStartFraction = 0.77f;

    private readonly record struct Ring(float Y, float Radius, bool Grip);

    public static ArrayMesh Build(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile) ||
            !profile.IsElongated ||
            profile.Swing is not { } swing ||
            !GodotObject.IsInstanceValid(swing))
        {
            throw new ArgumentException(
                "A lathed bat requires a live elongated swing profile.",
                nameof(profile));
        }

        float halfLength = profile.Length * 0.5f;
        float cylinderEnd = halfLength - profile.Radius;
        float gripStart = Mathf.Lerp(-halfLength, halfLength, GripStartFraction);
        float tipInset = profile.Radius * 0.45f;
        float capShoulder = profile.Radius * 0.97f;
        float handleRadius = Mathf.Min(4.0f, profile.Radius * 0.58f);
        float knobRadius = Mathf.Min(5.0f, profile.Radius * 0.72f);

        var rings = new List<Ring>
        {
            new(-halfLength, 0.0f, false),
            new(-halfLength + profile.Radius * 0.15f, tipInset, false),
            new(-halfLength + profile.Radius * 0.45f, profile.Radius * 0.78f, false),
            new(-cylinderEnd - profile.Radius * 0.15f, capShoulder, false),
            new(-cylinderEnd, profile.Radius, false),
            new(-profile.Length * 0.14f, profile.Radius, false),
            new(profile.Length * 0.18f, profile.Radius * 0.72f, false),
            new(gripStart, handleRadius, false),
            // Duplicate the wrap boundary so the authored colours do not blend
            // down the whole taper.
            new(gripStart + 0.001f, handleRadius, true),
            new(cylinderEnd, handleRadius, true),
            new(cylinderEnd + profile.Radius * 0.28f, knobRadius, true),
            new(halfLength - profile.Radius * 0.30f, knobRadius * 0.92f, true),
            new(halfLength, 0.0f, true),
        };

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int ring = 0; ring < rings.Count - 1; ring++)
        {
            Ring from = rings[ring];
            Ring to = rings[ring + 1];
            for (int radial = 0; radial < RadialSegments; radial++)
            {
                float angle0 = Mathf.Tau * radial / RadialSegments;
                float angle1 = Mathf.Tau * (radial + 1) / RadialSegments;
                Vector3 p00 = Point(from, angle0);
                Vector3 p01 = Point(from, angle1);
                Vector3 p10 = Point(to, angle0);
                Vector3 p11 = Point(to, angle1);
                Color fromColor = from.Grip ? swing.GripColor : profile.VisualColor;
                Color toColor = to.Grip ? swing.GripColor : profile.VisualColor;

                AddVertex(surface, p00, fromColor);
                AddVertex(surface, p10, toColor);
                AddVertex(surface, p11, toColor);
                AddVertex(surface, p00, fromColor);
                AddVertex(surface, p11, toColor);
                AddVertex(surface, p01, fromColor);
            }
        }

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the bat mesh.");
    }

    /// <summary>Independent capsule-envelope predicate shared with verification.</summary>
    public static bool IsInsideCapsule(Vector3 vertex, float length, float radius, float epsilon = 0.001f)
    {
        if (!vertex.IsFinite() || !float.IsFinite(length) || !float.IsFinite(radius) ||
            length <= radius * 2.0f || radius <= 0.0f)
        {
            return false;
        }

        float radial = new Vector2(vertex.X, vertex.Z).Length();
        float cylinderEnd = length * 0.5f - radius;
        float axialOutside = Mathf.Max(0.0f, Mathf.Abs(vertex.Y) - cylinderEnd);
        return radial * radial + axialOutside * axialOutside <=
               radius * radius + epsilon;
    }

    private static Vector3 Point(Ring ring, float angle) => new(
        ring.Radius * Mathf.Cos(angle),
        // The authoritative capsule lives in 2D where +Y points down. The
        // frontal 3D world maps that axis to -Y, so flip the lathe here: the
        // wooden barrel at local 2D -Y must share the glint/collider tip rather
        // than appearing at the physical handle end.
        -ring.Y,
        ring.Radius * Mathf.Sin(angle));

    private static void AddVertex(SurfaceTool surface, Vector3 point, Color color)
    {
        surface.SetColor(color);
        surface.AddVertex(point);
    }
}
