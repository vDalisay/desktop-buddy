using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class BuddyRuntimeProgressBindingTests
{
    [Fact]
    public void Split_binding_preserves_legacy_semantics_for_runtime_mutations()
    {
        const double cashPerPain = 1.0;
        var seed = new BuddyProgressState(
            cashPerPain,
            initialMood: -12.0f,
            harmfulContentIds: [ContentIds.ToolGrenade],
            unlockedToolIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
            traits: new BuddyTraits(63),
            statistics: new ProgressStatistics(2, 1, 3, 0, 15_000),
            times: new CumulativeTimes(10, 8, 2),
            initialBalanceMilliCredits: 30_000,
            initialFullness: 40.0f);
        ProgressSnapshot baseline = seed.Snapshot();

        var legacyState = new BuddyProgressState(cashPerPain);
        legacyState.Adopt(baseline);
        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(baseline, activeCharacterId: null);
        var player = new PlayerProgressState(cashPerPain, partition.Player);
        var buddy = new BuddyIdentityState(partition.Buddy);

        var legacy = new BuddyRuntimeProgressBinding(legacyState);
        var split = new BuddyRuntimeProgressBinding(new BuddyProgressCoordinator(player, buddy));

        ApplyRuntimeSequence(legacy);
        ApplyRuntimeSequence(split);

        Assert.Equal(legacy.SelectedTool, split.SelectedTool);
        Assert.Equal(legacy.Mood, split.Mood, precision: 4);
        Assert.Equal(legacy.MoodBand, split.MoodBand);
        Assert.Equal(legacy.Fullness, split.Fullness, precision: 4);
        Assert.Equal(legacy.Appetite, split.Appetite, precision: 4);
        Assert.Equal(legacy.Traits, split.Traits);
        Assert.Equal(legacy.InterestIn(FunActivityId.Pet), split.InterestIn(FunActivityId.Pet), precision: 4);
        Assert.Equal(legacy.IsContentHarmful(ContentIds.ToolGrenade), split.IsContentHarmful(ContentIds.ToolGrenade));
        Assert.Equal(legacy.Statistics.Knockouts, split.Statistics.Knockouts);
        Assert.Equal(legacy.Statistics.CareAwards, split.Statistics.CareAwards);
        Assert.Equal(legacy.Statistics.SuccessfulCatches, split.Statistics.SuccessfulCatches);
        Assert.Equal(legacy.Statistics.ToolUses![ContentIds.ToolPistol], split.Statistics.ToolUses![ContentIds.ToolPistol]);
        Assert.Equal(legacy.Times, split.Times);
        Assert.Equal("value", split.Extensions!.Values!["runtime.test"]);
    }

    [Fact]
    public void Two_split_bindings_share_account_selection_but_not_buddy_mood_or_hunger()
    {
        var player = new PlayerProgressState(
            cashPerPain: 1.0,
            new PlayerProgressSnapshot(
                Revision: 0,
                BalanceMilliCredits: 0,
                SelectedToolId: ContentIds.ToolGrab,
                UnlockedContentIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
                Statistics: default,
                Times: default));
        BuddyIdentityState firstBuddy = CreateBuddy("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", fullness: 20.0f);
        BuddyIdentityState secondBuddy = CreateBuddy("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", fullness: 70.0f);
        var first = new BuddyRuntimeProgressBinding(new BuddyProgressCoordinator(player, firstBuddy));
        var second = new BuddyRuntimeProgressBinding(new BuddyProgressCoordinator(player, secondBuddy));

        Assert.True(first.SelectTool(ToolId.Pistol));
        first.ApplyCareMood(8.0f);
        first.FillHunger(25.0f);

        Assert.Equal(ToolId.Pistol, second.SelectedTool);
        Assert.Equal(8.0f, first.Mood);
        Assert.Equal(0.0f, second.Mood);
        Assert.Equal(45.0f, first.Fullness);
        Assert.Equal(70.0f, second.Fullness);
        Assert.Equal(1, player.Statistics.CareAwards);
    }

    [Fact]
    public void Binding_exposes_which_persistence_model_it_wraps_without_conflating_them()
    {
        var legacyState = new BuddyProgressState(cashPerPain: 1.0);
        var legacy = new BuddyRuntimeProgressBinding(legacyState);
        Assert.False(legacy.IsSplit);
        Assert.Same(legacyState, legacy.LegacyProgress);
        Assert.Null(legacy.SplitProgress);

        var player = new PlayerProgressState(
            1.0,
            new PlayerProgressSnapshot(
                0,
                0,
                ContentIds.ToolGrab,
                [ContentIds.ToolGrab],
                default,
                default));
        BuddyIdentityState buddy = CreateBuddy("cccccccc-cccc-cccc-cccc-cccccccccccc", fullness: 50.0f);
        var coordinator = new BuddyProgressCoordinator(player, buddy);
        var split = new BuddyRuntimeProgressBinding(coordinator);

        Assert.True(split.IsSplit);
        Assert.Null(split.LegacyProgress);
        Assert.Same(coordinator, split.SplitProgress);
        Assert.Same(player, split.PlayerProgress);
        Assert.Same(buddy, split.BuddyProgress);
    }

    private static void ApplyRuntimeSequence(BuddyRuntimeProgressBinding progress)
    {
        Assert.True(progress.SelectTool(ToolId.Pistol));
        progress.ApplyCareMood(4.0f);
        progress.FillHunger(15.0f);
        progress.DrainHunger(1.0, HungerActivity.Working);
        progress.EngageFun(FunActivityId.Pet);
        progress.RechargeFun(0.5);
        progress.RecordKnockout();
        progress.RecordSuccessfulCatch();
        progress.RecordContentUse(ContentIds.ToolPistol);
        progress.AccrueTime(5.0, 5.0, 0.0);
        progress.SetExtensionValue("runtime.test", "value");
        progress.DriftMood(0.25);
    }

    private static BuddyIdentityState CreateBuddy(string id, float fullness)
    {
        return new BuddyIdentityState(new BuddyIdentitySnapshot(
            BuddyIdentityId.From(Guid.Parse(id)),
            Revision: 0,
            CharacterId: null,
            Mood: 0.0f,
            Fullness: fullness,
            HarmfulContentIds: Array.Empty<string>(),
            Traits: BuddyTraits.Default,
            FunInterest: null));
    }
}
