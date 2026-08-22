using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// The engine-free catalogue rules: what may be shown, what may be bought, and what may be
/// selected. It owns no authored prices, display copy, or Godot references — it reads a
/// validated <see cref="ToolCatalogue"/> snapshot and answers questions about it.
///
/// <para>
/// None of this runs on the 120 Hz gameplay tick: the shop and the tool grid ask these
/// questions when the panel opens or a purchase is attempted, so the filtering members are
/// allowed to allocate their result lists.
/// </para>
/// </summary>
public static class CataloguePolicy
{
    /// <summary>
    /// The single tool a fresh Demo save owns. Declared once here so save seeding and the
    /// shipped catalogue cannot drift apart. Pet, Tickle, and Boxing Glove became normal
    /// purchasables for the Demo progression pass on 2026-08-18.
    /// </summary>
    public static readonly IReadOnlyList<string> NewSaveUnlockedContentIds = new[]
    {
        ContentIds.ToolGrab,
    };

    /// <summary>
    /// The complete launch catalogue: sixteen selectable interactions, no passive upgrade.
    /// The order is also the free-choice shop/progression order: Grab is the only starting
    /// tool and the remaining fifteen entries are purchasable. This is an ordering contract,
    /// not a prerequisite graph — a player may save toward any affordable visible item.
    ///
    /// <para>FR-013.2 confirmed fourteen interactions; the Nerf Blaster is the fifteenth,
    /// added when M5 split the toy gun from the real one so the guns arrive as a
    /// progression rather than as one weapon the owner could not place. Power Grab is the
    /// sixteenth: it replaced the former passive Strength upgrade, which now survives only
    /// as a save migration.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> LaunchContentIds = new[]
    {
        ContentIds.ToolGrab,

        // Price order, low to high (owner schema 2026-08-22): the shop reads as a ladder, and
        // the bat stays the tutorial's first purchase at a token price. Ties keep the order the
        // owner listed them in.
        ContentIds.ToolBaseballBat,
        ContentIds.ToolPet,
        ContentIds.ToolBoxingGlove,
        ContentIds.ToolTickle,
        ContentIds.ToolBaseball,
        ContentIds.ToolMeal,
        ContentIds.ToolDrink,
        ContentIds.ToolNerfBlaster,
        ContentIds.ToolRepairKit,
        ContentIds.ToolPistol,
        ContentIds.ToolSoccerBall,
        ContentIds.ToolPowerGrab,
        ContentIds.ToolRopeSuspender,
        ContentIds.ToolGrenade,
        ContentIds.ToolShotgun,
        ContentIds.ToolFireSprayer,
    };

    /// <summary>Tool entries the general shop may offer: visible, not starting, in order.</summary>
    public static IReadOnlyList<CatalogueEntry> ShopEntries(ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var shop = new List<CatalogueEntry>(catalogue.Count);
        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            if (entry.Visible && !entry.IsStarting && entry.Kind != CatalogueEntryKind.Cosmetic)
                shop.Add(entry);
        }

        return shop;
    }

    /// <summary>Released cosmetic entries Buddy Studio may offer, in authored order.</summary>
    public static IReadOnlyList<CatalogueEntry> CosmeticEntries(ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var cosmetics = new List<CatalogueEntry>(catalogue.Count);
        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            if (entry.Visible && entry.Kind == CatalogueEntryKind.Cosmetic)
                cosmetics.Add(entry);
        }

        return cosmetics;
    }

    /// <summary>
    /// Entries the tool grid may show: visible and selectable. Passive upgrades are absent
    /// here in every state, owned or not (FR-019).
    /// </summary>
    public static IReadOnlyList<CatalogueEntry> SelectableEntries(ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var tools = new List<CatalogueEntry>(catalogue.Count);
        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            if (entry.Visible && entry.IsSelectable)
                tools.Add(entry);
        }

        return tools;
    }

    /// <summary>True when this ID may ever be selected as a tool.</summary>
    public static bool IsSelectable(ToolCatalogue catalogue, string? contentId)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return catalogue.TryGet(contentId, out CatalogueEntry entry) && entry.IsSelectable;
    }

    /// <summary>
    /// Decides whether one purchase attempt may proceed, without mutating anything.
    /// <see cref="PurchaseStatus.Purchased"/> means "eligible — the atomic spend may run";
    /// every other value is the reason the shop must refuse.
    /// </summary>
    public static PurchaseStatus EvaluatePurchase(
        ToolCatalogue catalogue,
        string? contentId,
        bool isOwned,
        long balanceMilliCredits)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        if (!catalogue.TryGet(contentId, out CatalogueEntry entry))
            return PurchaseStatus.InvalidContentId;

        // Order matters: an unfinished or never-sold entry is refused for what it is, not
        // for the balance, so the shop can explain itself and tests can pin the reason.
        if (!entry.Visible)
            return PurchaseStatus.NotAvailable;
        if (entry.IsStarting)
            return PurchaseStatus.NotPurchasable;
        if (!entry.HasValidPrice)
            return PurchaseStatus.InvalidPrice;
        if (isOwned)
            return PurchaseStatus.AlreadyOwned;
        if (balanceMilliCredits < entry.PriceMilliCredits)
            return PurchaseStatus.InsufficientFunds;

        return PurchaseStatus.Purchased;
    }

    /// <summary>
    /// Milestone-content rules for the <b>shipped</b> catalogue: every launch entry is
    /// present and the fresh-save starting set is exactly Normal Grab. Test catalogues are
    /// deliberately allowed to be partial, so this is a separate check from
    /// <see cref="ToolCatalogue.Validate"/>.
    /// </summary>
    public static IReadOnlyList<string> ValidateLaunchCatalogue(ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var errors = new List<string>();

        foreach (string id in LaunchContentIds)
        {
            if (!catalogue.Contains(id))
                errors.Add($"launch catalogue entry '{id}' is missing");
        }

        int toolEntryCount = 0;
        foreach (CatalogueEntry entry in catalogue.Entries)
            toolEntryCount += ContentIds.IsTool(entry.ContentId) ? 1 : 0;
        if (toolEntryCount != LaunchContentIds.Count)
        {
            errors.Add(
                $"launch catalogue must hold exactly {LaunchContentIds.Count} tool entries " +
                $"(FR-013.2); found {toolEntryCount}");
        }

        // The composition audit (M5 Task 13B): the shipped catalogue may only hold entries
        // this build can own and select, and the FR-019 passive upgrade is gone for good —
        // 'upgrade.strength' survives only as a v5→v6 save migration.
        var starting = new HashSet<string>(StringComparer.Ordinal);
        var tools = new HashSet<ToolId>();
        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            if (entry.IsStarting)
                starting.Add(entry.ContentId);

            if (entry.ContentId == ContentIds.UpgradeStrength)
            {
                errors.Add(
                    $"'{ContentIds.UpgradeStrength}' is a retired passive upgrade and must " +
                    "not appear in the launch catalogue (M5 Task 11)");
                continue;
            }

            if (!ContentIds.IsCatalogueEntry(entry.ContentId))
            {
                errors.Add($"'{entry.ContentId}' is not an ownable catalogue entry");
                continue;
            }

            if (entry.Kind == CatalogueEntryKind.Cosmetic)
                continue;

            if (!entry.IsSelectable)
            {
                errors.Add($"'{entry.ContentId}' is not selectable; every launch entry is a tool");
            }
            else if (!ContentIds.TryParseTool(entry.ContentId, out ToolId tool))
            {
                errors.Add($"selectable entry '{entry.ContentId}' maps to no tool");
            }
            else if (!tools.Add(tool))
            {
                errors.Add($"tool '{tool}' is sold by more than one catalogue entry");
            }
        }

        foreach (string id in NewSaveUnlockedContentIds)
        {
            if (!starting.Remove(id))
                errors.Add($"'{id}' must be a starting entry on a new save");
        }

        foreach (string extra in starting)
        {
            errors.Add($"'{extra}' is marked as a starting entry but a new save does not own it");
        }

        // Progression-order uniqueness is already ToolCatalogue.Validate's job, and the
        // catalogue is sorted by it, so the sequence check below is the ordering assert.
        // The purchasable sequence is also the benchmark schedule: an entry silently moving
        // must not cause a different item to inherit another item's tuning slot.
        var expected = new List<string>(LaunchContentIds.Count);
        foreach (string id in LaunchContentIds)
        {
            bool starts = false;
            foreach (string startingId in NewSaveUnlockedContentIds)
                starts |= string.Equals(startingId, id, StringComparison.Ordinal);
            if (!starts)
                expected.Add(id);
        }

        var actual = new List<string>(expected.Count);
        foreach (CatalogueEntry entry in ShopEntries(catalogue))
            actual.Add(entry.ContentId);

        if (expected.Count != actual.Count)
        {
            errors.Add(
                $"the shop must offer {expected.Count} purchasable entries; found {actual.Count}");
        }
        else
        {
            for (int index = 0; index < expected.Count; index++)
            {
                if (!string.Equals(expected[index], actual[index], StringComparison.Ordinal))
                {
                    errors.Add(
                        $"purchasable slot {index} must be '{expected[index]}'; " +
                        $"found '{actual[index]}'");
                }
            }
        }

        return errors;
    }
}
