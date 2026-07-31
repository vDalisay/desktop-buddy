using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Tools;

public sealed class GrenadeFuseModelTests
{
    private static readonly GrenadeFuseConstants Constants = GrenadeFuseConstants.Default;

    private static GrenadeFuseResult Tick(
        GrenadeFusePhase phase,
        bool pinPull = false,
        bool held = false) =>
        GrenadeFuseMachine.Tick(
            new GrenadeFuseInput(phase, pinPull, held, Constants));

    [Fact]
    public void AFreshGrenadeIsPinnedAndTheDefaultFuseIsThreeSecondsAtOneTwentyHertz()
    {
        GrenadeFusePhase fresh = GrenadeFusePhase.Fresh;

        Assert.Equal(GrenadeFuseStage.Pinned, fresh.Stage);
        Assert.False(fresh.PinIsOut);
        Assert.False(fresh.IsCountingDown);
        Assert.Equal(360, Constants.FuseTicks);
        Assert.True(Constants.IsWellFormed());
    }

    [Fact]
    public void APinnedGrenadeNeverExplodesHoweverLongItIsLeftAlone()
    {
        GrenadeFusePhase phase = GrenadeFusePhase.Fresh;

        // Thrown by plain grab: nobody holds it, and no pin was ever pulled.
        for (int tick = 0; tick < Constants.FuseTicks * 3; tick++)
        {
            GrenadeFuseResult result = Tick(phase);
            Assert.False(result.FuseStarted);
            Assert.False(result.Detonated);
            phase = result.Phase;
        }

        Assert.Equal(GrenadeFuseStage.Pinned, phase.Stage);
    }

    [Fact]
    public void TheFirstSecondaryPressPullsThePinAndOnlyWhileItIsHeld()
    {
        GrenadeFuseResult airborne = Tick(GrenadeFusePhase.Fresh, pinPull: true, held: false);
        Assert.False(airborne.PinPulled);
        Assert.Equal(GrenadeFuseStage.Pinned, airborne.Phase.Stage);

        GrenadeFuseResult pulled = Tick(GrenadeFusePhase.Fresh, pinPull: true, held: true);
        Assert.True(pulled.PinPulled);
        Assert.Equal(GrenadeFuseStage.PinPulled, pulled.Phase.Stage);
        Assert.True(pulled.Phase.PinIsOut);
        Assert.False(pulled.FuseStarted);
    }

    [Fact]
    public void ThePinOnlyComesOutOnceAndNeverGoesBackIn()
    {
        GrenadeFusePhase phase = Tick(GrenadeFusePhase.Fresh, pinPull: true, held: true).Phase;

        // Cancelling and re-beginning the pullback presses secondary again; the second
        // press must not spawn a second pin or restart anything.
        for (int press = 0; press < 5; press++)
        {
            GrenadeFuseResult result = Tick(phase, pinPull: true, held: true);
            Assert.False(result.PinPulled);
            Assert.Equal(GrenadeFuseStage.PinPulled, result.Phase.Stage);
            phase = result.Phase;
        }
    }

    [Fact]
    public void APinPulledGrenadeIsSafeForAsLongAsThePlayerHoldsIt()
    {
        GrenadeFusePhase phase = Tick(GrenadeFusePhase.Fresh, pinPull: true, held: true).Phase;

        // Six seconds in the hand, twice the fuse.
        for (int tick = 0; tick < Constants.FuseTicks * 2; tick++)
        {
            GrenadeFuseResult result = Tick(phase, held: true);
            Assert.False(result.FuseStarted);
            Assert.False(result.Detonated);
            phase = result.Phase;
        }

        Assert.Equal(GrenadeFuseStage.PinPulled, phase.Stage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFuseRunsExactlyThreeHundredAndSixtyTicksFromEitherReleasePath(bool viaCancelledPullback)
    {
        GrenadeFusePhase phase = Tick(GrenadeFusePhase.Fresh, pinPull: true, held: true).Phase;
        if (viaCancelledPullback)
        {
            // The pullback was cancelled and the grenade went back to being merely
            // grabbed. Still held, still safe, and the release below is a grab release
            // rather than a launch release.
            for (int tick = 0; tick < 30; tick++)
                phase = Tick(phase, held: true).Phase;
        }

        GrenadeFuseResult started = Tick(phase, held: false);
        Assert.True(started.FuseStarted);
        Assert.False(started.Detonated);
        Assert.Equal(GrenadeFuseStage.Live, started.Phase.Stage);
        Assert.True(started.Phase.IsCountingDown);
        Assert.Equal(Constants.FuseTicks, started.Phase.TicksRemaining);
        phase = started.Phase;

        for (int tick = 1; tick < Constants.FuseTicks; tick++)
        {
            GrenadeFuseResult result = Tick(phase);
            Assert.False(result.Detonated);
            Assert.Equal(Constants.FuseTicks - tick, result.Phase.TicksRemaining);
            phase = result.Phase;
        }

        GrenadeFuseResult blast = Tick(phase);
        Assert.True(blast.Detonated);
        Assert.Equal(GrenadeFuseStage.Detonated, blast.Phase.Stage);
        Assert.Equal(0, blast.Phase.TicksRemaining);
    }

    [Fact]
    public void ReGrabbingALiveGrenadeDoesNotPauseOrResetTheFuse()
    {
        GrenadeFusePhase phase = Tick(GrenadeFusePhase.Fresh, pinPull: true, held: true).Phase;
        phase = Tick(phase, held: false).Phase;

        int detonatedOn = 0;
        for (int tick = 1; tick <= Constants.FuseTicks; tick++)
        {
            // Caught, carried, thrown again, caught again: held or not, the countdown is
            // the same countdown. It goes off in whoever's hand holds it.
            bool held = tick % 3 == 0;
            GrenadeFuseResult result = Tick(phase, pinPull: held, held: held);
            phase = result.Phase;
            if (result.Detonated)
            {
                detonatedOn = tick;
                break;
            }
        }

        Assert.Equal(Constants.FuseTicks, detonatedOn);
    }

    [Fact]
    public void DetonatedIsTerminalAndTickingASpentGrenadeIsIdempotent()
    {
        GrenadeFusePhase phase = new(GrenadeFuseStage.Live, 1);
        GrenadeFuseResult blast = Tick(phase);
        Assert.True(blast.Detonated);
        phase = blast.Phase;

        for (int tick = 0; tick < 200; tick++)
        {
            GrenadeFuseResult result = Tick(phase, pinPull: true, held: true);
            Assert.True(result.IsValid);
            Assert.False(result.Detonated);
            Assert.False(result.PinPulled);
            Assert.False(result.FuseStarted);
            Assert.Equal(GrenadeFuseStage.Detonated, result.Phase.Stage);
            phase = result.Phase;
        }
    }

    [Fact]
    public void AnIllFormedFuseIsInertAndLeavesThePhaseAlone()
    {
        var broken = new GrenadeFuseConstants(FuseTicks: 0);
        Assert.False(broken.IsWellFormed());

        var phase = new GrenadeFusePhase(GrenadeFuseStage.PinPulled, 0);
        GrenadeFuseResult result = GrenadeFuseMachine.Tick(
            new GrenadeFuseInput(phase, PinPullRequested: true, PlayerControlled: false, broken));

        Assert.False(result.IsValid);
        Assert.False(result.FuseStarted);
        Assert.False(result.Detonated);
        Assert.Equal(phase, result.Phase);
    }

    [Fact]
    public void TheSameSequenceTwiceProducesIdenticalStates()
    {
        static GrenadeFusePhase[] Run()
        {
            var states = new GrenadeFusePhase[500];
            GrenadeFusePhase phase = GrenadeFusePhase.Fresh;
            for (int tick = 0; tick < states.Length; tick++)
            {
                bool pull = tick == 12;
                bool held = tick < 40;
                phase = GrenadeFuseMachine.Tick(
                    new GrenadeFuseInput(phase, pull, held, Constants)).Phase;
                states[tick] = phase;
            }

            return states;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ThePinPulledStageIsTheOnlyOneThatCanStartAFuse()
    {
        // The whole transition table, stated once: from every stage, with every input,
        // the machine lands where §2.1 says it does.
        Assert.Equal(
            GrenadeFuseStage.Pinned,
            Tick(GrenadeFusePhase.Fresh, pinPull: false, held: true).Phase.Stage);
        Assert.Equal(
            GrenadeFuseStage.Pinned,
            Tick(GrenadeFusePhase.Fresh, pinPull: false, held: false).Phase.Stage);
        Assert.Equal(
            GrenadeFuseStage.Pinned,
            Tick(GrenadeFusePhase.Fresh, pinPull: true, held: false).Phase.Stage);
        Assert.Equal(
            GrenadeFuseStage.PinPulled,
            Tick(GrenadeFusePhase.Fresh, pinPull: true, held: true).Phase.Stage);

        var pinPulled = new GrenadeFusePhase(GrenadeFuseStage.PinPulled, 0);
        Assert.Equal(
            GrenadeFuseStage.PinPulled,
            Tick(pinPulled, pinPull: true, held: true).Phase.Stage);
        Assert.Equal(GrenadeFuseStage.Live, Tick(pinPulled, held: false).Phase.Stage);

        var live = new GrenadeFusePhase(GrenadeFuseStage.Live, 5);
        Assert.Equal(GrenadeFuseStage.Live, Tick(live, held: true).Phase.Stage);
        Assert.Equal(4, Tick(live, held: true).Phase.TicksRemaining);
        Assert.Equal(
            GrenadeFuseStage.Detonated,
            Tick(new GrenadeFusePhase(GrenadeFuseStage.Live, 1)).Phase.Stage);
    }
}
