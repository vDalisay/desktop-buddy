using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

public partial class GrenadeVisual3D
{
    private const int CaptureBurstPoolCapacity = 8;
    private readonly List<GrenadeCaptureBurst3D> _captureBursts = new();
    private GrenadeComponent? _captureBurstSource;
    private EffectsSettings _captureBurstEffects = EffectsSettings.Default;

    /// <summary>Bounded active detonation accents, useful as a capture/performance oracle.</summary>
    public int ActiveCaptureBurstCount
    {
        get
        {
            int count = 0;
            foreach (GrenadeCaptureBurst3D burst in _captureBursts)
                if (burst.IsActive)
                    count++;
            return count;
        }
    }

    public int VisibleCaptureSmokePuffCount
    {
        get
        {
            int count = 0;
            foreach (GrenadeCaptureBurst3D burst in _captureBursts)
                count += burst.VisibleSmokePuffCount;
            return count;
        }
    }

    public int VisibleCaptureDebrisCount
    {
        get
        {
            int count = 0;
            foreach (GrenadeCaptureBurst3D burst in _captureBursts)
                count += burst.VisibleDebrisCount;
            return count;
        }
    }

    /// <summary>
    /// Explicit accessibility seam for compositions/scenarios that already own an effects snapshot.
    /// The normal desktop path also refreshes from machine-local settings at detonation time, so a
    /// settings change made while the game is open applies to the next burst without touching gameplay.
    /// </summary>
    public void ApplyCaptureEffectsSettings(EffectsSettings settings) => _captureBurstEffects = settings;

    private void EnsureCaptureBurstSubscription()
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(_multiGrenadeComponent))
            return;
        if (_captureBurstSource == _multiGrenadeComponent)
            return;

        ReleaseCaptureBurstSubscription();
        _captureBurstSource = _multiGrenadeComponent;
        _captureBurstSource!.Detonated += OnCaptureBurstDetonated;
    }

    private void ReleaseCaptureBurstSubscription()
    {
        if (GodotObject.IsInstanceValid(_captureBurstSource))
            _captureBurstSource!.Detonated -= OnCaptureBurstDetonated;
        _captureBurstSource = null;
    }

    private void OnCaptureBurstDetonated(Vector2 center)
    {
        if (!IsInitialized || !_presentationActive)
            return;

        RefreshCaptureEffectsFromDesktopSettings();
        GrenadeCaptureBurst3D burst = RentCaptureBurst();
        burst.Start(center, _profile, _captureBurstEffects);
    }

    private GrenadeCaptureBurst3D RentCaptureBurst()
    {
        foreach (GrenadeCaptureBurst3D burst in _captureBursts)
            if (!burst.IsActive)
                return burst;

        if (_captureBursts.Count < CaptureBurstPoolCapacity)
        {
            var burst = new GrenadeCaptureBurst3D
            {
                Name = $"GrenadeCaptureBurst_{_captureBursts.Count + 1}",
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            AddChild(burst);
            _captureBursts.Add(burst);
            return burst;
        }

        // The pool is intentionally hard-bounded. Under pathological grenade spam, recycle the
        // burst nearest natural expiry rather than allocating hidden presentation nodes forever.
        GrenadeCaptureBurst3D oldest = _captureBursts[0];
        foreach (GrenadeCaptureBurst3D burst in _captureBursts)
            if (burst.RemainingTicks < oldest.RemainingTicks)
                oldest = burst;
        return oldest;
    }

    private void AdvanceCaptureBursts()
    {
        foreach (GrenadeCaptureBurst3D burst in _captureBursts)
            burst.Advance();
    }

    private void RefreshCaptureEffectsFromDesktopSettings()
    {
        if (!IsInsideTree())
            return;

        if (GetTree().Root.FindChild(nameof(SandboxRoot), recursive: true, owned: false) is SandboxRoot sandbox &&
            GodotObject.IsInstanceValid(sandbox) &&
            GodotObject.IsInstanceValid(sandbox.Shell))
        {
            _captureBurstEffects = EffectsSettings.FromSave(sandbox.Shell.CurrentLocalSettings);
        }
    }

    /// <summary>
    /// Secondary detonation punctuation that deliberately complements rather than replaces the
    /// historical GrenadeVisual3D flash/fireball/ring. Every detonation gets its own short-lived
    /// smoke/debris/afterglow instance, so a second grenade can go off before the first one finishes
    /// without erasing the first explosion. All geometry is presentation-only and preallocated.
    /// </summary>
    private sealed partial class GrenadeCaptureBurst3D : Node3D
    {
        private const int MaximumSmokePuffs = 6;
        private const int MaximumDebris = 12;
        private const int SmokeLifetimeTicks = 84;

        private readonly List<MeshInstance3D> _smoke = new();
        private readonly List<MeshInstance3D> _debris = new();
        private StandardMaterial3D? _smokeMaterial;
        private StandardMaterial3D? _debrisMaterial;
        private StandardMaterial3D? _afterglowMaterial;
        private MeshInstance3D? _afterglow;
        private GrenadeProfile? _profile;
        private EffectsSettings _settings;
        private int _lifeTicks;
        private int _smokeCount;
        private int _debrisCount;

        public bool IsActive => _lifeTicks > 0;
        public int RemainingTicks => _lifeTicks;
        public int VisibleSmokePuffCount { get; private set; }
        public int VisibleDebrisCount { get; private set; }

        public void Start(Vector2 center, GrenadeProfile profile, EffectsSettings settings)
        {
            ArgumentNullException.ThrowIfNull(profile);
            _profile = profile;
            _settings = settings;
            EnsureBuilt(profile);

            Vector3 position = WorldPlaneMapping.To3D(center);
            position.Z = profile.VisualDepthOffset + 3.0f;
            GlobalPosition = position;

            _lifeTicks = Math.Max(SmokeLifetimeTicks, Math.Max(profile.RingTicks, profile.EmberTicks));
            int stride = Math.Max(1, settings.ParticleStride);
            _smokeCount = Math.Max(1, (MaximumSmokePuffs + stride - 1) / stride);
            _debrisCount = Math.Max(2, (MaximumDebris + stride - 1) / stride);
            Visible = true;
            UpdateVisuals();
        }

        public void Advance()
        {
            if (_lifeTicks <= 0)
                return;

            _lifeTicks--;
            if (_lifeTicks <= 0)
            {
                HideAll();
                return;
            }

            UpdateVisuals();
        }

        private void EnsureBuilt(GrenadeProfile profile)
        {
            if (_afterglow is not null)
                return;

            _afterglowMaterial = new StandardMaterial3D
            {
                ResourceName = "GrenadeCaptureAfterglowMaterial",
                AlbedoColor = new Color(profile.FireColor, 0.22f),
                EmissionEnabled = true,
                Emission = profile.FireColor,
                EmissionEnergyMultiplier = 1.25f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _afterglow = new MeshInstance3D
            {
                Name = "CaptureAfterglow",
                Mesh = new SphereMesh
                {
                    Radius = 1.0f,
                    Height = 2.0f,
                    RadialSegments = 12,
                    Rings = 6,
                },
                MaterialOverride = _afterglowMaterial,
                Visible = false,
            };
            AddChild(_afterglow);

            _smokeMaterial = new StandardMaterial3D
            {
                ResourceName = "GrenadeCaptureSmokeMaterial",
                AlbedoColor = new Color(profile.SmokeColor.Lightened(0.14f), 0.52f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var smokeQuad = new QuadMesh { Size = Vector2.One };
            for (int index = 0; index < MaximumSmokePuffs; index++)
            {
                var puff = new MeshInstance3D
                {
                    Name = $"CaptureSmoke_{index + 1}",
                    Mesh = smokeQuad,
                    MaterialOverride = _smokeMaterial,
                    Visible = false,
                };
                AddChild(puff);
                _smoke.Add(puff);
            }

            _debrisMaterial = new StandardMaterial3D
            {
                ResourceName = "GrenadeCaptureDebrisMaterial",
                AlbedoColor = new Color(profile.FireColor, 0.9f),
                EmissionEnabled = true,
                Emission = profile.FireColor,
                EmissionEnergyMultiplier = 1.4f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var debrisQuad = new QuadMesh { Size = Vector2.One };
            for (int index = 0; index < MaximumDebris; index++)
            {
                var piece = new MeshInstance3D
                {
                    Name = $"CaptureDebris_{index + 1}",
                    Mesh = debrisQuad,
                    MaterialOverride = _debrisMaterial,
                    Visible = false,
                };
                AddChild(piece);
                _debris.Add(piece);
            }
        }

        private void UpdateVisuals()
        {
            if (_profile is null || _afterglow is null || _afterglowMaterial is null ||
                _smokeMaterial is null || _debrisMaterial is null)
                return;

            float life = 1.0f - (_lifeTicks / (float)Math.Max(1, SmokeLifetimeTicks));
            life = Mathf.Clamp(life, 0.0f, 1.0f);
            float motion = _settings.ReducedMotion ? 0.35f : 1.0f;

            float glowLife = Mathf.Clamp(life / 0.42f, 0.0f, 1.0f);
            float glowRadius = _profile.BlastFullRadiusPx * Mathf.Lerp(0.42f, 1.05f, glowLife);
            _afterglow.Scale = new Vector3(glowRadius, glowRadius, glowRadius);
            _afterglowMaterial.AlbedoColor = new Color(
                _profile.FireColor.Lerp(_profile.SmokeColor, glowLife),
                0.28f * (1.0f - glowLife));
            _afterglowMaterial.Emission = _profile.FireColor.Lerp(_profile.SmokeColor, glowLife);
            _afterglow.Visible = glowLife < 1.0f;

            float smokeAlpha = 0.58f * (1.0f - (life * life));
            _smokeMaterial.AlbedoColor = new Color(_profile.SmokeColor.Lightened(0.16f), smokeAlpha);
            VisibleSmokePuffCount = 0;
            for (int index = 0; index < _smoke.Count; index++)
            {
                MeshInstance3D puff = _smoke[index];
                if (index >= _smokeCount)
                {
                    puff.Visible = false;
                    continue;
                }

                Vector2 radial = GrenadeProfile.EmberDirection(index + 3, MaximumSmokePuffs);
                Vector2 drift = (radial * (_profile.BlastFullRadiusPx * 0.48f * life * motion)) +
                                (Vector2.Up * (_profile.BlastFullRadiusPx * (0.18f + (0.72f * life)) * motion));
                Vector3 local = WorldPlaneMapping.To3D(drift);
                local.Z = 1.5f + (index * 0.03f);
                puff.Position = local;
                float size = _profile.BlastFullRadiusPx * (0.24f + (0.68f * life)) *
                             (0.82f + ((index % 3) * 0.12f));
                puff.Scale = new Vector3(size * 1.15f, size, 1.0f);
                puff.Rotation = new Vector3(0.0f, 0.0f, (index * 0.67f) + (life * 0.2f));
                puff.Visible = smokeAlpha > 0.01f;
                if (puff.Visible)
                    VisibleSmokePuffCount++;
            }

            float debrisLife = Mathf.Clamp(life / 0.62f, 0.0f, 1.0f);
            float debrisAlpha = 0.95f * (1.0f - debrisLife);
            _debrisMaterial.AlbedoColor = new Color(_profile.FireColor, debrisAlpha);
            _debrisMaterial.Emission = _profile.FireColor;
            VisibleDebrisCount = 0;
            for (int index = 0; index < _debris.Count; index++)
            {
                MeshInstance3D piece = _debris[index];
                if (index >= _debrisCount || debrisLife >= 1.0f)
                {
                    piece.Visible = false;
                    continue;
                }

                Vector2 direction = GrenadeProfile.EmberDirection(index, MaximumDebris);
                float reach = _profile.BlastFullRadiusPx * (1.2f + GrenadeProfile.EmberReachFraction(index));
                Vector2 travel = direction * (reach * debrisLife * motion);
                travel += Vector2.Down * (_profile.BlastFullRadiusPx * 0.52f * debrisLife * debrisLife * motion);
                Vector3 local = WorldPlaneMapping.To3D(travel);
                local.Z = 2.25f;
                piece.Position = local;
                float size = Math.Max(1.5f, _profile.BlastFullRadiusPx * 0.055f * (1.0f - (0.45f * debrisLife)));
                piece.Scale = new Vector3(size * 2.2f, size * 0.55f, 1.0f);
                piece.Rotation = new Vector3(0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(direction.Angle()));
                piece.Visible = debrisAlpha > 0.02f;
                if (piece.Visible)
                    VisibleDebrisCount++;
            }
        }

        private void HideAll()
        {
            _lifeTicks = 0;
            Visible = false;
            VisibleSmokePuffCount = 0;
            VisibleDebrisCount = 0;
            if (_afterglow is not null)
                _afterglow.Visible = false;
            foreach (MeshInstance3D puff in _smoke)
                puff.Visible = false;
            foreach (MeshInstance3D piece in _debris)
                piece.Visible = false;
        }
    }
}
