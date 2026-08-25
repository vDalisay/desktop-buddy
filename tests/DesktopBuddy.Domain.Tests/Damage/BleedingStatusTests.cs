using DesktopBuddy.Domain.Damage;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Damage;

public sealed class BleedingStatusTests
{
    private static readonly BleedingConstants Constants = BleedingConstants.Default;

    private static BleedWound Wound(float severity = 1.0f) =>
        BleedingStatus.Open(BleedWound.None, severity, Constants).Wound;

    private static BleedWound Advance(BleedWound wound, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            wound = BleedingStatus.Tick(wound, Constants).Wound;
        }

        return wound;
    }

    [Fact]
    public void TheDefaultsAreSixSecondsPerWound_FifteenCapped_AtOneTwentyHertz()
    {
        Assert.Equal(720, Constants.WoundTicks);
        Assert.Equal(1800, Constants.CapTicks);
        // 0.3 s to 1 s between drops. Slowed from 0.15-0.6 s, which read as a running tap
        // and cost a drawing node per drop (owner report 2026-08-25).
        Assert.Equal(36, Constants.DripIntervalTicks);
        Assert.Equal(120, Constants.SlowestDripIntervalTicks);
        Assert.True(Constants.IsWellFormed());
    }

    [Theory]
    [InlineData(0, 1800, 18, 72)]      // no duration
    [InlineData(720, 600, 18, 72)]     // cap below one wound
    [InlineData(720, 1800, 0, 72)]     // no cadence
    [InlineData(720, 1800, 72, 18)]    // slowest faster than fastest
    public void IllFormedConstantsAreRejectedRatherThanClamped(
        int woundTicks, int capTicks, int dripTicks, int slowestTicks)
    {
        var constants = new BleedingConstants(woundTicks, capTicks, dripTicks, slowestTicks);
        Assert.False(constants.IsWellFormed());

        BleedOpenResult opened = BleedingStatus.Open(BleedWound.None, 1.0f, constants);
        Assert.False(opened.IsValid);
        Assert.False(opened.Opened);
        Assert.Equal(BleedWound.None, opened.Wound);

        BleedTickResult ticked = BleedingStatus.Tick(Wound(), constants);
        Assert.False(ticked.IsValid);
        Assert.False(ticked.DripDue);
    }

    [Fact]
    public void AnUnhurtPartIsNotBleeding()
    {
        Assert.False(BleedWound.None.IsBleeding);
        Assert.Equal(0, BleedWound.None.Episode);
        Assert.Equal(0.0f, BleedWound.None.Intensity(Constants));
    }

    [Fact]
    public void AFreshHitGrantsExactlyTheWoundDurationAndCountsAsAnOpening()
    {
        BleedOpenResult result = BleedingStatus.Open(BleedWound.None, 1.0f, Constants);

        Assert.True(result.IsValid);
        Assert.True(result.Opened);
        Assert.True(result.Wound.IsBleeding);
        Assert.Equal(Constants.WoundTicks, result.Wound.TicksRemaining);
        Assert.Equal(0, result.Wound.TicksSinceDrip);
        Assert.Equal(1, result.Wound.Episode);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ANonPositiveOrNonFiniteSeverityOpensNothing(float severity)
    {
        BleedOpenResult result = BleedingStatus.Open(BleedWound.None, severity, Constants);

        Assert.False(result.IsValid);
        Assert.False(result.Opened);
        Assert.False(result.Wound.IsBleeding);
    }

    [Fact]
    public void SeverityIsClampedToOneSoAnOverEagerCallerCannotOpenAFirehose()
    {
        Assert.Equal(1.0f, BleedingStatus.Open(BleedWound.None, 40.0f, Constants).Wound.Severity);
    }

    [Fact]
    public void ASecondHitRefreshesTheWoundAndIsNotAnOpening()
    {
        BleedWound wound = Advance(Wound(), 200);
        BleedOpenResult again = BleedingStatus.Open(wound, 1.0f, Constants);

        Assert.True(again.IsValid);
        Assert.False(again.Opened);
        Assert.Equal(wound.TicksRemaining + Constants.WoundTicks, again.Wound.TicksRemaining);
        Assert.Equal(2, again.Wound.Episode);
    }

    [Fact]
    public void RepeatedHitsPinTheWoundAtTheCapRatherThanBleedingForever()
    {
        BleedWound wound = BleedWound.None;
        for (int i = 0; i < 20; i++)
        {
            wound = BleedingStatus.Open(wound, 1.0f, Constants).Wound;
        }

        Assert.Equal(Constants.CapTicks, wound.TicksRemaining);
    }

    [Fact]
    public void SeverityOnlyEverRises()
    {
        BleedWound deep = Wound(0.9f);
        Assert.Equal(0.9f, BleedingStatus.Open(deep, 0.2f, Constants).Wound.Severity, 5);
        Assert.Equal(0.95f, BleedingStatus.Open(deep, 0.95f, Constants).Wound.Severity, 5);
    }

    [Fact]
    public void ARefreshDoesNotRestartTheCadenceSoRepeatedHitsCannotSuppressEveryDrip()
    {
        // One tick short of a drip, then hit again: the drip must still land next tick.
        int interval = BleedingStatus.DripIntervalFor(Wound(), Constants);
        BleedWound wound = Advance(Wound(), interval - 1);
        Assert.Equal(interval - 1, wound.TicksSinceDrip);

        BleedWound hitAgain = BleedingStatus.Open(wound, 1.0f, Constants).Wound;
        Assert.Equal(interval - 1, hitAgain.TicksSinceDrip);
        Assert.True(BleedingStatus.Tick(hitAgain, Constants).DripDue);
    }

    /// <summary>
    /// The cadence is recomputed each tick from what is left of the wound, so a cycle is
    /// bounded by the two authored intervals rather than equal to either: it starts near
    /// the fast one and drifts toward the slow one as the wound closes. Asserting a fixed
    /// count here would be asserting the drift, not the rule.
    /// </summary>
    [Fact]
    public void TheFirstDripLandsNoSoonerThanTheFastIntervalAndNoLaterThanTheSlow()
    {
        BleedWound wound = Wound();

        for (int i = 1; i < Constants.DripIntervalTicks; i++)
        {
            BleedTickResult early = BleedingStatus.Tick(wound, Constants);
            Assert.False(early.DripDue);
            wound = early.Wound;
        }

        int extra = Constants.SlowestDripIntervalTicks - Constants.DripIntervalTicks + 1;
        for (int i = 0; i < extra; i++)
        {
            BleedTickResult result = BleedingStatus.Tick(wound, Constants);
            wound = result.Wound;
            if (result.DripDue)
            {
                return;
            }
        }

        Assert.Fail("A fresh wound must drip within the slowest authored interval.");
    }

    [Fact]
    public void AWoundKeepsDrippingForAsLongAsItLastsRatherThanStallingAsItTapers()
    {
        BleedWound wound = Wound();
        int drips = 0;
        for (int i = 0; i < Constants.WoundTicks; i++)
        {
            BleedTickResult result = BleedingStatus.Tick(wound, Constants);
            wound = result.Wound;
            if (result.DripDue)
            {
                drips++;
            }
        }

        // Six seconds between the 0.3 s and 1 s cadences can never yield fewer than six.
        Assert.InRange(drips, 6, Constants.WoundTicks / Constants.DripIntervalTicks);
    }

    [Fact]
    public void AFullStrengthWoundDripsAtTheFastCadenceAndAClosingOneAtTheSlow()
    {
        Assert.Equal(Constants.DripIntervalTicks, BleedingStatus.DripIntervalFor(Wound(), Constants));

        // Nearly run out: intensity approaches zero and the cadence approaches the slowest.
        BleedWound closing = Advance(Wound(), Constants.WoundTicks - 1);
        Assert.Equal(Constants.SlowestDripIntervalTicks, BleedingStatus.DripIntervalFor(closing, Constants));
    }

    [Fact]
    public void IntensityTapersWithWhatIsLeftAndScalesWithSeverity()
    {
        Assert.Equal(1.0f, Wound().Intensity(Constants), 3);
        Assert.Equal(0.5f, Wound(0.5f).Intensity(Constants), 3);

        BleedWound half = Advance(Wound(), Constants.WoundTicks / 2);
        Assert.Equal(0.5f, half.Intensity(Constants), 2);
    }

    [Fact]
    public void IntensityNeverExceedsSeverityEvenWhenARefreshPushesPastOneWound()
    {
        BleedWound stacked = BleedingStatus.Open(Wound(), 1.0f, Constants).Wound;
        Assert.True(stacked.TicksRemaining > Constants.WoundTicks);
        Assert.Equal(1.0f, stacked.Intensity(Constants), 3);
    }

    [Fact]
    public void AWoundRunsOutExactlyOnceAndThenTicksIdempotently()
    {
        BleedWound wound = Advance(Wound(), Constants.WoundTicks - 1);
        Assert.True(wound.IsBleeding);

        BleedTickResult last = BleedingStatus.Tick(wound, Constants);
        Assert.True(last.Expired);
        Assert.False(last.Wound.IsBleeding);
        Assert.Equal(0, last.Wound.TicksSinceDrip);

        BleedTickResult after = BleedingStatus.Tick(last.Wound, Constants);
        Assert.True(after.IsValid);
        Assert.False(after.Expired);
        Assert.False(after.DripDue);
        Assert.Equal(last.Wound, after.Wound);
    }

    [Fact]
    public void AStainHoldsFullStrengthAndThenFadesOut()
    {
        const double life = 20.0;
        Assert.Equal(1.0f, StainFade.AlphaFor(0.0, life));
        Assert.Equal(1.0f, StainFade.AlphaFor(life * StainFade.SolidFraction, life));

        // Halfway through the fading tail.
        float mid = StainFade.AlphaFor(life * (StainFade.SolidFraction + ((1.0f - StainFade.SolidFraction) * 0.5f)), life);
        Assert.InRange(mid, 0.45f, 0.55f);
    }

    /// <summary>
    /// The bug this exists for: the first version had no lifetime, so blood accumulated for
    /// as long as the application stayed open (owner report 2026-08-25).
    /// </summary>
    [Fact]
    public void AStainDoesNotLastForever()
    {
        const double life = 20.0;
        Assert.False(StainFade.HasDried(life - 0.01, life));
        Assert.True(StainFade.HasDried(life, life));
        Assert.True(StainFade.HasDried(life * 3.0, life));
        Assert.Equal(0.0f, StainFade.AlphaFor(life, life));
        Assert.Equal(0.0f, StainFade.AlphaFor(life * 3.0, life));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(1.0, -5.0)]
    [InlineData(double.NaN, 20.0)]
    [InlineData(-1.0, 20.0)]
    [InlineData(1.0, double.PositiveInfinity)]
    public void ANonsenseStainReadsAsGoneRatherThanAsOneThatNeverFades(double age, double lifetime)
    {
        Assert.Equal(0.0f, StainFade.AlphaFor(age, lifetime));
        Assert.True(StainFade.HasDried(age, lifetime));
    }

    [Fact]
    public void ClearingIsImmediateIdempotentAndKeepsTheEpisode()
    {
        BleedWound wound = Advance(Wound(), 100);
        BleedWound cleared = BleedingStatus.Clear(wound);

        Assert.False(cleared.IsBleeding);
        Assert.Equal(0.0f, cleared.Severity);
        Assert.Equal(wound.Episode, cleared.Episode);
        Assert.Equal(cleared, BleedingStatus.Clear(cleared));

        // A part opened again after a patch-up is a new wound, not the old one resumed.
        Assert.Equal(wound.Episode + 1, BleedingStatus.Open(cleared, 1.0f, Constants).Wound.Episode);
    }
}
