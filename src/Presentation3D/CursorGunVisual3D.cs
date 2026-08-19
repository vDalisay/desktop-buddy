using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The drawn gun, in the frontal 3D presentation. A gun has no physical body to hang a
/// <see cref="Body2DVisual3D"/> slot on — it is a cursor and an aim, not a collider — so
/// this is a focused presenter that follows those two directly rather than forcing the
/// attached-body abstraction around something that never attaches.
///
/// <para>Render-only, like every presenter here: it reads
/// <see cref="CursorGunComponent.Cursor"/> and <see cref="CursorGunComponent.AimForward"/>
/// and writes nothing back. The aim it follows is already smoothed and slewed by the aim
/// model, so there is deliberately no second smoothing layer — a visual that lagged the
/// aim would be showing the player a barrel their shots do not come out of.</para>
///
/// <para>A gun aimed left is rolled 180° around its barrel axis rather than reflected with
/// a negative scale. The roll keeps the grip under the cursor without reversing triangle
/// winding or normals, so the existing lighting rig shades both aim directions equally.</para>
/// </summary>
[GlobalClass]
public partial class CursorGunVisual3D : Node3D
{
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.Ordinal);
    private CursorGunComponent _gun = null!;
    private Node3D _orientation = null!;
    private MeshInstance3D _mesh = null!;
    private MeshInstance3D _pump = null!;
    private MeshInstance3D _flash = null!;
    private StandardMaterial3D _material = null!;
    private GunProfile? _shown;
    private bool _presentationActive;

    public bool IsInitialized { get; private set; }

    /// <summary>The profile whose silhouette is currently built into the mesh.</summary>
    public string? ShownContentId => _shown?.ContentId;

    /// <summary>
    /// Where the drawn barrel mouth is, in 2D world pixels, read back out of the node's
    /// real transform rather than recomputed. A scenario comparing this with where a round
    /// was born is then comparing the shot against the gun the player actually sees.
    /// </summary>
    public Vector2 MuzzlePoint2D => _shown is null
        ? Vector2.Zero
        : WorldPlaneMapping.To2D(
            GlobalTransform * new Vector3(_shown.VisualMuzzleTipPx, 0.0f, 0.0f));

    /// <summary>The direction the drawn barrel points, in 2D world space.</summary>
    public Vector2 Forward2D => Direction2D(Vector3.Right);

    /// <summary>
    /// The direction the drawn grip hangs, in 2D world space. Screen Y grows downward, so
    /// a gun that is the right way up has a positive Y here whichever way it points.
    /// </summary>
    public Vector2 GripDirection2D => Direction2D(_orientation.GlobalTransform.Basis, Vector3.Down);

    /// <summary>
    /// Determinant of the real mesh orientation used by the renderer. It must stay positive:
    /// a negative value means a reflection has inverted the mesh normals and lighting basis.
    /// </summary>
    public float VisualBasisDeterminant => IsInitialized
        ? _orientation.GlobalTransform.Basis.Determinant()
        : 0.0f;

    /// <summary>True while the aim points left and the silhouette is mirrored.</summary>
    public bool IsMirrored { get; private set; }

    /// <summary>True while the blast flare is on screen.</summary>
    public bool IsFlashVisible => IsInitialized && Visible && _flash.Visible;

    public void Initialize(CursorGunComponent gun)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(gun);
        if (!GodotObject.IsInstanceValid(gun))
            throw new ArgumentException("The gun visual requires a live gun component.", nameof(gun));

        _gun = gun;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _material = new StandardMaterial3D
        {
            ResourceName = "ProvisionalGunMaterial",
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = 0.7f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _orientation = new Node3D
        {
            Name = "GunOrientation",
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(_orientation);
        _mesh = new MeshInstance3D
        {
            Name = "GunMesh",
            MaterialOverride = _material,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        _orientation.AddChild(_mesh);
        _pump = new MeshInstance3D
        {
            Name = "ShotgunPump",
            MaterialOverride = _material,
            Visible = false,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        _orientation.AddChild(_pump);
        BuildFlash();

        foreach (GunProfile? profile in gun.Profiles)
        {
            if (GodotObject.IsInstanceValid(profile))
                _meshes[profile!.ContentId] = GunMeshBuilder.Build(profile);
        }

        Visible = false;
        IsInitialized = true;
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        if (!active)
            Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_presentationActive)
            return;

        GunProfile? profile = _gun.ActiveProfile;
        if (!_gun.IsActive || profile is null || _gun.AimForward == Vector2.Zero)
        {
            Visible = false;
            return;
        }

        if (!ReferenceEquals(profile, _shown))
        {
            if (!_meshes.TryGetValue(profile.ContentId, out Mesh? mesh))
            {
                mesh = GunMeshBuilder.Build(profile);
                _meshes[profile.ContentId] = mesh;
            }

            _mesh.Mesh = mesh;
            _pump.Mesh = profile.Visual3DKind == GunVisual3DKind.Shotgun
                ? GunMeshBuilder.BuildShotgunPump(profile)
                : null;
            _shown = profile;
        }

        Vector2 aim = _gun.AimForward;
        Vector3 position = WorldPlaneMapping.To3D(_gun.Cursor + _gun.RecoilOffset2D);
        position.Z = profile.VisualDepthOffset;
        GlobalPosition = position;
        GlobalRotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(aim.Angle()));
        IsMirrored = aim.X < 0.0f;
        Scale = Vector3.One;
        _orientation.Rotation = new Vector3(IsMirrored ? Mathf.Pi : 0.0f, 0.0f, 0.0f);
        _pump.Visible = profile.Visual3DKind == GunVisual3DKind.Shotgun;
        _pump.Position = new Vector3(-_gun.PumpSlideOffsetPx, 0.0f, 0.0f);
        Visible = true;
        UpdateFlash(profile);
    }

    /// <summary>
    /// The blast flare: an additive unshaded star at the barrel mouth that pops down over
    /// the authored ticks, cribbed from the bat's glint. It is driven by the component's
    /// own flash counter, which only a real launch starts, so a dry fire shows nothing.
    /// </summary>
    private void UpdateFlash(GunProfile profile)
    {
        float strength = _gun.MuzzleFlashStrength;
        if (strength <= 0.0f || profile.MuzzleFlashTicks <= 0)
        {
            _flash.Visible = false;
            return;
        }

        _flash.Position = new Vector3(profile.VisualMuzzleTipPx, 0.0f, 0.0f);
        float size = profile.VisualLengthPx * 0.42f * strength * profile.MuzzleFlashScale;
        _flash.Scale = new Vector3(size, size, size);
        _flash.Visible = true;
    }

    private void BuildFlash()
    {
        var material = new StandardMaterial3D
        {
            ResourceName = "ProvisionalMuzzleFlashMaterial",
            AlbedoColor = new Color(1.0f, 0.97f, 0.72f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.90f, 0.48f),
            EmissionEnergyMultiplier = 4.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _flash = new MeshInstance3D
        {
            Name = "MuzzleFlash",
            Mesh = new QuadMesh { Size = Vector2.One },
            MaterialOverride = material,
            Visible = false,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        _orientation.AddChild(_flash);
        var cross = new MeshInstance3D
        {
            Name = "MuzzleFlashCross",
            Mesh = new QuadMesh { Size = new Vector2(2.2f, 0.35f) },
            MaterialOverride = material,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        _flash.AddChild(cross);
    }

    private Vector2 Direction2D(Vector3 localAxis)
        => Direction2D(GlobalTransform.Basis, localAxis);

    private static Vector2 Direction2D(Basis basis, Vector3 localAxis)
    {
        Vector3 world = basis * localAxis;
        Vector2 plane = WorldPlaneMapping.To2D(world);
        return plane.IsZeroApprox() ? Vector2.Zero : plane.Normalized();
    }
}
