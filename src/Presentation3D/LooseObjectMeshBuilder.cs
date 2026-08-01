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
/// <para>Three shapes so far. The <b>soccer ball</b> is a white sphere with twelve raised dark
/// pentagons in the topology of an icosahedron — the traditional read without importing art
/// or building a full truncated-icosahedron collider. The <b>can</b>
/// is a straight cylinder with rolled rims and a wide band around its belly. The
/// <b>repair kit</b> is a two-tone case with a proud cross and an arched carry handle.</para>
///
/// <para>Clean-room: the ball carries no crest or maker's mark, the can is a generic
/// red-and-white drink container with no wordmark, script, or trade dress of any real
/// product, and the case carries a plain cross rather than any real organisation's emblem.
/// All three are placeholders until the M7 art pass.</para>
/// </summary>
public static class LooseObjectMeshBuilder
{
    /// <summary>Facets around the ball, and around the can's barrel.</summary>
    public const int RadialSegments = 18;

    /// <summary>Stacks from pole to pole on the ball.</summary>
    public const int BallRings = 12;

    private const int BallRadialSegments = 24;
    private const float PentagonAngularRadius = 0.20f;
    private const float PentagonSurfaceScale = 1.025f;

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
    /// First-aid case proportions, as multiples of the collider radius. The case is wider than
    /// it is tall — a satchel read rather than a lunchbox one — and its far corner sits at
    /// <c>sqrt(1.30^2 + 0.86^2 + 0.52^2) = 1.63</c> radii, inside the shared envelope.
    /// </summary>
    private const float KitHalfWidth = 1.30f;
    private const float KitHalfHeight = 0.86f;
    private const float KitHalfDepth = 0.52f;
    private const float KitSeamHeight = 0.10f;
    // Sized for a case that is about 26 px wide on screen: at the first pass the cross was
    // 1.4 px across the arms and simply did not read (owner, 2026-08-02, "I don't see it").
    private const float KitCrossArm = 0.52f;
    private const float KitCrossWidth = 0.26f;
    private const float KitCrossRelief = 0.06f;
    private const float KitHandleSpan = 0.46f;
    private const float KitHandleRise = 0.44f;
    private const float KitHandleDepth = 0.16f;
    private const float KitHandleThickness = 0.13f;
    private const int KitHandleSegments = 8;

    /// <summary>The cross on the case. Not authored: it is the same green on every kit.</summary>
    private static readonly Color KitCrossColor = new(0.16f, 0.72f, 0.36f, 1.0f);

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

            for (int segment = 0; segment < BallRadialSegments; segment++)
            {
                float startAngle = Mathf.Tau * segment / BallRadialSegments;
                float endAngle = Mathf.Tau * (segment + 1) / BallRadialSegments;

                Vector3 a = OnSphere(lowerRadius, lowerY, startAngle);
                Vector3 b = OnSphere(lowerRadius, lowerY, endAngle);
                Vector3 c = OnSphere(upperRadius, upperY, endAngle);
                Vector3 d = OnSphere(upperRadius, upperY, startAngle);

                AddTriangle(tool, a, b, c, fill);
                AddTriangle(tool, a, c, d, fill);
            }
        }

        AddPentagon(tool, Vector3.Forward, radius, panel);
        AddPentagon(tool, Vector3.Back, radius, panel);
        float ringZ = 1.0f / Mathf.Sqrt(5.0f);
        float ringRadius = 2.0f / Mathf.Sqrt(5.0f);
        for (int index = 0; index < 5; index++)
        {
            float upperAngle = Mathf.Tau * index / 5.0f;
            float lowerAngle = upperAngle + (Mathf.Pi / 5.0f);
            AddPentagon(tool, new Vector3(
                Mathf.Cos(upperAngle) * ringRadius,
                Mathf.Sin(upperAngle) * ringRadius,
                ringZ), radius, panel);
            AddPentagon(tool, new Vector3(
                Mathf.Cos(lowerAngle) * ringRadius,
                Mathf.Sin(lowerAngle) * ringRadius,
                -ringZ), radius, panel);
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
    /// A first-aid case: a squat box with a white lid over a <paramref name="shell"/>-coloured
    /// base, a cross of the same colour standing proud of the front face, and a metal carry
    /// handle arched over the top. <paramref name="fill"/> is the lid, and the seam between the
    /// two halves sits where a case's lid actually closes rather than at the middle.
    ///
    /// <para>Clean-room: a plain cross on a two-tone case, no maker's mark, no red-cross
    /// emblem in its protected form and no other real organisation's device. Placeholder until
    /// the M7 art pass, like the ball and the can.</para>
    /// </summary>
    public static ArrayMesh RepairKit(float radius, Color fill, Color shell)
    {
        RequireRadius(radius);
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);

        float halfWidth = radius * KitHalfWidth;
        float halfHeight = radius * KitHalfHeight;
        float halfDepth = radius * KitHalfDepth;
        // Where the lid meets the base. Above the centre line, so the case reads as a lid on a
        // deeper body rather than as two stacked halves.
        float seam = halfHeight * KitSeamHeight;
        var metal = new Color(0.72f, 0.75f, 0.78f, 1.0f);

        // The box, as two stacked slabs so the lid and the base carry their own colours.
        AddBox(
            tool,
            new Vector3(-halfWidth, seam, -halfDepth),
            new Vector3(halfWidth, halfHeight, halfDepth),
            fill);
        AddBox(
            tool,
            new Vector3(-halfWidth, -halfHeight, -halfDepth),
            new Vector3(halfWidth, seam, halfDepth),
            shell);

        // The cross, centred on the case and standing just proud of the front face so it never
        // z-fights the two slabs it straddles. Green rather than the shell colour (owner,
        // 2026-08-02): a red cross on a red base disappears, and green reads as the heal.
        float front = halfDepth + radius * KitCrossRelief;
        float armLength = radius * KitCrossArm;
        float armWidth = radius * KitCrossWidth;
        AddFrontQuad(tool, -armWidth, armWidth, -armLength, armLength, front, KitCrossColor);
        AddFrontQuad(tool, -armLength, armLength, -armWidth, armWidth, front, KitCrossColor);
        // And on the back, wound the other way round. A case tumbles when it is thrown, so it
        // shows whichever face it lands on, and a cross on one side only is a coin flip.
        AddFrontQuad(tool, armWidth, -armWidth, -armLength, armLength, -front, KitCrossColor);
        AddFrontQuad(tool, armLength, -armLength, -armWidth, armWidth, -front, KitCrossColor);

        // The handle: a flat strap arched over the lid, drawn as a ribbon of quads so it has
        // thickness from the side as well as from the front.
        float archHalfWidth = halfWidth * KitHandleSpan;
        float archRise = radius * KitHandleRise;
        float strapHalfDepth = radius * KitHandleDepth;
        float strapThickness = radius * KitHandleThickness;
        for (int segment = 0; segment < KitHandleSegments; segment++)
        {
            float startT = (float)segment / KitHandleSegments;
            float endT = (float)(segment + 1) / KitHandleSegments;
            Vector3 start = OnArch(startT, archHalfWidth, archRise, halfHeight);
            Vector3 end = OnArch(endT, archHalfWidth, archRise, halfHeight);

            // Outer face, inner face, and the two edges that close the ribbon.
            AddStrip(tool, start, end, strapHalfDepth, strapThickness, metal);
        }

        tool.GenerateNormals();
        return tool.Commit();
    }

    /// <summary>One point along the handle's arch, from one foot on the lid to the other.</summary>
    private static Vector3 OnArch(float t, float halfWidth, float rise, float lidY)
    {
        float angle = Mathf.Pi * t;
        return new Vector3(
            -Mathf.Cos(angle) * halfWidth,
            lidY + Mathf.Sin(angle) * rise,
            0.0f);
    }

    /// <summary>A four-sided ribbon segment between two points on the handle's arch.</summary>
    private static void AddStrip(
        SurfaceTool tool, Vector3 start, Vector3 end, float halfDepth, float thickness, Color tint)
    {
        Vector3 along = (end - start).Normalized();
        // The strap's own "up": across the arch, in the plane of the case.
        Vector3 out_ = new Vector3(-along.Y, along.X, 0.0f).Normalized() * thickness;
        Vector3 depth = new(0.0f, 0.0f, halfDepth);

        Vector3 a = start + out_ - depth;
        Vector3 b = end + out_ - depth;
        Vector3 c = end + out_ + depth;
        Vector3 d = start + out_ + depth;
        Vector3 e = start - out_ - depth;
        Vector3 f = end - out_ - depth;
        Vector3 g = end - out_ + depth;
        Vector3 h = start - out_ + depth;

        // Corners counter-clockwise as seen from outside each face, per AddQuad's contract.
        AddQuad(tool, a, d, c, b, tint);
        AddQuad(tool, h, e, f, g, tint);
        AddQuad(tool, d, h, g, c, tint);
        AddQuad(tool, e, a, b, f, tint);
    }

    /// <summary>An axis-aligned box between two corners, every face the same colour.</summary>
    private static void AddBox(SurfaceTool tool, Vector3 min, Vector3 max, Color tint)
    {
        var lbf = new Vector3(min.X, min.Y, max.Z);
        var rbf = new Vector3(max.X, min.Y, max.Z);
        var rtf = new Vector3(max.X, max.Y, max.Z);
        var ltf = new Vector3(min.X, max.Y, max.Z);
        var lbb = new Vector3(min.X, min.Y, min.Z);
        var rbb = new Vector3(max.X, min.Y, min.Z);
        var rtb = new Vector3(max.X, max.Y, min.Z);
        var ltb = new Vector3(min.X, max.Y, min.Z);

        AddQuad(tool, lbf, rbf, rtf, ltf, tint);
        AddQuad(tool, rbb, lbb, ltb, rtb, tint);
        AddQuad(tool, ltf, rtf, rtb, ltb, tint);
        AddQuad(tool, lbb, rbb, rbf, lbf, tint);
        AddQuad(tool, lbb, lbf, ltf, ltb, tint);
        AddQuad(tool, rbf, rbb, rtb, rtf, tint);
    }

    /// <summary>A rectangle on the case's front face, at a stated depth.</summary>
    private static void AddFrontQuad(
        SurfaceTool tool, float left, float right, float bottom, float top, float z, Color tint) =>
        AddQuad(
            tool,
            new Vector3(left, bottom, z),
            new Vector3(right, bottom, z),
            new Vector3(right, top, z),
            new Vector3(left, top, z),
            tint);

    /// <summary>
    /// A quad whose corners are given counter-clockwise <i>as seen from the side meant to be
    /// visible</i>, emitted as the clockwise triangles Godot treats as front faces. Getting this
    /// backwards does not look like a bug: a closed box wound inside-out still reads as a box,
    /// because what survives the cull is the inside of its own far wall — but anything applied
    /// to a surface, like the cross, vanishes. `the_kit_is_drawn_once_as_a_case` checks it.
    /// </summary>
    private static void AddQuad(
        SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint)
    {
        AddTriangle(tool, a, c, b, tint);
        AddTriangle(tool, a, d, c, tint);
    }

    private static void AddPentagon(
        SurfaceTool tool, Vector3 direction, float radius, Color tint)
    {
        Vector3 normal = direction.Normalized();
        Vector3 tangent = Mathf.Abs(normal.Y) < 0.9f
            ? normal.Cross(Vector3.Up).Normalized()
            : normal.Cross(Vector3.Right).Normalized();
        Vector3 bitangent = normal.Cross(tangent).Normalized();
        Vector3 center = normal * radius * PentagonSurfaceScale;
        var points = new Vector3[5];
        for (int index = 0; index < points.Length; index++)
        {
            float angle = -Mathf.Pi * 0.5f + (Mathf.Tau * index / points.Length);
            Vector3 offset = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
            points[index] = (normal + offset * Mathf.Tan(PentagonAngularRadius)).Normalized() *
                radius * PentagonSurfaceScale;
        }

        for (int index = 0; index < points.Length; index++)
            AddTriangle(tool, center, points[(index + 1) % points.Length], points[index], tint);
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
