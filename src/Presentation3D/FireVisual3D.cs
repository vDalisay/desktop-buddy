using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The frontal 3D view of a burning buddy: an additive flame body on the ignition part with
/// ember quads rising off it. The flat counterpart is <see cref="Tools.FireVisual2D"/>, and
/// the two are one fire seen two ways — never both at once.
///
/// <para>Render-only, on the <see cref="GrenadeVisual3D"/> idiom: additive unshaded
/// materials, one preallocated ember per authored count built at initialize rather than on
/// an ignition tick, and every ember's direction taken from its own index through the
/// golden-angle fan. No generator is drawn from here.</para>
///
/// <para>FR-017.3: the flicker is capped at the profile's safe rate whenever
/// photosensitivity-safe effects are on, and reduced particles thin the embers. Neither can
/// reach the burn's timing, pain, or mood.</para>
/// </summary>
[GlobalClass]
public partial class FireVisual3D : Node3D
{
    private readonly List<MeshInstance3D> _embers = new();

    private FireSprayerProfile _profile = null!;
    private FireSprayerComponent _sprayer = null!;
    private EffectsSettings _settings = EffectsSettings.Default;
    private Node3D _flame = null!;
    private MeshInstance3D _skirt = null!;
    private MeshInstance3D _core = null!;
    private StandardMaterial3D _skirtMaterial = null!;
    private StandardMaterial3D _coreMaterial = null!;
    private StandardMaterial3D _emberMaterial = null!;
    private bool _presentationActive;
    private int _ticks;

    public bool IsInitialized { get; private set; }

    /// <summary>True while the flame body is on screen.</summary>
    public bool IsBurningVisible => IsInitialized && Visible && _flame.Visible;

    /// <summary>The flicker rate actually in force, in Hz. The photosensitivity oracle.</summary>
    public float FlickerHz => _settings.FlickerHz(_profile.SafeFlickerHz, _profile.FullFlickerHz);

    /// <summary>Ember quads currently visible — the reduced-particles oracle.</summary>
    public int VisibleEmberCount { get; private set; }

    public void Initialize(FireSprayerComponent sprayer, FireSprayerProfile profile)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(sprayer);
        ArgumentNullException.ThrowIfNull(profile);
        _sprayer = sprayer;
        _profile = profile;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        _flame = new Node3D { Name = "BurningFlame", Visible = false };
        AddChild(_flame);

        _skirtMaterial = Additive("ProvisionalBurnSkirtMaterial", profile.FlameColor, 2.2f);
        _skirt = new MeshInstance3D
        {
            Name = "BurnSkirt",
            Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f, RadialSegments = 16, Rings = 8 },
            MaterialOverride = _skirtMaterial,
            Position = new Vector3(0.0f, 0.0f, -1.0f),
        };
        _flame.AddChild(_skirt);

        _coreMaterial = Additive("ProvisionalBurnCoreMaterial", profile.FlameCoreColor, 3.2f);
        _core = new MeshInstance3D
        {
            Name = "BurnCore",
            Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f, RadialSegments = 16, Rings = 8 },
            MaterialOverride = _coreMaterial,
            Position = new Vector3(0.0f, 0.0f, 1.0f),
        };
        _flame.AddChild(_core);

        _emberMaterial = Additive("ProvisionalBurnEmberMaterial", profile.EmberColor, 3.0f);
        var quad = new QuadMesh { Size = Vector2.One };
        int count = Mathf.Clamp(profile.EmberCount, 0, 64);
        for (int index = 0; index < count; index++)
        {
            var ember = new MeshInstance3D
            {
                Name = $"BurnEmber_{index + 1}",
                Mesh = quad,
                MaterialOverride = _emberMaterial,
                Position = new Vector3(0.0f, 0.0f, 1.5f),
                Visible = false,
            };
            _flame.AddChild(ember);
            _embers.Add(ember);
        }

        Visible = false;
        IsInitialized = true;
    }

    public void ApplyEffectsSettings(EffectsSettings settings) => _settings = settings;

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        ApplyVisibility();
    }

    /// <summary>Advances the flicker phase on the owning root's routed tick.</summary>
    public void PhysicsTick()
    {
        if (!IsInitialized)
            return;

        _ticks++;
        ApplyVisibility();
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_presentationActive || !_sprayer.IsBurning)
        {
            VisibleEmberCount = 0;
            return;
        }

        PuppetPartBody? part = FindPart(_sprayer.IgnitionPart);
        if (part is null)
            return;

        Vector3 anchor = WorldPlaneMapping.To3D(part.GlobalPosition);
        anchor.Z = _profile.VisualDepthOffset;
        _flame.GlobalPosition = anchor;

        float radius = part.Radius;
        float cycleTicks = Mathf.Max(1.0f, Engine.PhysicsTicksPerSecond / FlickerHz);
        float phase = (_ticks % cycleTicks) / cycleTicks;
        float pulse = 0.5f + (0.5f * Mathf.Sin(phase * Mathf.Tau));

        float skirt = radius * (1.15f + (0.25f * pulse));
        _skirt.Scale = new Vector3(skirt * 0.85f, skirt * 1.25f, skirt * 0.85f);
        _skirt.Position = new Vector3(0.0f, radius * 0.25f, -1.0f);
        _skirtMaterial.AlbedoColor = new Color(_profile.FlameColor, 0.42f + (0.12f * pulse));
        _core.Scale = new Vector3(skirt * 0.45f, skirt * 0.7f, skirt * 0.45f);
        _core.Position = new Vector3(0.0f, radius * 0.45f, 1.0f);
        _coreMaterial.AlbedoColor = new Color(_profile.FlameCoreColor, 0.68f + (0.18f * pulse));

        UpdateEmbers(radius);
    }

    private void UpdateEmbers(float radius)
    {
        int stride = _settings.ParticleStride;
        int cycle = Mathf.Max(1, _profile.EmberCycleTicks);
        float reach = radius * _profile.EmberReachFactor;
        int visible = 0;
        for (int index = 0; index < _embers.Count; index++)
        {
            MeshInstance3D ember = _embers[index];
            if (index % stride != 0)
            {
                ember.Visible = false;
                continue;
            }

            float angle = index * 2.399963f;
            float lateral = Mathf.Cos(angle) * radius * 0.8f;
            float life = ((_ticks + (index * cycle / Mathf.Max(1, _embers.Count))) % cycle) / (float)cycle;
            float size = Mathf.Max(0.8f, radius * 0.26f * (1.0f - life));
            ember.Position = new Vector3(lateral, reach * life, 1.5f);
            ember.Scale = new Vector3(size, size, 1.0f);
            ember.Visible = true;
            visible++;
        }

        _emberMaterial.AlbedoColor = new Color(_profile.EmberColor, 0.85f);
        VisibleEmberCount = visible;
    }

    private void ApplyVisibility()
    {
        Visible = _presentationActive && IsInitialized;
        if (IsInitialized)
            _flame.Visible = _presentationActive && _sprayer.IsBurning;
    }

    private PuppetPartBody? FindPart(BuddyPartId partId)
    {
        IReadOnlyList<PuppetPartBody> parts = _sprayer.Pipeline.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            if (parts[index].PartId == partId)
                return parts[index];
        }

        return null;
    }

    private static StandardMaterial3D Additive(string name, Color colour, float energy) =>
        new()
        {
            ResourceName = name,
            AlbedoColor = new Color(colour, 0.8f),
            EmissionEnabled = true,
            Emission = colour,
            EmissionEnergyMultiplier = energy,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
}
