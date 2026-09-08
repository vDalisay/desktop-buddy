using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SplitProgressStateTests
{
    [Fact]
    public void Two_buddies_keep_identity_state_independent_while_sharing_one_account_wallet()
    {
        var player = new PlayerProgressState(
            cashPerPain: 1.0,
            new PlayerProgressSnapshot(
                Revision: 0,
                BalanceMilliCredits: 10_000,
                SelectedToolId: ContentIds.ToolPistol,
                UnlockedContentIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
                Statistics: default,
                Times: default));
        var buddyA = new BuddyIdentityState(new BuddyIdentitySnapshot(
            BuddyIdentityId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            Revision: 0,
            CharacterId: null,
            Mood: 0.0f,
            Fullness: 20.0f,
            HarmfulContentIds: [],
            Traits: BuddyTraits.Default));
        var buddyB = new BuddyIdentityState(new BuddyIdentitySnapshot(
            BuddyIdentityId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            Revision: 0,
            CharacterId: null,
            Mood: 30.0f,
            Fullness: 70.0f,
            HarmfulContentIds: [],
            Traits: BuddyTraits.Default));

        long balanceBefore = player.BalanceMilliCredits;
        long reward = player.AcceptDamageReward(
            ContentIds.ToolPistol,
            pain: 10.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 0.0);
        bool trustReset = buddyA.ApplyImpactMood(
            ContentIds.ToolPistol,
            pain: 10.0f,
            ImpactMoodEffect.Harm);
        player.ObserveBuddyMood(buddyA.Mood);

        Assert.False(trustReset);
        Assert.Equal(-1.0f, buddyA.Mood);
        Assert.True(buddyA.IsContentHarmful(ContentIds.ToolPistol));
        Assert.Equal(20.0f, buddyA.Fullness);

        Assert.Equal(30.0f, buddyB.Mood);
        Assert.False(buddyB.IsContentHarmful(ContentIds.ToolPistol));
        Assert.Equal(70.0f, buddyB.Fullness);
        Assert.Equal(0, buddyB.Revision);

        Assert.Equal(balanceBefore + reward, player.BalanceMilliCredits);
        Assert.Equal(1, player.Statistics.ScoredImpacts);
        Assert.Equal(10_000, player.Statistics.TotalPainMilli);
    }

    [Fact]
    public void Legacy_partition_constructs_new_state_owners_without_losing_semantics()
    {
        Guid characterId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var legacy = new ProgressSnapshot(
            Revision: 12,
            BalanceMilliCredits: 250_000,
            SelectedToolId: ContentIds.ToolPistol,
            UnlockedToolIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
            Mood: -25.0f,
            HarmfulContentIds: [ContentIds.ToolPistol],
            Traits: new BuddyTraits(73),
            Statistics: new ProgressStatistics(ScoredImpacts: 4, Knockouts: 1, CareAwards: 2, TrustResets: 0, EarnedMilliCredits: 80_000),
            Times: new CumulativeTimes(100, 80, 20),
            Extensions: null,
            FunInterest: [new FunActivityInterest(FunActivityId.Pet, 55.0f, false)],
            Fullness: 62.5f);
        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(legacy, characterId);

        var player = new PlayerProgressState(1.0, partition.Player);
        var buddy = new BuddyIdentityState(partition.Buddy);
        var reconstructed = new LegacyProgressPartition(player.Snapshot(), buddy.Snapshot());
        LegacyProgressAggregateSnapshot aggregate =
            LegacyProgressPartitionPolicy.RecombineForLegacy(reconstructed);

        Assert.Equal(BuddyIdentityId.LegacyPrimary, buddy.BuddyIdentityId);
        Assert.Equal(characterId, buddy.CharacterId);
        Assert.Equal(legacy.BalanceMilliCredits, aggregate.Progress.BalanceMilliCredits);
        Assert.Equal(legacy.SelectedToolId, aggregate.Progress.SelectedToolId);
        Assert.Equal(legacy.Mood, aggregate.Progress.Mood);
        Assert.Equal(legacy.Fullness, aggregate.Progress.Fullness);
        Assert.Equal(legacy.Traits, aggregate.Progress.Traits);
        Assert.Equal(characterId, aggregate.ActiveCharacterId);
        Assert.Equal(legacy.Statistics.ScoredImpacts, aggregate.Progress.Statistics.ScoredImpacts);
        Assert.Equal(legacy.Times, aggregate.Progress.Times);
    }

    [Fact]
    public void Care_and_hunger_mutation_on_one_identity_never_touch_another_identity_or_account_balance()
    {
        var player = new PlayerProgressState(
            1.0,
            new PlayerProgressSnapshot(
                0,
                50_000,
                ContentIds.ToolGrab,
                [ContentIds.ToolGrab],
                default,
                default));
        var first = new BuddyIdentityState(new BuddyIdentitySnapshot(
            BuddyIdentityId.From(Guid.Parse("10000000-0000-0000-0000-000000000001")),
            0,
            null,
            10.0f,
            10.0f,
            [],
            BuddyTraits.Default));
        var second = new BuddyIdentityState(new BuddyIdentitySnapshot(
            BuddyIdentityId.From(Guid.Parse("20000000-0000-0000-0000-000000000002")),
            0,
            null,
            -10.0f,
            80.0f,
            [],
            BuddyTraits.Default));

        first.ApplyCareMood(5.0f);
        first.FillHunger(15.0f);

        Assert.Equal(15.0f, first.Mood);
        Assert.Equal(25.0f, first.Fullness);
        Assert.Equal(-10.0f, second.Mood);
        Assert.Equal(80.0f, second.Fullness);
        Assert.Equal(50_000, player.BalanceMilliCredits);
        Assert.Equal(0, player.Revision);
    }

    [Fact]
    public void Account_unlock_and_selection_are_single_shared_state_not_buddy_inventory()
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

        Assert.True(player.Unlock(ContentIds.ToolPistol));
        Assert.True(player.SelectTool(DesktopBuddy.Domain.Tools.ToolId.Pistol));
        Assert.True(player.IsUnlocked(ContentIds.ToolPistol));
        Assert.Equal(ContentIds.ToolPistol, player.SelectedToolId);
        Assert.False(player.Unlock(ContentIds.ToolPistol));
    }
}
