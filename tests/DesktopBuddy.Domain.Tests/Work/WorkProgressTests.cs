using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using DesktopBuddy.Work;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Work;

public sealed class WorkProgressTests
{
    [Fact]
    public void Counters_TrackKeyboardAndMouseSeparately()
    {
        var counters = new WorkCounterSnapshot();
        counters = counters.Add(WorkActivityKind.KeyboardPress, 3);
        counters = counters.Add(WorkActivityKind.MouseClick, 2);

        Assert.Equal(3, counters.KeyboardPresses);
        Assert.Equal(2, counters.MouseClicks);
        Assert.Equal(5, counters.TotalActions);
    }

    [Fact]
    public void SessionMilestone_RepeatsOnlyOnceWithinOneSession()
    {
        var lifetime = new WorkProgressState();
        var session = new WorkSessionState(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var catalogue = new WorkMilestoneCatalogue([
            new WorkMilestoneDefinition(
                "work.session.total.10",
                WorkCounterKind.TotalActions,
                WorkMilestoneScope.CurrentSession,
                10,
                5_000,
                WorkMilestoneRepeatPolicy.RepeatPerSession),
        ]);

        session.Record(WorkActivityKind.KeyboardPress, 10);
        Assert.Single(session.Evaluate(lifetime, catalogue));
        Assert.Empty(session.Evaluate(lifetime, catalogue));
    }

    [Fact]
    public void LifetimeMilestone_CannotBeClaimedAgainInAnotherSession()
    {
        var lifetime = new WorkProgressState();
        var catalogue = new WorkMilestoneCatalogue([
            new WorkMilestoneDefinition(
                "work.lifetime.total.3",
                WorkCounterKind.TotalActions,
                WorkMilestoneScope.Lifetime,
                3,
                10_000,
                WorkMilestoneRepeatPolicy.OnceLifetime),
        ]);

        lifetime.Record(WorkActivityKind.KeyboardPress, 3);
        Assert.Single(new WorkSessionState().Evaluate(lifetime, catalogue));
        Assert.Empty(new WorkSessionState().Evaluate(lifetime, catalogue));
        Assert.Contains("work.lifetime.total.3", lifetime.ClaimedLifetimeMilestoneIds);
    }

    [Fact]
    public void FirstEntryGlassesFlag_IsOneShot()
    {
        var progress = new WorkProgressState();

        Assert.True(progress.MarkFirstEntryGlassesGranted());
        Assert.False(progress.MarkFirstEntryGlassesGranted());
        Assert.True(progress.FirstEntryGlassesGranted);
    }

    [Fact]
    public async System.Threading.Tasks.Task FirstWorkEntry_UnlocksGlassesOnceWithoutAnEquipmentDependency()
    {
        var progress = new BuddyProgressState(0.018);
        var work = new WorkProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, work: work);
        var reward = new WorkFirstEntryRewardService(progress, work, saves);

        WorkFirstEntryRewardResult first = await reward.EnsureAsync();
        WorkFirstEntryRewardResult second = await reward.EnsureAsync();

        Assert.True(first.WasFirstEntry);
        Assert.True(first.OwnershipGranted);
        Assert.False(second.WasFirstEntry);
        Assert.False(second.OwnershipGranted);
        Assert.True(progress.IsToolUnlocked(ContentIds.CosmeticWorkGlasses));
        Assert.True(store.Progress!.Work.FirstEntryGlassesGranted);
        Assert.Contains(ContentIds.CosmeticWorkGlasses, store.Progress.UnlockedToolIds);
    }

    [Fact]
    public void SessionJournal_RestoresCountersAndPreventsDuplicateReward()
    {
        var progress = new WorkProgressState();
        var session = new WorkSessionState(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var catalogue = new WorkMilestoneCatalogue([
            new WorkMilestoneDefinition(
                "work.session.total.10",
                WorkCounterKind.TotalActions,
                WorkMilestoneScope.CurrentSession,
                10,
                5_000,
                WorkMilestoneRepeatPolicy.RepeatPerSession),
        ]);
        session.Record(WorkActivityKind.KeyboardPress, 10);
        Assert.Single(session.Evaluate(progress, catalogue));
        progress.CheckpointSession(session.Snapshot());

        ProgressSave saved = ProgressSave.FromSnapshot(
            new BuddyProgressState(0.018).Snapshot(),
            work: progress.Snapshot());
        ProgressSave restoredSave = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(saved)).Save!;
        WorkProgressState restoredProgress = restoredSave.Work.CreateState();
        var restoredSession = new WorkSessionState(restoredProgress.ActiveSession!.Value);

        Assert.Equal(session.SessionId, restoredSession.SessionId);
        Assert.Equal(10, restoredSession.Counters.TotalActions);
        Assert.Empty(restoredSession.Evaluate(restoredProgress, catalogue));
    }

    [Fact]
    public void SessionJournal_RejectsInvalidPersistedState()
    {
        var invalid = new ProgressSave
        {
            Work = new WorkProgressSave
            {
                ActiveSession = new WorkSessionSave
                {
                    SessionId = Guid.NewGuid(),
                    KeyboardPresses = -1,
                },
            },
        };

        Assert.Equal(
            SaveDecodeStatus.Invalid,
            ProgressSavePolicy.Decode(System.Text.Json.JsonSerializer.Serialize(invalid)).Status);
    }
}
