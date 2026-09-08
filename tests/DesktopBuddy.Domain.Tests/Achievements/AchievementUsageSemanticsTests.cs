using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Achievements;

public sealed class AchievementUsageSemanticsTests
{
    [Fact]
    public void VarietyHour_RequiresUseButNotSuccessfulDamage()
    {
        var progress = new BuddyProgressState(cashPerPain: 0.018);
        var achievements = new AchievementCoordinator(progress);

        foreach (string contentId in CataloguePolicy.LaunchContentIds)
            progress.RecordContentUse(contentId);

        achievements.EvaluatePersistentState(activeCharacterId: null);

        Assert.True(achievements.Store.IsQualified(AchievementIds.VarietyHour));
        Assert.False(achievements.Store.IsQualified(AchievementIds.TryEverythingOnce));
        Assert.False(achievements.Store.IsQualified(AchievementIds.FirstImpression));
        Assert.False(achievements.Store.IsQualified(AchievementIds.PunchingBag));
    }
}
