using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Grab;

/// <summary>
/// Provisional, laboratory-tunable tuning for the player grab tether
/// (RAGDOLL_AND_GAMEPLAY_SPEC.md Section 6). The tether is intentionally
/// unbreakable, so there is no break-force here; <see cref="ThrowSpeedCap"/> is
/// the calibrated safe maximum release velocity (FR-006.4). Final values are
/// established in the physics laboratory.
/// </summary>
[GlobalClass]
public partial class GrabTetherProfile : GameResource
{
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float Stiffness { get; set; } = 220.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float Damping { get; set; } = 20.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float MaximumForce { get; set; } = 6_000.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float ThrowSpeedCap { get; set; } = 900.0f;

    // --- Elastic limb: stretch limit, strain buzz, snap-back fling (owner request 2026-07-25) ---
    /// <summary>
    /// Maximum limb stretch in hand widths, measured from the torso anchor. A hand width is
    /// twice the grabbed part's radius, so this stays right if the rig is rescaled.
    /// </summary>
    [Export(PropertyHint.Range, "1,20,0.5")] public float StretchLimitHandWidths { get; set; } = 5.0f;
    /// <summary>Routed ticks the limb strains at the limit before snapping (360 = 3 s at 120 Hz).</summary>
    [Export(PropertyHint.Range, "1,1200,1")] public int StretchShakeTicks { get; set; } = 360;
    /// <summary>Peak buzz offset of the strained limb, in px.</summary>
    [Export(PropertyHint.Range, "0,32,0.1")] public float StretchShakeAmplitude { get; set; } = 3.5f;
    /// <summary>Ticks per buzz cycle — short, so strain reads as vibration.</summary>
    [Export(PropertyHint.Range, "1,120,1")] public int StretchShakeCycleTicks { get; set; } = 6;
    /// <summary>Ticks before the snap over which the buzz escalates (120 = the final second).</summary>
    [Export(PropertyHint.Range, "0,600,1")] public int StretchShakeRampTicks { get; set; } = 120;
    /// <summary>Peak buzz multiplier reached at the moment of snapping.</summary>
    [Export(PropertyHint.Range, "1,10,0.1")] public float StretchShakeRampMultiplier { get; set; } = 3.4f;
    /// <summary>How far inside the limit the player must ease off to cancel the snap countdown.</summary>
    [Export(PropertyHint.Range, "0,64,0.5")] public float StretchReleaseHysteresis { get; set; } = 8.0f;
    /// <summary>Fling impulse at a bare-minimum overpull.</summary>
    [Export(PropertyHint.Range, "0,20000,1,or_greater")] public float SnapImpulseBase { get; set; } = 300.0f;
    /// <summary>Extra fling impulse per pixel pulled beyond the limit — harder pull, harder launch.</summary>
    [Export(PropertyHint.Range, "0,200,0.1,or_greater")] public float SnapImpulsePerOverpullPixel { get; set; } = 3.0f;
    /// <summary>Bound on the fling impulse.</summary>
    [Export(PropertyHint.Range, "0,50000,1,or_greater")] public float MaximumSnapImpulse { get; set; } = 1_200.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (StretchLimitHandWidths <= 0.0f) errors.Add($"{nameof(StretchLimitHandWidths)} must be positive");
        if (StretchShakeTicks <= 0) errors.Add($"{nameof(StretchShakeTicks)} must be positive");
        if (StretchShakeAmplitude < 0.0f) errors.Add($"{nameof(StretchShakeAmplitude)} must be non-negative");
        if (StretchShakeCycleTicks <= 0) errors.Add($"{nameof(StretchShakeCycleTicks)} must be positive");
        if (StretchShakeRampTicks < 0) errors.Add($"{nameof(StretchShakeRampTicks)} must be non-negative");
        if (StretchShakeRampMultiplier < 1.0f) errors.Add($"{nameof(StretchShakeRampMultiplier)} must be at least 1");
        if (StretchReleaseHysteresis < 0.0f) errors.Add($"{nameof(StretchReleaseHysteresis)} must be non-negative");
        if (SnapImpulseBase < 0.0f) errors.Add($"{nameof(SnapImpulseBase)} must be non-negative");
        if (SnapImpulsePerOverpullPixel < 0.0f) errors.Add($"{nameof(SnapImpulsePerOverpullPixel)} must be non-negative");
        if (MaximumSnapImpulse < 0.0f) errors.Add($"{nameof(MaximumSnapImpulse)} must be non-negative");
        if (!float.IsFinite(Stiffness) || Stiffness < 0.0f)
        {
            errors.Add($"{nameof(Stiffness)} must be finite and non-negative");
        }

        if (!float.IsFinite(Damping) || Damping < 0.0f)
        {
            errors.Add($"{nameof(Damping)} must be finite and non-negative");
        }

        if (!float.IsFinite(MaximumForce) || MaximumForce <= 0.0f)
        {
            errors.Add($"{nameof(MaximumForce)} must be finite and positive");
        }

        if (!float.IsFinite(ThrowSpeedCap) || ThrowSpeedCap <= 0.0f)
        {
            errors.Add($"{nameof(ThrowSpeedCap)} must be finite and positive");
        }

        return errors;
    }
}
