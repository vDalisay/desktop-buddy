using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Autonomy;

public sealed class FunInterestModelTests
{
    /// <summary>The owner's worked example: loves catch, cannot stand being tickled.</summary>
    private static FunPreferences LovesCatch => new(
        CatchDrain: 1, PetDrain: 5, TickleDrain: 20, TreatDrain: 5);

    [Fact]
    public void FreshBuddy_FindsEverythingFun()
    {
        var model = new FunInterestModel(LovesCatch);

        foreach (FunActivityId activity in Enum.GetValues<FunActivityId>())
        {
            Assert.True(model.IsFun(activity));
            Assert.Equal(FunInterestModel.MaximumInterest, model.InterestIn(activity));
        }
    }

    /// <summary>
    /// The whole point of taste: the same game costs two buddies different amounts, so one
    /// stays interested far longer than the other.
    /// </summary>
    [Fact]
    public void TasteDecidesHowFastInterestDrains()
    {
        var enthusiast = new FunInterestModel(LovesCatch);
        var sceptic = new FunInterestModel(LovesCatch with { CatchDrain = 20 });

        enthusiast.Engage(FunActivityId.Catch);
        sceptic.Engage(FunActivityId.Catch);

        Assert.Equal(99.0f, enthusiast.InterestIn(FunActivityId.Catch));
        Assert.Equal(80.0f, sceptic.InterestIn(FunActivityId.Catch));
    }

    [Fact]
    public void RepeatedEngagement_StopsBeingFun()
    {
        // Twenty a throw: bored after five.
        var model = new FunInterestModel(LovesCatch with { CatchDrain = 20 });

        for (int round = 0; round < 5; round++)
        {
            Assert.True(model.Engage(FunActivityId.Catch).WasFun, $"round {round}");
        }

        Assert.Equal(0.0f, model.InterestIn(FunActivityId.Catch));
        Assert.False(model.IsFun(FunActivityId.Catch));
        Assert.False(model.Engage(FunActivityId.Catch).WasFun);
    }

    /// <summary>The engagement that empties the meter is still fun; the next one is not.</summary>
    [Fact]
    public void TheEngagementThatEmptiesTheMeter_IsStillFun()
    {
        var model = new FunInterestModel(LovesCatch with { CatchDrain = 100 });

        FunOutcome emptying = model.Engage(FunActivityId.Catch);

        Assert.True(emptying.WasFun);
        Assert.Equal(100.0f, emptying.InterestBefore);
        Assert.Equal(0.0f, emptying.InterestAfter);
        Assert.False(model.Engage(FunActivityId.Catch).WasFun);
    }

    [Fact]
    public void InterestNeverFallsBelowZero()
    {
        var model = new FunInterestModel(LovesCatch with { CatchDrain = 20 });

        for (int round = 0; round < 40; round++)
        {
            model.Engage(FunActivityId.Catch);
        }

        Assert.Equal(0.0f, model.InterestIn(FunActivityId.Catch));
    }

    /// <summary>
    /// Boredom has to be waited out. A sliver of recharge putting the meter back above zero
    /// must not make the activity fun again, or "interest fades" would last one tick.
    /// </summary>
    [Fact]
    public void ASpentActivity_StaysBoringUntilItHasRecoveredProperly()
    {
        var model = new FunInterestModel(LovesCatch with { CatchDrain = 100 });
        model.Engage(FunActivityId.Catch);
        Assert.False(model.IsFun(FunActivityId.Catch));

        // Above zero, but nowhere near the comeback level.
        model.Recharge(10.0);
        Assert.True(model.InterestIn(FunActivityId.Catch) > 0.0f);
        Assert.False(model.IsFun(FunActivityId.Catch));

        model.Recharge(60.0);

        Assert.True(model.IsFun(FunActivityId.Catch));
        Assert.True(model.InterestIn(FunActivityId.Catch) >= FunInterestModel.ComebackInterest);
    }

    /// <summary>An activity that never ran dry is not subject to the comeback gate.</summary>
    [Fact]
    public void PartiallySpentInterest_StaysFun()
    {
        var model = new FunInterestModel(LovesCatch with { CatchDrain = 20 });

        model.Engage(FunActivityId.Catch);
        model.Engage(FunActivityId.Catch);

        Assert.Equal(60.0f, model.InterestIn(FunActivityId.Catch));
        Assert.True(model.IsFun(FunActivityId.Catch));
    }

    [Fact]
    public void Recharge_StopsAtFullInterest()
    {
        var model = new FunInterestModel(LovesCatch);
        model.Engage(FunActivityId.Catch);

        model.Recharge(10_000.0);

        Assert.Equal(FunInterestModel.MaximumInterest, model.InterestIn(FunActivityId.Catch));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    public void Recharge_IgnoresNonAdvancingSpans(double elapsed)
    {
        var model = new FunInterestModel(LovesCatch with { CatchDrain = 20 });
        model.Engage(FunActivityId.Catch);
        float before = model.InterestIn(FunActivityId.Catch);

        model.Recharge(elapsed);

        Assert.Equal(before, model.InterestIn(FunActivityId.Catch));
    }

    /// <summary>Boredom with one toy says nothing about the others.</summary>
    [Fact]
    public void ActivitiesTireIndependently()
    {
        var model = new FunInterestModel(LovesCatch with { CatchDrain = 100 });

        model.Engage(FunActivityId.Catch);

        Assert.False(model.IsFun(FunActivityId.Catch));
        Assert.True(model.IsFun(FunActivityId.Pet));
        Assert.True(model.IsFun(FunActivityId.Tickle));
        Assert.True(model.IsFun(FunActivityId.Treat));
    }

    [Fact]
    public void RestoredInterest_IsClampedIntoRange()
    {
        var model = new FunInterestModel(LovesCatch);

        model.RestoreInterest(FunActivityId.Catch, 500.0f, bored: false);
        model.RestoreInterest(FunActivityId.Pet, -20.0f, bored: true);
        model.RestoreInterest(FunActivityId.Tickle, float.NaN, bored: true);

        Assert.Equal(100.0f, model.InterestIn(FunActivityId.Catch));
        Assert.Equal(0.0f, model.InterestIn(FunActivityId.Pet));
        Assert.Equal(100.0f, model.InterestIn(FunActivityId.Tickle));
    }

    [Fact]
    public void RestoreInterest_PreservesTheBoredomLatchAtTheSameMeterValue()
    {
        var stillFun = new FunInterestModel(LovesCatch);
        var recharging = new FunInterestModel(LovesCatch);

        stillFun.RestoreInterest(FunActivityId.Catch, 10.0f, bored: false);
        recharging.RestoreInterest(FunActivityId.Catch, 10.0f, bored: true);

        Assert.True(stillFun.IsFun(FunActivityId.Catch));
        Assert.False(recharging.IsFun(FunActivityId.Catch));
    }

    [Fact]
    public void SampledTastes_StayInsideTheValidRange()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            FunPreferences sampled = FunPreferences.Sample(new SeededRandomSource(seed));

            foreach (FunActivityId activity in Enum.GetValues<FunActivityId>())
            {
                int drain = sampled.DrainFor(activity);
                Assert.InRange(drain, FunPreferences.MinDrain, FunPreferences.MaxDrain);
            }
        }
    }

    /// <summary>A personality is sampled once; the same seed must reproduce it exactly.</summary>
    [Fact]
    public void SamplingIsDeterministicForASeed()
    {
        FunPreferences first = FunPreferences.Sample(new SeededRandomSource(4242));
        FunPreferences second = FunPreferences.Sample(new SeededRandomSource(4242));

        Assert.Equal(first, second);
    }

    /// <summary>The population must actually span tastes, not cluster on one value.</summary>
    [Fact]
    public void SamplingProducesBuddiesThatDifferFromEachOther()
    {
        var seen = new System.Collections.Generic.HashSet<int>();
        for (ulong seed = 0; seed < 200; seed++)
        {
            seen.Add(FunPreferences.Sample(new SeededRandomSource(seed)).CatchDrain);
        }

        Assert.True(seen.Count > 5, $"catch drain only produced {seen.Count} distinct tastes");
    }

    [Fact]
    public void PreferencesFromPersisted_AreClamped()
    {
        FunPreferences clamped = FunPreferences.FromPersisted(-5, 900, 3, 0);

        Assert.Equal(FunPreferences.MinDrain, clamped.CatchDrain);
        Assert.Equal(FunPreferences.MaxDrain, clamped.PetDrain);
        Assert.Equal(3, clamped.TickleDrain);
        Assert.Equal(FunPreferences.MinDrain, clamped.TreatDrain);
    }

    [Fact]
    public void EveryActivityHasATotalContentIdMapping()
    {
        foreach (FunActivityId activity in Enum.GetValues<FunActivityId>())
        {
            string id = ContentIds.ForFun(activity);

            Assert.True(ContentIds.IsKnown(id));
            Assert.True(ContentIds.TryParseFun(id, out FunActivityId roundTripped));
            Assert.Equal(activity, roundTripped);
        }
    }

    [Fact]
    public void UnknownFunIdIsRejected()
    {
        Assert.False(ContentIds.TryParseFun("fun.not_a_real_thing", out _));
        Assert.False(ContentIds.TryParseFun(null, out _));
    }
}
