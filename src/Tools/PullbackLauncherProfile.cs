using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Shared empirical pullback-launcher tuning. Gameplay values are authored data so the
/// M5 laboratory can calibrate feel without changing the input or lifecycle contract.
/// </summary>
[GlobalClass]
public partial class PullbackLauncherProfile : GameResource
{
    [Export(PropertyHint.Range, "1,400,1")]
    public float MaxPullDistance { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "0,40,0.5")]
    public float MinimumLaunchPullDistance { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float VelocityPerPullPixel { get; set; } = 15.0f;

    [Export(PropertyHint.Range, "1,3000,1")]
    public float MaxLaunchSpeed { get; set; } = 1800.0f;

    [Export(PropertyHint.Range, "0.1,3,0.05")]
    public float PredictionSeconds { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "2,32,1")]
    public int PredictionSegments { get; set; } = 12;

    [Export] public Color PullLineColor { get; set; } = new("58a7f0");
    [Export] public Color TrajectoryColor { get; set; } = new("f7c948");

    public bool IsRuntimeValid =>
        float.IsFinite(MaxPullDistance) && MaxPullDistance > 0.0f &&
        float.IsFinite(MinimumLaunchPullDistance) &&
        MinimumLaunchPullDistance >= 0.0f &&
        MinimumLaunchPullDistance <= MaxPullDistance &&
        float.IsFinite(VelocityPerPullPixel) && VelocityPerPullPixel > 0.0f &&
        float.IsFinite(MaxLaunchSpeed) && MaxLaunchSpeed > 0.0f &&
        float.IsFinite(PredictionSeconds) && PredictionSeconds > 0.0f &&
        PredictionSegments >= 2;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!float.IsFinite(MaxPullDistance) || MaxPullDistance <= 0.0f)
            errors.Add($"{nameof(MaxPullDistance)} must be finite and positive");
        if (!float.IsFinite(MinimumLaunchPullDistance) ||
            MinimumLaunchPullDistance < 0.0f ||
            MinimumLaunchPullDistance > MaxPullDistance)
        {
            errors.Add(
                $"{nameof(MinimumLaunchPullDistance)} must be finite and between zero and {nameof(MaxPullDistance)}");
        }
        if (!float.IsFinite(VelocityPerPullPixel) || VelocityPerPullPixel <= 0.0f)
            errors.Add($"{nameof(VelocityPerPullPixel)} must be finite and positive");
        if (!float.IsFinite(MaxLaunchSpeed) || MaxLaunchSpeed <= 0.0f)
            errors.Add($"{nameof(MaxLaunchSpeed)} must be finite and positive");
        if (!float.IsFinite(PredictionSeconds) || PredictionSeconds <= 0.0f)
            errors.Add($"{nameof(PredictionSeconds)} must be finite and positive");
        if (PredictionSegments < 2)
            errors.Add($"{nameof(PredictionSegments)} must be at least 2");
        return errors;
    }
}
