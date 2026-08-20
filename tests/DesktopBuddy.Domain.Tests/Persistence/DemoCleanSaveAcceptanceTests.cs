using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tests.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

/// <summary>
/// Demo clean-save semantic gate. Runtime scenarios own presentation/physics actions; these tests
/// prove the durable tutorial/tool/slot state survives relaunch without a second persistence model.
/// </summary>
public sealed class DemoCleanSaveAcceptanceTests
{
    [Fact]
    public void FreshSave_BaseballBatTutorialAndSlotEntitlementsSurviveRelaunch()
    {
        const long startingBalance = 2_000_000;
        ToolCatalogue catalogue = TestCatalogues.AllVisible();
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
        Assert.Equal(TutorialStepIds.GrabBuddy, tutorial.NextIncompleteStepId);
        Assert.Equal(3, slots.Capacity);

        Assert.True(tutorial.MarkCompleted(TutorialStepIds.GrabBuddy));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.OpenInventory));

        Assert.True(economy.Purchase(ContentIds.ToolBaseballBat).Succeeded);
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.PurchaseBaseballBat));
        Assert.True(progress.SelectTool(ToolId.BaseballBat));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.ChargedBatHit));
        Assert.True(progress.IsToolUnlocked(ContentIds.ToolBaseballBat));
        Assert.Equal(ContentIds.ToolBaseballBat, progress.SelectedToolId);

        foreach (string stepId in TutorialStepIds.Ordered.Skip(4))
            Assert.True(tutorial.MarkCompleted(stepId));
        Assert.True(tutorial.IsComplete);
        Assert.Null(tutorial.NextIncompleteStepId);

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

        Assert.True(restoredProgress.IsToolUnlocked(ContentIds.ToolBaseballBat));
        Assert.Equal(ContentIds.ToolBaseballBat, restoredProgress.SelectedToolId);
        foreach (string stepId in TutorialStepIds.Ordered)
            Assert.True(restoredTutorial.IsCompleted(stepId));
        Assert.True(restoredTutorial.IsComplete);
        Assert.Null(restoredTutorial.NextIncompleteStepId);
        Assert.Equal(1, restoredSlots.PurchasedSlotCount);
        Assert.Equal(4, restoredSlots.Capacity);
        Assert.Equal(economy.BalanceMilliCredits, restoredEconomy.BalanceMilliCredits);

        long revisionBeforeRepeatedCompletion = restoredProgress.Revision;
        Assert.False(restoredTutorial.MarkCompleted(TutorialStepIds.ExitWorkMode));
        Assert.Equal(revisionBeforeRepeatedCompletion, restoredProgress.Revision);
        Assert.False(restoredEconomy.Purchase(ContentIds.ToolBaseballBat).Succeeded);
        Assert.Single(restoredProgress.Snapshot().UnlockedToolIds, id => id == ContentIds.ToolBaseballBat);
        Assert.Equal(4, restoredSlots.Capacity);
    }

    [Fact]
    public void TutorialV2_HasExactConciseWorkspaceOrder()
    {
        Assert.Equal(
        [
            TutorialStepIds.GrabBuddy,
            TutorialStepIds.OpenInventory,
            TutorialStepIds.PurchaseBaseballBat,
            TutorialStepIds.ChargedBatHit,
            TutorialStepIds.UnequipTool,
            TutorialStepIds.OpenPaintBuddy,
            TutorialStepIds.SelectPaintBrush,
            TutorialStepIds.SelectPaintColor,
            TutorialStepIds.PaintBuddy,
            TutorialStepIds.SavePaintBuddy,
            TutorialStepIds.UsePaintedBuddy,
            TutorialStepIds.AdmirePaintedBuddy,
            TutorialStepIds.OpenPaintBackground,
            TutorialStepIds.SelectBackgroundSpray,
            TutorialStepIds.SelectBackgroundColor,
            TutorialStepIds.PaintBackground,
            TutorialStepIds.FloatPaintBackgroundPanel,
            TutorialStepIds.SaveAndExitPaintBackground,
            TutorialStepIds.OpenBuddyStudio,
            TutorialStepIds.SelectNoseCategory,
            TutorialStepIds.SelectNoseButtonStyle,
            TutorialStepIds.BuyStudioItem,
            TutorialStepIds.EquipStudioItem,
            TutorialStepIds.SaveBuddyStudio,
            TutorialStepIds.ExitBuddyStudio,
            TutorialStepIds.AdmireStudioBuddy,
            TutorialStepIds.EnterWorkMode,
            TutorialStepIds.DragWorkCompanion,
            TutorialStepIds.ResizeWorkCompanion,
            TutorialStepIds.ToggleWorkCounter,
            TutorialStepIds.ExitWorkMode,
            TutorialStepIds.Farewell,
        ], TutorialStepIds.Ordered);
        Assert.Equal(TutorialStepIds.EnterWorkMode, TutorialStepIds.Ordered[^6]);
        Assert.Equal(TutorialStepIds.ExitWorkMode, TutorialStepIds.Ordered[^2]);
        Assert.Equal(TutorialStepIds.Farewell, TutorialStepIds.Ordered[^1]);
    }

    /// <summary>
    /// Buddy Studio is taught in one visit: category, style, buy, equip, save, exit. Unequipping
    /// is explained in the prompt instead of being demanded, so no step repeats a round trip.
    /// </summary>
    [Fact]
    public void BuddyStudio_IsASingleVisitWithoutARequiredUnequipReturn()
    {
        var progress = new BuddyProgressState(cashPerPain: 10.0);
        var tutorial = new TutorialProgressState(progress);
        foreach (string stepId in TutorialStepIds.Ordered.TakeWhile(id => id != TutorialStepIds.SaveBuddyStudio))
            Assert.True(tutorial.MarkCompleted(stepId));

        Assert.Equal(TutorialStepIds.SaveBuddyStudio, tutorial.NextIncompleteStepId);
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.SaveBuddyStudio));
        Assert.Equal(TutorialStepIds.ExitBuddyStudio, tutorial.NextIncompleteStepId);
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.ExitBuddyStudio));
        Assert.Equal(TutorialStepIds.AdmireStudioBuddy, tutorial.NextIncompleteStepId);
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.AdmireStudioBuddy));
        Assert.Equal(TutorialStepIds.EnterWorkMode, tutorial.NextIncompleteStepId);
    }

    /// <summary>
    /// "Show Tutorial Again" replays from the first step, and must leave a record behind: a
    /// removed key would read as an existing player and be auto-skipped on the next launch.
    /// </summary>
    [Fact]
    public void Restart_ReplaysFromTheFirstStepAndKeepsAPersistedRecord()
    {
        var progress = new BuddyProgressState(cashPerPain: 10.0);
        var tutorial = new TutorialProgressState(progress);
        Assert.True(tutorial.Skip());
        Assert.True(tutorial.IsComplete);

        Assert.True(tutorial.Restart());
        Assert.True(tutorial.HasPersistedRecord);
        Assert.False(tutorial.IsComplete);
        Assert.Equal(TutorialStepIds.GrabBuddy, tutorial.NextIncompleteStepId);
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.GrabBuddy));
    }

    /// <summary>The walkthrough says goodbye; it never vanishes the instant Work Mode closes.</summary>
    [Fact]
    public void Farewell_IsTheTerminalStepAfterWorkMode()
    {
        var progress = new BuddyProgressState(cashPerPain: 10.0);
        var tutorial = new TutorialProgressState(progress);
        foreach (string stepId in TutorialStepIds.Ordered.TakeWhile(id => id != TutorialStepIds.ExitWorkMode))
            Assert.True(tutorial.MarkCompleted(stepId));

        Assert.True(tutorial.MarkCompleted(TutorialStepIds.ExitWorkMode));
        Assert.False(tutorial.IsComplete);
        Assert.Equal(TutorialStepIds.Farewell, tutorial.NextIncompleteStepId);
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.Farewell));
        Assert.True(tutorial.IsComplete);
        Assert.Null(tutorial.NextIncompleteStepId);
    }

    [Fact]
    public void LegacyV1Record_DoesNotMasqueradeAsV2Progress()
    {
        var progress = new BuddyProgressState(cashPerPain: 10.0);
        Assert.True(progress.SetExtensionValue(TutorialProgressState.LegacyExtensionKey, "demo.onboarding.grab_buddy"));

        var tutorial = new TutorialProgressState(progress);
        Assert.True(tutorial.HasLegacyRecord);
        Assert.False(tutorial.HasPersistedRecord);
        Assert.Equal(TutorialStepIds.GrabBuddy, tutorial.NextIncompleteStepId);
    }

    [Fact]
    public void V2Snapshot_FiltersUnknownAndDuplicateStepTokens()
    {
        var progress = new BuddyProgressState(cashPerPain: 10.0);
        Assert.True(progress.SetExtensionValue(
            TutorialProgressState.ExtensionKey,
            $"future.step|{TutorialStepIds.GrabBuddy}|{TutorialStepIds.GrabBuddy}|{TutorialStepIds.OpenInventory}"));

        var tutorial = new TutorialProgressState(progress);
        TutorialProgressSnapshot snapshot = tutorial.Snapshot();
        Assert.Equal([TutorialStepIds.GrabBuddy, TutorialStepIds.OpenInventory], snapshot.CompletedStepIds);
        Assert.Equal(TutorialStepIds.PurchaseBaseballBat, tutorial.NextIncompleteStepId);
    }

    [Fact]
    public void SkippedTutorial_SurvivesRelaunchAndRemainsTerminal()
    {
        var progress = new BuddyProgressState(cashPerPain: 10.0);
        var tutorial = new TutorialProgressState(progress);

        Assert.True(tutorial.Skip());
        Assert.True(tutorial.Snapshot().Skipped);
        Assert.True(tutorial.IsComplete);
        Assert.Null(tutorial.NextIncompleteStepId);

        ProgressSnapshot saved = progress.Snapshot();
        var restoredProgress = new BuddyProgressState(
            cashPerPain: 10.0,
            unlockedToolIds: saved.UnlockedToolIds,
            revision: saved.Revision,
            initialBalanceMilliCredits: saved.BalanceMilliCredits,
            selectedToolId: saved.SelectedToolId,
            extensions: saved.Extensions);
        var restoredTutorial = new TutorialProgressState(restoredProgress);

        Assert.True(restoredTutorial.Snapshot().Skipped);
        Assert.True(restoredTutorial.IsComplete);
        Assert.Null(restoredTutorial.NextIncompleteStepId);
        foreach (string stepId in TutorialStepIds.Ordered)
            Assert.True(restoredTutorial.IsCompleted(stepId));

        long revisionBefore = restoredProgress.Revision;
        Assert.False(restoredTutorial.MarkCompleted(TutorialStepIds.GrabBuddy));
        Assert.Equal(revisionBefore, restoredProgress.Revision);
    }

    [Fact]
    public async Task DemoExtensionsAndSelectedTool_RoundTripThroughProductionJsonStore()
    {
        const double cashPerPain = 10.0;
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-demo-save-{Guid.NewGuid():N}");
        string progressPath = Path.Combine(root, "progress.json");
        string settingsPath = Path.Combine(root, "settings.json");
        ToolCatalogue catalogue = TestCatalogues.AllVisible();

        try
        {
            var progress = new BuddyProgressState(
                cashPerPain,
                initialBalanceMilliCredits: 2_000_000);
            var economy = new EconomyService(progress, catalogue);
            var tutorial = new TutorialProgressState(progress);
            var slots = new CharacterSlotEntitlementState(progress, economy);

            foreach (string stepId in TutorialStepIds.Ordered)
                Assert.True(tutorial.MarkCompleted(stepId));
            Assert.True(economy.Purchase(ContentIds.ToolBaseballBat).Succeeded);
            Assert.True(progress.SelectTool(ToolId.BaseballBat));
            Assert.True(slots.PurchaseNext().Succeeded);

            var store = new JsonProgressStore(progressPath, settingsPath);
            await store.SaveProgressAsync(
                ProgressSave.FromSnapshot(progress.Snapshot()),
                CancellationToken.None);

            LoadResult<ProgressSave> loaded = await store.LoadProgressAsync(CancellationToken.None);
            Assert.Equal(SaveLoadStatus.Loaded, loaded.Status);
            ProgressSave disk = Assert.IsType<ProgressSave>(loaded.Value);
            Assert.Equal(ContentIds.ToolBaseballBat, disk.SelectedToolId);
            Assert.Contains(ContentIds.ToolBaseballBat, disk.UnlockedToolIds);
            Assert.NotEmpty(disk.Extensions.Values);

            var extensionData = new ProgressExtensionData(
                disk.Extensions.UnknownSelectedToolId,
                disk.Extensions.UnknownContentIds,
                disk.Extensions.Values);
            var restored = new BuddyProgressState(
                cashPerPain,
                initialMood: disk.Mood,
                harmfulContentIds: disk.HarmfulContentIds,
                unlockedToolIds: disk.UnlockedToolIds,
                revision: disk.Revision,
                initialBalanceMilliCredits: disk.BalanceMilliCredits,
                selectedToolId: disk.SelectedToolId,
                extensions: extensionData,
                initialFullness: disk.Fullness);
            var restoredEconomy = new EconomyService(restored, catalogue);
            var restoredTutorial = new TutorialProgressState(restored);
            var restoredSlots = new CharacterSlotEntitlementState(restored, restoredEconomy);

            Assert.Equal(ToolId.BaseballBat, restored.SelectedTool);
            Assert.True(restoredTutorial.IsComplete);
            Assert.Null(restoredTutorial.NextIncompleteStepId);
            Assert.Equal(4, restoredSlots.Capacity);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Temp cleanup must not hide the persistence assertion result on Windows CI.
            }
        }
    }
}
