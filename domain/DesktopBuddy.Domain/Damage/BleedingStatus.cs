using System;

namespace DesktopBuddy.Domain.Damage;

/// <summary>
/// The authored timing of one bleeding wound, in routed physics ticks rather than seconds,
/// so pausing holds a bleed by construction — the same clock rule
/// <see cref="BurningStatus"/> and every other model in this project follows.
///
/// <para>Gore Mode is <b>presentation only</b>. Nothing here feeds pain, payout, mood, or
/// knockout: a wound decides how long blood is drawn and how often a drip is emitted, and
/// that is the whole of its authority. The buddy stays immortal (FR-004.3) and a run with
/// Gore Mode on simulates identically to the same seed with it off.</para>
/// </summary>
public readonly record struct BleedingConstants(
    /// <summary>Ticks granted by one fresh wound — 6 s at 120 Hz.</summary>
    int WoundTicks,

    /// <summary>The ceiling a re-wounded part may never exceed — 15 s at 120 Hz.</summary>
    int CapTicks,

    /// <summary>Ticks between two drips from a wound at full severity.</summary>
    int DripIntervalTicks,

    /// <summary>
    /// Longest a drip interval may stretch to as a wound weakens. A wound that has nearly
    /// run out drips at this cadence rather than stopping abruptly, which is what makes a
    /// bleed read as tapering off instead of being switched off.
    /// </summary>
    int SlowestDripIntervalTicks)
{
    /// <summary>The shipped provisional: 6 s per wound, 15 s cap, drips every 0.15–0.6 s.</summary>
    public static BleedingConstants Default => new(
        WoundTicks: 720, CapTicks: 1800, DripIntervalTicks: 18, SlowestDripIntervalTicks: 72);

    /// <summary>
    /// Ill-formed constants make the model inert rather than clamped: a wound that silently
    /// ran on invented numbers would be indistinguishable from a tuned one.
    /// </summary>
    public bool IsWellFormed() =>
        WoundTicks > 0 &&
        CapTicks >= WoundTicks &&
        DripIntervalTicks > 0 &&
        SlowestDripIntervalTicks >= DripIntervalTicks;
}

/// <summary>
/// One part's wound. Immutable in and out, on the <see cref="BurningPhase"/> idiom: the
/// caller stores what it was handed and feeds it back, so nothing can observe a
/// half-advanced bleed. One slot per <see cref="Buddy.BuddyPart"/> is the whole model —
/// a second stab in the same arm deepens the wound already there rather than opening a
/// list that would need pruning.
/// </summary>
public readonly record struct BleedWound(
    /// <summary>Routed ticks of bleeding left.</summary>
    int TicksRemaining,

    /// <summary>Ticks accumulated toward the next drip.</summary>
    int TicksSinceDrip,

    /// <summary>
    /// How hard this wound bleeds, <c>0..1</c>. Set by the opening hit and never raised by
    /// time; a deeper hit on a shallow wound raises it, a shallower one does not lower it.
    /// </summary>
    float Severity,

    /// <summary>
    /// How many times this part has been opened. It is the wound's identity, so a consumer
    /// can tell a re-opened wound from the one that was already there.
    /// </summary>
    int Episode)
{
    /// <summary>A part that is not bleeding and never has been.</summary>
    public static BleedWound None => new(0, 0, 0.0f, 0);

    public bool IsBleeding => TicksRemaining > 0;

    /// <summary>
    /// How strongly the wound is bleeding right now, <c>0..1</c>: its authored severity
    /// tapered by how much of it is left. Presentation scales droplet count, size and
    /// spread by this, so a wound visibly weakens instead of stopping at full flow.
    /// </summary>
    public float Intensity(in BleedingConstants constants) =>
        !IsBleeding || !constants.IsWellFormed()
            ? 0.0f
            : Severity * Math.Min(1.0f, TicksRemaining / (float)constants.WoundTicks);
}

/// <summary>Allocation-free result of one bleeding tick.</summary>
public readonly record struct BleedTickResult(
    BleedWound Wound,

    /// <summary>True on the single tick a drip is owed.</summary>
    bool DripDue,

    /// <summary>True on the tick the last of the bleed ran out.</summary>
    bool Expired,
    bool IsValid);

/// <summary>Allocation-free result of opening or deepening a wound.</summary>
public readonly record struct BleedOpenResult(
    BleedWound Wound,

    /// <summary>True when this hit opened a part that was not already bleeding.</summary>
    bool Opened,
    bool IsValid);

/// <summary>
/// The pure bleeding timing model for Gore Mode. Engine-free and allocation-free, and a
/// deliberate sibling of <see cref="BurningStatus"/> rather than a generalisation of it:
/// the two share a shape but not a rule, and one merged "status" abstraction would have to
/// carry both sets of flags to serve either.
///
/// <list type="number">
///   <item><b>Opening refreshes and caps.</b> <c>remaining = min(remaining + WoundTicks,
///   CapTicks)</c>, exactly as a burn refreshes, so emptying a magazine into one arm makes
///   it bleed longer but never forever.</item>
///   <item><b>Severity only ever rises.</b> A grazing second hit cannot make a deep wound
///   bleed less than it already was.</item>
///   <item><b>The first drip is one interval after the wound opens.</b> The opening hit's
///   own spray is the presentation's business; the model does not double-count it.</item>
///   <item><b>The cadence slows as the wound closes</b>, between the two authored
///   intervals, so a bleed tapers.</item>
///   <item><b>Expiry is silent</b>, and ticking a closed wound is idempotent.</item>
///   <item><b>Clear is immediate and idempotent</b> — the Repair Kit's entry point, and the
///   fail-safe for a hard reposition.</item>
/// </list>
/// </summary>
public static class BleedingStatus
{
    /// <summary>
    /// A piercing hit landed on this part. Opens a fresh wound or deepens the one already
    /// there, never past the authored cap.
    /// </summary>
    /// <param name="severity">
    /// How deep the hit was, <c>0..1</c>. Clamped, because a caller deriving this from an
    /// impulse ratio should not be able to hand the presentation a firehose.
    /// </param>
    public static BleedOpenResult Open(
        in BleedWound wound,
        float severity,
        in BleedingConstants constants)
    {
        if (!constants.IsWellFormed() || !float.IsFinite(severity) || severity <= 0.0f)
        {
            return new BleedOpenResult(wound, Opened: false, IsValid: false);
        }

        severity = Math.Min(1.0f, severity);
        bool opened = !wound.IsBleeding;
        int remaining = Math.Min(wound.TicksRemaining + constants.WoundTicks, constants.CapTicks);
        return new BleedOpenResult(
            new BleedWound(
                remaining,
                // A second hit must not restart the cadence: shooting a part repeatedly
                // would otherwise push the next drip away forever and it would never bleed.
                opened ? 0 : wound.TicksSinceDrip,
                Math.Max(wound.Severity, severity),
                wound.Episode + 1),
            opened,
            IsValid: true);
    }

    /// <summary>Advances one routed tick of bleeding.</summary>
    public static BleedTickResult Tick(in BleedWound wound, in BleedingConstants constants)
    {
        if (!constants.IsWellFormed())
        {
            return new BleedTickResult(wound, DripDue: false, Expired: false, IsValid: false);
        }

        if (!wound.IsBleeding)
        {
            return new BleedTickResult(wound, DripDue: false, Expired: false, IsValid: true);
        }

        int remaining = wound.TicksRemaining - 1;
        int sinceDrip = wound.TicksSinceDrip + 1;
        bool due = sinceDrip >= DripIntervalFor(wound, constants);
        if (due)
        {
            sinceDrip = 0;
        }

        bool expired = remaining <= 0;
        if (expired)
        {
            remaining = 0;
            sinceDrip = 0;
        }

        return new BleedTickResult(
            new BleedWound(remaining, sinceDrip, wound.Severity, wound.Episode),
            due,
            expired,
            IsValid: true);
    }

    /// <summary>
    /// Ticks between drips at this wound's current strength: the fast authored interval at
    /// full intensity, the slow one as it closes.
    /// </summary>
    public static int DripIntervalFor(in BleedWound wound, in BleedingConstants constants)
    {
        if (!constants.IsWellFormed())
        {
            return 0;
        }

        float intensity = wound.Intensity(constants);
        int span = constants.SlowestDripIntervalTicks - constants.DripIntervalTicks;
        return constants.SlowestDripIntervalTicks - (int)MathF.Round(span * intensity);
    }

    /// <summary>
    /// Closes a wound immediately. Idempotent, and it deliberately keeps the episode
    /// counter: a part re-opened after a patch-up is a new wound.
    /// </summary>
    public static BleedWound Clear(in BleedWound wound) =>
        new(0, 0, 0.0f, wound.Episode);
}
