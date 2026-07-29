using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Tests.Content;

/// <summary>
/// Catalogue fixtures for the domain tests. The shipped prices and visibility live in the
/// authored Resources; these are deliberately independent numbers, so a test cannot pass
/// merely because it agrees with itself about what the shipped data says.
/// </summary>
internal static class TestCatalogues
{
    public const long BaseballPrice = 3_000;
    public const long MealPrice = 6_000;

    public static CatalogueEntry Entry(
        string contentId,
        CatalogueEntryKind kind,
        long priceMilliCredits,
        int progressionOrder,
        bool visible = true) => new(
        contentId,
        kind,
        priceMilliCredits,
        progressionOrder,
        visible,
        $"shop.{contentId}.name",
        $"shop.{contentId}.description");

    /// <summary>
    /// A catalogue shaped like the shipped one: the four starting tools, one finished
    /// purchasable tool (Baseball), one unfinished purchasable tool (Meal), and the hidden
    /// FR-019 upgrade.
    /// </summary>
    public static ToolCatalogue Standard() => new(StandardEntries());

    public static List<CatalogueEntry> StandardEntries() =>
    [
        Entry(ContentIds.ToolGrab, CatalogueEntryKind.StartingTool, 0, 0),
        Entry(ContentIds.ToolPet, CatalogueEntryKind.StartingTool, 0, 1),
        Entry(ContentIds.ToolTickle, CatalogueEntryKind.StartingTool, 0, 2),
        Entry(ContentIds.ToolBoxingGlove, CatalogueEntryKind.StartingTool, 0, 3),
        Entry(ContentIds.ToolBaseball, CatalogueEntryKind.PurchasableTool, BaseballPrice, 4),
        Entry(
            ContentIds.ToolMeal,
            CatalogueEntryKind.CareConsumable,
            MealPrice,
            5,
            visible: false),
        Entry(
            ContentIds.UpgradeStrength,
            CatalogueEntryKind.PassiveUpgrade,
            0,
            6,
            visible: false),
    ];

    /// <summary>The full FR-013.2 launch set, every entry finished and priced.</summary>
    public static ToolCatalogue AllVisible()
    {
        var entries = new List<CatalogueEntry>(CataloguePolicy.LaunchContentIds.Count);
        int order = 0;
        foreach (string id in CataloguePolicy.LaunchContentIds)
        {
            bool starting = false;
            foreach (string startingId in CataloguePolicy.NewSaveUnlockedContentIds)
                starting |= startingId == id;

            CatalogueEntryKind kind = starting
                ? CatalogueEntryKind.StartingTool
                : id == ContentIds.UpgradeStrength
                    ? CatalogueEntryKind.PassiveUpgrade
                    : CatalogueEntryKind.PurchasableTool;
            entries.Add(Entry(id, kind, starting ? 0 : 1_000 * (order + 1), order));
            order++;
        }

        return new ToolCatalogue(entries);
    }
}
