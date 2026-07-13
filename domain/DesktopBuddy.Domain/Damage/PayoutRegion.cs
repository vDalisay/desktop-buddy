using System;
using DesktopBuddy.Domain.Buddy;

namespace DesktopBuddy.Domain.Damage;

/// <summary>
/// The four body-region payout classes used by the reward formula (RAGDOLL §7.4).
/// The six anatomy parts collapse onto these: Head→Head, Torso→Torso, both hands→Arms,
/// both feet→Legs (RAGDOLL §6 part mapping).
/// </summary>
public enum PayoutRegion
{
    Head,
    Torso,
    Arms,
    Legs,
}

/// <summary>Maps a stable <see cref="BuddyPart"/> to its <see cref="PayoutRegion"/>.</summary>
public static class PayoutRegions
{
    public static PayoutRegion Of(BuddyPart part) => part switch
    {
        BuddyPart.Head => PayoutRegion.Head,
        BuddyPart.Torso => PayoutRegion.Torso,
        BuddyPart.LeftHand or BuddyPart.RightHand => PayoutRegion.Arms,
        BuddyPart.LeftFoot or BuddyPart.RightFoot => PayoutRegion.Legs,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown buddy part."),
    };
}
