using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Mood;

public sealed class HungerModelTests
{
    [Fact]
    public void TheBarIsTwoHundredPointsAndStartsEmpty()
    {
        var hunger = new HungerModel();

        Assert.Equal(200.0f, hunger.Capacity);
        Assert.Equal(0.0f, hunger.Fullness);
        Assert.Equal(200.0f, hunger.Appetite);
    }

    [Fact]
    public void TheOwnersWorkedExample()
    {
        // "hunger is at 160, a cake fills 50 — overshoots by 10, so no. An apple fills 10 —
        // that fits." (owner, 2026-07-29)
        var hunger = new HungerModel(initialFullness: 160.0f);

        Assert.False(hunger.Accepts(50.0f));
        Assert.True(hunger.Accepts(10.0f));
        Assert.Equal(40.0f, hunger.Appetite);
    }

    [Fact]
    public void AnItemThatExactlyFillsTheBarIsAccepted()
    {
        var hunger = new HungerModel(initialFullness: 160.0f);

        Assert.True(hunger.Accepts(40.0f));
        Assert.False(hunger.Accepts(40.01f));
    }

    [Fact]
    public void EatingFillsTheBarAndCannotOverflowIt()
    {
        var hunger = new HungerModel();

        hunger.Fill(50.0f);
        Assert.Equal(50.0f, hunger.Fullness);

        hunger.Fill(500.0f);
        Assert.Equal(hunger.Capacity, hunger.Fullness);
        Assert.Equal(0.0f, hunger.Appetite);
    }

    [Theory]
    [InlineData(HungerActivity.Working, 2.0f)]
    [InlineData(HungerActivity.Playing, 10.0f)]
    [InlineData(HungerActivity.Exerting, 20.0f)]
    public void AppetiteBurnsAtTheRateForWhatTheBuddyIsDoing(HungerActivity activity, float perMinute)
    {
        var hunger = new HungerModel(initialFullness: 200.0f);

        hunger.Drain(60.0, activity);

        Assert.Equal(200.0f - perMinute, hunger.Fullness, 3);
    }

    [Fact]
    public void DrainAccumulatesAcrossPartialSpansExactly()
    {
        var hunger = new HungerModel(initialFullness: 100.0f);

        // One minute of play, delivered a routed tick at a time.
        for (int tick = 0; tick < 120 * 60; tick++)
            hunger.Drain(1.0 / 120.0, HungerActivity.Playing);

        Assert.Equal(90.0f, hunger.Fullness, 2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-30.0)]
    [InlineData(double.NaN)]
    public void ANonAdvancingClockCannotFeedTheBuddy(double elapsed)
    {
        var hunger = new HungerModel(initialFullness: 100.0f);

        hunger.Drain(elapsed, HungerActivity.Playing);

        Assert.Equal(100.0f, hunger.Fullness);
    }

    [Fact]
    public void FullnessNeverGoesBelowEmpty()
    {
        var hunger = new HungerModel(initialFullness: 5.0f);

        hunger.Drain(600.0, HungerActivity.Exerting);

        Assert.Equal(0.0f, hunger.Fullness);
        Assert.True(hunger.Accepts(200.0f));
    }

    [Fact]
    public void ANonFillingConsumableIsNeverRefusedForAppetite()
    {
        // A repair item is not food; a full buddy must still be able to use one.
        var hunger = new HungerModel(initialFullness: 200.0f);

        Assert.True(hunger.Accepts(0.0f));
        Assert.False(hunger.Accepts(1.0f));
    }

    [Theory]
    // hidden, workMode, activeInteraction
    [InlineData(true, false, false, HungerActivity.Working)]
    [InlineData(true, false, true, HungerActivity.Working)]
    [InlineData(false, true, false, HungerActivity.Working)]
    [InlineData(false, true, true, HungerActivity.Working)]
    [InlineData(false, false, false, HungerActivity.Playing)]
    [InlineData(false, false, true, HungerActivity.Exerting)]
    public void TheRateFollowsWhatIsActuallyHappening(
        bool hidden,
        bool workMode,
        bool activeInteraction,
        HungerActivity expected) =>
        Assert.Equal(expected, HungerActivityPolicy.Classify(hidden, workMode, activeInteraction));

    [Fact]
    public void RestoreClampsPersistedValuesIntoTheBar()
    {
        var hunger = new HungerModel();

        hunger.Restore(9999.0f);
        Assert.Equal(200.0f, hunger.Fullness);

        hunger.Restore(-50.0f);
        Assert.Equal(0.0f, hunger.Fullness);

        hunger.Restore(float.NaN);
        Assert.Equal(0.0f, hunger.Fullness);
    }
}
