using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Stable identity of one persistent Buddy. A Buddy identity is not a live ragdoll actor and must
/// survive Scene teardown/recreation. The reserved legacy-primary ID makes migration of the current
/// one-Buddy save deterministic even when no Character has ever been created.
/// </summary>
public readonly struct BuddyIdentityId : IEquatable<BuddyIdentityId>, IComparable<BuddyIdentityId>
{
    // Reserved only for the one Buddy synthesized from a pre-Next-Fest aggregate save. It is scoped
    // to the player's local identity library; it is never a Workshop/package identity.
    private static readonly Guid LegacyPrimaryValue =
        Guid.Parse("d1f4eb34-43b8-4d2b-a7ec-606c9d5df100");

    private BuddyIdentityId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }
    public bool IsValid => Value != Guid.Empty;
    public static BuddyIdentityId LegacyPrimary => new(LegacyPrimaryValue);

    public static BuddyIdentityId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Buddy identity ID cannot be empty.", nameof(value));
        return new BuddyIdentityId(value);
    }

    public bool Equals(BuddyIdentityId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is BuddyIdentityId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(BuddyIdentityId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? Value.ToString("N") : string.Empty;
    public static bool operator ==(BuddyIdentityId left, BuddyIdentityId right) => left.Equals(right);
    public static bool operator !=(BuddyIdentityId left, BuddyIdentityId right) => !left.Equals(right);
}

/// <summary>
/// Account-global portion of the current aggregate progress. This is the migration/facade target for
/// the future single <c>PlayerProgressState</c>: wallet, permanent ownership, selected tool, lifetime
/// statistics, cumulative run time and global extension data live here, not on an individual Buddy.
/// WorkProgressState already has its own focused persistence owner and remains account-global beside
/// this snapshot.
/// </summary>
public readonly record struct PlayerProgressSnapshot(
    long Revision,
    long BalanceMilliCredits,
    string SelectedToolId,
    IReadOnlyList<string> UnlockedContentIds,
    ProgressStatistics Statistics,
    CumulativeTimes Times,
    ProgressExtensionData? Extensions = null);

/// <summary>
/// Persistent semantics owned by one Buddy identity. CharacterId is the appearance binding; the live
/// physics pose, velocities, pain window, grab state and other transient actor state are deliberately
/// absent.
/// </summary>
public readonly record struct BuddyIdentitySnapshot(
    BuddyIdentityId BuddyIdentityId,
    long Revision,
    Guid? CharacterId,
    float Mood,
    float Fullness,
    IReadOnlyList<string> HarmfulContentIds,
    BuddyTraits Traits,
    IReadOnlyList<FunActivityInterest>? FunInterest = null);

public readonly record struct LegacyProgressPartition(
    PlayerProgressSnapshot Player,
    BuddyIdentitySnapshot Buddy);

public readonly record struct LegacyProgressAggregateSnapshot(
    ProgressSnapshot Progress,
    Guid? ActiveCharacterId);

/// <summary>
/// Lossless deterministic seam from the current schema-8 one-Buddy aggregate to the account/Buddy
/// ownership model required before production multi-Buddy. It does not mutate disk or bump the save
/// schema; persistence can adopt this partition atomically in the later migration packet.
/// </summary>
public static class LegacyProgressPartitionPolicy
{
    public static LegacyProgressPartition Split(
        in ProgressSnapshot legacy,
        Guid? activeCharacterId)
    {
        if (activeCharacterId == Guid.Empty)
            throw new ArgumentException("Active Character ID cannot be the empty GUID.", nameof(activeCharacterId));

        var player = new PlayerProgressSnapshot(
            legacy.Revision,
            legacy.BalanceMilliCredits,
            legacy.SelectedToolId,
            CopyStrings(legacy.UnlockedToolIds),
            CopyStatistics(legacy.Statistics),
            legacy.Times,
            CopyExtensions(legacy.Extensions));

        var buddy = new BuddyIdentitySnapshot(
            BuddyIdentityId.LegacyPrimary,
            legacy.Revision,
            activeCharacterId,
            legacy.Mood,
            legacy.Fullness,
            CopyStrings(legacy.HarmfulContentIds),
            legacy.Traits,
            legacy.FunInterest is null ? null : [.. legacy.FunInterest]);

        return new LegacyProgressPartition(player, buddy);
    }

    /// <summary>
    /// Compatibility proof/helper while the schema-8 writer still exists. Independent account and
    /// Buddy revisions collapse to the newer revision so no mutation can be hidden from the legacy
    /// dirty tracker during staged migration.
    /// </summary>
    public static LegacyProgressAggregateSnapshot RecombineForLegacy(
        in LegacyProgressPartition partition)
    {
        PlayerProgressSnapshot player = partition.Player;
        BuddyIdentitySnapshot buddy = partition.Buddy;
        if (!buddy.BuddyIdentityId.IsValid)
            throw new ArgumentException("Buddy partition requires a stable identity.", nameof(partition));
        if (buddy.CharacterId == Guid.Empty)
            throw new ArgumentException("Buddy Character ID cannot be the empty GUID.", nameof(partition));

        long revision = Math.Max(player.Revision, buddy.Revision);
        var progress = new ProgressSnapshot(
            revision,
            player.BalanceMilliCredits,
            player.SelectedToolId,
            CopyStrings(player.UnlockedContentIds),
            buddy.Mood,
            CopyStrings(buddy.HarmfulContentIds),
            buddy.Traits,
            CopyStatistics(player.Statistics),
            player.Times,
            CopyExtensions(player.Extensions),
            buddy.FunInterest is null ? null : [.. buddy.FunInterest],
            buddy.Fullness);

        return new LegacyProgressAggregateSnapshot(progress, buddy.CharacterId);
    }

    private static string[] CopyStrings(IReadOnlyList<string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new string[source.Count];
        for (int i = 0; i < source.Count; i++)
            copy[i] = source[i];
        return copy;
    }

    private static ProgressStatistics CopyStatistics(in ProgressStatistics source) => new(
        source.ScoredImpacts,
        source.Knockouts,
        source.CareAwards,
        source.TrustResets,
        source.EarnedMilliCredits,
        source.SuccessfulCatches,
        source.TotalPainMilli,
        source.BestOneSecondMilliCredits,
        source.BestThreeSecondMilliCredits,
        source.BestTenSecondMilliCredits,
        source.HighestMood,
        source.LowestMood,
        source.ToolUses is null
            ? null
            : new Dictionary<string, long>(source.ToolUses, StringComparer.Ordinal),
        source.ToolPainMilli is null
            ? null
            : new Dictionary<string, long>(source.ToolPainMilli, StringComparer.Ordinal));

    private static ProgressExtensionData? CopyExtensions(ProgressExtensionData? source)
    {
        if (source is null)
            return null;

        return new ProgressExtensionData(
            source.UnknownSelectedToolId,
            source.UnknownContentIds is null ? null : [.. source.UnknownContentIds],
            source.Values is null
                ? null
                : new Dictionary<string, string>(source.Values, StringComparer.Ordinal));
    }
}
