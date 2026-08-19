using System.Linq;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tests.Content;
using DesktopBuddy.Economy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

/// <summary>
/// DEMO-9 semantic clean-save gate. Godot scenarios cover the presentation/physics half of the
/// journey; this test proves the cloud-eligible state survives the important first-session
/// transitions without inventing a second persistence model.
/// </summary>
public sealed class DemoCleanSaveAcceptanceTests
{
    [Fact]
    public void FreshSave_PurchaseTutorialAndSlotEntitlementsSurviveRelaunch()
    {
        const long startingBalance = 2_000_000;
        ToolCatalogue catalogue = TestCatalogues.Standard();
        var progress = new BuddyProgressState(
            cashPerPain: 10.0,
            initialBalanceMilliCredits: startingBalance);
        var economy = new EconomyService(progress, catalogue);
        var tutorial = new TutorialProgressState(progress);
        var slots = new CharacterSlotEntitlementState(progress, economy);

        ProgressSnapshot fresh = progress.Snapshot();
        Assert.Equal([ContentIds.ToolGrab], fresh.UnlockedToolIds.OrderBy(id => id));
        Assert.Equal(ContentIds.ToolGrab, fresh.SelectedToolId);
        Assert.False(tutorial.HasPersistedRecord);
        Assert.Equal(3, slots.Capacity);

        Assert.True(tutorial.MarkCompleted(TutorialStepIds.GrabBuddy));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.EarnCredits));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.OpenShop));

        Assert.True(economy.Purchase(ContentIds.ToolPet).Succeeded);
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.PurchaseContent));
        Assert.True(progress.IsToolUnlocked(ContentIds.ToolPet));

        long beforeSlot = economy.BalanceMilliCredits;
        long slotPrice = slots.NextPriceMilliCredits;
        Assert.True(slots.PurchaseNext().Succeeded);
        Assert.Equal(beforeSlot - slotPrice, economy.BalanceMilliCredits);
        Assert.Equal(4, slots.Capacity);

        ProgressSnapshot saved = progress.Snapshot();
        var restoredProgress = new BuddyProgressState(
            cashPerPain: 10.0,
            unlockedToolIds: saved.UnlockedToolIds,
            revision: saved.Revision,
            initialBalanceMilliCredits: saved.BalanceMilliCredits,
            selectedToolId: saved.SelectedToolId,
            extensions: saved.Extensions);
        var restoredEconomy = new EconomyService(restoredProgress, catalogue);
        var restoredTutorial = new TutorialProgressState(restoredProgress);
        var restoredSlots = new CharacterSlotEntitlementState(restoredProgress, restoredEconomy);

        Assert.True(restoredProgress.IsToolUnlocked(ContentIds.ToolPet));
        Assert.True(restoredTutorial.IsCompleted(TutorialStepIds.GrabBuddy));
        Assert.True(restoredTutorial.IsCompleted(TutorialStepIds.PurchaseContent));
        Assert.Equal(TutorialStepIds.OpenPaintBuddy, restoredTutorial.NextIncompleteStepId);
        Assert.Equal(1, restoredSlots.PurchasedSlotCount);
        Assert.Equal(4, restoredSlots.Capacity);
        Assert.Equal(economy.BalanceMilliCredits, restoredEconomy.BalanceMilliCredits);

        // Duplicate purchase attempts and repeated hydration are idempotent: no second tool or
        // phantom slot is minted simply because the game is relaunched.
        Assert.False(restoredEconomy.Purchase(ContentIds.ToolPet).Succeeded);
        Assert.Single(restoredProgress.Snapshot().UnlockedToolIds, id => id == ContentIds.ToolPet);
        Assert.Equal(4, restoredSlots.Capacity);
    }
}
