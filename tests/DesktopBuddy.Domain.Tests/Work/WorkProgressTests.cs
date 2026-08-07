using System;
using DesktopBuddy.Domain.Work;
using FluentAssertions;
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

        counters.KeyboardPresses.Should().Be(3);
        counters.MouseClicks.Should().Be(2);
        counters.TotalActions.Should().Be(5);
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
        session.Evaluate(lifetime, catalogue).Should().ContainSingle();
        session.Evaluate(lifetime, catalogue).Should().BeEmpty();
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
        new WorkSessionState().Evaluate(lifetime, catalogue).Should().ContainSingle();
        new WorkSessionState().Evaluate(lifetime, catalogue).Should().BeEmpty();
        lifetime.ClaimedLifetimeMilestoneIds.Should().Contain("work.lifetime.total.3");
    }

    [Fact]
    public void FirstEntryGlassesFlag_IsOneShot()
    {
        var progress = new WorkProgressState();

        progress.MarkFirstEntryGlassesGranted().Should().BeTrue();
        progress.MarkFirstEntryGlassesGranted().Should().BeFalse();
        progress.FirstEntryGlassesGranted.Should().BeTrue();
    }
}
