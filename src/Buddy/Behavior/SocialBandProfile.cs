using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>Typed data-resource form of one mood band's social vocabulary.</summary>
[GlobalClass]
public partial class SocialBandProfile : GameResource
{
    [Export(PropertyHint.Range, "0,1024,1")] public float StandoffDistance { get; set; }
    [Export(PropertyHint.Range, "0,1024,1")] public float ApproachDistance { get; set; }
    [Export(PropertyHint.Range, "0,256,1")] public float Hysteresis { get; set; } = 12.0f;
    [Export] public bool WillApproach { get; set; }
    [Export] public bool WillCatch { get; set; }
    [Export(PropertyHint.Range, "0,2,0.01")] public float LocomotionScale { get; set; }
    [Export(PropertyHint.Range, "0,7200,1")] public int GreetIntervalTicks { get; set; }

    public bool IsRuntimeValid =>
        float.IsFinite(StandoffDistance) && StandoffDistance >= 0.0f &&
        float.IsFinite(ApproachDistance) && ApproachDistance >= 0.0f &&
        float.IsFinite(Hysteresis) && Hysteresis >= 0.0f &&
        float.IsFinite(LocomotionScale) && LocomotionScale is >= 0.0f and <= 2.0f &&
        GreetIntervalTicks >= 0;

    public SocialBandTuning ToDomain() => new(
        StandoffDistance,
        ApproachDistance,
        Hysteresis,
        WillApproach,
        WillCatch,
        LocomotionScale,
        GreetIntervalTicks);

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        NonNegative(errors, StandoffDistance, nameof(StandoffDistance));
        NonNegative(errors, ApproachDistance, nameof(ApproachDistance));
        NonNegative(errors, Hysteresis, nameof(Hysteresis));
        if (!float.IsFinite(LocomotionScale) || LocomotionScale is < 0.0f or > 2.0f)
            errors.Add($"{nameof(LocomotionScale)} must be finite and within [0, 2]");
        if (GreetIntervalTicks < 0)
            errors.Add($"{nameof(GreetIntervalTicks)} must be non-negative");
        return errors;
    }

    private static void NonNegative(
        Godot.Collections.Array<string> errors,
        float value,
        string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
            errors.Add($"{name} must be finite and non-negative");
    }
}
