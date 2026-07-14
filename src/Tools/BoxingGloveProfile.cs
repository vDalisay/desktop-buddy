using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Provisional, laboratory-tunable tuning for the Boxing Glove's cursor-tethered
/// physical collider (RAGDOLL §9.1/§9.2). The glove reuses the M1 damped-elastic
/// tether mechanism; pain comes only from its real measured contact impulse
/// through the shared pain curve — there is no per-tool payout multiplier.
/// </summary>
[GlobalClass]
public partial class BoxingGloveProfile : GameResource
{
    [Export(PropertyHint.Range, "1,128,0.1,or_greater")] public float Radius { get; set; } = 14.0f;
    [Export(PropertyHint.Range, "0.01,100,0.01,or_greater")] public float Mass { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float Stiffness { get; set; } = 900.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float Damping { get; set; } = 45.0f;
    [Export(PropertyHint.Range, "0.1,200000,0.1,or_greater")] public float MaximumForce { get; set; } = 30_000.0f;
    [Export(PropertyHint.Range, "0,100,0.01,or_greater")] public float LinearDamp { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0.1,128,0.1,or_greater")] public float MinimumArmingTravel { get; set; } = 8.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!float.IsFinite(Radius) || Radius <= 0.0f)
        {
            errors.Add($"{nameof(Radius)} must be finite and positive");
        }

        if (!float.IsFinite(Mass) || Mass <= 0.0f)
        {
            errors.Add($"{nameof(Mass)} must be finite and positive");
        }

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

        if (!float.IsFinite(LinearDamp) || LinearDamp < 0.0f)
        {
            errors.Add($"{nameof(LinearDamp)} must be finite and non-negative");
        }

        if (!float.IsFinite(MinimumArmingTravel) || MinimumArmingTravel <= 0.0f)
        {
            errors.Add($"{nameof(MinimumArmingTravel)} must be finite and positive");
        }

        return errors;
    }
}
