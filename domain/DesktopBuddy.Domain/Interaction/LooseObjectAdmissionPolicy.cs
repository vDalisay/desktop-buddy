using System;

namespace DesktopBuddy.Domain.Interaction;

/// <summary>
/// One registry slot as the admission rule sees it. Runtime identity, bodies, profiles, and
/// cleanup stay with the registry; this is only what FR-014.2/14.3 need to choose a victim.
/// </summary>
/// <param name="Occupied">False for a free slot.</param>
/// <param name="SafeToEvict">Authored: this kind of object may be culled at all.</param>
/// <param name="Hazardous">Authored: live hazards are never culled (FR-014.3).</param>
/// <param name="PlayerHeld">The player's grab currently owns it.</param>
/// <param name="BuddyHeld">The buddy is carrying, eating, or about to throw it.</param>
/// <param name="ExplicitlyProtected">
/// Runtime protection the owning system asserted — a committed launch, a live fuse, an
/// in-flight consume. Set by the system that knows, never inferred here.
/// </param>
/// <param name="SpawnSequence">
/// Monotonic admission order. Lower is older; it is deliberately not a timestamp, so
/// eviction order cannot drift with the clock.
/// </param>
public readonly record struct LooseObjectSlot(
    bool Occupied,
    bool SafeToEvict,
    bool Hazardous,
    bool PlayerHeld,
    bool BuddyHeld,
    bool ExplicitlyProtected,
    ulong SpawnSequence)
{
    /// <summary>The FR-014.3 protection test: any one of these spares the object.</summary>
    public bool IsProtected =>
        !SafeToEvict || Hazardous || PlayerHeld || BuddyHeld || ExplicitlyProtected;

    /// <summary>An occupied slot the cap may reclaim.</summary>
    public bool IsEvictable => Occupied && !IsProtected;
}

/// <summary>What admitting one more object requires.</summary>
public enum AdmissionOutcome
{
    /// <summary>There is room; use <see cref="AdmissionDecision.Slot"/>.</summary>
    FreeSlot,

    /// <summary>At capacity; the object in <see cref="AdmissionDecision.Slot"/> must go first.</summary>
    Evict,

    /// <summary>At capacity with nothing evictable. The spawn is refused, cleanly.</summary>
    Refused,
}

public readonly record struct AdmissionDecision(AdmissionOutcome Outcome, int Slot)
{
    public static AdmissionDecision Refused => new(AdmissionOutcome.Refused, -1);
}

/// <summary>
/// The pure FR-014 cap rule: at most <see cref="Capacity"/> loose objects exist, admitting one
/// more evicts the <b>oldest evictable</b> object, and a protected object is never the victim —
/// if everything is protected the spawn is refused rather than forced through.
///
/// <para>
/// This is a decision, not an owner. <c>LooseObjectRegistry</c> remains the only thing that
/// holds runtime identity, flags, and cleanup; it asks this policy which slot to use. There is
/// deliberately no second budget (ARCHITECTURE §15).
/// </para>
///
/// <para>
/// Projectiles are not loose objects. Bullets, pellets, and VFX live in their own bounded pools
/// and never consume one of these slots (RAGDOLL §10).
/// </para>
/// </summary>
public static class LooseObjectAdmissionPolicy
{
    /// <summary>FR-014.1. The single authoritative cap.</summary>
    public const int Capacity = 24;

    /// <summary>
    /// Chooses where the next object goes. Allocation-free: the caller owns the span, and
    /// nothing is built per call.
    /// </summary>
    public static AdmissionDecision Decide(ReadOnlySpan<LooseObjectSlot> slots)
    {
        for (int index = 0; index < slots.Length; index++)
        {
            if (!slots[index].Occupied)
                return new AdmissionDecision(AdmissionOutcome.FreeSlot, index);
        }

        int victim = -1;
        ulong oldest = ulong.MaxValue;
        for (int index = 0; index < slots.Length; index++)
        {
            LooseObjectSlot slot = slots[index];
            if (!slot.IsEvictable || slot.SpawnSequence >= oldest)
                continue;

            victim = index;
            oldest = slot.SpawnSequence;
        }

        return victim < 0
            ? AdmissionDecision.Refused
            : new AdmissionDecision(AdmissionOutcome.Evict, victim);
    }
}
