using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class RecoveryClockTests
{
    [Fact]
    public void AssistanceStartsAtExactlyTwoSeconds()
    {
        var clock = new RecoveryClock();

        TickUnable(clock, RecoveryClock.AssistanceDelayTicks - 1);
        Assert.False(clock.State.AssistanceActive);

        clock.Tick(stableStanding: false, conscious: true);
        Assert.True(clock.State.AssistanceActive);
        Assert.Equal(0.0f, clock.State.AssistanceRamp);
    }

    [Fact]
    public void AssistanceRampsToFullOverFiveSeconds()
    {
        var clock = new RecoveryClock();

        TickUnable(clock, RecoveryClock.AssistanceDelayTicks + RecoveryClock.AssistanceRampTicks / 2);
        Assert.Equal(0.5f, clock.State.AssistanceRamp, 3);

        TickUnable(clock, RecoveryClock.AssistanceRampTicks / 2);
        Assert.Equal(1.0f, clock.State.AssistanceRamp);
    }

    [Fact]
    public void HardRecoveryWaitsTenSecondsAfterAssistanceStarts()
    {
        var clock = new RecoveryClock();

        TickUnable(clock,
            RecoveryClock.AssistanceDelayTicks + RecoveryClock.HardRecoveryDelayTicks - 1);
        Assert.False(clock.State.HardRecoveryDue);

        clock.Tick(stableStanding: false, conscious: true);
        Assert.True(clock.State.HardRecoveryDue);
        Assert.Equal(12 * RecoveryClock.PhysicsTicksPerSecond, clock.State.UnableTicks);
    }

    [Fact]
    public void StableStandingResetsAccumulatedRecoveryTime()
    {
        var clock = new RecoveryClock();
        TickUnable(clock, RecoveryClock.AssistanceDelayTicks + 10);

        clock.Tick(stableStanding: true, conscious: true);

        Assert.Equal(default, clock.State);
    }

    [Fact]
    public void UnconsciousTicksCannotBypassNaturalRecoveryDelay()
    {
        var clock = new RecoveryClock();
        TickUnable(clock, RecoveryClock.AssistanceDelayTicks - 1);

        for (int tick = 0; tick < 1_000; tick++)
        {
            clock.Tick(stableStanding: false, conscious: false);
        }

        Assert.Equal(default, clock.State);
        clock.Tick(stableStanding: false, conscious: true);
        Assert.Equal(1, clock.State.UnableTicks);
        Assert.False(clock.State.AssistanceActive);
    }

    private static void TickUnable(RecoveryClock clock, int count)
    {
        for (int tick = 0; tick < count; tick++)
        {
            clock.Tick(stableStanding: false, conscious: true);
        }
    }
}
