using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class ProgressStatePartitionTests
{
    [Fact]
    public void Legacy_aggregate_splits_losslessly_into_account_and_one_deterministic_buddy()
    {
        Guid characterId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        ProgressSnapshot legacy = RichLegacySnapshot();

        LegacyProgressPartition first = LegacyProgressPartitionPolicy.Split(legacy, characterId);
        LegacyProgressPartition second = LegacyProgressPartitionPolicy.Split(legacy, characterId);

        Assert.Equal(BuddyIdentityId.LegacyPrimary, first.Buddy.BuddyIdentityId);
        Assert.Equal(first.Buddy.BuddyIdentityId, second.Buddy.BuddyIdentityId);
        Assert.Equal(characterId, first.Buddy.CharacterId);

        Assert.Equal(legacy.BalanceMilliCredits, first.Player.BalanceMilliCredits);
        Assert.Equal(legacy.SelectedToolId, first.Player.SelectedToolId);
        Assert.Equal(legacy.UnlockedToolIds, first.Player.UnlockedContentIds);
        Assert.Equal(legacy.Statistics.ScoredImpacts, first.Player.Statistics.ScoredImpacts);
        Assert.Equal(legacy.Times, first.Player.Times);
        Assert.Equal(legacy.Extensions?.UnknownSelectedToolId, first.Player.Extensions?.UnknownSelectedToolId);

        Assert.Equal(legacy.Mood, first.Buddy.Mood);
        Assert.Equal(legacy.Fullness, first.Buddy.Fullness);
        Assert.Equal(legacy.HarmfulContentIds, first.Buddy.HarmfulContentIds);
        Assert.Equal(legacy.Traits, first.Buddy.Traits);
        Assert.Equal(legacy.FunInterest, first.Buddy.FunInterest);

        LegacyProgressAggregateSnapshot recombined =
            LegacyProgressPartitionPolicy.RecombineForLegacy(first);
        Assert.Equal(characterId, recombined.ActiveCharacterId);
        AssertProgressEquivalent(legacy, recombined.Progress);
    }

    [Fact]
    public void Built_in_character_binding_remains_null_during_partition()
    {
        ProgressSnapshot legacy = RichLegacySnapshot();

        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(legacy, null);
        LegacyProgressAggregateSnapshot recombined =
            LegacyProgressPartitionPolicy.RecombineForLegacy(partition);

        Assert.Null(partition.Buddy.CharacterId);
        Assert.Null(recombined.ActiveCharacterId);
        AssertProgressEquivalent(legacy, recombined.Progress);
    }

    [Fact]
    public void Partition_defensively_copies_mutable_legacy_collections()
    {
        var unlocks = new List<string> { ContentIds.ToolGrab, ContentIds.ToolPistol };
        var harmful = new List<string> { ContentIds.ToolPistol };
        var uses = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [ContentIds.ToolPistol] = 7,
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["demo.character_slots.v1"] = "2",
        };
        var fun = new List<FunActivityInterest>
        {
            new(FunActivityId.Pet, 55.0f, false),
        };
        var legacy = new ProgressSnapshot(
            4,
            12_000,
            ContentIds.ToolPistol,
            unlocks,
            -20.0f,
            harmful,
            BuddyTraits.Default,
            new ProgressStatistics(1, 2, 3, 4, 5, ToolUses: uses),
            new CumulativeTimes(10, 8, 2),
            new ProgressExtensionData(Values: values),
            fun,
            40.0f);

        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(legacy, null);

        unlocks.Add(ContentIds.ToolShotgun);
        harmful.Clear();
        uses[ContentIds.ToolPistol] = 999;
        values["demo.character_slots.v1"] = "99";
        fun[0] = new FunActivityInterest(FunActivityId.Pet, 0.0f, true);

        Assert.Equal(new[] { ContentIds.ToolGrab, ContentIds.ToolPistol }, partition.Player.UnlockedContentIds);
        Assert.Equal(new[] { ContentIds.ToolPistol }, partition.Buddy.HarmfulContentIds);
        Assert.Equal(7, partition.Player.Statistics.ToolUses![ContentIds.ToolPistol]);
        Assert.Equal("2", partition.Player.Extensions!.Values!["demo.character_slots.v1"]);
        Assert.Equal(55.0f, partition.Buddy.FunInterest![0].Interest);
        Assert.False(partition.Buddy.FunInterest[0].Bored);
    }

    [Fact]
    public void Recombine_uses_newest_independent_revision_for_legacy_dirty_tracking()
    {
        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(RichLegacySnapshot(), null);
        partition = partition with
        {
            Player = partition.Player with { Revision = 20 },
            Buddy = partition.Buddy with { Revision = 27 },
        };

        LegacyProgressAggregateSnapshot recombined =
            LegacyProgressPartitionPolicy.RecombineForLegacy(partition);

        Assert.Equal(27, recombined.Progress.Revision);
    }

    [Fact]
    public void Empty_character_or_buddy_identity_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LegacyProgressPartitionPolicy.Split(RichLegacySnapshot(), Guid.Empty));
        Assert.Throws<ArgumentException>(() => BuddyIdentityId.From(Guid.Empty));

        LegacyProgressPartition valid = LegacyProgressPartitionPolicy.Split(RichLegacySnapshot(), null);
        var invalid = valid with
        {
            Buddy = valid.Buddy with { BuddyIdentityId = default },
        };
        Assert.Throws<ArgumentException>(() =>
            LegacyProgressPartitionPolicy.RecombineForLegacy(invalid));
    }

    private static ProgressSnapshot RichLegacySnapshot()
    {
        var toolUses = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [ContentIds.ToolPistol] = 12,
            [ContentIds.ToolPet] = 4,
        };
        var toolPain = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [ContentIds.ToolPistol] = 33_000,
        };
        var extensions = new ProgressExtensionData(
            "future.tool",
            new[] { "future.content" },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account.test"] = "kept",
            });
        var fun = new[]
        {
            new FunActivityInterest(FunActivityId.Catch, 81.0f, false),
            new FunActivityInterest(FunActivityId.Pet, 42.0f, false),
            new FunActivityInterest(FunActivityId.Tickle, 12.0f, true),
            new FunActivityInterest(FunActivityId.Treat, 66.0f, false),
        };

        return new ProgressSnapshot(
            Revision: 19,
            BalanceMilliCredits: 456_000,
            SelectedToolId: ContentIds.ToolPistol,
            UnlockedToolIds: new[] { ContentIds.ToolGrab, ContentIds.ToolPet, ContentIds.ToolPistol },
            Mood: -37.5f,
            HarmfulContentIds: new[] { ContentIds.ToolPistol, ContentIds.ToolGrenade },
            Traits: new BuddyTraits(73),
            Statistics: new ProgressStatistics(
                ScoredImpacts: 10,
                Knockouts: 2,
                CareAwards: 5,
                TrustResets: 1,
                EarnedMilliCredits: 123_000,
                SuccessfulCatches: 4,
                TotalPainMilli: 77_000,
                BestOneSecondMilliCredits: 3_000,
                BestThreeSecondMilliCredits: 7_000,
                BestTenSecondMilliCredits: 11_000,
                HighestMood: 88.0f,
                LowestMood: -91.0f,
                ToolUses: toolUses,
                ToolPainMilli: toolPain),
            Times: new CumulativeTimes(1_200, 900, 300),
            Extensions: extensions,
            FunInterest: fun,
            Fullness: 62.5f);
    }

    private static void AssertProgressEquivalent(
        in ProgressSnapshot expected,
        in ProgressSnapshot actual)
    {
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.BalanceMilliCredits, actual.BalanceMilliCredits);
        Assert.Equal(expected.SelectedToolId, actual.SelectedToolId);
        Assert.Equal(expected.UnlockedToolIds, actual.UnlockedToolIds);
        Assert.Equal(expected.Mood, actual.Mood);
        Assert.Equal(expected.Fullness, actual.Fullness);
        Assert.Equal(expected.HarmfulContentIds, actual.HarmfulContentIds);
        Assert.Equal(expected.Traits, actual.Traits);
        Assert.Equal(expected.Statistics.ScoredImpacts, actual.Statistics.ScoredImpacts);
        Assert.Equal(expected.Statistics.Knockouts, actual.Statistics.Knockouts);
        AssertDictionaryEquivalent(expected.Statistics.ToolUses, actual.Statistics.ToolUses);
        AssertDictionaryEquivalent(expected.Statistics.ToolPainMilli, actual.Statistics.ToolPainMilli);
        Assert.Equal(expected.Times, actual.Times);
        Assert.Equal(expected.Extensions?.UnknownSelectedToolId, actual.Extensions?.UnknownSelectedToolId);
        Assert.Equal(expected.Extensions?.UnknownContentIds, actual.Extensions?.UnknownContentIds);
        AssertDictionaryEquivalent(expected.Extensions?.Values, actual.Extensions?.Values);
        Assert.Equal(expected.FunInterest, actual.FunInterest);
    }

    private static void AssertDictionaryEquivalent<TValue>(
        IReadOnlyDictionary<string, TValue>? expected,
        IReadOnlyDictionary<string, TValue>? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected is null, actual is null);
            return;
        }

        Assert.Equal(expected.Count, actual.Count);
        foreach ((string key, TValue value) in expected)
        {
            Assert.True(actual.TryGetValue(key, out TValue actualValue));
            Assert.Equal(value, actualValue);
        }
    }
}
