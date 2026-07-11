using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// Provisional bounded drive and standing-detection tuning. Confirmed recovery
/// durations remain fixed in <c>RecoveryClock</c>; only force/measurement
/// coefficients are calibrated through the laboratory.
/// </summary>
[GlobalClass]
public partial class ActiveDriveProfile : GameResource
{
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float UprightStiffness { get; set; } = 900.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float UprightDamping { get; set; } = 140.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float MaximumUprightTorque { get; set; } = 8_000.0f;
    [Export(PropertyHint.Range, "0,10,0.01,or_greater")] public float AssistedTorqueMultiplier { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float BalanceStiffness { get; set; } = 100.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float BalanceDamping { get; set; } = 25.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float MaximumBalanceForce { get; set; } = 3_000.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float SelfRightForce { get; set; } = 2_400.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float WalkForce { get; set; } = 600.0f;
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")] public float MaximumWalkSpeed { get; set; } = 55.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float GaitForce { get; set; } = 100.0f;
    [Export(PropertyHint.Range, "1,240,1")] public int GaitHalfCycleTicks { get; set; } = 18;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float JumpImpulse { get; set; } = 1_800.0f;

    [Export(PropertyHint.Range, "0.01,3.14,0.01")] public float MaximumStandingTorsoTilt { get; set; } = 0.45f;
    [Export(PropertyHint.Range, "0,128,0.1")] public float MinimumHeadAboveTorso { get; set; } = 8.0f;
    [Export(PropertyHint.Range, "0,128,0.1")] public float MinimumFeetBelowTorso { get; set; } = 12.0f;
    [Export(PropertyHint.Range, "0,256,0.1")] public float MaximumCenterOfMassError { get; set; } = 42.0f;
    [Export(PropertyHint.Range, "0,1000,0.1")] public float MaximumStandingSpeed { get; set; } = 24.0f;
    [Export(PropertyHint.Range, "1,240,1")] public int StableStandingTicks { get; set; } = 18;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        ValidatePositive(errors, UprightStiffness, nameof(UprightStiffness));
        ValidateNonNegative(errors, UprightDamping, nameof(UprightDamping));
        ValidatePositive(errors, MaximumUprightTorque, nameof(MaximumUprightTorque));
        ValidateNonNegative(errors, AssistedTorqueMultiplier, nameof(AssistedTorqueMultiplier));
        ValidateNonNegative(errors, BalanceStiffness, nameof(BalanceStiffness));
        ValidateNonNegative(errors, BalanceDamping, nameof(BalanceDamping));
        ValidatePositive(errors, MaximumBalanceForce, nameof(MaximumBalanceForce));
        ValidateNonNegative(errors, SelfRightForce, nameof(SelfRightForce));
        ValidateNonNegative(errors, WalkForce, nameof(WalkForce));
        ValidateNonNegative(errors, MaximumWalkSpeed, nameof(MaximumWalkSpeed));
        ValidateNonNegative(errors, GaitForce, nameof(GaitForce));
        if (GaitHalfCycleTicks <= 0)
        {
            errors.Add($"{nameof(GaitHalfCycleTicks)} must be positive");
        }
        ValidateNonNegative(errors, JumpImpulse, nameof(JumpImpulse));
        ValidatePositive(errors, MaximumStandingTorsoTilt, nameof(MaximumStandingTorsoTilt));
        ValidateNonNegative(errors, MinimumHeadAboveTorso, nameof(MinimumHeadAboveTorso));
        ValidateNonNegative(errors, MinimumFeetBelowTorso, nameof(MinimumFeetBelowTorso));
        ValidateNonNegative(errors, MaximumCenterOfMassError, nameof(MaximumCenterOfMassError));
        ValidateNonNegative(errors, MaximumStandingSpeed, nameof(MaximumStandingSpeed));
        if (StableStandingTicks <= 0)
        {
            errors.Add($"{nameof(StableStandingTicks)} must be positive");
        }

        return errors;
    }

    private static void ValidatePositive(Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            errors.Add($"{name} must be finite and positive");
        }
    }

    private static void ValidateNonNegative(Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            errors.Add($"{name} must be finite and non-negative");
        }
    }
}
