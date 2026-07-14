using DesktopBuddy.App;
using DesktopBuddy.Domain.Mood;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>Confirmed direct-care timing plus laboratory-tuned rubbing distance.</summary>
[GlobalClass]
public partial class CareInteractionProfile : GameResource
{
    [Export(PropertyHint.Range, "1,2000,1,or_greater")] public double PetDistancePerReward { get; set; } = 180.0;
    [Export(PropertyHint.Range, "1,2,0.01")] public double FavoriteSpotMultiplier { get; set; } = 1.2;
    [Export(PropertyHint.Range, "0.1,30,0.1")] public double SecondsPerReward { get; set; } = 3.0;
    [Export(PropertyHint.Range, "0.1,30,0.1")] public double TickleFriendlySeconds { get; set; } = 6.0;
    [Export(PropertyHint.Range, "0.1,60,0.1")] public double TickleCooldownSeconds { get; set; } = 8.0;
    [Export(PropertyHint.Range, "0.1,10,0.05")] public double FriendlyHopIntervalSeconds { get; set; } = 1.5;
    [Export(PropertyHint.Range, "0.1,10,0.05")] public double AngryHopIntervalSeconds { get; set; } = 0.75;
    [Export(PropertyHint.Range, "1,256,1")] public float MaximumStrokeDistancePerTick { get; set; } = 32.0f;

    public CareTuning ToTuning() => new(
        PetDistancePerReward,
        FavoriteSpotMultiplier,
        SecondsPerReward,
        TickleFriendlySeconds,
        TickleCooldownSeconds,
        FriendlyHopIntervalSeconds,
        AngryHopIntervalSeconds);

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

        if (!float.IsFinite(MaximumStrokeDistancePerTick) || MaximumStrokeDistancePerTick <= 0.0f)
            errors.Add($"{nameof(MaximumStrokeDistancePerTick)} must be finite and positive");
        return errors;
    }
}
