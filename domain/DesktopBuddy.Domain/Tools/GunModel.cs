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
    int ProjectilesPerShot,

    /// <summary>
    /// Whether a fired shot leaves the chamber empty until the player works the action.
    /// A pump gun answers <c>true</c>: the shell that just went off has to be cycled out
    /// before the next one can go off, and the player pays a primary press for it.
    /// </summary>
    bool RequiresPumpBetweenShots = false,

    /// <summary>
    /// Ticks the pump stroke takes. The gun ignores the trigger while it runs, so this is
    /// the real cost of the action — <c>24</c> is a fifth of a second at 120 Hz.
    /// </summary>
    int PumpTicks = 0,

    /// <summary>
    /// How long a press that arrived too early is remembered for. Zero — the default, and
    /// what every gun did before pumps — drops such a press on the floor. A pump gun wants
    /// a buffer: its stroke plus its interval is long enough that a player mashing primary
    /// spends most presses into a dead gun and reads it as the gun jamming.
    /// </summary>
    int PressBufferTicks = 0)
{
    public bool IsWellFormed() =>
        MagazineCapacity > 0 &&
        ShotIntervalTicks > 0 &&
        ReloadTicks > 0 &&
        ProjectilesPerShot > 0 &&
        (!RequiresPumpBetweenShots || PumpTicks > 0);
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
    int ShotEpoch,

    /// <summary>
    /// True on a pump gun between the shot that emptied the chamber and the stroke that
    /// recharges it. Always false on a gun that authors no pump, which is what keeps the
    /// pistol and the nerf on exactly the path they were on before pumps existed.
    /// </summary>
    bool ChamberEmpty = false,

    /// <summary>Ticks left in the running pump stroke, or <c>0</c> when none is running.</summary>
    int PumpTicksRemaining = 0,

    /// <summary>
    /// Ticks left on a remembered press that could not act when it arrived. It is consumed
    /// by the first tick that can act on it, so it produces one shot, never a burst.
    /// </summary>
    int BufferedPressTicks = 0)
{
    public bool IsReloading => ReloadTicksRemaining > 0;

    /// <summary>True while the action is being worked; the trigger is dead for the duration.</summary>
    public bool IsPumping => PumpTicksRemaining > 0;

    /// <summary>A freshly drawn gun with a full magazine and a charged chamber.</summary>
    public static GunPhase FullyLoaded(in GunConstants constants) => new(
        Rounds: Math.Max(0, constants.MagazineCapacity),
        TicksSinceShot: Math.Max(0, constants.ShotIntervalTicks),
        ReloadTicksRemaining: 0,
        TriggerHeld: false,
        ShotEpoch: 0,
        ChamberEmpty: false,
        PumpTicksRemaining: 0,
        BufferedPressTicks: 0);
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
    bool IsValid,

    /// <summary>True on the tick a primary press started the pump stroke instead of firing.</summary>
    bool PumpStarted = false,

    /// <summary>True on the tick the pump stroke finished and the chamber came back.</summary>
    bool PumpCompleted = false);

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
///   and no queued shot waiting to escape later — unless the gun authors
///   <see cref="GunConstants.PressBufferTicks"/>, in which case that one press is
///   remembered for that many ticks and acts on the first tick the gun can act. It is
///   still one press for one action: the buffer holds a single press, is cleared the
///   moment it is used, and a held trigger never refills it.</item>
///   <item>A press on an empty magazine is a dry fire and starts the automatic
///   reload. Emptying the magazine does <b>not</b>: the eighth shot leaves the gun
///   empty and ready, and it is the ninth pull that reloads it.</item>
///   <item>On a gun that authors <see cref="GunConstants.RequiresPumpBetweenShots"/>,
///   a fired shot leaves the chamber empty, and the <b>next primary press works the
///   action instead of firing</b>. The stroke owns the gun for
///   <see cref="GunConstants.PumpTicks"/>, and only the press after it can fire. The
///   pump is deliberately <b>not</b> gated on the shot interval: a player who cycles
///   the action the instant the shell leaves is rewarded with a gun that is ready when
///   the interval elapses, which is the whole feel of a pump gun. A completed reload
///   charges the chamber, so a reload is never followed by a wasted stroke.</item>
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
        bool pressEdge = input.TriggerHeld && !phase.TriggerHeld;
        // The remembered press ages every tick whether or not the gun can act on it, so a
        // press made a long way ahead of readiness dies rather than firing out of nowhere.
        int buffered = Math.Max(0, phase.BufferedPressTicks - 1);
        bool wantsAction = pressEdge || buffered > 0;
        bool fired = false;
        bool dryFired = false;
        bool reloadStarted = false;
        bool reloadCompleted = false;
        bool pumpStarted = false;
        bool pumpCompleted = false;
        int projectiles = 0;

        if (pressEdge && constants.PressBufferTicks > 0 &&
            (phase.IsReloading || phase.IsPumping))
        {
            buffered = constants.PressBufferTicks;
        }

        if (phase.IsReloading)
        {
            int remaining = phase.ReloadTicksRemaining - 1;
            if (remaining <= 0)
            {
                remaining = 0;
                reloadCompleted = true;
                // A reload ends with the action closed on a live shell: charging the
                // chamber here is what stops a reload from being followed by a stroke the
                // player has no way to know they still owe.
                phase = phase with
                {
                    Rounds = constants.MagazineCapacity,
                    ChamberEmpty = false,
                    PumpTicksRemaining = 0,
                };
            }

            phase = phase with { ReloadTicksRemaining = remaining };
        }
        else if (phase.IsPumping)
        {
            // The stroke owns the gun the way a reload does: presses during it are spent,
            // and nothing queues behind it.
            int remaining = phase.PumpTicksRemaining - 1;
            if (remaining <= 0)
            {
                remaining = 0;
                pumpCompleted = true;
                phase = phase with { ChamberEmpty = false };
            }

            phase = phase with { PumpTicksRemaining = remaining };
        }
        else if (input.ReloadRequested && phase.Rounds < constants.MagazineCapacity)
        {
            phase = phase with { ReloadTicksRemaining = constants.ReloadTicks };
            reloadStarted = true;
        }
        else if (wantsAction)
        {
            if (phase.ChamberEmpty)
            {
                // A press is spent by whatever it does — here, working the action.
                buffered = 0;
                // The press the player owes the action. Charged ahead of the cadence check
                // on purpose — see the class rules — so cycling early is rewarded rather
                // than swallowed by the interval that is still running.
                pumpStarted = true;
                phase = phase with { PumpTicksRemaining = Math.Max(1, constants.PumpTicks) };
            }
            else if (phase.TicksSinceShot < constants.ShotIntervalTicks)
            {
                // Inside the cadence window. Without an authored buffer the press is
                // consumed and nothing else happens — it must not linger, or a player
                // mashing the button would get a burst the moment the interval elapsed.
                // With one, this single press is remembered for its authored window — and a
                // press already in the buffer keeps ageing rather than being cleared here,
                // which is the whole point of it surviving the window.
                buffered = pressEdge ? constants.PressBufferTicks : buffered;
            }
            else if (phase.Rounds > 0)
            {
                buffered = 0;
                fired = true;
                projectiles = constants.ProjectilesPerShot;
                phase = phase with
                {
                    Rounds = phase.Rounds - 1,
                    TicksSinceShot = 0,
                    ShotEpoch = Advance(phase.ShotEpoch),
                    ChamberEmpty = constants.RequiresPumpBetweenShots,
                };
            }
            else
            {
                buffered = 0;
                dryFired = true;
                reloadStarted = true;
                phase = phase with { ReloadTicksRemaining = constants.ReloadTicks };
            }
        }

        phase = phase with { TriggerHeld = input.TriggerHeld, BufferedPressTicks = buffered };
        return new GunResult(
            phase,
            fired,
            projectiles,
            dryFired,
            reloadStarted,
            reloadCompleted,
            IsValid: true,
            pumpStarted,
            pumpCompleted);
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
