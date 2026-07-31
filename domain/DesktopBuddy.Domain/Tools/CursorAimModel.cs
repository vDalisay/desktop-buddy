using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Tools;

/// <summary>
/// The authored constants shared by every cursor weapon's aim (RAGDOLL §9.1).
/// Distances are pixels of pointer travel per routed tick, angles are degrees.
/// </summary>
public readonly record struct CursorAimConstants(
    /// <summary>
    /// Ticks for the smoothed pointer velocity to lose half its magnitude with the
    /// pointer at rest. Larger is heavier in the hand: the aim ignores more jitter and
    /// keeps travelling for longer after the player stops.
    /// </summary>
    float SmoothingHalfLifeTicks,

    /// <summary>
    /// Smoothed pointer speed, in pixels per routed tick, below which the aim stops
    /// steering and simply holds. This is a gate with hysteresis, not a decay: an aim
    /// below the gate never drifts back toward anything, so releasing the mouse cannot
    /// swing the weapon.
    /// </summary>
    float MinimumAimSpeed,

    /// <summary>
    /// The most the aim may turn in one routed tick. This is what makes the weapon feel
    /// like it is being steered rather than teleported: at 6 degrees a full reversal
    /// takes about a quarter of a second of deliberate travel.
    /// </summary>
    float MaxTurnDegreesPerTick,
    float DegreesPerWheelStep,
    float MaximumOffsetDegrees)
{
    public bool IsWellFormed() =>
        CursorAim.IsFinitePositive(SmoothingHalfLifeTicks) &&
        CursorAim.IsFinitePositive(MinimumAimSpeed) &&
        CursorAim.IsFinitePositive(MaxTurnDegreesPerTick) &&
        CursorAim.IsFinitePositive(DegreesPerWheelStep) &&
        CursorAim.IsFiniteNonNegative(MaximumOffsetDegrees);

    /// <summary>
    /// The fraction of the smoothed velocity carried into the next tick, derived from the
    /// authored half-life so the feel is stated in time rather than in a filter constant.
    /// </summary>
    public float SmoothingRetention =>
        MathF.Pow(2.0f, -1.0f / SmoothingHalfLifeTicks);
}

/// <summary>
/// The carried aim of one cursor weapon. Immutable: the caller stores the state it
/// was handed and feeds it back next tick, so no adapter field can drift out of
/// step with the aim an already-fired shot used.
/// </summary>
public readonly record struct CursorAimState(
    /// <summary>
    /// Exponentially smoothed pointer velocity in pixels per routed tick. Raw per-tick
    /// deltas are whole pixels, which quantizes any direction taken straight from one of
    /// them to a handful of angles; this is the signal the aim is really steered by.
    /// </summary>
    Vector2 SmoothedVelocity,

    /// <summary>
    /// The steered unit aim, <b>before</b> the wheel offset is pitched onto it.
    /// <see cref="Vector2.Zero"/> until the pointer has really travelled.
    /// </summary>
    Vector2 Forward,

    /// <summary>Accumulated wheel offset in degrees; positive aims upward.</summary>
    float OffsetDegrees)
{
    /// <summary>A weapon that has just appeared: no aim yet, no offset.</summary>
    public static CursorAimState Initial { get; } = new(Vector2.Zero, Vector2.Zero, 0.0f);

    /// <summary>True once pointer travel has established a forward.</summary>
    public bool HasAim => Forward != Vector2.Zero;
}

/// <summary>External facts for one aim tick.</summary>
public readonly record struct CursorAimInput(
    CursorAimState State,

    /// <summary>Pointer travel this tick, in pixels; not normalized.</summary>
    Vector2 Motion,

    /// <summary>Wheel steps this tick: positive up, negative down.</summary>
    int WheelSteps,
    CursorAimConstants Constants);

/// <summary>Allocation-free result for one aim tick.</summary>
public readonly record struct CursorAimResult(
    CursorAimState State,

    /// <summary>Unit aim direction with the wheel offset applied.</summary>
    Vector2 Forward,

    /// <summary>The offset actually in force, in degrees; positive aims upward.</summary>
    float OffsetDegrees,

    /// <summary>True on the tick the aim started steering again and dropped a live offset.</summary>
    bool OffsetCleared,

    /// <summary>True while the smoothed pointer speed is at or above the aiming gate.</summary>
    bool IsSteering,

    /// <summary>Smoothed pointer speed in pixels per routed tick, for readouts.</summary>
    float SmoothedSpeed,

    /// <summary>False when the weapon has no aim yet, or the inputs were malformed.</summary>
    bool IsValid);

/// <summary>
/// Shared aim for the cursor weapons (RAGDOLL §9.1): the weapon follows the direction the
/// pointer has lately been travelling, the wheel offsets that aim up or down, and moving
/// again clears the offset. Pure and engine-free, so the whole lifecycle is provable from
/// seeded input.
///
/// <para>The aim is deliberately <b>not</b> the latest pointer delta. Raw per-tick deltas
/// are near-integer pixel counts, so normalizing one snaps the aim to a handful of angles
/// — 0°, 26.6°, 45° — and it teleports between them every tick, which is what the owner
/// reported as "choppy, as if locked to different axes". Instead a smoothed velocity is
/// accumulated (so sub-pixel and slow travel steer as well as fast travel does), a gate
/// decides whether the player is aiming at all, and the aim <i>slews</i> toward the target
/// at a bounded rate. The weapon visibly steers, and it holds still when the hand does.</para>
///
/// <para>The offset is a <b>pitch</b> rather than a fixed rotation: "up" is the same
/// direction on screen whichever way the weapon points, so a positive offset lifts the
/// muzzle of a weapon aimed left exactly as it does one aimed right. Encoding it as a plain
/// rotation would send one of the two directions into the floor.</para>
/// </summary>
public static class CursorAim
{
    private const float DegreesToRadians = MathF.PI / 180.0f;
    private const float TwoPi = MathF.PI * 2.0f;

    /// <summary>Horizontal component below which an aim counts as purely vertical.</summary>
    private const float VerticalEpsilon = 1e-4f;

    public static CursorAimResult Tick(in CursorAimInput input)
    {
        CursorAimConstants constants = input.Constants;
        if (!constants.IsWellFormed() || !IsFinite(input.Motion))
        {
            return Inert(input.State);
        }

        CursorAimState state = input.State;
        float retention = constants.SmoothingRetention;
        Vector2 smoothed =
            (state.SmoothedVelocity * retention) + (input.Motion * (1.0f - retention));
        float speed = smoothed.Length();
        if (!float.IsFinite(speed))
        {
            return Inert(state);
        }

        // Hysteresis on the smoothed signal rather than on the raw delta: a hand that has
        // stopped decays below the gate and the aim holds there, and a hand creeping along
        // at a fraction of a pixel per tick still crosses it, which the old raw threshold
        // could never do — its floor was a full pixel per tick, 120 px/s.
        bool steering = speed >= constants.MinimumAimSpeed;
        bool wasSteering = state.SmoothedVelocity.Length() >= constants.MinimumAimSpeed;

        float offset = state.OffsetDegrees;
        bool offsetCleared = false;
        if (steering && !wasSteering && offset != 0.0f)
        {
            // "The next non-trivial movement drops the offset", restated against the
            // smoothed signal: the tick the player starts aiming again.
            offset = 0.0f;
            offsetCleared = true;
        }

        // Applied after the clear so a notch scrolled on the very tick the aim wakes up is
        // honoured rather than swallowed. Scrolling mid-sweep accumulates too — refusing
        // live input would read as a broken wheel — and it survives until the aim next
        // comes to rest and starts again.
        if (input.WheelSteps != 0)
        {
            float requested =
                offset + (input.WheelSteps * constants.DegreesPerWheelStep);
            offset = Math.Clamp(
                requested, -constants.MaximumOffsetDegrees, constants.MaximumOffsetDegrees);
        }

        Vector2 forward = state.Forward;
        if (steering)
        {
            Vector2 target = smoothed / speed;
            forward = forward == Vector2.Zero
                ? target
                : Slew(forward, target, constants.MaxTurnDegreesPerTick);
        }

        var next = new CursorAimState(smoothed, forward, offset);
        if (!next.HasAim)
        {
            // Nothing has been aimed yet, so there is nothing to offset from. The wheel
            // state is still carried, so a player who scrolls first is not ignored.
            return new CursorAimResult(
                next, Vector2.Zero, offset, offsetCleared, steering, speed, IsValid: false);
        }

        Vector2 pitched = ApplyPitch(forward, offset);
        if (!IsFinite(pitched) || pitched == Vector2.Zero)
        {
            return Inert(next);
        }

        return new CursorAimResult(
            next, pitched, offset, offsetCleared, steering, speed, IsValid: true);
    }

    /// <summary>
    /// Rotates a unit forward so that a positive <paramref name="offsetDegrees"/>
    /// raises it on screen. Screen Y grows downward, so raising a rightward aim is a
    /// negative rotation and raising a leftward aim is a positive one; the horizontal
    /// side the weapon already points to decides which.
    ///
    /// <para>A forward that is purely vertical has no side to pitch about — the aim is
    /// already at the extreme the offset is reaching for — so it is left alone rather
    /// than being spun about an arbitrary choice of side.</para>
    /// </summary>
    public static Vector2 ApplyPitch(Vector2 forward, float offsetDegrees)
    {
        if (!IsFinite(forward) || forward == Vector2.Zero || !float.IsFinite(offsetDegrees))
        {
            return forward;
        }

        // A forward within a rounding error of vertical has no side to pitch about either:
        // the sign of a value that small is float noise, and pitching on it would swing the
        // aim by the full offset in a direction nothing chose.
        if (offsetDegrees == 0.0f || MathF.Abs(forward.X) < VerticalEpsilon)
        {
            return forward;
        }

        float radians = -MathF.Sign(forward.X) * offsetDegrees * DegreesToRadians;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector2(
            (forward.X * cos) - (forward.Y * sin),
            (forward.X * sin) + (forward.Y * cos));
    }

    /// <summary>
    /// Turns <paramref name="forward"/> toward <paramref name="target"/> by at most
    /// <paramref name="maxTurnDegrees"/>, the short way around. The result is built from
    /// the angle rather than interpolated, so every intermediate aim is exactly unit
    /// length and a reversal cannot shrink the aim to nothing on its way past.
    /// </summary>
    private static Vector2 Slew(Vector2 forward, Vector2 target, float maxTurnDegrees)
    {
        float current = MathF.Atan2(forward.Y, forward.X);
        float wanted = MathF.Atan2(target.Y, target.X);
        float limit = maxTurnDegrees * DegreesToRadians;
        float delta = WrapToPi(wanted - current);
        if (delta == 0.0f)
        {
            // Already there. Rebuilding the vector would be a no-op in intent and not in
            // arithmetic: cos(-pi/2) is not quite zero, and an aim that drifted a rounding
            // error off vertical has a horizontal side the wheel pitch would then act on.
            return forward;
        }

        if (delta > limit)
            delta = limit;
        else if (delta < -limit)
            delta = -limit;

        float angle = current + delta;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>Folds an angle difference into (-pi, pi] so a turn takes the short way.</summary>
    private static float WrapToPi(float radians)
    {
        float wrapped = radians % TwoPi;
        if (wrapped > MathF.PI)
            wrapped -= TwoPi;
        else if (wrapped <= -MathF.PI)
            wrapped += TwoPi;
        return wrapped;
    }

    private static CursorAimResult Inert(CursorAimState state) =>
        new(
            state,
            Vector2.Zero,
            state.OffsetDegrees,
            OffsetCleared: false,
            IsSteering: false,
            SmoothedSpeed: 0.0f,
            IsValid: false);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    internal static bool IsFinitePositive(float value) =>
        float.IsFinite(value) && value > 0.0f;

    internal static bool IsFiniteNonNegative(float value) =>
        float.IsFinite(value) && value >= 0.0f;
}
