using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// The legacy-circles view of a burning buddy: hot puffs on every lit part with smoke rising
/// off them. The house rule is that every visual ships in both
/// presentation modes, and <see cref="Presentation3D.FireVisual3D"/> is the frontal one.
///
/// <para>Render-only, driven entirely from routed-tick counters and from the ember's own
/// index through the golden-angle fan idiom — no generator is drawn from here, so a
/// scenario replaying a seed gets the same fire.</para>
///
/// <para>FR-017.3 lives here rather than in the component: the flicker rate is capped at
/// the profile's safe rate whenever photosensitivity-safe effects are on (which is the
/// shipped default, so the safe look is the normal look and the faster flicker is the
/// opt-out), and reduced particles thin the ember motes. Neither setting can reach the
/// burn's timing, its pain, or its mood.</para>
/// </summary>
[GlobalClass]
public partial class FireVisual2D : Node2D
{
    private FireSprayerProfile _profile = null!;
    private FireSprayerComponent _sprayer = null!;
    private EffectsSettings _settings = EffectsSettings.Default;
    private bool _presentationActive;
    private int _ticks;

    public bool IsInitialized { get; private set; }

    /// <summary>True while flames are being drawn on the buddy.</summary>
    public bool IsBurningVisible => IsInitialized && _presentationActive && _sprayer.IsBurning;

    /// <summary>The flicker rate actually in force, in Hz. The photosensitivity oracle.</summary>
    public float FlickerHz => _settings.FlickerHz(_profile.SafeFlickerHz, _profile.FullFlickerHz);

    /// <summary>Ember motes actually drawn on the last frame — the reduced-particles oracle.</summary>
    public int DrawnEmberCount { get; private set; }

    /// <summary>Flame tongues actually drawn on the last frame.</summary>
    public int DrawnFlameCount { get; private set; }

    public void Initialize(FireSprayerComponent sprayer, FireSprayerProfile profile)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(sprayer);
        ArgumentNullException.ThrowIfNull(profile);
        _sprayer = sprayer;
        _profile = profile;
        IsInitialized = true;
        QueueRedraw();
    }

    public void ApplyEffectsSettings(EffectsSettings settings)
    {
        _settings = settings;
        QueueRedraw();
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        Visible = active;
        QueueRedraw();
    }

    /// <summary>Advances the flicker phase on the owning root's routed tick.</summary>
    public void PhysicsTick()
    {
        if (!IsInitialized)
            return;

        _ticks++;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawnEmberCount = 0;
        DrawnFlameCount = 0;
        if (!IsInitialized || !_presentationActive || !_sprayer.IsBurning)
            return;

        for (int partIndex = 0; partIndex < 6; partIndex++)
        {
            var partId = (BuddyPartId)partIndex;
            if (!_sprayer.IsPartBurning(partId))
                continue;

            PuppetPartBody? part = FindPart(partId);
            if (part is not null)
                DrawPartCloud(part, partIndex);
        }
    }

    private void DrawPartCloud(PuppetPartBody part, int partIndex)
    {
        Vector2 centre = part.GlobalPosition;
        float radius = part.Radius;
        float cycleTicks = Mathf.Max(1.0f, Engine.PhysicsTicksPerSecond / FlickerHz);
        float phase = ((_ticks + (partIndex * 7)) % cycleTicks) / cycleTicks;
        float pulse = 0.5f + (0.5f * Mathf.Sin(phase * Mathf.Tau));

        // Two overlapping hot puffs use the stream's colour language and avoid the old
        // single white oval that swallowed an entire body part.
        float skirt = radius * (0.9f + (0.18f * pulse));
        DrawCircle(
            centre - new Vector2(radius * 0.22f, radius * 0.18f),
            skirt,
            new Color(_profile.FlameColor, 0.58f + (0.12f * pulse)),
            true, -1.0f, true);
        DrawCircle(
            centre + new Vector2(radius * 0.18f, -radius * 0.42f),
            skirt * 0.82f,
            new Color(_profile.FlameCoreColor, 0.72f + (0.12f * pulse)),
            true, -1.0f, true);
        DrawnFlameCount += 2;

        DrawSmoke(centre, radius, phase, partIndex);
    }

    private void DrawSmoke(Vector2 centre, float radius, float phase, int partIndex)
    {
        int count = Mathf.Clamp(_profile.EmberCount, 0, 64);
        if (count == 0)
            return;

        int stride = _settings.ParticleStride;
        float reach = radius * _profile.EmberReachFactor;
        int cycle = Mathf.Max(1, _profile.EmberCycleTicks);
        for (int index = 0; index < count; index++)
        {
            if (index % stride != 0)
                continue;

            float angle = index * 2.399963f;
            float lateral = Mathf.Cos(angle) * radius * 0.8f;
            float life = (((_ticks + (index * cycle / count) + (partIndex * 11)) % cycle) /
                          (float)cycle);
            float rise = reach * (_settings.ReducedMotion ? 0.35f : 2.0f) * life;
            float alpha = 0.52f * (1.0f - (life * life));
            var mote = new Vector2(
                centre.X + lateral + (_settings.ReducedMotion
                    ? 0.0f
                    : Mathf.Sin((life + phase) * Mathf.Tau) * radius * 0.2f),
                centre.Y - rise);
            DrawCircle(
                mote,
                Mathf.Max(1.0f, radius * (0.35f + (life * 0.75f))),
                new Color(_profile.FlameColor.Lerp(_profile.SmokeColor, life), alpha),
                true, -1.0f, true);
            DrawnEmberCount++;
        }
    }

    private PuppetPartBody? FindPart(BuddyPartId partId)
    {
        System.Collections.Generic.IReadOnlyList<PuppetPartBody> parts =
            _sprayer.Pipeline.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            if (parts[index].PartId == partId)
                return parts[index];
        }

        return null;
    }
}
