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

    /// <summary>
    /// Physics ticks the repeat-envelope scenario drives each buddy under seeded
    /// autonomous motion after it first settles, so the envelope reflects driven
    /// behavior rather than the trivial spawn-and-settle pose.
    /// </summary>
    [Export] public int AutonomyObservationTicks { get; set; } = 600;

    /// <summary>
    /// Max final-pose spread permitted among runs that share the same seed. This is
    /// the repeatability bound: identical seeds must produce near-identical driven
    /// outcomes. Looser cross-seed variation is bounded by MaximumFinalPoseSpread.
    /// </summary>
    [Export] public float MaximumSameSeedPoseSpread { get; set; } = 64.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (MaximumLinkStrain < 1 || MaximumFinalPoseSpread <= 0 || MaximumSettleTicks <= 0 ||
            MaximumSettleTickSpread < 0 || AutonomyObservationTicks < 0 || MaximumSameSeedPoseSpread <= 0)
            errors.Add("Envelope bounds must be positive and strain must be at least one.");
        return errors;
    }
}
