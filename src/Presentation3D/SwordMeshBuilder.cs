using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The Sword's 3D form. A sibling of <see cref="BatMeshBuilder"/> and deliberately not a
/// generalisation of it: a bat is a solid of revolution and a sword is not, so the one
/// thing that would have to change to share the code — the circular cross-section — is the
/// whole difference between the two shapes.
///
/// <para>Each section is an <b>ellipse</b> rather than a circle: wide across the flat of
/// the blade, thin through it. Bridging consecutive ellipses gives a blade with edges, a
/// crossguard that reads as a crossguard from the front, and a round grip, out of one
/// loop.</para>
///
/// <para>Like the bat, the lathe axis is flipped on the way out. The authoritative capsule
/// lives in 2D where +Y points down; the frontal 3D world maps that to -Y, so the point of
/// the blade ends up sharing the collider tip that actually does the stabbing rather than
/// appearing at the pommel.</para>
/// </summary>
public static class SwordMeshBuilder
{
    /// <summary>
    /// Segments around one section. Half the bat's: the blade is a flattened ellipse whose
    /// silhouette is carried by four sections, not by how round it is.
    /// </summary>
    public const int RadialSegments = 12;

    /// <summary>Where the grip begins, as a fraction from tip to pommel.</summary>
    public const float GuardFraction = 0.62f;

    private readonly record struct Section(float Y, float RadiusX, float RadiusZ, bool Hilt);

    public static ArrayMesh Build(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile) ||
            !profile.IsElongated ||
            profile.Swing is not { } swing ||
            !GodotObject.IsInstanceValid(swing))
        {
            throw new ArgumentException(
                "A sword requires a live elongated swing profile.",
                nameof(profile));
        }

        float halfLength = profile.Length * 0.5f;
        float radius = profile.Radius;

        // Everything is expressed as a fraction of the collider radius so the mesh cannot
        // leave the capsule the physics actually uses — the same envelope rule the bat is
        // verified against.
        float bladeHalfWidth = radius * 0.66f;
        float bladeHalfThickness = radius * 0.20f;
        float guardHalfWidth = radius * 1.0f;
        float guardHalfThickness = radius * 0.34f;
        float gripRadius = radius * 0.34f;
        float pommelRadius = radius * 0.46f;

        float guardY = Mathf.Lerp(-halfLength, halfLength, GuardFraction);

        var sections = new List<Section>
        {
            // The point, and a short taper out of it.
            new(-halfLength, 0.0f, 0.0f, false),
            new(-halfLength + (radius * 0.55f), bladeHalfWidth * 0.55f, bladeHalfThickness * 0.6f, false),
            new(-halfLength + (radius * 1.4f), bladeHalfWidth * 0.92f, bladeHalfThickness, false),

            // The long run of the blade, barely tapering.
            new(guardY - (radius * 1.2f), bladeHalfWidth, bladeHalfThickness, false),

            // The ricasso, then the guard. The boundary is duplicated so the steel does not
            // blend into the hilt colour down the whole blade.
            new(guardY - (radius * 0.28f), bladeHalfWidth * 0.86f, bladeHalfThickness, false),
            new(guardY - (radius * 0.27f), bladeHalfWidth * 0.86f, bladeHalfThickness, true),
            new(guardY, guardHalfWidth, guardHalfThickness, true),
            new(guardY + (radius * 0.30f), guardHalfWidth * 0.88f, guardHalfThickness * 0.9f, true),

            // Grip and pommel.
            new(guardY + (radius * 0.55f), gripRadius, gripRadius, true),
            new(halfLength - (radius * 0.55f), gripRadius, gripRadius, true),
            new(halfLength - (radius * 0.22f), pommelRadius, pommelRadius, true),
            new(halfLength, 0.0f, 0.0f, true),
        };

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int index = 0; index < sections.Count - 1; index++)
        {
            Section from = sections[index];
            Section to = sections[index + 1];
            Color fromColor = from.Hilt ? swing.GripColor : profile.VisualColor;
            Color toColor = to.Hilt ? swing.GripColor : profile.VisualColor;

            for (int radial = 0; radial < RadialSegments; radial++)
            {
                float angle0 = Mathf.Tau * radial / RadialSegments;
                float angle1 = Mathf.Tau * (radial + 1) / RadialSegments;
                Vector3 p00 = Point(from, angle0);
                Vector3 p01 = Point(from, angle1);
                Vector3 p10 = Point(to, angle0);
                Vector3 p11 = Point(to, angle1);

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
            "SurfaceTool failed to build the sword mesh.");
    }

    private static Vector3 Point(Section section, float angle) => new(
        section.RadiusX * Mathf.Cos(angle),
        -section.Y,
        section.RadiusZ * Mathf.Sin(angle));

    private static void AddVertex(SurfaceTool surface, Vector3 point, Color color)
    {
        surface.SetColor(color);
        surface.AddVertex(point);
    }
}
