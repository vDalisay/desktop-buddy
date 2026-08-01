using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room gun silhouettes as vertex-coloured boxes, in the same spirit as
/// <see cref="BatMeshBuilder"/>: no imported art, every dimension derived from the
/// authored <see cref="GunProfile"/>, and one shared envelope predicate the verification
/// can check the result against.
///
/// <para>Local space is the gun's own: the grip sits at the origin, which is where the
/// cursor is, the barrel runs along <b>+X</b>, and the grip hangs toward <b>-Y</b>. The
/// presenter rotates that whole frame to the aim, so nothing here knows which way the
/// player is pointing.</para>
///
/// <para>The silhouettes are deliberately different shapes rather than recolours of one:
/// the Nerf Blaster is chunky, rounded-off and oversized with a wide orange tip ring, the
/// Pistol is a compact slide-and-frame with a raked grip, and the Shotgun is a long pump
/// with a walnut forend and stock. None carries any real-world model's trade dress.</para>
/// </summary>
public static class GunMeshBuilder
{
    /// <summary>Half-height of the envelope, as a fraction of the authored length.</summary>
    public const float EnvelopeHeightFraction = 0.62f;

    /// <summary>Half-depth of the envelope, as a fraction of the authored length.</summary>
    public const float EnvelopeDepthFraction = 0.30f;

    /// <summary>One axis-aligned block of the gun, in local pixels.</summary>
    private readonly record struct Block(Vector3 Centre, Vector3 Size, Color Tint);

    public static ArrayMesh Build(GunProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("A gun mesh requires a live profile.", nameof(profile));

        IReadOnlyList<Block> blocks = profile.Visual3DKind switch
        {
            GunVisual3DKind.NerfBlaster => NerfBlaster(profile),
            GunVisual3DKind.RealPistol => RealPistol(profile),
            GunVisual3DKind.Shotgun => Shotgun(profile),
            _ => throw new ArgumentException(
                $"'{profile.ContentId}' has no authored gun silhouette.", nameof(profile)),
        };

        return Build(blocks);
    }

    /// <summary>The Shotgun's separately animated wooden forend.</summary>
    public static ArrayMesh BuildShotgunPump(GunProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        float length = profile.VisualLengthPx;
        return Build(new[]
        {
            new Block(
                new Vector3(length * 0.60f, -length * 0.09f, 0.0f),
                new Vector3(length * 0.24f, length * 0.13f, length * 0.13f),
                profile.AccentColor),
        });
    }

    private static ArrayMesh Build(IReadOnlyList<Block> blocks)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (Block block in blocks)
            AddBox(surface, block);

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the gun mesh.");
    }

    /// <summary>
    /// The box every vertex of a built gun must lie inside: from the grip at the origin
    /// forward to the authored length, and no taller or deeper than the fractions above.
    /// Shared with verification so the mesh is checked against a stated envelope rather
    /// than against itself.
    /// </summary>
    public static Aabb Envelope(GunProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        float length = profile.VisualLengthPx;
        float height = length * EnvelopeHeightFraction;
        float depth = length * EnvelopeDepthFraction;
        float back = profile.Visual3DKind == GunVisual3DKind.Shotgun ? 0.40f : 0.12f;
        return new Aabb(
            new Vector3(-length * back, -height, -depth),
            new Vector3(length * (1.0f + back), height * 2.0f, depth * 2.0f));
    }

    public static bool IsInsideEnvelope(Vector3 vertex, Aabb envelope, float epsilon = 0.001f)
    {
        if (!vertex.IsFinite())
            return false;

        return vertex.X >= envelope.Position.X - epsilon &&
               vertex.Y >= envelope.Position.Y - epsilon &&
               vertex.Z >= envelope.Position.Z - epsilon &&
               vertex.X <= envelope.End.X + epsilon &&
               vertex.Y <= envelope.End.Y + epsilon &&
               vertex.Z <= envelope.End.Z + epsilon;
    }

    /// <summary>
    /// The toy: a fat rounded body, an oversized barrel, and the wide orange tip that
    /// makes a toy gun legible as one at a glance.
    /// </summary>
    private static List<Block> NerfBlaster(GunProfile profile)
    {
        float length = profile.VisualLengthPx;
        float tip = profile.VisualMuzzleTipPx;
        Color body = profile.MuzzleColor;
        Color accent = profile.AccentColor;
        float barrelBore = length * 0.20f;

        return new List<Block>
        {
            // Body: deliberately bulky, which is the whole point of the silhouette.
            new(
                new Vector3(length * 0.34f, -length * 0.02f, 0.0f),
                new Vector3(length * 0.52f, length * 0.34f, length * 0.26f),
                body),
            // Barrel, centred on the bore line — the axis rounds are really born on.
            new(
                new Vector3(length * 0.72f, 0.0f, 0.0f),
                new Vector3(length * 0.36f, barrelBore, barrelBore),
                body),
            // The orange tip ring, wider than the barrel it caps.
            new(
                new Vector3(tip - (length * 0.04f), 0.0f, 0.0f),
                new Vector3(length * 0.10f, barrelBore * 1.45f, barrelBore * 1.45f),
                accent),
            // Grip, tucked under the body and hanging below the cursor.
            new(
                new Vector3(length * 0.16f, -length * 0.30f, 0.0f),
                new Vector3(length * 0.20f, length * 0.34f, length * 0.20f),
                body),
            // A splash of accent at the butt so the toy palette reads from both ends.
            new(
                new Vector3(length * 0.16f, -length * 0.45f, 0.0f),
                new Vector3(length * 0.21f, length * 0.07f, length * 0.21f),
                accent),
        };
    }

    /// <summary>
    /// The real one: slide over frame, trigger guard, raked grip. Compact where the toy
    /// is bulky, and dark where the toy is bright.
    /// </summary>
    private static List<Block> RealPistol(GunProfile profile)
    {
        float length = profile.VisualLengthPx;
        float tip = profile.VisualMuzzleTipPx;
        Color body = profile.MuzzleColor;
        Color grip = profile.AccentColor;

        return new List<Block>
        {
            // Slide: the long top mass, carrying the bore out to the muzzle.
            new(
                new Vector3(tip * 0.5f, 0.0f, 0.0f),
                new Vector3(tip, length * 0.17f, length * 0.13f),
                body),
            // Frame under the slide, stopping short of the muzzle.
            new(
                new Vector3(tip * 0.42f, -length * 0.11f, 0.0f),
                new Vector3(tip * 0.78f, length * 0.10f, length * 0.11f),
                body),
            // Trigger guard: a bar under the frame rather than a hollow loop, which a
            // box mesh cannot honestly make.
            new(
                new Vector3(length * 0.36f, -length * 0.20f, 0.0f),
                new Vector3(length * 0.22f, length * 0.04f, length * 0.09f),
                body),
            // Grip, raked back and down under the cursor.
            new(
                new Vector3(length * 0.14f, -length * 0.28f, 0.0f),
                new Vector3(length * 0.17f, length * 0.30f, length * 0.11f),
                grip),
            new(
                new Vector3(length * 0.10f, -length * 0.41f, 0.0f),
                new Vector3(length * 0.15f, length * 0.06f, length * 0.11f),
                grip),
        };
    }

    /// <summary>
    /// The Shotgun: a pump silhouette, and deliberately the longest of the three. A single
    /// bore on the <b>y = 0</b> axis — which is where the presenter puts the muzzle flash and
    /// where the profile says rounds are born — with the magazine tube slung under it, a
    /// walnut pump forend around that tube, and a walnut stock and grip carrying the whole
    /// weapon back over the cursor. The wood is the accent colour, so at a glance the
    /// shotgun reads long-and-brown where the pistol reads short-and-black.
    /// </summary>
    private static List<Block> Shotgun(GunProfile profile)
    {
        float length = profile.VisualLengthPx;
        float tip = profile.VisualMuzzleTipPx;
        Color body = profile.MuzzleColor;
        Color wood = profile.AccentColor;
        float barrelBreech = length * 0.30f;

        return new List<Block>
        {
            // Barrel, on the bore line the shot really leaves along.
            new(
                new Vector3((barrelBreech + tip) * 0.5f, 0.0f, 0.0f),
                new Vector3(tip - barrelBreech, length * 0.10f, length * 0.10f),
                body),
            // Magazine tube, slung under the barrel.
            new(
                new Vector3(length * 0.59f, -length * 0.09f, 0.0f),
                new Vector3(length * 0.54f, length * 0.07f, length * 0.07f),
                body),
            // Receiver: the thick block the barrel screws into.
            new(
                new Vector3(length * 0.26f, -length * 0.02f, 0.0f),
                new Vector3(length * 0.24f, length * 0.19f, length * 0.14f),
                body),
            // Trigger guard, a bar rather than a hollow loop, as the pistol's is.
            new(
                new Vector3(length * 0.24f, -length * 0.16f, 0.0f),
                new Vector3(length * 0.15f, length * 0.035f, length * 0.08f),
                body),
            // Stock, doubled lengthwise from the first pass and carried behind the cursor.
            new(
                new Vector3(-length * 0.08f, -length * 0.10f, 0.0f),
                new Vector3(length * 0.56f, length * 0.15f, length * 0.12f),
                wood),
            // Dark butt plate and receiver cap keep the longer stock from reading as a box.
            new(
                new Vector3(-length * 0.35f, -length * 0.10f, 0.0f),
                new Vector3(length * 0.04f, length * 0.18f, length * 0.14f),
                body),
            new(
                new Vector3(length * 0.39f, -length * 0.02f, 0.0f),
                new Vector3(length * 0.035f, length * 0.21f, length * 0.15f),
                body),
            // Grip, hanging below the cursor the way both other guns' do.
            new(
                new Vector3(length * 0.14f, -length * 0.26f, 0.0f),
                new Vector3(length * 0.13f, length * 0.26f, length * 0.11f),
                wood),
        };
    }

    private static void AddBox(SurfaceTool surface, Block block)
    {
        Vector3 half = block.Size * 0.5f;
        Vector3 c = block.Centre;

        // Corners, indexed by (x, y, z) sign bits.
        Span<Vector3> corner = stackalloc Vector3[8];
        for (int index = 0; index < 8; index++)
        {
            corner[index] = new Vector3(
                c.X + ((index & 1) == 0 ? -half.X : half.X),
                c.Y + ((index & 2) == 0 ? -half.Y : half.Y),
                c.Z + ((index & 4) == 0 ? -half.Z : half.Z));
        }

        // Six faces, wound counter-clockwise seen from outside.
        AddQuad(surface, corner[0], corner[2], corner[3], corner[1], block.Tint); // -Z
        AddQuad(surface, corner[5], corner[7], corner[6], corner[4], block.Tint); // +Z
        AddQuad(surface, corner[4], corner[6], corner[2], corner[0], block.Tint); // -X
        AddQuad(surface, corner[1], corner[3], corner[7], corner[5], block.Tint); // +X
        AddQuad(surface, corner[0], corner[1], corner[5], corner[4], block.Tint); // -Y
        AddQuad(surface, corner[6], corner[7], corner[3], corner[2], block.Tint); // +Y
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

    private static void AddVertex(SurfaceTool surface, Vector3 point, Color tint)
    {
        surface.SetColor(tint);
        surface.AddVertex(point);
    }
}
