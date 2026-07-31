using System;
using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Mood;

public sealed class NerfMoodToleranceModelTests
{
    [Fact]
    public void HitsOneThroughTwentyAreEnjoyedAndTwentyOneIsAnnoying()
    {
        var model = new NerfMoodToleranceModel();

        for (int hit = 1; hit <= 20; hit++)
        {
            NerfMoodHit result = model.RegisterHit(hit * 0.25);
            Assert.Equal(hit, result.HitNumber);
            Assert.True(result.Enjoyed);
            Assert.Equal(ImpactMoodEffectKind.Enjoyment, result.MoodEffect.Kind);
            Assert.Equal(0.25f, result.MoodEffect.EnjoymentMoodGain);
        }

        NerfMoodHit annoyed = model.RegisterHit(5.25);
        Assert.Equal(21, annoyed.HitNumber);
        Assert.False(annoyed.Enjoyed);
        Assert.True(model.IsAnnoyed);
        Assert.Equal(ImpactMoodEffectKind.Annoyance, annoyed.MoodEffect.Kind);
    }

    [Fact]
    public void TenSecondsWithoutAHitResetsBeforeTheNextHit()
    {
        var model = new NerfMoodToleranceModel();
        for (int hit = 0; hit < 21; hit++)
            model.RegisterHit(hit * 0.1);

        Assert.True(model.Update(12.0));
        Assert.Equal(0, model.HitsInCurrentBarrage);

        NerfMoodHit fresh = model.RegisterHit(12.0);
        Assert.Equal(1, fresh.HitNumber);
        Assert.True(fresh.Enjoyed);
    }

    [Fact]
    public void JustUnderTenSecondsDoesNotReset()
    {
        var model = new NerfMoodToleranceModel();
        model.RegisterHit(2.0);

        Assert.False(model.Update(11.999));
        Assert.Equal(1, model.HitsInCurrentBarrage);
    }

    [Fact]
    public void InvalidTuningAndNonMonotonicTimeAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NerfMoodToleranceModel(new NerfMoodToleranceTuning(0, 0.25f, 10.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NerfMoodToleranceModel(new NerfMoodToleranceTuning(20, 0.0f, 10.0)));

        var model = new NerfMoodToleranceModel();
        model.RegisterHit(4.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Update(3.0));
    }
}
