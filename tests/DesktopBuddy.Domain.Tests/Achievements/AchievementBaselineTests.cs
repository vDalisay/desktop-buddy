using System;
using System.Linq;
using DesktopBuddy.Achievements;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Content;
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
        achievements.EvaluatePersistentState();
        Assert.False(achievements.Store.IsQualified(AchievementIds.EmployeeDay));

        work.Record(WorkActivityKind.KeyboardPress, 1);
        achievements.EvaluatePersistentState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeDay));
        Assert.False(achievements.Store.IsQualified(AchievementIds.EmployeeWeek));

        work.Record(WorkActivityKind.MouseClick, 900);
        achievements.EvaluatePersistentState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeWeek));

        work.Record(WorkActivityKind.KeyboardPress, 9_000);
        achievements.EvaluatePersistentState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeMonth));

        work.Record(WorkActivityKind.MouseClick, 90_000);
        achievements.EvaluatePersistentState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeYear));

        work.Record(WorkActivityKind.KeyboardPress, 900_000);
        achievements.EvaluatePersistentState();
        Assert.True(achievements.Store.IsQualified(AchievementIds.EmployeeForLife));
    }

    [Fact]
    public void PunchingBag_RequiresOneHundredAcceptedGloveDamageEvents()
    {
        var achievements = new AchievementCoordinator(new BuddyProgressState(CashPerPain));

        for (int hit = 0; hit < 99; hit++)
            achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, hit * 0.1);

        Assert.False(achievements.Store.IsQualified(AchievementIds.PunchingBag));
        achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 10.0);
        Assert.True(achievements.Store.IsQualified(AchievementIds.PunchingBag));
        Assert.True(achievements.Store.IsQualified(AchievementIds.FirstImpression));
    }

    [Fact]
    public void RubeGoldberg_RequiresThreeDifferentDamageSourcesInsideFiveSeconds()
    {
        var achievements = new AchievementCoordinator(new BuddyProgressState(CashPerPain));

        achievements.RecordDamage(ContentIds.ToolPistol, 1.0f, 1, 10.0);
        achievements.RecordDamage(ContentIds.ToolBaseball, 1.0f, 1, 12.0);
        achievements.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 15.0);
        Assert.True(achievements.Store.IsQualified(AchievementIds.RubeGoldberg));

        var outsideWindow = new AchievementCoordinator(new BuddyProgressState(CashPerPain));
        outsideWindow.RecordDamage(ContentIds.ToolPistol, 1.0f, 1, 10.0);
        outsideWindow.RecordDamage(ContentIds.ToolBaseball, 1.0f, 1, 16.0);
        outsideWindow.RecordDamage(ContentIds.ToolBoxingGlove, 1.0f, 1, 17.0);
        Assert.False(outsideWindow.Store.IsQualified(AchievementIds.RubeGoldberg));
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
    public void HomeSweetHome_RemembersCategoriesAcrossRoomEdits()
    {
        var achievements = new AchievementCoordinator(new BuddyProgressState(CashPerPain));
        string[] required = ["Lamp", "Sofa", "Painting", "Wallpaper", "Plant", "Table"];

        foreach (string category in required[..^1])
            achievements.RecordEnvironmentCategory(category);
        achievements.EvaluateHomeSweetHome(required);
        Assert.False(achievements.Store.IsQualified(AchievementIds.HomeSweetHome));

        achievements.RecordEnvironmentCategory(required[^1]);
        achievements.EvaluateHomeSweetHome(required);
        Assert.True(achievements.Store.IsQualified(AchievementIds.HomeSweetHome));
    }
}
