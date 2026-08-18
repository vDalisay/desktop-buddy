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
    public const long PetPrice = 1_000;
    public const long TicklePrice = 2_000;
    public const long BoxingGlovePrice = 3_000;
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
    /// A partial catalogue shaped like the Demo progression contract: Grab is the only
    /// starting tool; Pet, Tickle, Boxing Glove, and Baseball are finished purchasables;
    /// Meal is an unfinished purchasable; and the retired passive upgrade stays hidden.
    /// Nothing ships as a passive upgrade since Power Grab replaced Strength Upgrade, but
    /// the entry kind still exists and its rules still hold.
    /// </summary>
    public static ToolCatalogue Standard() => new(StandardEntries());

    public static List<CatalogueEntry> StandardEntries() =>
    [
        Entry(ContentIds.ToolGrab, CatalogueEntryKind.StartingTool, 0, 0),
        Entry(ContentIds.ToolPet, CatalogueEntryKind.PurchasableTool, PetPrice, 1),
        Entry(ContentIds.ToolTickle, CatalogueEntryKind.PurchasableTool, TicklePrice, 2),
        Entry(ContentIds.ToolBoxingGlove, CatalogueEntryKind.PurchasableTool, BoxingGlovePrice, 3),
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

    /// <summary>The full launch set, every entry finished and priced.</summary>
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
                : CatalogueEntryKind.PurchasableTool;
            entries.Add(Entry(id, kind, starting ? 0 : 1_000 * (order + 1), order));
            order++;
        }

        return new ToolCatalogue(entries);
    }
}
