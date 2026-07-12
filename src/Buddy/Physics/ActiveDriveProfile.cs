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

    // --- Stepping gait (replaces the old whole-body-only push; feet visibly step) ---
    /// <summary>Fraction of WalkForce kept as a whole-body propulsion assist; the rest of the motion comes from the feet.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float WalkAssistScale { get; set; } = 0.4f;
    /// <summary>Forward/back reach of a foot within one gait cycle (px).</summary>
    [Export(PropertyHint.Range, "0,128,0.1,or_greater")] public float StepLength { get; set; } = 20.0f;
    /// <summary>Peak height the swing foot lifts off the floor (px).</summary>
    [Export(PropertyHint.Range, "0,128,0.1,or_greater")] public float StepLift { get; set; } = 14.0f;
    /// <summary>Ticks for a full left+right gait cycle at 120 Hz.</summary>
    [Export(PropertyHint.Range, "8,240,1")] public int GaitCycleTicks { get; set; } = 48;
    /// <summary>Spring stiffness driving each foot toward its gait target.</summary>
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float StepDriveStiffness { get; set; } = 200.0f;
    /// <summary>Damping on the foot-target drive.</summary>
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float StepDriveDamping { get; set; } = 12.0f;
    /// <summary>Bound on the per-foot gait drive force.</summary>
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float StepDriveMaxForce { get; set; } = 4_000.0f;
    /// <summary>Force-per-px converting the gait torso-bob offset into an upward torso lift.</summary>
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float TorsoBob { get; set; } = 6.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float TorsoBobStiffness { get; set; } = 90.0f;
    /// <summary>Forward lean fraction and the head force that realizes it (leans into travel).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float TorsoLean { get; set; } = 0.12f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float TorsoLeanForce { get; set; } = 2_400.0f;

    // --- Jump ---
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float JumpImpulse { get; set; } = 1_800.0f;
    /// <summary>Anticipation crouch before the jump impulse (ticks); 0 = instant pop.</summary>
    [Export(PropertyHint.Range, "0,60,1")] public int JumpCrouchTicks { get; set; } = 14;
    /// <summary>Downward torso / upward-relative foot force applied during the crouch.</summary>
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float JumpCrouchForce { get; set; } = 1_000.0f;

    /// <summary>Bounded whole-body force a fearful buddy applies to resist a grab (RAGDOLL Section 6).</summary>
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float GrabResistanceForce { get; set; } = 3_500.0f;

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
        ValidateNonNegative(errors, WalkAssistScale, nameof(WalkAssistScale));
        ValidateNonNegative(errors, StepLength, nameof(StepLength));
        ValidateNonNegative(errors, StepLift, nameof(StepLift));
        if (GaitCycleTicks <= 0)
        {
            errors.Add($"{nameof(GaitCycleTicks)} must be positive");
        }
        ValidateNonNegative(errors, StepDriveStiffness, nameof(StepDriveStiffness));
        ValidateNonNegative(errors, StepDriveDamping, nameof(StepDriveDamping));
        ValidatePositive(errors, StepDriveMaxForce, nameof(StepDriveMaxForce));
        ValidateNonNegative(errors, TorsoBob, nameof(TorsoBob));
        ValidateNonNegative(errors, TorsoBobStiffness, nameof(TorsoBobStiffness));
        ValidateNonNegative(errors, TorsoLean, nameof(TorsoLean));
        ValidateNonNegative(errors, TorsoLeanForce, nameof(TorsoLeanForce));
        ValidateNonNegative(errors, JumpImpulse, nameof(JumpImpulse));
        if (JumpCrouchTicks < 0)
        {
            errors.Add($"{nameof(JumpCrouchTicks)} must be non-negative");
        }
        ValidateNonNegative(errors, JumpCrouchForce, nameof(JumpCrouchForce));
        ValidateNonNegative(errors, GrabResistanceForce, nameof(GrabResistanceForce));
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
