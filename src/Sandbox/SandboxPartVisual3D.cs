using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Draws the room's built parts as lit 3D shapes in the frontal presentation, so a beam or a wheel
/// reads like the Buddy standing on it rather than a flat sticker (owner note 2026-09-10): a box for
/// beams, blocks and plates, a rubber cylinder with a spoke for a wheel.
///
/// <para>Render-only: follows each part's 2D body and never writes to it. It keeps running while
/// the room is paused, because Build moves parts with the room paused. The flat body keeps drawing
/// the selection outline and the frozen nail over the shape.</para>
/// </summary>
public partial class SandboxPartVisual3D : Node3D
{
    /// <summary>Wheels sit in front of the beams they are hinged through, as an axle would show.</summary>
    private const float BoxDepthLane = 0.0f;
    private const float WheelDepthLane = 20.0f;
    private const float MinimumBoxDepth = 24.0f;
    private const float WheelThickness = 16.0f;

    private readonly Dictionary<SandboxPartBody, Node3D> _drawn = [];
    private readonly Dictionary<SemanticDefinitionId, (Mesh Mesh, Material Material, Mesh? Spoke)> _looks = [];
    private readonly Dictionary<SandboxPartBody, (Vector2 Position, float Rotation)> _previous = [];
    private readonly List<SandboxPartBody> _gone = [];
    private IReadOnlyDictionary<SandboxPartId, SandboxPartBody> _parts = null!;
    private StandardMaterial3D? _spokeMaterial;
    private bool _active;

    /// <summary>How many parts are drawn as shapes right now, for verification.</summary>
    public int DrawnCount => _drawn.Count;

    public void Configure(IReadOnlyDictionary<SandboxPartId, SandboxPartBody> parts)
    {
        _parts = parts;
        ProcessMode = ProcessModeEnum.Always;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
    }

    public void SetPresentationActive(bool active)
    {
        _active = active;
        Visible = active;
        foreach (SandboxPartBody body in _drawn.Keys)
        {
            if (GodotObject.IsInstanceValid(body))
                body.DrawsShape = !active;
        }
    }

    public override void _Process(double delta)
    {
        if (_parts is null)
            return;

        _gone.Clear();
        foreach ((SandboxPartBody body, Node3D node) in _drawn)
        {
            if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree())
            {
                _gone.Add(body);
                node.QueueFree();
            }
        }
        foreach (SandboxPartBody body in _gone)
        {
            _drawn.Remove(body);
            _previous.Remove(body);
        }

        bool paused = GetTree().Paused;
        foreach (SandboxPartBody body in _parts.Values)
        {
            if (!GodotObject.IsInstanceValid(body) || !body.IsConfigured || !body.IsInsideTree())
                continue;
            if (!_drawn.TryGetValue(body, out Node3D? node))
                node = Adopt(body);

            // Blended between physics ticks while the room plays, as Body2DVisual3D does; exact
            // while Build drags a part in a paused room, where no tick will catch the blend up.
            Vector2 at = body.GlobalPosition;
            float angle = body.GlobalRotation;
            if (!paused && _previous.TryGetValue(body, out (Vector2 Position, float Rotation) before))
            {
                float fraction = Mathf.Clamp((float)Engine.GetPhysicsInterpolationFraction(), 0.0f, 1.0f);
                at = before.Position.Lerp(at, fraction);
                angle = Mathf.LerpAngle(before.Rotation, angle, fraction);
            }
            Vector3 position = WorldPlaneMapping.To3D(at);
            position.Z = body.Definition.Shape == SandboxPartShape.Circle ? WheelDepthLane : BoxDepthLane;
            node.GlobalPosition = position;
            node.GlobalRotation = new Vector3(0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(angle));
        }
    }

    /// <summary>The end of the previous solver step, captured before this one runs.</summary>
    public override void _PhysicsProcess(double delta)
    {
        foreach (SandboxPartBody body in _drawn.Keys)
        {
            if (GodotObject.IsInstanceValid(body))
                _previous[body] = (body.GlobalPosition, body.GlobalRotation);
        }
    }

    private Node3D Adopt(SandboxPartBody body)
    {
        (Mesh mesh, Material material, Mesh? spoke) = LookFor(body.Definition);
        var node = new Node3D { Name = body.Name };
        var shape = new MeshInstance3D { Name = "Shape", Mesh = mesh, MaterialOverride = material };
        if (body.Definition.Shape == SandboxPartShape.Circle)
            shape.RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f); // cylinder axis along the camera
        node.AddChild(shape);
        if (spoke is not null)
            node.AddChild(new MeshInstance3D { Name = "Hub", Mesh = spoke, MaterialOverride = _spokeMaterial });
        AddChild(node);
        _drawn[body] = node;
        body.DrawsShape = !_active;
        return node;
    }

    private (Mesh, Material, Mesh?) LookFor(SandboxPartDefinition definition)
    {
        if (_looks.TryGetValue(definition.Id, out var look))
            return look;

        StandardMaterial3D material = Lit(SandboxPartBody.FillFor(definition.Material),
            definition.Material == SandboxPartMaterial.Metal ? 0.45f : 0.85f);

        if (definition.Shape == SandboxPartShape.Circle)
        {
            // A rounded tyre around a metal hub: seen head-on, the torus is what reads as round.
            _spokeMaterial ??= Lit(SandboxPartBody.FillFor(SandboxPartMaterial.Metal), 0.45f);
            float radius = definition.Radius;
            float tube = radius * 0.24f;
            look = (
                new TorusMesh { InnerRadius = radius - tube * 2.0f, OuterRadius = radius, Rings = 32, RingSegments = 12 },
                material,
                Hub(radius - tube * 2.0f, WheelThickness * 0.6f));
        }
        else
        {
            float depth = Math.Max(MinimumBoxDepth, Math.Min(definition.Width, definition.Height));
            look = (BevelledBox(definition.Width, definition.Height, depth), material, null);
        }

        _looks[definition.Id] = look;
        return look;
    }

    private static StandardMaterial3D Lit(Color color, float roughness) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        Roughness = roughness,
    };

    /// <summary>The bar across the hub, so a rolling wheel reads as rolling rather than sliding.</summary>
    private static BoxMesh Hub(float innerRadius, float thickness) =>
        new() { Size = new Vector3(innerRadius * 2.0f, innerRadius * 0.45f, thickness) };

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
}
