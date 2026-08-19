using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tests.Content;
using DesktopBuddy.Economy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

public sealed class CharacterSlotEntitlementTests
{
    [Fact]
    public void ThreeFreeSlots_ExpandPermanentlyWithoutAFiniteSlotList()
    {
        const long startingBalance = 5_000_000;
        var progress = new BuddyProgressState(
            cashPerPain: 10.0,
            initialBalanceMilliCredits: startingBalance);
        var economy = new EconomyService(progress, TestCatalogues.Standard());
        var slots = new CharacterSlotEntitlementState(progress, economy);

        Assert.Equal(3, slots.Capacity);
        Assert.Equal(3, slots.Remaining(0));
        Assert.Equal(0, slots.PurchasedSlotCount);

        long firstPrice = slots.NextPriceMilliCredits;
        Assert.True(firstPrice > 0);
        Assert.True(slots.PurchaseNext().Succeeded);
        Assert.Equal(1, slots.PurchasedSlotCount);
        Assert.Equal(4, slots.Capacity);
        Assert.Equal(startingBalance - firstPrice, economy.BalanceMilliCredits);

        ProgressSnapshot saved = progress.Snapshot();
        var restoredProgress = new BuddyProgressState(
            cashPerPain: 10.0,
            unlockedToolIds: saved.UnlockedToolIds,
            revision: saved.Revision,
            initialBalanceMilliCredits: saved.BalanceMilliCredits,
            selectedToolId: saved.SelectedToolId,
            extensions: saved.Extensions);
        var restoredEconomy = new EconomyService(restoredProgress, TestCatalogues.Standard());
        var restoredSlots = new CharacterSlotEntitlementState(restoredProgress, restoredEconomy);

        Assert.Equal(1, restoredSlots.PurchasedSlotCount);
        Assert.Equal(4, restoredSlots.Capacity);
        long secondPrice = restoredSlots.NextPriceMilliCredits;
        Assert.True(secondPrice > firstPrice);
        Assert.True(restoredSlots.PurchaseNext().Succeeded);
        Assert.Equal(2, restoredSlots.PurchasedSlotCount);
        Assert.Equal(5, restoredSlots.Capacity);
    }
}
