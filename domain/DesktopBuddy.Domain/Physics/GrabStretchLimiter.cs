using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Physics;

/// <summary>How far through the stretch → shake → snap sequence a grabbed limb is.</summary>
public enum GrabStretchState
{
    /// <summary>Inside the stretch limit; an ordinary tether pull.</summary>
    Slack,

    /// <summary>Held at the limit and vibrating; the snap countdown is running.</summary>
    Straining,

    /// <summary>The countdown finished this tick: release and fling.</summary>
    Snapped,
}

/// <summary>
/// Elastic-limb tuning (owner feel request 2026-07-25).
/// </summary>
/// <param name="LimitHandWidths">
/// Maximum arm extension, in hand widths, measured from the shoulder anchor to the hand
/// centre. Expressed in hand widths rather than pixels because that is how the limit was
/// specified and it stays correct if the rig is rescaled.
/// </param>
/// <param name="ShakeTicks">Routed ticks held at the limit before the limb snaps back.</param>
/// <param name="ShakeAmplitude">Peak vibration offset of the strained limb, in px.</param>
/// <param name="ShakeCycleTicks">Ticks per vibration cycle — short: this is a buzz, not a sweep.</param>
/// <param name="ShakeRampTicks">
/// Ticks before the snap over which the buzz escalates, telegraphing the pop. The limb shakes
/// visibly harder and faster through this window so the snap never feels arbitrary.
/// </param>
/// <param name="ShakeRampMultiplier">Peak amplitude multiplier reached at the moment of snapping.</param>
/// <param name="ReleaseHysteresis">
/// How far back inside the limit the player must ease off to cancel the countdown. Without
/// it a limb hovering exactly at the limit would arm and disarm every tick.
/// </param>
/// <param name="SnapImpulseBase">Fling impulse applied even at a bare-minimum overpull.</param>
/// <param name="SnapImpulsePerOverpullPixel">
/// Extra fling impulse per pixel the player pulled <i>beyond</i> the limit. This is what makes
/// a harder pull fling harder: the limb cannot travel further, so the surplus demand is what
/// gets stored and released.
/// </param>
/// <param name="MaximumSnapImpulse">Bound on the fling so no pull can launch the buddy absurdly.</param>
public readonly record struct GrabStretchTuning(
    float LimitHandWidths,
    int ShakeTicks,
    float ShakeAmplitude,
    int ShakeCycleTicks,
    int ShakeRampTicks,
    float ShakeRampMultiplier,
    float ReleaseHysteresis,
    float SnapImpulseBase,
    float SnapImpulsePerOverpullPixel,
    float MaximumSnapImpulse)
{
    public static GrabStretchTuning Default => new(
        LimitHandWidths: 5.0f,
        ShakeTicks: 360,          // 3 s at 120 Hz
        ShakeAmplitude: 3.5f,
        ShakeCycleTicks: 6,
        ShakeRampTicks: 120,      // the final 1 s escalates
        ShakeRampMultiplier: 3.4f,
        ReleaseHysteresis: 8.0f,
        // Calibrated against the 2.5 torso mass: a bare snap is a ~120 px/s nudge, a hard
        // pull lands near 480 px/s — well under the 900 px/s throw cap.
        SnapImpulseBase: 300.0f,
        SnapImpulsePerOverpullPixel: 3.0f,
        MaximumSnapImpulse: 1_200.0f);
}

/// <summary>One tick of the stretch limiter.</summary>
/// <param name="State">Current phase of the sequence.</param>
/// <param name="ClampedTarget">
/// Where the tether is allowed to pull the limb — the cursor while slack, the point on the
/// limit circle while straining. The limb physically stops here, which is the whole point.
/// </param>
/// <param name="ShakeOffset">Vibration to add to the clamped target while straining.</param>
/// <param name="ShakeTicksRemaining">Routed ticks left before the snap.</param>
/// <param name="Overpull">How far past the limit the cursor currently is, in px.</param>
/// <param name="SnapImpulse">
/// Fling impulse magnitude, non-zero only on the <see cref="GrabStretchState.Snapped"/> tick.
/// </param>
/// <param name="SnapDirection">
/// Unit direction of the fling: from the shoulder anchor toward where the limb was being held.
/// The stored elastic energy hauls the body after the hand, so the buddy is flung the way it
/// was being stretched.
/// </param>
public readonly record struct GrabStretchResult(
    GrabStretchState State,
    Vector2 ClampedTarget,
    Vector2 ShakeOffset,
    int ShakeTicksRemaining,
    float Overpull,
    float SnapImpulse,
    Vector2 SnapDirection);

/// <summary>
/// Makes a grabbed limb behave like an elastic band with a real end stop (owner feel request
/// 2026-07-25): it stretches only so far, buzzes under strain for a fixed few seconds, then
/// snaps back and flings the buddy after it.
///
/// <para><b>The limit is a clamp, not a force.</b> While straining, the tether is told to pull
/// toward the point on the limit circle rather than the cursor, so no amount of stiffness can
/// drag the hand further. That keeps the arm length bounded without fighting the passive
/// constraint springs that hold the puppet together.</para>
///
/// <para><b>Pull hardness is surplus demand.</b> Since travel is capped, "pulling harder"
/// shows up as the cursor sitting further beyond the limit. The limiter tracks the
/// <i>peak</i> overpull across the strain, so a player who yanks hard once and eases off still
/// earns the big fling, and the launch scales with how hard they actually pulled.</para>
///
/// <para><b>Easing off cancels.</b> Coming back inside the limit by
/// <see cref="GrabStretchTuning.ReleaseHysteresis"/> resets the countdown and the peak, so a
/// gentle drag never accidentally detonates and a limb resting at the limit cannot arm and
/// disarm every tick.</para>
///
/// <para>Deterministic and allocation-free: the buzz is a function of the routed tick count,
/// drawn from no RNG stream (ARCHITECTURE §23).</para>
/// </summary>
public sealed class GrabStretchLimiter
{
    private const float Epsilon = 0.00001f;

    private readonly GrabStretchTuning _tuning;

    private int _strainTicks;
    private float _peakOverpull;
    private bool _snapped;

    public GrabStretchLimiter(GrabStretchTuning? tuning = null)
    {
        _tuning = tuning ?? GrabStretchTuning.Default;
    }

    public GrabStretchState State { get; private set; } = GrabStretchState.Slack;

    /// <summary>Routed ticks the limb has been held at the limit.</summary>
    public int StrainTicks => _strainTicks;

    /// <summary>Largest overpull seen during the current strain, in px.</summary>
    public float PeakOverpull => _peakOverpull;

    /// <summary>The stretch limit in px for a given hand radius.</summary>
    public float LimitFor(float handRadius) => _tuning.LimitHandWidths * handRadius * 2.0f;

    /// <summary>
    /// Advances one routed tick.
    /// </summary>
    /// <param name="anchor">Shoulder anchor the limb hangs from.</param>
    /// <param name="cursor">Where the player is pulling.</param>
    /// <param name="handRadius">Grabbed part radius; one hand width is twice this.</param>
    public GrabStretchResult Tick(Vector2 anchor, Vector2 cursor, float handRadius)
    {
        if (_snapped)
        {
            // The sequence is finished; the caller is expected to have released the grab.
            return new GrabStretchResult(
                GrabStretchState.Slack, cursor, Vector2.Zero, 0, 0.0f, 0.0f, Vector2.Zero);
        }

        float limit = LimitFor(handRadius);
        Vector2 reach = cursor - anchor;
        float distance = reach.Length();
        Vector2 direction = distance > Epsilon ? reach / distance : Vector2.UnitX;

        if (distance <= limit)
        {
            bool easedOff = distance <= limit - _tuning.ReleaseHysteresis;
            if (easedOff)
            {
                _strainTicks = 0;
                _peakOverpull = 0.0f;
                State = GrabStretchState.Slack;
                return new GrabStretchResult(
                    GrabStretchState.Slack, cursor, Vector2.Zero, 0, 0.0f, 0.0f, Vector2.Zero);
            }

            // Inside the limit but still within the dead band: hold the countdown where it is
            // rather than resetting, so a hand jittering at the limit keeps straining.
            if (State != GrabStretchState.Straining)
            {
                return new GrabStretchResult(
                    GrabStretchState.Slack, cursor, Vector2.Zero, 0, 0.0f, 0.0f, Vector2.Zero);
            }
        }

        float overpull = MathF.Max(0.0f, distance - limit);
        _peakOverpull = MathF.Max(_peakOverpull, overpull);
        _strainTicks++;
        State = GrabStretchState.Straining;

        Vector2 clamped = anchor + (direction * limit);
        int remaining = Math.Max(0, _tuning.ShakeTicks - _strainTicks);

        if (remaining > 0)
        {
            return new GrabStretchResult(
                GrabStretchState.Straining,
                clamped,
                Shake(_strainTicks, remaining, direction),
                remaining,
                overpull,
                0.0f,
                direction);
        }

        // Countdown done: snap back and fling.
        _snapped = true;
        State = GrabStretchState.Snapped;
        float impulse = MathF.Min(
            _tuning.MaximumSnapImpulse,
            _tuning.SnapImpulseBase + (_peakOverpull * _tuning.SnapImpulsePerOverpullPixel));

        return new GrabStretchResult(
            GrabStretchState.Snapped,
            clamped,
            Vector2.Zero,
            0,
            overpull,
            impulse,
            direction);
    }

    /// <summary>Clears the sequence. Called on release, re-grab, and hard reposition.</summary>
    public void Reset()
    {
        _strainTicks = 0;
        _peakOverpull = 0.0f;
        _snapped = false;
        State = GrabStretchState.Slack;
    }

    /// <summary>
    /// Buzz perpendicular to the stretch, so a strained arm vibrates across its own length
    /// like a plucked band rather than pumping along it.
    ///
    /// <para>The buzz escalates over the last <see cref="GrabStretchTuning.ShakeRampTicks"/> —
    /// wider <i>and</i> faster — so the snap is telegraphed instead of arriving out of nowhere
    /// (owner feel request 2026-07-25).</para>
    /// </summary>
    private Vector2 Shake(int tick, int remaining, Vector2 direction)
    {
        if (_tuning.ShakeCycleTicks <= 0 || _tuning.ShakeAmplitude <= 0.0f)
        {
            return Vector2.Zero;
        }

        float ramp = RampFactor(remaining);
        // Faster as well as wider: the cycle tightens toward the snap, but never below one
        // tick per cycle or the buzz would alias into a straight line.
        int cycle = Math.Max(1, (int)MathF.Round(_tuning.ShakeCycleTicks / MathF.Max(1.0f, ramp)));
        float phase = (tick % cycle) / (float)cycle;
        float magnitude = MathF.Sin(phase * MathF.Tau) * _tuning.ShakeAmplitude * ramp;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        return perpendicular * magnitude;
    }

    /// <summary>
    /// Amplitude multiplier for this tick: <c>1</c> for most of the strain, rising smoothly to
    /// <see cref="GrabStretchTuning.ShakeRampMultiplier"/> as the snap arrives.
    /// </summary>
    public float RampFactor(int remaining)
    {
        if (_tuning.ShakeRampTicks <= 0 || remaining >= _tuning.ShakeRampTicks)
        {
            return 1.0f;
        }

        float progress = 1.0f - (remaining / (float)_tuning.ShakeRampTicks);
        float peak = MathF.Max(1.0f, _tuning.ShakeRampMultiplier);
        // Eased so the escalation reads as building tension rather than a linear slide.
        return 1.0f + ((peak - 1.0f) * progress * progress);
    }
}
