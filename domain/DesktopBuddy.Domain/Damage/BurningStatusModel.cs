using System;

namespace DesktopBuddy.Domain.Damage;

/// <summary>
/// The authored timing of the Burning status (RAGDOLL §9.3, FR-010.7–FR-010.10), in
/// routed physics ticks rather than seconds, so the laboratory's pause holds a burn by
/// construction — the same clock rule every other model in this project follows.
/// </summary>
public readonly record struct BurningConstants(
    /// <summary>Ticks granted by one fresh fire contact — 4 s at 120 Hz (§9.3).</summary>
    int ApplyTicks,

    /// <summary>The ceiling remaining may never exceed — 8 s at 120 Hz (§9.3).</summary>
    int CapTicks,

    /// <summary>Ticks of continuous burning between two attributed pain events.</summary>
    int PainIntervalTicks)
{
    /// <summary>The M5 Task 7 provisional: 4 s applied, 8 s cap, one pain event each 0.5 s.</summary>
    public static BurningConstants Default => new(
        ApplyTicks: 480, CapTicks: 960, PainIntervalTicks: 60);

    /// <summary>
    /// Ill-formed constants make the model inert rather than clamped: a burn that silently
    /// ran on invented numbers would be indistinguishable from a tuned one.
    /// </summary>
    public bool IsWellFormed() =>
        ApplyTicks > 0 && CapTicks >= ApplyTicks && PainIntervalTicks > 0;
}

/// <summary>
/// One buddy's Burning state. Immutable in and out, on the <see cref="Tools.GunMachine"/>
/// and <see cref="Tools.GrenadeFuseMachine"/> idiom: the caller stores what it was handed
/// and feeds it back, so nothing can observe a half-advanced burn.
/// </summary>
public readonly record struct BurningPhase(
    /// <summary>Routed ticks of burning left.</summary>
    int TicksRemaining,

    /// <summary>
    /// Ticks accumulated toward the next pain event. Reset to zero on ignition, so the
    /// first event lands one full interval after the fire connected — the spray contact
    /// itself scores nothing (plan §2.3).
    /// </summary>
    int TicksSincePainEvent,

    /// <summary>
    /// How many times this buddy has caught fire. It is the burn's identity: the component
    /// mints one interaction id per episode and re-mints it when a lapsed burn reignites,
    /// so rolling-pain bookkeeping sees a continuous burn as one source.
    /// </summary>
    int Episode)
{
    /// <summary>A buddy that is not on fire and never has been.</summary>
    public static BurningPhase None => new(0, 0, 0);

    public bool IsBurning => TicksRemaining > 0;
}

/// <summary>Allocation-free result of one burning tick.</summary>
public readonly record struct BurningTickResult(
    BurningPhase Phase,

    /// <summary>True on the single tick an attributed burn pain event is owed.</summary>
    bool PainEventDue,

    /// <summary>True on the tick the last of the burn ran out.</summary>
    bool Expired,
    bool IsValid);

/// <summary>Allocation-free result of applying fire contact.</summary>
public readonly record struct BurningApplyResult(
    BurningPhase Phase,

    /// <summary>True when this contact lit a buddy that was not already burning.</summary>
    bool Ignited,
    bool IsValid);

/// <summary>
/// The pure Burning timing model (M5 Task 7 plan §2.1). Engine-free and allocation-free.
///
/// <para>It owns <b>timing only</b>. Which part burns, how much a burn event hurts, and
/// what that does to mood are the driving component's and the shared damage pipeline's
/// business — burn pain goes through the same impulse → curve → payout → mood path as a
/// bat or a bullet, so there is no second pain machinery anywhere in this slice.</para>
///
/// <list type="number">
///   <item><b>Apply refreshes and caps.</b> <c>remaining = min(remaining + ApplyTicks,
///   CapTicks)</c>. Sustained per-tick contact therefore pins remaining at the cap, which
///   is exactly FR-010.8's "refresh without exceeding 8 seconds".</item>
///   <item><b>The first pain event is one full interval after ignition.</b> A stream that
///   grazes the buddy for a single tick still costs it a burn, but the contact itself is
///   not also an impact.</item>
///   <item><b>Expiry is silent.</b> There is no exit event beyond the flag going quiet, and
///   ticking a burnt-out buddy is idempotent.</item>
///   <item><b>Clear is immediate and idempotent</b> — the entry point for the hard
///   reposition fail-safe and, later, the Repair Kit (FR-010.10).</item>
/// </list>
/// </summary>
public static class BurningStatus
{
    /// <summary>
    /// Fire touched the buddy. Refreshes an existing burn or lights a fresh one, never
    /// past the authored cap.
    /// </summary>
    public static BurningApplyResult Apply(in BurningPhase phase, in BurningConstants constants)
    {
        if (!constants.IsWellFormed())
        {
            return new BurningApplyResult(phase, Ignited: false, IsValid: false);
        }

        bool ignited = !phase.IsBurning;
        int remaining = Math.Min(phase.TicksRemaining + constants.ApplyTicks, constants.CapTicks);
        return new BurningApplyResult(
            new BurningPhase(
                remaining,
                // A refresh must not restart the pain cadence: a player holding the stream
                // on the buddy would otherwise push the next event away forever and the
                // burn would cost nothing at all.
                ignited ? 0 : phase.TicksSincePainEvent,
                ignited ? phase.Episode + 1 : phase.Episode),
            ignited,
            IsValid: true);
    }

    /// <summary>Advances one routed tick of burning.</summary>
    public static BurningTickResult Tick(in BurningPhase phase, in BurningConstants constants)
    {
        if (!constants.IsWellFormed())
        {
            return new BurningTickResult(phase, PainEventDue: false, Expired: false, IsValid: false);
        }

        if (!phase.IsBurning)
        {
            return new BurningTickResult(
                phase, PainEventDue: false, Expired: false, IsValid: true);
        }

        int remaining = phase.TicksRemaining - 1;
        int sincePain = phase.TicksSincePainEvent + 1;
        bool due = sincePain >= constants.PainIntervalTicks;
        if (due)
        {
            sincePain = 0;
        }

        bool expired = remaining <= 0;
        if (expired)
        {
            remaining = 0;
            sincePain = 0;
        }

        return new BurningTickResult(
            new BurningPhase(remaining, sincePain, phase.Episode),
            due,
            expired,
            IsValid: true);
    }

    /// <summary>
    /// Puts a burn out immediately. Idempotent, and it deliberately keeps the episode
    /// counter: a buddy relit after a clear is a new burn with a new attribution id.
    /// </summary>
    public static BurningPhase Clear(in BurningPhase phase) =>
        new(0, 0, phase.Episode);
}
