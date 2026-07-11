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

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
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
