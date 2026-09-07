using System;
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
    public void Baseline_HasTwentyFourStableUniqueDefinitions()
    {
        Assert.Equal(24, AchievementCatalog.Baseline.Count);
        Assert.Empty(AchievementCatalog.Validate());
        Assert.Equal(
            AchievementCatalog.Baseline.Count,
            AchievementCatalog.Baseline.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            AchievementCatalog.Baseline.Count,
            AchievementCatalog.Baseline.Select(item => item.SteamApiName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EmployeeLadder_UsesTheFiveApprovedLifetimeThresholds()
    {
        var progress = new BuddyProgressState(CashPerPain);
        var work = new WorkProgressState();
        var achievements = new AchievementCoordinator(progress, work);

        work.Record(WorkActivityKind.KeyboardPress, 99);
        achievements.EvaluatePersistentState(null);
        Assert.False(achievements.Store.IsQualified(AchievementIds.EmployeeDay));

        work.Record(WorkActivityKind.KeyboardPress, 1);
        achievements.EvaluatePersistentState(null);
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeDay));
        Assert.False(achievements.Store.IsQualified(AchievementIds.EmployeeWeek));

        work.Record(WorkActivityKind.MouseClick, 900);
        achievements.EvaluatePersistentState(null);
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeWeek));

        work.Record(WorkActivityKind.KeyboardPress, 9_000);
        achievements.EvaluatePersistentState(null);
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeMonth));

        work.Record(WorkActivityKind.MouseClick, 90_000);
        achievements.EvaluatePersistentState(null);
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeYear));

        work.Record(WorkActivityKind.KeyboardPress, 900_000);
        achievements.EvaluatePersistentState(null);
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeForLife));
    }

    [Fact]
    public void PunchingBag_RequiresOneHundredAcceptedGloveDamageEvents()
    {
        var achievements = new AchievementCoordinator(new BuddyProgressState(CashPerPain));

        for (int hit = 0; hit < 99; hit++)
            achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, hit * 0.1, null);

        Assert.False(achievements.Store.IsQualified(AchievementIds.PunchingBag));
        achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 10.0, null);
        Assert.True(achievements.Store.IsQualified(AchievementIds.PunchingBag));
        Assert.True(achievements.Store.IsQualified(AchievementIds.FirstImpression));
    }

    [Fact]
    public void RubeGoldberg_RequiresThreeDifferentDamageSourcesInsideFiveSeconds()
    {
        var achievements = new AchievementCoordinator(new BuddyProgressState(CashPerPain));

        achievements.RecordDamage(ContentIds.ToolPistol, 1.0f, 1, 10.0, null);
        achievements.RecordDamage(ContentIds.ToolBaseball, 1.0f, 1, 12.0, null);
        achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 15.0, null);
        Assert.True(achievements.Store.IsQualified(AchievementIds.RubeGoldberg));

        var outsideWindow = new AchievementCoordinator(new BuddyProgressState(CashPerPain));
        outsideWindow.RecordDamage(ContentIds.ToolPistol, 1.0f, 1, 10.0, null);
        outsideWindow.RecordDamage(ContentIds.ToolBaseball, 1.0f, 1, 16.0, null);
        outsideWindow.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 17.0, null);
        Assert.False(outsideWindow.Store.IsQualified(AchievementIds.RubeGoldberg));
    }

    [Fact]
    public void DamageEvaluation_PreservesCharacterArcContext()
    {
        Guid character = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var progress = new BuddyProgressState(CashPerPain);
        var achievements = new AchievementCoordinator(progress);

        progress.ApplyCareMood(-200.0f);
        achievements.EvaluatePersistentState(character);
        achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 1.0, character);
        progress.ApplyCareMood(200.0f);
        achievements.EvaluatePersistentState(character);

        Assert.True(achievements.Store.IsQualified(AchievementIds.CharacterArc));
    }

    [Fact]
    public void CharacterArc_RequiresOneContinuousActiveCharacter()
    {
        Guid first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        Guid second = Guid.Parse("22222222-2222-4222-8222-222222222222");

        var switchedProgress = new BuddyProgressState(CashPerPain);
        var switched = new AchievementCoordinator(switchedProgress);
        switchedProgress.ApplyCareMood(-200.0f);
        switched.EvaluatePersistentState(first);
        switched.EvaluatePersistentState(second);
        switchedProgress.ApplyCareMood(200.0f);
        switched.EvaluatePersistentState(first);
        Assert.False(switched.Store.IsQualified(AchievementIds.CharacterArc));

        var continuousProgress = new BuddyProgressState(CashPerPain);
        var continuous = new AchievementCoordinator(continuousProgress);
        continuousProgress.ApplyCareMood(-200.0f);
        continuous.EvaluatePersistentState(first);
        continuousProgress.ApplyCareMood(200.0f);
        continuous.EvaluatePersistentState(first);
        Assert.True(continuous.Store.IsQualified(AchievementIds.CharacterArc));
    }

    [Fact]
    public void MakeItYours_RequiresAllFourAreasForTheSameCharacter()
    {
        var achievements = new AchievementCoordinator(new BuddyProgressState(CashPerPain));
        Guid first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        Guid second = Guid.Parse("22222222-2222-4222-8222-222222222222");

        achievements.RecordCustomization(first, CustomizationArea.BuddyStudio);
        achievements.RecordCustomization(first, CustomizationArea.PaintBuddy);
        achievements.RecordCustomization(second, CustomizationArea.PaintBackground);
        achievements.RecordCustomization(second, CustomizationArea.EnvironmentDecorator);
        Assert.False(achievements.Store.IsQualified(AchievementIds.MakeItYours));

        achievements.RecordCustomization(first, CustomizationArea.PaintBackground);
        achievements.RecordCustomization(first, CustomizationArea.EnvironmentDecorator);
        Assert.True(achievements.Store.IsQualified(AchievementIds.MakeItYours));
    }

    [Fact]
    public void HomeSweetHome_UsesAuthoritativeDecorationCategories()
    {
        var achievements = new AchievementCoordinator(new BuddyProgressState(CashPerPain));
        DecorationCategory[] categories = Enum.GetValues<DecorationCategory>();

        foreach (DecorationCategory category in categories[..^1])
            achievements.RecordEnvironmentCategory(category);
        Assert.False(achievements.Store.IsQualified(AchievementIds.HomeSweetHome));

        achievements.RecordEnvironmentCategory(categories[^1]);
        Assert.True(achievements.Store.IsQualified(AchievementIds.HomeSweetHome));
    }
}
