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

    [Theory]
    [InlineData(-60.0f, 45.0f, true, false, true)]
    [InlineData(60.0f, -45.0f, false, true, true)]
    [InlineData(-60.0f, -30.0f, true, false, false)]
    [InlineData(60.0f, 30.0f, false, true, false)]
    [InlineData(-60.0f, 45.0f, false, false, false)]
    [InlineData(-4.0f, 4.0f, true, false, false)]
    public void BankShotRicochet_RequiresRealDirectionReversalAtASideWall(
        float previousVelocityX,
        float currentVelocityX,
        bool nearLeftWall,
        bool nearRightWall,
        bool expected)
    {
        bool result = AchievementPhysicsRules.IsSideWallRicochet(
            previousVelocityX,
            currentVelocityX,
            nearLeftWall,
            nearRightWall,
            minimumHorizontalSpeed: 8.0f);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BankShotRicochet_RejectsNonFiniteObservations()
    {
        Assert.False(AchievementPhysicsRules.IsSideWallRicochet(
            float.NaN,
            20.0f,
            nearLeftWall: true,
            nearRightWall: false,
            minimumHorizontalSpeed: 8.0f));
    }
}
