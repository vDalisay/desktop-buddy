using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Mood;

public sealed class MoodModelTests
{
    private const float Tolerance = 0.0005f;

    [Theory]
    [InlineData(-100.0f, MoodBand.Fearful)]
    [InlineData(-61.0f, MoodBand.Fearful)]
    [InlineData(-60.0f, MoodBand.Wary)]
    [InlineData(-21.0f, MoodBand.Wary)]
    [InlineData(-20.0f, MoodBand.Neutral)]
    [InlineData(20.0f, MoodBand.Neutral)]
    [InlineData(21.0f, MoodBand.Content)]
    [InlineData(60.0f, MoodBand.Content)]
    [InlineData(61.0f, MoodBand.Delighted)]
    [InlineData(100.0f, MoodBand.Delighted)]
    public void Band_MatchesSpecBoundaries(float mood, MoodBand expected) =>
        Assert.Equal(expected, MoodModel.BandFor(mood));

    [Fact]
    public void RegisterHarm_ReducesMoodByTenthOfPain()
    {
        var model = new MoodModel(0.0f);

        model.RegisterHarm(ContentIds.ToolBoxingGlove, pain: 50.0f); // 50 * 0.1 = 5

        Assert.Equal(-5.0f, model.Mood, Tolerance);
        Assert.True(model.IsToolHarmful(ContentIds.ToolBoxingGlove));
    }

    [Fact]
    public void RegisterHarm_ReductionCapsAtTen()
    {
        var model = new MoodModel(0.0f);

        model.RegisterHarm(ContentIds.ToolBoxingGlove, pain: 500.0f); // 50 uncapped → capped to 10

        Assert.Equal(-10.0f, model.Mood, Tolerance);
    }

    [Fact]
    public void Mood_ClampsToBounds()
    {
        var low = new MoodModel(-100.0f);
        low.RegisterHarm(ContentIds.LooseObject, 200.0f);
        Assert.Equal(-100.0f, low.Mood, Tolerance);

        var high = new MoodModel(100.0f);
        high.ApplyMoodDelta(50.0f);
        Assert.Equal(100.0f, high.Mood, Tolerance);
    }

    [Fact]
    public void Drift_MovesTowardZeroAtHalfPointPerMinute()
    {
        var positive = new MoodModel(10.0f);
        positive.Drift(60.0); // one minute → 0.5 toward 0
        Assert.Equal(9.5f, positive.Mood, Tolerance);

        var negative = new MoodModel(-10.0f);
        negative.Drift(120.0); // two minutes → 1.0 toward 0
        Assert.Equal(-9.0f, negative.Mood, Tolerance);
    }

    [Fact]
    public void Drift_NeverOvershootsNeutral()
    {
        var model = new MoodModel(0.2f);

        // A huge elapsed span (e.g. a mistakenly long tick) stops exactly at 0.
        model.Drift(100_000.0);

        Assert.Equal(0.0f, model.Mood, Tolerance);
    }

    [Fact]
    public void TrustReset_FiresOnceOnUpwardCrossOfSixty()
    {
        var model = new MoodModel(59.0f);
        model.RegisterHarm(ContentIds.ToolBoxingGlove, pain: 100.0f); // mood 59 → 49, glove harmful
        Assert.True(model.IsToolHarmful(ContentIds.ToolBoxingGlove));

        bool reset = model.ApplyMoodDelta(11.0f); // 49 → 60, crosses upward

        Assert.True(reset);
        Assert.False(model.IsToolHarmful(ContentIds.ToolBoxingGlove));
        Assert.Empty(model.HarmfulTools);
    }

    [Fact]
    public void TrustReset_DoesNotRefireWhileStayingAtOrAboveSixty()
    {
        var model = new MoodModel(60.0f); // constructed already above; no reset at ctor

        bool first = model.ApplyMoodDelta(10.0f); // 60 → 70, still above
        bool second = model.ApplyMoodDelta(-5.0f); // 70 → 65, still above

        Assert.False(first);
        Assert.False(second);
    }

    [Fact]
    public void TrustReset_ReArmsOnlyAfterFallingBelowSixty()
    {
        var model = new MoodModel(55.0f);
        Assert.True(model.ApplyMoodDelta(10.0f)); // 55 → 65, first cross fires

        model.ApplyMoodDelta(-20.0f); // 65 → 45, below 60 again (re-arm)
        model.RegisterHarm(ContentIds.LooseObject, 100.0f); // record a fresh harmful source at mood 35

        bool refire = model.ApplyMoodDelta(30.0f); // 35 → 65, crosses upward again
        Assert.True(refire);
        Assert.False(model.IsToolHarmful(ContentIds.LooseObject));
    }

    [Fact]
    public void Construction_AtOrAboveSixty_DoesNotFireReset()
    {
        // Sanity: starting delighted with a pre-seeded harmful tool would need the
        // save layer; here we assert construction itself performs no crossing.
        var model = new MoodModel(80.0f);
        Assert.False(model.ApplyMoodDelta(0.0f));
    }

    [Fact]
    public void RegisterHarm_RequiresAStableContentId()
    {
        var model = new MoodModel();

        Assert.Throws<ArgumentException>(() => model.RegisterHarm(string.Empty, 10.0f));
        Assert.Throws<ArgumentException>(() => model.RegisterHarm("  ", 10.0f));
    }

    [Fact]
    public void Construction_RestoresPersistedHarmfulHistoryWithoutCrossing()
    {
        // A delighted save with recorded harm is a restore, not a trust event: the
        // crossing rule must not fire and must not wipe the loaded history.
        var model = new MoodModel(80.0f, new[] { ContentIds.ToolBoxingGlove, ContentIds.LooseObject });

        Assert.True(model.IsToolHarmful(ContentIds.ToolBoxingGlove));
        Assert.True(model.IsToolHarmful(ContentIds.LooseObject));
        Assert.Equal(2, model.HarmfulTools.Count);
        Assert.False(model.ApplyMoodDelta(0.0f));
    }
}
