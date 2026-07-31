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
/// <para>A gun aimed left is <b>mirrored</b> rather than rotated past vertical. Rotating a
/// side-on gun by 180° stands it on its head, grip in the air; mirroring about the barrel
/// axis keeps the grip under the cursor where a hand would be. The mirror is a negative
/// scale, so the material disables backface culling: a mirrored mesh has inverted winding
/// and would otherwise render inside-out.</para>
/// </summary>
[GlobalClass]
public partial class CursorGunVisual3D : Node3D
{
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.Ordinal);
    private CursorGunComponent _gun = null!;
    private MeshInstance3D _mesh = null!;
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
    public Vector2 GripDirection2D => Direction2D(Vector3.Down);

    /// <summary>True while the aim points left and the silhouette is mirrored.</summary>
    public bool IsMirrored { get; private set; }

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
            // A mirrored gun is a negative-scale gun, which reverses triangle winding.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _mesh = new MeshInstance3D
        {
            Name = "GunMesh",
            MaterialOverride = _material,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(_mesh);

        // Every authored gun's mesh is built once here, on composition, rather than on the
        // tick a player draws one.
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
        // No gun drawn, or one with nowhere to point yet: the aim model owns that state,
        // and a barrel pointing at a direction nobody chose is worse than no barrel.
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
            _shown = profile;
        }

        Vector2 aim = _gun.AimForward;
        Vector3 position = WorldPlaneMapping.To3D(_gun.Cursor);
        position.Z = profile.VisualDepthOffset;
        GlobalPosition = position;
        GlobalRotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(aim.Angle()));
        IsMirrored = aim.X < 0.0f;
        Scale = new Vector3(1.0f, IsMirrored ? -1.0f : 1.0f, 1.0f);
        Visible = true;
    }

    private Vector2 Direction2D(Vector3 localAxis)
    {
        Vector3 world = GlobalTransform.Basis * localAxis;
        Vector2 plane = WorldPlaneMapping.To2D(world);
        return plane.IsZeroApprox() ? Vector2.Zero : plane.Normalized();
    }
}
