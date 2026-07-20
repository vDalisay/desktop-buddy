using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Autonomy;

public sealed class AutonomousMotionPlannerTests
{
    private static readonly AutonomousMotionTuning Tuning = new(
        MinimumIdleTicks: 2,
        MaximumIdleTicks: 5,
        MinimumWalkTicks: 3,
        MaximumWalkTicks: 7,
        MinimumJumpIntervalTicks: 4,
        MaximumJumpIntervalTicks: 9,
        IdleWeight: 2,
        WalkLeftWeight: 3,
        WalkRightWeight: 3);

    [Fact]
    public void SameSeedProducesSameDecisionTrace()
    {
        var first = new AutonomousMotionPlanner(new SeededRandomSource(8675309), Tuning);
        var second = new AutonomousMotionPlanner(new SeededRandomSource(8675309), Tuning);

        Assert.Equal(Capture(first, 500), Capture(second, 500));
    }

    [Fact]
    public void DifferentSeedsProduceDifferentDecisionTraces()
    {
        var first = new AutonomousMotionPlanner(new SeededRandomSource(10), Tuning);
        var second = new AutonomousMotionPlanner(new SeededRandomSource(11), Tuning);

        Assert.NotEqual(Capture(first, 100), Capture(second, 100));
    }

    [Fact]
    public void SuppressionPausesPlannerAndProducesNoActuation()
    {
        var paused = new AutonomousMotionPlanner(new SeededRandomSource(42), Tuning);
        var reference = new AutonomousMotionPlanner(new SeededRandomSource(42), Tuning);

        for (int tick = 0; tick < 25; tick++)
        {
            AutonomousMotionIntent intent = paused.Tick(enabled: false, canWalk: true, canJump: true);
            Assert.True(intent.IsSuppressed);
            Assert.Equal(0.0f, intent.WalkDirection);
            Assert.False(intent.JumpRequested);
        }

        Assert.Equal(Capture(reference, 100), Capture(paused, 100));
    }

    [Fact]
    public void DueJumpWaitsForAValidSupportState()
    {
        var planner = new AutonomousMotionPlanner(new SeededRandomSource(99), Tuning);

        for (int tick = 0; tick < Tuning.MaximumJumpIntervalTicks + 1; tick++)
        {
            Assert.False(planner.Tick(enabled: true, canWalk: true, canJump: false).JumpRequested);
        }

        AutonomousMotionIntent jump = planner.Tick(enabled: true, canWalk: true, canJump: true);
        Assert.True(jump.JumpRequested);
        Assert.InRange(
            planner.JumpTicksRemaining,
            Tuning.MinimumJumpIntervalTicks,
            Tuning.MaximumJumpIntervalTicks);
    }

    [Fact]
    public void MissingWalkSupportSuppressesDirectionWithoutDiscardingGoal()
    {
        var planner = new AutonomousMotionPlanner(new SeededRandomSource(7), Tuning);

        for (int tick = 0; tick < 100; tick++)
        {
            AutonomousMotionIntent intent = planner.Tick(enabled: true, canWalk: false, canJump: false);
            Assert.Equal(0.0f, intent.WalkDirection);
            Assert.False(intent.IsSuppressed);
        }
    }

    [Fact]
    public void AmbientJumpsEnabled_ByDefault_StillJumps()
    {
        var planner = new AutonomousMotionPlanner(new SeededRandomSource(3), Tuning);
        List<AutonomousMotionIntent> trace = Capture(planner, 200);
        Assert.Contains(trace, intent => intent.JumpRequested);
    }

    [Fact]
    public void AmbientJumpsDisabled_NeverRequestsAJump()
    {
        var planner = new AutonomousMotionPlanner(
            new SeededRandomSource(3), Tuning with { AmbientJumpsEnabled = false });
        List<AutonomousMotionIntent> trace = Capture(planner, 2000);
        Assert.DoesNotContain(trace, intent => intent.JumpRequested);
    }

    [Fact]
    public void AmbientJumpsDisabled_StillWalksAndIdles()
    {
        // Disabling the jump timer must not stall ambient locomotion.
        var planner = new AutonomousMotionPlanner(
            new SeededRandomSource(3), Tuning with { AmbientJumpsEnabled = false });
        List<AutonomousMotionIntent> trace = Capture(planner, 500);
        Assert.Contains(trace, intent => intent.WalkDirection < 0.0f);
        Assert.Contains(trace, intent => intent.WalkDirection > 0.0f);
        Assert.Contains(trace, intent => intent.Goal == AutonomousMotionGoal.Idle);
    }

    private static List<AutonomousMotionIntent> Capture(AutonomousMotionPlanner planner, int ticks)
    {
        var trace = new List<AutonomousMotionIntent>(ticks);
        for (int tick = 0; tick < ticks; tick++)
        {
            trace.Add(planner.Tick(enabled: true, canWalk: true, canJump: true));
        }

        return trace;
    }
}
