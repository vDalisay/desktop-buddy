using DesktopBuddy.App;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Typed M3.6 expressive-presentation tuning (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md).
/// Task 1 fields: the tracking-to-performance blend time, the post-impact cooldown that
/// forces Tracking after an accepted hit, and the per-part offset cap as a fraction of the
/// part radius (plan prime invariant 2 caps it at 0.5). Later M3.6 tasks extend this same
/// resource with facing, activity, look-at, and face tuning. Validation delegates into the
/// pure <see cref="ExpressionTuningData"/> so the numeric contract lives in dotnet tests.
/// </summary>
[GlobalClass]
public partial class BuddyExpressionProfile : GameResource
{
    [Export(PropertyHint.Range, "0.01,2,0.01")]
    public float PerformanceBlendSeconds { get; set; } = 0.2f;

    [Export(PropertyHint.Range, "0,1200,1")]
    public int PostImpactCooldownTicks { get; set; } = 60;

    [Export(PropertyHint.Range, "0.01,0.5,0.01")]
    public float OffsetCapRadiusFraction { get; set; } = 0.5f;

    // Task 2 facing tuning: the owner-accepted ~30-degree three-quarter states, the
    // eased turn duration, walk-direction hysteresis, and the seeded idle-variety
    // side-flip cadence (delegated engineering defaults, judged at the exit gate).
    [Export(PropertyHint.Range, "5,45,0.5")]
    public float FacingYawDegrees { get; set; } = 30.0f;

    [Export(PropertyHint.Range, "0.05,2,0.01")]
    public float FacingTurnSeconds { get; set; } = 0.5f;

    [Export(PropertyHint.Range, "1,600,1")]
    public int FacingWalkCommitTicks { get; set; } = 36;

    [Export(PropertyHint.Range, "0,0.99,0.01")]
    public float FacingWalkDeadband { get; set; } = 0.05f;

    [Export(PropertyHint.Range, "1,14400,1")]
    public int FacingIdleFlipMinimumTicks { get; set; } = 720;

    [Export(PropertyHint.Range, "2,14400,1")]
    public int FacingIdleFlipMaximumTicks { get; set; } = 1920;

    // Task 3 activity tuning: selector timing plus the very-subtle clip amplitudes the
    // animator bakes into its offset tracks (world pixels; the offset cap still clamps
    // on top). Walk dressing cycles once per WalkCyclePixels of measured travel.
    [Export(PropertyHint.Range, "1,1000,0.5")]
    public float ActivityWalkSpeedThreshold { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "4,400,1")]
    public float ActivityWalkCyclePixels { get; set; } = 48.0f;

    [Export(PropertyHint.Range, "0.05,10,0.01")]
    public float ActivityJumpAnticipationSeconds { get; set; } = 0.15f;

    [Export(PropertyHint.Range, "0.1,10,0.1")]
    public float ActivityWaveSeconds { get; set; } = 1.2f;

    [Export(PropertyHint.Range, "0.1,10,0.1")]
    public float ActivityEatDefaultSeconds { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.5,10,0.1")]
    public float ActivityBreatheSeconds { get; set; } = 3.2f;

    [Export(PropertyHint.Range, "0.1,6,0.1")]
    public float ActivityBreatheAmplitude { get; set; } = 1.2f;

    [Export(PropertyHint.Range, "0.1,6,0.1")]
    public float ActivityWalkBobAmplitude { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "0.1,6,0.1")]
    public float ActivityWaveAmplitude { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.1,6,0.1")]
    public float ActivityChewAmplitude { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0.1,6,0.1")]
    public float ActivityJumpSquashAmplitude { get; set; } = 2.5f;

    // The refusal head-shake is a deliberate "no", so it is authored wider than the wave's
    // hand beat — it still stays inside the same six-pixel amplitude bound, and the offset
    // cap clamps on top (owner instruction 2026-07-29).
    [Export(PropertyHint.Range, "0.1,6,0.1")]
    public float ActivityRefuseAmplitude { get; set; } = 5.0f;

    // Task 4 look-at tuning: the cone the head may turn inside, the acquisition ease, the
    // virtual gaze depth the target angles are measured against, how close an engaged
    // cursor must be to be worth watching, how long a hit stays interesting, the seeded
    // ambient glance cadence (delegated defaults: glance every ~4-10 s, held ~0.6-1.4 s),
    // and the pupil quantization Task 5's face consumes.
    [Export(PropertyHint.Range, "1,60,0.5")]
    public float LookConeYawDegrees { get; set; } = 28.0f;

    [Export(PropertyHint.Range, "1,45,0.5")]
    public float LookConePitchDegrees { get; set; } = 18.0f;

    [Export(PropertyHint.Range, "0.02,1,0.01")]
    public float LookEaseSeconds { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "10,600,1")]
    public float LookGazeDepthPixels { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "10,1000,1")]
    public float LookEngagementRangePixels { get; set; } = 220.0f;

    [Export(PropertyHint.Range, "0,600,1")]
    public int LookImpactMemoryTicks { get; set; } = 240;

    [Export(PropertyHint.Range, "1,14400,1")]
    public int LookGlanceIntervalMinimumTicks { get; set; } = 480;

    [Export(PropertyHint.Range, "2,14400,1")]
    public int LookGlanceIntervalMaximumTicks { get; set; } = 1200;

    [Export(PropertyHint.Range, "1,600,1")]
    public int LookGlanceHoldMinimumTicks { get; set; } = 72;

    [Export(PropertyHint.Range, "2,600,1")]
    public int LookGlanceHoldMaximumTicks { get; set; } = 168;

    [Export(PropertyHint.Range, "2,8,1")]
    public int LookPupilQuantizationSteps { get; set; } = 4;

    // Task 5 face tuning: the seeded blink cadence (delegated engineering defaults:
    // a blink every ~2-6 s held ~0.12 s, judged at the exit gate) and the eat chew
    // cycle the mouth overlay animates on. Blink counts ROUTED ticks so a pause holds it.
    [Export(PropertyHint.Range, "1,2400,1")]
    public int BlinkIntervalMinimumTicks { get; set; } = 240;

    [Export(PropertyHint.Range, "2,2400,1")]
    public int BlinkIntervalMaximumTicks { get; set; } = 720;

    [Export(PropertyHint.Range, "1,60,1")]
    public int BlinkClosedTicks { get; set; } = 14;

    [Export(PropertyHint.Range, "12,240,1")]
    public int ChewCycleTicks { get; set; } = 42;

    /// <summary>
    /// Reaction faces that suppress look-at (the gaze eases to rest while one is showing).
    /// These are <c>Reactions.CurrentFace</c> strings — the semantic face contract is
    /// unchanged; this list only names which of those faces own the head.
    /// </summary>
    [Export]
    public Godot.Collections.Array<string> LookSuppressionFaces { get; set; } =
        new() { ">_<", "x_x" };

    /// <summary>True when the current semantic face suppresses look-at.</summary>
    public bool SuppressesLookAt(string face)
    {
        if (LookSuppressionFaces is null)
        {
            return false;
        }

        foreach (string suppressed in LookSuppressionFaces)
        {
            if (suppressed == face)
            {
                return true;
            }
        }

        return false;
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (LookSuppressionFaces is null)
        {
            errors.Add("look suppression face list must be assigned");
        }

        foreach (string error in ToData().Validate())
        {
            errors.Add(error);
        }

        foreach (string error in ToActivityData().Validate())
        {
            errors.Add(error);
        }

        return errors;
    }

    /// <summary>Projects the activity exports into their pure-logic validation image.</summary>
    public ActivityTuningData ToActivityData() => new(
        ActivityWalkSpeedThreshold,
        ActivityWalkCyclePixels,
        ActivityJumpAnticipationSeconds,
        ActivityWaveSeconds,
        ActivityEatDefaultSeconds,
        ActivityBreatheSeconds,
        ActivityBreatheAmplitude,
        ActivityWalkBobAmplitude,
        ActivityWaveAmplitude,
        ActivityChewAmplitude,
        ActivityJumpSquashAmplitude,
        ActivityRefuseAmplitude);

    /// <summary>Projects the exported Godot fields into the pure-logic validation image.</summary>
    public ExpressionTuningData ToData() => new(
        PerformanceBlendSeconds,
        PostImpactCooldownTicks,
        OffsetCapRadiusFraction,
        FacingYawDegrees,
        FacingTurnSeconds,
        FacingWalkCommitTicks,
        FacingWalkDeadband,
        FacingIdleFlipMinimumTicks,
        FacingIdleFlipMaximumTicks,
        LookConeYawDegrees,
        LookConePitchDegrees,
        LookEaseSeconds,
        LookGazeDepthPixels,
        LookEngagementRangePixels,
        LookImpactMemoryTicks,
        LookGlanceIntervalMinimumTicks,
        LookGlanceIntervalMaximumTicks,
        LookGlanceHoldMinimumTicks,
        LookGlanceHoldMaximumTicks,
        LookPupilQuantizationSteps,
        BlinkIntervalMinimumTicks,
        BlinkIntervalMaximumTicks,
        BlinkClosedTicks,
        ChewCycleTicks);
}
