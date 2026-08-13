using System;
using DesktopBuddy.Domain.Autonomy;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>What the head is currently watching. Rest is the neutral forward pose.</summary>
public enum LookAtSource
{
    Rest = 0,
    Cursor = 1,
    Item = 2,
    Impact = 3,
    Glance = 4,
}

/// <summary>
/// Semantic inputs to look-at arbitration, sampled per rendered frame. Points are 2D
/// world pixels (the simulation's own units); the head point is the gaze origin. The
/// engagement-range cutoff lives in the model, so the caller reports only whether a
/// tool interaction is engaged, never whether it is close enough.
/// </summary>
public readonly record struct LookAtInputs(
    bool InteractionEngaged,
    float CursorX,
    float CursorY,
    bool ItemTargetValid,
    float ItemX,
    float ItemY,
    int TicksSinceImpact,
    float ImpactX,
    float ImpactY,
    bool FaceSuppressed,
    float HeadX,
    float HeadY);

/// <summary>Look-at tuning subset consumed by the pure model.</summary>
public readonly record struct LookAtParameters(
    float ConeYawDegrees,
    float ConePitchDegrees,
    float EaseSeconds,
    float GazeDepth,
    float EngagementRange,
    int ImpactMemoryTicks,
    int GlanceIntervalMinimumTicks,
    int GlanceIntervalMaximumTicks,
    int GlanceHoldMinimumTicks,
    int GlanceHoldMaximumTicks,
    int PupilQuantizationSteps);

/// <summary>The eased head angles for this frame, in degrees.</summary>
public readonly record struct LookAtAngles(float YawDegrees, float PitchDegrees);

/// <summary>
/// Pure head look-at arbitration, cone clamping, and easing
/// (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md Task 4). Priority: an engaged tool interaction
/// whose cursor is inside the engagement range, then a valid item target, then a brief
/// memory of the last accepted impact point, then a seeded ambient glance, then rest. A
/// suppressed face (a high-priority reaction) targets rest through the normal ease.
///
/// Target angles are computed against a virtual gaze depth — yaw = atan2(dx, depth),
/// pitch = atan2(dy, depth) with (dx, dy) = target minus head in 2D world units — and
/// clamped into the cone BEFORE easing, so the eased value (a lerp between two in-cone
/// angles) can never leave the cone or overshoot. The ease restarts only when the gaze
/// ACQUIRES something new (a different source, or a freshly sampled glance); while a
/// source is held the target keeps updating, so the head follows a moving cursor instead
/// of stalling on a smoothstep that restarts every frame. Presentation-only: nothing here
/// reads or writes physics.
/// </summary>
public sealed class LookAtModel
{
    private const int GlanceSampleResolution = 1000;

    /// <summary>How much further than the acquire range a held cursor keeps the gaze.</summary>
    private const float ReleaseRangeFactor = 1.25f;

    private readonly IRandomSource _random;
    private readonly LookAtParameters _parameters;

    private float _startYawDegrees;
    private float _startPitchDegrees;
    private float _targetYawDegrees;
    private float _targetPitchDegrees;
    private double _easeProgress = 1.0;

    private LookAtSource _lastSource = LookAtSource.Rest;
    private int _glanceSerial;
    private int _lastGlanceSerial;
    private bool _glanceArmed;
    private bool _glanceActive;
    private int _glanceTicksRemaining;
    private float _glanceYawDegrees;
    private float _glancePitchDegrees;

    public LookAtModel(IRandomSource random, in LookAtParameters parameters)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        if (!IsPositiveFinite(parameters.ConeYawDegrees) ||
            !IsPositiveFinite(parameters.ConePitchDegrees) ||
            !IsPositiveFinite(parameters.EaseSeconds) ||
            !IsPositiveFinite(parameters.GazeDepth) ||
            !IsPositiveFinite(parameters.EngagementRange) ||
            parameters.ImpactMemoryTicks < 0 ||
            parameters.GlanceIntervalMinimumTicks < 1 ||
            parameters.GlanceIntervalMaximumTicks <= parameters.GlanceIntervalMinimumTicks ||
            parameters.GlanceHoldMinimumTicks < 1 ||
            parameters.GlanceHoldMaximumTicks <= parameters.GlanceHoldMinimumTicks ||
            parameters.PupilQuantizationSteps < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Invalid look-at parameters.");
        }

        _parameters = parameters;
    }

    public LookAtSource CurrentSource { get; private set; } = LookAtSource.Rest;
    public float CurrentYawDegrees { get; private set; }
    public float CurrentPitchDegrees { get; private set; }

    /// <summary>Quantized pupil offset in [-1, 1] per axis (the Task 5 face seam).</summary>
    public float PupilOffsetX { get; private set; }

    /// <summary>Quantized pupil offset in [-1, 1] per axis (the Task 5 face seam).</summary>
    public float PupilOffsetY { get; private set; }

    /// <summary>
    /// Advances arbitration by <paramref name="ticksElapsed"/> physics ticks and the ease
    /// by <paramref name="deltaSeconds"/>; returns the current cone-clamped angles.
    /// </summary>
    public LookAtAngles Update(in LookAtInputs inputs, int ticksElapsed, double deltaSeconds)
    {
        if (ticksElapsed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksElapsed), ticksElapsed, "Ticks cannot be negative.");
        }

        LookAtSource source = ResolveSource(inputs, ticksElapsed, out float targetYaw, out float targetPitch);

        // Clamp into the cone BEFORE easing so no eased sample can ever leave it.
        targetYaw = Clamp(targetYaw, _parameters.ConeYawDegrees);
        targetPitch = Clamp(targetPitch, _parameters.ConePitchDegrees);

        bool acquired = source != _lastSource || _glanceSerial != _lastGlanceSerial;
        if (acquired)
        {
            _startYawDegrees = CurrentYawDegrees;
            _startPitchDegrees = CurrentPitchDegrees;
            _easeProgress = 0.0;
            _lastSource = source;
            _lastGlanceSerial = _glanceSerial;
        }

        _targetYawDegrees = targetYaw;
        _targetPitchDegrees = targetPitch;
        CurrentSource = source;

        if (_easeProgress < 1.0 && deltaSeconds > 0.0)
        {
            _easeProgress = Math.Min(1.0, _easeProgress + (deltaSeconds / _parameters.EaseSeconds));
        }

        float eased = SmoothStep((float)_easeProgress);
        CurrentYawDegrees = _startYawDegrees + ((_targetYawDegrees - _startYawDegrees) * eased);
        CurrentPitchDegrees = _startPitchDegrees + ((_targetPitchDegrees - _startPitchDegrees) * eased);
        PupilOffsetX = Quantize(CurrentYawDegrees / _parameters.ConeYawDegrees);
        PupilOffsetY = Quantize(CurrentPitchDegrees / _parameters.ConePitchDegrees);
        return new LookAtAngles(CurrentYawDegrees, CurrentPitchDegrees);
    }

    private LookAtSource ResolveSource(
        in LookAtInputs inputs, int ticksElapsed, out float targetYaw, out float targetPitch)
    {
        targetYaw = 0.0f;
        targetPitch = 0.0f;

        // A high-priority reaction face owns the head: everything else stands down and the
        // gaze eases back to rest exactly like any other source change.
        if (inputs.FaceSuppressed)
        {
            DisarmGlance();
            return LookAtSource.Rest;
        }

        if (inputs.InteractionEngaged &&
            WithinRange(inputs.CursorX - inputs.HeadX, inputs.CursorY - inputs.HeadY))
        {
            DisarmGlance();
            Aim(inputs.CursorX - inputs.HeadX, inputs.CursorY - inputs.HeadY, out targetYaw, out targetPitch);
            return LookAtSource.Cursor;
        }

        if (inputs.ItemTargetValid)
        {
            DisarmGlance();
            Aim(inputs.ItemX - inputs.HeadX, inputs.ItemY - inputs.HeadY, out targetYaw, out targetPitch);
            return LookAtSource.Item;
        }

        if (inputs.TicksSinceImpact < _parameters.ImpactMemoryTicks)
        {
            DisarmGlance();
            Aim(inputs.ImpactX - inputs.HeadX, inputs.ImpactY - inputs.HeadY, out targetYaw, out targetPitch);
            return LookAtSource.Impact;
        }

        // Ambient: the seeded timer alternates a quiet rest interval with a held glance.
        // The glance is an ANGLE PAIR inside the cone, never a world point, so ambient
        // idling can never be mistaken for cursor tracking.
        if (!_glanceArmed)
        {
            _glanceArmed = true;
            _glanceActive = false;
            _glanceTicksRemaining = NextInterval();
        }

        _glanceTicksRemaining -= ticksElapsed;
        if (_glanceTicksRemaining <= 0)
        {
            if (_glanceActive)
            {
                _glanceActive = false;
                _glanceTicksRemaining = NextInterval();
            }
            else
            {
                _glanceActive = true;
                _glanceTicksRemaining = NextHold();
                _glanceYawDegrees = SampleAngle(_parameters.ConeYawDegrees);
                _glancePitchDegrees = SampleAngle(_parameters.ConePitchDegrees);
            }

            _glanceSerial++;
        }

        if (!_glanceActive)
        {
            return LookAtSource.Rest;
        }

        targetYaw = _glanceYawDegrees;
        targetPitch = _glancePitchDegrees;
        return LookAtSource.Glance;
    }

    private void DisarmGlance()
    {
        _glanceArmed = false;
        _glanceActive = false;
    }

    /// <summary>
    /// The engagement range, widened while the cursor is already the held source. Without
    /// the release margin a buddy walking past a resting cursor crossed the boundary every
    /// few frames, and each crossing restarted the ease between a cursor angle and an
    /// ambient one — a head visibly snapping back and forth (owner report 2026-08-13).
    /// </summary>
    private bool WithinRange(float dx, float dy)
    {
        if (!float.IsFinite(dx) || !float.IsFinite(dy))
        {
            return false;
        }

        float range = _parameters.EngagementRange *
            (_lastSource == LookAtSource.Cursor ? ReleaseRangeFactor : 1.0f);
        return (dx * dx) + (dy * dy) <= range * range;
    }

    /// <summary>Point-to-angle convention: atan2 of the world delta over the gaze depth.</summary>
    private void Aim(float dx, float dy, out float yawDegrees, out float pitchDegrees)
    {
        if (!float.IsFinite(dx) || !float.IsFinite(dy))
        {
            yawDegrees = 0.0f;
            pitchDegrees = 0.0f;
            return;
        }

        yawDegrees = RadiansToDegrees(MathF.Atan2(dx, _parameters.GazeDepth));
        pitchDegrees = RadiansToDegrees(MathF.Atan2(dy, _parameters.GazeDepth));
    }

    private int NextInterval() => _random.NextInt(
        _parameters.GlanceIntervalMinimumTicks, _parameters.GlanceIntervalMaximumTicks + 1);

    private int NextHold() => _random.NextInt(
        _parameters.GlanceHoldMinimumTicks, _parameters.GlanceHoldMaximumTicks + 1);

    private float SampleAngle(float limitDegrees) =>
        _random.NextInt(-GlanceSampleResolution, GlanceSampleResolution + 1) /
        (float)GlanceSampleResolution * limitDegrees;

    private float Quantize(float normalized)
    {
        float clamped = Math.Clamp(normalized, -1.0f, 1.0f);
        int steps = _parameters.PupilQuantizationSteps;
        return MathF.Round(clamped * steps) / steps;
    }

    private static float Clamp(float value, float limit) =>
        !float.IsFinite(value) ? 0.0f : Math.Clamp(value, -limit, limit);

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0.0f;

    private static float RadiansToDegrees(float radians) => radians * (180.0f / MathF.PI);

    private static float SmoothStep(float t) => t * t * (3.0f - (2.0f * t));
}
