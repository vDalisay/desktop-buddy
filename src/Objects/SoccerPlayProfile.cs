using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using Godot;

namespace DesktopBuddy.Objects;

/// <summary>
/// Authored opt-in for the trap → dwell → kick beat (owner instruction 2026-08-01: "the buddy
/// should treat it as a soccer ball"). A <see cref="LooseObjectProfile"/> that references one
/// of these is played with; every profile that does not is completely unaffected, which is the
/// whole point of putting the switch in data.
///
/// <para>The gameplay rules live in the engine-free
/// <see cref="DesktopBuddy.Domain.Autonomy.SoccerPlayModel"/>; this Resource only says how far,
/// how fast, how long, and how hard. Numbers are provisional until the owner's feel gate.</para>
/// </summary>
[GlobalClass]
public partial class SoccerPlayProfile : GameResource
{
    /// <summary>Horizontal gap to the ball's near surface within which the foot can meet it.</summary>
    [Export(PropertyHint.Range, "1,200,0.5")] public float TrapDistance { get; set; } = 34.0f;

    /// <summary>How far above the foot line a rolling ball may still be trapped.</summary>
    [Export(PropertyHint.Range, "1,200,0.5")] public float TrapHeight { get; set; } = 30.0f;

    /// <summary>Below this closing speed the ball is not rolling at anybody.</summary>
    [Export(PropertyHint.Range, "1,2000,1")] public float MinimumApproachSpeed { get; set; } = 40.0f;

    /// <summary>Above this it is a projectile, not a pass, and the foot stays out of the way.</summary>
    [Export(PropertyHint.Range, "1,4000,1")] public float MaximumApproachSpeed { get; set; } = 900.0f;

    /// <summary>The beat between the trap and the kick. <c>120</c> is one second at 120 Hz.</summary>
    [Export(PropertyHint.Range, "1,1200,1")] public int DwellTicks { get; set; } = 120;

    /// <summary>Speed the ball leaves the foot at.</summary>
    [Export(PropertyHint.Range, "1,4000,1")] public float KickSpeed { get; set; } = 520.0f;

    /// <summary>Widest loft off horizontal the kick may take — "angled a bit towards the player".</summary>
    [Export(PropertyHint.Range, "0,89,0.5")] public float MaximumKickLoftDegrees { get; set; } = 24.0f;

    /// <summary>How many evenly spaced loft options, the first always dead straight.</summary>
    [Export(PropertyHint.Range, "1,8,1")] public int KickLoftChoices { get; set; } = 3;

    [Export(PropertyHint.Range, "1,2400,1")] public int ReceiveWalkTicks { get; set; } = 600;
    [Export(PropertyHint.Range, "1,1200,1")] public int ReceivePauseTicks { get; set; } = 120;
    [Export(PropertyHint.Range, "1,500,1")] public float WallTurnDistance { get; set; } = 72.0f;
    [Export(PropertyHint.Range, "1,600,1")] public int TurnTicks { get; set; } = 60;

    /// <summary>Spring constants for planting the foot on the ball; engineering, not feel.</summary>
    [Export(PropertyHint.Range, "0.1,20000,0.1")] public float FootStiffness { get; set; } = 260.0f;
    [Export(PropertyHint.Range, "0.1,2000,0.1")] public float FootDamping { get; set; } = 26.0f;
    [Export(PropertyHint.Range, "0.1,200000,1")] public float MaximumFootForce { get; set; } = 5_000.0f;

    public bool IsRuntimeValid => ToDomainTuning().IsValid &&
        float.IsFinite(FootStiffness) && FootStiffness > 0.0f &&
        float.IsFinite(FootDamping) && FootDamping > 0.0f &&
        float.IsFinite(MaximumFootForce) && MaximumFootForce > 0.0f;

    public SoccerPlayTuning ToDomainTuning() => new(
        TrapDistance,
        TrapHeight,
        MinimumApproachSpeed,
        MaximumApproachSpeed,
        DwellTicks,
        KickSpeed,
        MaximumKickLoftDegrees,
        KickLoftChoices,
        ReceiveWalkTicks,
        ReceivePauseTicks,
        WallTurnDistance,
        TurnTicks);

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!ToDomainTuning().IsValid)
        {
            errors.Add(
                "Soccer play tuning must have positive distances, an increasing approach-speed " +
                "window, a positive dwell and kick speed, a loft under 90 degrees, and at least " +
                "one loft choice, valid receive cadence, wall distance, and turn duration");
        }
        if (!float.IsFinite(FootStiffness) || FootStiffness <= 0.0f)
            errors.Add($"{nameof(FootStiffness)} must be finite and positive");
        if (!float.IsFinite(FootDamping) || FootDamping <= 0.0f)
            errors.Add($"{nameof(FootDamping)} must be finite and positive");
        if (!float.IsFinite(MaximumFootForce) || MaximumFootForce <= 0.0f)
            errors.Add($"{nameof(MaximumFootForce)} must be finite and positive");
        return errors;
    }
}
