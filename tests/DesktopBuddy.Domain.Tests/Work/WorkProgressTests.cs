using System;
using DesktopBuddy.Domain.Work;
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
}
