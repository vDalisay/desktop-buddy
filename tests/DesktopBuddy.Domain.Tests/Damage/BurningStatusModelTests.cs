using DesktopBuddy.Domain.Damage;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Damage;

public sealed class BurningStatusModelTests
{
    private static readonly BurningConstants Constants = BurningConstants.Default;

    private static BurningPhase Ignite() =>
        BurningStatus.Apply(BurningPhase.None, Constants).Phase;

    [Fact]
    public void TheDefaultsAreFourSecondsApplied_EightCapped_AndAHalfSecondCadenceAt120Hz()
    {
        Assert.Equal(480, Constants.ApplyTicks);
        Assert.Equal(960, Constants.CapTicks);
        Assert.Equal(60, Constants.PainIntervalTicks);
        Assert.True(Constants.IsWellFormed());
    }

    [Fact]
    public void ABuddyThatWasNeverSprayedIsNotBurning()
    {
        Assert.False(BurningPhase.None.IsBurning);
        Assert.Equal(0, BurningPhase.None.TicksRemaining);
        Assert.Equal(0, BurningPhase.None.Episode);
    }

    [Fact]
    public void AFreshContactGrantsExactlyTheAppliedDurationAndCountsAsAnIgnition()
    {
        BurningApplyResult result = BurningStatus.Apply(BurningPhase.None, Constants);

        Assert.True(result.IsValid);
        Assert.True(result.Ignited);
        Assert.True(result.Phase.IsBurning);
        Assert.Equal(Constants.ApplyTicks, result.Phase.TicksRemaining);
        Assert.Equal(0, result.Phase.TicksSincePainEvent);
        Assert.Equal(1, result.Phase.Episode);
    }

    [Fact]
    public void AMidBurnRefreshAddsToWhatIsLeftAndIsNotAFreshIgnition()
    {
        BurningPhase phase = Ignite();
        for (int tick = 0; tick < 120; tick++)
            phase = BurningStatus.Tick(phase, Constants).Phase;

        Assert.Equal(Constants.ApplyTicks - 120, phase.TicksRemaining);

        BurningApplyResult refreshed = BurningStatus.Apply(phase, Constants);

        Assert.False(refreshed.Ignited);
        Assert.Equal(Constants.ApplyTicks - 120 + Constants.ApplyTicks, refreshed.Phase.TicksRemaining);
        Assert.Equal(1, refreshed.Phase.Episode);
    }

    [Fact]
    public void SustainedContactPinsTheRemainingDurationAtTheCapAndNeverAboveIt()
    {
        BurningPhase phase = BurningPhase.None;
        for (int tick = 0; tick < 1200; tick++)
        {
            phase = BurningStatus.Apply(phase, Constants).Phase;
            Assert.True(phase.TicksRemaining <= Constants.CapTicks);
            phase = BurningStatus.Tick(phase, Constants).Phase;
        }

        Assert.Equal(Constants.CapTicks - 1, phase.TicksRemaining);
    }

    [Fact]
    public void ARefreshAtTheCapIsIdempotentOnTheRemainingDuration()
    {
        BurningPhase phase = Ignite();
        phase = BurningStatus.Apply(phase, Constants).Phase;
        Assert.Equal(Constants.CapTicks, phase.TicksRemaining);

        BurningApplyResult again = BurningStatus.Apply(phase, Constants);

        Assert.False(again.Ignited);
        Assert.Equal(Constants.CapTicks, again.Phase.TicksRemaining);
    }

    [Fact]
    public void TheFirstPainEventLandsOneFullIntervalAfterIgnitionAndNotOnContact()
    {
        BurningPhase phase = Ignite();

        for (int tick = 1; tick < Constants.PainIntervalTicks; tick++)
        {
            BurningTickResult result = BurningStatus.Tick(phase, Constants);
            Assert.False(result.PainEventDue);
            phase = result.Phase;
        }

        BurningTickResult due = BurningStatus.Tick(phase, Constants);
        Assert.True(due.PainEventDue);
        Assert.Equal(0, due.Phase.TicksSincePainEvent);
    }

    [Fact]
    public void AFourSecondBurnPaysExactlyEightEventsAndThenExpires()
    {
        BurningPhase phase = Ignite();
        int events = 0;
        int expiries = 0;
        for (int tick = 0; tick < Constants.ApplyTicks; tick++)
        {
            BurningTickResult result = BurningStatus.Tick(phase, Constants);
            if (result.PainEventDue)
                events++;
            if (result.Expired)
                expiries++;
            phase = result.Phase;
        }

        Assert.Equal(Constants.ApplyTicks / Constants.PainIntervalTicks, events);
        Assert.Equal(1, expiries);
        Assert.False(phase.IsBurning);
        Assert.Equal(0, phase.TicksRemaining);
    }

    [Fact]
    public void ExpiryIsExactAndTickingABurntOutBuddyIsIdempotentAndSilent()
    {
        BurningPhase phase = Ignite();
        for (int tick = 0; tick < Constants.ApplyTicks; tick++)
            phase = BurningStatus.Tick(phase, Constants).Phase;

        for (int tick = 0; tick < 300; tick++)
        {
            BurningTickResult result = BurningStatus.Tick(phase, Constants);
            Assert.True(result.IsValid);
            Assert.False(result.PainEventDue);
            Assert.False(result.Expired);
            Assert.Equal(phase, result.Phase);
            phase = result.Phase;
        }
    }

    [Fact]
    public void ARefreshDoesNotPushTheNextPainEventAwaySoAHeldStreamStillCosts()
    {
        BurningPhase phase = Ignite();
        int events = 0;
        // A player holding the stream on the buddy: contact every single tick.
        for (int tick = 0; tick < Constants.PainIntervalTicks * 4; tick++)
        {
            phase = BurningStatus.Apply(phase, Constants).Phase;
            BurningTickResult result = BurningStatus.Tick(phase, Constants);
            if (result.PainEventDue)
                events++;
            phase = result.Phase;
        }

        Assert.Equal(4, events);
    }

    [Fact]
    public void ClearingMidIntervalPutsTheBurnOutImmediatelyAndIsIdempotent()
    {
        BurningPhase phase = Ignite();
        for (int tick = 0; tick < 45; tick++)
            phase = BurningStatus.Tick(phase, Constants).Phase;

        BurningPhase cleared = BurningStatus.Clear(phase);
        Assert.False(cleared.IsBurning);
        Assert.Equal(0, cleared.TicksRemaining);
        Assert.Equal(0, cleared.TicksSincePainEvent);
        // The episode survives, so a relit buddy is attributed as a new burn.
        Assert.Equal(phase.Episode, cleared.Episode);

        Assert.Equal(cleared, BurningStatus.Clear(cleared));

        BurningTickResult after = BurningStatus.Tick(cleared, Constants);
        Assert.False(after.PainEventDue);
        Assert.False(after.Expired);
    }

    [Fact]
    public void RelightingAfterALapseMintsANewEpisode()
    {
        BurningPhase phase = Ignite();
        Assert.Equal(1, phase.Episode);
        for (int tick = 0; tick < Constants.ApplyTicks; tick++)
            phase = BurningStatus.Tick(phase, Constants).Phase;

        BurningApplyResult relit = BurningStatus.Apply(phase, Constants);
        Assert.True(relit.Ignited);
        Assert.Equal(2, relit.Phase.Episode);
        Assert.Equal(Constants.ApplyTicks, relit.Phase.TicksRemaining);
    }

    [Fact]
    public void ManyCapCyclesStayDeterministicAndBounded()
    {
        static (BurningPhase Phase, int Events) Run()
        {
            BurningPhase phase = BurningPhase.None;
            int events = 0;
            for (int cycle = 0; cycle < 6; cycle++)
            {
                for (int tick = 0; tick < Constants.CapTicks; tick++)
                {
                    phase = BurningStatus.Apply(phase, Constants).Phase;
                    Assert.True(phase.TicksRemaining <= Constants.CapTicks);
                    BurningTickResult result = BurningStatus.Tick(phase, Constants);
                    if (result.PainEventDue)
                        events++;
                    phase = result.Phase;
                }

                // Let it burn all the way out before the next cycle relights it.
                for (int tick = 0; tick < Constants.CapTicks; tick++)
                {
                    BurningTickResult result = BurningStatus.Tick(phase, Constants);
                    if (result.PainEventDue)
                        events++;
                    phase = result.Phase;
                }

                Assert.False(phase.IsBurning);
            }

            return (phase, events);
        }

        (BurningPhase firstPhase, int firstEvents) = Run();
        (BurningPhase secondPhase, int secondEvents) = Run();

        Assert.Equal(firstPhase, secondPhase);
        Assert.Equal(firstEvents, secondEvents);
        Assert.Equal(6, firstPhase.Episode);
        // Six cycles, each 960 sprayed ticks plus a full cap burning out: 16 events while
        // the cap is pinned, and 15 on the way down from 959 remaining.
        Assert.Equal(6 * 31, firstEvents);
    }

    [Theory]
    [InlineData(0, 960, 60)]
    [InlineData(-1, 960, 60)]
    [InlineData(480, 240, 60)]
    [InlineData(480, 960, 0)]
    [InlineData(480, 960, -5)]
    public void IllFormedConstantsMakeTheModelInert(int apply, int cap, int interval)
    {
        var constants = new BurningConstants(apply, cap, interval);
        Assert.False(constants.IsWellFormed());

        BurningApplyResult applied = BurningStatus.Apply(BurningPhase.None, constants);
        Assert.False(applied.IsValid);
        Assert.False(applied.Ignited);
        Assert.Equal(BurningPhase.None, applied.Phase);

        BurningTickResult ticked = BurningStatus.Tick(Ignite(), constants);
        Assert.False(ticked.IsValid);
        Assert.False(ticked.PainEventDue);
    }
}
