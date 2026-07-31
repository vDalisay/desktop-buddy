using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room grenade as a lathed vertex-coloured mesh, on the
/// <see cref="BatMeshBuilder"/> idiom: no imported art, every dimension derived from the
/// authoritative collider radius and the authored <see cref="GrenadeProfile"/> colours, and
/// one shared envelope predicate that verification can check the result against.
///
/// <para>The silhouette is deliberately simple, as the owner asked: an olive-drab ovoid
/// body, a darker cap on top with a short lever laid along it, and — while the pin is still
/// in — a light ring beside the cap. Nothing here carries any real-world design's markings
/// or proportions.</para>
///
/// <para>The lathe axis is local Y, authored directly in the frontal 3D frame where +Y is
/// screen up — so the cap is on top of the grenade the player sees. Unlike the bat, this
/// mesh has no elongated 2D collider whose ends it has to agree with, so there is nothing
/// to flip.</para>
/// </summary>
public static class GrenadeMeshBuilder
{
    public const int RadialSegments = 20;

    /// <summary>
    /// How far past the collider radius the drawn grenade may reach. The cap and lever sit
    /// on top of the body, so unlike the bat this mesh is not strictly inside its circle —
    /// but it is bounded, and the bound is stated here rather than discovered.
    /// </summary>
    public const float EnvelopeRadiusFactor = 1.35f;

    private readonly record struct Ring(float Y, float Radius, Color Tint);

    /// <param name="radius">The grenade body's authoritative collider radius, in px.</param>
    /// <param name="pinIn">Whether the pin ring is still drawn beside the cap.</param>
    public static ArrayMesh Build(GrenadeProfile profile, float radius, bool pinIn)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("A grenade mesh requires a live profile.", nameof(profile));
        if (!float.IsFinite(radius) || radius <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius));

        Color body = profile.BodyColor;
        Color cap = profile.CapColor;

        // An ovoid a little taller than it is wide, capped top and bottom.
        var rings = new List<Ring>
        {
            new(-radius * 1.10f, 0.0f, body),
            new(-radius * 0.95f, radius * 0.42f, body),
            new(-radius * 0.62f, radius * 0.80f, body),
            new(-radius * 0.20f, radius * 0.97f, body),
            new(radius * 0.28f, radius * 0.92f, body),
            new(radius * 0.66f, radius * 0.68f, body),
            new(radius * 0.86f, radius * 0.40f, body),
            // Duplicated boundary so the cap's colour does not blend down the shoulder.
            new(radius * 0.86f + 0.001f, radius * 0.40f, cap),
            new(radius * 1.02f, radius * 0.38f, cap),
            new(radius * 1.18f, radius * 0.30f, cap),
            new(radius * 1.24f, 0.0f, cap),
        };

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Lathe(surface, rings);
        AddLever(surface, radius, cap);
        if (pinIn)
            AddPinRing(surface, radius, profile.PinColor);

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the grenade mesh.");
    }

    /// <summary>
    /// The sphere every vertex of a built grenade must lie inside. Shared with verification
    /// so the mesh is checked against a stated envelope rather than against itself.
    /// </summary>
    public static bool IsInsideEnvelope(Vector3 vertex, float radius, float epsilon = 0.001f)
    {
        if (!vertex.IsFinite() || !float.IsFinite(radius) || radius <= 0.0f)
            return false;

        float bound = radius * EnvelopeRadiusFactor;
        return vertex.LengthSquared() <= (bound * bound) + epsilon;
    }

    private static void Lathe(SurfaceTool surface, List<Ring> rings)
    {
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

                AddVertex(surface, p00, from.Tint);
                AddVertex(surface, p10, to.Tint);
                AddVertex(surface, p11, to.Tint);
                AddVertex(surface, p00, from.Tint);
                AddVertex(surface, p11, to.Tint);
                AddVertex(surface, p01, from.Tint);
            }
        }
    }

    /// <summary>The safety lever, a thin slab laid down one side of the body from the cap.</summary>
    private static void AddLever(SurfaceTool surface, float radius, Color tint) =>
        AddBox(
            surface,
            new Vector3(radius * 0.44f, radius * 0.55f, 0.0f),
            new Vector3(radius * 0.24f, radius * 1.20f, radius * 0.34f),
            tint);

    /// <summary>The pin ring, drawn only while the pin is still in the grenade.</summary>
    private static void AddPinRing(SurfaceTool surface, float radius, Color tint)
    {
        const int Segments = 10;
        float ringRadius = radius * 0.26f;
        float wire = radius * 0.08f;
        var centre = new Vector3(-radius * 0.42f, radius * 0.72f, 0.0f);
        for (int segment = 0; segment < Segments; segment++)
        {
            float angle = Mathf.Tau * segment / Segments;
            var offset = new Vector3(
                ringRadius * Mathf.Cos(angle), ringRadius * Mathf.Sin(angle), 0.0f);
            AddBox(surface, centre + offset, new Vector3(wire, wire, wire), tint);
        }
    }

    private static void AddBox(SurfaceTool surface, Vector3 centre, Vector3 size, Color tint)
    {
        Vector3 half = size * 0.5f;
        Span<Vector3> corner = stackalloc Vector3[8];
        for (int index = 0; index < 8; index++)
        {
            corner[index] = new Vector3(
                centre.X + ((index & 1) == 0 ? -half.X : half.X),
                centre.Y + ((index & 2) == 0 ? -half.Y : half.Y),
                centre.Z + ((index & 4) == 0 ? -half.Z : half.Z));
        }

        AddQuad(surface, corner[0], corner[2], corner[3], corner[1], tint); // -Z
        AddQuad(surface, corner[5], corner[7], corner[6], corner[4], tint); // +Z
        AddQuad(surface, corner[4], corner[6], corner[2], corner[0], tint); // -X
        AddQuad(surface, corner[1], corner[3], corner[7], corner[5], tint); // +X
        AddQuad(surface, corner[0], corner[1], corner[5], corner[4], tint); // -Y
        AddQuad(surface, corner[6], corner[7], corner[3], corner[2], tint); // +Y
    }

    private static void AddQuad(
        SurfaceTool surface,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color tint)
    {
        AddVertex(surface, a, tint);
        AddVertex(surface, b, tint);
        AddVertex(surface, c, tint);
        AddVertex(surface, a, tint);
        AddVertex(surface, c, tint);
        AddVertex(surface, d, tint);
    }

    private static Vector3 Point(Ring ring, float angle) => new(
        ring.Radius * Mathf.Cos(angle),
        ring.Y,
        ring.Radius * Mathf.Sin(angle));

    private static void AddVertex(SurfaceTool surface, Vector3 point, Color tint)
    {
        surface.SetColor(tint);
        surface.AddVertex(point);
    }
}
