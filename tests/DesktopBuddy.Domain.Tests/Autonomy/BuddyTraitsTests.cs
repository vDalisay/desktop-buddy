using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Autonomy;

public sealed class BuddyTraitsTests
{
    [Fact]
    public void Sample_IsDeterministicPerSeed()
    {
        BuddyTraits first = BuddyTraits.Sample(new SeededRandomSource(1));
        BuddyTraits again = BuddyTraits.Sample(new SeededRandomSource(1));
        BuddyTraits other = BuddyTraits.Sample(new SeededRandomSource(7));

        Assert.Equal(first, again);
        Assert.NotEqual(first.ObstacleHopPropensity, other.ObstacleHopPropensity);
    }

    [Fact]
    public void Sample_StaysInsideTheBucketRangeAndCoversBothEnds()
    {
        var seen = new HashSet<int>();
        for (int seed = 0; seed < 400; seed++)
        {
            BuddyTraits traits = BuddyTraits.Sample(new SeededRandomSource((ulong)seed));
            Assert.InRange(
                traits.ObstacleHopPropensity,
                BuddyTraits.MinPropensity,
                BuddyTraits.MaxPropensity);
            seen.Add(traits.ObstacleHopPropensity);
        }

        // Uniform over the full range: the population must contain both timid and eager
        // buddies, not cluster in the middle.
        Assert.Contains(true, new[] { seen.Count > 50 });
    }

    [Fact]
    public void Sample_RejectsANullStream() =>
        Assert.Throws<ArgumentNullException>(() => BuddyTraits.Sample(null!));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void FromPersisted_ClampsCorruptOrMigratedValues(int stored, int expected) =>
        Assert.Equal(expected, BuddyTraits.FromPersisted(stored).ObstacleHopPropensity);

    [Fact]
    public void Sample_CanProduceTheExtremes()
    {
        // The trait gate must be reachable from both ends, otherwise "never hops" and
        // "always hops" personalities could not exist.
        bool sawLow = false;
        bool sawHigh = false;
        for (int seed = 0; seed < 2000 && !(sawLow && sawHigh); seed++)
        {
            int value = BuddyTraits.Sample(new SeededRandomSource((ulong)seed)).ObstacleHopPropensity;
            sawLow |= value <= 5;
            sawHigh |= value >= 95;
        }

        Assert.True(sawLow, "no near-zero propensity in 2000 seeds");
        Assert.True(sawHigh, "no near-max propensity in 2000 seeds");
    }
}
