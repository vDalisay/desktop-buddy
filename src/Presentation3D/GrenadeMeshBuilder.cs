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
/// body with moulded grooves, a darker cap on top with a folded lever laid down its side,
/// and — while the pin is still in — a ring of wire beside the cap. Nothing here carries any
/// real-world design's markings or proportions.</para>
///
/// <para><b>The mesh is built at the drawn radius, not the collider radius</b>
/// (<see cref="GrenadeProfile.VisualScale"/>): the collider is sized for how a grenade
/// should throw, and drawing to it left a lump too small to carry a shape. The guns took
/// the same decision for the same reason. The envelope below scales with it, so the bound
/// is still stated rather than discovered.</para>
///
/// <para>Triangles are added unindexed, so <see cref="SurfaceTool.GenerateNormals"/> gives
/// one normal per face. The faceting is the point: flat facets catch the two-light rig and
/// are what makes a 40 px object read as a solid rather than as a disc.</para>
///
/// <para>The lathe axis is local Y, authored directly in the frontal 3D frame where +Y is
/// screen up — so the cap is on top of the grenade the player sees. Unlike the bat, this
/// mesh has no elongated 2D collider whose ends it has to agree with, so there is nothing
/// to flip.</para>
/// </summary>
public static class GrenadeMeshBuilder
{
    public const int RadialSegments = 20;
    public const float BodyBottomRadiusFactor = 1.12f;

    /// <summary>
    /// How far past the <b>drawn</b> radius the built grenade may reach. The cap and lever
    /// sit on top of the body, so this mesh is not strictly inside its own circle — but it
    /// is bounded, and the bound is stated here rather than discovered.
    /// </summary>
    public const float EnvelopeRadiusFactor = 1.35f;

    /// <summary>Segments around the dropped pin's wire ring.</summary>
    private const int PinRingSegments = 14;

    /// <summary>Segments around the wire's own circular section.</summary>
    private const int PinWireSegments = 6;

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

        float drawn = DrawnRadius(profile, radius);
        Color body = profile.BodyColor;
        Color cap = profile.CapColor;
        // A hair darker than the body, for the moulded grooves that step into it.
        var groove = new Color(
            body.R * 0.72f, body.G * 0.72f, body.B * 0.72f, body.A);

        // An ovoid a little taller than it is wide, capped top and bottom, with three
        // grooves stepped into the belly so the light has something to break on.
        var rings = new List<Ring>
        {
            new(-drawn * BodyBottomRadiusFactor, 0.0f, body),
            new(-drawn * 0.98f, 0.46f * drawn, body),
            new(-drawn * 0.74f, 0.80f * drawn, body),
            new(-drawn * 0.62f, 0.88f * drawn, body),
            new(-drawn * 0.56f, 0.83f * drawn, groove),
            new(-drawn * 0.50f, 0.90f * drawn, body),
            new(-drawn * 0.22f, 0.99f * drawn, body),
            new(-drawn * 0.06f, 1.00f * drawn, body),
            new(0.0f, 0.93f * drawn, groove),
            new(drawn * 0.06f, 1.00f * drawn, body),
            new(drawn * 0.30f, 0.94f * drawn, body),
            new(drawn * 0.36f, 0.87f * drawn, groove),
            new(drawn * 0.42f, 0.91f * drawn, body),
            new(drawn * 0.66f, 0.70f * drawn, body),
            new(drawn * 0.84f, 0.42f * drawn, body),
            // Duplicated boundary so the cap's colour does not blend down the shoulder.
            new((drawn * 0.84f) + 0.001f, 0.42f * drawn, cap),
            new(drawn * 0.94f, 0.44f * drawn, cap),
            new(drawn * 1.06f, 0.40f * drawn, cap),
            new(drawn * 1.18f, 0.31f * drawn, cap),
            new(drawn * 1.26f, 0.18f * drawn, cap),
            new(drawn * 1.29f, 0.0f, cap),
        };

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Lathe(surface, rings);
        AddLever(surface, drawn, cap);
        if (pinIn)
            AddPinRing(surface, drawn, profile.PinColor);

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the grenade mesh.");
    }

    /// <summary>
    /// The dropped pin, as a solid ring of wire with the straight leg trailing off it —
    /// the same shape <see cref="Tools.PinBody"/> draws flat, built round so the 3D
    /// presentation has something to light. Sized from the pin body's own collider radius
    /// through the same <see cref="GrenadeProfile.VisualScale"/> as the grenade.
    /// </summary>
    /// <param name="ringRadius">The pin body's collider radius, in px.</param>
    public static ArrayMesh BuildPin(GrenadeProfile profile, float ringRadius)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("A pin mesh requires a live profile.", nameof(profile));
        if (!float.IsFinite(ringRadius) || ringRadius <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(ringRadius));

        float drawn = DrawnRadius(profile, ringRadius);
        float wire = drawn * 0.30f;
        Color tint = profile.PinColor;

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        // The ring lies in the XY plane, facing the camera, the way the flat pin reads.
        for (int segment = 0; segment < PinRingSegments; segment++)
        {
            float angle0 = Mathf.Tau * segment / PinRingSegments;
            float angle1 = Mathf.Tau * (segment + 1) / PinRingSegments;
            for (int section = 0; section < PinWireSegments; section++)
            {
                float sweep0 = Mathf.Tau * section / PinWireSegments;
                float sweep1 = Mathf.Tau * (section + 1) / PinWireSegments;
                AddQuad(
                    surface,
                    WirePoint(drawn, wire, angle0, sweep0),
                    WirePoint(drawn, wire, angle1, sweep0),
                    WirePoint(drawn, wire, angle1, sweep1),
                    WirePoint(drawn, wire, angle0, sweep1),
                    tint);
            }
        }

        // The straight leg, so it reads as a pin rather than as a bubble.
        AddBox(
            surface,
            new Vector3(drawn * 1.55f, 0.0f, 0.0f),
            new Vector3(drawn * 1.70f, wire, wire),
            tint);

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the grenade pin mesh.");
    }

    /// <summary>
    /// The drawn radius a collider radius of <paramref name="radius"/> builds at. One
    /// place, so a caller measuring the result and the builder producing it can never
    /// disagree about the scale.
    /// </summary>
    public static float DrawnRadius(GrenadeProfile profile, float radius)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.DrawnRadiusPx(radius);
    }

    public static float VisualGroundOffset(GrenadeProfile profile, float radius) =>
        Mathf.Max(0.0f, DrawnRadius(profile, radius) * BodyBottomRadiusFactor - radius);

    /// <summary>
    /// The sphere every vertex of a built grenade must lie inside. Shared with verification
    /// so the mesh is checked against a stated envelope rather than against itself.
    /// </summary>
    public static bool IsInsideEnvelope(
        Vector3 vertex, GrenadeProfile profile, float radius, float epsilon = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!vertex.IsFinite() || !float.IsFinite(radius) || radius <= 0.0f)
            return false;

        float bound = DrawnRadius(profile, radius) * EnvelopeRadiusFactor;
        return vertex.LengthSquared() <= (bound * bound) + epsilon;
    }

    private static Vector3 WirePoint(float ringRadius, float wire, float angle, float sweep)
    {
        // Around the ring, then around the wire's own section in the plane that contains
        // the ring's radial direction and the camera axis.
        var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0.0f);
        float half = wire * 0.5f;
        return (radial * (ringRadius + (half * Mathf.Cos(sweep)))) +
               new Vector3(0.0f, 0.0f, half * Mathf.Sin(sweep));
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

    /// <summary>
    /// The safety lever: a thin slab laid down one side of the body from the cap, with the
    /// short fold that hooks over the top. Two boxes rather than one, because the fold is
    /// what stops the lever reading as a stuck-on tab.
    /// </summary>
    private static void AddLever(SurfaceTool surface, float drawn, Color tint)
    {
        AddBox(
            surface,
            new Vector3(drawn * 0.46f, drawn * 0.50f, 0.0f),
            new Vector3(drawn * 0.22f, drawn * 1.24f, drawn * 0.32f),
            tint);
        AddBox(
            surface,
            new Vector3(drawn * 0.30f, drawn * 1.06f, 0.0f),
            new Vector3(drawn * 0.44f, drawn * 0.18f, drawn * 0.30f),
            tint);
    }

    /// <summary>The pin ring, drawn only while the pin is still in the grenade.</summary>
    private static void AddPinRing(SurfaceTool surface, float drawn, Color tint)
    {
        float ringRadius = drawn * 0.26f;
        float wire = drawn * 0.09f;
        var centre = new Vector3(-drawn * 0.42f, drawn * 0.72f, 0.0f);
        for (int segment = 0; segment < PinRingSegments; segment++)
        {
            float angle0 = Mathf.Tau * segment / PinRingSegments;
            float angle1 = Mathf.Tau * (segment + 1) / PinRingSegments;
            for (int section = 0; section < PinWireSegments; section++)
            {
                float sweep0 = Mathf.Tau * section / PinWireSegments;
                float sweep1 = Mathf.Tau * (section + 1) / PinWireSegments;
                AddQuad(
                    surface,
                    centre + WirePoint(ringRadius, wire, angle0, sweep0),
                    centre + WirePoint(ringRadius, wire, angle1, sweep0),
                    centre + WirePoint(ringRadius, wire, angle1, sweep1),
                    centre + WirePoint(ringRadius, wire, angle0, sweep1),
                    tint);
            }
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
