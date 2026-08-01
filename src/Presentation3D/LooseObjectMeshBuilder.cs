using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the drawn shapes for loose objects that ask for one, on the
/// <see cref="GrenadeMeshBuilder"/> idiom: no imported art, every dimension derived from the
/// authoritative collider radius and the profile's authored colours, unindexed triangles so
/// <see cref="SurfaceTool.GenerateNormals"/> gives one normal per face, and a stated envelope
/// verification can check the result against.
///
/// <para>Two shapes so far. The <b>soccer ball</b> is a faceted sphere with alternating dark
/// panels — the traditional look, arrived at by colouring facets rather than by modelling a
/// truncated icosahedron, because at this size the pattern is the whole read. The <b>can</b>
/// is a straight cylinder with rolled rims and a wide band around its belly.</para>
///
/// <para>Clean-room: the ball carries no crest or maker's mark, and the can is a generic
/// red-and-white drink container with no wordmark, script, or trade dress of any real
/// product. Both are placeholders until the M7 art pass.</para>
/// </summary>
public static class LooseObjectMeshBuilder
{
    /// <summary>Facets around the ball, and around the can's barrel.</summary>
    public const int RadialSegments = 18;

    /// <summary>Stacks from pole to pole on the ball.</summary>
    public const int BallRings = 9;

    /// <summary>
    /// How far past the collider radius a built mesh may reach. The ball is a sphere and sits
    /// inside its own circle at <c>1.0</c>; the can is taller than it is wide, and its widest
    /// point is the base rim corner at <c>sqrt(1.35^2 + 0.82^2) = 1.58</c> radii. The bound is
    /// stated here with headroom rather than discovered, the way the grenade's is.
    /// </summary>
    public const float EnvelopeRadiusFactor = 1.80f;

    /// <summary>Can proportions, as multiples of the collider radius.</summary>
    private const float CanHalfHeight = 1.35f;
    private const float CanRimInset = 0.82f;
    private const float CanRimHeight = 0.10f;

    /// <summary>
    /// A traditional panelled ball. <paramref name="fill"/> is the light panel colour and
    /// <paramref name="panel"/> the dark one, both authored on the object's profile.
    /// </summary>
    public static ArrayMesh SoccerBall(float radius, Color fill, Color panel)
    {
        RequireRadius(radius);
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);

        for (int ring = 0; ring < BallRings; ring++)
        {
            float lowerAngle = Mathf.Pi * ring / BallRings;
            float upperAngle = Mathf.Pi * (ring + 1) / BallRings;
            float lowerY = Mathf.Cos(lowerAngle) * radius;
            float upperY = Mathf.Cos(upperAngle) * radius;
            float lowerRadius = Mathf.Sin(lowerAngle) * radius;
            float upperRadius = Mathf.Sin(upperAngle) * radius;

            for (int segment = 0; segment < RadialSegments; segment++)
            {
                float startAngle = Mathf.Tau * segment / RadialSegments;
                float endAngle = Mathf.Tau * (segment + 1) / RadialSegments;
                Color tint = IsDarkPanel(ring, segment) ? panel : fill;

                Vector3 a = OnSphere(lowerRadius, lowerY, startAngle);
                Vector3 b = OnSphere(lowerRadius, lowerY, endAngle);
                Vector3 c = OnSphere(upperRadius, upperY, endAngle);
                Vector3 d = OnSphere(upperRadius, upperY, startAngle);

                AddTriangle(tool, a, b, c, tint);
                AddTriangle(tool, a, c, d, tint);
            }
        }

        tool.GenerateNormals();
        return tool.Commit();
    }

    /// <summary>
    /// A generic drink can: a <paramref name="fill"/>-coloured barrel with a wide
    /// <paramref name="band"/> around its belly and rolled rims top and bottom. No wordmark
    /// and no real product's trade dress — the shape and the two colours are the whole design.
    /// </summary>
    public static ArrayMesh Can(float radius, Color fill, Color band)
    {
        RequireRadius(radius);
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);

        float half = radius * CanHalfHeight;
        float rim = radius * CanRimInset;
        float rimStep = radius * CanRimHeight;
        // Slightly darker than the body: the metal the lid and base are pressed from.
        var metal = new Color(band.R * 0.78f, band.G * 0.78f, band.B * 0.78f, band.A);

        // Bottom cap, base rim, barrel below the band, the band itself, barrel above it,
        // top rim, and the lid. The band occupies the middle third, where a label would be.
        var rings = new List<(float Y, float Radius, Color Tint)>
        {
            (-half - rimStep, 0.0f, metal),
            (-half, rim, metal),
            (-half + rimStep, radius, fill),
            (-radius * 0.34f, radius, fill),
            (-radius * 0.32f, radius, band),
            (radius * 0.32f, radius, band),
            (radius * 0.34f, radius, fill),
            (half - rimStep, radius, fill),
            (half, rim, metal),
            (half + rimStep, 0.0f, metal),
        };

        for (int index = 0; index < rings.Count - 1; index++)
        {
            (float lowerY, float lowerRadius, Color lowerTint) = rings[index];
            (float upperY, float upperRadius, _) = rings[index + 1];

            for (int segment = 0; segment < RadialSegments; segment++)
            {
                float startAngle = Mathf.Tau * segment / RadialSegments;
                float endAngle = Mathf.Tau * (segment + 1) / RadialSegments;

                Vector3 a = OnSphere(lowerRadius, lowerY, startAngle);
                Vector3 b = OnSphere(lowerRadius, lowerY, endAngle);
                Vector3 c = OnSphere(upperRadius, upperY, endAngle);
                Vector3 d = OnSphere(upperRadius, upperY, startAngle);

                // Degenerate at the poles, where one of the two rings has collapsed.
                if (lowerRadius > 0.0f)
                    AddTriangle(tool, a, b, c, lowerTint);
                if (upperRadius > 0.0f)
                    AddTriangle(tool, a, c, d, lowerTint);
            }
        }

        tool.GenerateNormals();
        return tool.Commit();
    }

    /// <summary>
    /// Whether this facet is one of the dark panels. A fixed function of the facet's own
    /// indices and nothing else: presentation may never consume simulation randomness, and
    /// two runs of the same seed must draw the same ball.
    /// </summary>
    public static bool IsDarkPanel(int ring, int segment)
    {
        // Poles get a panel dead centre, and the belly rows are offset from each other so the
        // dark patches scatter the way a stitched ball's do instead of forming stripes.
        if (ring == 0 || ring == BallRings - 1)
            return segment % 3 == 0;

        return (segment + (ring * 5)) % 4 == 0;
    }

    /// <summary>The bound every built mesh stays inside, in world pixels.</summary>
    public static float EnvelopeRadius(float radius) => radius * EnvelopeRadiusFactor;

    private static Vector3 OnSphere(float ringRadius, float y, float angle) =>
        new(Mathf.Cos(angle) * ringRadius, y, Mathf.Sin(angle) * ringRadius);

    private static void AddTriangle(
        SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Color tint)
    {
        tool.SetColor(tint);
        tool.AddVertex(a);
        tool.SetColor(tint);
        tool.AddVertex(b);
        tool.SetColor(tint);
        tool.AddVertex(c);
    }

    private static void RequireRadius(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
    }
}
