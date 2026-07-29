using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>
/// Presentation activities (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md Task 3). None means the
/// performance layer is idle-suppressed (Tracking). Class P activities are triggered by
/// the selector itself; Eat is the slice's Class B activity, observed from the
/// gameplay-owned behavior-activity seam (M4 wires the real consume reasons later).
/// </summary>
public enum ActivityId
{
    None = 0,
    IdleBreathe = 1,
    WalkCycle = 2,
    JumpAnticipation = 3,
    Wave = 4,
    Eat = 5,

    /// <summary>
    /// "No thanks": the head-shake the buddy gives an offered item it has no appetite for,
    /// before putting the thing down (owner instruction 2026-07-29).
    /// </summary>
    Refuse = 6,
}

/// <summary>Semantic inputs sampled per rendered frame; presentation never writes them.</summary>
public readonly record struct ActivityInputs(
    bool PerformanceActive,
    float HorizontalSpeed,
    bool JumpRequested);

/// <summary>Activity tuning subset consumed by the pure selector.</summary>
public readonly record struct ActivityParameters(
    float WalkSpeedThreshold,
    float WalkCyclePixelsPerCycle,
    float JumpAnticipationSeconds,
    float WaveSeconds);

/// <summary>
/// Pure-logic image of the M3.6 activity tuning (selector timing plus the clip
/// amplitudes the Godot animator bakes into its offset tracks). Amplitudes are bounded
/// well inside the offset cap per the owner's very-subtle direction; the presenter's
/// clamp still applies on top, so even bad data cannot take a visual off its body.
/// </summary>
public readonly record struct ActivityTuningData(
    float WalkSpeedThreshold,
    float WalkCyclePixelsPerCycle,
    float JumpAnticipationSeconds,
    float WaveSeconds,
    float EatDefaultSeconds,
    float BreatheSeconds,
    float BreatheAmplitude,
    float WalkBobAmplitude,
    float WaveAmplitude,
    float ChewAmplitude,
    float JumpSquashAmplitude)
{
    // "Alive but never busy": authored amplitudes stay tiny in world pixels. The
    // smallest part cap today is ~0.5 x hand radius; six pixels already reads bold.
    public const float MaximumAmplitude = 6.0f;
    public const float MaximumSeconds = 10.0f;
    public const float MaximumWalkCyclePixels = 400.0f;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        AddPositiveBounded(errors, WalkSpeedThreshold, 1000.0f, "activity walk speed threshold");
        AddPositiveBounded(errors, WalkCyclePixelsPerCycle, MaximumWalkCyclePixels, "activity walk cycle pixels");
        AddPositiveBounded(errors, JumpAnticipationSeconds, MaximumSeconds, "activity jump anticipation seconds");
        AddPositiveBounded(errors, WaveSeconds, MaximumSeconds, "activity wave seconds");
        AddPositiveBounded(errors, EatDefaultSeconds, MaximumSeconds, "activity eat default seconds");
        AddPositiveBounded(errors, BreatheSeconds, MaximumSeconds, "activity breathe seconds");
        AddPositiveBounded(errors, BreatheAmplitude, MaximumAmplitude, "activity breathe amplitude");
        AddPositiveBounded(errors, WalkBobAmplitude, MaximumAmplitude, "activity walk bob amplitude");
        AddPositiveBounded(errors, WaveAmplitude, MaximumAmplitude, "activity wave amplitude");
        AddPositiveBounded(errors, ChewAmplitude, MaximumAmplitude, "activity chew amplitude");
        AddPositiveBounded(errors, JumpSquashAmplitude, MaximumAmplitude, "activity jump squash amplitude");
        return errors;
    }

    /// <summary>The selector subset.</summary>
    public ActivityParameters ToActivityParameters() => new(
        WalkSpeedThreshold, WalkCyclePixelsPerCycle, JumpAnticipationSeconds, WaveSeconds);

    private static void AddPositiveBounded(
        List<string> errors, float value, float maximum, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f || value > maximum)
        {
            errors.Add($"{name} must be finite within (0-{maximum:0})");
        }
    }
}

/// <summary>
/// Pure activity arbitration and walk-cycle phase math. Priority: behavior-backed Eat,
/// then the one-shot Wave request, then the JumpAnticipation squash window opened by a
/// real jump request, then WalkCycle dressing whenever measured travel speed exceeds the
/// threshold, then IdleBreathe. The walk phase advances proportionally to MEASURED
/// horizontal speed (never a physics write), so the step rate always matches travel and
/// freezes at rest — feet cannot moonwalk.
/// </summary>
public sealed class ActivitySelector
{
    private readonly ActivityParameters _parameters;
    private double _eatSecondsRemaining;
    private double _refuseSecondsRemaining;
    private double _waveSecondsRemaining;
    private double _jumpSecondsRemaining;

    public ActivitySelector(in ActivityParameters parameters)
    {
        if (!float.IsFinite(parameters.WalkSpeedThreshold) || parameters.WalkSpeedThreshold <= 0.0f ||
            !float.IsFinite(parameters.WalkCyclePixelsPerCycle) || parameters.WalkCyclePixelsPerCycle <= 0.0f ||
            !float.IsFinite(parameters.JumpAnticipationSeconds) || parameters.JumpAnticipationSeconds <= 0.0f ||
            !float.IsFinite(parameters.WaveSeconds) || parameters.WaveSeconds <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Invalid activity parameters.");
        }

        _parameters = parameters;
    }

    public ActivityId Current { get; private set; } = ActivityId.None;

    /// <summary>Walk-cycle phase in [0, 1); advances with measured travel only.</summary>
    public float WalkPhase { get; private set; }

    /// <summary>Behavior seam: requests the Class B Eat activity for a bounded duration.</summary>
    public void RequestEat(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Duration must be positive.");
        }

        _eatSecondsRemaining = durationSeconds;
    }

    /// <summary>Requests the one-shot Wave; it plays for the profile duration.</summary>
    public void RequestWave() => _waveSecondsRemaining = _parameters.WaveSeconds;

    /// <summary>
    /// Behavior seam: requests the one-shot refusal head-shake. It outranks Eat, because the
    /// point of it is that the buddy has decided <i>not</i> to eat what it is holding.
    /// </summary>
    public void RequestRefuse(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds), durationSeconds, "Duration must be positive.");
        }

        _eatSecondsRemaining = 0.0;
        _refuseSecondsRemaining = durationSeconds;
    }

    /// <summary>Cancels any behavior-backed activity (hard cut back to ambient).</summary>
    public void CancelRequests()
    {
        _eatSecondsRemaining = 0.0;
        _refuseSecondsRemaining = 0.0;
        _waveSecondsRemaining = 0.0;
        _jumpSecondsRemaining = 0.0;
    }

    public ActivityId Update(in ActivityInputs inputs, double deltaSeconds)
    {
        if (deltaSeconds > 0.0)
        {
            _eatSecondsRemaining = Math.Max(0.0, _eatSecondsRemaining - deltaSeconds);
            _refuseSecondsRemaining = Math.Max(0.0, _refuseSecondsRemaining - deltaSeconds);
            _waveSecondsRemaining = Math.Max(0.0, _waveSecondsRemaining - deltaSeconds);
            _jumpSecondsRemaining = Math.Max(0.0, _jumpSecondsRemaining - deltaSeconds);
        }

        if (inputs.JumpRequested)
        {
            _jumpSecondsRemaining = _parameters.JumpAnticipationSeconds;
        }

        if (!inputs.PerformanceActive)
        {
            // Tracking cut: ambient state is suppressed instantly; behavior-backed
            // requests keep counting down so a punched buddy does not resume a stale
            // eat long after the ragdoll settles.
            Current = ActivityId.None;
            return Current;
        }

        if (_refuseSecondsRemaining > 0.0)
        {
            Current = ActivityId.Refuse;
        }
        else if (_eatSecondsRemaining > 0.0)
        {
            Current = ActivityId.Eat;
        }
        else if (_waveSecondsRemaining > 0.0)
        {
            Current = ActivityId.Wave;
        }
        else if (_jumpSecondsRemaining > 0.0)
        {
            Current = ActivityId.JumpAnticipation;
        }
        else if (float.IsFinite(inputs.HorizontalSpeed) &&
            MathF.Abs(inputs.HorizontalSpeed) > _parameters.WalkSpeedThreshold)
        {
            Current = ActivityId.WalkCycle;
            float travel = MathF.Abs(inputs.HorizontalSpeed) * (float)deltaSeconds;
            WalkPhase = (WalkPhase + (travel / _parameters.WalkCyclePixelsPerCycle)) % 1.0f;
        }
        else
        {
            Current = ActivityId.IdleBreathe;
        }

        return Current;
    }
}
