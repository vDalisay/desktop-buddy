using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Content;

public sealed class CataloguePolicyTests
{
    [Fact]
    public void TheUpgradeIsNeverSelectableOwnedOrNot()
    {
        // FR-019: the Strength Upgrade is shop content, never a tool. Ownership is not part
        // of this rule — there is no state in which it appears in the tool grid.
        ToolCatalogue catalogue = TestCatalogues.AllVisible();

        Assert.DoesNotContain(
            CataloguePolicy.SelectableEntries(catalogue),
            entry => entry.ContentId == ContentIds.UpgradeStrength);
        Assert.False(CataloguePolicy.IsSelectable(catalogue, ContentIds.UpgradeStrength));
        Assert.False(ContentIds.IsTool(ContentIds.UpgradeStrength));
        Assert.False(ContentIds.TryParseTool(ContentIds.UpgradeStrength, out _));
    }

    [Fact]
    public void TheUpgradeIsOfferedInTheShop()
    {
        ToolCatalogue catalogue = TestCatalogues.AllVisible();

        Assert.Contains(
            CataloguePolicy.ShopEntries(catalogue),
            entry => entry.ContentId == ContentIds.UpgradeStrength);
    }

    [Fact]
    public void UnfinishedEntriesAreAbsentFromBothTheShopAndTheToolGrid()
    {
        // The owner's "no unfinished shop entry is shown" rule (2026-07-28).
        ToolCatalogue catalogue = TestCatalogues.Standard();

        Assert.DoesNotContain(
            CataloguePolicy.ShopEntries(catalogue),
            entry => entry.ContentId == ContentIds.ToolMeal);
        Assert.DoesNotContain(
            CataloguePolicy.SelectableEntries(catalogue),
            entry => entry.ContentId == ContentIds.ToolMeal);
    }

    [Fact]
    public void StartingToolsAreNeverOfferedForSale()
    {
        ToolCatalogue catalogue = TestCatalogues.AllVisible();
        IReadOnlyList<CatalogueEntry> shop = CataloguePolicy.ShopEntries(catalogue);

        foreach (string id in CataloguePolicy.NewSaveUnlockedContentIds)
        {
            Assert.DoesNotContain(shop, entry => entry.ContentId == id);
            Assert.Contains(
                CataloguePolicy.SelectableEntries(catalogue),
                entry => entry.ContentId == id);
        }
    }

    [Fact]
    public void ShopAndToolListsStayInProgressionOrder()
    {
        ToolCatalogue catalogue = TestCatalogues.AllVisible();

        Assert.Equal(
            CataloguePolicy.ShopEntries(catalogue).Select(entry => entry.ProgressionOrder),
            CataloguePolicy.ShopEntries(catalogue)
                .Select(entry => entry.ProgressionOrder)
                .OrderBy(order => order));
    }

    [Theory]
    [InlineData("tool.not_in_this_build", PurchaseStatus.InvalidContentId)]
    [InlineData(null, PurchaseStatus.InvalidContentId)]
    [InlineData(ContentIds.ToolGrab, PurchaseStatus.NotPurchasable)]
    [InlineData(ContentIds.ToolMeal, PurchaseStatus.NotAvailable)]
    [InlineData(ContentIds.UpgradeStrength, PurchaseStatus.NotAvailable)]
    public void IneligibleEntriesAreRefusedForWhatTheyAreNotForTheBalance(
        string? contentId,
        PurchaseStatus expected)
    {
        ToolCatalogue catalogue = TestCatalogues.Standard();

        Assert.Equal(
            expected,
            CataloguePolicy.EvaluatePurchase(
                catalogue,
                contentId,
                isOwned: false,
                balanceMilliCredits: long.MaxValue));
    }

    [Fact]
    public void EligibilityChecksOwnershipBeforeFunds()
    {
        ToolCatalogue catalogue = TestCatalogues.Standard();

        Assert.Equal(
            PurchaseStatus.AlreadyOwned,
            CataloguePolicy.EvaluatePurchase(
                catalogue,
                ContentIds.ToolBaseball,
                isOwned: true,
                balanceMilliCredits: 0));
        Assert.Equal(
            PurchaseStatus.InsufficientFunds,
            CataloguePolicy.EvaluatePurchase(
                catalogue,
                ContentIds.ToolBaseball,
                isOwned: false,
                balanceMilliCredits: TestCatalogues.BaseballPrice - 1));
        Assert.Equal(
            PurchaseStatus.Purchased,
            CataloguePolicy.EvaluatePurchase(
                catalogue,
                ContentIds.ToolBaseball,
                isOwned: false,
                balanceMilliCredits: TestCatalogues.BaseballPrice));
    }

    [Fact]
    public void TheLaunchCatalogueIsTheSixteenConfirmedEntries()
    {
        // FR-013.2 by count and by ID, so an entry cannot be quietly added or dropped.
        Assert.Equal(16, CataloguePolicy.LaunchContentIds.Count);
        Assert.Equal(
            CataloguePolicy.LaunchContentIds.Count,
            CataloguePolicy.LaunchContentIds.Distinct().Count());
        Assert.Empty(CataloguePolicy.ValidateLaunchCatalogue(TestCatalogues.AllVisible()));
    }

    [Fact]
    public void AnIncompleteOrMisStartedLaunchCatalogueIsReported()
    {
        Assert.NotEmpty(CataloguePolicy.ValidateLaunchCatalogue(TestCatalogues.Standard()));

        List<CatalogueEntry> entries = [.. TestCatalogues.AllVisible().Entries];
        int baseballIndex = entries.FindIndex(entry => entry.ContentId == ContentIds.ToolBaseball);
        entries[baseballIndex] = entries[baseballIndex] with
        {
            Kind = CatalogueEntryKind.StartingTool,
            PriceMilliCredits = 0,
        };

        Assert.Contains(
            CataloguePolicy.ValidateLaunchCatalogue(new ToolCatalogue(entries)),
            error => error.Contains("a new save does not own it"));
    }

    [Fact]
    public void EveryLaunchEntryIsAKnownContentId()
    {
        foreach (string id in CataloguePolicy.LaunchContentIds)
        {
            Assert.True(ContentIds.IsCatalogueEntry(id), id);
            Assert.True(ContentIds.IsKnown(id), id);
        }
    }
}
