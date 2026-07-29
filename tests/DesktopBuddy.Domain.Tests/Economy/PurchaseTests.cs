using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tests.Content;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Economy;

public sealed class PurchaseTests
{
    private const long BaseballPrice = TestCatalogues.BaseballPrice;

    private static readonly ToolCatalogue Catalogue = TestCatalogues.Standard();

    [Fact]
    public void Purchase_SpendsAndPermanentlyUnlocksAtomically()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(5_000);
        var changes = new List<ProgressChange>();
        progress.Changed += changes.Add;
        long revisionBefore = progress.Revision;

        PurchaseResult result = progress.Purchase(ContentIds.ToolBaseball, Catalogue);

        Assert.Equal(PurchaseStatus.Purchased, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal(2_000, result.BalanceMilliCredits);
        Assert.Equal(2_000, progress.BalanceMilliCredits);
        Assert.True(progress.IsToolUnlocked(ContentIds.ToolBaseball));
        Assert.True(progress.Revision > revisionBefore);
        Assert.Equal([ProgressChange.ContentPurchased], changes);
    }

    [Fact]
    public void Purchase_ChargesTheCatalogPriceAndNothingTheCallerAsksFor()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(50_000);

        PurchaseResult result = progress.Purchase(ContentIds.ToolBaseball, Catalogue);

        // The only price in play is the catalogue's: there is no parameter to override it.
        Assert.Equal(BaseballPrice, result.PriceMilliCredits);
        Assert.Equal(50_000 - BaseballPrice, progress.BalanceMilliCredits);
    }

    [Fact]
    public void Purchase_InsufficientFundsDoesNotPartiallyMutate()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(2_999);
        long revisionBefore = progress.Revision;

        PurchaseResult result = progress.Purchase(ContentIds.ToolBaseball, Catalogue);

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

        Assert.True(progress.Purchase(ContentIds.ToolBaseball, Catalogue).Succeeded);
        long balanceAfterFirst = progress.BalanceMilliCredits;
        long revisionAfterFirst = progress.Revision;

        PurchaseResult repeat = progress.Purchase(ContentIds.ToolBaseball, Catalogue);

        Assert.Equal(PurchaseStatus.AlreadyOwned, repeat.Status);
        Assert.Equal(balanceAfterFirst, progress.BalanceMilliCredits);
        Assert.Equal(revisionAfterFirst, progress.Revision);
    }

    [Theory]
    // Not in this build's catalogue at all.
    [InlineData("tool.not_known", PurchaseStatus.InvalidContentId)]
    [InlineData("care.lab_food", PurchaseStatus.InvalidContentId)]
    // Owned from the first save and never sold (FR-013.1).
    [InlineData("tool.grab", PurchaseStatus.NotPurchasable)]
    [InlineData("tool.boxing_glove", PurchaseStatus.NotPurchasable)]
    // Unfinished slices are invisible, and an invisible entry cannot be bought.
    [InlineData("tool.meal", PurchaseStatus.NotAvailable)]
    [InlineData("upgrade.strength", PurchaseStatus.NotAvailable)]
    public void Purchase_IneligibleRequestDoesNotMutate(string contentId, PurchaseStatus expected)
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(100_000);
        long revisionBefore = progress.Revision;

        PurchaseResult result = progress.Purchase(contentId, Catalogue);

        Assert.Equal(expected, result.Status);
        Assert.Equal(100_000, progress.BalanceMilliCredits);
        Assert.Equal(revisionBefore, progress.Revision);
        Assert.False(progress.IsToolUnlocked(ContentIds.ToolMeal));
        Assert.False(progress.IsToolUnlocked(ContentIds.UpgradeStrength));
    }

    [Fact]
    public void LockedToolCannotBeSelectedUntilPurchaseSucceeds()
    {
        var progress = new BuddyProgressState(1.0);

        Assert.False(progress.SelectTool(ToolId.Baseball));
        Assert.Equal(ToolId.Grab, progress.SelectedTool);

        progress.Deposit(BaseballPrice);
        Assert.True(progress.Purchase(ContentIds.ToolBaseball, Catalogue).Succeeded);
        Assert.True(progress.SelectTool(ToolId.Baseball));
        Assert.Equal(ToolId.Baseball, progress.SelectedTool);
    }

    [Theory]
    [InlineData(ToolId.Baseball)]
    [InlineData(ToolId.Meal)]
    [InlineData(ToolId.BaseballBat)]
    [InlineData(ToolId.Pistol)]
    [InlineData(ToolId.Grenade)]
    [InlineData(ToolId.FireSprayer)]
    [InlineData(ToolId.SoccerBall)]
    [InlineData(ToolId.Drink)]
    [InlineData(ToolId.Shotgun)]
    [InlineData(ToolId.RepairKit)]
    public void EveryUnownedToolIsRejectedAtTheSelectionBoundary(ToolId tool)
    {
        // One rule for the whole catalogue: selection asks about ownership, not about which
        // tool it is, so a new slice cannot forget to lock itself.
        var progress = new BuddyProgressState(1.0);

        Assert.False(progress.SelectTool(tool));
        Assert.Equal(ToolId.Grab, progress.SelectedTool);

        Assert.True(progress.Unlock(ContentIds.ForTool(tool)));
        Assert.True(progress.SelectTool(tool));
        Assert.Equal(tool, progress.SelectedTool);
    }

    [Fact]
    public void NewSaveOwnsExactlyTheFourStartingTools()
    {
        var progress = new BuddyProgressState(1.0);

        Assert.Equal(0, progress.BalanceMilliCredits);
        Assert.Equal(ToolId.Grab, progress.SelectedTool);
        foreach (string id in CataloguePolicy.NewSaveUnlockedContentIds)
            Assert.True(progress.IsToolUnlocked(id), id);

        foreach (string id in CataloguePolicy.LaunchContentIds)
        {
            if (!CataloguePolicy.NewSaveUnlockedContentIds.Contains(id))
                Assert.False(progress.IsToolUnlocked(id), id);
        }
    }

    [Fact]
    public void PurchasedBaseballAndSelectionSurviveSaveRoundTrip()
    {
        var progress = new BuddyProgressState(1.0);
        progress.Deposit(5_000);
        Assert.True(progress.Purchase(ContentIds.ToolBaseball, Catalogue).Succeeded);
        Assert.True(progress.SelectTool(ToolId.Baseball));

        ProgressSave save = ProgressSave.FromSnapshot(progress.Snapshot());
        ProgressSave decoded = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(save)).Save!;
        BuddyProgressState restored = ProgressSavePolicy.CreateState(decoded, 1.0);

        Assert.Equal(2_000, restored.BalanceMilliCredits);
        Assert.True(restored.IsToolUnlocked(ContentIds.ToolBaseball));
        Assert.Equal(ToolId.Baseball, restored.SelectedTool);
    }

    [Fact]
    public void OwnershipOfAFutureCatalogueEntryIsRetainedNotDiscarded()
    {
        // A save written by a newer build may own content this build has never heard of.
        // This build cannot activate it, but it must hand the ID back out unchanged.
        var progress = new BuddyProgressState(1.0);
        Assert.True(progress.Unlock("tool.from_a_later_build"));

        BuddyProgressState restored = RoundTrip(progress);

        Assert.False(restored.IsToolUnlocked("tool.from_a_later_build"));
        Assert.Contains("tool.from_a_later_build", restored.Extensions!.UnknownContentIds!);
        Assert.Equal(ToolId.Grab, restored.SelectedTool);

        ProgressSave rewritten = ProgressSave.FromSnapshot(restored.Snapshot());
        Assert.Contains("tool.from_a_later_build", rewritten.Extensions!.UnknownContentIds!);
    }

    [Fact]
    public void OwningThePassiveUpgradeSurvivesSaveRoundTrip()
    {
        // FR-019 content is owned like any other purchase even though it is never a tool;
        // filtering unlocks down to selectable tools would silently refund it.
        var progress = new BuddyProgressState(1.0);
        Assert.True(progress.Unlock(ContentIds.UpgradeStrength));

        BuddyProgressState restored = RoundTrip(progress);

        Assert.True(restored.IsToolUnlocked(ContentIds.UpgradeStrength));
        Assert.Equal(ToolId.Grab, restored.SelectedTool);
    }

    private static BuddyProgressState RoundTrip(BuddyProgressState progress)
    {
        ProgressSave save = ProgressSave.FromSnapshot(progress.Snapshot());
        ProgressSave decoded = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(save)).Save!;
        return ProgressSavePolicy.CreateState(decoded, 1.0);
    }
}
