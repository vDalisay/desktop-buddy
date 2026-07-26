using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Typed empirical tuning for sensing and bounded object actuation. Semantic
/// lifecycle durations are converted to the Godot-free domain tuning.
/// </summary>
[GlobalClass]
public partial class ObjectInteractionProfile : GameResource
{
    [Export(PropertyHint.Range, "16,512,1")] public float SenseRadius { get; set; } = 220.0f;
    [Export(PropertyHint.Range, "1,256,0.5")] public float CatchDistance { get; set; } = 46.0f;
    [Export(PropertyHint.Range, "1,512,0.5")] public float ApproachDistance { get; set; } = 220.0f;
    [Export(PropertyHint.Range, "1,600,1")] public int CatchTimeoutTicks { get; set; } = 90;
    [Export(PropertyHint.Range, "1,1200,1")] public int HoldTicks { get; set; } = 120;
    [Export(PropertyHint.Range, "1,1200,1")] public int InspectTicks { get; set; } = 150;

    /// <summary>Shoulder-line origin every reach is measured and clamped from.</summary>
    [Export] public Vector2 ReachOriginOffset { get; set; } = new(0.0f, -6.0f);

    /// <summary>
    /// Natural arm's length from <see cref="ReachOriginOffset"/>. The shipped rig rests its
    /// hands `38 px` out, so this is barely beyond a relaxed arm.
    /// </summary>
    [Export(PropertyHint.Range, "8,256,0.5")] public float ReachRadius { get; set; } = 44.0f;

    /// <summary>
    /// How far past <see cref="ReachRadius"/> a hand target may ever be placed. Every hand
    /// target is clamped into this circle, so no command can ask the arms to stretch across
    /// the room however far away the object is (owner correction 2026-07-26).
    /// </summary>
    [Export(PropertyHint.Range, "0,64,0.5")] public float MaximumReachExtension { get; set; } = 6.0f;

    /// <summary>Slack added to hand+object radii when testing whether the catch touched.</summary>
    [Export(PropertyHint.Range, "0,64,0.5")] public float CatchContactTolerance { get; set; } = 4.0f;

    /// <summary>Routed ticks the scoop dip plays before a resting object attaches.</summary>
    [Export(PropertyHint.Range, "1,240,1")] public int ScoopTicks { get; set; } = 24;

    /// <summary>Bounded downward force that dips the torso and head during a scoop.</summary>
    [Export(PropertyHint.Range, "0,100000,1")] public float ScoopDipForce { get; set; } = 2_400.0f;

    /// <summary>Routed ticks the hand draws back before the return throw releases.</summary>
    [Export(PropertyHint.Range, "1,240,1")] public int ThrowWindupTicks { get; set; } = 14;

    [Export(PropertyHint.Range, "0,128,0.5")] public float CatchHandClearance { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,128,0.5")] public float CatchConfirmDistance { get; set; } = 12.0f;
    /// <summary>
    /// How far a held object may separate from the hold centre before the grip counts as
    /// physically lost. Without this the hold is unconditional and nothing — a glove
    /// strike, a shove, a fall — can knock an object out of the buddy's hands, which also
    /// makes the model's interrupted-meal drop path unreachable (FR-008.10).
    /// </summary>
    [Export(PropertyHint.Range, "1,512,0.5")] public float HoldReleaseDistance { get; set; } = 72.0f;
    [Export] public Vector2 HoldCenterOffset { get; set; } = new(0.0f, -24.0f);
    [Export(PropertyHint.Range, "0,64,0.5")] public float HoldHandHalfSeparation { get; set; } = 15.0f;

    [Export(PropertyHint.Range, "0,100000,0.1")] public float HandStiffness { get; set; } = 1_800.0f;
    [Export(PropertyHint.Range, "0,10000,0.1")] public float HandDamping { get; set; } = 65.0f;
    /// <summary>
    /// Reduced from `18000` at owner correction 2026-07-26. Combined with the clamped
    /// reach envelope, the hands now ease out rather than being yanked to a target.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,200000,0.1")] public float MaximumHandForce { get; set; } = 6_000.0f;

    /// <summary>Forward distance ahead of the reach origin that counts as blocked.</summary>
    [Export(PropertyHint.Range, "1,256,0.5")] public float ObstacleForwardWindow { get; set; } = 52.0f;

    [Export(PropertyHint.Range, "0,10000,1")] public float TossImpulse { get; set; } = 720.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float TossLiftImpulse { get; set; } = 240.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float DiscardImpulse { get; set; } = 180.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float DiscardLiftImpulse { get; set; } = 40.0f;

    /// <summary>
    /// Routed ticks the toss gesture holds priority 5. Must exceed
    /// <see cref="ThrowWindupTicks"/> so the release beat actually happens.
    /// </summary>
    [Export(PropertyHint.Range, "2,600,1")] public int TossTicks { get; set; } = 20;

    public ObjectInteractionTuning ToDomainTuning() => new(
        CatchDistance,
        ApproachDistance,
        CatchTimeoutTicks,
        HoldTicks,
        InspectTicks,
        TossTicks);

    /// <summary>The absolute limit any hand target is clamped into.</summary>
    public float MaximumReach => ReachRadius + MaximumReachExtension;

    public bool IsRuntimeValid =>
        float.IsFinite(SenseRadius) && SenseRadius > 0.0f &&
        float.IsFinite(CatchDistance) && CatchDistance > 0.0f &&
        float.IsFinite(ApproachDistance) && ApproachDistance >= CatchDistance &&
        CatchTimeoutTicks > 0 && HoldTicks > 0 && InspectTicks > 0 &&
        ReachOriginOffset.IsFinite() &&
        float.IsFinite(ReachRadius) && ReachRadius > 0.0f &&
        float.IsFinite(MaximumReachExtension) && MaximumReachExtension >= 0.0f &&
        CatchDistance <= MaximumReach &&
        float.IsFinite(CatchContactTolerance) && CatchContactTolerance >= 0.0f &&
        ScoopTicks > 0 && ThrowWindupTicks > 0 && TossTicks > ThrowWindupTicks &&
        float.IsFinite(ScoopDipForce) && ScoopDipForce >= 0.0f &&
        float.IsFinite(ObstacleForwardWindow) && ObstacleForwardWindow > 0.0f &&
        float.IsFinite(CatchHandClearance) && CatchHandClearance >= 0.0f &&
        float.IsFinite(CatchConfirmDistance) && CatchConfirmDistance >= 0.0f &&
        float.IsFinite(HoldReleaseDistance) && HoldReleaseDistance > CatchConfirmDistance &&
        HoldCenterOffset.IsFinite() &&
        float.IsFinite(HoldHandHalfSeparation) && HoldHandHalfSeparation >= 0.0f &&
        float.IsFinite(HandStiffness) && HandStiffness >= 0.0f &&
        float.IsFinite(HandDamping) && HandDamping >= 0.0f &&
        float.IsFinite(MaximumHandForce) && MaximumHandForce > 0.0f &&
        float.IsFinite(TossImpulse) && TossImpulse >= 0.0f &&
        float.IsFinite(TossLiftImpulse) && TossLiftImpulse >= 0.0f &&
        float.IsFinite(DiscardImpulse) && DiscardImpulse >= 0.0f &&
        float.IsFinite(DiscardLiftImpulse) && DiscardLiftImpulse >= 0.0f &&
        DiscardImpulse <= TossImpulse;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        Positive(errors, SenseRadius, nameof(SenseRadius));
        Positive(errors, CatchDistance, nameof(CatchDistance));
        Positive(errors, ApproachDistance, nameof(ApproachDistance));
        if (ApproachDistance < CatchDistance)
            errors.Add($"{nameof(ApproachDistance)} must be >= {nameof(CatchDistance)}");
        if (CatchTimeoutTicks <= 0) errors.Add($"{nameof(CatchTimeoutTicks)} must be positive");
        if (HoldTicks <= 0) errors.Add($"{nameof(HoldTicks)} must be positive");
        if (InspectTicks <= 0) errors.Add($"{nameof(InspectTicks)} must be positive");
        NonNegative(errors, CatchHandClearance, nameof(CatchHandClearance));
        NonNegative(errors, CatchConfirmDistance, nameof(CatchConfirmDistance));
        if (!float.IsFinite(HoldReleaseDistance) || HoldReleaseDistance <= CatchConfirmDistance)
        {
            errors.Add(
                $"{nameof(HoldReleaseDistance)} must be finite and greater than " +
                $"{nameof(CatchConfirmDistance)}");
        }
        if (!HoldCenterOffset.IsFinite()) errors.Add($"{nameof(HoldCenterOffset)} must be finite");
        if (!ReachOriginOffset.IsFinite()) errors.Add($"{nameof(ReachOriginOffset)} must be finite");
        Positive(errors, ReachRadius, nameof(ReachRadius));
        NonNegative(errors, MaximumReachExtension, nameof(MaximumReachExtension));
        if (CatchDistance > MaximumReach)
        {
            errors.Add(
                $"{nameof(CatchDistance)} must not exceed {nameof(ReachRadius)} + " +
                $"{nameof(MaximumReachExtension)} — the machine may not commit to a catch " +
                "the arms cannot physically reach");
        }
        NonNegative(errors, CatchContactTolerance, nameof(CatchContactTolerance));
        if (ScoopTicks <= 0) errors.Add($"{nameof(ScoopTicks)} must be positive");
        if (ThrowWindupTicks <= 0) errors.Add($"{nameof(ThrowWindupTicks)} must be positive");
        if (TossTicks <= ThrowWindupTicks)
            errors.Add($"{nameof(TossTicks)} must exceed {nameof(ThrowWindupTicks)}");
        NonNegative(errors, ScoopDipForce, nameof(ScoopDipForce));
        Positive(errors, ObstacleForwardWindow, nameof(ObstacleForwardWindow));
        NonNegative(errors, HoldHandHalfSeparation, nameof(HoldHandHalfSeparation));
        NonNegative(errors, HandStiffness, nameof(HandStiffness));
        NonNegative(errors, HandDamping, nameof(HandDamping));
        Positive(errors, MaximumHandForce, nameof(MaximumHandForce));
        NonNegative(errors, TossImpulse, nameof(TossImpulse));
        NonNegative(errors, TossLiftImpulse, nameof(TossLiftImpulse));
        NonNegative(errors, DiscardImpulse, nameof(DiscardImpulse));
        NonNegative(errors, DiscardLiftImpulse, nameof(DiscardLiftImpulse));
        if (DiscardImpulse > TossImpulse)
            errors.Add($"{nameof(DiscardImpulse)} must not exceed {nameof(TossImpulse)}");
        return errors;
    }

    private static void Positive(Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
            errors.Add($"{name} must be finite and positive");
    }

    private static void NonNegative(Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
            errors.Add($"{name} must be finite and non-negative");
    }
}
