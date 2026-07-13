using DesktopBuddy.Domain.Economy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Economy;

public sealed class PassiveIncomeTests
{
    private const float Tolerance = 0.0005f;

    [Theory]
    [InlineData(-100.0f, 0.25f)]
    [InlineData(-50.0f, 0.625f)]
    [InlineData(0.0f, 1.0f)]
    [InlineData(50.0f, 1.5f)]
    [InlineData(100.0f, 2.0f)]
    public void MoodMultiplier_HitsAnchorsAndMidpoints(float mood, float expected) =>
        Assert.Equal(expected, PassiveIncome.MoodMultiplier(mood), Tolerance);

    [Theory]
    [InlineData(-9999.0f, 0.25f)]
    [InlineData(9999.0f, 2.0f)]
    public void MoodMultiplier_ClampsOutOfRange(float mood, float expected) =>
        Assert.Equal(expected, PassiveIncome.MoodMultiplier(mood), Tolerance);

    [Fact]
    public void Accrue_AppliesBaseRateAndMultiplier()
    {
        // 1 credit/s base, neutral mood (1.0x), 10 s → 10 credits = 10_000 milli.
        var income = new PassiveIncome(baseCreditsPerSecond: 1.0);

        long milli = income.Accrue(0.0f, 10.0);

        Assert.Equal(10_000, milli);
    }

    [Fact]
    public void Accrue_ScalesWithMood()
    {
        var income = new PassiveIncome(baseCreditsPerSecond: 1.0);

        // Delighted (+100 → 2.0x) doubles the neutral rate over the same span.
        Assert.Equal(20_000, income.Accrue(100.0f, 10.0));
    }

    [Fact]
    public void Accrue_CarriesFractionalMilliWithoutDrift()
    {
        // Each 0.5 s tick earns 0.001*0.5 credit = 0.5 milli — below 1 milli, so a naive
        // per-tick floor would lose it all. The fractional carry must recover every milli.
        // 0.5 is exactly representable, so the elapsed sum is exact and the total is 1000.
        var income = new PassiveIncome(baseCreditsPerSecond: 0.001);

        long total = 0;
        for (int i = 0; i < 2_000; i++)
        {
            total += income.Accrue(0.0f, 0.5);
        }

        // 0.001 credit/s * 1000 s = 1 credit = 1000 milli, exact via fractional carry.
        Assert.Equal(1_000, total);
    }

    [Fact]
    public void Accrue_NonPositiveElapsed_EarnsNothing()
    {
        var income = new PassiveIncome(baseCreditsPerSecond: 5.0);

        Assert.Equal(0, income.Accrue(50.0f, 0.0));
        Assert.Equal(0, income.Accrue(50.0f, -3.0));
    }
}
