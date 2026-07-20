using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>Laboratory-tuned ambient goal timing and selection weights.</summary>
[GlobalClass]
public partial class AutonomousMotionProfile : GameResource
{
    [Export(PropertyHint.Range, "1,7200,1")] public int MinimumIdleTicks { get; set; } = 60;
    [Export(PropertyHint.Range, "1,7200,1")] public int MaximumIdleTicks { get; set; } = 120;
    [Export(PropertyHint.Range, "1,7200,1")] public int MinimumWalkTicks { get; set; } = 120;
    [Export(PropertyHint.Range, "1,7200,1")] public int MaximumWalkTicks { get; set; } = 240;
    [Export(PropertyHint.Range, "1,7200,1")] public int MinimumJumpIntervalTicks { get; set; } = 240;
    [Export(PropertyHint.Range, "1,7200,1")] public int MaximumJumpIntervalTicks { get; set; } = 480;
    [Export(PropertyHint.Range, "0,100,1")] public int IdleWeight { get; set; } = 2;
    [Export(PropertyHint.Range, "0,100,1")] public int WalkLeftWeight { get; set; } = 3;
    [Export(PropertyHint.Range, "0,100,1")] public int WalkRightWeight { get; set; } = 3;

    /// <summary>Ambient timer-driven jumping. Owner-disabled 2026-07-20 (see DECISIONS.md);
    /// tool-reaction hops and future behaviour-driven jumps are unaffected.</summary>
    [Export] public bool AmbientJumpsEnabled { get; set; } = true;

    public AutonomousMotionTuning ToTuning() => new(
        MinimumIdleTicks,
        MaximumIdleTicks,
        MinimumWalkTicks,
        MaximumWalkTicks,
        MinimumJumpIntervalTicks,
        MaximumJumpIntervalTicks,
        IdleWeight,
        WalkLeftWeight,
        WalkRightWeight,
        AmbientJumpsEnabled);

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        try
        {
            ToTuning().Validate();
        }
        catch (System.ArgumentException exception)
        {
            errors.Add(exception.Message);
        }

        return errors;
    }
}
