using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The frontal 3D view of a burning buddy: the stream's procedural cloud shader on every
/// part touched in the current episode, with older puffs rising and cooling into smoke. The
/// flat counterpart is <see cref="Tools.FireVisual2D"/>, and the two are never shown at once.
///
/// <para>Render-only. All cards and materials are preallocated at initialize; positions and
/// shader phases derive from part/slot indices and routed ticks, never simulation RNG.</para>
///
/// <para>FR-017.3: the flicker is capped at the profile's safe rate whenever
/// photosensitivity-safe effects are on, and reduced particles thin the embers. Neither can
/// reach the burn's timing, pain, or mood.</para>
/// </summary>
[GlobalClass]
public partial class FireVisual3D : Node3D
{
    private const int PuffsPerPart = 5;
    private readonly List<BurnPuff> _puffs = new();

    private FireSprayerProfile _profile = null!;
    private FireSprayerComponent _sprayer = null!;
    private EffectsSettings _settings = EffectsSettings.Default;
    private bool _presentationActive;
    private int _ticks;

    private readonly record struct BurnPuff(
        MeshInstance3D Mesh,
        ShaderMaterial Material,
        int PartIndex,
        int Slot);

    public bool IsInitialized { get; private set; }

    /// <summary>True while the flame body is on screen.</summary>
    public bool IsBurningVisible => IsInitialized && Visible && VisiblePuffCount > 0;

    /// <summary>The flicker rate actually in force, in Hz. The photosensitivity oracle.</summary>
    public float FlickerHz => _settings.FlickerHz(_profile.SafeFlickerHz, _profile.FullFlickerHz);

    /// <summary>Shader-cloud puffs currently visible — the reduced-particles oracle.</summary>
    public int VisiblePuffCount { get; private set; }

    /// <summary>Compatibility readout retained for the existing effects-settings scenario.</summary>
    public int VisibleEmberCount => VisiblePuffCount;

    public void Initialize(FireSprayerComponent sprayer, FireSprayerProfile profile)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(sprayer);
        ArgumentNullException.ThrowIfNull(profile);
        _sprayer = sprayer;
        _profile = profile;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        Shader shader = GD.Load<Shader>("res://shaders/sprayer_cloud.gdshader") ??
                        throw new InvalidOperationException("The sprayer cloud shader is missing.");
        var quad = new QuadMesh { Size = Vector2.One };
        for (int partIndex = 0; partIndex < 6; partIndex++)
        {
            for (int slot = 0; slot < PuffsPerPart; slot++)
            {
                var material = new ShaderMaterial
                {
                    ResourceName = $"BodyFireCloudMaterial_{partIndex}_{slot}",
                    Shader = shader,
                };
                material.SetShaderParameter("core_color", profile.FlameCoreColor);
                material.SetShaderParameter("flame_color", profile.FlameColor);
                material.SetShaderParameter("smoke_color", profile.SmokeColor.Lightened(0.22f));
                material.SetShaderParameter("seed", (partIndex * 1.73f) + (slot * 0.61f));
                var puff = new MeshInstance3D
                {
                    Name = $"BodyFirePuff_{partIndex}_{slot}",
                    Mesh = quad,
                    MaterialOverride = material,
                    Visible = false,
                    PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
                };
                AddChild(puff);
                _puffs.Add(new BurnPuff(puff, material, partIndex, slot));
            }
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
            HideAll();
            return;
        }

        UpdateClouds();
    }

    private void UpdateClouds()
    {
        int stride = _settings.ParticleStride;
        int cycle = Mathf.Max(1, _profile.EmberCycleTicks);
        int visible = 0;
        for (int index = 0; index < _puffs.Count; index++)
        {
            BurnPuff puff = _puffs[index];
            var partId = (BuddyPartId)puff.PartIndex;
            bool core = puff.Slot < 2;
            int trailIndex = puff.Slot - 2;
            if (!_sprayer.IsPartBurning(partId) || (!core && trailIndex % stride != 0))
            {
                puff.Mesh.Visible = false;
                continue;
            }

            PuppetPartBody? part = FindPart(partId);
            if (part is null)
                continue;

            float radius = part.Radius;
            float life = core
                ? 0.05f + (puff.Slot * 0.12f)
                : ((_ticks + (trailIndex * cycle / 3) + (puff.PartIndex * 11)) % cycle) /
                  (float)cycle;
            float rise = core
                ? radius * (0.12f + (puff.Slot * 0.35f))
                : radius * (0.55f + (_profile.EmberReachFactor *
                    (_settings.ReducedMotion ? 0.35f : 2.0f) * life));
            float lateral = core
                ? (puff.Slot == 0 ? -1.0f : 1.0f) * radius * 0.22f
                : Mathf.Sin((life * Mathf.Tau) + puff.PartIndex) * radius * 0.55f;
            float size = core
                ? radius * (1.35f + (puff.Slot * 0.2f))
                : radius * (0.85f + (life * 1.35f));

            Vector3 position = WorldPlaneMapping.To3D(part.GlobalPosition);
            position += new Vector3(lateral, rise, 0.0f);
            position.Z = _profile.VisualDepthOffset + 2.0f;
            puff.Mesh.GlobalPosition = position;
            puff.Mesh.Rotation = new Vector3(0.0f, 0.0f, Mathf.Sin(index + life * 3.0f) * 0.16f);
            puff.Mesh.Scale = new Vector3(size * 1.1f, size * 1.3f, 1.0f);
            puff.Material.SetShaderParameter("age", life);
            puff.Material.SetShaderParameter(
                "phase",
                _settings.ReducedMotion ? 0.0f : _ticks / (float)Engine.PhysicsTicksPerSecond);
            puff.Mesh.Visible = true;
            visible++;
        }

        VisiblePuffCount = visible;
        Visible = visible > 0;
    }

    private void ApplyVisibility()
    {
        Visible = _presentationActive && IsInitialized;
        if (!_presentationActive || !IsInitialized || !_sprayer.IsBurning)
            HideAll();
    }

    private void HideAll()
    {
        for (int index = 0; index < _puffs.Count; index++)
            _puffs[index].Mesh.Visible = false;
        VisiblePuffCount = 0;
        Visible = false;
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

}
