using System;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Builds the two care-tool cursor visuals in the same 1.5D style as the bat and glove
/// (owner instruction 2026-08-19). Forward is +X, matching the shared cursor-aim convention;
/// units are world pixels, so these line up with the legacy vector drawings.
/// </summary>
public static class CareToolMeshBuilder
{
    private const int RingSegments = 20;

    /// <summary>Grip to vane tip: the whole tool, and the length its dropped collider must be.</summary>
    public static float FeatherLength =>
        CareToolGeometry.StickLength + CareToolGeometry.FerruleLength + CareToolGeometry.PlumeLength;

    private static readonly Color Brass = new("d6a94a");
    private static readonly Color BrassDark = new("b9862c");
    private static readonly Color PlumeLight = new("f7f3e2");
    private static readonly Color PlumeShade = new("cdc4a4");
    private static readonly Color Rachis = new("8a6544");
    private static readonly Color Wood = new("f0b969");
    private static readonly Color WoodTop = new("f6cd8c");
    private static readonly Color WoodShade = new("c8853a");
    private static readonly Color Knob = new("5b4433");
    private static readonly Color KnobDark = new("463427");
    private static readonly Color Bristle = new("6b5340");

    /// <summary>
    /// Feather duster: brass stick from the grip at the origin, a crimped ferrule, and a flat
    /// vane above it. The vane is deliberately thin across Z — a radially symmetric plume reads
    /// as an ice cream cone, which is exactly what the first pass looked like.
    /// </summary>
    /// <param name="worldForm">
    /// True for the copy lying on the floor. The held feather is built grip-at-origin along +X,
    /// because the grip is the pointer; a dropped body is a capsule centred on its own origin with
    /// its long axis along local Y, so the same geometry has to be re-seated onto that axis. Built
    /// the held way, the feather rendered a stick length away from its own collider and clicking
    /// the thing you could see hit empty space (owner report 2026-08-19).
    /// </param>
    public static ArrayMesh BuildFeatherDuster(bool worldForm = false)
    {
        float stick = CareToolGeometry.StickLength;
        float ferrule = CareToolGeometry.FerruleLength;
        float plume = CareToolGeometry.PlumeLength;
        float wide = CareToolGeometry.PlumeHalfWidth;

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        // Brass shaft, slightly tapered toward the grip so it does not read as a pipe.
        AddLathe(surface, Vector3.Right, Vector3.Zero,
        [
            new Ring(0.0f, 1.4f, 1.4f, BrassDark),
            new Ring(2.5f, 2.1f, 2.1f, Brass),
            new Ring(stick * 0.5f, 1.9f, 1.9f, Brass),
            new Ring(stick, 2.0f, 2.0f, Brass),
        ]);

        // Ferrule: the metal cup the vane is crimped into.
        AddLathe(surface, Vector3.Right, Vector3.Zero,
        [
            new Ring(stick - 2.0f, 2.2f, 2.2f, BrassDark),
            new Ring(stick + 1.0f, 3.4f, 3.4f, Brass),
            new Ring(stick + ferrule, 2.4f, 2.4f, BrassDark),
        ]);

        float root = stick + ferrule;

        // The vane: a flat leaf-shaped shell, narrow at the quill, widest a third up, drawn to a
        // point. Stations alternate light and shade so the length bands into something that reads
        // as barbs at cursor scale.
        (float At, float Width, float Thickness)[] vane =
        [
            (0.00f, 0.8f, 0.8f),
            (0.07f, 0.38f, 0.90f),
            (0.17f, 0.68f, 1.00f),
            (0.30f, 0.90f, 1.00f),
            (0.43f, 1.00f, 0.95f),
            (0.56f, 0.97f, 0.88f),
            (0.68f, 0.86f, 0.78f),
            (0.79f, 0.68f, 0.66f),
            (0.89f, 0.45f, 0.50f),
            (0.96f, 0.24f, 0.34f),
            (1.00f, 0.05f, 0.16f),
        ];

        var rings = new Ring[vane.Length];
        for (int index = 0; index < vane.Length; index++)
        {
            (float at, float width, float thickness) = vane[index];
            rings[index] = new Ring(
                root + (plume * at),
                Mathf.Max(0.1f, wide * width),
                Mathf.Max(0.1f, 1.9f * thickness),
                index % 2 == 0 ? PlumeLight : PlumeShade);
        }

        AddLathe(surface, Vector3.Right, Vector3.Zero, rings);

        // The rachis, standing a little proud of both faces so the vane has a spine rather than
        // being one undifferentiated blade.
        AddLathe(surface, Vector3.Right, Vector3.Zero,
        [
            new Ring(root, 1.3f, 2.4f, Rachis),
            new Ring(root + (plume * 0.45f), 1.0f, 2.3f, Rachis),
            new Ring(root + (plume * 0.85f), 0.6f, 1.5f, Rachis),
            new Ring(root + plume, 0.2f, 0.3f, Rachis),
        ]);

        return Commit(surface, worldForm ? "FeatherDusterWorldForm" : "FeatherDuster");
    }

    /// <summary>
    /// Horse brush: an oval wooden block with a flat bristled underside, a domed back, and a
    /// dark knob handle standing on top (owner reference 2026-08-19). Built as an extruded oval
    /// rather than an ellipsoid — a lathe has no flat face to seat bristles on, which is why the
    /// first pass read as a croissant.
    /// </summary>
    public static ArrayMesh BuildBrush()
    {
        const float halfLength = 19.0f;
        const float halfDepth = 10.0f;
        const float bottom = -3.5f;
        const float top = 3.0f;
        const float dome = 3.0f;

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        AddOvalSlab(surface, halfLength, halfDepth, bottom, top, dome);

        // Knob handle: a short tapered post on the back, the darkest thing on the tool.
        AddLathe(surface, Vector3.Up, new Vector3(0.0f, top + dome - 1.0f, 0.0f),
        [
            new Ring(0.0f, 6.0f, 4.6f, KnobDark),
            new Ring(1.6f, 5.4f, 4.2f, Knob),
            new Ring(5.4f, 4.4f, 3.4f, Knob),
            new Ring(6.6f, 3.4f, 2.6f, KnobDark),
        ]);

        // Bristles: a dense, even fringe over the whole flat underside. Even length is what makes
        // it read as a brush; the varied tufts of the first pass read as lumps.
        const int alongLength = 11;
        const int alongDepth = 3;
        for (int lengthIndex = 0; lengthIndex < alongLength; lengthIndex++)
        {
            float u = (lengthIndex + 0.5f) / alongLength;
            float x = Mathf.Lerp(-halfLength + 2.5f, halfLength - 2.5f, u);
            // Rows shrink toward the ends of the oval so the fringe follows the outline.
            float span = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (((u * 2.0f) - 1.0f) * ((u * 2.0f) - 1.0f))));
            for (int depthIndex = 0; depthIndex < alongDepth; depthIndex++)
            {
                float v = (depthIndex + 0.5f) / alongDepth;
                float z = Mathf.Lerp(-halfDepth + 2.5f, halfDepth - 2.5f, v) * span;
                AddLathe(surface, Vector3.Down, new Vector3(x, bottom + 0.5f, z),
                [
                    new Ring(0.0f, 1.5f, 1.5f, Bristle),
                    new Ring(8.0f, 1.3f, 1.3f, Bristle),
                    new Ring(9.8f, 0.7f, 0.7f, Bristle),
                ]);
            }
        }

        return Commit(surface, "HorseBrush");
    }

    /// <summary>One cross-section: distance along the axis and the two half-widths across it.</summary>
    private readonly record struct Ring(float Distance, float RadiusA, float RadiusB, Color Fill);

    /// <summary>
    /// An oval block: straight sides, a flat bottom face bristles can sit on, and a domed back.
    /// Long axis is X, short axis Z, thickness along Y.
    /// </summary>
    private static void AddOvalSlab(
        SurfaceTool surface,
        float halfLength,
        float halfDepth,
        float bottom,
        float top,
        float dome)
    {
        Vector3 Rim(float theta, float scale, float y) =>
            new(Mathf.Cos(theta) * halfLength * scale, y, Mathf.Sin(theta) * halfDepth * scale);

        for (int segment = 0; segment < RingSegments; segment++)
        {
            float theta0 = Mathf.Tau * segment / RingSegments;
            float theta1 = Mathf.Tau * (segment + 1) / RingSegments;

            // Straight side wall.
            AddQuad(
                surface,
                Rim(theta0, 1.0f, bottom),
                Rim(theta0, 1.0f, top),
                Rim(theta1, 1.0f, top),
                Rim(theta1, 1.0f, bottom),
                Wood);

            // Flat underside, wound so it faces down.
            AddTriangle(
                surface,
                new Vector3(0.0f, bottom, 0.0f),
                Rim(theta1, 1.0f, bottom),
                Rim(theta0, 1.0f, bottom),
                WoodShade);

            // Domed back in two courses so the highlight has somewhere to sit.
            AddQuad(
                surface,
                Rim(theta0, 1.0f, top),
                Rim(theta0, 0.72f, top + (dome * 0.7f)),
                Rim(theta1, 0.72f, top + (dome * 0.7f)),
                Rim(theta1, 1.0f, top),
                WoodTop);
            AddTriangle(
                surface,
                new Vector3(0.0f, top + dome, 0.0f),
                Rim(theta0, 0.72f, top + (dome * 0.7f)),
                Rim(theta1, 0.72f, top + (dome * 0.7f)),
                WoodTop);
        }
    }

    /// <summary>
    /// Sweeps <paramref name="rings"/> along <paramref name="axis"/> from <paramref name="origin"/>
    /// and caps both ends, so each call contributes one closed shell.
    /// </summary>
    private static void AddLathe(SurfaceTool surface, Vector3 axis, Vector3 origin, Ring[] rings)
    {
        Vector3 forward = axis.Normalized();
        // Any vector not parallel to the axis gives a stable frame. For the flat shells it must
        // be a fixed one: RadiusA has to stay in the screen plane and RadiusB across it, or the
        // vane would be thin in the wrong direction.
        Vector3 reference = Mathf.Abs(forward.Z) < 0.9f ? Vector3.Back : Vector3.Up;
        // RadiusA must land in the screen plane and RadiusB along the camera axis, or the flat
        // vane would be built edge-on to the camera and vanish.
        Vector3 sideA = forward.Cross(reference).Normalized();
        Vector3 sideB = forward.Cross(sideA).Normalized();

        for (int ring = 0; ring < rings.Length - 1; ring++)
        {
            for (int segment = 0; segment < RingSegments; segment++)
            {
                float theta0 = Mathf.Tau * segment / RingSegments;
                float theta1 = Mathf.Tau * (segment + 1) / RingSegments;
                AddQuad(
                    surface,
                    Point(rings[ring], theta0),
                    Point(rings[ring + 1], theta0),
                    Point(rings[ring + 1], theta1),
                    Point(rings[ring], theta1),
                    rings[ring].Fill);
            }
        }

        AddCap(rings[0], reversed: true);
        AddCap(rings[^1], reversed: false);
        return;

        Vector3 Point(Ring ring, float theta) =>
            origin + (forward * ring.Distance) +
            (sideA * (Mathf.Cos(theta) * ring.RadiusA)) +
            (sideB * (Mathf.Sin(theta) * ring.RadiusB));

        void AddCap(Ring ring, bool reversed)
        {
            if (ring.RadiusA <= 0.05f || ring.RadiusB <= 0.05f)
                return;

            Vector3 centre = origin + (forward * ring.Distance);
            for (int segment = 0; segment < RingSegments; segment++)
            {
                Vector3 a = Point(ring, Mathf.Tau * segment / RingSegments);
                Vector3 b = Point(ring, Mathf.Tau * (segment + 1) / RingSegments);
                AddTriangle(surface, centre, reversed ? b : a, reversed ? a : b, ring.Fill);
            }
        }
    }

    private static void AddQuad(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color fill)
    {
        AddTriangle(surface, a, b, c, fill);
        AddTriangle(surface, a, c, d, fill);
    }

    private static void AddTriangle(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Color fill)
    {
        surface.SetColor(fill);
        surface.AddVertex(a);
        surface.SetColor(fill);
        surface.AddVertex(b);
        surface.SetColor(fill);
        surface.AddVertex(c);
    }

    /// <summary>
    /// Re-seats the grip-at-origin, +X-forward feather onto a dropped capsule: a quarter turn so
    /// the tool runs along local Y, and a shift so its midpoint is the body origin. A rotation
    /// rather than an axis swap, so the winding — and every normal — survives.
    /// </summary>
    private static ArrayMesh ToWorldForm(ArrayMesh mesh)
    {
        var transform = new Transform3D(
            new Basis(Vector3.Back, Mathf.Pi * 0.5f),
            new Vector3(0.0f, -FeatherLength * 0.5f, 0.0f));
        var source = new MeshDataTool();
        source.CreateFromSurface(mesh, 0);
        for (int index = 0; index < source.GetVertexCount(); index++)
            source.SetVertex(index, transform * source.GetVertex(index));

        var rebuilt = new ArrayMesh();
        source.CommitToSurface(rebuilt);
        rebuilt.ResourceName = mesh.ResourceName;
        return rebuilt;
    }

    private static ArrayMesh Commit(SurfaceTool surface, string name)
    {
        surface.GenerateNormals();
        ArrayMesh mesh = surface.Commit()
            ?? throw new InvalidOperationException($"Failed to build the {name} care-tool mesh.");
        mesh.ResourceName = name;
        return name.EndsWith("WorldForm", StringComparison.Ordinal) ? ToWorldForm(mesh) : mesh;
    }
}
