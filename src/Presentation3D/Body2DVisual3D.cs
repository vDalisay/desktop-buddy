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
    private MeshInstance3D? _glintHorizontal;
    private MeshInstance3D? _glintDiagonal;
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
            Mesh = BuildMesh(radius, 0.0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(_mesh);
        BuildGlint();
        Visible = false;
        IsInitialized = true;
    }

    /// <summary>
    /// Re-shapes the render body for a source whose geometry is not fixed for the
    /// run — the cursor-tool slot, where the collider that attaches depends on which
    /// tool is selected. <paramref name="length"/> of <c>0</c> is a sphere; a longer
    /// body becomes a capsule along its local Y, matching CapsuleShape2D so the
    /// render body and the collider agree about which way the long axis points.
    /// </summary>
    public void SetGeometry(float radius, float length, Color color, float depthOffset)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("Body2DVisual3D used before initialization.");
        }

        if (!float.IsFinite(radius) || radius <= 0.0f ||
            !float.IsFinite(length) || length < 0.0f ||
            !float.IsFinite(depthOffset))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), "Body2DVisual3D geometry must be finite and positive.");
        }

        _depthOffset = depthOffset;
        _mesh!.Mesh = BuildMesh(radius, length);
        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
    }

    /// <summary>
    /// Injects an authored visual into this dynamic body slot. Unlike
    /// <see cref="SetGeometry"/>, this seam does not replace the supplied mesh or
    /// force an unshaded material, so focused presenters can provide a genuinely
    /// lit shape while the scalar sphere/capsule path stays unchanged.
    /// </summary>
    public void SetVisual(Mesh mesh, Material material, float depthOffset)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("Body2DVisual3D used before initialization.");
        }

        if (!GodotObject.IsInstanceValid(mesh) ||
            !GodotObject.IsInstanceValid(material))
        {
            throw new ArgumentException(
                "Body2DVisual3D requires live mesh and material Resources.");
        }

        if (!float.IsFinite(depthOffset))
        {
            throw new ArgumentOutOfRangeException(
                nameof(depthOffset), "Body2DVisual3D depth must be finite.");
        }

        _depthOffset = depthOffset;
        _mesh!.Mesh = mesh;
        _mesh.MaterialOverride = material;
    }

    private static Mesh BuildMesh(float radius, float length) =>
        length > radius * 2.0f
            ? new CapsuleMesh { Radius = radius, Height = length }
            : new SphereMesh { Radius = radius, Height = radius * 2.0f };

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
        Vector2 visualOffset = _pulseSource?.VisualOffset2D ?? Vector2.Zero;
        Vector2 position2D = _previousPosition.Lerp(_currentPosition, fraction) + visualOffset;
        float rotation2D = Mathf.LerpAngle(_previousRotation, _currentRotation, fraction);

        Vector3 position3D = WorldPlaneMapping.To3D(position2D);
        position3D.Z = _depthOffset;
        GlobalPosition = position3D;

        Vector2 visualScale = _pulseSource?.VisualScale2D ?? Vector2.One;
        float visualRotation = _pulseSource?.VisualRotation2D ?? 0.0f;
        GlobalRotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(rotation2D + visualRotation));
        _mesh!.Scale = new Vector3(visualScale.X, visualScale.Y, 1.0f);
        UpdateGlint();
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
        UpdateGlint();
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

    /// <summary>Whether the tracked source's one-shot tip glimmer is currently visible.</summary>
    public bool IsGlintVisible =>
        _glintHorizontal?.Visible == true && _glintHorizontal.Scale.X > 0.0f;

    private void BuildGlint()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.94f, 0.58f, 0.92f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.84f, 0.30f),
            EmissionEnergyMultiplier = 1.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var quad = new QuadMesh { Size = new Vector2(1.0f, 0.22f) };

        _glintHorizontal = new MeshInstance3D
        {
            Name = "ChargeGlintHorizontal",
            Mesh = quad,
            MaterialOverride = material,
            Visible = false,
        };
        _glintDiagonal = new MeshInstance3D
        {
            Name = "ChargeGlintDiagonal",
            Mesh = quad,
            MaterialOverride = material,
            Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f),
            Visible = false,
        };
        AddChild(_glintHorizontal);
        AddChild(_glintDiagonal);
    }

    private void UpdateGlint()
    {
        if (_glintHorizontal is null || _glintDiagonal is null || _pulseSource is null)
        {
            return;
        }

        float strength = _pulseSource.VisualGlintStrength;
        bool visible = strength > 0.0f;
        _glintHorizontal.Visible = visible;
        _glintDiagonal.Visible = visible;
        if (!visible)
        {
            return;
        }

        Vector2 local = _pulseSource.VisualGlintLocalPosition;
        Vector3 position = WorldPlaneMapping.To3D(local);
        position.Z = 0.75f;
        float size = _pulseSource.VisualGlintSizePx * strength;
        Vector3 scale = new(size, size, 1.0f);
        _glintHorizontal.Position = position;
        _glintDiagonal.Position = position;
        _glintHorizontal.Scale = scale;
        _glintDiagonal.Scale = scale;
    }
}
