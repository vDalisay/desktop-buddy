using System;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Focused presenter for the one dynamic cursor-tool visual slot. It accepts a
/// profile, asks the internal factory for a visual, and delegates transform
/// tracking to the reusable <see cref="Body2DVisual3D"/>. Composition roots do
/// not branch on tool identity or construct render Resources.
/// </summary>
[GlobalClass]
public partial class CursorToolVisual3D : Node3D
{
    private const float GloveAimTravelThresholdPx = 0.25f;
    private const float SwingGuideOffsetPx = 28.0f;

    private Body2DVisual3D _slot = null!;
    private MeshInstance3D _swingGuide = null!;
    private bool _presentationActive;
    private float _gloveFacingAngle;
    private bool _hasGloveFacing;
    private Vector2 _previousGloveCursor;
    private bool _hasGloveCursorSample;

    public bool IsInitialized { get; private set; }
    public bool IsAttached => IsInitialized && _slot.IsAttached;
    public RigidBody2D? Target => IsInitialized ? _slot.Target : null;
    public MeshInstance3D Mesh => IsInitialized
        ? _slot.Mesh
        : throw new InvalidOperationException("CursorToolVisual3D used before initialization.");
    public bool IsGlintVisible => IsInitialized && _slot.IsGlintVisible;
    public CursorToolVisual3DKind ActiveKind { get; private set; }
    public Body2DVisual3D Slot => _slot;

    // Tests and read-only presentation consumers observe the delegated transform.
    public new Vector3 GlobalRotation => IsInitialized ? _slot.GlobalRotation : Vector3.Zero;

    public void Initialize(CursorToolProfile initialProfile)
    {
        if (IsInitialized)
            return;

        ValidateProfile(initialProfile);
        _slot = new Body2DVisual3D { Name = "DynamicBodyVisualSlot" };
        AddChild(_slot);
        _slot.Initialize(
            initialProfile.Radius,
            initialProfile.VisualColor,
            initialProfile.VisualDepthOffset);
        BuildSwingGuide();
        IsInitialized = true;
        SetProfile(initialProfile);
        Visible = false;
    }

    public void SetProfile(CursorToolProfile profile)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("CursorToolVisual3D used before initialization.");

        ValidateProfile(profile);
        ActiveKind = profile.Visual3DKind;
        _hasGloveFacing = false;
        _hasGloveCursorSample = false;
        _gloveFacingAngle = 0.0f;
        _previousGloveCursor = Vector2.Zero;
        _slot.Mesh.Rotation = Vector3.Zero;
        _swingGuide.Visible = false;

        CursorToolVisual? visual = CursorToolVisualFactory.Create(profile);
        if (visual is null)
        {
            // The original sphere/capsule path remains the exact default.
            _slot.SetGeometry(
                profile.Radius,
                profile.Length,
                profile.VisualColor,
                profile.VisualDepthOffset);
            return;
        }

        _slot.SetVisual(visual.Value.Mesh, visual.Value.Material, profile.VisualDepthOffset);
    }

    public void Attach(RigidBody2D target)
    {
        _slot.Attach(target);
        _hasGloveCursorSample = false;
        if (ActiveKind == CursorToolVisual3DKind.BoxingGlove &&
            target.GetParent() is CursorToolController controller && controller.HasCursor)
        {
            _previousGloveCursor = controller.Cursor;
            _hasGloveCursorSample = true;
        }
        Visible = _presentationActive && _slot.IsAttached;
    }

    public void Detach(RigidBody2D target)
    {
        _slot.Detach(target);
        _hasGloveCursorSample = false;
        _swingGuide.Visible = false;
        Visible = false;
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        _slot.SetPresentationActive(active);
        if (!active && GodotObject.IsInstanceValid(_swingGuide))
            _swingGuide.Visible = false;
        Visible = active && _slot.IsAttached;
    }

    public void CaptureTickSnapshot() => _slot.CaptureTickSnapshot();

    public override void _Process(double delta)
    {
        if (!IsInitialized || Target is not RigidBody2D target ||
            !GodotObject.IsInstanceValid(target) ||
            target.GetParent() is not CursorToolController controller || !controller.HasCursor)
        {
            if (GodotObject.IsInstanceValid(_swingGuide))
                _swingGuide.Visible = false;
            return;
        }

        UpdateSwingGuide(controller);

        if (ActiveKind != CursorToolVisual3DKind.BoxingGlove)
            return;

        // Match the pistol's control convention: facing comes from pointer travel, not from the
        // lagging physical body's velocity. Moving right faces right, moving left faces left, and
        // the last deliberate direction is held while the pointer rests. The body/collider remains
        // free to react to contacts underneath this presentation-only orientation.
        Vector2 cursor = controller.Cursor;
        if (!_hasGloveCursorSample)
        {
            _previousGloveCursor = cursor;
            _hasGloveCursorSample = true;
        }
        else
        {
            Vector2 motion = cursor - _previousGloveCursor;
            if (motion.LengthSquared() >= GloveAimTravelThresholdPx * GloveAimTravelThresholdPx)
            {
                _gloveFacingAngle = motion.Angle();
                _hasGloveFacing = true;
            }
            _previousGloveCursor = cursor;
        }

        if (!_hasGloveFacing)
            return;

        // Body2DVisual3D follows the solver body's rotation. Counter-rotate that local frame and
        // then apply pointer-facing so incidental spin from a punch never turns the glove away.
        float localFacing = _gloveFacingAngle - target.GlobalRotation;
        _slot.Mesh.Rotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(localFacing));
    }

    private void UpdateSwingGuide(CursorToolController controller)
    {
        CursorToolProfile? profile = controller.ActiveProfile;
        bool show = _presentationActive && controller.IsSwingCapable && profile is not null &&
            controller.SwingState is not (ChargedSwingState.Swinging or ChargedSwingState.Recovery);
        if (!show)
        {
            _swingGuide.Visible = false;
            return;
        }

        int sign = controller.SwingDirectionSign < 0 ? -1 : 1;
        Vector2 anchor = controller.Cursor + new Vector2(sign * SwingGuideOffsetPx, 0.0f);
        Vector3 position = WorldPlaneMapping.To3D(anchor);
        position.Z = profile!.VisualDepthOffset + 4.0f;
        _swingGuide.GlobalPosition = position;
        _swingGuide.GlobalRotation = new Vector3(
            0.0f,
            0.0f,
            WorldPlaneMapping.To3DRotationZ(sign < 0 ? Mathf.Pi : 0.0f));
        _swingGuide.Visible = true;
    }

    private void BuildSwingGuide()
    {
        var surface = new SurfaceTool();
        surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        Color fill = new(1.0f, 0.94f, 0.45f, 0.95f);
        AddGuideTriangle(surface, new Vector3(0.0f, -2.2f, 0.0f), new Vector3(11.0f, -2.2f, 0.0f), new Vector3(11.0f, 2.2f, 0.0f), fill);
        AddGuideTriangle(surface, new Vector3(0.0f, -2.2f, 0.0f), new Vector3(11.0f, 2.2f, 0.0f), new Vector3(0.0f, 2.2f, 0.0f), fill);
        AddGuideTriangle(surface, new Vector3(9.0f, -6.2f, 0.0f), new Vector3(19.0f, 0.0f, 0.0f), new Vector3(9.0f, 6.2f, 0.0f), fill);
        ArrayMesh mesh = surface.Commit() ?? throw new InvalidOperationException("Failed to build swing direction guide mesh.");
        var material = new StandardMaterial3D
        {
            ResourceName = "CaptureSwingGuideMaterial",
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _swingGuide = new MeshInstance3D
        {
            Name = "SwingDirectionGuide",
            Mesh = mesh,
            MaterialOverride = material,
            Visible = false,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(_swingGuide);
    }

    private static void AddGuideTriangle(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        surface.SetColor(color);
        surface.AddVertex(a);
        surface.SetColor(color);
        surface.AddVertex(b);
        surface.SetColor(color);
        surface.AddVertex(c);
    }

    private static void ValidateProfile(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("Cursor-tool visual profile must be live.", nameof(profile));
    }
}

internal readonly record struct CursorToolVisual(Mesh Mesh, Material Material);

internal static class CursorToolVisualFactory
{
    public static CursorToolVisual? Create(CursorToolProfile profile)
    {
        ArrayMesh? mesh = profile.Visual3DKind switch
        {
            CursorToolVisual3DKind.LathedBat => BatMeshBuilder.Build(profile),
            CursorToolVisual3DKind.BoxingGlove => BoxingGloveMeshBuilder.Build(profile),
            _ => null,
        };
        if (mesh is null)
            return null;

        string materialName = profile.Visual3DKind == CursorToolVisual3DKind.BoxingGlove
            ? "CapturePolishBoxingGloveMaterial"
            : "ProvisionalLathedBatMaterial";
        var material = new StandardMaterial3D
        {
            ResourceName = materialName,
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = profile.Visual3DKind == CursorToolVisual3DKind.BoxingGlove ? 0.72f : 0.7f,
            Metallic = 0.0f,
        };
        return new CursorToolVisual(mesh, material);
    }
}
