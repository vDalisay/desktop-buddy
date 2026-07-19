using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>Presentation pose modes (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md core mechanism).</summary>
public enum PresentationPoseMode
{
    /// <summary>Socket transforms written 1:1 from the mapped physics bodies (exactly M3.5).</summary>
    Tracking = 0,
    /// <summary>Sockets posed as tracked body pose plus clamped authored offsets.</summary>
    Performance = 1,
}

/// <summary>
/// Semantic inputs to pose-mode arbitration, sampled on the rendered frame from existing
/// gameplay state. Presentation never writes any of these.
/// </summary>
public readonly record struct PoseModeInputs(
    bool Unconscious,
    bool RecoveryActive,
    bool GrabActive,
    bool ReactionActive,
    bool StableStanding,
    int TicksSinceImpact);

/// <summary>
/// Pure pose-mode arbitration: Tracking is forced while any physics-dominated state is
/// live or within the post-impact cooldown; Performance is allowed otherwise. The rule
/// list is the plan's, verbatim; adding a forcing state means adding an input, never a
/// special case at a call site.
/// </summary>
public static class PoseModeArbiter
{
    public static PresentationPoseMode Evaluate(in PoseModeInputs inputs, int postImpactCooldownTicks)
    {
        if (postImpactCooldownTicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(postImpactCooldownTicks), postImpactCooldownTicks,
                "Cooldown ticks must be non-negative.");
        }

        bool tracking = inputs.Unconscious ||
            inputs.RecoveryActive ||
            inputs.GrabActive ||
            inputs.ReactionActive ||
            !inputs.StableStanding ||
            inputs.TicksSinceImpact < postImpactCooldownTicks;
        return tracking ? PresentationPoseMode.Tracking : PresentationPoseMode.Performance;
    }
}

/// <summary>
/// Time-based tracking-to-performance blend weight. Eases 0 to 1 over the profile
/// duration while Performance is allowed; snaps instantly to 0 when Tracking is forced
/// (the cut back to raw physics must never smear). Time-based, never frame-count-based
/// (display-rate independence).
/// </summary>
public sealed class PerformanceBlend
{
    private readonly float _blendSeconds;

    public PerformanceBlend(float blendSeconds)
    {
        if (!float.IsFinite(blendSeconds) || blendSeconds <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blendSeconds), blendSeconds, "Blend duration must be finite and positive.");
        }

        _blendSeconds = blendSeconds;
    }

    /// <summary>Current performance weight in [0, 1]; 0 is pure tracking.</summary>
    public float Weight { get; private set; }

    public float Update(double deltaSeconds, PresentationPoseMode mode)
    {
        if (mode == PresentationPoseMode.Tracking)
        {
            Weight = 0.0f;
            return Weight;
        }

        if (deltaSeconds > 0.0)
        {
            Weight = Math.Clamp(Weight + (float)(deltaSeconds / _blendSeconds), 0.0f, 1.0f);
        }

        return Weight;
    }

    public void Reset() => Weight = 0.0f;
}

/// <summary>
/// Clamps an authored visual offset so the final pose can never stray from the tracked
/// physics body by more than the profile cap (plan prime invariant 2: visuals decorate
/// the truth but never leave it). Component math only — engine-free.
/// </summary>
public static class BoundedOffset
{
    /// <summary>Clamps the offset vector's magnitude to <paramref name="cap"/>.</summary>
    public static (float X, float Y, float Z) Clamp(float x, float y, float z, float cap)
    {
        if (!float.IsFinite(cap) || cap < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cap), cap, "Offset cap must be finite and non-negative.");
        }

        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        {
            return (0.0f, 0.0f, 0.0f);
        }

        float lengthSquared = (x * x) + (y * y) + (z * z);
        if (lengthSquared <= cap * cap)
        {
            return (x, y, z);
        }

        float scale = cap / MathF.Sqrt(lengthSquared);
        return (x * scale, y * scale, z * scale);
    }
}

/// <summary>
/// Pure-logic image of the M3.6 expression/performance tuning resource. The Godot
/// <c>BuddyExpressionProfile</c> copies its exported fields here and delegates
/// validation, mirroring the <see cref="BuddyLookData"/> pattern so the numeric
/// contract is covered by fast dotnet tests.
/// </summary>
public readonly record struct ExpressionTuningData(
    float PerformanceBlendSeconds,
    int PostImpactCooldownTicks,
    float OffsetCapRadiusFraction,
    float FacingYawDegrees,
    float FacingTurnSeconds,
    int FacingWalkCommitTicks,
    float FacingWalkDeadband,
    int FacingIdleFlipMinimumTicks,
    int FacingIdleFlipMaximumTicks)
{
    /// <summary>Plan prime invariant 2: the per-part offset cap may never exceed half the part radius.</summary>
    public const float MaximumOffsetCapRadiusFraction = 0.5f;

    // A blend longer than this stops reading as an ease and starts reading as lag.
    public const float MaximumPerformanceBlendSeconds = 2.0f;

    // Generous garbage bound only: ten seconds of forced tracking after a poke would
    // make performance mode effectively unreachable during play.
    public const int MaximumPostImpactCooldownTicks = 10 * 120;

    // The owner-accepted three-quarter read is about 30 degrees; anything approaching
    // a full profile (90) contradicts the 2026-07-15 Variant C decision.
    public const float MaximumFacingYawDegrees = 45.0f;

    // A turn slower than this reads as broken, not eased.
    public const float MaximumFacingTurnSeconds = 2.0f;

    // Hysteresis beyond five seconds would make walking feel unacknowledged.
    public const int MaximumFacingWalkCommitTicks = 5 * 120;

    // Idle side flips are ambient variety; anything past two minutes is effectively off.
    public const int MaximumFacingIdleFlipTicks = 120 * 120;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!float.IsFinite(PerformanceBlendSeconds) ||
            PerformanceBlendSeconds <= 0.0f ||
            PerformanceBlendSeconds > MaximumPerformanceBlendSeconds)
        {
            errors.Add($"performance blend seconds must be finite within (0-{MaximumPerformanceBlendSeconds:0.0}]");
        }

        if (PostImpactCooldownTicks < 0 || PostImpactCooldownTicks > MaximumPostImpactCooldownTicks)
        {
            errors.Add($"post-impact cooldown ticks must be within 0-{MaximumPostImpactCooldownTicks}");
        }

        if (!float.IsFinite(OffsetCapRadiusFraction) ||
            OffsetCapRadiusFraction <= 0.0f ||
            OffsetCapRadiusFraction > MaximumOffsetCapRadiusFraction)
        {
            errors.Add($"offset cap radius fraction must be finite within (0-{MaximumOffsetCapRadiusFraction:0.0}]");
        }

        if (!float.IsFinite(FacingYawDegrees) ||
            FacingYawDegrees <= 0.0f ||
            FacingYawDegrees > MaximumFacingYawDegrees)
        {
            errors.Add($"facing yaw degrees must be finite within (0-{MaximumFacingYawDegrees:0})");
        }

        if (!float.IsFinite(FacingTurnSeconds) ||
            FacingTurnSeconds <= 0.0f ||
            FacingTurnSeconds > MaximumFacingTurnSeconds)
        {
            errors.Add($"facing turn seconds must be finite within (0-{MaximumFacingTurnSeconds:0.0}]");
        }

        if (FacingWalkCommitTicks < 1 || FacingWalkCommitTicks > MaximumFacingWalkCommitTicks)
        {
            errors.Add($"facing walk commit ticks must be within 1-{MaximumFacingWalkCommitTicks}");
        }

        if (!float.IsFinite(FacingWalkDeadband) ||
            FacingWalkDeadband < 0.0f ||
            FacingWalkDeadband >= 1.0f)
        {
            errors.Add("facing walk deadband must be finite within [0-1)");
        }

        if (FacingIdleFlipMinimumTicks < 1 ||
            FacingIdleFlipMaximumTicks <= FacingIdleFlipMinimumTicks ||
            FacingIdleFlipMaximumTicks > MaximumFacingIdleFlipTicks)
        {
            errors.Add($"facing idle flip ticks must satisfy 1 <= minimum < maximum <= {MaximumFacingIdleFlipTicks}");
        }

        return errors;
    }

    /// <summary>The facing subset consumed by the pure <see cref="FacingModel"/>.</summary>
    public FacingParameters ToFacingParameters() => new(
        FacingYawDegrees,
        FacingTurnSeconds,
        FacingWalkCommitTicks,
        FacingWalkDeadband,
        FacingIdleFlipMinimumTicks,
        FacingIdleFlipMaximumTicks);
}
