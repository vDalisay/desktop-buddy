using DesktopBuddy.Domain.Damage;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Damage;

public sealed class ScorchStateModelTests
{
    private static readonly ScorchConstants Constants = ScorchConstants.Default;

    private static ScorchPhase Burn(ScorchPhase phase, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            phase = ScorchState.Tick(phase, burning: true, Constants).Phase;
        return phase;
    }

    private static ScorchPhase Cool(ScorchPhase phase, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            phase = ScorchState.Tick(phase, burning: false, Constants).Phase;
        return phase;
    }

    [Fact]
    public void TheDefaultsHoldForTenSecondsAndFadeOverFiveAt120Hz()
    {
        Assert.Equal(1200, Constants.HoldTicks);
        Assert.Equal(600, Constants.FadeTicks);
        Assert.Equal(720, Constants.TicksToFullDarkness);
        Assert.True(Constants.IsWellFormed());
    }

    [Fact]
    public void ACeilingBelowOneMeansAPartNeverGoesFullyBlack()
    {
        Assert.True(Constants.MaxDarkness > 0.0f);
        Assert.True(Constants.MaxDarkness < 1.0f);
    }

    [Fact]
    public void APartThatHasNeverBurnedIsCleanSkin()
    {
        Assert.False(ScorchPhase.None.IsMarked);
        Assert.False(ScorchPhase.None.IsHolding);
        Assert.False(ScorchPhase.None.IsFading);
        Assert.Equal(0.0f, ScorchPhase.None.Darkness);
    }

    [Fact]
    public void BurningDarkensProgressivelyAndTheLongerItBurnsTheDarkerItGets()
    {
        ScorchPhase quarter = Burn(ScorchPhase.None, Constants.TicksToFullDarkness / 4);
        ScorchPhase half = Burn(ScorchPhase.None, Constants.TicksToFullDarkness / 2);
        ScorchPhase whole = Burn(ScorchPhase.None, Constants.TicksToFullDarkness);

        Assert.True(quarter.Darkness > 0.0f);
        Assert.True(half.Darkness > quarter.Darkness);
        Assert.True(whole.Darkness > half.Darkness);
        Assert.Equal(Constants.MaxDarkness * 0.25f, quarter.Darkness, 3);
        Assert.Equal(Constants.MaxDarkness * 0.5f, half.Darkness, 3);
        Assert.Equal(Constants.MaxDarkness, whole.Darkness, 3);
    }

    [Fact]
    public void HeldInTheStreamForeverTheDarknessStopsAtTheAuthoredCeiling()
    {
        ScorchPhase phase = Burn(ScorchPhase.None, Constants.TicksToFullDarkness * 6);

        Assert.Equal(Constants.MaxDarkness, phase.Darkness, 4);
        Assert.True(phase.Darkness <= Constants.MaxDarkness);
    }

    [Fact]
    public void TheMarkHoldsAtFullStrengthForTheAuthoredHold()
    {
        ScorchPhase burned = Burn(ScorchPhase.None, Constants.TicksToFullDarkness);
        float darkness = burned.Darkness;

        ScorchPhase phase = burned;
        for (int tick = 0; tick < Constants.HoldTicks; tick++)
        {
            phase = ScorchState.Tick(phase, burning: false, Constants).Phase;
            Assert.Equal(darkness, phase.Darkness, 4);
        }

        Assert.True(phase.IsFading);
        Assert.Equal(Constants.FadeTicks, phase.FadeTicksRemaining);
    }

    [Fact]
    public void AfterTheHoldTheMarkFadesToCleanSkinInExactlyTheAuthoredFade()
    {
        ScorchPhase phase = Burn(ScorchPhase.None, Constants.TicksToFullDarkness);
        phase = Cool(phase, Constants.HoldTicks);

        float previous = phase.Darkness;
        int cleared = 0;
        for (int tick = 0; tick < Constants.FadeTicks; tick++)
        {
            ScorchTickResult result = ScorchState.Tick(phase, burning: false, Constants);
            if (result.Cleared)
                cleared++;
            Assert.True(result.Phase.Darkness <= previous);
            previous = result.Phase.Darkness;
            phase = result.Phase;
        }

        Assert.Equal(1, cleared);
        Assert.False(phase.IsMarked);
        Assert.Equal(0.0f, phase.Darkness);
    }

    [Fact]
    public void ALightMarkAndAFullOneBothTakeTheSameAuthoredTimeToFade()
    {
        static int TicksToClean(int burnTicks)
        {
            ScorchPhase phase = Burn(ScorchPhase.None, burnTicks);
            for (int tick = 1; tick <= 10_000; tick++)
            {
                ScorchTickResult result = ScorchState.Tick(phase, burning: false, Constants);
                phase = result.Phase;
                if (result.Cleared)
                    return tick;
            }

            return -1;
        }

        int light = TicksToClean(Constants.TicksToFullDarkness / 8);
        int full = TicksToClean(Constants.TicksToFullDarkness);

        Assert.Equal(Constants.HoldTicks + Constants.FadeTicks, light);
        Assert.Equal(light, full);
    }

    [Fact]
    public void CatchingFireAgainReArmsTheHoldAndAbandonsARunningFade()
    {
        ScorchPhase phase = Burn(ScorchPhase.None, Constants.TicksToFullDarkness);
        phase = Cool(phase, Constants.HoldTicks + (Constants.FadeTicks / 2));
        Assert.True(phase.IsFading);
        float faded = phase.Darkness;
        Assert.True(faded < Constants.MaxDarkness);

        ScorchPhase relit = ScorchState.Tick(phase, burning: true, Constants).Phase;

        Assert.True(relit.Darkness > faded);
        Assert.Equal(Constants.HoldTicks, relit.HoldTicksRemaining);
        Assert.Equal(0, relit.FadeTicksRemaining);
        Assert.False(relit.IsFading);
        Assert.True(relit.IsHolding);
    }

    [Fact]
    public void ASecondBurnStacksOnTopOfWhatTheFirstOneLeft()
    {
        ScorchPhase once = Burn(ScorchPhase.None, Constants.TicksToFullDarkness / 4);
        ScorchPhase held = Cool(once, Constants.HoldTicks / 2);
        ScorchPhase twice = Burn(held, Constants.TicksToFullDarkness / 4);

        Assert.Equal(Constants.MaxDarkness * 0.5f, twice.Darkness, 3);
    }

    [Fact]
    public void CleanSkinStaysCleanAndTickingItIsIdempotent()
    {
        ScorchPhase phase = ScorchPhase.None;
        for (int tick = 0; tick < 600; tick++)
        {
            ScorchTickResult result = ScorchState.Tick(phase, burning: false, Constants);
            Assert.True(result.IsValid);
            Assert.False(result.Cleared);
            Assert.Equal(ScorchPhase.None, result.Phase);
            phase = result.Phase;
        }
    }

    [Fact]
    public void ClearWipesTheMarkImmediatelyAndIsIdempotent()
    {
        ScorchPhase phase = Burn(ScorchPhase.None, Constants.TicksToFullDarkness);
        Assert.True(phase.IsMarked);

        ScorchPhase cleared = ScorchState.Clear(phase);
        Assert.False(cleared.IsMarked);
        Assert.Equal(ScorchPhase.None, cleared);
        Assert.Equal(cleared, ScorchState.Clear(cleared));

        // And it stays clean: the wipe is not a fade that could bounce back.
        Assert.Equal(ScorchPhase.None, Cool(cleared, 600));
    }

    [Fact]
    public void TheWholeLifecycleIsDeterministicAcrossRepeatedRuns()
    {
        static (ScorchPhase Phase, int Clears) Run()
        {
            ScorchPhase phase = ScorchPhase.None;
            int clears = 0;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                phase = Burn(phase, 480);
                for (int tick = 0; tick < Constants.HoldTicks + Constants.FadeTicks; tick++)
                {
                    ScorchTickResult result = ScorchState.Tick(phase, burning: false, Constants);
                    if (result.Cleared)
                        clears++;
                    phase = result.Phase;
                }
            }

            return (phase, clears);
        }

        (ScorchPhase first, int firstClears) = Run();
        (ScorchPhase second, int secondClears) = Run();

        Assert.Equal(first, second);
        Assert.Equal(firstClears, secondClears);
        Assert.Equal(3, firstClears);
        Assert.False(first.IsMarked);
    }

    [Theory]
    [InlineData(0, 0.72f, 1200, 600)]
    [InlineData(-1, 0.72f, 1200, 600)]
    [InlineData(720, 0.0f, 1200, 600)]
    [InlineData(720, 1.5f, 1200, 600)]
    [InlineData(720, 0.72f, -1, 600)]
    [InlineData(720, 0.72f, 1200, 0)]
    public void IllFormedConstantsMakeTheModelInert(
        int ticksToFull,
        float maxDarkness,
        int holdTicks,
        int fadeTicks)
    {
        var constants = new ScorchConstants(ticksToFull, maxDarkness, holdTicks, fadeTicks);
        Assert.False(constants.IsWellFormed());

        ScorchTickResult result = ScorchState.Tick(ScorchPhase.None, burning: true, constants);
        Assert.False(result.IsValid);
        Assert.Equal(ScorchPhase.None, result.Phase);
    }
}
