using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Content;

public sealed class ToolCatalogueTests
{
    [Fact]
    public void EntriesAreOrderedByProgressionRegardlessOfAuthoringOrder()
    {
        List<CatalogueEntry> entries = TestCatalogues.StandardEntries();
        entries.Reverse();

        var catalogue = new ToolCatalogue(entries);

        Assert.Equal(
            entries.OrderBy(entry => entry.ProgressionOrder).Select(entry => entry.ContentId),
            catalogue.Entries.Select(entry => entry.ContentId));
    }

    [Fact]
    public void DuplicateContentIdIsRejected()
    {
        List<CatalogueEntry> entries = TestCatalogues.StandardEntries();
        entries.Add(TestCatalogues.Entry(
            ContentIds.ToolBaseball,
            CatalogueEntryKind.PurchasableTool,
            9_000,
            9));

        IReadOnlyList<string> errors = ToolCatalogue.Validate(entries);

        Assert.Contains(errors, error => error.Contains("declared more than once"));
        Assert.Throws<ArgumentException>(() => new ToolCatalogue(entries));
    }

    [Fact]
    public void DuplicateProgressionOrderIsRejected()
    {
        List<CatalogueEntry> entries = TestCatalogues.StandardEntries();
        entries.Add(TestCatalogues.Entry(
            ContentIds.ToolPistol,
            CatalogueEntryKind.PurchasableTool,
            9_000,
            entries[0].ProgressionOrder));

        Assert.Contains(
            ToolCatalogue.Validate(entries),
            error => error.Contains("reuses progression order"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("care.lab_food")]
    [InlineData("object.loose")]
    [InlineData("tool.from_a_later_build")]
    public void OnlyCatalogueContentIdsAreAccepted(string contentId)
    {
        var entry = TestCatalogues.Entry(
            contentId,
            CatalogueEntryKind.PurchasableTool,
            1_000,
            0);

        Assert.NotEmpty(ToolCatalogue.Validate([entry]));
    }

    [Fact]
    public void MissingTranslationKeysAreRejected()
    {
        CatalogueEntry noName = TestCatalogues.Entry(
            ContentIds.ToolBaseball,
            CatalogueEntryKind.PurchasableTool,
            3_000,
            0) with
        { NameKey = "  " };
        CatalogueEntry noDescription = TestCatalogues.Entry(
            ContentIds.ToolBaseball,
            CatalogueEntryKind.PurchasableTool,
            3_000,
            0) with
        { DescriptionKey = string.Empty };

        Assert.Contains(
            ToolCatalogue.Validate([noName]),
            error => error.Contains("name translation key"));
        Assert.Contains(
            ToolCatalogue.Validate([noDescription]),
            error => error.Contains("description translation key"));
    }

    [Theory]
    [InlineData(-1_000)]
    [InlineData(1_500)]
    [InlineData(1)]
    public void PartCreditAndNegativePricesAreRejected(long priceMilliCredits)
    {
        var entry = TestCatalogues.Entry(
            ContentIds.ToolBaseball,
            CatalogueEntryKind.PurchasableTool,
            priceMilliCredits,
            0);

        Assert.NotEmpty(ToolCatalogue.Validate([entry]));
    }

    [Fact]
    public void VisibleEntryWithoutACalibratedPriceIsRejected()
    {
        var entry = TestCatalogues.Entry(
            ContentIds.ToolBaseball,
            CatalogueEntryKind.PurchasableTool,
            0,
            0);

        Assert.Contains(
            ToolCatalogue.Validate([entry]),
            error => error.Contains("no calibrated price"));
    }

    [Fact]
    public void UnfinishedEntryMayStayUnpriced()
    {
        var entry = TestCatalogues.Entry(
            ContentIds.ToolShotgun,
            CatalogueEntryKind.PurchasableTool,
            0,
            0,
            visible: false);

        Assert.Empty(ToolCatalogue.Validate([entry]));
    }

    [Fact]
    public void StartingEntryCannotCarryAPriceOrBeHidden()
    {
        CatalogueEntry priced = TestCatalogues.Entry(
            ContentIds.ToolGrab,
            CatalogueEntryKind.StartingTool,
            1_000,
            0);
        CatalogueEntry hidden = TestCatalogues.Entry(
            ContentIds.ToolGrab,
            CatalogueEntryKind.StartingTool,
            0,
            0,
            visible: false);

        Assert.Contains(
            ToolCatalogue.Validate([priced]),
            error => error.Contains("cannot carry a price"));
        Assert.Contains(
            ToolCatalogue.Validate([hidden]),
            error => error.Contains("cannot be hidden"));
    }

    [Fact]
    public void APassiveUpgradeMayNotAlsoBeATool()
    {
        // FR-019: the upgrade must not be expressible as a selectable tool at all.
        CatalogueEntry upgradeOnAToolId = TestCatalogues.Entry(
            ContentIds.ToolBaseball,
            CatalogueEntryKind.PassiveUpgrade,
            1_000,
            0);
        CatalogueEntry toolOnTheUpgradeId = TestCatalogues.Entry(
            ContentIds.UpgradeStrength,
            CatalogueEntryKind.PurchasableTool,
            1_000,
            0);

        Assert.Contains(
            ToolCatalogue.Validate([upgradeOnAToolId]),
            error => error.Contains("also a selectable tool ID"));
        Assert.Contains(
            ToolCatalogue.Validate([toolOnTheUpgradeId]),
            error => error.Contains("is not a tool ID"));
    }

    [Fact]
    public void LookupIsByStableIdOnly()
    {
        ToolCatalogue catalogue = TestCatalogues.Standard();

        Assert.True(catalogue.TryGet(ContentIds.ToolBaseball, out CatalogueEntry entry));
        Assert.Equal(TestCatalogues.BaseballPrice, entry.PriceMilliCredits);
        Assert.False(catalogue.TryGet("Tool.Baseball", out _));
        Assert.False(catalogue.TryGet(null, out _));
    }
}
