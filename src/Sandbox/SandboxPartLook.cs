using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Sandbox;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// What a built part looks like, in one place, so the room and the Build palette's preview cannot
/// drift apart (owner note 2026-09-11: the Timer previewed differently from the placed Timer).
///
/// <para>The 3D model is the whole look, built like the tools are (owner note 2026-09-11: "look at
/// the bat, it's way more 3D"): rounded, smoothly shaded shapes that catch the room's light, with a
/// device's working bits modelled rather than drawn on — a Button's red cap, a Timer's dial and
/// hands, a Lamp's glass bulb that glows and lights what is near it, a Piston's rod. The flat
/// drawing is only the fallback for when the 3D presentation is off.</para>
///
/// <para>A Piston reads like a Minecraft piston: a stone base, a plank head, and a metal rod
/// between them when the head is out. Its head is a real collision shape on
/// <see cref="SandboxPartBody"/>; the look only follows it.</para>
/// </summary>
public static class SandboxPartLook
{
    /// <summary>A Piston's head, the plank slab on top that does the pushing.</summary>
    public const float PistonHeadThickness = 8.0f;

    /// <summary>How far a Piston's head travels when out.</summary>
    public const float PistonReach = 24.0f;

    private const float OutlineWidth = 2.0f;
    private const float MinimumDepth = 24.0f;
    private const float WheelThickness = 16.0f;
    private const int BandSteps = 4;

    private static readonly Color WoodFill = new("b4813f");
    private static readonly Color MetalFill = new("8f9bab");
    private static readonly Color RubberFill = new("3a3f47");
    private static readonly Color StoneFill = new("8e8e8a");
    private static readonly Color DeviceFill = new("6f7c8c");
    private static readonly Color RodFill = new("d0d4d8");
    private static readonly Color Outline = new("2a2118");
    private static readonly Color ButtonRed = new("d0392b");
    private static readonly Color Paper = new("f4f1e8");
    private static readonly Color Ink = new("2a2118");
    private static readonly Color BulbOff = new("b3ad93");
    private static readonly Color BulbOn = new("ffd24a");
    private static readonly Color LampLight = new("ffd27a");

    private static readonly Dictionary<(float, float, float, float), ArrayMesh> RoundedBoxes = [];
    private static readonly Dictionary<(Color, float, float), StandardMaterial3D> Materials = [];
    private static StandardMaterial3D? _bulbOn;

    public static Color OutlineColor => Outline;

    public static Color FillFor(SandboxPartMaterial material) => material switch
    {
        SandboxPartMaterial.Metal => MetalFill,
        SandboxPartMaterial.Rubber => RubberFill,
        _ => WoodFill,
    };

    /// <summary>A Piston's base in part space: everything below the head.</summary>
    public static Rect2 PistonBase(SandboxPartDefinition definition)
    {
        float halfWidth = definition.Width * 0.5f;
        float halfHeight = definition.Height * 0.5f;
        return new Rect2(-halfWidth, -halfHeight + PistonHeadThickness,
            definition.Width, definition.Height - PistonHeadThickness);
    }

    /// <summary>A Piston's head in part space, <paramref name="extension"/> of the way out (0..1).</summary>
    public static Rect2 PistonHead(SandboxPartDefinition definition, float extension)
    {
        float halfWidth = definition.Width * 0.5f;
        float top = -definition.Height * 0.5f - PistonReach * Mathf.Clamp(extension, 0.0f, 1.0f);
        return new Rect2(-halfWidth, top, definition.Width, PistonHeadThickness);
    }

    // ---- 3D ----------------------------------------------------------------------------------

    /// <summary>A fresh model of the part, centred on the part's origin in 3D world units (= px).</summary>
    public static Node3D Build(SandboxPartDefinition definition)
    {
        var node = new Node3D();
        float depth = Math.Max(MinimumDepth, Math.Min(definition.Width, definition.Height));
        float halfWidth = definition.Width * 0.5f;
        float halfHeight = definition.Height * 0.5f;

        switch (definition.Device)
        {
            case SandboxDeviceKind.Piston:
            {
                Rect2 baseRect = PistonBase(definition);
                Add(node, "Base", RoundedBox(baseRect.Size.X, baseRect.Size.Y, depth, 7.0f), Material(StoneFill, 0.95f), FromPart(baseRect.GetCenter()));
                Add(node, "Rod", new CylinderMesh { TopRadius = definition.Width * 0.11f, BottomRadius = definition.Width * 0.11f, Height = 1.0f, RadialSegments = 16, Rings = 1 },
                    Material(RodFill, 0.3f, 0.7f), Vector3.Zero).Visible = false;
                Add(node, "Head", RoundedBox(definition.Width, PistonHeadThickness, depth, 3.8f), Material(WoodFill, 0.8f), Vector3.Zero);
                Pose(node, definition, 0.0f, false);
                return node;
            }
            case SandboxDeviceKind.Button:
                Add(node, "Shape", RoundedBox(definition.Width, definition.Height, depth, 6.0f), Material(DeviceFill, 0.5f, 0.2f), Vector3.Zero);
                // The cap stands on top, where the flat look drew it.
                Add(node, "Cap", RoundedBox(halfWidth * 1.1f, 8.0f, depth * 0.6f, 3.5f), Material(ButtonRed, 0.35f),
                    FromPart(new Vector2(0.0f, -halfHeight - 2.0f)));
                return node;
            case SandboxDeviceKind.Timer:
            {
                Add(node, "Shape", RoundedBox(definition.Width, definition.Height, depth, 9.0f), Material(DeviceFill, 0.5f, 0.2f), Vector3.Zero);
                float face = Math.Min(halfWidth, halfHeight) * 0.72f;
                float front = depth * 0.5f;
                Add(node, "Dial", new CylinderMesh { TopRadius = face, BottomRadius = face, Height = 3.0f, RadialSegments = 32, Rings = 1 },
                    Material(Paper, 0.6f), new Vector3(0.0f, 0.0f, front)).RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f);
                Add(node, "HourHand", new BoxMesh { Size = new Vector3(face * 0.5f, 2.0f, 1.5f) }, Material(Ink, 0.6f),
                    new Vector3(face * 0.25f, 0.0f, front + 2.0f));
                Add(node, "MinuteHand", new BoxMesh { Size = new Vector3(2.0f, face * 0.75f, 1.5f) }, Material(Ink, 0.6f),
                    new Vector3(0.0f, face * 0.375f, front + 2.0f));
                return node;
            }
            case SandboxDeviceKind.Lamp:
            {
                // A metal foot in the bottom third, a glass bulb above it.
                float footHeight = definition.Height * 0.38f;
                Add(node, "Foot", RoundedBox(definition.Width, footHeight, depth, 5.0f), Material(DeviceFill, 0.5f, 0.2f),
                    FromPart(new Vector2(0.0f, halfHeight - footHeight * 0.5f)));
                float bulb = Math.Min(halfWidth, (definition.Height - footHeight) * 0.5f) * 0.95f;
                Vector3 bulbAt = FromPart(new Vector2(0.0f, halfHeight - footHeight - bulb * 0.8f));
                Add(node, "Bulb", new SphereMesh { Radius = bulb, Height = bulb * 2.0f, RadialSegments = 24, Rings = 12 },
                    Material(BulbOff, 0.2f), bulbAt);
                // The room is measured in pixels, so Godot's inverse-square falloff would spend the
                // whole light in the first few pixels; no falloff exponent, and the range does the fade.
                node.AddChild(new OmniLight3D
                {
                    Name = "Glow",
                    LightColor = LampLight,
                    LightEnergy = 2.0f,
                    OmniRange = 180.0f,
                    OmniAttenuation = 0.0f,
                    Position = bulbAt + new Vector3(0.0f, 0.0f, 30.0f),
                    Visible = false,
                });
                return node;
            }
        }

        if (definition.Shape == SandboxPartShape.Circle)
        {
            // A rounded tyre around a metal hub: seen head-on, the torus is what reads as round.
            float radius = definition.Radius;
            float tube = radius * 0.24f;
            float inner = radius - tube * 2.0f;
            Add(node, "Shape", new TorusMesh { InnerRadius = inner, OuterRadius = radius, Rings = 32, RingSegments = 12 },
                Material(RubberFill, 0.85f), Vector3.Zero).RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f);
            // The bar across the hub, so a rolling wheel reads as rolling rather than sliding.
            Add(node, "Hub", RoundedBox(inner * 2.0f, inner * 0.45f, WheelThickness * 0.6f, 2.0f), Material(MetalFill, 0.35f, 0.5f), Vector3.Zero);
            return node;
        }

        bool metal = definition.Material == SandboxPartMaterial.Metal;
        float corner = Math.Min(definition.Width, definition.Height) * 0.32f;
        Add(node, "Shape", RoundedBox(definition.Width, definition.Height, depth, corner),
            Material(FillFor(definition.Material), metal ? 0.45f : 0.8f, metal ? 0.25f : 0.0f), Vector3.Zero);
        return node;
    }

    /// <summary>Brings a model up to the part's live state: a Piston's head and rod, a Lamp's light.</summary>
    public static void Pose(Node3D model, SandboxPartDefinition definition, float extension, bool lit)
    {
        switch (definition.Device)
        {
            case SandboxDeviceKind.Piston:
                Rect2 baseRect = PistonBase(definition);
                Rect2 head = PistonHead(definition, extension);
                model.GetNode<Node3D>("Head").Position = FromPart(head.GetCenter());
                var rod = model.GetNode<Node3D>("Rod");
                float length = baseRect.Position.Y - head.End.Y + 2.0f;   // tucked into both ends
                rod.Visible = extension > 0.001f;
                if (rod.Visible)
                {
                    rod.Position = FromPart(new Vector2(0.0f, (baseRect.Position.Y + head.End.Y) * 0.5f));
                    rod.Scale = new Vector3(1.0f, length, 1.0f);
                }
                break;
            case SandboxDeviceKind.Lamp:
                var bulb = model.GetNode<MeshInstance3D>("Bulb");
                Material wanted = lit ? BulbOnMaterial() : Material(BulbOff, 0.2f);
                if (!ReferenceEquals(bulb.MaterialOverride, wanted))
                    bulb.MaterialOverride = wanted;
                model.GetNode<Node3D>("Glow").Visible = lit;
                break;
        }
    }

    private static MeshInstance3D Add(Node3D parent, string name, Mesh mesh, Material material, Vector3 position)
    {
        var instance = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material, Position = position };
        parent.AddChild(instance);
        return instance;
    }

    /// <summary>Part space is y-down; 3D is y-up.</summary>
    private static Vector3 FromPart(Vector2 point) => new(point.X, -point.Y, 0.0f);

    private static StandardMaterial3D Material(Color color, float roughness, float metallic = 0.0f)
    {
        if (!Materials.TryGetValue((color, roughness, metallic), out StandardMaterial3D? material))
        {
            Materials[(color, roughness, metallic)] = material = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                Roughness = roughness,
                Metallic = metallic,
            };
        }
        return material;
    }

    private static StandardMaterial3D BulbOnMaterial() => _bulbOn ??= new StandardMaterial3D
    {
        AlbedoColor = BulbOn,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        Roughness = 0.15f,
        EmissionEnabled = true,
        Emission = BulbOn,
        EmissionEnergyMultiplier = 0.9f,
    };

    /// <summary>
    /// A box with every edge and corner rounded to <paramref name="radius"/> and smooth normals, so
    /// the room's light rolls over its edges the way it does over the bat. Built as a subdivided box
    /// whose points are pulled onto the rounded surface: the flat middles stay flat, the bands near
    /// each edge curve.
    /// </summary>
    public static ArrayMesh RoundedBox(float width, float height, float depth, float radius)
    {
        var key = (width, height, depth, radius);
        if (RoundedBoxes.TryGetValue(key, out ArrayMesh? cached))
            return cached;

        var half = new Vector3(width, height, depth) * 0.5f;
        float r = Math.Clamp(radius, 0.01f, Math.Min(half.X, Math.Min(half.Y, half.Z)));
        Vector3 inner = half - new Vector3(r, r, r);

        float[] Samples(float h, float i)
        {
            var samples = new float[BandSteps * 2 + 2];
            for (int k = 0; k <= BandSteps; k++)
            {
                samples[k] = -h + (h - i) * k / BandSteps;
                samples[BandSteps + 1 + k] = i + (h - i) * k / BandSteps;
            }
            return samples;
        }
        float[][] axes = [Samples(half.X, inner.X), Samples(half.Y, inner.Y), Samples(half.Z, inner.Z)];

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;
            foreach (float sign in new[] { -1.0f, 1.0f })
            {
                int count = axes[u].Length;
                var points = new Vector3[count, count];
                var normals = new Vector3[count, count];
                for (int a = 0; a < count; a++)
                {
                    for (int b = 0; b < count; b++)
                    {
                        var onBox = new Vector3();
                        onBox[axis] = sign * half[axis];
                        onBox[u] = axes[u][a];
                        onBox[v] = axes[v][b];
                        Vector3 core = onBox.Clamp(-inner, inner);
                        Vector3 outward = onBox - core;
                        if (outward.LengthSquared() < 1e-8f)
                        {
                            outward = new Vector3();
                            outward[axis] = sign;
                        }
                        Vector3 normal = outward.Normalized();
                        points[a, b] = core + normal * r;
                        normals[a, b] = normal;
                    }
                }

                for (int a = 0; a < count - 1; a++)
                {
                    for (int b = 0; b < count - 1; b++)
                    {
                        Triangle(surface, points, normals, (a, b), (a + 1, b), (a + 1, b + 1));
                        Triangle(surface, points, normals, (a, b), (a + 1, b + 1), (a, b + 1));
                    }
                }
            }
        }
        ArrayMesh mesh = surface.Commit();
        RoundedBoxes[key] = mesh;
        return mesh;
    }

    private static void Triangle(SurfaceTool surface, Vector3[,] points, Vector3[,] normals,
        (int A, int B) i, (int A, int B) j, (int A, int B) k)
    {
        Vector3 p0 = points[i.A, i.B];
        Vector3 p1 = points[j.A, j.B];
        Vector3 p2 = points[k.A, k.B];
        Vector3 face = (p1 - p0).Cross(p2 - p0);
        if (face.LengthSquared() < 1e-10f)
            return; // collapsed where two samples met on a corner
        Vector3 outward = normals[i.A, i.B] + normals[j.A, j.B] + normals[k.A, k.B];
        // Godot's front faces wind clockwise as seen from outside.
        (int A, int B)[] order = face.Dot(outward) > 0.0f ? [i, k, j] : [i, j, k];
        foreach ((int a, int b) in order)
        {
            surface.SetNormal(normals[a, b]);
            surface.AddVertex(points[a, b]);
        }
    }

    // ---- 2D ----------------------------------------------------------------------------------

    /// <summary>
    /// The flat look, drawn in part space on <paramref name="canvas"/> only while the 3D
    /// presentation is off (<paramref name="drawsShape"/>); with 3D on, the model is the whole look.
    /// </summary>
    public static void Draw(CanvasItem canvas, SandboxPartDefinition definition, bool drawsShape, bool lit, float extension)
    {
        if (!drawsShape)
            return;

        if (definition.Shape == SandboxPartShape.Circle)
        {
            canvas.DrawCircle(Vector2.Zero, definition.Radius, FillFor(definition.Material), true, -1.0f, true);
            // A spoke, so a rolling wheel reads as rolling rather than sliding.
            canvas.DrawLine(Vector2.Zero, new Vector2(definition.Radius, 0.0f), Outline, OutlineWidth, true);
            canvas.DrawArc(Vector2.Zero, definition.Radius, 0.0f, Mathf.Tau, 32, Outline, OutlineWidth, true);
            return;
        }

        if (definition.Device == SandboxDeviceKind.Piston)
        {
            Rect2 baseRect = PistonBase(definition);
            Rect2 head = PistonHead(definition, extension);
            float rodWidth = definition.Width * 0.22f;
            var rod = new Rect2(-rodWidth * 0.5f, head.End.Y, rodWidth, baseRect.Position.Y - head.End.Y);
            if (rod.Size.Y > 0.5f)
            {
                canvas.DrawRect(rod, RodFill, filled: true);
                canvas.DrawRect(rod, Outline, filled: false, 1.5f);
            }
            canvas.DrawRect(baseRect, StoneFill, filled: true);
            canvas.DrawRect(head, WoodFill, filled: true);
            canvas.DrawRect(baseRect, Outline, filled: false, OutlineWidth);
            canvas.DrawRect(head, Outline, filled: false, OutlineWidth);
            return;
        }

        float halfWidth = definition.Width * 0.5f;
        float halfHeight = definition.Height * 0.5f;
        var rect = new Rect2(-halfWidth, -halfHeight, definition.Width, definition.Height);
        canvas.DrawRect(rect, definition.Device == SandboxDeviceKind.None ? FillFor(definition.Material) : DeviceFill, filled: true);
        canvas.DrawRect(rect, Outline, filled: false, OutlineWidth);
        switch (definition.Device)
        {
            case SandboxDeviceKind.Button:
                var cap = new Rect2(-halfWidth * 0.55f, -halfHeight - 6.0f, halfWidth * 1.1f, 6.0f);
                canvas.DrawRect(cap, ButtonRed, filled: true);
                canvas.DrawRect(cap, Outline, filled: false, 1.5f);
                break;
            case SandboxDeviceKind.Timer:
                float face = Math.Min(halfWidth, halfHeight) * 0.72f;
                canvas.DrawCircle(Vector2.Zero, face, Paper, true, -1.0f, true);
                canvas.DrawArc(Vector2.Zero, face, 0.0f, Mathf.Tau, 24, Outline, 1.5f, true);
                canvas.DrawLine(Vector2.Zero, new Vector2(0.0f, -face * 0.75f), Ink, 1.5f, true);
                canvas.DrawLine(Vector2.Zero, new Vector2(face * 0.5f, 0.0f), Ink, 1.5f, true);
                break;
            case SandboxDeviceKind.Lamp:
                float bulb = halfWidth * 0.8f;
                var centre = new Vector2(0.0f, -halfHeight * 0.25f);
                canvas.DrawCircle(centre, bulb, lit ? BulbOn : BulbOff, true, -1.0f, true);
                canvas.DrawArc(centre, bulb, 0.0f, Mathf.Tau, 20, Outline, 1.5f, true);
                break;
        }
    }
}
