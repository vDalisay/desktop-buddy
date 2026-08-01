using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the clean-room flamethrower silhouette as vertex-coloured boxes, in exactly the
/// spirit of <see cref="GunMeshBuilder"/> and <see cref="BatMeshBuilder"/>: no imported art,
/// every dimension derived from the authored <see cref="FireSprayerProfile"/>, and one shared
/// envelope predicate the verification can check the result against.
///
/// <para>Local space is the weapon's own, the same convention the guns use: the grip sits at
/// the origin, which is where the cursor is, the barrel runs along <b>+X</b>, and the grip
/// hangs toward <b>-Y</b>. The presenter rotates that whole frame to the aim, so nothing here
/// knows which way the player is pointing.</para>
///
/// <para>The silhouette has to read apart from the two guns at a glance, and it does it with
/// shape rather than with colour: a fat pressure canister slung above and behind the grip, a
/// slim wand running well forward of it, and a flared nozzle ring at the tip. Where a pistol
/// is a solid mass along the bore line, the flamethrower is a heavy back end and a thin front
/// one — which is what makes it legible even as a small silhouette on a cursor.</para>
/// </summary>
public static class SprayerMeshBuilder
{
    /// <summary>Half-height of the envelope, as a fraction of the authored length.</summary>
    public const float EnvelopeHeightFraction = 0.70f;

    /// <summary>Half-depth of the envelope, as a fraction of the authored length.</summary>
    public const float EnvelopeDepthFraction = 0.36f;

    /// <summary>One axis-aligned block of the weapon, in local pixels.</summary>
    private readonly record struct Block(Vector3 Centre, Vector3 Size, Color Tint);

    public static ArrayMesh Build(FireSprayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("A sprayer mesh requires a live profile.", nameof(profile));

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (Block block in Flamethrower(profile))
            AddBox(surface, block);

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException(
            "SurfaceTool failed to build the sprayer mesh.");
    }

    /// <summary>
    /// The box every vertex of a built sprayer must lie inside. Taller and deeper than the
    /// guns' envelope because the canister genuinely is: stating it here rather than
    /// measuring the mesh against itself is what makes the check worth running.
    /// </summary>
    public static Aabb Envelope(FireSprayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        float length = profile.VisualLengthPx;
        float height = length * EnvelopeHeightFraction;
        float depth = length * EnvelopeDepthFraction;
        return new Aabb(
            new Vector3(-length * 0.30f, -height, -depth),
            new Vector3(length * 1.42f, height * 2.0f, depth * 2.0f));
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
    /// Canister, yoke, receiver, wand, and nozzle ring, plus the pilot light that says at a
    /// glance this is the thing that makes fire.
    /// </summary>
    private static List<Block> Flamethrower(FireSprayerProfile profile)
    {
        float length = profile.VisualLengthPx;
        float tip = profile.VisualMuzzleTipPx;
        Color body = profile.BodyColor;
        Color accent = profile.AccentColor;
        Color flame = profile.FlameCoreColor;
        float wandBore = length * 0.13f;

        return new List<Block>
        {
            // Pressure canister: slung behind and above the grip, and the heaviest mass in
            // the silhouette. This is the block that stops it reading as a pistol.
            new(
                new Vector3(-length * 0.11f, length * 0.10f, 0.0f),
                new Vector3(length * 0.34f, length * 0.52f, length * 0.34f),
                body),
            // Its collar and its base cap, in accent, so the canister reads as a tank with
            // ends rather than as a plain slab.
            new(
                new Vector3(-length * 0.11f, length * 0.36f, 0.0f),
                new Vector3(length * 0.24f, length * 0.08f, length * 0.24f),
                accent),
            new(
                new Vector3(-length * 0.11f, -length * 0.15f, 0.0f),
                new Vector3(length * 0.28f, length * 0.07f, length * 0.28f),
                accent),
            // The yoke that carries the canister forward onto the receiver.
            new(
                new Vector3(length * 0.06f, length * 0.10f, 0.0f),
                new Vector3(length * 0.22f, length * 0.13f, length * 0.14f),
                body),
            // Receiver: the block the hand is really on, at the cursor.
            new(
                new Vector3(length * 0.18f, 0.0f, 0.0f),
                new Vector3(length * 0.30f, length * 0.20f, length * 0.18f),
                body),
            // Wand: deliberately slim and long, running well past where a pistol's slide
            // would stop.
            new(
                new Vector3((tip + (length * 0.30f)) * 0.5f, 0.0f, 0.0f),
                new Vector3(tip - (length * 0.02f), wandBore, wandBore),
                body),
            // Flared nozzle ring at the mouth, wider than the wand it caps.
            new(
                new Vector3(tip - (length * 0.05f), 0.0f, 0.0f),
                new Vector3(length * 0.09f, wandBore * 1.9f, wandBore * 1.9f),
                accent),
            // Pilot light: a small hot bead just under the nozzle. One emissive-coloured
            // block is all it takes to say what this weapon does.
            new(
                new Vector3(tip - (length * 0.16f), -wandBore * 1.1f, 0.0f),
                new Vector3(length * 0.07f, length * 0.05f, length * 0.05f),
                flame),
            // Grip, raked back and down under the cursor, on the guns' convention.
            new(
                new Vector3(length * 0.14f, -length * 0.28f, 0.0f),
                new Vector3(length * 0.18f, length * 0.34f, length * 0.14f),
                accent),
            // Trigger bar under the receiver — a bar rather than a hollow loop, which a box
            // mesh cannot honestly make.
            new(
                new Vector3(length * 0.32f, -length * 0.17f, 0.0f),
                new Vector3(length * 0.20f, length * 0.04f, length * 0.09f),
                body),
        };
    }

    private static void AddBox(SurfaceTool surface, Block block)
    {
        Vector3 half = block.Size * 0.5f;
        Vector3 c = block.Centre;

        Span<Vector3> corner = stackalloc Vector3[8];
        for (int index = 0; index < 8; index++)
        {
            corner[index] = new Vector3(
                c.X + ((index & 1) == 0 ? -half.X : half.X),
                c.Y + ((index & 2) == 0 ? -half.Y : half.Y),
                c.Z + ((index & 4) == 0 ? -half.Z : half.Z));
        }

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
