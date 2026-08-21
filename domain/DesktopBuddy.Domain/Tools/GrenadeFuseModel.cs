using System;

namespace DesktopBuddy.Domain.Tools;

/// <summary>Where one grenade is in its pin-and-fuse life.</summary>
public enum GrenadeFuseStage
{
    /// <summary>Pin in. Inert forever, however it is thrown or caught.</summary>
    Pinned,

    /// <summary>Pin out, still under player control. Safe for as long as it is held.</summary>
    PinPulled,

    /// <summary>Let go with the pin out. The countdown is running and nothing stops it.</summary>
    Live,

    /// <summary>Terminal. The blast has been scored and the body is gone.</summary>
    Detonated,
}

/// <summary>
/// The authored fuse timing. In routed physics ticks, never seconds, so the laboratory's
/// pause holds the fuse by construction — exactly like every other clock in this project.
/// </summary>
public readonly record struct GrenadeFuseConstants(int FuseTicks)
{
    /// <summary>3.0 s at 120 Hz (plan §2.1).</summary>
    public static GrenadeFuseConstants Default => new(FuseTicks: 360);

    public bool IsWellFormed() => FuseTicks > 0;
}

/// <summary>
/// The carried state of one grenade's fuse. Immutable for the same reason
/// <see cref="GunPhase"/> is: the caller stores what it was handed and feeds it back, so
/// nothing can observe a half-advanced countdown.
/// </summary>
public readonly record struct GrenadeFusePhase(
    GrenadeFuseStage Stage,

    /// <summary>Ticks left before detonation while <see cref="GrenadeFuseStage.Live"/>.</summary>
    int TicksRemaining)
{
    /// <summary>A grenade as it leaves the spawn key: pin in, inert.</summary>
    public static GrenadeFusePhase Fresh => new(GrenadeFuseStage.Pinned, TicksRemaining: 0);

    /// <summary>True while the countdown is running — the registry's protection rule.</summary>
    public bool IsCountingDown => Stage == GrenadeFuseStage.Live;

    /// <summary>
    /// True once the pin is out and can never go back in. Used by presentation to decide
    /// whether the pin ring is still drawn on the body.
    /// </summary>
    public bool PinIsOut => Stage != GrenadeFuseStage.Pinned;
}

/// <summary>External facts for one fuse tick.</summary>
public readonly record struct GrenadeFuseInput(
    GrenadeFusePhase Phase,

    /// <summary>
    /// The secondary button's press edge. The first one pulls the pin; later ones are
    /// nothing, because the pin only comes out once.
    /// </summary>
    bool PinPullRequested,

    /// <summary>
    /// Whether the player holds this grenade right now — by the grab tether or by the
    /// launcher's aim, which are the same thing to the fuse: control has not been let go.
    /// </summary>
    bool PlayerControlled,
    GrenadeFuseConstants Constants,

    /// <summary>
    /// Something hit the grenade hard enough to knock the pin out — a bat, a glove, a Nerf
    /// dart. Unlike <see cref="PinPullRequested"/> this does not need the player to be
    /// holding it: that is the whole point of knocking the pin out of one lying on the floor.
    /// </summary>
    bool StruckPinPull = false,

    /// <summary>
    /// Something set it off outright rather than starting the countdown — a shotgun shell,
    /// three pistol rounds, another grenade's blast, three seconds under the flame.
    /// </summary>
    bool ForcedDetonation = false);

/// <summary>Allocation-free result for one fuse tick.</summary>
public readonly record struct GrenadeFuseResult(
    GrenadeFusePhase Phase,

    /// <summary>True on the tick the pin left the grenade — the tick to spawn the pin body.</summary>
    bool PinPulled,

    /// <summary>True on the tick the countdown started, whichever release started it.</summary>
    bool FuseStarted,

    /// <summary>True on the single tick the grenade goes off.</summary>
    bool Detonated,
    bool IsValid);

/// <summary>
/// The pure pin-and-fuse state machine (M5 Task 6 plan §2.1). Engine-free and
/// allocation-free, like <see cref="GunMachine"/>.
///
/// <para><c>Pinned → PinPulled → Live(countdown) → Detonated</c>. The rules, in the order
/// they resolve on a tick:</para>
/// <list type="number">
///   <item><b>Pinned is inert forever.</b> A grenade thrown with a plain grab never
///   explodes — it is a ball, including for the buddy. Secondary is the only way to pull
///   the pin, and secondary's first press is also the pullback's begin, so every
///   pullback-launched grenade is live and every inert one was thrown by hand. There is no
///   separate arming input.</item>
///   <item><b>The pin is one-way.</b> Cancelling the pullback keeps the grenade in
///   <see cref="GrenadeFuseStage.PinPulled"/> while it is still held; it does not go back
///   in, and a second press does nothing.</item>
///   <item><b>The countdown starts when player control ends</b> — launch release or grab
///   release, the model does not distinguish them, because to the grenade they are the
///   same event: nobody is holding it any more.</item>
///   <item><b>Nothing pauses or resets a live fuse.</b> Not a buddy catch, not a player
///   re-grab (owner default 1). It goes off in whoever's hand is holding it. The routed
///   tick is the only clock it counts, so a paused laboratory holds it and never skips it.</item>
///   <item><b>Detonated is terminal.</b> The tick is idempotent afterwards: a caller that
///   keeps ticking a spent grenade gets no second blast.</item>
/// </list>
/// </summary>
public static class GrenadeFuseMachine
{
    public static GrenadeFuseResult Tick(in GrenadeFuseInput input)
    {
        GrenadeFuseConstants constants = input.Constants;
        if (!constants.IsWellFormed())
        {
            return Inert(input.Phase);
        }

        GrenadeFusePhase phase = input.Phase;
        bool pinPulled = false;
        bool fuseStarted = false;
        bool detonated = false;

        if (phase.Stage == GrenadeFuseStage.Detonated)
        {
            return new GrenadeFuseResult(phase, false, false, false, IsValid: true);
        }

        // Set off outright. It skips the countdown entirely, so a grenade that never had its
        // pin pulled still goes off — a shotgun shell does not care about the pin.
        if (input.ForcedDetonation)
        {
            return new GrenadeFuseResult(
                new GrenadeFusePhase(GrenadeFuseStage.Detonated, 0),
                PinPulled: false,
                FuseStarted: false,
                Detonated: true,
                IsValid: true);
        }

        // The pin can only be pulled out of a grenade somebody is holding. A pull request
        // arriving for an airborne grenade is not a way to arm it in flight.
        if (phase.Stage == GrenadeFuseStage.Pinned &&
            ((input.PinPullRequested && input.PlayerControlled) || input.StruckPinPull))
        {
            phase = phase with { Stage = GrenadeFuseStage.PinPulled };
            pinPulled = true;
        }

        if (phase.Stage == GrenadeFuseStage.PinPulled && !input.PlayerControlled)
        {
            phase = new GrenadeFusePhase(GrenadeFuseStage.Live, constants.FuseTicks);
            fuseStarted = true;
        }
        else if (phase.Stage == GrenadeFuseStage.Live)
        {
            int remaining = phase.TicksRemaining - 1;
            if (remaining <= 0)
            {
                phase = new GrenadeFusePhase(GrenadeFuseStage.Detonated, 0);
                detonated = true;
            }
            else
            {
                phase = phase with { TicksRemaining = remaining };
            }
        }

        return new GrenadeFuseResult(phase, pinPulled, fuseStarted, detonated, IsValid: true);
    }

    private static GrenadeFuseResult Inert(GrenadeFusePhase phase) => new(
        phase,
        PinPulled: false,
        FuseStarted: false,
        Detonated: false,
        IsValid: false);
}
