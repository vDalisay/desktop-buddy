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
        Assert.Equal(TutorialStepIds.GrabBuddy, tutorial.NextIncompleteStepId);
        Assert.Equal(3, slots.Capacity);

        Assert.True(tutorial.MarkCompleted(TutorialStepIds.GrabBuddy));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.EarnCredits));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.OpenShop));

        Assert.True(economy.Purchase(ContentIds.ToolPet).Succeeded);
        Assert.True(progress.SelectTool(ToolId.Pet));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.PurchaseContent));
        Assert.True(progress.IsToolUnlocked(ContentIds.ToolPet));
        Assert.Equal(ContentIds.ToolPet, progress.SelectedToolId);

        Assert.True(tutorial.MarkCompleted(TutorialStepIds.OpenPaintBuddy));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.EnterWorkMode));
        Assert.True(tutorial.MarkCompleted(TutorialStepIds.ExitWorkMode));
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

        Assert.True(restoredProgress.IsToolUnlocked(ContentIds.ToolPet));
        Assert.Equal(ContentIds.ToolPet, restoredProgress.SelectedToolId);
        foreach (string stepId in TutorialStepIds.Ordered)
            Assert.True(restoredTutorial.IsCompleted(stepId));
        Assert.True(restoredTutorial.IsComplete);
        Assert.Null(restoredTutorial.NextIncompleteStepId);
        Assert.Equal(1, restoredSlots.PurchasedSlotCount);
        Assert.Equal(4, restoredSlots.Capacity);
        Assert.Equal(economy.BalanceMilliCredits, restoredEconomy.BalanceMilliCredits);

        // Duplicate completion/purchase attempts and repeated hydration are idempotent: no second
        // tutorial mutation, tool or phantom slot is minted simply because the game is relaunched.
        long revisionBeforeRepeatedCompletion = restoredProgress.Revision;
        Assert.False(restoredTutorial.MarkCompleted(TutorialStepIds.ExitWorkMode));
        Assert.Equal(revisionBeforeRepeatedCompletion, restoredProgress.Revision);
        Assert.False(restoredEconomy.Purchase(ContentIds.ToolPet).Succeeded);
        Assert.Single(restoredProgress.Snapshot().UnlockedToolIds, id => id == ContentIds.ToolPet);
        Assert.Equal(4, restoredSlots.Capacity);
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
        ToolCatalogue catalogue = TestCatalogues.Standard();

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
            Assert.True(economy.Purchase(ContentIds.ToolPet).Succeeded);
            Assert.True(progress.SelectTool(ToolId.Pet));
            Assert.True(slots.PurchaseNext().Succeeded);

            var store = new JsonProgressStore(progressPath, settingsPath);
            await store.SaveProgressAsync(
                ProgressSave.FromSnapshot(progress.Snapshot()),
                CancellationToken.None);

            LoadResult<ProgressSave> loaded = await store.LoadProgressAsync(CancellationToken.None);
            Assert.Equal(SaveLoadStatus.Loaded, loaded.Status);
            ProgressSave disk = Assert.IsType<ProgressSave>(loaded.Value);
            Assert.Equal(ContentIds.ToolPet, disk.SelectedToolId);
            Assert.Contains(ContentIds.ToolPet, disk.UnlockedToolIds);
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

            Assert.Equal(ToolId.Pet, restored.SelectedTool);
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
