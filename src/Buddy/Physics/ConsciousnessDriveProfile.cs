using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// Data-only scale profile for one consciousness state. The unconscious asset
/// disables every active output while passive structure remains unaffected.
/// </summary>
[GlobalClass]
public partial class ConsciousnessDriveProfile : GameResource
{
    [Export] public bool ActiveDriveEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,4,0.01,or_greater")] public float UprightScale { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,4,0.01,or_greater")] public float BalanceScale { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,4,0.01,or_greater")] public float LocomotionScale { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,4,0.01,or_greater")] public float JumpScale { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,4,0.01,or_greater")] public float RecoveryScale { get; set; } = 1.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        ValidateScale(errors, UprightScale, nameof(UprightScale));
        ValidateScale(errors, BalanceScale, nameof(BalanceScale));
        ValidateScale(errors, LocomotionScale, nameof(LocomotionScale));
        ValidateScale(errors, JumpScale, nameof(JumpScale));
        ValidateScale(errors, RecoveryScale, nameof(RecoveryScale));
        return errors;
    }

    private static void ValidateScale(Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            errors.Add($"{name} must be finite and non-negative");
        }
    }
}
