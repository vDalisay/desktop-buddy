using System;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Reusable render-only counterpart for a dynamically attached 2D physics body.
/// The owning composition root feeds one pre-solver snapshot per engine tick;
/// this node interpolates the current source state at render rate and never
/// registers a physics callback or writes to the source transform.
/// </summary>
[GlobalClass]
public partial class Body2DVisual3D : Node3D
{
    private RigidBody2D? _target;
    private IBody2DVisualPulseSource? _pulseSource;
    private MeshInstance3D? _mesh;
    private Vector2 _previousPosition;
    private Vector2 _currentPosition;
    private float _previousRotation;
    private float _currentRotation;
    private float _depthOffset;
    private bool _presentationActive;

    public bool IsInitialized { get; private set; }
    public bool IsAttached => GodotObject.IsInstanceValid(_target);
    public RigidBody2D? Target => IsAttached ? _target : null;
    public MeshInstance3D Mesh => _mesh ?? throw new InvalidOperationException(
        "Body2DVisual3D has not been initialized.");

    public void Initialize(float radius, Color color, float depthOffset)
    {
        if (IsInitialized)
        {
            return;
        }

        if (!float.IsFinite(radius) || radius <= 0.0f ||
            !float.IsFinite(depthOffset))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), "Body2DVisual3D geometry must be finite and positive.");
        }

        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _depthOffset = depthOffset;
        _mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2.0f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(_mesh);
        Visible = false;
        IsInitialized = true;
    }

    public void Attach(RigidBody2D target)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("Body2DVisual3D used before initialization.");
        }

        ArgumentNullException.ThrowIfNull(target);
        if (!GodotObject.IsInstanceValid(target))
        {
            throw new ArgumentException("The visual target must be a live RigidBody2D.", nameof(target));
        }

        if (GodotObject.IsInstanceValid(_target) && _target != target)
        {
            _target!.Visible = true;
        }

        _target = target;
        _pulseSource = target as IBody2DVisualPulseSource;
        SnapSnapshots();
        ApplyPresentationVisibility();
    }

    public void Detach(RigidBody2D target)
    {
        if (!GodotObject.IsInstanceValid(_target) || _target != target)
        {
            return;
        }

        _target!.Visible = true;
        _target = null;
        _pulseSource = null;
        Visible = false;
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        ApplyPresentationVisibility();
    }

    /// <summary>Capture the end of the previous 2D solver step.</summary>
    public void CaptureTickSnapshot()
    {
        if (!GodotObject.IsInstanceValid(_target))
        {
            return;
        }

        _previousPosition = _target!.GlobalPosition;
        _previousRotation = _target.GlobalRotation;
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(_target))
        {
            return;
        }

        _currentPosition = _target!.GlobalPosition;
        _currentRotation = _target.GlobalRotation;
        float fraction = Mathf.Clamp(
            (float)Engine.GetPhysicsInterpolationFraction(), 0.0f, 1.0f);
        Vector2 position2D = _previousPosition.Lerp(_currentPosition, fraction);
        float rotation2D = Mathf.LerpAngle(_previousRotation, _currentRotation, fraction);

        Vector3 position3D = WorldPlaneMapping.To3D(position2D);
        position3D.Z = _depthOffset;
        GlobalPosition = position3D;

        Vector2 visualScale = _pulseSource?.VisualScale2D ?? Vector2.One;
        float visualRotation = _pulseSource?.VisualRotation2D ?? 0.0f;
        GlobalRotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(rotation2D + visualRotation));
        _mesh!.Scale = new Vector3(visualScale.X, visualScale.Y, 1.0f);
    }

    private void SnapSnapshots()
    {
        _currentPosition = _target!.GlobalPosition;
        _currentRotation = _target.GlobalRotation;
        _previousPosition = _currentPosition;
        _previousRotation = _currentRotation;
        Vector3 position = WorldPlaneMapping.To3D(_currentPosition);
        position.Z = _depthOffset;
        GlobalPosition = position;
        GlobalRotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(_currentRotation));
        _mesh!.Scale = Vector3.One;
    }

    private void ApplyPresentationVisibility()
    {
        bool attached = GodotObject.IsInstanceValid(_target);
        Visible = _presentationActive && attached;
        if (attached)
        {
            _target!.Visible = !_presentationActive;
        }
    }
}
