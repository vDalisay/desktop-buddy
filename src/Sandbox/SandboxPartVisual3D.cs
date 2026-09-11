using System.Collections.Generic;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Draws the room's built parts as lit 3D shapes in the frontal presentation, so a beam or a wheel
/// reads like the Buddy standing on it rather than a flat sticker (owner note 2026-09-10). The
/// models come from <see cref="SandboxPartLook"/>, the same ones the Build palette previews.
///
/// <para>Render-only: follows each part's 2D body and never writes to it. It keeps running while
/// the room is paused, because Build moves parts with the room paused. The flat body keeps drawing
/// the selection outline and frozen nail over the model.</para>
/// </summary>
public partial class SandboxPartVisual3D : Node3D
{
    /// <summary>Wheels sit in front of the beams they are hinged through, as an axle would show.</summary>
    private const float BoxDepthLane = 0.0f;
    private const float WheelDepthLane = 20.0f;

    private readonly Dictionary<SandboxPartBody, Node3D> _drawn = [];
    private readonly Dictionary<SandboxPartBody, SandboxPartDefinition> _modelOf = [];
    private readonly Dictionary<SandboxPartBody, (Vector2 Position, float Rotation)> _previous = [];
    private readonly List<SandboxPartBody> _gone = [];
    private IReadOnlyDictionary<SandboxPartId, SandboxPartBody> _parts = null!;
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
            _modelOf.Remove(body);
            _previous.Remove(body);
        }

        bool paused = GetTree().Paused;
        foreach (SandboxPartBody body in _parts.Values)
        {
            if (!GodotObject.IsInstanceValid(body) || !body.IsConfigured || !body.IsInsideTree())
                continue;
            // A beam resized in Build is a new definition; its model is rebuilt to match.
            if (_drawn.TryGetValue(body, out Node3D? node) && !ReferenceEquals(_modelOf[body], body.Definition))
            {
                node.QueueFree();
                node = null;
            }
            node ??= Adopt(body);

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
            SandboxPartLook.Pose(node, body.Definition, body.PistonExtension, body.Lit);
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
        Node3D node = SandboxPartLook.Build(body.Definition);
        node.Name = body.Name;
        AddChild(node);
        _drawn[body] = node;
        _modelOf[body] = body.Definition;
        body.DrawsShape = !_active;
        return node;
    }
}
