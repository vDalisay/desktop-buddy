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

    /// <summary>Clearance kept between the foremost body circle and a wall.</summary>
    [Export(PropertyHint.Range, "0,512,0.5")]
    public float WallAvoidMarginPixels { get; set; } = 12.0f;

    /// <summary>Forward velocity projection used to begin braking before contact.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float WallLookAheadSeconds { get; set; } = 0.3f;

    /// <summary>Forward loose-object probe used only for obstacle-hop evidence.</summary>
    [Export(PropertyHint.Range, "1,256,0.5")]
    public float ObstacleProbeDistance { get; set; } = 56.0f;

    /// <summary>
    /// Height of the obstacle probe below the torso centre, in world pixels. An object
    /// the buddy would walk into rests on the floor, not at chest height: with the
    /// shipped rig the floor line sits about <c>72 px</c> below the torso centre, so a
    /// probe at torso height passes clear over everything. <c>64</c> keeps the ray
    /// <c>8 px</c> above the floor, crossing any resting object of radius <c>6</c> or
    /// more, while the layer-3 mask keeps the room floor and the buddy's own feet out.
    /// </summary>
    [Export(PropertyHint.Range, "0,256,0.5")]
    public float ObstacleProbeHeightOffset { get; set; } = 64.0f;

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

    /// <summary>
    /// Managed-only startup predicate used by the fixed-tick worker. Avoids
    /// retaining a temporary native Godot Array until finalization after long
    /// headless scenarios have begun engine teardown.
    /// </summary>
    public bool IsRuntimeValid
    {
        get
        {
            try
            {
                ToTuning().Validate();
            }
            catch (System.ArgumentException)
            {
                return false;
            }

            return float.IsFinite(WallAvoidMarginPixels) && WallAvoidMarginPixels >= 0.0f &&
                float.IsFinite(WallLookAheadSeconds) && WallLookAheadSeconds >= 0.0f &&
                float.IsFinite(ObstacleProbeDistance) && ObstacleProbeDistance > 0.0f &&
                float.IsFinite(ObstacleProbeHeightOffset) && ObstacleProbeHeightOffset >= 0.0f;
        }
    }

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

        if (!float.IsFinite(WallAvoidMarginPixels) || WallAvoidMarginPixels < 0.0f)
        {
            errors.Add("wall avoid margin pixels must be finite and non-negative");
        }
        if (!float.IsFinite(WallLookAheadSeconds) || WallLookAheadSeconds < 0.0f)
            errors.Add("wall look-ahead seconds must be finite and non-negative");
        if (!float.IsFinite(ObstacleProbeDistance) || ObstacleProbeDistance <= 0.0f)
            errors.Add("obstacle probe distance must be finite and positive");
        if (!float.IsFinite(ObstacleProbeHeightOffset) || ObstacleProbeHeightOffset < 0.0f)
            errors.Add("obstacle probe height offset must be finite and non-negative");

        return errors;
    }
}
