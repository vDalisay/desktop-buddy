using System;
using System.Globalization;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Unbounded character-capacity entitlement for the Steam Demo. Three slots are free; each paid
/// expansion is represented by one durable generic-ownership purchase and a compact versioned
/// progress extension recording how many expansions have been claimed. No finite slot list is
/// preallocated.
/// </summary>
public sealed class CharacterSlotEntitlementState
{
    public const int FreeSlots = 3;
    public const string ExtensionKey = "demo.character_slots.v1";

    private const long FirstPaidSlotMilliCredits = 100_000;
    private const long PaidSlotStepMilliCredits = 50_000;
    private const string OwnershipPrefix = "cosmetic.character_slot_entitlement.";

    private readonly BuddyProgressState _progress;
    private readonly EconomyService _economy;

    public CharacterSlotEntitlementState(BuddyProgressState progress, EconomyService economy)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    }

    public int PurchasedSlotCount
    {
        get
        {
            if (_progress.Extensions?.Values is not { } values ||
                !values.TryGetValue(ExtensionKey, out string? encoded) ||
                !int.TryParse(encoded, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
                return 0;
            return Math.Max(0, count);
        }
    }

    public int Capacity => checked(FreeSlots + PurchasedSlotCount);

    /// <summary>
    /// Price for the next expansion. The exact numbers are intentionally isolated here because
    /// final slot pacing is an owner gate; the rule itself is deterministic and survives UI
    /// refactors. First paid slot is 100 credits, then +50 credits per additional expansion.
    /// </summary>
    public long NextPriceMilliCredits => PriceForPurchasedIndex(PurchasedSlotCount + 1);

    public int Remaining(int occupiedSlots) => Math.Max(0, Capacity - Math.Max(0, occupiedSlots));

    public PurchaseResult PurchaseNext()
    {
        int nextIndex = checked(PurchasedSlotCount + 1);
        string contentId = OwnershipId(nextIndex);
        long price = PriceForPurchasedIndex(nextIndex);
        var oneShotCatalogue = new ToolCatalogue(
        [
            new CatalogueEntry(
                contentId,
                CatalogueEntryKind.Cosmetic,
                price,
                ProgressionOrder: 0,
                Visible: true,
                NameKey: "character.slot.entitlement",
                DescriptionKey: "Permanent additional character slot"),
        ]);

        PurchaseResult result = _economy.Purchase(contentId);
        if (result.Status == PurchaseStatus.InvalidContentId)
        {
            // EconomyService intentionally owns its shipping catalogue. Slot expansions are
            // dynamic/unbounded, so use the same progress purchase boundary with a one-entry
            // authoritative catalogue rather than teaching the global catalogue infinite IDs.
            result = _progress.Purchase(contentId, oneShotCatalogue);
            if (result.Succeeded)
                _economy.NotifyBalanceChanged();
        }

        if (result.Succeeded || result.Status == PurchaseStatus.AlreadyOwned)
            _progress.SetExtensionValue(ExtensionKey, nextIndex.ToString(CultureInfo.InvariantCulture));
        return result;
    }

    public static long PriceForPurchasedIndex(int purchasedIndex)
    {
        if (purchasedIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(purchasedIndex));
        return checked(FirstPaidSlotMilliCredits + ((long)purchasedIndex - 1L) * PaidSlotStepMilliCredits);
    }

    public static string OwnershipId(int purchasedIndex)
    {
        if (purchasedIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(purchasedIndex));
        return OwnershipPrefix + purchasedIndex.ToString(CultureInfo.InvariantCulture);
    }
}
