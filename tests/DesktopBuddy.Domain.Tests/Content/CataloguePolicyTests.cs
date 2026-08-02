using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Content;

public sealed class CataloguePolicyTests
{
    [Fact]
    public void TheRetiredUpgradeIsNeverATool()
    {
        // FR-019.9: Power Grab replaced the passive upgrade. The old ID survives for the
        // schema-5 migration only — it is still known, and it is still never a tool.
        ToolCatalogue catalogue = TestCatalogues.AllVisible();

        Assert.DoesNotContain(
            CataloguePolicy.LaunchContentIds,
            id => id == ContentIds.UpgradeStrength);
        Assert.False(CataloguePolicy.IsSelectable(catalogue, ContentIds.UpgradeStrength));
        Assert.False(ContentIds.IsTool(ContentIds.UpgradeStrength));
        Assert.False(ContentIds.TryParseTool(ContentIds.UpgradeStrength, out _));
    }

    [Fact]
    public void PowerGrabIsBothSoldAndSelectable()
    {
        // FR-019: unlike the upgrade it replaced, Power Grab is an ordinary purchasable tool.
        ToolCatalogue catalogue = TestCatalogues.AllVisible();

        Assert.Contains(
            CataloguePolicy.ShopEntries(catalogue),
            entry => entry.ContentId == ContentIds.ToolPowerGrab);
        Assert.Contains(
            CataloguePolicy.SelectableEntries(catalogue),
            entry => entry.ContentId == ContentIds.ToolPowerGrab);
        Assert.True(CataloguePolicy.IsSelectable(catalogue, ContentIds.ToolPowerGrab));
    }

    [Fact]
    public void TheSelectableSetIsTheSixteenLaunchInteractions()
    {
        // 11D-5: no dock exists yet, so this is the proof that every launch entry — Power
        // Grab included — reaches the tool grid, and that the shop offers the twelve
        // purchasables in the §1.1 schedule order.
        ToolCatalogue catalogue = TestCatalogues.AllVisible();

        Assert.Equal(
            CataloguePolicy.LaunchContentIds,
            CataloguePolicy.SelectableEntries(catalogue).Select(entry => entry.ContentId));
        Assert.Equal(
            CataloguePolicy.LaunchContentIds.Skip(CataloguePolicy.NewSaveUnlockedContentIds.Count),
            CataloguePolicy.ShopEntries(catalogue).Select(entry => entry.ContentId));
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
    public void APurchasableOutOfScheduleOrderIsReported()
    {
        // Task 12 prices each slot by its position, so an entry that quietly moves would
        // re-price the wrong item against the wrong target time.
        List<CatalogueEntry> entries = [.. TestCatalogues.AllVisible().Entries];
        int power = entries.FindIndex(entry => entry.ContentId == ContentIds.ToolPowerGrab);
        int drink = entries.FindIndex(entry => entry.ContentId == ContentIds.ToolDrink);
        (entries[power], entries[drink]) = (
            entries[power] with { ProgressionOrder = entries[drink].ProgressionOrder },
            entries[drink] with { ProgressionOrder = entries[power].ProgressionOrder });

        Assert.Contains(
            CataloguePolicy.ValidateLaunchCatalogue(new ToolCatalogue(entries)),
            error => error.Contains("purchasable slot"));
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

    [Fact]
    public void EverySelectableLaunchEntryMapsToExactlyOneTool()
    {
        // 13B-1: the shipped catalogue is sixteen tools and nothing else — no attribution
        // ID, no retired upgrade, and no two entries selling the same tool.
        ToolCatalogue catalogue = TestCatalogues.AllVisible();
        var tools = new HashSet<ToolId>();

        foreach (CatalogueEntry entry in CataloguePolicy.SelectableEntries(catalogue))
        {
            Assert.True(ContentIds.TryParseTool(entry.ContentId, out ToolId tool), entry.ContentId);
            Assert.True(tools.Add(tool), entry.ContentId);
        }

        Assert.Equal(catalogue.Count, tools.Count);
        Assert.Empty(CataloguePolicy.ValidateLaunchCatalogue(catalogue));
    }

    [Fact]
    public void ARetiredUpgradeInTheCatalogueIsReported()
    {
        List<CatalogueEntry> entries = [.. TestCatalogues.AllVisible().Entries];
        entries[entries.Count - 1] = entries[^1] with
        {
            ContentId = ContentIds.UpgradeStrength,
            Kind = CatalogueEntryKind.PassiveUpgrade,
        };

        Assert.Contains(
            CataloguePolicy.ValidateLaunchCatalogue(new ToolCatalogue(entries)),
            error => error.Contains("retired passive upgrade"));
    }

    [Fact]
    public void ForToolIsTotalOverEveryToolId()
    {
        // 13B-2: the check that catches a future appended ToolId that was never wired into
        // the content vocabulary or the launch catalogue.
        var ids = new HashSet<string>();
        foreach (ToolId tool in Enum.GetValues<ToolId>())
        {
            string contentId = ContentIds.ForTool(tool);
            Assert.True(ids.Add(contentId), contentId);
            Assert.True(ContentIds.TryParseTool(contentId, out ToolId roundTrip));
            Assert.Equal(tool, roundTrip);
            Assert.Contains(contentId, CataloguePolicy.LaunchContentIds);
        }

        Assert.Equal(CataloguePolicy.LaunchContentIds.Count, ids.Count);
    }
}
