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
    private const float SwingGuideOffsetPx = 28.0f;

    private Body2DVisual3D _slot = null!;
    private MeshInstance3D _swingGuide = null!;
    private bool _presentationActive;
    private float _gloveFacingAngle;
    private bool _hasGloveFacing;

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
        ResetGloveAim();
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
        ResetGloveAim();
        Visible = _presentationActive && _slot.IsAttached;
    }

    public void Detach(RigidBody2D target)
    {
        _slot.Detach(target);
        ResetGloveAim();
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

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInitialized || ActiveKind != CursorToolVisual3DKind.BoxingGlove ||
            Target is not RigidBody2D target || !GodotObject.IsInstanceValid(target) ||
            target.GetParent() is not CursorToolController controller || !controller.HasCursor)
        {
            return;
        }

        // The controller owns this direction, because it is not only a drawing: the wind-up
        // punch travels along it (owner instruction 2026-08-22), and a facing the presentation
        // derived for itself could point somewhere the punch does not go.
        if (!controller.HasToolFacing)
            return;

        _gloveFacingAngle = controller.ToolFacingAngle;
        _hasGloveFacing = true;
    }

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

        if (ActiveKind != CursorToolVisual3DKind.BoxingGlove || !_hasGloveFacing)
            return;

        // The desired direction now comes from the same CursorAim state machine the pistol uses.
        // Counter-rotate the circular solver body's incidental spin so only the presentation faces
        // along that aim; collision, punch velocity and damage remain untouched.
        float localFacing = _gloveFacingAngle - target.GlobalRotation;
        _slot.Mesh.Rotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(localFacing));
    }

    private void ResetGloveAim()
    {
        _gloveFacingAngle = 0.0f;
        _hasGloveFacing = false;
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
        // A dropped gun/sprayer borrows the very mesh it was drawn with while equipped, so
        // putting one down changes where it is, not what it looks like.
        ArrayMesh? mesh = profile.WorldDropGunVisual is not null &&
                          GodotObject.IsInstanceValid(profile.WorldDropGunVisual)
            ? GunMeshBuilder.BuildCentred(profile.WorldDropGunVisual)
            : profile.WorldDropSprayerVisual is not null &&
              GodotObject.IsInstanceValid(profile.WorldDropSprayerVisual)
                ? SprayerMeshBuilder.Build(profile.WorldDropSprayerVisual)
                : profile.Visual3DKind switch
                {
                    CursorToolVisual3DKind.LathedBat => BatMeshBuilder.Build(profile),
                    CursorToolVisual3DKind.Sword => SwordMeshBuilder.Build(profile),
                    CursorToolVisual3DKind.BoxingGlove => BoxingGloveMeshBuilder.Build(profile),
                    // Only ever reached for the copy on the floor, which is why it is the
                    // world form: the held feather is drawn by CareToolVisual3D.
                    CursorToolVisual3DKind.FeatherDuster =>
                        CareToolMeshBuilder.BuildFeatherDuster(worldForm: true),
                    _ => null,
                };
        if (mesh is null)
            return null;

        // The sword is the only tool here made of steel, so it is the only one that gets a
        // metallic response; everything else keeps the matte look it shipped with.
        bool steel = profile.Visual3DKind == CursorToolVisual3DKind.Sword;
        string materialName = profile.Visual3DKind switch
        {
            CursorToolVisual3DKind.BoxingGlove => "CapturePolishBoxingGloveMaterial",
            CursorToolVisual3DKind.Sword => "SteamDemoSwordMaterial",
            _ => "ProvisionalLathedBatMaterial",
        };
        var material = new StandardMaterial3D
        {
            ResourceName = materialName,
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = profile.Visual3DKind switch
            {
                CursorToolVisual3DKind.BoxingGlove => 0.72f,
                CursorToolVisual3DKind.Sword => 0.26f,
                _ => 0.7f,
            },
            Metallic = steel ? 0.75f : 0.0f,
        };
        return new CursorToolVisual(mesh, material);
    }
}
