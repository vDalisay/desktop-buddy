using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Laboratory;

[GlobalClass]
public partial class EnvelopeBoundsProfile : GameResource
{
    [Export] public float MaximumLinkStrain { get; set; } = 1.1f;
    [Export] public float MaximumFinalPoseSpread { get; set; } = 440.0f;
    [Export] public int MaximumSettleTicks { get; set; } = 720;
    [Export] public float MaximumSettleTickSpread { get; set; } = 240;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (MaximumLinkStrain < 1 || MaximumFinalPoseSpread <= 0 || MaximumSettleTicks <= 0 || MaximumSettleTickSpread < 0)
            errors.Add("Envelope bounds must be positive and strain must be at least one.");
        return errors;
    }
}
