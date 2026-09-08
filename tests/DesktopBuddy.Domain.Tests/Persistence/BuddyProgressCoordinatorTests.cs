using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class BuddyProgressCoordinatorTests
{
    [Fact]
    public void Split_damage_routing_matches_legacy_one_buddy_semantics()
    {
        var legacy = new BuddyProgressState(
            cashPerPain: 1.0,
            initialMood: 10.0f,
            unlockedToolIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
            selectedToolId: ContentIds.ToolPistol);
        LegacyProgressPartition partition =
            LegacyProgressPartitionPolicy.Split(legacy.Snapshot(), activeCharacterId: null);
        var player = new PlayerProgressState(1.0, partition.Player);
        var buddy = new BuddyIdentityState(partition.Buddy);
        var split = new BuddyProgressCoordinator(player, buddy);

        long legacyReward = legacy.AcceptDamage(
            ContentIds.ToolPistol,
            pain: 25.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 0.0,
            ImpactMoodEffect.Harm);
        SplitDamageProgressResult splitResult = split.AcceptDamage(
            ContentIds.ToolPistol,
            pain: 25.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 0.0,
            ImpactMoodEffect.Harm);

        Assert.Equal(legacyReward, splitResult.MilliCredits);
        Assert.False(splitResult.TrustReset);
        Assert.Equal(legacy.BalanceMilliCredits, player.BalanceMilliCredits);
        Assert.Equal(legacy.Mood, buddy.Mood);
        Assert.Equal(legacy.IsContentHarmful(ContentIds.ToolPistol), buddy.IsContentHarmful(ContentIds.ToolPistol));
        Assert.Equal(legacy.Statistics.ScoredImpacts, player.Statistics.ScoredImpacts);
        Assert.Equal(legacy.Statistics.EarnedMilliCredits, player.Statistics.EarnedMilliCredits);
        Assert.Equal(legacy.Statistics.TotalPainMilli, player.Statistics.TotalPainMilli);
        Assert.Equal(
            legacy.Statistics.ToolPainMilli![ContentIds.ToolPistol],
            player.Statistics.ToolPainMilli![ContentIds.ToolPistol]);
    }

    [Fact]
    public void Split_care_routing_matches_legacy_trust_reset_and_care_statistics()
    {
        var legacy = new BuddyProgressState(
            cashPerPain: 1.0,
            initialMood: 59.0f,
            harmfulContentIds: [ContentIds.ToolPistol],
            unlockedToolIds: [ContentIds.ToolGrab]);
        LegacyProgressPartition partition =
            LegacyProgressPartitionPolicy.Split(legacy.Snapshot(), activeCharacterId: null);
        var player = new PlayerProgressState(1.0, partition.Player);
        var buddy = new BuddyIdentityState(partition.Buddy);
        var split = new BuddyProgressCoordinator(player, buddy);

        bool legacyReset = legacy.ApplyCareMood(2.0f);
        bool splitReset = split.ApplyCareMood(2.0f);

        Assert.True(legacyReset);
        Assert.Equal(legacyReset, splitReset);
        Assert.Equal(legacy.Mood, buddy.Mood);
        Assert.Empty(legacy.HarmfulContentIds);
        Assert.Empty(buddy.HarmfulContentIds);
        Assert.Equal(legacy.Statistics.CareAwards, player.Statistics.CareAwards);
        Assert.Equal(legacy.Statistics.TrustResets, player.Statistics.TrustResets);
        Assert.Equal(legacy.Statistics.HighestMood, player.Statistics.HighestMood);
    }

    [Fact]
    public void Knockout_and_catch_statistics_remain_account_global()
    {
        var player = new PlayerProgressState(
            1.0,
            new PlayerProgressSnapshot(
                0,
                0,
                ContentIds.ToolGrab,
                [ContentIds.ToolGrab],
                default,
                default));
        var buddy = new BuddyIdentityState(new BuddyIdentitySnapshot(
            BuddyIdentityId.LegacyPrimary,
            0,
            null,
            0.0f,
            0.0f,
            [],
            DesktopBuddy.Domain.Autonomy.BuddyTraits.Default));
        var coordinator = new BuddyProgressCoordinator(player, buddy);

        coordinator.RecordKnockout();
        coordinator.RecordSuccessfulCatch();

        Assert.Equal(1, player.Statistics.Knockouts);
        Assert.Equal(1, player.Statistics.SuccessfulCatches);
        Assert.Equal(0, buddy.Revision);
    }
}
