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
/// <para>Render-only. The blast is an additive flash for a few ticks plus one ring
/// expanding to the real full-effect radius, so what the player sees is the size of what
/// the physics did — driven by counters this node advances on the routed tick, never by
/// wall clock.</para>
/// </summary>
[GlobalClass]
public partial class GrenadeVisual3D : Node3D
{
    private GrenadeProfile _profile = null!;
    private Body2DVisual3D _slot = null!;
    private Node3D _blast = null!;
    private MeshInstance3D _flash = null!;
    private MeshInstance3D _ring = null!;
    private StandardMaterial3D _bodyMaterial = null!;
    private Mesh? _pinnedMesh;
    private Mesh? _pinPulledMesh;
    private bool _presentationActive;
    private bool _showingPinnedMesh;
    private float _builtForRadius;
    private int _flashTicks;
    private int _ringTicks;

    public bool IsInitialized { get; private set; }
    public bool IsAttached => IsInitialized && _slot.IsAttached;

    /// <summary>True while the additive detonation flash is on screen.</summary>
    public bool IsFlashVisible => IsInitialized && Visible && _flash.Visible;

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

        BuildBlast();
        Visible = false;
        IsInitialized = true;
    }

    public void Attach(LooseObjectBody body, bool pinIn)
    {
        RequireInitialized();
        ArgumentNullException.ThrowIfNull(body);
        EnsureMesh(body.Radius, pinIn);
        _slot.Attach(body);
        Visible = _presentationActive;
    }

    public void Detach(LooseObjectBody body)
    {
        if (!IsInitialized)
            return;

        _slot.Detach(body);
        Visible = _presentationActive && (_flashTicks > 0 || _ringTicks > 0);
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
        PeakRingRadiusPx = 0.0f;
        Visible = _presentationActive;
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        if (IsInitialized)
            _slot.SetPresentationActive(active);
        Visible = active && (IsAttached || _flashTicks > 0 || _ringTicks > 0);
    }

    public void CaptureTickSnapshot()
    {
        if (IsInitialized)
            _slot.CaptureTickSnapshot();
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
        if (_flashTicks == 0 && _ringTicks == 0 && !IsAttached)
            Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_presentationActive)
            return;

        UpdateFlash();
        UpdateRing();
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
        // honest about how big the dangerous part is.
        float size = _profile.BlastFullRadiusPx * 1.6f * strength;
        _flash.Scale = new Vector3(size, size, size);
        _flash.Visible = true;
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
            AlbedoColor = new Color(_profile.BlastColor, 0.92f),
            EmissionEnabled = true,
            Emission = _profile.BlastColor,
            EmissionEnergyMultiplier = 2.4f,
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

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("GrenadeVisual3D used before initialization.");
    }
}
