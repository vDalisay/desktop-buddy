using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The little feather barbs that come off the duster where it meets the buddy (owner
/// instruction 2026-08-21). Tickling was silent visually: the feather swayed and the buddy
/// reacted, but nothing happened at the point of contact, so it read as hovering rather than
/// as touching him.
///
/// <para>A fixed pool of tiny quads, spawned only while the stroke reports real contact and
/// drifting up and away from wherever the vane is. Presentation only: nothing here is read by
/// the care pipeline, so a frame the player never sees costs the tickle nothing.</para>
/// </summary>
public partial class CareToolVisual3D
{
    private const int FluffCapacity = 12;
    private const double FluffEmissionSeconds = 0.045;
    private const double FluffLifetimeSeconds = 0.55;
    private const float FluffSizePx = 3.2f;
    private const float FluffRisePx = 26.0f;
    private const float FluffDriftPx = 18.0f;

    private readonly Fluff[] _fluff = new Fluff[FluffCapacity];
    private MeshInstance3D[] _fluffVisuals = System.Array.Empty<MeshInstance3D>();
    private double _fluffEmission;
    private int _nextFluff;
    private ulong _fluffSeed = 0x9E3779B97F4A7C15UL;

    /// <summary>Barbs currently in the air, for readouts and focused tests.</summary>
    public int ActiveFluffCount { get; private set; }

    /// <summary>Barbs this session has spawned, for readouts and focused tests.</summary>
    public int FluffEmissionCount { get; private set; }

    private void BuildFluff()
    {
        var mesh = new QuadMesh { Size = new Vector2(FluffSizePx, FluffSizePx) };
        _fluffVisuals = new MeshInstance3D[FluffCapacity];
        for (int index = 0; index < FluffCapacity; index++)
        {
            var instance = new MeshInstance3D
            {
                Name = $"TickleFluff{index + 1}",
                Mesh = mesh,
                MaterialOverride = new StandardMaterial3D
                {
                    ResourceName = "TickleFluffMaterial",
                    AlbedoColor = new Color(1.0f, 0.99f, 0.94f, 0.9f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled,
                },
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            AddChild(instance);
            _fluffVisuals[index] = instance;
        }
    }

    private void TickFluff(double delta)
    {
        bool emitting = _presentationActive &&
            _tool == ToolId.Tickle &&
            _held &&
            _careStroke.IsTickleContact &&
            !_reducedParticles;

        if (emitting)
        {
            _fluffEmission += delta;
            while (_fluffEmission >= FluffEmissionSeconds)
            {
                _fluffEmission -= FluffEmissionSeconds;
                Spawn(_careStroke.ContactPoint);
            }
        }
        else
        {
            _fluffEmission = 0.0;
        }

        int alive = 0;
        for (int index = 0; index < FluffCapacity; index++)
        {
            ref Fluff barb = ref _fluff[index];
            MeshInstance3D instance = _fluffVisuals[index];
            if (barb.Remaining <= 0.0)
            {
                if (instance.Visible)
                    instance.Visible = false;
                continue;
            }

            barb.Remaining -= delta;
            if (barb.Remaining <= 0.0)
            {
                instance.Visible = false;
                continue;
            }

            alive++;
            float age = 1.0f - (float)(barb.Remaining / FluffLifetimeSeconds);
            // Up and outward, easing off — a barb loses its push almost immediately and then
            // just floats, which is the whole reason a feather looks like a feather.
            float rise = FluffRisePx * (1.0f - ((1.0f - age) * (1.0f - age)));
            Vector2 world = barb.Origin + new Vector2(barb.Drift * age, -rise);
            Vector3 position = WorldPlaneMapping.To3D(world);
            position.Z = DepthOffset - 1.0f;
            instance.Position = position;
            instance.RotationDegrees = new Vector3(0.0f, 0.0f, barb.Spin * age);
            float fade = 1.0f - (age * age);
            instance.Scale = Vector3.One * Mathf.Max(0.05f, fade);
            var material = (StandardMaterial3D)instance.MaterialOverride;
            material.AlbedoColor = new Color(1.0f, 0.99f, 0.94f, 0.9f * fade);
            instance.Visible = true;
        }

        ActiveFluffCount = alive;
        // Barbs already in the air keep this node on screen after the feather is put away.
        ApplyVisibility();
    }

    private void ClearFluff()
    {
        for (int index = 0; index < FluffCapacity; index++)
        {
            _fluff[index].Remaining = 0.0;
            if (_fluffVisuals.Length == FluffCapacity && GodotObject.IsInstanceValid(_fluffVisuals[index]))
                _fluffVisuals[index].Visible = false;
        }

        _fluffEmission = 0.0;
        ActiveFluffCount = 0;
    }

    private void Spawn(Vector2 contact)
    {
        ref Fluff barb = ref _fluff[_nextFluff];
        _nextFluff = (_nextFluff + 1) % FluffCapacity;
        barb.Origin = contact + new Vector2(NextUnit() * 5.0f, NextUnit() * 5.0f);
        barb.Drift = NextUnit() * FluffDriftPx;
        barb.Spin = NextUnit() * 120.0f;
        barb.Remaining = FluffLifetimeSeconds;
        FluffEmissionCount++;
    }

    /// <summary>Deterministic −1..1; presentation jitter must not reach into gameplay's RNG.</summary>
    private float NextUnit()
    {
        _fluffSeed ^= _fluffSeed << 13;
        _fluffSeed ^= _fluffSeed >> 7;
        _fluffSeed ^= _fluffSeed << 17;
        return ((_fluffSeed >> 40) / (float)(1 << 24) * 2.0f) - 1.0f;
    }

    private struct Fluff
    {
        public Vector2 Origin;
        public float Drift;
        public float Spin;
        public double Remaining;
    }
}
