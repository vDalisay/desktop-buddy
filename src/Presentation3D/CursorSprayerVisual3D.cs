using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The drawn flamethrower and its stream, in the frontal 3D presentation (owner feedback
/// 2026-08-01). Built on exactly the <see cref="CursorGunVisual3D"/> shape, because the
/// sprayer is the same kind of thing: a cursor and an aim with no collider to hang a
/// <see cref="Body2DVisual3D"/> slot on.
///
/// <para>Render-only. It reads <see cref="FireSprayerComponent.Cursor"/>,
/// <see cref="FireSprayerComponent.AimForward"/> and the pooled droplets, and writes nothing
/// back. The aim it follows is already smoothed and slewed by the shared aim model, so there
/// is deliberately no second smoothing layer.</para>
///
/// <para>The stream is a <b>mist</b> rather than a row of pellets: one soft, additive,
/// semi-transparent puff rides each live droplet, born small and hot at the nozzle and
/// swelling and cooling toward soot as the droplet ages. The puffs of neighbouring droplets
/// overlap, so what the player sees is a billowing smoky column even though the physics is
/// still the same handful of tiny circles. The puff pool is preallocated at initialize,
/// never on a firing tick, and honours the reduced-particles setting through the same
/// <see cref="SprayDropletBody.DrawEnabled"/> flag the component already sets.</para>
/// </summary>
[GlobalClass]
public partial class CursorSprayerVisual3D : Node3D
{
    private readonly List<MeshInstance3D> _puffs = new();
    private readonly List<ShaderMaterial> _puffMaterials = new();

    private FireSprayerComponent _sprayer = null!;
    private FireSprayerProfile _profile = null!;
    private EffectsSettings _settings = EffectsSettings.Default;
    private Node3D _orientation = null!;
    private Node3D _stream = null!;
    private MeshInstance3D _mesh = null!;
    private MeshInstance3D _canister = null!;
    private MeshInstance3D _pilot = null!;
    private StandardMaterial3D _material = null!;
    private StandardMaterial3D _pilotMaterial = null!;
    private bool _presentationActive;
    private int _ticks;

    public bool IsInitialized { get; private set; }

    /// <summary>True while the flamethrower silhouette is on screen.</summary>
    public bool IsWeaponVisible => IsInitialized && Visible && _mesh.Visible;

    /// <summary>Mist puffs currently drawn — the reduced-particles oracle for the stream.</summary>
    public int VisiblePuffCount { get; private set; }

    /// <summary>Largest presentation-only upward lift in the current cloud.</summary>
    public float MaxPuffRisePx { get; private set; }

    /// <summary>True when the stream uses the procedural fire/smoke shader.</summary>
    public bool UsesCloudShader => _puffMaterials.Count > 0;

    /// <summary>True when the separate cylindrical fuel tank is part of the visible model.</summary>
    public bool IsCanisterVisible => IsWeaponVisible && _canister.Visible;

    public float CanisterDepthOffset => IsInitialized ? _canister.Position.Z : float.NaN;

    public float CanisterDiameterPx => IsInitialized && _canister.Mesh is CapsuleMesh capsule
        ? capsule.Radius * 2.0f
        : 0.0f;

    /// <summary>
    /// Where the drawn nozzle mouth is, in 2D world pixels, read back out of the node's real
    /// transform rather than recomputed, so a check comparing it with where droplets are born
    /// is comparing the stream against the weapon the player actually sees.
    /// </summary>
    public Vector2 NozzlePoint2D => !IsInitialized
        ? Vector2.Zero
        : WorldPlaneMapping.To2D(
            GlobalTransform * new Vector3(_profile.VisualMuzzleTipPx, 0.0f, 0.0f));

    /// <summary>The direction the drawn wand points, in 2D world space.</summary>
    public Vector2 Forward2D => Direction2D(GlobalTransform.Basis, Vector3.Right);

    /// <summary>
    /// Determinant of the real mesh orientation used by the renderer. It must stay positive:
    /// a negative value means a reflection has inverted the mesh normals and lighting basis.
    /// </summary>
    public float VisualBasisDeterminant => IsInitialized
        ? _orientation.GlobalTransform.Basis.Determinant()
        : 0.0f;

    /// <summary>True while the aim points left and the silhouette is rolled.</summary>
    public bool IsMirrored { get; private set; }

    public void Initialize(FireSprayerComponent sprayer, FireSprayerProfile profile)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(sprayer);
        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(sprayer) || !GodotObject.IsInstanceValid(profile))
        {
            throw new ArgumentException(
                "The sprayer visual requires a live component and profile.", nameof(sprayer));
        }

        _sprayer = sprayer;
        _profile = profile;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        _material = new StandardMaterial3D
        {
            ResourceName = "ProvisionalSprayerMaterial",
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = 0.65f,
            Metallic = 0.0f,
            // Readable from either side of the camera, like the gun silhouettes.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        _orientation = new Node3D
        {
            Name = "SprayerOrientation",
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(_orientation);
        _mesh = new MeshInstance3D
        {
            Name = "SprayerMesh",
            Mesh = SprayerMeshBuilder.Build(profile),
            MaterialOverride = _material,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        _orientation.AddChild(_mesh);
        BuildCanister();
        BuildPilotLight();

        // The stream hangs off the root rather than off the rolled orientation: a puff is
        // positioned in world space on the droplet it rides, and inheriting the weapon's roll
        // would drag the whole plume round with the aim.
        _stream = new Node3D { Name = "SprayerMist" };
        AddChild(_stream);
        BuildMist(sprayer.Droplets.Count);

        Visible = false;
        IsInitialized = true;
    }

    public void ApplyEffectsSettings(EffectsSettings settings) => _settings = settings;

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        if (!active)
        {
            Visible = false;
            VisiblePuffCount = 0;
            MaxPuffRisePx = 0.0f;
        }
    }

    /// <summary>Advances the mist's own flicker phase on the owning root's routed tick.</summary>
    public void PhysicsTick()
    {
        if (IsInitialized)
            _ticks++;
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_presentationActive)
            return;

        UpdateMist();

        // No weapon drawn, or one with nowhere to point yet: the aim model owns that state,
        // and a nozzle pointing somewhere nobody chose is worse than no nozzle. The mist is
        // still updated above, because droplets already in the air belong to the room.
        if (!_sprayer.IsActive || _sprayer.AimForward == Vector2.Zero)
        {
            _mesh.Visible = false;
            _pilot.Visible = false;
            Visible = VisiblePuffCount > 0;
            return;
        }

        Vector2 aim = _sprayer.AimForward;
        Vector3 position = WorldPlaneMapping.To3D(_sprayer.Cursor);
        position.Z = _profile.VisualDepthOffset;
        GlobalPosition = position;
        GlobalRotation = new Vector3(
            0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(aim.Angle()));
        IsMirrored = aim.X < 0.0f;
        Scale = Vector3.One;
        // A proper rotation has determinant +1. Local X is the wand axis, so this roll flips
        // the canister and grip over without reversing winding or normals.
        _orientation.Rotation = new Vector3(IsMirrored ? Mathf.Pi : 0.0f, 0.0f, 0.0f);
        _mesh.Visible = true;
        UpdatePilotLight();
        Visible = true;
    }

    /// <summary>
    /// The pilot light at the nozzle: a small additive bead that breathes gently while the
    /// weapon is out and flares while it is spraying. Its modulation is capped by the same
    /// photosensitivity rule the burning flicker obeys, because it is the same kind of
    /// pulsing light on the same screen.
    /// </summary>
    private void UpdatePilotLight()
    {
        float hz = _settings.FlickerHz(_profile.SafeFlickerHz, _profile.FullFlickerHz);
        float cycleTicks = Mathf.Max(1.0f, Engine.PhysicsTicksPerSecond / hz);
        float phase = (_ticks % cycleTicks) / cycleTicks;
        float pulse = 0.5f + (0.5f * Mathf.Sin(phase * Mathf.Tau));
        float strength = _sprayer.IsSpraying ? 1.0f : 0.45f;
        float size = _profile.VisualLengthPx * 0.10f * strength * (0.75f + (0.25f * pulse));

        _pilot.Position = new Vector3(
            _profile.VisualMuzzleTipPx - (_profile.VisualLengthPx * 0.16f),
            -_profile.VisualLengthPx * 0.15f,
            0.0f);
        _pilot.Scale = new Vector3(size, size, size);
        _pilotMaterial.AlbedoColor = new Color(
            _profile.FlameCoreColor, 0.55f + (0.35f * pulse * strength));
        _pilot.Visible = true;
    }

    /// <summary>
    /// Rides one puff on each live droplet. Everything a puff does comes from that droplet's
    /// own life fraction and its index, so the plume is exactly as deterministic as the
    /// stream it is drawn from — no generator is consulted here.
    /// </summary>
    private void UpdateMist()
    {
        IReadOnlyList<SprayDropletBody> droplets = _sprayer.Droplets;
        int visible = 0;
        float maximumRise = 0.0f;
        for (int index = 0; index < _puffs.Count; index++)
        {
            MeshInstance3D puff = _puffs[index];
            if (index >= droplets.Count)
            {
                puff.Visible = false;
                continue;
            }

            SprayDropletBody droplet = droplets[index];
            if (droplet.State != SprayDropletState.Live || !droplet.DrawEnabled)
            {
                puff.Visible = false;
                continue;
            }

            float life = Mathf.Clamp(droplet.LifeFraction, 0.0f, 1.0f);
            float swell = 1.0f + ((_profile.MistSpreadFactor - 1.0f) * Mathf.Sqrt(life));
            float size = droplet.Radius * (2.6f + swell) * 3.0f;
            float rise = _settings.ReducedMotion
                ? 0.0f
                : _profile.VisualLengthPx * 0.70f * Mathf.Pow(life, 1.35f);

            Vector3 position = WorldPlaneMapping.To3D(droplet.GlobalPosition);
            position.Y += rise;
            position.Z = _profile.VisualDepthOffset - (_profile.VisualLengthPx * 0.25f);
            puff.GlobalPosition = position;
            Vector2 velocity = droplet.LinearVelocity.LengthSquared() > 1.0f
                ? droplet.LinearVelocity
                : droplet.LaunchVelocity;
            float angle = velocity == Vector2.Zero
                ? 0.0f
                : WorldPlaneMapping.To3DRotationZ(velocity.Angle());
            puff.Rotation = new Vector3(0.0f, 0.0f, angle + (Mathf.Sin(index + life * 4.0f) * 0.12f));
            puff.Scale = new Vector3(size * 1.45f, size, 1.0f);
            ShaderMaterial material = _puffMaterials[index];
            material.SetShaderParameter("age", life);
            material.SetShaderParameter("phase", _ticks / (float)Engine.PhysicsTicksPerSecond);
            puff.Visible = true;
            visible++;
            maximumRise = Mathf.Max(maximumRise, rise);
        }

        VisiblePuffCount = visible;
        MaxPuffRisePx = maximumRise;
    }

    private void BuildMist(int capacity)
    {
        Shader shader = GD.Load<Shader>("res://shaders/sprayer_cloud.gdshader") ??
                        throw new InvalidOperationException("The sprayer cloud shader is missing.");

        // Front-facing cards let the shader erode the silhouette into irregular foam instead
        // of leaving a chain of obviously spherical pellets.
        var billow = new QuadMesh { Size = Vector2.One };
        for (int index = 0; index < Math.Max(1, capacity); index++)
        {
            var material = new ShaderMaterial
            {
                ResourceName = $"SprayerCloudMaterial_{index + 1}",
                Shader = shader,
            };
            material.SetShaderParameter("core_color", _profile.FlameCoreColor);
            material.SetShaderParameter("flame_color", _profile.FlameColor);
            material.SetShaderParameter("smoke_color", _profile.SmokeColor.Lightened(0.22f));
            material.SetShaderParameter("seed", index * 0.731f);
            var puff = new MeshInstance3D
            {
                Name = $"MistPuff_{index + 1}",
                Mesh = billow,
                MaterialOverride = material,
                Visible = false,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            _stream.AddChild(puff);
            _puffs.Add(puff);
            _puffMaterials.Add(material);
        }
    }

    private void BuildCanister()
    {
        float length = _profile.VisualLengthPx;
        var canisterMaterial = new StandardMaterial3D
        {
            ResourceName = "SprayerCanisterMaterial",
            AlbedoColor = _profile.BodyColor.Lightened(0.18f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = 0.68f,
            Metallic = 0.08f,
        };
        _canister = new MeshInstance3D
        {
            Name = "FuelCanister",
            Mesh = new CapsuleMesh
            {
                Radius = length * 0.135f,
                Height = length * 0.46f,
                RadialSegments = 16,
                Rings = 4,
            },
            MaterialOverride = canisterMaterial,
            Position = new Vector3(-length * 0.08f, length * 0.08f, 0.0f),
        };
        _orientation.AddChild(_canister);
    }

    private void BuildPilotLight()
    {
        _pilotMaterial = new StandardMaterial3D
        {
            ResourceName = "ProvisionalSprayerPilotMaterial",
            AlbedoColor = new Color(_profile.FlameCoreColor, 0.8f),
            EmissionEnabled = true,
            Emission = _profile.FlameCoreColor,
            EmissionEnergyMultiplier = 2.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _pilot = new MeshInstance3D
        {
            Name = "SprayerPilotLight",
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = _pilotMaterial,
            Visible = false,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        _orientation.AddChild(_pilot);
    }

    private static Vector2 Direction2D(Basis basis, Vector3 localAxis)
    {
        Vector3 world = basis * localAxis;
        Vector2 plane = WorldPlaneMapping.To2D(world);
        return plane.IsZeroApprox() ? Vector2.Zero : plane.Normalized();
    }
}
