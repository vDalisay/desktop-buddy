using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Economy;

public sealed class PurchaseTests
{
    private const long BaseballPrice = 3_000;

    [Fact]
    public void Purchase_SpendsAndPermanentlyUnlocksAtomically()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(5_000);
        var changes = new List<ProgressChange>();
        progress.Changed += changes.Add;
        long revisionBefore = progress.Revision;

        PurchaseResult result = progress.Purchase(ContentIds.ToolBaseball, BaseballPrice);

        Assert.Equal(PurchaseStatus.Purchased, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal(2_000, result.BalanceMilliCredits);
        Assert.Equal(2_000, progress.BalanceMilliCredits);
        Assert.True(progress.IsToolUnlocked(ContentIds.ToolBaseball));
        Assert.True(progress.Revision > revisionBefore);
        Assert.Equal([ProgressChange.ContentPurchased], changes);
    }

    [Fact]
    public void Purchase_InsufficientFundsDoesNotPartiallyMutate()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(2_999);
        long revisionBefore = progress.Revision;

        PurchaseResult result = progress.Purchase(ContentIds.ToolBaseball, BaseballPrice);

        Assert.Equal(PurchaseStatus.InsufficientFunds, result.Status);
        Assert.Equal(2_999, progress.BalanceMilliCredits);
        Assert.False(progress.IsToolUnlocked(ContentIds.ToolBaseball));
        Assert.Equal(revisionBefore, progress.Revision);
    }

    [Fact]
    public void Purchase_AlreadyOwnedCannotChargeTwice()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(10_000);

        Assert.True(progress.Purchase(ContentIds.ToolBaseball, BaseballPrice).Succeeded);
        long balanceAfterFirst = progress.BalanceMilliCredits;
        long revisionAfterFirst = progress.Revision;

        PurchaseResult repeat = progress.Purchase(ContentIds.ToolBaseball, BaseballPrice);

        Assert.Equal(PurchaseStatus.AlreadyOwned, repeat.Status);
        Assert.Equal(balanceAfterFirst, progress.BalanceMilliCredits);
        Assert.Equal(revisionAfterFirst, progress.Revision);
    }

    [Theory]
    [InlineData("tool.not_known", 3_000, PurchaseStatus.InvalidContentId)]
    [InlineData("tool.baseball", 0, PurchaseStatus.InvalidPrice)]
    [InlineData("tool.baseball", -1, PurchaseStatus.InvalidPrice)]
    [InlineData("tool.baseball", 1_500, PurchaseStatus.InvalidPrice)]
    public void Purchase_InvalidRequestDoesNotMutate(
        string contentId,
        long price,
        PurchaseStatus expected)
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(10_000);
        long revisionBefore = progress.Revision;

        PurchaseResult result = progress.Purchase(contentId, price);

        Assert.Equal(expected, result.Status);
        Assert.Equal(10_000, progress.BalanceMilliCredits);
        Assert.Equal(revisionBefore, progress.Revision);
        Assert.False(progress.IsToolUnlocked(ContentIds.ToolBaseball));
    }

    [Fact]
    public void LockedToolCannotBeSelectedUntilPurchaseSucceeds()
    {
        var progress = new BuddyProgressState(1.0);

        Assert.False(progress.SelectTool(ToolId.Baseball));
        Assert.Equal(ToolId.Grab, progress.SelectedTool);

        progress.Deposit(BaseballPrice);
        Assert.True(progress.Purchase(ContentIds.ToolBaseball, BaseballPrice).Succeeded);
        Assert.True(progress.SelectTool(ToolId.Baseball));
        Assert.Equal(ToolId.Baseball, progress.SelectedTool);
    }

    [Fact]
    public void PurchasedBaseballAndSelectionSurviveSaveRoundTrip()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(5_000);
        Assert.True(progress.Purchase(ContentIds.ToolBaseball, BaseballPrice).Succeeded);
        Assert.True(progress.SelectTool(ToolId.Baseball));

        ProgressSave save = ProgressSave.FromSnapshot(progress.Snapshot());
        ProgressSave decoded = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(save)).Save!;
        BuddyProgressState restored = ProgressSavePolicy.CreateState(decoded, 1.0);

        Assert.Equal(2_000, restored.BalanceMilliCredits);
        Assert.True(restored.IsToolUnlocked(ContentIds.ToolBaseball));
        Assert.Equal(ToolId.Baseball, restored.SelectedTool);
    }
}
