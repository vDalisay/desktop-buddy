using DesktopBuddy.App;
using DesktopBuddy.Domain.Lifecycle;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Lifecycle;

public sealed class MonotonicSpanFilterTests
{
    [Fact]
    public void FirstAndResetSamples_EstablishBaselineWithoutAccrual()
    {
        var filter = new MonotonicSpanFilter(5.0);

        Assert.False(filter.TryAccept(100.0, out _));
        Assert.True(filter.TryAccept(100.25, out double elapsed));
        Assert.Equal(0.25, elapsed);

        filter.Reset();
        Assert.False(filter.TryAccept(500.0, out _));
        Assert.True(filter.TryAccept(500.1, out elapsed));
        Assert.Equal(0.1, elapsed, 9);
    }

    [Theory]
    [InlineData(100.0, 100.0)]
    [InlineData(100.0, 99.0)]
    [InlineData(100.0, 105.0001)]
    public void NonForwardAndDiscontinuousSpans_AreExcluded(
        double first,
        double second)
    {
        var filter = new MonotonicSpanFilter(5.0);
        filter.TryAccept(first, out _);

        Assert.False(filter.TryAccept(second, out double elapsed));
        Assert.Equal(0.0, elapsed);
        Assert.Equal(1, filter.ExcludedSpanCount);
    }

    [Fact]
    public void ExcludedDiscontinuity_BecomesNewBaselineWithoutCatchup()
    {
        var filter = new MonotonicSpanFilter(5.0);
        filter.TryAccept(10.0, out _);
        Assert.False(filter.TryAccept(100.0, out _));

        Assert.True(filter.TryAccept(100.1, out double elapsed));
        Assert.Equal(0.1, elapsed, 9);
    }

    [Fact]
    public void GameClock_UsesInjectedMonotonicSource()
    {
        var source = new ManualTimeSource { Seconds = 50.0 };
        var clock = new GameClock(source, 5.0);
        Assert.False(clock.TrySample(out _));

        source.Seconds = 51.25;
        Assert.True(clock.TrySample(out double elapsed));
        Assert.Equal(1.25, elapsed);

        source.Seconds = 80.0;
        Assert.False(clock.TrySample(out _));
        Assert.Equal(1, clock.ExcludedSpanCount);
    }

    private sealed class ManualTimeSource : IMonotonicTimeSource
    {
        public double Seconds { get; set; }
    }
}
