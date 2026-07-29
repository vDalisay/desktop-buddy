using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>Authoritative fixed-tick timing for behavior-backed activities.</summary>
[GlobalClass]
public partial class BehaviorActivityProfile : GameResource
{
    [Export(PropertyHint.Range, "1,600,1")] public int EatChestHoldTicks { get; set; } = 60;
    [Export(PropertyHint.Range, "1,10,1")] public int EatBiteCount { get; set; } = 5;
    [Export(PropertyHint.Range, "12,240,1")] public int EatBiteCycleTicks { get; set; } = 72;
    [Export(PropertyHint.Range, "0.1,0.9,0.01")] public float EatBiteMoment { get; set; } = 0.55f;
    [Export(PropertyHint.Range, "1,120,1")] public int EatFinalLowerHoldTicks { get; set; } = 30;
    [Export(PropertyHint.Range, "1,600,1")] public int WaveDurationTicks { get; set; } = 144;

    /// <summary>
    /// How long the "no thanks" head-shake plays before the buddy puts the item down.
    /// `96` ticks is 0.8 s at 120 Hz: two clear shakes, not a lingering performance.
    /// </summary>
    [Export(PropertyHint.Range, "1,600,1")] public int RefuseDurationTicks { get; set; } = 96;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (EatChestHoldTicks < 1) errors.Add("eat chest hold ticks must be positive");
        if (EatBiteCount < 1) errors.Add("eat bite count must be positive");
        if (EatBiteCycleTicks < 12) errors.Add("eat bite cycle must be at least 12 ticks");
        if (!float.IsFinite(EatBiteMoment) || EatBiteMoment <= 0.0f || EatBiteMoment >= 1.0f)
            errors.Add("eat bite moment must be between zero and one");
        if (EatFinalLowerHoldTicks < 1) errors.Add("eat final lower hold ticks must be positive");
        if (WaveDurationTicks < 1) errors.Add("wave duration ticks must be positive");
        if (RefuseDurationTicks < 1) errors.Add("refuse duration ticks must be positive");
        return errors;
    }
}
