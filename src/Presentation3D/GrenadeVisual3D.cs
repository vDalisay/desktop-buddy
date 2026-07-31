using System;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The drawn grenade and its explosion, in the frontal 3D presentation. Unlike the guns —
/// which are a cursor and an aim with no collider to hang a visual on — the grenade has a
/// real physical body, so this uses the standard <see cref="Body2DVisual3D"/> attach seam
/// and adds only what a loose object has no concept of: the mesh swap when the pin comes
/// out, and the small blast.
///
/// <para>Render-only. The blast is four layers over the same routed-tick counters — a
/// white-hot core, a fireball that swells and cools, embers thrown out of it, and one ring
/// expanding to the real full-effect radius — so what the player sees is the size of what
/// the physics did, and never a wall clock.</para>
///
/// <para>The embers' directions and reaches come from
/// <see cref="GrenadeProfile.EmberDirection"/> and
/// <see cref="GrenadeProfile.EmberReachFraction"/>, which are functions of the ember's index
/// and nothing else. No generator is drawn from here: presentation must never consume
/// simulation randomness, and a scenario replaying a seed must get the same explosion.</para>
/// </summary>
[GlobalClass]
public partial class GrenadeVisual3D : Node3D
{
    private readonly System.Collections.Generic.List<MeshInstance3D> _embers = new();

    private GrenadeProfile _profile = null!;
    private Body2DVisual3D _slot = null!;
    private GrenadePinVisual3D _pins = null!;
    private Node3D _blast = null!;
    private MeshInstance3D _flash = null!;
    private MeshInstance3D _fireball = null!;
    private MeshInstance3D _ring = null!;
    private StandardMaterial3D _bodyMaterial = null!;
    private StandardMaterial3D _fireballMaterial = null!;
    private StandardMaterial3D _emberMaterial = null!;
    private Mesh? _pinnedMesh;
    private Mesh? _pinPulledMesh;
    private bool _presentationActive;
    private bool _showingPinnedMesh;
    private float _builtForRadius;
    private int _flashTicks;
    private int _ringTicks;
    private int _fireballTicks;
    private int _emberTicks;

    public bool IsInitialized { get; private set; }
    public bool IsAttached => IsInitialized && _slot.IsAttached;

    /// <summary>True while the additive detonation flash is on screen.</summary>
    public bool IsFlashVisible => IsInitialized && Visible && _flash.Visible;

    /// <summary>True while the fireball is on screen.</summary>
    public bool IsFireballVisible => IsInitialized && Visible && _fireball.Visible;

    /// <summary>How many embers are currently drawn.</summary>
    public int VisibleEmberCount { get; private set; }

    /// <summary>The dropped pins' 3D presenter, for a scenario that wants to count them.</summary>
    public GrenadePinVisual3D Pins => _pins;

    /// <summary>True while the expanding blast ring is on screen.</summary>
    public bool IsRingVisible => IsInitialized && Visible && _ring.Visible;

    /// <summary>The ring's current drawn radius in world pixels; zero when not drawn.</summary>
    public float RingRadiusPx { get; private set; }

    /// <summary>The largest radius the last ring reached — the size the blast read as.</summary>
    public float PeakRingRadiusPx { get; private set; }

    /// <summary>True while the drawn grenade still carries its pin ring.</summary>
    public bool ShowsPin => _showingPinnedMesh;

    public void Initialize(GrenadeProfile profile)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("The grenade visual requires a live profile.", nameof(profile));

        _profile = profile;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _bodyMaterial = new StandardMaterial3D
        {
            ResourceName = "ProvisionalGrenadeMaterial",
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = 0.7f,
            Metallic = 0.0f,
        };

        _slot = new Body2DVisual3D { Name = "GrenadeBodyVisualSlot" };
        AddChild(_slot);
        // The slot needs some geometry to exist before SetVisual replaces it; the radius
        // here is only that placeholder, and the real mesh is built per grenade body.
        _slot.Initialize(8.0f, profile.BodyColor, profile.VisualDepthOffset);

        _pins = new GrenadePinVisual3D { Name = "GrenadePinVisual3D" };
        AddChild(_pins);
        _pins.Initialize(profile);

        BuildBlast();
        Visible = false;
        IsInitialized = true;
    }

    /// <summary>
    /// Adopts the grenade component's pooled cosmetic pins so they are drawn as meshes
    /// here rather than as their own flat rings. Composition roots call this once, after
    /// both this node and the component are initialized.
    /// </summary>
    public void TrackPins(System.Collections.Generic.IReadOnlyList<PinBody> pins)
    {
        RequireInitialized();
        _pins.TrackPins(pins);
    }

    public void Attach(LooseObjectBody body, bool pinIn)
    {
        RequireInitialized();
        ArgumentNullException.ThrowIfNull(body);
        EnsureMesh(body.Radius, pinIn);
        _slot.Attach(body);
        ApplyVisibility();
    }

    public void Detach(LooseObjectBody body)
    {
        if (!IsInitialized)
            return;

        _slot.Detach(body);
        ApplyVisibility();
    }

    /// <summary>Swaps to the pinless silhouette when the player pulls the pin.</summary>
    public void NotifyPinPulled()
    {
        if (!IsInitialized || !_slot.IsAttached || !_showingPinnedMesh)
            return;

        EnsureMesh(_builtForRadius, pinIn: false);
    }

    /// <summary>Starts the blast at <paramref name="center"/>, in 2D world pixels.</summary>
    public void NotifyDetonated(Vector2 center)
    {
        if (!IsInitialized)
            return;

        Vector3 position = WorldPlaneMapping.To3D(center);
        position.Z = _profile.VisualDepthOffset;
        _blast.GlobalPosition = position;
        _flashTicks = Mathf.Max(0, _profile.FlashTicks);
        _ringTicks = Mathf.Max(1, _profile.RingTicks);
        _fireballTicks = Mathf.Max(0, _profile.FireballTicks);
        _emberTicks = _profile.EmberCount > 0 ? Mathf.Max(0, _profile.EmberTicks) : 0;
        PeakRingRadiusPx = 0.0f;
        ApplyVisibility();
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        if (IsInitialized)
        {
            _slot.SetPresentationActive(active);
            _pins.SetPresentationActive(active);
        }

        ApplyVisibility();
    }

    public void CaptureTickSnapshot()
    {
        if (IsInitialized)
        {
            _slot.CaptureTickSnapshot();
            _pins.CaptureTickSnapshot();
        }
    }

    /// <summary>Advances the blast envelopes on the owning root's routed tick.</summary>
    public void PhysicsTick()
    {
        if (!IsInitialized)
            return;

        if (_flashTicks > 0)
            _flashTicks--;
        if (_ringTicks > 0)
            _ringTicks--;
        if (_fireballTicks > 0)
            _fireballTicks--;
        if (_emberTicks > 0)
            _emberTicks--;
        ApplyVisibility();
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_presentationActive)
            return;

        UpdateFlash();
        UpdateFireball();
        UpdateEmbers();
        UpdateRing();
    }

    /// <summary>True while any layer of the blast still has ticks left to run.</summary>
    private bool BlastIsRunning =>
        _flashTicks > 0 || _ringTicks > 0 || _fireballTicks > 0 || _emberTicks > 0;

    /// <summary>
    /// The root stays visible for the whole presentation mode, because the dropped pins
    /// hang off it and outlive both the grenade and its blast by design. Only the blast
    /// subtree is gated on the blast — the body slot and the pins own their own.
    /// </summary>
    private void ApplyVisibility()
    {
        Visible = _presentationActive && IsInitialized;
        // Tolerates being asked before composition has built the blast, the way the
        // presentation toggle it hangs off always could.
        if (IsInitialized)
            _blast.Visible = _presentationActive && BlastIsRunning;
    }

    private void UpdateFlash()
    {
        int authored = Mathf.Max(1, _profile.FlashTicks);
        float strength = (float)_flashTicks / authored;
        if (_profile.FlashTicks <= 0 || strength <= 0.0f)
        {
            _flash.Visible = false;
            return;
        }

        // Sized against the full-effect radius, so even the first frame of the blast is
        // honest about how big the dangerous part is. The core is white-hot and brief:
        // it is the bang, and the fireball behind it is the fire.
        float size = _profile.BlastFullRadiusPx * 2.1f * strength;
        _flash.Scale = new Vector3(size, size, size);
        _flash.Visible = true;
    }

    /// <summary>
    /// The fireball: out fast, then cooling. The swell is an ease-out so the first two
    /// ticks do most of it — a linear one reads as an expanding balloon rather than as
    /// something detonating.
    /// </summary>
    private void UpdateFireball()
    {
        int authored = Mathf.Max(1, _profile.FireballTicks);
        if (_profile.FireballTicks <= 0 || _fireballTicks <= 0)
        {
            _fireball.Visible = false;
            return;
        }

        float life = 1.0f - ((float)_fireballTicks / authored);
        float swell = 1.0f - ((1.0f - life) * (1.0f - life));
        float radius = _profile.BlastFullRadiusPx * _profile.FireballRadiusFactor *
                       Mathf.Lerp(0.35f, 1.0f, swell);
        _fireball.Scale = new Vector3(radius, radius, radius);

        // White-hot, then flame, then the colour it goes out at.
        Color colour = life < 0.35f
            ? _profile.FireCoreColor.Lerp(_profile.FireColor, life / 0.35f)
            : _profile.FireColor.Lerp(_profile.SmokeColor, (life - 0.35f) / 0.65f);
        // Additive, so fading alpha is the only way it can leave.
        _fireballMaterial.AlbedoColor = new Color(colour, 0.95f * (1.0f - (life * life)));
        _fireballMaterial.Emission = colour;
        _fireball.Visible = true;
    }

    /// <summary>
    /// The embers: fixed directions, fixed reaches, thrown out on an ease-out and shrinking
    /// as they go. What turns a flash into an explosion is debris leaving it.
    /// </summary>
    private void UpdateEmbers()
    {
        int authored = Mathf.Max(1, _profile.EmberTicks);
        if (_embers.Count == 0 || _profile.EmberTicks <= 0 || _emberTicks <= 0)
        {
            foreach (MeshInstance3D ember in _embers)
                ember.Visible = false;
            VisibleEmberCount = 0;
            return;
        }

        float life = 1.0f - ((float)_emberTicks / authored);
        float travel = 1.0f - ((1.0f - life) * (1.0f - life) * (1.0f - life));
        float reach = _profile.BlastFullRadiusPx * _profile.EmberReachFactor;
        float size = _profile.BlastFullRadiusPx * 0.30f * (1.0f - life);
        var tint = new Color(
            _profile.FireColor.Lerp(_profile.FireCoreColor, 0.35f),
            0.95f * (1.0f - (life * life)));
        _emberMaterial.AlbedoColor = tint;
        _emberMaterial.Emission = new Color(tint, 1.0f);

        for (int index = 0; index < _embers.Count; index++)
        {
            Vector2 direction = GrenadeProfile.EmberDirection(index, _embers.Count);
            float distance = reach * GrenadeProfile.EmberReachFraction(index) * travel;
            Vector3 offset = WorldPlaneMapping.To3D(direction * distance);
            // In front of the fireball, behind the core: the same sort order as the build.
            offset.Z = 0.5f;
            MeshInstance3D ember = _embers[index];
            ember.Position = offset;
            // Stretched along the direction of travel, so each one reads as a streak.
            ember.Rotation = new Vector3(
                0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(direction.Angle()));
            ember.Scale = new Vector3(size * 1.9f, size, size);
            ember.Visible = true;
        }

        VisibleEmberCount = _embers.Count;
    }

    private void UpdateRing()
    {
        if (_ringTicks <= 0)
        {
            _ring.Visible = false;
            RingRadiusPx = 0.0f;
            return;
        }

        int authored = Mathf.Max(1, _profile.RingTicks);
        float progress = 1.0f - ((float)_ringTicks / authored);
        RingRadiusPx = _profile.BlastFullRadiusPx * progress;
        PeakRingRadiusPx = Mathf.Max(PeakRingRadiusPx, RingRadiusPx);
        // The torus is authored with outer radius 0.5 in its own XZ plane, so scaling
        // those two axes by the diameter gives exactly RingRadiusPx. The Y axis is its
        // thickness and is deliberately left alone.
        float diameter = Mathf.Max(0.001f, RingRadiusPx * 2.0f);
        _ring.Scale = new Vector3(diameter, 1.0f, diameter);
        var material = (StandardMaterial3D)_ring.MaterialOverride;
        material.AlbedoColor = new Color(_profile.BlastColor, 0.75f * (1.0f - progress));
        _ring.Visible = true;
    }

    private void EnsureMesh(float radius, bool pinIn)
    {
        bool rebuild = !Mathf.IsEqualApprox(radius, _builtForRadius) ||
                       (pinIn ? _pinnedMesh : _pinPulledMesh) is null;
        if (rebuild)
        {
            // Both silhouettes are built for this body's radius, so pulling the pin later
            // is a mesh swap rather than a rebuild on a gameplay tick.
            _pinnedMesh = GrenadeMeshBuilder.Build(_profile, radius, pinIn: true);
            _pinPulledMesh = GrenadeMeshBuilder.Build(_profile, radius, pinIn: false);
            _builtForRadius = radius;
        }

        _showingPinnedMesh = pinIn;
        _slot.SetVisual(
            pinIn ? _pinnedMesh! : _pinPulledMesh!,
            _bodyMaterial,
            _profile.VisualDepthOffset);
    }

    private void BuildBlast()
    {
        _blast = new Node3D
        {
            Name = "GrenadeBlast",
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(_blast);

        var flashMaterial = new StandardMaterial3D
        {
            ResourceName = "ProvisionalBlastFlashMaterial",
            AlbedoColor = new Color(_profile.FireCoreColor, 0.95f),
            EmissionEnabled = true,
            Emission = _profile.FireCoreColor,
            EmissionEnergyMultiplier = 3.2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _flash = new MeshInstance3D
        {
            Name = "BlastFlash",
            Mesh = new QuadMesh { Size = Vector2.One },
            MaterialOverride = flashMaterial,
            // The nearest layer of the blast, so the hot centre sorts over the fire.
            Position = new Vector3(0.0f, 0.0f, 1.0f),
            Visible = false,
        };
        _blast.AddChild(_flash);
        var cross = new MeshInstance3D
        {
            Name = "BlastFlashCross",
            Mesh = new QuadMesh { Size = new Vector2(2.4f, 0.4f) },
            MaterialOverride = flashMaterial,
        };
        _flash.AddChild(cross);
        var crossDiagonal = new MeshInstance3D
        {
            Name = "BlastFlashCrossDiagonal",
            Mesh = new QuadMesh { Size = new Vector2(2.4f, 0.4f) },
            MaterialOverride = flashMaterial,
            Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f),
        };
        _flash.AddChild(crossDiagonal);

        BuildFireball();
        BuildEmbers();

        var ringMaterial = new StandardMaterial3D
        {
            ResourceName = "ProvisionalBlastRingMaterial",
            AlbedoColor = new Color(_profile.BlastColor, 0.75f),
            EmissionEnabled = true,
            Emission = _profile.BlastColor,
            EmissionEnergyMultiplier = 1.4f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _ring = new MeshInstance3D
        {
            Name = "BlastRing",
            Mesh = new TorusMesh
            {
                InnerRadius = 0.44f,
                OuterRadius = 0.5f,
                RingSegments = 24,
                Rings = 6,
            },
            MaterialOverride = ringMaterial,
            // The torus lies in the XZ plane by default; stand it up to face the camera.
            Rotation = new Vector3(Mathf.Pi * 0.5f, 0.0f, 0.0f),
            Visible = false,
        };
        _blast.AddChild(_ring);
    }

    /// <summary>
    /// The fireball is a real sphere rather than a billboard: in a presentation whose whole
    /// point is that the room is solid, a flat disc pretending to be fire is the one thing
    /// that would give the trick away.
    /// </summary>
    private void BuildFireball()
    {
        _fireballMaterial = new StandardMaterial3D
        {
            ResourceName = "ProvisionalBlastFireballMaterial",
            AlbedoColor = new Color(_profile.FireColor, 0.95f),
            EmissionEnabled = true,
            Emission = _profile.FireColor,
            EmissionEnergyMultiplier = 2.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _fireball = new MeshInstance3D
        {
            Name = "BlastFireball",
            // Radius 1 and height 2, so the instance scale is the radius in world px.
            Mesh = new SphereMesh
            {
                Radius = 1.0f,
                Height = 2.0f,
                RadialSegments = 20,
                Rings = 10,
            },
            MaterialOverride = _fireballMaterial,
            // Just behind the core, so the two sort in the order they read in.
            Position = new Vector3(0.0f, 0.0f, -1.0f),
            Visible = false,
        };
        _blast.AddChild(_fireball);
    }

    private void BuildEmbers()
    {
        _emberMaterial = new StandardMaterial3D
        {
            ResourceName = "ProvisionalBlastEmberMaterial",
            AlbedoColor = new Color(_profile.FireColor, 0.95f),
            EmissionEnabled = true,
            Emission = _profile.FireColor,
            EmissionEnergyMultiplier = 3.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        // The pool is the authored count, built once here rather than on the tick a
        // grenade goes off. Every ember shares one quad and one material; only their
        // transforms differ, and those come from the index.
        var quad = new QuadMesh { Size = Vector2.One };
        int count = Mathf.Clamp(_profile.EmberCount, 0, 64);
        for (int index = 0; index < count; index++)
        {
            var ember = new MeshInstance3D
            {
                Name = $"BlastEmber_{index + 1}",
                Mesh = quad,
                MaterialOverride = _emberMaterial,
                Position = new Vector3(0.0f, 0.0f, 0.5f),
                Visible = false,
            };
            _blast.AddChild(ember);
            _embers.Add(ember);
        }
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("GrenadeVisual3D used before initialization.");
    }
}
