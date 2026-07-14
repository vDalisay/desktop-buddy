using System;
using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Mood;

public sealed class CareModelTests
{
    private static readonly CareTuning Tuning = CareTuning.Default;

    [Fact]
    public void PetRequiresBothDistanceAndThreeValidSeconds()
    {
        var care = new CareModel(Tuning);

        Assert.Equal(0, care.AccumulatePet(180.0, false, 2.9).PositiveMoodAwards);
        Assert.Equal(1, care.AccumulatePet(0.0, false, 0.1).PositiveMoodAwards);
        Assert.Equal(0.0, care.PetDistanceProgress);
        Assert.Equal(0.0, care.PetValidSecondsProgress);

        care.Reset();
        Assert.Equal(0, care.AccumulatePet(179.0, false, 3.0).PositiveMoodAwards);
        Assert.Equal(1, care.AccumulatePet(1.0, false, 0.0).PositiveMoodAwards);
    }

    [Fact]
    public void FavoriteSpotContributesTwentyPercentMoreDistance()
    {
        var normal = new CareModel(Tuning);
        var favorite = new CareModel(Tuning);

        normal.AccumulatePet(100.0, false, 0.0);
        favorite.AccumulatePet(100.0, true, 0.0);

        Assert.Equal(100.0, normal.PetDistanceProgress, 6);
        Assert.Equal(120.0, favorite.PetDistanceProgress, 6);
    }

    [Fact]
    public void PetCompletionDropsExcessBecauseBarResets()
    {
        var care = new CareModel(Tuning);

        PetCareResult result = care.AccumulatePet(500.0, true, 9.0);

        Assert.True(result.Completed);
        Assert.Equal(1, result.PositiveMoodAwards);
        Assert.Equal(0.0, result.DistanceProgress);
        Assert.Equal(0.0, result.ValidSecondsProgress);
    }

    [Fact]
    public void PetBarCapsWhileWaitingForTimeGate()
    {
        var care = new CareModel(Tuning);

        PetCareResult result = care.AccumulatePet(500.0, true, 1.0);

        Assert.False(result.Completed);
        Assert.Equal(Tuning.PetDistancePerReward, result.DistanceProgress, 6);
        Assert.Equal(1.0, result.ValidSecondsProgress, 6);
    }

    [Fact]
    public void TickleRewardsAtThreeAndSixThenBecomesAngry()
    {
        var care = new CareModel(Tuning);

        TickleCareResult before = care.TickTickle(true, 2.99);
        TickleCareResult first = care.TickTickle(true, 0.01);
        TickleCareResult second = care.TickTickle(true, 3.0);

        Assert.Equal(0, before.PositiveMoodAwards);
        Assert.Equal(1, first.PositiveMoodAwards);
        Assert.Equal(TickleDisposition.Friendly, first.Disposition);
        Assert.Equal(1, second.PositiveMoodAwards);
        Assert.True(second.BecameAngry);
        Assert.Equal(TickleDisposition.Angry, second.Disposition);
    }

    [Fact]
    public void AngryTickleStopsPositiveRewardsAndAppliesNegativeCadence()
    {
        var care = new CareModel(Tuning);
        care.TickTickle(true, 6.0);

        TickleCareResult before = care.TickTickle(true, 2.99);
        TickleCareResult award = care.TickTickle(true, 0.01);
        TickleCareResult later = care.TickTickle(true, 6.0);

        Assert.Equal(0, before.PositiveMoodAwards);
        Assert.Equal(0, before.NegativeMoodAwards);
        Assert.Equal(0, award.PositiveMoodAwards);
        Assert.Equal(1, award.NegativeMoodAwards);
        Assert.Equal(0, later.PositiveMoodAwards);
        Assert.Equal(2, later.NegativeMoodAwards);
    }

    [Fact]
    public void TickleCooldownResetsOnlyAfterEightNoContactSeconds()
    {
        var care = new CareModel(Tuning);
        care.TickTickle(true, 6.0);

        TickleCareResult early = care.TickTickle(false, 7.99);
        TickleCareResult reset = care.TickTickle(false, 0.01);

        Assert.False(early.CooldownReset);
        Assert.Equal(TickleDisposition.Angry, early.Disposition);
        Assert.True(reset.CooldownReset);
        Assert.Equal(TickleDisposition.Friendly, reset.Disposition);
        Assert.Equal(0.0, reset.ContactSeconds);
    }

    [Fact]
    public void TickleHopCadenceChangesWithDisposition()
    {
        var care = new CareModel(Tuning);

        Assert.False(care.TickTickle(true, 1.49).HopRequested);
        Assert.True(care.TickTickle(true, 0.01).HopRequested);
        care.TickTickle(true, 4.5); // reaches Angry and resets the hop clock on its request
        Assert.False(care.TickTickle(true, 0.74).HopRequested);
        Assert.True(care.TickTickle(true, 0.01).HopRequested);
    }

    [Fact]
    public void EmptySpaceDoesNotAdvanceCareButDoesAdvanceAngerCooldown()
    {
        var care = new CareModel(Tuning);

        care.AccumulatePet(0.0, false, 0.0);
        TickleCareResult empty = care.TickTickle(false, 100.0);

        Assert.Equal(0.0, care.PetDistanceProgress);
        Assert.Equal(0.0, empty.ContactSeconds);
        Assert.False(empty.CooldownReset);
    }

    [Fact]
    public void InvalidInputsFailFast()
    {
        var care = new CareModel(Tuning);

        Assert.Throws<ArgumentOutOfRangeException>(() => care.AccumulatePet(double.NaN, false, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => care.AccumulatePet(0.0, false, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => care.TickTickle(true, double.PositiveInfinity));
    }

    [Fact]
    public void ResetClearsPetAndTickleTransientState()
    {
        var care = new CareModel(Tuning);
        care.AccumulatePet(50.0, true, 1.0);
        care.TickTickle(true, 6.0);

        care.Reset();

        Assert.Equal(0.0, care.PetDistanceProgress);
        Assert.Equal(0.0, care.PetValidSecondsProgress);
        Assert.Equal(0.0, care.TickleContactSeconds);
        Assert.Equal(TickleDisposition.Friendly, care.TickleDisposition);
    }
}
