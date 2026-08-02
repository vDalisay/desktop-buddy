using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

/// <summary>
/// The M5 Task 13A reset matrix. Reset takes gameplay progress back to a first run, writes
/// it, and touches nothing else — in particular it never writes the settings payload, and a
/// failed write leaves memory and disk exactly as they were.
/// </summary>
public sealed class ProgressResetTests
{
    private const double CashPerPain = 0.018;

    [Fact]
    public async Task ConfirmedReset_EqualsABrandNewSaveExceptTraits()
    {
        (BuddyProgressState progress, SaveCoordinator saves, InMemoryProgressStore store,
            EconomyService economy) = Played();

        Assert.True(await ProgressReset.ResetAsync(progress, saves, economy));

        ProgressSnapshot after = progress.Snapshot();
        ProgressSnapshot fresh =
            new BuddyProgressState(CashPerPain, traits: after.Traits).Snapshot();
        Assert.Equal(Payload(fresh), Payload(after));
        // The whole matrix, spelled out: balance, ownership, selection, mood, fullness,
        // memories, novelty, statistics, and timers.
        Assert.Equal(0, after.BalanceMilliCredits);
        Assert.Equal(ContentIds.ToolGrab, after.SelectedToolId);
        Assert.Equal(
            CataloguePolicy.NewSaveUnlockedContentIds.OrderBy(id => id, StringComparer.Ordinal),
            after.UnlockedToolIds);
        Assert.Equal(0.0f, after.Mood);
        Assert.Equal(0.0f, after.Fullness);
        Assert.Empty(after.HarmfulContentIds);
        Assert.Equal(default, after.Statistics with { ToolUses = null, ToolPainMilli = null });
        Assert.Empty(after.Statistics.ToolUses!);
        Assert.Empty(after.Statistics.ToolPainMilli!);
        Assert.Equal(default, after.Times);
        // Written through the normal coordinator path, and the save file still exists.
        Assert.False(saves.IsDirty);
        Assert.Equal(0, store.Progress!.BalanceMilliCredits);
    }

    [Fact]
    public async Task ConfirmedReset_ResamplesTraitsAndKeepsTheSameInstance()
    {
        (BuddyProgressState progress, SaveCoordinator saves, _, EconomyService economy) = Played();
        object identity = progress;
        var sampled = new HashSet<BuddyTraits>();

        for (int reset = 0; reset < 8; reset++)
        {
            Assert.True(await ProgressReset.ResetAsync(progress, saves, economy));
            sampled.Add(progress.Traits);
        }

        // The instance never changes, so nothing composed at startup can be left holding a
        // pre-reset state; the personality does, because reset goes through the same
        // first-run factory a brand-new player does.
        Assert.Same(identity, progress);
        Assert.True(sampled.Count > 1, "reset must resample traits");
    }

    [Fact]
    public async Task ConfirmedReset_ReadsBackThroughTheSameEconomyInstance()
    {
        (BuddyProgressState progress, SaveCoordinator saves, _, EconomyService economy) = Played();
        long announced = -1;
        economy.BalanceChanged += balance => announced = balance;

        Assert.True(await ProgressReset.ResetAsync(progress, saves, economy));

        // This is the proof that nothing holds a pre-reset state: the service composed
        // before the reset reports the post-reset numbers, and it announced the change so
        // the HUD repaints.
        Assert.Equal(0, economy.BalanceMilliCredits);
        Assert.Equal(0, announced);
        Assert.False(economy.IsUnlocked(ContentIds.ToolPistol));
        Assert.Equal(
            CataloguePolicy.NewSaveUnlockedContentIds.Count,
            CataloguePolicy.SelectableEntries(economy.Catalogue)
                .Count(entry => economy.IsUnlocked(entry.ContentId)));

        // And it still moves: a deposit after the reset reads back through the same service.
        economy.DepositPassive(2_500);
        Assert.Equal(2_500, economy.BalanceMilliCredits);
    }

    [Fact]
    public async Task FailedWrite_MutatesNothingInMemoryOrOnDisk()
    {
        (BuddyProgressState progress, SaveCoordinator saves, InMemoryProgressStore store,
            EconomyService economy) = Played();
        await saves.FlushProgressAsync(force: true);
        ProgressSnapshot before = progress.Snapshot();
        ProgressSave onDisk = store.Progress!;
        int writes = store.ProgressWriteCount;
        store.NextProgressFailure = new IOException("Injected write failure.");

        Assert.False(await ProgressReset.ResetAsync(progress, saves, economy));

        Assert.Equal(Payload(before), Payload(progress.Snapshot()));
        Assert.Equal(before.Revision, progress.Revision);
        Assert.Same(onDisk, store.Progress);
        Assert.Equal(writes, store.ProgressWriteCount);
        Assert.False(saves.IsDirty);
    }

    [Fact]
    public async Task Reset_NeverWritesTheSettingsPayload()
    {
        (BuddyProgressState progress, SaveCoordinator saves, InMemoryProgressStore store,
            EconomyService economy) = Played();

        Assert.True(await ProgressReset.ResetAsync(progress, saves, economy));

        // Preferences are preserved for free: they are a separate payload written by a
        // separate call, and reset makes no such call.
        Assert.Equal(0, store.SettingsWriteCount);
        Assert.Null(store.Settings);
    }

    [Fact]
    public void ResetMatrix_HasNoAchievementSurfaceYet()
    {
        // 13A-3b: there is no achievements subsystem, so the matrix promises awarded
        // achievements survive a reset by never speaking to one. When achievements land this
        // fails, and whoever adds them has to revisit that row rather than discover it later.
        IEnumerable<string> names = typeof(ProgressStatisticsSave)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name)
            .Concat(typeof(ProgressSave)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Select(member => member.Name));

        Assert.DoesNotContain(
            names,
            name => name.Contains("Achievement", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The persisted bytes for one snapshot, with the revision normalized away. Snapshots
    /// carry arrays and dictionaries, so record equality on them is reference equality; the
    /// save payload is both structural and exactly what "unchanged on disk" means.
    /// </summary>
    private static string Payload(ProgressSnapshot snapshot) =>
        ProgressSavePolicy.Serialize(ProgressSave.FromSnapshot(snapshot with { Revision = 0 }));

    /// <summary>A save with something in it: money, unlocks, a selection, mood, and counters.</summary>
    private static (BuddyProgressState, SaveCoordinator, InMemoryProgressStore, EconomyService)
        Played()
    {
        var progress = new BuddyProgressState(CashPerPain);
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store);
        var economy = new EconomyService(progress, Catalogue());

        economy.DepositPassive(500_000);
        Assert.True(economy.Purchase(ContentIds.ToolPistol).Succeeded);
        Assert.True(progress.SelectTool(DesktopBuddy.Domain.Tools.ToolId.Pistol));
        progress.AcceptDamage(
            ContentIds.ToolPistol,
            4.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 1.0,
            new ImpactMoodEffect(ImpactMoodEffectKind.Harm));
        progress.RecordKnockout();
        progress.RecordContentUse(ContentIds.ToolPistol);
        progress.FillHunger(20.0f);
        progress.AccrueTime(120.0, 90.0, 30.0);
        return (progress, saves, store, economy);
    }

    private static ToolCatalogue Catalogue()
    {
        var entries = new List<CatalogueEntry>();
        int order = 0;
        foreach (string id in CataloguePolicy.LaunchContentIds)
        {
            bool starting = CataloguePolicy.NewSaveUnlockedContentIds.Contains(id);
            entries.Add(new CatalogueEntry(
                id,
                starting ? CatalogueEntryKind.StartingTool : CatalogueEntryKind.PurchasableTool,
                starting ? 0 : 40_000,
                order++,
                Visible: true,
                NameKey: $"shop.{id}.name",
                DescriptionKey: $"shop.{id}.description"));
        }

        return new ToolCatalogue(entries);
    }
}
