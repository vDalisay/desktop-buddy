using System;

namespace DesktopBuddy.Domain.Tools;

/// <summary>
/// The authored constants one cursor gun needs (RAGDOLL §9.2). Durations are in
/// routed physics ticks, never seconds or wall clock, so a paused laboratory and a
/// hit-lag freeze both stop the cadence exactly as they stop everything else.
/// </summary>
public readonly record struct GunConstants(
    int MagazineCapacity,

    /// <summary>Ticks that must pass between two fired shots.</summary>
    int ShotIntervalTicks,

    /// <summary>Ticks a reload takes to complete after the tick that starts it.</summary>
    int ReloadTicks,

    /// <summary>Projectiles one shot releases: <c>1</c> for a bullet, more for a spread.</summary>
    int ProjectilesPerShot)
{
    public bool IsWellFormed() =>
        MagazineCapacity > 0 &&
        ShotIntervalTicks > 0 &&
        ReloadTicks > 0 &&
        ProjectilesPerShot > 0;
}

/// <summary>
/// The carried state of one gun. Immutable for the same reason the charged swing's
/// phase is: the caller stores what it was handed and feeds it back, so nothing can
/// read a half-updated magazine.
/// </summary>
public readonly record struct GunPhase(
    int Rounds,

    /// <summary>
    /// Ticks since the last fired shot, saturating. Starts satisfied so a gun that
    /// has just been drawn fires on the first press instead of eating it.
    /// </summary>
    int TicksSinceShot,

    /// <summary>Ticks left before the running reload completes, or <c>0</c> when not reloading.</summary>
    int ReloadTicksRemaining,

    /// <summary>Trigger state last tick — this is what makes firing one-per-press.</summary>
    bool TriggerHeld,

    /// <summary>Monotonic shot identity; <c>0</c> before the first shot.</summary>
    int ShotEpoch)
{
    public bool IsReloading => ReloadTicksRemaining > 0;

    /// <summary>A freshly drawn gun with a full magazine.</summary>
    public static GunPhase FullyLoaded(in GunConstants constants) => new(
        Rounds: Math.Max(0, constants.MagazineCapacity),
        TicksSinceShot: Math.Max(0, constants.ShotIntervalTicks),
        ReloadTicksRemaining: 0,
        TriggerHeld: false,
        ShotEpoch: 0);
}

/// <summary>External facts for one gun tick.</summary>
public readonly record struct GunInput(
    GunPhase Phase,

    /// <summary>Primary held state this tick; the model finds the press edge itself.</summary>
    bool TriggerHeld,

    /// <summary>The <c>R</c> reload action was pressed this tick.</summary>
    bool ReloadRequested,
    GunConstants Constants);

/// <summary>Allocation-free result for one gun tick.</summary>
public readonly record struct GunResult(
    GunPhase Phase,

    /// <summary>True on the tick a shot left the barrel.</summary>
    bool Fired,

    /// <summary>Projectiles this tick's shot released, or <c>0</c> when nothing fired.</summary>
    int Projectiles,

    /// <summary>True on the tick the trigger was pulled on an empty magazine.</summary>
    bool DryFired,

    /// <summary>True on the tick a reload began, whether requested or automatic.</summary>
    bool ReloadStarted,

    /// <summary>True on the tick the magazine came back full.</summary>
    bool ReloadCompleted,
    bool IsValid);

/// <summary>
/// The pure cadence/magazine/reload state machine every cursor gun runs on
/// (RAGDOLL §9.2). One model, one profile per gun: the Pistol and the Shotgun differ
/// only in authored numbers.
///
/// <para>The rules, in the order they resolve on a tick:</para>
/// <list type="number">
///   <item>A running reload owns the gun. It ignores the trigger and further reload
///   requests, and completes exactly <see cref="GunConstants.ReloadTicks"/> ticks after
///   the tick that started it.</item>
///   <item>An explicit reload request is honored only when the magazine is not
///   already full — otherwise <c>R</c> would be a free way to cancel the shot
///   interval.</item>
///   <item>A trigger <b>press edge</b> fires at most one shot. Holding the trigger
///   never fires a second: that is what "fires once per primary press" means, and it
///   is why the model tracks the previous trigger state rather than taking an edge
///   from the caller.</item>
///   <item>A press inside the shot interval is simply spent — no shot, no dry fire,
///   and no queued shot waiting to escape later.</item>
///   <item>A press on an empty magazine is a dry fire and starts the automatic
///   reload. Emptying the magazine does <b>not</b>: the eighth shot leaves the gun
///   empty and ready, and it is the ninth pull that reloads it.</item>
/// </list>
/// </summary>
public static class GunMachine
{
    public static GunResult Tick(in GunInput input)
    {
        GunConstants constants = input.Constants;
        if (!constants.IsWellFormed())
        {
            return Inert(input.Phase);
        }

        GunPhase phase = input.Phase with { TicksSinceShot = Advance(input.Phase.TicksSinceShot) };
        bool fired = false;
        bool dryFired = false;
        bool reloadStarted = false;
        bool reloadCompleted = false;
        int projectiles = 0;

        if (phase.IsReloading)
        {
            int remaining = phase.ReloadTicksRemaining - 1;
            if (remaining <= 0)
            {
                remaining = 0;
                reloadCompleted = true;
                phase = phase with { Rounds = constants.MagazineCapacity };
            }

            phase = phase with { ReloadTicksRemaining = remaining };
        }
        else if (input.ReloadRequested && phase.Rounds < constants.MagazineCapacity)
        {
            phase = phase with { ReloadTicksRemaining = constants.ReloadTicks };
            reloadStarted = true;
        }
        else if (input.TriggerHeld && !phase.TriggerHeld)
        {
            if (phase.TicksSinceShot < constants.ShotIntervalTicks)
            {
                // Inside the cadence window: the press is consumed and nothing else
                // happens. It must not linger, or a player mashing the button would
                // get a burst the moment the interval elapsed.
            }
            else if (phase.Rounds > 0)
            {
                fired = true;
                projectiles = constants.ProjectilesPerShot;
                phase = phase with
                {
                    Rounds = phase.Rounds - 1,
                    TicksSinceShot = 0,
                    ShotEpoch = Advance(phase.ShotEpoch),
                };
            }
            else
            {
                dryFired = true;
                reloadStarted = true;
                phase = phase with { ReloadTicksRemaining = constants.ReloadTicks };
            }
        }

        phase = phase with { TriggerHeld = input.TriggerHeld };
        return new GunResult(
            phase,
            fired,
            projectiles,
            dryFired,
            reloadStarted,
            reloadCompleted,
            IsValid: true);
    }

    /// <summary>Increment that saturates instead of wrapping into negative counters.</summary>
    private static int Advance(int value) =>
        value >= int.MaxValue - 1 ? int.MaxValue - 1 : Math.Max(0, value) + 1;

    private static GunResult Inert(GunPhase phase) => new(
        phase,
        Fired: false,
        Projectiles: 0,
        DryFired: false,
        ReloadStarted: false,
        ReloadCompleted: false,
        IsValid: false);
}
