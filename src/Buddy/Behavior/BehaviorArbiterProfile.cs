using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Per-presentation-independent Task 3 behavior tuning. All five band resources
/// are required so runtime workers share one complete vocabulary.
/// </summary>
[GlobalClass]
public partial class BehaviorArbiterProfile : GameResource
{
    [Export(PropertyHint.Range, "0,600,1")] public int CommitTicks { get; set; } = 36;
    [Export(PropertyHint.Range, "0,100,1")] public int HopPropensityThreshold { get; set; } = 35;

    /// <summary>
    /// How much of an obstacle hop's impulse goes forward along the committed walk. A purely
    /// vertical hop lands the buddy back on the object it was trying to clear.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ObstacleHopHorizontalRatio { get; set; } = 0.3f;
    [Export] public SocialBandProfile Fearful { get; set; } = null!;
    [Export] public SocialBandProfile Wary { get; set; } = null!;
    [Export] public SocialBandProfile Neutral { get; set; } = null!;
    [Export] public SocialBandProfile Content { get; set; } = null!;
    [Export] public SocialBandProfile Delighted { get; set; } = null!;

    public bool IsRuntimeValid =>
        CommitTicks >= 0 &&
        HopPropensityThreshold is >= 0 and <= 100 &&
        float.IsFinite(ObstacleHopHorizontalRatio) &&
        ObstacleHopHorizontalRatio is >= 0.0f and <= 1.0f &&
        Valid(Fearful) && Valid(Wary) && Valid(Neutral) &&
        Valid(Content) && Valid(Delighted);

    public BehaviorArbiterTuning ToDomainTuning() =>
        new(CommitTicks, HopPropensityThreshold);

    public SocialTuningSet ToSocialTuning() => new(
        Fearful.ToDomain(),
        Wary.ToDomain(),
        Neutral.ToDomain(),
        Content.ToDomain(),
        Delighted.ToDomain());

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (CommitTicks < 0) errors.Add($"{nameof(CommitTicks)} must be non-negative");
        if (HopPropensityThreshold is < 0 or > 100)
            errors.Add($"{nameof(HopPropensityThreshold)} must be within [0, 100]");
        if (!float.IsFinite(ObstacleHopHorizontalRatio) ||
            ObstacleHopHorizontalRatio is < 0.0f or > 1.0f)
        {
            errors.Add($"{nameof(ObstacleHopHorizontalRatio)} must be within [0, 1]");
        }
        Require(errors, Fearful, nameof(Fearful));
        Require(errors, Wary, nameof(Wary));
        Require(errors, Neutral, nameof(Neutral));
        Require(errors, Content, nameof(Content));
        Require(errors, Delighted, nameof(Delighted));
        return errors;
    }

    private static bool Valid(SocialBandProfile? profile) =>
        profile is not null && GodotObject.IsInstanceValid(profile) && profile.IsRuntimeValid;

    private static void Require(
        Godot.Collections.Array<string> errors,
        SocialBandProfile? profile,
        string name)
    {
        if (!Valid(profile))
            errors.Add($"{name} must reference a valid SocialBandProfile");
    }
}
