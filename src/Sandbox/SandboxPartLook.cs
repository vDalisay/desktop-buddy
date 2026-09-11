using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Sandbox;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// What a built part looks like, in one place, so the room and the Build palette's preview cannot
/// drift apart (owner note 2026-09-11: the Timer previewed differently from the placed Timer). The
/// 3D model is the part's body; the flat drawing is what sits on top of it — outlines and each
/// device's face — or the whole part when the 3D presentation is off.
///
/// <para>A Piston reads like a Minecraft piston: a cobblestone base, a plank head, and an iron rod
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
    private const float MinimumBoxDepth = 24.0f;
    private const float WheelThickness = 16.0f;

    private static readonly Color WoodFill = new("b4813f");
    private static readonly Color MetalFill = new("9aa6b4");
    private static readonly Color RubberFill = new("3a3f47");
    private static readonly Color StoneFill = new("8c8c8c");
    private static readonly Color StoneDark = new("6a6a6a");
    private static readonly Color PlankLine = new("7a5424");
    private static readonly Color RodFill = new("c8c8c8");
    private static readonly Color Outline = new("2a2118");
    private static readonly Color ButtonRed = new("c0392b");
    private static readonly Color Paper = new("ffffff");
    private static readonly Color LampOff = new("6b6552");
    private static readonly Color LampOn = new("ffd84a");
    private static readonly Color LampGlow = new(1.0f, 0.85f, 0.3f, 0.28f);

    private static readonly Dictionary<SemanticDefinitionId, Mesh[]> Meshes = [];
    private static readonly Dictionary<Color, StandardMaterial3D> Materials = [];

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
        Mesh[] meshes = MeshesFor(definition);
        var node = new Node3D();
        if (definition.Shape == SandboxPartShape.Circle)
        {
            // A rounded tyre around a metal hub: seen head-on, the torus is what reads as round.
            node.AddChild(new MeshInstance3D
            {
                Name = "Shape", Mesh = meshes[0], MaterialOverride = Material(FillFor(definition.Material), 0.85f),
                RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f), // cylinder axis along the camera
            });
            node.AddChild(new MeshInstance3D { Name = "Hub", Mesh = meshes[1], MaterialOverride = Material(MetalFill, 0.45f) });
        }
        else if (definition.Device == SandboxDeviceKind.Piston)
        {
            node.AddChild(new MeshInstance3D { Name = "Base", Mesh = meshes[0], MaterialOverride = Material(StoneFill, 0.95f) });
            node.AddChild(new MeshInstance3D { Name = "Rod", Mesh = meshes[1], MaterialOverride = Material(RodFill, 0.4f), Visible = false });
            node.AddChild(new MeshInstance3D { Name = "Head", Mesh = meshes[2], MaterialOverride = Material(WoodFill, 0.85f) });
            Pose(node, definition, 0.0f);
        }
        else
        {
            node.AddChild(new MeshInstance3D
            {
                Name = "Shape", Mesh = meshes[0],
                MaterialOverride = Material(FillFor(definition.Material),
                    definition.Material == SandboxPartMaterial.Metal ? 0.45f : 0.85f),
            });
        }
        return node;
    }

    /// <summary>Moves a Piston model's head and rod to <paramref name="extension"/>; nothing else moves.</summary>
    public static void Pose(Node3D model, SandboxPartDefinition definition, float extension)
    {
        if (definition.Device != SandboxDeviceKind.Piston)
            return;
        Rect2 baseRect = PistonBase(definition);
        Rect2 head = PistonHead(definition, extension);
        // Part space is y-down; 3D is y-up.
        model.GetNode<Node3D>("Base").Position = new Vector3(0.0f, -baseRect.GetCenter().Y, 0.0f);
        model.GetNode<Node3D>("Head").Position = new Vector3(0.0f, -head.GetCenter().Y, 0.0f);
        var rod = model.GetNode<Node3D>("Rod");
        float length = baseRect.Position.Y - head.End.Y;
        rod.Visible = length > 0.5f;
        if (rod.Visible)
        {
            rod.Position = new Vector3(0.0f, -(baseRect.Position.Y + head.End.Y) * 0.5f, 0.0f);
            rod.Scale = new Vector3(1.0f, length, 1.0f);
        }
    }

    private static Mesh[] MeshesFor(SandboxPartDefinition definition)
    {
        if (Meshes.TryGetValue(definition.Id, out Mesh[]? cached))
            return cached;

        float depth = Math.Max(MinimumBoxDepth, Math.Min(definition.Width, definition.Height));
        Mesh[] meshes;
        if (definition.Shape == SandboxPartShape.Circle)
        {
            float radius = definition.Radius;
            float tube = radius * 0.24f;
            float inner = radius - tube * 2.0f;
            meshes =
            [
                new TorusMesh { InnerRadius = inner, OuterRadius = radius, Rings = 32, RingSegments = 12 },
                // The bar across the hub, so a rolling wheel reads as rolling rather than sliding.
                new BoxMesh { Size = new Vector3(inner * 2.0f, inner * 0.45f, WheelThickness * 0.6f) },
            ];
        }
        else if (definition.Device == SandboxDeviceKind.Piston)
        {
            Rect2 baseRect = PistonBase(definition);
            meshes =
            [
                BevelledBox(definition.Width, baseRect.Size.Y, depth),
                new BoxMesh { Size = new Vector3(definition.Width * 0.25f, 1.0f, depth * 0.4f) }, // scaled to length
                BevelledBox(definition.Width, PistonHeadThickness, depth),
            ];
        }
        else
        {
            meshes = [BevelledBox(definition.Width, definition.Height, depth)];
        }
        Meshes[definition.Id] = meshes;
        return meshes;
    }

    private static StandardMaterial3D Material(Color color, float roughness)
    {
        if (!Materials.TryGetValue(color, out StandardMaterial3D? material))
        {
            Materials[color] = material = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                Roughness = roughness,
            };
        }
        return material;
    }

    /// <summary>
    /// A box whose front edges are chamfered. The frontal camera only ever sees the front, so a
    /// plain box is a flat rectangle; the lit chamfers are what give it thickness. Back faces are
    /// never seen and are left out.
    /// </summary>
    private static ArrayMesh BevelledBox(float width, float height, float depth)
    {
        float x = width * 0.5f;
        float y = height * 0.5f;
        float z = depth * 0.5f;
        float bevel = Math.Min(Math.Min(x, y) * 0.45f, 6.0f);
        float inX = x - bevel;
        float inY = y - bevel;
        float edgeZ = z - bevel;

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 normal = (b - a).Cross(c - a).Normalized();
            bool counterClockwise = normal.Dot((a + b + c + d) * 0.25f) > 0.0f;
            if (!counterClockwise)
                normal = -normal; // every face here points away from the centre
            // Godot's front faces wind clockwise as seen from outside.
            Vector3[] order = counterClockwise ? [a, c, b, a, d, c] : [a, b, c, a, c, d];
            foreach (Vector3 vertex in order)
            {
                surface.SetNormal(normal);
                surface.AddVertex(vertex);
            }
        }

        Quad(new(-inX, -inY, z), new(inX, -inY, z), new(inX, inY, z), new(-inX, inY, z));
        // Chamfers from the front face out to the full outline.
        Quad(new(-inX, inY, z), new(inX, inY, z), new(x, y, edgeZ), new(-x, y, edgeZ));
        Quad(new(-inX, -inY, z), new(inX, -inY, z), new(x, -y, edgeZ), new(-x, -y, edgeZ));
        Quad(new(inX, -inY, z), new(inX, inY, z), new(x, y, edgeZ), new(x, -y, edgeZ));
        Quad(new(-inX, -inY, z), new(-inX, inY, z), new(-x, y, edgeZ), new(-x, -y, edgeZ));
        // Sides back to the rear.
        Quad(new(-x, y, edgeZ), new(x, y, edgeZ), new(x, y, -z), new(-x, y, -z));
        Quad(new(-x, -y, edgeZ), new(x, -y, edgeZ), new(x, -y, -z), new(-x, -y, -z));
        Quad(new(x, -y, edgeZ), new(x, y, edgeZ), new(x, y, -z), new(x, -y, -z));
        Quad(new(-x, -y, edgeZ), new(-x, y, edgeZ), new(-x, y, -z), new(-x, -y, -z));
        return surface.Commit();
    }

    // ---- 2D ----------------------------------------------------------------------------------

    /// <summary>
    /// Draws the part in part space on <paramref name="canvas"/>: its fill when
    /// <paramref name="drawsShape"/> (the 3D model is off), then always its outline and device face.
    /// </summary>
    public static void Draw(CanvasItem canvas, SandboxPartDefinition definition, bool drawsShape, bool lit, float extension)
    {
        if (definition.Shape == SandboxPartShape.Circle)
        {
            if (drawsShape)
            {
                canvas.DrawCircle(Vector2.Zero, definition.Radius, FillFor(definition.Material), true, -1.0f, true);
                // A spoke, so a rolling wheel reads as rolling rather than sliding.
                canvas.DrawLine(Vector2.Zero, new Vector2(definition.Radius, 0.0f), Outline, OutlineWidth, true);
            }
            canvas.DrawArc(Vector2.Zero, definition.Radius, 0.0f, Mathf.Tau, 32, Outline, OutlineWidth, true);
            return;
        }

        if (definition.Device == SandboxDeviceKind.Piston)
        {
            DrawPiston(canvas, definition, drawsShape, extension);
            return;
        }

        var rect = new Rect2(-definition.Width * 0.5f, -definition.Height * 0.5f, definition.Width, definition.Height);
        if (drawsShape)
            canvas.DrawRect(rect, FillFor(definition.Material), filled: true);
        canvas.DrawRect(rect, Outline, filled: false, OutlineWidth);
        DrawDeviceFace(canvas, definition, lit);
    }

    private static void DrawPiston(CanvasItem canvas, SandboxPartDefinition definition, bool drawsShape, float extension)
    {
        Rect2 baseRect = PistonBase(definition);
        Rect2 head = PistonHead(definition, extension);
        float rodWidth = definition.Width * 0.25f;
        var rod = new Rect2(-rodWidth * 0.5f, head.End.Y, rodWidth, baseRect.Position.Y - head.End.Y);
        if (rod.Size.Y > 0.5f)
        {
            if (drawsShape)
                canvas.DrawRect(rod, RodFill, filled: true);
            canvas.DrawRect(rod, Outline, filled: false, 1.5f);
        }

        if (drawsShape)
        {
            canvas.DrawRect(baseRect, StoneFill, filled: true);
            canvas.DrawRect(head, WoodFill, filled: true);
        }
        // Cobbles on the base, a grain line on the plank: what makes it a piston, not a block.
        float w = baseRect.Size.X;
        float h = baseRect.Size.Y;
        Vector2 o = baseRect.Position;
        foreach ((float x, float y, float cw, float ch) in new[]
                 { (0.12f, 0.15f, 0.30f, 0.26f), (0.55f, 0.10f, 0.32f, 0.30f), (0.08f, 0.58f, 0.36f, 0.28f), (0.54f, 0.56f, 0.34f, 0.30f) })
        {
            canvas.DrawRect(new Rect2(o + new Vector2(x * w, y * h), new Vector2(cw * w, ch * h)), StoneDark, filled: true);
        }
        canvas.DrawLine(new Vector2(head.Position.X + 3.0f, head.GetCenter().Y), new Vector2(head.End.X - 3.0f, head.GetCenter().Y),
            PlankLine, 1.0f, true);
        canvas.DrawRect(baseRect, Outline, filled: false, OutlineWidth);
        canvas.DrawRect(head, Outline, filled: false, OutlineWidth);
    }

    /// <summary>A Button's red cap, a Timer's clock face, a Lamp's bulb.</summary>
    private static void DrawDeviceFace(CanvasItem canvas, SandboxPartDefinition definition, bool lit)
    {
        float halfWidth = definition.Width * 0.5f;
        float halfHeight = definition.Height * 0.5f;
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
                canvas.DrawLine(Vector2.Zero, new Vector2(0.0f, -face * 0.75f), Outline, 1.5f, true);
                canvas.DrawLine(Vector2.Zero, new Vector2(face * 0.5f, 0.0f), Outline, 1.5f, true);
                break;
            case SandboxDeviceKind.Lamp:
                float bulb = halfWidth * 0.8f;
                var centre = new Vector2(0.0f, -halfHeight * 0.25f);
                if (lit)
                    canvas.DrawCircle(centre, bulb * 2.2f, LampGlow, true, -1.0f, true);
                canvas.DrawCircle(centre, bulb, lit ? LampOn : LampOff, true, -1.0f, true);
                canvas.DrawArc(centre, bulb, 0.0f, Mathf.Tau, 20, Outline, 1.5f, true);
                break;
        }
    }
}
