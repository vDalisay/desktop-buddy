using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>Bounded Tickle flee/hop and Boxing Glove guard tuning.</summary>
[GlobalClass]
public partial class ToolReactionProfile : GameResource
{
    [Export(PropertyHint.Range, "0,2,0.01")] public float AngryFleeScale { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "0,2,0.01")] public float FriendlyJumpScale { get; set; } = 1.15f;
    [Export(PropertyHint.Range, "0,2,0.01")] public float AngryJumpScale { get; set; } = 1.35f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float TickleJumpHorizontalRatio { get; set; } = 0.45f;
    [Export(PropertyHint.Range, "0,512,1")] public float DefenseRange { get; set; } = 180.0f;
    [Export(PropertyHint.Range, "0,128,1")] public float GuardReach { get; set; } = 42.0f;
    [Export(PropertyHint.Range, "0,128,1")] public float GuardHandSeparation { get; set; } = 24.0f;
    [Export(PropertyHint.Range, "0,2,0.01")] public float DefenseFleeScale { get; set; } = 0.75f;
    [Export(PropertyHint.Range, "0.01,1,0.01")] public float GuardAimLagSeconds { get; set; } = 0.12f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float GuardAbsorption { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0,100000,1")] public float GuardStiffness { get; set; } = 1_600.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float GuardDamping { get; set; } = 70.0f;
    [Export(PropertyHint.Range, "0,200000,1")] public float GuardMaximumForce { get; set; } = 16_000.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        ValidateRange(errors, AngryFleeScale, 0, 2, nameof(AngryFleeScale));
        ValidateRange(errors, FriendlyJumpScale, 0, 2, nameof(FriendlyJumpScale));
        ValidateRange(errors, AngryJumpScale, 0, 2, nameof(AngryJumpScale));
        ValidateRange(errors, TickleJumpHorizontalRatio, 0, 1, nameof(TickleJumpHorizontalRatio));
        ValidateRange(errors, DefenseRange, 0, 512, nameof(DefenseRange));
        ValidateRange(errors, GuardReach, 0, 128, nameof(GuardReach));
        ValidateRange(errors, GuardHandSeparation, 0, 128, nameof(GuardHandSeparation));
        ValidateRange(errors, DefenseFleeScale, 0, 2, nameof(DefenseFleeScale));
        ValidateRange(errors, GuardAimLagSeconds, 0.01f, 1, nameof(GuardAimLagSeconds));
        ValidateRange(errors, GuardAbsorption, 0, 1, nameof(GuardAbsorption));
        ValidateRange(errors, GuardStiffness, 0, 100000, nameof(GuardStiffness));
        ValidateRange(errors, GuardDamping, 0, 10000, nameof(GuardDamping));
        ValidateRange(errors, GuardMaximumForce, 0, 200000, nameof(GuardMaximumForce));
        return errors;
    }

    private static void ValidateRange(
        Godot.Collections.Array<string> errors,
        float value,
        float minimum,
        float maximum,
        string name)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
            errors.Add($"{name} must be finite and within [{minimum}, {maximum}]");
    }
}
