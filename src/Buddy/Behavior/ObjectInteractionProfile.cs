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

    [Export(PropertyHint.Range, "0,128,0.5")] public float CatchHandClearance { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,128,0.5")] public float CatchConfirmDistance { get; set; } = 12.0f;
    [Export] public Vector2 HoldCenterOffset { get; set; } = new(0.0f, -24.0f);
    [Export(PropertyHint.Range, "0,64,0.5")] public float HoldHandHalfSeparation { get; set; } = 15.0f;

    [Export(PropertyHint.Range, "0,100000,0.1")] public float HandStiffness { get; set; } = 1_800.0f;
    [Export(PropertyHint.Range, "0,10000,0.1")] public float HandDamping { get; set; } = 65.0f;
    [Export(PropertyHint.Range, "0.1,200000,0.1")] public float MaximumHandForce { get; set; } = 18_000.0f;
    [Export(PropertyHint.Range, "0,100000,0.1")] public float ObjectStiffness { get; set; } = 1_400.0f;
    [Export(PropertyHint.Range, "0,10000,0.1")] public float ObjectDamping { get; set; } = 55.0f;
    [Export(PropertyHint.Range, "0.1,200000,0.1")] public float MaximumObjectForce { get; set; } = 14_000.0f;

    [Export(PropertyHint.Range, "0,10000,1")] public float TossImpulse { get; set; } = 720.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float TossLiftImpulse { get; set; } = 240.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float DiscardImpulse { get; set; } = 180.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float DiscardLiftImpulse { get; set; } = 40.0f;

    public ObjectInteractionTuning ToDomainTuning() => new(
        CatchDistance,
        ApproachDistance,
        CatchTimeoutTicks,
        HoldTicks,
        InspectTicks);

    public bool IsRuntimeValid =>
        float.IsFinite(SenseRadius) && SenseRadius > 0.0f &&
        float.IsFinite(CatchDistance) && CatchDistance > 0.0f &&
        float.IsFinite(ApproachDistance) && ApproachDistance >= CatchDistance &&
        CatchTimeoutTicks > 0 && HoldTicks > 0 && InspectTicks > 0 &&
        float.IsFinite(CatchHandClearance) && CatchHandClearance >= 0.0f &&
        float.IsFinite(CatchConfirmDistance) && CatchConfirmDistance >= 0.0f &&
        HoldCenterOffset.IsFinite() &&
        float.IsFinite(HoldHandHalfSeparation) && HoldHandHalfSeparation >= 0.0f &&
        float.IsFinite(HandStiffness) && HandStiffness >= 0.0f &&
        float.IsFinite(HandDamping) && HandDamping >= 0.0f &&
        float.IsFinite(MaximumHandForce) && MaximumHandForce > 0.0f &&
        float.IsFinite(ObjectStiffness) && ObjectStiffness >= 0.0f &&
        float.IsFinite(ObjectDamping) && ObjectDamping >= 0.0f &&
        float.IsFinite(MaximumObjectForce) && MaximumObjectForce > 0.0f &&
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
        if (!HoldCenterOffset.IsFinite()) errors.Add($"{nameof(HoldCenterOffset)} must be finite");
        NonNegative(errors, HoldHandHalfSeparation, nameof(HoldHandHalfSeparation));
        NonNegative(errors, HandStiffness, nameof(HandStiffness));
        NonNegative(errors, HandDamping, nameof(HandDamping));
        Positive(errors, MaximumHandForce, nameof(MaximumHandForce));
        NonNegative(errors, ObjectStiffness, nameof(ObjectStiffness));
        NonNegative(errors, ObjectDamping, nameof(ObjectDamping));
        Positive(errors, MaximumObjectForce, nameof(MaximumObjectForce));
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
