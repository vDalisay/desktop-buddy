using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Achievements;

public sealed class AchievementBaselineTests
{
    private const double CashPerPain = 0.018;

    [Fact]
    public void Baseline_has_twenty_four_stable_unique_definitions()
    {
        Assert.Equal(24, AchievementCatalog.Baseline.Count);
        Assert.Empty(AchievementCatalog.Validate());
        Assert.Equal(5, AchievementCatalog.Baseline.Count(item => item.Hidden));
        Assert.Equal(
            AchievementCatalog.Baseline.Count,
            AchievementCatalog.Baseline.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            AchievementCatalog.Baseline.Count,
            AchievementCatalog.Baseline.Select(item => item.SteamApiName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Employee_ladder_uses_the_five_approved_lifetime_thresholds()
    {
        (PlayerProgressState player, _) = Split();
        var work = new WorkProgressState();
        var achievements = new AchievementCoordinator(player, work);

        work.Record(WorkActivityKind.KeyboardPress, 99);
        achievements.EvaluateAccountState();
        Assert.False(achievements.Store.IsQualified(AchievementIds.EmployeeDay));

        work.Record(WorkActivityKind.KeyboardPress, 1);
        achievements.EvaluateAccountState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeDay));
        Assert.False(achievements.Store.IsQualified(AchievementIds.EmployeeWeek));

        work.Record(WorkActivityKind.MouseClick, 900);
        achievements.EvaluateAccountState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeWeek));

        work.Record(WorkActivityKind.KeyboardPress, 9_000);
        achievements.EvaluateAccountState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeMonth));

        work.Record(WorkActivityKind.MouseClick, 90_000);
        achievements.EvaluateAccountState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeYear));

        work.Record(WorkActivityKind.KeyboardPress, 900_000);
        achievements.EvaluateAccountState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeForLife));
    }

    [Fact]
    public void Punching_bag_requires_one_hundred_accepted_glove_damage_events()
    {
        (PlayerProgressState player, BuddyIdentityState buddy) = Split();
        var achievements = new AchievementCoordinator(player);

        for (int hit = 0; hit < 99; hit++)
            achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, hit * 0.1, buddy);

        Assert.False(achievements.Store.IsQualified(AchievementIds.PunchingBag));
        achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 10.0, buddy);
        Assert.True(achievements.Store.IsQualified(AchievementIds.PunchingBag));
        Assert.True(achievements.Store.IsQualified(AchievementIds.FirstImpression));
    }

    [Fact]
    public void Rube_goldberg_requires_three_different_sources_inside_five_seconds()
    {
        (PlayerProgressState player, BuddyIdentityState buddy) = Split();
        var achievements = new AchievementCoordinator(player);
        achievements.RecordDamage(ContentIds.ToolPistol, 1.0f, 1, 10.0, buddy);
        achievements.RecordDamage(ContentIds.ToolBaseball, 1.0f, 1, 12.0, buddy);
        achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 15.0, buddy);
        Assert.True(achievements.Store.IsQualified(AchievementIds.RubeGoldberg));

        (PlayerProgressState latePlayer, BuddyIdentityState lateBuddy) = Split();
        var outsideWindow = new AchievementCoordinator(latePlayer);
        outsideWindow.RecordDamage(ContentIds.ToolPistol, 1.0f, 1, 10.0, lateBuddy);
        outsideWindow.RecordDamage(ContentIds.ToolBaseball, 1.0f, 1, 16.0, lateBuddy);
        outsideWindow.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 17.0, lateBuddy);
        Assert.False(outsideWindow.Store.IsQualified(AchievementIds.RubeGoldberg));
    }

    [Fact]
    public void Character_arc_is_independent_per_buddy_identity()
    {
        (PlayerProgressState player, BuddyIdentityState first) = Split();
        BuddyIdentitySnapshot secondSnapshot = first.Snapshot() with
        {
            BuddyIdentityId = BuddyIdentityId.From(Guid.Parse("22222222-2222-4222-8222-222222222222")),
            Revision = 0,
        };
        var second = new BuddyIdentityState(secondSnapshot);
        var achievements = new AchievementCoordinator(player);

        first.ApplyCareMood(-200.0f);
        achievements.EvaluatePersistentState(first);

        // Observing another Buddy must not erase the first Buddy's low endpoint.
        achievements.EvaluatePersistentState(second);
        first.ApplyCareMood(200.0f);
        achievements.EvaluatePersistentState(first);

        Assert.True(achievements.Store.IsQualified(AchievementIds.CharacterArc));
    }

    [Fact]
    public void Make_it_yours_requires_all_four_areas_for_the_same_buddy()
    {
        (PlayerProgressState player, BuddyIdentityState first) = Split();
        BuddyIdentityId second = BuddyIdentityId.From(Guid.Parse("33333333-3333-4333-8333-333333333333"));
        var achievements = new AchievementCoordinator(player);

        achievements.RecordCustomization(first.BuddyIdentityId, CustomizationArea.BuddyStudio);
        achievements.RecordCustomization(first.BuddyIdentityId, CustomizationArea.PaintBuddy);
        achievements.RecordCustomization(second, CustomizationArea.PaintBackground);
        achievements.RecordCustomization(second, CustomizationArea.EnvironmentDecorator);
        Assert.False(achievements.Store.IsQualified(AchievementIds.MakeItYours));

        achievements.RecordCustomization(first.BuddyIdentityId, CustomizationArea.PaintBackground);
        achievements.RecordCustomization(first.BuddyIdentityId, CustomizationArea.EnvironmentDecorator);
        Assert.True(achievements.Store.IsQualified(AchievementIds.MakeItYours));
    }

    [Fact]
    public void Home_sweet_home_uses_authoritative_decoration_categories()
    {
        (PlayerProgressState player, _) = Split();
        var achievements = new AchievementCoordinator(player);
        DecorationCategory[] categories = Enum.GetValues<DecorationCategory>();

        foreach (DecorationCategory category in categories[..^1])
            achievements.RecordEnvironmentCategory(category);
        Assert.False(achievements.Store.IsQualified(AchievementIds.HomeSweetHome));

        achievements.RecordEnvironmentCategory(categories[^1]);
        Assert.True(achievements.Store.IsQualified(AchievementIds.HomeSweetHome));
    }

    [Fact]
    public void Qualified_values_are_the_only_achievement_state_preserved_by_reset_policy()
    {
        (PlayerProgressState player, _) = Split();
        var store = new AchievementProgressStore(player);
        store.Qualify(AchievementIds.FirstImpression);
        store.IncrementCounter("boxing_glove_hits", 42);
        store.SetValue("working", "yes");

        IReadOnlyDictionary<string, string> kept =
            AchievementProgressStore.PreserveQualifiedAchievementValues(player.Extensions);

        Assert.Single(kept);
        Assert.Contains(kept, pair =>
            pair.Key.EndsWith(AchievementIds.FirstImpression, StringComparison.Ordinal) && pair.Value == "1");
    }

    [Fact]
    public void Reconciler_is_no_op_after_success_until_qualification_changes()
    {
        (PlayerProgressState player, _) = Split();
        var store = new AchievementProgressStore(player);
        var remote = new FakeAchievementRemote();
        var reconciler = new AchievementReconciler(store, remote);

        store.Qualify(AchievementIds.FirstImpression);
        Assert.True(reconciler.TrySynchronize());
        Assert.Equal(1, remote.SetCalls);
        Assert.Equal(1, remote.FlushCalls);

        Assert.True(reconciler.TrySynchronize());
        Assert.Equal(1, remote.SetCalls);
        Assert.Equal(1, remote.FlushCalls);

        store.Qualify(AchievementIds.LightsOut);
        Assert.True(reconciler.TrySynchronize());
        Assert.Equal(3, remote.SetCalls);
        Assert.Equal(2, remote.FlushCalls);
    }

    [Fact]
    public void Reconciler_replays_complete_desired_state_after_failed_flush()
    {
        (PlayerProgressState player, _) = Split();
        var store = new AchievementProgressStore(player);
        var remote = new FakeAchievementRemote();
        remote.FlushResults.Enqueue(false);
        remote.FlushResults.Enqueue(true);
        var reconciler = new AchievementReconciler(store, remote);

        store.Qualify(AchievementIds.FirstImpression);
        Assert.False(reconciler.TrySynchronize());
        Assert.True(reconciler.TrySynchronize());
        Assert.Equal(2, remote.SetCalls);
        Assert.Equal(2, remote.FlushCalls);
    }

    [Theory]
    [InlineData(-5.0f, 4.0f, true, false, true)]
    [InlineData(5.0f, -4.0f, false, true, true)]
    [InlineData(-5.0f, -4.0f, true, false, false)]
    [InlineData(float.NaN, 4.0f, true, false, false)]
    public void Bank_shot_wall_classifier_is_bounded_and_directional(
        float previousX,
        float currentX,
        bool nearLeft,
        bool nearRight,
        bool expected)
    {
        Assert.Equal(expected, AchievementPhysicsRules.IsSideWallRicochet(
            previousX, currentX, nearLeft, nearRight, minimumHorizontalSpeed: 1.0f));
    }

    private static (PlayerProgressState Player, BuddyIdentityState Buddy) Split()
    {
        var legacy = new BuddyProgressState(CashPerPain);
        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(
            legacy.Snapshot(),
            activeCharacterId: null);
        return (
            new PlayerProgressState(CashPerPain, partition.Player),
            new BuddyIdentityState(partition.Buddy));
    }

    private sealed class FakeAchievementRemote : IAchievementRemote
    {
        public bool IsAvailable { get; set; } = true;
        public int SetCalls { get; private set; }
        public int FlushCalls { get; private set; }
        public Queue<bool> FlushResults { get; } = new();

        public bool TrySetAchievement(string apiName)
        {
            SetCalls++;
            return !string.IsNullOrWhiteSpace(apiName);
        }

        public bool TryFlush()
        {
            FlushCalls++;
            return FlushResults.Count == 0 || FlushResults.Dequeue();
        }
    }
}
