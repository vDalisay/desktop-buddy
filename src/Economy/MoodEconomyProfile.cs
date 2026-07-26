using Godot;

namespace DesktopBuddy.Economy;

/// <summary>Provisional M4 passive/lifecycle laboratory tuning.</summary>
[GlobalClass]
public partial class MoodEconomyProfile : Resource
{
    [Export(PropertyHint.Range, "0,10,0.01")]
    public double NeutralCreditsPerMinute { get; set; } = 1.0;

    [Export(PropertyHint.Range, "0.1,5,0.1")]
    public double ForegroundUpdateSeconds { get; set; } = 1.0;

    [Export(PropertyHint.Range, "0.05,1,0.05")]
    public double HiddenUpdateSeconds { get; set; } = 0.1;

    [Export(PropertyHint.Range, "1,30,0.5")]
    public double DiscontinuitySeconds { get; set; } = 5.0;

    public bool IsRuntimeValid =>
        NeutralCreditsPerMinute >= 0.0 &&
        ForegroundUpdateSeconds > 0.0 &&
        HiddenUpdateSeconds > 0.0 &&
        DiscontinuitySeconds > 0.0;
}
