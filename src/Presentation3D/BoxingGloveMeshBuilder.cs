using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room boxing-glove cursor visual out of four separated forms, in the same
/// vertex-coloured spirit as <see cref="SprayerMeshBuilder"/>: nothing imported, every
/// dimension derived from the authored <see cref="CursorToolProfile"/>.
///
/// <para>An earlier version blended the whole glove — cuff, mitt and thumb — into one lathed
/// surface with the thumb as a Gaussian bulge. That reads as a blob at cursor size, because a
/// boxing glove is recognised by its <b>separations</b>: a fat rounded mitt, a distinctly
/// stepped and darker wrist cuff, a small thumb pod sitting proud of the lower front, and the
/// pale lace ridge over the crown. Each of those is its own form here, so the silhouette and
/// the flat-shaded colour blocks both say "boxing glove" at a dozen pixels.</para>
///
/// <para>Local space follows the shared cursor-aim convention: forward is <b>+X</b>, and the
/// mesh sits on the 3D presentation plane where <b>+Y is screen up</b>
/// (see <see cref="WorldPlaneMapping"/>). Gameplay remains the original circular collider;
/// this is presentation only.</para>
/// </summary>
public static class BoxingGloveMeshBuilder
{
    private const int RingSegments = 24;
    private const float CaptureVisualScale = 1.8f;

    /// <summary>One lathe cross-section: a ring of the surface, in glove units.</summary>
    private readonly record struct Ring(float X, float CenterY, float RadiusY, float RadiusZ, Color Tint);

    /// <summary>One axis-aligned block, in glove units.</summary>
    private readonly record struct Block(Vector3 Centre, Vector3 Size, Color Tint);

    public static ArrayMesh Build(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile) || profile.IsElongated || profile.Radius <= 0f)
            throw new ArgumentException("A boxing-glove visual requires a live circular cursor-tool profile.", nameof(profile));

        float r = profile.Radius * CaptureVisualScale;
        Color mitt = profile.VisualColor;
        Color cuff = profile.OutlineColor;
        Color wrap = mitt.Lerp(Colors.White, 0.55f);
        Color lace = mitt.Lerp(Colors.White, 0.88f);
        Color thumb = mitt.Darkened(0.12f);

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        // The mitt: a single fat rounded mass with a broad, slightly flattened striking face.
        AddLathe(surface, new[]
        {
            new Ring(-0.44f, 0.00f, 0.02f, 0.02f, mitt),
            new Ring(-0.42f, 0.00f, 0.52f, 0.50f, mitt),
            new Ring(-0.18f, 0.00f, 0.72f, 0.64f, mitt),
            new Ring( 0.08f, 0.02f, 0.88f, 0.76f, mitt),
            new Ring( 0.34f, 0.02f, 0.95f, 0.82f, mitt),
            new Ring( 0.58f, 0.01f, 0.94f, 0.81f, mitt),
            new Ring( 0.80f, 0.00f, 0.84f, 0.73f, mitt),
            new Ring( 0.95f, 0.00f, 0.58f, 0.53f, mitt),
            new Ring( 1.02f, 0.00f, 0.02f, 0.02f, mitt),
        }, r, Transform3D.Identity);

        // The cuff: deliberately narrower than the mitt so the step is visible from any angle,
        // and in the authored outline colour with one pale wrap band around it.
        AddLathe(surface, new[]
        {
            new Ring(-0.90f, 0.00f, 0.02f, 0.02f, cuff),
            new Ring(-0.94f, 0.00f, 0.40f, 0.39f, cuff),
            new Ring(-1.12f, 0.00f, 0.44f, 0.43f, cuff),
            new Ring(-1.15f, 0.00f, 0.56f, 0.54f, cuff),
            new Ring(-1.06f, 0.00f, 0.57f, 0.55f, wrap),
            new Ring(-0.90f, 0.00f, 0.56f, 0.54f, wrap),
            new Ring(-0.80f, 0.00f, 0.55f, 0.53f, cuff),
            new Ring(-0.34f, 0.00f, 0.58f, 0.56f, cuff),
            new Ring(-0.32f, 0.00f, 0.02f, 0.02f, cuff),
        }, r, Transform3D.Identity);

        // The thumb: its own pod on the lower front, angled forward and slightly down. Rooted
        // inside the mitt so it never floats, tipped well clear of it so it always reads.
        AddLathe(surface, new[]
        {
            new Ring(-0.10f, 0.0f, 0.02f, 0.02f, thumb),
            new Ring(-0.06f, 0.0f, 0.32f, 0.30f, thumb),
            new Ring( 0.34f, 0.0f, 0.34f, 0.31f, thumb),
            new Ring( 0.62f, 0.0f, 0.29f, 0.27f, thumb),
            new Ring( 0.76f, 0.0f, 0.17f, 0.17f, thumb),
            new Ring( 0.80f, 0.0f, 0.02f, 0.02f, thumb),
        }, r, new Transform3D(
            new Basis(Vector3.Back, -0.20f),
            new Vector3(0.12f, -0.70f, 0.0f) * r));

        // Three pale lace ridges over the crown, straddling the mitt surface so they read as
        // raised stitching rather than as a decal.
        foreach (Block bar in new[]
        {
            new Block(new Vector3(0.10f, 0.96f, 0.0f), new Vector3(0.16f, 0.20f, 0.58f), lace),
            new Block(new Vector3(0.34f, 0.99f, 0.0f), new Vector3(0.16f, 0.20f, 0.62f), lace),
            new Block(new Vector3(0.58f, 0.95f, 0.0f), new Vector3(0.16f, 0.20f, 0.58f), lace),
        })
        {
            AddBox(surface, bar, r);
        }

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the boxing-glove mesh.");
    }

    private static void AddLathe(
        SurfaceTool surface,
        IReadOnlyList<Ring> rings,
        float scale,
        Transform3D placement)
    {
        for (int ring = 0; ring < rings.Count - 1; ring++)
        {
            for (int segment = 0; segment < RingSegments; segment++)
            {
                float theta0 = Mathf.Tau * segment / RingSegments;
                float theta1 = Mathf.Tau * (segment + 1) / RingSegments;
                AddQuad(
                    surface,
                    placement * RingPoint(rings[ring], theta0, scale),
                    placement * RingPoint(rings[ring + 1], theta0, scale),
                    placement * RingPoint(rings[ring + 1], theta1, scale),
                    placement * RingPoint(rings[ring], theta1, scale),
                    rings[ring].Tint,
                    rings[ring + 1].Tint);
            }
        }
    }

    private static Vector3 RingPoint(Ring ring, float theta, float scale) => new(
        ring.X * scale,
        (ring.CenterY + (Mathf.Cos(theta) * ring.RadiusY)) * scale,
        Mathf.Sin(theta) * ring.RadiusZ * scale);

    private static void AddBox(SurfaceTool surface, Block block, float scale)
    {
        Vector3 half = block.Size * 0.5f * scale;
        Vector3 c = block.Centre * scale;

        Span<Vector3> corner = stackalloc Vector3[8];
        for (int index = 0; index < 8; index++)
        {
            corner[index] = new Vector3(
                c.X + ((index & 1) == 0 ? -half.X : half.X),
                c.Y + ((index & 2) == 0 ? -half.Y : half.Y),
                c.Z + ((index & 4) == 0 ? -half.Z : half.Z));
        }

        AddQuad(surface, corner[0], corner[2], corner[3], corner[1], block.Tint, block.Tint);
        AddQuad(surface, corner[5], corner[7], corner[6], corner[4], block.Tint, block.Tint);
        AddQuad(surface, corner[4], corner[6], corner[2], corner[0], block.Tint, block.Tint);
        AddQuad(surface, corner[1], corner[3], corner[7], corner[5], block.Tint, block.Tint);
        AddQuad(surface, corner[0], corner[1], corner[5], corner[4], block.Tint, block.Tint);
        AddQuad(surface, corner[6], corner[7], corner[3], corner[2], block.Tint, block.Tint);
    }

    private static void AddQuad(
        SurfaceTool surface,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color tintAd,
        Color tintBc)
    {
        AddVertex(surface, a, tintAd);
        AddVertex(surface, b, tintBc);
        AddVertex(surface, c, tintBc);
        AddVertex(surface, a, tintAd);
        AddVertex(surface, c, tintBc);
        AddVertex(surface, d, tintAd);
    }

    private static void AddVertex(SurfaceTool surface, Vector3 point, Color tint)
    {
        surface.SetColor(tint);
        surface.AddVertex(point);
    }
}
