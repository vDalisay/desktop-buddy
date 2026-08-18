using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class BuddyProgressStateTests
{
    private const double CashPerPain = 0.5;

    private static BuddyProgressState NewSave() => new(CashPerPain);

    [Fact]
    public void NewSave_MatchesDemoDefaults()
    {
        BuddyProgressState state = NewSave();

        Assert.Equal(0, state.BalanceMilliCredits);
        Assert.Equal(ToolId.Grab, state.SelectedTool);
        Assert.Equal(ContentIds.ToolGrab, state.SelectedToolId);
        Assert.True(state.IsToolUnlocked(ContentIds.ToolGrab));
        Assert.False(state.IsToolUnlocked(ContentIds.ToolPet));
        Assert.False(state.IsToolUnlocked(ContentIds.ToolTickle));
        Assert.False(state.IsToolUnlocked(ContentIds.ToolBoxingGlove));
        Assert.False(state.IsToolUnlocked(ContentIds.ToolBaseball));
        Assert.Equal(0.0f, state.Mood);
        Assert.Empty(state.HarmfulContentIds);
    }

    [Fact]
    public void LoadedLegacyStartingSet_RemainsOwnedAfterDemoDefaultChange()
    {
        // Before the Demo progression pass, a fresh save owned Grab/Pet/Tickle/Boxing Glove.
        // A loaded save is explicit historical state, not a request to reseed today's defaults:
        // changing the new-save contract must never take already-owned interactions away.
        var state = new BuddyProgressState(
            CashPerPain,
            unlockedToolIds: new[]
            {
                ContentIds.ToolGrab,
                ContentIds.ToolPet,
                ContentIds.ToolTickle,
                ContentIds.ToolBoxingGlove,
            },
            selectedToolId: ContentIds.ToolBoxingGlove);

        Assert.True(state.IsToolUnlocked(ContentIds.ToolGrab));
        Assert.True(state.IsToolUnlocked(ContentIds.ToolPet));
        Assert.True(state.IsToolUnlocked(ContentIds.ToolTickle));
        Assert.True(state.IsToolUnlocked(ContentIds.ToolBoxingGlove));
        Assert.Equal(ToolId.BoxingGlove, state.SelectedTool);
        Assert.Equal(
            new[]
            {
                ContentIds.ToolBoxingGlove,
                ContentIds.ToolGrab,
                ContentIds.ToolPet,
                ContentIds.ToolTickle,
            },
            state.Snapshot().UnlockedToolIds);
    }

    [Fact]
    public void AcceptDamage_PaysHarmsAndCounts()
    {
        BuddyProgressState state = NewSave();

        long milli = state.AcceptDamage(
            ContentIds.ToolBoxingGlove,
            pain: 20.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 1.0);

        // 20 pain × 1.0 region × 1.0 conscious × 0.5 cash = 10 credits = 10_000 milli.
        Assert.Equal(10_000, milli);
        Assert.Equal(10_000, state.BalanceMilliCredits);
        Assert.Equal(10, state.BalanceCredits);
        Assert.True(state.IsContentHarmful(ContentIds.ToolBoxingGlove));
        Assert.Equal(-2.0f, state.Mood, 0.0005f); // 20 × 0.1
        Assert.Equal(-2.0f, state.Statistics.LowestMood, 0.0005f);
        Assert.Equal(0.0f, state.Statistics.HighestMood, 0.0005f);
        Assert.Equal(1, state.Statistics.ScoredImpacts);
        Assert.Equal(10_000, state.Statistics.EarnedMilliCredits);
    }

    [Fact]
    public void EnjoyedImpactPaysAndTracksPainButRaisesMoodWithoutHarmfulMemory()
    {
        BuddyProgressState state = NewSave();

        long milli = state.AcceptDamage(
            ContentIds.ToolNerfBlaster,
            pain: 20.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 1.0,
            ImpactMoodEffect.Enjoyment(0.25f));

        Assert.Equal(10_000, milli);
        Assert.Equal(0.25f, state.Mood, 0.0005f);
        Assert.False(state.IsContentHarmful(ContentIds.ToolNerfBlaster));
        Assert.Equal(20_000, state.Statistics.TotalPainMilli);
        Assert.Equal(20_000, state.Statistics.ToolPainMilli![ContentIds.ToolNerfBlaster]);
        Assert.Equal(0, state.Statistics.CareAwards);
    }

    [Fact]
    public void AnnoyingImpactUsesSharedPainMoodLossWithoutPersistentHarmfulMemory()
    {
        BuddyProgressState state = NewSave();

        state.AcceptDamage(
            ContentIds.ToolNerfBlaster,
            pain: 40.0f,
            PayoutRegion.Head,
            DamageConsciousness.Conscious,
            now: 1.0,
            ImpactMoodEffect.Annoyance);

        Assert.Equal(-4.0f, state.Mood, 0.0005f);
        Assert.False(state.IsContentHarmful(ContentIds.ToolNerfBlaster));
        Assert.Equal(-4.0f, state.Statistics.LowestMood, 0.0005f);
    }

    [Fact]
    public void DamageTracksLowestMoodAcrossRepeatedHits()
    {
        BuddyProgressState state = NewSave();

        state.AcceptDamage(
            ContentIds.ToolBoxingGlove,
            pain: 20.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 1.0);
        state.AcceptDamage(
            ContentIds.ToolBoxingGlove,
            pain: 30.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 2.0);

        Assert.Equal(-5.0f, state.Mood, 0.0005f);
        Assert.Equal(-5.0f, state.Statistics.LowestMood, 0.0005f);
    }

    [Fact]
    public void Deposit_AddsToBalanceWithoutTouchingMood()
    {
        BuddyProgressState state = NewSave();

        state.Deposit(2_500);

        Assert.Equal(2_500, state.BalanceMilliCredits);
        Assert.Equal(2, state.BalanceCredits);
        Assert.Equal(0.0f, state.Mood);
        Assert.Equal(2_500, state.Statistics.EarnedMilliCredits);
    }

    [Fact]
    public void Revision_AdvancesOnEveryPersistentMutationAndNotOnNoOps()
    {
        BuddyProgressState state = NewSave();
        long start = state.Revision;

        Assert.False(state.SelectTool(ToolId.Grab)); // already selected
        state.Deposit(0);
        state.DriftMood(0.0);
        state.AccrueTime(0.0, 0.0, 0.0);
        Assert.Equal(start, state.Revision);

        Assert.True(state.Unlock(ContentIds.ToolPet));
        long unlockedRevision = state.Revision;
        Assert.False(state.Unlock(ContentIds.ToolPet));
        Assert.Equal(unlockedRevision, state.Revision);

        Assert.True(state.SelectTool(ToolId.Pet));
        Assert.True(state.Revision > unlockedRevision);
    }

    [Fact]
    public void SelectTool_RaisesOneSemanticEvent()
    {
        BuddyProgressState state = NewSave();
        Assert.True(state.Unlock(ContentIds.ToolPet));
        var seen = new List<ProgressChange>();
        state.Changed += seen.Add;

        state.SelectTool(ToolId.Pet);
        state.SelectTool(ToolId.Pet); // no-op

        Assert.Equal(new[] { ProgressChange.ToolSelected }, seen);
    }

    [Fact]
    public void DriftMood_RaisesNoSemanticEvent()
    {
        // Drift runs continuously at runtime; a per-tick event would flood subscribers.
        // The save coordinator watches Revision instead.
        var state = new BuddyProgressState(CashPerPain, initialMood: 30.0f);
        var seen = new List<ProgressChange>();
        state.Changed += seen.Add;

        state.DriftMood(60.0);

        Assert.Empty(seen);
        Assert.Equal(29.5f, state.Mood, 0.0005f);
    }

    [Fact]
    public void ApplyCareMood_CountsAwardsAndReportsTrustReset()
    {
        var state = new BuddyProgressState(CashPerPain, initialMood: 59.0f);
        state.AcceptDamage(
            ContentIds.ToolBoxingGlove,
            pain: 10.0f,
            PayoutRegion.Torso,
            DamageConsciousness.Conscious,
            now: 0.0);
        Assert.True(state.IsContentHarmful(ContentIds.ToolBoxingGlove));

        var seen = new List<ProgressChange>();
        state.Changed += seen.Add;
        bool reset = state.ApplyCareMood(+2.0f); // 58 → 60, crosses upward

        Assert.True(reset);
        Assert.False(state.IsContentHarmful(ContentIds.ToolBoxingGlove));
        Assert.Equal(1, state.Statistics.TrustResets);
        Assert.Equal(1, state.Statistics.CareAwards);
        Assert.Contains(ProgressChange.TrustReset, seen);
    }

    [Fact]
    public void Snapshot_CarriesSemanticStateInAStableOrder()
    {
        var state = new BuddyProgressState(
            CashPerPain,
            initialMood: -12.5f,
            harmfulContentIds: new[] { ContentIds.LooseObject, ContentIds.ToolBoxingGlove },
            unlockedToolIds: new[] { ContentIds.ToolPet },
            traits: BuddyTraits.FromPersisted(73),
            statistics: new ProgressStatistics(5, 2, 3, 1, 9_000),
            times: new CumulativeTimes(120.0, 90.0, 30.0),
            revision: 41);
        state.SelectTool(ToolId.Pet);

        ProgressSnapshot snapshot = state.Snapshot();

        Assert.Equal(ContentIds.ToolPet, snapshot.SelectedToolId);
        Assert.Equal(-12.5f, snapshot.Mood, 0.0005f);
        Assert.Equal(73, snapshot.Traits.ObstacleHopPropensity);
        Assert.Equal(5, snapshot.Statistics.ScoredImpacts);
        Assert.Equal(120.0, snapshot.Times.RunSeconds);
        Assert.True(snapshot.Revision > 41);

        // Ordinal-sorted so an unchanged state serializes byte-identically.
        Assert.Equal(new[] { ContentIds.LooseObject, ContentIds.ToolBoxingGlove }, snapshot.HarmfulContentIds);
        // Grab is never absent even when a save omits it (RAGDOLL §9).
        Assert.Equal(new[] { ContentIds.ToolGrab, ContentIds.ToolPet }, snapshot.UnlockedToolIds);
    }

    [Fact]
    public void LoadedState_RestoresMoodBandAndHistoryWithoutTrustResetting()
    {
        var state = new BuddyProgressState(
            CashPerPain,
            initialMood: 85.0f,
            harmfulContentIds: new[] { ContentIds.ToolBoxingGlove });

        Assert.Equal(MoodBand.Delighted, state.MoodBand);
        Assert.True(state.IsContentHarmful(ContentIds.ToolBoxingGlove));
        Assert.Equal(0, state.Statistics.TrustResets);
    }

    [Fact]
    public void AccrueTime_SumsForegroundAndHiddenSeparately()
    {
        BuddyProgressState state = NewSave();

        state.AccrueTime(10.0, 10.0, 0.0);
        state.AccrueTime(5.0, 0.0, 5.0);

        Assert.Equal(15.0, state.Times.RunSeconds);
        Assert.Equal(10.0, state.Times.ActiveSeconds);
        Assert.Equal(5.0, state.Times.HiddenSeconds);
    }
}
