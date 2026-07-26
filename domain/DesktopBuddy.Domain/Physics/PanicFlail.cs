using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Physics;

/// <summary>Panic-flail shape tuning (lengths in world px; cycle in routed ticks).</summary>
public readonly record struct PanicFlailTuning(
    float Amplitude,      // sweep reach of each hand across one arc
    float Lift,           // vertical span of the arc
    int CycleTicks,       // ticks for one full sweep — long, so the flail reads as flailing
    float Asymmetry,      // phase offset between hands so they never mirror
    float ReachBias);     // how far the arc anchors toward the pull-free direction

/// <summary>Per-hand target offsets for one tick of flailing, relative to a shoulder anchor.</summary>
public readonly record struct PanicFlailSample(
    Vector2 LeftHandOffset,
    Vector2 RightHandOffset);

/// <summary>
/// The flailing reach of a frightened, grabbed buddy trying to pull itself free (RAGDOLL §4
/// priority 4; owner feel notes 2026-07-25).
///
/// <para><b>This is a purposeful reach, not a vibration.</b> The arc is anchored
/// <see cref="PanicFlailTuning.ReachBias"/> px in the direction the buddy is straining —
/// away from the grab point — so a free hand looks like it is grabbing for purchase to haul
/// itself loose, and only sweeps around that offset anchor. Both hands bias the same way;
/// the caller decides which hands are free to drive, since a grabbed hand belongs to the
/// tether and must not be fought by a spring.</para>
///
/// <para><b>Slow and wide.</b> One smooth sine per axis over a long cycle, quarter-phase
/// offset so each hand traces an arc rather than a line. An earlier version added a third
/// harmonic and shortened the cycle with fear; it read as random back-and-forth spam, so
/// both are gone. Fear scales how far the hands reach and sweep, never how fast they
/// twitch.</para>
///
/// <para><b>Deterministic, not random.</b> Purely a function of the routed tick count, so it
/// draws from no RNG stream and replays identically in headless scenarios
/// (ARCHITECTURE §23).</para>
/// </summary>
public static class PanicFlail
{
    /// <summary>
    /// Samples the flail at <paramref name="tick"/> for a fear level in [0,1].
    /// </summary>
    /// <param name="reachDirection">
    /// Signed direction the buddy is straining toward (-1 or +1) — away from the grab point.
    /// The arc anchors this way so the reach reads as pulling free.
    /// </param>
    public static PanicFlailSample Sample(
        int tick,
        float fear,
        float reachDirection,
        in PanicFlailTuning tuning)
    {
        fear = Math.Clamp(fear, 0.0f, 1.0f);
        if (fear <= 0.0f || tuning.CycleTicks <= 0)
        {
            return new PanicFlailSample(Vector2.Zero, Vector2.Zero);
        }

        // Integer tick math keeps this frame-pacing proof (never rendered-frame derived).
        // The cycle length is fixed: panic makes the reach bigger, not faster.
        float phase = (tick % tuning.CycleTicks) / (float)tuning.CycleTicks;
        float offsetPhase = phase + (Math.Clamp(tuning.Asymmetry, 0.0f, 1.0f) * 0.5f);

        float reach = Math.Clamp(reachDirection, -1.0f, 1.0f) * tuning.ReachBias * fear;
        float amplitude = tuning.Amplitude * fear;
        float lift = tuning.Lift * fear;

        return new PanicFlailSample(
            new Vector2(reach + (Sine(phase) * amplitude), -Arc(phase) * lift),
            new Vector2(reach + (Sine(offsetPhase) * amplitude), -Arc(offsetPhase) * lift));
    }

    private static float Sine(float phase) => MathF.Sin(phase * MathF.Tau);

    /// <summary>
    /// Vertical component in [0,1], a quarter cycle behind the horizontal sweep so the hand
    /// traces an arc instead of sliding along a line.
    /// </summary>
    private static float Arc(float phase) => 0.5f + (0.5f * MathF.Sin((phase + 0.25f) * MathF.Tau));
}
