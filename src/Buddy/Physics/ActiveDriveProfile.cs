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
    /// <summary>
    /// Gravity-style gain that lets the center-anchored spring frame swing
    /// around an unsupported grabbed part rather than servoing into position.
    /// </summary>
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float HangGravityGain { get; set; } = 980.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float HangSwingDamping { get; set; } = 0.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float MaximumHangAlignTorque { get; set; } = 48_000.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float HeadUprightStiffness { get; set; } = 500.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float HeadUprightDamping { get; set; } = 110.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float MaximumHeadUprightTorque { get; set; } = 1_600.0f;
    /// <summary>Two calm seconds on the authoritative 120 Hz clock.</summary>
    [Export(PropertyHint.Range, "0,1200,1")] public int HeadRightingDelayTicks { get; set; } = 240;
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
    // Doubled from 1800 at owner request 2026-07-26: the previous impulse produced a
    // ~35 px torso rise, which did not reliably carry the feet over a resting loose
    // object now that obstacle hops actually fire.
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float JumpImpulse { get; set; } = 3_600.0f;
    /// <summary>Anticipation crouch before the jump impulse (ticks); 0 = instant pop.</summary>
    [Export(PropertyHint.Range, "0,60,1")] public int JumpCrouchTicks { get; set; } = 14;
    /// <summary>Downward torso / upward-relative foot force applied during the crouch.</summary>
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float JumpCrouchForce { get; set; } = 1_000.0f;

    /// <summary>Bounded whole-body force a fearful buddy applies to resist a grab (RAGDOLL Section 6).</summary>
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float GrabResistanceForce { get; set; } = 3_500.0f;

    // --- Grab resistance: walking and panic hands (owner feel notes 2026-07-25) ---
    // A resisting buddy must *walk* away, not slide: locomotion runs during resistance so the
    // gait drives the feet, and the whole-body resistance force becomes a strain assist on top.
    /// <summary>Locomotion scale applied while resisting a grab; 0 disables stepping.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")] public float GrabResistanceWalkScale { get; set; } = 0.9f;

    /// <summary>Shoulder anchor (torso-relative) the panic hands thrash around.</summary>
    [Export] public Vector2 PanicHandOriginOffset { get; set; } = new(0.0f, -14.0f);
    /// <summary>Horizontal sweep of each flailing hand at full fear.</summary>
    [Export(PropertyHint.Range, "0,128,0.5")] public float PanicFlailAmplitude { get; set; } = 34.0f;
    /// <summary>Vertical span of the flail arc at full fear.</summary>
    [Export(PropertyHint.Range, "0,128,0.5")] public float PanicFlailLift { get; set; } = 26.0f;
    /// <summary>
    /// Routed ticks for one full sweep. Long on purpose: fear widens the reach, never speeds
    /// it up, because a fast cycle reads as random twitching rather than flailing.
    /// </summary>
    [Export(PropertyHint.Range, "1,480,1")] public int PanicFlailCycleTicks { get; set; } = 132;
    /// <summary>Phase offset between the hands so they never mirror each other exactly.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float PanicFlailAsymmetry { get; set; } = 0.55f;
    /// <summary>
    /// How far the flail arc anchors toward the direction the buddy is straining, so a free
    /// hand reaches away from the grab point as if pulling itself loose.
    /// </summary>
    [Export(PropertyHint.Range, "0,128,0.5")] public float PanicFlailReachBias { get; set; } = 24.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float PanicHandStiffness { get; set; } = 2_200.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float PanicHandDamping { get; set; } = 55.0f;
    [Export(PropertyHint.Range, "0.1,200000,0.1,or_greater")] public float PanicHandMaximumForce { get; set; } = 18_000.0f;

    // --- Behavior-backed hand reach (Eat now; Wave may reuse this seam later) ---
    [Export] public Vector2 EatChestTargetOffset { get; set; } = new(0.0f, -24.0f);
    [Export] public Vector2 EatMouthTargetOffset { get; set; } = new(0.0f, 31.0f);
    [Export] public Vector2 EatFinalLowerTargetOffset { get; set; } = new(0.0f, -5.0f);
    [Export(PropertyHint.Range, "0,64,0.5")] public float EatHandHalfSeparation { get; set; } = 16.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float ActivityHandStiffness { get; set; } = 1_600.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float ActivityHandDamping { get; set; } = 70.0f;
    [Export(PropertyHint.Range, "0.1,200000,0.1,or_greater")] public float ActivityHandMaximumForce { get; set; } = 16_000.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float StationaryHorizontalDamping { get; set; } = 600.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float MaximumStationaryForce { get; set; } = 30_000.0f;

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
        ValidateNonNegative(errors, HangGravityGain, nameof(HangGravityGain));
        ValidateNonNegative(errors, HangSwingDamping, nameof(HangSwingDamping));
        ValidatePositive(errors, MaximumHangAlignTorque, nameof(MaximumHangAlignTorque));
        ValidateNonNegative(errors, HeadUprightStiffness, nameof(HeadUprightStiffness));
        ValidateNonNegative(errors, HeadUprightDamping, nameof(HeadUprightDamping));
        ValidatePositive(errors, MaximumHeadUprightTorque, nameof(MaximumHeadUprightTorque));
        if (HeadRightingDelayTicks < 0)
        {
            errors.Add($"{nameof(HeadRightingDelayTicks)} must be non-negative");
        }
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
        ValidateNonNegative(errors, GrabResistanceWalkScale, nameof(GrabResistanceWalkScale));
        if (!PanicHandOriginOffset.IsFinite()) errors.Add($"{nameof(PanicHandOriginOffset)} must be finite");
        ValidateNonNegative(errors, PanicFlailAmplitude, nameof(PanicFlailAmplitude));
        ValidateNonNegative(errors, PanicFlailLift, nameof(PanicFlailLift));
        if (PanicFlailCycleTicks <= 0)
        {
            errors.Add($"{nameof(PanicFlailCycleTicks)} must be positive");
        }
        ValidateNonNegative(errors, PanicFlailAsymmetry, nameof(PanicFlailAsymmetry));
        ValidateNonNegative(errors, PanicFlailReachBias, nameof(PanicFlailReachBias));
        ValidateNonNegative(errors, PanicHandStiffness, nameof(PanicHandStiffness));
        ValidateNonNegative(errors, PanicHandDamping, nameof(PanicHandDamping));
        ValidatePositive(errors, PanicHandMaximumForce, nameof(PanicHandMaximumForce));
        if (!EatChestTargetOffset.IsFinite()) errors.Add($"{nameof(EatChestTargetOffset)} must be finite");
        if (!EatMouthTargetOffset.IsFinite()) errors.Add($"{nameof(EatMouthTargetOffset)} must be finite");
        if (!EatFinalLowerTargetOffset.IsFinite()) errors.Add($"{nameof(EatFinalLowerTargetOffset)} must be finite");
        ValidateNonNegative(errors, EatHandHalfSeparation, nameof(EatHandHalfSeparation));
        ValidateNonNegative(errors, ActivityHandStiffness, nameof(ActivityHandStiffness));
        ValidateNonNegative(errors, ActivityHandDamping, nameof(ActivityHandDamping));
        ValidatePositive(errors, ActivityHandMaximumForce, nameof(ActivityHandMaximumForce));
        ValidateNonNegative(errors, StationaryHorizontalDamping, nameof(StationaryHorizontalDamping));
        ValidatePositive(errors, MaximumStationaryForce, nameof(MaximumStationaryForce));
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
