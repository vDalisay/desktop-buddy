using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>Confirmed maximum-hit stop plus empirical non-graphic feedback tuning.</summary>
[GlobalClass]
public partial class ImpactFeedbackProfile : GameResource
{
    [Export(PropertyHint.Range, "0.01,1,0.01")] public float HitStopScale { get; set; } = 0.15f;
    [Export(PropertyHint.Range, "0.01,0.5,0.01")] public double HitStopSeconds { get; set; } = 0.12;
    [Export(PropertyHint.Range, "1,1000,1")] public float MaximumPain { get; set; } = 100.0f;
    [Export(PropertyHint.Range, "0.01,0.5,0.01")] public double RingSeconds { get; set; } = 0.22;
    [Export(PropertyHint.Range, "0.01,0.5,0.01")] public double GloveSquashSeconds { get; set; } = 0.16;
    [Export(PropertyHint.Range, "0,24,0.1")] public float CanvasJoltPixels { get; set; } = 4.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!float.IsFinite(HitStopScale) || HitStopScale <= 0.0f || HitStopScale > 1.0f)
            errors.Add("HitStopScale must be within (0,1]");
        if (!double.IsFinite(HitStopSeconds) || HitStopSeconds <= 0.0)
            errors.Add("HitStopSeconds must be finite and positive");
        if (!float.IsFinite(MaximumPain) || MaximumPain <= 0.0f)
            errors.Add("MaximumPain must be finite and positive");
        if (!double.IsFinite(RingSeconds) || RingSeconds <= 0.0 ||
            !double.IsFinite(GloveSquashSeconds) || GloveSquashSeconds <= 0.0)
            errors.Add("feedback durations must be finite and positive");
        if (!float.IsFinite(CanvasJoltPixels) || CanvasJoltPixels < 0.0f)
            errors.Add("CanvasJoltPixels must be finite and non-negative");
        return errors;
    }
}
